using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Workmate.Poc.Domain;
using Workmate.Poc.WorkflowEngine.Workflows;

namespace Workmate.Poc.Data;

public class DemoDataSeeder : ITransientDependency
{
    private readonly IUnitOfWorkManager _uow;
    private readonly IDbContextProvider<PocDbContext> _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guids;
    private readonly IRepository<Employee, Guid> _employees;
    private readonly IRepository<FormDefinition, Guid> _forms;
    private readonly IRepository<FormDefinitionVersion, Guid> _versions;
    private readonly IRepository<ProcessBinding, Guid> _bindings;
    private readonly IRepository<CompanyFormSettings, Guid> _companySettings;

    public DemoDataSeeder(IUnitOfWorkManager uow, IDbContextProvider<PocDbContext> db, ICurrentTenant currentTenant,
        IGuidGenerator guids, IRepository<Employee, Guid> employees,
        IRepository<FormDefinition, Guid> forms, IRepository<FormDefinitionVersion, Guid> versions,
        IRepository<ProcessBinding, Guid> bindings, IRepository<CompanyFormSettings, Guid> companySettings)
    {
        _uow = uow; _db = db; _currentTenant = currentTenant; _guids = guids;
        _employees = employees; _forms = forms; _versions = versions; _bindings = bindings; _companySettings = companySettings;
    }

    public async Task SeedAsync()
    {
        using (var uow = _uow.Begin(requiresNew: true, isTransactional: false))
        {
            var db = await _db.GetDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            await uow.CompleteAsync();
        }

        // Khalid and Noor are ordinary employees with the company-admin flag: they design forms
        // and still have a line manager, so their own requests route like anyone else's.
        await SeedTenantAsync(DemoTenants.Acme, withSickLeaveRoute: true,
            ("Omar Khalid", EmployeeRoles.Manager, null, false), ("Huda Ali", EmployeeRoles.Hr, null, false),
            ("Sara Ahmed", EmployeeRoles.Employee, "Omar Khalid", false), ("Khalid Mansour", EmployeeRoles.Employee, "Omar Khalid", true),
            ("Layla Haddad", EmployeeRoles.Finance, "Omar Khalid", false));

        await SeedTenantAsync(DemoTenants.Globex, withSickLeaveRoute: false,
            ("Fatima Saleh", EmployeeRoles.Manager, null, false), ("Yousef Nasser", EmployeeRoles.Hr, null, false),
            ("Ali Hassan", EmployeeRoles.Employee, "Fatima Saleh", false), ("Noor Ibrahim", EmployeeRoles.Employee, "Fatima Saleh", true),
            ("Yara Saleh", EmployeeRoles.Finance, "Fatima Saleh", false));
    }

    private async Task SeedTenantAsync(DemoTenant tenant, bool withSickLeaveRoute,
        params (string Name, string Role, string? Manager, bool IsAdmin)[] staff)
    {
        using (_currentTenant.Change(tenant.Id))
        using (var uow = _uow.Begin(requiresNew: true))
        {
            if (await _employees.GetCountAsync() > 0) return;

            // Phase K demo: Acme previews SurveyJS by default, Globex stays Native — also proves the
            // setting is tenant-isolated (spec 006 US2).
            await _companySettings.InsertAsync(new CompanyFormSettings(_guids.Create(), tenant.Id)
            {
                DefaultRenderer = withSickLeaveRoute ? FormRenderer.SurveyJs : FormRenderer.Native
            });

            var people = new Dictionary<string, Employee>();
            foreach (var p in staff)
            {
                var managerId = p.Manager != null ? people[p.Manager].Id : (Guid?)null;
                var e = new Employee(_guids.Create(), tenant.Id, p.Name, p.Role, managerId) { IsAdmin = p.IsAdmin };
                people[p.Name] = e;
                await _employees.InsertAsync(e);
            }

            // Leave: Acme routes sick leave straight to HR; everything else goes manager -> HR.
            // Globex has only the default route. Same form key, different process per tenant.
            var leave = await SeedFormAsync(tenant, "leave-request", "Leave request", "Time off", LeaveRequestSchemaV1);
            if (withSickLeaveRoute)
            {
                await _bindings.InsertAsync(new ProcessBinding(_guids.Create(), tenant.Id, leave.Id,
                    HrOnlyApprovalWorkflow.DefinitionId, priority: 1, conditionField: "leaveType", conditionValue: "Sick"));
                // Acme only: a v2 with a conditional required field and two pages, so the demo shows all
                // three US4 extensions (spec 006 FR-009).
                await AddLeaveRequestV2Async(leave);
            }
            await BindDefaultAsync(tenant, leave, LeaveApprovalWorkflow.DefinitionId);

            // Other employee self-service forms.
            await BindDefaultAsync(tenant, await SeedFormAsync(tenant, "work-from-home", "Work from home", "Time off", WorkFromHomeSchema),
                LeaveApprovalWorkflow.DefinitionId);
            await BindDefaultAsync(tenant, await SeedFormAsync(tenant, "expense-claim", "Expense claim", "Finance", ExpenseClaimSchema),
                LeaveApprovalWorkflow.DefinitionId);
            await BindDefaultAsync(tenant, await SeedFormAsync(tenant, "letter-request", "Letter request", "HR services", LetterRequestSchema),
                HrOnlyApprovalWorkflow.DefinitionId);

            await uow.CompleteAsync();
        }
    }

    private async Task<FormDefinition> SeedFormAsync(DemoTenant tenant, string key, string name, string category, string schema)
    {
        var form = new FormDefinition(_guids.Create(), tenant.Id, key, name, category);
        var v1 = new FormDefinitionVersion(_guids.Create(), tenant.Id, form.Id, 1, schema)
        {
            Status = FormVersionStatus.Published,
            PublishedAt = DateTime.UtcNow
        };
        form.CurrentVersionId = v1.Id;
        await _forms.InsertAsync(form);
        // autoSave: AddLeaveRequestV2Async (Acme only) re-fetches v1 by id right after this returns;
        // without a flush here that GetAsync races the still-pending insert and throws EntityNotFoundException.
        await _versions.InsertAsync(v1, autoSave: true);
        return form;
    }

    private Task BindDefaultAsync(DemoTenant tenant, FormDefinition form, string workflowDefinitionId) =>
        _bindings.InsertAsync(new ProcessBinding(_guids.Create(), tenant.Id, form.Id, workflowDefinitionId, priority: 10));

    private async Task AddLeaveRequestV2Async(FormDefinition form)
    {
        var v1 = await _versions.GetAsync(form.CurrentVersionId!.Value);
        v1.Status = FormVersionStatus.Retired;
        await _versions.UpdateAsync(v1);

        var v2 = new FormDefinitionVersion(_guids.Create(), form.TenantId, form.Id, 2, LeaveRequestSchemaV2)
        {
            Status = FormVersionStatus.Published,
            PublishedAt = DateTime.UtcNow
        };
        await _versions.InsertAsync(v2);
        form.CurrentVersionId = v2.Id;
        await _forms.UpdateAsync(form);
    }

    private const string LeaveRequestSchemaV1 = """
    {
      "title": "Leave request",
      "fields": [
        { "key": "leaveType", "label": "Leave type", "type": "select", "options": ["Annual", "Sick", "Unpaid"], "required": true, "reportable": true, "workflowVariable": true },
        { "key": "startDate", "label": "First day", "type": "date", "required": true, "reportable": true },
        { "key": "endDate", "label": "Last day", "type": "date", "required": true, "reportable": true },
        { "key": "days", "label": "Working days", "type": "number", "required": true, "reportable": true, "workflowVariable": true },
        { "key": "reason", "label": "Reason", "type": "textarea", "required": false }
      ]
    }
    """;

    /// <summary>Acme only — adds a conditional required field and two pages (spec 006 US4, FR-009).</summary>
    private const string LeaveRequestSchemaV2 = """
    {
      "title": "Leave request",
      "fields": [
        { "key": "leaveType", "label": "Leave type", "type": "select", "options": ["Annual", "Sick", "Unpaid"], "required": true, "reportable": true, "workflowVariable": true, "page": "Details" },
        { "key": "startDate", "label": "First day", "type": "date", "required": true, "reportable": true, "page": "Details" },
        { "key": "endDate", "label": "Last day", "type": "date", "required": true, "reportable": true, "page": "Details" },
        { "key": "days", "label": "Working days", "type": "number", "required": true, "reportable": true, "workflowVariable": true, "page": "Details" },
        { "key": "reason", "label": "Reason", "type": "textarea", "required": false, "page": "Notes" },
        { "key": "medicalNoteReference", "label": "Medical note reference", "type": "text", "required": true, "page": "Notes",
          "description": "Reference number for the doctor's note on file.",
          "visibleIf": { "field": "leaveType", "equals": "Sick" } }
      ]
    }
    """;

    private const string WorkFromHomeSchema = """
    {
      "title": "Work from home",
      "fields": [
        { "key": "startDate", "label": "From", "type": "date", "required": true, "reportable": true },
        { "key": "endDate", "label": "To", "type": "date", "required": true, "reportable": true },
        { "key": "days", "label": "Working days", "type": "number", "required": true, "reportable": true, "workflowVariable": true },
        { "key": "reachable", "label": "I will be reachable during working hours", "type": "checkbox", "required": true },
        { "key": "reason", "label": "Reason", "type": "textarea", "required": false }
      ]
    }
    """;

    private const string ExpenseClaimSchema = """
    {
      "title": "Expense claim",
      "fields": [
        { "key": "expenseDate", "label": "Expense date", "type": "date", "required": true, "reportable": true },
        { "key": "expenseType", "label": "Type", "type": "select", "options": ["Travel", "Meals", "Training", "Other"], "required": true, "reportable": true, "workflowVariable": true },
        { "key": "amount", "label": "Amount", "type": "number", "required": true, "reportable": true, "workflowVariable": true },
        { "key": "description", "label": "Description", "type": "textarea", "required": true }
      ]
    }
    """;

    private const string LetterRequestSchema = """
    {
      "title": "Letter request",
      "fields": [
        { "key": "letterType", "label": "Letter", "type": "select", "options": ["Salary certificate", "Employment letter", "No objection certificate"], "required": true, "reportable": true, "workflowVariable": true },
        { "key": "addressedTo", "label": "Addressed to", "type": "text", "required": true },
        { "key": "purpose", "label": "Purpose", "type": "textarea", "required": false }
      ]
    }
    """;
}
