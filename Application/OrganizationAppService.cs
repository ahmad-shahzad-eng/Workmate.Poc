using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Workmate.Poc.Domain;

namespace Workmate.Poc.Application;

/// <summary>
/// The company's departments (spec 004 US2): ABP Identity OrganizationUnits for the tree, with the
/// head kept on the HR side (FR-001). Reads are open to any signed-in user (pickers).
/// </summary>
public class OrganizationAppService : ApplicationService
{
    private readonly IRepository<OrganizationUnit, Guid> _units;
    private readonly OrganizationUnitManager _unitManager;
    private readonly IRepository<DepartmentHead, Guid> _heads;
    private readonly IRepository<Employee, Guid> _employees;

    public OrganizationAppService(IRepository<OrganizationUnit, Guid> units, OrganizationUnitManager unitManager,
        IRepository<DepartmentHead, Guid> heads, IRepository<Employee, Guid> employees)
    {
        _units = units; _unitManager = unitManager; _heads = heads; _employees = employees;
    }

    public virtual async Task<List<DepartmentDto>> GetDepartmentsAsync()
    {
        var units = await _units.GetListAsync();
        var heads = (await _heads.GetListAsync()).ToDictionary(h => h.OrganizationUnitId, h => h.EmployeeId);
        var employees = await _employees.GetListAsync();
        var names = employees.ToDictionary(e => e.Id, e => e.Name);
        return units.OrderBy(u => u.Code).Select(u =>
        {
            var headId = heads.TryGetValue(u.Id, out var h) ? h : (Guid?)null;
            return new DepartmentDto(u.Id, u.DisplayName, u.ParentId, headId, headId is { } id ? names.GetValueOrDefault(id) : null,
                employees.Count(e => e.DepartmentId == u.Id && e.IsActive));
        }).ToList();
    }

    [Authorize(WorkmatePermissions.OrgManage)]
    public virtual async Task<DepartmentDto> CreateDepartmentAsync(CreateDepartmentInput input)
    {
        var name = Clean(input.Name) ?? throw new UserFriendlyException("Department name is required.");
        if (input.ParentId is { } parentId && await _units.FindAsync(parentId) == null) // tenant-filtered
            throw new UserFriendlyException("The parent department does not belong to this company.");

        var unit = new OrganizationUnit(GuidGenerator.Create(), name, input.ParentId, CurrentTenant.Id);
        await _unitManager.CreateAsync(unit); // generates the hierarchical Code
        return await GetDtoAsync(unit.Id);
    }

    /// <summary>Renames the department and sets or clears its head (any employee of this company).</summary>
    [Authorize(WorkmatePermissions.OrgManage)]
    public virtual async Task<DepartmentDto> UpdateDepartmentAsync(Guid id, UpdateDepartmentInput input)
    {
        var unit = await _units.GetAsync(id);
        unit.DisplayName = Clean(input.Name) ?? throw new UserFriendlyException("Department name is required.");
        await _unitManager.UpdateAsync(unit);

        var head = await _heads.FindAsync(h => h.OrganizationUnitId == id);
        if (input.HeadEmployeeId is { } headId)
        {
            if (await _employees.FindAsync(headId) == null)
                throw new UserFriendlyException("The chosen head does not belong to this company.");
            if (head == null) await _heads.InsertAsync(new DepartmentHead(GuidGenerator.Create(), CurrentTenant.Id, id, headId));
            else { head.EmployeeId = headId; await _heads.UpdateAsync(head); }
        }
        else if (head != null)
        {
            await _heads.DeleteAsync(head);
        }

        return await GetDtoAsync(id);
    }

    /// <summary>US2 AC2: refused while it has employees or sub-departments. Checked first because
    /// OrganizationUnitManager.DeleteAsync deletes the whole subtree.</summary>
    [Authorize(WorkmatePermissions.OrgManage)]
    public virtual async Task DeleteDepartmentAsync(Guid id)
    {
        await _units.GetAsync(id);
        if (await _employees.AnyAsync(e => e.DepartmentId == id) || await _units.AnyAsync(u => u.ParentId == id))
            throw new UserFriendlyException("Move its employees and sub-departments first.");

        await _heads.DeleteAsync(h => h.OrganizationUnitId == id);
        await _unitManager.DeleteAsync(id);
    }

    private async Task<DepartmentDto> GetDtoAsync(Guid id)
    {
        await CurrentUnitOfWork!.SaveChangesAsync(); // GetDepartmentsAsync queries the database
        return (await GetDepartmentsAsync()).First(d => d.Id == id);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
