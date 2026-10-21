using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application;

/// <summary>
/// Reads the job-role catalogue (spec 004 FR-006). Role names compare case-insensitively: the
/// column uses SQLite's NOCASE collation, so an equality filter does too.
/// </summary>
public class JobRoleLookup : ITransientDependency
{
    private readonly IRepository<JobRole, Guid> _roles;
    private readonly IRepository<EmployeeJobRole, Guid> _links;
    private readonly IRepository<Employee, Guid> _employees;

    public JobRoleLookup(IRepository<JobRole, Guid> roles, IRepository<EmployeeJobRole, Guid> links,
        IRepository<Employee, Guid> employees)
    {
        _roles = roles; _links = links; _employees = employees;
    }

    public Task<JobRole?> FindAsync(string roleName) => _roles.FindAsync(r => r.Name == roleName);

    public async Task<List<string>> GetRoleNamesAsync(Guid employeeId) =>
        (await GetRoleNamesByEmployeeAsync()).GetValueOrDefault(employeeId) ?? new();

    /// <summary>Every employee's role names in one pass, for lists.</summary>
    public async Task<Dictionary<Guid, List<string>>> GetRoleNamesByEmployeeAsync()
    {
        var names = (await _roles.GetListAsync()).ToDictionary(r => r.Id, r => r.Name);
        return (await _links.GetListAsync())
            .Where(l => names.ContainsKey(l.JobRoleId))
            .GroupBy(l => l.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(l => names[l.JobRoleId]).OrderBy(n => n).ToList());
    }

    /// <summary>Active employees holding the role; nobody when the role isn't in the catalogue.</summary>
    public async Task<List<Employee>> GetActiveHoldersAsync(string roleName)
    {
        var role = await FindAsync(roleName);
        if (role == null) return new();
        var employeeIds = (await _links.GetListAsync(l => l.JobRoleId == role.Id)).Select(l => l.EmployeeId).ToList();
        return await _employees.GetListAsync(e => employeeIds.Contains(e.Id) && e.IsActive);
    }

    public async Task<bool> HoldsAsync(Guid employeeId, string roleName)
    {
        var role = await FindAsync(roleName);
        return role != null && await _links.AnyAsync(l => l.EmployeeId == employeeId && l.JobRoleId == role.Id);
    }
}
