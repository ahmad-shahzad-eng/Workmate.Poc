using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Domain.Repositories;
using Workmate.Poc.Application;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Controllers;

/// <summary>Explicit routes for the demo UI. Production can use ABP's auto API controllers instead.</summary>
[Route("api/poc")]
public class PocController : AbpControllerBase
{
    private readonly FormAppService _forms;
    private readonly SubmissionAppService _submissions;
    private readonly TaskAppService _tasks;
    private readonly RoutingAppService _routing;
    private readonly ApprovalWorkflowAppService _approvalWorkflows;
    private readonly IRepository<Employee, Guid> _employees;

    public PocController(FormAppService forms, SubmissionAppService submissions, TaskAppService tasks,
        RoutingAppService routing, ApprovalWorkflowAppService approvalWorkflows, IRepository<Employee, Guid> employees)
    {
        _forms = forms; _submissions = submissions; _tasks = tasks; _routing = routing;
        _approvalWorkflows = approvalWorkflows; _employees = employees;
    }

    [HttpGet("tenants")]
    public List<TenantDto> GetTenants() =>
        DemoTenants.All.Select(t => new TenantDto(t.Id, t.Name, t.DisplayName)).ToList();

    [HttpGet("users")]
    public async Task<List<EmployeeDto>> GetUsersAsync()
    {
        var all = await _employees.GetListAsync();
        var users = all.OrderBy(e => e.Role).ThenBy(e => e.Name).Select(e => new EmployeeDto(e.Id, e.Name, e.Role,
            all.FirstOrDefault(m => m.Id == e.ManagerId)?.Name, e.IsAdmin)).ToList();
        // The vendor admin is not an employee of any company; listed so the demo can act as them.
        users.Add(new EmployeeDto(DemoTenants.VendorAdminId, DemoTenants.VendorAdminName, "Vendor admin", null, true));
        return users;
    }

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
