# Research notes: ABP theming (2026-10-22)

The facts gathered before writing [spec.md](./spec.md). `/speckit-plan` turns these into decisions in `research.md`.

## 1. ABP's MVC theming system

- Themes are MVC/Razor Pages packages. Each must implement three standard layouts: **Application** (main pages), **Account** (login, register, password pages) and **Empty** (no layout components).
- Layout parts come from ABP services that a theme renders **server-side**:
  - **Main menu**: `IMenuManager`, `StandardMenus.Main`; items are added by `IMenuContributor` and filtered by permission.
  - **Toolbar** (user menu, language switch): `IToolbarManager` with `IToolbarContributor`.
  - **Branding** (app name, logo): `IBrandingProvider`, with `AppName`, `LogoUrl` and `LogoReverseUrl`. It is called on every page render, so it can return per-tenant values. The docs say a tenant-specific name can be returned but show no code.
  - **Page alerts** (`IAlertManager`) and **layout hooks** (`LayoutHooks.Head.First`, …).
- Selection: `ITheme` and `[ThemeName]`, registered in `AbpThemingOptions`; `IThemeSelector` picks the theme at run time. ABP has no built-in per-tenant theme selection.
- Bundling: themes extend `StandardBundles.Styles.Global` / `StandardBundles.Scripts.Global`. Base client libraries (Bootstrap, jQuery, DataTables, …) come from the npm package `@abp/aspnetcore.mvc.ui.theme.shared`, normally copied to `wwwroot/libs` by `abp install-libs`.
- Sources: [ABP MVC theming docs (9.1)](https://abp.io/docs/9.1/framework/ui/mvc-razor-pages/theming), [Theming 8.1](https://abp.io/docs/8.1/UI/AspNetCore/Theming), [Branding docs](https://abp.io/docs/latest/framework/ui/mvc-razor-pages/branding).

## 2. Which themes are free

| Theme | License | Notes |
| --- | --- | --- |
| Basic Theme | Open source (npm `@abp/aspnetcore.mvc.ui.theme.basic` 9.1.0: LGPL-3.0) | Plain Bootstrap, minimal |
| **LeptonX Lite** | Open source (npm `@abp/aspnetcore.mvc.ui.theme.leptonxlite` 4.1.0: LGPL-3.0) | Modern; what ABP startup templates ship |
| Lepton, LeptonX | **Commercial** (ABP licence) | Ruled out by the constitution |

## 3. LeptonX Lite: the version that matches this project

- NuGet `Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite` has its own version line. **4.1.0** depends on `Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared` **9.1.0**, `Volo.Abp.AspNetCore.Mvc.UI.MultiTenancy` 9.1.0 and `Volo.Abp.AutoMapper` 9.1.0, and targets `net9.0` (usable from `net10.0`). 4.1.1 pairs with ABP 9.1.1. Checked on nuget.org.
- Install (docs): add the package, add `typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule)` to `[DependsOn]`, and use `LeptonXLiteThemeBundles.Styles.Global` in `AbpBundlingOptions`.
- **What's in the package** (inspected the 4.1.0 `.nupkg`): the CSS, JS, fonts and Bootstrap are **embedded in the DLL** and served through ABP's virtual file system under `/Themes/LeptonXLite/Global/side_menu/…` (`layout-bundle.css`, `abp-bundle.css`, `font-bundle.css`, `js-bundle.css`, `bootstrap-dim.css`, `js/lepton-x.bundle.min.js`, plus `*.rtl.css` for each). The npm package of the same name is a stub that only depends on `@abp/aspnetcore.mvc.ui.theme.shared`.
- Razor views included: `Layouts/Application|Account|Empty.cshtml`; components Brand, Menu (+ `_MenuItem`), BreadCrumbs, PageAlerts, Toolbar (UserMenu, MobileUserMenu, LanguageSwitch, MobileLanguageSwitch, MobileLoginLink). Each can be overridden by a file at the same path.
- **One colour style**: only `bootstrap-dim.css` ships. The CSS contains `lpx-theme-light|dim|dark` class names and `--lpx-*` variables (`--lpx-brand`, `--lpx-primary`, `--lpx-logo`, `--lpx-logo-icon`, `--lpx-navbar-text-color`, `--lpx-content-bg`, …), but no light/dark switch option. The light/dark/dim/system switcher belongs to commercial LeptonX (forum reports; no official comparison found).
- **RTL**: an `.rtl.css` ships for every bundle.
- Sources: [LeptonX Lite MVC docs (9.1)](https://abp.io/docs/9.1/ui-themes/lepton-x-lite/asp-net-core), [9.2](https://abp.io/docs/9.2/ui-themes/lepton-x-lite/asp-net-core), [ABP support: dark theme](https://abp.io/support/questions/10130/disable-dark-theme), [LeptonX announcement](https://abp.io/community/articles/introducing-the-lepton-theme-next-generation-l3jw9ize).

## 4. Where this project stands

- `Volo.Abp.AspNetCore.Mvc.UI`, `.Bootstrap`, `.Bundling`, `.Packages`, `.Theme.Shared` and `.Widgets` (all 9.1.0) are **already referenced transitively** (via `AbpOpenIddictAspNetCoreModule`), so LeptonX Lite adds one direct package (plus `Mvc.UI.MultiTenancy` and `AutoMapper`, transitively).
- `AbpMvcLibsOptions.CheckLibs = false` today, because `wwwroot/libs` doesn't exist. A Razor-layout approach needs the shared npm libraries in `wwwroot/libs`. `npm` is installed; the `abp` CLI isn't (it can be installed as a dotnet tool, or the files copied by a small script).
- The UI is one hand-written `wwwroot/index.html` (vanilla JS, about 1,100 lines) plus `js/renderers.js` and vendored SurveyJS; there are no Razor pages. Sign-in is a bearer token kept in `sessionStorage`; there is **no cookie session**, so a server-rendered layout can't know the user or their permissions when it renders, unless cookie sign-in is added (spec 003 chose bearer tokens).
- Today's look: CSS variables (`--tenant`, `--tenant-soft`, …), OS dark mode via `prefers-color-scheme`, hard-coded company colours (`body[data-tenant="globex"]`), an RTL toggle (`dir="rtl"`).
- Spec 005's email HTML layout uses a fixed blue header with the company name.
- The open-source **Account module** (`Volo.Abp.Account.Web`, LGPL) provides themed login, register, forgot-password and reset-password pages, but uses cookie sign-in and has self-registration (it can be switched off by a setting). Spec 003 deliberately didn't use it.

## 5. The two delivery approaches (FR-002)

| | A: ABP server-rendered layout | B: theme look in the existing page |
| --- | --- | --- |
| What | A Razor page with LeptonX Lite's Application layout hosts today's views; menu from `IMenuContributor`, user menu from the toolbar, logo and name from a per-tenant `IBrandingProvider` | Keep `index.html`; load LeptonX Lite's CSS/JS from the virtual file system; rebuild the side menu and top bar in the theme's HTML structure; branding from our own settings API, applied with `--lpx-*` variables |
| Proves | Real ABP theming (layouts, contributors, branding provider, bundling) | That the free theme's look fits; per-company branding |
| Auth | Needs the server to know the user while rendering: add cookie sign-in alongside bearer (or render the menu anonymously and hide items client-side, which defeats IMenuContributor's permission filtering) | Unchanged (bearer) |
| Libraries | `wwwroot/libs` via npm; `CheckLibs` back on | None beyond the theme package |
| Effort | Larger: rewrite the page shell into Razor, add a cookie scheme, keep the SPA views working inside the layout | Smaller: markup and CSS changes in one file, plus a branding API |
| Production fit | Matches an ABP MVC product UI | Matches a product whose UI is a separate SPA |
