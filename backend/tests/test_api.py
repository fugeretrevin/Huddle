from __future__ import annotations

from tests.conftest import drain_until, open_session


def test_health(client):
    r = client.get("/health")
    assert r.status_code == 200
    body = r.json()
    assert body["status"] == "ok"
    assert body["schema_version"] == 1


def test_session_and_snapshot(client):
    body, headers = open_session(client)
    assert body["session_id"].startswith("ses-")
    assert body["last_seq"] >= 1  # session.ready emitted
    r = client.get(f"/v1/sessions/{body['session_id']}/snapshot", headers=headers)
    assert r.status_code == 200
    snap = r.json()
    assert {a["agent_id"] for a in snap["agents"]} == {"researcher", "developer", "tester"}
    assert snap["mode"] == "live_local"


def test_auth_required_and_scoped(client):
    body, headers = open_session(client)
    sid = body["session_id"]
    # missing token
    assert client.get(f"/v1/sessions/{sid}/snapshot").status_code == 401
    # wrong token
    assert client.get(f"/v1/sessions/{sid}/snapshot",
                      headers={"Authorization": "Bearer nope"}).status_code == 401
    # a second session's token cannot read the first session
    body2, headers2 = open_session(client)
    assert client.get(f"/v1/sessions/{sid}/snapshot", headers=headers2).status_code == 401


def test_event_seq_monotonic_and_no_gaps(client):
    body, headers = open_session(client)
    sid = body["session_id"]
    token = body["token"]
    with client.websocket_connect(f"/v1/sessions/{sid}/events?after_seq=0&token={token}") as ws:
        # session.ready already persisted (seq 1); replayed on connect.
        first = ws.receive_json()
        assert first["type"] == "session.ready"
        assert first["seq"] == 1
        r = client.post(f"/v1/sessions/{sid}/tasks", headers=headers,
                        json={"schema_version": 1, "task_template": "auth_demo",
                              "idempotency_key": "quest-create-aaaaaaaa"})
        assert r.status_code == 201
        events = drain_until(ws, "decision.requested")
    seqs = [first["seq"]] + [e["seq"] for e in events]
    assert seqs == list(range(1, len(seqs) + 1))  # strictly 1..N, no gaps/dupes


def test_decision_requested_payload_and_source_mode(client):
    body, headers = open_session(client)
    sid, token = body["session_id"], body["token"]
    with client.websocket_connect(f"/v1/sessions/{sid}/events?after_seq=1&token={token}") as ws:
        client.post(f"/v1/sessions/{sid}/tasks", headers=headers,
                    json={"schema_version": 1, "task_template": "auth_demo",
                          "idempotency_key": "quest-create-bbbbbbbb"})
        events = drain_until(ws, "decision.requested")
    dr = events[-1]
    p = dr["payload"]
    assert dr["agent_id"] == "researcher"
    for key in ("title", "summary", "rationale", "allowed_actions", "risk_level", "source_mode"):
        assert key in p
    assert p["source_mode"] == "demo"
    assert p["priority"] == "blocking"


def _drive_to_decision(client, headers, sid, token, key):
    with client.websocket_connect(f"/v1/sessions/{sid}/events?after_seq=1&token={token}") as ws:
        client.post(f"/v1/sessions/{sid}/tasks", headers=headers,
                    json={"schema_version": 1, "task_template": "auth_demo",
                          "idempotency_key": key})
        events = drain_until(ws, "decision.requested")
    return events[-1]["decision_id"]


def test_decision_approve_then_replay_idempotent(client):
    body, headers = open_session(client)
    sid, token = body["session_id"], body["token"]
    did = _drive_to_decision(client, headers, sid, token, "quest-create-cccccccc")
    url = f"/v1/sessions/{sid}/decisions/{did}/respond"

    r1 = client.post(url, headers=headers,
                     json={"schema_version": 1, "action": "approve",
                           "idempotency_key": "resolve-key-0001"})
    assert r1.status_code == 200
    assert r1.json()["applied"] is True
    assert r1.json()["state"] == "approved"

    # exact replay of same key: canonical, not re-applied
    r2 = client.post(url, headers=headers,
                     json={"schema_version": 1, "action": "approve",
                           "idempotency_key": "resolve-key-0001"})
    assert r2.status_code == 200
    assert r2.json()["applied"] is False
    assert r2.json()["state"] == "approved"


def test_conflicting_second_action_is_409(client):
    body, headers = open_session(client)
    sid, token = body["session_id"], body["token"]
    did = _drive_to_decision(client, headers, sid, token, "quest-create-dddddddd")
    url = f"/v1/sessions/{sid}/decisions/{did}/respond"

    client.post(url, headers=headers,
                json={"schema_version": 1, "action": "approve",
                      "idempotency_key": "resolve-key-approve"})
    r = client.post(url, headers=headers,
                    json={"schema_version": 1, "action": "reject",
                          "idempotency_key": "resolve-key-reject"})
    assert r.status_code == 409
    assert r.json()["error_code"] == "DECISION_ALREADY_RESOLVED"
    assert r.json()["canonical"]["action"] == "approve"


def test_same_action_different_key_is_safe(client):
    body, headers = open_session(client)
    sid, token = body["session_id"], body["token"]
    did = _drive_to_decision(client, headers, sid, token, "quest-create-eeeeeeee")
    url = f"/v1/sessions/{sid}/decisions/{did}/respond"
    a = client.post(url, headers=headers,
                    json={"schema_version": 1, "action": "approve",
                          "idempotency_key": "key-one-aaaa"})
    b = client.post(url, headers=headers,
                    json={"schema_version": 1, "action": "approve",
                          "idempotency_key": "key-two-bbbb"})
    assert a.json()["applied"] is True
    assert b.status_code == 200 and b.json()["applied"] is False


def test_approve_produces_artifact_and_completion(client):
    body, headers = open_session(client)
    sid, token = body["session_id"], body["token"]
    with client.websocket_connect(f"/v1/sessions/{sid}/events?after_seq=1&token={token}") as ws:
        client.post(f"/v1/sessions/{sid}/tasks", headers=headers,
                    json={"schema_version": 1, "task_template": "auth_demo",
                          "idempotency_key": "quest-create-ffffffff"})
        pre = drain_until(ws, "decision.requested")
        did = pre[-1]["decision_id"]
        client.post(f"/v1/sessions/{sid}/decisions/{did}/respond", headers=headers,
                    json={"schema_version": 1, "action": "approve",
                          "idempotency_key": "resolve-approve-xyz1"})
        post = drain_until(ws, "artifact.created")
    art = post[-1]["payload"]
    assert art["source_mode"] == "demo"
    assert art["tests_passed"] == 5
    assert "files_changed" in art


def test_reconnect_replay_is_gapless(client):
    body, headers = open_session(client)
    sid, token = body["session_id"], body["token"]
    did = _drive_to_decision(client, headers, sid, token, "quest-create-99999999")
    client.post(f"/v1/sessions/{sid}/decisions/{did}/respond", headers=headers,
                json={"schema_version": 1, "action": "approve",
                      "idempotency_key": "resolve-approve-rc01"})
    # fresh client reconnects from scratch and replays whole history
    with client.websocket_connect(f"/v1/sessions/{sid}/events?after_seq=0&token={token}") as ws:
        got = []
        import queue
        try:
            for _ in range(200):
                got.append(ws.receive_json())
                if got[-1]["type"] == "task.completed" and \
                        got[-1]["payload"].get("artifact_id"):
                    break
        except queue.Empty:
            pass
    seqs = [e["seq"] for e in got]
    assert seqs == sorted(set(seqs))  # no duplicates, ascending
    assert seqs[0] == 1
    types = {e["type"] for e in got}
    assert "session.ready" in types and "decision.resolved" in types
