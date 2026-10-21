using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Workmate.Poc.Domain;

/// <summary>
/// A job role in the company's catalogue (spec 004 US3), used by "Role" approval steps as
/// <c>role:{Name}</c>. HR-side data, deliberately separate from ABP's permission roles
/// (Workmate.CompanyAdmin) — research R1. The name is the rule key, so it never changes (R2).
/// </summary>
public class JobRole : AggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public string Name { get; private set; } = default!;  // unique per tenant (case-insensitive), immutable
    public bool IsBuiltIn { get; set; }                    // informational only; deletion rules are the same

    protected JobRole() { }
    public JobRole(Guid id, Guid? tenantId, string name, bool isBuiltIn) : base(id)
    {
        TenantId = tenantId; Name = name; IsBuiltIn = isBuiltIn;
    }
}

/// <summary>An employee holds zero or more job roles (spec 004 FR-006).</summary>
public class EmployeeJobRole : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid JobRoleId { get; set; }

    protected EmployeeJobRole() { }
    public EmployeeJobRole(Guid id, Guid? tenantId, Guid employeeId, Guid jobRoleId) : base(id)
    {
        TenantId = tenantId; EmployeeId = employeeId; JobRoleId = jobRoleId;
    }
}

/// <summary>The head of one department (an ABP OrganizationUnit), kept on the HR side (spec 004 FR-001).</summary>
public class DepartmentHead : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid OrganizationUnitId { get; set; }  // unique: one head per unit
    public Guid EmployeeId { get; set; }

    protected DepartmentHead() { }
    public DepartmentHead(Guid id, Guid? tenantId, Guid organizationUnitId, Guid employeeId) : base(id)
    {
        TenantId = tenantId; OrganizationUnitId = organizationUnitId; EmployeeId = employeeId;
    }
}

/// <summary>Seeded into every company's role catalogue.</summary>
public static class BuiltInJobRoles
{
    public static readonly string[] All = { EmployeeRoles.Employee, EmployeeRoles.Manager, EmployeeRoles.Hr, EmployeeRoles.Finance };
}
