using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application;

/// <summary>
/// Resolves who may act on a task from HR data. Workflow definitions only name a rule key,
/// so org changes never require editing workflows.
/// Rules: "line-manager", "manager-of-manager", "role:{Role}" (every active holder of that catalogue
/// role, spec 004 FR-004), "user:{EmployeeId}", "department-head" (spec 004 US2).
/// The submitter is always excluded from their own request's candidates, for every rule, and so is
/// anyone deactivated (spec 004 FR-007).
/// When the result is empty and a fallback rule is given, the fallback is resolved instead.
/// </summary>
public class AssignmentResolver : ITransientDependency
{
    private readonly IRepository<Employee, Guid> _employees;
    private readonly JobRoleLookup _jobRoles;
    private readonly IRepository<DepartmentHead, Guid> _heads;
    private readonly IRepository<OrganizationUnit, Guid> _units;

    public AssignmentResolver(IRepository<Employee, Guid> employees, JobRoleLookup jobRoles,
        IRepository<DepartmentHead, Guid> heads, IRepository<OrganizationUnit, Guid> units)
    {
        _employees = employees; _jobRoles = jobRoles; _heads = heads; _units = units;
    }

    public async Task<(List<Employee> People, bool UsedFallback)> ResolveAsync(string rule, Guid submitterUserId, string? fallbackRule = null)
    {
        var primary = await ResolveExcludingSubmitterAsync(rule, submitterUserId);
        if (primary.Count > 0) return (primary, false);
        if (string.IsNullOrWhiteSpace(fallbackRule)) return (primary, false);

        var fallback = await ResolveExcludingSubmitterAsync(fallbackRule, submitterUserId);
        return (fallback, true);
    }

    private async Task<List<Employee>> ResolveExcludingSubmitterAsync(string rule, Guid submitterUserId) =>
        (await ResolveRuleAsync(rule, submitterUserId)).Where(p => p.Id != submitterUserId && p.IsActive).ToList();

    private async Task<List<Employee>> ResolveRuleAsync(string rule, Guid submitterUserId)
    {
        if (rule == "line-manager")
        {
            var submitter = await _employees.FindAsync(submitterUserId);
            if (submitter?.ManagerId == null) return new();
            var manager = await _employees.FindAsync(submitter.ManagerId.Value);
            return manager == null ? new() : new() { manager };
        }

        if (rule == "manager-of-manager")
        {
            var submitter = await _employees.FindAsync(submitterUserId);
            if (submitter?.ManagerId == null) return new();
            var manager = await _employees.FindAsync(submitter.ManagerId.Value);
            if (manager?.ManagerId == null) return new();
            var managersManager = await _employees.FindAsync(manager.ManagerId.Value);
            return managersManager == null ? new() : new() { managersManager };
        }

        if (rule.StartsWith("role:", StringComparison.OrdinalIgnoreCase))
        {
            return await _jobRoles.GetActiveHoldersAsync(rule["role:".Length..]);
        }

        if (rule == ApproverTypes.DepartmentHead)
            return await ResolveDepartmentHeadAsync(submitterUserId);

        if (rule.StartsWith("user:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(rule["user:".Length..], out var userId))
        {
            var user = await _employees.FindAsync(userId); // tenant-filtered repository: a cross-tenant id resolves to nobody
            return user == null ? new() : new() { user };
        }

        return new();
    }

    /// <summary>
    /// The head of the submitter's department; walks up to the parent while the department has no
    /// head, the head is inactive, or the head is the submitter (clarification Q3). Nobody at the top
    /// means the step's fallback applies (spec 002 FR-010).
    /// </summary>
    private async Task<List<Employee>> ResolveDepartmentHeadAsync(Guid submitterUserId)
    {
        var submitter = await _employees.FindAsync(submitterUserId);
        var visited = new HashSet<Guid>();
        for (var unitId = submitter?.DepartmentId; unitId != null && visited.Add(unitId.Value);
             unitId = (await _units.FindAsync(unitId.Value))?.ParentId)
        {
            var head = await _heads.FindAsync(h => h.OrganizationUnitId == unitId);
            if (head == null || head.EmployeeId == submitterUserId) continue;
            var person = await _employees.FindAsync(head.EmployeeId);
            if (person is { IsActive: true }) return new() { person };
        }
        return new();
    }
}
