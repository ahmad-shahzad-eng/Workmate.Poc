using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application;

/// <summary>
/// The company's departments (spec 004 US2): ABP Identity OrganizationUnits for the tree, with the
/// head kept on the HR side (FR-001); and its job-role catalogue (US3). Reads are open to any
/// signed-in user (pickers).
/// </summary>
public class OrganizationAppService : ApplicationService
{
    private readonly IRepository<OrganizationUnit, Guid> _units;
    private readonly OrganizationUnitManager _unitManager;
    private readonly IRepository<DepartmentHead, Guid> _heads;
    private readonly IRepository<Employee, Guid> _employees;
    private readonly IRepository<JobRole, Guid> _roles;
    private readonly IRepository<EmployeeJobRole, Guid> _roleLinks;
    private readonly IRepository<ApprovalWorkflow, Guid> _workflows;
    private readonly IRepository<ApprovalWorkflowVersion, Guid> _workflowVersions;

    /// <summary>Code-defined workflows (by display name) and the role rules their steps use (research R6).</summary>
    private static readonly (string Name, string Rule)[] CodeDefinedRoleRules =
    {
        ("Line manager, then HR", "role:HR"), ("HR only", "role:HR")
    };

    public OrganizationAppService(IRepository<OrganizationUnit, Guid> units, OrganizationUnitManager unitManager,
        IRepository<DepartmentHead, Guid> heads, IRepository<Employee, Guid> employees, IRepository<JobRole, Guid> roles,
        IRepository<EmployeeJobRole, Guid> roleLinks, IRepository<ApprovalWorkflow, Guid> workflows,
        IRepository<ApprovalWorkflowVersion, Guid> workflowVersions)
    {
        _units = units; _unitManager = unitManager; _heads = heads; _employees = employees;
        _roles = roles; _roleLinks = roleLinks; _workflows = workflows; _workflowVersions = workflowVersions;
    }

    public virtual async Task<List<DepartmentDto>> GetDepartmentsAsync()
    {
        var units = await _units.GetListAsync();
        var heads = (await _heads.GetListAsync()).ToDictionary(h => h.OrganizationUnitId, h => h.EmployeeId);
        var employees = await _employees.GetListAsync();
        var names = employees.ToDictionary(e => e.Id, e => e.Name);
        return units.OrderBy(u => u.Code).Select(u =>
        {
            var headId = heads.TryGetValue(u.Id, out var h) ? h : (Guid?)null;
            return new DepartmentDto(u.Id, u.DisplayName, u.ParentId, headId, headId is { } id ? names.GetValueOrDefault(id) : null,
                employees.Count(e => e.DepartmentId == u.Id && e.IsActive));
        }).ToList();
    }

    [Authorize(WorkmatePermissions.OrgManage)]
    public virtual async Task<DepartmentDto> CreateDepartmentAsync(CreateDepartmentInput input)
    {
        var name = Clean(input.Name) ?? throw new UserFriendlyException("Department name is required.");
        if (input.ParentId is { } parentId && await _units.FindAsync(parentId) == null) // tenant-filtered
            throw new UserFriendlyException("The parent department does not belong to this company.");

        var unit = new OrganizationUnit(GuidGenerator.Create(), name, input.ParentId, CurrentTenant.Id);
        await _unitManager.CreateAsync(unit); // generates the hierarchical Code
        return await GetDtoAsync(unit.Id);
    }

    /// <summary>Renames the department and sets or clears its head (any employee of this company).</summary>
    [Authorize(WorkmatePermissions.OrgManage)]
    public virtual async Task<DepartmentDto> UpdateDepartmentAsync(Guid id, UpdateDepartmentInput input)
    {
        var unit = await _units.GetAsync(id);
        unit.DisplayName = Clean(input.Name) ?? throw new UserFriendlyException("Department name is required.");
        await _unitManager.UpdateAsync(unit);

        var head = await _heads.FindAsync(h => h.OrganizationUnitId == id);
        if (input.HeadEmployeeId is { } headId)
        {
            if (await _employees.FindAsync(headId) == null)
                throw new UserFriendlyException("The chosen head does not belong to this company.");
            if (head == null) await _heads.InsertAsync(new DepartmentHead(GuidGenerator.Create(), CurrentTenant.Id, id, headId));
            else { head.EmployeeId = headId; await _heads.UpdateAsync(head); }
        }
        else if (head != null)
        {
            await _heads.DeleteAsync(head);
        }

        return await GetDtoAsync(id);
    }

    /// <summary>US2 AC2: refused while it has employees or sub-departments. Checked first because
    /// OrganizationUnitManager.DeleteAsync deletes the whole subtree.</summary>
    [Authorize(WorkmatePermissions.OrgManage)]
    public virtual async Task DeleteDepartmentAsync(Guid id)
    {
        await _units.GetAsync(id);
        if (await _employees.AnyAsync(e => e.DepartmentId == id) || await _units.AnyAsync(u => u.ParentId == id))
            throw new UserFriendlyException("Move its employees and sub-departments first.");

        await _heads.DeleteAsync(h => h.OrganizationUnitId == id);
        await _unitManager.DeleteAsync(id);
    }

    public virtual async Task<List<JobRoleDto>> GetRolesAsync()
    {
        var active = (await _employees.GetListAsync(e => e.IsActive)).Select(e => e.Id).ToHashSet();
        var counts = (await _roleLinks.GetListAsync()).Where(l => active.Contains(l.EmployeeId))
            .GroupBy(l => l.JobRoleId).ToDictionary(g => g.Key, g => g.Count());
        return (await _roles.GetListAsync()).OrderBy(r => r.Name)
            .Select(r => new JobRoleDto(r.Id, r.Name, r.IsBuiltIn, counts.GetValueOrDefault(r.Id))).ToList();
    }

    [Authorize(WorkmatePermissions.EmployeesManage)]
    public virtual async Task<JobRoleDto> CreateRoleAsync(CreateJobRoleInput input)
    {
        var name = Clean(input.Name) ?? throw new UserFriendlyException("Role name is required.");
        if (name.Contains(':')) throw new UserFriendlyException("A role name cannot contain ':'."); // it becomes the rule "role:{name}"
        if (await _roles.AnyAsync(r => r.Name == name)) // NOCASE column: case-insensitive
            throw new UserFriendlyException($"A role named '{name}' already exists.");

        var role = await _roles.InsertAsync(new JobRole(GuidGenerator.Create(), CurrentTenant.Id, name, isBuiltIn: false), autoSave: true);
        return new JobRoleDto(role.Id, role.Name, role.IsBuiltIn, 0);
    }

    /// <summary>
    /// US3 (clarification Q5): any role, built-in or custom, can't be deleted while a published
    /// workflow uses it; an unused one is deleted and removed from everyone who held it.
    /// </summary>
    [Authorize(WorkmatePermissions.EmployeesManage)]
    public virtual async Task DeleteRoleAsync(Guid id)
    {
        var role = await _roles.GetAsync(id);
        var usedBy = await GetWorkflowsUsingRoleAsync(role.Name);
        if (usedBy.Count > 0)
            throw new UserFriendlyException($"Role '{role.Name}' is used by: {string.Join(", ", usedBy)}.");

        await _roleLinks.DeleteAsync(l => l.JobRoleId == id);
        await _roles.DeleteAsync(role);
    }

    /// <summary>Names of active workflows whose published version has a step or fallback "role:{name}" (research R6).</summary>
    private async Task<List<string>> GetWorkflowsUsingRoleAsync(string roleName)
    {
        var rule = $"role:{roleName}";
        var names = CodeDefinedRoleRules.Where(c => string.Equals(c.Rule, rule, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Name).ToList();

        var published = (await _workflowVersions.GetListAsync(v => v.Status == FormVersionStatus.Published)).ToLookup(v => v.ApprovalWorkflowId);
        foreach (var workflow in await _workflows.GetListAsync(w => w.IsActive))
        {
            var uses = published[workflow.Id].Any(v =>
                string.Equals(v.FallbackRule, rule, StringComparison.OrdinalIgnoreCase) ||
                ApprovalWorkflowSchema.Parse(v.StepsJson).Steps.Any(s => string.Equals(s.ToAssignmentRule(), rule, StringComparison.OrdinalIgnoreCase)));
            if (uses) names.Add(workflow.Name);
        }
        return names;
    }

    private async Task<DepartmentDto> GetDtoAsync(Guid id)
    {
        await CurrentUnitOfWork!.SaveChangesAsync(); // GetDepartmentsAsync queries the database
        return (await GetDepartmentsAsync()).First(d => d.Id == id);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
