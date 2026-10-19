using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application;

/// <summary>
/// Resolves who may act on a task from HR data. Workflow definitions only name a rule key,
/// so org changes never require editing workflows.
/// Rules: "line-manager", "manager-of-manager", "role:{Role}", "user:{EmployeeId}".
/// The submitter is always excluded from their own request's candidates, for every rule.
/// When the result is empty and a fallback rule is given, the fallback is resolved instead.
/// </summary>
public class AssignmentResolver : ITransientDependency
{
    private readonly IRepository<Employee, Guid> _employees;

    public AssignmentResolver(IRepository<Employee, Guid> employees) => _employees = employees;

    public async Task<(List<Employee> People, bool UsedFallback)> ResolveAsync(string rule, Guid submitterUserId, string? fallbackRule = null)
    {
        var primary = await ResolveExcludingSubmitterAsync(rule, submitterUserId);
        if (primary.Count > 0) return (primary, false);
        if (string.IsNullOrWhiteSpace(fallbackRule)) return (primary, false);

        var fallback = await ResolveExcludingSubmitterAsync(fallbackRule, submitterUserId);
        return (fallback, true);
    }

    private async Task<List<Employee>> ResolveExcludingSubmitterAsync(string rule, Guid submitterUserId) =>
        (await ResolveRuleAsync(rule, submitterUserId)).Where(p => p.Id != submitterUserId).ToList();

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
            var role = rule["role:".Length..];
            return await _employees.GetListAsync(e => e.Role == role);
        }

        if (rule.StartsWith("user:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(rule["user:".Length..], out var userId))
        {
            var user = await _employees.FindAsync(userId); // tenant-filtered repository: a cross-tenant id resolves to nobody
            return user == null ? new() : new() { user };
        }

        return new();
    }
}
