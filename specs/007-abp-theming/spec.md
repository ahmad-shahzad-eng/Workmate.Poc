# Feature Specification: ABP theming and per-company branding

**Feature Branch**: `007-abp-theming` | **Created**: 2026-10-22 | **Status**: Draft | **Depends on**: 003 (sign-in), 004 (company admin), 005 (email layout), 006 (renderer switch)

**Input**: Ahmad, 2026-10-22: explore ABP theming, check whether we can apply it, gather the facts, and write one spec. Research findings are in [research-notes.md](./research-notes.md).

**Phase**: L (new row in HANDOVER §4).

## Clarifications

### Session 2026-10-22
- Q: Use ABP's full server-rendered theme layout, or only the theme's look inside the existing page? → A: The theme's look only. The existing single page stays, using LeptonX Lite's styles, layout structure and scripts; branding comes from our own per-company settings. Sign-in stays bearer-token. The full server-rendered layout (menu and toolbar contributors, branding provider, cookie sign-in) is recorded as the production path in HANDOVER, with its cost, for the go/no-go decision.
- Q: Keep our own sign-in screens restyled, or switch to ABP's Account module pages? → A: Keep our own sign-in, forgot-password and set-password screens, restyled with the theme. The Account module is not added.
- Q: The free theme has one colour style; keep dark mode? → A: Accept the single style in the PoC and record "no light/dark switch in the free theme" as a finding. The OS dark-mode styling is removed.

## Summary

Today the demo UI is a hand-written page with its own look and two hard-coded company colours (Acme teal, Globex indigo). This feature gives Workmate HRM the look and structure of a standard ABP application, using ABP's free, open-source **LeptonX Lite** theme: a side menu, a top bar with the signed-in person, and themed pages and sign-in screen. It also lets each company carry its own branding: name, logo and brand colour, set by its company admin. Everything that works today keeps working.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The product looks like a standard ABP application (Priority: P1)

Every signed-in person sees the LeptonX Lite look: a side menu listing only the sections they may use (New request, My requests, Inbox with its waiting count; for admins: Form designer, Workflow designer, Employees, Organisation, Email log), a top bar with the product or company name and a user menu (who is signed in, company, sign out), and page content (lists, forms, buttons, tabs, status badges) in the theme's style. The sign-in, forgot-password and set-password screens use the theme's account-page style.

**Why this priority**: It's the visible result Ahmad wants to judge: whether ABP's own theming fits Workmate HRM and how much work it takes on this codebase.

**Independent Test**: Sign in as Sara, then as Khalid, then as the vendor admin; walk every section; compare with today's screens. Every existing action still works and `demo.py` stays green.

**Acceptance Scenarios**:

1. **Given** Sara (employee) signs in, **When** the app opens, **Then** the side menu shows only New request, My requests and Inbox, the top bar shows her name and company, and the pages use the theme's styles.
2. **Given** Khalid (company admin) signs in, **Then** the side menu also shows Form designer, Workflow designer, Employees, Organisation and Email log; a direct visit to an admin section by Sara is still refused by the server.
3. **Given** the vendor admin signs in, **Then** the top bar still offers the "Acting as" company picker, and the menu and data follow the chosen company.
4. **Given** a form renders with either renderer (native or SurveyJS, spec 006), **Then** it fits the theme's look and still validates and submits as before; the per-browser renderer toggle and the RTL toggle still work.
5. **Given** a narrow screen (phone width), **Then** the side menu collapses into the theme's mobile menu and every section remains usable.
6. **Given** a link from an email (`?view=inbox`, `?setPassword=1…`, spec 005), **Then** it opens the right screen in the themed UI.

---

### User Story 2 - Each company carries its own branding (Priority: P1)

A company admin opens a Branding page and sets the company's display name (as shown in the app), a logo image and a brand colour, then sees a preview. Everyone in that company sees that branding after their next page load: logo and name in the top bar and on the sign-in screen once the company is picked, and the brand colour on primary buttons, the active menu item and highlights. Spec 005's emails use the company's brand colour and display name in their HTML layout. A company with no branding set gets the product defaults (Workmate HRM name, default logo and colour). Acme and Globex are seeded with branding equivalent to today's colours, so the demo still tells them apart.

**Why this priority**: Per-company identity is the multi-tenant value of theming; without it, a shared theme only replaces one look with another.

**Independent Test**: As Khalid, set Acme's logo, name and colour; sign in as Sara (Acme) and as Ali (Globex); Acme shows the new branding, Globex is unchanged; an Acme email shows the new colour and name.

**Acceptance Scenarios**:

1. **Given** Khalid saves a brand colour and logo for Acme, **When** Sara next loads the app, **Then** she sees Acme's logo, name and colour; **And** Ali (Globex) still sees Globex's branding (tenant isolation).
2. **Given** the vendor admin acts as Globex, **Then** they see Globex's branding; switching to Acme switches the branding.
3. **Given** an admin uploads a file that is not an image, is larger than the allowed size, or enters an invalid colour, **Then** it is refused with a clear message and the previous branding stays.
4. **Given** a brand colour too light or too dark to read text on, **Then** the system still keeps buttons and links readable (it picks a readable text colour on top of the brand colour, or warns before saving).
5. **Given** Khalid resets branding to default, **Then** Acme shows the product defaults.
6. **Given** a non-admin calls the branding settings, **Then** they are refused; anyone signed in to the company may read its branding (it's shown to everyone).
7. **Given** the sign-in screen with a company picked, **Then** it shows that company's logo and name; with no company picked, the product defaults.

---

### User Story 3 - Document the theming decision for go/no-go (Priority: P2)

The PoC records what ABP theming gave, what it cost, and what it couldn't do, so the go/no-go decision (Phase G) can weigh it: which ABP theme pieces were used (the theme's styles and scripts) and which weren't (server-rendered layout, menu and toolbar contributors, branding provider, Account pages) and what adopting them would cost, the gaps of the free theme compared with the commercial one (for example a single colour style, no light/dark switcher), and the effort.

**Why this priority**: The PoC's purpose is decision-ready evidence; the UI itself is a demo.

**Independent Test**: README and HANDOVER contain a "Theming" section with these facts, and CHANGELOG lists each change.

**Acceptance Scenarios**:

1. **Given** the feature is done, **Then** HANDOVER has a Phase L entry stating what was adopted from ABP theming, what stayed custom and why, the free-vs-commercial gaps found, and open follow-ups.

---

### Edge Cases

- Dark mode: the app shows the theme's single colour style even when the operating system is in dark mode (FR-010).
- A company's logo fails to load (deleted file, broken image): the name is shown instead, and the layout doesn't break.
- A very wide or very tall logo: it's scaled to fit the top-bar slot without distorting.
- A long company display name: it's shortened with an ellipsis in the top bar; the full name shows on hover.
- The vendor admin (host, no company of their own): sees the product defaults until they pick a company to act as.
- A deactivated employee's open session (spec 004): unchanged; branding doesn't affect access.
- The theme's assets fail to load (offline, blocked file): the app stays usable, unstyled, rather than blank, the same principle as spec 006's renderer fallback.
- Right-to-left: the RTL toggle switches the theme into its right-to-left variant (the theme ships RTL styles); the side menu moves to the right.
- Branding changed while people are signed in: they see the new branding on their next page load; no live push is needed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The UI MUST use ABP's open-source LeptonX Lite theme at the version that matches the project's ABP version. No commercial theme or package may be added (constitution).
- **FR-002**: The existing single page MUST keep its structure and adopt the theme's look: LeptonX Lite's styles, layout structure (side menu, top bar, content area, mobile menu) and scripts, served by the app. Menu visibility stays driven by the signed-in person's permissions from the API. ABP's server-rendered layout, menu and toolbar contributors and branding provider are not used in the PoC; HANDOVER records them as the production path, with their cost (clarification Q1).
- **FR-003**: The side menu MUST show only the sections the signed-in person's permissions allow (Principle VII), with Inbox showing its waiting count; the server MUST still refuse every admin call from a non-admin regardless of what the menu shows.
- **FR-004**: The top bar MUST show the company branding (logo, or name if there's no logo), the signed-in person's name and company, a user menu with Sign out, and for the vendor admin the "Acting as" company picker. The per-browser renderer toggle and the RTL toggle stay available (they're demo controls).
- **FR-005**: Sign-in MUST stay the bearer-token password sign-in of spec 003 (no self-registration, lockout unchanged). Our own sign-in, forgot-password and set-password screens (specs 003, 005) are kept and restyled in the theme's account-page look; ABP's Account module is not added (clarification Q2).
- **FR-006**: A company admin (`Workmate.Forms.Design`, or a new `Workmate.Branding.Manage` permission granted to the `Workmate.CompanyAdmin` role) MUST be able to set, preview and reset the company's display name, logo and brand colour. The vendor admin MAY do the same for any company they act as.
- **FR-007**: Logos MUST be PNG, JPEG or SVG and at most 200 KB. SVG logos MUST be made safe: no scripts and no external references. Colours MUST be valid 6-digit hex values. Text drawn on the brand colour MUST meet WCAG AA contrast (4.5:1), with the text colour chosen automatically.
- **FR-008**: Branding MUST be stored per company and never visible to, or changeable by, another company (Principle IV). Reading a company's branding requires being signed in to that company. The sign-in screen MAY show a company's logo and name to anonymous visitors only after that company is picked; these two are public by design.
- **FR-009**: Spec 005's HTML email layout MUST use the company's brand colour and display name; the plain-text part is unchanged.
- **FR-010**: The UI MUST use the free theme's single colour style; the operating-system dark-mode styling is removed. HANDOVER records "no light/dark switch in the free theme" as a finding (clarification Q3).
- **FR-011**: Existing behaviour MUST be preserved: every `demo.py` step (1–13 and the restart check) passes unchanged except for UI-only wording; the native/SurveyJS renderer switch, the RTL toggle, the email deep links and the forgot/set-password flow keep working.
- **FR-012**: The seeded companies MUST get default branding equivalent to today's colours (Acme teal `#0f6e6e`, Globex indigo `#4a4fb0`) and their names, so the demo still tells them apart; the hard-coded per-company colours in the page are removed.
- **FR-013**: The theme's assets MUST be served by the app itself (no external CDN at run time), as with SurveyJS (spec 006).

### Key Entities

- **Company branding** (one per company): display name (optional; defaults to the company name), logo (optional image: PNG/JPEG/SVG, at most 200 KB), brand colour (optional hex; defaults to the product colour). Belongs to one company; changed only by that company's admin, or by the vendor admin acting as it.
- **Product default branding**: the name "Workmate HRM", a default logo and colour, used by the vendor admin before they pick a company, on the sign-in screen before a company is picked, and for companies without their own branding.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of today's user-facing actions (every section and action listed in README demo steps 1–13) can be completed in the themed UI, and `demo.py` passes with 0 failures.
- **SC-002**: A company admin can change their company's logo, name and colour in under 2 minutes, and every person in that company sees it on their next page load; no other company's screens change.
- **SC-003**: Every person sees only the menu sections their permissions allow, checked for one employee, one company admin and the vendor admin.
- **SC-004**: The app shows correctly at phone width (360 px) and desktop width (1440 px), and in right-to-left mode, with no horizontal scrolling of the page.
- **SC-005**: Text on brand-coloured elements meets WCAG AA contrast for every seeded company and for any colour an admin can save.
- **SC-006**: HANDOVER records the adopt/keep-custom/gaps findings for the go/no-go decision.

## Assumptions

- "ABP theming" means ABP's MVC/Razor Pages theming system and its free themes. Basic Theme is plain Bootstrap; LeptonX Lite is the modern free theme and is chosen here because it is what ABP's startup templates ship. Angular and Blazor themes are out of scope: this PoC has no Angular or Blazor UI.
- Commercial LeptonX (style switcher, extra layouts, theme builder) is out of scope (constitution: open-source ABP only). Its gaps are recorded as findings, not worked around with paid packages.
- Fonts and icons come from the theme's own bundled assets; no external font service.
- No per-user theme preferences; branding is per company only.
- Language switching (the theme has a language menu) stays out of scope: the PoC is English-only, with an RTL visual check only.
- Logo storage uses the app's own database or file store; no external storage service.
- Spec 005's email layout already has a company-name header; this feature only changes its colour and name.
