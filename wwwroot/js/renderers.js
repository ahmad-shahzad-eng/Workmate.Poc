// ---------- Renderer contract: both engines sit behind one factory (Phase K, spec 006 FR-002) ----------
// createRenderer(kind, schema, values, readOnly) -> { mount(container), getData(), validate() }
// "kind" is "native" or "surveyjs". Callers never touch SurveyJS or the native markup directly.

// Uses the global `esc` already defined by index.html's own script (loaded after this file, but not
// called until a view actually renders — plain <script> tags share one global scope).

function isVisible(field, data) {
  if (!field.visibleIf) return true;
  const shown = data[field.visibleIf.field];
  return String(shown ?? "").toLowerCase() === String(field.visibleIf.equals ?? "").toLowerCase();
}

// ---------- Native renderer: today's markup, extended with description/placeholder and a static visibleIf hint ----------
function createNativeRenderer(schema, values, readOnly) {
  let container;

  function fieldHtml(f) {
    const id = `f_${f.key}`, v = values[f.key] ?? "";
    const hint = f.visibleIf ? `<small class="meta" style="margin:2px 0 0">Only needed when ${esc(f.visibleIf.field)} = ${esc(f.visibleIf.equals)}</small>` : "";
    if (readOnly) {
      if (!isVisible(f, values)) return ""; // hidden fields are omitted when empty, native (US4 scenario 5)
      const shown = f.type === "checkbox" ? (v === "true" ? "Yes" : "No") : (v || "Not provided");
      return `<div class="field"><label>${esc(f.label)}</label><div class="value">${esc(shown)}</div></div>`;
    }
    const label = `<label for="${id}">${esc(f.label)}${f.required ? ' <span class="req" aria-hidden="true">*</span>' : ""}</label>`;
    const desc = f.description ? `<small class="meta" style="margin:0 0 4px">${esc(f.description)}</small>` : "";
    const ph = f.placeholder ? ` placeholder="${esc(f.placeholder)}"` : "";
    const req = f.required ? "required" : "";
    let input;
    switch (f.type) {
      case "textarea": input = `<textarea id="${id}" name="${f.key}" ${req}${ph}>${esc(v)}</textarea>`; break;
      case "select": input = `<select id="${id}" name="${f.key}" ${req}><option value="">Choose…</option>${
        f.options.map(o => `<option ${o === v ? "selected" : ""}>${esc(o)}</option>`).join("")}</select>`; break;
      case "checkbox": input = `<input type="checkbox" id="${id}" name="${f.key}" ${v === "true" ? "checked" : ""}>`; break;
      case "number": input = `<input type="number" step="any" id="${id}" name="${f.key}" value="${esc(v)}" ${req}${ph}>`; break;
      case "date": input = `<input type="date" id="${id}" name="${f.key}" value="${esc(v)}" ${req}>`; break;
      default: input = `<input type="text" id="${id}" name="${f.key}" value="${esc(v)}" ${req}${ph}>`;
    }
    return `<div class="field">${label}${desc}${input}${hint}</div>`;
  }

  return {
    mount(el) { container = el; container.innerHTML = schema.fields.map(fieldHtml).join(""); },
    getData() {
      const data = {};
      for (const f of schema.fields) {
        const input = container.querySelector(`[name="${f.key}"]`);
        if (input) data[f.key] = f.type === "checkbox" ? String(input.checked) : input.value;
      }
      return data;
    },
    // Self-contained (works whether mounted into a <div> or a <form>): checks each control directly
    // via the browser's own `required` attribute. The server is the real gatekeeper either way (US3).
    validate() {
      let ok = true;
      container.querySelectorAll("[name]").forEach(el => { if (el.reportValidity && !el.reportValidity()) ok = false; });
      return ok;
    }
  };
}

// ---------- SurveyJS adapter: schema -> SurveyJS JSON, SurveyJS answers -> our string formats ----------
function toSurveyJson(schema) {
  const pages = [];
  const pageByName = {};
  function pageFor(name) {
    const key = name || "";
    if (!pageByName[key]) { pageByName[key] = { name: key || "page1", elements: [] }; pages.push(pageByName[key]); }
    return pageByName[key];
  }

  for (const f of schema.fields) {
    const q = { name: f.key, title: f.label, isRequired: !!f.required };
    if (f.description) q.description = f.description;
    if (f.placeholder) q.placeholder = f.placeholder;
    if (f.visibleIf) q.visibleIf = `{${f.visibleIf.field}} = '${String(f.visibleIf.equals).replace(/'/g, "\\'")}'`;
    switch (f.type) {
      case "textarea": q.type = "comment"; break;
      case "select": q.type = "dropdown"; q.choices = f.options; break;
      case "checkbox": q.type = "boolean"; q.labelTrue = "Yes"; q.labelFalse = "No"; break;
      case "number": q.type = "text"; q.inputType = "number"; break;
      case "date": q.type = "text"; q.inputType = "date"; break;
      default: q.type = "text";
    }
    pageFor(f.page).elements.push(q);
  }
  return { title: schema.title, showNavigationButtons: false, pages };
}

function fromSurveyData(schema, data) {
  const out = {};
  for (const f of schema.fields) {
    if (!(f.key in data) || data[f.key] == null) continue;
    const v = data[f.key];
    out[f.key] = f.type === "checkbox" ? (v === true || v === "true" ? "true" : "false") : String(v);
  }
  return out;
}

function createSurveyJsRenderer(schema, values, readOnly) {
  let survey, container, failed = false;

  return {
    mount(el) {
      container = el;
      if (typeof Survey === "undefined") { failed = true; container.innerHTML = `<p class="message error">SurveyJS failed to load; showing the native form instead.</p>`; return createNativeRenderer(schema, values, readOnly).mount(el); }
      survey = new Survey.Model(toSurveyJson(schema));
      survey.data = values;
      survey.mode = readOnly ? "display" : "edit";
      if (readOnly) {
        // Hidden fields stay hidden read-only (US4 scenario 5) — SurveyJS already hides them via visibleIf.
      } else if (survey.pages.length > 1) {
        // We own navigation so the same outer "Send request" button works for every page count.
        const nav = document.createElement("div");
        nav.className = "actions";
        const prev = Object.assign(document.createElement("button"), { type: "button", className: "btn secondary", textContent: "Previous" });
        const next = Object.assign(document.createElement("button"), { type: "button", className: "btn secondary", textContent: "Next" });
        const sync = () => { prev.disabled = survey.isFirstPage; next.hidden = survey.isLastPage; };
        prev.addEventListener("click", () => { survey.prevPage(); sync(); });
        next.addEventListener("click", () => { if (survey.currentPage.validate(true)) { survey.nextPage(); sync(); } });
        nav.append(prev, next);
        container.appendChild(nav);
        sync();
      }
      const surveyEl = document.createElement("div");
      container.insertBefore(surveyEl, container.firstChild);
      survey.render(surveyEl);
    },
    getData() { return failed ? createNativeRenderer(schema, values, readOnly).getData() : fromSurveyData(schema, survey.data); },
    // Validates every page (not just the current one) before final submit; per-page gating for
    // Next/Previous is handled separately in mount() via currentPage.validate().
    validate() { return failed ? true : survey.validate(true, true); }
  };
}

function createRenderer(kind, schema, values = {}, readOnly = false) {
  return kind === "surveyjs" ? createSurveyJsRenderer(schema, values, readOnly) : createNativeRenderer(schema, values, readOnly);
}
