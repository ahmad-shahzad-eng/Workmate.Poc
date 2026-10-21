# Research: ABP theming and per-company branding

Decisions behind [plan.md](./plan.md). The background facts and sources are in [research-notes.md](./research-notes.md). Items marked **(spike)** were checked on 2026-10-22 in a throwaway copy of this project: LeptonX Lite 4.1.0 added, port 5099, its own database, deleted afterwards. They are observed, not assumed.

## R1. Package and module

- **Decision**: Add `Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite` **4.1.0**, the release built against ABP 9.1.0 (its nuspec pins `Theme.Shared`, `Mvc.UI.MultiTenancy` and `AutoMapper` at 9.1.0), and `typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule)` in `[DependsOn]`.
  - Its other dependencies are already in the project transitively (via `AbpOpenIddictAspNetCoreModule`).
  - **(spike)** builds and starts with no other change; `/` still serves `index.html`.
- **Licence**: LGPL-3.0 (npm metadata for 4.1.0); free, as the constitution requires. The NuGet page links to commercial.abp.io, but ABP's docs list LeptonX Lite as open source and free.
- **Alternatives**: Basic Theme (plain Bootstrap, less to show); commercial LeptonX (ruled out).

## R2. How the theme's files reach the browser

- **Decision**: Link the theme's files directly from `index.html`, served from the DLL through ABP's virtual file system by the existing `app.UseStaticFiles()`. No npm step, no `wwwroot/libs`, no ABP bundling; `AbpMvcLibsOptions.CheckLibs` stays `false`.
- **(spike)** Every file returns 200, under `/Themes/LeptonXLite/Global/side-menu/` (with a **dash**; the embedded resource names use `side_menu`, so the underscore path returns 404):
  - `css/bootstrap-dim.css` (270 KB, Bootstrap 5 in the theme's single "dim" style)
  - `css/layout-bundle.css`, `css/abp-bundle.css`, `css/font-bundle.css`, `css/js-bundle.css`
  - `libs/bootstrap-icons/font/bootstrap-icons.css`
  - `libs/bootstrap/js/bootstrap.bundle.min.js`
  - `js/lepton-x.bundle.min.js`
  - every CSS file also has an `.rtl.css` twin
  - `/Themes/LeptonXLite/Global/scripts/leptonx-mvc-ui-initializer.js`
- **Not used**: ABP's bundle tag helper. **(spike)** It logs "Could not find the bundle file '/libs/abp/core/abp.css'…" (also Font Awesome, toastr, abp-utils) because `wwwroot/libs` doesn't exist. That only matters for the server-rendered layout (clarification Q1), so this PoC doesn't need them.
- FR-013 holds: every file comes from our own server.

## R3. Page structure: mirror the theme's Application layout

- **Decision**: `index.html` keeps its views and API calls, but its shell takes the exact markup of LeptonX Lite's Application layout. **(spike)** It was rendered with a probe page and a two-item `IMenuContributor`, giving:
  - `#lpx-wrapper` containing:
    - `.lpx-mobile-navbar-container` (mobile header, user and menu toggles `data-lpx-toggle="mobile-navbar"`)
    - `.lpx-sidebar-container > #lpx-sidebar > nav.lpx-nav`, with `.lpx-logo-container > a > .lpx-brand-logo` and `ul.lpx-nav-menu#desktop-sidebar`
    - `.lpx-content-container > .lpx-topbar-container > .lpx-topbar` (breadcrumb + `.lpx-topbar-content`) and `.lpx-content`
  - Menu item: `li.outer-menu-item > a.lpx-menu-item-link.lpx-menu-item` with `span.lpx-menu-item-icon > i.lpx-icon.bi.bi-…` and `span.lpx-menu-item-text`; groups use `ul.lpx-inner-menu`; the selected item gets `.selected`.
- The menu is built in the browser from `/my-permissions` (as today), not by `IMenuContributor`, which would need the server to know the user while it renders (clarification Q1). The server keeps refusing every admin call (FR-003).
- Top bar: the "Acting as" picker (vendor admin), the renderer and RTL demo toggles, and a user dropdown (name, company, Sign out) built with the theme's dropdown classes.
- **Scripts**: `bootstrap.bundle.min.js` plus `lepton-x.bundle.min.js`, which handles sidebar collapse, mobile toggles and dropdowns. The 143-byte initializer is checked during implementation: if it needs the `abp` global or jQuery (absent here), LeptonX is initialised directly and the initializer isn't loaded.
- **Icons**: Bootstrap Icons from the theme (`bi bi-inbox`, `bi-send`, `bi-list-check`, `bi-ui-checks`, `bi-diagram-3`, `bi-people`, `bi-building`, `bi-envelope`, `bi-palette`).

## R4. The page's own components: one compatibility stylesheet

- **Decision**: Keep the page's own class names and restyle them in one new file, `wwwroot/css/workmate-theme.css`, on top of the theme's variables. That's about 20 classes (`field`, `btn`, `secondary`, `meta`, `actions`, `panel`, `lead`, `inline`, `status`, `rows`, `split`, `tabs`, `steps`, `icon`, `empty`, `versions`, `fieldrows`, `timeline`, `message`). Bootstrap's base styles cover `input`, `select` and `textarea` through element selectors (`.form-control` / `.form-select` look).
  - The inline `<style>` block (≈120 lines, with its `--tenant` variables, dark mode and hard-coded company colours) is removed (FR-010, FR-012).
  - Markup changes stay limited to the shell and the sign-in screens.
- **Why**: Rewriting every generated snippet to Bootstrap classes would touch most of the 1,100-line page for no visible gain; the compatibility layer keeps the diff reviewable (HANDOVER §1).
- **Renderers**: the native renderer (`renderers.js`, 4 classes) is covered by the same stylesheet; SurveyJS gets the brand colour through its CSS variables (`--sjs-primary-backcolor`, `--sjs-primary-backcolor-dark`, `--sjs-primary-forecolor`).

## R5. Colours: the brand colour drives both the theme and Bootstrap

- **(spike)** `layout-bundle.css` defines `:root { --lpx-brand: #f72585; --lpx-primary: #355dff; … --lpx-logo: ; --lpx-logo-icon: url(data:…) }`. `--lpx-brand` colours the menu's hover, selected and expanded icons and breadcrumbs; `.lpx-brand-logo` is `background-image: var(--lpx-logo)` at 32 px high. Bootstrap's primary is **hard-coded** in `bootstrap-dim.css` (`#355dff` 26 times, `53, 93, 255` 11 times), so `.btn-primary`, links and focus rings don't follow a variable.
- **Decision**: On load, and on every "Acting as" change, the page sets on `:root` `--lpx-brand`, `--lpx-primary`, `--bs-primary`, `--bs-primary-rgb`, `--bs-link-color`, `--wm-on-brand`, `--wm-brand-soft`, `--wm-brand-text` and `--lpx-logo`/`--lpx-logo-icon`. `workmate-theme.css` then applies them to buttons (`.btn` and `.btn-primary` through `--bs-btn-*`), links, focus rings, the active tab and badges.
- **Contrast (FR-007, SC-005)**: the server computes the effective colours so the UI and email agree:
  - `onBrand` = black or white, whichever contrasts more with the brand colour. By WCAG's formula the better of the two is always ≥ 4.58:1, so AA (4.5:1) holds for any colour.
  - `brandText` = the brand colour darkened in 5% steps until it reaches 4.5:1 on the content background `#ffffff`; used for brand-coloured text and links on light surfaces.
  - `brandSoft` = 12% brand mixed with white; used for the selected-row and highlight backgrounds.
- **Defaults**: product colour `#355dff` (the theme's own primary) and name "Workmate HRM".

## R6. Dark mode and RTL

- **Dark mode (clarification Q3)**: the `prefers-color-scheme` rules go; the single "dim" style is used. **(spike)** None of the theme's CSS files contains `prefers-color-scheme`.
- **RTL**: the RTL toggle sets `dir="rtl"` and swaps each theme `<link>` to its `.rtl.css` twin (all of them ship). Both are kept as `<link>` elements whose `href` is switched, so there's no reload.

## R7. Branding data and logo storage

- **Decision**: New entity `CompanyBranding` (one row per tenant, table `Ui_CompanyBranding`), holding:
  - `DisplayName` (≤ 64 characters)
  - `BrandColor` (`#rrggbb`)
  - `LogoContentType` (`image/png|jpeg|svg+xml`)
  - `Logo` (`byte[]`, ≤ 200 KB)
  - `LogoVersion` (an integer bumped on each change, used in the logo URL to bust caches)
- Stored in the database: no file store to manage, and a few hundred KB per company is fine for a PoC. Production: blob storage (ABP BlobStoring) behind the same endpoint.
- Seed: Acme `#0f6e6e`, Globex `#4a4fb0` (FR-012), display names = company names, and a small generated SVG monogram logo ("A", "G") so the logo path is demonstrated from the first run.

## R8. Logo safety (FR-007)

- **Accepted types**: PNG and JPEG are accepted only if their first bytes match (`89 50 4E 47`, `FF D8 FF`); the client's declared type is ignored. SVG must parse as XML with DTDs prohibited and a root `<svg>` element.
- **SVG is refused** if it contains:
  - `script` or `foreignObject` elements
  - any `on*` attribute
  - a `javascript:` value anywhere
  - an `href`/`xlink:href` that isn't a `#fragment` or a `data:image/png|jpeg` URI
  - a `<style>` with `url(` or `@import`
- **Defence in depth**: logos are only shown as `<img>` and CSS backgrounds, where browsers never run SVG scripts. The logo endpoint also sends `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; sandbox` and `X-Content-Type-Options: nosniff`, so even opening the URL directly can't run anything.
- **Size**: at most 200 KB, checked before parsing.

## R9. Who can read and change branding (FR-006, FR-008)

- **Decision**: A new permission `Workmate.Branding.Manage` (multi-tenancy side **Both**, like `Forms.Design`, so the vendor admin acting as a company can use it). It's added to `TenantAdminRoleSeeder`'s list, so every company admin role and the vendor admin get it (seeded companies on reset; new companies at onboarding). It's also listed in `/my-permissions`.
- **Reading**:
  - signed-in callers get their own company's branding (or the acted-as company's for the vendor admin, or product defaults for a host user acting as nobody)
  - anonymous callers get only the public fields (display name, colours, logo URL) of a company **named** in the request, for the sign-in screen after a company is picked; company names are already public through the anonymous `GET tenants`
  - the logo image is anonymous by URL (tenant id + version)
- **Alternatives**: reuse `Workmate.Forms.Design`. Rejected: branding isn't form design, and a separate permission keeps Principle VII precise.

## R10. Emails use the brand (FR-009)

- **Decision**: `EmailModel` gains `BrandColor` and `OnBrandColor`; `NotificationService` reads the effective branding when it queues an email; `EmailTemplateRenderer.ToHtml` uses them for the header and button, and the display name for the header text.
- The `{{ company.name }}` placeholder stays the company (tenant) name, as spec 005 defines; only the fixed layout changes.
- The email shows no logo: images from `localhost` don't load in real mail clients, and embedded images add size. Recorded as a follow-up.

## R11. Sign-in screens (clarification Q2)

- **Decision**: Our own sign-in, forgot-password and set-password forms stay. They're styled like the theme's Account layout: a centred card on the theme background, with the logo and name above it.
- Picking a company in the sign-in picker calls the anonymous public-branding endpoint and recolours the screen; with no company picked, the product defaults show.
- The `?setPassword=1&tenant=…` link (spec 005) applies that company's branding by tenant id, through the same endpoint.

## R12. When branding changes take effect

- **Decision**: The page fetches branding on load and on every "Acting as" change. A save also re-applies it at once for the admin who saved. Nobody else gets a live push; they see it on their next page load (spec edge case).

## R13. The production path (US3, recorded in HANDOVER)

- **ABP's full theming**, for a product UI built as ABP MVC: a Razor host page on the Application layout, `IMenuContributor` for permission-filtered menus, `IToolbarContributor` for the user menu and "Acting as", a per-tenant `IBrandingProvider` (DisplayName, LogoUrl) reading `CompanyBranding`, `wwwroot/libs` via `abp install-libs` with `CheckLibs` back on, and a cookie sign-in scheme so the server knows the user while rendering. The Account module's pages would then be an option.
- **Cost estimate** (from this spike): the page shell into Razor, a second auth scheme, and npm library management. About L, against M for this spec.
- **Findings to record**:
  - the free theme has a single colour style (no light/dark switcher, which is commercial)
  - Bootstrap's primary colour is compiled in, so per-company colours need an override layer
  - assets need no npm step when only the theme's own files are linked

## R14. How it's tested (constitution: `demo.py` per acceptance scenario)

- New `demo.py` step 14 checks the API and served page:
  - branding read and save
  - isolation (Acme vs Globex) and the vendor admin acting as each company
  - validation: bad colour, oversized logo, non-image, SVG with a script
  - the contrast rules (`onBrand` for a light and a dark colour)
  - non-admin refused
  - the anonymous public view returns only public fields
  - logo response headers
  - the HTML email uses the brand colour and display name
  - `index.html` links only same-origin theme assets, and those return 200
  - reset back to defaults
- Steps 1–13 and the restart check must stay green (FR-011).
- The visual checks (phone width, RTL, renderer look) are in quickstart.md's manual list, like specs 004–006.
