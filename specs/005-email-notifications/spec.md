# Feature Specification: Email notifications with per-form templates

**Feature Branch**: `005-email-notifications` | **Created**: 2026-10-20 | **Status**: Draft (seeded) | **Depends on**: 003, 004 (real users with emails)

**Input**: Ahmad, 2026-10-20: the initiator gets an email when a request is finalised (approved at the final stage, or rejected at any step); an approver gets an email when a request is waiting for them; and so on. The form designer can add templates for each email, for the initiator side and the approver side.

## Clarifications

### Session 2026-10-21
- Q: Should account emails carry a set-password link or the generated password? → A: A one-time set-password link (expires, e.g. 72 hours); the user picks their own password; the same flow covers "forgot password"; the on-screen password from specs 003/004 is removed.
- Q: Do template edits need a new published form version? → A: No. Templates belong to the form (not a version) and take effect as soon as they're saved; each email uses the template current at send time.
- Q: How many retries for a failed email, and where do admins see failures? → A: Up to 5 attempts with increasing delay (about 1 min → 1 h), then marked Failed; company admins see a per-company Email log (recipient, event, request, status, last error) and can resend a failed email.
- Q: HTML, plain text, or both? → A: Admins write plain-text subject and body (with placeholders); the system sends multipart: that text, plus an HTML version in a fixed layout (values escaped, line breaks kept, `{{ link }}` shown as a button).
- Q: Can users turn off notification emails in the PoC? → A: No. Every user gets every email in the event table; per-user preferences or a daily digest are a production follow-up.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Notify approvers and initiators (Priority: P1)

| Event | Recipient | Default |
| --- | --- | --- |
| Request submitted | Initiator | on (confirmation) |
| Task assigned (step waiting) | Every candidate approver of the step | on |
| Request approved (final step) | Initiator | on |
| Request rejected (any step) | Initiator | on |
| Sent back to a previous step | Approvers of that step; initiator (info) | on |
| Step reassigned to fallback | Fallback approvers | on |
| Account invitation / password reset | New user | on: a one-time set-password link, never the password itself (replaces the on-screen password of specs 003/004) |

**Acceptance Scenarios**:
1. **Given** Sara submits, **Then** Omar gets "Leave request from Sara is waiting for your approval" with a link to the task.
2. **Given** HR approves at the last step, **Then** Sara gets "approved"; **given** Omar rejects at step 1, **Then** Sara gets "rejected" with Omar's comment and no HR email is sent.
3. **Given** email sending fails, **Then** the request still proceeds; the email is retried up to 5 times with increasing delay (about 1 min → 1 h), then marked Failed. The company admin sees it in the Email log with the last error and can resend it.

### User Story 2 - Templates per form, per event, per side (Priority: P1)
In the form designer, an "Emails" tab lists each event for the initiator and approver side. The admin can override the subject and a plain-text body using placeholders, preview with sample data (both the text and the HTML version), and reset to default. Admins never write HTML: the system sends each email as multipart, the admin's text plus an HTML version in a fixed layout.

Placeholders: `{{ request.title }}`, `{{ request.number }}`, `{{ request.status }}`, `{{ initiator.name }}`, `{{ approver.name }}`, `{{ step.title }}`, `{{ comment }}`, `{{ link }}`, `{{ company.name }}`, and form fields as `{{ fields.days }}`, `{{ fields.leaveType }}`.

**Acceptance Scenarios**:
1. **Given** an unknown placeholder, **When** saving, **Then** it is rejected with the placeholder named.
2. **Given** a form field value contains HTML, **Then** it is escaped in the HTML part and shown as literal text in the text part.
3. **Given** a template is saved, **Then** the next email for that form and event uses it, with no new form version needed. Email wording doesn't affect how a submission renders or validates, so Principle I doesn't apply.

### User Story 3 - Company defaults (Priority: P2)
The company admin sets default templates (and sender name) used by every form without its own override; the vendor ships built-in defaults.

## Requirements *(mandatory)*
- **FR-001**: Use ABP open-source `Volo.Abp.Emailing`, `Volo.Abp.TextTemplating.Scriban` and `Volo.Abp.BackgroundJobs` (retry); SMTP settings per environment (per tenant later).
- **FR-002**: Emails MUST be queued only after the related transaction commits (same rule as workflows, Principle II), via a background job, never sent inside the request.
- **FR-003**: Templates are tenant data owned by the form (not a form version); template resolution order: form → company default → built-in default, read at send time.
- **FR-004**: Templates MUST be rendered in a sandbox: only the placeholders above, no functions that reach the file system, network or other tenants' data.
- **FR-005**: Each email is logged (recipient, event, request, status Queued/Sent/Failed, attempts, last error) and visible to that company's admins in an Email log, tenant-filtered; a Failed email can be resent from there.
- **FR-006**: Users can't turn off any notification email in the PoC; every event in the US1 table is always sent. Per-user preferences and a daily digest are production follow-ups, not built here.
- **FR-007**: `demo.py` checks emails via a local SMTP catcher (Mailpit API).
- **FR-008**: Account emails (new company admin, new employee, password reset requested by an admin or by the user) MUST carry a one-time set-password link built on ABP Identity's password-reset token, valid for a limited time (e.g. 72 hours) and unusable after first use. No password is ever emailed or shown on screen; creating an account no longer returns a generated password.

## Success Criteria *(mandatory)*
- **SC-001**: Approve path sends: submitted → assigned (manager) → assigned (HR) → approved; reject path sends: submitted → assigned → rejected (new demo.py step 13, read from Mailpit; step 12 is spec 004).
- **SC-002**: A custom template on one form is used for that form only; another company's templates are never used.

## Files likely touched
`Workmate.Poc.csproj` (Emailing, TextTemplating.Scriban, BackgroundJobs), `WorkmatePocModule.cs`, `appsettings.json` (SMTP → Mailpit `localhost:1025`), new `Domain/Notifications.cs` (form templates, company defaults, email log), new `Application/NotificationService.cs` + `Application/EmailJob.cs`, `Application/ProcessManager.cs` (raise events on task created / completed / process completed), `Application/SubmissionAppService.cs`, `Application/TaskAppService.cs`, `Application/FormAppService.cs` (save/preview templates), `Application/Dtos.cs`, `Data/PocDbContext.cs`, `Data/DemoDataSeeder.cs` (built-in defaults), `Controllers/PocController.cs`, `wwwroot/index.html` (Emails tab), `demo.py`.
