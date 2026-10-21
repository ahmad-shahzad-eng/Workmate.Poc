# API contract: Employees and organisation structure

All routes are under `api/poc`, need a bearer token (spec 003 T027) and are tenant-scoped by the caller's token. Errors use ABP's `UserFriendlyException` shape (HTTP 403 with `error.message`), the same as existing routes. Permission failures return 403, and no token returns 401.

## Employees: `Workmate.Employees.Manage` unless noted

| Method & route | Body | Returns | Notes |
|---|---|---|---|
| `GET users` | — | `EmployeeDto[]` | **changed shape**, any signed-in user (pickers). Active employees only unless `?includeInactive=true` (requires the permission) |
| `POST employees` | `SaveEmployeeInput` | `EmployeeCreatedDto` | creates `IdentityUser` + `Employee` + role links + OU membership in one unit of work (FR-002) |
| `PUT employees/{id}` | `SaveEmployeeInput` | `EmployeeDto` | email change updates the `IdentityUser` too |
| `POST employees/{id}/deactivate` | — | `DeactivationResultDto` | FR-007; idempotent |
| `POST employees/{id}/activate` | — | `EmployeeDto` | |

```text
EmployeeDto            { id, name, email, jobTitle, roles: string[], managerId?, managerName?, departmentId?, departmentName?, isActive }
SaveEmployeeInput      { name, email, jobTitle?, roles: string[], managerId?, departmentId? }
EmployeeCreatedDto     { employee: EmployeeDto, generatedPassword }
DeactivationResultDto  { employee: EmployeeDto, reassignedTaskCount }
```

Refusals (`UserFriendlyException`): duplicate email in the company (US1 AC3), manager cycle or self (US1 AC2), unknown role name, manager/department not in this company.

## Role catalogue: `Workmate.Employees.Manage` for writes

| Method & route | Body | Returns | Notes |
|---|---|---|---|
| `GET roles` | — | `JobRoleDto[]` | any signed-in user (route designer picker) |
| `POST roles` | `{ name }` | `JobRoleDto` | name unique per company, case-insensitive |
| `DELETE roles/{id}` | — | 204 | refused while in use, naming the workflows (US3 AC1) |

```text
JobRoleDto { id, name, isBuiltIn, employeeCount }
```

## Departments: `Workmate.Org.Manage` for writes

| Method & route | Body | Returns | Notes |
|---|---|---|---|
| `GET departments` | — | `DepartmentDto[]` (flat, with `parentId`) | any signed-in user |
| `POST departments` | `{ name, parentId? }` | `DepartmentDto` | |
| `PUT departments/{id}` | `{ name, headEmployeeId? }` | `DepartmentDto` | rename; set or clear the head |
| `DELETE departments/{id}` | — | 204 | refused while it has employees or sub-units (US2 AC2) |

```text
DepartmentDto { id, name, parentId?, headEmployeeId?, headName?, employeeCount }
```

## Changed: approval workflow designer

- Step `approverType` accepts `"department-head"` (no `role`/`userId`).
- A `role` step or `role:` fallback must name a role in the catalogue. The existing "role has no employees" check now counts **active** holders.
