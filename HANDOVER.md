# Handover: Workmate HRM — Forms & Workflow PoC

Read this first. It says what the project is, where it stands, what is planned next, and how to continue with spec-kit.
Then read `README.md` (how to run, demo steps) and `CHANGELOG.md` (every change so far, one line each).

## 1. Who and why

- **Owner:** Ahmad, architect on the Workmate HCM platform. He reviews every change: keep changes small, explained, easy to review. Be efficient; avoid long exploratory runs; report briefly.
- **Goal:** prove that a new .NET project on **ABP Framework + Elsa Workflows 3 + a JSON-schema form layer** delivers dynamic forms, dynamic approval workflows and form-to-workflow assignment, before the team commits to the full build.
- **Product name:** **Workmate HRM** (display name). Code namespaces stay `Workmate.Poc`.
- **Design doc:** https://claude.ai/code/artifact/7f176669-80cc-4e01-b05f-d2e8daa3d871 (architecture, data model, runtime flow, design rules, PoC criteria). If code and doc disagree, follow the doc's design rules and tell Ahmad.

## 2. Environment

- Project folder: `~/Downloads/Workmate.Poc` on Ahmad's Mac (Apple Silicon), .NET SDK 10.0.402.
- `net10.0`, ABP 9.1.0 (open-source packages only), Elsa 3.7.0, SQLite file `workmate-poc.db` recreated on every start (`Poc:ResetDatabaseOnStartup`).
- App: `dotnet run --urls http://0.0.0.0:5080`, UI at http://localhost:5080.
- **Build/test loop:** Claude's sandboxes cannot reach nuget.org or the .NET downloads (org allowlist), so Ahmad runs the commands and Claude reads the logs from the folder:
  ```bash
  lsof -ti :5080 | xargs kill            # always stop the old app first (port clash caused a false failure once)
  cd ~/Downloads/Workmate.Poc && dotnet build 2>&1 | tee build.log
  dotnet run --urls http://0.0.0.0:5080 2>&1 | tee run.log
  python3 demo.py                         # second window; writes demo.log (steps 1-7)
  ```
  To remove this round-trip, an org Owner can allowlist `dot.net`, `builds.dotnet.microsoft.com`, `dotnetcli.azureedge.net`, `dotnetcli.blob.core.windows.net`, `nuget.org`, `www.nuget.org`, `api.nuget.org`, `globalcdn.nuget.org` (claude.ai → Admin settings → Capabilities).

## 3. Current state

### Done and verified (demo.py, last full run)
- Clean build (3 compile fixes, see CHANGELOG).
- Clean start; both tenants seeded.
- README demo steps 1–6 all pass: happy path, conditional routing, reject path, form versioning, tenant isolation, typed reporting values.
- No runtime fixes were needed: Elsa gateway, bookmarks/resume, after-commit start and tenant isolation worked first time.

### Done and verified (demo.py steps 1–7: 72 PASS, 0 FAIL)
**Phase B — form creation, routing UI, admins, employee forms**
- Admin-only form designer: create form, visual field editor + JSON tab, live preview, publish versions.
- Routing UI over `ProcessBinding`: default route + "field equals value" routes, each bound to a published workflow (listed through `IWorkflowGateway.ListPublishedAsync`).
- Seeded employee forms in both tenants: leave request, work from home, expense claim (manager → HR), letter request (HR only).
- Request-type picker on New request; unpublished forms cannot be submitted.
- **Admin is a permission, not a job role** (fix for "admin's request reached nobody"):
  - *Company admin* = employee with `IsAdmin` (Khalid → reports to Omar; Noor → reports to Fatima). Designs forms and still submits/approves normally.
  - *Vendor admin* = "Aramis Support" (`DemoTenants.VendorAdminId`), host-side user of the vendor; designs for any company, cannot submit or approve.
  - Check lives in `CurrentEmployeeProvider.EnsureAdminAsync()` (PoC stand-in for an ABP permission such as `Workmate.Forms.Design`).
- `demo.py` step 7 (7a–7q) covers all of the above. The previous step-7 run was invalid (an old app instance held port 5080). Re-run on 2026-10-20: all steps pass.

### Demo people
Tenants no longer have fixed ids (Phase H — `Tenant`'s constructor is ABP-internal, so `TenantManager.CreateAsync`
always generates one); look them up by name (`GET /api/poc/tenants`) or sign in with the emails below.

| Company | People |
| --- | --- |
| Acme Trading | Sara (employee → Omar), Omar (manager), Huda (HR), Layla (Finance → Omar), Khalid (employee + `Workmate.CompanyAdmin` → Omar) |
| Globex Logistics | Ali (employee → Fatima), Fatima (manager), Yousef (HR), Yara (Finance → Fatima), Noor (employee + `Workmate.CompanyAdmin` → Fatima) |
| Vendor | Aramis Support (`aramis@workmate.vendor`, host user, any company) |

### Open items (awaiting Ahmad's decision unless noted)
1. ~~**No-manager fallback:** anyone without a line manager (e.g. Omar) gets stuck at a "line-manager" step.~~ **Closed by Phase D** (FR-010): every designed workflow requires a fallback approver, used whenever a rule (including `manager-of-manager`) resolves to nobody. The two code-defined workflows (`leave-approval`, `hr-only-approval`) are unchanged and keep today's "stuck, rule or HR data needs fixing" behaviour until they are re-created in the designer (FR-011).
2. **SQLite security warning (NU1903)** on `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 (CVE-2025-6965, fixed in SQLite ≥ 3.50.2). Pin a newer `SQLitePCLRaw.bundle_e_sqlite3` once the right version is confirmed (`dotnet package search SQLitePCLRaw.bundle_e_sqlite3 --exact-match`). Needs Ahmad's OK (package change).
3. Housekeeping: add `*.log` to `.gitignore`; delete the stray empty file `.claude_write_test` in the project root.

### Done and verified (demo.py step 8: 11 PASS, 0 FAIL; steps 1-7 re-verified on a non-empty database)
**Phase C — Elsa persistence (EF Core SQLite, own file), reset-on-startup off by default**
- Elsa on `Elsa.Persistence.EFCore.Sqlite` 3.7.0 (the plan named `Elsa.EntityFrameworkCore.Sqlite`, Elsa's pre-3.6 package id; it was renamed in the 3.6 line, same `Elsa` 3.7.0 version otherwise — see CHANGELOG).
- `Data/PocDatabaseReset.cs` deletes both SQLite files (+ `-shm`/`-wal`) before anything opens them, only when `Poc:ResetDatabaseOnStartup` is `true` (now defaults to `false`).
- `IWorkflowGateway.InstanceExistsAsync`; `TaskAppService.CompleteAsync` refuses to complete a task whose workflow instance is missing; `WorkflowConsistencyCheck` warns at startup for any `Running` process with a missing instance. Both treat a workflow-store query failure the same as "not found" (fixed a startup crash found while testing the mismatch path, see CHANGELOG).
- `demo.py restart-before` / `restart-after`: a request per tenant is left pending, the app is restarted by hand, then each resumes at the same step (approve, reject, and the HR-only route), tenant isolation holds, and per-tenant employee/form counts are unchanged.
- Fixed two latent demo.py bugs surfaced by running steps 1-7 repeatedly on a non-empty database: `leave-request` submissions failed once a prior run had published a version requiring `handoverTo`; step 7's form keys (`business-trip`, `asset-request`) collided with a prior run's forms.

### Done and verified (demo.py steps 1-7 and 9: 52 PASS, 0 FAIL; step 8 restart check re-verified unchanged)
**Phase D — client-admin approval route designer (option B)**
- Admins build workflows step by step (line manager, manager's manager, role, specific user; required fallback; per-step reject: end or back to previous step) in a new Workflow designer tab; publishing turns the steps into a real Elsa definition through the gateway only.
- `AssignmentResolver` gained `manager-of-manager` and `user:{EmployeeId}`, and now excludes the submitter from every rule's candidates — for all workflows, including the two existing ones (closes a latent self-approval gap).
- Three real Elsa integration bugs found only by actually publishing and running a hand-built graph (not just building it) — see CHANGELOG for details: a missing `Container.Activities` registration (crashed on publish), a missing explicit entry point once that was fixed (`Flowchart.Start` isn't persisted; a reject-to-previous back-edge removes the only structural "no incoming connections" node), and missing `NodeId`s breaking resume after the first suspension (`IIdentityGraphService.AssignIdentitiesAsync` must be called explicitly for hand-built graphs).
- Also found and fixed: `ListPublishedAsync()` returning both code-defined and designed workflows from the same Elsa store, which let a designed workflow's id slip past the tenant-ownership check in routing (not just a cosmetic duplicate in the workflow list).
- Routing's condition value input now adapts to the chosen field's type (dropdown/Yes-no/number/date/text), validated server-side too, with existing bindings flagged if a later form edit makes them stale.
- Trimmed spec 002's "active"/"left" employee wording: no such concept exists in `Employee`; the fallback path is demonstrated via a rule that naturally resolves to nobody instead.

### Done and verified (demo.py steps 1-7, 9-10: 59 PASS, 0 FAIL; step 8 restart check re-verified, 3 PASS before + 11 PASS after)
**Phase K — switchable form renderer (native or SurveyJS Form Library)**
- Native renderer and a new SurveyJS Form Library adapter sit behind one contract, `createRenderer(kind, schema, values, readOnly) -> {mount, getData, validate}`; `wwwroot/index.html` no longer builds or reads raw HTML form strings.
- Renderer choice resolves demo toggle (this browser only) → per-form setting → tenant's company default (`native` for Globex, `surveyjs` for Acme — also proves the setting is tenant-isolated).
- Schema extensions: `visibleIf` (same field/value matching as `ProcessBinding.Matches`), `description`, `placeholder`, `page`. The server is the gatekeeper regardless of renderer: a hidden field is never required and any submitted value for it is dropped before storage (`SubmissionAppService.Validate`), verified at the API level by `demo.py` step 10 against Acme's seeded Leave request v2 (`medicalNoteReference`, required only when `leaveType = Sick`).
- Real bug found only by actually running the seeder (not just building it): seeding Acme's v2 re-fetched the just-inserted v1 version by id, racing ABP's `InsertAsync` (no auto-save by default) and throwing `EntityNotFoundException` on every startup — fixed with an explicit `autoSave: true` on that insert.
- **SC-003 findings (not independently verified — no browser tool available this session, see §2):** both renderers cover all six field types, pagination and the full `visibleIf`/description/placeholder vocabulary used in this PoC, confirmed by code inspection of `renderers.js` and a live field-by-field comparison would be the natural next check once a browser tool is available. RTL (`dir="rtl"` toggle) is wired (`document.documentElement.dir`) but its visual correctness in either renderer was not confirmed by this session — flag for Phase G's design-doc comparison.

### Done and verified (demo.py steps 1-7, 9-11: 67 PASS, 0 FAIL; step 8 restart check re-verified, 3 PASS before + 11 PASS after)
**Phase H — real sign-in and client onboarding**
- `DemoUserMiddleware`/`X-Demo-User`/`__tenant`-per-call are gone. Real ABP Identity + OpenIddict: everyone signs in via the resource-owner-password-credentials grant at `/connect/token`, gets a JWT, and calls the API with `Authorization: Bearer`. `Employee.IsAdmin` is gone too — "company admin" is membership in a real per-tenant `Workmate.CompanyAdmin` role; the vendor admin is a genuine ABP host user (no tenant, no `Employee` row at all).
- Tenant spoofing (the thing step 5's old curl example demonstrated being refused) is now structurally harder to even attempt: ABP's built-in `CurrentUserTenantResolveContributor` resolves tenant from the signed-in token's own claim, ahead of any header, for every authenticated request. The one deliberate exception — `HostActAsTenantResolveContributor` (new) — only ever fires for a genuine host user (no tenant claim at all), letting the vendor admin pick a company via `X-Act-As-Tenant`; an ordinary employee's forged header is always a no-op, confirmed live.
- Seven real bugs found only by starting the app and calling `/connect/token` for real (not discoverable by building alone) — full list with root causes in `specs/003-login-onboarding/tasks.md`'s Phase 2/3 gates. Two worth knowing for future phases: (a) ABP's `PermissionChecker` gates `MultiTenancySide` eligibility on the **signed-in token's own tenant claim**, never on the ambient `ICurrentTenant.Id` a custom resolver changes — a host-authenticated principal can never satisfy a tenant-only permission, full stop, regardless of any grant (so "vendor admin can design for any company" needed those three permissions declared `Both`, not `Tenant`); (b) `Tenant`'s own constructor is `protected internal` to ABP's TenantManagement module, so there is no way to force a specific tenant id from application code — `demo.py` and the UI now look tenants up by `Name`, not a fixed guid.
- Demo credentials: `<firstname>@<acme|globex>.demo` / `1q2w3E*` for every seeded person (e.g. `sara@acme.demo`); vendor admin is `aramis@workmate.vendor` / `1q2w3E*`, no company.

## 4. The plan to complete the PoC

Each phase ends with something demoable, and `demo.py` gains checks for it.

| # | Phase | Scope | Proves | Size | Status |
|---|---|---|---|---|---|
| A | Housekeeping | SQLite pin, `.gitignore`, stray file | Clean build, no warnings | S | Open (needs OK for pin) |
| B | Form creation + routing UI | See §3 | Admin creates and routes a new form with no code | M | **Done; verified** |
| C | Elsa persistence (Phase 2 item 1) | Elsa on EF Core (SQLite for the PoC); turn off DB reset on startup | Restart mid-approval resumes correctly; prerequisite for D | M | **Done; verified** |
| D | Workflow designer | **Option A:** host Elsa Studio behind ABP OpenIddict + permissions (Phase 2 item 2) and enable Elsa multi-tenancy from ABP's `ICurrentTenant` (Phase 2 item 3). **Option B:** own approval-route designer (ordered steps, assignment rule, reject behaviour) that the gateway turns into an Elsa definition | Admin draws a new workflow, binds it to a form, runs it, isolated per tenant | M | **Done; verified** (option B, 2026-10-20); spec `specs/002-route-designer` |
| H | Sign-in + client onboarding | ABP Identity/OpenIddict/TenantManagement/PermissionManagement (open source, **not** Account — see spec); no self-registration; vendor admin creates company + client admin; permissions replace `IsAdmin` | Real auth, no sign-up, tenant from the account | L | **Done; verified** (2026-10-20); spec `specs/003-login-onboarding` |
| I | Employees + org structure | Client admin manages employees, departments (OrganizationUnit), unit heads, roles catalogue; new `department-head` approver | Client runs without vendor help | M | Spec `specs/004-employees-org` |
| J | Email notifications | Assigned / approved / rejected / sent back emails; per-form templates for initiator and approver side; Mailpit locally | Notifications with editable templates | M | Spec `specs/005-email-notifications` |
| K | Switchable renderer | SurveyJS Form Library (MIT) next to the native renderer; demo toggle, per-form and company setting; canonical schema unchanged | Compare renderers on the same forms | S–M | **Done; verified** (2026-10-20, API-level; visual/RTL check pending a browser tool); spec `specs/006-surveyjs-renderer` |
| E | Version pinning (Phase 2 item 4) | Publish workflow v2 while v1 requests are in flight | Running requests keep their version | S | Planned |
| F | Reliable start (Phase 2 item 5) | ABP distributed event bus + outbox replaces `CurrentUnitOfWork.OnCompleted` start hook | Crash between submit and start loses nothing | M | **In-scope decision pending** |
| G | Wrap-up | Final demo run, README, findings + go/no-go in the design doc | Decision-ready | S | Planned |

Order: A → B (verify) → C (done) → D (done) → H (done) → I → J → K (done) → E → F → G (H–K added 2026-10-20; K was done first since it had no dependencies). Hard dependencies: C before D; H before I; H and I before J.
**Phase D decision (2026-10-20): option B.** Workmate HRM is multi-tenant SaaS: client HR admins design workflows, so the designer must be simple, run no client code on shared servers, keep routes in our tenant-filtered tables, and survive Elsa upgrades (our route model is the contract; Elsa definitions are generated). Elsa and Elsa Studio are MIT, but ABP's Elsa Pro integration needs an ABP Team licence (ruled out). Studio may be added later for Aramis staff only. Scope added by Ahmad: each step asks the approver type (line manager, manager's manager, role → which role, specific user → which user); routing value inputs adapt to the chosen field's type. For go/no-go, record that Studio hosting/auth was not proven.

**Decisions Ahmad still owes:** whether F is in PoC scope; SQLite pin.

## 5. Design rules (must hold in every phase)

1. Published form versions are immutable; a submission always renders with its own version.
2. Workflows start and resume only after the related transaction commits.
3. Assignment is resolved by `AssignmentResolver` from rules stored as data; code never hard-codes people. Admin-designed steps may name a specific user of the same tenant, with a fallback (constitution v1.1.0).
4. Tenant id travels in workflow input and is re-applied inside activities; never bypass ABP's tenant filter.
5. Reportable fields are written to `Forms_SubmissionValues` at submit time.
6. **All calls to Elsa's runtime and stores stay inside `IWorkflowGateway` / `ElsaWorkflowGateway`.**
7. Domain records (submission, process, task, timeline) are the system of record, not Elsa state.
8. Form design and routing are admin-only (`Workmate.CompanyAdmin` role or the host-level `Workmate.Tenants.Manage` permission); everyone else only takes part in workflows.

## 6. Boundaries

- No commercial packages (ABP Pro modules, SurveyJS Creator, etc.) without asking Ahmad.
- Keep all `Volo.Abp.*` packages on one version; Elsa stays on 3.7.x unless Ahmad agrees.
- Do not change architecture or data model just to make something compile; adapt to the library API. If a design rule cannot be kept, stop and ask.
- Do not push to any remote or touch `workmate-platform-api`.
- ~~The fake sign-in (`X-Demo-User` header) and the PoC admin check are intentional~~ — **retired by Phase H** (2026-10-20): real ABP Identity + OpenIddict sign-in, `Workmate.*` permissions replace `IsAdmin`. Elsa Studio, if ever hosted, is vendor-staff-only, not client-facing — Phase D is option B, no Studio.
- Log every change in `CHANGELOG.md`, one line each.

## 7. Code map (short)

| Path | Role |
| --- | --- |
| `Domain/Forms.cs`, `Domain/Processes.cs`, `Domain/Employee.cs` | Forms (definitions, immutable versions, submissions, typed values, schema), Processes (bindings, instances, tasks, timeline), HR record linked to a real `IdentityUser` (`Employee.UserId`) |
| `Application/ProcessManager.cs` | Glue: picks a binding, starts the workflow after commit, creates tasks, records outcomes |
| `Application/AssignmentResolver.cs` | `line-manager`, `manager-of-manager`, `role:{Role}`, `user:{EmployeeId}`; submitter always excluded; fallback when empty |
| `Application/FormAppService.cs`, `RoutingAppService.cs` | Admin-only (`[Authorize(WorkmatePermissions.X)]`) form design and routing (merges code-defined + designed workflows; tenant-scoped binding validation) |
| `Application/ApprovalWorkflowAppService.cs` | Workflow designer: create/draft/publish a designed workflow |
| `Application/SubmissionAppService.cs`, `TaskAppService.cs` | Submit, my requests, inbox, complete task |
| `Application/CurrentEmployeeProvider.cs` | Resolves the signed-in user's `Employee` row by `UserId` |
| `Application/WorkmatePermissions.cs`, `WorkmatePermissionDefinitionProvider.cs`, `TenantAdminRoleSeeder.cs`, `TenantOnboardingAppService.cs` | Phase H: real permissions, the `Workmate.CompanyAdmin` role, vendor onboarding |
| `Http/HostActAsTenantResolveContributor.cs` | Phase H: lets only a signed-in host user act within a chosen tenant (`X-Act-As-Tenant`) |
| `Data/OpenIddictDataSeeder.cs` | Phase H: registers the one public OAuth client the demo UI/`demo.py` sign in with |
| `WorkflowEngine/IWorkflowGateway.cs`, `ElsaWorkflowGateway.cs` | The only door into Elsa |
| `WorkflowEngine/Activities/HumanTask.cs`, `CompleteProcess.cs` | Custom activities (bookmark + outcomes; final status) |
| `WorkflowEngine/Workflows/*.cs` | `leave-approval` ("Line manager, then HR"), `hr-only-approval` ("HR only") — code-defined |
| `WorkflowEngine/ApprovalWorkflowGraphBuilder.cs` | Turns a designed workflow's steps into a plain Elsa activity graph |
| `Domain/ApprovalWorkflows.cs` | Designed workflow entities, step schema + validation |
| `Data/DemoDataSeeder.cs`, `Data/PocDbContext.cs` | Seed data; table prefixes `Hr_`, `Forms_`, `Proc_` |
| `Controllers/PocController.cs` | `/api/poc/...` routes |
| `wwwroot/index.html` | Demo UI incl. form designer, workflow designer and routing |
| `wwwroot/js/renderers.js`, `wwwroot/lib/surveyjs/` | `createRenderer(kind, schema, values, readOnly)` contract (native + vendored SurveyJS Form Library) |
| `demo.py` | API demo/regression script (steps 1–7, 9, 10, 11; two-part restart check 8); real sign-in (`sign_in()`/bearer tokens, Phase H), no more `X-Demo-User` |

## 8. Continuing with spec-kit

Setup is **done** (git repo with baseline commit; `.specify/` and `.claude/skills/speckit-*` installed). For reference, the command was (`--ai` was renamed to `--integration`):

```bash
cd ~/Downloads/Workmate.Poc
uvx --from git+https://github.com/github/spec-kit.git specify init --here --integration claude --extension git --script sh
```

Then, in the new Claude session:

1. `/speckit.constitution` — build it from §5 (design rules) and §6 (boundaries) of this file, plus "small, reviewable changes; CHANGELOG entry per change; demo.py check per feature".
2. ~~Verify Phase B~~ — done, steps 1–7 pass.
3. One `/speckit.specify` per remaining phase, in plan order. Seed each with the phase row from §4, e.g.:
   - **C:** "Persist Elsa workflow state with EF Core so a restart mid-approval resumes correctly; turn off DB reset on startup."
   - **D:** option B, already seeded in `specs/002-route-designer/spec.md`; run `/speckit-clarify` on it after Phase C.
   - **E, F, G** as in §4; **A** and the no-manager fallback can be small specs or direct changes once approved.
4. `/speckit.clarify` to surface the pending decisions (§3 open items, §4 decisions) before `/speckit.plan`.
5. `/speckit.plan` → `/speckit.tasks` → `/speckit.implement`; keep the build/demo loop from §2 as the acceptance check for every task.
