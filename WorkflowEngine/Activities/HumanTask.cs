using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Workmate.Poc.Application;
using Workmate.Poc.Domain;

namespace Workmate.Poc.WorkflowEngine.Activities;

/// <summary>Bookmark payload; makes a human-task bookmark identifiable in Elsa's stores.</summary>
public record HumanTaskStimulus(string StepKey, string ProcessInstanceId);

/// <summary>
/// Creates a task for the people an assignment rule resolves to, then suspends the workflow
/// until someone completes the task. Returns the outcome both as Result (for sequence-style
/// workflows) and as a flow outcome (for flowcharts drawn in Elsa Studio).
/// </summary>
[Activity("Workmate", "Human Tasks", "Assigns a task using an assignment rule and waits for its outcome.")]
[FlowNode("Approved", "Rejected")]
public class HumanTask : Activity<string>
{
    /// <summary>Stable key of the step, e.g. "manager-approval".</summary>
    public Input<string> StepKey { get; set; } = default!;

    /// <summary>What the approver sees in the inbox.</summary>
    public Input<string> TaskTitle { get; set; } = default!;

    /// <summary>"line-manager" or "role:HR" in the PoC.</summary>
    public Input<string> AssignmentRule { get; set; } = default!;

    /// <summary>Used when a designed workflow's rule resolves to nobody (or only the submitter); unset for code-defined workflows.</summary>
    public Input<string>? FallbackRule { get; set; }

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var inputs = WorkflowInputs.From(context);
        var stepKey = StepKey.Get(context);

        var bookmark = context.CreateBookmark(
            new HumanTaskStimulus(stepKey, inputs.ProcessInstanceId.ToString()), OnResumeAsync);

        var processes = context.GetRequiredService<ProcessManager>();
        await processes.CreateTaskAsync(inputs, context.WorkflowExecutionContext.Id, bookmark.Id,
            stepKey, TaskTitle.Get(context), AssignmentRule.Get(context), FallbackRule?.Get(context));
    }

    private async ValueTask OnResumeAsync(ActivityExecutionContext context)
    {
        var inputs = WorkflowInputs.From(context);
        var outcome = WorkflowInputs.Read(context.WorkflowExecutionContext.Input, WorkflowInputs.OutcomeKey);

        // Fallback: the task record is the system of record for the decision.
        if (!TaskOutcomes.All.Contains(outcome))
        {
            var processes = context.GetRequiredService<ProcessManager>();
            outcome = await processes.GetLatestOutcomeAsync(inputs, StepKey.Get(context)) ?? TaskOutcomes.Rejected;
        }

        context.Set(Result, outcome);
        await context.CompleteActivityWithOutcomesAsync(outcome!);
    }
}
