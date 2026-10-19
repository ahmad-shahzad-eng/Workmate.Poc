using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Workmate.Poc.Application;

namespace Workmate.Poc.WorkflowEngine.Activities;

/// <summary>
/// Writes the business outcome to domain records (submission status, process status, timeline).
/// In the real build this is where an approved leave request posts to the entitlement ledger.
/// </summary>
[Activity("Workmate", "Processes", "Sets the submission's final status and closes the process.")]
public class CompleteProcess : CodeActivity
{
    /// <summary>"Approved" or "Rejected".</summary>
    public Input<string> FinalStatus { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var processes = context.GetRequiredService<ProcessManager>();
        await processes.CompleteProcessAsync(WorkflowInputs.From(context), FinalStatus.Get(context));
    }
}
