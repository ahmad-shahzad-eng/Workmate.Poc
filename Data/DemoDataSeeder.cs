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

    public DemoDataSeeder(IUnitOfWorkManager uow, IDbContextProvider<PocDbContext> db, ICurrentTenant currentTenant,
        IGuidGenerator guids, IRepository<Employee, Guid> employees,
        IRepository<FormDefinition, Guid> forms, IRepository<FormDefinitionVersion, Guid> versions,
        IRepository<ProcessBinding, Guid> bindings)
    {
        _uow = uow; _db = db; _currentTenant = currentTenant; _guids = guids;
        _employees = employees; _forms = forms; _versions = versions; _bindings = bindings;
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
            ("Sara Ahmed", EmployeeRoles.Employee, "Omar Khalid", false), ("Khalid Mansour", EmployeeRoles.Employee, "Omar Khalid", true));

        await SeedTenantAsync(DemoTenants.Globex, withSickLeaveRoute: false,
            ("Fatima Saleh", EmployeeRoles.Manager, null, false), ("Yousef Nasser", EmployeeRoles.Hr, null, false),
            ("Ali Hassan", EmployeeRoles.Employee, "Fatima Saleh", false), ("Noor Ibrahim", EmployeeRoles.Employee, "Fatima Saleh", true));
    }

    private async Task SeedTenantAsync(DemoTenant tenant, bool withSickLeaveRoute,
        params (string Name, string Role, string? Manager, bool IsAdmin)[] staff)
    {
        using (_currentTenant.Change(tenant.Id))
        using (var uow = _uow.Begin(requiresNew: true))
        {
            if (await _employees.GetCountAsync() > 0) return;

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
                await _bindings.InsertAsync(new ProcessBinding(_guids.Create(), tenant.Id, leave.Id,
                    HrOnlyApprovalWorkflow.DefinitionId, priority: 1, conditionField: "leaveType", conditionValue: "Sick"));
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
        await _versions.InsertAsync(v1);
        return form;
    }

    private Task BindDefaultAsync(DemoTenant tenant, FormDefinition form, string workflowDefinitionId) =>
        _bindings.InsertAsync(new ProcessBinding(_guids.Create(), tenant.Id, form.Id, workflowDefinitionId, priority: 10));

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
