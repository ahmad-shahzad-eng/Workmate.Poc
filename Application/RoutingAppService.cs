using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Workmate.Poc.Domain;
using Workmate.Poc.WorkflowEngine;

namespace Workmate.Poc.Application;

/// <summary>
/// Admin-only: binds a form to the workflow its submissions start. First active binding by
/// lowest priority whose condition matches wins (see ProcessManager.StartForSubmissionAsync).
/// </summary>
public class RoutingAppService : ApplicationService
{
    private readonly CurrentEmployeeProvider _currentEmployee;
    private readonly IWorkflowGateway _gateway;
    private readonly IRepository<FormDefinition, Guid> _forms;
    private readonly IRepository<FormDefinitionVersion, Guid> _versions;
    private readonly IRepository<ProcessBinding, Guid> _bindings;

    public RoutingAppService(CurrentEmployeeProvider currentEmployee, IWorkflowGateway gateway,
        IRepository<FormDefinition, Guid> forms, IRepository<FormDefinitionVersion, Guid> versions,
        IRepository<ProcessBinding, Guid> bindings)
    {
        _currentEmployee = currentEmployee; _gateway = gateway; _forms = forms; _versions = versions; _bindings = bindings;
    }

    public virtual async Task<List<WorkflowDefinitionDto>> GetWorkflowsAsync()
    {
        await _currentEmployee.EnsureAdminAsync();
        return (await _gateway.ListPublishedAsync()).Select(w => new WorkflowDefinitionDto(w.DefinitionId, w.Name)).ToList();
    }

    public virtual async Task<List<ProcessBindingDto>> GetBindingsAsync(string formKey)
    {
        await _currentEmployee.EnsureAdminAsync();
        var form = await GetFormAsync(formKey);
        return (await _bindings.GetListAsync(b => b.FormDefinitionId == form.Id))
            .OrderBy(b => b.Priority).Select(Map).ToList();
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
        var workflows = await _gateway.ListPublishedAsync();
        if (workflows.All(w => w.DefinitionId != input.WorkflowDefinitionId))
            throw new UserFriendlyException($"Workflow '{input.WorkflowDefinitionId}' is not published.");

        if (input.Priority is < 0 or > 1000)
            throw new UserFriendlyException("Priority must be between 0 and 1000.");

        var field = Blank(input.ConditionField);
        var value = Blank(input.ConditionValue);
        if ((field == null) != (value == null))
            throw new UserFriendlyException("A condition needs both a field and a value, or neither for a default route.");

        if (field != null)
        {
            // The condition must refer to a field of the latest version (draft or published).
            var latest = (await _versions.GetListAsync(v => v.FormDefinitionId == form.Id))
                .OrderByDescending(v => v.VersionNo).First();
            if (FormSchema.Parse(latest.SchemaJson).Fields.All(f => f.Key != field))
                throw new UserFriendlyException($"Field '{field}' is not on the form.");
        }
    }

    private async Task<FormDefinition> GetFormAsync(string key) =>
        await _forms.FindAsync(f => f.Key == key) ?? throw new UserFriendlyException($"Form '{key}' does not exist.");

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ProcessBindingDto Map(ProcessBinding b) =>
        new(b.Id, b.WorkflowDefinitionId, b.ConditionField, b.ConditionValue, b.Priority, b.IsActive);
}
