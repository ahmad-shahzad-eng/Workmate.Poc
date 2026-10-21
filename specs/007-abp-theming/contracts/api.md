# API and UI contract: ABP theming and per-company branding

Routes are under `api/poc`. They need a bearer token unless marked **anonymous**. Errors use the existing `UserFriendlyException` shape (403 with `error.message`).

## Branding

```text
BrandingDto {
  displayName?,            // what the admin set (null = not set)
  effectiveDisplayName,    // displayName ?? company name ?? "Workmate HRM"
  brandColor?,             // "#rrggbb" or null
  effectiveBrandColor,     // brandColor ?? "#355dff"
  onBrandColor,            // "#000000" | "#ffffff"  (text on brand-coloured surfaces)
  brandTextColor,          // brand colour darkened to ≥ 4.5:1 on white (text and links on light surfaces)
  brandSoftColor,          // light tint for highlights
  logoUrl?                 // "/api/poc/branding/logo/{tenantId}?v={n}" or null
}
```

| Method & route | Who | Body | Returns |
|---|---|---|---|
| `GET branding` | any signed-in user | — | `BrandingDto` for the current company: the acted-as company for the vendor admin, product defaults for a host user acting as nobody |
| `GET branding/public?company={name}` | **anonymous** | — | `BrandingDto` for the named company (product defaults if the name is unknown or empty); used by the sign-in screen once a company is picked |
| `GET branding/public?tenantId={id}` | **anonymous** | — | same, by id (the spec 005 set-password link carries the tenant id) |
| `GET branding/logo/{tenantId}?v={n}` | **anonymous** | — | the logo bytes with their stored content type, `Cache-Control: public, max-age=86400`, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; sandbox`; 404 if the company has no logo |
| `PUT branding` | `Workmate.Branding.Manage` | `{ displayName?, brandColor? }` | `BrandingDto`; refused: colour not `#rrggbb`, name longer than 64 characters or containing a line break |
| `PUT branding/logo` | `Workmate.Branding.Manage` | multipart form, one file field `logo` | `BrandingDto`; refused: over 200 KB, not PNG/JPEG/SVG by content, unsafe SVG (the message names the reason, e.g. "SVG logos may not contain scripts.") |
| `DELETE branding/logo` | `Workmate.Branding.Manage` | — | 204 |
| `DELETE branding` | `Workmate.Branding.Manage` | — | 204; the whole row goes, so the company falls back to the defaults |

`my-permissions` also lists `Workmate.Branding.Manage` when granted.

## Changed

| Where | Change |
|---|---|
| Email HTML layout (spec 005) | header background and button use `effectiveBrandColor`, text on them `onBrandColor`; header text `effectiveDisplayName`. The text part is unchanged. |

## UI contract (`wwwroot/index.html`)

| Area | Contract |
|---|---|
| Assets | Only same-origin URLs: `/Themes/LeptonXLite/Global/side-menu/css/{bootstrap-dim,layout-bundle,abp-bundle,font-bundle,js-bundle}.css` (or `.rtl.css` in RTL), `…/libs/bootstrap-icons/font/bootstrap-icons.css`, `…/libs/bootstrap/js/bootstrap.bundle.min.js`, `…/js/lepton-x.bundle.min.js`, `/css/workmate-theme.css`, plus the existing SurveyJS files and `renderers.js` |
| Shell | LeptonX Lite Application-layout markup (`#lpx-wrapper`, sidebar `ul.lpx-nav-menu`, `.lpx-topbar`, mobile navbar, `.lpx-content`) per research R3 |
| Menu | Items: New request, My requests, Inbox (with count), then the admin items, each shown only with its permission: Form designer (`Forms.Design`), Workflow designer (`Workflows.Design`), Employees (`Employees.Manage`), Organisation (`Org.Manage`), Email log (`Employees.Manage`), **Branding** (`Branding.Manage`) |
| Branding page | Display name, brand colour (colour picker + hex field), logo upload/remove, live preview of the top bar, a button and an email header, Save / Reset to default; shows "Text on this colour will be black/white" from `onBrandColor` |
| Sign-in screens | Centred card in the theme's account-page look; logo and name of the picked company (`branding/public`) |
