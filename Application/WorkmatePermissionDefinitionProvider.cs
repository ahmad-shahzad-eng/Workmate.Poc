using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;

namespace Workmate.Poc.Application;

/// <summary>Replaces the PoC's Employee.IsAdmin flag / hard-coded vendor-admin id (spec 003 US3).</summary>
public class WorkmatePermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(WorkmatePermissions.GroupName);

        // Both, not Tenant: ABP's permission checker gates MultiTenancySide on the signed-in principal's
        // OWN tenant claim (ClaimsPrincipal.GetMultiTenancySide(), confirmed from source) — never on the
        // ambient ICurrentTenant.Id our HostActAsTenantResolveContributor changes. A host-authenticated
        // vendor-admin token has no tenant claim at all, so a Tenant-only permission is categorically
        // unreachable for them regardless of any grant — confirmed by actually hitting a 403 with a grant
        // already in place (plan.md assumed Tenant-side was enough for "design for any company", US1).
        group.AddPermission(WorkmatePermissions.FormsDesign, multiTenancySide: MultiTenancySides.Both);
        group.AddPermission(WorkmatePermissions.RoutingManage, multiTenancySide: MultiTenancySides.Both);
        group.AddPermission(WorkmatePermissions.WorkflowsDesign, multiTenancySide: MultiTenancySides.Both);
        group.AddPermission(WorkmatePermissions.EmployeesManage, multiTenancySide: MultiTenancySides.Tenant);
        group.AddPermission(WorkmatePermissions.OrgManage, multiTenancySide: MultiTenancySides.Tenant);
        group.AddPermission(WorkmatePermissions.TenantsManage, multiTenancySide: MultiTenancySides.Host);
    }
}
