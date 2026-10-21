# Tasks: ABP theming and per-company branding

**Input**: [spec.md](./spec.md) · [plan.md](./plan.md) · [research.md](./research.md) · [data-model.md](./data-model.md) · [contracts/api.md](./contracts/api.md) · [quickstart.md](./quickstart.md)

**Tests**: The constitution requires `demo.py` checks for every acceptance scenario that can be checked through the API, so each story phase ends with its step-14 checks. Visual checks are in quickstart's manual list. There are no unit-test projects.

**Gate after each phase**: Mailpit running (`mailpit &`) → stop the old app (port 5080 free) → `dotnet build` → `dotnet run` → `python3 demo.py` → restart check. One CHANGELOG line per task.

**Reset**: Phase 2 adds a table and a permission grant, so set `Poc:ResetDatabaseOnStartup=true` for its first run, then back to `false`.

**Spike facts to rely on** (research.md):
- theme files live under `/Themes/LeptonXLite/Global/side-menu/…` (**dash**, not underscore)
- layout markup: research R3
- `--lpx-brand` / `--lpx-logo` drive the theme, but Bootstrap's primary colour is hard-coded `#355dff` and must be overridden (R5)

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 themed UI, US2 company branding, US3 go/no-go write-up (spec.md)

---

## Phase 1: Setup

- [X] T001 In `Workmate.Poc.csproj` add `<PackageReference Include="Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite" Version="4.1.0" />` (the release built against ABP 9.1.0; do **not** take 4.1.1+, which pairs with ABP 9.1.1). In `WorkmatePocModule.cs` add `typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule)` to `[DependsOn]` (namespace `Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite`). Leave `AbpMvcLibsOptions.CheckLibs = false` and don't add ABP bundling (research R2).
- [X] T002 New permission `Workmate.Branding.Manage`:
  - `Application/WorkmatePermissions.cs`: `public const string BrandingManage = GroupName + ".Branding.Manage";`
  - `Application/WorkmatePermissionDefinitionProvider.cs`: `group.AddPermission(WorkmatePermissions.BrandingManage, multiTenancySide: MultiTenancySides.Both)` ("Both" so the vendor admin acting as a company can use it, same reason as `FormsDesign`)
  - `Application/TenantAdminRoleSeeder.cs`: add it to `TenantSidePermissions`
  - `Controllers/PocController.cs` `GetMyPermissionsAsync`: add it to the list
- **Gate**: `dotnet build` succeeds; `dotnet run` (no reset yet), and `curl http://localhost:5080/Themes/LeptonXLite/Global/side-menu/css/layout-bundle.css` returns 200 and `/` still serves `index.html`.

---

## Phase 2: Foundational (blocks every story)

**Purpose**: Branding data, its read side, colour maths, logo rules, seed. US1 needs the seeded colours (the page's hard-coded colours go away), and US2 adds the write side.

- [X] T003 [P] Create `Domain/Branding.cs`: `CompanyBranding : AggregateRoot<Guid>, IMultiTenant` (data-model.md), with:
  - `TenantId` (Guid?)
  - `DisplayName` (string?): "trimmed, 1–64 characters, no line breaks; null means the company (tenant) name"
  - `BrandColor` (string?): "`#rrggbb` (6 hex digits, stored lower-case); null means the product colour `#355dff`"
  - `LogoContentType` (string?): "`image/png`, `image/jpeg` or `image/svg+xml`, decided by the file's own bytes"
  - `Logo` (byte[]?): "≤ 200 KB"
  - `LogoVersion` (int): "+1 on every logo change"
  - constants `MaxDisplayNameLength = 64`, `MaxLogoBytes = 200 * 1024`, `DefaultColor = "#355dff"`, `ProductName = "Workmate HRM"`
- [X] T004 In `Data/PocDbContext.cs` (depends on T003): `DbSet<CompanyBranding> CompanyBrandings`; `b.Entity<CompanyBranding>` → table `Ui_CompanyBranding`, `ConfigureByConvention()`, unique index on `TenantId`, `DisplayName` max length 64, `BrandColor` max length 7.
- [X] T005 [P] Create `Application/Branding/BrandColors.cs` (static; research R5):
  - `bool IsValidHex(string)` (`^#[0-9a-fA-F]{6}$`)
  - `double Contrast(string a, string b)` (WCAG 2.x relative luminance)
  - `string OnBrand(string brand)`: `#000000` or `#ffffff`, whichever contrasts more (always ≥ 4.58)
  - `string BrandText(string brand)`: darken toward black in 5% steps until `Contrast(x, "#ffffff") >= 4.5`
  - `string BrandSoft(string brand)`: 12% brand + 88% white
  - all outputs lower-case `#rrggbb`
- [X] T006 [P] Create `Application/Branding/LogoValidator.cs` (`ITransientDependency`; research R8): `(string ContentType, byte[] Bytes) Validate(byte[] bytes)` throwing `UserFriendlyException` with a specific message.
  - Size: `> 200 KB` → "Logos can be at most 200 KB."
  - PNG by `89 50 4E 47 0D 0A 1A 0A`, JPEG by `FF D8 FF`. Otherwise try SVG: UTF-8 text, `XmlReader` with `DtdProcessing.Prohibit` and `XmlResolver = null`, root element `svg`.
  - Refuse SVG with:
    - element `script` → "SVG logos may not contain scripts."
    - element `foreignObject`
    - any attribute named `on*`
    - any attribute value containing `javascript:`
    - `href`/`xlink:href` not starting with `#` or `data:image/png`/`data:image/jpeg`
    - a `style` element or attribute containing `url(` or `@import` → "SVG logos may not load external content."
  - Anything else → "The logo must be a PNG, JPEG or SVG image."
  - Return the content type from the bytes, never the client's.
- [X] T007 Create `Application/Branding/BrandingAppService.cs` (`ApplicationService`; depends on T003–T005), read side, plus the DTO in `Application/Dtos.cs`: `BrandingDto(string? DisplayName, string EffectiveDisplayName, string? BrandColor, string EffectiveBrandColor, string OnBrandColor, string BrandTextColor, string BrandSoftColor, string? LogoUrl)`.
  - `GetAsync()` (any signed-in user): current tenant's row, effective values per data-model.md "Derived". Company name from `ITenantStore`; host with no tenant → product defaults, name "Workmate HRM"; `LogoUrl` = `/api/poc/branding/logo/{tenantId}?v={LogoVersion}` only when `Logo != null`.
  - `[AllowAnonymous] GetPublicAsync(string? company, Guid? tenantId)`: resolve the tenant by name (`ITenantStore.FindAsync(name)`) or id; unknown/empty → product defaults; read inside `CurrentTenant.Change(id)`; same DTO.
  - `[AllowAnonymous] GetLogoAsync(Guid tenantId)` → `(byte[] Bytes, string ContentType)?` inside `CurrentTenant.Change(tenantId)`.
- [X] T008 In `Controllers/PocController.cs` (depends on T007):
  - `GET branding`
  - `[AllowAnonymous] GET branding/public?company=&tenantId=`
  - `[AllowAnonymous] GET branding/logo/{tenantId:guid}`: return `File(bytes, contentType)` with headers `Cache-Control: public, max-age=86400`, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; sandbox`; 404 when there's no logo
- [X] T009 In `Data/DemoDataSeeder.cs`: when seeding each company, insert `CompanyBranding`:
  - Acme: `DisplayName = "Acme Trading"`, `BrandColor = "#0f6e6e"`; Globex: `"Globex Logistics"`, `"#4a4fb0"` (FR-012)
  - `Logo` = a small SVG monogram (rounded square in the brand colour, white letter "A"/"G"), built as a string and passed through `LogoValidator` so the seed obeys the same rules; `LogoContentType = "image/svg+xml"`, `LogoVersion = 1`
  - Also add `wwwroot/branding/workmate.svg` (product default mark) for the UI's default logo.
- [X] T010 [P] In `demo.py`: helpers `branding(tok)` (GET `/branding`), `public_branding(company=None, tenant_id=None)` (anonymous), and `contrast(a, b)` (WCAG formula, for 14e).
- **Gate**: `dotnet build`; **reset once**; `dotnet run`; `python3 demo.py` steps 1–13 all pass (nothing visible changed yet); restart check; manually `curl -i` the Acme logo URL and see the three headers.

---

## Phase 3: User Story 1 — The product looks like a standard ABP application (Priority: P1) 🎯 MVP

**Goal**: Everyone sees LeptonX Lite's layout and styles, with a permission-driven side menu, a top bar with the user, themed sign-in screens and seeded company colours. All existing behaviour unchanged.

**Independent Test**: Sign in as Sara, Khalid and the vendor admin; walk every section; `demo.py` green; 14a and 14h pass.

- [X] T011 [P] [US1] Create `wwwroot/css/workmate-theme.css` (research R4/R5):
  - `:root` defaults for the brand variables (`--wm-brand: #355dff; --wm-on-brand: #fff; --wm-brand-text: #355dff; --wm-brand-soft: #e6ebff`)
  - map them onto Bootstrap and the theme: `--bs-primary`, `--bs-primary-rgb`, `--bs-link-color`, `--bs-link-hover-color`, `.btn-primary` `--bs-btn-bg/border/hover/active/color` from `--wm-brand`/`--wm-on-brand`, `.btn-outline-primary` from `--wm-brand-text`, `--bs-focus-ring-color`, `.form-control:focus`/`.form-select:focus` border, `.nav-tabs .nav-link.active`, `.lpx-menu-item.selected` text
  - restyle the page's own classes with Bootstrap/theme variables: `.panel` (card), `.btn` (primary, with `.secondary` = outline), `.status.{Pending,InProcess,Approved,Rejected,Submitted}` (badges: warning/info/success/danger/secondary), `.rows` (list group, `aria-pressed="true"` → `--wm-brand-soft`), `.tabs` (nav tabs), `.field`/`.inline`/`.actions`/`.split` (form layout and grid, `.split` single-column under 860 px), `.meta`/`.lead`/`.empty`, `.message.ok|.error` (alerts), `.steps` (table), `.versions`, `.timeline`, `.icon`, `.schema` (monospace textarea), `.req`
  - native renderer classes from `renderers.js` (`field`, `value`, `req`, `meta`)
  - SurveyJS: `--sjs-primary-backcolor: var(--wm-brand); --sjs-primary-backcolor-dark: var(--wm-brand-text); --sjs-primary-forecolor: var(--wm-on-brand)`
- [X] T012 [US1] In `wwwroot/index.html` `<head>`: delete the whole inline `<style>` block (its `--tenant` variables, `prefers-color-scheme` dark mode and `body[data-tenant=…]` colours; FR-010, FR-012), and move any rule still needed for page-specific layout into `workmate-theme.css`.
  - Link, in order: `/Themes/LeptonXLite/Global/side-menu/libs/bootstrap-icons/font/bootstrap-icons.css`, then `…/css/font-bundle.css`, `…/css/bootstrap-dim.css`, `…/css/layout-bundle.css`, `…/css/abp-bundle.css`, `…/css/js-bundle.css`, the existing SurveyJS CSS, then `/css/workmate-theme.css`. Give each theme link a `data-theme-css` attribute.
  - Before the existing scripts: `…/libs/bootstrap/js/bootstrap.bundle.min.js` and `…/js/lepton-x.bundle.min.js`. Check whether the sidebar/mobile toggles work without `/Themes/LeptonXLite/Global/scripts/leptonx-mvc-ui-initializer.js`. Include it only if needed and only if it runs without the `abp` global or jQuery; otherwise call LeptonX's init directly (plan Risks).
  - RTL toggle: set `document.documentElement.dir` and swap each `[data-theme-css]` href between `X.css` and `X.rtl.css`.
- [X] T013 [US1] In `wwwroot/index.html`: replace the shell (`header.identity`, `div.shell > nav + main`) with LeptonX Lite's Application-layout markup from research R3:
  - `.lpx-scroll-container > #lpx-wrapper` with the mobile navbar container (logo, `bi-person` user toggle, `bi-list` menu toggle, `#mobile-user-menu-group`, `nav#mobile-navbar > ul.lpx-nav-menu#mobile-sidebar`)
  - `.lpx-sidebar-container > #lpx-sidebar > nav.lpx-nav` (`.lpx-logo-container` with `.lpx-brand-logo` + collapse icon, `ul.lpx-nav-menu#desktop-sidebar`)
  - `.lpx-content-container` with `.lpx-topbar` (left: page title as breadcrumb; right in `.lpx-topbar-content`: the "Acting as" select (vendor admin only), the Renderer select, the RTL checkbox, and a Bootstrap dropdown user menu showing the person's name and company with Sign out) and `.lpx-content > main#main`
  - Rewrite `render()` to build menu items, desktop and mobile, as `li.outer-menu-item > a.lpx-menu-item-link.lpx-menu-item[data-view]` with `span.lpx-menu-item-icon > i.lpx-icon.bi.<icon>` and `span.lpx-menu-item-text`:
    - New request `bi-plus-square`, My requests `bi-send`, Inbox `bi-inbox` (+ count badge), all for everyone
    - Form designer `bi-ui-checks` (`Forms.Design`), Workflow designer `bi-diagram-3` (`Workflows.Design`), Employees `bi-people` (`Employees.Manage`), Organisation `bi-building` (`Org.Manage`), Email log `bi-envelope` (`Employees.Manage`)
    - the current item gets `.selected`; clicking an item sets `state.view`, closes the mobile menu and re-renders
  - Keep every existing view function and `#main` usage unchanged. The `?view=` deep links still work.
- [X] T014 [US1] In `wwwroot/index.html`: `applyBranding(b)` sets on `document.documentElement.style`:
  - `--wm-brand`, `--wm-on-brand`, `--wm-brand-text`, `--wm-brand-soft`, `--lpx-brand`, `--lpx-primary` = `effectiveBrandColor`
  - `--bs-primary-rgb` (computed from the hex)
  - `--lpx-logo` and `--lpx-logo-icon` = `url("<logoUrl>")` (or `url("/branding/workmate.svg")` when null)
  - It also sets the top bar/menu name text to `effectiveDisplayName`, `document.title = "<effectiveDisplayName> · Workmate HRM"`, and an `onerror` fallback that hides a broken logo and shows the name (edge case).
  - Call it after sign-in (`GET /api/poc/branding`) and after each "Acting as" change. Delete `document.body.dataset.tenant` handling and `slug()` if unused.
  - Long names: CSS ellipsis with `title` = full name.
- [X] T015 [US1] In `wwwroot/index.html`: restyle `#signInShell` (sign-in, forgot-password, set-password forms; spec 005) as the theme's account page: a centred `.card` on the content background, logo (`.lpx-brand-logo`-style image) and name above the form, Bootstrap form controls, primary button.
  - On load, and on each `#siCompany` change, call `GET /api/poc/branding/public?company=<name>` (empty → product defaults) and `applyBranding`.
  - When the URL has `?setPassword=1&tenant=<id>`, use `?tenantId=<id>` instead.
- [X] T016 [P] [US1] In `demo.py` add `def s14():` registered as `("Step 14 theming and company branding (spec 007)", s14)` after step 13, with:
  - 14a: Sara's `branding()` → `effectiveBrandColor == "#0f6e6e"`, `effectiveDisplayName == "Acme Trading"`, a `logoUrl` whose GET returns 200 with `image/svg+xml`; Ali's → `#4a4fb0`, "Globex Logistics"
  - 14h: `GET /` (raw HTML), extract every `href`/`src`; none points to another host (`http(s)://` not starting with `http://localhost:5080`); each same-origin `/Themes/…` and `/css/…` URL returns 200
- **Gate (US1 Independent Test)**: steps 1–13 pass unchanged; 14a and 14h pass; restart check. Manual: quickstart items 1, 3, 4, 5 and 6 (as far as possible without a browser, at least load `/` and confirm no console-breaking script errors via `node --check` on the inline script).

---

## Phase 4: User Story 2 — Each company carries its own branding (Priority: P1)

**Goal**: Company admins edit name, colour and logo; everyone in that company sees it; emails use it; other companies are untouched.

**Independent Test**: As Khalid set Acme's branding; Sara sees it, Ali doesn't; an Acme email shows it; invalid input is refused; reset returns the defaults.

- [X] T017 [US2] In `Application/Branding/BrandingAppService.cs` add the write side, all `[Authorize(WorkmatePermissions.BrandingManage)]`, plus `SaveBrandingInput { DisplayName?, BrandColor? }` in `Application/Dtos.cs`:
  - `SaveAsync(input)`: trim the name. Empty → null; longer than 64 characters → "The display name can be at most 64 characters."; containing `\r`/`\n` → refused. Colour: empty → null; else `BrandColors.IsValidHex`, else "Enter the colour as #rrggbb, for example #0f6e6e.", stored lower-case. Upsert the row; return `GetAsync()`.
  - `SetLogoAsync(byte[] bytes)`: `LogoValidator.Validate`, store bytes + content type, `LogoVersion++`.
  - `RemoveLogoAsync()`: null logo, `LogoVersion++`.
  - `ResetAsync()`: delete the row.
  - Refuse when `CurrentTenant.Id == null` ("Choose a company first.").
- [X] T018 [US2] In `Controllers/PocController.cs` (depends on T017):
  - `PUT branding` (`[FromBody] SaveBrandingInput`)
  - `PUT branding/logo` taking `IFormFile logo` from a multipart form (read into a byte array capped at 200 KB + 1 so oversized uploads are refused without buffering more; missing file → "Choose a logo file.")
  - `DELETE branding/logo`, `DELETE branding`
  - Confirm a bearer-only multipart request isn't refused by ABP's antiforgery filter (plan Risks).
- [X] T019 [P] [US2] Emails (FR-009; research R10):
  - `Application/Notifications/EmailTemplateRenderer.cs`: add `string BrandColor` and `string OnBrandColor` to `EmailModel` (defaults `#355dff`/`#ffffff`); `ToHtml` uses them for the header background and text and for the button, and `model.CompanyName` stays the header text
  - `Application/Notifications/NotificationService.cs`: resolve the effective branding once per `QueueAsync`/`RenderAccountAsync`/preview (call `BrandingAppService` read logic or share a small `BrandingReader` service) and pass `effectiveDisplayName` as the header name, `effectiveBrandColor`, `onBrandColor`
  - `{{ company.name }}` stays the tenant name (spec 005 contract)
  - Update every `new EmailModel(...)` call site (NotificationService, EmailTemplateAppService preview)
- [X] T020 [US2] In `wwwroot/index.html` add a **Branding** view (`views.branding`, menu item `bi-palette`, `Branding.Manage` only):
  - fields: display name (placeholder = company name), colour (`<input type="color">` synced with a hex text field), logo file input with Upload and Remove
  - live preview: a mini top bar with logo and name, a primary button, an email-header strip, plus the note "Text on this colour will be black/white" from a preview call or client-side contrast
  - actions: Save (PUT branding), Upload (FormData to PUT branding/logo), Remove logo, Reset to default (DELETE branding, with a confirm)
  - errors shown with `message("error", …)`; after any change call `applyBranding` with the returned DTO
- [X] T021 [US2] In `demo.py` `s14` add (quickstart):
  - 14b: Khalid `PUT /branding` `{displayName: "Acme Trading Co", brandColor: "#7a1f5c"}`; Sara reads both; Ali still reads `#4a4fb0`
  - 14c: the vendor admin with `act_as_tenant` = Acme id reads Acme's, = Globex id reads Globex's, without it reads `effectiveDisplayName == "Workmate HRM"`
  - 14d: refused with 403 and a message each:
    - `brandColor: "red"`
    - a 201 KB PNG-headed upload
    - a text file named `logo.png`
    - an SVG with `<script>`
    - an SVG with `onload=`
    Multipart is built with the stdlib (`uuid` boundary).
  - 14e: save `#ffff00` → `onBrandColor == "#000000"` and `contrast(brandTextColor, "#ffffff") >= 4.5`; `#0f6e6e` → `#ffffff`
  - 14f:
    - Sara `PUT /branding` → 403
    - anonymous `public_branding(company=ACME)` returns exactly the `BrandingDto` fields with no tenant id or extra data
    - `GET` the logo URL: headers include `sandbox` in CSP and `nosniff`
  - 14g: with Acme set to `#7a1f5c` / "Acme Trading Co", Sara submits a leave request; her "submitted" email's HTML part contains `#7a1f5c` and `Acme Trading Co`
  - 14i: Khalid `DELETE /branding` → Sara reads `#355dff` and "Acme Trading"; then `PUT` back `#0f6e6e`/"Acme Trading" and re-upload the monogram (fetched from the seeded logo URL before 14b) so reruns start clean
- **Gate (US2 Independent Test)**: all previous checks plus 14b–14g and 14i pass; restart check; manual quickstart item 2 and 7.

---

## Phase 5: User Story 3 — Document the theming decision for go/no-go (Priority: P2)

**Goal**: The PoC records what ABP theming gave, cost and couldn't do.

**Independent Test**: HANDOVER has the Phase L entry with the findings; README explains theming and branding.

- [X] T022 [US3] `HANDOVER.md`:
  - "Done and verified" Phase L entry: what was adopted (LeptonX Lite 4.1.0 assets and layout structure via the virtual file system, no npm), what stayed custom (menu/top bar built in the browser from permissions, own sign-in screens, branding API), and the free-theme gaps found (single "dim" style, no light/dark switch; Bootstrap primary compiled in, so per-company colour needs an override layer; ABP bundles need `wwwroot/libs`)
  - the production path from research R13 with its cost (Razor host page + `IMenuContributor`/`IToolbarContributor`/per-tenant `IBrandingProvider`, `abp install-libs`, cookie sign-in; about L)
  - the plan row L status and the code map row
- [X] T023 [P] [US3] `README.md`:
  - intro: themed with ABP's free LeptonX Lite, per-company branding
  - demo script step 14 (Branding: as Khalid set a colour/logo, sign in as Sara to see it, Globex unchanged, emails follow)
  - code map rows (`Domain/Branding.cs`, `Application/Branding/*`, `wwwroot/css/workmate-theme.css`)
  - troubleshooting row "Page unstyled → hard refresh; the theme's files come from `/Themes/LeptonXLite/…`"
  - shortcuts row (browser-built menu instead of ABP's server layout; logo in the database instead of blob storage)

---

## Phase 6: Polish

- [X] T024 [P] `specs/006-surveyjs-renderer/spec.md`: append a Clarifications bullet "Update 2026-10-22 (spec 007): the page now uses the LeptonX Lite theme; the per-company renderer and the demo toggle are unchanged, and the native renderer is restyled by `workmate-theme.css`". Don't edit the original text.
- [X] T025 Final loop: Mailpit running → fresh reset → `python3 demo.py` (steps 1–14) **twice** → restart check (`restart-before`, restart, `restart-after`). Record the counts in CHANGELOG and HANDOVER. Then the manual UI pass from quickstart (all 7 items); if no browser is available in the implementing session, say so in T025's note and CHANGELOG, as specs 004–005 did. *(Done: 131/131 twice, restart 3/3 + 12/12. Visual check by headless Chrome screenshots of 8 screens, no JS errors; a person's click-through is still pending.)*

---

## Dependencies & Execution Order

T001 → T002 → **Phase 2 (T003 ∥ T005 ∥ T006 ∥ T010; T004 after T003; T007 after T003–T005; T008 after T007; T009 after T003 + T006)** → **US1 (T011 ∥ T016; T012 → T013 → T014 → T015)** → **US2 (T017 → T018; T019 ∥ T020 after T017; T021 last)** → **US3 (T022 ∥ T023)** → T024, T025.

- US1 needs only Phase 2: seeded branding is read-only there. US2 needs US1's `applyBranding` (T014) for its page, but its API (T017–T019) needs only Phase 2.
- `wwwroot/index.html` is touched by T012–T015 and T020, so those run in sequence.
- `Controllers/PocController.cs` is touched by T002, T008 and T018, so those run in sequence.
- `demo.py` tasks (T010, T016, T021) are one file, so they run in sequence.

## Parallel execution examples

```text
# Phase 2:
Task: "Domain/Branding.cs" (T003)
Task: "Application/Branding/BrandColors.cs" (T005)
Task: "Application/Branding/LogoValidator.cs" (T006)
Task: "demo.py branding helpers" (T010)

# US1:
Task: "wwwroot/css/workmate-theme.css" (T011), alongside "demo.py 14a/14h" (T016)

# US2:
Task: "Email layout brand colours" (T019), alongside "Branding page in index.html" (T020)
```

## Implementation Strategy

- **MVP = Phase 2 + US1.** The whole product in the LeptonX Lite look, with seeded per-company colours and logos and nothing broken. This alone answers "can we apply ABP theming?".
- **US2** adds admin-editable branding and branded emails (the multi-tenant value).
- **US3** turns the result into go/no-go evidence.
- Stop at each gate. Every phase leaves `demo.py` fully green with Mailpit running.
