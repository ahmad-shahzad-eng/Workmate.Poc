using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Workmate.Poc.Domain;

public static class ProcessStatus
{
    public const string Running = "Running";
    public const string Completed = "Completed";
}

public static class TaskStatusValues
{
    public const string Pending = "Pending";
    public const string Completed = "Completed";
}

public static class TaskOutcomes
{
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public static readonly string[] All = { Approved, Rejected };
}

/// <summary>Which workflow a submission of a form starts. First active match by Priority wins.</summary>
public class ProcessBinding : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid FormDefinitionId { get; set; }
    public string WorkflowDefinitionId { get; set; } = default!;
    public string? ConditionField { get; set; }   // PoC condition: field == value
    public string? ConditionValue { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;

    protected ProcessBinding() { }
    public ProcessBinding(Guid id, Guid? tenantId, Guid formDefinitionId, string workflowDefinitionId,
        int priority, string? conditionField = null, string? conditionValue = null) : base(id)
    {
        TenantId = tenantId; FormDefinitionId = formDefinitionId; WorkflowDefinitionId = workflowDefinitionId;
        Priority = priority; ConditionField = conditionField; ConditionValue = conditionValue;
    }

    public bool Matches(IReadOnlyDictionary<string, string?> data)
    {
        if (string.IsNullOrEmpty(ConditionField)) return true;
        return data.TryGetValue(ConditionField, out var v) &&
               string.Equals(v, ConditionValue, StringComparison.OrdinalIgnoreCase);
    }
}

public class ProcessInstance : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid BindingId { get; set; }
    public string WorkflowDefinitionId { get; set; } = default!;
    public string? ElsaWorkflowInstanceId { get; set; }
    public string Status { get; set; } = ProcessStatus.Running;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    protected ProcessInstance() { }
    public ProcessInstance(Guid id, Guid? tenantId, Guid submissionId, Guid bindingId,
        string workflowDefinitionId, DateTime startedAt) : base(id)
    {
        TenantId = tenantId; SubmissionId = submissionId; BindingId = bindingId;
        WorkflowDefinitionId = workflowDefinitionId; StartedAt = startedAt;
    }
}

public class ProcessTask : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid ProcessInstanceId { get; set; }
    public Guid SubmissionId { get; set; }
    public string ElsaWorkflowInstanceId { get; set; } = default!;
    public string ElsaBookmarkId { get; set; } = default!;
    public string StepKey { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string AssignmentRule { get; set; } = default!;

    /// <summary>Copied from HumanTask at creation; null (tasks created before spec 004) means role:HR (research R4).</summary>
    public string? FallbackRule { get; set; }
    public string Status { get; set; } = TaskStatusValues.Pending;
    public string? Outcome { get; set; }
    public string? Comment { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedByUserId { get; set; }

    protected ProcessTask() { }
    public ProcessTask(Guid id, Guid? tenantId, Guid processInstanceId, Guid submissionId,
        string elsaWorkflowInstanceId, string elsaBookmarkId, string stepKey, string title, string assignmentRule) : base(id)
    {
        TenantId = tenantId; ProcessInstanceId = processInstanceId; SubmissionId = submissionId;
        ElsaWorkflowInstanceId = elsaWorkflowInstanceId; ElsaBookmarkId = elsaBookmarkId;
        StepKey = stepKey; Title = title; AssignmentRule = assignmentRule;
    }
}

public class ProcessTaskCandidate : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }

    protected ProcessTaskCandidate() { }
    public ProcessTaskCandidate(Guid id, Guid? tenantId, Guid taskId, Guid userId) : base(id)
    {
        TenantId = tenantId; TaskId = taskId; UserId = userId;
    }
}

/// <summary>Readable business timeline. ABP's audit log remains the technical trail.</summary>
public class ProcessEvent : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid? ProcessInstanceId { get; set; }
    public Guid? TaskId { get; set; }
    public string EventType { get; set; } = default!;
    public string? Actor { get; set; }
    public string Detail { get; set; } = default!;
    public DateTime OccurredAt { get; set; }

    protected ProcessEvent() { }
    public ProcessEvent(Guid id, Guid? tenantId, Guid submissionId, Guid? processInstanceId, Guid? taskId,
        string eventType, string? actor, string detail, DateTime occurredAt) : base(id)
    {
        TenantId = tenantId; SubmissionId = submissionId; ProcessInstanceId = processInstanceId; TaskId = taskId;
        EventType = eventType; Actor = actor; Detail = detail; OccurredAt = occurredAt;
    }
}
