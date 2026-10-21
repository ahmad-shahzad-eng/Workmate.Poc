# Research: Employees and organisation structure

Decisions behind [plan.md](./plan.md). Each one was checked against the current code (branch `004-employees-org`, on top of spec 003), not assumed.

## R1. Job roles: HR-side catalogue, not ABP `IdentityRole`

- **Decision**: New HR-side entities `JobRole` (tenant catalogue) and `EmployeeJobRole` (link). `Employee.Role` is removed.
- **Rationale**: ABP `IdentityRole` is the *permission* role (spec 003's `Workmate.CompanyAdmin`). Putting "HR"/"Finance" there would mix "who may design forms" with "who approves expense claims", and would let a role-catalogue edit change permissions. Constitution III says assignment comes from HR data, and `AssignmentResolver` already reads only `Employee`.
- **Alternatives**: `IdentityRole` for both (rejected: mixes permissions with routing, and the "Role" picker would list `Workmate.CompanyAdmin`); keep the single `Employee.Role` string (rejected by clarification Q1).

## R2. Role names are the rule key, and they never change

- **Decision**: Rules keep storing `role:{Name}`, as published workflows already do. `JobRole.Name` is unique per tenant (case-insensitive) and can't be renamed, only created or deleted.
- **Rationale**: Published workflow versions are immutable JSON containing `role:HR` (Principle I applies to workflow versions in spirit). Renaming would silently break them. Switching rules to role ids would mean rewriting every published version.
- **Alternatives**: rule by role id (rejected: would rewrite published versions); rename cascades into drafts only (rejected: published versions would still hold the old name).

## R3. Departments: ABP `OrganizationUnit`; membership and head on the HR side

- **Decision**: ABP Identity `OrganizationUnit` (already in `PocDbContext` since spec 003) is the tree (FR-001). Each employee belongs to at most one unit: `Employee.DepartmentId` (an OU id) is the source of truth, mirrored into ABP's user–OU link via `IdentityUserManager.SetOrganizationUnitsAsync` in the same unit of work (FR-001, FR-002). The unit head is a new HR-side entity `DepartmentHead { OrganizationUnitId, EmployeeId }`.
- **Rationale**: FR-001 puts the tree in `OrganizationUnit` and the head on the HR side. A single `DepartmentId` makes "my department" well-defined for the `department-head` rule. The mirror keeps ABP's own OU queries correct for later (permissions per OU, etc.).
- **Alternatives**: rely only on ABP's user–OU link (rejected: many-to-many, so "my department" would be ambiguous); store the head as an `OrganizationUnit` extra property (rejected: FR-001 says HR side, and extra properties are untyped).
- **Verify on build**: `OrganizationUnitManager.CreateAsync` generates the hierarchical `Code`; delete refusal (US2 AC2) is our own check, since ABP's `DeleteAsync` cascades.

## R4. Deactivation: block sign-in, move open tasks to the fallback (FR-007)

- **Decision**:
  1. `IdentityUser.SetIsActive(false)`. ABP 9.1's `AbpSignInManager.PreSignInCheck` and the OpenIddict `TokenController` both check `IsActive` (symbols confirmed in the 9.1.0 binaries), so `/connect/token` refuses with no custom code.
  2. `CurrentEmployeeProvider.GetAsync` also refuses an inactive employee, because an already-issued access token stays valid until it expires.
  3. `AssignmentResolver` skips inactive employees for every rule.
  4. Every pending `ProcessTask` where they are a candidate has its candidates replaced with the resolution of the task's fallback rule, plus a `TaskReassigned` timeline event.
- **New field**: `ProcessTask.FallbackRule` (nullable), written in `ProcessManager.CreateTaskAsync` (the value is already passed in from `HumanTask`). Tasks created before this change have no value.
- **Code-defined workflows** (`leave-approval`, `hr-only-approval`) have no fallback. Their `HumanTask`s get `FallbackRule = "role:HR"` (constitution III: never stuck), and a task with a null `FallbackRule` (created before this phase) uses `role:HR` too.
- **Fallback resolves to nobody** (e.g. HR is the deactivated person): the candidates are cleared and the timeline says the request is stuck until HR data is fixed. That's the same wording and behaviour as `CreateTaskAsync` today, and visible rather than silent.
- **Elsa untouched**: candidates live in `ProcessTaskCandidate` (our table). The Elsa bookmark is unchanged, so Principle VI holds.
- **Alternatives**: drop only the deactivated person when others remain (rejected by clarification Q2, option D); re-resolve the task's own rule (rejected: the clarification chose the fallback).

## R5. `department-head` rule

- **Decision**: New `ApproverTypes.DepartmentHead = "department-head"` (passes through `ToAssignmentRule` like `line-manager`). Resolution: start at the submitter's `DepartmentId`. While the unit has no head, its head is inactive, or the head **is the submitter**, move to the parent unit (via the OU's `ParentId`). Return the head found, or nobody at the root, which triggers the existing FR-010 fallback (clarification Q3). Not allowed as a fallback (FR-010 already restricts the fallback to role/user).

## R6. Built-in roles and the "in use" check (US3)

- **Decision**: Every tenant gets `Employee`, `Manager`, `HR`, `Finance` as `JobRole` rows with `IsBuiltIn = true` (seeder, and `TenantOnboardingAppService.CreateCompanyAsync` for new companies). "In use" means referenced by any **published** `ApprovalWorkflowVersion` (step `role:X` or `FallbackRule = role:X`) of an **active** workflow, or by a code-defined workflow. Code-defined workflows use `role:HR`, so `HR` is always in use. Deleting an unused role removes its `EmployeeJobRole` links (clarification Q5). `IsBuiltIn` is informational only; deletion rules are the same for every role.

## R7. First password

- **Decision**: Same generator as `TenantOnboardingAppService.GeneratePassword()`, moved to a shared static helper. The create-employee response returns it once (clarification Q4).

## R8. Who may call what

- **Decision**: Employee create/edit/deactivate: `Workmate.Employees.Manage`. Departments/heads: `Workmate.Org.Manage`. Role catalogue: `Workmate.Employees.Manage`. All three permissions already exist and are granted to `Workmate.CompanyAdmin` (spec 003). Read lists (`GET users`, `GET roles`, `GET departments`) only need a signed-in caller (the route designer's pickers use them).
- **Vendor admin**: `EmployeesManage`/`OrgManage` are `MultiTenancySides.Tenant` today, so the host vendor admin can't manage a client's employees. That's intended (spec 003 US1: the client admin adds employees). No change.
