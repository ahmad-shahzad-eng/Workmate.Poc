# Data model: ABP theming and per-company branding

Decisions: [research.md](./research.md).

## New: `CompanyBranding` (one per company, table `Ui_CompanyBranding`)

`AggregateRoot<Guid>, IMultiTenant` (Principle IV). A company without a row uses the product defaults.

| Field | Type | Rules |
|---|---|---|
| `Id`, `TenantId` | | unique `TenantId` |
| `DisplayName` | string? | trimmed, 1–64 characters, no line breaks; null means the company (tenant) name |
| `BrandColor` | string? | `#rrggbb` (6 hex digits, stored lower-case); null means the product colour `#355dff` |
| `LogoContentType` | string? | `image/png`, `image/jpeg` or `image/svg+xml`, decided by the file's own bytes (R8) |
| `Logo` | byte[]? | ≤ 200 KB; SVG made safe per R8 |
| `LogoVersion` | int | +1 on every logo change; part of the logo URL (cache busting) |

## Derived (not stored): effective branding

Computed by the server for the UI and the email layout (R5):

| Value | Rule |
|---|---|
| `effectiveDisplayName` | `DisplayName` ?? company name ?? "Workmate HRM" (host with no company) |
| `effectiveBrandColor` | `BrandColor` ?? `#355dff` |
| `onBrandColor` | `#000000` or `#ffffff`, whichever has the higher WCAG contrast with `effectiveBrandColor` (always ≥ 4.58:1) |
| `brandTextColor` | `effectiveBrandColor` darkened in 5% steps until its contrast on `#ffffff` is ≥ 4.5:1 |
| `brandSoftColor` | 12% `effectiveBrandColor` + 88% `#ffffff` |
| `logoUrl` | `/api/poc/branding/logo/{tenantId}?v={LogoVersion}` when a logo exists, else null (the UI shows the name) |

## Changed

| Where | Change |
|---|---|
| `WorkmatePermissions` | + `Workmate.Branding.Manage` (side Both); granted through `TenantAdminRoleSeeder` and listed in `/my-permissions` |
| `EmailModel` (spec 005) | + `BrandColor`, `OnBrandColor`; the HTML layout header and button use them, the header shows `effectiveDisplayName` |
| `DemoDataSeeder` | seeds Acme `#0f6e6e` and Globex `#4a4fb0` with their names and an SVG monogram logo |
| `wwwroot/index.html` | inline company colours and dark-mode CSS removed (FR-010, FR-012) |

**Reset required once** (new table, new permission grant).
