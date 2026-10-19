# Feature Specification: Switchable form renderer (native or SurveyJS Form Library)

**Feature Branch**: `006-surveyjs-renderer` | **Created**: 2026-10-20 | **Status**: Draft (seeded)

**Input**: Ahmad, 2026-10-20: "Use the SurveyJS renderer, or keep both, with an option to switch in the PoC."

## Decision context

- SurveyJS **Form Library** (renderer) is MIT and free for SaaS. SurveyJS **Creator** (designer) is commercial, per developer; not used here (constitution boundary).
- Goal: compare the two renderers on the same forms, keep the server independent of the renderer, and leave the final choice for the go/no-go (Phase G).
- Approach: **our form schema stays canonical** (it is what versions, validation, typed reporting values and routing read). The SurveyJS renderer receives JSON generated from it by an adapter in the browser. Switching never changes stored data.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Same form, either renderer (Priority: P1)
Sara opens New request → Leave request. With the renderer set to SurveyJS she sees the SurveyJS form; set to Native she sees today's form. Both show the same fields, labels, options and required marks, and both submit the same data.

**Acceptance Scenarios**:
1. **Given** any seeded form, **When** it is submitted through either renderer with the same answers, **Then** the stored submission and its `Forms_SubmissionValues` rows are identical.
2. **Given** a submitted request, **When** it is opened in My requests or the approver's Inbox, **Then** it renders read-only with its own form version in the selected renderer (Principle I).
3. **Given** all six field types (text, textarea, select, checkbox, number, date), **Then** each maps to a SurveyJS question (text, comment, dropdown, boolean, text/inputType number, text/inputType date) with the same key as the question `name`.

### User Story 2 - Switch the renderer (Priority: P1)
Three levels, most specific wins:

| Level | Who sets it | Where |
| --- | --- | --- |
| Demo toggle | anyone, PoC only | "Renderer: Native / SurveyJS" switch in the top bar; remembered in the browser |
| Per form | company admin | form designer setting "Renderer: company default / Native / SurveyJS" |
| Company default | company admin (vendor admin for any company) | company settings |

**Acceptance Scenarios**:
1. **Given** the demo toggle is set, **Then** it overrides form and company settings in that browser only.
2. **Given** no toggle, a form set to SurveyJS renders with SurveyJS even if the company default is Native.
3. **Given** the setting changes, **Then** no form version, submission or reporting value changes.

### User Story 3 - Server stays the gatekeeper (Priority: P1)
Whatever the browser does, the API validates submissions against the form version (required, type, options) as today.

**Acceptance Scenarios**:
1. **Given** a request sent straight to the API with a missing required field, an unknown option or a non-number in a number field, **Then** it is refused with field-level messages, whichever renderer is configured.

### User Story 4 - Conditional fields, help text and pages (Priority: P2, in PoC scope, decided 2026-10-20)
The form designer can add three optional schema extensions:

| Extension | Schema | SurveyJS renderer | Native renderer |
| --- | --- | --- | --- |
| Show field only when | `"visibleIf": { "field": "leaveType", "equals": "Sick" }` (one equals condition, same rule as routing) | shown/hidden live (`visibleIf: "{leaveType} = 'Sick'"`) | field always shown, marked "only needed when Leave type = Sick" |
| Help text | `"description": "..."`, `"placeholder": "..."` | shown | shown (cheap to support) |
| Pages | `"page": "Details"` on fields; fields with the same page are grouped in order | multi-page with Next/Previous | one long form with a heading per page |

The designer marks visibleIf and pages "Full effect in SurveyJS"; when a form's renderer is Native it shows a notice, not an error.

**Acceptance Scenarios**:
1. **Given** Leave request v2 with a required "Medical note reference" visible only when Leave type = Sick, **When** Sara picks Annual in SurveyJS, **Then** the field is hidden and the request submits without it.
2. **Given** the same form, **When** a request with Leave type = Annual is sent straight to the API with a value for the hidden field, **Then** the value is dropped and not stored; **with** Leave type = Sick and no value, **Then** it is refused ("Medical note reference is required").
3. **Given** a 2-page form in SurveyJS, **Then** Next is blocked until page 1 is valid, and the submitted data matches the one-page native submission.
4. **Given** visibleIf points at a field that does not exist or comes after it, **When** saving the form, **Then** the designer refuses with a clear message.
5. **Given** a submitted request, **When** shown read-only, **Then** hidden fields stay hidden (SurveyJS) or are omitted when empty (native).

### Edge Cases
- SurveyJS script fails to load: fall back to the native renderer and show a notice; never block submission.
- Values: SurveyJS booleans and numbers are converted back to today's string format (`"true"/"false"`, invariant number, `yyyy-MM-dd`) before sending.
- Arabic / right-to-left: both renderers must work with `dir="rtl"` (check only; translations are later work).

## Requirements *(mandatory)*
- **FR-001**: Add SurveyJS Form Library (`survey-core` + `survey-js-ui`, MIT, one exact pinned 2.x version) to the demo UI, served from `wwwroot/lib/surveyjs/` (vendored, no CDN at run time).
- **FR-002**: A browser-side adapter MUST convert our schema to SurveyJS JSON and SurveyJS answers back to our submission format; both renderers sit behind one function, e.g. `renderForm(schema, values, readOnly)` → `{ getData(), validate() }`.
- **FR-003**: `FormDefinition` gets `Renderer` (`default` | `native` | `surveyjs`); the company default is stored per tenant. The API returns the effective renderer with the form.
- **FR-004**: Stored schema, versions, validation, typed values and routing are unchanged (canonical schema, Principles I and V).
- **FR-005**: The form designer's live preview MUST show the selected renderer, with a quick "preview in the other renderer" button.
- **FR-006**: No SurveyJS Creator, Dashboard or PDF Generator (commercial).
- **FR-007**: The server MUST evaluate `visibleIf` with the same rule as the UI: a hidden field is not required, and any value sent for it is discarded before storing and before typed values are written.
- **FR-008**: The form designer MUST edit `visibleIf` (pick an earlier field, then the value input adapts to its type, as in spec 002 US3), `description`, `placeholder` and `page`.
- **FR-009**: Seed Leave request v2 in Acme with a conditional required field and two pages, so the demo shows all three extensions.

## Success Criteria *(mandatory)*
- **SC-001**: All seeded forms render, submit and display read-only in both renderers with identical stored data (demo.py step 13 submits via the API using both formats' converted payloads; manual UI check for rendering).
- **SC-002**: Switching renderer at any level needs no restart and changes no stored data.
- **SC-004**: demo.py step 13 also covers the conditional field via the API (hidden value dropped; required when visible).
- **SC-003**: Findings for Phase G: a short comparison (field coverage, look and feel, RTL, effort to extend) recorded in the design doc.

## Files likely touched
New `wwwroot/lib/surveyjs/` (survey-core, survey-js-ui JS/CSS, licence file), new `wwwroot/js/renderers.js` (native renderer moved from `index.html` + SurveyJS adapter), `wwwroot/index.html` (top-bar toggle, use `renderers.js`, designer renderer setting and preview), `Domain/Forms.cs` (`Renderer` on `FormDefinition`; `VisibleIf`, `Description`, `Placeholder`, `Page` on the field model; visibility check), `Application/SubmissionAppService.cs` (drop hidden values, required only when visible), new tenant setting (company default renderer) in `Domain/` + `Data/PocDbContext.cs`, `Application/FormAppService.cs` (save setting, return effective renderer), `Application/Dtos.cs`, `Controllers/PocController.cs`, `Data/DemoDataSeeder.cs` (e.g. Acme default SurveyJS, Globex Native), `demo.py`, `README.md`.

## Assumptions
- SurveyJS is loaded as plain JS (no React/Angular build step), matching the current single-page demo UI.
- If SurveyJS is chosen for production, the canonical schema can later move to SurveyJS JSON; that is a Phase G decision, not part of this spec.
