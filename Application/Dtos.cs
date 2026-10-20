namespace Workmate.Poc.Application;

public record TenantDto(Guid Id, string Name, string DisplayName);
public record EmployeeDto(Guid Id, string Name, string Role, string? ManagerName, bool IsAdmin);

public record FormSummaryDto(string Key, string Name, string Category, Guid VersionId, int VersionNo, string SchemaJson, string EffectiveRenderer);
public record FormVersionDto(Guid Id, int VersionNo, string Status, string SchemaJson, DateTime? PublishedAt);
public record FormDesignDto(string Key, string Name, Guid? CurrentVersionId, string Renderer, List<FormVersionDto> Versions);
public class SaveDraftInput { public string SchemaJson { get; set; } = default!; }
public class SetRendererInput { public string Renderer { get; set; } = default!; }
public record CompanyRendererDto(string DefaultRenderer);
public class SetCompanyRendererInput { public string DefaultRenderer { get; set; } = default!; }

public class SubmitFormInput
{
    public string FormKey { get; set; } = default!;
    public Dictionary<string, string?> Data { get; set; } = new();
}

public record SubmissionListItemDto(Guid Id, string FormName, int VersionNo, string Status, DateTime SubmittedAt);

public record TaskSummaryDto(Guid Id, string Title, string Status, string? Outcome, string? Comment,
    List<string> CandidateNames, string? CompletedByName, DateTime? CompletedAt);

public record EventDto(DateTime OccurredAt, string EventType, string? Actor, string Detail);

public record SubmissionDetailDto(Guid Id, string FormName, int VersionNo, string VersionStatus, string SchemaJson,
    Dictionary<string, string?> Data, string Status, DateTime SubmittedAt, string SubmitterName,
    string? WorkflowDefinitionId, List<TaskSummaryDto> Tasks, List<EventDto> Events, string EffectiveRenderer);

public record InboxItemDto(Guid TaskId, string Title, Guid SubmissionId, string FormName, int VersionNo,
    string SubmitterName, DateTime CreatedAt);

public class CompleteTaskInput
{
    public string Outcome { get; set; } = default!;
    public string? Comment { get; set; }
}

// --- Form administration (admins only)
public record FormListItemDto(string Key, string Name, string Category, bool IsActive, int? PublishedVersionNo, bool HasDraft, string Renderer);

public class CreateFormInput
{
    public string Key { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
}

// --- Routing: which workflow a form starts (admins only)
public record WorkflowDefinitionDto(string DefinitionId, string Name);

public record ProcessBindingDto(Guid Id, string WorkflowDefinitionId, string? ConditionField, string? ConditionValue,
    int Priority, bool IsActive, bool IsStale);

public class SaveBindingInput
{
    public string WorkflowDefinitionId { get; set; } = default!;
    public string? ConditionField { get; set; }
    public string? ConditionValue { get; set; }
    public int Priority { get; set; } = 10;
    public bool IsActive { get; set; } = true;
}

// --- Workflow designer (admins only; Phase D, option B)
public record ApprovalWorkflowListItemDto(string Key, string Name, bool IsActive, int? PublishedVersionNo, bool HasDraft);

public record ApprovalWorkflowVersionDto(Guid Id, int VersionNo, string Status, string StepsJson, string? FallbackRule, DateTime? PublishedAt);

public record ApprovalWorkflowDesignDto(string Key, string Name, Guid? CurrentVersionId, List<ApprovalWorkflowVersionDto> Versions);

public class CreateApprovalWorkflowInput
{
    public string Key { get; set; } = default!;
    public string Name { get; set; } = default!;
}

public class SaveApprovalWorkflowDraftInput
{
    public string StepsJson { get; set; } = default!;
    public string? FallbackRule { get; set; }
}
