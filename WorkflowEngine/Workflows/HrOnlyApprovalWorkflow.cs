using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Workmate.Poc.Domain;
using Workmate.Poc.WorkflowEngine.Activities;

namespace Workmate.Poc.WorkflowEngine.Workflows;

/// <summary>HR decides directly (Acme sick leave, letter requests).</summary>
public class HrOnlyApprovalWorkflow : WorkflowBase
{
    public const string DefinitionId = "hr-only-approval";

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        builder.Name = "HR only";

        var hrOutcome = builder.WithVariable<string>();

        builder.Root = new Sequence
        {
            Activities =
            {
                new HumanTask
                {
                    StepKey = new("hr-approval"),
                    TaskTitle = new("HR review"),
                    AssignmentRule = new("role:HR"),
                    Result = new(hrOutcome)
                },
                new CompleteProcess
                {
                    FinalStatus = new(context =>
                        hrOutcome.Get(context) == TaskOutcomes.Approved ? SubmissionStatus.Approved : SubmissionStatus.Rejected)
                }
            }
        };
    }
}
