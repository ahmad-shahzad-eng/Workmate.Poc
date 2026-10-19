using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Models;
using Workmate.Poc.Domain;
using Workmate.Poc.WorkflowEngine.Activities;
using Connection = Elsa.Workflows.Activities.Flowchart.Models.Connection;
using Endpoint = Elsa.Workflows.Activities.Flowchart.Models.Endpoint;

namespace Workmate.Poc.WorkflowEngine;

/// <summary>
/// Turns a designed workflow's ordered steps into a plain Elsa activity graph: a Flowchart of
/// HumanTask nodes wired on their "Approved"/"Rejected" flow ports, ending in CompleteProcess.
/// No Elsa service calls here — only the gateway persists anything (Principle VI).
/// </summary>
public static class ApprovalWorkflowGraphBuilder
{
    public static IActivity Build(IReadOnlyList<ApprovalStep> steps, string fallbackRule)
    {
        var approvedEnd = new CompleteProcess { FinalStatus = new(SubmissionStatus.Approved) };
        var rejectedEnd = new CompleteProcess { FinalStatus = new(SubmissionStatus.Rejected) };

        var nodes = steps.Select((s, i) => new HumanTask
        {
            StepKey = new($"step-{i + 1}"),
            TaskTitle = new(s.Title),
            AssignmentRule = new(s.ToAssignmentRule()),
            FallbackRule = new(fallbackRule),
            CanStartWorkflow = i == 0
        }).ToList();

        // Every node must be registered here, not just reachable from Start/Connections — Elsa's own
        // flowchart (de)serializer resolves connection endpoints by id against this collection, and the
        // gateway assigns those ids (IIdentityGraphService) before publishing. "Start" itself is not part
        // of the persisted JSON (confirmed by inspecting a published definition) — a back-edge (reject to
        // a previous step) can leave every node with an incoming connection, so the entry point must be
        // marked explicitly via CanStartWorkflow, not inferred structurally.
        var flowchart = new Flowchart { Start = nodes[0] };
        foreach (var node in nodes) flowchart.Activities.Add(node);
        flowchart.Activities.Add(approvedEnd);
        flowchart.Activities.Add(rejectedEnd);

        for (var i = 0; i < nodes.Count; i++)
        {
            IActivity approvedTarget = i + 1 < nodes.Count ? nodes[i + 1] : approvedEnd;
            flowchart.Connections.Add(new Connection(new Endpoint(nodes[i], "Approved"), new Endpoint(approvedTarget, "In")));

            IActivity rejectTarget = steps[i].OnReject == OnRejectActions.BackToPrevious && i > 0 ? nodes[i - 1] : rejectedEnd;
            flowchart.Connections.Add(new Connection(new Endpoint(nodes[i], "Rejected"), new Endpoint(rejectTarget, "In")));
        }

        return flowchart;
    }
}
