using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Workmate.Poc.Domain;
using Workmate.Poc.WorkflowEngine;
using Workmate.Poc.WorkflowEngine.Workflows;

namespace Workmate.Poc.Application;

/// <summary>
/// Admin-only: binds a form to the workflow its submissions start. First active binding by
/// lowest priority whose condition matches wins (see ProcessManager.StartForSubmissionAsync).
/// </summary>
public class RoutingAppService : ApplicationService
{
    // Elsa's own store now also holds designed workflows (same gateway, same database as the two
    // code-defined ones) — ListPublishedAsync() returns ALL of them, so these two known ids are what
    // actually distinguish "global, code-defined" from "tenant-owned, designed" below.
    private static readonly HashSet<string> CodeDefinedIds = new() { LeaveApprovalWorkflow.DefinitionId, HrOnlyApprovalWorkflow.DefinitionId };

    private readonly CurrentEmployeeProvider _currentEmployee;
    private readonly IWorkflowGateway _gateway;
    private readonly IRepository<FormDefinition, Guid> _forms;
    private readonly IRepository<FormDefinitionVersion, Guid> _versions;
    private readonly IRepository<ProcessBinding, Guid> _bindings;
    private readonly IRepository<ApprovalWorkflow, Guid> _approvalWorkflows;

    public RoutingAppService(CurrentEmployeeProvider currentEmployee, IWorkflowGateway gateway,
        IRepository<FormDefinition, Guid> forms, IRepository<FormDefinitionVersion, Guid> versions,
        IRepository<ProcessBinding, Guid> bindings, IRepository<ApprovalWorkflow, Guid> approvalWorkflows)
    {
        _currentEmployee = currentEmployee; _gateway = gateway; _forms = forms; _versions = versions;
        _bindings = bindings; _approvalWorkflows = approvalWorkflows;
    }

    public virtual async Task<List<WorkflowDefinitionDto>> GetWorkflowsAsync()
    {
        await _currentEmployee.EnsureAdminAsync();
        var codeDefined = (await _gateway.ListPublishedAsync())
            .Where(w => CodeDefinedIds.Contains(w.DefinitionId))
            .Select(w => new WorkflowDefinitionDto(w.DefinitionId, w.Name));
        // Tenant isolation here comes from the repository's ABP tenant filter, not from asking Elsa to filter.
        var designed = (await GetPublishedDesignedWorkflowsAsync()).Select(w => new WorkflowDefinitionDto(w.ElsaDefinitionId, w.Name));
        return codeDefined.Concat(designed).OrderBy(w => w.Name).ToList();
    }

    private async Task<List<ApprovalWorkflow>> GetPublishedDesignedWorkflowsAsync() =>
        await _approvalWorkflows.GetListAsync(w => w.IsActive && w.CurrentVersionId != null);

    public virtual async Task<List<ProcessBindingDto>> GetBindingsAsync(string formKey)
    {
        await _currentEmployee.EnsureAdminAsync();
        var form = await GetFormAsync(formKey);
        var fields = (await LatestVersionAsync(form)).Fields;
        return (await _bindings.GetListAsync(b => b.FormDefinitionId == form.Id))
            .OrderBy(b => b.Priority).Select(b => Map(b, IsStale(b, fields))).ToList();
    }

    /// <summary>A condition whose field was removed, or whose value no longer matches the field's current type/options (US3 scenario 4).</summary>
    private static bool IsStale(ProcessBinding b, List<FormField> fields)
    {
        if (b.ConditionField == null) return false;
        var field = fields.FirstOrDefault(f => f.Key == b.ConditionField);
        return field == null || !field.ValidateValue(b.ConditionValue ?? "");
    }

    public virtual async Task<ProcessBindingDto> CreateBindingAsync(string formKey, SaveBindingInput input)
    {
        await _currentEmployee.EnsureAdminAsync();
        var form = await GetFormAsync(formKey);
        await ValidateAsync(form, input);

        var binding = new ProcessBinding(GuidGenerator.Create(), CurrentTenant.Id, form.Id, input.WorkflowDefinitionId,
            input.Priority, Blank(input.ConditionField), Blank(input.ConditionValue)) { IsActive = input.IsActive };
        await _bindings.InsertAsync(binding);
        return Map(binding);
    }

    public virtual async Task<ProcessBindingDto> UpdateBindingAsync(Guid id, SaveBindingInput input)
    {
        await _currentEmployee.EnsureAdminAsync();
        var binding = await _bindings.GetAsync(id); // tenant-filtered
        var form = await _forms.GetAsync(binding.FormDefinitionId);
        await ValidateAsync(form, input);

        binding.WorkflowDefinitionId = input.WorkflowDefinitionId;
        binding.ConditionField = Blank(input.ConditionField);
        binding.ConditionValue = Blank(input.ConditionValue);
        binding.Priority = input.Priority;
        binding.IsActive = input.IsActive;
        await _bindings.UpdateAsync(binding);
        return Map(binding);
    }

    /// <summary>Soft delete. Running processes keep their BindingId for the audit trail.</summary>
    public virtual async Task DeleteBindingAsync(Guid id)
    {
        await _currentEmployee.EnsureAdminAsync();
        await _bindings.DeleteAsync(id);
    }

    private async Task ValidateAsync(FormDefinition form, SaveBindingInput input)
    {
        if (!CodeDefinedIds.Contains(input.WorkflowDefinitionId))
        {
            // Not a global code-defined workflow: it must be one of THIS tenant's own published designed
            // workflows (ABP's tenant filter on the repository), never another tenant's (US2 scenario 3).
            var designed = await _approvalWorkflows.FindAsync(w => w.ElsaDefinitionId == input.WorkflowDefinitionId);
            if (designed == null || !designed.IsActive || designed.CurrentVersionId == null)
                throw new UserFriendlyException($"Workflow '{input.WorkflowDefinitionId}' is not published.");
        }

        if (input.Priority is < 0 or > 1000)
            throw new UserFriendlyException("Priority must be between 0 and 1000.");

        var field = Blank(input.ConditionField);
        var value = Blank(input.ConditionValue);
        if ((field == null) != (value == null))
            throw new UserFriendlyException("A condition needs both a field and a value, or neither for a default route.");

        if (field != null)
        {
            // The condition must refer to a field of the latest version (draft or published), and the
            // value must match that field's type/options — checked server-side, not only in the UI (FR-009).
            var matchedField = (await LatestVersionAsync(form)).Fields.FirstOrDefault(f => f.Key == field)
                ?? throw new UserFriendlyException($"Field '{field}' is not on the form.");
            if (!matchedField.ValidateValue(value!))
                throw new UserFriendlyException($"'{value}' is not a valid value for field '{field}'.");
        }
    }

    private async Task<FormSchema> LatestVersionAsync(FormDefinition form)
    {
        var latest = (await _versions.GetListAsync(v => v.FormDefinitionId == form.Id))
            .OrderByDescending(v => v.VersionNo).First();
        return FormSchema.Parse(latest.SchemaJson);
    }

    private async Task<FormDefinition> GetFormAsync(string key) =>
        await _forms.FindAsync(f => f.Key == key) ?? throw new UserFriendlyException($"Form '{key}' does not exist.");

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ProcessBindingDto Map(ProcessBinding b, bool isStale = false) =>
        new(b.Id, b.WorkflowDefinitionId, b.ConditionField, b.ConditionValue, b.Priority, b.IsActive, isStale);
}
