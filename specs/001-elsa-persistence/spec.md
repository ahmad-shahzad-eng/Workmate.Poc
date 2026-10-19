# Feature Specification: Persist workflow state across restarts

**Feature Branch**: `001-elsa-persistence`

**Created**: 2026-10-20

**Status**: Clarified

**Input**: HANDOVER.md §4 Phase C: "Persist Elsa workflow state with EF Core so a restart mid-approval resumes correctly; turn off DB reset on startup."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Approval survives a restart (Priority: P1)

Sara (Acme) sends an Annual leave request. Before Omar approves it, the app is stopped and started again. Omar still sees the task in his Inbox, approves it, Huda then gets her task, approves it, and Sara sees Approved with the full timeline.

**Why this priority**: This is the point of Phase C and the prerequisite for Phase D (a workflow designer is useless if drawn workflows and running requests vanish on restart).

**Independent Test**: Run the "before restart" demo step, restart the app, run the "after restart" demo step; all checks pass.

**Acceptance Scenarios**:

1. **Given** a leave request waiting at the line-manager step, **When** the app restarts and Omar approves, **Then** the workflow resumes at the same step and the next task goes to HR.
2. **Given** a request waiting at the HR step, **When** the app restarts and HR approves, **Then** the process completes and the submission shows Approved.
3. **Given** a request waiting at any step, **When** the app restarts and the approver rejects, **Then** the workflow ends on the reject path with no further tasks.
4. **Given** pending requests in Acme and Globex, **When** the app restarts, **Then** each tenant still sees only its own requests and tasks, and resuming one tenant's request never touches the other's.

---

### User Story 2 - Data stays across restarts (Priority: P2)

Admins' forms, published versions, routes, and all submissions, tasks and timelines remain after a restart. The demo seed runs only into an empty database and never duplicates people or forms.

**Why this priority**: Turning off the startup reset is required for Story 1, and must not break seeding or the existing demo.

**Independent Test**: Start the app twice without reset; run `demo.py` steps 1–7; all pass, and employee/form counts per tenant are unchanged after the second start.

**Acceptance Scenarios**:

1. **Given** a database from a previous run, **When** the app starts, **Then** no seed data is duplicated and existing records are untouched.
2. **Given** a database from a previous run, **When** `demo.py` steps 1–7 run, **Then** they all pass (checks must not assume an empty database).
3. **Given** reset is switched back on in configuration, **When** the app starts, **Then** it starts from a clean, seeded state (kept for clean demo runs).

### Edge Cases

- App stops between submit commit and workflow start (the after-commit hook never runs): out of scope here; Phase F (outbox) covers it. Record it as a known gap.
- A task completion arrives for a workflow instance that no longer exists in the workflow store: the action fails with a clear message and the domain task is not marked done.
- Workflow definitions are code-defined: after a restart they must resolve to the same definition and version as the running instance.
- The workflow store and the domain store disagree (e.g. one file deleted): detected and reported at startup or on access, not silently ignored. The app logs a warning and keeps running; acting on an affected task fails with a clear message (decided 2026-10-20).

## Clarifications

### Session 2026-10-20

- Q: Where does workflow state live? → A: Separate SQLite file `workmate-elsa.db`.
- Q: Store mismatch handling? → A: Log a warning; affected task fails clearly when acted on.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Workflow instances, bookmarks and definitions MUST be stored durably (EF Core persistence) instead of in memory.
- **FR-002**: A workflow waiting at a human step MUST resume after an app restart when its task is completed, at the same step and version.
- **FR-003**: `Poc:ResetDatabaseOnStartup` MUST default to `false`; setting it to `true` MUST reset both the domain data and the workflow data together, so they never get out of step.
- **FR-004**: Seeding MUST only add data to an empty tenant and MUST be safe to run on every start.
- **FR-005**: All new calls to the workflow engine's stores or runtime MUST stay inside `IWorkflowGateway` / `ElsaWorkflowGateway`; persistence setup lives only in module configuration (Principle VI).
- **FR-006**: Tenant id MUST still be carried in workflow input and re-applied in activities after a resume (Principle IV).
- **FR-007**: Domain records remain the system of record; the inbox and My requests MUST keep reading from domain tables, not workflow state (Principle VI).
- **FR-008**: `demo.py` MUST gain a restart check (step 8) that can be run in two parts around a manual restart, saving the ids it needs between the parts, and MUST keep steps 1–7 passing on a non-empty database.
- **FR-009**: Domain data and workflow data MUST be stored in separate SQLite files: domain data in `workmate-poc.db`, workflow data in `workmate-elsa.db` (decided 2026-10-20; mirrors production's own schema for Elsa and keeps `EnsureCreated` for domain tables unaffected). Reset deletes both.

### Key Entities

- **Workflow instance (engine)**: Elsa's record of a running workflow, its state and bookmarks; owned by the engine, reached only through the gateway.
- **Process instance / Task (domain)**: existing records in `Proc_` tables that link a submission to a workflow instance id; unchanged by this feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `demo.py` step 8 passes: a request pending at the manager step before a restart completes through HR after it, in both tenants.
- **SC-002**: `demo.py` steps 1–7 pass on a fresh database and again on the same database after a restart.
- **SC-003**: Two consecutive starts without reset leave per-tenant employee and form counts unchanged.
- **SC-004**: Clean build with no new warnings; no new package outside Elsa 3.7.x / ABP 9.1.0 open source.

## Assumptions

- Persistence uses Elsa's own open-source EF Core SQLite package on 3.7.x; no ABP or Elsa version change.
- Domain tables keep using `EnsureCreated` for the PoC (no ABP migrations yet); production uses migrations on SQL Server.
- Workflows stay code-defined; Elsa Studio and Elsa multi-tenancy are Phase D.
- Reliable start (outbox) is Phase F; the submit-then-crash gap is accepted for now.
- The restart in step 8 is done by hand (Ahmad stops and starts `dotnet run`), matching the current build/run loop.
