# Data model: Employees and organisation structure

All new entities are `IMultiTenant` (tenant-filtered, Principle IV). Decisions: [research.md](./research.md).

> Naming note: `ProcessTaskCandidate.UserId`, `FormSubmission.SubmittedByUserId` and `AssignmentResolver`'s `submitterUserId` hold an **`Employee.Id`** (a leftover name from before spec 003). Nothing in this feature renames them.

## Changed: `Employee` (`Domain/Employee.cs`)

| Field | Type | Change | Rules |
|---|---|---|---|
| `Role` | string | **removed** | replaced by `EmployeeJobRole` (FR-006) |
| `Email` | string | **new** | required; mirrors `IdentityUser.Email`; unique per tenant (ABP `RequireUniqueEmail`, tenant-filtered) — same email in another tenant is allowed (US1 AC3) |
| `JobTitle` | string? | **new** | free text, display only |
| `DepartmentId` | Guid? | **new** | an `OrganizationUnit.Id` in the same tenant; at most one |
| `IsActive` | bool | **new**, default `true` | mirrors `IdentityUser.IsActive` (R4) |
| `ManagerId` | Guid? | unchanged | must be an employee of the same tenant; may not create a cycle (US1 AC2); may not be self |

**State**: Active ⇄ Inactive. Active → Inactive runs FR-007 (block sign-in, move open tasks). Inactive → Active only re-enables sign-in and resolution; tasks are not moved back. No hard delete (US1).

## New: `JobRole` (`Domain/Organization.cs`)

| Field | Type | Rules |
|---|---|---|
| `Id` | Guid | |
| `TenantId` | Guid? | |
| `Name` | string | required, unique per tenant (case-insensitive), immutable (R2); it's the `role:{Name}` rule key |
| `IsBuiltIn` | bool | true for `Employee`, `Manager`, `HR`, `Finance` (seeded per tenant); informational only |

Delete: refused while in use (R6), naming the workflows; otherwise removes all its `EmployeeJobRole` links (US3).

## New: `EmployeeJobRole` (`Domain/Organization.cs`)

| Field | Type | Rules |
|---|---|---|
| `Id` | Guid | |
| `TenantId` | Guid? | |
| `EmployeeId` | Guid | |
| `JobRoleId` | Guid | unique pair (`EmployeeId`, `JobRoleId`) |

## New: `DepartmentHead` (`Domain/Organization.cs`)

| Field | Type | Rules |
|---|---|---|
| `Id` | Guid | |
| `TenantId` | Guid? | |
| `OrganizationUnitId` | Guid | unique: one head per unit |
| `EmployeeId` | Guid | any employee of the tenant (needn't be a member of the unit) |

## Reused: ABP `OrganizationUnit` (already mapped since spec 003)

Tree via `ParentId`/`Code` (ABP-managed). Delete refused while any employee has `DepartmentId` = it or it has child units (US2 AC2). Deleting it also deletes its `DepartmentHead` row.

## Changed: `ProcessTask` (`Domain/Processes.cs`)

| Field | Type | Change |
|---|---|---|
| `FallbackRule` | string? | **new**: copied at creation from `HumanTask.FallbackRule`; `null` (older tasks) means `role:HR` (R4) |

## Changed: `ApproverTypes` (`Domain/ApprovalWorkflows.cs`)

Adds `DepartmentHead = "department-head"` to `All`. `ToAssignmentRule()` passes it through unchanged. `FallbackRules.Parse` still accepts role/user only.

## Timeline events (`ProcessEvent.EventType`)

| Type | When | Detail example |
|---|---|---|
| `TaskReassigned` | FR-007 deactivation | "Manager approval: Omar Khalid was deactivated; reassigned to Huda Ali via the fallback." |
