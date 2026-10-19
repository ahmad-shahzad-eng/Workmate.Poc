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
| Company | People |
| --- | --- |
| Acme Trading (`a1a1a1a1-0000-0000-0000-000000000001`) | Sara (employee → Omar), Omar (manager), Huda (HR), Khalid (employee + company admin → Omar) |
| Globex Logistics (`b2b2b2b2-0000-0000-0000-000000000002`) | Ali (employee → Fatima), Fatima (manager), Yousef (HR), Noor (employee + company admin → Fatima) |
| Vendor | Aramis Support (vendor admin, any company) |

### Open items (awaiting Ahmad's decision unless noted)
1. **No-manager fallback:** anyone without a line manager (e.g. Omar) gets stuck at a "line-manager" step. Proposal: in `AssignmentResolver`, fall back to `role:HR` when there is no manager (keeps design rule 3). Ahmad has not decided yet.
2. **SQLite security warning (NU1903)** on `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 (CVE-2025-6965, fixed in SQLite ≥ 3.50.2). Pin a newer `SQLitePCLRaw.bundle_e_sqlite3` once the right version is confirmed (`dotnet package search SQLitePCLRaw.bundle_e_sqlite3 --exact-match`). Needs Ahmad's OK (package change).
3. Housekeeping: add `*.log` to `.gitignore`; delete the stray empty file `.claude_write_test` in the project root.

### Done and verified (demo.py step 8: 11 PASS, 0 FAIL; steps 1-7 re-verified on a non-empty database)
**Phase C — Elsa persistence (EF Core SQLite, own file), reset-on-startup off by default**
- Elsa on `Elsa.Persistence.EFCore.Sqlite` 3.7.0 (the plan named `Elsa.EntityFrameworkCore.Sqlite`, Elsa's pre-3.6 package id; it was renamed in the 3.6 line, same `Elsa` 3.7.0 version otherwise — see CHANGELOG).
- `Data/PocDatabaseReset.cs` deletes both SQLite files (+ `-shm`/`-wal`) before anything opens them, only when `Poc:ResetDatabaseOnStartup` is `true` (now defaults to `false`).
- `IWorkflowGateway.InstanceExistsAsync`; `TaskAppService.CompleteAsync` refuses to complete a task whose workflow instance is missing; `WorkflowConsistencyCheck` warns at startup for any `Running` process with a missing instance. Both treat a workflow-store query failure the same as "not found" (fixed a startup crash found while testing the mismatch path, see CHANGELOG).
- `demo.py restart-before` / `restart-after`: a request per tenant is left pending, the app is restarted by hand, then each resumes at the same step (approve, reject, and the HR-only route), tenant isolation holds, and per-tenant employee/form counts are unchanged.
- Fixed two latent demo.py bugs surfaced by running steps 1-7 repeatedly on a non-empty database: `leave-request` submissions failed once a prior run had published a version requiring `handoverTo`; step 7's form keys (`business-trip`, `asset-request`) collided with a prior run's forms.

## 4. The plan to complete the PoC

Each phase ends with something demoable, and `demo.py` gains checks for it.

| # | Phase | Scope | Proves | Size | Status |
|---|---|---|---|---|---|
| A | Housekeeping | SQLite pin, `.gitignore`, stray file | Clean build, no warnings | S | Open (needs OK for pin) |
| B | Form creation + routing UI | See §3 | Admin creates and routes a new form with no code | M | **Done; verified** |
| C | Elsa persistence (Phase 2 item 1) | Elsa on EF Core (SQLite for the PoC); turn off DB reset on startup | Restart mid-approval resumes correctly; prerequisite for D | M | **Done; verified** |
| D | Workflow designer | **Option A:** host Elsa Studio behind ABP OpenIddict + permissions (Phase 2 item 2) and enable Elsa multi-tenancy from ABP's `ICurrentTenant` (Phase 2 item 3). **Option B:** own approval-route designer (ordered steps, assignment rule, reject behaviour) that the gateway turns into an Elsa definition | Admin draws a new workflow, binds it to a form, runs it, isolated per tenant | M | **Decided: option B** (2026-10-20); spec `specs/002-route-designer` |
| H | Sign-in + client onboarding | ABP Identity/OpenIddict/Account/TenantManagement (open source); no self-registration; vendor admin creates company + client admin; permissions replace `IsAdmin` | Real auth, no sign-up, tenant from the account | L | Spec `specs/003-login-onboarding` |
| I | Employees + org structure | Client admin manages employees, departments (OrganizationUnit), unit heads, roles catalogue; new `department-head` approver | Client runs without vendor help | M | Spec `specs/004-employees-org` |
| J | Email notifications | Assigned / approved / rejected / sent back emails; per-form templates for initiator and approver side; Mailpit locally | Notifications with editable templates | M | Spec `specs/005-email-notifications` |
| E | Version pinning (Phase 2 item 4) | Publish workflow v2 while v1 requests are in flight | Running requests keep their version | S | Planned |
| F | Reliable start (Phase 2 item 5) | ABP distributed event bus + outbox replaces `CurrentUnitOfWork.OnCompleted` start hook | Crash between submit and start loses nothing | M | **In-scope decision pending** |
| G | Wrap-up | Final demo run, README, findings + go/no-go in the design doc | Decision-ready | S | Planned |

Order: A → B (verify) → C (done) → D → H → I → J → E → F → G (H–J added 2026-10-20). Hard dependencies: C before D; H before I; H and I before J.
**Phase D decision (2026-10-20): option B.** Workmate HRM is multi-tenant SaaS: client HR admins design workflows, so the designer must be simple, run no client code on shared servers, keep routes in our tenant-filtered tables, and survive Elsa upgrades (our route model is the contract; Elsa definitions are generated). Elsa and Elsa Studio are MIT, but ABP's Elsa Pro integration needs an ABP Team licence (ruled out). Studio may be added later for Aramis staff only. Scope added by Ahmad: each step asks the approver type (line manager, manager's manager, role → which role, specific user → which user); routing value inputs adapt to the chosen field's type. For go/no-go, record that Studio hosting/auth was not proven.

**Decisions Ahmad still owes:** whether F is in PoC scope; SQLite pin; fallback approver when a rule resolves to nobody (now FR-010 in spec 002, also covers the no-manager case).

## 5. Design rules (must hold in every phase)

1. Published form versions are immutable; a submission always renders with its own version.
2. Workflows start and resume only after the related transaction commits.
3. Assignment is resolved by `AssignmentResolver` from rules stored as data; code never hard-codes people. Admin-designed steps may name a specific user of the same tenant, with a fallback (constitution v1.1.0).
4. Tenant id travels in workflow input and is re-applied inside activities; never bypass ABP's tenant filter.
5. Reportable fields are written to `Forms_SubmissionValues` at submit time.
6. **All calls to Elsa's runtime and stores stay inside `IWorkflowGateway` / `ElsaWorkflowGateway`.**
7. Domain records (submission, process, task, timeline) are the system of record, not Elsa state.
8. Form design and routing are admin-only (company admin flag or vendor admin); everyone else only takes part in workflows.

## 6. Boundaries

- No commercial packages (ABP Pro modules, SurveyJS Creator, etc.) without asking Ahmad.
- Keep all `Volo.Abp.*` packages on one version; Elsa stays on 3.7.x unless Ahmad agrees.
- Do not change architecture or data model just to make something compile; adapt to the library API. If a design rule cannot be kept, stop and ask.
- Do not push to any remote or touch `workmate-platform-api`.
- The fake sign-in (`X-Demo-User` header) and the PoC admin check are intentional; leave them unless a phase replaces them (Phase D option A brings OpenIddict for Studio).
- Log every change in `CHANGELOG.md`, one line each.

## 7. Code map (short)

| Path | Role |
| --- | --- |
| `Domain/Forms.cs`, `Domain/Processes.cs`, `Domain/Employee.cs` | Forms (definitions, immutable versions, submissions, typed values, schema), Processes (bindings, instances, tasks, timeline), HR stand-in (`IsAdmin`) |
| `Application/ProcessManager.cs` | Glue: picks a binding, starts the workflow after commit, creates tasks, records outcomes |
| `Application/AssignmentResolver.cs` | `line-manager`, `role:{Role}` |
| `Application/FormAppService.cs`, `RoutingAppService.cs` | Admin-only design and routing |
| `Application/SubmissionAppService.cs`, `TaskAppService.cs` | Submit, my requests, inbox, complete task |
| `Application/CurrentEmployeeProvider.cs` | Current employee + `EnsureAdminAsync` |
| `WorkflowEngine/IWorkflowGateway.cs`, `ElsaWorkflowGateway.cs` | The only door into Elsa |
| `WorkflowEngine/Activities/HumanTask.cs`, `CompleteProcess.cs` | Custom activities (bookmark + outcomes; final status) |
| `WorkflowEngine/Workflows/*.cs` | `leave-approval` ("Line manager, then HR"), `hr-only-approval` ("HR only") |
| `Data/DemoDataSeeder.cs`, `Data/PocDbContext.cs` | Seed data; table prefixes `Hr_`, `Forms_`, `Proc_` |
| `Controllers/PocController.cs` | `/api/poc/...` routes |
| `wwwroot/index.html` | Demo UI incl. form designer and routing |
| `demo.py` | API demo/regression script (steps 1–7) |

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
