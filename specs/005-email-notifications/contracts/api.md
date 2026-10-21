# API contract: Email notifications

Routes are under `api/poc`. They need a bearer token unless marked **anonymous**. Errors use the existing `UserFriendlyException` shape (403 with `error.message`).

## Form templates (US2): `Workmate.Forms.Design`

| Method & route | Body | Returns |
|---|---|---|
| `GET forms/{key}/emails` | — | `EmailTemplateDto[]`, one per valid workflow (event, audience) pair, with the **effective** template and where it comes from |
| `PUT forms/{key}/emails/{event}/{audience}` | `{ subject, body }` | `EmailTemplateDto`; refused, naming it, on an unknown placeholder or disallowed syntax (US2 AC1) |
| `DELETE forms/{key}/emails/{event}/{audience}` | — | 204; back to the company default or built-in ("reset to default") |
| `POST forms/{key}/emails/preview` | `{ event, audience, subject, body }` | `EmailPreviewDto` rendered with sample data, including a sample field value containing `<b>` to show escaping |

```text
EmailTemplateDto { event, audience, subject, body, source: "form" | "company" | "built-in" }
EmailPreviewDto  { subject, text, html }
```

## Company defaults (US3): `Workmate.Forms.Design`

| Method & route | Body | Returns |
|---|---|---|
| `GET company-settings/emails` | — | `EmailTemplateDto[]` for every valid pair, Account events included (`source`: company or built-in) |
| `PUT company-settings/emails/{event}/{audience}` | `{ subject, body }` | `EmailTemplateDto` (`fields.*` not allowed here) |
| `DELETE company-settings/emails/{event}/{audience}` | — | 204 |
| `GET company-settings/email-sender` | — | `{ senderName, effectiveSenderName }` |
| `PUT company-settings/email-sender` | `{ senderName? }` | same |

## Email log (FR-005): `Workmate.Employees.Manage`

| Method & route | Body | Returns |
|---|---|---|
| `GET emails?status=Failed` | — | `EmailLogItemDto[]`, newest first, max 200; this company only |
| `POST emails/{id}/resend` | — | `EmailLogItemDto`; only for `Failed` |

```text
EmailLogItemDto { id, event, audience, recipientEmail, recipientName?, submissionId?, subject, status, attempts, lastError?, creationTime, sentAt? }
```

## Account (FR-008): anonymous

| Method & route | Body | Returns |
|---|---|---|
| `POST account/forgot-password` | `{ email }` (company via the `__tenant` header, as at sign-in; omit it for host users) | **always 204**, whether or not the account exists |
| `POST account/set-password` | `{ tenantId?, userId, token, newPassword }` | 204; 403 "This link is invalid or has expired." for a bad, used or expired token; password-policy errors as messages |

## Changed responses

| Route | Change |
|---|---|
| `POST employees` | returns `EmployeeDto` (no `generatedPassword`); an AccountInvite email is queued |
| `POST tenants` | `CompanyCreatedDto { tenantId, adminEmployeeId, adminEmail }`; AccountInvite queued to the admin |
| `POST tenants/reset-admin-password` | 204; PasswordReset email queued (lockout still cleared) |
