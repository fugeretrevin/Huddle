"""v2 HTTP + WebSocket route tests, including the P2-00 exit criterion and v1
isolation (MASTER §3, ticket exit criterion)."""
from __future__ import annotations

import asyncio

from tests.conftest import open_session, seed_v2_task


def _read_n(ws, n):
    return [ws.receive_json() for _ in range(n)]


def test_v2_snapshot_shape_and_auth(client):
    body, headers = open_session(client)
    sid = body["session_id"]
    r = client.get(f"/v2/sessions/{sid}/snapshot", headers=headers)
    assert r.status_code == 200
    snap = r.json()
    assert snap["schema_version"] == 2
    assert snap["source_mode"] == "demo"
    assert snap["focus"]["mode"] == "normal"
    # auth enforced exactly like v1
    assert client.get(f"/v2/sessions/{sid}/snapshot").status_code == 401


def test_create_assign_and_snapshot_reflects_state(client):
    body, headers = open_session(client)
    sid = body["session_id"]
    task = seed_v2_task(client, sid, headers, title="Build auth", key="k-create-0001")
    assert task["task_state"] == "queued"
    assert task["revision"] == 0
    r = client.post(f"/v2/sessions/{sid}/tasks/{task['task_id']}/assign", headers=headers,
                    json={"schema_version": 2, "target_agent_id": "developer",
                          "expected_revision": 0, "idempotency_key": "k-assign-0001"})
    assert r.status_code == 200
    assert r.json()["agent_id"] == "developer"
    assert r.json()["revision"] == 1
    snap = client.get(f"/v2/sessions/{sid}/snapshot", headers=headers).json()
    t = next(t for t in snap["tasks"] if t["task_id"] == task["task_id"])
    assert t["agent_id"] == "developer" and t["revision"] == 1


def test_assign_idempotent_replay_applies_once(client):
    body, headers = open_session(client)
    sid = body["session_id"]
    task = seed_v2_task(client, sid, headers, key="k-create-0002")
    tid = task["task_id"]
    payload = {"schema_version": 2, "target_agent_id": "tester",
               "expected_revision": 0, "idempotency_key": "k-assign-dup"}
    r1 = client.post(f"/v2/sessions/{sid}/tasks/{tid}/assign", headers=headers, json=payload)
    r2 = client.post(f"/v2/sessions/{sid}/tasks/{tid}/assign", headers=headers, json=payload)
    assert r1.status_code == r2.status_code == 200
    assert r1.json() == r2.json()  # canonical replay, identical
    # exactly one revision bump despite the retry
    snap = client.get(f"/v2/sessions/{sid}/snapshot", headers=headers).json()
    t = next(t for t in snap["tasks"] if t["task_id"] == tid)
    assert t["revision"] == 1


def test_assign_stale_revision_returns_409_with_canonical(client):
    body, headers = open_session(client)
    sid = body["session_id"]
    tid = seed_v2_task(client, sid, headers, key="k-create-0003")["task_id"]
    client.post(f"/v2/sessions/{sid}/tasks/{tid}/assign", headers=headers,
                json={"schema_version": 2, "target_agent_id": "developer",
                      "expected_revision": 0, "idempotency_key": "k-assign-a"})
    r = client.post(f"/v2/sessions/{sid}/tasks/{tid}/assign", headers=headers,
                    json={"schema_version": 2, "target_agent_id": "tester",
                          "expected_revision": 0, "idempotency_key": "k-assign-b"})
    assert r.status_code == 409
    assert r.json()["code"] == "REVISION_CONFLICT"
    assert r.json()["canonical_task"]["revision"] == 1


def test_invalid_payload_is_422_not_crash(client):
    body, headers = open_session(client)
    sid = body["session_id"]
    tid = seed_v2_task(client, sid, headers, key="k-create-0004")["task_id"]
    # priority out of range → validation error, safe fallback
    r = client.post(f"/v2/sessions/{sid}/tasks/{tid}/priority", headers=headers,
                    json={"schema_version": 2, "priority": 9, "expected_revision": 0,
                          "idempotency_key": "k-prio-bad"})
    assert r.status_code == 422


def test_diagram_endpoint_builds_auth_flow(client):
    body, headers = open_session(client)
    sid = body["session_id"]
    r = client.post(f"/v2/sessions/{sid}/diagrams", headers=headers,
                    json={"schema_version": 2, "template": "auth_flow",
                          "idempotency_key": "k-dia-0001"})
    assert r.status_code == 201
    assert r.json()["template"] == "auth_flow"
    assert len(r.json()["nodes"]) >= 1


def test_focus_endpoint_and_snapshot(client):
    body, headers = open_session(client)
    sid = body["session_id"]
    r = client.post(f"/v2/sessions/{sid}/focus", headers=headers,
                    json={"schema_version": 2, "mode": "do_not_disturb",
                          "idempotency_key": "k-focus-0001"})
    assert r.status_code == 200 and r.json()["mode"] == "do_not_disturb"
    snap = client.get(f"/v2/sessions/{sid}/snapshot", headers=headers).json()
    assert snap["focus"]["mode"] == "do_not_disturb"


def test_ws_reconnect_replay_is_identical(client):
    body, headers = open_session(client)
    sid, token = body["session_id"], body["token"]
    tid = seed_v2_task(client, sid, headers, key="k-create-ws01")["task_id"]
    client.post(f"/v2/sessions/{sid}/tasks/{tid}/assign", headers=headers,
                json={"schema_version": 2, "target_agent_id": "developer",
                      "expected_revision": 0, "idempotency_key": "k-assign-ws01"})
    client.post(f"/v2/sessions/{sid}/tasks/{tid}/priority", headers=headers,
                json={"schema_version": 2, "priority": 3, "expected_revision": 1,
                      "idempotency_key": "k-prio-ws01"})
    last = client.get(f"/v2/sessions/{sid}/snapshot", headers=headers).json()["last_seq"]
    assert last == 3  # task.created + task.assigned + task.priority_changed

    def read_all():
        with client.websocket_connect(
                f"/v2/sessions/{sid}/events?after_seq=0&token={token}") as ws:
            return _read_n(ws, last)

    first = read_all()
    second = read_all()
    assert [e["seq"] for e in first] == [1, 2, 3]
    assert first == second  # byte-identical replay → identical reconstructed state
    assert [e["type"] for e in first] == ["task.created", "task.assigned",
                                          "task.priority_changed"]


def test_v1_ws_never_receives_v2_events(client):
    """A v1 subscriber on the same session must only ever see v1 events."""
    body, headers = open_session(client)
    sid, token = body["session_id"], body["token"]
    tid = seed_v2_task(client, sid, headers, key="k-create-iso1")["task_id"]
    client.post(f"/v2/sessions/{sid}/tasks/{tid}/assign", headers=headers,
                json={"schema_version": 2, "target_agent_id": "developer",
                      "expected_revision": 0, "idempotency_key": "k-assign-iso1"})
    # v1 stream has only session.ready (seq 1); v2 events live in a separate log.
    with client.websocket_connect(f"/v1/sessions/{sid}/events?after_seq=0&token={token}") as ws:
        first = ws.receive_json()
    assert first["type"] == "session.ready"
    assert first["schema_version"] == 1


def test_handoff_created_once_over_shared_store_and_survives_reconnect(client):
    """Ticket exit criterion: load snapshot, receive exactly one handoff.created,
    disconnect, reconnect, identical state. Handoffs are minted by the backend
    service (not the client); we drive the service against the app's shared store."""
    from app.phase2.events import Phase2EventBus
    from app.phase2.handoffs import HandoffService
    from app.phase2.artifacts import ArtifactService

    body, headers = open_session(client)
    sid, token = body["session_id"], body["token"]
    tid = seed_v2_task(client, sid, headers, title="develop", agent_id="developer",
                       key="k-create-ho01")["task_id"]
    store = client.app.state.phase2_store

    async def mint():
        bus = Phase2EventBus(store)  # shares the same durable events_v2 store
        art = await ArtifactService(store, bus, "demo").create_artifact(
            sid, tid, "research_report", "blueprint",
            [{"page": 0, "title": "plan", "content": "ok", "line_refs": []}])
        # deliver the handoff twice to prove exactly-once at the event layer
        hs = HandoffService(store, bus, "demo")
        await hs.create(sid, "researcher", "developer", tid, art.artifact_id,
                        "blueprint", handoff_id="HX1")
        await hs.create(sid, "researcher", "developer", tid, art.artifact_id,
                        "blueprint", handoff_id="HX1")

    asyncio.run(mint())

    snap = client.get(f"/v2/sessions/{sid}/snapshot", headers=headers).json()
    assert len(snap["handoffs"]) == 1
    last = snap["last_seq"]

    def read_all():
        with client.websocket_connect(
                f"/v2/sessions/{sid}/events?after_seq=0&token={token}") as ws:
            return _read_n(ws, last)

    first = read_all()
    created = [e for e in first if e["type"] == "handoff.created"]
    assert len(created) == 1
    assert read_all() == first  # reconnect → identical
