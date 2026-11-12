"""Regression tests for the v2 defects found in the 2026-11-12 backend audit
(docs/Engineering/_audit_backend.md). Each test names the defect it pins."""
from __future__ import annotations

import asyncio

from fastapi.testclient import TestClient

from app.main import create_app
from app.phase2.models import InboxItemV2
from app.settings import Settings
from tests.conftest import open_session, p2_fresh, seed_v2_task

SID = "ses-test"


def _events(client, sid, token, n):
    with client.websocket_connect(f"/v2/sessions/{sid}/events?after_seq=0&token={token}") as ws:
        return [ws.receive_json() for _ in range(n)]


# ── D1: POST /v2/tasks must announce the card (the Unity board waits for it) ──
def test_create_task_emits_task_created_with_card_fields(client):
    body, headers = open_session(client)
    sid, token = body["session_id"], body["token"]
    card = seed_v2_task(client, sid, headers, title="Write login tests", agent_id="tester",
                        priority=3, key="k-created-0001")
    (ev,) = _events(client, sid, token, 1)
    assert ev["type"] == "task.created" and ev["seq"] == 1
    assert ev["task_id"] == card["task_id"]
    assert ev["agent_id"] == "tester"
    p = ev["payload"]
    # exactly what Phase2Reducer.ApplyTaskDelta reads to seed the card
    assert p["title"] == "Write login tests"
    assert p["task_state"] == "queued"
    assert p["revision"] == 0 and p["priority"] == 3
    assert p["source_mode"] == card["source_mode"] == "demo"


def test_create_task_replay_does_not_emit_a_second_event(client):
    body, headers = open_session(client)
    sid = body["session_id"]
    a = seed_v2_task(client, sid, headers, key="k-created-dup1")
    b = seed_v2_task(client, sid, headers, key="k-created-dup1")  # same key + body
    assert a == b
    snap = client.get(f"/v2/sessions/{sid}/snapshot", headers=headers).json()
    assert snap["last_seq"] == 1 and len(snap["tasks"]) == 1


def test_unassigned_created_event_omits_agent_id(client):
    body, headers = open_session(client)
    sid, token = body["session_id"], body["token"]
    seed_v2_task(client, sid, headers, key="k-created-0002")
    (ev,) = _events(client, sid, token, 1)
    assert "agent_id" not in ev  # wire() drops None ids (schema types them as string)


# ── D2: a task id is only addressable inside the session that owns it ──
def test_task_commands_are_scoped_to_the_owning_session(client):
    a, a_headers = open_session(client)
    b, b_headers = open_session(client)
    tid = seed_v2_task(client, a["session_id"], a_headers, key="k-scope-0001")["task_id"]

    cmds = {
        "assign": {"target_agent_id": "tester", "expected_revision": 0},
        "priority": {"priority": 3, "expected_revision": 0},
        "pause": {"expected_revision": 0},
        "resume": {"expected_revision": 0},
    }
    for i, (verb, extra) in enumerate(cmds.items()):
        r = client.post(f"/v2/sessions/{b['session_id']}/tasks/{tid}/{verb}", headers=b_headers,
                        json={"schema_version": 2, "idempotency_key": f"k-scope-b-{i:04d}", **extra})
        assert r.status_code == 404, (verb, r.text)
        assert r.json()["code"] == "NOT_FOUND"

    # session A's task is untouched, and session B's log is empty
    t = client.get(f"/v2/sessions/{a['session_id']}/snapshot", headers=a_headers).json()["tasks"][0]
    assert t["agent_id"] is None and t["revision"] == 0 and t["task_state"] == "queued"
    assert client.get(f"/v2/sessions/{b['session_id']}/snapshot",
                      headers=b_headers).json()["last_seq"] == 0


def test_diagram_rejects_another_sessions_artifact(client):
    from app.phase2.artifacts import ArtifactService
    from app.phase2.events import Phase2EventBus

    a, a_headers = open_session(client)
    b, b_headers = open_session(client)
    tid = seed_v2_task(client, a["session_id"], a_headers, key="k-scope-art1")["task_id"]
    store = client.app.state.phase2_store

    async def mint():
        return await ArtifactService(store, Phase2EventBus(store), "demo").create_artifact(
            a["session_id"], tid, "research_report", "plan",
            [{"page": 0, "title": "p", "content": "ok", "line_refs": []}])

    art = asyncio.run(mint())
    body = {"schema_version": 2, "template": "auth_flow", "artifact_id": art.artifact_id}
    r = client.post(f"/v2/sessions/{b['session_id']}/diagrams", headers=b_headers,
                    json={**body, "idempotency_key": "k-scope-dia-b"})
    assert r.status_code == 404
    r = client.post(f"/v2/sessions/{a['session_id']}/diagrams", headers=a_headers,
                    json={**body, "idempotency_key": "k-scope-dia-a"})
    assert r.status_code == 201


# ── D3: inbox.updated carries the full item the client reducer upserts ──
def test_inbox_updated_payload_carries_full_item():
    async def body():
        store, bus, svc = p2_fresh()
        f = svc["focus"]
        await f.set_focus(SID, "do_not_disturb", None, [])
        await f.route_notification(SID, "progress", "minor update", 0, critical=False)
        ev = [e for e in store.events_after_v2(SID, 0) if e["type"] == "inbox.updated"][0]
        item = InboxItemV2(**ev["payload"]["item"])  # validates the exact snapshot shape
        row = store.list_inbox_v2(SID)[0]
        assert item.item_id == row["item_id"] and item.created_at == row["created_at"]
        assert item.summary == "minor update" and item.critical is False
        assert ev["payload"]["item_id"] == item.item_id  # flat fields kept
    asyncio.run(body())


# ── D6: mode labels in a live run are consistent, and canned content stays "demo" ──
def _live_client(tmp_path) -> TestClient:
    return TestClient(create_app(Settings(
        db_path=":memory:", producer="live", producer_step_seconds=0.0,
        require_pairing=True, workspace_root=str(tmp_path), task_timeout_seconds=60)))


def test_live_producer_labels_board_state_live_but_canned_content_demo(tmp_path):
    with _live_client(tmp_path) as client:
        body, headers = open_session(client)
        sid, token = body["session_id"], body["token"]

        # real, backend-recorded state → live
        assert client.get(f"/v2/sessions/{sid}/snapshot", headers=headers).json()["source_mode"] == "live"
        card = seed_v2_task(client, sid, headers, key="k-live-mode-01")
        assert card["source_mode"] == "live"
        with client.websocket_connect(f"/v1/sessions/{sid}/events?after_seq=0&token={token}") as ws:
            assert ws.receive_json()["payload"]["source_mode"] == "live"  # session.ready

        # a graph derived from the session's real tasks → live
        r = client.post(f"/v2/sessions/{sid}/diagrams", headers=headers,
                        json={"schema_version": 2, "template": "task_dependency_graph",
                              "idempotency_key": "k-live-dia-task"})
        assert r.status_code == 201 and r.json()["source_mode"] == "live"

        # fixed illustrative templates and the canned Q&A are NEVER presented as live
        for i, template in enumerate(("auth_flow", "module_dependency_graph")):
            r = client.post(f"/v2/sessions/{sid}/diagrams", headers=headers,
                            json={"schema_version": 2, "template": template,
                                  "idempotency_key": f"k-live-dia-{i:04d}"})
            assert r.status_code == 201 and r.json()["source_mode"] == "demo", template
        q = client.post(f"/v1/sessions/{sid}/questions", headers=headers,
                        json={"schema_version": 1, "agent_id": "researcher", "question": "why?"})
        assert q.status_code == 200 and q.json()["source_mode"] == "demo"


def test_demo_producer_never_labels_anything_live(client):
    body, headers = open_session(client)
    sid = body["session_id"]
    assert client.get(f"/v2/sessions/{sid}/snapshot", headers=headers).json()["source_mode"] == "demo"
    for i, template in enumerate(("auth_flow", "module_dependency_graph", "task_dependency_graph")):
        if template == "task_dependency_graph":
            seed_v2_task(client, sid, headers, key="k-demo-dia-seed")
        r = client.post(f"/v2/sessions/{sid}/diagrams", headers=headers,
                        json={"schema_version": 2, "template": template,
                              "idempotency_key": f"k-demo-dia-{i:04d}"})
        assert r.status_code == 201 and r.json()["source_mode"] == "demo"
    q = client.post(f"/v1/sessions/{sid}/questions", headers=headers,
                    json={"schema_version": 1, "agent_id": "researcher", "question": "why?"})
    assert q.json()["source_mode"] == "demo"
