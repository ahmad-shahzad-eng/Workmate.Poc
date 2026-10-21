using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;

namespace Workmate.Poc.Application;

/// <summary>
/// Reusable "make this tenant's admin role real" step, shared by DemoDataSeeder (the two seeded demo
/// companies) and TenantOnboardingAppService (every company the vendor admin creates afterward) —
/// spec 003 US3: a company admin is membership in this role, not an Employee.IsAdmin flag.
/// </summary>
public class TenantAdminRoleSeeder : ITransientDependency
{
    public const string RoleName = "Workmate.CompanyAdmin";

    private readonly ICurrentTenant _currentTenant;
    private readonly IdentityRoleManager _roleManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IPermissionManager _permissionManager;

    public TenantAdminRoleSeeder(ICurrentTenant currentTenant, IdentityRoleManager roleManager,
        IGuidGenerator guidGenerator, IPermissionManager permissionManager)
    {
        _currentTenant = currentTenant; _roleManager = roleManager;
        _guidGenerator = guidGenerator; _permissionManager = permissionManager;
    }

    private static readonly string[] TenantSidePermissions =
    {
        WorkmatePermissions.FormsDesign, WorkmatePermissions.RoutingManage,
        WorkmatePermissions.WorkflowsDesign, WorkmatePermissions.EmployeesManage, WorkmatePermissions.OrgManage
    };

    /// <summary>
    /// Must be called inside a <c>_currentTenant.Change(tenantId)</c> block — the role (and any grant
    /// to <paramref name="alsoGrantToUserId"/>) is tenant-scoped: <c>PermissionGrant</c> implements
    /// <see cref="Volo.Abp.MultiTenancy.IMultiTenant"/> like everything else, so a grant written once
    /// in host context is invisible once <see cref="ICurrentTenant"/> switches to a specific tenant —
    /// confirmed by actually calling the vendor-admin-acts-as-a-tenant path and getting a 403 (plan.md
    /// assumed a single host-side grant would be enough; it isn't). The vendor admin therefore needs
    /// this called once per tenant too, via <paramref name="alsoGrantToUserId"/> (spec 003 US1 — "vendor
    /// admin... can design for any company").
    /// </summary>
    public async Task<IdentityRole> EnsureCompanyAdminRoleAsync(Guid? alsoGrantToUserId = null)
    {
        var role = await _roleManager.FindByNameAsync(RoleName);
        if (role == null)
        {
            role = new IdentityRole(_guidGenerator.Create(), RoleName, _currentTenant.Id);
            var result = await _roleManager.CreateAsync(role);
            if (!result.Succeeded)
                throw new UserFriendlyException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        foreach (var permission in TenantSidePermissions)
            await _permissionManager.SetAsync(permission, RolePermissionValueProvider.ProviderName, role.Name, true);

        if (alsoGrantToUserId != null)
            foreach (var permission in TenantSidePermissions)
                await _permissionManager.SetAsync(permission, UserPermissionValueProvider.ProviderName, alsoGrantToUserId.Value.ToString(), true);

        return role;
    }
}
