using System.Text.Json;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Workmate.Poc.Domain;

/// <summary>Admin-designed workflow (Phase D, option B): tenant-owned, draft/published versions.</summary>
public class ApprovalWorkflow : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public string Key { get; set; } = default!;
    public string Name { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public Guid? CurrentVersionId { get; set; }

    /// <summary>
    /// Stable across every version of this workflow — Elsa groups definitions by this id, so it is
    /// minted once at creation, not re-generated per publish (only ElsaDefinitionVersion varies).
    /// </summary>
    public string ElsaDefinitionId { get; set; } = default!;

    protected ApprovalWorkflow() { }
    public ApprovalWorkflow(Guid id, Guid? tenantId, string key, string name, string elsaDefinitionId) : base(id)
    {
        TenantId = tenantId; Key = key; Name = name; ElsaDefinitionId = elsaDefinitionId;
    }
}

/// <summary>
/// One version of a designed workflow. Steps are a JSON value (<see cref="ApprovalWorkflowSchema"/>),
/// not a separate table, the same way form fields live inside FormDefinitionVersion.SchemaJson.
/// </summary>
public class ApprovalWorkflowVersion : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid ApprovalWorkflowId { get; set; }
    public int VersionNo { get; set; }
    public string StepsJson { get; set; } = default!;

    /// <summary>"role:{Role}" or "user:{EmployeeId}" — required to publish (FR-010).</summary>
    public string? FallbackRule { get; set; }

    public string Status { get; set; } = FormVersionStatus.Draft;

    /// <summary>Set once published: the Elsa version number this publish produced (same ElsaDefinitionId as every other version, on the parent ApprovalWorkflow).</summary>
    public int? ElsaDefinitionVersion { get; set; }
    public DateTime? PublishedAt { get; set; }

    protected ApprovalWorkflowVersion() { }
    public ApprovalWorkflowVersion(Guid id, Guid? tenantId, Guid approvalWorkflowId, int versionNo, string stepsJson) : base(id)
    {
        TenantId = tenantId; ApprovalWorkflowId = approvalWorkflowId; VersionNo = versionNo; StepsJson = stepsJson;
    }
}

public static class ApproverTypes
{
    public const string LineManager = "line-manager";
    public const string ManagerOfManager = "manager-of-manager";
    public const string Role = "role";
    public const string User = "user";
    public static readonly string[] All = { LineManager, ManagerOfManager, Role, User };
}

public static class OnRejectActions
{
    public const string End = "end";
    public const string BackToPrevious = "back-to-previous";
    public static readonly string[] All = { End, BackToPrevious };
}

/// <summary>One step of a designed workflow, as stored in <see cref="ApprovalWorkflowVersion.StepsJson"/>.</summary>
public class ApprovalStep
{
    public string Title { get; set; } = default!;
    public string ApproverType { get; set; } = default!;
    public string? Role { get; set; }
    public Guid? UserId { get; set; }
    public string OnReject { get; set; } = OnRejectActions.End;

    /// <summary>The rule string AssignmentResolver understands, e.g. "role:Finance" or "user:{guid}".</summary>
    public string ToAssignmentRule() => ApproverType switch
    {
        ApproverTypes.Role => $"role:{Role}",
        ApproverTypes.User => $"user:{UserId}",
        _ => ApproverType // line-manager / manager-of-manager pass through as-is
    };
}

public class ApprovalWorkflowSchema
{
    public List<ApprovalStep> Steps { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static ApprovalWorkflowSchema Parse(string json)
    {
        ApprovalWorkflowSchema? schema;
        try { schema = JsonSerializer.Deserialize<ApprovalWorkflowSchema>(json, Options); }
        catch (JsonException ex) { throw new UserFriendlyException($"Steps are not valid JSON: {ex.Message}"); }

        if (schema == null || schema.Steps.Count == 0)
            throw new UserFriendlyException("A workflow needs at least one step.");

        for (var i = 0; i < schema.Steps.Count; i++)
        {
            var s = schema.Steps[i];
            var label = string.IsNullOrWhiteSpace(s.Title) ? $"Step {i + 1}" : $"Step {i + 1} ('{s.Title}')";

            if (string.IsNullOrWhiteSpace(s.Title))
                throw new UserFriendlyException($"{label}: a title is required.");
            if (!ApproverTypes.All.Contains(s.ApproverType))
                throw new UserFriendlyException($"{label}: unknown approver type '{s.ApproverType}'.");
            if (s.ApproverType == ApproverTypes.Role && string.IsNullOrWhiteSpace(s.Role))
                throw new UserFriendlyException($"{label}: pick a role.");
            if (s.ApproverType == ApproverTypes.User && s.UserId == null)
                throw new UserFriendlyException($"{label}: pick a user.");
            if (s.ApproverType != ApproverTypes.Role && !string.IsNullOrWhiteSpace(s.Role))
                throw new UserFriendlyException($"{label}: a role only applies to the 'Role' approver type.");
            if (s.ApproverType != ApproverTypes.User && s.UserId != null)
                throw new UserFriendlyException($"{label}: a user only applies to the 'Specific user' approver type.");
            if (!OnRejectActions.All.Contains(s.OnReject))
                throw new UserFriendlyException($"{label}: unknown on-reject action '{s.OnReject}'.");
            if (i == 0 && s.OnReject == OnRejectActions.BackToPrevious)
                throw new UserFriendlyException($"{label}: the first step cannot send a reject back to a previous step.");
        }

        return schema;
    }
}

/// <summary>Parses a workflow's required fallback approver rule (FR-010): a role or a specific user, never line manager / manager's manager.</summary>
public static class FallbackRules
{
    public static (string Type, string? Role, Guid? UserId) Parse(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
            throw new UserFriendlyException("A fallback approver (role or specific user) is required to publish.");
        if (rule.StartsWith("role:", StringComparison.OrdinalIgnoreCase))
            return (ApproverTypes.Role, rule["role:".Length..], null);
        if (rule.StartsWith("user:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(rule["user:".Length..], out var id))
            return (ApproverTypes.User, null, id);
        throw new UserFriendlyException("The fallback approver must be a role or a specific user.");
    }
}
