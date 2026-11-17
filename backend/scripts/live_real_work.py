"""LIVE check that the Manager's team does REAL work (backend started with MUSEDESK_P3_EXECUTE_CODE=true,
i.e. scripts/start_wall_backend.ps1 with a model key).

    python -I scripts/live_real_work.py http://127.0.0.1:8765 [evidence.json] [sandbox_root]

  R1  the Manager plans the canonical objective with the model (LIVE_MODEL) and says work_execution=sandbox
  R2  approve -> the runner works every card to an end state (completed / failed / blocked, none stuck)
  R3  every Developer / Tester card that completed carries work_mode=sandbox and real pytest counts
  R4  the files the cards name exist in <sandbox_root>/plans/<plan_id>
  R5  INDEPENDENT re-run: this script runs pytest on that folder itself and compares with the last card's counts
  R6  /btw to the Tester "Have the tests passed?" answers from the recorded result (nothing changes)
Prints PASS/FAIL/INFO lines and writes every response to the evidence file. Never prints credentials.
"""
import json, os, re, subprocess, sys, time, uuid
from pathlib import Path

import httpx

BASE = sys.argv[1].rstrip("/")
OUT = sys.argv[2] if len(sys.argv) > 2 else "live_real_work_evidence.json"
ROOT = Path(sys.argv[3] if len(sys.argv) > 3 else Path(__file__).resolve().parents[1] / "sandbox_workspaces")
OBJECTIVE = "Build me a login page with email validation, password requirements, and tests."
evidence, results = {"objective": OBJECTIVE, "steps": []}, []


def check(name, ok, detail=""):
    results.append(bool(ok))
    print(("PASS " if ok else "FAIL ") + name + (f"  [{str(detail)[:200]}]" if detail != "" else ""), flush=True)


def info(name, detail):
    print("INFO " + name + f"  [{str(detail)[:300]}]", flush=True)


def keep(step, resp):
    try:
        body = resp.json()
    except Exception:
        body = resp.text[:2000]
    evidence["steps"].append({"step": step, "status": resp.status_code, "body": body})
    return body


def key(tag):
    return f"real-{tag}-{uuid.uuid4().hex[:10]}"


with httpx.Client(base_url=BASE, timeout=240) as c:
    b = keep("session", c.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local", "client": "quest"}))
    sid = b["session_id"]
    h = {"Authorization": f"Bearer {b['token']}"}

    t0 = time.time()
    r = c.post(f"/v2/sessions/{sid}/manager/plans", headers=h, json={
        "schema_version": 2, "request_id": "real-1", "objective": OBJECTIVE, "idempotency_key": key("propose")})
    plan = keep("R1 propose", r)
    pid = plan.get("plan_id")
    check("R1 the Manager planned with the model", r.status_code == 201 and plan.get("provenance") == "LIVE_MODEL",
          f"{r.status_code} {plan.get('provenance')} in {time.time() - t0:.0f}s")
    check("R1 the plan says its cards run in the sandbox", plan.get("work_execution") == "sandbox", plan.get("work_execution"))
    items = plan.get("items", [])
    info("R1 plan", f"{plan.get('title')} | " + "; ".join(f"{i.get('proposed_agent')}: {i.get('title')}" for i in items))

    approve = [i["client_item_id"] for i in items]
    danger = [i["client_item_id"] for i in items if i.get("dangerous")]
    r = c.post(f"/v2/sessions/{sid}/manager/plans/{pid}/commit", headers=h, json={
        "schema_version": 2, "revision": plan.get("revision", 1), "approved_item_ids": approve,
        "authorized_dangerous_item_ids": danger, "idempotency_key": key("commit")})
    com = keep("R2 commit", r)
    tasks = com.get("tasks") or []
    check("R2 approval created the cards", r.status_code == 200 and tasks, f"{len(tasks)} cards")

    st, t0 = {}, time.time()
    while time.time() - t0 < 1800:
        st = c.get(f"/v2/sessions/{sid}/manager/plans/{pid}/status", headers=h).json()
        s = json.dumps(st)
        if '"running"' not in s and '"queued"' not in s and time.time() - t0 > 5:
            break
        time.sleep(6)
    keep("R2 status (final)", httpx.Response(200, json=st))
    states = [cd.get("task_state") or cd.get("state") for cd in st.get("cards", [])]
    check("R2 every card reached an end state", all(x in ("completed", "failed", "blocked", "cancelled") for x in states),
          f"{time.time() - t0:.0f}s states={states}")

    # What the headset reads: each card's recorded result (completed) or failure text.
    sandbox_cards = []
    for cd in st.get("cards", []):
        state = cd.get("task_state") or cd.get("state")
        text = cd.get("result") or cd.get("failure") or ""
        info(f"card {cd.get('agent_id')}", f"{state} | {text[:200]}")
        if cd.get("agent_id") in ("developer", "tester") and state in ("completed", "failed"):
            m = re.search(r"(\d+) passed, (\d+) failed", text)
            files = re.match(r"Code written: (.*?) ·", text)
            sandbox_cards.append((cd, {
                "sandbox": text.startswith(("Code written:", "Tests run:")) or "sandbox" in text.lower()
                           or "nothing was" in text.lower(),
                "tests_ran": bool(m), "tests_passed": int(m.group(1)) if m else 0,
                "tests_failed": int(m.group(2)) if m else 0,
                "files_changed": [x.strip() for x in files.group(1).split(",")] if files else [],
                "state": state}))
    check("R3 Developer/Tester cards were worked in the sandbox (not text)",
          sandbox_cards and all(p["sandbox"] for _, p in sandbox_cards), len(sandbox_cards))
    check("R3 at least one Developer/Tester card completed with a real pytest run",
          any(p["state"] == "completed" and p["tests_ran"] and p["tests_passed"] > 0 for _, p in sandbox_cards),
          [(p["tests_passed"], p["tests_failed"]) for _, p in sandbox_cards])

    folder = ROOT / "plans" / pid
    named = sorted({f for _, p in sandbox_cards for f in p["files_changed"]})
    tests_on_disk = sorted(x.name for x in folder.glob("test_*.py")) if folder.exists() else []
    info("R4 folder", f"{folder.name}: " + ", ".join(sorted(x.name for x in folder.iterdir())) if folder.exists() else "missing")
    check("R4 the files the cards name exist on disk, with tests", folder.exists() and tests_on_disk
          and all((folder / f).exists() for f in named), f"named={named} tests={tests_on_disk}")
    # The folder's final state is the one the LAST card that ran pytest left (completed or failed).
    last = next((p for _, p in reversed(sandbox_cards) if p["tests_ran"]), None)
    if folder.exists():
        env = {k: v for k, v in os.environ.items() if not re.search(r"(?i)KEY|TOKEN|SECRET|MUSEDESK_", k)}
        proc = subprocess.run([sys.executable, "-m", "pytest", "-q", "-p", "no:cacheprovider", "--rootdir",
                               str(folder), str(folder)], cwd=str(folder), capture_output=True, text=True,
                              timeout=120, env=env)
        out = proc.stdout + proc.stderr
        m_p = re.search(r"(\d+) passed", out)
        m_f = re.search(r"(\d+) failed", out)
        mine = (int(m_p.group(1)) if m_p else 0, int(m_f.group(1)) if m_f else 0)
        evidence["independent_pytest"] = out[-3000:]
        theirs = (last["tests_passed"], last["tests_failed"]) if last else None
        check("R5 an independent pytest run of the folder matches the last card's counts", theirs == mine,
              f"script={mine} card={theirs}")
    else:
        check("R5 an independent pytest run of the folder matches the last card's counts", False, "no folder")

    tester = next((cd.get("task_id") for cd in st.get("cards", []) if cd.get("agent_id") == "tester"), None)
    before = json.dumps(st, sort_keys=True)
    r = c.post(f"/v2/sessions/{sid}/agents/tester/side-question", headers=h, json={
        "schema_version": 2, "request_id": "real-ask", "task_id": tester, "visit_session_id": "visit-real",
        "question": "Have the tests passed?", "idempotency_key": key("ask")})
    ans = keep("R6 ask tester", r)
    info("R6 Tester says", (ans.get("reply") if isinstance(ans, dict) else ans) or "")
    after = json.dumps(c.get(f"/v2/sessions/{sid}/manager/plans/{pid}/status", headers=h).json(), sort_keys=True)
    check("R6 asking changed nothing", before == after)

Path(OUT).write_text(json.dumps(evidence, indent=1), encoding="utf-8")
print(f"{sum(results)}/{len(results)} checks passed; evidence: {OUT}")
sys.exit(0 if all(results) else 1)
