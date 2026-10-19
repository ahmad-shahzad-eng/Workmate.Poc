# Feature Specification: Employees and organisation structure (client admin)

**Feature Branch**: `004-employees-org` | **Created**: 2026-10-20 | **Status**: Draft (seeded) | **Depends on**: 003

**Input**: Ahmad, 2026-10-20: the client admin adds employees and the organisation structure.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add and manage employees (Priority: P1)
The Initech admin adds employees (name, email, job title, role(s), line manager, department, active). Each gets a sign-in account and an invitation. The admin can edit, deactivate and re-activate; never hard-delete someone with requests or tasks.

**Acceptance Scenarios**:
1. **Given** a new employee with a line manager, **Then** their requests route to that manager (spec 002 rules work without code changes).
2. **Given** a manager chain A → B → A, **Then** saving is refused (no cycles).
3. **Given** a duplicate email within the company, **Then** saving is refused; the same email in another company is allowed.

### User Story 2 - Organisation structure (Priority: P1)
The admin builds a tree of departments/units (e.g. Initech → Finance → Payables), sets a head for each unit and places employees in units.

**Acceptance Scenarios**:
1. **Given** a unit with a head, **Then** a new approver type "Head of my department" is available in the route designer (spec 002) and resolves to the submitter's unit head (walking up the tree if empty).
2. **Given** a unit with employees or sub-units, **When** the admin deletes it, **Then** it is refused until they are moved.

### User Story 3 - Roles catalogue (Priority: P2)
The admin manages the company's roles (HR, Finance, Payroll, ...) used by "Role" steps; built-in roles cannot be deleted while a workflow uses them.

### User Story 4 - Bulk import (Priority: P3)
Import employees from CSV with a validation report (out of PoC scope unless time allows).

## Requirements *(mandatory)*
- **FR-001**: Use ABP Identity `OrganizationUnit` (open source) for the tree and unit membership; store the unit head and the line manager on the HR side (`Employee`).
- **FR-002**: Employee create/edit MUST create/update the linked Identity user (spec 003) in the same unit of work.
- **FR-003**: All lists and pickers are tenant-filtered; the submitter rule of spec 002 still applies.
- **FR-004**: `AssignmentResolver` MUST add `department-head` and use the role catalogue for `role:{Role}`.
- **FR-005**: Changes to manager or department apply to new steps only; already-created tasks keep their candidates (recorded in the timeline).

## Success Criteria *(mandatory)*
- **SC-001**: A client admin builds a 3-level org and 6 employees, then a request routes via line manager → department head → HR with no code (demo.py step 11).

## Files likely touched
`Domain/Employee.cs` (JobTitle, DepartmentId, IsActive, roles), new `Domain/Organization.cs` (unit head, role catalogue), `Data/PocDbContext.cs`, `Data/DemoDataSeeder.cs`, `Application/AssignmentResolver.cs`, new `Application/EmployeeAppService.cs`, new `Application/OrganizationAppService.cs`, `Application/Dtos.cs`, `Controllers/PocController.cs`, `wwwroot/index.html` (Employees and Organisation screens; new approver type in the route designer), `demo.py`.
