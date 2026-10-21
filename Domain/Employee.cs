using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Workmate.Poc.Domain;

/// <summary>Names of the built-in job roles seeded into every company's catalogue (see <see cref="JobRole"/>).</summary>
public static class EmployeeRoles
{
    public const string Employee = "Employee";
    public const string Manager = "Manager";
    public const string Hr = "HR";
    public const string Finance = "Finance";
}

/// <summary>Minimal HR record, linked to the real Identity user who signs in as this employee (spec 003 FR-004).</summary>
public class Employee : AggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = default!;
    public string Email { get; set; } = default!;     // mirrors IdentityUser.Email; unique per tenant
    public string? JobTitle { get; set; }             // display only
    public Guid? ManagerId { get; set; }
    public Guid? DepartmentId { get; set; }           // an OrganizationUnit.Id; at most one (spec 004 research R3)
    public bool IsActive { get; set; } = true;        // mirrors IdentityUser.IsActive (spec 004 FR-007)
    // Job roles (used by "Role" approval steps) are EmployeeJobRole links, not a field here (spec 004 FR-006).

    protected Employee() { }
    public Employee(Guid id, Guid? tenantId, Guid userId, string name, string email, Guid? managerId) : base(id)
    {
        TenantId = tenantId; UserId = userId; Name = name; Email = email; ManagerId = managerId;
    }
}
