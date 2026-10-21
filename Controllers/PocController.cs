using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.TenantManagement;
using Workmate.Poc.Application;
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

    public PocController(FormAppService forms, SubmissionAppService submissions, TaskAppService tasks,
        RoutingAppService routing, ApprovalWorkflowAppService approvalWorkflows, IRepository<Employee, Guid> employees,
        ITenantRepository tenants, TenantOnboardingAppService onboarding, IPermissionChecker permissionChecker,
        EmployeeAppService employeeService)
    {
        _forms = forms; _submissions = submissions; _tasks = tasks; _routing = routing;
        _approvalWorkflows = approvalWorkflows; _employees = employees; _tenants = tenants; _onboarding = onboarding;
        _permissionChecker = permissionChecker; _employeeService = employeeService;
    }

    [HttpPost("tenants")]
    public Task<CompanyCreatedDto> CreateCompanyAsync([FromBody] CreateCompanyInput input) => _onboarding.CreateCompanyAsync(input);

    [HttpPost("tenants/reset-admin-password")]
    public Task<ResetAdminPasswordDto> ResetAdminPasswordAsync([FromBody] ResetAdminPasswordInput input) =>
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
            WorkmatePermissions.EmployeesManage, WorkmatePermissions.OrgManage, WorkmatePermissions.TenantsManage
        };
        var result = new List<string>();
        foreach (var p in all)
            if (await _permissionChecker.IsGrantedAsync(p)) result.Add(p);
        return result;
    }

    [HttpPost("employees")]
    public Task<EmployeeCreatedDto> CreateEmployeeAsync([FromBody] SaveEmployeeInput input) => _employeeService.CreateAsync(input);

    [HttpPut("employees/{id:guid}")]
    public Task<EmployeeDto> UpdateEmployeeAsync(Guid id, [FromBody] SaveEmployeeInput input) => _employeeService.UpdateAsync(id, input);

    [HttpPost("employees/{id:guid}/deactivate")]
    public Task<DeactivationResultDto> DeactivateEmployeeAsync(Guid id) => _employeeService.DeactivateAsync(id);

    [HttpPost("employees/{id:guid}/activate")]
    public Task<EmployeeDto> ActivateEmployeeAsync(Guid id) => _employeeService.ActivateAsync(id);

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
