"""Drive a REAL running MuseDesk backend (live producer) over HTTP + WebSocket, the way the
Quest client does. Usage: python live_e2e.py <base_url>. Prints one line per check."""
import asyncio, json, sys, time
import httpx, websockets

BASE = sys.argv[1].rstrip("/")
WS = BASE.replace("http", "ws", 1)
results = []


def check(name, ok, detail=""):
    results.append(ok)
    print(("PASS " if ok else "FAIL ") + name + (f"  [{detail}]" if detail else ""))


async def collect(sid, token, until, timeout=120.0, after=0):
    """Read the event socket until `until(events)` is true."""
    events = []
    url = f"{WS}/v1/sessions/{sid}/events?after_seq={after}&token={token}"
    async with websockets.connect(url) as ws:
        end = time.time() + timeout
        while time.time() < end:
            try:
                msg = await asyncio.wait_for(ws.recv(), timeout=max(0.1, end - time.time()))
            except asyncio.TimeoutError:
                break
            events.append(json.loads(msg))
            if until(events):
                break
    return events


def types(events):
    return [e["type"] for e in events]


async def run(action):
    async with httpx.AsyncClient(base_url=BASE, timeout=30) as c:
        r = await c.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local", "client": "quest"})
        check(f"[{action}] session created", r.status_code == 201, str(r.status_code))
        body = r.json(); sid, token = body["session_id"], body["token"]
        h = {"Authorization": f"Bearer {token}"}

        r = await c.post(f"/v1/sessions/{sid}/tasks", headers=h,
                         json={"schema_version": 1, "task_template": "auth_demo", "idempotency_key": f"e2e-start-{action}-01"})
        check(f"[{action}] task accepted", r.status_code == 201, str(r.status_code))

        r2 = await c.post(f"/v1/sessions/{sid}/tasks", headers=h,
                          json={"schema_version": 1, "task_template": "auth_demo", "idempotency_key": f"e2e-start-{action}-02"})
        check(f"[{action}] second task refused while running", r2.status_code == 409, r2.text[:80])

        ev = await collect(sid, token, lambda es: any(e["type"] == "decision.requested" for e in es))
        dec = next((e for e in ev if e["type"] == "decision.requested"), None)
        check(f"[{action}] decision requested", dec is not None)
        if dec is None:
            return
        did = dec["decision_id"]
        pre = types(ev)
        check(f"[{action}] nothing built before the decision",
              "artifact.created" not in pre, ",".join(sorted(set(pre))))
        base = [e for e in ev if e["type"] == "agent.status_changed" and e.get("agent_id") == "tester"]
        check(f"[{action}] tester ran real baseline tests", any("passed" in (e["payload"].get("progress_message") or "") for e in base),
              base[-1]["payload"].get("progress_message") if base else "none")
        check(f"[{action}] all pre-decision events labelled live",
              all(e["payload"].get("source_mode", "live") == "live" for e in ev))
        seqs = [e["seq"] for e in ev]
        check(f"[{action}] seq dense from 1", seqs == list(range(1, len(seqs) + 1)), f"{seqs[:3]}..{seqs[-1]}")

        snap = (await c.get(f"/v1/sessions/{sid}/snapshot", headers=h)).json()
        pend = [d for d in snap["decisions"] if d["state"] == "requested"]
        check(f"[{action}] snapshot shows the pending decision", len(pend) == 1 and pend[0]["decision_id"] == did)

        key = f"e2e-resp-{action}-0001"
        r = await c.post(f"/v1/sessions/{sid}/decisions/{did}/respond", headers=h,
                         json={"schema_version": 1, "action": action, "idempotency_key": key})
        check(f"[{action}] response applied", r.status_code == 200 and r.json().get("applied") is True, r.text[:90])
        r = await c.post(f"/v1/sessions/{sid}/decisions/{did}/respond", headers=h,
                         json={"schema_version": 1, "action": action, "idempotency_key": key})
        check(f"[{action}] replayed response applies nothing new", r.status_code == 200 and r.json().get("applied") is False, r.text[:90])
        other = "reject" if action == "approve" else "approve"
        r = await c.post(f"/v1/sessions/{sid}/decisions/{did}/respond", headers=h,
                         json={"schema_version": 1, "action": other, "idempotency_key": f"e2e-resp-{action}-0002"})
        check(f"[{action}] conflicting second answer rejected", r.status_code == 409, str(r.status_code))

        last = seqs[-1]
        done = lambda es: any(e["type"] in ("task.completed", "task.failed") and (e.get("task_id") or "").endswith("-oauth") for e in es)
        ev2 = await collect(sid, token, done, after=last)
        t2 = types(ev2)
        check(f"[{action}] resume from after_seq has no duplicates", all(e["seq"] > last for e in ev2))
        fin = next((e for e in ev2 if e["type"] in ("task.completed", "task.failed") and (e.get("task_id") or "").endswith("-oauth")), None)
        if action == "approve":
            art = next((e for e in ev2 if e["type"] == "artifact.created"), None)
            check("[approve] developer then tester worked",
                  any(e["type"] == "agent.status_changed" and e.get("agent_id") == "developer" and e["payload"]["state"] == "coding" for e in ev2)
                  and any(e["type"] == "agent.status_changed" and e.get("agent_id") == "tester" and e["payload"]["state"] == "testing" for e in ev2))
            check("[approve] artifact carries real test counts", art is not None and art["payload"].get("tests_passed", 0) > 0,
                  json.dumps({k: art["payload"].get(k) for k in ("tests_passed", "tests_failed", "files_changed")}) if art else "none")
            check("[approve] completed with an artifact reference",
                  fin is not None and fin["type"] == "task.completed" and fin["payload"].get("artifact_id") and fin["payload"].get("task_state") == "completed",
                  json.dumps(fin["payload"])[:120] if fin else "none")
            if art:
                a = await c.get(f"/v1/sessions/{sid}/artifacts/{art['payload']['artifact_id']}", headers=h)
                check("[approve] artifact retrievable", a.status_code == 200, str(a.status_code))
            snap2 = (await c.get(f"/v2/sessions/{sid}/snapshot", headers=h)).json()
            check("[approve] v2 artifacts captured as live", any(x.get("source_mode") == "live" for x in snap2.get("artifacts", [])),
                  str([(x.get("kind"), x.get("source_mode")) for x in snap2.get("artifacts", [])]))
        else:
            check("[reject] no artifact, nothing written", "artifact.created" not in t2, ",".join(sorted(set(t2))))
            check("[reject] branch reported as cancelled, with no artifact reference",
                  fin is not None and fin["payload"].get("task_state") == "cancelled" and not fin["payload"].get("artifact_id"),
                  json.dumps(fin["payload"])[:120] if fin else "none")

        # cross-session isolation
        r = await c.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local", "client": "quest"})
        other_h = {"Authorization": f"Bearer {r.json()['token']}"}
        x = await c.get(f"/v1/sessions/{sid}/snapshot", headers=other_h)
        check(f"[{action}] another session's token is refused", x.status_code == 401, str(x.status_code))


async def main():
    async with httpx.AsyncClient(base_url=BASE, timeout=10) as c:
        r = await c.get("/health")
        check("health", r.status_code == 200, r.text[:80])
    await run("approve")
    await run("reject")
    print(f"TOTAL {sum(results)}/{len(results)} passed")
    sys.exit(0 if all(results) else 1)

asyncio.run(main())
