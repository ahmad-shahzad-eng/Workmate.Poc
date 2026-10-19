using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Models;
using Workmate.Poc.Domain;
using Workmate.Poc.WorkflowEngine.Activities;

namespace Workmate.Poc.WorkflowEngine.Workflows;

/// <summary>
/// Default approval route (used by leave and most employee forms): line manager, then HR. Defined in code for the PoC; in the real build
/// admins draw this in Elsa Studio using the same HumanTask and CompleteProcess activities.
/// </summary>
public class LeaveApprovalWorkflow : WorkflowBase
{
    public const string DefinitionId = "leave-approval";

    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId(DefinitionId);
        builder.Name = "Line manager, then HR";

        var managerOutcome = builder.WithVariable<string>();
        var hrOutcome = builder.WithVariable<string>();

        builder.Root = new Sequence
        {
            Activities =
            {
                new HumanTask
                {
                    StepKey = new("manager-approval"),
                    TaskTitle = new("Line manager approval"),
                    AssignmentRule = new("line-manager"),
                    Result = new(managerOutcome)
                },
                new If(context => managerOutcome.Get(context) == TaskOutcomes.Approved)
                {
                    Then = new Sequence
                    {
                        Activities =
                        {
                            new HumanTask
                            {
                                StepKey = new("hr-approval"),
                                TaskTitle = new("HR approval"),
                                AssignmentRule = new("role:HR"),
                                Result = new(hrOutcome)
                            },
                            new CompleteProcess
                            {
                                FinalStatus = new(context =>
                                    hrOutcome.Get(context) == TaskOutcomes.Approved ? SubmissionStatus.Approved : SubmissionStatus.Rejected)
                            }
                        }
                    },
                    Else = new CompleteProcess { FinalStatus = new(SubmissionStatus.Rejected) }
                }
            }
        };
    }
}
