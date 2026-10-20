# Changelog

Changes made while getting the PoC to build, start and pass the README demo.

- `WorkflowEngine/Activities/HumanTask.cs`: added `using Elsa.Workflows.Activities.Flowchart.Attributes;` so `[FlowNode]` resolves (the earlier fix was not in the file).
- `Workmate.Poc.csproj`: target `net10.0` instead of `net9.0` to match the installed SDK (the earlier change was not in the file).
- `WorkmatePocModule.cs`: added `using Volo.Abp.MultiTenancy.ConfigurationStore;`, the namespace of `AbpDefaultTenantStoreOptions` in ABP 9.x.
- `demo.py` (new): stdlib-only script that runs README demo steps 1-6 against the API and writes `demo.log`.
- `demo.py`: step 4 checks version numbers relative to the current version, so it also passes when versions were published in the UI before the run.

## Phase B: form creation, routing UI, admins, employee forms

- Display name is now **Workmate HRM** (UI title/header, README). Code namespaces stay `Workmate.Poc`.
- `Domain/Employee.cs`: `EmployeeRoles` constants, including the new `Admin` role.
- `Application/CurrentEmployeeProvider.cs`: `EnsureAdminAsync()`, the PoC stand-in for an ABP permission check.
- `Application/FormAppService.cs`: `CreateAsync` (key rules, unique key, starter v1 draft) and `GetAllAsync`; all designer writes are Admin-only (HR can no longer edit forms).
- `Application/RoutingAppService.cs` (new): list workflows, list/create/update/delete `ProcessBinding`s with validation (published workflow, both-or-neither condition, field exists on the form). Admin-only.
- `WorkflowEngine/IWorkflowGateway.cs`, `ElsaWorkflowGateway.cs`: `ListPublishedAsync()` via Elsa's `IWorkflowDefinitionStore`; Elsa calls stay in the gateway (rule 6).
- `WorkflowEngine/Workflows/*.cs`: display names "Line manager, then HR" and "HR only"; HR-only task title is now generic ("HR review") since it serves several forms. Definition ids unchanged.
- `Application/SubmissionAppService.cs`: unpublished forms cannot be submitted (was a null dereference); HR role check uses the constant.
- `Controllers/PocController.cs`: routes `GET forms/all`, `POST forms`, `GET workflows`, `GET|POST forms/{key}/bindings`, `PUT|DELETE bindings/{id}`.
- `Data/DemoDataSeeder.cs`: an Admin per tenant (Khalid, Noor); new forms work-from-home, expense-claim (manager then HR) and letter-request (HR only) in both tenants.
- `wwwroot/index.html`: request-type picker on New request; Form designer (Admin only) with form list, create form, visual field editor (add/reorder/remove, type, options, required/reportable/workflow flags) plus JSON tab, live preview, and a Routing section; `[hidden]` CSS fix.
- `demo.py`: steps 4 and 5 updated for the Admin role; new step 7 covers admin-only access, form creation, publishing, routing validation and conditional routing.
- `README.md`: name, people table, .NET 10, step 7.
- `demo.py`: step 7 submissions now use the `business-trip` form (the helper always sent `leave-request`); refusal checks now match the expected error message, not just any 4xx.

### Admin is a permission, not a job role (fix: a request from the Admin never reached anyone)

- Cause: the Admin users had job role "Admin" and no manager, so the "line-manager" rule matched nobody and the task was stuck.
- `Domain/Employee.cs`: removed the `Admin` job role; added `IsAdmin` (company admin flag, stand-in for an ABP permission).
- `Domain/DemoTenants.cs`: vendor admin "Aramis Support" (host-side user, not an employee of any company).
- `Application/CurrentEmployeeProvider.cs`: `EnsureAdminAsync` allows the vendor admin or an employee with `IsAdmin`.
- `Data/DemoDataSeeder.cs`: Khalid and Noor are employees reporting to Omar and Fatima, with `IsAdmin`.
- `Application/Dtos.cs`, `Controllers/PocController.cs`: users list returns `IsAdmin` and includes the vendor admin.
- `wwwroot/index.html`: designer visibility uses `isAdmin`; user picker shows "+ admin".
- `demo.py`: 7o company admin's own request reaches their manager; 7p vendor admin creates a form for Globex; 7q vendor admin cannot submit.
- `README.md`: people table and the two kinds of admin.
- `HANDOVER.md` (new): handover for the next session (state, plan, decisions, rules, spec-kit steps).
- `.specify/memory/constitution.md`: filled the Spec Kit constitution from HANDOVER.md §5–§6 (v1.0.0).
- `specs/001-elsa-persistence/spec.md` (new): Spec Kit spec for Phase C, Elsa persistence across restarts.
- `.gitignore`: ignore `*.log` (housekeeping item 3). Run `git rm --cached build.log demo.log run.log` once to untrack them.
- `specs/001-elsa-persistence/plan.md`, `tasks.md` (new): plan and 15 tasks for Phase C; Elsa.EntityFrameworkCore.Sqlite 3.7.0 approved.

## Phase C: Elsa persistence across restarts

- `Workmate.Poc.csproj`: added `Elsa.Persistence.EFCore.Sqlite` 3.7.0, not `Elsa.EntityFrameworkCore.Sqlite` as planned — that package id was retired in Elsa's 3.6 line; `Elsa.Persistence.EFCore.Sqlite` is its replacement, same 3.7.0 version as core `Elsa`.
- `appsettings.json`: added `ConnectionStrings:Elsa` (`Data Source=workmate-elsa.db`); `Poc:ResetDatabaseOnStartup` now defaults to `false`.
- `Data/PocDatabaseReset.cs` (new): deletes both SQLite files (+ `-shm`/`-wal`) when reset is on; called first in `ConfigureServices`, before Elsa's migrations or the seeder can open either file.
- `WorkmatePocModule.cs`: Elsa's workflow management + runtime now use EF Core SQLite persistence (`workmate-elsa.db`); calls `WorkflowConsistencyCheck` after seeding.
- `Data/DemoDataSeeder.cs`: dropped `EnsureDeletedAsync` and the reset flag check (moved to `PocDatabaseReset`); keeps only `EnsureCreatedAsync`.
- `WorkflowEngine/IWorkflowGateway.cs`, `ElsaWorkflowGateway.cs`: added `InstanceExistsAsync`. It catches workflow-store query failures (e.g. a missing/mismatched file) and treats them as "not found" rather than crashing — found by testing the mismatch edge case (T013): an unhandled `SqliteException` at startup when `workmate-elsa.db` was deleted but `Proc_Instances` still had `Running` rows.
- `Application/TaskAppService.cs`: `CompleteAsync` checks `InstanceExistsAsync` before completing; throws a `UserFriendlyException` (task stays pending) if the workflow instance is gone.
- `Application/WorkflowConsistencyCheck.cs` (new): after seeding, warns once per demo tenant for each `Running` process instance whose workflow instance is missing; never throws.
- `demo.py`: `restart-before` / `restart-after` modes add step 8 (8a-8e: resume at the same step in both tenants, reject path, tenant isolation, unchanged per-tenant counts across a manual restart). Also fixed two bugs surfaced by running steps 1-7 repeatedly on a non-empty database (required for FR-008): `annual`/`sick` now include a harmless default `handoverTo` so they stay valid once a prior run's step 4 has required it; step 7's form keys are suffixed with a per-run id so they don't collide with a prior run's forms; step 5b's own-requests check is now relative to its own before/after count instead of assuming zero prior requests.
- `.gitignore`: ignore `demo_restart.json`.
- Verified: fresh DB (reset on) → steps 1-7 (36/36); reset off → steps 1-7 three times in a row on the same database (36/36 each); step 8 around a real restart (11/11); the mismatch path (delete `workmate-elsa.db` only) logs a startup warning per affected process and fails cleanly on approve, without crashing the app.
- `specs/002-route-designer/spec.md` (new): Phase D option B spec (approver type per step; routing value input by field type).
- `.specify/memory/constitution.md`: v1.1.0, Principle III allows admin-picked specific users with tenant check and fallback.
- `HANDOVER.md`: Phase D decided (option B, SaaS reasons), design rule 3 updated, open decisions refreshed.
- `specs/002-route-designer/spec.md`: clarified (fallback chosen per workflow; submitter never an approver of own request).
- `specs/003-login-onboarding`, `specs/004-employees-org`, `specs/005-email-notifications` (new): seeded specs for sign-in/onboarding, employees and org structure, email notifications with templates.
- `HANDOVER.md`: added phases H (sign-in/onboarding), I (employees/org), J (emails) and the new order.
- `specs/006-surveyjs-renderer/spec.md` (new): switchable renderer (native or SurveyJS Form Library); HANDOVER phase K added.
- `specs/006-surveyjs-renderer/spec.md`: conditional fields, help text and pages moved into PoC scope (P2), with server-side visibility rules.

## Phase D: client-admin approval route designer (option B)

- `Domain/ApprovalWorkflows.cs` (new): `ApprovalWorkflow`/`ApprovalWorkflowVersion` (tenant-owned, draft/published, mirrors `FormDefinition`); `ApprovalWorkflowSchema.Parse` validates steps (title, approver type, role/user presence, on-reject, and rejects `back-to-previous` on the first step — no previous step exists); `FallbackRules.Parse` validates the required fallback rule.
- `Data/PocDbContext.cs`: `Proc_ApprovalWorkflows` / `Proc_ApprovalWorkflowVersions`, same pattern as Forms.
- `Domain/Employee.cs`, `Data/DemoDataSeeder.cs`: new `Finance` role, one employee per tenant (Layla/Yara), needed for the role-step scenario.
- `Application/AssignmentResolver.cs`: added `manager-of-manager` and `user:{EmployeeId}` (tenant-filtered lookup); **the submitter is now excluded from every rule's candidates, for all workflows including the two existing ones** (closes a latent self-approval gap, not only a new-workflow concern); accepts an optional fallback rule, resolved when the primary result is empty.
- `WorkflowEngine/Activities/HumanTask.cs`, `Application/ProcessManager.cs`: optional `FallbackRule` input/parameter, unset for the two code-defined workflows (unchanged behaviour); records "resolved via fallback" in the timeline when used.
- `WorkflowEngine/ApprovalWorkflowGraphBuilder.cs` (new): turns steps into a plain `Flowchart` graph (`HumanTask` nodes wired on their "Approved"/"Rejected" ports, back-edge for `back-to-previous`, terminal `CompleteProcess` nodes). No Elsa service calls — only the gateway persists anything.
- `WorkflowEngine/IWorkflowGateway.cs`, `ElsaWorkflowGateway.cs`: `PublishDesignedWorkflowAsync` via `IWorkflowDefinitionPublisher`. Three real issues found by actually running a published graph (not just building it), fixed before this could work at all:
  1. **Missing `Container.Activities` registration** — reflection research during planning used `DeclaredOnly` and missed this inherited collection; without every node registered there (not just reachable via `Start`/`Connections`), Elsa's own flowchart deserializer threw `KeyNotFoundException` on publish.
  2. **No entry point after fixing #1** — `Flowchart.Start` is not part of the persisted JSON at all (confirmed by inspecting a published definition's stored JSON), and a `back-to-previous` edge can leave every node with an incoming connection, so there is no structural "root" to infer; the workflow silently completed instantly via the first activity in the list instead of the first step. Fixed by setting `CanStartWorkflow = true` on the first node explicitly.
  3. **Resume failed after the first suspension** (`WorkflowExecutionContext: Could not find activity execution context ... for bookmark ...`) — a hand-built graph never gets a `NodeId` (only reachable via `IIdentityGraphService.AssignIdentitiesAsync`, normally run implicitly for code-defined workflows registered through `AddWorkflowsFrom`); the gateway now calls it explicitly before publishing.
  4. Separately: `ListPublishedAsync()` returns every published Elsa definition, code-defined or designed (same store) — `RoutingAppService` was filtering by it naively, which both duplicated entries in the routing workflow list and, more seriously, let the tenant-isolation check in `ValidateAsync` treat *any* definition id (including another tenant's designed workflow) as a trusted "global" one, skipping the ownership check entirely. Fixed by checking against the two known code-defined ids directly rather than the gateway's full list.
- `Application/ApprovalWorkflowAppService.cs` (new): create/draft/publish, admin-only; validates `role:`/`user:` step targets and the fallback against the current tenant's HR data.
- `Application/RoutingAppService.cs`: `GetWorkflowsAsync` merges the two code-defined workflows with the tenant's own published designed ones (tenant isolation from the repository's ABP filter, not from Elsa); `ValidateAsync` requires a non-code-defined id to be a *published designed workflow in the current tenant*; `ConditionValue` is now checked against the field's type/options (`FormField.ValidateValue`, extracted from `SubmissionAppService`); `GetBindingsAsync` flags (`IsStale`) a binding whose condition no longer matches the form's latest version.
- `Application/Dtos.cs`, `Controllers/PocController.cs`: workflow-design DTOs and `GET/POST /workflows/design...` routes; `ProcessBindingDto` gained `IsStale`.
- `wwwroot/index.html`: "Workflow designer" tab (admin-only) — step editor (approver type + conditional role/user picker, on-reject, fallback), draft/publish; routing's condition-value input now switches control by field type (dropdown/Yes-no/number/date/text) and shows the stale flag.
- `demo.py`: step 9 — builds/publishes/binds/runs a 3-step workflow (line manager → role → specific user) with one reject-to-previous-step, and a separate fallback check (a `manager-of-manager` rule resolving to nobody, closing HANDOVER §3 item 1); confirms Globex can neither see, bind, nor start Acme's designed workflow.
- `specs/002-route-designer/spec.md`: trimmed the "active"/"left" employee wording (no such concept exists in this PoC — `Employee` has no active/deleted field); the fallback scenario is demonstrated via a rule that naturally resolves to nobody instead. Noted `ApprovalStep` is a JSON value, not a table.
- Verified: fresh DB (reset on) → steps 1-7 and 9 (52/52); reset off → same again on a non-empty database (52/52); step 8 restart check still passes unchanged (regression check on the shared `AssignmentResolver`/`ProcessManager` code path).

## Phase K: switchable form renderer (native or SurveyJS Form Library)

- `wwwroot/lib/surveyjs/` (new): vendored SurveyJS 3.2.1 (`survey-core`/`survey-js-ui`, MIT, confirmed on the npm registry and jsdelivr's package listing) as static `.min.js`/`.min.css` files, no CDN at runtime; `LICENSE.txt` notes only the Form Library is used, not Creator/Dashboard/PDF Generator (constitution-forbidden).
- `Domain/Forms.cs`: `FormRenderer` constants (`default`/`native`/`surveyjs`); `FormDefinition.Renderer`; `FormField.VisibleIf` (`VisibleIfRule{Field, Equals}` — a `Value` property under the `equals` JSON name, to avoid shadowing `object.Equals`), `Description`, `Placeholder`, `Page`; new `CompanyFormSettings` entity (tenant's default renderer); `FormSchema.Parse` rejects a `visibleIf` naming a field that doesn't exist or isn't earlier in the field list.
- `Data/PocDbContext.cs`: `CompanyFormSettings` → `Forms_CompanySettings`, unique index on `TenantId`. Adds a `Renderer` column to the existing `Forms_Definitions` table — **`Poc:ResetDatabaseOnStartup=true` required once** (`EnsureCreatedAsync` does not migrate an existing database).
- `wwwroot/js/renderers.js` (new): `createRenderer(kind, schema, values, readOnly) -> {mount, getData, validate}` — one contract for both renderers. `NativeRenderer` is the extracted `renderForm`/`readForm` logic, rewritten to be container-agnostic (`querySelector`/`querySelectorAll("[name]")` instead of assuming an HTML `<form>` element). `SurveyJsRenderer` adapts our schema to SurveyJS JSON (fields, pages, `visibleIf` expressions), mounts via `new Survey.Model(json).render(container)`, and converts SurveyJS's booleans/numbers/dates back to our string formats on `getData()`; pagination uses `showNavigationButtons: false` plus custom Previous/Next buttons calling `survey.nextPage()`/`.prevPage()`/`.currentPage.validate()`, so the same outer submit button and `validate()`/`getData()` contract work regardless of page count.
- `Application/Dtos.cs`, `FormAppService.cs`, `SubmissionAppService.cs`: `EffectiveRenderer` (form-level `default` cascades to the tenant's `CompanyFormSettings.DefaultRenderer`, defaulting to `native`); `SetRendererAsync`, `GetCompanyRendererAsync`/`SetCompanyRendererAsync` (admin-only).
- `Controllers/PocController.cs`: `PUT forms/{key}/renderer`, `GET|PUT company-settings/renderer`.
- `Application/SubmissionAppService.cs`'s `Validate`: single forward pass over fields, tracking accepted values; a field whose `visibleIf` doesn't match what's been accepted so far is skipped — never required, and any value submitted for it is dropped before storage (same case-insensitive comparison as `ProcessBinding.Matches`).
- `wwwroot/index.html`: vendored SurveyJS `<script>`/`<link>` tags plus `renderers.js`; top-bar renderer demo toggle and RTL checkbox (both `localStorage`/this-browser-only, no effect on stored data); form designer renderer picker, company-settings renderer control, and a "preview in the other renderer" button; field editor gained Visible-only-when (reusing Phase D's `conditionValueInput` verbatim), Description, Placeholder and Page columns; `viewNew`/`showDetail` now mount through `createRenderer(...)` instead of building/reading raw HTML strings.
- `Data/DemoDataSeeder.cs`: Acme's company default renderer is `surveyjs`, Globex's is `native` (also exercises tenant isolation on the new setting); Acme's Leave request gets a v2 with `medicalNoteReference` (required, `visibleIf: leaveType = Sick`) split across two pages ("Details"/"Notes"). Found and fixed a real bug here: re-fetching the just-inserted v1 by id (`_versions.GetAsync`) raced ABP's repository `InsertAsync`, which does not auto-save by default — the row wasn't in SQLite yet, so the query threw `EntityNotFoundException` at every startup. Fixed by inserting v1 with `autoSave: true`.
- `demo.py`: `sick` payload gained a harmless `medicalNoteReference` (same precedent as `handoverTo` in Phase C) so it stays valid against Acme's new v2 schema in every step that reuses it; new step 10 — malformed submissions (missing required field, unknown select option, non-number) are refused with field-level messages regardless of renderer (US3), and against Acme's v2 schema: Annual leave submits without the hidden field, a value sent for it while hidden is dropped and not stored, and Sick leave with no value is refused (US4, API-level; live hide/show, page-gated Next, and read-only hidden-field display are UI/manual, not automated here).
- Verified: fresh DB (reset on) → steps 1-7, 9-10 (59/59); a manual app restart (reset off) → step 8 restart check (3/3 before, 11/11 after, including a request submitted against the v2 schema before the restart). RTL and live SurveyJS rendering (hide/show, page gating) were checked by code inspection only — no browser tool was available this session to visually confirm them.
- `README.md`: setup guide for macOS and Windows (software, project setup, first run, checks, troubleshooting); "If it does not compile" renamed "Upgrading packages".
- `docs/ARCHITECTURE.md` (new): Mermaid diagrams of components by source (ABP, Elsa, SurveyJS, ours), submit/approve sequence, admin design flow, multi-tenancy notes.
- `docs/architecture.html` (new): standalone offline HTML of the architecture diagrams (pre-rendered SVG) + setup guide; component diagram in `docs/ARCHITECTURE.md` redrawn as stacked layers.
