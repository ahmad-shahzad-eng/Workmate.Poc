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

/// <summary>Minimal HR record, linked to the real Identity user who signs in as this employee (spec 003 FR-004).</summary>
public class Employee : AggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = default!;
    public string Role { get; set; } = default!;      // job role used by assignment rules: Employee | Manager | HR
    public Guid? ManagerId { get; set; }

    protected Employee() { }
    public Employee(Guid id, Guid? tenantId, Guid userId, string name, string role, Guid? managerId) : base(id)
    {
        TenantId = tenantId; UserId = userId; Name = name; Role = role; ManagerId = managerId;
    }
}
