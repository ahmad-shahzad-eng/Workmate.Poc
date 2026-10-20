# Tasks: Switchable form renderer (native or SurveyJS Form Library)

Spec: [spec.md](./spec.md) · Plan: [plan.md](./plan.md)
Gate after each phase: stop old app → `dotnet build` → `dotnet run` → `python3 demo.py` (all pass). One CHANGELOG line per task.
Note: this phase adds a column to the existing `Forms_Definitions` table — `Poc:ResetDatabaseOnStartup=true` is required once after T002/T003 land (`EnsureCreatedAsync` does not migrate an existing database; see plan Risks).

## Phase 1: Setup

- [X] T001 Vendor SurveyJS 3.2.1 (confirmed MIT, browser-ready) into `wwwroot/lib/surveyjs/`: `survey.core.min.js`, `survey-js-ui.min.js`, `survey-core.min.css` from the published npm packages, plus a `LICENSE` note (no CDN at run time — FR-001, FR-006: no Creator/Dashboard/PDF Generator)
- **Gate:** `dotnet build`; the three vendored files are served at their `wwwroot` URLs

## Phase 2: Foundational (blocks all stories)

- [X] T002 In `Domain/Forms.cs`: `FormRenderer` constants (`default`/`native`/`surveyjs`); `FormDefinition.Renderer` (defaults to `default`); `FormField.VisibleIf` (new `VisibleIfRule { Field, Equals }`), `Description`, `Placeholder`, `Page`; new `CompanyFormSettings` entity (`TenantId`, `DefaultRenderer` — only `native`/`surveyjs`, never `default`)
- [X] T003 In `Data/PocDbContext.cs`: register `CompanyFormSettings` → `Forms_CompanySettings` (unique index on `TenantId`); confirm `Forms_Definitions`' new `Renderer` column is covered by the reset note above
- [X] T004 In `Domain/Forms.cs`'s `FormSchema.Parse`: validate each field's `VisibleIf` — the named field MUST exist and appear **earlier** in `Fields`; otherwise reject naming the field (US4 scenario 4)
- **Gate:** `dotnet build`; start with `Poc:ResetDatabaseOnStartup=true` once; `demo.py` steps 1–9 pass on the freshly-reset database (regression check before any renderer work)

## Phase 3: User Story 1 — Same form, either renderer (P1)

- [X] T005 [US1] Create `wwwroot/js/renderers.js`: extract today's `renderForm`/`readForm` out of `index.html` as `NativeRenderer`, behind a shared contract `createRenderer(kind, schema, values, readOnly) → { mount(container), getData(), validate() }`; native `mount()` also shows `description`/`placeholder` and a static "only needed when X = Y" hint for `visibleIf` fields (native never hides — US4 table)
- [X] T006 [US1] In `wwwroot/js/renderers.js`: add `SurveyJsRenderer` — schema→SurveyJS JSON adapter for all six field types (text→text, textarea→comment, select→dropdown, checkbox→boolean, number→text/`inputType:"number"`, date→text/`inputType:"date"`; `page` groups fields into SurveyJS pages in order; `visibleIf` → SurveyJS's `visibleIf` expression); `mount()` via `new Survey.Model(json).render(container)`; `getData()` converts SurveyJS booleans/numbers/dates back to our string formats (`"true"/"false"`, invariant number, `yyyy-MM-dd` — Edge Cases); `validate()` wraps SurveyJS's own validation
- [X] T007 [US1] In `wwwroot/index.html`: load the vendored SurveyJS scripts with an `onerror` fallback to `NativeRenderer` plus a one-line notice (Edge Cases — never blocks submission); switch `viewNew` and `showDetail`'s read-only render from raw `renderForm(...)` HTML-string calls to `renderer.mount(container)` / `.getData()` / `.validate()`
- **Gate (US1 Independent Test):** submit the same form and answers through both renderers; confirm the stored submission and its `Forms_SubmissionValues` rows are identical; open a submitted request read-only in both renderers

## Phase 4: User Story 2 — Switch the renderer (P1)

- [X] T008 [US2] In `Application/Dtos.cs` and `Application/FormAppService.cs`: `FormSummaryDto.EffectiveRenderer` (`form.Renderer == default ? tenant's CompanyFormSettings.DefaultRenderer : form.Renderer`); `FormDesignDto`/`FormListItemDto` expose the raw `Renderer`; `SetRendererAsync(key, renderer)`; `GetCompanyRendererAsync`/`SetCompanyRendererAsync` (find-or-create the tenant's single `CompanyFormSettings` row), admin-only via `EnsureAdminAsync()`
- [X] T009 [US2] Add routes to `Controllers/PocController.cs`: `PUT /forms/{key}/renderer`, `GET`/`PUT /company-settings/renderer`
- [X] T010 [US2] In `wwwroot/index.html`: top-bar "Renderer: Native / SurveyJS" demo toggle (remembered in `localStorage`, this browser only); form designer renderer picker (company default / native / surveyjs) and a company-settings renderer control; resolve precedence demo toggle → per-form → company default before calling `createRenderer`; add the "preview in the other renderer" button (FR-005) to the designer preview pane
- **Gate (US2 Independent Test):** the demo toggle overrides form and company settings in that browser only; a form set to `surveyjs` renders with SurveyJS even when the company default is `native`; changing any of the three settings changes no form version, submission or reporting value

## Phase 5: User Story 3 — Server stays the gatekeeper (P1)

- [X] T011 [US3] In `Application/SubmissionAppService.cs`'s `Validate`: single forward pass over `schema.Fields` (valid since `VisibleIf` can only reference an earlier field) — track accepted values as they're read; a field whose `VisibleIf` doesn't match the accepted value so far is skipped (not required, value dropped, no typed-value row — FR-007); comparison is case-insensitive, same rule as `ProcessBinding.Matches`
- [X] T012 [US3] Add `demo.py` checks: a request sent straight to the API with a missing required field, an unknown option, or a non-number in a number field is refused with a field-level message, independent of which renderer is configured
- **Gate (US3 Independent Test):** direct API submissions with bad data are refused with clear messages regardless of the configured renderer

## Phase 6: User Story 4 — Conditional fields, help text, pages (P2)

- [X] T013 [US4] In `wwwroot/index.html`'s form designer: `visibleIf` editor — a field picker limited to fields earlier in the list, with the value input adapting to the picked field's type by reusing `conditionValueInput(schema, fieldKey, value)` from Phase D's routing section verbatim ("same rule as routing" — spec Decision context); `description`/`placeholder` inputs; a `page` grouping input per field
- [X] T014 [US4] In `Data/DemoDataSeeder.cs`: seed Acme's Leave request v2 with a conditional required field ("Medical note reference", `visibleIf: leaveType = Sick`) across two pages (FR-009); set Acme's company default renderer to `surveyjs`, Globex's to `native` (also exercises US2's tenant isolation)
- [X] T015 [US4] Add `demo.py` checks against the T014 seed: Annual leave submits without the hidden field; a value sent for it while `leaveType = Annual` is dropped and not stored; `leaveType = Sick` with no value is refused ("Medical note reference is required") — both at the API level, independent of renderer (US4 scenarios 1–2 are manual/UI, noted in the gate below, not automatable via the API alone)
- **Gate (US4 Independent Test, part API / part manual):** the API-level checks from T015 pass; manually in SurveyJS — the conditional field hides/shows live, Next is blocked until page 1 is valid on the 2-page form, and a read-only submitted request keeps hidden fields hidden (SurveyJS) or omits them when empty (native)

## Phase 7: Polish

- [X] T016 Manual check: `dir="rtl"` toggle (demo-only, no translations) in both renderers, both tenants; record SC-003 findings (field coverage, look and feel, RTL, effort to extend) in `HANDOVER.md` as a note for Phase G's design-doc comparison
- [X] T017 Update `README.md`, `CHANGELOG.md`, `HANDOVER.md` (vendored SurveyJS, renderer settings, the reset-required note from Phase 2, mark Phase K done)
- [X] T018 Final full loop: fresh reset → `demo.py` steps 1–10 in both tenants → manual RTL check from T016

## Order

T001 → T002–T004 → T005–T007 → T008–T010 → T011–T012 → T013–T015 → T016–T018. T011–T012 (US3, server-side) have no dependency on T005–T010 (US1/US2, frontend) and can run in parallel with them once Phase 2 passes. T013 depends on T006 (the adapter) only loosely — it edits the designer, not the renderer itself — and on Phase D's existing `conditionValueInput`, already in `wwwroot/index.html`. T014 should land before T015 so there's seeded data to check against.
