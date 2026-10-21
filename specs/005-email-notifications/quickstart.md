# Quickstart: validate spec 005

## Prerequisites

- **Mailpit running** (SMTP `localhost:1025`, web + API `http://localhost:8025`). It's installed on this machine (`/opt/homebrew/bin/mailpit`). Start it with `mailpit &`, or `brew install mailpit` elsewhere. `demo.py` now needs it for steps 11–13, because new accounts get their password by link only.
- **Reset once** (`Poc:ResetDatabaseOnStartup=true`): new `Notif_*` tables and ABP's background-jobs table.
- `dotnet build`, then `dotnet run`.

## Automated: `python3 demo.py`

The existing steps 1–12 must still pass. Steps 11 and 12 now finish account creation through the emailed link (`demo.py` reads Mailpit, follows the link through `POST account/set-password`, then signs in). New **step 13** ([contracts/api.md](./contracts/api.md)):

| Check | Proves |
|---|---|
| 13a | Approve path (SC-001): Sara submits → Sara gets "submitted", Omar "waiting for your approval" (with a link), Huda the same after Omar approves, then Sara "approved"; nobody else gets mail for that request |
| 13b | Reject path (SC-001): Omar rejects at step 1 → Sara gets "rejected" with Omar's comment; Huda gets nothing |
| 13c | Sent back: in a designed workflow with "back to previous", a reject at step 2 emails step 1's approver and tells the initiator |
| 13d | A form-level template override is used for that form only; another form and another company keep their defaults (SC-002) |
| 13e | Unknown placeholder refused, naming it; `{{ include ... }}` refused (US2 AC1, FR-004) |
| 13f | A field value `<b>x</b>` arrives escaped in the HTML part and literal in the text part (US2 AC2) |
| 13g | Account invite: a new employee's email has a set-password link; setting a password then signing in works; reusing the link fails; the email body isn't in the Email log |
| 13h | Forgot password: 204 for a real and an unknown email alike; only the real one gets mail |
| 13i | Failure path (US1 AC3): an employee whose email starts with `fail-` submits. In Development, sends to `fail-…@` addresses always throw (`Poc:Email:FailRecipientPrefix`, honoured only when the environment is Development). The request still completes; the email shows `Failed` with `Attempts = 5` and a last error, after the fast Development retry policy (`appsettings.Development.json`: 2 s, factor 1); Resend puts it back in the queue (it then fails again, which is still a resend) |
| 13j | A non-admin can't read the Email log or edit templates (403) |

Then the restart check (`restart-before`, restart, `restart-after`). It also confirms a queued email survives a restart (persistent job store).

## Manual (UI)

1. Form designer → **Emails** tab: list of events per side, edit, preview (text + HTML), reset to default.
2. Company settings: default templates and sender name.
3. **Email log** (company admin): statuses, the last error, Resend.
4. A sign-in screen "Forgot password?" link, and the set-password page opened from an email link (Mailpit UI at http://localhost:8025).
