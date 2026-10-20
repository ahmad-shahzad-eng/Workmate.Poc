# Implementation Plan: Switchable form renderer (native or SurveyJS Form Library)

**Branch**: `006-surveyjs-renderer` | **Date**: 2026-10-20 | **Spec**: [spec.md](./spec.md)

## Summary

Keep our JSON schema canonical; add a `Renderer` setting (per form, overridable by a per-tenant company default, overridable by a browser-only demo toggle) and three optional schema extensions (`visibleIf`, `description`/`placeholder`, `page`). Extract the existing native renderer out of `index.html` into a shared `renderers.js` module behind one contract both renderers implement, and add a SurveyJS adapter next to it. The server evaluates `visibleIf` itself (same rule as routing's condition matching) so a hidden field's value is never required or stored, regardless of which renderer a client used.

## Technical Context

- **Language/Version**: C# `net10.0` backend (unchanged); vanilla JS frontend, no build step (unchanged — matches the existing single-page demo UI)
- **Primary Dependencies**: SurveyJS Form Library — `survey-core` + `survey-js-ui`, MIT. **Pin 3.2.1**, not "2.x" as the spec's Decision context assumed: that line no longer exists on npm (confirmed via the registry — latest is 3.2.1). Vendored as static files under `wwwroot/lib/surveyjs/` (UMD/browser builds confirmed downloadable from the published package: `survey.core.min.js`, `survey-js-ui.min.js`, `survey-core.min.css`), no CDN at run time, no NuGet package, no ABP/Elsa change.
- **Storage**: SQLite, same `workmate-poc.db`. New table `Forms_CompanySettings` (one row per tenant); **new columns on the existing `Forms_Definitions` table** (`Renderer`) and new properties inside `FormDefinitionVersion.SchemaJson`'s field objects (`visibleIf`/`description`/`placeholder`/`page` — JSON, no schema change there). See Risks: a new column on an *existing* table needs a reset on first run, unlike Phase D's brand-new tables.
- **Testing**: `demo.py` — next available step number (the spec assumes "step 13", which presumes Phases H/I/J are implemented first and claim 10–12; if this phase ships before them, use the next free number instead — currently 10)
- **Constraints**: no SurveyJS Creator/Dashboard/PDF Generator (commercial, constitution boundary already names this); canonical schema and `Forms_SubmissionValues` typed-value behavior unchanged (Principles I, V)

## Constitution Check

| Principle / gate | Status |
| --- | --- |
| I Immutable form versions | Unchanged: `Renderer` lives on `FormDefinition` (not the version), switching it touches no `FormDefinitionVersion` row; `visibleIf`/`description`/`placeholder`/`page` are ordinary new field-schema properties, versioned exactly like today's `type`/`options` |
| II Commit before workflow | Not touched |
| III Assignment from HR data | Not touched |
| IV Tenant isolation | `Forms_CompanySettings` is `IMultiTenant`, one row per tenant; `demo.py` proves Acme/Globex can set different defaults with no bleed |
| V Typed reporting values | A field hidden by `visibleIf` gets no typed-value row (FR-007) — explicit new rule, not a violation: nothing was reported because nothing was answered |
| VI One door into Elsa | Not touched |
| VII Admin is a permission | Renderer settings and `visibleIf`/`description`/`placeholder`/`page` editing stay behind `CurrentEmployeeProvider.EnsureAdminAsync()`, same as today's form designer |
| Gates | CHANGELOG line per change; `demo.py` step added; build/run/demo loop must pass; no commercial SurveyJS package |

Result: **PASS**, no exceptions needed.

## Design

### 1. Schema additions (`Domain/Forms.cs`)

```csharp
public static class FormRenderer { public const string Default = "default", Native = "native", SurveyJs = "surveyjs"; }

// FormDefinition gains:
public string Renderer { get; set; } = FormRenderer.Default;

// FormField gains (all optional):
public VisibleIfRule? VisibleIf { get; set; }   // { Field, Equals } — same shape/semantics as ProcessBinding's condition
public string? Description { get; set; }
public string? Placeholder { get; set; }
public string? Page { get; set; }

public class VisibleIfRule { public string Field { get; set; } = default!; public string Equals { get; set; } = default!; }
```

New tenant-owned singleton-per-tenant entity for the company default:

```csharp
public class CompanyFormSettings : AggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public string DefaultRenderer { get; set; } = FormRenderer.Native; // "native" | "surveyjs" — never "default" here
}
```

`Data/PocDbContext.cs`: `ToTable("Forms_CompanySettings")`, unique index on `TenantId`. `FormAppService` (or a small new app-service method) does find-or-create-on-first-read so there's always exactly one row once touched.

### 2. `FormSchema.Parse` validates `visibleIf` (US4 scenario 4)

For each field, if `VisibleIf` is set: the named field must exist **and appear earlier in the `Fields` list** (same linear-order constraint a wizard needs); otherwise `UserFriendlyException` naming the field. This runs wherever `FormSchema.Parse` already runs (draft save, publish, submission validation) — no new call sites.

### 3. Submission validation becomes visibility-aware (`Application/SubmissionAppService.cs`)

`Validate` already loops `schema.Fields` in order; because `visibleIf` can only reference an earlier field, a single forward pass is enough — track accepted values as they're validated, then for each field:

```csharp
var isVisible = f.VisibleIf == null || string.Equals(accepted.GetValueOrDefault(f.VisibleIf.Field), f.VisibleIf.Equals, StringComparison.OrdinalIgnoreCase);
if (!isVisible) continue; // required is ignored, nothing stored, no typed value (FR-007) — matches ProcessBinding.Matches' comparison
if (string.IsNullOrEmpty(value)) { if (f.Required) errors.Add(...); continue; }
...
```

Only fields that pass `isVisible` get into `clean`/typed `Forms_SubmissionValues` rows, so a value sent for a hidden field is silently dropped (US4 scenario 2) rather than rejected — the field simply isn't evaluated.

### 4. Effective renderer + settings (`Application/FormAppService.cs`, `Application/Dtos.cs`)

- `FormSummaryDto` (the employee-facing `/forms` list) gains `EffectiveRenderer`: `form.Renderer == Default ? tenant's CompanyFormSettings.DefaultRenderer : form.Renderer`.
- `FormDesignDto`/`FormListItemDto` (admin view) gain the raw `Renderer` (so the designer can show "company default" distinctly from an explicit override).
- New admin-only endpoints: `PUT /forms/{key}/renderer` (body `{ renderer }`, one of the three values) and `GET/PUT /company-settings/renderer` (body `{ defaultRenderer }`, `native`/`surveyjs` only). Both reuse `EnsureAdminAsync()`.

### 5. Frontend: one renderer contract, two implementations (`wwwroot/js/renderers.js`, new)

Move today's `renderForm`/`readForm` out of `index.html` as `NativeRenderer`, written against a shared contract both renderers implement (this is the central abstraction FR-002 asks for — the existing native renderer returns an HTML string today, which a SurveyJS instance can't do, since SurveyJS mounts itself imperatively into a container):

```js
// renderers.js
export function createRenderer(kind, schema, values, readOnly) {
  // kind: "native" | "surveyjs"
  // returns { mount(container), getData(), validate() } for both kinds
}
```

- `NativeRenderer`: `mount()` sets `container.innerHTML` to today's markup (extended for `description`/`placeholder`, and a static "only needed when X = Y" hint for `visibleIf` fields — native never hides); `getData()`/`validate()` wrap today's `readForm` logic.
- `SurveyJsRenderer`: a schema→SurveyJS JSON adapter (`text→text`, `textarea→comment`, `select→dropdown`, `checkbox→boolean`, `number→text` with `inputType:"number"`, `date→text` with `inputType:"date"`; `visibleIf` → SurveyJS's own `visibleIf: "{field} = 'value'"` expression string; `page` groups fields into SurveyJS pages in order); `mount()` calls `new Survey.Model(json).render(container)`; `getData()` converts SurveyJS's native booleans/numbers back to our string formats (`"true"/"false"`, invariant number, `yyyy-MM-dd` — Edge Cases); `validate()` calls the SurveyJS model's own validation and reports the first error per question, consistent with the native renderer's message shape.
- If the vendored script fails to load (`<script>` `onerror`, checked once at startup), `createRenderer("surveyjs", ...)` falls back to `NativeRenderer` and the call site shows a one-line notice — never blocks submission (Edge Cases).
- `index.html` call sites (`viewNew`, `showDetail` read-only, designer preview) switch from `main.innerHTML = renderForm(...)` to `renderer.mount(container)` + read via `renderer.getData()`/`.validate()` on submit. The demo toggle (`localStorage`, top bar) and the designer's renderer picker both just choose which `kind` to pass in; the "preview in the other renderer" button (FR-005) re-mounts with the other kind into the same preview pane.
- The `visibleIf` editor in the form designer (field picker + a value input that adapts to the picked field's type) reuses `conditionValueInput(schema, fieldKey, value)` from Phase D's routing section verbatim — same "same rule as routing" reuse the spec itself calls for.

### 6. RTL check

`dir="rtl"` on the page root toggled by a demo-only control (no translations); confirm both the native markup and SurveyJS's own RTL support (`survey.locale`/CSS logical properties) don't visually break. Manual check only, no automated assertion (SC-003 is a recorded finding, not a demo.py check).

## Risks and research (verify on first build)

- **SurveyJS version**: pin **3.2.1** (confirmed on npm, MIT), not the spec's assumed "2.x" line — note this in CHANGELOG the same way Phase C noted the Elsa package rename.
- **SurveyJS JS API surface** (`Survey.Model`, `.render(containerId)`, `.data`, `.onComplete`/`.validate()`, `visibleIf` expression syntax): based on SurveyJS's documented API shape, not verified against the actual vendored 3.2.1 build in this repo yet — adjust only `renderers.js`'s `SurveyJsRenderer` if method names differ.
- **EnsureCreatedAsync does not migrate existing databases.** Phase D added brand-new tables, which only needed to exist by first *write*; this phase adds a **column to the existing `Forms_Definitions` table**, which breaks on the very first *read* of any form on a non-reset database. Document clearly (README/CHANGELOG) that `Poc:ResetDatabaseOnStartup=true` is required once after deploying this phase, exactly like a real migration would be.
- **`demo.py` step number**: use the next free number at implementation time rather than hardcoding the spec's "13" — currently that's 10, since Phases H/I/J haven't been implemented yet.

## Files touched

`Domain/Forms.cs` (`Renderer`, `VisibleIfRule`, field additions, `CompanyFormSettings`), `Data/PocDbContext.cs`, `Application/FormAppService.cs`, `Application/SubmissionAppService.cs`, `Application/Dtos.cs`, `Controllers/PocController.cs`, `Data/DemoDataSeeder.cs` (Acme default SurveyJS, Globex Native; Leave request v2 with a conditional field + two pages per FR-009), `wwwroot/lib/surveyjs/` (new, vendored), `wwwroot/js/renderers.js` (new), `wwwroot/index.html` (top-bar toggle, designer renderer setting + preview button, visibleIf editor reusing `conditionValueInput`), `demo.py`, `README.md`, `CHANGELOG.md`, `HANDOVER.md`.
