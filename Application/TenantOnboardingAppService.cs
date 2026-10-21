using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application;

/// <summary>
/// Vendor onboards a new client (spec 003 US1): create the company, its first admin user, and reset
/// that admin's password later if they're locked out — host-only (the vendor admin, no tenant of
/// their own).
/// </summary>
[Authorize(WorkmatePermissions.TenantsManage)]
public class TenantOnboardingAppService : ApplicationService
{
    private readonly ICurrentTenant _currentTenant;
    private readonly ITenantRepository _tenants;
    private readonly TenantManager _tenantManager;
    private readonly IdentityUserManager _userManager;
    private readonly IRepository<Employee, Guid> _employees;
    private readonly TenantAdminRoleSeeder _adminRoleSeeder;
    private readonly IDataFilter _dataFilter;
    private readonly IRepository<JobRole, Guid> _jobRoles;
    private readonly IRepository<EmployeeJobRole, Guid> _jobRoleLinks;

    public TenantOnboardingAppService(ICurrentTenant currentTenant, ITenantRepository tenants, TenantManager tenantManager,
        IdentityUserManager userManager, IRepository<Employee, Guid> employees, TenantAdminRoleSeeder adminRoleSeeder,
        IDataFilter dataFilter, IRepository<JobRole, Guid> jobRoles, IRepository<EmployeeJobRole, Guid> jobRoleLinks)
    {
        _currentTenant = currentTenant; _tenants = tenants; _tenantManager = tenantManager;
        _userManager = userManager; _employees = employees; _adminRoleSeeder = adminRoleSeeder; _dataFilter = dataFilter;
        _jobRoles = jobRoles; _jobRoleLinks = jobRoleLinks;
    }

    /// <summary>Creates the tenant, its first admin user (in the Workmate.CompanyAdmin role) and matching Employee row.</summary>
    public virtual async Task<CompanyCreatedDto> CreateCompanyAsync(CreateCompanyInput input)
    {
        var name = input.Name?.Trim();
        var email = input.AdminEmail?.Trim();
        var adminName = input.AdminName?.Trim();
        if (string.IsNullOrEmpty(name)) throw new UserFriendlyException("Company name is required.");
        if (string.IsNullOrEmpty(email)) throw new UserFriendlyException("Admin email is required.");
        if (string.IsNullOrEmpty(adminName)) throw new UserFriendlyException("Admin name is required.");

        var tenant = await _tenantManager.CreateAsync(name);
        await _tenants.InsertAsync(tenant);

        var password = PasswordGenerator.New();
        Guid employeeId;

        using (_currentTenant.Change(tenant.Id))
        {
            var user = new IdentityUser(GuidGenerator.Create(), email, email, tenant.Id);
            var createResult = await _userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
                throw new UserFriendlyException(string.Join(" ", createResult.Errors.Select(e => e.Description)));

            // Also grants the vendor admin (the caller, enforced by the class-level [Authorize]) the same
            // tenant-side permissions in this brand-new tenant — "design for any company" (spec 003 US1).
            var role = await _adminRoleSeeder.EnsureCompanyAdminRoleAsync(CurrentUser.Id);
            var roleResult = await _userManager.AddToRoleAsync(user, role.Name);
            if (!roleResult.Succeeded)
                throw new UserFriendlyException(string.Join(" ", roleResult.Errors.Select(e => e.Description)));

            // First person in a new company: no line manager of their own yet (US1 AC1 — "sees an empty company").
            var employee = new Employee(GuidGenerator.Create(), tenant.Id, user.Id, adminName, email, null);
            await _employees.InsertAsync(employee);

            // Spec 004: seed the company's built-in job roles; the first admin is a Manager.
            foreach (var roleName in BuiltInJobRoles.All)
            {
                var jobRole = await _jobRoles.InsertAsync(new JobRole(GuidGenerator.Create(), tenant.Id, roleName, isBuiltIn: true));
                if (roleName == EmployeeRoles.Manager)
                    await _jobRoleLinks.InsertAsync(new EmployeeJobRole(GuidGenerator.Create(), tenant.Id, employee.Id, jobRole.Id));
            }
            employeeId = employee.Id;
        }

        return new CompanyCreatedDto(tenant.Id, employeeId, email, password);
    }

    /// <summary>Edge case: the client admin is locked out / forgot their password (spec 005 will add email; shown on screen until then).
    /// Only a company admin's password can be reset this way, and the reset also lifts any lockout.</summary>
    public virtual async Task<ResetAdminPasswordDto> ResetClientAdminPasswordAsync(ResetAdminPasswordInput input)
    {
        var employee = await FindEmployeeAnyTenantAsync(input.EmployeeId);

        var password = PasswordGenerator.New();
        using (_currentTenant.Change(employee.TenantId))
        {
            var user = await _userManager.GetByIdAsync(employee.UserId);
            if (!await _userManager.IsInRoleAsync(user, TenantAdminRoleSeeder.RoleName))
                throw new UserFriendlyException("Only a company admin's password can be reset here.");

            await _userManager.RemovePasswordAsync(user);
            var result = await _userManager.AddPasswordAsync(user, password);
            if (!result.Succeeded)
                throw new UserFriendlyException(string.Join(" ", result.Errors.Select(e => e.Description)));
            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.ResetAccessFailedCountAsync(user);
        }

        return new ResetAdminPasswordDto(password);
    }

    /// <summary>Edge case: the client admin leaves — make another employee of the same company admin,
    /// optionally removing the departed admin. The change applies from each user's next sign-in, since
    /// role membership travels in the access token.</summary>
    public virtual async Task AssignClientAdminAsync(AssignClientAdminInput input)
    {
        var employee = await FindEmployeeAnyTenantAsync(input.EmployeeId);
        var removed = input.RemoveEmployeeId == null ? null : await FindEmployeeAnyTenantAsync(input.RemoveEmployeeId.Value);
        if (removed != null && removed.TenantId != employee.TenantId)
            throw new UserFriendlyException("Both employees must belong to the same company.");
        if (removed?.Id == employee.Id)
            throw new UserFriendlyException("Cannot assign and remove the same employee.");

        using (_currentTenant.Change(employee.TenantId))
        {
            var role = await _adminRoleSeeder.EnsureCompanyAdminRoleAsync();
            var user = await _userManager.GetByIdAsync(employee.UserId);
            if (!await _userManager.IsInRoleAsync(user, role.Name))
                ThrowIfFailed(await _userManager.AddToRoleAsync(user, role.Name));

            if (removed != null)
            {
                var removedUser = await _userManager.GetByIdAsync(removed.UserId);
                if (await _userManager.IsInRoleAsync(removedUser, role.Name))
                    ThrowIfFailed(await _userManager.RemoveFromRoleAsync(removedUser, role.Name));
            }
        }
    }

    private async Task<Employee> FindEmployeeAnyTenantAsync(Guid employeeId)
    {
        using (_dataFilter.Disable<IMultiTenant>()) // host context can't otherwise see a specific tenant's employee by id
        {
            return await _employees.GetAsync(employeeId);
        }
    }

    private static void ThrowIfFailed(Microsoft.AspNetCore.Identity.IdentityResult result)
    {
        if (!result.Succeeded)
            throw new UserFriendlyException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
