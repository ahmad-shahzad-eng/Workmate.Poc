using Elsa.Common.Models;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;
using Volo.Abp.DependencyInjection;

namespace Workmate.Poc.WorkflowEngine;

/// <summary>
/// Elsa 3 implementation of the gateway, written against the workflow client API (Elsa 3.5+).
/// If you pin a different Elsa version and this file does not compile, this is the only
/// file to adjust: the runtime client API changed between Elsa 3.x minor versions.
/// </summary>
public class ElsaWorkflowGateway : IWorkflowGateway, ITransientDependency
{
    private readonly IWorkflowRuntime _runtime;
    private readonly IWorkflowDefinitionStore _definitions;

    public ElsaWorkflowGateway(IWorkflowRuntime runtime, IWorkflowDefinitionStore definitions)
    {
        _runtime = runtime; _definitions = definitions;
    }

    public async Task<string> StartAsync(string workflowDefinitionId, IDictionary<string, object> input)
    {
        var client = await _runtime.CreateClientAsync();
        await client.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(workflowDefinitionId, VersionOptions.Published),
            Input = input
        });
        return client.WorkflowInstanceId;
    }

    public async Task ResumeAsync(string workflowInstanceId, string bookmarkId, IDictionary<string, object> input)
    {
        var client = await _runtime.CreateClientAsync(workflowInstanceId);
        await client.RunInstanceAsync(new RunWorkflowInstanceRequest
        {
            BookmarkId = bookmarkId,
            Input = input
        });
    }

    public async Task<IReadOnlyList<WorkflowDefinitionInfo>> ListPublishedAsync()
    {
        var summaries = await _definitions.FindSummariesAsync(new WorkflowDefinitionFilter { VersionOptions = VersionOptions.Published });
        return summaries
            .Select(s => new WorkflowDefinitionInfo(s.DefinitionId, string.IsNullOrWhiteSpace(s.Name) ? s.DefinitionId : s.Name!))
            .OrderBy(w => w.Name)
            .ToList();
    }
}
