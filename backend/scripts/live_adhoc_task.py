"""LIVE check that a card the USER adds and assigns is worked for real (no Manager plan).

Backend started LIVE with a model, the plan runner on, and MUSEDESK_P3_EXECUTE_CODE=true
(scripts/start_wall_backend.ps1 with a model key does all three).

    python -I scripts/live_adhoc_task.py http://127.0.0.1:8765 [sandbox_root]

  A1  create a card (no agent), assign it to the Developer, set its priority: the same three
      commands the headset sent in the owner's rc21 session
  A2  the card goes queued -> running -> completed/failed within the time limit (never stuck)
  A3  its artifact carries real pytest counts
  A4  the files exist in <sandbox_root>/tasks/<card id> and an INDEPENDENT pytest run of that
      folder by this script agrees with the card
  A5  /btw to the Developer answers from the recorded result
Prints PASS/FAIL/INFO lines. Never prints credentials.
"""
import os, re, subprocess, sys, time, uuid
from pathlib import Path

import httpx

BASE = sys.argv[1].rstrip("/") if len(sys.argv) > 1 else "http://127.0.0.1:8765"
ROOT = Path(sys.argv[2] if len(sys.argv) > 2 else Path(__file__).resolve().parents[1] / "sandbox_workspaces")
TITLE = os.environ.get("MUSEDESK_ADHOC_TITLE") or "Write a function that reverses a string, with tests"
results = []


def check(name, ok, detail=""):
    results.append(bool(ok))
    print(("PASS " if ok else "FAIL ") + name + (f"  [{str(detail)[:240]}]" if detail != "" else ""), flush=True)


def info(name, detail):
    print("INFO " + name + f"  [{str(detail)[:400]}]", flush=True)


def key(tag):
    return f"adhoc-{tag}-{uuid.uuid4().hex[:10]}"


def card_of(c, sid, h, tid):
    snap = c.get(f"/v2/sessions/{sid}/snapshot", headers=h).json()
    return next((t for t in snap.get("tasks", []) if t.get("task_id") == tid), {}), snap


with httpx.Client(base_url=BASE, timeout=60) as c:
    b = c.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local", "client": "quest"}).json()
    sid, h = b["session_id"], {"Authorization": f"Bearer {b['token']}"}

    r = c.post(f"/v2/sessions/{sid}/tasks", headers=h, json={
        "schema_version": 2, "title": TITLE, "priority": 1, "dependencies": [], "idempotency_key": key("create")})
    card = r.json()
    tid = card.get("task_id")
    r2 = c.post(f"/v2/sessions/{sid}/tasks/{tid}/assign", headers=h, json={
        "schema_version": 2, "target_agent_id": "developer", "expected_revision": card.get("revision", 0),
        "idempotency_key": key("assign")})
    card = r2.json()
    r3 = c.post(f"/v2/sessions/{sid}/tasks/{tid}/priority", headers=h, json={
        "schema_version": 2, "priority": 2, "expected_revision": card.get("revision", 1),
        "idempotency_key": key("prio")})
    check("A1 create / assign / priority accepted", (r.status_code, r2.status_code, r3.status_code) == (201, 200, 200),
          f"{r.status_code} {r2.status_code} {r3.status_code} card={tid}")

    t0, seen, cur = time.time(), [], {}
    while time.time() - t0 < 900:
        cur, snap = card_of(c, sid, h, tid)
        st = cur.get("task_state")
        if not seen or seen[-1] != st:
            seen.append(st)
            info("state", f"{st} at {time.time() - t0:.0f}s")
        if st in ("completed", "failed", "cancelled"):
            break
        if st == "queued" and time.time() - t0 > 45:
            info("hint", "still queued after 45 s: is the backend LIVE with a model, and are "
                         "MUSEDESK_P3_MANAGER_ENABLED / MUSEDESK_P3_PLAN_RUNNER_ENABLED on? "
                         "(scripts/start_wall_backend.ps1 sets them)")
            break
        time.sleep(3)
    final = cur.get("task_state")
    # 'running' is only seen when the work outlasts one 3 s poll (it does with a real model).
    check("A2 the card was worked to an end state (not left queued)",
          final in ("completed", "failed"), f"states={seen} in {time.time() - t0:.0f}s")

    summary, page = "", ""
    for a in snap.get("artifacts", []):
        ar = c.get(f"/v2/sessions/{sid}/artifacts/{a['artifact_id']}", headers=h, params={"page": 0, "page_size": 1}).json()
        if ar.get("task_id") == tid:
            summary = ar.get("summary") or ""
            page = (ar.get("pages") or [{}])[0].get("content", "")
    info("card result", summary)
    info("artifact page 0", page[:400].replace("\n", " | "))
    m = re.search(r"(\d+) passed, (\d+) failed", summary)
    check("A3 the card's result carries real pytest counts", bool(m), summary[:160])

    folder = ROOT / "tasks" / tid
    files = sorted(p.name for p in folder.iterdir()) if folder.exists() else []
    info("A4 folder", f"{folder} : {', '.join(files) or 'missing'}")
    if folder.exists():
        env = {k: v for k, v in os.environ.items() if not re.search(r"(?i)KEY|TOKEN|SECRET|MUSEDESK_", k)}
        proc = subprocess.run([sys.executable, "-m", "pytest", "-q", "-p", "no:cacheprovider", "--rootdir",
                               str(folder), str(folder)], cwd=str(folder), capture_output=True, text=True,
                              timeout=120, env=env)
        out = proc.stdout + proc.stderr
        mp, mf = re.search(r"(\d+) passed", out), re.search(r"(\d+) failed", out)
        mine = (int(mp.group(1)) if mp else 0, int(mf.group(1)) if mf else 0)
        theirs = (int(m.group(1)), int(m.group(2))) if m else None
        info("A4 independent pytest", out.strip().splitlines()[-1] if out.strip() else "(no output)")
        check("A4 an independent pytest run of the folder matches the card", theirs == mine, f"script={mine} card={theirs}")
    else:
        check("A4 an independent pytest run of the folder matches the card", False, "no folder")

    r = c.post(f"/v2/sessions/{sid}/agents/developer/side-question", headers=h, json={
        "schema_version": 2, "request_id": "adhoc-ask", "task_id": tid, "visit_session_id": "visit-adhoc",
        "question": "What have you done?", "idempotency_key": key("ask")})
    reply = (r.json().get("reply") if r.status_code == 200 else r.text) or ""
    info("A5 Developer says", reply)
    check("A5 /btw answered", r.status_code == 200 and bool(reply.strip()), r.status_code)

print(f"{sum(results)}/{len(results)} checks passed")
sys.exit(0 if all(results) else 1)
