using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Workmate.Poc.Domain;
using Workmate.Poc.WorkflowEngine;

namespace Workmate.Poc.Application;

public class TaskAppService : ApplicationService
{
    private readonly CurrentEmployeeProvider _currentEmployee;
    private readonly ProcessManager _processes;
    private readonly IWorkflowGateway _gateway;
    private readonly IRepository<Employee, Guid> _employees;
    private readonly IRepository<FormDefinition, Guid> _forms;
    private readonly IRepository<FormDefinitionVersion, Guid> _versions;
    private readonly IRepository<FormSubmission, Guid> _submissions;
    private readonly IRepository<ProcessTask, Guid> _tasks;
    private readonly IRepository<ProcessTaskCandidate, Guid> _candidates;

    public TaskAppService(CurrentEmployeeProvider currentEmployee, ProcessManager processes, IWorkflowGateway gateway,
        IRepository<Employee, Guid> employees, IRepository<FormDefinition, Guid> forms,
        IRepository<FormDefinitionVersion, Guid> versions, IRepository<FormSubmission, Guid> submissions,
        IRepository<ProcessTask, Guid> tasks, IRepository<ProcessTaskCandidate, Guid> candidates)
    {
        _currentEmployee = currentEmployee; _processes = processes; _gateway = gateway; _employees = employees;
        _forms = forms; _versions = versions; _submissions = submissions; _tasks = tasks; _candidates = candidates;
    }

    public virtual async Task<List<InboxItemDto>> GetInboxAsync()
    {
        var me = await _currentEmployee.GetAsync();
        var myTaskIds = (await _candidates.GetListAsync(c => c.UserId == me.Id)).Select(c => c.TaskId).ToList();
        var tasks = (await _tasks.GetListAsync(t => myTaskIds.Contains(t.Id) && t.Status == TaskStatusValues.Pending))
            .OrderBy(t => t.CreationTime).ToList();
        if (tasks.Count == 0) return new();

        var submissionIds = tasks.Select(t => t.SubmissionId).Distinct().ToList();
        var submissions = (await _submissions.GetListAsync(s => submissionIds.Contains(s.Id))).ToDictionary(s => s.Id);
        var forms = (await _forms.GetListAsync()).ToDictionary(f => f.Id);
        var versionIds = submissions.Values.Select(s => s.FormVersionId).Distinct().ToList();
        var versions = (await _versions.GetListAsync(v => versionIds.Contains(v.Id))).ToDictionary(v => v.Id);
        var people = (await _employees.GetListAsync()).ToDictionary(e => e.Id, e => e.Name);

        return tasks.Select(t =>
        {
            var s = submissions[t.SubmissionId];
            return new InboxItemDto(t.Id, t.Title, s.Id, forms[s.FormDefinitionId].Name,
                versions[s.FormVersionId].VersionNo, people.GetValueOrDefault(s.SubmittedByUserId, "?"), t.CreationTime);
        }).ToList();
    }

    public virtual async Task CompleteAsync(Guid id, CompleteTaskInput input)
    {
        var me = await _currentEmployee.GetAsync();
        var task = await _tasks.GetAsync(id);

        if (task.Status != TaskStatusValues.Pending)
            throw new UserFriendlyException("This task has already been completed.");
        if ((await _candidates.GetListAsync(c => c.TaskId == id && c.UserId == me.Id)).Count == 0)
            throw new UserFriendlyException("This task is not assigned to you.");
        if (!TaskOutcomes.All.Contains(input.Outcome))
            throw new UserFriendlyException($"Outcome must be one of: {string.Join(", ", TaskOutcomes.All)}.");
        if (!await _gateway.InstanceExistsAsync(task.ElsaWorkflowInstanceId))
            throw new UserFriendlyException("This request's workflow can no longer be found. Ask an admin to reset the demo.");

        task.Status = TaskStatusValues.Completed;
        task.Outcome = input.Outcome;
        task.Comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        task.CompletedAt = Clock.Now;
        task.CompletedByUserId = me.Id;
        await _tasks.UpdateAsync(task);

        var detail = $"{task.Title}: {input.Outcome.ToLowerInvariant()}." + (task.Comment == null ? "" : $" \u201c{task.Comment}\u201d");
        await _processes.AddEventAsync(task.SubmissionId, task.ProcessInstanceId, task.Id, "TaskCompleted", me.Name, detail);

        // Resume the workflow only after the decision is committed. The core ids are passed again
        // so the workflow keeps its tenant context however the engine merges resume input.
        var resumeInput = WorkflowInputs.Create(CurrentTenant.Id!.Value, task.ProcessInstanceId, task.SubmissionId,
            new Dictionary<string, string?> { [WorkflowInputs.OutcomeKey] = input.Outcome });
        var elsaInstanceId = task.ElsaWorkflowInstanceId;
        var bookmarkId = task.ElsaBookmarkId;
        CurrentUnitOfWork!.OnCompleted(() => _gateway.ResumeAsync(elsaInstanceId, bookmarkId, resumeInput));
    }
}
