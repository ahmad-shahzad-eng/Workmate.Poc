namespace Workmate.Poc.WorkflowEngine;

/// <summary>
/// The only door into the workflow engine. Everything else in the solution talks to this
/// interface, so an Elsa upgrade (or an engine swap) touches one class.
/// </summary>
public interface IWorkflowGateway
{
    /// <summary>Starts the published version of a workflow and returns the engine's instance id.</summary>
    Task<string> StartAsync(string workflowDefinitionId, IDictionary<string, object> input);

    /// <summary>Resumes a suspended workflow at a bookmark (e.g. a completed human task).</summary>
    Task ResumeAsync(string workflowInstanceId, string bookmarkId, IDictionary<string, object> input);

    /// <summary>Published workflow definitions an admin can bind a form to.</summary>
    Task<IReadOnlyList<WorkflowDefinitionInfo>> ListPublishedAsync();
}

public record WorkflowDefinitionInfo(string DefinitionId, string Name);
