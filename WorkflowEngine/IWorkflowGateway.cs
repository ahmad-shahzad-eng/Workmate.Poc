using Elsa.Workflows;

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

    /// <summary>Whether the engine still has a workflow instance with this id (it may have been lost, e.g. a deleted store).</summary>
    Task<bool> InstanceExistsAsync(string workflowInstanceId);

    /// <summary>Publishes a hand-built activity graph (a designed workflow) as a new Elsa definition version.</summary>
    Task<(string DefinitionId, int Version)> PublishDesignedWorkflowAsync(string definitionId, string name, Guid tenantId, IActivity root);
}

public record WorkflowDefinitionInfo(string DefinitionId, string Name);
