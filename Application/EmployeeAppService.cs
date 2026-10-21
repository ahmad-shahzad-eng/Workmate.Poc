using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Workmate.Poc.Application.Notifications;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application;

/// <summary>The company admin's employee management (spec 004 US1). Reads are open to any signed-in user (pickers).</summary>
public class EmployeeAppService : ApplicationService
{
    private readonly IRepository<Employee, Guid> _employees;
    private readonly IRepository<OrganizationUnit, Guid> _units;
    private readonly IRepository<JobRole, Guid> _roles;
    private readonly IRepository<EmployeeJobRole, Guid> _roleLinks;
    private readonly JobRoleLookup _jobRoles;
    private readonly IdentityUserManager _userManager;
    private readonly TaskReassignmentService _reassignment;
    private readonly NotificationService _notifications;

    public EmployeeAppService(IRepository<Employee, Guid> employees, IRepository<OrganizationUnit, Guid> units,
        IRepository<JobRole, Guid> roles, IRepository<EmployeeJobRole, Guid> roleLinks, JobRoleLookup jobRoles,
        IdentityUserManager userManager, TaskReassignmentService reassignment, NotificationService notifications)
    {
        _employees = employees; _units = units; _roles = roles; _roleLinks = roleLinks; _jobRoles = jobRoles;
        _userManager = userManager; _reassignment = reassignment;
        _notifications = notifications;
    }

    /// <summary>Active employees by name; inactive ones too only for an employee manager (contracts/api.md).</summary>
    public virtual async Task<List<EmployeeDto>> GetListAsync(bool includeInactive = false)
    {
        if (includeInactive) await AuthorizationService.CheckAsync(WorkmatePermissions.EmployeesManage);

        var all = await _employees.GetListAsync();
        var roles = await _jobRoles.GetRoleNamesByEmployeeAsync();
        var units = (await _units.GetListAsync()).ToDictionary(u => u.Id, u => u.DisplayName);
        return all.Where(e => includeInactive || e.IsActive).OrderBy(e => e.Name)
            .Select(e => Map(e, all, roles, units)).ToList();
    }

    /// <summary>Creates the sign-in account and the HR record together (FR-002). The account has no password:
    /// the person chooses one from the emailed invite (spec 005 FR-008).</summary>
    [Authorize(WorkmatePermissions.EmployeesManage)]
    public virtual async Task<EmployeeDto> CreateAsync(SaveEmployeeInput input)
    {
        var (name, email, roles) = await ValidateAsync(input, employeeId: null);

        var user = new IdentityUser(GuidGenerator.Create(), email, email, CurrentTenant.Id);
        ThrowIfFailed(await _userManager.CreateAsync(user)); // duplicate email in this company is refused here (US1 AC3)

        var employee = new Employee(GuidGenerator.Create(), CurrentTenant.Id, user.Id, name, email, input.ManagerId)
        {
            JobTitle = Clean(input.JobTitle), DepartmentId = input.DepartmentId
        };
        await _employees.InsertAsync(employee);
        await SetRolesAsync(employee, roles);
        await SyncDepartmentAsync(user, employee.DepartmentId);
        await _notifications.QueueAccountAsync(EmailEvents.AccountInvite, user.Id, email);

        return await GetDtoAsync(employee);
    }

    /// <summary>Manager and department changes apply to new steps only; existing tasks keep their candidates (FR-005).</summary>
    [Authorize(WorkmatePermissions.EmployeesManage)]
    public virtual async Task<EmployeeDto> UpdateAsync(Guid id, SaveEmployeeInput input)
    {
        var employee = await _employees.GetAsync(id);
        var (name, email, roles) = await ValidateAsync(input, id);

        var user = await _userManager.GetByIdAsync(employee.UserId);
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            ThrowIfFailed(await _userManager.SetEmailAsync(user, email));
            ThrowIfFailed(await _userManager.SetUserNameAsync(user, email)); // the user name is the email (spec 003)
        }

        employee.Name = name; employee.Email = email; employee.JobTitle = Clean(input.JobTitle);
        employee.ManagerId = input.ManagerId; employee.DepartmentId = input.DepartmentId;
        await _employees.UpdateAsync(employee);
        await SetRolesAsync(employee, roles);
        await SyncDepartmentAsync(user, employee.DepartmentId);

        return await GetDtoAsync(employee);
    }

    /// <summary>Blocks sign-in and moves the person's open tasks to their fallback approver (FR-007). Idempotent.</summary>
    [Authorize(WorkmatePermissions.EmployeesManage)]
    public virtual async Task<DeactivationResultDto> DeactivateAsync(Guid id)
    {
        var employee = await _employees.GetAsync(id);
        if (!employee.IsActive) return new DeactivationResultDto(await GetDtoAsync(employee), 0);
        if (employee.UserId == CurrentUser.Id) throw new UserFriendlyException("You cannot deactivate yourself.");

        employee.IsActive = false;
        // Saved before reassigning: the resolver filters inactive people in SQL, so they must not be picked again.
        await _employees.UpdateAsync(employee, autoSave: true);

        var user = await _userManager.GetByIdAsync(employee.UserId);
        user.SetIsActive(false);
        ThrowIfFailed(await _userManager.UpdateAsync(user));

        var moved = await _reassignment.ReassignOpenTasksAsync(employee);
        return new DeactivationResultDto(await GetDtoAsync(employee), moved);
    }

    /// <summary>Re-enables sign-in and assignment; tasks moved away on deactivation stay where they are.</summary>
    [Authorize(WorkmatePermissions.EmployeesManage)]
    public virtual async Task<EmployeeDto> ActivateAsync(Guid id)
    {
        var employee = await _employees.GetAsync(id);
        if (employee.IsActive) return await GetDtoAsync(employee);

        employee.IsActive = true;
        await _employees.UpdateAsync(employee);
        var user = await _userManager.GetByIdAsync(employee.UserId);
        user.SetIsActive(true);
        ThrowIfFailed(await _userManager.UpdateAsync(user));
        return await GetDtoAsync(employee);
    }

    private async Task<(string Name, string Email, List<JobRole> Roles)> ValidateAsync(SaveEmployeeInput input, Guid? employeeId)
    {
        var name = Clean(input.Name) ?? throw new UserFriendlyException("Name is required.");
        var email = Clean(input.Email) ?? throw new UserFriendlyException("Email is required.");

        var roles = new List<JobRole>();
        foreach (var roleName in (input.Roles ?? new()).Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            roles.Add(await _jobRoles.FindAsync(roleName) ?? throw new UserFriendlyException($"Unknown role '{roleName}'."));

        if (input.ManagerId is { } managerId)
        {
            if (managerId == employeeId) throw new UserFriendlyException("An employee cannot be their own manager.");
            if (await _employees.FindAsync(managerId) == null) // tenant-filtered: another company's id is not found
                throw new UserFriendlyException("The chosen manager does not belong to this company.");
            if (employeeId != null && await WouldCreateLoopAsync(employeeId.Value, managerId))
                throw new UserFriendlyException("That manager change would create a reporting loop.");
        }

        if (input.DepartmentId is { } departmentId && await _units.FindAsync(departmentId) == null)
            throw new UserFriendlyException("The chosen department does not belong to this company.");

        return (name, email, roles);
    }

    /// <summary>US1 AC2: walk up from the proposed manager; reaching the employee means a cycle.</summary>
    private async Task<bool> WouldCreateLoopAsync(Guid employeeId, Guid proposedManagerId)
    {
        var managers = (await _employees.GetListAsync()).ToDictionary(e => e.Id, e => e.ManagerId);
        var visited = new HashSet<Guid>();
        for (Guid? current = proposedManagerId; current != null && visited.Add(current.Value); current = managers.GetValueOrDefault(current.Value))
            if (current == employeeId) return true;
        return false;
    }

    /// <summary>Adds and removes only what changed, so the (employee, role) unique index never sees a duplicate.</summary>
    private async Task SetRolesAsync(Employee employee, List<JobRole> roles)
    {
        var wanted = roles.Select(r => r.Id).ToHashSet();
        var existing = await _roleLinks.GetListAsync(l => l.EmployeeId == employee.Id);
        foreach (var link in existing.Where(l => !wanted.Contains(l.JobRoleId)))
            await _roleLinks.DeleteAsync(link);
        foreach (var roleId in wanted.Except(existing.Select(l => l.JobRoleId)))
            await _roleLinks.InsertAsync(new EmployeeJobRole(GuidGenerator.Create(), CurrentTenant.Id, employee.Id, roleId));
    }

    /// <summary>Mirrors Employee.DepartmentId into ABP's user–unit link (at most one unit; research R3).</summary>
    private Task SyncDepartmentAsync(IdentityUser user, Guid? departmentId) =>
        _userManager.SetOrganizationUnitsAsync(user, departmentId is { } d ? new[] { d } : Array.Empty<Guid>());

    private async Task<EmployeeDto> GetDtoAsync(Employee employee)
    {
        await CurrentUnitOfWork!.SaveChangesAsync(); // the lookups below query the database, so flush pending changes first
        var all = await _employees.GetListAsync();
        var units = (await _units.GetListAsync()).ToDictionary(u => u.Id, u => u.DisplayName);
        return Map(employee, all, await _jobRoles.GetRoleNamesByEmployeeAsync(), units);
    }

    private static EmployeeDto Map(Employee e, List<Employee> all, Dictionary<Guid, List<string>> roles,
        Dictionary<Guid, string> units) =>
        new(e.Id, e.Name, e.Email, e.JobTitle, roles.GetValueOrDefault(e.Id) ?? new(), e.ManagerId,
            all.FirstOrDefault(m => m.Id == e.ManagerId)?.Name, e.DepartmentId,
            e.DepartmentId is { } d ? units.GetValueOrDefault(d) : null, e.IsActive);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ThrowIfFailed(Microsoft.AspNetCore.Identity.IdentityResult result)
    {
        if (!result.Succeeded)
            throw new UserFriendlyException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
