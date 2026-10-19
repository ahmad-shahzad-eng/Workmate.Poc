# Workmate HRM PoC Constitution

Scope: the Forms & Workflow PoC in this folder (namespaces `Workmate.Poc`). Purpose: prove ABP + Elsa 3 + a JSON-schema form layer before the full build.

## Core Principles

### I. Immutable form versions
Published form versions never change. A submission always renders and validates with the version it was submitted on.

### II. Commit before workflow
Workflows start and resume only after the related transaction commits. No workflow side effect may run inside an uncommitted unit of work.

### III. Assignment from HR data
Task candidates are resolved at run time by `AssignmentResolver` from rules stored as data (`line-manager`, `manager-of-manager`, `role:{Role}`, `user:{EmployeeId}`). Code-defined workflows never name specific people. A company admin may pick a specific user in a designed workflow; that user must belong to the tenant, and a rule that resolves to nobody or only to the submitter goes to a defined fallback, never leaves a request stuck.

### IV. Tenant isolation (NON-NEGOTIABLE)
Tenant id travels in workflow input and is re-applied inside every activity. Nothing bypasses ABP's tenant filter. Every feature that touches data proves isolation in `demo.py`.

### V. Typed reporting values
Reportable fields are written to `Forms_SubmissionValues` as typed values at submit time. Analytics never parses submission JSON.

### VI. One door into Elsa
All calls to Elsa's runtime and stores stay inside `IWorkflowGateway` / `ElsaWorkflowGateway`. Domain records (submission, process, task, timeline) are the system of record, not Elsa state.

### VII. Admin is a permission
Form design and routing are admin-only (company admin flag or vendor admin). Everyone else only takes part in workflows. A company admin is still an ordinary employee for submitting and approving.

## Technology and Boundaries

- Stack: `net10.0`, ABP 9.1.0 (open-source packages only, all `Volo.Abp.*` on one version), Elsa 3.7.x, SQLite for the PoC.
- No commercial packages (ABP Pro, SurveyJS Creator, etc.) and no Elsa or ABP version change without Ahmad's approval.
- Never change the architecture or data model just to make something compile; adapt to the library API. If a principle cannot be kept, stop and ask.
- Do not push to any remote or touch `workmate-platform-api`.
- The fake sign-in (`X-Demo-User`) and the PoC admin check stay until a phase explicitly replaces them.

## Development Workflow and Quality Gates

- Changes are small, explained and easy to review; one feature per spec, in the plan order of HANDOVER.md §4.
- Every change gets a one-line entry in `CHANGELOG.md`.
- Every feature adds `demo.py` checks for its acceptance scenarios.
- Acceptance gate for every task: stop the old app, `dotnet build`, `dotnet run`, `python3 demo.py`; all steps pass (logs: `build.log`, `run.log`, `demo.log`).
- Open decisions (HANDOVER.md §3–§4) are raised in `/speckit-clarify`, never assumed.

## Governance

This constitution overrides other practices in this repo. If code and the design doc disagree, follow the design rules and tell Ahmad. Amendments need Ahmad's approval, a version bump and a CHANGELOG line. Every plan's Constitution Check lists Principles I–VII and the gates above.

**Version**: 1.1.0 | **Ratified**: 2026-10-20 | **Last Amended**: 2026-10-20 (III: specific-user steps, approved by Ahmad)
