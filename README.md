# Workmate HRM: Forms & Workflow PoC

One runnable slice of the target design: **ABP Framework** (multi-tenancy, data, units of work)
+ **Elsa Workflows 3** (long-running workflows with human steps) + a **JSON-schema form layer**,
joined by our own Processes code (bindings, tasks, assignment, timeline).

Two demo companies, each with employee self-service forms (leave request, work from home, expense claim,
letter request). Only **admins** design forms, workflows and routing; everyone else only takes part in the workflows. There are two kinds:
a **company admin** is an ordinary employee with an admin flag (still has a manager, still submits and approves),
and the **vendor admin** (Aramis Support) is a host-side user of the HRM vendor who can design for any company but is not an employee there.
Besides the two built-in workflows, company admins can build their own in **Workflow designer** (Phase D): an ordered list
of steps, each naming who approves (line manager, manager's manager, a role, or a specific person), with a required
fallback approver and a reject action (end, or back to the previous step) per step.

| Company | People | Routes |
| --- | --- | --- |
| Acme Trading | Sara (employee, reports to Omar), Omar (manager), Huda (HR), Layla (Finance, reports to Omar), Khalid (employee + company admin, reports to Omar) | Sick leave and letter requests go to HR only; everything else goes line manager, then HR |
| Globex Logistics | Ali (employee, reports to Fatima), Fatima (manager), Yousef (HR), Yara (Finance, reports to Fatima), Noor (employee + company admin, reports to Fatima) | Letter requests go to HR only; everything else goes line manager, then HR |

## Run it

Requirements: .NET 10 SDK.

```bash
dotnet run
```

Open http://localhost:5080. Domain data (`workmate-poc.db`) and workflow state (`workmate-elsa.db`,
Elsa's own EF Core SQLite persistence) both survive a restart. Set `Poc:ResetDatabaseOnStartup` to
`true` (`appsettings.json`) for a clean, re-seeded demo; it deletes both files together so they never
get out of step.

Use the **Company** and **Acting as** dropdowns at the top to switch tenant and person.
Sign-in is faked with an `X-Demo-User` header (`Http/DemoUserMiddleware.cs`); production uses ABP Identity + OpenIddict.

## Demo script (maps to the PoC success criteria)

1. **Happy path.** As Sara (Acme), send an Annual leave request. Switch to Omar and approve it in Inbox.
   Switch to Huda and approve. Back as Sara, My requests shows Approved with the full timeline.
2. **Conditional routing.** As Sara, send a Sick leave request. It skips Omar and goes straight to Huda
   (binding with `leaveType = Sick`, priority 1).
3. **Reject path.** Send another Annual request; reject it as Omar. The workflow ends without an HR step.
4. **Form versioning.** As a company admin (Khalid), open Form designer, add a field, for example
   `{ "key": "handoverTo", "label": "Handover to", "type": "text", "required": true }`, save the draft and publish v2.
   New requests show the new field; requests sent on v1 still render with v1 (marked as replaced).
5. **Tenant isolation.** Switch to Globex. None of Acme's requests, tasks or users are visible, and the same form key
   runs Globex's own binding. To try crossing tenants on purpose, call the API with an Acme user id and Globex's tenant id:

   ```bash
   curl -H "__tenant: b2b2b2b2-0000-0000-0000-000000000002" \
        -H "X-Demo-User: <an Acme employee id from /api/poc/users>" \
        http://localhost:5080/api/poc/submissions/mine
   ```

   The request is refused ("This user does not belong to the selected company").
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

`demo.py` runs steps 1-7 and 9 against the API and writes `demo.log` (`python3 demo.py` with the app freshly started).
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

## If it does not compile

This project was written without access to a .NET build environment, so expect small fixes on first build.
Where they are most likely:

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
| Fake sign-in from a header | ABP Identity + OpenIddict, permissions per role |
| Workflow start via `OnCompleted` after commit | `FormSubmittedEto` on ABP's distributed event bus with the outbox |
| Elsa EF Core persistence in its own SQLite file | Same server as ABP, own schema |
| Two workflows defined in C#; others admin-designed (own builder, no Elsa Studio) | Elsa Studio stays vendor-staff-only, behind OpenIddict; clients never see it (ruled out: no client code/expressions on shared servers) |
| Role catalogue is just the distinct roles already in HR data | `AssignmentRule` entities and a real role catalogue per tenant |
| Binding condition is `field == value` | Elsa expressions or a decision table |
| JSON textarea designer | SurveyJS Creator, Form.io builder, or our own builder emitting the same schema |
| Single project | ABP modular monolith: Forms, Processes, WorkflowEngine modules |

## Phase 2 (after this PoC passes)

1. ~~Switch Elsa to EF Core persistence; turn off `Poc:ResetDatabaseOnStartup`; prove a restart mid-approval resumes correctly.~~ **Done** (step 8).
2. ~~Give admins a way to draw workflows with no code.~~ **Done** (step 9, own designer — Elsa Studio ruled out for clients, see shortcuts table).
3. Enable Elsa multi-tenancy with a tenant resolver backed by ABP's `ICurrentTenant` (only relevant if Elsa Studio is hosted for vendor staff later).
4. Publish a second workflow version while requests are in flight; prove running instances keep their version (for designed workflows too, not just code-defined ones).
5. Replace the start hook with the distributed event bus + outbox; prove a crash between submit and start loses nothing.
