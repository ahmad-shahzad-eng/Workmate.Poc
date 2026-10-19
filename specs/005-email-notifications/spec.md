# Feature Specification: Email notifications with per-form templates

**Feature Branch**: `005-email-notifications` | **Created**: 2026-10-20 | **Status**: Draft (seeded) | **Depends on**: 003, 004 (real users with emails)

**Input**: Ahmad, 2026-10-20: the initiator gets an email when a request is finalised (approved at the final stage, or rejected at any step); an approver gets an email when a request is waiting for them; and so on. The form designer can add templates for each email, for the initiator side and the approver side.

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
| Account invitation / password reset | New user | on (from spec 003) |

**Acceptance Scenarios**:
1. **Given** Sara submits, **Then** Omar gets "Leave request from Sara is waiting for your approval" with a link to the task.
2. **Given** HR approves at the last step, **Then** Sara gets "approved"; **given** Omar rejects at step 1, **Then** Sara gets "rejected" with Omar's comment and no HR email is sent.
3. **Given** email sending fails, **Then** the request still proceeds; the email is retried and the failure is visible to admins.

### User Story 2 - Templates per form, per event, per side (Priority: P1)
In the form designer, an "Emails" tab lists each event for the initiator and approver side. The admin can override subject and body using placeholders, preview with sample data, and reset to default.

Placeholders: `{{ request.title }}`, `{{ request.number }}`, `{{ request.status }}`, `{{ initiator.name }}`, `{{ approver.name }}`, `{{ step.title }}`, `{{ comment }}`, `{{ link }}`, `{{ company.name }}`, and form fields as `{{ fields.days }}`, `{{ fields.leaveType }}`.

**Acceptance Scenarios**:
1. **Given** an unknown placeholder, **When** saving, **Then** it is rejected with the placeholder named.
2. **Given** a form field value contains HTML, **Then** it is escaped in the email.
3. **Given** a template is saved, **Then** it is versioned with the form (published form versions keep their templates; Principle I).

### User Story 3 - Company defaults (Priority: P2)
The company admin sets default templates (and sender name) used by every form without its own override; the vendor ships built-in defaults.

## Requirements *(mandatory)*
- **FR-001**: Use ABP open-source `Volo.Abp.Emailing`, `Volo.Abp.TextTemplating.Scriban` and `Volo.Abp.BackgroundJobs` (retry); SMTP settings per environment (per tenant later).
- **FR-002**: Emails MUST be queued only after the related transaction commits (same rule as workflows, Principle II), via a background job, never sent inside the request.
- **FR-003**: Templates are tenant data; template resolution order: form version → company default → built-in default.
- **FR-004**: Templates MUST be rendered in a sandbox: only the placeholders above, no functions that reach the file system, network or other tenants' data.
- **FR-005**: Each sent/failed email is logged (recipient, event, request, status) and visible to admins.
- **FR-006**: Users can't turn off approval emails in the PoC. [NEEDS CLARIFICATION: per-user opt-out or digest later?]
- **FR-007**: `demo.py` checks emails via a local SMTP catcher (Mailpit API).

## Success Criteria *(mandatory)*
- **SC-001**: Approve path sends: submitted → assigned (manager) → assigned (HR) → approved; reject path sends: submitted → assigned → rejected (demo.py step 12, read from Mailpit).
- **SC-002**: A custom template on one form is used for that form only; another company's templates are never used.

## Files likely touched
`Workmate.Poc.csproj` (Emailing, TextTemplating.Scriban, BackgroundJobs), `WorkmatePocModule.cs`, `appsettings.json` (SMTP → Mailpit `localhost:1025`), `Domain/Forms.cs` (email templates on form version), new `Domain/Notifications.cs` (company defaults, email log), new `Application/NotificationService.cs` + `Application/EmailJob.cs`, `Application/ProcessManager.cs` (raise events on task created / completed / process completed), `Application/SubmissionAppService.cs`, `Application/TaskAppService.cs`, `Application/FormAppService.cs` (save/preview templates), `Application/Dtos.cs`, `Data/PocDbContext.cs`, `Data/DemoDataSeeder.cs` (built-in defaults), `Controllers/PocController.cs`, `wwwroot/index.html` (Emails tab), `demo.py`.
