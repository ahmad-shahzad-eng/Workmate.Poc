using System.Globalization;
using System.Text.Json;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application;

public class SubmissionAppService : ApplicationService
{
    private readonly CurrentEmployeeProvider _currentEmployee;
    private readonly ProcessManager _processes;
    private readonly IRepository<Employee, Guid> _employees;
    private readonly IRepository<FormDefinition, Guid> _forms;
    private readonly IRepository<FormDefinitionVersion, Guid> _versions;
    private readonly IRepository<FormSubmission, Guid> _submissions;
    private readonly IRepository<FormSubmissionValue, Guid> _values;
    private readonly IRepository<ProcessInstance, Guid> _instances;
    private readonly IRepository<ProcessTask, Guid> _tasks;
    private readonly IRepository<ProcessTaskCandidate, Guid> _candidates;
    private readonly IRepository<ProcessEvent, Guid> _events;

    public SubmissionAppService(CurrentEmployeeProvider currentEmployee, ProcessManager processes,
        IRepository<Employee, Guid> employees, IRepository<FormDefinition, Guid> forms,
        IRepository<FormDefinitionVersion, Guid> versions, IRepository<FormSubmission, Guid> submissions,
        IRepository<FormSubmissionValue, Guid> values, IRepository<ProcessInstance, Guid> instances,
        IRepository<ProcessTask, Guid> tasks, IRepository<ProcessTaskCandidate, Guid> candidates,
        IRepository<ProcessEvent, Guid> events)
    {
        _currentEmployee = currentEmployee; _processes = processes; _employees = employees; _forms = forms;
        _versions = versions; _submissions = submissions; _values = values; _instances = instances;
        _tasks = tasks; _candidates = candidates; _events = events;
    }

    public virtual async Task<Guid> SubmitAsync(SubmitFormInput input)
    {
        var employee = await _currentEmployee.GetAsync();
        var form = await _forms.FindAsync(f => f.Key == input.FormKey && f.IsActive && f.CurrentVersionId != null)
                   ?? throw new UserFriendlyException($"Form '{input.FormKey}' is not available.");
        var version = await _versions.GetAsync(form.CurrentVersionId!.Value);
        var schema = FormSchema.Parse(version.SchemaJson);

        var data = Validate(schema, input.Data);

        var submission = new FormSubmission(GuidGenerator.Create(), CurrentTenant.Id, form.Id, version.Id,
            employee.Id, JsonSerializer.Serialize(data), Clock.Now);
        await _submissions.InsertAsync(submission);

        foreach (var field in schema.Fields.Where(f => f.Reportable && data.ContainsKey(f.Key)))
            await _values.InsertAsync(ToValue(submission, field, data[field.Key]!));

        await _processes.AddEventAsync(submission.Id, null, null, "Submitted", employee.Name,
            $"Submitted {form.Name} on form version {version.VersionNo}.");

        // Start the workflow only after this transaction commits.
        // Production: publish FormSubmittedEto on ABP's distributed event bus with the outbox enabled.
        var tenantId = CurrentTenant.Id!.Value;
        CurrentUnitOfWork!.OnCompleted(() => _processes.StartForSubmissionAsync(tenantId, submission.Id));

        return submission.Id;
    }

    public virtual async Task<List<SubmissionListItemDto>> GetMineAsync()
    {
        var employee = await _currentEmployee.GetAsync();
        var submissions = (await _submissions.GetListAsync(s => s.SubmittedByUserId == employee.Id))
            .OrderByDescending(s => s.SubmittedAt).ToList();

        var forms = (await _forms.GetListAsync()).ToDictionary(f => f.Id);
        var versionIds = submissions.Select(s => s.FormVersionId).Distinct().ToList();
        var versions = (await _versions.GetListAsync(v => versionIds.Contains(v.Id))).ToDictionary(v => v.Id);

        return submissions.Select(s => new SubmissionListItemDto(s.Id, forms[s.FormDefinitionId].Name,
            versions[s.FormVersionId].VersionNo, s.Status, s.SubmittedAt)).ToList();
    }

    public virtual async Task<SubmissionDetailDto> GetDetailAsync(Guid id)
    {
        var me = await _currentEmployee.GetAsync();
        var submission = await _submissions.GetAsync(id); // tenant-filtered: other tenants get 404

        var tasks = (await _tasks.GetListAsync(t => t.SubmissionId == id)).OrderBy(t => t.CreationTime).ToList();
        var taskIds = tasks.Select(t => t.Id).ToList();
        var candidates = await _candidates.GetListAsync(c => taskIds.Contains(c.TaskId));

        var mayView = submission.SubmittedByUserId == me.Id || me.Role == EmployeeRoles.Hr || candidates.Any(c => c.UserId == me.Id);
        if (!mayView) throw new UserFriendlyException("You can only open your own requests or requests assigned to you.");

        var people = (await _employees.GetListAsync()).ToDictionary(e => e.Id, e => e.Name);
        var form = await _forms.GetAsync(submission.FormDefinitionId);
        var version = await _versions.GetAsync(submission.FormVersionId);
        var instance = await _instances.FindAsync(i => i.SubmissionId == id);
        var events = (await _events.GetListAsync(e => e.SubmissionId == id)).OrderBy(e => e.OccurredAt).ToList();

        return new SubmissionDetailDto(
            submission.Id, form.Name, version.VersionNo, version.Status, version.SchemaJson, submission.GetData(),
            submission.Status, submission.SubmittedAt, people.GetValueOrDefault(submission.SubmittedByUserId, "?"),
            instance?.WorkflowDefinitionId,
            tasks.Select(t => new TaskSummaryDto(t.Id, t.Title, t.Status, t.Outcome, t.Comment,
                candidates.Where(c => c.TaskId == t.Id).Select(c => people.GetValueOrDefault(c.UserId, "?")).ToList(),
                t.CompletedByUserId == null ? null : people.GetValueOrDefault(t.CompletedByUserId.Value), t.CompletedAt)).ToList(),
            events.Select(e => new EventDto(e.OccurredAt, e.EventType, e.Actor, e.Detail)).ToList());
    }

    private static Dictionary<string, string?> Validate(FormSchema schema, Dictionary<string, string?> raw)
    {
        var clean = new Dictionary<string, string?>();
        var errors = new List<string>();

        foreach (var f in schema.Fields)
        {
            raw.TryGetValue(f.Key, out var value);
            value = value?.Trim();

            if (string.IsNullOrEmpty(value) || (f.Type == "checkbox" && value == "false" && f.Required))
            {
                if (f.Required) errors.Add($"{f.Label} is required.");
                continue;
            }

            var ok = f.Type switch
            {
                "number" => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
                "date" => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "select" => f.Options!.Contains(value),
                "checkbox" => value is "true" or "false",
                _ => true
            };
            if (!ok) errors.Add($"{f.Label} has an invalid value.");
            clean[f.Key] = value;
        }

        if (errors.Count > 0) throw new UserFriendlyException(string.Join(" ", errors));
        return clean;
    }

    private FormSubmissionValue ToValue(FormSubmission s, FormField f, string value)
    {
        var v = new FormSubmissionValue(GuidGenerator.Create(), s.TenantId, s.Id, f.Key);
        switch (f.Type)
        {
            case "number": v.NumberValue = decimal.Parse(value, CultureInfo.InvariantCulture); break;
            case "date": v.DateValue = DateTime.Parse(value, CultureInfo.InvariantCulture); break;
            case "checkbox": v.BoolValue = value == "true"; break;
            default: v.StringValue = value; break;
        }
        return v;
    }
}
