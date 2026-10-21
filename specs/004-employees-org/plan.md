# Implementation Plan: Employees and organisation structure (client admin)

**Branch**: `004-employees-org` | **Date**: 2026-10-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/004-employees-org/spec.md` (clarified 2026-10-21, 5 answers)

## Summary

Give the client admin real HR data management: employees (create, edit, deactivate) with a linked Identity user; a department tree on ABP `OrganizationUnit` with one head per unit; and a per-company job-role catalogue replacing the single `Employee.Role` string. Routing gains a `department-head` approver that walks up the tree. Deactivation blocks sign-in and moves the person's open tasks to the workflow's fallback approver. Everything stays HR data read by `AssignmentResolver` (Principle III). No workflow definitions change, and Elsa is untouched except for one `FallbackRule` input on the two code-defined workflows. Decisions with rationale: [research.md](./research.md).

## Technical Context

**Language/Version**: C# on `net10.0` (unchanged)

**Primary Dependencies**: ABP 9.1.0 open-source. Identity's `OrganizationUnit`/`OrganizationUnitManager`/`IdentityUserManager` are already loaded since spec 003, so **no new packages**. Elsa 3.7.0 unchanged.

**Storage**: Same `workmate-poc.db` and single `EnsureCreatedAsync`. New tables `JobRoles`, `EmployeeJobRoles`, `DepartmentHeads`; new columns on `Employees` and `ProcessTasks`; `Employees.Role` dropped. **`Poc:ResetDatabaseOnStartup=true` required once.**

**Testing**: `demo.py`. Existing steps 1–11 plus the restart check must stay green; new step 12 ([quickstart.md](./quickstart.md)).

**Target Platform**: Local dev, single ASP.NET Core process (unchanged)

**Project Type**: Web service + vanilla-JS demo UI (`wwwroot/index.html`)

**Performance Goals**: N/A for the PoC (tens of employees per tenant). The department walk-up is at most tree-depth queries.

**Constraints**: Open-source ABP only; tenant isolation on every new entity; no change to Elsa workflow definitions beyond adding `FallbackRule` to code-defined `HumanTask`s.

**Scale/Scope**: 4 user stories, of which US1–US3 are in scope and US4 (CSV import) is out of scope per the spec.

## Constitution Check

*GATE: checked before research and again after design. Both pass.*

| Principle / gate | Status |
| --- | --- |
| I Immutable form versions | Not touched. Role names are immutable (R2) so published workflow versions holding `role:X` stay valid. |
| II Commit before workflow | Not touched. Deactivation edits only our task/candidate tables and never calls Elsa. |
| III Assignment from HR data | **Extended.** New `department-head` rule; `role:` reads the catalogue; inactive employees are never candidates. Code-defined workflows gain a `role:HR` fallback so deactivation never leaves a task stuck silently (R4). |
| IV Tenant isolation | **Applies.** All new entities are `IMultiTenant`; manager, department and role references are validated within the tenant; `demo.py` step 12e proves the same email works across tenants without leaking. |
| V Typed reporting values | Not touched |
| VI One door into Elsa | Kept. Reassignment rewrites `ProcessTaskCandidate` rows only; the Elsa bookmark is unchanged. |
| VII Admin is a permission | **Applies.** `Workmate.Employees.Manage` / `Workmate.Org.Manage` (defined in spec 003, granted to `Workmate.CompanyAdmin`) guard every write. |
| Gates | CHANGELOG line per change; `demo.py` checks for every acceptance scenario (step 12); build → run → demo loop after one reset. |

Result: **PASS**, no exceptions needed. (Complexity Tracking is therefore empty.)

## Project Structure

### Documentation (this feature)

```text
specs/004-employees-org/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R8 decisions
├── data-model.md
├── quickstart.md        # demo.py step 12 + manual UI checks
├── contracts/api.md     # new and changed routes
└── tasks.md             # /speckit-tasks (not yet)
```

### Source Code (repository root, existing single-project layout)

```text
Domain/
├── Employee.cs                  # changed: -Role, +Email, JobTitle, DepartmentId, IsActive
├── Organization.cs              # new: JobRole, EmployeeJobRole, DepartmentHead, BuiltInJobRoles
├── ApprovalWorkflows.cs         # changed: ApproverTypes.DepartmentHead
└── Processes.cs                 # changed: ProcessTask.FallbackRule
Application/
├── EmployeeAppService.cs        # new: create/edit/activate/deactivate (FR-002, FR-007)
├── OrganizationAppService.cs    # new: departments + heads, role catalogue
├── AssignmentResolver.cs        # changed: role catalogue, department-head, skip inactive
├── TaskReassignmentService.cs   # new: FR-007 move-to-fallback
├── PasswordGenerator.cs         # new: shared with TenantOnboardingAppService (R7)
├── ProcessManager.cs            # changed: store FallbackRule on the task
├── ApprovalWorkflowAppService.cs# changed: role checks via catalogue
├── SubmissionAppService.cs      # changed: "is HR" via role links
├── CurrentEmployeeProvider.cs   # changed: refuse inactive
├── TenantOnboardingAppService.cs# changed: seed built-in roles, set Email
└── Dtos.cs
Data/
├── PocDbContext.cs              # new DbSets + indexes
└── DemoDataSeeder.cs            # built-in roles, role links, emails
WorkflowEngine/Workflows/
├── LeaveApprovalWorkflow.cs     # FallbackRule = "role:HR"
└── HrOnlyApprovalWorkflow.cs    # FallbackRule = "role:HR"
Controllers/PocController.cs     # routes per contracts/api.md
wwwroot/index.html               # Employees + Organisation tabs; route designer approver type + role picker
demo.py                          # step 12
```

**Structure Decision**: Keep the existing single ASP.NET Core project (`Domain/`, `Application/`, `Data/`, `WorkflowEngine/`, `Controllers/`, `wwwroot/`). No new projects.

## Design

### 1. Data model

As in [data-model.md](./data-model.md). `PocDbContext.OnModelCreating` adds `b.Entity<JobRole>()` with a unique index on (`TenantId`, `Name`) (SQLite `NOCASE` collation for case-insensitivity), `EmployeeJobRole` with a unique index on (`EmployeeId`, `JobRoleId`), and `DepartmentHead` with a unique index on `OrganizationUnitId`.

### 2. `EmployeeAppService` (`[Authorize(WorkmatePermissions.EmployeesManage)]` on writes)

- **Create** (FR-002, one unit of work): validate (roles exist; manager and department in the tenant; no cycle, by walking `ManagerId` from the proposed manager and refusing if it reaches this employee or loops; at most tree-size steps) → `IdentityUser(email, email, tenantId)` + `CreateAsync(user, PasswordGenerator.New())` (ABP refuses a duplicate email in the tenant, US1 AC3; surface its message) → `Employee` → `EmployeeJobRole` rows → `SetOrganizationUnitsAsync(user, departmentId)` → return the generated password once.
- **Update**: same validation; `SetEmailAsync`/`SetUserNameAsync` if the email changed; replace role links; re-sync the OU. Manager/department changes affect new steps only (FR-005); existing candidates are untouched.
- **Deactivate** (FR-007, one unit of work): `employee.IsActive = false`; `user.SetIsActive(false)`; `TaskReassignmentService.ReassignOpenTasksAsync(employee)` → returns the count.
- **Activate**: flips both flags back; doesn't move tasks back.

### 3. `TaskReassignmentService` (FR-007)

For each `ProcessTask` with `Status == Pending` that has a `ProcessTaskCandidate` for this employee: resolve `task.FallbackRule ?? "role:HR"` through `AssignmentResolver` (which already excludes the submitter and, after this phase, inactive people). Replace the task's candidate rows with the result, then add a `TaskReassigned` event: "*{title}: {name} was deactivated; reassigned to {people} via the fallback.*" If the fallback resolves to nobody, clear the candidates and say the request is stuck until HR data is fixed (the same wording `CreateTaskAsync` already uses). Runs in the caller's unit of work; there's no Elsa call (Principle VI).

### 4. `AssignmentResolver`

- All rules filter `IsActive`.
- `role:{Name}`: `JobRole` by name → `EmployeeJobRole` → active employees.
- `department-head` (R5): `unit = submitter.DepartmentId`; loop: find `DepartmentHead` for the unit; if the head exists, is active and isn't the submitter, return them; else `unit = OrganizationUnit.ParentId`; stop at the root and return nobody, so the existing fallback path takes over (FR-010).

### 5. `OrganizationAppService`

- **Departments** (`[Authorize(WorkmatePermissions.OrgManage)]` on writes): `OrganizationUnitManager.CreateAsync(new OrganizationUnit(id, name, parentId, tenantId))`, rename via `UpdateAsync`. Head: upsert or delete the `DepartmentHead` row (the head must be an employee of the tenant). Delete: refuse if any `Employee.DepartmentId == id` or any OU has `ParentId == id` (US2 AC2); otherwise delete the head row, then `OrganizationUnitManager.DeleteAsync`.
- **Roles** (`EmployeesManage` on writes): create (unique name); delete: collect published-and-active `ApprovalWorkflowVersion`s whose steps or `FallbackRule` reference `role:{name}`, plus code-defined workflows (`role:HR`). If any, refuse and name them (US3 AC1); else delete the links, then the role (US3 AC2).

### 6. Existing code that read `Employee.Role`

| Place | Becomes |
|---|---|
| `SubmissionAppService.GetDetailAsync` "HR may view" | the employee holds the `HR` role |
| `ApprovalWorkflowAppService` "role has no employees" (×2) | `JobRole` exists **and** has ≥1 active holder |
| `PocController.GetUsersAsync` / `EmployeeDto` | new DTO shape (contracts); ordered by name |
| `DemoDataSeeder` (two tenants) | seed built-in roles per tenant; link each demo person to their old role; set `Email` |
| `TenantOnboardingAppService.CreateCompanyAsync` | seed built-in roles; first admin gets `Manager`; set `Email`; use `PasswordGenerator` |

### 7. `ProcessTask.FallbackRule` and code-defined workflows

`ProcessManager.CreateTaskAsync` already receives `fallbackRule`; it now also stores it on the task. `LeaveApprovalWorkflow` and `HrOnlyApprovalWorkflow` set `FallbackRule = new("role:HR")` on their `HumanTask`s. For `hr-only-approval`'s own `role:HR` step this is a no-op on creation (same rule) and matters only on deactivation.

### 8. Demo UI (`wwwroot/index.html`)

- **Employees** tab (shown when `my-permissions` contains `Workmate.Employees.Manage`): table plus add/edit form (name, email, job title, role multi-select from `GET roles`, manager picker, department picker), deactivate/activate buttons, the one-time generated password notice, and a small roles panel (add, delete with the refusal message).
- **Organisation** tab (`Workmate.Org.Manage`): indented tree from `GET departments`, add child, rename, set head, delete.
- **Route designer**: approver list adds `["department-head", "Head of my department"]`; `rolesOf()` reads `GET roles` instead of deriving from users.

### 9. `demo.py` step 12

As listed in [quickstart.md](./quickstart.md). Uses a fresh vendor-created company (like step 11) so it never disturbs Acme/Globex data that steps 1–10 rely on.

## Risks (verify on first build)

- **Inactive-user sign-in refusal**: relies on ABP 9.1 checking `IsActive` (R4). If `/connect/token` still issues a token, add an explicit check in `CurrentEmployeeProvider` (already planned) and refuse at token time via `AbpOpenIddictAspNetCoreOptions` or a custom `IdentityUserManager` check.
- **Email change**: `SetEmailAsync` resets `EmailConfirmed` (harmless, no confirmation flow) and must be paired with `SetUserNameAsync`, since the username is the email (spec 003).
- **`OrganizationUnitManager.DeleteAsync`** deletes children recursively; our refusal check must run first (US2 AC2).
- **Existing demo data**: Khalid and Noor are company admins *and* hold the job role `Employee`. Keep that, so steps 7a/7b, which depend on who's in which role, stay unchanged.

## Complexity Tracking

No constitution violations; nothing to justify.
