# Implementation Plan: Persist workflow state across restarts

**Branch**: `001-elsa-persistence` | **Date**: 2026-10-20 | **Spec**: [spec.md](./spec.md)

## Summary

Move Elsa from in-memory stores to its open-source EF Core SQLite persistence in a separate file (`workmate-elsa.db`), turn the startup reset off by default (reset now clears both files before anything opens them), make task completion check that the workflow instance still exists, log a warning at startup for running processes whose workflow instance is missing, and add a two-part restart check to `demo.py`.

## Technical Context

- **Language/Version**: C# on `net10.0`
- **Primary Dependencies**: ABP 9.1.0 (open source), Elsa 3.7.0; **new:** `Elsa.EntityFrameworkCore.Sqlite` 3.7.0 (approved 2026-10-20)
- **Storage**: SQLite. Domain: `workmate-poc.db` (`EnsureCreated`). Workflows: `workmate-elsa.db` (Elsa's own migrations, run at startup)
- **Testing**: `demo.py` (stdlib) against the running app; manual restart for step 8
- **Constraints**: no ABP/Elsa version change; no Elsa calls outside the gateway

## Constitution Check

| Principle / gate | Status |
| --- | --- |
| I Immutable form versions | Not touched |
| II Commit before workflow | Unchanged: start and resume stay in `OnCompleted`. The new existence check runs *before* commit and only reads |
| III Assignment from HR data | Not touched |
| IV Tenant isolation | Tenant id already in workflow input; persisted with the instance, so re-applied after resume. Step 8 covers both tenants |
| V Typed reporting values | Not touched |
| VI One door into Elsa | Persistence set up in module config only; new `InstanceExistsAsync` lives in the gateway |
| VII Admin is a permission | Not touched |
| Gates | CHANGELOG line per change; `demo.py` step 8 added; build/run/demo loop must pass |

Result: **PASS**, no exceptions needed.

## Design

### 1. Package and Elsa configuration (`Workmate.Poc.csproj`, `WorkmatePocModule.cs`)

Add `Elsa.EntityFrameworkCore.Sqlite` 3.7.0. In `AddElsa`:

```csharp
var elsaDb = configuration.GetConnectionString("Elsa"); // "Data Source=workmate-elsa.db"
elsa.UseWorkflowManagement(m => m.UseEntityFrameworkCore(ef => ef.UseSqlite(elsaDb)));
elsa.UseWorkflowRuntime(r => r.UseEntityFrameworkCore(ef => ef.UseSqlite(elsaDb)));
```

Add `ConnectionStrings:Elsa` to `appsettings.json`. Elsa runs its own migrations at startup (default `RunMigrations = true`).

### 2. Reset before anything opens the files (`Data/PocDatabaseReset.cs`, new)

Today the reset (`EnsureDeletedAsync`) runs in the seeder during app initialization. Elsa's migrations run from a hosted service, which can open `workmate-elsa.db` first, so the reset must move earlier.
`PocDatabaseReset.Apply(configuration)` is called at the top of `ConfigureServices`: if `Poc:ResetDatabaseOnStartup` is true, delete both SQLite files (plus `-shm`/`-wal`). The seeder keeps only `EnsureCreatedAsync()`. Default becomes `false` in `appsettings.json`.

### 3. Gateway: instance existence (`IWorkflowGateway`, `ElsaWorkflowGateway`)

Add `Task<bool> InstanceExistsAsync(string workflowInstanceId)`, implemented with Elsa's `IWorkflowInstanceStore.FindAsync(new WorkflowInstanceFilter { Id = ... })`.

### 4. Task completion check (`Application/TaskAppService.cs`)

Before marking the task completed, call `InstanceExistsAsync(task.ElsaWorkflowInstanceId)`. If false, throw `UserFriendlyException("This request's workflow can no longer be found. Ask an admin to reset the demo.")`; the unit of work rolls back, so the task stays pending (spec edge case).

### 5. Startup consistency warning (`Application/WorkflowConsistencyCheck.cs`, new)

After seeding, for each demo tenant: load `ProcessInstance` rows with `Status == Running` and a non-null `ElsaWorkflowInstanceId`; log a warning for each one where `InstanceExistsAsync` is false. Never throws. Called from `OnApplicationInitializationAsync` after the seeder.

### 6. `demo.py` step 8 (two parts)

- `python3 demo.py restart-before`: in Acme, Sara submits Annual leave (A1) and Sick leave (A2, goes to HR); in Globex, Ali submits leave (G1). Waits for the first task of each. Writes ids to `demo_restart.json` (gitignored).
- Ahmad restarts the app (reset off).
- `python3 demo.py restart-after`: reads the ids, then: Omar approves A1 → Huda gets a task → approves → Approved (8a); Huda rejects A2 → Rejected, no further tasks (8b); Fatima approves G1 → Yousef approves → Approved (8c); Acme users see none of Globex's ids and vice versa (8d).
- `python3 demo.py` with no argument runs steps 1–7 as today.

### 7. Steps 1–7 on a non-empty database

Review checks that count rows or assume first-run state (step 4 is already relative). Make any absolute checks relative to the state at the start of the step.

## Risks and research (verify on first build)

- **EF Core version alignment.** Elsa 3.7's EF packages and ABP 9.1's must resolve to the same `Microsoft.EntityFrameworkCore.*` major. If restore picks two, pin the higher one explicitly and note it in CHANGELOG.
- **Elsa API names.** `UseWorkflowManagement`/`UseWorkflowRuntime` + `UseEntityFrameworkCore(ef => ef.UseSqlite(...))` are the 3.x pattern; if 3.7 differs, change only `WorkmatePocModule.cs`.
- **Code-defined workflows.** Elsa re-registers code workflows on start; check the persisted definition version stays the same across restarts (spec edge case). If it bumps, running instances must still resume on their own version.
- **SQLite locking.** Two files, two connection pools; no cross-file transactions are needed because start/resume already happen after commit.

## Files touched

`Workmate.Poc.csproj`, `appsettings.json`, `WorkmatePocModule.cs`, `Data/PocDatabaseReset.cs` (new), `Data/DemoDataSeeder.cs`, `WorkflowEngine/IWorkflowGateway.cs`, `WorkflowEngine/ElsaWorkflowGateway.cs`, `Application/TaskAppService.cs`, `Application/WorkflowConsistencyCheck.cs` (new), `demo.py`, `.gitignore`, `README.md`, `CHANGELOG.md`.
