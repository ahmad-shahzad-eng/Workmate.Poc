# Quickstart: validate spec 004

## Prerequisites

- Branch `004-employees-org` built (`dotnet build`).
- **Reset once**: set `Poc:ResetDatabaseOnStartup=true` in `appsettings.json` (new tables, and the `Employee.Role` column is removed). `EnsureCreatedAsync` doesn't migrate, so set it back to `false` after the first start.
- Start the app (`dotnet run`, http://localhost:5080).

## Automated: `python3 demo.py`

All existing steps (1–7, 9–11) must still pass, since roles moved from a column to a catalogue with no change in behaviour. The new **step 12** proves SC-001 and the acceptance scenarios against the API ([contracts/api.md](./contracts/api.md)):

| Check | Proves |
|---|---|
| 12a | Vendor creates a company; its admin builds a 3-level tree (Company → Finance → Payables) and adds 6 employees with roles, managers and departments |
| 12b | A Payables employee's request routes line manager → department head → HR with no code (SC-001, US2 AC1), through a designed workflow using the new "Head of my department" step |
| 12c | The Payables head's own request skips to the Finance head (walk up, clarification Q3) |
| 12d | Manager cycle A → B → A refused (US1 AC2) |
| 12e | Duplicate email in the same company refused; the same email in another company accepted (US1 AC3) |
| 12f | Deleting a department with employees or sub-units refused (US2 AC2) |
| 12g | Deleting a role used by a published workflow refused, naming it; deleting an unused role succeeds and removes it from holders (US3) |
| 12h | Deactivating an approver with a pending task moves the task to the fallback approver, with a `TaskReassigned` timeline entry (FR-007) |
| 12i | The deactivated employee can't sign in, and their old token is refused |
| 12j | A non-admin employee can't create employees, roles or departments (403) |

Then run the restart check: `python3 demo.py restart-before`, restart the app (reset **off**), `python3 demo.py restart-after`.

## Manual (UI)

1. Sign in as the company admin and open **Employees**: add, edit, deactivate. The generated password is shown once.
2. Open **Organisation**: build the tree, set a head, try to delete a non-empty unit.
3. In **Workflow designer**, the approver list includes **Head of my department**, and the role picker lists the catalogue (not the roles of existing employees).
4. Sign in as a non-admin: the Employees and Organisation tabs aren't shown.
