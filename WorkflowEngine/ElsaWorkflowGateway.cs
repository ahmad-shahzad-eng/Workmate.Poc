using Elsa.Common.Models;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;
using Microsoft.Extensions.Logging;
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
    private readonly IWorkflowInstanceStore _instances;
    private readonly ILogger<ElsaWorkflowGateway> _logger;

    public ElsaWorkflowGateway(IWorkflowRuntime runtime, IWorkflowDefinitionStore definitions,
        IWorkflowInstanceStore instances, ILogger<ElsaWorkflowGateway> logger)
    {
        _runtime = runtime; _definitions = definitions; _instances = instances; _logger = logger;
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

    public async Task<bool> InstanceExistsAsync(string workflowInstanceId)
    {
        try
        {
            var instance = await _instances.FindAsync(new WorkflowInstanceFilter { Id = workflowInstanceId });
            return instance != null;
        }
        catch (Exception ex)
        {
            // The workflow store disagreeing with the domain store (e.g. its file was deleted) is an
            // edge case this method exists to surface, not a reason to crash: report it the same way
            // as "not found" so callers log a warning / fail the action cleanly instead of 500ing.
            _logger.LogWarning(ex, "Could not query the workflow store for instance {WorkflowInstanceId}; treating it as missing.", workflowInstanceId);
            return false;
        }
    }
}
