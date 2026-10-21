#!/usr/bin/env python3
"""Runs README demo steps 1-7, 9, 10 and 11 (no args), or the two-part restart check (step 8:
`restart-before` / `restart-after`, run with a manual app restart in between), against the running
PoC. Everyone signs in for real (spec 003) via OpenIddict's password grant at /connect/token — no more
X-Demo-User header. Stdlib only."""
import json, sqlite3, sys, time, urllib.request, urllib.error, urllib.parse, os

BASE = "http://localhost:5080/api/poc"
TOKEN_URL = "http://localhost:5080/connect/token"
CLIENT_ID = "Workmate_Demo"
ACME, GLOBEX = "Acme Trading", "Globex Logistics"
DEMO_PASSWORD = "1q2w3E*"
VENDOR_EMAIL = "aramis@workmate.vendor"
LOG = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "demo.log"), "w")
RESTART_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "demo_restart.json")
RUN_SUFFIX = str(int(time.time()))[-6:]  # keeps form/company keys unique across repeated runs on a non-empty database
results = []

def log(*a):
    s = " ".join(str(x) for x in a); print(s); LOG.write(s + "\n"); LOG.flush()

def slug(tenant_name):
    return tenant_name.split()[0].lower()  # "Acme Trading" -> "acme" — matches DemoDataSeeder's email scheme

def email_for(first_name, tenant_name):
    return f"{first_name.lower()}@{slug(tenant_name)}.demo"

def sign_in(email, password=DEMO_PASSWORD, tenant_name=None):
    """ROPC grant at /connect/token (spec 003 FR-007). tenant_name is sent as the __tenant header —
    the one legitimate unauthenticated use of that header (plan.md Design §4): it just tells the
    server which company's user table to check the password against. Host users (the vendor admin)
    omit it. Returns (http_code, access_token_or_None, parsed_body)."""
    body = urllib.parse.urlencode({
        "grant_type": "password", "username": email, "password": password,
        "client_id": CLIENT_ID, "scope": "Workmate"
    }).encode()
    headers = {"Content-Type": "application/x-www-form-urlencoded", "Accept": "application/json"}
    if tenant_name: headers["__tenant"] = tenant_name
    req = urllib.request.Request(TOKEN_URL, data=body, method="POST", headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            raw = r.read().decode(); code = r.status
    except urllib.error.HTTPError as e:
        raw = e.read().decode(); code = e.code
    log(f"  POST /connect/token ({email}) -> {code} {raw[:300]}")
    payload = json.loads(raw) if raw else None
    return code, (payload.get("access_token") if code == 200 and payload else None), payload

def call(method, path, token=None, body=None, act_as_tenant=None, tenant_header=None):
    h = {"Content-Type": "application/json", "Accept": "application/json"}
    if token: h["Authorization"] = f"Bearer {token}"
    if act_as_tenant: h["X-Act-As-Tenant"] = act_as_tenant
    if tenant_header: h["__tenant"] = tenant_header
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method, headers=h)
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            raw = r.read().decode(); code = r.status
    except urllib.error.HTTPError as e:
        raw = e.read().decode(); code = e.code
    log(f"  {method} {path} -> {code} {raw[:600]}")
    try: payload = json.loads(raw) if raw else None
    except ValueError: payload = raw
    return code, payload

def check(step, ok, note=""):
    results.append((step, ok, note)); log(f"[{'PASS' if ok else 'FAIL'}] {step} {note}")

def tokens_for(tenant_name, first_names):
    """Signs in as every named demo person once; returns {first_name: access_token}."""
    out = {}
    for n in first_names:
        code, tok, _ = sign_in(email_for(n, tenant_name), tenant_name=tenant_name)
        out[n] = tok
        if tok is None: log(f"  WARNING: sign-in failed for {n} ({email_for(n, tenant_name)}): http {code}")
    return out

def roster(any_token):
    _, us = call("GET", "/users", any_token)
    return {u["name"].split()[0]: u["id"] for u in us or []}

def counts_for(any_token):
    _, us = call("GET", "/users", any_token)
    _, fs = call("GET", "/forms", any_token)
    return {"employees": len(us or []), "forms": len(fs or [])}

def inbox_task(token, submission_id, wait=8):
    end = time.time() + wait
    while True:
        _, items = call("GET", "/tasks/inbox", token)
        for i in items or []:
            if i["submissionId"] == submission_id: return i["taskId"]
        if time.time() > end: return None
        time.sleep(1)

def submit(token, data, form_key="leave-request"):
    code, sid = call("POST", "/submissions", token, {"formKey": form_key, "data": data})
    return sid if code == 200 else None

def detail(token, sid, wait_status=None, wait=8):
    end = time.time() + wait
    while True:
        _, d = call("GET", f"/submissions/{sid}", token)
        if not wait_status or (isinstance(d, dict) and d.get("status") == wait_status) or time.time() > end: return d
        time.sleep(1)

# handoverTo satisfies leave-request once a prior run's step 4 has published a version that requires it;
# medicalNoteReference satisfies Acme's seeded v2 when leaveType=Sick (spec 006 US4). Both are silently
# ignored by versions that don't have the field, so this works on any run and in either tenant.
annual = {"leaveType": "Annual", "startDate": "2026-11-01", "endDate": "2026-11-03", "days": "3", "reason": "Family trip", "handoverTo": "Omar"}
sick = {"leaveType": "Sick", "startDate": "2026-11-05", "endDate": "2026-11-05", "days": "1", "handoverTo": "Huda", "medicalNoteReference": "DR-10492"}

def step(name, fn):
    log(f"\n=== {name}")
    try: fn()
    except Exception as e: check(name, False, f"exception: {e!r}")

state = {}
ACME_NAMES = ["Omar", "Huda", "Sara", "Khalid", "Layla"]
GLOBEX_NAMES = ["Fatima", "Yousef", "Ali", "Noor", "Yara"]

def tenant_ids():
    """Anonymous: the sign-in screen's Company picker needs this before anyone is authenticated."""
    _, ts = call("GET", "/tenants")
    return {t["name"]: t["id"] for t in ts or []}

def s1():
    t = tokens_for(ACME, ACME_NAMES); state["acme_tok"] = t
    state["acme"] = roster(t["Sara"])
    sid = submit(t["Sara"], annual); state["s1"] = sid
    tk = inbox_task(t["Omar"], sid); check("1a Omar gets task", tk is not None)
    call("POST", f"/tasks/{tk}/complete", t["Omar"], {"outcome": "Approved", "comment": "OK"})
    tk2 = inbox_task(t["Huda"], sid); check("1b Huda gets task after Omar", tk2 is not None)
    call("POST", f"/tasks/{tk2}/complete", t["Huda"], {"outcome": "Approved"})
    d = detail(t["Sara"], sid, "Approved")
    check("1c Sara sees Approved + timeline", d.get("status") == "Approved" and len(d.get("events", [])) >= 3,
          f"status={d.get('status')} events={len(d.get('events', []))}")

def s2():
    t = state["acme_tok"]
    sid = submit(t["Sara"], sick); state["acme_sick_sid"] = sid
    check("2a Omar gets no task", inbox_task(t["Omar"], sid, wait=3) is None)
    tk = inbox_task(t["Huda"], sid); check("2b Huda gets task directly", tk is not None)
    d = detail(t["Sara"], sid)
    check("2c hr-only-approval workflow used", d.get("workflowDefinitionId") == "hr-only-approval", str(d.get("workflowDefinitionId")))
    if tk: call("POST", f"/tasks/{tk}/complete", t["Huda"], {"outcome": "Approved"})

def s3():
    t = state["acme_tok"]
    sid = submit(t["Sara"], annual)
    tk = inbox_task(t["Omar"], sid)
    call("POST", f"/tasks/{tk}/complete", t["Omar"], {"outcome": "Rejected", "comment": "Busy period"})
    d = detail(t["Sara"], sid, "Rejected")
    check("3a status Rejected", d.get("status") == "Rejected", str(d.get("status")))
    check("3b no HR step", len(d.get("tasks", [])) == 1 and inbox_task(t["Huda"], sid, wait=2) is None,
          f"tasks={len(d.get('tasks', []))}")

def s4():
    t = state["acme_tok"]
    _, design = call("GET", "/forms/leave-request/design", t["Khalid"])
    cur = next(v for v in design["versions"] if v["id"] == design["currentVersionId"])
    schema = json.loads(cur["schemaJson"]); old_no = cur["versionNo"]; new_no = max(v["versionNo"] for v in design["versions"]) + 1
    schema["fields"] = [x for x in schema["fields"] if x["key"] != "handoverTo"]
    schema["fields"].append({"key": "handoverTo", "label": "Handover to", "type": "text", "required": True})
    c1, _ = call("PUT", "/forms/leave-request/draft", t["Khalid"], {"schemaJson": json.dumps(schema)})
    c2, v2 = call("POST", "/forms/leave-request/publish", t["Khalid"])
    check("4a draft saved + new version published", c1 == 200 and c2 == 200 and v2.get("versionNo") == new_no, str(v2)[:100])
    _, forms = call("GET", "/forms", t["Sara"])
    f = next(x for x in forms if x["key"] == "leave-request")
    check("4b new requests use new version with new field", f["versionNo"] == new_no and "handoverTo" in f["schemaJson"])
    no_handover = {k: v for k, v in annual.items() if k != "handoverTo"}
    code, _ = call("POST", "/submissions", t["Sara"], {"formKey": "leave-request", "data": no_handover})
    check("4c new version enforces required handoverTo", code >= 400, f"http {code}")
    sid = submit(t["Sara"], dict(annual, handoverTo="Omar"))
    d2 = detail(t["Sara"], sid)
    check("4d new-version submission accepted", d2.get("versionNo") == new_no, str(d2.get("versionNo")))
    d1 = detail(t["Sara"], state["s1"])
    check("4e older submission still renders its own version, marked replaced",
          d1.get("versionNo") == old_no and d1.get("schemaJson") == cur["schemaJson"] and d1.get("versionStatus") != "Published",
          f"v={d1.get('versionNo')} versionStatus={d1.get('versionStatus')}")

def s5():
    gt = tokens_for(GLOBEX, GLOBEX_NAMES); state["globex_tok"] = gt
    g = roster(gt["Ali"]); state["globex"] = g
    at = state["acme_tok"]
    check("5a Globex sees only its users", set(g) == {"Fatima", "Yousef", "Ali", "Noor", "Yara"}, str(sorted(g)))
    _, mine_before = call("GET", "/submissions/mine", gt["Ali"])
    sid = submit(gt["Ali"], sick); state["globex_sick_sid"] = sid
    tk = inbox_task(gt["Fatima"], sid)
    check("5c Globex sick leave goes to manager (own binding)", tk is not None)
    _, mine_after = call("GET", "/submissions/mine", gt["Ali"])
    check("5b Ali's own requests gained this one and include no Acme ids",
          len(mine_after or []) == len(mine_before or []) + 1 and all(m["id"] != state["s1"] for m in mine_after or []),
          f"{len(mine_before or [])} -> {len(mine_after or [])}")
    _, inbox = call("GET", "/tasks/inbox", at["Omar"])
    check("5d Acme inbox has no Globex task", all(i["submissionId"] != sid for i in inbox or []))
    # 5e: a real, signed token from one tenant cannot reach another tenant's submission by id — claims-based
    # tenant resolution (not a header) decides which tenant's data a request can see at all (spec 003 US2 scenario 2).
    code, body = call("GET", f"/submissions/{sid}", at["Sara"])
    check("5e Acme token cannot fetch a Globex submission by id", code >= 400, f"http {code}")
    # 5f: an ordinary employee's forged X-Act-As-Tenant header has no effect — only a signed-in HOST user
    # (no tenant of their own) can use it; Sara still sees only her own company's forms (plan.md Design §4).
    tids = tenant_ids()
    _, forms_spoofed = call("GET", "/forms", at["Sara"], act_as_tenant=tids[GLOBEX])
    check("5f forged X-Act-As-Tenant header is ignored for a non-host token",
          {f["key"] for f in forms_spoofed or []} == {f["key"] for f in call("GET", "/forms", at["Sara"])[1]},
          str(sorted(f["key"] for f in forms_spoofed or [])))

def s6():
    db = os.path.join(os.path.dirname(os.path.abspath(__file__)), "workmate-poc.db")
    con = sqlite3.connect(db)
    cols = [r[1] for r in con.execute("PRAGMA table_info(Forms_SubmissionValues)")]
    rows = con.execute("SELECT * FROM Forms_SubmissionValues LIMIT 12").fetchall()
    log("  columns:", cols)
    for r in rows: log("  ", r)
    typed = "NumberValue" in cols and "DateValue" in cols
    has_num = typed and con.execute("SELECT COUNT(*) FROM Forms_SubmissionValues WHERE NumberValue IS NOT NULL").fetchone()[0] > 0
    has_date = typed and con.execute("SELECT COUNT(*) FROM Forms_SubmissionValues WHERE DateValue IS NOT NULL").fetchone()[0] > 0
    check("6 typed reportable values stored", typed and has_num and has_date, f"rows={len(rows)}")

def s7():
    biz, asset = f"business-trip-{RUN_SUFFIX}", f"asset-request-{RUN_SUFFIX}"
    t = state["acme_tok"]; gt = state["globex_tok"]
    tids = tenant_ids()
    code, b = call("GET", "/forms/all", t["Huda"])
    check("7a HR cannot open the form designer", code == 403, f"http {code}")
    code, b = call("POST", "/forms", t["Sara"], {"key": "nope", "name": "Nope", "category": "X"})
    check("7b employee cannot create forms", code == 403, f"http {code}")
    code, b = call("GET", "/forms/leave-request/design", t["Sara"])
    check("7r employee cannot read a form design (drafts included)", code == 403, f"http {code}")
    _, forms = call("GET", "/forms", t["Sara"])
    check("7c seeded employee forms published", {"leave-request", "work-from-home", "expense-claim", "letter-request"} <= {f["key"] for f in forms},
          str(sorted(f["key"] for f in forms)))
    _, wfs = call("GET", "/workflows", t["Khalid"])
    ids = {w["definitionId"] for w in wfs or []}
    check("7d admin lists published workflows", {"leave-approval", "hr-only-approval"} <= ids, str(wfs))
    code, d = call("POST", "/forms", t["Khalid"], {"key": biz, "name": "Business trip", "category": "Travel"})
    check("7e admin creates a form with a v1 draft", code == 200 and d["versions"][0]["status"] == "Draft")
    code, b = call("POST", "/forms", t["Khalid"], {"key": biz, "name": "Again", "category": "Travel"})
    check("7f duplicate key refused", code == 403 and "already exists" in json.dumps(b), f"http {code}")
    code, _ = call("POST", "/submissions", t["Sara"], {"formKey": biz, "data": {"details": "x"}})
    check("7g unpublished form cannot be submitted", code >= 400, f"http {code}")
    schema = {"title": "Business trip", "fields": [
        {"key": "tripType", "label": "Trip type", "type": "select", "options": ["Domestic", "International"], "required": True, "reportable": True, "workflowVariable": True},
        {"key": "destination", "label": "Destination", "type": "text", "required": True, "reportable": True},
        {"key": "days", "label": "Days", "type": "number", "required": True, "reportable": True}]}
    call("PUT", f"/forms/{biz}/draft", t["Khalid"], {"schemaJson": json.dumps(schema)})
    code, v = call("POST", f"/forms/{biz}/publish", t["Khalid"])
    check("7h admin publishes v1", code == 200 and v["versionNo"] == 1)
    code, b = call("POST", f"/forms/{biz}/bindings", t["Khalid"], {"workflowDefinitionId": "made-up", "priority": 10})
    check("7i unknown workflow refused", code == 403 and "not published" in json.dumps(b), f"http {code}")
    code, b = call("POST", f"/forms/{biz}/bindings", t["Khalid"], {"workflowDefinitionId": "leave-approval", "conditionField": "nope", "conditionValue": "x", "priority": 1})
    check("7j condition on unknown field refused", code == 403 and "not on the form" in json.dumps(b), f"http {code}")
    c1, _ = call("POST", f"/forms/{biz}/bindings", t["Khalid"], {"workflowDefinitionId": "leave-approval", "priority": 10})
    c2, _ = call("POST", f"/forms/{biz}/bindings", t["Khalid"], {"workflowDefinitionId": "hr-only-approval", "conditionField": "tripType", "conditionValue": "Domestic", "priority": 1})
    check("7k admin adds default + conditional routes", c1 == 200 and c2 == 200)
    sid = submit(t["Sara"], {"tripType": "Domestic", "destination": "Riyadh", "days": "2"}, biz)
    check("7l domestic trip routed to HR only", inbox_task(t["Huda"], sid) is not None and inbox_task(t["Omar"], sid, wait=2) is None)
    sid = submit(t["Sara"], {"tripType": "International", "destination": "London", "days": "4"}, biz)
    check("7m international trip routed to line manager", inbox_task(t["Omar"], sid) is not None)
    # Company admin is still an employee: their own request routes to their line manager.
    sid = submit(t["Khalid"], dict(annual, handoverTo="Sara"))
    check("7o company admin's own request reaches their manager", inbox_task(t["Omar"], sid) is not None)
    # Vendor admin designs for any company but is not an employee there — acts as Globex via X-Act-As-Tenant.
    vendor_tok = state["vendor_tok"]
    code, d = call("POST", "/forms", vendor_tok, {"key": asset, "name": "Asset request", "category": "IT"}, act_as_tenant=tids[GLOBEX])
    check("7p vendor admin creates a form for Globex", code == 200, f"http {code}")
    code, b = call("POST", "/submissions", vendor_tok, {"formKey": "leave-request", "data": annual}, act_as_tenant=tids[GLOBEX])
    check("7q vendor admin cannot submit requests", code == 403 and "does not belong" in json.dumps(b), f"http {code}")
    _, gforms = call("GET", "/forms", gt["Ali"])
    check("7n Globex does not see Acme's new form", all(f["key"] != biz for f in gforms or []))

def step9():
    """Phase D (option B): a company admin builds, publishes, binds and runs a designed workflow."""
    a_ids = state["acme"]; t = state["acme_tok"]; gt = state["globex_tok"]
    wf_key = f"expense-3step-{RUN_SUFFIX}"

    # 9a: build and publish a 3-step workflow: line manager -> role Finance -> specific user (Huda).
    code, _ = call("POST", "/workflows/design", t["Khalid"], {"key": wf_key, "name": "Expense: manager, finance, HR"})
    check("9a admin creates a designed workflow", code == 200, f"http {code}")
    steps = {"steps": [
        {"title": "Manager approval", "approverType": "line-manager", "onReject": "end"},
        {"title": "Finance review", "approverType": "role", "role": "Finance", "onReject": "back-to-previous"},
        {"title": "HR sign-off", "approverType": "user", "userId": a_ids["Huda"], "onReject": "end"},
    ]}
    code, _ = call("PUT", f"/workflows/design/{wf_key}/draft", t["Khalid"],
                   {"stepsJson": json.dumps(steps), "fallbackRule": "role:HR"})
    check("9a draft saved", code == 200, f"http {code}")
    code, v = call("POST", f"/workflows/design/{wf_key}/publish", t["Khalid"])
    check("9a published", code == 200 and v.get("status") == "Published", str(v)[:120])

    _, wfs = call("GET", "/workflows", t["Khalid"])
    def_id = next((w["definitionId"] for w in wfs or [] if w["name"] == "Expense: manager, finance, HR"), None)
    check("9a workflow listed for Acme", def_id is not None)

    # 9b: Globex can neither see nor bind Acme's designed workflow.
    _, gwfs = call("GET", "/workflows", gt["Noor"])
    check("9b Globex does not see Acme's designed workflow", all(w["definitionId"] != def_id for w in gwfs or []))
    code, _ = call("POST", "/forms/leave-request/bindings", gt["Noor"], {"workflowDefinitionId": def_id, "priority": 1})
    check("9b Globex cannot bind to Acme's workflow", code >= 400, f"http {code}")

    # 9c: bind to Expense claim and run it end to end, with one reject-to-previous-step.
    code, _ = call("POST", "/forms/expense-claim/bindings", t["Khalid"], {"workflowDefinitionId": def_id, "priority": 1})
    check("9c bound to Expense claim", code == 200, f"http {code}")

    sid = submit(t["Sara"], {"expenseDate": "2026-11-01", "expenseType": "Travel", "amount": "120", "description": "Taxi"}, "expense-claim")
    tk = inbox_task(t["Omar"], sid)
    check("9c Omar gets manager-approval task", tk is not None)
    if tk: call("POST", f"/tasks/{tk}/complete", t["Omar"], {"outcome": "Approved"})

    tk2 = inbox_task(t["Layla"], sid)
    check("9c Layla (Finance role) gets task", tk2 is not None)
    if tk2: call("POST", f"/tasks/{tk2}/complete", t["Layla"], {"outcome": "Rejected", "comment": "needs re-check"})

    tk3 = inbox_task(t["Omar"], sid)
    check("9c reject sends the task back to Omar (back-to-previous)", tk3 is not None)
    if tk3: call("POST", f"/tasks/{tk3}/complete", t["Omar"], {"outcome": "Approved"})

    tk4 = inbox_task(t["Layla"], sid)
    check("9c Layla gets the task again after Omar re-approves", tk4 is not None)
    if tk4: call("POST", f"/tasks/{tk4}/complete", t["Layla"], {"outcome": "Approved"})

    tk5 = inbox_task(t["Huda"], sid)
    check("9c Huda (specific user) gets the final task", tk5 is not None)
    if tk5: call("POST", f"/tasks/{tk5}/complete", t["Huda"], {"outcome": "Approved"})

    d = detail(t["Sara"], sid, "Approved")
    check("9c request Approved after all steps", d.get("status") == "Approved", str(d.get("status")))

    # 9d: a rule that resolves to nobody (manager's manager, for someone whose own manager has no
    # manager) falls back instead of leaving the request stuck (SC-004; closes HANDOVER.md §3 item 1).
    fb_key = f"fallback-test-{RUN_SUFFIX}"
    call("POST", "/workflows/design", t["Khalid"], {"key": fb_key, "name": "Fallback test"})
    call("PUT", f"/workflows/design/{fb_key}/draft", t["Khalid"], {"fallbackRule": "role:HR", "stepsJson": json.dumps(
        {"steps": [{"title": "Manager's manager", "approverType": "manager-of-manager", "onReject": "end"}]})})
    code, _ = call("POST", f"/workflows/design/{fb_key}/publish", t["Khalid"])
    check("9d fallback-test workflow published", code == 200, f"http {code}")
    _, wfs2 = call("GET", "/workflows", t["Khalid"])
    fb_def_id = next((w["definitionId"] for w in wfs2 or [] if w["name"] == "Fallback test"), None)
    call("POST", "/forms/letter-request/bindings", t["Khalid"], {"workflowDefinitionId": fb_def_id,
         "conditionField": "letterType", "conditionValue": "No objection certificate", "priority": 1})

    # Khalid reports to Omar, who has no manager: manager-of-manager resolves to nobody here.
    sid2 = submit(t["Khalid"], {"letterType": "No objection certificate", "addressedTo": "Bank", "purpose": "Mortgage"}, "letter-request")
    tk6 = inbox_task(t["Huda"], sid2)
    check("9d rule resolving to nobody falls back to the fallback approver", tk6 is not None)
    if tk6: call("POST", f"/tasks/{tk6}/complete", t["Huda"], {"outcome": "Approved"})
    d2 = detail(t["Khalid"], sid2, "Approved")
    fallback_event = next((e for e in d2.get("events", []) if "fallback" in e.get("detail", "").lower()), None)
    check("9d timeline records why the fallback was used", fallback_event is not None, str(fallback_event))

def s10():
    """Server-side gatekeeper is renderer-independent (US3), and Acme's v2 leave-request conditional
    field is enforced at the API: hidden values are dropped, required-when-visible is enforced (US4)."""
    t = state["acme_tok"]

    missing_type = {k: v for k, v in annual.items() if k != "leaveType"}
    code, b = call("POST", "/submissions", t["Sara"], {"formKey": "leave-request", "data": missing_type})
    check("10a missing required field refused with field-level message", code >= 400 and "Leave type" in json.dumps(b), f"http {code}")

    code, b = call("POST", "/submissions", t["Sara"], {"formKey": "leave-request", "data": dict(annual, leaveType="NotAnOption")})
    check("10b unknown select option refused", code >= 400, f"http {code}")

    code, b = call("POST", "/submissions", t["Sara"], {"formKey": "leave-request", "data": dict(annual, days="not-a-number")})
    check("10c non-number in a number field refused", code >= 400, f"http {code}")

    sid = submit(t["Sara"], annual)
    d = detail(t["Sara"], sid)
    check("10d annual leave submits fine without the hidden medicalNoteReference field", sid is not None and d is not None, f"sid={sid}")

    sid2 = submit(t["Sara"], dict(annual, medicalNoteReference="should-be-dropped"))
    d2 = detail(t["Sara"], sid2)
    check("10e a value sent for a hidden field is dropped, not stored",
          sid2 is not None and "medicalNoteReference" not in (d2.get("data") or {}), str(d2.get("data")))

    sick_no_note = {k: v for k, v in sick.items() if k != "medicalNoteReference"}
    code, b = call("POST", "/submissions", t["Sara"], {"formKey": "leave-request", "data": sick_no_note})
    check("10f sick leave without the now-required medical note reference is refused",
          code >= 400 and "Medical note reference is required" in json.dumps(b), f"http {code}")

    sid3 = submit(t["Sara"], sick)
    d3 = detail(t["Sara"], sid3)
    check("10g sick leave with a medical note reference is accepted and the value is stored",
          sid3 is not None and (d3.get("data") or {}).get("medicalNoteReference") == sick["medicalNoteReference"], str(d3.get("data")))

def s11():
    """Vendor onboards a new client (US1), and real sign-in's remaining guarantees: no sign-up path,
    lockout after repeated failures (US2). Lockout deliberately burns a throwaway account (the new
    company's own admin, unused anywhere else) so it can't break any other step in this run."""
    company = f"Initech {RUN_SUFFIX}"
    admin_email = f"bill-{RUN_SUFFIX}@initech.demo"

    code, vendor_tok, _ = sign_in(VENDOR_EMAIL)
    check("11a vendor admin signs in (host, no tenant)", code == 200 and vendor_tok is not None, f"http {code}")
    state["vendor_tok"] = vendor_tok

    code, created = call("POST", "/tenants", vendor_tok, {"name": company, "adminName": "Bill Lumbergh", "adminEmail": admin_email})
    check("11b vendor admin creates a company with an admin email", code == 200 and created.get("generatedPassword"), f"http {code}")
    password = created.get("generatedPassword")

    code, bill_tok, _ = sign_in(admin_email, password, tenant_name=company)
    check("11c new admin signs in to their own company", code == 200 and bill_tok is not None, f"http {code}")

    _, forms = call("GET", "/forms", bill_tok)
    _, users_ = call("GET", "/users", bill_tok)
    check("11d SC-001 new company starts empty and isolated",
          forms == [] and len(users_ or []) == 1, f"forms={forms} users={users_}")

    # Self-contained (doesn't depend on step 1 having run yet — s11 runs first): sign Sara in directly.
    _, sara_tok, _ = sign_in(email_for("Sara", ACME), tenant_name=ACME)
    code, b = call("POST", "/tenants", sara_tok, {"name": "Should not exist", "adminName": "X", "adminEmail": "x@x.demo"})
    check("11e a non-vendor-admin cannot create a company", code == 403, f"http {code}")

    code, b = call("POST", "/api/account/register", None, {"email": "nope@nope.demo", "password": "x"})
    check("11f no self-registration endpoint exists (US2 AC1)", code in (404, 405), f"http {code}")

    for i in range(6):
        code, _, _ = sign_in(admin_email, "wrong-password", tenant_name=company)
    code, locked_tok, body = sign_in(admin_email, password, tenant_name=company)
    check("11g account locked out after repeated failed sign-ins, even with the correct password (US2 AC3)",
          code != 200 and locked_tok is None, f"http {code} {str(body)[:150]}")

    # Anonymous callers still resolve a tenant from __tenant (needed for /connect/token), so every
    # data route must refuse them outright (Constitution IV, SC-002).
    codes = {p: call("GET", p, None, tenant_header=ACME)[0]
             for p in ["/users", "/forms", "/forms/leave-request/design", "/company-settings/renderer"]}
    check("11h anonymous caller with a __tenant header cannot read that tenant's data",
          all(c == 401 for c in codes.values()), str(codes))

    # Edge case "forgot password": the reset also lifts the lockout 11g just triggered.
    code, reset = call("POST", "/tenants/reset-admin-password", vendor_tok, {"employeeId": created.get("adminEmployeeId")})
    new_password = (reset or {}).get("generatedPassword") if code == 200 else None
    code, bill_tok2, _ = sign_in(admin_email, new_password or "x", tenant_name=company)
    check("11i vendor admin resets a locked-out admin's password and the admin signs in again",
          bill_tok2 is not None, f"http {code}")

    acme_ids = roster(sara_tok)
    code, _ = call("POST", "/tenants/reset-admin-password", vendor_tok, {"employeeId": acme_ids["Sara"]})
    check("11j vendor admin cannot reset a non-admin employee's password", code == 403, f"http {code}")

    # Edge case "client admin leaves": assign another admin, then hand it back. Role membership
    # travels in the token, so Sara signs in again after each change.
    def sara_can_design():
        _, tok, _ = sign_in(email_for("Sara", ACME), tenant_name=ACME)
        _, perms = call("GET", "/my-permissions", tok)
        return "Workmate.Forms.Design" in (perms or [])
    code, _ = call("POST", "/tenants/assign-admin", vendor_tok, {"employeeId": acme_ids["Sara"]})
    check("11k vendor admin assigns another employee as company admin", code in (200, 204) and sara_can_design(), f"http {code}")
    code, _ = call("POST", "/tenants/assign-admin", vendor_tok, {"employeeId": acme_ids["Khalid"], "removeEmployeeId": acme_ids["Sara"]})
    check("11l assigning a new admin can remove the departed one", code in (200, 204) and not sara_can_design(), f"http {code}")
    code, _ = call("POST", "/tenants/assign-admin", sara_tok, {"employeeId": acme_ids["Sara"]})
    check("11m a non-vendor-admin cannot assign company admins", code == 403, f"http {code}")

def s12():
    """Spec 004: a client admin builds their company's people and structure, and requests route from it.
    Runs in fresh vendor-created companies so it never disturbs the Acme/Globex data steps 1-10 rely on.
    Deactivation runs last: it removes an approver the routing checks need."""
    ctx = s12_setup()
    s12_employees(ctx)
    s12_deactivation(ctx)

def s12_company(vendor_tok, name, admin_first):
    """Vendor creates a company; returns (company_name, admin_token)."""
    admin_email = f"{admin_first.lower()}-{RUN_SUFFIX}@{slug(name)}.demo"
    code, created = call("POST", "/tenants", vendor_tok, {"name": name, "adminName": f"{admin_first} Admin", "adminEmail": admin_email})
    if code != 200: raise RuntimeError(f"could not create company {name}: http {code}")
    _, tok, _ = sign_in(admin_email, created["generatedPassword"], tenant_name=name)
    return tok

def s12_setup():
    company = f"Umbrella {RUN_SUFFIX}"
    _, vendor_tok, _ = sign_in(VENDOR_EMAIL)
    admin = s12_company(vendor_tok, company, "Ada")
    ctx = {"company": company, "vendor": vendor_tok, "admin": admin, "ids": {}, "tok": {}, "email": {}, "pw": {}}

    # 12a (US1 part): six employees with roles and line managers. Rita -> Frank -> Paula -> Carl; Ivan
    # reports to Frank; Hana is HR (the workflow fallback). Departments come in the US2 part.
    staff = [("Rita", "Manager", None), ("Frank", "Manager", "Rita"), ("Paula", "Manager", "Frank"),
             ("Carl", "Employee", "Paula"), ("Ivan", "Employee", "Frank"), ("Hana", "HR", None)]
    ok = True
    for first, role, mgr in staff:
        email = f"{first.lower()}-{RUN_SUFFIX}@umbrella.demo"
        code, created = call("POST", "/employees", admin, {"name": f"{first} Umbrella", "email": email, "jobTitle": role,
                                                          "roles": [role], "managerId": ctx["ids"].get(mgr)})
        if code != 200: ok = False; continue
        ctx["ids"][first] = created["employee"]["id"]; ctx["email"][first] = email; ctx["pw"][first] = created["generatedPassword"]
        _, ctx["tok"][first], _ = sign_in(email, created["generatedPassword"], tenant_name=company)
        ok = ok and ctx["tok"][first] is not None and created["employee"]["roles"] == [role]
    check("12a admin adds 6 employees with roles and managers; each signs in with the password shown once", ok and len(ctx["ids"]) == 6,
          str(sorted(ctx["ids"])))
    return ctx

def s12_employees(ctx):
    admin, ids = ctx["admin"], ctx["ids"]
    _, people = call("GET", "/users", admin)
    rita = next(p for p in people or [] if p["id"] == ids["Rita"])

    # 12d: Rita reporting to Carl would close the loop Carl -> Paula -> Frank -> Rita -> Carl.
    body = {"name": rita["name"], "email": rita["email"], "roles": rita["roles"], "managerId": ids["Carl"]}
    code, b = call("PUT", f"/employees/{ids['Rita']}", admin, body)
    check("12d a manager cycle is refused (US1 AC2)", code == 403 and "loop" in json.dumps(b), f"http {code}")

    # 12e: duplicate email refused in the same company, accepted in another company.
    code, b = call("POST", "/employees", admin, {"name": "Carl Again", "email": ctx["email"]["Carl"], "roles": []})
    other_admin = s12_company(ctx["vendor"], f"Wayne {RUN_SUFFIX}", "Bruce")
    code2, _ = call("POST", "/employees", other_admin, {"name": "Carl Elsewhere", "email": ctx["email"]["Carl"], "roles": []})
    check("12e duplicate email refused in the same company, allowed in another (US1 AC3)", code == 403 and code2 == 200,
          f"same={code} other={code2}")

    # 12j: an ordinary employee cannot manage employees.
    code, _ = call("POST", "/employees", ctx["tok"]["Carl"], {"name": "Nope", "email": f"nope-{RUN_SUFFIX}@umbrella.demo", "roles": []})
    check("12j a non-admin cannot add employees", code == 403, f"http {code}")

def s12_publish_workflow(ctx, key, name, steps, fallback="role:HR"):
    """Creates, drafts and publishes a designed workflow; returns its Elsa definition id."""
    admin = ctx["admin"]
    call("POST", "/workflows/design", admin, {"key": key, "name": name})
    call("PUT", f"/workflows/design/{key}/draft", admin, {"stepsJson": json.dumps({"steps": steps}), "fallbackRule": fallback})
    code, _ = call("POST", f"/workflows/design/{key}/publish", admin)
    _, wfs = call("GET", "/workflows", admin)
    return next((w["definitionId"] for w in wfs or [] if w["name"] == name), None) if code == 200 else None

def s12_form(ctx, key, definition_id):
    """Creates and publishes a one-field form bound to the given workflow."""
    admin = ctx["admin"]
    call("POST", "/forms", admin, {"key": key, "name": key, "category": "Test"})
    schema = {"title": key, "fields": [{"key": "details", "label": "Details", "type": "text", "required": True}]}
    call("PUT", f"/forms/{key}/draft", admin, {"schemaJson": json.dumps(schema)})
    call("POST", f"/forms/{key}/publish", admin)
    code, _ = call("POST", f"/forms/{key}/bindings", admin, {"workflowDefinitionId": definition_id, "priority": 1})
    return code == 200

def s12_deactivation(ctx):
    admin, ids, tok = ctx["admin"], ctx["ids"], ctx["tok"]
    def_id = s12_publish_workflow(ctx, f"manager-only-{RUN_SUFFIX}", "Manager only",
                                  [{"title": "Manager approval", "approverType": "line-manager", "onReject": "end"}])
    bound = def_id is not None and s12_form(ctx, f"ask-{RUN_SUFFIX}", def_id)
    sid = submit(tok["Carl"], {"details": "new laptop"}, f"ask-{RUN_SUFFIX}") if bound else None
    before = inbox_task(tok["Paula"], sid) if sid else None

    # 12h: deactivating Paula moves her open task to the fallback (HR: Hana), with a timeline entry (FR-007).
    paula_old_token = tok["Paula"]
    code, r = call("POST", f"/employees/{ids['Paula']}/deactivate", admin)
    moved = inbox_task(tok["Hana"], sid) if sid else None
    d = detail(tok["Carl"], sid) if sid else {}
    reassigned = any(ev.get("eventType") == "TaskReassigned" for ev in (d or {}).get("events", []))
    check("12h deactivating an approver moves their open task to the fallback, with a timeline entry (FR-007)",
          before is not None and code == 200 and (r or {}).get("reassignedTaskCount") == 1 and moved is not None and reassigned,
          f"before={before is not None} http={code} moved={moved is not None} timeline={reassigned}")

    # 12i: Paula can no longer sign in, and the token she already had stops working.
    code_sign, new_tok, _ = sign_in(ctx["email"]["Paula"], ctx["pw"]["Paula"], tenant_name=ctx["company"])  # her real password
    code_old, _ = call("GET", "/tasks/inbox", paula_old_token)
    check("12i a deactivated employee cannot sign in, and their existing token is refused",
          new_tok is None and code_old == 403, f"sign-in={code_sign} old-token={code_old}")

def restart_before():
    """Puts one request per tenant at a pending human step, then records their ids for restart_after."""
    at = tokens_for(ACME, ACME_NAMES); state["acme_tok"] = at
    gt = tokens_for(GLOBEX, GLOBEX_NAMES); state["globex_tok"] = gt

    a1 = submit(at["Sara"], annual)  # Annual: sits at Omar (line manager)
    check("8-before A1 Omar gets task", inbox_task(at["Omar"], a1) is not None)

    a2 = submit(at["Sara"], sick)  # Sick: routes straight to HR (Huda)
    check("8-before A2 Huda gets task", inbox_task(at["Huda"], a2) is not None)

    g1 = submit(gt["Ali"], annual)  # Globex has only the default route: manager first
    check("8-before G1 Fatima gets task", inbox_task(gt["Fatima"], g1) is not None)

    data = {"a1": a1, "a2": a2, "g1": g1, "counts_before": {"acme": counts_for(at["Sara"]), "globex": counts_for(gt["Ali"])}}
    with open(RESTART_FILE, "w") as fh: json.dump(data, fh)
    log(f"Wrote {RESTART_FILE}: {data}")
    log("Now restart the app (reset off), then run: python3 demo.py restart-after")

def restart_after():
    """Reads the ids from restart_before and resumes each workflow from where it left off."""
    with open(RESTART_FILE) as fh: data = json.load(fh)
    a1, a2, g1 = data["a1"], data["a2"], data["g1"]
    at = tokens_for(ACME, ACME_NAMES); state["acme_tok"] = at
    gt = tokens_for(GLOBEX, GLOBEX_NAMES); state["globex_tok"] = gt

    # 8a: A1 (at Omar before restart) resumes manager -> HR -> Approved.
    tk = inbox_task(at["Omar"], a1)
    check("8a Omar still has A1 task after restart", tk is not None)
    if tk: call("POST", f"/tasks/{tk}/complete", at["Omar"], {"outcome": "Approved"})
    tk2 = inbox_task(at["Huda"], a1)
    check("8a Huda gets task after Omar resumes", tk2 is not None)
    if tk2: call("POST", f"/tasks/{tk2}/complete", at["Huda"], {"outcome": "Approved"})
    d = detail(at["Sara"], a1, "Approved")
    check("8a A1 Approved after restart", d.get("status") == "Approved", str(d.get("status")))

    # 8b: A2 (at Huda before restart) resumes and rejects: ends with no further tasks.
    tk3 = inbox_task(at["Huda"], a2)
    check("8b Huda still has A2 task after restart", tk3 is not None)
    if tk3: call("POST", f"/tasks/{tk3}/complete", at["Huda"], {"outcome": "Rejected", "comment": "Not now"})
    d2 = detail(at["Sara"], a2, "Rejected")
    check("8b A2 Rejected, no further tasks", d2.get("status") == "Rejected" and len(d2.get("tasks", [])) == 1,
          f"status={d2.get('status')} tasks={len(d2.get('tasks', []))}")

    # 8c: G1 (at Fatima before restart) resumes manager -> HR -> Approved, in the other tenant.
    tg = inbox_task(gt["Fatima"], g1)
    check("8c Fatima still has G1 task after restart", tg is not None)
    if tg: call("POST", f"/tasks/{tg}/complete", gt["Fatima"], {"outcome": "Approved"})
    tg2 = inbox_task(gt["Yousef"], g1)
    check("8c Yousef gets task after Fatima resumes", tg2 is not None)
    if tg2: call("POST", f"/tasks/{tg2}/complete", gt["Yousef"], {"outcome": "Approved"})
    dg = detail(gt["Ali"], g1, "Approved")
    check("8c G1 Approved after restart", dg.get("status") == "Approved", str(dg.get("status")))

    # 8d: resuming one tenant's request never surfaces the other tenant's ids.
    _, acme_inbox = call("GET", "/tasks/inbox", at["Omar"])
    _, acme_mine = call("GET", "/submissions/mine", at["Sara"])
    check("8d Acme sees none of Globex's ids",
          all(i["submissionId"] != g1 for i in acme_inbox or []) and all(s["id"] != g1 for s in acme_mine or []))
    _, globex_inbox = call("GET", "/tasks/inbox", gt["Fatima"])
    _, globex_mine = call("GET", "/submissions/mine", gt["Ali"])
    check("8d Globex sees none of Acme's ids",
          all(i["submissionId"] not in (a1, a2) for i in globex_inbox or []) and
          all(s["id"] not in (a1, a2) for s in globex_mine or []))

    # 8e: seeding stayed idempotent across the restart; nothing was re-seeded or lost.
    counts_after = {"acme": counts_for(at["Sara"]), "globex": counts_for(gt["Ali"])}
    check("8e per-tenant employee/form counts unchanged across restart",
          counts_after == data["counts_before"], f"before={data['counts_before']} after={counts_after}")

if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else None
    if mode == "restart-before":
        step("Step 8 (before restart)", restart_before)
    elif mode == "restart-after":
        step("Step 8 (after restart)", restart_after)
    elif mode is None:
        for n, f in [("Step 11 vendor onboarding + sign-in guarantees", s11),
                     ("Step 1 happy path", s1), ("Step 2 conditional routing", s2), ("Step 3 reject path", s3),
                     ("Step 4 form versioning", s4), ("Step 5 tenant isolation", s5), ("Step 6 reporting values", s6),
                     ("Step 7 form creation, routing, admin-only", s7), ("Step 9 workflow designer", step9),
                     ("Step 10 server gatekeeper + conditional field (US3/US4)", s10),
                     ("Step 12 employees and organisation structure (spec 004)", s12)]:
            step(n, f)
    else:
        log(f"Unknown mode {mode!r}. Use no argument, 'restart-before' or 'restart-after'.")
        sys.exit(1)

    log("\n=== SUMMARY")
    for s, ok, note in results: log(f"{'PASS' if ok else 'FAIL'}  {s}  {note}")
