# Implementation Plan: Client-admin approval route designer

**Branch**: `002-route-designer` | **Date**: 2026-10-20 | **Spec**: [spec.md](./spec.md)

## Summary

Add a tenant-owned "designed workflow" concept (`ApprovalWorkflow` / `ApprovalWorkflowVersion`, draft + published, stored like forms) that a company admin builds step by step (title, approver type, on-reject action) with a required fallback approver. Publishing turns the steps into a plain Elsa activity graph (`HumanTask`/`CompleteProcess`, wired with a `Flowchart` so a step can send a reject back to the previous step) and persists it through Elsa's own definition publisher — still only from inside the gateway. `AssignmentResolver` gains `manager-of-manager` and `user:{EmployeeId}`, always excludes the submitter from their own request's candidates, and falls back when a rule resolves to nobody. Routing's condition-value input becomes type-aware. `demo.py` gains step 9.

## Technical Context

- **Language/Version**: C# on `net10.0` (unchanged)
- **Primary Dependencies**: ABP 9.1.0, Elsa 3.7.0 (unchanged) — **no new packages**; this feature is built entirely on APIs already in the restored `Elsa` / `Elsa.Persistence.EFCore.Sqlite` packages (confirmed by reflecting on the installed 3.7.0 assemblies, see Risks)
- **Storage**: SQLite, same two files as Phase C. New tables `Proc_ApprovalWorkflows`, `Proc_ApprovalWorkflowVersions` in `workmate-poc.db` (`EnsureCreated`, like every other domain table). Elsa's own `workmate-elsa.db` gains rows the same way code-defined workflows already do — no schema change there.
- **Testing**: `demo.py` step 9 (new)
- **Constraints**: no ABP/Elsa version change; no Elsa calls outside the gateway; clients get no code/expression input (steps are structured data only)

## Constitution Check

| Principle / gate | Status |
| --- | --- |
| I Immutable form versions | Not touched |
| II Commit before workflow | Unchanged: publishing writes to Elsa's store directly (not a workflow side effect); start/resume stay in `OnCompleted` |
| III Assignment from HR data | Extended, as already approved in constitution v1.1.0: `manager-of-manager`, `user:{EmployeeId}` (tenant-checked), fallback when a rule resolves to nobody or only the submitter. Still resolved only by `AssignmentResolver` from data, never hard-coded |
| IV Tenant isolation | `ApprovalWorkflow`/`Version` are `IMultiTenant`; a tenant's workflow list and binding validation are read from these tables (ABP's filter), not from asking Elsa to filter — see Design §3. Elsa's own `WorkflowDefinition.TenantId` is set too, as a second layer, not the enforcement point |
| V Typed reporting values | Not touched |
| VI One door into Elsa | New `IWorkflowGateway.PublishDesignedWorkflowAsync` is the only place that calls `IWorkflowDefinitionPublisher`; the activity graph itself is built by a plain, Elsa-type-only builder with no service calls, so it can be unit-reasoned-about without a gateway, but nothing *persists* outside the gateway |
| VII Admin is a permission | Workflow design is admin-only via the existing `CurrentEmployeeProvider.EnsureAdminAsync()`, same as forms/routing |
| Gates | CHANGELOG line per change; `demo.py` step 9 added; build/run/demo loop must pass |

Result: **PASS**, no exceptions needed.

## Design

### 1. Storage: `ApprovalWorkflow` / `ApprovalWorkflowVersion` (`Domain/ApprovalWorkflows.cs`, new)

Mirrors `FormDefinition`/`FormDefinitionVersion` exactly (draft/published, `CurrentVersionId`, immutable once published):

```csharp
public class ApprovalWorkflow : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId; public string Key; public string Name; public bool IsActive = true; public Guid? CurrentVersionId;
}

public class ApprovalWorkflowVersion : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId; public Guid ApprovalWorkflowId; public int VersionNo;
    public string StepsJson;              // ordered steps, see ApprovalWorkflowSchema below
    public string FallbackRule;           // "role:{Role}" or "user:{EmployeeId}" — required to publish
    public string Status;                 // Draft | Published | Retired (FormVersionStatus values, reused)
    public string? ElsaDefinitionId;       // set on publish
    public int? ElsaDefinitionVersion;    // Elsa's own version number for this publish
    public DateTime? PublishedAt;
}
```

`ApprovalWorkflowSchema.Parse(stepsJson)` (same file, mirrors `FormSchema.Parse`): each step has `Title`, `ApproverType` (`line-manager` | `manager-of-manager` | `role` | `user`), `Role`/`UserId` (present only when the type needs it), `OnReject` (`end` | `back-to-previous`). Throws `UserFriendlyException` naming the step on a missing title, a type without its required follow-up value, or zero steps (US1 scenario 5). Tenant membership of a `user:` step's employee and of the fallback is checked in the app service (needs `CurrentTenant`/repository access, not available to a pure parser), as is `role:` resolving to a role that actually exists in HR data for that tenant.

Add to `Data/PocDbContext.cs`: two `DbSet`s, `ToTable("Proc_ApprovalWorkflows")` / `"Proc_ApprovalWorkflowVersions")`, same `ConfigureByConvention()` + unique index pattern as `FormDefinition`/`FormDefinitionVersion` (`(TenantId, Key)` and `(ApprovalWorkflowId, VersionNo)`).

### 2. `AssignmentResolver` changes (`Application/AssignmentResolver.cs`)

- Add `manager-of-manager` (submitter → manager → manager's manager) and `user:{EmployeeId}` (single employee, tenant-filtered repository lookup so a cross-tenant id simply returns nobody).
- **The submitter is now excluded from their own request's candidates for every rule**, not just designer-built ones (`people.Where(p => p.Id != submitterUserId)`). This also closes a latent gap in the two existing workflows (e.g. an HR employee submitting their own request through `hr-only-approval` could previously approve themselves).
- New optional parameter `fallbackRule`: if the (submitter-excluded) result is empty, resolve `fallbackRule` instead (when provided) and have the caller record why. Legacy code-defined workflows pass no fallback, so they keep today's behaviour exactly (empty candidates → the existing "stuck, rule or HR data needs fixing" event in `ProcessManager.CreateTaskAsync`).

### 3. Running a designed workflow: `HumanTask` gets a `FallbackRule` input (`WorkflowEngine/Activities/HumanTask.cs`)

`public Input<string>? FallbackRule` (optional; unset for `LeaveApprovalWorkflow`/`HrOnlyApprovalWorkflow`, so FR-011 holds with zero changes to those two files). `ProcessManager.CreateTaskAsync` passes it to `AssignmentResolver.ResolveAsync` and adds the "resolved via fallback: {reason}" detail to the timeline event when it was used (FR-010, US2 scenario 2).

### 4. Turning steps into an Elsa definition (`WorkflowEngine/ApprovalWorkflowGraphBuilder.cs`, new — no Elsa service calls, just the object graph)

Linear steps with named flow outcomes need a `Flowchart`, not a `Sequence`+`If` chain, because "reject → back to the previous step" is a graph back-edge:

```csharp
var nodes = steps.Select(s => new HumanTask
{
    StepKey = new(s.Key), TaskTitle = new(s.Title), AssignmentRule = new(s.ResolvedRule),
    FallbackRule = new(fallbackRule)
}).ToList();

var flowchart = new Flowchart { Start = nodes[0] };
for (var i = 0; i < nodes.Count; i++)
{
    var approvedTarget = i + 1 < nodes.Count ? (IActivity)nodes[i + 1] : approvedEnd; // CompleteProcess{Approved}
    flowchart.Connections.Add(new Connection(new Endpoint(nodes[i], "Approved"), new Endpoint(approvedTarget, "In")));

    var rejectTarget = steps[i].OnReject == "back-to-previous" && i > 0 ? nodes[i - 1] : rejectedEnd; // CompleteProcess{Rejected}
    flowchart.Connections.Add(new Connection(new Endpoint(nodes[i], "Rejected"), new Endpoint(rejectTarget, "In")));
}
```

(`HumanTask` already declares `[FlowNode("Approved", "Rejected")]` — added for exactly this, per its own doc comment — so this is additive, not a reinterpretation of an existing activity.) The two `CompleteProcess` end nodes carry `FinalStatus = new(SubmissionStatus.Approved)` / `new(SubmissionStatus.Rejected)`.

### 5. Gateway: publish the graph (`IWorkflowGateway`, `ElsaWorkflowGateway`)

```csharp
Task<(string DefinitionId, int Version)> PublishDesignedWorkflowAsync(string definitionId, string name, Guid tenantId, IActivity root);
```

Implemented with `IWorkflowDefinitionPublisher` (confirmed on `Elsa.Workflows.Management`, 3.7.0):

```csharp
var definition = await _publisher.NewAsync(root);
definition.DefinitionId = definitionId; definition.Name = name; definition.TenantId = tenantId.ToString();
var published = await _publisher.PublishAsync(definition);
return (published.DefinitionId, published.Version);
```

`definitionId` is a new GUID generated once per `ApprovalWorkflow` (stable across its versions, like `FormDefinition.Key` is stable across form versions); each publish creates a new Elsa version under the same id, exactly like Elsa's own versioning for code-defined workflows — so FR-008 (running requests keep their version) falls out of Elsa's existing `VersionOptions.Published` resolution for free.

### 6. Tenant isolation is enforced by our own tables, not by asking Elsa to filter

`WorkflowDefinition.TenantId` and `WorkflowDefinitionFilter.TenantAgnostic` exist on the Elsa side, but whether EF Core persistence applies that filter automatically (it likely depends on an ambient tenant accessor we have not wired up) is unconfirmed — see Risks. The plan does not depend on it:

- **Listing** (`RoutingAppService.GetWorkflowsAsync`, admin's workflow picker): merge `_gateway.ListPublishedAsync()` (the two global code-defined workflows, unchanged) with a new tenant-filtered read of `ApprovalWorkflowVersion` where `Status == Published`, for the current tenant only (ABP's repository filter does this for free — same mechanism `ProcessBinding`/`FormDefinition` already rely on).
- **Binding validation** (`RoutingAppService.ValidateAsync`): a `WorkflowDefinitionId` that isn't one of the two global ids must resolve to a `Published` `ApprovalWorkflowVersion` **in the current tenant**; otherwise refused, same error shape as today's "not published". A Globex admin can never discover or bind Acme's designer-built id this way, independent of whatever Elsa itself would have allowed (SC-002, US2 scenario 3).

### 7. Routing condition values adapt to field type (FR-009, US3)

Extract the per-type check already in `SubmissionAppService.Validate` (number/date/select-options) into `FormField.ValidateValue(string value)` on `Domain/Forms.cs`, reused by `RoutingAppService.ValidateAsync` for `ConditionValue` against the field named by `ConditionField`, on the form's **latest** version (already loaded there). UI: `wwwroot/index.html` routing section picks the input control (dropdown/Yes-no/number/date/text) from the chosen field's `type`, same pattern the existing field editor already uses for forms.

### 8. New admin surface

- `Application/ApprovalWorkflowAppService.cs` (new): `GetAllAsync`, `CreateAsync`, `GetDesignAsync`, `SaveDraftAsync`, `PublishAsync` — same shape as `FormAppService`. `PublishAsync` parses + validates steps/fallback (tenant membership for `user:`/`role:` targets), builds the graph (§4), calls the gateway (§5), retires the previous version, stores the returned `ElsaDefinitionId`/`ElsaDefinitionVersion`.
- `Controllers/PocController.cs`: `GET/POST /workflows/design` routes mirroring the forms-design routes.
- `wwwroot/index.html`: "Workflow designer" tab (admin-only, next to Form designer) — step list editor (add/reorder/remove), approver-type dropdown with the matching follow-up control, fallback picker, draft/publish buttons.

## Risks and research (verify on first build)

- **Dynamically-published definitions actually run.** `IWorkflowDefinitionPublisher.New/PublishAsync` building from a hand-built `IActivity` root (rather than a `WorkflowBase` subclass found by `AddWorkflowsFrom`) is confirmed to exist in 3.7.0, but whether the resulting definition resumes bookmarks through our custom `HumanTask`/`CompleteProcess` activities exactly like a code-defined one needs a real end-to-end run (first `demo.py` step 9 attempt).
- **`Flowchart`/`Connection`/`Endpoint` wiring.** Confirmed types and constructors (`Connection(Endpoint, Endpoint)`, `Endpoint(IActivity, string port)`) via reflection on the installed assembly; the exact port name strings ("Approved"/"Rejected" from `HumanTask`'s `[FlowNode]`, "In" as the default inbound port) need confirming against how Elsa's flowchart runtime actually reads `[FlowNode]` outcomes — adjust only `ApprovalWorkflowGraphBuilder.cs` if the port names differ.
- **`WorkflowDefinition.TenantId` / `WorkflowDefinitionFilter.TenantAgnostic`.** Exist on the entity/filter; not relied on for enforcement (§6), but confirm setting `TenantId` doesn't throw or interact badly with `IWorkflowDefinitionPublisher`.
- **EF Core migration for the two new tables.** Same `EnsureCreated` mechanism as every other domain table; no Elsa-side migration involved (Elsa manages its own schema, unaffected by our new tables living in `workmate-poc.db`).

## Files touched

`Domain/ApprovalWorkflows.cs` (new), `Domain/Forms.cs` (extract `FormField.ValidateValue`), `Data/PocDbContext.cs`, `Application/AssignmentResolver.cs`, `Application/ProcessManager.cs`, `Application/SubmissionAppService.cs` (use shared validator), `Application/RoutingAppService.cs`, `Application/ApprovalWorkflowAppService.cs` (new), `Application/Dtos.cs`, `WorkflowEngine/Activities/HumanTask.cs`, `WorkflowEngine/ApprovalWorkflowGraphBuilder.cs` (new), `WorkflowEngine/IWorkflowGateway.cs`, `WorkflowEngine/ElsaWorkflowGateway.cs`, `Controllers/PocController.cs`, `wwwroot/index.html`, `Data/DemoDataSeeder.cs` (seed a `Finance` role person per spec's Assumptions, needed for the demo workflow), `demo.py`, `README.md`, `CHANGELOG.md`, `HANDOVER.md`.
