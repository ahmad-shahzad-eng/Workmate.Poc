using System.Globalization;
using System.Text.Json;
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

public class FormDefinition : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public string Key { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public Guid? CurrentVersionId { get; set; }

    protected FormDefinition() { }
    public FormDefinition(Guid id, Guid? tenantId, string key, string name, string category) : base(id)
    {
        TenantId = tenantId; Key = key; Name = name; Category = category;
    }
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

        foreach (var f in schema.Fields)
        {
            if (string.IsNullOrWhiteSpace(f.Key) || string.IsNullOrWhiteSpace(f.Label))
                throw new UserFriendlyException("Every field needs a key and a label.");
            if (!FormField.Types.Contains(f.Type))
                throw new UserFriendlyException($"Field '{f.Key}' has unknown type '{f.Type}'. Use: {string.Join(", ", FormField.Types)}.");
            if (f.Type == "select" && (f.Options == null || f.Options.Count == 0))
                throw new UserFriendlyException($"Select field '{f.Key}' needs options.");
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
