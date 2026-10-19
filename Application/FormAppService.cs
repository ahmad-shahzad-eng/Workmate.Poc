using System.Text.RegularExpressions;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application;

/// <summary>Form designer operations: create forms, drafts, publishing, immutable versions. Writes are Admin-only.</summary>
public class FormAppService : ApplicationService
{
    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9-]{1,49}$", RegexOptions.Compiled);

    private readonly CurrentEmployeeProvider _currentEmployee;
    private readonly IRepository<FormDefinition, Guid> _forms;
    private readonly IRepository<FormDefinitionVersion, Guid> _versions;

    public FormAppService(CurrentEmployeeProvider currentEmployee, IRepository<FormDefinition, Guid> forms,
        IRepository<FormDefinitionVersion, Guid> versions)
    {
        _currentEmployee = currentEmployee; _forms = forms; _versions = versions;
    }

    public virtual async Task<List<FormSummaryDto>> GetPublishedAsync()
    {
        var forms = await _forms.GetListAsync(f => f.IsActive && f.CurrentVersionId != null);
        var ids = forms.Select(f => f.CurrentVersionId!.Value).ToList();
        var versions = await _versions.GetListAsync(v => ids.Contains(v.Id));

        return forms.OrderBy(f => f.Category).ThenBy(f => f.Name).Select(f =>
        {
            var v = versions.Single(x => x.Id == f.CurrentVersionId);
            return new FormSummaryDto(f.Key, f.Name, f.Category, v.Id, v.VersionNo, v.SchemaJson);
        }).ToList();
    }

    /// <summary>Every form of the tenant, including ones not published yet (designer list).</summary>
    public virtual async Task<List<FormListItemDto>> GetAllAsync()
    {
        await _currentEmployee.EnsureAdminAsync();
        var forms = await _forms.GetListAsync();
        var formIds = forms.Select(f => f.Id).ToList();
        var versions = await _versions.GetListAsync(v => formIds.Contains(v.FormDefinitionId));

        return forms.OrderBy(f => f.Category).ThenBy(f => f.Name).Select(f =>
        {
            var mine = versions.Where(v => v.FormDefinitionId == f.Id).ToList();
            var published = mine.FirstOrDefault(v => v.Id == f.CurrentVersionId);
            return new FormListItemDto(f.Key, f.Name, f.Category, f.IsActive, published?.VersionNo,
                mine.Any(v => v.Status == FormVersionStatus.Draft));
        }).ToList();
    }

    /// <summary>Creates a form with a starter v1 draft. It is not offered to employees until published.</summary>
    public virtual async Task<FormDesignDto> CreateAsync(CreateFormInput input)
    {
        await _currentEmployee.EnsureAdminAsync();
        var key = (input.Key ?? "").Trim().ToLowerInvariant();
        if (!KeyPattern.IsMatch(key))
            throw new UserFriendlyException("Form key must be 2-50 characters: lowercase letters, digits and dashes, starting with a letter.");
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Category))
            throw new UserFriendlyException("Form name and category are required.");
        if (await _forms.AnyAsync(f => f.Key == key))
            throw new UserFriendlyException($"A form with key '{key}' already exists.");

        var form = new FormDefinition(GuidGenerator.Create(), CurrentTenant.Id, key, input.Name.Trim(), input.Category.Trim());
        await _forms.InsertAsync(form);
        await _versions.InsertAsync(new FormDefinitionVersion(GuidGenerator.Create(), CurrentTenant.Id, form.Id, 1,
            StarterSchema(form.Name)));

        await CurrentUnitOfWork!.SaveChangesAsync();
        return await GetDesignAsync(key);
    }

    public virtual async Task<FormDesignDto> GetDesignAsync(string key)
    {
        var form = await GetFormAsync(key);
        var versions = await GetVersionsAsync(form.Id);
        return new FormDesignDto(form.Key, form.Name, form.CurrentVersionId, versions.Select(Map).ToList());
    }

    /// <summary>Updates the open draft, or opens a new draft after the latest published version.</summary>
    public virtual async Task<FormVersionDto> SaveDraftAsync(string key, string schemaJson)
    {
        await _currentEmployee.EnsureAdminAsync();
        FormSchema.Parse(schemaJson);
        var form = await GetFormAsync(key);
        var latest = (await GetVersionsAsync(form.Id)).First();

        if (latest.Status == FormVersionStatus.Draft)
        {
            latest.UpdateDraft(schemaJson);
            await _versions.UpdateAsync(latest);
            return Map(latest);
        }

        var draft = new FormDefinitionVersion(GuidGenerator.Create(), CurrentTenant.Id, form.Id, latest.VersionNo + 1, schemaJson);
        await _versions.InsertAsync(draft);
        return Map(draft);
    }

    /// <summary>Publishes the draft. The previous version is retired but still renders its old submissions.</summary>
    public virtual async Task<FormVersionDto> PublishAsync(string key)
    {
        await _currentEmployee.EnsureAdminAsync();
        var form = await GetFormAsync(key);
        var versions = await GetVersionsAsync(form.Id);
        var draft = versions.FirstOrDefault(v => v.Status == FormVersionStatus.Draft)
                    ?? throw new UserFriendlyException("There is no draft to publish. Save a draft first.");

        FormSchema.Parse(draft.SchemaJson);

        foreach (var published in versions.Where(v => v.Status == FormVersionStatus.Published))
        {
            published.Status = FormVersionStatus.Retired;
            await _versions.UpdateAsync(published);
        }

        draft.Status = FormVersionStatus.Published;
        draft.PublishedAt = Clock.Now;
        draft.PublishedBy = CurrentUser.Id;
        await _versions.UpdateAsync(draft);

        form.CurrentVersionId = draft.Id;
        await _forms.UpdateAsync(form);
        return Map(draft);
    }

    private async Task<FormDefinition> GetFormAsync(string key) =>
        await _forms.FindAsync(f => f.Key == key) ?? throw new UserFriendlyException($"Form '{key}' does not exist.");

    private async Task<List<FormDefinitionVersion>> GetVersionsAsync(Guid formId) =>
        (await _versions.GetListAsync(v => v.FormDefinitionId == formId)).OrderByDescending(v => v.VersionNo).ToList();

    private static FormVersionDto Map(FormDefinitionVersion v) => new(v.Id, v.VersionNo, v.Status, v.SchemaJson, v.PublishedAt);

    private static string StarterSchema(string title) => System.Text.Json.JsonSerializer.Serialize(new
    {
        title,
        fields = new object[]
        {
            new { key = "details", label = "Details", type = "textarea", required = true }
        }
    });
}
