using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application.Notifications;

/// <summary>
/// Email wording per form (US2) and per company (US3), plus the From display name. Templates belong to the
/// form, not a form version (clarification Q2). Saving validates against the sandbox rules (FR-004);
/// "reset to default" deletes the row so resolution falls through to the next level (FR-003).
/// </summary>
[Authorize(WorkmatePermissions.FormsDesign)]
public class EmailTemplateAppService : ApplicationService
{
    private const string SampleFieldValue = "<b>sample</b>"; // preview shows that field values are escaped

    private readonly IRepository<EmailTemplate, Guid> _templates;
    private readonly IRepository<CompanyEmailSettings, Guid> _settings;
    private readonly IRepository<FormDefinition, Guid> _forms;
    private readonly IRepository<FormDefinitionVersion, Guid> _versions;
    private readonly EmailTemplateRenderer _renderer;
    private readonly NotificationService _notifications;

    public EmailTemplateAppService(IRepository<EmailTemplate, Guid> templates, IRepository<CompanyEmailSettings, Guid> settings,
        IRepository<FormDefinition, Guid> forms, IRepository<FormDefinitionVersion, Guid> versions,
        EmailTemplateRenderer renderer, NotificationService notifications)
    {
        _templates = templates; _settings = settings; _forms = forms; _versions = versions;
        _renderer = renderer; _notifications = notifications;
    }

    // ---------- Form overrides (US2): workflow events only

    public virtual async Task<List<EmailTemplateDto>> GetFormTemplatesAsync(string key)
    {
        var form = await GetFormAsync(key);
        var result = new List<EmailTemplateDto>();
        foreach (var (evt, audience) in EmailAudiences.ValidPairs.Where(p => EmailAudiences.IsWorkflowPair(p.Event, p.Audience)))
            result.Add(await GetEffectiveAsync(form.Id, evt, audience));
        return result;
    }

    public virtual async Task<EmailTemplateDto> SaveFormTemplateAsync(string key, string evt, string audience, SaveEmailTemplateInput input)
    {
        var form = await GetFormAsync(key);
        EnsureWorkflowPair(evt, audience);
        _renderer.Validate(input.Subject, input.Body, await GetFieldKeysAsync(form));
        await UpsertAsync(form.Id, evt, audience, input);
        return await GetEffectiveAsync(form.Id, evt, audience);
    }

    public virtual async Task ResetFormTemplateAsync(string key, string evt, string audience)
    {
        var form = await GetFormAsync(key);
        await _templates.DeleteAsync(t => t.FormDefinitionId == form.Id && t.Event == evt && t.Audience == audience);
    }

    public virtual async Task<EmailPreviewDto> PreviewFormAsync(string key, PreviewEmailInput input)
    {
        var form = await GetFormAsync(key);
        EnsureWorkflowPair(input.Event, input.Audience);
        var fieldKeys = await GetFieldKeysAsync(form);
        _renderer.Validate(input.Subject, input.Body, fieldKeys);
        return await PreviewAsync(input, form.Name, fieldKeys);
    }

    // ---------- Company defaults (US3): every pair, account events included; no fields.*

    public virtual async Task<List<EmailTemplateDto>> GetCompanyTemplatesAsync()
    {
        var result = new List<EmailTemplateDto>();
        foreach (var (evt, audience) in EmailAudiences.ValidPairs)
            result.Add(await GetEffectiveAsync(null, evt, audience));
        return result;
    }

    public virtual async Task<EmailTemplateDto> SaveCompanyTemplateAsync(string evt, string audience, SaveEmailTemplateInput input)
    {
        EnsureValidPair(evt, audience);
        _renderer.Validate(input.Subject, input.Body, allowedFieldKeys: null);
        await UpsertAsync(null, evt, audience, input);
        return await GetEffectiveAsync(null, evt, audience);
    }

    public virtual Task ResetCompanyTemplateAsync(string evt, string audience) =>
        _templates.DeleteAsync(t => t.FormDefinitionId == null && t.Event == evt && t.Audience == audience);

    public virtual async Task<EmailPreviewDto> PreviewCompanyAsync(PreviewEmailInput input)
    {
        EnsureValidPair(input.Event, input.Audience);
        _renderer.Validate(input.Subject, input.Body, allowedFieldKeys: null);
        return await PreviewAsync(input, "Annual leave", new HashSet<string>());
    }

    public virtual async Task<EmailSenderDto> GetSenderAsync()
    {
        var (name, effective) = await _notifications.GetSenderNameAsync();
        return new EmailSenderDto(name, effective);
    }

    public virtual async Task<EmailSenderDto> SetSenderAsync(SetEmailSenderInput input)
    {
        if (CurrentTenant.Id == null) throw new UserFriendlyException("Choose a company first.");
        var name = string.IsNullOrWhiteSpace(input.SenderName) ? null : input.SenderName.Trim().Replace("\r", " ").Replace("\n", " ");
        if (name?.Length > 100) throw new UserFriendlyException("Sender name can be at most 100 characters.");

        var settings = (await _settings.GetListAsync()).FirstOrDefault();
        if (settings == null)
            await _settings.InsertAsync(new CompanyEmailSettings(GuidGenerator.Create(), CurrentTenant.Id) { SenderName = name }, autoSave: true);
        else
        {
            settings.SenderName = name;
            await _settings.UpdateAsync(settings, autoSave: true);
        }
        return await GetSenderAsync();
    }

    // ---------- Helpers

    private async Task<EmailTemplateDto> GetEffectiveAsync(Guid? formId, string evt, string audience)
    {
        var (subject, body, source) = await _notifications.ResolveTemplateAsync(formId, evt, audience);
        return new EmailTemplateDto(evt, audience, subject, body, source);
    }

    private async Task UpsertAsync(Guid? formId, string evt, string audience, SaveEmailTemplateInput input)
    {
        var existing = await _templates.FindAsync(t => t.FormDefinitionId == formId && t.Event == evt && t.Audience == audience);
        if (existing == null)
            await _templates.InsertAsync(new EmailTemplate(GuidGenerator.Create(), CurrentTenant.Id, formId, evt, audience,
                input.Subject, input.Body), autoSave: true);
        else
        {
            existing.Subject = input.Subject; existing.Body = input.Body;
            await _templates.UpdateAsync(existing, autoSave: true);
        }
    }

    private async Task<EmailPreviewDto> PreviewAsync(PreviewEmailInput input, string formName, ISet<string> fieldKeys)
    {
        var account = input.Audience == EmailAudiences.Account;
        var link = account
            ? $"{_notifications.PublicBaseUrl}/?setPassword=1&tenant={CurrentTenant.Id}&user=sample&token=sample"
            : input.Audience == EmailAudiences.Approver
                ? $"{_notifications.PublicBaseUrl}/?view=inbox"
                : $"{_notifications.PublicBaseUrl}/?view=mine&submission=sample";
        var model = account
            ? new EmailModel(null, null, null, null, null, null, null, link, await _notifications.GetCompanyNameAsync(), null)
            : new EmailModel(formName, "1A2B3C4D", SubmissionStatus.InProcess, "Sample Person", "Sample Approver",
                "Manager approval", "Looks fine", link, await _notifications.GetCompanyNameAsync(),
                fieldKeys.ToDictionary(k => k, _ => (string?)SampleFieldValue));
        var rendered = _renderer.Render(input.Subject, input.Body, model);
        return new EmailPreviewDto(rendered.Subject, rendered.Text, rendered.Html);
    }

    /// <summary>A field may be referenced if any version of the form has it; missing values render empty (research R4).</summary>
    private async Task<HashSet<string>> GetFieldKeysAsync(FormDefinition form)
    {
        var keys = new HashSet<string>();
        foreach (var version in await _versions.GetListAsync(v => v.FormDefinitionId == form.Id))
        {
            try { keys.UnionWith(FormSchema.Parse(version.SchemaJson).Fields.Select(f => f.Key)); }
            catch (UserFriendlyException) { /* an unfinished draft adds no fields */ }
        }
        return keys;
    }

    private async Task<FormDefinition> GetFormAsync(string key) =>
        await _forms.FindAsync(f => f.Key == key) ?? throw new UserFriendlyException($"Form '{key}' was not found.");

    private static void EnsureWorkflowPair(string evt, string audience)
    {
        if (!EmailAudiences.IsWorkflowPair(evt, audience))
            throw new UserFriendlyException($"A form has no '{evt}' email for the {audience.ToLowerInvariant()} side. Account emails are company-wide.");
    }

    private static void EnsureValidPair(string evt, string audience)
    {
        if (!EmailAudiences.ValidPairs.Contains((evt, audience)))
            throw new UserFriendlyException($"There is no '{evt}' email for the {audience.ToLowerInvariant()} side.");
    }
}
