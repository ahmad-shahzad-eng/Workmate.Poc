# Implementation Plan: ABP theming and per-company branding

**Branch**: `007-abp-theming` | **Date**: 2026-10-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/007-abp-theming/spec.md` (clarified 2026-10-22, 3 answers)

## Summary

The demo page takes on ABP's free LeptonX Lite theme. It keeps its views and API calls but gets the theme's Application-layout structure (side menu, top bar, mobile menu, content area) and the theme's own CSS and JS. All of these are served straight from the theme package through ABP's virtual file system, with no npm step. The page's ~20 own classes are restyled in one compatibility stylesheet.

Each company gets a `CompanyBranding` row (display name, brand colour, safe logo) that its admin edits on a new Branding page. The server computes readable colour variants. The page applies them as CSS variables, which drive both the theme (`--lpx-brand`, `--lpx-logo`) and Bootstrap's primary colour, compiled into the theme and therefore overridden. Spec 005's HTML emails use the same colours and name.

ABP's server-rendered layout, menu and toolbar contributors and branding provider are deliberately not used (clarification Q1). HANDOVER records them as the production path, with their cost. Every technical point was checked in a throwaway spike first ([research.md](./research.md), "(spike)").

## Technical Context

**Language/Version**: C# on `net10.0`, vanilla JS (unchanged)

**Primary Dependencies**: ABP 9.1.0 open-source, adding `Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite` **4.1.0** (LGPL-3.0, built against ABP 9.1.0, R1). Bootstrap 5, Bootstrap Icons and the LeptonX script all come embedded in that package. Elsa and SurveyJS are unchanged.

**Storage**: Same `workmate-poc.db`; new `Ui_CompanyBranding` (logo bytes inline, ≤ 200 KB). **Reset required once.**

**Testing**: `demo.py` steps 1–13 and the restart check stay green; new step 14 ([quickstart.md](./quickstart.md)); visual checks are manual.

**Target Platform**: Local dev, desktop browser and phone width.

**Project Type**: Web service + vanilla-JS demo UI.

**Performance Goals**: N/A (PoC). The theme CSS (~400 KB uncompressed) loads once and is cached.

**Constraints**:
- open-source ABP only (constitution)
- no external CDN at run time (FR-013)
- bearer-token sign-in unchanged (spec 003)
- tenant-isolated branding (Principle IV)
- WCAG AA text contrast on brand colours (FR-007)
- safe SVG (R8)

**Scale/Scope**: US1 (P1) themed UI, US2 (P1) company branding, US3 (P2) go/no-go write-up.

## Constitution Check

*GATE: checked before research and again after design. Both pass.*

| Principle / gate | Status |
| --- | --- |
| I Immutable form versions | Not affected: styling only; renderers unchanged in behaviour |
| II Commit before workflow | Not affected; emails still queue in the event's unit of work (only their HTML layout changes) |
| III Assignment from HR data | Not touched |
| IV Tenant isolation (NON-NEGOTIABLE) | **Applies.** `CompanyBranding` is `IMultiTenant`; reads and writes go through the tenant filter. The anonymous public view and the logo URL expose only what the sign-in screen shows, by design (FR-008). `demo.py` 14b/14c prove Acme and Globex stay apart. |
| V Typed reporting values | Not touched |
| VI One door into Elsa | Not touched |
| VII Admin is a permission | **Applies.** New `Workmate.Branding.Manage`, granted through the `Workmate.CompanyAdmin` role (and to the vendor admin per company, as for `Forms.Design`); the menu's visibility is cosmetic, and the server refuses non-admins (14f). |
| Technology boundaries | **Applies.** One new package, free (LGPL-3.0), on the same ABP version line (4.1.0 ↔ 9.1.0, as the nuspec confirms); no commercial theme; no ABP or Elsa version change. |
| Gates | CHANGELOG line per task; `demo.py` step 14 covers the acceptance scenarios that can be checked through the API, with the rest in quickstart's manual list; build → run → demo loop after one reset. |

Result: **PASS**, no exceptions. (Complexity Tracking is empty.)

## Project Structure

### Documentation (this feature)

```text
specs/007-abp-theming/
├── spec.md
├── research-notes.md     # raw findings + sources (from /speckit-specify)
├── plan.md               # this file
├── research.md           # R1–R14 decisions, spike evidence
├── data-model.md
├── contracts/api.md      # branding API + UI contract
├── quickstart.md         # demo.py step 14 + manual checks
├── checklists/requirements.md
└── tasks.md              # /speckit-tasks (not yet)
```

### Source Code (repository root, existing single project)

```text
Workmate.Poc.csproj                       # + Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite 4.1.0
WorkmatePocModule.cs                      # + AbpAspNetCoreMvcUiLeptonXLiteThemeModule
Domain/Branding.cs                        # new: CompanyBranding
Application/Branding/
├── BrandColors.cs                        # new: WCAG contrast, onBrand / brandText / brandSoft
├── LogoValidator.cs                      # new: magic bytes, size, safe-SVG rules (R8)
└── BrandingAppService.cs                 # new: get, public get, save, logo upload/remove, reset
Application/WorkmatePermissions.cs, WorkmatePermissionDefinitionProvider.cs, TenantAdminRoleSeeder.cs  # + Branding.Manage
Application/Notifications/EmailTemplateRenderer.cs, NotificationService.cs  # brand colour + name in the HTML layout
Application/Dtos.cs, Data/PocDbContext.cs, Data/DemoDataSeeder.cs, Controllers/PocController.cs
wwwroot/index.html                        # LeptonX Lite shell, menu from permissions, Branding page, themed sign-in; inline theme CSS removed
wwwroot/css/workmate-theme.css            # new: compatibility layer + brand variables (R4, R5)
wwwroot/branding/workmate.svg             # new: product default logo
demo.py                                   # step 14
```

**Structure Decision**: Keep the single project. Branding code is grouped under `Application/Branding/`, the same way spec 005 grouped notifications.

## Design

### 1. Theme assets (R1, R2)

- Add the package and module. `index.html` links the theme's CSS and JS directly by their virtual-file-system paths (`/Themes/LeptonXLite/Global/side-menu/…`, with a dash).
- No ABP bundling, no `wwwroot/libs`; `CheckLibs` stays `false`.
- The RTL toggle swaps each theme `<link>` to its `.rtl.css` twin.

### 2. Page shell (R3)

- Replace the current header/nav/main grid with LeptonX Lite's Application-layout markup, as captured in the spike.
- `render()` builds the `ul.lpx-nav-menu` items (desktop and mobile) from `state.permissions`, marks the current one `.selected`, and keeps the Inbox count badge.
- The top bar holds the "Acting as" picker, the renderer and RTL toggles, and a user dropdown (name, company, Sign out).
- The existing views render into `.lpx-content` unchanged.

### 3. Components (R4)

- New `workmate-theme.css` restyles the page's own classes (`panel` → card look, `btn`/`secondary` → primary/outline buttons, `status` → badges, `rows` → list group, `tabs` → nav tabs, `field`/`inline`/`actions` → form layout, `message` → alerts) using Bootstrap and theme variables.
- The inline `<style>` block (with its dark mode and company colours) is deleted.
- SurveyJS colours come from the brand variables.

### 4. Branding (R5, R7–R9, R12)

- `BrandingAppService`: `GetAsync`, `GetPublicAsync(company | tenantId)` (anonymous; inside `CurrentTenant.Change`), `SaveAsync`, `SetLogoAsync(IFormFile)`, `RemoveLogoAsync`, `ResetAsync`, and `GetLogoAsync(tenantId)` for the image endpoint (anonymous, security headers set by the controller).
- `BrandColors` computes the effective colours; `LogoValidator` enforces R8.
- The page calls `applyBranding(dto)` on load, after "Acting as" changes and after a save; it sets the CSS variables, the logo, the document title and the favicon colour.
- New **Branding** page (`Branding.Manage`): name, colour picker + hex, logo upload/remove, live preview, Save, Reset.

### 5. Emails (R10)

- `NotificationService` reads the effective branding once per queue call and passes `BrandColor`/`OnBrandColor`/display name into `EmailModel`.
- `EmailTemplateRenderer.ToHtml` uses them in place of the fixed blue.

### 6. Sign-in screens (R11)

- A centred card with the logo and name. Changing the company picker fetches `branding/public?company=` and applies it.
- The set-password link applies `branding/public?tenantId=`.

### 7. Seed and permission (R7, R9)

- `DemoDataSeeder` writes Acme and Globex branding (colours from FR-012, names, monogram SVGs).
- `Workmate.Branding.Manage` is added to the definitions, the admin-role seeder and `/my-permissions`.

### 8. `demo.py`

- Step 14 per quickstart. Steps 1–13 unchanged; the API doesn't change for them.

### 9. Docs (US3)

- README: theming section, Branding page, step 14, code map.
- HANDOVER: Phase L state, the production-path write-up (R13) and findings.
- CHANGELOG per task.

## Risks (verify on first build)

- **Theme JS initialisation**: whether `lepton-x.bundle.min.js` initialises the sidebar and mobile toggles on its own, or needs the 143-byte MVC initializer (which may expect ABP's `abp` global or jQuery). If so, call its init directly.
- **Bootstrap reset vs SurveyJS**: Bootstrap's base styles may change SurveyJS's look; scope fixes in `workmate-theme.css`.
- **Static file precedence**: our own `wwwroot` must not shadow `/Themes/…`. It has no such folder; keep it that way.
- **Multipart upload through `PocController`** under ABP's antiforgery filter: bearer-only requests aren't subject to it (no auth cookie), but confirm a 200, not a 400.
- **Logo caching**: the URL carries the version; confirm a new upload shows without a hard refresh.

## Complexity Tracking

No constitution violations; nothing to justify.
