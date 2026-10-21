using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Workmate.Poc.Application.Branding;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application.Notifications;

/// <summary>The request an email is about. The approver name is filled per recipient on the approver side.</summary>
public record NotificationContext(FormSubmission Submission, FormDefinition Form, Employee Initiator,
    string? StepTitle = null, string? ApproverName = null, string? Comment = null);

/// <summary>Where an effective template came from (contracts: <c>source</c>).</summary>
public static class EmailTemplateSource
{
    public const string Form = "form";
    public const string Company = "company";
    public const string BuiltIn = "built-in";
}

/// <summary>
/// Turns an event into queued emails (plan Design §2): resolves the template (form → company → built-in),
/// renders it, writes one EmailMessage per recipient and enqueues its send job. Runs in the caller's unit of
/// work and never commits on its own, so the rows and job records commit together with the event and the
/// job only sees them afterwards (Principle II, FR-002).
/// </summary>
public class NotificationService : ITransientDependency
{
    private readonly ICurrentTenant _currentTenant;
    private readonly ITenantStore _tenantStore;
    private readonly IGuidGenerator _guids;
    private readonly IConfiguration _config;
    private readonly IBackgroundJobManager _jobs;
    private readonly EmailTemplateRenderer _renderer;
    private readonly IRepository<EmailTemplate, Guid> _templates;
    private readonly IRepository<EmailMessage, Guid> _messages;
    private readonly IRepository<CompanyEmailSettings, Guid> _settings;
    private readonly IRepository<FormSubmission, Guid> _submissions;
    private readonly IRepository<FormDefinition, Guid> _forms;
    private readonly IRepository<Employee, Guid> _employees;
    private readonly BrandingReader _branding;

    public NotificationService(ICurrentTenant currentTenant, ITenantStore tenantStore, IGuidGenerator guids,
        IConfiguration config, IBackgroundJobManager jobs, EmailTemplateRenderer renderer,
        IRepository<EmailTemplate, Guid> templates, IRepository<EmailMessage, Guid> messages,
        IRepository<CompanyEmailSettings, Guid> settings, IRepository<FormSubmission, Guid> submissions,
        IRepository<FormDefinition, Guid> forms, IRepository<Employee, Guid> employees, BrandingReader branding)
    {
        _currentTenant = currentTenant; _tenantStore = tenantStore; _guids = guids; _config = config; _jobs = jobs;
        _renderer = renderer; _templates = templates; _messages = messages; _settings = settings;
        _submissions = submissions; _forms = forms; _employees = employees; _branding = branding;
    }

    public string PublicBaseUrl => (_config["Poc:PublicBaseUrl"] ?? "http://localhost:5080").TrimEnd('/');

    /// <summary>Loads the submission, its form and its initiator for <see cref="QueueAsync"/>.</summary>
    public async Task<NotificationContext> GetContextAsync(Guid submissionId, string? stepTitle = null,
        string? approverName = null, string? comment = null)
    {
        var submission = await _submissions.GetAsync(submissionId);
        var form = await _forms.GetAsync(submission.FormDefinitionId);
        var initiator = await _employees.GetAsync(submission.SubmittedByUserId); // holds an Employee.Id (spec 004)
        return new NotificationContext(submission, form, initiator, stepTitle, approverName, comment);
    }

    /// <summary>Queues a workflow email to each active recipient with an email address.</summary>
    public async Task QueueAsync(string evt, string audience, IEnumerable<Employee> recipients, NotificationContext ctx)
    {
        var (subject, body, _) = await ResolveTemplateAsync(ctx.Form.Id, evt, audience);
        var companyName = await GetCompanyNameAsync();
        var brand = await GetEmailBrandAsync();
        var link = audience == EmailAudiences.Approver
            ? $"{PublicBaseUrl}/?view=inbox"
            : $"{PublicBaseUrl}/?view=mine&submission={ctx.Submission.Id}";

        foreach (var recipient in recipients.Where(r => r.IsActive && !string.IsNullOrWhiteSpace(r.Email)).DistinctBy(r => r.Id))
        {
            var model = new EmailModel(ctx.Form.Name, RequestNumber(ctx.Submission.Id), ctx.Submission.Status,
                ctx.Initiator.Name, audience == EmailAudiences.Approver ? recipient.Name : ctx.ApproverName,
                ctx.StepTitle, ctx.Comment, link, companyName, ctx.Submission.GetData(), brand);
            var rendered = _renderer.Render(subject, body, model);

            var message = new EmailMessage(_guids.Create(), _currentTenant.Id, evt, audience, recipient.Email, rendered.Subject)
            {
                RecipientEmployeeId = recipient.Id, SubmissionId = ctx.Submission.Id,
                TextBody = rendered.Text, HtmlBody = rendered.Html
            };
            await InsertAndEnqueueAsync(message);
        }
    }

    /// <summary>Queues an invite or password-reset email. The body (with its one-time link) is rendered by the
    /// send job at each attempt and never stored (research R6), so only the subject is rendered here.</summary>
    public async Task QueueAccountAsync(string evt, Guid userId, string email)
    {
        var (subject, _, _) = await ResolveTemplateAsync(null, evt, EmailAudiences.Account);
        var rendered = _renderer.Render(subject, "-", AccountModel(await GetCompanyNameAsync(), null, null));
        var employee = await _employees.FindAsync(e => e.UserId == userId);

        var message = new EmailMessage(_guids.Create(), _currentTenant.Id, evt, EmailAudiences.Account, email, rendered.Subject)
        {
            RecipientUserId = userId, RecipientEmployeeId = employee?.Id
        };
        await InsertAndEnqueueAsync(message);
    }

    /// <summary>Renders an account email with its link (called by the send job, in the message's tenant).</summary>
    public async Task<RenderedEmail> RenderAccountAsync(string evt, string link)
    {
        var (subject, body, _) = await ResolveTemplateAsync(null, evt, EmailAudiences.Account);
        return _renderer.Render(subject, body, AccountModel(await GetCompanyNameAsync(), link, await GetEmailBrandAsync()));
    }

    /// <summary>FR-003: the form's own template, else the company default, else the built-in one.</summary>
    public async Task<(string Subject, string Body, string Source)> ResolveTemplateAsync(Guid? formDefinitionId, string evt, string audience)
    {
        if (formDefinitionId != null)
        {
            var own = await _templates.FindAsync(t => t.FormDefinitionId == formDefinitionId && t.Event == evt && t.Audience == audience);
            if (own != null) return (own.Subject, own.Body, EmailTemplateSource.Form);
        }

        var company = await _templates.FindAsync(t => t.FormDefinitionId == null && t.Event == evt && t.Audience == audience);
        if (company != null) return (company.Subject, company.Body, EmailTemplateSource.Company);

        var (subject, body) = BuiltInEmailTemplates.Get(evt, audience);
        return (subject, body, EmailTemplateSource.BuiltIn);
    }

    public async Task<string> GetCompanyNameAsync()
    {
        if (_currentTenant.Id is not { } tenantId) return "Workmate";
        return (await _tenantStore.FindAsync(tenantId))?.Name ?? "Workmate";
    }

    /// <summary>The From display name (US3): the company's own, or "{company} via Workmate".</summary>
    public async Task<(string? SenderName, string EffectiveSenderName)> GetSenderNameAsync()
    {
        var settings = _currentTenant.Id == null ? null : (await _settings.GetListAsync()).FirstOrDefault();
        var name = string.IsNullOrWhiteSpace(settings?.SenderName) ? null : settings!.SenderName;
        return (name, name ?? (_currentTenant.Id == null ? "Workmate" : $"{await GetCompanyNameAsync()} via Workmate"));
    }

    /// <summary>Makes a Failed message Queued again and enqueues a new send job (Email log Resend).</summary>
    public async Task RequeueAsync(EmailMessage message)
    {
        message.Status = EmailStatus.Queued;
        message.Attempts = 0;
        message.LastError = null;
        await _messages.UpdateAsync(message);
        await _jobs.EnqueueAsync(new SendEmailArgs { TenantId = message.TenantId, EmailMessageId = message.Id });
    }

    /// <summary>R9: there's no request-number column; the first 8 hex characters of the id stand in for one.</summary>
    public static string RequestNumber(Guid submissionId) => submissionId.ToString("N")[..8].ToUpperInvariant();

    /// <summary>The current company's effective branding, for the HTML layout (spec 007 FR-009).</summary>
    public async Task<EmailBrand> GetEmailBrandAsync()
    {
        var b = await _branding.GetCurrentAsync();
        return new EmailBrand(b.EffectiveDisplayName, b.EffectiveBrandColor, b.OnBrandColor);
    }

    private static EmailModel AccountModel(string companyName, string? link, EmailBrand? brand) =>
        new(null, null, null, null, null, null, null, link, companyName, null, brand);

    private async Task InsertAndEnqueueAsync(EmailMessage message)
    {
        await _messages.InsertAsync(message);
        await _jobs.EnqueueAsync(new SendEmailArgs { TenantId = message.TenantId, EmailMessageId = message.Id });
    }
}
