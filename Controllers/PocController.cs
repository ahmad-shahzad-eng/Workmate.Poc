using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.TenantManagement;
using Workmate.Poc.Application;
using Workmate.Poc.Application.Branding;
using Workmate.Poc.Application.Notifications;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Controllers;

/// <summary>Explicit routes for the demo UI. Production can use ABP's auto API controllers instead.
/// Every route needs a signed-in caller: an anonymous request still resolves a tenant from the
/// <c>__tenant</c> header (needed for /connect/token), so without this it could read that tenant's data.</summary>
[Authorize]
[Route("api/poc")]
public class PocController : AbpControllerBase
{
    private readonly FormAppService _forms;
    private readonly SubmissionAppService _submissions;
    private readonly TaskAppService _tasks;
    private readonly RoutingAppService _routing;
    private readonly ApprovalWorkflowAppService _approvalWorkflows;
    private readonly IRepository<Employee, Guid> _employees;
    private readonly ITenantRepository _tenants;
    private readonly TenantOnboardingAppService _onboarding;
    private readonly IPermissionChecker _permissionChecker;
    private readonly EmployeeAppService _employeeService;
    private readonly OrganizationAppService _organization;
    private readonly AccountAppService _account;
    private readonly EmailLogAppService _emailLog;
    private readonly EmailTemplateAppService _emailTemplates;
    private readonly BrandingAppService _branding;

    public PocController(FormAppService forms, SubmissionAppService submissions, TaskAppService tasks,
        RoutingAppService routing, ApprovalWorkflowAppService approvalWorkflows, IRepository<Employee, Guid> employees,
        ITenantRepository tenants, TenantOnboardingAppService onboarding, IPermissionChecker permissionChecker,
        EmployeeAppService employeeService, OrganizationAppService organization, AccountAppService account,
        EmailLogAppService emailLog, EmailTemplateAppService emailTemplates, BrandingAppService branding)
    {
        _forms = forms; _submissions = submissions; _tasks = tasks; _routing = routing;
        _approvalWorkflows = approvalWorkflows; _employees = employees; _tenants = tenants; _onboarding = onboarding;
        _permissionChecker = permissionChecker; _employeeService = employeeService; _organization = organization;
        _account = account; _emailLog = emailLog; _emailTemplates = emailTemplates; _branding = branding;
    }

    // --- Branding (spec 007)
    [HttpGet("branding")]
    public Task<BrandingDto> GetBrandingAsync() => _branding.GetAsync();

    /// <summary>Anonymous: the sign-in screen shows the picked company's name, colours and logo (FR-008).</summary>
    [AllowAnonymous]
    [HttpGet("branding/public")]
    public Task<BrandingDto> GetPublicBrandingAsync([FromQuery] string? company = null, [FromQuery] Guid? tenantId = null) =>
        _branding.GetPublicAsync(company, tenantId);

    [HttpPut("branding")]
    public Task<BrandingDto> SaveBrandingAsync([FromBody] SaveBrandingInput input) => _branding.SaveAsync(input);

    /// <summary>Multipart upload (field "logo"). Read with a cap just over the limit, so a huge upload is refused
    /// without buffering it all; the validator then gives the size message.</summary>
    [HttpPut("branding/logo")]
    [RequestSizeLimit(CompanyBranding.MaxLogoBytes + 64 * 1024)]
    public async Task<BrandingDto> SetLogoAsync(IFormFile? logo)
    {
        if (logo == null) throw new Volo.Abp.UserFriendlyException("Choose a logo file.");
        if (logo.Length > CompanyBranding.MaxLogoBytes) throw new Volo.Abp.UserFriendlyException("Logos can be at most 200 KB.");
        using var buffer = new MemoryStream();
        await logo.CopyToAsync(buffer);
        return await _branding.SetLogoAsync(buffer.ToArray());
    }

    [HttpDelete("branding/logo")]
    public Task RemoveLogoAsync() => _branding.RemoveLogoAsync();

    [HttpDelete("branding")]
    public Task ResetBrandingAsync() => _branding.ResetAsync();

    /// <summary>Anonymous image. The CSP sandbox means even an SVG opened directly can never run script (research R8).</summary>
    [AllowAnonymous]
    [HttpGet("branding/logo/{tenantId:guid}")]
    public async Task<IActionResult> GetLogoAsync(Guid tenantId)
    {
        var logo = await _branding.GetLogoAsync(tenantId);
        if (logo is not { } l) return NotFound();
        Response.Headers.CacheControl = "public, max-age=86400";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        return File(l.Bytes, l.ContentType);
    }

    [HttpPost("tenants")]
    public Task<CompanyCreatedDto> CreateCompanyAsync([FromBody] CreateCompanyInput input) => _onboarding.CreateCompanyAsync(input);

    [HttpPost("tenants/reset-admin-password")]
    public Task ResetAdminPasswordAsync([FromBody] ResetAdminPasswordInput input) =>
        _onboarding.ResetClientAdminPasswordAsync(input);

    [HttpPost("tenants/assign-admin")]
    public Task AssignAdminAsync([FromBody] AssignClientAdminInput input) => _onboarding.AssignClientAdminAsync(input);

    /// <summary>Anonymous: the sign-in screen's Company picker needs this before anyone is authenticated.</summary>
    [AllowAnonymous]
    [HttpGet("tenants")]
    public async Task<List<TenantDto>> GetTenantsAsync() =>
        (await _tenants.GetListAsync()).Select(t => new TenantDto(t.Id, t.Name)).ToList();

    /// <summary>The signed-in user's own granted Workmate permissions, so the UI can decide menu
    /// visibility without the removed Employee.IsAdmin flag (spec 003 US3).</summary>
    [HttpGet("my-permissions")]
    public async Task<List<string>> GetMyPermissionsAsync()
    {
        var all = new[]
        {
            WorkmatePermissions.FormsDesign, WorkmatePermissions.RoutingManage, WorkmatePermissions.WorkflowsDesign,
            WorkmatePermissions.EmployeesManage, WorkmatePermissions.OrgManage, WorkmatePermissions.BrandingManage,
            WorkmatePermissions.TenantsManage
        };
        var result = new List<string>();
        foreach (var p in all)
            if (await _permissionChecker.IsGrantedAsync(p)) result.Add(p);
        return result;
    }

    /// <summary>Anonymous (spec 005): always 204, whether or not the account exists. Company from <c>__tenant</c>.</summary>
    [AllowAnonymous]
    [HttpPost("account/forgot-password")]
    public Task ForgotPasswordAsync([FromBody] ForgotPasswordInput input) => _account.ForgotPasswordAsync(input);

    /// <summary>Anonymous (spec 005): protected by the one-time token from the emailed link.</summary>
    [AllowAnonymous]
    [HttpPost("account/set-password")]
    public Task SetPasswordAsync([FromBody] SetPasswordInput input) => _account.SetPasswordAsync(input);

    [HttpGet("emails")]
    public Task<List<EmailLogItemDto>> GetEmailsAsync([FromQuery] string? status = null) => _emailLog.GetListAsync(status);

    [HttpPost("emails/{id:guid}/resend")]
    public Task<EmailLogItemDto> ResendEmailAsync(Guid id) => _emailLog.ResendAsync(id);

    [HttpPost("employees")]
    public Task<EmployeeDto> CreateEmployeeAsync([FromBody] SaveEmployeeInput input) => _employeeService.CreateAsync(input);

    [HttpPut("employees/{id:guid}")]
    public Task<EmployeeDto> UpdateEmployeeAsync(Guid id, [FromBody] SaveEmployeeInput input) => _employeeService.UpdateAsync(id, input);

    [HttpPost("employees/{id:guid}/deactivate")]
    public Task<DeactivationResultDto> DeactivateEmployeeAsync(Guid id) => _employeeService.DeactivateAsync(id);

    [HttpPost("employees/{id:guid}/activate")]
    public Task<EmployeeDto> ActivateEmployeeAsync(Guid id) => _employeeService.ActivateAsync(id);

    [HttpGet("departments")]
    public Task<List<DepartmentDto>> GetDepartmentsAsync() => _organization.GetDepartmentsAsync();

    [HttpPost("departments")]
    public Task<DepartmentDto> CreateDepartmentAsync([FromBody] CreateDepartmentInput input) => _organization.CreateDepartmentAsync(input);

    [HttpPut("departments/{id:guid}")]
    public Task<DepartmentDto> UpdateDepartmentAsync(Guid id, [FromBody] UpdateDepartmentInput input) =>
        _organization.UpdateDepartmentAsync(id, input);

    [HttpDelete("departments/{id:guid}")]
    public Task DeleteDepartmentAsync(Guid id) => _organization.DeleteDepartmentAsync(id);

    [HttpGet("roles")]
    public Task<List<JobRoleDto>> GetRolesAsync() => _organization.GetRolesAsync();

    [HttpPost("roles")]
    public Task<JobRoleDto> CreateRoleAsync([FromBody] CreateJobRoleInput input) => _organization.CreateRoleAsync(input);

    [HttpDelete("roles/{id:guid}")]
    public Task DeleteRoleAsync(Guid id) => _organization.DeleteRoleAsync(id);

    [HttpGet("users")]
    public Task<List<EmployeeDto>> GetUsersAsync([FromQuery] bool includeInactive = false) =>
        _employeeService.GetListAsync(includeInactive);

    [HttpGet("forms")]
    public Task<List<FormSummaryDto>> GetFormsAsync() => _forms.GetPublishedAsync();

    [HttpGet("forms/all")]
    public Task<List<FormListItemDto>> GetAllFormsAsync() => _forms.GetAllAsync();

    [HttpPost("forms")]
    public Task<FormDesignDto> CreateFormAsync([FromBody] CreateFormInput input) => _forms.CreateAsync(input);

    [HttpGet("forms/{key}/design")]
    public Task<FormDesignDto> GetDesignAsync(string key) => _forms.GetDesignAsync(key);

    [HttpPut("forms/{key}/draft")]
    public Task<FormVersionDto> SaveDraftAsync(string key, [FromBody] SaveDraftInput input) =>
        _forms.SaveDraftAsync(key, input.SchemaJson);

    [HttpPost("forms/{key}/publish")]
    public Task<FormVersionDto> PublishAsync(string key) => _forms.PublishAsync(key);

    [HttpPut("forms/{key}/renderer")]
    public Task<FormDesignDto> SetRendererAsync(string key, [FromBody] SetRendererInput input) =>
        _forms.SetRendererAsync(key, input.Renderer);

    [HttpGet("company-settings/renderer")]
    public async Task<CompanyRendererDto> GetCompanyRendererAsync() => new(await _forms.GetCompanyRendererAsync());

    [HttpPut("company-settings/renderer")]
    public Task SetCompanyRendererAsync([FromBody] SetCompanyRendererInput input) =>
        _forms.SetCompanyRendererAsync(input.DefaultRenderer);

    // --- Email templates (spec 005 US2: per form; US3: company defaults and sender name)
    [HttpGet("forms/{key}/emails")]
    public Task<List<EmailTemplateDto>> GetFormEmailsAsync(string key) => _emailTemplates.GetFormTemplatesAsync(key);

    [HttpPut("forms/{key}/emails/{evt}/{audience}")]
    public Task<EmailTemplateDto> SaveFormEmailAsync(string key, string evt, string audience, [FromBody] SaveEmailTemplateInput input) =>
        _emailTemplates.SaveFormTemplateAsync(key, evt, audience, input);

    [HttpDelete("forms/{key}/emails/{evt}/{audience}")]
    public Task ResetFormEmailAsync(string key, string evt, string audience) => _emailTemplates.ResetFormTemplateAsync(key, evt, audience);

    [HttpPost("forms/{key}/emails/preview")]
    public Task<EmailPreviewDto> PreviewFormEmailAsync(string key, [FromBody] PreviewEmailInput input) =>
        _emailTemplates.PreviewFormAsync(key, input);

    [HttpGet("company-settings/emails")]
    public Task<List<EmailTemplateDto>> GetCompanyEmailsAsync() => _emailTemplates.GetCompanyTemplatesAsync();

    [HttpPut("company-settings/emails/{evt}/{audience}")]
    public Task<EmailTemplateDto> SaveCompanyEmailAsync(string evt, string audience, [FromBody] SaveEmailTemplateInput input) =>
        _emailTemplates.SaveCompanyTemplateAsync(evt, audience, input);

    [HttpDelete("company-settings/emails/{evt}/{audience}")]
    public Task ResetCompanyEmailAsync(string evt, string audience) => _emailTemplates.ResetCompanyTemplateAsync(evt, audience);

    [HttpPost("company-settings/emails/preview")]
    public Task<EmailPreviewDto> PreviewCompanyEmailAsync([FromBody] PreviewEmailInput input) => _emailTemplates.PreviewCompanyAsync(input);

    [HttpGet("company-settings/email-sender")]
    public Task<EmailSenderDto> GetEmailSenderAsync() => _emailTemplates.GetSenderAsync();

    [HttpPut("company-settings/email-sender")]
    public Task<EmailSenderDto> SetEmailSenderAsync([FromBody] SetEmailSenderInput input) => _emailTemplates.SetSenderAsync(input);

    [HttpGet("workflows")]
    public Task<List<WorkflowDefinitionDto>> GetWorkflowsAsync() => _routing.GetWorkflowsAsync();

    [HttpGet("workflows/design")]
    public Task<List<ApprovalWorkflowListItemDto>> GetApprovalWorkflowsAsync() => _approvalWorkflows.GetAllAsync();

    [HttpPost("workflows/design")]
    public Task<ApprovalWorkflowDesignDto> CreateApprovalWorkflowAsync([FromBody] CreateApprovalWorkflowInput input) =>
        _approvalWorkflows.CreateAsync(input);

    [HttpGet("workflows/design/{key}")]
    public Task<ApprovalWorkflowDesignDto> GetApprovalWorkflowDesignAsync(string key) => _approvalWorkflows.GetDesignAsync(key);

    [HttpPut("workflows/design/{key}/draft")]
    public Task<ApprovalWorkflowVersionDto> SaveApprovalWorkflowDraftAsync(string key, [FromBody] SaveApprovalWorkflowDraftInput input) =>
        _approvalWorkflows.SaveDraftAsync(key, input);

    [HttpPost("workflows/design/{key}/publish")]
    public Task<ApprovalWorkflowVersionDto> PublishApprovalWorkflowAsync(string key) => _approvalWorkflows.PublishAsync(key);

    [HttpGet("forms/{key}/bindings")]
    public Task<List<ProcessBindingDto>> GetBindingsAsync(string key) => _routing.GetBindingsAsync(key);

    [HttpPost("forms/{key}/bindings")]
    public Task<ProcessBindingDto> CreateBindingAsync(string key, [FromBody] SaveBindingInput input) =>
        _routing.CreateBindingAsync(key, input);

    [HttpPut("bindings/{id:guid}")]
    public Task<ProcessBindingDto> UpdateBindingAsync(Guid id, [FromBody] SaveBindingInput input) =>
        _routing.UpdateBindingAsync(id, input);

    [HttpDelete("bindings/{id:guid}")]
    public Task DeleteBindingAsync(Guid id) => _routing.DeleteBindingAsync(id);

    [HttpPost("submissions")]
    public Task<Guid> SubmitAsync([FromBody] SubmitFormInput input) => _submissions.SubmitAsync(input);

    [HttpGet("submissions/mine")]
    public Task<List<SubmissionListItemDto>> GetMineAsync() => _submissions.GetMineAsync();

    [HttpGet("submissions/{id:guid}")]
    public Task<SubmissionDetailDto> GetSubmissionAsync(Guid id) => _submissions.GetDetailAsync(id);

    [HttpGet("tasks/inbox")]
    public Task<List<InboxItemDto>> GetInboxAsync() => _tasks.GetInboxAsync();

    [HttpPost("tasks/{id:guid}/complete")]
    public Task CompleteTaskAsync(Guid id, [FromBody] CompleteTaskInput input) => _tasks.CompleteAsync(id, input);
}
