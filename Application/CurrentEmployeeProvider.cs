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
            throw new UserFriendlyException("Sign in before continuing.");
        return await _employees.FindAsync(e => e.UserId == _currentUser.Id.Value)
               ?? throw new UserFriendlyException("This user does not belong to the selected company.");
    }
}
