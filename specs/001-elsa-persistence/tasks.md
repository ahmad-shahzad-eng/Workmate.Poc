# Tasks: Persist workflow state across restarts

Spec: [spec.md](./spec.md) · Plan: [plan.md](./plan.md)
Gate after each phase: stop old app → `dotnet build` → `dotnet run` → `python3 demo.py` (all pass). One CHANGELOG line per task.

## Phase 1: Setup

- [ ] T001 Add `Elsa.EntityFrameworkCore.Sqlite` 3.7.0 to `Workmate.Poc.csproj`
- [ ] T002 Add `ConnectionStrings:Elsa` = `Data Source=workmate-elsa.db` and set `Poc:ResetDatabaseOnStartup` to `false` in `appsettings.json`
- [ ] T003 Add `demo_restart.json` to `.gitignore`
- **Gate:** `dotnet build` restores cleanly; check `build.log` for EF Core version conflicts (plan risk 1)

## Phase 2: Foundation (blocks both stories)

- [ ] T004 Create `Data/PocDatabaseReset.cs` (delete both DB files + `-shm`/`-wal` when reset is on); call it first in `ConfigureServices` in `WorkmatePocModule.cs`
- [ ] T005 Remove `EnsureDeletedAsync` and the in-memory comment from `Data/DemoDataSeeder.cs`; keep `EnsureCreatedAsync`
- [ ] T006 Configure Elsa management + runtime EF Core SQLite persistence in `WorkmatePocModule.cs`
- **Gate:** app starts with reset on and off; `workmate-elsa.db` is created; `demo.py` steps 1–7 pass on a fresh database

## Phase 3: User Story 1 — approval survives a restart (P1)

- [ ] T007 [US1] Add `InstanceExistsAsync` to `WorkflowEngine/IWorkflowGateway.cs` and implement it in `ElsaWorkflowGateway.cs`. Confirm this is purely additive: no existing read path (Inbox, My requests, task list) is changed to query Elsa stores instead of domain tables (FR-007)
- [ ] T008 [US1] In `Application/TaskAppService.cs`, check `InstanceExistsAsync` before completing; throw `UserFriendlyException` if missing
- [ ] T009 [US1] Add `restart-before` / `restart-after` modes (step 8a–8d) to `demo.py`
- **Gate:** `demo.py restart-before` → restart app → `demo.py restart-after`: 8a–8d pass. Diff `TaskAppService.cs`/inbox query code against pre-change version to confirm domain tables (not Elsa state) remain the only read source for Inbox/My requests (FR-007)

## Phase 4: User Story 2 — data stays across restarts (P2)

- [ ] T010 [US2] Create `Application/WorkflowConsistencyCheck.cs` (log a warning per running process with a missing workflow instance); call it after seeding
- [ ] T011 [US2] Make any absolute checks in `demo.py` steps 1–7 relative so they pass on a non-empty database
- [ ] T012 [US2] Add `demo.py` check 8e: per-tenant employee and form counts unchanged across the restart (recorded in `demo_restart.json`)
- **Gate:** run `demo.py` twice with a restart in between, reset off: all pass both times (SC-002, SC-003)

## Phase 5: Polish

- [ ] T013 Manual check of the mismatch path: stop app, delete `workmate-elsa.db` only, start; confirm the startup warning in `run.log` and the clear error when approving
- [ ] T013a Manual check of definition versioning: during `restart-after`, confirm the resumed workflow instance's persisted definition version still matches the code-defined version Elsa registers on the new process start (plan.md risk "Code-defined workflows"); log any mismatch
- [ ] T014 Update `README.md` (two DB files, reset flag, step 8 commands; remove "Phase 2 item 1" from the open list) and mark Phase C done in `HANDOVER.md` §4
- [ ] T015 Final full loop: fresh DB (reset on) → steps 1–7; reset off → step 8 around a restart; clean build, no new warnings (SC-004)

## Order

T001–T003 → T004–T006 → T007–T009 → T010–T012 → T013–T015 (incl. T013a). T007 and T010 touch different files and can be done together once Phase 2 passes.
