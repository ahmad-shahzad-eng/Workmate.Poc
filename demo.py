#!/usr/bin/env python3
"""Runs README demo steps 1-7 and 9-13 (no args), or the two-part restart check (step 8:
`restart-before` / `restart-after`, run with a manual app restart in between), against the running
PoC. Everyone signs in for real (spec 003) via OpenIddict's password grant at /connect/token — no more
X-Demo-User header. Needs Mailpit running (spec 005): new accounts get their password only through an
emailed link, and step 13 reads every email back from Mailpit's API. Stdlib only."""
import json, sqlite3, sys, time, urllib.request, urllib.error, urllib.parse, os

BASE = "http://localhost:5080/api/poc"
TOKEN_URL = "http://localhost:5080/connect/token"
MAILPIT = "http://localhost:8025/api/v1"  # spec 005: every email the app sends lands here (SMTP localhost:1025)
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

# --- Mailpit (spec 005). New accounts only ever get a password through an emailed link.
def mailpit(path):
    with urllib.request.urlopen(MAILPIT + path, timeout=10) as r:
        return json.loads(r.read().decode())

def mail_ready():
    """Fails fast instead of a cascade of failures when Mailpit isn't running (plan Design §8)."""
    try: mailpit("/info")
    except Exception as e:
        log(f"Mailpit is not reachable at {MAILPIT} ({e!r}). Start Mailpit (mailpit &) first.")
        sys.exit(1)

def mail_list(address, subject_contains=None):
    """Every message to `address` (newest first), optionally only those whose subject contains the text."""
    q = urllib.parse.quote(f'to:"{address}"')
    msgs = mailpit(f"/search?query={q}&limit=200").get("messages") or []
    return [m for m in msgs if not subject_contains or subject_contains.lower() in (m.get("Subject") or "").lower()]

def mail_for(address, subject_contains=None, wait=15, after=None):
    """Polls for the newest message to `address` (and subject text); `after` skips message ids already seen."""
    end = time.time() + wait
    while True:
        msgs = [m for m in mail_list(address, subject_contains) if not after or m["ID"] not in after]
        if msgs:
            log(f"  mail to {address}: {msgs[0].get('Subject')!r}")
            return msgs[0]
        if time.time() > end:
            log(f"  no mail to {address} (subject contains {subject_contains!r}) within {wait}s")
            return None
        time.sleep(1)

def mail_ids(address):
    return {m["ID"] for m in mail_list(address)}

def mail_body(msg):
    """Returns (text, html) of a message summary from mail_for."""
    full = mailpit(f"/message/{msg['ID']}")
    return full.get("Text") or "", full.get("HTML") or ""

def mail_from_name(msg):
    return ((mailpit(f"/message/{msg['ID']}").get("From") or {}).get("Name")) or ""

def mail_link(msg, marker):
    """The first URL in the text part containing `marker`."""
    text, _ = mail_body(msg)
    for word in text.split():
        if word.startswith("http") and marker in word: return word
    return None

def set_password_link_parts(link):
    q = urllib.parse.parse_qs(urllib.parse.urlparse(link).query)
    return {"tenantId": (q.get("tenant") or [None])[0] or None, "userId": q["user"][0], "token": q["token"][0]}

def set_password_from_mail(address, password, subject_contains=None, after=None):
    """Finds the newest invite/reset mail to `address`, follows its link through POST account/set-password.
    Returns the HTTP code (204 on success), or 0 if no mail/link arrived."""
    msg = mail_for(address, subject_contains, after=after)
    link = mail_link(msg, "setPassword=1") if msg else None
    if not link: return 0
    code, _ = call("POST", "/account/set-password", None, {**set_password_link_parts(link), "newPassword": password})
    return code

def request_number(sid):
    """Spec 005 R9: the first 8 hex characters of the submission id, upper case."""
    return sid.replace("-", "")[:8].upper()

def mail_about(address, number, subject_contains=None, wait=20):
    """The newest message to `address` (subject containing the text) that mentions request `number` in its
    subject or text. Robust against earlier steps' mail to the same person."""
    end = time.time() + wait
    while True:
        for m in mail_list(address, subject_contains)[:15]:
            text, _ = mail_body(m)
            if number in (m.get("Subject") or "") or number in text:
                log(f"  mail to {address} about {number}: {m.get('Subject')!r}")
                return m
        if time.time() > end:
            log(f"  no mail to {address} about {number} (subject contains {subject_contains!r}) within {wait}s")
            return None
        time.sleep(1)

def emails_for_submission(admin_tok, sid, expected=None, wait=10):
    """Email log rows for one request (polls until `expected` rows exist or the wait ends)."""
    end = time.time() + wait
    while True:
        _, rows = call("GET", "/emails", admin_tok)
        mine = [r for r in rows or [] if r.get("submissionId") == sid]
        if expected is None or len(mine) >= expected or time.time() > end: return mine
        time.sleep(1)

NEW_PASSWORD = "Demo-Pass1!"  # what demo.py chooses on every emailed set-password link

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
    # Spec 005 FR-008: no password comes back; the admin chooses one from the emailed invite.
    sp_code = set_password_from_mail(admin_email, NEW_PASSWORD) if code == 200 else 0
    check("11b vendor admin creates a company with an admin email; the admin sets a password from the invite",
          code == 200 and "generatedPassword" not in (created or {}) and sp_code == 204, f"http {code} set-password {sp_code}")
    password = NEW_PASSWORD

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
    # Spec 005: the reset lifts the lockout and emails a set-password link instead of showing a password.
    seen = mail_ids(admin_email)
    code, _ = call("POST", "/tenants/reset-admin-password", vendor_tok, {"employeeId": created.get("adminEmployeeId")})
    sp_code = set_password_from_mail(admin_email, "Demo-Pass2!", subject_contains="Reset", after=seen) if code == 204 else 0
    code, bill_tok2, _ = sign_in(admin_email, "Demo-Pass2!", tenant_name=company)
    check("11i vendor admin resets a locked-out admin's password; the admin sets a new one from the emailed link and signs in again",
          bill_tok2 is not None and sp_code == 204, f"http {code} set-password {sp_code}")

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
    s12_org(ctx)
    s12_roles(ctx)
    s12_deactivation(ctx)

def s12_company(vendor_tok, name, admin_first):
    """Vendor creates a company; returns (company_name, admin_token)."""
    admin_email = f"{admin_first.lower()}-{RUN_SUFFIX}@{slug(name)}.demo"
    code, created = call("POST", "/tenants", vendor_tok, {"name": name, "adminName": f"{admin_first} Admin", "adminEmail": admin_email})
    if code != 200: raise RuntimeError(f"could not create company {name}: http {code}")
    set_password_from_mail(admin_email, NEW_PASSWORD)
    _, tok, _ = sign_in(admin_email, NEW_PASSWORD, tenant_name=name)
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
        ctx["ids"][first] = created["id"]; ctx["email"][first] = email; ctx["pw"][first] = NEW_PASSWORD
        set_password_from_mail(email, NEW_PASSWORD)
        _, ctx["tok"][first], _ = sign_in(email, NEW_PASSWORD, tenant_name=company)
        ok = ok and ctx["tok"][first] is not None and created["roles"] == [role] and "generatedPassword" not in created
    check("12a admin adds 6 employees with roles and managers; each sets a password from the emailed invite and signs in", ok and len(ctx["ids"]) == 6,
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

def s12_org(ctx):
    """US2: a 3-level tree with heads, and routing by "Head of my department"."""
    admin, ids, tok = ctx["admin"], ctx["ids"], ctx["tok"]

    def dept(name, parent=None):
        code, d = call("POST", "/departments", admin, {"name": name, "parentId": parent})
        return d["id"] if code == 200 else None
    hq = dept("Head office"); fin = dept("Finance", hq); pay = dept("Payables", fin)
    for unit, head in [(hq, "Rita"), (fin, "Frank"), (pay, "Ivan")]:
        call("PUT", f"/departments/{unit}", admin, {"name": {hq: "Head office", fin: "Finance", pay: "Payables"}[unit], "headEmployeeId": ids[head]})
    _, people = call("GET", "/users", admin)
    placement = {"Rita": hq, "Hana": hq, "Frank": fin, "Ivan": pay, "Paula": pay, "Carl": pay}
    for p in people or []:
        first = p["name"].split()[0]
        if first in placement:
            call("PUT", f"/employees/{p['id']}", admin, {"name": p["name"], "email": p["email"], "roles": p["roles"],
                                                        "managerId": p["managerId"], "departmentId": placement[first]})
    _, depts = call("GET", "/departments", admin)
    by_name = {d["name"]: d for d in depts or []}
    check("12a three-level department tree with heads, employees placed",
          by_name.get("Payables", {}).get("parentId") == fin and by_name.get("Finance", {}).get("parentId") == hq
          and by_name.get("Payables", {}).get("headName", "").startswith("Ivan") and by_name.get("Payables", {}).get("employeeCount") == 3,
          str({n: (d["headName"], d["employeeCount"]) for n, d in by_name.items()}))

    # 12b (SC-001): line manager -> head of department -> HR, configured with no code.
    def_id = s12_publish_workflow(ctx, f"mgr-head-hr-{RUN_SUFFIX}", "Manager, department head, HR", [
        {"title": "Manager approval", "approverType": "line-manager", "onReject": "end"},
        {"title": "Department head approval", "approverType": "department-head", "onReject": "end"},
        {"title": "HR sign-off", "approverType": "role", "role": "HR", "onReject": "end"}])
    form = f"purchase-{RUN_SUFFIX}"
    bound = def_id is not None and s12_form(ctx, form, def_id)
    sid = submit(tok["Carl"], {"details": "office chairs"}, form) if bound else None
    route = []
    for approver in ["Paula", "Ivan", "Hana"]:
        tk = inbox_task(tok[approver], sid) if sid else None
        route.append(approver if tk else f"!{approver}")
        if tk: call("POST", f"/tasks/{tk}/complete", tok[approver], {"outcome": "Approved"})
    d = detail(tok["Carl"], sid, wait_status="Approved") if sid else {}
    check("12b SC-001 request routes line manager -> department head -> HR with no code",
          route == ["Paula", "Ivan", "Hana"] and (d or {}).get("status") == "Approved", f"route={route} status={(d or {}).get('status')}")

    # 12c: Ivan heads Payables, so his own department-head step walks up to Finance's head, Frank.
    sid2 = submit(tok["Ivan"], {"details": "team offsite"}, form) if bound else None
    first = inbox_task(tok["Frank"], sid2) if sid2 else None  # Ivan's line manager is Frank too
    if first: call("POST", f"/tasks/{first}/complete", tok["Frank"], {"outcome": "Approved"})
    second = None
    end = time.time() + 8
    while sid2 and time.time() < end and second is None:
        _, items = call("GET", "/tasks/inbox", tok["Frank"])
        second = next((i["taskId"] for i in items or [] if i["submissionId"] == sid2 and i["taskId"] != first), None)
        if second is None: time.sleep(1)
    check("12c a head's own request goes to the next head up (clarification Q3)", first is not None and second is not None,
          f"manager-step={first is not None} head-step-to-Frank={second is not None}")

    # 12f: departments with sub-departments or employees can't be deleted; an empty one can.
    code_fin, _ = call("DELETE", f"/departments/{fin}", admin)
    code_pay, _ = call("DELETE", f"/departments/{pay}", admin)
    temp = dept("Temporary", hq)
    code_tmp, _ = call("DELETE", f"/departments/{temp}", admin)
    check("12f a department with sub-departments or employees can't be deleted; an empty one can (US2 AC2)",
          code_fin == 403 and code_pay == 403 and code_tmp in (200, 204), f"finance={code_fin} payables={code_pay} empty={code_tmp}")

def s12_set_roles(ctx, first, roles):
    """Replaces one employee's roles, keeping everything else as it is."""
    _, people = call("GET", "/users", ctx["admin"])
    p = next(x for x in people if x["id"] == ctx["ids"][first])
    return call("PUT", f"/employees/{p['id']}", ctx["admin"], {"name": p["name"], "email": p["email"], "roles": roles,
                                                              "managerId": p["managerId"], "departmentId": p["departmentId"]})

def s12_roles(ctx):
    """US3: a role a published workflow uses can't be deleted; an unused one can, and leaves its holders."""
    admin = ctx["admin"]
    call("POST", "/roles", admin, {"name": "Payroll"})
    s12_set_roles(ctx, "Hana", ["HR", "Payroll"])
    wf_name = "Payroll check"
    s12_publish_workflow(ctx, f"payroll-check-{RUN_SUFFIX}", wf_name,
                         [{"title": "Payroll review", "approverType": "role", "role": "Payroll", "onReject": "end"}])
    _, roles = call("GET", "/roles", admin)
    payroll = next((r for r in roles or [] if r["name"] == "Payroll"), None)
    code_used, b = call("DELETE", f"/roles/{payroll['id']}", admin) if payroll else (None, None)

    _, temp = call("POST", "/roles", admin, {"name": "Temp"})
    s12_set_roles(ctx, "Carl", ["Employee", "Temp"])
    code_unused, _ = call("DELETE", f"/roles/{(temp or {}).get('id')}", admin)
    _, roles_after = call("GET", "/roles", admin)
    _, people = call("GET", "/users", admin)
    carl = next((p for p in people or [] if p["id"] == ctx["ids"]["Carl"]), {})
    check("12g a role used by a published workflow can't be deleted (named); an unused one is deleted and leaves its holders (US3)",
          code_used == 403 and wf_name in json.dumps(b) and code_unused in (200, 204)
          and all(r["name"] != "Temp" for r in roles_after or []) and carl.get("roles") == ["Employee"],
          f"used={code_used} unused={code_unused} carl={carl.get('roles')}")

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

def s13():
    """Spec 005: emails for every workflow and account event, read back from Mailpit (quickstart.md)."""
    t = tokens_for(ACME, ACME_NAMES)
    s13_approve_path(t)
    s13_reject_path(t)
    s13_sent_back(t)
    s13_accounts_and_failures()
    s13_form_templates(t)
    s13_company_defaults(t)
    code, _ = call("GET", "/emails", t["Sara"])
    check("13j a non-admin cannot read the Email log", code == 403, f"http {code}")
    code, _ = call("PUT", "/forms/leave-request/emails/TaskAssigned/Approver", t["Sara"], {"subject": "x", "body": "x"})
    code2, _ = call("PUT", "/company-settings/emails/Submitted/Initiator", t["Sara"], {"subject": "x", "body": "x"})
    check("13j a non-admin cannot edit form or company email templates", code == 403 and code2 == 403, f"form {code} company {code2}")

wfh = {"startDate": "2026-11-10", "endDate": "2026-11-10", "days": "1", "reachable": "true", "reason": "Plumber visit"}

def s13_form_templates(t):
    """US2: a form override applies to that form only (13d), bad templates are refused (13e), values are escaped (13f)."""
    gt = tokens_for(GLOBEX, ["Ali", "Fatima"])
    path = "/forms/expense-claim/emails/TaskAssigned/Approver"
    code, saved = call("PUT", path, t["Khalid"], {"subject": "EXPENSE {{ request.number }} for {{ approver.name }}",
                                                   "body": "{{ initiator.name }} claimed {{ fields.amount }} ({{ fields.expenseType }}).\n\n{{ link }}"})
    check("13d admin saves a form-level override", code == 200 and (saved or {}).get("source") == "form", f"http {code}")

    sid = submit(t["Sara"], {"expenseDate": "2026-11-03", "expenseType": "Meals", "amount": "18", "description": "Lunch"}, "expense-claim")
    num = request_number(sid)
    omar = mail_about(email_for("Omar", ACME), num, f"EXPENSE {num} for Omar")
    text, _ = mail_body(omar) if omar else ("", "")
    check("13d the override is used for that form (subject and fields.* in the body)", omar is not None and "18 (Meals)" in text, text[:120])

    sid2 = submit(t["Sara"], annual); num2 = request_number(sid2)
    check("13d another form in the same company keeps the default wording",
          mail_about(email_for("Omar", ACME), num2, "waiting for your approval") is not None)
    gsid = submit(gt["Ali"], annual); gnum = request_number(gsid)
    check("13d another company keeps the default wording (SC-002)",
          mail_about(email_for("Fatima", GLOBEX), gnum, "waiting for your approval") is not None)
    code, _ = call("DELETE", path, t["Khalid"])
    _, items = call("GET", "/forms/expense-claim/emails", t["Khalid"])
    row = next((i for i in items or [] if i["event"] == "TaskAssigned"), {})
    check("13d reset to default removes the override", code == 204 and row.get("source") == "built-in", f"http {code} {row.get('source')}")

    code, body = call("PUT", path, t["Khalid"], {"subject": "Hi {{ request.secret }}", "body": "x"})
    msg = ((body or {}).get("error") or {}).get("message", "") if isinstance(body, dict) else ""
    check("13e an unknown placeholder is refused, naming it", code == 403 and "request.secret" in msg, f"http {code} {msg}")
    code, body = call("PUT", path, t["Khalid"], {"subject": "Hi", "body": "{{ include 'secrets' }}"})
    msg = ((body or {}).get("error") or {}).get("message", "") if isinstance(body, dict) else ""
    check("13e include is refused", code == 403 and "include" in msg, f"http {code} {msg}")
    code, _ = call("PUT", "/forms/leave-request/emails/Submitted/Initiator", t["Khalid"], {"subject": "x", "body": "{{ fields.nope }}"})
    check("13e a field the form doesn't have is refused", code == 403, f"http {code}")

    # 13f: HTML typed into a field is shown, never interpreted.
    wpath = "/forms/work-from-home/emails/Submitted/Initiator"
    call("PUT", wpath, t["Khalid"], {"subject": "Got your {{ request.title }} {{ request.number }}", "body": "Reason: {{ fields.reason }}"})
    sid3 = submit(t["Sara"], {**wfh, "reason": "<b>x</b>"}, "work-from-home"); num3 = request_number(sid3)
    sara = mail_about(email_for("Sara", ACME), num3, "Got your")
    text, html = mail_body(sara) if sara else ("", "")
    check("13f a field value <b>x</b> is escaped in the HTML part and literal in the text part",
          "&lt;b&gt;x&lt;/b&gt;" in html and "<b>x</b>" not in html and "<b>x</b>" in text, f"text={text[:60]!r}")
    call("DELETE", wpath, t["Khalid"])

    code, p = call("POST", "/forms/work-from-home/emails/preview", t["Khalid"],
                   {"event": "Submitted", "audience": "Initiator", "subject": "S {{ initiator.name }}", "body": "{{ fields.reason }}"})
    check("13f preview renders sample data with field values escaped in HTML",
          code == 200 and p.get("subject") == "S Sample Person" and "&lt;b&gt;sample&lt;/b&gt;" in p.get("html", ""), f"http {code}")

def s13_company_defaults(t):
    """13k US3: a company default applies to every form without its own override; a form override still wins;
    another company is unaffected; the From line shows the sender name."""
    gt = tokens_for(GLOBEX, ["Ali"])
    cpath = "/company-settings/emails/Submitted/Initiator"
    c1, _ = call("PUT", cpath, t["Khalid"], {"subject": "Acme got your {{ request.title }} ({{ request.number }})", "body": "Thanks, {{ initiator.name }}.\n{{ link }}"})
    c2, sender = call("PUT", "/company-settings/email-sender", t["Khalid"], {"senderName": "Acme HR"})
    check("13k admin sets a company default and a sender name", c1 == 200 and c2 == 200 and (sender or {}).get("effectiveSenderName") == "Acme HR",
          f"template {c1} sender {c2}")

    sid = submit(t["Sara"], wfh, "work-from-home"); num = request_number(sid)
    m = mail_about(email_for("Sara", ACME), num, "Acme got your Work from home")
    check("13k the company default is used, from 'Acme HR'", m is not None and mail_from_name(m) == "Acme HR",
          mail_from_name(m) if m else "no mail")

    fpath = "/forms/work-from-home/emails/Submitted/Initiator"
    call("PUT", fpath, t["Khalid"], {"subject": "WFH form override {{ request.number }}", "body": "{{ link }}"})
    sid2 = submit(t["Sara"], wfh, "work-from-home"); num2 = request_number(sid2)
    check("13k a form override beats the company default", mail_about(email_for("Sara", ACME), num2, "WFH form override") is not None)

    gsid = submit(gt["Ali"], annual); gnum = request_number(gsid)
    check("13k another company keeps the built-in wording", mail_about(email_for("Ali", GLOBEX), gnum, "We received your") is not None)

    r1, _ = call("DELETE", fpath, t["Khalid"])
    r2, _ = call("DELETE", cpath, t["Khalid"])
    r3, sender = call("PUT", "/company-settings/email-sender", t["Khalid"], {"senderName": None})
    check("13k overrides reset and sender name cleared", r1 == 204 and r2 == 204 and (sender or {}).get("senderName") is None,
          f"{r1} {r2} {r3} {(sender or {}).get('effectiveSenderName')}")

def s13_approve_path(t):
    """13a SC-001: submit -> Omar -> Huda -> approved, and exactly those four emails."""
    sid = submit(t["Sara"], annual); num = request_number(sid)
    check("13a Sara gets 'submitted'", mail_about(email_for("Sara", ACME), num, "received") is not None, num)
    omar = mail_about(email_for("Omar", ACME), num, "waiting for your approval")
    link = mail_link(omar, "?view=inbox") if omar else None
    check("13a Omar gets 'waiting for your approval' with an inbox link", link is not None, str(link))
    tk = inbox_task(t["Omar"], sid)
    if tk: call("POST", f"/tasks/{tk}/complete", t["Omar"], {"outcome": "Approved"})
    check("13a Huda gets 'waiting for your approval' after Omar approves",
          mail_about(email_for("Huda", ACME), num, "waiting for your approval") is not None)
    tk2 = inbox_task(t["Huda"], sid)
    if tk2: call("POST", f"/tasks/{tk2}/complete", t["Huda"], {"outcome": "Approved"})
    sara = mail_about(email_for("Sara", ACME), num, "approved")
    _, html = mail_body(sara) if sara else ("", "")
    check("13a Sara gets 'approved' (multipart: the HTML part has the company layout)", sara is not None and ACME in html)
    rows = emails_for_submission(t["Khalid"], sid, expected=4)
    check("13a this request produced exactly 4 emails, all sent",
          len(rows) == 4 and all(r["status"] == "Sent" for r in rows),
          str(sorted((r["event"], r["recipientEmail"], r["status"]) for r in rows)))

def s13_reject_path(t):
    """13b: a reject at step 1 tells Sara with Omar's comment; HR (Huda) is never involved."""
    sid = submit(t["Sara"], annual); num = request_number(sid)
    tk = inbox_task(t["Omar"], sid)
    if tk: call("POST", f"/tasks/{tk}/complete", t["Omar"], {"outcome": "Rejected", "comment": "Not this week"})
    sara = mail_about(email_for("Sara", ACME), num, "rejected")
    text, _ = mail_body(sara) if sara else ("", "")
    check("13b Sara gets 'rejected' with Omar's name and comment", "Not this week" in text and "Omar" in text, text[:200])
    rows = emails_for_submission(t["Khalid"], sid, expected=3)
    check("13b no email to HR for a request rejected at step 1",
          len(rows) == 3 and all(r["recipientEmail"] != email_for("Huda", ACME) for r in rows),
          str(sorted((r["event"], r["recipientEmail"]) for r in rows)))

def s13_sent_back(t):
    """13c: step 9 bound Expense claim to manager -> Finance (back-to-previous on reject) -> Huda."""
    sid = submit(t["Sara"], {"expenseDate": "2026-11-02", "expenseType": "Travel", "amount": "45", "description": "Train"}, "expense-claim")
    num = request_number(sid)
    tk = inbox_task(t["Omar"], sid)
    if tk: call("POST", f"/tasks/{tk}/complete", t["Omar"], {"outcome": "Approved"})
    tk2 = inbox_task(t["Layla"], sid)
    if tk2: call("POST", f"/tasks/{tk2}/complete", t["Layla"], {"outcome": "Rejected", "comment": "Receipt missing"})
    omar = mail_about(email_for("Omar", ACME), num, "sent back to you")
    text, _ = mail_body(omar) if omar else ("", "")
    check("13c a reject at step 2 emails step 1's approver 'sent back' with the comment", "Receipt missing" in text, text[:200])
    check("13c the initiator is told the request went back a step",
          mail_about(email_for("Sara", ACME), num, "sent back") is not None)

def s13_accounts_and_failures():
    """13g-13i run in a fresh company so Acme/Globex data stays untouched."""
    company = f"Hooli {RUN_SUFFIX}"
    _, vendor_tok, _ = sign_in(VENDOR_EMAIL)
    admin = s12_company(vendor_tok, company, "Gavin")
    _, people = call("GET", "/users", admin)
    admin_id = (people or [{}])[0].get("id")

    # 13g: an invite carries a one-time set-password link; the body isn't kept in the Email log.
    dinesh = f"dinesh-{RUN_SUFFIX}@hooli.demo"
    code, created = call("POST", "/employees", admin, {"name": "Dinesh Hooli", "email": dinesh, "roles": ["Employee"], "managerId": admin_id})
    invite = mail_for(dinesh, "account") if code == 200 else None
    link = mail_link(invite, "setPassword=1") if invite else None
    parts = set_password_link_parts(link) if link else {}
    sp1, _ = call("POST", "/account/set-password", None, {**parts, "newPassword": NEW_PASSWORD}) if link else (0, None)
    _, dinesh_tok, _ = sign_in(dinesh, NEW_PASSWORD, tenant_name=company)
    check("13g invite link sets a password and the new employee signs in", sp1 == 204 and dinesh_tok is not None, f"set-password {sp1}")
    sp2, _ = call("POST", "/account/set-password", None, {**parts, "newPassword": "Another-Pass1!"}) if link else (0, None)
    check("13g the same link can't be used twice", sp2 == 403, f"http {sp2}")
    _, log_rows = call("GET", "/emails", admin)
    row = next((r for r in log_rows or [] if r["recipientEmail"] == dinesh and r["event"] == "AccountInvite"), None)
    db = sqlite3.connect(os.path.join(os.path.dirname(os.path.abspath(__file__)), "workmate-poc.db"))
    stored = db.execute("SELECT TextBody, HtmlBody FROM Notif_EmailMessages WHERE upper(Id) = upper(?)",
                        ((row or {}).get("id", ""),)).fetchone()
    check("13g the Email log lists the invite but never stores its body (the link)",
          row is not None and row["status"] == "Sent" and stored == (None, None), f"{row and row['status']} stored={stored}")

    # 13h: forgot-password answers the same for a real and an unknown address; only the real one gets mail.
    seen = mail_ids(dinesh)
    unknown = f"nobody-{RUN_SUFFIX}@hooli.demo"
    c_real, _ = call("POST", "/account/forgot-password", None, {"email": dinesh}, tenant_header=company)
    c_unknown, _ = call("POST", "/account/forgot-password", None, {"email": unknown}, tenant_header=company)
    check("13h forgot-password returns 204 for a real and an unknown address alike", c_real == 204 and c_unknown == 204,
          f"real {c_real} unknown {c_unknown}")
    got_real = mail_for(dinesh, "Reset", after=seen) is not None
    got_unknown = mail_for(unknown, wait=3) is not None
    check("13h only the real account gets a reset email", got_real and not got_unknown, f"real={got_real} unknown={got_unknown}")

    # 13i: a send that keeps failing doesn't affect the request; after 5 attempts it shows Failed and can be resent.
    def_id = s12_publish_workflow({"admin": admin}, f"admin-approves-{RUN_SUFFIX}", "Admin approves",
                                  [{"title": "Admin approval", "approverType": "user", "userId": admin_id, "onReject": "end"}],
                                  fallback=f"user:{admin_id}")  # a brand-new company has nobody in HR yet
    form = f"ask-hooli-{RUN_SUFFIX}"
    bound = def_id is not None and s12_form({"admin": admin}, form, def_id)
    erlich = f"erlich-{RUN_SUFFIX}@hooli.demo"; failing = f"fail-erlich-{RUN_SUFFIX}@hooli.demo"
    code, e = call("POST", "/employees", admin, {"name": "Erlich Hooli", "email": erlich, "roles": ["Employee"], "managerId": admin_id})
    set_password_from_mail(erlich, NEW_PASSWORD)
    # Only now switch to a failing address (the invite itself must arrive); the sign-in name follows the email.
    call("PUT", f"/employees/{(e or {}).get('id')}", admin, {"name": "Erlich Hooli", "email": failing, "roles": ["Employee"], "managerId": admin_id})
    _, erlich_tok, _ = sign_in(failing, NEW_PASSWORD, tenant_name=company)
    sid = submit(erlich_tok, {"details": "standing desk"}, form) if bound and erlich_tok else None
    d = detail(erlich_tok, sid, "InProcess") if sid else {}
    check("13i the request goes ahead normally while its email fails", d.get("status") == "InProcess", str(d.get("status")))
    failed = None; end = time.time() + 75
    while sid and failed is None and time.time() < end:
        _, rows = call("GET", "/emails?status=Failed", admin)
        failed = next((r for r in rows or [] if r.get("submissionId") == sid and r["event"] == "Submitted"), None)
        if failed is None: time.sleep(3)
    check("13i after 5 attempts the email shows Failed with the last error",
          failed is not None and failed["attempts"] == 5 and bool(failed.get("lastError")), str(failed)[:200])
    code, resent = call("POST", f"/emails/{failed['id']}/resend", admin) if failed else (0, None)
    check("13i Resend puts the failed email back in the queue", code == 200 and (resent or {}).get("status") == "Queued", f"http {code}")

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

    # 8f (spec 005): the email queue is persistent; every email for these requests, queued before or after the
    # restart, ends up Sent, with none left stuck in Queued.
    end = time.time() + 30
    while True:
        rows = (emails_for_submission(at["Khalid"], a1, expected=4) + emails_for_submission(at["Khalid"], a2, expected=3)
                + emails_for_submission(gt["Noor"], g1, expected=4))
        if all(r["status"] == "Sent" for r in rows) or time.time() > end: break
        time.sleep(2)
    check("8f every email for the restart-check requests was sent (persistent queue across the restart)",
          len(rows) == 11 and all(r["status"] == "Sent" for r in rows),
          str(sorted((r["event"], r["status"]) for r in rows)))

if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else None
    mail_ready()
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
                     ("Step 12 employees and organisation structure (spec 004)", s12),
                     ("Step 13 email notifications (spec 005)", s13)]:
            step(n, f)
    else:
        log(f"Unknown mode {mode!r}. Use no argument, 'restart-before' or 'restart-after'.")
        sys.exit(1)

    log("\n=== SUMMARY")
    for s, ok, note in results: log(f"{'PASS' if ok else 'FAIL'}  {s}  {note}")
