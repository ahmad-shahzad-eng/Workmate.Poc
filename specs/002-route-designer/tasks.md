# Tasks: Client-admin approval route designer

Spec: [spec.md](./spec.md) · Plan: [plan.md](./plan.md)
Gate after each phase: stop old app → `dotnet build` → `dotnet run` → `python3 demo.py` (all pass, steps 1–8 unaffected). One CHANGELOG line per task.

## Phase 1: Setup

- [X] T001 Seed a `Finance` role with one employee per tenant (reporting to the existing manager) in `Data/DemoDataSeeder.cs` — spec Assumptions: the demo's role-step scenario needs a role beyond Employee/Manager/HR
- **Gate:** `dotnet build`; `demo.py` steps 1–8 still pass with the extra seeded people (no absolute employee-count checks broken)

## Phase 2: Foundational (blocks both P1 stories)

- [X] T002 Create `Domain/ApprovalWorkflows.cs`: `ApprovalWorkflow` (key, name, active flag, `CurrentVersionId`) and `ApprovalWorkflowVersion` (steps JSON, fallback rule, status, `ElsaDefinitionId`/`ElsaDefinitionVersion`) aggregates, both `IMultiTenant`, mirroring `FormDefinition`/`FormDefinitionVersion`; `ApprovalWorkflowSchema.Parse(stepsJson)` validates each step (title required; approver type one of `line-manager`/`manager-of-manager`/`role`/`user`; `role`/`user` type requires its value, others must not carry one; `onReject` one of `end`/`back-to-previous`, and the **first** step cannot be `back-to-previous` since there is no previous step) and rejects zero steps, naming the failing step in the message (FR-002, FR-003, US1 scenario 5)
- [X] T003 Register `Proc_ApprovalWorkflows` / `Proc_ApprovalWorkflowVersions` in `Data/PocDbContext.cs` (`ToTable` + `ConfigureByConvention()` + unique indexes `(TenantId, Key)` / `(ApprovalWorkflowId, VersionNo)`, same pattern as `FormDefinition`/`FormDefinitionVersion`)
- [X] T004 Add an optional `FallbackRule` input to `WorkflowEngine/Activities/HumanTask.cs` (unset for `LeaveApprovalWorkflow`/`HrOnlyApprovalWorkflow` — FR-011) and thread it through `Application/ProcessManager.cs` `CreateTaskAsync` so it reaches the resolver call
- **Gate:** `dotnet build`; app starts; `demo.py` steps 1–8 pass unchanged (regression check before touching shared activity/process-manager code)

## Phase 3: User Story 1 — Build an approval workflow step by step (P1)

- [X] T005 [US1] Create `WorkflowEngine/ApprovalWorkflowGraphBuilder.cs`: build a `Flowchart` of `HumanTask` nodes wired with `Connection`/`Endpoint` on the "Approved"/"Rejected" ports — "Approved" always advances to the next node (or a terminal `CompleteProcess{Approved}`); "Rejected" goes to a terminal `CompleteProcess{Rejected}` when `OnReject == end`, or back to the previous node when `back-to-previous` (no plain-object Elsa service calls here, per plan §4/§6 Principle VI)
- [X] T006 [US1] Add `Task<(string DefinitionId, int Version)> PublishDesignedWorkflowAsync(string definitionId, string name, Guid tenantId, IActivity root)` to `WorkflowEngine/IWorkflowGateway.cs`; implement in `ElsaWorkflowGateway.cs` via `IWorkflowDefinitionPublisher.NewAsync`/`PublishAsync`, setting `DefinitionId`, `Name`, `TenantId` on the draft before publishing
- [X] T007 [US1] Create `Application/ApprovalWorkflowAppService.cs` (admin-only via `CurrentEmployeeProvider.EnsureAdminAsync()` — FR-001): `GetAllAsync`, `CreateAsync` (starter empty draft, unique key per tenant), `GetDesignAsync`, `SaveDraftAsync` (parses steps via `ApprovalWorkflowSchema.Parse`, then checks every `role:`/`user:` target and the fallback rule against the current tenant's HR data — reject a `user:` whose employee is not in the tenant, FR-005), `PublishAsync` (builds the graph via T005, publishes via T006, stores the returned `ElsaDefinitionId`/`ElsaDefinitionVersion`, retires the previous version — same immutability as `FormAppService.PublishAsync`)
- [X] T008 [P] [US1] Add DTOs for workflow list/design/version/step shapes to `Application/Dtos.cs`
- [X] T009 [US1] Add workflow-design routes (list, create, get design, save draft, publish) to `Controllers/PocController.cs`, mirroring the existing forms-design routes
- [X] T010 [US1] Extend `Application/RoutingAppService.GetWorkflowsAsync` to merge `_gateway.ListPublishedAsync()` (the two global code-defined workflows) with the current tenant's own `Published` `ApprovalWorkflowVersion` rows (`DefinitionId`/`Name`) — tenant isolation here comes from the repository's ABP tenant filter, not from asking Elsa to filter (plan §6)
- [X] T011 [US1] Add a "Workflow designer" tab to `wwwroot/index.html` (admin-only, next to Form designer): ordered step list (add/reorder/remove), approver-type dropdown with the matching follow-up control (role list derived client-side from the distinct roles in the current tenant's `/users` response / searchable user list, both from the current tenant only), fallback picker (role or specific user, required to publish), on-reject control per step (disabled/hidden for "back to previous" on the first step), draft/publish actions
- **Gate:** build a 3-step workflow (one step of each kind: line manager, role, specific user) through the UI/API, publish it, and see it listed in `GET /workflows` for that tenant and absent from the other tenant's list (US1 Independent Test)

## Phase 4: User Story 2 — Run a designed workflow (P1)

- [X] T012 [US2] In `Application/AssignmentResolver.cs`: add `manager-of-manager` (submitter → manager → manager's manager) and `user:{EmployeeId}` (tenant-filtered lookup, so a cross-tenant id resolves to nobody); exclude the submitter from every rule's result, for all workflows including the two existing ones (closes a latent self-approval gap, not only a new-workflow concern); accept an optional `fallbackRule` parameter, resolved (submitter still excluded) when the primary result is empty
- [X] T013 [US2] In `Application/ProcessManager.cs` `CreateTaskAsync`: pass the workflow's fallback rule (from T004's threaded input) to the resolver call, and when the fallback was used, add a "resolved via fallback: {reason}" timeline event instead of the plain assignment detail (FR-010, US2 scenario 2)
- [X] T014 [US2] Extend `Application/RoutingAppService.ValidateAsync` so a `WorkflowDefinitionId` that is not one of the two code-defined ids must resolve to a `Published` `ApprovalWorkflowVersion` **in the current tenant**, refused with the same "not published" error otherwise — a Globex admin can never bind (or thereby start) Acme's designed workflow (US2 scenario 3, SC-002)
- [X] T015 [US2] Add `demo.py` step 9: Khalid (Acme) builds and publishes a 3-step workflow (line manager → role `Finance` → specific user), binds it to Expense claim; Sara submits and each step is approved in order, with one reject-at-step-2 check that returns the task to step 1 (`back-to-previous`); a `manager-of-manager` step on someone whose manager has no manager (e.g. Omar) resolves to nobody and falls back, recorded in the timeline (SC-004, closes HANDOVER §3 item 1); Globex cannot see, bind to, or start Acme's workflow id
- **Gate:** `demo.py` step 9 passes in both tenants; reject-to-previous-step and the fallback case both behave as specified (SC-001, SC-002, SC-004)

## Phase 5: User Story 3 — Routing conditions adapt to the chosen field (P2)

- [X] T016 [US3] Extract `FormField.ValidateValue(string value)` (number/date/select-options check) in `Domain/Forms.cs` from the existing per-type logic in `Application/SubmissionAppService.cs`'s `Validate`; update `SubmissionAppService` to call it (pure refactor, no behavior change)
- [X] T017 [US3] Use `FormField.ValidateValue` in `Application/RoutingAppService.ValidateAsync` to check `ConditionValue` against the field named by `ConditionField` on the form's latest version, in addition to the existing "field exists" check (FR-009)
- [X] T018 [US3] In `Application/RoutingAppService.GetBindingsAsync`, flag (new `IsStale` field on `ProcessBindingDto`) any existing binding whose `ConditionValue` no longer matches its field's current type/options on the form's latest version (edge case: field edited after the route was saved)
- [X] T019 [US3] In `wwwroot/index.html`'s routing section, switch the condition-value input control by the chosen field's type (dropdown for select/radio, Yes/No for checkbox, number box, date picker, text box), clearing the old value on a field change, and show the `IsStale` flag from T018
- **Gate:** manual check — picking "Leave type" shows a dropdown of Annual/Sick/Unpaid, picking "Working days" shows a number box; an invalid value (bad option, non-number) is refused by the server too, not only the UI (SC-003)

> **FR-008 note**: running requests keeping their started version is only lightly touched here (one publish per demo workflow in T015); full proof — publishing v2 of a *designed* workflow while a v1 instance is in flight — is deferred to Phase E (version pinning), per spec.md's own "(overlaps Phase E)" framing on FR-008. Not a gap in this phase's scope.

## Phase 6: Polish

- [X] T020 Update `README.md` (Workflow designer, step 9), `CHANGELOG.md`, and `HANDOVER.md` (mark Phase D done in §4, update §3 open items: item 1 closed by T015's fallback check)
- [X] T021 Final full loop: clean build (no new warnings) → `demo.py` steps 1–9 in both tenants → manual US3 check from T019's gate

## Order

T001 → T002–T004 → T005–T011 → T012–T015 → T016–T019 → T020–T021. T006 must land before T007 can call it; T010 must land before T011's UI has anything tenant-scoped to list. T016–T019 (US3) have no dependency on T005–T015 (US1/US2) and could run in parallel with them once Phase 2 passes.
