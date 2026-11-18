"""Remove a task / clear the board (owner, rc21): the card leaves the board as cancelled +
removed, work in flight never lands, pending decisions are withdrawn, nothing waits."""
from __future__ import annotations

import json
import time

from app.phase2.board_remove import BoardRemover
from tests.phase3.p3helpers import FakeProvider, open_session
from tests.phase3.test_p3_adhoc import _client, _exec, assign, create, events
from tests.phase3.test_p3_runner import _committed, artifacts, state, tick
from tests.phase3.test_p3_sandbox_work import ScriptedProvider


def remove(c, sid, h, task_id, key=None):
    headers = dict(h)
    if key:
        headers["Idempotency-Key"] = key
    return c.post(f"/v2/sessions/{sid}/tasks/{task_id}/remove", headers=headers)


def clear(c, sid, h, include_running=None):
    body = {} if include_running is None else {"include_running": include_running}
    return c.post(f"/v2/sessions/{sid}/board/clear", headers=h, json=body)


def snapshot(c, sid, h):
    r = c.get(f"/v2/sessions/{sid}/snapshot", headers=h)
    assert r.status_code == 200, r.text
    return r.json()


def removed_events(c, sid, task_id=None):
    return [e for e in events(c, sid, task_id, "task.completed") if e["payload"].get("removed")]


def row(c, task_id):
    return c.app.state.phase2_store.get_task_v2(task_id)


# ─────────────────────── remove ───────────────────────
def test_remove_queued_card_contract_and_idempotence():
    with _client(FakeProvider()) as c:
        sid, h = open_session(c)
        card = create(c, sid, h, title="Throwaway")
        r = remove(c, sid, h, card["task_id"])
        assert r.status_code == 200, r.text
        body = r.json()
        assert set(body) == {"task"}
        t = body["task"]
        assert t["task_id"] == card["task_id"] and t["task_state"] == "cancelled"
        assert t["revision"] == card["revision"] + 1
        assert row(c, card["task_id"])["removed"] == 1           # kept, never deleted

        ev = removed_events(c, sid, card["task_id"])
        assert len(ev) == 1
        assert ev[0]["event_id"] == f"evt2-removed-{card['task_id']}-r{t['revision']}"
        p = ev[0]["payload"]
        assert p["task_state"] == "cancelled" and p["revision"] == t["revision"]
        assert p["removed"] is True and p["previous_state"] == "queued"

        assert snapshot(c, sid, h)["tasks"] == []

        again = remove(c, sid, h, card["task_id"])                 # no-op: same card, no event
        assert again.status_code == 200 and again.json() == body
        assert len(removed_events(c, sid, card["task_id"])) == 1

        assert remove(c, sid, h, "t2-doesnotexist").status_code == 404
        sid2, h2 = open_session(c)
        assert remove(c, sid2, h2, card["task_id"]).status_code == 404   # other session's card
        assert remove(c, sid, {}, card["task_id"]).status_code == 401


def test_remove_with_idempotency_key_header_replays():
    with _client() as c:
        sid, h = open_session(c)
        card = create(c, sid, h)
        first = remove(c, sid, h, card["task_id"], key="rm-key-00000001")
        second = remove(c, sid, h, card["task_id"], key="rm-key-00000001")
        assert first.status_code == second.status_code == 200
        assert first.json() == second.json()
        assert len(removed_events(c, sid, card["task_id"])) == 1


def test_removed_card_refuses_other_commands_and_is_never_worked():
    prov = FakeProvider("Done.\nSummary: ok.")
    with _client(prov) as c:
        sid, h = open_session(c)
        card = assign(c, sid, h, create(c, sid, h), "developer")     # recorded as ad-hoc work
        t = remove(c, sid, h, card["task_id"]).json()["task"]
        assert tick(c) == 0 and prov.calls == []
        r = c.post(f"/v2/sessions/{sid}/tasks/{card['task_id']}/assign", headers=h, json={
            "schema_version": 2, "target_agent_id": "tester", "expected_revision": t["revision"],
            "idempotency_key": "rm-assign-0001"})
        assert r.status_code == 409


class RemovingProvider:
    """Removes the card being worked while the 'model' is answering (as the route would)."""

    def __init__(self, c, inner):
        self.c, self.inner, self.target, self.sid = c, inner, None, None

    async def complete(self, *, system, messages, max_tokens=600, purpose="side"):
        if purpose == "work" and self.target:
            st = self.c.app.state
            await BoardRemover(st.phase2_store, st.phase2_bus, "live").remove(self.sid, self.target)
            self.target = None
        return await self.inner.complete(system=system, messages=messages,
                                         max_tokens=max_tokens, purpose=purpose)


def _remove_while_worked(c, prov, agent="developer"):
    sid, h = open_session(c)
    card = assign(c, sid, h, create(c, sid, h), agent)
    prov.sid, prov.target = sid, card["task_id"]
    assert tick(c) == 1
    tid = card["task_id"]
    assert state(c, tid) == "cancelled" and row(c, tid)["removed"] == 1
    types = [e["type"] for e in events(c, sid, tid)]
    assert types.count("task.started") == 1
    assert "artifact.created" not in types and "task.failed" not in types
    done = events(c, sid, tid, "task.completed")
    assert len(done) == 1 and done[0]["payload"]["removed"] is True
    assert tick(c) == 0
    assert snapshot(c, sid, h)["tasks"] == [] and snapshot(c, sid, h)["artifacts"] == []
    return sid, tid


def test_remove_running_adhoc_text_card_result_never_lands():
    with _client() as c:
        prov = RemovingProvider(c, FakeProvider("Done.\nSummary: ok."))
        c.app.state.p3_provider = prov
        _remove_while_worked(c, prov)


def test_remove_running_adhoc_sandbox_card_result_never_lands(tmp_path):
    with _exec(tmp_path, None) as c:
        prov = RemovingProvider(c, ScriptedProvider())
        c.app.state.p3_provider = prov
        sid, tid = _remove_while_worked(c, prov)
        assert artifacts(c, sid, tid) == []          # nothing recorded for the card either


def test_the_unity_client_bodies_are_accepted_and_idempotent():
    """Exactly what the Unity client sends, plus empty bodies."""
    with _client() as c:
        sid, h = open_session(c)
        a = create(c, sid, h, title="A")
        body = {"schema_version": 2, "idempotency_key": "unity-remove-000000001"}
        r1 = c.post(f"/v2/sessions/{sid}/tasks/{a['task_id']}/remove", headers=h, json=body)
        r2 = c.post(f"/v2/sessions/{sid}/tasks/{a['task_id']}/remove", headers=h, json=body)
        assert r1.status_code == r2.status_code == 200 and r1.json() == r2.json()
        assert r1.json()["task"]["task_state"] == "cancelled"
        assert len(removed_events(c, sid, a["task_id"])) == 1
        # The same key on another card's remove is a different command: refused, nothing removed.
        b = create(c, sid, h, title="B")
        r = c.post(f"/v2/sessions/{sid}/tasks/{b['task_id']}/remove", headers=h, json=body)
        assert r.status_code == 409 and r.json()["code"] == "IDEMPOTENCY_KEY_REUSE"
        assert state(c, b["task_id"]) == "queued"

        cbody = {"schema_version": 2, "include_running": True,
                 "idempotency_key": "unity-clear-0000000001"}
        r1 = c.post(f"/v2/sessions/{sid}/board/clear", headers=h, json=cbody)
        assert r1.status_code == 200 and r1.json() == {"removed": [b["task_id"]]}
        create(c, sid, h, title="C")                              # replay does not remove it
        r2 = c.post(f"/v2/sessions/{sid}/board/clear", headers=h, json=cbody)
        assert r2.status_code == 200 and r2.json() == r1.json()
        assert len(snapshot(c, sid, h)["tasks"]) == 1

        # Empty bodies / no body: defaults (include_running true), no idempotency.
        d = create(c, sid, h, title="D")
        assert c.post(f"/v2/sessions/{sid}/tasks/{d['task_id']}/remove", headers=h,
                      json={}).status_code == 200
        assert c.post(f"/v2/sessions/{sid}/board/clear", headers=h).json()["removed"]
        assert snapshot(c, sid, h)["tasks"] == []
        assert c.post(f"/v2/sessions/{sid}/board/clear", headers=h, json={}).json() == {"removed": []}


# ─────────────────────── clear ───────────────────────
def test_clear_removes_every_card_and_the_snapshot_is_empty():
    with _client(FakeProvider("Done.\nSummary: ok.")) as c:
        sid, h = open_session(c)
        done = assign(c, sid, h, create(c, sid, h, title="Done one"), "developer")
        assert tick(c) == 1 and state(c, done["task_id"]) == "completed"
        queued = create(c, sid, h, title="Queued")
        blocked = create(c, sid, h, title="Blocked", deps=[queued["task_id"]])
        _, cards = _committed(c, sid, h)
        assert snapshot(c, sid, h)["artifacts"]               # the finished card's result
        ids = [t["task_id"] for t in snapshot(c, sid, h)["tasks"]]
        assert len(ids) == 6

        r = clear(c, sid, h)
        assert r.status_code == 200, r.text
        assert r.json() == {"removed": ids}
        snap = snapshot(c, sid, h)
        assert snap["tasks"] == [] and snap["artifacts"] == [] and snap["handoffs"] == []
        for tid in ids:
            ev = removed_events(c, sid, tid)
            assert len(ev) == 1 and ev[0]["payload"]["task_state"] == "cancelled"
        assert removed_events(c, sid, done["task_id"])[0]["payload"]["previous_state"] == "completed"
        assert removed_events(c, sid, blocked["task_id"])[0]["payload"]["previous_state"] == "blocked"
        assert clear(c, sid, h).json() == {"removed": []}         # nothing left
        assert tick(c) == 0
        # Every plan card was removed: no plan summary is written for it.
        assert [a for a in c.app.state.phase2_store.list_artifacts_v2(sid)
                if a["kind"] == "completion"] == []


def test_clear_can_leave_running_cards():
    with _client(mode="demo") as c:                               # works the same in demo mode
        sid, h = open_session(c)
        a = create(c, sid, h, title="A")
        b = create(c, sid, h, title="B")
        c.app.state.phase2_store.update_task_v2(a["task_id"], task_state="running",
                                                now="2026-11-17T00:00:00Z")
        r = clear(c, sid, h, include_running=False)
        assert r.json() == {"removed": [b["task_id"]]}
        assert [t["task_id"] for t in snapshot(c, sid, h)["tasks"]] == [a["task_id"]]
        assert clear(c, sid, h, include_running=True).json() == {"removed": [a["task_id"]]}


# ─────────────────────── plans ───────────────────────
def test_plan_runner_never_picks_a_removed_plan_card_and_status_says_why():
    prov = FakeProvider("Done.\nSummary: ok.")
    with _client(prov) as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        first, second = cards[0]["task_id"], cards[1]["task_id"]
        remove(c, sid, h, first)
        assert tick(c) == 0
        assert [x["purpose"] for x in prov.calls] == ["plan"]       # the plan summary, no work
        assert state(c, second) == "blocked"                      # dependents unchanged
        st = c.get(f"/v2/sessions/{sid}/manager/plans/{plan['plan_id']}/status", headers=h).json()
        by_id = {x["task_id"]: x for x in st["cards"]}
        assert by_id[first]["state"] == "cancelled" and by_id[first]["removed"] is True
        assert by_id[first]["result"] == "Removed from the board."
        assert "removed from the board" in by_id[second]["blocked_reason"]
        assert st["settled"] is True                              # the Wall does not wait on it


# ─────────────────────── pipeline decisions ───────────────────────
def _wait(pred, seconds=5.0):
    end = time.time() + seconds
    while time.time() < end:
        if pred():
            return True
        time.sleep(0.05)
    return pred()


def _pipeline_card(c, sid, h):
    card = assign(c, sid, h, create(c, sid, h, title="Auth demo", template="auth_demo"), "researcher")
    return card, row(c, card["task_id"])["run_task_id"]


def test_removing_a_card_awaiting_approval_withdraws_the_decision_and_ends_the_run():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        card, run_id = _pipeline_card(c, sid, h)
        assert _wait(lambda: state(c, card["task_id"]) == "awaiting_approval")
        storage = c.app.state.storage
        dec = [d for d in storage.list_decisions(sid) if d["resolved_at"] is None]
        assert len(dec) == 1

        assert remove(c, sid, h, card["task_id"]).json()["task"]["task_state"] == "cancelled"
        d = storage.get_decision(dec[0]["id"])
        assert d["status"] == "expired"
        assert _wait(lambda: storage.get_task(run_id)["status"] in ("cancelled", "failed", "completed"))
        assert not c.app.state.run_launcher.run_in_progress(sid)
        # The run did not build the gated work; the card stays removed.
        assert storage.get_task(run_id)["status"] == "cancelled"
        assert state(c, card["task_id"]) == "cancelled" and row(c, card["task_id"])["removed"] == 1
        # A late answer is refused.
        r = c.post(f"/v1/sessions/{sid}/decisions/{dec[0]['id']}/respond", headers=h, json={
            "action": "approve", "idempotency_key": "late-approve-0001"})
        assert r.status_code == 409
        resolved = [e for e in storage.events_after(sid, 0) if e["type"] == "decision.resolved"]
        assert len(resolved) == 1
        assert resolved[0]["payload"]["state"] == "expired" and resolved[0]["payload"]["withdrawn"] is True


def test_a_decision_raised_after_the_card_was_removed_is_withdrawn_at_once():
    with _client(mode="demo", producer_step_seconds=0.2) as c:
        sid, h = open_session(c)
        card, run_id = _pipeline_card(c, sid, h)
        assert state(c, card["task_id"]) == "running"
        remove(c, sid, h, card["task_id"])
        storage = c.app.state.storage
        assert _wait(lambda: storage.get_task(run_id)["status"] in ("cancelled", "failed", "completed"))
        decisions = storage.list_decisions(sid)
        assert decisions and all(d["status"] == "expired" for d in decisions)
        assert not c.app.state.run_launcher.run_in_progress(sid)
        assert state(c, card["task_id"]) == "cancelled"
        # The card never moved after removal (no awaiting_approval, no late completion).
        after = [e for e in events(c, sid, card["task_id"])
                 if e["seq"] > removed_events(c, sid, card["task_id"])[0]["seq"]]
        assert after == []
