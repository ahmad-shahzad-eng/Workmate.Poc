# Workmate HRM: Forms & Workflow PoC

One runnable slice of the target design: **ABP Framework** (multi-tenancy, data, units of work)
+ **Elsa Workflows 3** (long-running workflows with human steps) + a **JSON-schema form layer**,
joined by our own Processes code (bindings, tasks, assignment, timeline).

Two demo companies, each with employee self-service forms (leave request, work from home, expense claim,
letter request). Everyone signs in for real (ABP Identity + OpenIddict, Phase H) — there is no sign-up.
Only **admins** design forms, workflows and routing; everyone else only takes part in the workflows. There are two kinds:
a **company admin** is an ordinary employee in the `Workmate.CompanyAdmin` role (still has a manager, still submits and approves),
and the **vendor admin** (Aramis Support) is a host-side user of the HRM vendor — no tenant of their own — who can design for
any company (acting within it via the UI's "Acting as" company picker) but is not an employee there and cannot submit or approve.
Besides the two built-in workflows, company admins can build their own in **Workflow designer** (Phase D): an ordered list
of steps, each naming who approves (line manager, manager's manager, head of my department, a role, or a specific person),
with a required fallback approver and a reject action (end, or back to the previous step) per step. Company admins also
manage their own **Employees** (with sign-in accounts and a per-company role list) and **Organisation** (a department
tree with a head per department) — spec 004. Every request event and every new account sends an **email** (spec 005):
plain-text + HTML, worded per form, per company or built-in, with an Email log for admins. New accounts never see a
password; they choose one from an emailed one-time link.

| Company | People | Routes |
| --- | --- | --- |
| Acme Trading | Sara (employee, reports to Omar), Omar (manager), Huda (HR), Layla (Finance, reports to Omar), Khalid (employee + company admin, reports to Omar) | Sick leave and letter requests go to HR only; everything else goes line manager, then HR |
| Globex Logistics | Ali (employee, reports to Fatima), Fatima (manager), Yousef (HR), Yara (Finance, reports to Fatima), Noor (employee + company admin, reports to Fatima) | Letter requests go to HR only; everything else goes line manager, then HR |

## Demo accounts (user names and passwords)

These are local demo accounts, created by the seeder on every reset. On the sign-in screen, pick the **Company** first,
then the email and password. The user name is the email.

| Company (sign-in picker) | Name | Email (user name) | Password | Job role | Reports to | Can do |
| --- | --- | --- | --- | --- | --- | --- |
| Vendor / host (no company) | Aramis Support | `aramis@workmate.vendor` | `1q2w3E*` | — | — | Vendor admin: create companies, reset a company admin's password, design forms/workflows/routing for any company ("Acting as" picker); cannot submit or approve |
| Acme Trading | Khalid Mansour | `khalid@acme.demo` | `1q2w3E*` | Employee | Omar | **Company admin**: form designer (incl. Emails), workflow designer, Employees, Organisation, Email log; also submits requests |
| Acme Trading | Omar Khalid | `omar@acme.demo` | `1q2w3E*` | Manager | — | Approves his team's requests (step 1) |
| Acme Trading | Huda Ali | `huda@acme.demo` | `1q2w3E*` | HR | — | HR approval (step 2, sick leave, letters, fallback) |
| Acme Trading | Sara Ahmed | `sara@acme.demo` | `1q2w3E*` | Employee | Omar | Submits requests |
| Acme Trading | Layla Haddad | `layla@acme.demo` | `1q2w3E*` | Finance | Omar | Finance steps in designed workflows |
| Globex Logistics | Noor Ibrahim | `noor@globex.demo` | `1q2w3E*` | Employee | Fatima | **Company admin** (same rights as Khalid, for Globex) |
| Globex Logistics | Fatima Saleh | `fatima@globex.demo` | `1q2w3E*` | Manager | — | Approves her team's requests |
| Globex Logistics | Yousef Nasser | `yousef@globex.demo` | `1q2w3E*` | HR | — | HR approval |
| Globex Logistics | Ali Hassan | `ali@globex.demo` | `1q2w3E*` | Employee | Fatima | Submits requests |
| Globex Logistics | Yara Saleh | `yara@globex.demo` | `1q2w3E*` | Finance | Fatima | Finance steps in designed workflows |

**Accounts created later** (a new company's admin, a new employee) have **no password** until the person opens the
invitation email in Mailpit (http://localhost:8025) and chooses one. "Forgot password?" on the sign-in screen sends a
reset link the same way. `demo.py` creates extra companies and people on each run (`Initech …`, `Umbrella …`,
`Wayne …`, `Hooli …`, with emails such as `bill-<run>@initech.demo`). It sets their password to `Demo-Pass1!` through
the emailed link. Bill, the Initech admin, ends up with `Demo-Pass2!` after step 11's reset. `<run>` is the 6-digit
suffix shown in `demo.log`.

**Other local tools:** Mailpit (http://localhost:8025) has no sign-in. The SQLite files `workmate-poc.db` and
`workmate-elsa.db` have no password.

Five wrong passwords in a row lock an account for a few minutes (ABP Identity's default). The vendor admin's reset,
or "Forgot password?" followed by the emailed link, unlocks it at once.

## Quick start (how to run)

Needs the .NET 10 SDK, Python 3 and Mailpit (install commands in [Setup](#setup-macos-or-windows) below).

```bash
# 1. Terminal 1: the mail catcher (every email the app sends lands here)
mailpit                                            # inbox: http://localhost:8025

# 2. Terminal 2: build and run the app
cd Workmate.Poc
dotnet build
dotnet run -- --Poc:ResetDatabaseOnStartup=true    # first run, or after pulling a data-model change
# later runs keep the data:  dotnet run
# wait for "Now listening on: http://localhost:5080", then open http://localhost:5080

# 3. Terminal 3 (optional): the automated demo/regression run
python3 demo.py                                    # Windows: py demo.py; results in demo.log
```

Sign in with any account above, for example Company **Acme Trading**, `sara@acme.demo`, `1q2w3E*`.

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for diagrams of the components (colour-coded by source: ABP Framework,
Elsa Workflows, SurveyJS, our code), the submit-and-approve runtime flow, and the admin design flow.

## Setup (macOS or Windows)

### 1. Install the software

| Software | Needed for | macOS (Homebrew) | Windows (PowerShell, winget) |
| --- | --- | --- | --- |
| **.NET 10 SDK** | build and run (`net10.0`) | `brew install --cask dotnet-sdk` | `winget install Microsoft.DotNet.SDK.10` |
| **Git** | get the code, branches | `brew install git` (or Xcode Command Line Tools: `xcode-select --install`) | `winget install Git.Git` |
| **Python 3.10+** | `demo.py` (standard library only, no pip packages) | `brew install python` | `winget install Python.Python.3.12` |
| **Mailpit** | catches every email the app sends (SMTP `localhost:1025`, inbox at http://localhost:8025); `demo.py` needs it | `brew install mailpit` | `winget install axllent.mailpit` |
| DB Browser for SQLite *(optional)* | look at `workmate-poc.db` / `workmate-elsa.db` | `brew install --cask db-browser-for-sqlite` | `winget install DBBrowserForSQLite.DBBrowserForSQLite` |
| Editor *(optional)* | code | VS Code + C# Dev Kit, or JetBrains Rider | Visual Studio 2026 (or a 2022 release with .NET 10 support), VS Code + C# Dev Kit, or Rider |
| uv + Claude Code *(optional)* | Spec Kit workflow in `specs/` | `brew install uv` | `winget install astral-sh.uv` |

No Node.js, Docker or database server is needed: the SurveyJS files are already in `wwwroot/lib/surveyjs/`, and both
databases are SQLite files created on first run. No Homebrew on the Mac? Install it from https://brew.sh, or use the
installers from https://dotnet.microsoft.com/download and https://www.python.org/downloads/.

Open a **new** terminal after installing, then check:

```bash
dotnet --list-sdks     # shows a 10.0.x line
git --version
python3 --version      # Windows: py --version
```

The first build downloads NuGet packages from nuget.org (ABP 9.1.0, Elsa 3.7.0). Behind a company proxy, make sure
`api.nuget.org` is reachable or configure the proxy for NuGet.

### 2. Get the project

```bash
git clone <repository-url> Workmate.Poc     # or copy the Workmate.Poc folder
cd Workmate.Poc
dotnet restore
dotnet build
```

`dotnet build` must end with `Build succeeded`. Warnings about `SQLitePCLRaw` and `Scriban` (NU1902/NU1903) are known open
items: both come in through ABP 9.1.0. Email templates use a locked-down Scriban sandbox (spec 005 research R4).

### 3. First run (clean, seeded databases)

Run this once on a new machine, and again whenever the data model changes (the app uses `EnsureCreated`, which does
not migrate an existing database):

```bash
mailpit &              # Windows: start mailpit (catches the app's emails; inbox at http://localhost:8025)
dotnet run -- --Poc:ResetDatabaseOnStartup=true
```

Wait for `Now listening on: http://localhost:5080`, then open http://localhost:5080. Spec 005 added new tables
(`Notif_*` and `AbpBackgroundJobs`), so this reset is needed once after pulling it.
Both `workmate-poc.db` and `workmate-elsa.db` are deleted and re-created, and the two demo companies are seeded
— including real sign-in accounts for everyone (Phase H: ABP Identity + OpenIddict + TenantManagement +
PermissionManagement add 14 tables to `workmate-poc.db`; the reset above is required once after pulling this phase).

### 4. Normal runs (data kept between restarts)

Start Mailpit first (once per session), then the app:

```bash
mailpit &              # Windows: start mailpit
dotnet run
```

Every email lands in Mailpit's inbox at http://localhost:8025, nothing leaves the machine. SMTP settings are under
`Settings` in `appsettings.json` (`Abp.Mailing.*`). For a real server, set `Abp.Mailing.Smtp.UseDefaultCredentials` to
`false` and add `Abp.Mailing.Smtp.UserName`/`Password`. `appsettings.Development.json` shortens email retries (2 s instead
of 1 min) and makes sends to `fail-…` addresses fail on purpose, for `demo.py` 13i.

Stop with `Ctrl+C`. If port 5080 is still in use from an earlier run:

| macOS | Windows (PowerShell) |
| --- | --- |
| `lsof -ti :5080 \| xargs kill` | `Get-NetTCPConnection -LocalPort 5080 \| % { Stop-Process -Id $_.OwningProcess -Force }` |

To use another port: `dotnet run -- --urls http://localhost:5090` (then set `BASE` at the top of `demo.py`).

### 5. Check it works

With the app running, in a second terminal in the same folder:

```bash
python3 demo.py        # Windows: py demo.py
```

No line in `demo.log` should start with `FAIL`. Run it on a freshly reset database for a clean result. Without Mailpit
running, `demo.py` stops straight away with "Start Mailpit (mailpit &) first".

### 6. Using the demo UI

Sign in with one of the demo accounts above (real ABP Identity + OpenIddict — resource-owner-password-credentials
grant at `/connect/token`, spec 003). There is no sign-up. The vendor admin additionally gets an "Acting as"
company picker in the top bar to design forms/workflows/routing for any company; ordinary employees don't see it —
their own company comes from their signed-in account, not a header they could change. Use the **Renderer** toggle
to switch between the native renderer and SurveyJS, this browser only. The seeded demo users keep the password
`1q2w3E*`; anyone created later (new companies' admins, new employees) chooses a password from the emailed invite in
Mailpit. "Forgot password?" on the sign-in screen emails a reset link.

### Troubleshooting

| Problem | Fix |
| --- | --- |
| `dotnet` not found, or no 10.0 SDK listed | Open a new terminal after installing; on Windows sign out and in again so `PATH` updates. |
| `NETSDK1045: The current .NET SDK does not support targeting .NET 10.0` | An older SDK is first in `PATH`; install the .NET 10 SDK and check `dotnet --list-sdks`. |
| Restore fails (401, timeout, proxy) | Check access to `api.nuget.org`; `dotnet nuget list source` should show nuget.org enabled. |
| `SQLite Error 1: no such table` or `no such column` after pulling changes | Run once with `-- --Poc:ResetDatabaseOnStartup=true`. |
| `address already in use` on 5080 | Stop the old instance (see step 4). |
| `demo.py` fails with connection refused | Start the app first and wait for `Now listening`. |
| `demo.py` stops with "Start Mailpit (mailpit &) first" | Start Mailpit (`mailpit &`), then run `demo.py` again. |
| No invitation or reset email arrives | Check Mailpit is running at http://localhost:8025. Failed sends are retried, then listed in the **Email log** tab (company admin) with the error and a Resend button. |
| "Invalid username or password" for a demo user | Pick the right **Company** first (the vendor admin uses "Vendor / host (no company)"). After 5 wrong tries the account is locked for a few minutes; use "Forgot password?" or wait. |
| Windows: `python3` opens the Microsoft Store | Use `py demo.py`, or turn off the "App execution aliases" for Python in Settings. |
| macOS: "database is locked" | Close DB Browser for SQLite (it locks the file) and run again. |
| SurveyJS form does not show | Hard-refresh the browser (`Cmd+Shift+R` / `Ctrl+F5`); the page falls back to the native renderer if SurveyJS fails to load. |

## Demo script (maps to the PoC success criteria)

1. **Happy path.** As Sara (Acme), send an Annual leave request. Switch to Omar and approve it in Inbox.
   Switch to Huda and approve. Back as Sara, My requests shows Approved with the full timeline.
2. **Conditional routing.** As Sara, send a Sick leave request. It skips Omar and goes straight to Huda
   (binding with `leaveType = Sick`, priority 1).
3. **Reject path.** Send another Annual request; reject it as Omar. The workflow ends without an HR step.
4. **Form versioning.** As a company admin (Khalid), open Form designer, add a field, for example
   `{ "key": "handoverTo", "label": "Handover to", "type": "text", "required": true }`, save the draft and publish v2.
   New requests show the new field; requests sent on v1 still render with v1 (marked as replaced).
5. **Tenant isolation.** Sign out and sign in as an Ali (Globex). None of Acme's requests, tasks or users are
   visible, and the same form key runs Globex's own binding. Which company a request can see comes entirely from
   the signed-in token's own tenant claim — not a header a user could change — so crossing tenants on purpose no
   longer has an attack surface to try: even a forged `X-Act-As-Tenant` header (the one mechanism that *can*
   switch company, and only for the vendor admin) is silently ignored for an ordinary employee's token.
6. **Reporting values.** Open `workmate-poc.db` in any SQLite browser and query `Forms_SubmissionValues`:
   reportable fields are stored typed (`NumberValue`, `DateValue`), so analytics never parses JSON.

7. **Form creation and routing (admins only).** As Khalid, or as Aramis Support for any company, create a form in Form designer, add fields in the Fields tab
   (or edit the JSON), publish it, then add routes under Routing: a default route plus conditional ones (field equals value).
   Sara can now pick it under New request. Huda (HR) and Sara do not see Form designer, and the API refuses them.

8. **Restart mid-approval.** Submit a request and leave it pending at a human step, then stop and restart the app
   (reset off). The task is still there; approving it resumes the workflow at the same step, in both tenants.

9. **Workflow designer (admins only).** As Khalid, open Workflow designer, create a workflow, add a step for each
   approver type (line manager, manager's manager, role, specific user), pick a fallback approver, save and publish.
   Bind it to a form in that form's Routing section and run a request through every step. Huda (HR) does not see
   Workflow designer, and Globex cannot see, bind to, or start Acme's workflow.

10. **Switchable form renderer (Phase K).** Acme's company default renders forms with the vendored SurveyJS Form
    Library; Globex's stays the plain native renderer — set under Form designer's company-settings panel, overridable
    per form (Renderer dropdown) and, for this browser only, by the top-bar "Renderer" demo toggle. Try Acme's Leave
    request as Sara: pick `Sick`, and the Medical note reference field (page "Notes") appears and becomes required;
    switch back to `Annual` and it hides again. The API enforces the same rule regardless of renderer — a direct
    `POST /submissions` with `leaveType=Annual` and a value for `medicalNoteReference` has that value silently
    dropped (never stored), and `leaveType=Sick` with no value is refused. Use the RTL checkbox to preview either
    renderer right-to-left (demo-only, no translations).

11. **Real sign-in and onboarding (Phase H).** As the vendor admin, create a new company (name + admin email) —
    the admin gets an invitation email with a set-password link (spec 005; open it from Mailpit). Sign in as that admin: the company
    starts empty and isolated. There is no sign-up path anywhere, and repeated wrong passwords lock an account out
    (ABP Identity's default lockout) even once the correct password is used again.

12. **Employees and organisation (spec 004).** As a company admin, open **Employees**: add people (each gets a sign-in
    and an invitation email to choose a password), give them roles from the company's role list, and set line managers — a
    reporting loop is refused. Open **Organisation**: build departments (e.g. Head office → Finance → Payables) and set
    a head for each, then put people in departments from Employees. In Workflow designer, a "Head of my department"
    step goes to the requester's department head, or the next head up if the requester heads it themselves.
    Deactivating someone blocks their sign-in and moves their open tasks to the workflow's fallback approver (the
    timeline says so). A role that a published workflow uses can't be deleted.

13. **Email notifications (spec 005).** With Mailpit open at http://localhost:8025, run the happy path again: Sara gets
    "submitted", Omar and then Huda get "waiting for your approval" with a link to their inbox, and Sara gets "approved".
    A reject sends Sara the approver's comment; a send-back in a designed workflow tells both the previous approver and
    Sara. As Khalid, Form designer → **Emails** changes one form's wording (with placeholders such as
    `{{ request.title }}`, `{{ fields.amount }}`, and `{{ if comment }}…{{ end }}`), with a preview; **Company email
    defaults** sets the wording for every other form, the invitation/reset emails, and the sender name. Unknown
    placeholders and anything beyond if/else are refused. **Email log** (company admins) shows every email, failures
    with their last error after 5 attempts, and Resend.

`demo.py` runs steps 1-7 and 9-13 against the API and writes `demo.log` (`python3 demo.py` with the app freshly started).
Step 8 is two-part, around a manual restart:

```bash
python3 demo.py restart-before   # submits pending requests, records their ids in demo_restart.json
# stop and restart the app (reset off) here
python3 demo.py restart-after    # resumes and completes them, checks tenant isolation and unchanged counts
```

## Code map

| Path | Role in the target design |
| --- | --- |
| `Domain/Forms.cs` | Forms module: definitions, immutable versions, submissions, typed values, JSON schema |
| `Domain/Processes.cs` | Processes module: bindings, instances, tasks, candidates, timeline events |
| `Application/ProcessManager.cs` | The glue: picks a binding, starts the workflow, creates tasks, records outcomes |
| `Application/AssignmentResolver.cs` | Resolves task candidates from HR data (`line-manager`, `role:HR`) |
| `Application/*AppService.cs` | Use cases for forms, routing (`RoutingAppService`), submissions and the task inbox |
| `WorkflowEngine/IWorkflowGateway.cs` | The only door into the engine |
| `WorkflowEngine/ElsaWorkflowGateway.cs` | Elsa implementation of that door |
| `WorkflowEngine/Activities/HumanTask.cs` | Custom Elsa activity: create task, bookmark, wait for outcome |
| `WorkflowEngine/Activities/CompleteProcess.cs` | Custom Elsa activity: write final status to domain records |
| `WorkflowEngine/Workflows/*.cs` | The two built-in workflows, defined in code |
| `Domain/ApprovalWorkflows.cs` | Admin-designed workflows: steps + fallback approver, draft/published versions |
| `WorkflowEngine/ApprovalWorkflowGraphBuilder.cs` | Turns designed steps into a plain Elsa activity graph (no Elsa calls) |
| `Application/ApprovalWorkflowAppService.cs` | Workflow designer: create, draft, publish (builds the graph, publishes via the gateway) |
| `Data/` | ABP DbContext (table prefixes stand in for module schemas), demo seed data, startup DB reset |
| `Application/WorkflowConsistencyCheck.cs` | Startup-only: warns if a running process's workflow instance is missing |
| `wwwroot/index.html` | Demo UI: form renderer, my requests, inbox, form designer, workflow designer |
| `wwwroot/js/renderers.js` | `createRenderer(kind, schema, values, readOnly)`: native HTML renderer and the SurveyJS adapter, same `{mount, getData, validate}` contract |
| `wwwroot/lib/surveyjs/` | Vendored SurveyJS Form Library (MIT, no CDN at runtime) — rendering only, not Creator/Dashboard/PDF Generator |
| `Application/WorkmatePermissions.cs`, `WorkmatePermissionDefinitionProvider.cs` | Real ABP permissions (Phase H) — replace the old `Employee.IsAdmin` flag |
| `Application/TenantOnboardingAppService.cs` | Vendor admin creates a company + its first admin user (host-only) |
| `Application/EmployeeAppService.cs`, `TaskReassignmentService.cs` | Spec 004: employees with sign-in accounts; deactivation moves open tasks to the fallback approver |
| `Application/OrganizationAppService.cs`, `JobRoleLookup.cs`, `Domain/Organization.cs` | Spec 004: departments (ABP OrganizationUnits) with heads; the per-company job-role catalogue |
| `Application/TenantAdminRoleSeeder.cs` | Shared "grant the `Workmate.CompanyAdmin` role + permissions, in this tenant" step — used at seed time and at onboarding time |
| `Http/HostActAsTenantResolveContributor.cs` | The only legitimate way a signed-in user can pick a tenant other than their own: a host user (no tenant claim), via `X-Act-As-Tenant` |
| `Data/OpenIddictDataSeeder.cs` | Registers the one public OAuth client the demo UI and `demo.py` sign in with |
| `Domain/Notifications.cs` | Spec 005: email events and sides, per-form/company templates, sender name, `EmailMessage` (outbox row + Email log) |
| `Application/Notifications/NotificationService.cs` | Resolves the template (form → company → built-in), renders it, writes the `EmailMessage` and enqueues its job in the caller's unit of work |
| `Application/Notifications/EmailTemplateRenderer.cs` | Sandboxed Scriban: validates the syntax tree against the placeholder allow-list; renders text + a fixed, escaped HTML layout |
| `Application/Notifications/SendEmailJob.cs` | ABP background job (persistent, after commit): sends over SMTP, retries 5 times, then Failed; renders account emails with a fresh one-time link |
| `Application/Notifications/EmailTemplateAppService.cs`, `EmailLogAppService.cs`, `Application/AccountAppService.cs` | Template editing and preview, the Email log with Resend, forgot/set password |

## Upgrading packages

The project builds and passes `demo.py` on the pinned versions. If you move to newer versions, these are the likely places to adjust:

- **`WorkflowEngine/ElsaWorkflowGateway.cs`.** Elsa's runtime client API changed between 3.x minor versions.
  The file targets the workflow client API (`IWorkflowRuntime.CreateClientAsync`, Elsa 3.5+). If your restored Elsa
  version differs, adjust only this file; nothing else calls Elsa's runtime.
- **Elsa activity helpers** (`HumanTask.cs`, `CompleteProcess.cs`, the workflow classes): method names such as
  `CreateBookmark`, `CompleteActivityWithOutcomesAsync` and `WithDefinitionId` are stable across 3.x, but check them
  against the version you restore.
- **Package versions.** ABP 9.1.0 and Elsa 3.7.0 are pinned in the `.csproj`. You can move ABP to a newer 9.x or 10.x
  release; keep all `Volo.Abp.*` packages on the same version.

## Deliberate PoC shortcuts (and what production does instead)

| Shortcut | Production |
| --- | --- |
| ~~Fake sign-in from a header~~ **Done** (Phase H): real ABP Identity + OpenIddict, permissions replace `IsAdmin` | Add MFA/SSO per client; move off the password grant to authorization-code + PKCE for a future separate SPA/mobile client |
| Workflow start via `OnCompleted` after commit | `FormSubmittedEto` on ABP's distributed event bus with the outbox |
| Elsa EF Core persistence in its own SQLite file | Same server as ABP, own schema |
| Two workflows defined in C#; others admin-designed (own builder, no Elsa Studio) | Elsa Studio stays vendor-staff-only, behind OpenIddict; clients never see it (ruled out: no client code/expressions on shared servers) |
| Role catalogue is just the distinct roles already in HR data | `AssignmentRule` entities and a real role catalogue per tenant |
| Binding condition is `field == value` | Elsa expressions or a decision table |
| JSON textarea designer | SurveyJS Creator, Form.io builder, or our own builder emitting the same schema |
| Single project | ABP modular monolith: Forms, Processes, WorkflowEngine modules |
| One SMTP server for every company, Mailpit locally; request number = first 8 characters of the id | Per-company SMTP/sender domain; a real per-company request number sequence |

## Phase 2 (after this PoC passes)

1. ~~Switch Elsa to EF Core persistence; turn off `Poc:ResetDatabaseOnStartup`; prove a restart mid-approval resumes correctly.~~ **Done** (step 8).
2. ~~Give admins a way to draw workflows with no code.~~ **Done** (step 9, own designer — Elsa Studio ruled out for clients, see shortcuts table).
3. Enable Elsa multi-tenancy with a tenant resolver backed by ABP's `ICurrentTenant` (only relevant if Elsa Studio is hosted for vendor staff later).
4. Publish a second workflow version while requests are in flight; prove running instances keep their version (for designed workflows too, not just code-defined ones).
5. Replace the start hook with the distributed event bus + outbox; prove a crash between submit and start loses nothing.
