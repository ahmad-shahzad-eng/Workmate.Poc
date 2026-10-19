using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Workmate.Poc.Domain;
using Workmate.Poc.WorkflowEngine;

namespace Workmate.Poc.Application;

/// <summary>
/// The glue between Forms and the workflow engine: picks a binding, starts the workflow,
/// creates tasks for HumanTask steps and records outcomes. Every entry point re-applies the
/// tenant explicitly and runs in its own unit of work, because Elsa may call it from any scope.
/// </summary>
public class ProcessManager : ITransientDependency
{
    private readonly IUnitOfWorkManager _uow;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guids;
    private readonly IClock _clock;
    private readonly IWorkflowGateway _gateway;
    private readonly AssignmentResolver _assignment;
    private readonly IRepository<FormSubmission, Guid> _submissions;
    private readonly IRepository<FormDefinitionVersion, Guid> _versions;
    private readonly IRepository<ProcessBinding, Guid> _bindings;
    private readonly IRepository<ProcessInstance, Guid> _instances;
    private readonly IRepository<ProcessTask, Guid> _tasks;
    private readonly IRepository<ProcessTaskCandidate, Guid> _candidates;
    private readonly IRepository<ProcessEvent, Guid> _events;

    public ProcessManager(IUnitOfWorkManager uow, ICurrentTenant currentTenant, IGuidGenerator guids, IClock clock,
        IWorkflowGateway gateway, AssignmentResolver assignment,
        IRepository<FormSubmission, Guid> submissions, IRepository<FormDefinitionVersion, Guid> versions,
        IRepository<ProcessBinding, Guid> bindings, IRepository<ProcessInstance, Guid> instances,
        IRepository<ProcessTask, Guid> tasks, IRepository<ProcessTaskCandidate, Guid> candidates,
        IRepository<ProcessEvent, Guid> events)
    {
        _uow = uow; _currentTenant = currentTenant; _guids = guids; _clock = clock; _gateway = gateway;
        _assignment = assignment; _submissions = submissions; _versions = versions; _bindings = bindings;
        _instances = instances; _tasks = tasks; _candidates = candidates; _events = events;
    }

    /// <summary>Called after the submission is committed. Production: distributed event + outbox.</summary>
    public async Task StartForSubmissionAsync(Guid tenantId, Guid submissionId)
    {
        using (_currentTenant.Change(tenantId))
        {
            string workflowDefinitionId;
            IDictionary<string, object> input;

            using (var uow = _uow.Begin(requiresNew: true, isTransactional: true))
            {
                var submission = await _submissions.GetAsync(submissionId);
                var data = submission.GetData();

                var binding = (await _bindings.GetListAsync(b => b.FormDefinitionId == submission.FormDefinitionId && b.IsActive))
                    .OrderBy(b => b.Priority)
                    .FirstOrDefault(b => b.Matches(data));

                if (binding == null)
                {
                    await AddEventAsync(submission.Id, null, null, "NoProcess", null,
                        "No workflow is bound to this form, so the request was recorded only.");
                    await uow.CompleteAsync();
                    return;
                }

                var instance = new ProcessInstance(_guids.Create(), tenantId, submission.Id, binding.Id,
                    binding.WorkflowDefinitionId, _clock.Now);
                await _instances.InsertAsync(instance);

                submission.Status = SubmissionStatus.InProcess;
                await _submissions.UpdateAsync(submission);

                var why = binding.ConditionField == null
                    ? "default route"
                    : $"because {binding.ConditionField} is {binding.ConditionValue}";
                await AddEventAsync(submission.Id, instance.Id, null, "ProcessStarted", null,
                    $"Started workflow '{binding.WorkflowDefinitionId}' ({why}).");

                // Fields flagged workflowVariable are passed into the workflow as input.
                var version = await _versions.GetAsync(submission.FormVersionId);
                var variables = FormSchema.Parse(version.SchemaJson).Fields
                    .Where(f => f.WorkflowVariable && data.ContainsKey(f.Key))
                    .ToDictionary(f => f.Key, f => data[f.Key]);

                workflowDefinitionId = binding.WorkflowDefinitionId;
                input = WorkflowInputs.Create(tenantId, instance.Id, submission.Id, variables);
                await uow.CompleteAsync();
            }

            await _gateway.StartAsync(workflowDefinitionId, input);
        }
    }

    /// <summary>Called by the HumanTask activity when the workflow reaches a human step.</summary>
    public async Task CreateTaskAsync(WorkflowInputs inputs, string elsaWorkflowInstanceId, string bookmarkId,
        string stepKey, string title, string assignmentRule)
    {
        using (_currentTenant.Change(inputs.TenantId))
        using (var uow = _uow.Begin(requiresNew: true, isTransactional: true))
        {
            var instance = await _instances.GetAsync(inputs.ProcessInstanceId);
            if (instance.ElsaWorkflowInstanceId == null)
            {
                instance.ElsaWorkflowInstanceId = elsaWorkflowInstanceId;
                await _instances.UpdateAsync(instance);
            }

            var submission = await _submissions.GetAsync(inputs.SubmissionId);
            var people = await _assignment.ResolveAsync(assignmentRule, submission.SubmittedByUserId);

            var task = new ProcessTask(_guids.Create(), inputs.TenantId, instance.Id, submission.Id,
                elsaWorkflowInstanceId, bookmarkId, stepKey, title, assignmentRule);
            await _tasks.InsertAsync(task);

            foreach (var person in people)
                await _candidates.InsertAsync(new ProcessTaskCandidate(_guids.Create(), inputs.TenantId, task.Id, person.Id));

            var detail = people.Count == 0
                ? $"{title}: nobody matched rule '{assignmentRule}'. The request is stuck until the rule or HR data is fixed."
                : $"{title} assigned to {string.Join(", ", people.Select(p => p.Name))}.";
            await AddEventAsync(submission.Id, instance.Id, task.Id, "TaskCreated", null, detail);

            await uow.CompleteAsync();
        }
    }

    /// <summary>Called by the CompleteProcess activity at the end of a workflow.</summary>
    public async Task CompleteProcessAsync(WorkflowInputs inputs, string finalStatus)
    {
        using (_currentTenant.Change(inputs.TenantId))
        using (var uow = _uow.Begin(requiresNew: true, isTransactional: true))
        {
            var instance = await _instances.GetAsync(inputs.ProcessInstanceId);
            instance.Status = ProcessStatus.Completed;
            instance.CompletedAt = _clock.Now;
            await _instances.UpdateAsync(instance);

            var submission = await _submissions.GetAsync(inputs.SubmissionId);
            submission.Status = finalStatus;
            await _submissions.UpdateAsync(submission);

            // Production: an approved leave request posts its transaction to the entitlement ledger here.
            await AddEventAsync(submission.Id, instance.Id, null, "ProcessCompleted", null,
                $"Request {finalStatus.ToLowerInvariant()}.");

            await uow.CompleteAsync();
        }
    }

    /// <summary>Fallback for HumanTask resume: the task record holds the decision.</summary>
    public async Task<string?> GetLatestOutcomeAsync(WorkflowInputs inputs, string stepKey)
    {
        using (_currentTenant.Change(inputs.TenantId))
        using (var uow = _uow.Begin(requiresNew: true, isTransactional: false))
        {
            var task = (await _tasks.GetListAsync(t =>
                    t.ProcessInstanceId == inputs.ProcessInstanceId && t.StepKey == stepKey && t.Status == TaskStatusValues.Completed))
                .OrderByDescending(t => t.CompletedAt)
                .FirstOrDefault();
            await uow.CompleteAsync();
            return task?.Outcome;
        }
    }

    /// <summary>Adds a timeline entry in the ambient unit of work.</summary>
    public async Task AddEventAsync(Guid submissionId, Guid? processInstanceId, Guid? taskId,
        string eventType, string? actor, string detail)
    {
        await _events.InsertAsync(new ProcessEvent(_guids.Create(), _currentTenant.Id, submissionId,
            processInstanceId, taskId, eventType, actor, detail, _clock.Now));
    }
}
