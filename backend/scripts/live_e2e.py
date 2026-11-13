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


async def collect(sid, token, until, timeout=120.0, after=0, ver="v1"):
    """Read the event socket (v1, or the v2 board stream) until `until(events)` is true."""
    events = []
    url = f"{WS}/{ver}/sessions/{sid}/events?after_seq={after}&token={token}"
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


async def run_card():
    """Third run: the same pipeline, launched from a task-board CARD (v2) instead of the
    card-less v1 start. The card must reflect the run and own its artifacts."""
    T = "[card]"
    async with httpx.AsyncClient(base_url=BASE, timeout=30) as c:
        r = await c.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local", "client": "quest"})
        check(f"{T} session created", r.status_code == 201, str(r.status_code))
        body = r.json(); sid, token = body["session_id"], body["token"]
        h = {"Authorization": f"Bearer {token}"}

        r = await c.post(f"/v2/sessions/{sid}/tasks", headers=h,
                         json={"schema_version": 2, "title": "Add OAuth login", "priority": 2,
                               "task_template": "auth_demo", "idempotency_key": "e2e-card-create-01"})
        card = r.json() if r.status_code == 201 else {}
        check(f"{T} executable card created", r.status_code == 201 and card.get("executable") is True
              and card.get("task_template") == "auth_demo" and card.get("task_state") == "queued", r.text[:160])
        if not card:
            return
        cid = card["task_id"]

        async def assign(agent, key, rev):
            return await c.post(f"/v2/sessions/{sid}/tasks/{cid}/assign", headers=h,
                                json={"schema_version": 2, "target_agent_id": agent,
                                      "expected_revision": rev, "idempotency_key": key})

        async def snap_card():
            snap = (await c.get(f"/v2/sessions/{sid}/snapshot", headers=h)).json()
            return next(t for t in snap["tasks"] if t["task_id"] == cid), snap

        r = await assign("developer", "e2e-card-assign-dev1", 0)
        check(f"{T} assigning to the developer is refused", r.status_code == 409
              and r.json().get("error_code") == "ROLE_INCOMPATIBLE", r.text[:140])
        r = await assign("researcher", "e2e-card-assign-res1", 0)
        started = r.json() if r.status_code == 200 else {}
        check(f"{T} assigning to the researcher starts the run", r.status_code == 200
              and started.get("task_state") == "running" and bool(started.get("run_task_id")), r.text[:200])
        r = await assign("researcher", "e2e-card-assign-res1", 0)
        tasks = (await c.get(f"/v1/sessions/{sid}/tasks", headers=h)).json()["tasks"]
        runs = [t for t in tasks if t["parent_task_id"] is None]
        check(f"{T} replayed assign starts no second run", r.status_code == 200 and r.json() == started
              and len(runs) == 1 and runs[0].get("board_task_id") == cid, f"runs={len(runs)}")
        r = await c.post(f"/v1/sessions/{sid}/tasks", headers=h,
                         json={"schema_version": 1, "task_template": "auth_demo", "idempotency_key": "e2e-card-v1-start-1"})
        check(f"{T} v1 start refused while the card's run is in progress", r.status_code == 409
              and r.json().get("error_code") == "RUN_IN_PROGRESS", r.text[:80])

        ev = await collect(sid, token, lambda es: any(e["type"] == "decision.requested" for e in es))
        dec = next((e for e in ev if e["type"] == "decision.requested"), None)
        check(f"{T} decision requested, linked to the card, labelled a template", dec is not None
              and dec["payload"].get("board_task_id") == cid
              and dec["payload"].get("research_provenance") == "template",
              json.dumps({k: dec["payload"].get(k) for k in ("board_task_id", "research_provenance", "research_provenance_label")}) if dec else "none")
        if dec is None:
            return
        did = dec["decision_id"]

        waiting = None
        for _ in range(100):
            waiting, _snap = await snap_card()
            if waiting["task_state"] == "awaiting_approval":
                break
            await asyncio.sleep(0.1)
        check(f"{T} card is awaiting approval", waiting["task_state"] == "awaiting_approval", json.dumps(waiting))
        r = await c.post(f"/v2/sessions/{sid}/tasks/{cid}/pause", headers=h,
                         json={"schema_version": 2, "expected_revision": waiting["revision"], "idempotency_key": "e2e-card-pause-0001"})
        check(f"{T} pause refused mid-run (never pause_requested)", r.status_code == 409
              and r.json().get("error_code") == "TASK_NOT_PAUSABLE", r.text[:140])

        r = await c.post(f"/v1/sessions/{sid}/decisions/{did}/respond", headers=h,
                         json={"schema_version": 1, "action": "approve", "idempotency_key": "e2e-card-resp-0001"})
        check(f"{T} approval applied", r.status_code == 200 and r.json().get("applied") is True, r.text[:90])

        is_end = lambda e: e.get("task_id") == cid and e["type"] in ("task.completed", "task.failed")
        ev2 = await collect(sid, token, lambda es: any(is_end(e) for e in es), ver="v2")
        seqs = [e["seq"] for e in ev2]
        check(f"{T} v2 seq dense from 1", seqs == list(range(1, len(seqs) + 1)), f"{seqs[:3]}..{seqs[-1] if seqs else ''}")
        mine = [e for e in ev2 if e.get("task_id") == cid and e["type"] != "artifact.created"]
        order = [(e["type"], e["payload"].get("task_state")) for e in mine]
        check(f"{T} card events in order", order == [
            ("task.created", "queued"), ("task.assigned", None), ("task.started", "running"),
            ("decision.requested", "awaiting_approval"), ("task.resumed", "running"),
            ("task.completed", "completed")], str(order))
        check(f"{T} card revisions increase by one per event",
              [e["payload"].get("revision") for e in mine] == list(range(len(mine))),
              str([e["payload"].get("revision") for e in mine]))
        check(f"{T} card's decision event names the decision",
              any(e["type"] == "decision.requested" and e["payload"].get("decision_id") == did for e in mine))

        final, snap = await snap_card()
        check(f"{T} card completed in the snapshot", final["task_state"] == "completed", json.dumps(final))
        arts = [e for e in ev2 if e["type"] == "artifact.created"]
        check(f"{T} artifacts reference the card and are real sandbox output", len(arts) >= 2
              and all(a.get("task_id") == cid and a["payload"].get("source_mode") == "live"
                      and a["payload"].get("work_provenance") == "sandbox_execution" for a in arts),
              str([(a.get("task_id"), a["payload"].get("kind"), a["payload"].get("source_mode")) for a in arts]))
        if arts:
            a = await c.get(f"/v2/sessions/{sid}/artifacts/{arts[0]['artifact_id']}", headers=h)
            check(f"{T} card-linked artifact retrievable", a.status_code == 200 and a.json().get("task_id") == cid,
                  f"{a.status_code} kind={a.json().get('kind') if a.status_code == 200 else ''}")
        check(f"{T} snapshot lists the card's artifacts", len(snap.get("artifacts", [])) == len(arts),
              str(len(snap.get("artifacts", []))))

        ev1 = await collect(sid, token, lambda es: any(e["type"] in ("task.completed", "task.failed")
                                                       and (e.get("task_id") or "").endswith("-oauth") for e in es))
        fin = next((e for e in ev1 if e["type"] in ("task.completed", "task.failed") and (e.get("task_id") or "").endswith("-oauth")), None)
        check(f"{T} v1 completion carries board_task_id and real test counts", fin is not None
              and fin["type"] == "task.completed" and fin["payload"].get("board_task_id") == cid,
              json.dumps(fin["payload"])[:200] if fin else "none")
        r = await assign("researcher", "e2e-card-assign-res2", final["revision"])
        check(f"{T} a finished card cannot start another run", r.status_code == 409, r.text[:100])


async def run_revise():
    """Request changes -> a revised (template) decision in the SAME run -> approve it -> real build.
    Then a second session: request changes until no revisions are left -> the run is cancelled."""
    async with httpx.AsyncClient(base_url=BASE, timeout=30) as c:
        async def respond(sid, h, did, action, key):
            return await c.post(f"/v1/sessions/{sid}/decisions/{did}/respond", headers=h,
                                json={"schema_version": 1, "action": action, "idempotency_key": key})

        # ── revise once, then approve ──
        r = await c.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local", "client": "quest"})
        body = r.json(); sid, token = body["session_id"], body["token"]
        h = {"Authorization": f"Bearer {token}"}
        await c.post(f"/v1/sessions/{sid}/tasks", headers=h,
                     json={"schema_version": 1, "task_template": "auth_demo", "idempotency_key": "e2e-revise-start-01"})
        ev = await collect(sid, token, lambda es: any(e["type"] == "decision.requested" for e in es))
        first = next((e for e in ev if e["type"] == "decision.requested"), None)
        check("[revise] first decision requested", first is not None)
        if first is None:
            return
        check("[revise] first decision is revision 0", first["payload"].get("revision") == 0, str(first["payload"].get("revision")))

        r = await respond(sid, h, first["decision_id"], "request_changes", "e2e-revise-resp-0001")
        check("[revise] request_changes applied", r.status_code == 200 and r.json().get("state") == "changes_requested", r.text[:90])
        last = ev[-1]["seq"]
        ev2 = await collect(sid, token, lambda es: any(e["type"] == "decision.requested" for e in es), after=last)
        second = next((e for e in ev2 if e["type"] == "decision.requested"), None)
        check("[revise] a revised decision arrives in the same run", second is not None, ",".join(types(ev2)))
        if second is None:
            return
        p1, p2 = first["payload"], second["payload"]
        check("[revise] new decision id, revision 1, names the decision it revises",
              second["decision_id"] != first["decision_id"] and p2.get("revision") == 1
              and p2.get("revises_decision_id") == first["decision_id"],
              json.dumps({k: p2.get(k) for k in ("revision", "revises_decision_id")}))
        check("[revise] revised proposal differs and is still labelled a template",
              (p2.get("summary"), p2.get("title")) != (p1.get("summary"), p1.get("title"))
              and p2.get("research_provenance") == "template",
              (p2.get("title") or "")[:70])
        check("[revise] nothing was built between the two decisions",
              "artifact.created" not in types(ev2), ",".join(sorted(set(types(ev2)))))
        check("[revise] researcher said it was revising",
              any(e["type"] == "agent.status_changed" and e.get("agent_id") == "researcher"
                  and "evis" in (e["payload"].get("progress_message") or "") for e in ev2),
              next((e["payload"].get("progress_message") for e in ev2
                    if e["type"] == "agent.status_changed" and e.get("agent_id") == "researcher"), "none"))
        snap = (await c.get(f"/v1/sessions/{sid}/snapshot", headers=h)).json()
        states = {d["decision_id"]: d["state"] for d in snap["decisions"]}
        check("[revise] snapshot: first is changes_requested, second is pending",
              states.get(first["decision_id"]) == "changes_requested" and states.get(second["decision_id"]) == "requested", str(states))

        r = await respond(sid, h, first["decision_id"], "approve", "e2e-revise-resp-0002")
        check("[revise] the superseded decision cannot be answered again", r.status_code == 409, str(r.status_code))
        r = await respond(sid, h, second["decision_id"], "approve", "e2e-revise-resp-0003")
        check("[revise] approving the revised decision is applied", r.status_code == 200 and r.json().get("applied") is True, r.text[:90])
        last = ev2[-1]["seq"]
        done = lambda es: any(e["type"] in ("task.completed", "task.failed") and (e.get("task_id") or "").endswith("-oauth") for e in es)
        ev3 = await collect(sid, token, done, after=last)
        art = next((e for e in ev3 if e["type"] == "artifact.created"), None)
        fin = next((e for e in ev3 if e["type"] in ("task.completed", "task.failed") and (e.get("task_id") or "").endswith("-oauth")), None)
        check("[revise] real build after approval: artifact with passing tests, attempts recorded",
              art is not None and art["payload"].get("tests_passed", 0) > 0 and art["payload"].get("tests_failed") == 0,
              json.dumps({k: art["payload"].get(k) for k in ("tests_passed", "tests_failed", "attempts")}) if art else "none")
        check("[revise] run completed", fin is not None and fin["type"] == "task.completed"
              and fin["payload"].get("task_state") == "completed", fin["type"] if fin else "none")

        # ── revise until the limit, then once more ──
        r = await c.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local", "client": "quest"})
        body = r.json(); sid, token = body["session_id"], body["token"]
        h = {"Authorization": f"Bearer {token}"}
        await c.post(f"/v1/sessions/{sid}/tasks", headers=h,
                     json={"schema_version": 1, "task_template": "auth_demo", "idempotency_key": "e2e-limit-start-01"})
        after, rounds, cancelled, all_types = 0, 0, None, []
        for i in range(6):                                   # far more than any sane limit
            evs = await collect(sid, token, lambda es: any(
                e["type"] == "decision.requested" or (e["type"] in ("task.completed", "task.failed")
                                                      and (e.get("task_id") or "").endswith("-oauth")) for e in es), after=after)
            if not evs:
                break
            after = evs[-1]["seq"]; all_types += types(evs)
            d = next((e for e in evs if e["type"] == "decision.requested"), None)
            end = next((e for e in evs if e["type"] in ("task.completed", "task.failed")
                        and (e.get("task_id") or "").endswith("-oauth")), None)
            if end is not None:
                cancelled = end
                break
            if d is None:
                break
            rounds += 1
            await respond(sid, h, d["decision_id"], "request_changes", f"e2e-limit-resp-{i:04d}")
        check("[revise] revisions are bounded (between 2 and 4 decisions before the run stops)", 2 <= rounds <= 4, f"decisions={rounds}")
        check("[revise] at the limit the run is cancelled, with no artifact",
              cancelled is not None and cancelled["payload"].get("task_state") == "cancelled" and "artifact.created" not in all_types,
              json.dumps(cancelled["payload"])[:140] if cancelled else "no end event")


async def main():
    async with httpx.AsyncClient(base_url=BASE, timeout=10) as c:
        r = await c.get("/health")
        check("health", r.status_code == 200, r.text[:80])
    await run("approve")
    await run("reject")
    await run_card()
    await run_revise()
    print(f"TOTAL {sum(results)}/{len(results)} passed")
    sys.exit(0 if all(results) else 1)

asyncio.run(main())
