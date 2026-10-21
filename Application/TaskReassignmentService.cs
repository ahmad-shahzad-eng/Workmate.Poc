using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application;

/// <summary>
/// Spec 004 FR-007: when an employee is deactivated, every open task they could act on moves to
/// that task's fallback approver. Only our own task/candidate rows change — the Elsa bookmark the
/// task resumes is untouched, so this never goes near the workflow engine (Principle VI).
/// Runs in the caller's unit of work.
/// </summary>
public class TaskReassignmentService : ITransientDependency
{
    /// <summary>Tasks created before spec 004, and code-defined workflows' tasks, fall back to HR (research R4).</summary>
    public const string DefaultFallbackRule = "role:HR";

    private readonly IGuidGenerator _guids;
    private readonly AssignmentResolver _assignment;
    private readonly ProcessManager _processes;
    private readonly IRepository<ProcessTask, Guid> _tasks;
    private readonly IRepository<ProcessTaskCandidate, Guid> _candidates;
    private readonly IRepository<FormSubmission, Guid> _submissions;

    public TaskReassignmentService(IGuidGenerator guids, AssignmentResolver assignment, ProcessManager processes,
        IRepository<ProcessTask, Guid> tasks, IRepository<ProcessTaskCandidate, Guid> candidates,
        IRepository<FormSubmission, Guid> submissions)
    {
        _guids = guids; _assignment = assignment; _processes = processes; _tasks = tasks;
        _candidates = candidates; _submissions = submissions;
    }

    /// <summary>Returns how many open tasks were moved. Call after <paramref name="deactivated"/> is marked inactive.</summary>
    public async Task<int> ReassignOpenTasksAsync(Employee deactivated)
    {
        var taskIds = (await _candidates.GetListAsync(c => c.UserId == deactivated.Id)).Select(c => c.TaskId).ToList();
        var openTasks = await _tasks.GetListAsync(t => taskIds.Contains(t.Id) && t.Status == TaskStatusValues.Pending);

        foreach (var task in openTasks)
        {
            var submission = await _submissions.GetAsync(task.SubmissionId);
            var (people, _) = await _assignment.ResolveAsync(task.FallbackRule ?? DefaultFallbackRule, submission.SubmittedByUserId);

            await _candidates.DeleteAsync(c => c.TaskId == task.Id);
            foreach (var person in people)
                await _candidates.InsertAsync(new ProcessTaskCandidate(_guids.Create(), task.TenantId, task.Id, person.Id));

            var detail = people.Count == 0
                ? $"{task.Title}: {deactivated.Name} was deactivated and the fallback matched nobody. The request is stuck until the rule or HR data is fixed."
                : $"{task.Title}: {deactivated.Name} was deactivated; reassigned to {string.Join(", ", people.Select(p => p.Name))} via the fallback.";
            await _processes.AddEventAsync(task.SubmissionId, task.ProcessInstanceId, task.Id, "TaskReassigned", null, detail);
        }

        return openTasks.Count;
    }
}
