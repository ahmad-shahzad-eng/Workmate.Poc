using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Workmate.Poc.Domain;

public static class EmployeeRoles
{
    public const string Employee = "Employee";
    public const string Manager = "Manager";
    public const string Hr = "HR";
    public const string Finance = "Finance";
}

/// <summary>Minimal HR record. In the PoC an employee's Id doubles as their user id.</summary>
public class Employee : AggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public string Name { get; set; } = default!;
    public string Role { get; set; } = default!;      // job role used by assignment rules: Employee | Manager | HR
    public Guid? ManagerId { get; set; }

    /// <summary>
    /// Company admin: may design forms and routing for this company. A permission, not a job role, so the
    /// person still has a manager and takes part in workflows like anyone else.
    /// PoC stand-in for an ABP permission (e.g. Workmate.Forms.Design) granted to the user's role.
    /// </summary>
    public bool IsAdmin { get; set; }

    protected Employee() { }
    public Employee(Guid id, Guid? tenantId, string name, string role, Guid? managerId) : base(id)
    {
        TenantId = tenantId; Name = name; Role = role; ManagerId = managerId;
    }
}
