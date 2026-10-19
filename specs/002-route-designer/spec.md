# Feature Specification: Client-admin approval route designer (Phase D, option B)

**Feature Branch**: `002-route-designer`

**Created**: 2026-10-20

**Status**: Clarified

**Input**: HANDOVER.md §4 Phase D, option B (decided 2026-10-20 for the SaaS product): company admins build approval workflows in our own designer; the gateway turns each one into a standard Elsa definition. Each step asks who approves: line manager, a role (then pick the role), a specific user (then pick the user), and so on. In routing, the value input of every condition adapts to the chosen field.

## Decision context

Workmate HRM is sold as multi-tenant SaaS. Client HR admins design workflows, so the designer must be simple, safe on shared servers (no code or expressions from clients), isolated per tenant under ABP's filter, and stable across Elsa upgrades (our route model is the contract; Elsa definitions are generated from it). Elsa Studio is not exposed to clients; it may be added later for Aramis staff only, behind vendor sign-in.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Build an approval workflow step by step (Priority: P1)

Khalid (Acme company admin) creates a workflow "Manager, Finance, then HR". For each step he enters a title and answers "Who approves this step?":

| Approver type | Then the designer asks | Resolved at run time to |
| --- | --- | --- |
| Line manager | nothing more | submitter's manager |
| Manager's manager | nothing more | the manager's manager |
| Role | **Which role?** (list of the company's roles) | everyone with that role (any one of them can act) |
| Specific user | **Which user?** (searchable list of the company's active employees) | that person |

He also picks the workflow's **fallback approver** (a role or a specific user, used when a step finds nobody) and what happens on reject (end the request, or send it back to the previous step), saves a draft, and publishes it.

**Why this priority**: This is the core of Phase D: a client admin creates a dynamic workflow with no code.

**Independent Test**: Build a 3-step workflow with one step of each kind (line manager, role, specific user), publish it, and see it listed as a published workflow in routing.

**Acceptance Scenarios**:

1. **Given** the step editor, **When** Khalid picks "Role", **Then** a role list appears and the step cannot be saved without a role.
2. **Given** the step editor, **When** he picks "Specific user", **Then** a list of Acme's active employees appears (never Globex's), and the step cannot be saved without a user.
3. **Given** he switches a step from "Role" to "Line manager", **Then** the role choice is cleared and no further question is shown.
4. **Given** a valid draft, **When** he publishes, **Then** a new version is created and the previous version stays unchanged for requests already running.
5. **Given** a draft with zero steps or an incomplete step, **When** he publishes, **Then** publishing is refused with a message naming the step.

---

### User Story 2 - Run a designed workflow (Priority: P1)

Khalid binds the new workflow to Expense claim in routing. Sara submits a claim; it goes to Omar (line manager), then to anyone in Finance (role), then to the chosen user, then completes as Approved. A reject at step 2 with "back to previous step" returns the task to Omar.

**Why this priority**: A designer that cannot run end to end proves nothing.

**Independent Test**: `demo.py` step 9 builds, binds and runs the workflow through every step, plus one reject path.

**Acceptance Scenarios**:

1. **Given** the workflow above, **When** Sara submits, **Then** each step creates a task for the people the rule resolves to, in order, and the timeline shows each step.
2. **Given** a step with "Specific user", **When** that user has left or is inactive at run time, **Then** the task goes to the fallback (see FR-010) and the timeline says why.
3. **Given** an Acme workflow, **When** a Globex admin lists workflows or a Globex user submits, **Then** Acme's workflow is neither visible nor startable.

---

### User Story 3 - Routing conditions adapt to the chosen field (Priority: P2)

In Routing, when the admin picks the field in "When field ...", the value input changes to suit that field:

| Field type | Value input |
| --- | --- |
| select / radio | dropdown of that field's options |
| checkbox | Yes / No |
| number | number box |
| date | date picker |
| text / textarea | text box |

**Why this priority**: Free-text values cause silent mismatches (for example "sick" vs "Sick", or an option that does not exist).

**Independent Test**: In Routing for Leave request, pick "Leave type": a dropdown with Annual, Sick, Unpaid appears. Pick "Working days": a number box appears.

**Acceptance Scenarios**:

1. **Given** the field is changed, **Then** the old value is cleared and the new input type is shown.
2. **Given** a select field, **Then** only its current options can be chosen, and the saved route stores the option exactly as defined.
3. **Given** the API receives a value that is not valid for the field (an unknown option, a non-number for a number field), **Then** it rejects the route with a clear message (the server checks too, not only the UI).
4. **Given** an existing route whose value no longer matches the form's latest version (option removed), **Then** the routing list flags it.

### Edge Cases

- Role with no members at run time: the step goes to the fallback (FR-010); never stuck silently.
- The submitter is never an approver of their own request: they are removed from every step's candidates at run time. If that leaves nobody, the step goes to the workflow's fallback (decided 2026-10-20).
- Line manager / manager's manager missing (e.g. Omar has no manager): same fallback; this closes open item 1 in HANDOVER.md §3.
- A published workflow is in use by routes: it can get new versions but cannot be deleted; it can be deactivated.
- Editing the form after a route was saved: see US3 scenario 4.

## Clarifications

### Session 2026-10-20

- Q: Fallback when a step finds nobody? → A: Chosen per workflow (role or specific user), required to publish.
- Q: Submitter as approver? → A: Never; the submitter is excluded from candidates of their own request.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Workflow design MUST be admin-only (company admin for their company; vendor admin for any company), per Principle VII.
- **FR-002**: A workflow MUST be an ordered list of steps; each step has a title, an approver rule and an on-reject action (end, or back to previous step).
- **FR-003**: Approver rule types MUST be: line manager, manager's manager, role (+ role), specific user (+ user). The UI asks the follow-up question only for the type that needs it.
- **FR-004**: Approver rules MUST be stored as data (`line-manager`, `manager-of-manager`, `role:{Role}`, `user:{EmployeeId}`) and resolved at run time by `AssignmentResolver` only (Principle III, amended in constitution v1.1.0).
- **FR-005**: Role and user lists MUST come from the current tenant only, and the submitter MUST never appear among the candidates for their own request; the server MUST reject a `user:` rule whose employee is not in the tenant.
- **FR-006**: Workflows MUST be stored in our own tenant-filtered tables with draft and published versions; published versions are immutable.
- **FR-007**: Publishing MUST generate and publish a standard Elsa workflow definition through `IWorkflowGateway` only (Principle VI), built from the existing `HumanTask` and `CompleteProcess` activities. No client-supplied expressions or code.
- **FR-008**: Running requests MUST keep the version they started with (overlaps Phase E).
- **FR-009**: Routing condition values MUST be validated against the field type and options of the form's latest published version, in the UI and on the server.
- **FR-010**: When an approver rule resolves to nobody (or only to the submitter), the step MUST go to the fallback approver chosen for that workflow and record the reason in the timeline. Each workflow MUST have a fallback (role or specific user; not line manager / manager's manager, which can themselves be missing); publishing is refused without one (decided 2026-10-20).
- **FR-011**: The two code-defined workflows (`leave-approval`, `hr-only-approval`) MUST keep working; they can later be re-created in the designer.

### Key Entities

- **ApprovalWorkflow**: tenant-owned; key, name, active flag; has versions.
- **ApprovalWorkflowVersion**: version number, fallback approver rule, status (draft/published), ordered steps, the Elsa definition id/version it generated.
- **ApprovalStep**: order, title, approver rule (type + role or employee id), on-reject action.
- **ProcessBinding** (existing): condition field/value now type-checked; points at a published workflow.

## Success Criteria *(mandatory)*

- **SC-001**: A company admin builds, publishes, binds and runs a 3-step workflow with line manager, role and specific-user steps without touching code (demo.py step 9).
- **SC-002**: No workflow, role list or user list from one company is visible to another (demo.py check).
- **SC-003**: Every routing value input matches its field type for the seeded forms (manual check + server rejects invalid values).
- **SC-004**: A step whose rule resolves to nobody never leaves a request stuck.

## Assumptions

- Roles are the existing employee roles (Employee, Manager, HR) plus any added to demo data (e.g. Finance); a role catalogue per tenant is production work.
- Parallel approval, timers/escalation, conditions inside a workflow and "next approver chosen by the current approver" are out of scope for the PoC; record them as follow-ups.
- Depends on Phase C (persistent workflow store).
