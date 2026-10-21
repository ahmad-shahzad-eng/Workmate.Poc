# Tasks: Email notifications with per-form templates

**Input**: [spec.md](./spec.md) · [plan.md](./plan.md) · [research.md](./research.md) · [data-model.md](./data-model.md) · [contracts/api.md](./contracts/api.md) · [quickstart.md](./quickstart.md)

**Tests**: The constitution requires `demo.py` checks for every acceptance scenario, so each story phase ends with its step-13 checks (read from Mailpit's API). There are no unit-test projects.

**Gate after each phase**: Mailpit running (`mailpit &`) → stop the old app (check port 5080 is free; a stale instance made one spec 004 gate run test old code) → `dotnet build` → `dotnet run` → `python3 demo.py` → restart check. One CHANGELOG line per task.

**Reset**: Phase 2 adds tables, so set `Poc:ResetDatabaseOnStartup=true` for its first run, then back to `false`.

**Reminder** (spec 004): `ProcessTaskCandidate.UserId` and `FormSubmission.SubmittedByUserId` hold an **`Employee.Id`**; an employee's sign-in id is `Employee.UserId`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 notifications, US2 form templates, US3 company defaults (spec.md)

---

## Phase 1: Setup

- [ ] T001 In `Workmate.Poc.csproj` add `Volo.Abp.Emailing`, `Volo.Abp.TextTemplating.Scriban`, `Volo.Abp.BackgroundJobs.Domain`, `Volo.Abp.BackgroundJobs.EntityFrameworkCore`, all `Version="9.1.0"` (research R1). In `WorkmatePocModule.cs` add `typeof(AbpEmailingModule)`, `typeof(AbpTextTemplatingScribanModule)`, `typeof(AbpBackgroundJobsDomainModule)`, `typeof(AbpBackgroundJobsEntityFrameworkCoreModule)` to `[DependsOn]`.
- [ ] T002 In `WorkmatePocModule.ConfigureServices`:
  - `Configure<AbpBackgroundJobWorkerOptions>(o => { o.DefaultFirstWaitDuration = config.GetValue("Poc:Email:RetryFirstWaitSeconds", 60); o.DefaultWaitFactor = config.GetValue("Poc:Email:RetryWaitFactor", 2.5); })`
  - `Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(72))` (research R3, R7)
- [ ] T003 [P] In `appsettings.json`:
  - add `"Settings": { "Abp.Mailing.Smtp.Host": "localhost", "Abp.Mailing.Smtp.Port": "1025", "Abp.Mailing.Smtp.EnableSsl": "false", "Abp.Mailing.Smtp.UseDefaultCredentials": "false", "Abp.Mailing.DefaultFromAddress": "no-reply@workmate.local", "Abp.Mailing.DefaultFromDisplayName": "Workmate" }`
  - under `Poc`: `"PublicBaseUrl": "http://localhost:5080"`, `"Email": { "RetryFirstWaitSeconds": 60, "RetryWaitFactor": 2.5 }`
  - Create `appsettings.Development.json` with `{ "Poc": { "Email": { "RetryFirstWaitSeconds": 2, "RetryWaitFactor": 1, "FailRecipientPrefix": "fail-" } } }` (research R3).
- [ ] T004 In `Data/PocDbContext.cs`: add `[ReplaceDbContext(typeof(IBackgroundJobsDbContext))]`, implement `IBackgroundJobsDbContext` (its `DbSet<BackgroundJobRecord> BackgroundJobs`), and call `b.ConfigureBackgroundJobs();` next to the other module `Configure*()` calls.
- **Gate**: `dotnet build` succeeds.

---

## Phase 2: Foundational (blocks every story)

**Purpose**: The notification pipeline (entities, built-in templates, sandboxed renderer, queueing, sending job) and `demo.py`'s Mailpit helpers. Nothing calls it yet.

- [ ] T005 [P] Create `Domain/Notifications.cs` (data-model.md):
  - `EmailEvents` (`Submitted`, `TaskAssigned`, `SentBack`, `Approved`, `Rejected`, `TaskReassigned`, `AccountInvite`, `PasswordReset`) and `EmailAudiences` (`Initiator`, `Approver`, `Account`), plus a `static readonly (string Event, string Audience)[] ValidPairs` exactly per data-model.md's table, and `IsWorkflowPair`/`IsAccountPair` helpers.
  - `EmailTemplate : AggregateRoot<Guid>, IMultiTenant` { `TenantId`, `FormDefinitionId` (Guid?; null = company default), `Event`, `Audience`, `Subject`, `Body` }.
  - `CompanyEmailSettings : AggregateRoot<Guid>, IMultiTenant` { `TenantId`, `SenderName` (string?) }.
  - `EmailMessage : CreationAuditedAggregateRoot<Guid>, IMultiTenant` { `TenantId`, `Event`, `Audience`, `RecipientEmployeeId?`, `RecipientUserId?`, `RecipientEmail`, `SubmissionId?`, `Subject`, `TextBody?`, `HtmlBody?` ("null for account emails"), `Status` (`EmailStatus.Queued|Sent|Failed`), `Attempts` ("≤ 5"), `LastError?` ("≤ 1000 characters"), `SentAt?` }.
- [ ] T006 In `Data/PocDbContext.cs` (depends on T004, T005): DbSets and `b.Entity<...>` for the three, tables `Notif_EmailTemplates` (unique index `TenantId, FormDefinitionId, Event, Audience`), `Notif_CompanyEmailSettings` (unique `TenantId`), `Notif_EmailMessages` (index `TenantId, Status`).
- [ ] T007 [P] Create `Application/Notifications/BuiltInEmailTemplates.cs`: `static (string Subject, string Body) Get(string evt, string audience)` for every valid pair, using only allowed placeholders. For example TaskAssigned/Approver: subject `{{ request.title }} from {{ initiator.name }} is waiting for your approval`, body with `{{ step.title }}`, `{{ request.number }}`, `{{ link }}`. Rejected/Initiator includes `{{ if comment }}Comment: {{ comment }}{{ end }}` and `{{ approver.name }}`. AccountInvite/PasswordReset bodies include `{{ link }}` and say the link works once and expires in 72 hours.
- [ ] T008 [P] Create `Application/Notifications/EmailTemplateRenderer.cs` (`ITransientDependency`; research R4, R5):
  - `void Validate(string subject, string body, ISet<string>? allowedFieldKeys)`: `Template.Parse`; throw `UserFriendlyException` on parse errors. Walk the syntax tree (Scriban `ScriptVisitor`): allow only raw text, `ScriptIfStatement`/`else`/`end`, and expressions that are an allowed placeholder path (`request.title|number|status`, `initiator.name`, `approver.name`, `step.title`, `comment`, `link`, `company.name`, `fields.<key>` only when `allowedFieldKeys` contains `<key>`; `fields.*` not allowed when it's null). Reject anything else, naming the first offender: "Unknown placeholder '{{ x }}'." or "'include' is not allowed in email templates.".
  - `RenderedEmail Render(subject, body, EmailModel model)`: a fresh `TemplateContext { TemplateLoader = null, LoopLimit = 100, RecursiveLimit = 20 }` with one `ScriptObject` built from `model`. Subject: CR/LF stripped, capped at 200 characters. Text = the rendered body. Html = a fixed layout: company name header, `HtmlEncoder.Default.Encode(text)` with `\n` → `<br>` and `http(s)://…` → anchors, a button for `model.Link` when set.
  - `record EmailModel(...)` and `record RenderedEmail(string Subject, string Text, string Html)`.
- [ ] T009 Create `Application/Notifications/SendEmailJob.cs` (depends on T005, T008; research R3, R6, R7). `SendEmailArgs { Guid? TenantId; Guid EmailMessageId; }` is a plain serializable class. `SendEmailJob : AsyncBackgroundJob<SendEmailArgs>, ITransientDependency`:
  1. `using (CurrentTenant.Change(args.TenantId))`, own unit of work; load the message; skip it if `Sent`.
  2. For account emails, generate a fresh token (`IdentityUserManager.GeneratePasswordResetTokenAsync` for `RecipientUserId`), build the link `{PublicBaseUrl}/?setPassword=1&tenant={tenantId}&user={userId}&token={Uri.EscapeDataString(token)}`, and render with the resolved company/built-in account template (never stored).
  3. If `IHostEnvironment.IsDevelopment()` and `Poc:Email:FailRecipientPrefix` is set and the recipient starts with it, throw `InvalidOperationException("Test failure (FailRecipientPrefix)")`.
  4. Build a `MailMessage` (From = `DefaultFromAddress` with the company sender name, R10; To; Subject; `AlternateView`s text/plain and text/html) and send with `IEmailSender.SendAsync(mailMessage)`.
  5. Success: `Sent`, `SentAt`, `Attempts++`. Exception: `Attempts++`, `LastError` (truncated); if `Attempts >= 5`, set `Failed` and return; else save, complete the unit of work, and rethrow.
- [ ] T010 Create `Application/Notifications/NotificationService.cs` (`ITransientDependency`; depends on T005, T007, T008, T009; plan Design §2): `Task QueueAsync(string evt, string audience, IEnumerable<Employee> recipients, NotificationContext ctx)` and `Task QueueAccountAsync(string evt, Guid userId, string email)`.
  - Resolve the template: form row (`ctx.FormDefinitionId`), else company row, else built-in.
  - Build the `EmailModel` per research R9:
    - `request.number` = the first 8 hex characters of the submission id, upper case
    - `link` = `{base}/?view=inbox` (approver side) or `{base}/?view=mine&submission={id}` (initiator side)
    - `company.name` from `ITenantStore` / `ICurrentTenant.Name`
    - `fields` = `submission.GetData()`
  - Render workflow emails now. Insert one `EmailMessage` (Queued) per **active** recipient with a non-empty email, and `IBackgroundJobManager.EnqueueAsync(new SendEmailArgs { … })`. Account emails insert with `TextBody/HtmlBody = null` and `RecipientUserId`.
  - Never calls `SaveChanges`/commit itself (runs in the caller's unit of work; Principle II).
- [ ] T011 [P] In `demo.py`:
  - add `MAILPIT = "http://localhost:8025/api/v1"`
  - `mail_ready()` (GET `/info`; at start-up, if unreachable, log "Start Mailpit (mailpit &) first" and `sys.exit(1)`)
  - `mail_for(address, subject_contains=None, wait=15)` (poll `GET /search?query=to:"{address}"`, return the newest matching message summary)
  - `mail_body(msg)` (GET `/message/{ID}` → `Text`, `HTML`)
  - `mail_link(msg, marker)` (the first URL in the text containing `marker`)
  - `set_password_from_mail(address, password)` (find the invite, parse `tenant`/`user`/`token` from the link, `POST /account/set-password` anonymous; returns the HTTP code)
  - Call `mail_ready()` at the top of `__main__` for every mode.
- **Gate**: `dotnet build`; reset once; `dotnet run` (check the startup log for no background-job or emailing errors); `python3 demo.py` steps 1–12 still pass unchanged (nothing is wired to events yet); restart check.

---

## Phase 3: User Story 1 — Notify approvers and initiators (P1) 🎯 MVP

**Goal**: Every event in the spec's US1 table sends its email after commit. Accounts get set-password links (no passwords anywhere). Failures retry and show in the Email log with Resend.

**Independent Test**: With Mailpit running, `demo.py` 13a/13b/13c/13g/13h/13i pass and steps 11/12 complete accounts through emailed links. The built-in templates are enough; no admin template editing is needed.

- [ ] T012 [US1] In `Application/SubmissionAppService.cs` `SubmitAsync`: after inserting the submission, call `NotificationService.QueueAsync(Submitted, Initiator, [employee], ctx)` in the same unit of work (not in `OnCompleted`: the row and job must commit together; the job itself is what runs after commit).
- [ ] T013 [US1] In `Application/ProcessManager.cs`:
  - `CreateTaskAsync`: after the candidates are inserted, decide sent-back: the most recently completed task (`Status == Completed`, latest `CompletedAt`) in this `ProcessInstanceId` has `Outcome == Rejected` (research R8).
    - If so, queue `SentBack/Approver` to the candidates and `SentBack/Initiator` to the submitter.
    - Else queue `TaskAssigned/Approver` to the candidates.
  - `CompleteProcessAsync`: queue `Approved/Initiator` or `Rejected/Initiator`. For Rejected, fill `approver.name` and `comment` from the latest completed Rejected task (`CompletedByUserId` → employee name).
  - Everything goes in each method's existing unit of work.
- [ ] T014 [US1] In `Application/TaskReassignmentService.cs`: after inserting the new candidates for a task, queue `TaskReassigned/Approver` to them.
- [ ] T015 [US1] Create `Application/AccountAppService.cs` (`ApplicationService`, `[AllowAnonymous]` on both; contracts "Account"):
  - `ForgotPasswordAsync({ email })`: find the user by email in the current tenant (resolved from `__tenant` as at sign-in); if found and active, `QueueAccountAsync(PasswordReset, user.Id, email)`; **always** return normally.
  - `SetPasswordAsync({ tenantId?, userId, token, newPassword })`: inside `CurrentTenant.Change(tenantId)`, `ResetPasswordAsync`, then `SetLockoutEndDateAsync(null)` + `ResetAccessFailedCountAsync`. Map an invalid/expired token or unknown user to `UserFriendlyException("This link is invalid or has expired.")`; pass password-policy errors through.
  - Routes in `Controllers/PocController.cs`: `POST account/forgot-password`, `POST account/set-password`, both `[AllowAnonymous]` (the controller is `[Authorize]` since spec 003 T027).
- [ ] T016 [US1] Accounts without passwords (FR-008):
  - `Application/EmployeeAppService.cs` `CreateAsync`: `CreateAsync(user)` with **no password**, then `QueueAccountAsync(AccountInvite, user.Id, email)`; return `EmployeeDto` (`EmployeeCreatedDto` removed from `Application/Dtos.cs`).
  - `Application/TenantOnboardingAppService.cs` `CreateCompanyAsync`: the same for the first admin, inside the tenant; `CompanyCreatedDto` loses `GeneratedPassword`. `ResetClientAdminPasswordAsync`: keep the admin-role check and lockout clearing, remove the password change, queue `PasswordReset`, return nothing (`ResetAdminPasswordDto` removed; the route returns 204).
  - Delete `Application/PasswordGenerator.cs`.
- [ ] T017 [P] [US1] Create `Application/Notifications/EmailLogAppService.cs` (`[Authorize(WorkmatePermissions.EmployeesManage)]`): `GetListAsync(string? status)` (newest first, max 200, recipient name via `RecipientEmployeeId`) and `ResendAsync(Guid id)` (only `Failed`: set `Queued`, `Attempts = 0`, `LastError = null`, enqueue a new job). DTO `EmailLogItemDto` per contracts. Routes `GET emails`, `POST emails/{id:guid}/resend`.
- [ ] T018 [US1] In `wwwroot/index.html`:
  - The sign-in screen gets a "Forgot password?" link: an email field (+ the company picker already there) → `POST /api/poc/account/forgot-password` with `__tenant`, then always "If that account exists, we've emailed a link."
  - On load, `?setPassword=1&tenant=&user=&token=` shows a set-password form (new password ×2) → `POST /api/poc/account/set-password`, then back to sign-in with a success message.
  - The Employees create notice becomes "Invitation sent to {email}" (no password); the vendor onboarding/reset notices likewise.
  - An **Email log** tab (`Workmate.Employees.Manage`) lists entries (filter: all/Failed) with Resend on Failed rows.
  - After sign-in, honour `?view=inbox|mine` (and `submission=` to open that request).
- [ ] T019 [US1] In `demo.py` steps 11 and 12: replace every use of `generatedPassword` with `set_password_from_mail(address, <chosen password e.g. "Demo-Pass1!">)` followed by `sign_in`. Step 11's lockout test (11g) and the vendor reset (11i) now read the reset link from Mailpit (reset → set a new password → sign in). Keep the check names.
- [ ] T020 [US1] In `demo.py` add `def s13():` and register `("Step 13 email notifications (spec 005)", s13)` after step 12. US1 checks (quickstart):
  - 13a: Acme Annual leave from Sara → Sara "submitted", Omar assigned (subject has "waiting for your approval", text has a `?view=inbox` link), Omar approves → Huda assigned, Huda approves → Sara "approved". Count that this submission produced exactly 4 messages (matching the request number in subjects).
  - 13b: a second request rejected by Omar with comment "Not this week" → Sara "rejected" containing the comment, and **no** HR mail for it.
  - 13c: Acme's step-9 style designed workflow with `back-to-previous` → a reject at step 2 mails step 1's approver (SentBack) and Sara (info).
  - 13g: a vendor-created company admin creates an employee → the invite arrives; the link sets a password; sign-in works; reusing the same link gets 403; `GET /emails` shows that message with no body.
  - 13h: `forgot-password` for a real and an unknown address both return 204; only the real one gets mail.
  - 13i: an employee `fail-…@…` submits; the request still has status InProcess/Approved as normal; within ~40 s `GET /emails?status=Failed` shows their Submitted email with `attempts == 5` and a last error; `POST emails/{id}/resend` returns it as Queued.
  - 13j (part): a non-admin gets 403 on `GET /emails`.
  - Use a fresh `RUN_SUFFIX`-keyed company for 13g–13i so Acme/Globex data stays untouched.
- **Gate (US1 Independent Test)**: steps 1–12 pass (11/12 via emailed links); 13a, 13b, 13c, 13g, 13h, 13i and 13j (part) pass; restart check passes; Mailpit UI shows readable HTML emails.

---

## Phase 4: User Story 2 — Templates per form, per event, per side (P1)

**Goal**: The form designer edits subject/body per event and side, with validation, preview and reset. Form overrides apply to that form only.

**Independent Test**: Override TaskAssigned/Approver on one Acme form and submit; the override is used for that form only. Other forms and Globex keep defaults. Invalid templates are refused with the reason; HTML in field values is escaped.

- [ ] T021 [P] [US2] In `Application/Dtos.cs`: `EmailTemplateDto(string Event, string Audience, string Subject, string Body, string Source)`, `SaveEmailTemplateInput { Subject, Body }`, `PreviewEmailInput { Event, Audience, Subject, Body }`, `EmailPreviewDto(string Subject, string Text, string Html)`.
- [ ] T022 [US2] Create `Application/Notifications/EmailTemplateAppService.cs` (`[Authorize(WorkmatePermissions.FormsDesign)]`; depends on T008, T021), form part:
  - `GetFormTemplatesAsync(key)`: every workflow pair with its effective template and `Source` form/company/built-in.
  - `SaveFormTemplateAsync(key, evt, audience, input)`: valid workflow pair only; `Validate` with `allowedFieldKeys` = the union of field keys over **all** versions of the form (`FormSchema.Parse`); upsert.
  - `ResetFormTemplateAsync(key, evt, audience)`: delete the row.
  - `PreviewAsync(key, input)`: `Validate`, then `Render` with sample data (initiator "Sample Person", approver "Sample Approver", step "Manager approval", comment "Looks fine", `link` `{base}/?view=inbox`, every form field = `"<b>sample</b>"`).
  - Routes per contracts (`GET|POST forms/{key}/emails…`, `PUT|DELETE forms/{key}/emails/{event}/{audience}`).
- [ ] T023 [US2] In `wwwroot/index.html` form designer: an **Emails** tab next to the existing editor tabs. It lists each workflow event/side with its source badge; selecting one shows subject + body inputs, a placeholder cheat-sheet (including this form's `fields.*`), and Save, Preview (subject + text `<pre>` + HTML in `<iframe sandbox srcdoc>`) and Reset buttons, with error messages from validation.
- [ ] T024 [US2] In `demo.py` `s13` add:
  - 13d (form part): Khalid overrides TaskAssigned/Approver on Acme `expense-claim` with subject `EXPENSE {{ request.number }} for {{ approver.name }}`. An expense claim from Sara makes Omar's mail use it; a leave request in Acme and one in Globex still use the default subject (SC-002). Reset afterwards so reruns start clean.
  - 13e: saving `{{ request.secret }}` is refused naming `request.secret`; `{{ include 'x' }}` is refused.
  - 13f: a `work-from-home` (or any text field) value `<b>x</b>`: the HTML part contains `&lt;b&gt;x&lt;/b&gt;` and the text part contains `<b>x</b>` literally. Add a body override that includes `{{ fields.<key> }}`, then reset.
  - 13j (part): a non-admin gets 403 on `PUT forms/{key}/emails/...`.
- **Gate (US2 Independent Test)**: all previous checks pass; 13d (form), 13e, 13f, 13j pass; manual Emails tab check.

---

## Phase 5: User Story 3 — Company defaults (P2)

**Goal**: The company admin sets default templates (Account events included) and the sender name, used by every form without its own override.

**Independent Test**: A company default for Submitted/Initiator applies to every Acme form without an override; a form override still wins; Globex is unaffected; the From display name shows the configured sender name.

- [ ] T025 [US3] In `Application/Notifications/EmailTemplateAppService.cs` add the company part:
  - `GetCompanyTemplatesAsync()` (every valid pair, Account included)
  - `SaveCompanyTemplateAsync(evt, audience, input)` (`allowedFieldKeys = null`, so `fields.*` is refused)
  - `ResetCompanyTemplateAsync`
  - `GetSenderAsync()` / `SetSenderAsync({ senderName? })` (`effectiveSenderName` = the name or `"{company} via Workmate"`)
  - Routes `GET company-settings/emails`, `PUT|DELETE company-settings/emails/{event}/{audience}`, `GET|PUT company-settings/email-sender`.
  - Make sure `SendEmailJob` (T009) uses the effective sender name.
- [ ] T026 [US3] In `wwwroot/index.html`: in the form designer's company-settings panel, add "Company email defaults" (same editor as T023, plus the Account events) and a sender name field.
- [ ] T027 [US3] In `demo.py` `s13` add 13k:
  - Khalid sets the Acme company default Submitted/Initiator subject `Acme got your {{ request.title }}` and sender name "Acme HR".
  - Sara's new work-from-home request yields that subject, with From display name "Acme HR".
  - After a form override for Submitted on `work-from-home`, the override wins.
  - A Globex submission keeps the built-in subject.
  - Reset both (and clear the sender name) at the end.
- **Gate (US3 Independent Test)**: all previous checks pass; 13k passes; manual company panel check.

---

## Phase 6: Polish

- [ ] T028 [P] `README.md`: Mailpit in the software table and run steps (`mailpit &`, http://localhost:8025), demo step 13, the code map rows; seeded demo users keep `1q2w3E*`, while new accounts get links. `HANDOVER.md`: Phase J state (with the counts from T030), plan row and order. `CHANGELOG.md`: lines per task under `## Spec 005: email notifications`.
- [ ] T029 [P] `specs/003-login-onboarding/spec.md` and `specs/004-employees-org/spec.md`: append a Clarifications "Update 2026-10-21 (spec 005)" bullet noting that the on-screen generated password is replaced by an emailed set-password link (spec 005 FR-008). Don't edit their original text.
- [ ] T030 Final loop: Mailpit running → fresh reset → `python3 demo.py` (steps 1–13) **twice** (to catch intermittent failures, as spec 004 found) → restart check. Then the manual UI pass: Emails tab, company defaults, Email log, forgot/set password from a real Mailpit link, the non-admin view.

---

## Dependencies & Execution Order

T001 → T002/T003/T004 → **Phase 2 (T005 ∥ T007 ∥ T008 ∥ T011; then T006; T009 after T005+T008; T010 last)** → **US1 (T012–T020)** → **US2 (T021–T024)** → **US3 (T025–T027)** → T028–T030.

- US1 depends only on Phase 2. US2's code (T021–T023) needs only Phase 2 too: built-in templates already flow through the same `NotificationService` resolution, but its `demo.py` checks reuse `s13` (T020). US3 extends US2's service and editor (T022, T023).
- `demo.py` tasks (T011, T019, T020, T024, T027) are one file, so they run in sequence.
- `Controllers/PocController.cs` is touched by T015, T017, T022 and T025, so those run in sequence.

## Parallel execution examples

```text
# Phase 2:
Task: "Domain/Notifications.cs" (T005)
Task: "BuiltInEmailTemplates.cs" (T007)
Task: "EmailTemplateRenderer.cs" (T008)
Task: "demo.py Mailpit helpers" (T011)

# US1:
Task: "EmailLogAppService.cs" (T017), alongside T012–T014 (different files)
```

## Implementation Strategy

- **MVP = Phase 2 + US1.** Every event emails the right people with built-in wording, accounts use links (no passwords), and failures are visible and resendable. This alone meets SC-001.
- **US2** adds per-form wording (SC-002); **US3** adds company defaults and sender name.
- Stop at each gate. Every phase leaves `demo.py` fully green with Mailpit running.
