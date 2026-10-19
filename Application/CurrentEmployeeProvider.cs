using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application;

/// <summary>
/// Maps the signed-in user to an employee of the current tenant. Because the Employee query is
/// tenant-filtered, a user of tenant A acting under tenant B's header finds no employee and is refused.
/// </summary>
public class CurrentEmployeeProvider : ITransientDependency
{
    private readonly ICurrentUser _currentUser;
    private readonly IRepository<Employee, Guid> _employees;

    public CurrentEmployeeProvider(ICurrentUser currentUser, IRepository<Employee, Guid> employees)
    {
        _currentUser = currentUser; _employees = employees;
    }

    public async Task<Employee> GetAsync()
    {
        if (_currentUser.Id == null)
            throw new UserFriendlyException("Choose who you are acting as before continuing.");
        return await _employees.FindAsync(_currentUser.Id.Value)
               ?? throw new UserFriendlyException("This user does not belong to the selected company.");
    }

    /// <summary>
    /// PoC stand-in for an ABP permission check: form and routing design is for the vendor admin
    /// or a company employee flagged as admin.
    /// </summary>
    public async Task EnsureAdminAsync()
    {
        if (_currentUser.Id == DemoTenants.VendorAdminId) return;
        var employee = await GetAsync();
        if (!employee.IsAdmin)
            throw new UserFriendlyException("Only Admin users can design forms and routing.");
    }
}
