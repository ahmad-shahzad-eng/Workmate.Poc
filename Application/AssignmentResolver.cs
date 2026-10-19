using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application;

/// <summary>
/// Resolves who may act on a task from HR data. Workflow definitions only name a rule key,
/// so org changes never require editing workflows.
/// PoC rules: "line-manager" and "role:{Role}". Production: AssignmentRule entities + org units.
/// </summary>
public class AssignmentResolver : ITransientDependency
{
    private readonly IRepository<Employee, Guid> _employees;

    public AssignmentResolver(IRepository<Employee, Guid> employees) => _employees = employees;

    public async Task<List<Employee>> ResolveAsync(string rule, Guid submitterUserId)
    {
        if (rule == "line-manager")
        {
            var submitter = await _employees.FindAsync(submitterUserId);
            if (submitter?.ManagerId == null) return new();
            var manager = await _employees.FindAsync(submitter.ManagerId.Value);
            return manager == null ? new() : new() { manager };
        }

        if (rule.StartsWith("role:", StringComparison.OrdinalIgnoreCase))
        {
            var role = rule["role:".Length..];
            return await _employees.GetListAsync(e => e.Role == role);
        }

        return new();
    }
}
