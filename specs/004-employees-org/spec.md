# Feature Specification: Employees and organisation structure (client admin)

**Feature Branch**: `004-employees-org` | **Created**: 2026-10-20 | **Status**: Draft (seeded) | **Depends on**: 003

**Input**: Ahmad, 2026-10-20: the client admin adds employees and the organisation structure.

## Clarifications

### Session 2026-10-21
- Q: Can an employee hold more than one role, or exactly one? → A: Zero or more roles per employee, picked from the company's role catalogue (US3).
- Q: When an employee with open approval tasks is deactivated, what happens to those tasks? → A: Each open task moves immediately to that workflow's fallback approver (spec 002 FR-010), with the reason recorded in the timeline.
- Q: Who approves a "Head of my department" step when the submitter is their own department's head? → A: Walk up to the parent department's head (and further up); use the fallback approver only if nobody is found at the top.
- Q: Until spec 005 sends email, how does a new employee get their first password? → A: A random password is generated and shown once to the admin on screen (same as spec 003's company admin); spec 005 replaces this with an emailed set-password link.
- Q: Which catalogue roles are protected from deletion? → A: Any role, built-in or custom, cannot be deleted while a published workflow uses it; an unused role can be deleted and is removed from any employees holding it.
- Update 2026-10-21 (spec 005): the on-screen generated password is replaced by an emailed set-password link (spec 005 FR-008). A new employee is created with no password and chooses one from the invite; anyone can also ask for a reset link from the sign-in screen ("Forgot password?"). The original text above is kept as the record of the decision at the time.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add and manage employees (Priority: P1)
The Initech admin adds employees (name, email, job title, zero or more roles from the company's role catalogue, line manager, department, active). Each gets a sign-in account; the generated first password is shown once to the admin on screen (an emailed set-password link replaces this in spec 005). The admin can edit, deactivate and re-activate; never hard-delete someone with requests or tasks.

**Acceptance Scenarios**:
1. **Given** a new employee with a line manager, **Then** their requests route to that manager (spec 002 rules work without code changes).
2. **Given** a manager chain A → B → A, **Then** saving is refused (no cycles).
3. **Given** a duplicate email within the company, **Then** saving is refused; the same email in another company is allowed.

### User Story 2 - Organisation structure (Priority: P1)
The admin builds a tree of departments/units (e.g. Initech → Finance → Payables), sets a head for each unit and places employees in units.

**Acceptance Scenarios**:
1. **Given** a unit with a head, **Then** a new approver type "Head of my department" is available in the route designer (spec 002) and resolves to the submitter's unit head, walking up the tree while the unit has no head **or the head is the submitter**; if nobody is found at the top, the step goes to the fallback approver (spec 002 FR-010).
2. **Given** a unit with employees or sub-units, **When** the admin deletes it, **Then** it is refused until they are moved.

### User Story 3 - Roles catalogue (Priority: P2)
The admin manages the company's roles (HR, Finance, Payroll, ...) used by "Role" steps.

**Acceptance Scenarios**:
1. **Given** a role (built-in or custom) used by a "Role" step in a published workflow, **When** the admin deletes it, **Then** it is refused, naming the workflows that use it.
2. **Given** a role no published workflow uses, **When** the admin deletes it, **Then** it is deleted and removed from every employee who held it.

### User Story 4 - Bulk import (Priority: P3)
Import employees from CSV with a validation report (out of PoC scope unless time allows).

## Requirements *(mandatory)*
- **FR-001**: Use ABP Identity `OrganizationUnit` (open source) for the tree and unit membership; store the unit head and the line manager on the HR side (`Employee`).
- **FR-002**: Employee create/edit MUST create/update the linked Identity user (spec 003) in the same unit of work.
- **FR-003**: All lists and pickers are tenant-filtered; the submitter rule of spec 002 still applies.
- **FR-004**: `AssignmentResolver` MUST add `department-head` and use the role catalogue for `role:{Role}`: a role step's candidates are every employee holding that role.
- **FR-005**: Changes to manager or department apply to new steps only; already-created tasks keep their candidates (recorded in the timeline). The one exception is deactivation (FR-007).
- **FR-006**: An employee MAY hold zero or more roles, each from the company's role catalogue (US3); the single `Employee.Role` field is replaced by an employee–role link.
- **FR-007**: Deactivating an employee MUST block their sign-in and, in the same unit of work, move each open task where they are a candidate to that workflow's fallback approver (spec 002 FR-010), recording the reason in the timeline. Re-activation does not move tasks back.

## Success Criteria *(mandatory)*
- **SC-001**: A client admin builds a 3-level org and 6 employees, then a request routes via line manager → department head → HR with no code (new demo.py step 12; step 11 is spec 003's vendor onboarding).

## Files likely touched
`Domain/Employee.cs` (JobTitle, DepartmentId, IsActive, roles), new `Domain/Organization.cs` (unit head, role catalogue), `Data/PocDbContext.cs`, `Data/DemoDataSeeder.cs`, `Application/AssignmentResolver.cs`, new `Application/EmployeeAppService.cs`, new `Application/OrganizationAppService.cs`, `Application/Dtos.cs`, `Controllers/PocController.cs`, `wwwroot/index.html` (Employees and Organisation screens; new approver type in the route designer), `demo.py`.
