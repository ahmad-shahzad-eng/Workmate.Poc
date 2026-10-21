namespace Workmate.Poc.Application;

public record TenantDto(Guid Id, string Name);
public record EmployeeDto(Guid Id, string Name, string Email, string? JobTitle, List<string> Roles, Guid? ManagerId,
    string? ManagerName, Guid? DepartmentId, string? DepartmentName, bool IsActive);

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

// --- Vendor onboarding (host/vendor-admin only; spec 003 US1)
public class CreateCompanyInput
{
    public string Name { get; set; } = default!;
    public string AdminName { get; set; } = default!;
    public string AdminEmail { get; set; } = default!;
}

/// <summary>The admin chooses a password from the emailed invite (spec 005 FR-008); none is returned.</summary>
public record CompanyCreatedDto(Guid TenantId, Guid AdminEmployeeId, string AdminEmail);

public class ResetAdminPasswordInput
{
    public Guid EmployeeId { get; set; }
}

/// <summary>Edge case "client admin leaves": make another employee the company admin, optionally
/// removing the departed one in the same call.</summary>
public class AssignClientAdminInput
{
    public Guid EmployeeId { get; set; }
    public Guid? RemoveEmployeeId { get; set; }
}

// --- Employees (client admin; spec 004 US1)
public class SaveEmployeeInput
{
    public string Name { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? JobTitle { get; set; }
    public List<string> Roles { get; set; } = new();
    public Guid? ManagerId { get; set; }
    public Guid? DepartmentId { get; set; }
}

public record DeactivationResultDto(EmployeeDto Employee, int ReassignedTaskCount);

// --- Departments (client admin; spec 004 US2)
public record DepartmentDto(Guid Id, string Name, Guid? ParentId, Guid? HeadEmployeeId, string? HeadName, int EmployeeCount);

public class CreateDepartmentInput
{
    public string Name { get; set; } = default!;
    public Guid? ParentId { get; set; }
}

public class UpdateDepartmentInput
{
    public string Name { get; set; } = default!;
    public Guid? HeadEmployeeId { get; set; }
}

// --- Role catalogue (client admin; spec 004 US3)
public record JobRoleDto(Guid Id, string Name, bool IsBuiltIn, int EmployeeCount);

public class CreateJobRoleInput
{
    public string Name { get; set; } = default!;
}

// --- Accounts (anonymous; spec 005 FR-008)
public class ForgotPasswordInput
{
    public string Email { get; set; } = default!;
}

public class SetPasswordInput
{
    public Guid? TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = default!;
    public string NewPassword { get; set; } = default!;
}

// --- Email log (spec 005 FR-005)
public record EmailLogItemDto(Guid Id, string Event, string Audience, string RecipientEmail, string? RecipientName,
    Guid? SubmissionId, string Subject, string Status, int Attempts, string? LastError, DateTime CreationTime, DateTime? SentAt);

// --- Email templates (spec 005 US2 form overrides, US3 company defaults)
public record EmailTemplateDto(string Event, string Audience, string Subject, string Body, string Source);

public class SaveEmailTemplateInput
{
    public string Subject { get; set; } = default!;
    public string Body { get; set; } = default!;
}

public class PreviewEmailInput
{
    public string Event { get; set; } = default!;
    public string Audience { get; set; } = default!;
    public string Subject { get; set; } = default!;
    public string Body { get; set; } = default!;
}

public record EmailPreviewDto(string Subject, string Text, string Html);

public record EmailSenderDto(string? SenderName, string EffectiveSenderName);

public class SetEmailSenderInput
{
    public string? SenderName { get; set; }
}
