"""Scripted offline demo producer tests (MASTER §13.3, §14).

The demo must replay the full task-card -> research -> approval -> handoff -> diff
-> FAILED test -> recovery -> passing test -> completion story as a schema-valid,
gap-free v2 event sequence, with every entity stamped source_mode=demo, and it must
be serveable over the existing /v2 snapshot + websocket.
"""
from __future__ import annotations

import asyncio
import json
from pathlib import Path

from app.phase2.demo import DemoGatewayV2, load_demo_script
from app.phase2.models import EventEnvelopeV2
from tests.conftest import open_session, p2_fresh

SID = "ses-demo-test"
REPO = Path(__file__).resolve().parents[3]
SCHEMA_ENUM = set(
    json.loads((REPO / "contracts" / "phase2" / "event_v2.schema.json")
               .read_text(encoding="utf-8"))["properties"]["type"]["enum"]
)


def _run_demo(source_mode_check=True):
    async def body():
        store, bus, _ = p2_fresh()
        gw = DemoGatewayV2(store, bus)
        result = await gw.replay(SID)
        return store, bus, result
    return asyncio.run(body())


def test_demo_fixture_loads_and_has_recovery_content():
    script = load_demo_script()
    assert script["source_mode"] == "demo"
    assert set(script["tasks"]) == {"research-auth", "develop-auth", "test-auth"}
    # The fixture must contain BOTH a failing and a passing test report (recovery).
    assert script["artifacts"]["test_report_failed"]["kind"] == "test_report"
    assert script["artifacts"]["test_report_passed"]["kind"] == "test_report"
    assert "failed" in script["artifacts"]["test_report_failed"]["summary"].lower()


def test_demo_sequence_is_gapfree_and_schema_valid():
    store, bus, result = _run_demo()
    events = store.events_after_v2(SID, 0)
    seqs = [e["seq"] for e in events]
    assert seqs == list(range(1, len(events) + 1)), "seq must be monotonic and gap-free"
    assert result.last_seq == len(events)
    # Every event validates against the frozen envelope DTO and the schema enum.
    for ev in events:
        env = EventEnvelopeV2(**ev)
        assert env.type.value in SCHEMA_ENUM
        assert ev["schema_version"] == 2
        # No null optional id leaked onto the wire.
        for k in ("task_id", "agent_id", "artifact_id", "handoff_id"):
            assert ev.get(k, "nonnull") is not None


def test_demo_has_failure_and_recovery_branch():
    store, bus, result = _run_demo()
    types = result.event_types
    assert "task.failed" in types, "recovery branch requires a genuine failure beat"
    assert types.count("task.completed") == 3  # research, test (2nd run), develop
    assert "task.resumed" in types  # developer resumes to fix after the failure
    # Two test_request handoffs (initial + re-test) and one test_failure handoff.
    handoffs = store.list_handoffs_v2(SID)
    kinds = [h["kind"] for h in handoffs]
    assert kinds.count("test_request") == 2
    assert "test_failure" in kinds
    assert "blueprint" in kinds
    # Failed test report precedes the passing test report in creation order.
    arts = store.list_artifacts_v2(SID)
    test_reports = [a for a in arts if a["kind"] == "test_report"]
    assert len(test_reports) == 2
    summaries = [a["summary"] for a in test_reports]
    assert any("1 failed" in s for s in summaries), "failed test report present"
    assert any("3 passed" in s and "0 failed" in s for s in summaries), "passing report present"


def test_demo_everything_is_demo_mode_never_live():
    store, bus, result = _run_demo()
    for t in store.list_tasks_v2(SID):
        assert t["source_mode"] == "demo"
    for h in store.list_handoffs_v2(SID):
        assert h["source_mode"] == "demo"
    for a in store.list_artifacts_v2(SID):
        assert a["source_mode"] == "demo"
    for ev in store.events_after_v2(SID, 0):
        # Every event is explicitly stamped demo; the demo producer can never emit a
        # live source_mode on any entity or event.
        assert ev["payload"].get("source_mode") == "demo"


def test_demo_tasks_reach_terminal_states():
    store, bus, result = _run_demo()
    by_logical = {}
    for t in store.list_tasks_v2(SID):
        by_logical[t["id"].rsplit("--", 1)[-1]] = t
    assert by_logical["research-auth"]["task_state"] == "completed"
    assert by_logical["develop-auth"]["task_state"] == "completed"
    assert by_logical["test-auth"]["task_state"] == "completed"


def test_demo_replay_is_idempotent():
    async def body():
        store, bus, _ = p2_fresh()
        gw = DemoGatewayV2(store, bus)
        r1 = await gw.replay(SID)
        last1 = store.last_seq_v2(SID)
        r2 = await gw.replay(SID)
        last2 = store.last_seq_v2(SID)
        return r1, r2, last1, last2, store
    r1, r2, last1, last2, store = asyncio.run(body())
    assert r1.already_ran is False
    assert r2.already_ran is True
    assert last1 == last2, "a second replay must not append or duplicate events"
    # No duplicate tasks/handoffs/artifacts.
    assert len(store.list_tasks_v2(SID)) == 3
    assert len(store.list_handoffs_v2(SID)) == 4


def test_demo_served_over_v2_snapshot_and_ws(client):
    body, headers = open_session(client)
    sid = body["session_id"]
    store = client.app.state.phase2_store
    bus = client.app.state.phase2_bus
    asyncio.run(DemoGatewayV2(store, bus).replay(sid))

    r = client.get(f"/v2/sessions/{sid}/snapshot", headers=headers)
    assert r.status_code == 200, r.text
    snap = r.json()
    assert snap["schema_version"] == 2
    assert len(snap["tasks"]) == 3
    assert len(snap["artifacts"]) == 6
    assert len(snap["handoffs"]) == 4
    assert all(a["source_mode"] == "demo" for a in snap["artifacts"])

    # The whole story is replayable over the websocket from seq 0.
    token = body["token"]
    seen = []
    with client.websocket_connect(f"/v2/sessions/{sid}/events?after_seq=0&token={token}") as ws:
        for _ in range(200):
            ev = ws.receive_json()
            seen.append(ev["type"])
            if ev["type"] == "station.milestone":
                break
    assert seen[0] == "task.created"
    assert "handoff.created" in seen and "task.failed" in seen
    assert seen[-1] == "station.milestone"
