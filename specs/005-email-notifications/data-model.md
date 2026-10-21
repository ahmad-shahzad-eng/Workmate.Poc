# Data model: Email notifications

All entities are `IMultiTenant` (Principle IV) and live in new `Domain/Notifications.cs`. Decisions: [research.md](./research.md).

## Constants

```text
EmailEvents:    Submitted, TaskAssigned, SentBack, Approved, Rejected, TaskReassigned, AccountInvite, PasswordReset
EmailAudiences: Initiator, Approver, Account
```

Valid (event, audience) pairs, the only ones a template may exist for:

| Event | Initiator | Approver | Account |
|---|---|---|---|
| Submitted | ✓ | | |
| TaskAssigned | | ✓ | |
| SentBack | ✓ (info) | ✓ | |
| Approved | ✓ | | |
| Rejected | ✓ | | |
| TaskReassigned | | ✓ | |
| AccountInvite | | | ✓ (company default / built-in only) |
| PasswordReset | | | ✓ (company default / built-in only) |

## New: `EmailTemplate`

| Field | Type | Rules |
|---|---|---|
| `Id` | Guid | |
| `TenantId` | Guid? | |
| `FormDefinitionId` | Guid? | null means the company default (US3); set means a form override (US2). Belongs to the form, not a version (clarification Q2) |
| `Event` | string | one of `EmailEvents`; the pair must be valid (table above); Account events only with `FormDefinitionId = null` |
| `Audience` | string | one of `EmailAudiences` |
| `Subject` | string | required; validated placeholders (research R4); rendered ≤ 200 characters, CR/LF stripped |
| `Body` | string | required; plain text; validated placeholders |

Unique (`TenantId`, `FormDefinitionId`, `Event`, `Audience`). Resolution at queue time: form → company default → built-in (code constants) (FR-003). "Reset to default" deletes the row.

## New: `CompanyEmailSettings`

| Field | Type | Rules |
|---|---|---|
| `Id`, `TenantId` | | unique `TenantId` |
| `SenderName` | string? | display name on the From line; null means `"{company name} via Workmate"` |

## New: `EmailMessage` (outbox + Email log, FR-005)

| Field | Type | Rules |
|---|---|---|
| `Id`, `TenantId` | | |
| `Event`, `Audience` | string | as above |
| `RecipientEmployeeId` | Guid? | null for a host user (the vendor admin never gets workflow mail) |
| `RecipientUserId` | Guid? | set for account emails (the token is generated for this user at send time) |
| `RecipientEmail` | string | |
| `SubmissionId` | Guid? | null for account emails |
| `Subject` | string | rendered |
| `TextBody`, `HtmlBody` | string? | rendered at queue time; **null for account emails**, which are rendered at send time and never stored (R6) |
| `Status` | string | `Queued` → `Sent`, or `Queued` → … → `Failed` after 5 attempts; Resend: `Failed` → `Queued` (attempts reset) |
| `Attempts` | int | ≤ 5 |
| `LastError` | string? | last exception message, ≤ 1000 characters |
| `CreationTime`, `SentAt` | DateTime / DateTime? | |

## Changed

| Where | Change |
|---|---|
| `EmployeeCreatedDto` | `GeneratedPassword` removed (FR-008) |
| `CompanyCreatedDto` | `GeneratedPassword` removed |
| `ResetAdminPasswordDto` | removed; the route returns 204 and the link goes by email |
| `Application/PasswordGenerator.cs` | deleted; accounts are created with no password |
| ABP `AbpBackgroundJobs` table | added via `IBackgroundJobsDbContext` on `PocDbContext` (reset needed) |
