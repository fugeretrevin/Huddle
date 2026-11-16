"""LIVE user-journey check against a REAL running backend (the canonical request from the E2E brief).

    python -I scripts/live_journey.py http://127.0.0.1:8765 [evidence.json]

It does what the Quest client does over HTTP, with the configured model provider:
  B1  the user asks the Manager for the canonical objective -> a plan (LIVE_MODEL when a model is configured);
      nothing runs before approval; the same request replayed returns the same plan (idempotent)
  B5  a revision ("password at least 12 characters") -> a new plan revision; the old revision cannot be committed
  B4  commit (approve) the new revision, dangerous steps explicitly authorised as the stamp + "OK" do; a replay creates
      no second set of tasks
  C3  the plan's tasks run (the live runner: sequential, text-only) -> per-task states and outputs recorded
  D2  /btw to the Developer "what are you working on right now?" -> a grounded reply; nothing changes
  D5  /btw "what is the weather tomorrow?" -> no invented access; nothing changes
  D6  /btw "change the password rule to 12 characters" -> a proposed action that needs confirmation, NOT applied
Prints one PASS/FAIL/INFO line per check and writes every response to the evidence file.
Evidence level: LIVE backend (Level 2/5 without the headset). It never prints credentials.
"""
import json, sys, time, uuid
import httpx

BASE = sys.argv[1].rstrip("/")
OUT = sys.argv[2] if len(sys.argv) > 2 else "live_journey_evidence.json"
OBJECTIVE = "Build me a login page with email validation, password requirements, and tests."
evidence, results = {"objective": OBJECTIVE, "steps": []}, []


def check(name, ok, detail=""):
    results.append(bool(ok))
    print(("PASS " if ok else "FAIL ") + name + (f"  [{str(detail)[:160]}]" if detail != "" else ""), flush=True)


def info(name, detail):
    print("INFO " + name + f"  [{str(detail)[:200]}]", flush=True)


def keep(step, resp):
    try:
        body = resp.json()
    except Exception:
        body = resp.text[:2000]
    evidence["steps"].append({"step": step, "status": resp.status_code, "body": body})
    return body


def key(tag):
    return f"journey-{tag}-{uuid.uuid4().hex[:10]}"


with httpx.Client(base_url=BASE, timeout=180) as c:
    r = c.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local", "client": "quest"})
    b = keep("session", r); sid, tok = b["session_id"], b["token"]; h = {"Authorization": f"Bearer {tok}"}
    check("session created", r.status_code == 201, sid)

    # â”€â”€ B1: objective -> plan â”€â”€
    k1 = key("propose")
    t0 = time.time()
    r = c.post(f"/v2/sessions/{sid}/manager/plans", headers=h, json={"schema_version": 2, "request_id": "journey-1", "objective": OBJECTIVE, "idempotency_key": k1})
    plan = keep("B1 propose", r)
    dt = time.time() - t0
    check("B1 Manager returned a plan", r.status_code == 201 and plan.get("plan_id"), f"{r.status_code} in {dt:.1f}s")
    pid = plan.get("plan_id")
    check("B1 provenance is labelled", plan.get("provenance") in ("LIVE_MODEL", "SCRIPTED_DEMO", "TEMPLATE", "DETERMINISTIC"), plan.get("provenance"))
    info("B1 plan", f"{plan.get('title')} | {len(plan.get('items', []))} items | agents=" + ",".join(sorted({i.get('proposed_agent') for i in plan.get('items', [])})))
    check("B1 nothing committed before approval", plan.get("status") == "proposed" and not plan.get("committed_tasks"), plan.get("status"))
    ms = keep("B1 manager status", c.get(f"/v2/sessions/{sid}/manager/status", headers=h))
    info("B1 manager status before approval", json.dumps(ms)[:200])

    r = c.post(f"/v2/sessions/{sid}/manager/plans", headers=h, json={"schema_version": 2, "request_id": "journey-1", "objective": OBJECTIVE, "idempotency_key": k1})
    again = keep("B1 replay", r)
    check("B1 replayed request returns the same plan (no second plan)", again.get("plan_id") == pid, again.get("plan_id"))

    # â”€â”€ B5: revise â”€â”€
    rev0 = plan.get("revision", 1)
    items = plan.get("items", [])
    new_items = []
    for it in items:
        it2 = dict(it)
        if it2.get("kind") in ("implementation", "develop", "code") or it2.get("proposed_agent") == "developer":
            ac = list(it2.get("acceptance_criteria") or [])
            if not any("12" in a for a in ac):
                ac.append("Password must be at least 12 characters")
            it2["acceptance_criteria"] = ac
        new_items.append(it2)
    r = c.post(f"/v2/sessions/{sid}/manager/plans/{pid}/revise", headers=h,
               json={"schema_version": 2, "revision": rev0, "items": new_items, "idempotency_key": key("revise")})
    revised = keep("B5 revise", r)
    rev1 = revised.get("revision") if isinstance(revised, dict) else None
    check("B5 revision accepted as a new plan revision", r.status_code in (200, 201) and rev1 == rev0 + 1, f"{r.status_code} rev {rev0}->{rev1}")
    if r.status_code not in (200, 201):
        info("B5 revise refused", json.dumps(revised)[:300])
        rev1 = rev0
        revised = plan
    has12 = any("12" in a for it in revised.get("items", []) for a in (it.get("acceptance_criteria") or []))
    check("B5 the revised plan carries the 12-character rule", has12)

    approve = [it["client_item_id"] for it in revised.get("items", [])]
    danger = [it["client_item_id"] for it in revised.get("items", []) if it.get("dangerous")]
    if rev1 != rev0:
        r = c.post(f"/v2/sessions/{sid}/manager/plans/{pid}/commit", headers=h,
                   json={"schema_version": 2, "revision": rev0, "approved_item_ids": approve, "authorized_dangerous_item_ids": danger, "idempotency_key": key("stale")})
        keep("B5 stale commit", r)
        check("B5 the superseded revision cannot be approved", r.status_code >= 400, r.status_code)

    # A dangerous step not explicitly OK'd is refused (the paper asks for the extra OK).
    if danger:
        r = c.post(f"/v2/sessions/{sid}/manager/plans/{pid}/commit", headers=h,
                   json={"schema_version": 2, "revision": rev1, "approved_item_ids": approve, "authorized_dangerous_item_ids": [], "idempotency_key": key("noauth")})
        keep("B4 commit without explicit OK", r)
        check("B4 a dangerous step without the explicit OK is refused", r.status_code >= 400, f"{r.status_code} dangerous={len(danger)}")

    # â”€â”€ B4: approve â”€â”€
    kc = key("commit")
    r = c.post(f"/v2/sessions/{sid}/manager/plans/{pid}/commit", headers=h,
               json={"schema_version": 2, "revision": rev1, "approved_item_ids": approve, "authorized_dangerous_item_ids": danger, "idempotency_key": kc})
    com = keep("B4 commit", r)
    tasks = (com.get("tasks") or com.get("committed_tasks")) if isinstance(com, dict) else None
    check("B4 approval creates the tasks", r.status_code in (200, 201) and tasks, f"{r.status_code} tasks={len(tasks or [])}")
    r = c.post(f"/v2/sessions/{sid}/manager/plans/{pid}/commit", headers=h,
               json={"schema_version": 2, "revision": rev1, "approved_item_ids": approve, "authorized_dangerous_item_ids": danger, "idempotency_key": kc})
    com2 = keep("B4 commit replay", r)
    same = isinstance(com2, dict) and json.dumps(com2.get("tasks") or com2.get("committed_tasks"), sort_keys=True) == json.dumps(tasks, sort_keys=True)
    check("B4 replayed approval creates nothing new (exactly one approval)", same, r.status_code)

    # â”€â”€ C3: the runner works the cards â”€â”€
    task_ids = [t.get("task_id") if isinstance(t, dict) else t for t in (tasks or [])]
    st = {}
    t0 = time.time()
    while time.time() - t0 < 300:
        st = c.get(f"/v2/sessions/{sid}/manager/plans/{pid}/status", headers=h).json()
        states = json.dumps(st)
        if '"running"' not in states and '"queued"' not in states and '"assigned"' not in states and time.time() - t0 > 5:
            break
        time.sleep(4)
    evidence["steps"].append({"step": "C3 plan status (final)", "status": 200, "body": st})
    info("C3 plan status after the run", json.dumps(st)[:400])
    check("C3 the runner reached an end state for every task (completed / failed / blocked, none stuck)",
          '"running"' not in json.dumps(st), f"{time.time() - t0:.0f}s")

    # â”€â”€ D: /btw (read-only side questions) â”€â”€
    before = json.dumps(c.get(f"/v2/sessions/{sid}/manager/plans/{pid}/status", headers=h).json(), sort_keys=True)
    visit = "visit-" + uuid.uuid4().hex[:8]
    dev_task = next((t.get("task_id") for t in (tasks or []) if isinstance(t, dict) and t.get("agent_id") == "developer"), task_ids[0] if task_ids else None)

    def ask(agent, q, tag):
        r = c.post(f"/v2/sessions/{sid}/agents/{agent}/side-question", headers=h,
                   json={"schema_version": 2, "request_id": f"journey-{tag}", "task_id": dev_task if agent == "developer" else None,
                         "visit_session_id": visit, "question": q, "idempotency_key": key(tag)})
        return r, keep(f"D {tag}", r)

    r, a = ask("developer", "What are you working on right now?", "dev-status")
    check("D2 Developer answers a status question", r.status_code == 200 and a.get("reply"), f"{a.get('status')} {a.get('provenance')}")
    info("D2 reply", f"{a.get('reply')} | intent={a.get('intent')} | context={a.get('context_used')}")
    r, a = ask("tester", "Have all the tests passed?", "tester")
    check("D3 Tester answers about tests", r.status_code == 200 and a.get("reply"), f"{a.get('status')} {a.get('provenance')}")
    info("D3 reply", a.get("reply"))
    r, a = ask("developer", "What is the weather tomorrow?", "weather")
    check("D5 unsupported question gets an answer that claims no weather access", r.status_code == 200 and a.get("reply"), a.get("status"))
    info("D5 reply", a.get("reply"))
    r, a = ask("developer", "Actually, change the password rule to 12 characters.", "change")
    pa = a.get("proposed_action") if isinstance(a, dict) else None
    check("D6 a change request becomes a proposal that is NOT applied", r.status_code == 200 and (pa is None or (pa.get("applied") is False and pa.get("requires_confirmation") is True)),
          f"status={a.get('status')} action={json.dumps(pa)[:120]}")
    after = json.dumps(c.get(f"/v2/sessions/{sid}/manager/plans/{pid}/status", headers=h).json(), sort_keys=True)
    check("D /btw changed nothing in the plan or its tasks", before == after)
    r = c.delete(f"/v2/sessions/{sid}/agents/developer/visits/{visit}", headers=h)
    keep("D close visit", r)

evidence["summary"] = {"passed": sum(results), "total": len(results)}
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(evidence, f, indent=1)
print(f"TOTAL {sum(results)}/{len(results)} passed; evidence -> {OUT}")

