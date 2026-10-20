using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Workmate.Poc.Domain;

public static class FormVersionStatus
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Retired = "Retired";
}

public static class SubmissionStatus
{
    public const string Submitted = "Submitted";
    public const string InProcess = "InProcess";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

/// <summary>Which engine renders a form. "Default" defers to the tenant's CompanyFormSettings.</summary>
public static class FormRenderer
{
    public const string Default = "default";
    public const string Native = "native";
    public const string SurveyJs = "surveyjs";
    public static readonly string[] All = { Default, Native, SurveyJs };

    /// <summary>The company default itself is never "default" — it must resolve to one concrete renderer.</summary>
    public static readonly string[] Concrete = { Native, SurveyJs };
}

public class FormDefinition : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public string Key { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public Guid? CurrentVersionId { get; set; }

    /// <summary>"default" (use the tenant's CompanyFormSettings), "native", or "surveyjs".</summary>
    public string Renderer { get; set; } = FormRenderer.Default;

    protected FormDefinition() { }
    public FormDefinition(Guid id, Guid? tenantId, string key, string name, string category) : base(id)
    {
        TenantId = tenantId; Key = key; Name = name; Category = category;
    }
}

/// <summary>One row per tenant: the renderer used when a form's own setting is "default".</summary>
public class CompanyFormSettings : AggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public string DefaultRenderer { get; set; } = FormRenderer.Native;

    protected CompanyFormSettings() { }
    public CompanyFormSettings(Guid id, Guid? tenantId) : base(id) => TenantId = tenantId;
}

public class FormDefinitionVersion : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid FormDefinitionId { get; set; }
    public int VersionNo { get; set; }
    public string SchemaJson { get; set; } = default!;
    public string Status { get; set; } = FormVersionStatus.Draft;
    public DateTime? PublishedAt { get; set; }
    public Guid? PublishedBy { get; set; }

    protected FormDefinitionVersion() { }
    public FormDefinitionVersion(Guid id, Guid? tenantId, Guid formDefinitionId, int versionNo, string schemaJson) : base(id)
    {
        TenantId = tenantId; FormDefinitionId = formDefinitionId; VersionNo = versionNo; SchemaJson = schemaJson;
    }

    public void UpdateDraft(string schemaJson)
    {
        if (Status != FormVersionStatus.Draft)
            throw new UserFriendlyException("Published versions are immutable. Save a new draft instead.");
        SchemaJson = schemaJson;
    }
}

public class FormSubmission : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid FormDefinitionId { get; set; }
    public Guid FormVersionId { get; set; }
    public Guid SubmittedByUserId { get; set; }
    public string DataJson { get; set; } = "{}";
    public string Status { get; set; } = SubmissionStatus.Submitted;
    public DateTime SubmittedAt { get; set; }

    protected FormSubmission() { }
    public FormSubmission(Guid id, Guid? tenantId, Guid formDefinitionId, Guid formVersionId,
        Guid submittedByUserId, string dataJson, DateTime submittedAt) : base(id)
    {
        TenantId = tenantId; FormDefinitionId = formDefinitionId; FormVersionId = formVersionId;
        SubmittedByUserId = submittedByUserId; DataJson = dataJson; SubmittedAt = submittedAt;
    }

    public Dictionary<string, string?> GetData() =>
        JsonSerializer.Deserialize<Dictionary<string, string?>>(DataJson) ?? new();
}

/// <summary>Typed copy of a reportable field, so analytics never parses DataJson.</summary>
public class FormSubmissionValue : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid SubmissionId { get; set; }
    public string FieldKey { get; set; } = default!;
    public string? StringValue { get; set; }
    public decimal? NumberValue { get; set; }
    public DateTime? DateValue { get; set; }
    public bool? BoolValue { get; set; }

    protected FormSubmissionValue() { }
    public FormSubmissionValue(Guid id, Guid? tenantId, Guid submissionId, string fieldKey) : base(id)
    {
        TenantId = tenantId; SubmissionId = submissionId; FieldKey = fieldKey;
    }
}

// ---------------------------------------------------------------------------
// The JSON form schema (what a form builder produces and the renderer consumes)
// ---------------------------------------------------------------------------

public class FormSchema
{
    public string? Title { get; set; }
    public List<FormField> Fields { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static FormSchema Parse(string json)
    {
        FormSchema? schema;
        try { schema = JsonSerializer.Deserialize<FormSchema>(json, Options); }
        catch (JsonException ex) { throw new UserFriendlyException($"Schema is not valid JSON: {ex.Message}"); }

        if (schema == null || schema.Fields.Count == 0)
            throw new UserFriendlyException("Schema must contain at least one field.");

        var duplicate = schema.Fields.GroupBy(f => f.Key).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new UserFriendlyException($"Field key '{duplicate.Key}' is used more than once.");

        for (var i = 0; i < schema.Fields.Count; i++)
        {
            var f = schema.Fields[i];
            if (string.IsNullOrWhiteSpace(f.Key) || string.IsNullOrWhiteSpace(f.Label))
                throw new UserFriendlyException("Every field needs a key and a label.");
            if (!FormField.Types.Contains(f.Type))
                throw new UserFriendlyException($"Field '{f.Key}' has unknown type '{f.Type}'. Use: {string.Join(", ", FormField.Types)}.");
            if (f.Type == "select" && (f.Options == null || f.Options.Count == 0))
                throw new UserFriendlyException($"Select field '{f.Key}' needs options.");
            if (f.VisibleIf != null)
            {
                var targetIndex = schema.Fields.FindIndex(x => x.Key == f.VisibleIf.Field);
                if (targetIndex < 0 || targetIndex >= i)
                    throw new UserFriendlyException($"Field '{f.Key}': \"visible only when\" must point at an earlier field on the form.");
            }
        }
        return schema;
    }
}

public class FormField
{
    public static readonly string[] Types = { "text", "textarea", "number", "date", "select", "checkbox" };

    public string Key { get; set; } = default!;
    public string Label { get; set; } = default!;
    public string Type { get; set; } = "text";
    public List<string>? Options { get; set; }
    public bool Required { get; set; }
    public bool Reportable { get; set; }
    public bool WorkflowVariable { get; set; }

    /// <summary>Shown only when an earlier field equals a value — same rule as ProcessBinding's routing condition.</summary>
    public VisibleIfRule? VisibleIf { get; set; }
    public string? Description { get; set; }
    public string? Placeholder { get; set; }

    /// <summary>Fields sharing a Page are grouped together (SurveyJS: a wizard page; native: a heading).</summary>
    public string? Page { get; set; }

    /// <summary>Whether a (non-empty) value is valid for this field's type — number parses, date parses, select is one of Options.</summary>
    public bool ValidateValue(string value) => Type switch
    {
        "number" => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
        "date" => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "select" => Options!.Contains(value),
        "checkbox" => value is "true" or "false",
        _ => true
    };
}

public class VisibleIfRule
{
    public string Field { get; set; } = default!;

    [JsonPropertyName("equals")]
    public string Value { get; set; } = default!;

    /// <summary>Same comparison ProcessBinding.Matches uses for routing conditions.</summary>
    public bool Matches(string? value) => string.Equals(value, Value, StringComparison.OrdinalIgnoreCase);
}
