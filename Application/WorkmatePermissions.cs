namespace Workmate.Poc.Application;

public static class WorkmatePermissions
{
    public const string GroupName = "Workmate";

    public const string FormsDesign = GroupName + ".Forms.Design";
    public const string RoutingManage = GroupName + ".Routing.Manage";
    public const string WorkflowsDesign = GroupName + ".Workflows.Design";
    public const string EmployeesManage = GroupName + ".Employees.Manage";
    public const string OrgManage = GroupName + ".Org.Manage";
    public const string BrandingManage = GroupName + ".Branding.Manage";

    /// <summary>Host-only: creating/managing companies (spec 003 US1).</summary>
    public const string TenantsManage = GroupName + ".Tenants.Manage";
}
