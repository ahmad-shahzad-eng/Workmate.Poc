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
