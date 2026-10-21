# Tasks: Employees and organisation structure (client admin)

**Input**: [spec.md](./spec.md) · [plan.md](./plan.md) · [research.md](./research.md) · [data-model.md](./data-model.md) · [contracts/api.md](./contracts/api.md) · [quickstart.md](./quickstart.md)

**Tests**: The constitution requires `demo.py` checks for every acceptance scenario, so each story phase ends with its `demo.py` step-12 checks. There are no unit-test projects (none exist in this repo).

**Gate after each phase**: stop the old app → `dotnet build` → `dotnet run` → `python3 demo.py` (see each phase's gate). One CHANGELOG line per task (constitution).

**Reset**: Phase 2 removes the `Employees.Role` column and adds tables, so set `Poc:ResetDatabaseOnStartup=true` for the first run after Phase 2, then back to `false` (`EnsureCreatedAsync` doesn't migrate).

**Naming reminder** (data-model.md): `ProcessTaskCandidate.UserId`, `FormSubmission.SubmittedByUserId` and `AssignmentResolver`'s `submitterUserId` all hold an **`Employee.Id`**. Don't rename them.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 employees, US2 organisation structure, US3 role catalogue (spec.md). US4 (CSV import) is out of scope per the spec and has no tasks.

---

## Phase 1: Setup

**Purpose**: Shared helpers. No new packages (plan Technical Context).

- [X] T001 [P] Create `Application/PasswordGenerator.cs`: `public static class PasswordGenerator { public static string New() }`, moving the body of `TenantOnboardingAppService.GeneratePassword()` unchanged (10 chars from `ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789` via `RandomNumberGenerator`, plus `"1!"`). Make `TenantOnboardingAppService` call it and delete its private copy (research R7).

---

## Phase 2: Foundational (blocks every story)

**Purpose**: Replace `Employee.Role` with the role catalogue, add the new HR fields and task fallback, and keep every existing feature working. Nothing compiles in between T003 and T011, so treat T002–T011 as one build unit.

- [X] T002 [P] Create `Domain/Organization.cs` (namespace `Workmate.Poc.Domain`) with:
  - `JobRole : AggregateRoot<Guid>, IMultiTenant` { `TenantId`, `Name` (required; "unique per tenant (case-insensitive), immutable"), `IsBuiltIn` (bool) } with a protected parameterless ctor and `JobRole(Guid id, Guid? tenantId, string name, bool isBuiltIn)`.
  - `EmployeeJobRole : Entity<Guid>, IMultiTenant` { `TenantId`, `EmployeeId`, `JobRoleId` }.
  - `DepartmentHead : Entity<Guid>, IMultiTenant` { `TenantId`, `OrganizationUnitId` ("unique: one head per unit"), `EmployeeId` }.
  - `public static class BuiltInJobRoles { public static readonly string[] All = { EmployeeRoles.Employee, EmployeeRoles.Manager, EmployeeRoles.Hr, EmployeeRoles.Finance }; }` (data-model.md).
- [X] T003 In `Domain/Employee.cs`: remove `Role`; add `Email` (string, required), `JobTitle` (string?), `DepartmentId` (Guid?, an `OrganizationUnit.Id`), `IsActive` (bool, default `true`). The constructor becomes `Employee(Guid id, Guid? tenantId, Guid userId, string name, string email, Guid? managerId)`. Keep the `EmployeeRoles` constants (they're now the built-in role names). Update the doc comment on `Role`'s former line in `EmployeeRoles` ("job role used by assignment rules") to point at `JobRole`.
- [X] T004 [P] In `Domain/Processes.cs`: add `public string? FallbackRule { get; set; }` to `ProcessTask`, with doc comment "Copied from HumanTask at creation; null (tasks created before spec 004) means role:HR (research R4)."
- [X] T005 In `Data/PocDbContext.cs` (depends on T002–T004): add `DbSet<JobRole> JobRoles`, `DbSet<EmployeeJobRole> EmployeeJobRoles`, `DbSet<DepartmentHead> DepartmentHeads`. In `OnModelCreating` add `b.Entity<JobRole>(e => { e.Property(x => x.Name).UseCollation("NOCASE"); e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique(); })`, `b.Entity<EmployeeJobRole>(e => e.HasIndex(x => new { x.EmployeeId, x.JobRoleId }).IsUnique())`, `b.Entity<DepartmentHead>(e => e.HasIndex(x => x.OrganizationUnitId).IsUnique())`, following the existing `b.Entity<...>` style and table naming in that file.
- [X] T006 Create `Application/JobRoleLookup.cs` (`ITransientDependency`): `Task<List<string>> GetRoleNamesAsync(Guid employeeId)`, `Task<Dictionary<Guid, List<string>>> GetRoleNamesByEmployeeAsync()` (one query, for lists), `Task<List<Employee>> GetActiveHoldersAsync(string roleName)` (`JobRole` by name, case-insensitive → `EmployeeJobRole` → employees with `IsActive`), `Task<bool> HoldsAsync(Guid employeeId, string roleName)`. Used by T007–T010 and the stories.
- [X] T007 In `Application/AssignmentResolver.cs` (depends on T006): the `role:` branch uses `JobRoleLookup.GetActiveHoldersAsync(role)`; `line-manager`, `manager-of-manager` and `user:` return nobody when the found employee is `!IsActive`. Update the class doc comment's rule list. (`department-head` is added in US2, T024.)
- [X] T008 [P] In `Application/SubmissionAppService.cs` line ~88: replace `me.Role == EmployeeRoles.Hr` with `await _jobRoles.HoldsAsync(me.Id, EmployeeRoles.Hr)` (inject `JobRoleLookup`).
- [X] T009 [P] In `Application/ApprovalWorkflowAppService.cs` lines ~139 and ~148: replace both `_employees.AnyAsync(e => e.Role == ...)` checks with "a `JobRole` with that name exists **and** `(await _jobRoles.GetActiveHoldersAsync(name)).Count > 0`"; keep the existing messages, adding "Role '{name}' is not in this company's role list." for an unknown name (contracts/api.md, "Changed: approval workflow designer").
- [X] T010 [P] In `Application/ProcessManager.cs` `CreateTaskAsync`: set `task.FallbackRule = fallbackRule` before `InsertAsync`. In `WorkflowEngine/Workflows/LeaveApprovalWorkflow.cs` and `WorkflowEngine/Workflows/HrOnlyApprovalWorkflow.cs`: add `FallbackRule = new("role:HR")` to every `HumanTask` (plan Design §7; constitution III).
- [X] T011 Update the role and email seeding (depends on T002–T006):
  - `Data/DemoDataSeeder.cs`: per tenant, inside `_currentTenant.Change(tenant.Id)`, insert the four `BuiltInJobRoles.All` as `JobRole(isBuiltIn: true)`. For each demo person, construct `Employee` with `email` (the same address already given to their `IdentityUser`) and insert one `EmployeeJobRole` for their former role (Khalid and Noor keep job role `Employee`, plan Risks).
  - `Application/TenantOnboardingAppService.cs` `CreateCompanyAsync`: seed the four built-in roles in the new tenant; construct the admin `Employee` with `email`; link them to `Manager`.
  - `Controllers/PocController.cs` `GetUsersAsync`: return the new `EmployeeDto` shape `{ id, name, email, jobTitle, roles: string[], managerId?, managerName?, departmentId?, departmentName?, isActive }` (contracts/api.md), active employees only, ordered by name. Update the `EmployeeDto` record in `Application/Dtos.cs`. `departmentName` comes from `OrganizationUnit.DisplayName`.
  - `wwwroot/index.html`: `rolesOf()` becomes `[...new Set(state.users.flatMap(u => u.roles))]`, and any display of `u.role` shows `u.roles.join(", ")`.
- **Gate**: `dotnet build`; reset once; `dotnet run`; `python3 demo.py` steps 1–11 all pass unchanged (roles moved from a column to links with identical behaviour); restart check passes.

---

## Phase 3: User Story 1 — Add and manage employees (P1) 🎯 MVP

**Goal**: The company admin adds, edits, deactivates and re-activates employees, each with a real sign-in. Deactivation moves their open tasks to the fallback approver.

**Independent Test**: In a fresh vendor-created company, the admin creates employees via `POST employees` (password shown once), each can sign in, a new employee's request routes to their line manager, cycles and duplicate emails are refused, and deactivating an approver moves their pending task to the fallback and blocks their sign-in.

- [X] T012 [P] [US1] In `Application/Dtos.cs` add (contracts/api.md): `SaveEmployeeInput { Name, Email, JobTitle?, Roles: List<string>, ManagerId?, DepartmentId? }`, `record EmployeeCreatedDto(EmployeeDto Employee, string GeneratedPassword)`, `record DeactivationResultDto(EmployeeDto Employee, int ReassignedTaskCount)`.
- [X] T013 [P] [US1] In `Application/CurrentEmployeeProvider.cs` `GetAsync`: after finding the employee, throw `UserFriendlyException("This account is deactivated.")` when `!IsActive` (a token issued before deactivation stays valid until it expires; research R4).
- [X] T014 [US1] Create `Application/TaskReassignmentService.cs` (`ITransientDependency`; plan Design §3): `Task<int> ReassignOpenTasksAsync(Employee deactivated)`. For each `ProcessTask` with `Status == TaskStatusValues.Pending` that has a `ProcessTaskCandidate` with `UserId == deactivated.Id`: resolve `task.FallbackRule ?? "role:HR"` with `AssignmentResolver.ResolveAsync(rule, submission.SubmittedByUserId)` (load the `FormSubmission` for the submitter). Delete the task's candidate rows and insert the resolved people. Add a `ProcessEvent` with `EventType = "TaskReassigned"` and detail `"{task.Title}: {name} was deactivated; reassigned to {names} via the fallback."`, or, when the fallback resolves to nobody, `"{task.Title}: {name} was deactivated and the fallback matched nobody. The request is stuck until the rule or HR data is fixed."`. Use the ambient unit of work; **no `IWorkflowGateway` call** (Principle VI). Return the number of tasks touched.
- [X] T015 [US1] Create `Application/EmployeeAppService.cs` (`ApplicationService`; depends on T006, T012, T014). All methods `[Authorize(WorkmatePermissions.EmployeesManage)]`.
  - `CreateAsync(SaveEmployeeInput)` returns `EmployeeCreatedDto`, all in one unit of work (FR-002):
    1. Validate: name and email required; every role name exists in `JobRole` ("Unknown role '{x}'."); `ManagerId` (if set) is an employee of this tenant; `DepartmentId` (if set) is an `OrganizationUnit` of this tenant.
    2. Create `IdentityUser(GuidGenerator.Create(), email, email, CurrentTenant.Id)` with `IdentityUserManager.CreateAsync(user, PasswordGenerator.New())`. On failure, throw its error descriptions; ABP's duplicate-email error covers US1 AC3, and the same email in another tenant succeeds.
    3. Insert the `Employee` and its `EmployeeJobRole` rows; if `DepartmentId` is set, `SetOrganizationUnitsAsync(user, departmentId)`.
    4. Return the employee plus the password.
  - `UpdateAsync(Guid id, SaveEmployeeInput)`: same validation, plus a cycle check: walk `ManagerId` upward from the proposed manager; refuse with "That manager change would create a reporting loop." if it reaches `id`, or "An employee cannot be their own manager." if `ManagerId == id` (US1 AC2). If the email changed, `SetEmailAsync` + `SetUserNameAsync` on the `IdentityUser` (plan Risks). Replace role links; re-sync the OU (`SetOrganizationUnitsAsync` with `[]` when cleared). Don't touch existing task candidates (FR-005).
  - `DeactivateAsync(Guid id)` returns `DeactivationResultDto`: if already inactive, return a count of 0 (idempotent). Otherwise set `employee.IsActive = false`, `user.SetIsActive(false)` + `UpdateAsync`, then `ReassignOpenTasksAsync`, all in one unit of work (FR-007).
  - `ActivateAsync(Guid id)`: set both flags back to `true`; don't move tasks back.
- [X] T016 [US1] In `Controllers/PocController.cs` (depends on T015): `POST employees`, `PUT employees/{id:guid}`, `POST employees/{id:guid}/deactivate`, `POST employees/{id:guid}/activate`. Extend `GET users` with `[FromQuery] bool includeInactive = false`; when true, require `Workmate.Employees.Manage` via `IPermissionChecker` (403 otherwise) and include inactive employees.
- [X] T017 [US1] In `wwwroot/index.html`: an **Employees** tab, shown only when `state.permissions` includes `Workmate.Employees.Manage`. It has a table (`GET users?includeInactive=true`: name, email, job title, roles, manager, department, active) and an add/edit form (name, email, job title, roles as checkboxes from `rolesOf()`, manager `<select>` from `state.users`, department `<select>` from `GET departments` if available, else omit the field until US2). Show the `generatedPassword` notice once after create. Deactivate/Activate buttons show `reassignedTaskCount`. Use the existing `message()`/`api()`/`esc()` helpers and tab pattern.
- [X] T018 [US1] In `demo.py` add `def s12():` and register `("Step 12 employees + org structure (spec 004)", s12)` after step 11 in `__main__`. Start s12 like s11: the vendor signs in and creates company `f"Umbrella {RUN_SUFFIX}"`, and the admin signs in (store tokens in a local dict). Then add the US1 checks:
  - 12a-part1: create 6 employees (the org skeleton from quickstart.md 12a; departments are added in T026). Each returns a `generatedPassword`, and each person can sign in.
  - 12d: a manager cycle is refused (US1 AC2).
  - 12e: a duplicate email in the same company is refused, and the same email in Globex is accepted. Create it through Globex's admin Noor (`noor@globex.demo`), who holds `Employees.Manage` via the CompanyAdmin role.
  - 12h: bind a designed workflow `[line-manager step]` with fallback `role:HR` to a fresh form in this company (reuse step 9's create-form/create-workflow/bind call pattern). An employee submits, so the task is with their manager. Deactivate the manager; the task is now in the HR holder's inbox, the manager's inbox no longer has it, and the timeline has a `TaskReassigned` event.
  - 12i: the deactivated manager's `/connect/token` fails, and their pre-deactivation token gets 403 on `GET /tasks/inbox`.
  - 12j: a non-admin employee calling `POST /employees` gets 403.
- **Gate (US1 Independent Test)**: steps 1–11 still pass; 12a-part1, 12d, 12e, 12h, 12i, 12j pass; manual check of the Employees tab.

---

## Phase 4: User Story 2 — Organisation structure (P1)

**Goal**: The admin builds the department tree, sets heads and places employees. A "Head of my department" approver walks up the tree.

**Independent Test**: Build Company → Finance → Payables with heads, put employees in units, and a designed workflow `line-manager → department-head → role:HR` routes a Payables request with no code. A head's own request goes to the parent head. A non-empty unit can't be deleted.

- [ ] T019 [P] [US2] In `Domain/ApprovalWorkflows.cs`: add `public const string DepartmentHead = "department-head";` to `ApproverTypes` and include it in `All`. `ToAssignmentRule()` already passes unknown non-role/user types through. Confirm `ApprovalWorkflowSchema.Parse` accepts it with no `Role`/`UserId`. `FallbackRules.Parse` stays role/user only.
- [ ] T020 [P] [US2] In `Application/Dtos.cs` add (contracts/api.md): `record DepartmentDto(Guid Id, string Name, Guid? ParentId, Guid? HeadEmployeeId, string? HeadName, int EmployeeCount)`, `CreateDepartmentInput { Name, ParentId? }`, `UpdateDepartmentInput { Name, HeadEmployeeId? }`.
- [ ] T021 [US2] Create `Application/OrganizationAppService.cs` (`ApplicationService`; depends on T002, T020) with the department part:
  - `GetDepartmentsAsync()`: any signed-in user; flat list from `IRepository<OrganizationUnit, Guid>` with head and employee count.
  - `CreateDepartmentAsync`: `[Authorize(WorkmatePermissions.OrgManage)]`; `OrganizationUnitManager.CreateAsync(new OrganizationUnit(GuidGenerator.Create(), name, parentId, CurrentTenant.Id))`; the parent must exist in this tenant.
  - `UpdateDepartmentAsync(id, input)`: `[Authorize(OrgManage)]`; rename via `OrganizationUnitManager.UpdateAsync`. Upsert the `DepartmentHead` row when `HeadEmployeeId` is set (it must be an employee of this tenant); delete it when null.
  - `DeleteDepartmentAsync(id)`: `[Authorize(OrgManage)]`. Refuse with "Move its employees and sub-departments first." when any `Employee.DepartmentId == id` or any `OrganizationUnit.ParentId == id` (US2 AC2). This check must run **before** `OrganizationUnitManager.DeleteAsync`, which deletes recursively (plan Risks). Then delete the head row and the unit.
- [ ] T022 [US2] In `Controllers/PocController.cs` (depends on T021): `GET departments`, `POST departments`, `PUT departments/{id:guid}`, `DELETE departments/{id:guid}`.
- [ ] T023 [US2] Make `EmployeeAppService` (T015) fill `departmentName` and the `GET users` `EmployeeDto.departmentName`/`departmentId` from the unit. If T015 already validates and syncs `DepartmentId`, this is just the display. Confirm the OU mirror (`SetOrganizationUnitsAsync`) holds at most one unit (research R3).
- [ ] T024 [US2] In `Application/AssignmentResolver.cs` (depends on T019): add the `department-head` branch (research R5, plan Design §4). Load the submitter. `unitId = submitter.DepartmentId`, then loop while `unitId != null`: find the `DepartmentHead` for the unit; if the head exists, `IsActive`, and `head.Id != submitterUserId`, return `[head]`; else `unitId = (await _orgUnits.GetAsync(unitId)).ParentId`. Return nobody at the root, so the existing fallback path applies (FR-010, clarification Q3). Guard against loops with a visited set.
- [ ] T025 [US2] In `wwwroot/index.html`:
  - An **Organisation** tab, shown when `state.permissions` includes `Workmate.Org.Manage`. It shows an indented tree from `GET departments` (by `parentId`), with add child, rename, set head (`<select>` of `state.users`), and delete (show the refusal message).
  - In the route designer's approver list (line ~551), add `["department-head", "Head of my department"]`.
  - In the Employees form (T017), show the department picker.
- [ ] T026 [US2] In `demo.py` `s12`, after T018's employee creation, add:
  - 12a-part2: build `Umbrella → Finance → Payables`, set heads (Finance head, Payables head), place employees, and confirm `GET departments` shows the 3 levels.
  - 12b (SC-001): a designed workflow with steps `line-manager`, `department-head` and `role:HR`, fallback `role:HR`, bound to a fresh form. A Payables clerk's request goes to their manager, then the Payables head, then HR, then Approved. Check each inbox in turn, as step 9 does.
  - 12c: the Payables head submits the same form, and the department-head step lands with the Finance head (clarification Q3).
  - 12f: deleting Finance (it has a sub-unit) and deleting Payables (it has employees) are both refused.
- **Gate (US2 Independent Test)**: all previous checks still pass; 12a-part2, 12b, 12c, 12f pass; manual check of the Organisation tab and the new approver type.

---

## Phase 5: User Story 3 — Roles catalogue (P2)

**Goal**: The admin manages the company's job roles. A role a published workflow uses can't be deleted.

**Independent Test**: Add a custom role, assign it, use it in a published workflow; deleting it is refused and names the workflow. An unused role deletes and disappears from its holders.

- [ ] T027 [P] [US3] In `Application/Dtos.cs` add `record JobRoleDto(Guid Id, string Name, bool IsBuiltIn, int EmployeeCount)` and `CreateJobRoleInput { Name }`.
- [ ] T028 [US3] In `Application/OrganizationAppService.cs` (depends on T021, T027) add:
  - `GetRolesAsync()`: any signed-in user.
  - `CreateRoleAsync`: `[Authorize(EmployeesManage)]`; name trimmed and required; refuse a case-insensitive duplicate with "A role named '{x}' already exists."
  - `DeleteRoleAsync(id)`: `[Authorize(EmployeesManage)]`. Collect the names of active `ApprovalWorkflow`s whose **published** `ApprovalWorkflowVersion` has any step with `ToAssignmentRule() == "role:{name}"` (case-insensitive) or `FallbackRule == "role:{name}"`. Add `"Leave approval"`/`"HR only approval"` (the code-defined workflows) when the name is `HR` (research R6). If any, throw "Role '{name}' is used by: {names}." (US3 AC1). Else delete its `EmployeeJobRole` rows, then the role (US3 AC2, clarification Q5). Built-in roles follow the same rule.
- [ ] T029 [US3] In `Controllers/PocController.cs`: `GET roles`, `POST roles`, `DELETE roles/{id:guid}`.
- [ ] T030 [US3] In `wwwroot/index.html`: `rolesOf()` now reads a `state.roles` loaded from `GET roles` at sign-in (instead of deriving from users), so the route designer and Employees form list catalogue roles even when nobody holds them yet. Add a roles panel to the Employees tab (list with employee counts, add, delete, showing the refusal message).
- [ ] T031 [US3] In `demo.py` `s12` add 12g: create role `Payroll`, give it to one employee, and publish a workflow with a `role:Payroll` step. Deleting `Payroll` is refused and names that workflow. Create role `Temp`, give it to one employee, delete it; `GET roles` no longer lists it, and the employee's `roles` no longer contains it.
- **Gate (US3 Independent Test)**: all previous checks pass; 12g passes; manual roles panel check.

---

## Phase 6: Polish

- [ ] T032 [P] Update `README.md` (Employees/Organisation tabs, the new approver type, reset needed), `HANDOVER.md` (mark spec 004 done; note the reset), and `CHANGELOG.md` (one line per task, under a new `## Spec 004: employees and organisation structure` heading).
- [ ] T033 [P] In `specs/002-route-designer/spec.md`, leave the text as is, but append to its Clarifications an "Update 2026-10-21" bullet that employees now have an active flag and deactivation reassignment (spec 004 FR-007), so the "no active/inactive concept" assumption is now superseded. Don't edit the original assumption line.
- [ ] T034 Final loop: fresh reset → `python3 demo.py` (steps 1–12) → restart check (`restart-before`, restart, `restart-after`). Record the pass counts in CHANGELOG. Do a manual UI pass as a company admin (Employees, Organisation, route designer) and as a non-admin (the tabs are hidden).

---

## Dependencies & Execution Order

T001 → **Phase 2 (T002–T011, one build unit; T002/T004 in parallel, then T005; T006 → T007; T008/T009/T010 in parallel after T006; T011 last)** → **US1 (T012–T018)** → **US2 (T019–T026)** → **US3 (T027–T031)** → T032–T034.

- US2's code (T019–T022, T024) doesn't depend on US1. It needs Phase 2's `DepartmentHead`/`Employee.DepartmentId` only. But its `demo.py` checks (T026) build on T018's company and employees, and T023/T025 extend US1's service and form. So in practice do US2 after US1.
- US3's code (T027–T029) depends only on Phase 2 and on `OrganizationAppService` existing (T021). T030 extends T017's tab.
- All `demo.py` tasks (T018, T026, T031) edit one file, so they run sequentially.

## Parallel execution examples

```text
# Phase 2, independent files:
Task: "Create Domain/Organization.cs" (T002)
Task: "ProcessTask.FallbackRule in Domain/Processes.cs" (T004)

# Phase 2, after T006 (JobRoleLookup):
Task: "SubmissionAppService HR check" (T008)
Task: "ApprovalWorkflowAppService role checks" (T009)
Task: "ProcessManager + code workflows FallbackRule" (T010)

# US1:
Task: "Employee DTOs in Application/Dtos.cs" (T012)
Task: "CurrentEmployeeProvider refuses inactive" (T013)

# US2:
Task: "ApproverTypes.DepartmentHead" (T019)
Task: "Department DTOs" (T020)
```

## Implementation Strategy

- **MVP = Phase 2 + US1.** Behaviour matches today with roles moved to a catalogue (steps 1–11 green), plus the admin can add employees who sign in and route by line manager, and deactivation can't strand a request.
- **US2** adds the department tree and the `department-head` approver. Together with US1 it delivers SC-001 (12b).
- **US3** adds role catalogue management with in-use protection.
- Stop at each phase's gate. Every phase leaves `demo.py` fully green.
