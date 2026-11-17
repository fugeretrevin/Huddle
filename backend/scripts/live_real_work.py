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
# Any real objective can be run (owner, rc18: "test this with building something real"): MUSEDESK_REAL_OBJECTIVE and the
# follow-up change request MUSEDESK_REAL_CHANGE (worded to finish "Developer, can you ...?"). The defaults are the canonical demo.
OBJECTIVE = os.environ.get("MUSEDESK_REAL_OBJECTIVE") or "Build me a login page with email validation, password requirements, and tests."
CHANGE = os.environ.get("MUSEDESK_REAL_CHANGE") or "change the password minimum to 12 characters"
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

    # ── R7: read-only "what have you changed?" to the Developer ──
    dev_task = next((cd.get("task_id") for cd in reversed(st.get("cards", [])) if cd.get("agent_id") == "developer"), None)
    r = c.post(f"/v2/sessions/{sid}/agents/developer/side-question", headers=h, json={
        "schema_version": 2, "request_id": "real-changed", "task_id": dev_task, "visit_session_id": "visit-real",
        "question": "Developer, what have you changed?", "idempotency_key": key("changed")})
    ans7 = keep("R7 ask developer what changed", r)
    reply7 = (ans7.get("reply") if isinstance(ans7, dict) else "") or ""
    info("R7 Developer says", reply7)
    check("R7 the Developer's answer names a real file", any(f in reply7 for f in named) or any(
        x.name in reply7 for x in folder.glob("*.py")), "")

    # ── R8/R9: a change request is only a suggestion; Apply change adds ONE step that is really worked ──
    r = c.post(f"/v2/sessions/{sid}/agents/developer/side-question", headers=h, json={
        "schema_version": 2, "request_id": "real-change", "task_id": dev_task, "visit_session_id": "visit-real",
        "question": f"Developer, can you {CHANGE}?", "idempotency_key": key("change")})
    ans8 = keep("R8 change request", r)
    amend_txt = ((ans8.get("proposed_amendment") or {}).get("summary") if isinstance(ans8, dict) else None)
    st8 = c.get(f"/v2/sessions/{sid}/manager/plans/{pid}/status", headers=h).json()
    check("R8 the change came back as a suggestion and nothing changed",
          bool(amend_txt) and json.dumps(st8, sort_keys=True) == after, (amend_txt or "no amendment")[:120])
    plan_now = c.get(f"/v2/sessions/{sid}/manager/plans/{pid}", headers=h).json()
    t_apply = time.time()
    akey = key("apply")
    body = {"schema_version": 2, "revision": plan_now.get("revision"), "summary": amend_txt or CHANGE,
            "agent_id": "developer", "after_task_id": dev_task, "idempotency_key": akey}
    r = c.post(f"/v2/sessions/{sid}/manager/plans/{pid}/amend", headers=h, json=body)
    applied = keep("R9 apply change", r)
    again = c.post(f"/v2/sessions/{sid}/manager/plans/{pid}/amend", headers=h, json=body).json()
    new_ids = [t.get("task_id") for t in (applied.get("tasks") or [])] if isinstance(applied, dict) else []
    check("R9 Apply change created exactly one step; pressing again created nothing",
          r.status_code == 200 and len(new_ids) == 1 and [t.get("task_id") for t in (again.get("tasks") or [])] == new_ids,
          f"{r.status_code} {new_ids}")
    end9, t0 = {}, time.time()
    while new_ids and time.time() - t0 < 900:
        s9 = c.get(f"/v2/sessions/{sid}/manager/plans/{pid}/status", headers=h).json()
        end9 = next((cd for cd in s9.get("cards", []) if cd.get("task_id") == new_ids[0]), {})
        if (end9.get("task_state") or end9.get("state")) in ("completed", "failed", "blocked", "cancelled"):
            break
        time.sleep(6)
    res9 = end9.get("result") or end9.get("failure") or ""
    info("R9 the new step", f"{end9.get('title')} -> {end9.get('task_state') or end9.get('state')} | {res9[:200]} | {time.time() - t_apply:.0f}s")
    check("R9 the new step was worked for real (pytest counts on its card)",
          (end9.get("task_state") or end9.get("state")) == "completed" and bool(re.search(r"\d+ passed", res9)), res9[:120])
    evidence["apply_change"] = {"suggestion": amend_txt, "task": new_ids, "end": end9, "seconds": round(time.time() - t_apply)}

# ── human-readable evidence (FINAL_LIVE_DEMO_EVIDENCE.md quotes it) ──
md = [f"# Live run evidence {time.strftime('%Y-%m-%d %H:%M')}", "",
      f"- backend: {BASE} (Meta model; MUSEDESK_P3_EXECUTE_CODE on)", f"- objective: {OBJECTIVE}",
      f"- plan: `{pid}` ({plan.get('provenance')}), {len(items)} steps", ""]
md += ["| card | agent | state | recorded result |", "|---|---|---|---|"]
try:
    with httpx.Client(base_url=BASE, timeout=30) as c2:
        fin = c2.get(f"/v2/sessions/{sid}/manager/plans/{pid}/status", headers=h).json()
    for cd in fin.get("cards", []):
        md.append(f"| `{cd.get('task_id')}` {cd.get('title', '')[:60]} | {cd.get('agent_id')} | {cd.get('task_state') or cd.get('state')} | "
                  f"{(cd.get('result') or cd.get('failure') or cd.get('blocked_reason') or '').replace('|', '/')[:160]} |")
except Exception as exc:  # noqa: BLE001
    md.append(f"(status not readable: {type(exc).__name__})")
if folder.exists():
    md += ["", f"## Files in `sandbox_workspaces/plans/{pid}`", ""]
    md += [f"- `{p.name}` ({p.stat().st_size} bytes)" for p in sorted(folder.iterdir()) if p.is_file()]
    code = next((p for p in sorted(folder.glob("*.py")) if not p.name.startswith("test_")), None)
    if code:
        md += ["", f"### `{code.name}` (first 40 lines, verbatim)", "", "```python",
               *code.read_text(encoding="utf-8").splitlines()[:40], "```"]
md += ["", "## Independent pytest run of the folder (this script, after the run)", "", "```",
       *(evidence.get("independent_pytest", "")[-1500:].splitlines()), "```"]
if "apply_change" in evidence:
    a = evidence["apply_change"]
    md += ["", "## Apply change", "", f"- suggestion: {a['suggestion']}", f"- new step: `{a['task']}` -> "
           f"{(a['end'] or {}).get('task_state') or (a['end'] or {}).get('state')}: {((a['end'] or {}).get('result') or '')[:200]}",
           f"- seconds from Apply to the end of the step: {a['seconds']}"]
md += ["", f"Checks: {sum(results)}/{len(results)} passed."]
Path(OUT).with_suffix(".md").write_text("\n".join(md), encoding="utf-8")

Path(OUT).write_text(json.dumps(evidence, indent=1), encoding="utf-8")
print(f"{sum(results)}/{len(results)} checks passed; evidence: {OUT}")
sys.exit(0 if all(results) else 1)
