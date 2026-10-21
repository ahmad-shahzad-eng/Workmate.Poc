# Research: Email notifications with per-form templates

Decisions behind [plan.md](./plan.md), checked against the code on `005-email-notifications` (built on spec 004) and against nuget.org, not assumed.

## R1. Packages (FR-001)

- **Decision**: Add these, all confirmed at exactly `9.1.0` on nuget.org:
  - `Volo.Abp.Emailing` (`IEmailSender`, SMTP settings)
  - `Volo.Abp.TextTemplating.Scriban` (brings Scriban 5.10.0)
  - `Volo.Abp.BackgroundJobs.Domain` + `Volo.Abp.BackgroundJobs.EntityFrameworkCore` (persistent job store; `.Domain` depends on `Volo.Abp.BackgroundJobs` and `Volo.Abp.AutoMapper`)
- No MailKit: ABP's default `SmtpEmailSender` (System.Net.Mail) is enough for Mailpit and plain SMTP.
- **Rationale**: Spec FR-001. A *persistent* job store matters. The in-memory default would lose queued emails on restart, and would break FR-002's "queued only after commit", because an EF-stored job row is inserted in the same unit of work as the business change and becomes visible to the worker only once that commits.
- **Verify on build**: the new jobs table joins `PocDbContext` via `[ReplaceDbContext(typeof(IBackgroundJobsDbContext))]` + `b.ConfigureBackgroundJobs()`, the same pattern spec 003 used. Also confirm no `NullEmailSender` replaces SMTP in Debug.

## R2. One outbox row per email: `EmailMessage`

- **Decision**: Every notification creates an `EmailMessage` row (the Email log, FR-005) **and** enqueues a background job carrying only `(TenantId, EmailMessageId)`, in the same unit of work as the event.
- The job loads the row, sends, and updates its status. The row is both the outbox and the admin log, so there's no second log table.
- **Principle II**: rows and jobs are written inside the triggering unit of work (`ProcessManager.CreateTaskAsync`'s own UoW, `CompleteProcessAsync`'s, the submit request's, the deactivation request's). Nothing is sent inside it.

## R3. Retries (clarification Q3)

- **Decision**: `AbpBackgroundJobWorkerOptions`: `DefaultFirstWaitDuration = 60` s, `DefaultWaitFactor = 2.5`, so retries come at about 1, 2.5, 6, 16 and 39 minutes (≈ 1 h in total).
- `EmailMessage.Attempts` counts tries. On an exception the job records `LastError` and rethrows (ABP reschedules), **unless** this was attempt 5. Then it marks the row `Failed` and returns normally, so ABP stops.
- **Resend** (admin): reset `Attempts = 0`, set `Status = Queued`, enqueue a new job.
- The worker's poll period stays at ABP's 5 s default, so `demo.py` sees mail within seconds.
- **Configurable for the demo**: `Poc:Email:RetryFirstWaitSeconds` / `Poc:Email:RetryWaitFactor` (defaults 60 / 2.5 in `appsettings.json`). The new `appsettings.Development.json` (`dotnet run` uses `ASPNETCORE_ENVIRONMENT=Development`, see `launchSettings.json`) sets 2 / 1, so five attempts take about 30 s. A test seam, `Poc:Email:FailRecipientPrefix` (`"fail-"` in Development only), makes the job throw for matching recipients. It's honoured **only** when `IHostEnvironment.IsDevelopment()`, so it can't fail real mail elsewhere.

## R4. Rendering: sandboxed Scriban, one path for every template (FR-004)

- **Decision**: All templates (form, company default, built-in) are strings rendered with **Scriban's own API** (the engine `Volo.Abp.TextTemplating.Scriban` wraps), in a locked-down `TemplateContext`:
  - no `TemplateLoader` (so `include` fails)
  - `LoopLimit = 100`, `RecursiveLimit = 20`
  - a single `ScriptObject` holding only the allowed values
- **Validation on save**: parse, then walk the syntax tree. Allowed: text, expressions that are allowed placeholders (`request.title|number|status`, `initiator.name`, `approver.name`, `step.title`, `comment`, `link`, `company.name`, `fields.<key>`), and `if`/`else`/`end`. Anything else (unknown variables, function definitions, `include`/`import`, loops, assignments) is rejected, naming the first offender (US2 AC1).
- `fields.<key>` must be a field key in **any** version of that form. It isn't allowed in company defaults, which have no form. A field missing from a particular submission renders empty.
- **Why not ABP's `ITemplateRenderer`**: it renders *template definitions* from code or virtual files. Tenant-edited strings with an allow-list are simpler and safer through Scriban directly, and there's then exactly one sandbox to reason about.

## R5. Text + HTML (clarification Q4)

- **Decision**: Render the admin's plain-text body once with raw values; that's the text part. The HTML part is `HtmlEncoder.Encode(text)` with line breaks turned into `<br>` and URLs into links, inside a fixed layout with the company name and a button for `{{ link }}`. Encoding the whole rendered text covers US2 AC2 (HTML in a field value is shown, never interpreted).
- **Subject**: rendered as text, CR/LF stripped (no header injection), capped at 200 characters.

## R6. When content is rendered

- **Decision**: Workflow emails are rendered **when queued** (the moment of the event) and stored on the `EmailMessage`, so retries and resends send exactly what was first composed. "The template current at send time" (clarification Q2) is read as the time the event queues the email, normally seconds earlier.
- **Account emails are the exception**: their body is **never stored**. The job renders them at each attempt and generates a fresh set-password token then, and the log shows only "(set-password link not stored)". Otherwise any admin reading the Email log could use another person's link.

## R7. Set-password links (clarification Q1, FR-008)

- **Decision**: `IdentityUserManager.GeneratePasswordResetTokenAsync(user)`. The link is `{Poc:PublicBaseUrl}/?setPassword=1&tenant={tenantId|empty}&user={userId}&token={urlencoded}`.
- The new anonymous `POST account/set-password` calls `ResetPasswordAsync`, then `SetLockoutEndDateAsync(null)`.
- **Single use**: a successful reset changes the security stamp, which invalidates the token.
- **Expiry**: `Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(72))`. This applies to every Identity token; none of the others are used in this PoC.
- **Verify on build**: `AbpIdentityAspNetCoreModule` (loaded since spec 003) calls `AddDefaultTokenProviders` (symbol confirmed in the 9.1.0 binary). Also check whether ABP sets its own lifespan after ours.
- **Account creation**: users are created with **no password** (`CreateAsync(user)`); they can't sign in until they use the link. `EmployeeCreatedDto.GeneratedPassword` and `CompanyCreatedDto.GeneratedPassword` are removed, and `ResetClientAdminPasswordAsync` sends the link instead of returning a password. `PasswordGenerator` is deleted.
- **Forgot password**: new anonymous `POST account/forgot-password { email }`, with the company from the `__tenant` header as at sign-in. It always returns 204, whether or not the email exists (no account discovery).

## R8. Where each event fires

| Event (US1) | Code point | Recipients |
|---|---|---|
| Submitted | `SubmissionAppService.SubmitAsync` (request UoW) | initiator |
| TaskAssigned | `ProcessManager.CreateTaskAsync`, unless sent back | each candidate |
| SentBack | `CreateTaskAsync`, when the instance's most recently completed task was `Rejected` (a reject that ends the workflow creates no task, so a new task after a reject is a send-back) | each candidate (approver side) + initiator (initiator side) |
| Approved / Rejected | `ProcessManager.CompleteProcessAsync(finalStatus)`; Rejected includes the rejecting task's comment and approver | initiator |
| TaskReassigned | `TaskReassignmentService` (spec 004 FR-007) | new candidates |
| AccountInvite | `EmployeeAppService.CreateAsync`, `TenantOnboardingAppService.CreateCompanyAsync` | the new user |
| PasswordReset | vendor reset (`ResetClientAdminPasswordAsync`), `account/forgot-password` | the user |

Recipients are `Employee.Email`, active employees only. A rule that resolves via the fallback at creation sends `TaskAssigned` like any other assignment.

## R9. Placeholder values

- `request.title`: the form name.
- `request.number`: the first 8 characters of the submission id, upper case. There's no request-number column, and this avoids a schema change and a per-tenant sequence.
- `request.status`, `initiator.name`, `approver.name` (the recipient on the approver side; the deciding approver on Approved/Rejected), `step.title`, `comment` (the last decision's comment).
- `link`: approver side `{base}/?view=inbox`, initiator side `{base}/?view=mine&submission={id}`.
- `company.name`: the tenant name.
- `fields.*`: the submission's values as submitted.
- `Poc:PublicBaseUrl` defaults to `http://localhost:5080`.

## R10. Sender and SMTP

- **Decision**: ABP settings in `appsettings.json` → `Settings`: `Abp.Mailing.Smtp.Host=localhost`, `Port=1025`, `EnableSsl=false`, `UseDefaultCredentials=false`, `Abp.Mailing.DefaultFromAddress=no-reply@workmate.local`.
- The display name is the company's sender name (US3; new `CompanyEmailSettings.SenderName`, default `"{company} via Workmate"`). Per-company SMTP stays "later" (FR-001).

## R11. Permissions

- **Decision**: Form and company templates, sender name: `Workmate.Forms.Design` (company admin, and the vendor admin acting as a company). Email log and resend: `Workmate.Employees.Manage`. No new permissions (Principle VII).
