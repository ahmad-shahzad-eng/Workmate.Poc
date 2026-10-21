# Implementation Plan: Email notifications with per-form templates

**Branch**: `005-email-notifications` | **Date**: 2026-10-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/005-email-notifications/spec.md` (clarified 2026-10-21, 5 answers)

## Summary

Every workflow event (submitted, task assigned, sent back, approved, rejected, reassigned) and every account event (invite, password reset) writes an `EmailMessage` row and enqueues an ABP background job **in the same unit of work** as the event. After commit, the job sends it over SMTP (Mailpit locally) as multipart text + HTML, retrying up to 5 times before marking it Failed. The row doubles as the company's Email log, with Resend.

Templates are plain-text Scriban strings with an allow-listed set of placeholders, resolved form → company default → built-in at queue time. Admins edit them in a new Emails tab with preview. Account emails carry a one-time set-password link instead of a password, which retires the on-screen passwords from specs 003/004. Decisions with rationale: [research.md](./research.md).

## Technical Context

**Language/Version**: C# on `net10.0` (unchanged)

**Primary Dependencies**: ABP 9.1.0 open-source, adding `Volo.Abp.Emailing`, `Volo.Abp.TextTemplating.Scriban` (Scriban 5.10.0), `Volo.Abp.BackgroundJobs.Domain`, `Volo.Abp.BackgroundJobs.EntityFrameworkCore` (all confirmed at 9.1.0 on nuget.org, R1). Elsa unchanged.

**Storage**: Same `workmate-poc.db` and single `EnsureCreatedAsync`. New `Notif_EmailTemplates`, `Notif_CompanyEmailSettings`, `Notif_EmailMessages`, plus ABP's background-jobs table via `[ReplaceDbContext(typeof(IBackgroundJobsDbContext))]`. **Reset required once.**

**Testing**: `demo.py`. Steps 1–12 stay green, with steps 11/12 now completing accounts through the emailed link. New step 13 reads Mailpit's HTTP API ([quickstart.md](./quickstart.md)).

**Target Platform**: Local dev + Mailpit (`localhost:1025` SMTP, `:8025` API)

**Project Type**: Web service + vanilla-JS demo UI

**Performance Goals**: N/A (PoC). Mail visible in Mailpit within the 5 s job poll period.

**Constraints**: Open-source ABP only; no email sent inside a request or an uncommitted unit of work (Principle II, FR-002); templates sandboxed (FR-004); tenant-filtered everywhere (Principle IV); no password is ever emailed or displayed (FR-008).

**Scale/Scope**: US1 (P1) notifications, US2 (P1) form templates, US3 (P2) company defaults + sender name.

## Constitution Check

*GATE: checked before research and again after design. Both pass.*

| Principle / gate | Status |
| --- | --- |
| I Immutable form versions | Not affected: templates belong to the form, not a version (clarification Q2). Email wording doesn't change how a submission renders or validates. |
| II Commit before workflow | **Extended to email.** `EmailMessage` + its job row are written in the triggering unit of work; the worker only sees them after commit. Nothing is sent inline. |
| III Assignment from HR data | Not touched. Recipients are the task's resolved candidates (`Employee.Email`). |
| IV Tenant isolation | **Applies.** All new entities are `IMultiTenant`; the job re-applies `TenantId` (`CurrentTenant.Change`); template resolution never crosses tenants; `demo.py` 13d proves another company's templates are never used (SC-002). |
| V Typed reporting values | Not touched |
| VI One door into Elsa | Kept. Email hooks live in `ProcessManager`/app services, never in Elsa activities. |
| VII Admin is a permission | **Applies.** Templates use `Workmate.Forms.Design`; the Email log uses `Workmate.Employees.Manage`; account routes are anonymous by design (token-protected or always-204). |
| Gates | CHANGELOG line per task; `demo.py` step 13 covers every acceptance scenario; build → run → demo loop after one reset, with Mailpit running. |

Result: **PASS**, no exceptions. (Complexity Tracking is empty.)

## Project Structure

### Documentation (this feature)

```text
specs/005-email-notifications/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R11
├── data-model.md
├── quickstart.md        # demo.py step 13 + manual checks
├── contracts/api.md
└── tasks.md             # /speckit-tasks (not yet)
```

### Source Code (repository root, existing single project)

```text
Workmate.Poc.csproj                       # +4 ABP packages
WorkmatePocModule.cs                      # DependsOn, job worker options, token lifespan
appsettings.json                          # Abp.Mailing.Smtp.* → Mailpit; Poc:PublicBaseUrl; retry defaults
appsettings.Development.json              # new: fast retries + fail-recipient test seam
Domain/Notifications.cs                   # new: EmailEvents, EmailAudiences, EmailTemplate, CompanyEmailSettings, EmailMessage
Application/Notifications/
├── BuiltInEmailTemplates.cs              # new: default subject/body per (event, audience)
├── EmailTemplateRenderer.cs              # new: sandboxed Scriban validate + render, text → HTML layout
├── NotificationService.cs                # new: resolve template, build model, write EmailMessage, enqueue job
├── SendEmailJob.cs                       # new: AsyncBackgroundJob<SendEmailArgs>; retries, Failed after 5, account rendering
├── EmailTemplateAppService.cs            # new: form + company templates, preview, sender name
└── EmailLogAppService.cs                 # new: list + resend
Application/AccountAppService.cs          # new: forgot-password, set-password (anonymous)
Application/ProcessManager.cs             # notify on task created (assigned / sent back) and process completed
Application/SubmissionAppService.cs       # notify on submit
Application/TaskReassignmentService.cs    # notify reassigned candidates
Application/EmployeeAppService.cs         # create with no password + invite; DTO without password
Application/TenantOnboardingAppService.cs # same for the first admin; reset sends a link
Application/PasswordGenerator.cs          # deleted
Application/Dtos.cs, Data/PocDbContext.cs, Controllers/PocController.cs
wwwroot/index.html                        # Emails tab, company email settings, Email log, forgot/set-password screens
demo.py                                   # Mailpit helpers; steps 11/12 via link; step 13
```

**Structure Decision**: Keep the single ASP.NET Core project. Notification code is grouped under `Application/Notifications/`.

## Design

### 1. Module wiring

- `[DependsOn]` adds `AbpEmailingModule`, `AbpTextTemplatingScribanModule`, `AbpBackgroundJobsDomainModule`, `AbpBackgroundJobsEntityFrameworkCoreModule`.
- `Configure<AbpBackgroundJobWorkerOptions>` reads `Poc:Email:RetryFirstWaitSeconds` / `RetryWaitFactor` (R3).
- `Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(72))` (R7).
- SMTP and from-address go in `appsettings.json` → `Settings` (R10).

### 2. `NotificationService.QueueAsync(event, audience, recipients, context)`

1. Resolve the template: the form row, else the company row, else built-in (FR-003).
2. Build the model from `context` (submission, form, step, approver, comment) per R9.
3. For workflow events, render subject + text + HTML now (R5, R6).
4. For each active recipient with an email: insert an `EmailMessage` (Queued) and `IBackgroundJobManager.EnqueueAsync(new SendEmailArgs(tenantId, messageId))`.

Account events store no body (R6). Runs in the caller's unit of work, never commits on its own.

### 3. `SendEmailJob`

1. `CurrentTenant.Change(args.TenantId)`, then load the message.
2. If it's an account email, generate the token and render now.
3. Build a `MailMessage` with text and HTML `AlternateView`s, from `"{sender name}" <DefaultFromAddress>`, and send via `IEmailSender.SendAsync(mailMessage)`.
4. On success: `Sent` and `SentAt`.
5. On an exception: `Attempts++`, `LastError`. If `Attempts == 5`, mark `Failed` and return. Otherwise save and rethrow (ABP reschedules).

The Development-only `FailRecipientPrefix` seam throws before sending (R3).

### 4. Event hooks (R8)

- `SubmitAsync` → Submitted.
- `CreateTaskAsync` → SentBack (approver + initiator) if the latest completed task in the instance was Rejected, else TaskAssigned.
- `CompleteProcessAsync` → Approved/Rejected (the Rejected email includes the rejecting task's approver and comment).
- `ReassignOpenTasksAsync` → TaskReassigned.

### 5. Templates and preview (`EmailTemplateRenderer`, `EmailTemplateAppService`)

- `Validate(subject, body, allowFields)` walks Scriban's syntax tree per R4 and throws `UserFriendlyException("Unknown placeholder '{{ x.y }}'.")` or `"'include' is not allowed in email templates."`.
- `Render(template, model)` uses a fresh locked-down `TemplateContext` each time.
- Preview renders with sample data, including a field value `<b>sample</b>`.

### 6. Accounts (R7)

- `EmployeeAppService.CreateAsync` and `TenantOnboardingAppService.CreateCompanyAsync` create the user with no password and queue AccountInvite. `ResetClientAdminPasswordAsync` clears lockout and queues PasswordReset. `PasswordGenerator` is removed.
- `AccountAppService` (anonymous): `ForgotPasswordAsync` (always 204) and `SetPasswordAsync` (`ResetPasswordAsync` + clear lockout; any failure gives one generic message).

### 7. UI (`wwwroot/index.html`)

- **Form designer**: an Emails tab per form (list, edit, Preview showing subject/text/HTML in a sandboxed `iframe srcdoc`, Reset).
- **Form designer company panel**: company default templates + sender name.
- **Email log** tab (`Employees.Manage`): table, a Failed filter, Resend.
- **Sign-in screen**: a "Forgot password?" link. A `?setPassword=1&…` URL shows the set-password form, then returns to sign-in.
- **Employees and onboarding**: "Invitation sent to {email}" replaces the password notice.
- **Deep links**: `?view=inbox` / `?view=mine&submission=` open the right view after sign-in.

### 8. `demo.py`

- Mailpit helpers: `mail_clear()`, `mail_for(address, subject_contains=None, wait=10)` (`GET /api/v1/search?query=to:"…"`), `mail_link(message)`, `set_password_from_mail(address, password)`.
- Steps 11 and 12 replace `generatedPassword` with `set_password_from_mail`.
- New step 13 per quickstart. If Mailpit isn't reachable, `demo.py` fails fast with "Start Mailpit (mailpit &) first", not a cascade of failures.

## Risks (verify on first build)

- **SMTP sender in Debug**: confirm `IEmailSender` resolves to `SmtpEmailSender`, not `NullEmailSender`.
- **Job serialization**: `SendEmailArgs` must be a plain serializable class; ABP stores it as JSON in the jobs table.
- **Token lifespan**: if ABP configures `DataProtectionTokenProviderOptions` after our module, ours may be overridden. Check `TokenLifespan` at runtime.
- **Token across tenants**: the reset token must be generated and checked inside the user's tenant (`CurrentTenant.Change(tenantId)`), or the user lookup fails.
- **Steps 11/12 now need Mailpit**: `demo.py` without Mailpit fails early by design (FR-007). README setup gains Mailpit.

## Complexity Tracking

No constitution violations; nothing to justify.
