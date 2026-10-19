#!/usr/bin/env python3
"""Runs README demo steps 1-7 (no args), or the two-part restart check (step 8: `restart-before` /
`restart-after`, run with a manual app restart in between), against the running PoC. Stdlib only."""
import json, sqlite3, sys, time, urllib.request, urllib.error, os

BASE = "http://localhost:5080/api/poc"
ACME, GLOBEX = "a1a1a1a1-0000-0000-0000-000000000001", "b2b2b2b2-0000-0000-0000-000000000002"
LOG = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "demo.log"), "w")
RESTART_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "demo_restart.json")
RUN_SUFFIX = str(int(time.time()))[-6:]  # keeps form keys unique across repeated runs on a non-empty database
results = []

def log(*a):
    s = " ".join(str(x) for x in a); print(s); LOG.write(s + "\n"); LOG.flush()

def call(method, path, tenant, user=None, body=None):
    h = {"__tenant": tenant, "Content-Type": "application/json", "Accept": "application/json"}
    if user: h["X-Demo-User"] = user
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

def users(tenant):
    _, us = call("GET", "/users", tenant)
    return {u["name"].split()[0]: u["id"] for u in us}

def counts_for(tenant):
    _, us = call("GET", "/users", tenant)
    _, fs = call("GET", "/forms", tenant)
    return {"employees": len(us or []), "forms": len(fs or [])}

def inbox_task(tenant, user, submission_id, wait=8):
    end = time.time() + wait
    while True:
        _, items = call("GET", "/tasks/inbox", tenant, user)
        for i in items or []:
            if i["submissionId"] == submission_id: return i["taskId"]
        if time.time() > end: return None
        time.sleep(1)

def submit(tenant, user, data, form_key="leave-request"):
    code, sid = call("POST", "/submissions", tenant, user, {"formKey": form_key, "data": data})
    return sid if code == 200 else None

def detail(tenant, user, sid, wait_status=None, wait=8):
    end = time.time() + wait
    while True:
        _, d = call("GET", f"/submissions/{sid}", tenant, user)
        if not wait_status or (isinstance(d, dict) and d.get("status") == wait_status) or time.time() > end: return d
        time.sleep(1)

# handoverTo satisfies leave-request once a prior run's step 4 has published a version that requires it;
# it is silently ignored by earlier versions that don't have the field, so this works on any run.
annual = {"leaveType": "Annual", "startDate": "2026-11-01", "endDate": "2026-11-03", "days": "3", "reason": "Family trip", "handoverTo": "Omar"}
sick = {"leaveType": "Sick", "startDate": "2026-11-05", "endDate": "2026-11-05", "days": "1", "handoverTo": "Huda"}

def step(name, fn):
    log(f"\n=== {name}")
    try: fn()
    except Exception as e: check(name, False, f"exception: {e!r}")

state = {}

def s1():
    u = users(ACME); state["acme"] = u
    sid = submit(ACME, u["Sara"], annual); state["s1"] = sid
    t = inbox_task(ACME, u["Omar"], sid); check("1a Omar gets task", t is not None)
    call("POST", f"/tasks/{t}/complete", ACME, u["Omar"], {"outcome": "Approved", "comment": "OK"})
    t2 = inbox_task(ACME, u["Huda"], sid); check("1b Huda gets task after Omar", t2 is not None)
    call("POST", f"/tasks/{t2}/complete", ACME, u["Huda"], {"outcome": "Approved"})
    d = detail(ACME, u["Sara"], sid, "Approved")
    check("1c Sara sees Approved + timeline", d.get("status") == "Approved" and len(d.get("events", [])) >= 3,
          f"status={d.get('status')} events={len(d.get('events', []))}")

def s2():
    u = state["acme"]
    sid = submit(ACME, u["Sara"], sick)
    check("2a Omar gets no task", inbox_task(ACME, u["Omar"], sid, wait=3) is None)
    t = inbox_task(ACME, u["Huda"], sid); check("2b Huda gets task directly", t is not None)
    d = detail(ACME, u["Sara"], sid)
    check("2c hr-only-approval workflow used", d.get("workflowDefinitionId") == "hr-only-approval", str(d.get("workflowDefinitionId")))
    if t: call("POST", f"/tasks/{t}/complete", ACME, u["Huda"], {"outcome": "Approved"})

def s3():
    u = state["acme"]
    sid = submit(ACME, u["Sara"], annual)
    t = inbox_task(ACME, u["Omar"], sid)
    call("POST", f"/tasks/{t}/complete", ACME, u["Omar"], {"outcome": "Rejected", "comment": "Busy period"})
    d = detail(ACME, u["Sara"], sid, "Rejected")
    check("3a status Rejected", d.get("status") == "Rejected", str(d.get("status")))
    check("3b no HR step", len(d.get("tasks", [])) == 1 and inbox_task(ACME, u["Huda"], sid, wait=2) is None,
          f"tasks={len(d.get('tasks', []))}")

def s4():
    u = state["acme"]
    _, design = call("GET", "/forms/leave-request/design", ACME, u["Khalid"])
    cur = next(v for v in design["versions"] if v["id"] == design["currentVersionId"])
    schema = json.loads(cur["schemaJson"]); old_no = cur["versionNo"]; new_no = max(v["versionNo"] for v in design["versions"]) + 1
    schema["fields"] = [x for x in schema["fields"] if x["key"] != "handoverTo"]
    schema["fields"].append({"key": "handoverTo", "label": "Handover to", "type": "text", "required": True})
    c1, _ = call("PUT", "/forms/leave-request/draft", ACME, u["Khalid"], {"schemaJson": json.dumps(schema)})
    c2, v2 = call("POST", "/forms/leave-request/publish", ACME, u["Khalid"])
    check("4a draft saved + new version published", c1 == 200 and c2 == 200 and v2.get("versionNo") == new_no, str(v2)[:100])
    _, forms = call("GET", "/forms", ACME, u["Sara"])
    f = next(x for x in forms if x["key"] == "leave-request")
    check("4b new requests use new version with new field", f["versionNo"] == new_no and "handoverTo" in f["schemaJson"])
    no_handover = {k: v for k, v in annual.items() if k != "handoverTo"}
    code, _ = call("POST", "/submissions", ACME, u["Sara"], {"formKey": "leave-request", "data": no_handover})
    check("4c new version enforces required handoverTo", code >= 400, f"http {code}")
    sid = submit(ACME, u["Sara"], dict(annual, handoverTo="Omar"))
    d2 = detail(ACME, u["Sara"], sid)
    check("4d new-version submission accepted", d2.get("versionNo") == new_no, str(d2.get("versionNo")))
    d1 = detail(ACME, u["Sara"], state["s1"])
    check("4e older submission still renders its own version, marked replaced",
          d1.get("versionNo") == old_no and d1.get("schemaJson") == cur["schemaJson"] and d1.get("versionStatus") != "Published",
          f"v={d1.get('versionNo')} versionStatus={d1.get('versionStatus')}")

def s5():
    g = users(GLOBEX); a = state["acme"]
    check("5a Globex sees only its users", set(g) == {"Fatima", "Yousef", "Ali", "Noor", "Aramis"}, str(sorted(g)))
    _, mine_before = call("GET", "/submissions/mine", GLOBEX, g["Ali"])
    sid = submit(GLOBEX, g["Ali"], sick)
    t = inbox_task(GLOBEX, g["Fatima"], sid)
    check("5c Globex sick leave goes to manager (own binding)", t is not None)
    _, mine_after = call("GET", "/submissions/mine", GLOBEX, g["Ali"])
    check("5b Ali's own requests gained this one and include no Acme ids",
          len(mine_after or []) == len(mine_before or []) + 1 and all(m["id"] != state["s1"] for m in mine_after or []),
          f"{len(mine_before or [])} -> {len(mine_after or [])}")
    _, inbox = call("GET", "/tasks/inbox", ACME, a["Omar"])
    check("5d Acme inbox has no Globex task", all(i["submissionId"] != sid for i in inbox or []))
    code, body = call("GET", "/submissions/mine", GLOBEX, a["Sara"])
    check("5e cross-tenant call refused", code >= 400 and "does not belong" in json.dumps(body), f"http {code}")

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
    u = state["acme"]; g = users(GLOBEX)
    code, b = call("GET", "/forms/all", ACME, u["Huda"])
    check("7a HR cannot open the form designer", code == 403 and "Only Admin" in json.dumps(b), f"http {code}")
    code, b = call("POST", "/forms", ACME, u["Sara"], {"key": "nope", "name": "Nope", "category": "X"})
    check("7b employee cannot create forms", code == 403 and "Only Admin" in json.dumps(b), f"http {code}")
    _, forms = call("GET", "/forms", ACME, u["Sara"])
    check("7c seeded employee forms published", {"leave-request", "work-from-home", "expense-claim", "letter-request"} <= {f["key"] for f in forms},
          str(sorted(f["key"] for f in forms)))
    _, wfs = call("GET", "/workflows", ACME, u["Khalid"])
    ids = {w["definitionId"] for w in wfs or []}
    check("7d admin lists published workflows", {"leave-approval", "hr-only-approval"} <= ids, str(wfs))
    code, d = call("POST", "/forms", ACME, u["Khalid"], {"key": biz, "name": "Business trip", "category": "Travel"})
    check("7e admin creates a form with a v1 draft", code == 200 and d["versions"][0]["status"] == "Draft")
    code, b = call("POST", "/forms", ACME, u["Khalid"], {"key": biz, "name": "Again", "category": "Travel"})
    check("7f duplicate key refused", code == 403 and "already exists" in json.dumps(b), f"http {code}")
    code, _ = call("POST", "/submissions", ACME, u["Sara"], {"formKey": biz, "data": {"details": "x"}})
    check("7g unpublished form cannot be submitted", code >= 400, f"http {code}")
    schema = {"title": "Business trip", "fields": [
        {"key": "tripType", "label": "Trip type", "type": "select", "options": ["Domestic", "International"], "required": True, "reportable": True, "workflowVariable": True},
        {"key": "destination", "label": "Destination", "type": "text", "required": True, "reportable": True},
        {"key": "days", "label": "Days", "type": "number", "required": True, "reportable": True}]}
    call("PUT", f"/forms/{biz}/draft", ACME, u["Khalid"], {"schemaJson": json.dumps(schema)})
    code, v = call("POST", f"/forms/{biz}/publish", ACME, u["Khalid"])
    check("7h admin publishes v1", code == 200 and v["versionNo"] == 1)
    code, b = call("POST", f"/forms/{biz}/bindings", ACME, u["Khalid"], {"workflowDefinitionId": "made-up", "priority": 10})
    check("7i unknown workflow refused", code == 403 and "not published" in json.dumps(b), f"http {code}")
    code, b = call("POST", f"/forms/{biz}/bindings", ACME, u["Khalid"], {"workflowDefinitionId": "leave-approval", "conditionField": "nope", "conditionValue": "x", "priority": 1})
    check("7j condition on unknown field refused", code == 403 and "not on the form" in json.dumps(b), f"http {code}")
    c1, _ = call("POST", f"/forms/{biz}/bindings", ACME, u["Khalid"], {"workflowDefinitionId": "leave-approval", "priority": 10})
    c2, _ = call("POST", f"/forms/{biz}/bindings", ACME, u["Khalid"], {"workflowDefinitionId": "hr-only-approval", "conditionField": "tripType", "conditionValue": "Domestic", "priority": 1})
    check("7k admin adds default + conditional routes", c1 == 200 and c2 == 200)
    sid = submit(ACME, u["Sara"], {"tripType": "Domestic", "destination": "Riyadh", "days": "2"}, biz)
    check("7l domestic trip routed to HR only", inbox_task(ACME, u["Huda"], sid) is not None and inbox_task(ACME, u["Omar"], sid, wait=2) is None)
    sid = submit(ACME, u["Sara"], {"tripType": "International", "destination": "London", "days": "4"}, biz)
    check("7m international trip routed to line manager", inbox_task(ACME, u["Omar"], sid) is not None)
    # Company admin is still an employee: their own request routes to their line manager.
    sid = submit(ACME, u["Khalid"], dict(annual, handoverTo="Sara"))
    check("7o company admin's own request reaches their manager", inbox_task(ACME, u["Omar"], sid) is not None)
    # Vendor admin designs for any company but is not an employee there.
    code, d = call("POST", "/forms", GLOBEX, u["Aramis"], {"key": asset, "name": "Asset request", "category": "IT"})
    check("7p vendor admin creates a form for Globex", code == 200, f"http {code}")
    code, b = call("POST", "/submissions", GLOBEX, u["Aramis"], {"formKey": "leave-request", "data": annual})
    check("7q vendor admin cannot submit requests", code == 403 and "does not belong" in json.dumps(b), f"http {code}")
    _, gforms = call("GET", "/forms", GLOBEX, g["Ali"])
    check("7n Globex does not see Acme's new form", all(f["key"] != biz for f in gforms or []))

def restart_before():
    """Puts one request per tenant at a pending human step, then records their ids for restart_after."""
    a = users(ACME); state["acme"] = a
    g = users(GLOBEX); state["globex"] = g

    a1 = submit(ACME, a["Sara"], annual)  # Annual: sits at Omar (line manager)
    check("8-before A1 Omar gets task", inbox_task(ACME, a["Omar"], a1) is not None)

    a2 = submit(ACME, a["Sara"], sick)  # Sick: routes straight to HR (Huda)
    check("8-before A2 Huda gets task", inbox_task(ACME, a["Huda"], a2) is not None)

    g1 = submit(GLOBEX, g["Ali"], annual)  # Globex has only the default route: manager first
    check("8-before G1 Fatima gets task", inbox_task(GLOBEX, g["Fatima"], g1) is not None)

    data = {"a1": a1, "a2": a2, "g1": g1, "counts_before": {"acme": counts_for(ACME), "globex": counts_for(GLOBEX)}}
    with open(RESTART_FILE, "w") as fh: json.dump(data, fh)
    log(f"Wrote {RESTART_FILE}: {data}")
    log("Now restart the app (reset off), then run: python3 demo.py restart-after")

def restart_after():
    """Reads the ids from restart_before and resumes each workflow from where it left off."""
    with open(RESTART_FILE) as fh: data = json.load(fh)
    a1, a2, g1 = data["a1"], data["a2"], data["g1"]
    a = users(ACME); state["acme"] = a
    g = users(GLOBEX); state["globex"] = g

    # 8a: A1 (at Omar before restart) resumes manager -> HR -> Approved.
    t = inbox_task(ACME, a["Omar"], a1)
    check("8a Omar still has A1 task after restart", t is not None)
    if t: call("POST", f"/tasks/{t}/complete", ACME, a["Omar"], {"outcome": "Approved"})
    t2 = inbox_task(ACME, a["Huda"], a1)
    check("8a Huda gets task after Omar resumes", t2 is not None)
    if t2: call("POST", f"/tasks/{t2}/complete", ACME, a["Huda"], {"outcome": "Approved"})
    d = detail(ACME, a["Sara"], a1, "Approved")
    check("8a A1 Approved after restart", d.get("status") == "Approved", str(d.get("status")))

    # 8b: A2 (at Huda before restart) resumes and rejects: ends with no further tasks.
    t3 = inbox_task(ACME, a["Huda"], a2)
    check("8b Huda still has A2 task after restart", t3 is not None)
    if t3: call("POST", f"/tasks/{t3}/complete", ACME, a["Huda"], {"outcome": "Rejected", "comment": "Not now"})
    d2 = detail(ACME, a["Sara"], a2, "Rejected")
    check("8b A2 Rejected, no further tasks", d2.get("status") == "Rejected" and len(d2.get("tasks", [])) == 1,
          f"status={d2.get('status')} tasks={len(d2.get('tasks', []))}")

    # 8c: G1 (at Fatima before restart) resumes manager -> HR -> Approved, in the other tenant.
    tg = inbox_task(GLOBEX, g["Fatima"], g1)
    check("8c Fatima still has G1 task after restart", tg is not None)
    if tg: call("POST", f"/tasks/{tg}/complete", GLOBEX, g["Fatima"], {"outcome": "Approved"})
    tg2 = inbox_task(GLOBEX, g["Yousef"], g1)
    check("8c Yousef gets task after Fatima resumes", tg2 is not None)
    if tg2: call("POST", f"/tasks/{tg2}/complete", GLOBEX, g["Yousef"], {"outcome": "Approved"})
    dg = detail(GLOBEX, g["Ali"], g1, "Approved")
    check("8c G1 Approved after restart", dg.get("status") == "Approved", str(dg.get("status")))

    # 8d: resuming one tenant's request never surfaces the other tenant's ids.
    _, acme_inbox = call("GET", "/tasks/inbox", ACME, a["Omar"])
    _, acme_mine = call("GET", "/submissions/mine", ACME, a["Sara"])
    check("8d Acme sees none of Globex's ids",
          all(i["submissionId"] != g1 for i in acme_inbox or []) and all(s["id"] != g1 for s in acme_mine or []))
    _, globex_inbox = call("GET", "/tasks/inbox", GLOBEX, g["Fatima"])
    _, globex_mine = call("GET", "/submissions/mine", GLOBEX, g["Ali"])
    check("8d Globex sees none of Acme's ids",
          all(i["submissionId"] not in (a1, a2) for i in globex_inbox or []) and
          all(s["id"] not in (a1, a2) for s in globex_mine or []))

    # 8e: seeding stayed idempotent across the restart; nothing was re-seeded or lost.
    counts_after = {"acme": counts_for(ACME), "globex": counts_for(GLOBEX)}
    check("8e per-tenant employee/form counts unchanged across restart",
          counts_after == data["counts_before"], f"before={data['counts_before']} after={counts_after}")

if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else None
    if mode == "restart-before":
        step("Step 8 (before restart)", restart_before)
    elif mode == "restart-after":
        step("Step 8 (after restart)", restart_after)
    elif mode is None:
        for n, f in [("Step 1 happy path", s1), ("Step 2 conditional routing", s2), ("Step 3 reject path", s3),
                     ("Step 4 form versioning", s4), ("Step 5 tenant isolation", s5), ("Step 6 reporting values", s6),
                     ("Step 7 form creation, routing, admin-only", s7)]:
            step(n, f)
    else:
        log(f"Unknown mode {mode!r}. Use no argument, 'restart-before' or 'restart-after'.")
        sys.exit(1)

    log("\n=== SUMMARY")
    for s, ok, note in results: log(f"{'PASS' if ok else 'FAIL'}  {s}  {note}")
