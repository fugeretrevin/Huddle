"""Regression tests for the v1 / live-orchestrator defects found in the 2026-11-12
backend audit (docs/Engineering/_audit_backend.md). Each test names the defect it pins.

These tests never block on a live WebSocket read: they poll a bounded replay of the
durable event log, so a regression FAILS within the deadline instead of hanging the
suite waiting for an event that is never emitted.
"""
from __future__ import annotations

import time

import pytest
from fastapi.testclient import TestClient

from app.main import create_app
from app.settings import Settings
from app.workers import DeveloperWorker, TesterWorker
from app.workspace_guard import TestResult

PASS = TestResult(passed=3, failed=0, returncode=0, output="3 passed")


def _open(client):
    r = client.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local",
                                          "client": "quest"})
    assert r.status_code == 201, r.text
    body = r.json()
    return body["session_id"], body["token"], {"Authorization": f"Bearer {body['token']}"}


def _live_client(tmp_path) -> TestClient:
    return TestClient(create_app(Settings(
        db_path=":memory:", producer="live", producer_step_seconds=0.0,
        require_pairing=True, workspace_root=str(tmp_path), task_timeout_seconds=60)))


def _fake_tester(monkeypatch, *results: TestResult) -> None:
    """Make TesterWorker.run return the given results in order (no subprocess)."""
    queue = list(results)

    def run(self, guard, ws, timeout=90):
        return queue.pop(0)

    monkeypatch.setattr(TesterWorker, "run", run)


def _replay(client, sid, token, headers) -> list[dict]:
    """Every event persisted so far, read as a from-zero replay (never blocks: it
    reads exactly ``last_seq`` events, all of which already exist)."""
    last = client.get(f"/v1/sessions/{sid}/snapshot", headers=headers).json()["last_seq"]
    with client.websocket_connect(f"/v1/sessions/{sid}/events?after_seq=0&token={token}") as ws:
        return [ws.receive_json() for _ in range(last)]


def _events_until(client, sid, token, headers, done, what: str, timeout: float = 15.0):
    deadline = time.monotonic() + timeout
    events: list[dict] = []
    while time.monotonic() < deadline:
        events = _replay(client, sid, token, headers)
        if done(events):
            return events
        time.sleep(0.05)
    pytest.fail(f"timed out waiting for {what}; saw {[e['type'] for e in events]}")


def _has(type_: str, task_id: str | None = None):
    return lambda evs: any(e["type"] == type_ and (task_id is None or e.get("task_id") == task_id)
                           for e in evs)


def _start(client, headers, sid, key):
    r = client.post(f"/v1/sessions/{sid}/tasks", headers=headers,
                    json={"schema_version": 1, "task_template": "auth_demo",
                          "idempotency_key": key})
    assert r.status_code == 201, r.text
    return r.json()


def _decision_id(client, sid, token, headers) -> str:
    evs = _events_until(client, sid, token, headers, _has("decision.requested"),
                        "decision.requested")
    return next(e for e in evs if e["type"] == "decision.requested")["decision_id"]


def _approve(client, headers, sid, did, key):
    r = client.post(f"/v1/sessions/{sid}/decisions/{did}/respond", headers=headers,
                    json={"schema_version": 1, "action": "approve", "idempotency_key": key})
    assert r.status_code == 200, r.text
    return r.json()


def _agent_states(events, agent_id) -> list[str]:
    return [e["payload"]["state"] for e in events
            if e["type"] == "agent.status_changed" and e["agent_id"] == agent_id]


# ── D7: the create-task response uses the key the client DTO reads ──
def test_create_task_response_carries_template(client):
    sid, _, headers = _open(client)
    created = _start(client, headers, sid, "quest-create-tmpl0001")
    assert created["template"] == created["task_template"] == "auth_demo"


# ── D8: an idempotency key from another request is not a decision replay ──
def test_respond_with_key_from_another_request_is_409_not_a_replay(client):
    sid, token, headers = _open(client)
    create_key = "quest-create-reuse001"
    _start(client, headers, sid, create_key)
    did = _decision_id(client, sid, token, headers)

    url = f"/v1/sessions/{sid}/decisions/{did}/respond"
    # raise_server_exceptions is on, so the old behaviour (a 500 from parsing the
    # task-create record as a resolution) surfaces as an exception here.
    r = client.post(url, headers=headers,
                    json={"schema_version": 1, "action": "approve", "idempotency_key": create_key})
    assert r.status_code == 409
    assert r.json()["error_code"] == "IDEMPOTENCY_KEY_REUSE"

    # the decision was NOT resolved by the rejected request; a proper key still applies
    assert _approve(client, headers, sid, did, "resolve-reuse-fresh1")["applied"] is True


# ── D4: a run whose tests did not pass is task.failed, never task.completed ──
@pytest.mark.parametrize("bad", [
    TestResult(passed=4, failed=2, returncode=1, output="4 passed, 2 failed"),
    TestResult(passed=0, failed=0, returncode=2, output="INTERNALERROR"),      # crashed run
    TestResult(passed=0, failed=0, returncode=-1, output="[timeout]", timed_out=True),
], ids=["failed-tests", "crashed-no-summary", "timed-out"])
def test_live_failed_run_emits_task_failed_not_completed(tmp_path, monkeypatch, bad):
    _fake_tester(monkeypatch, PASS, bad, bad)  # baseline passes; post-approval + 1 retest fail
    with _live_client(tmp_path) as client:
        sid, token, headers = _open(client)
        parent = _start(client, headers, sid, "live-create-fail0001")["task_id"]
        oauth = f"{parent}-oauth"
        _approve(client, headers, sid, _decision_id(client, sid, token, headers),
                 "live-approve-fail01")
        ended = lambda evs: _has("task.failed", oauth)(evs) or _has("task.completed", oauth)(evs)
        events = _events_until(client, sid, token, headers, ended, "the oauth task to end")

        outcome = [e for e in events if e.get("task_id") == oauth
                   and e["type"] in ("task.completed", "task.failed")]
        assert [e["type"] for e in outcome] == ["task.failed"]
        assert outcome[0]["payload"]["source_mode"] == "live"
        assert _agent_states(events, "developer")[-1] == "error"
        assert _agent_states(events, "tester")[-1] == "error"  # not left "testing" forever
        art = next(e for e in events if e["type"] == "artifact.created")["payload"]
        assert "did not pass" in art["summary"] and art["source_mode"] == "live"

        tasks = {t["task_id"]: t["status"] for t in
                 client.get(f"/v1/sessions/{sid}/snapshot", headers=headers).json()["tasks"]}
        assert tasks[oauth] == "failed"


def test_live_passing_run_still_completes_and_settles_the_tester(tmp_path, monkeypatch):
    _fake_tester(monkeypatch, PASS, TestResult(6, 0, 0, "6 passed"))
    with _live_client(tmp_path) as client:
        sid, token, headers = _open(client)
        parent = _start(client, headers, sid, "live-create-pass0001")["task_id"]
        oauth = f"{parent}-oauth"
        _approve(client, headers, sid, _decision_id(client, sid, token, headers),
                 "live-approve-pass01")
        ended = lambda evs: _has("task.failed", oauth)(evs) or _has("task.completed", oauth)(evs)
        events = _events_until(client, sid, token, headers, ended, "the oauth task to end")
        assert not [e for e in events if e["type"] == "task.failed"]
        assert _agent_states(events, "tester")[-1] == "complete"
        assert _agent_states(events, "developer")[-1] == "complete"


def test_live_failed_baseline_is_reported_as_failed(tmp_path, monkeypatch):
    _fake_tester(monkeypatch, TestResult(2, 1, 1, "2 passed, 1 failed"))
    with _live_client(tmp_path) as client:
        sid, token, headers = _open(client)
        parent = _start(client, headers, sid, "live-create-base0001")["task_id"]
        events = _events_until(client, sid, token, headers, _has("decision.requested"),
                               "decision.requested")
        test_task = f"{parent}-test"
        assert [e["type"] for e in events if e.get("task_id") == test_task
                and e["type"] in ("task.completed", "task.failed")] == ["task.failed"]
        assert _agent_states(events, "tester")[-1] == "error"


# ── D5: a worker exception is reported, not swallowed by the background task ──
def test_live_worker_exception_is_reported_and_sanitized(tmp_path, monkeypatch):
    _fake_tester(monkeypatch, PASS)

    def boom(self, guard, ws):
        raise RuntimeError(r"write failed at C:\Users\someone\work\oauth.py token=abc123supersecret")

    monkeypatch.setattr(DeveloperWorker, "implement_oauth", boom)
    with _live_client(tmp_path) as client:
        sid, token, headers = _open(client)
        parent = _start(client, headers, sid, "live-create-boom0001")["task_id"]
        _approve(client, headers, sid, _decision_id(client, sid, token, headers),
                 "live-approve-boom01")
        events = _events_until(client, sid, token, headers, _has("task.failed", parent),
                               "the run failure to be reported", timeout=8.0)

        warning = next(e for e in events if e["type"] == "system.warning")["payload"]
        assert warning["code"] == "RUN_FAILED" and warning["source_mode"] == "live"
        assert "RuntimeError" in warning["message"]
        failed = next(e for e in events if e["type"] == "task.failed")["payload"]
        for leaked in ("C:\\Users", "someone", "abc123supersecret"):
            assert leaked not in warning["message"]
            assert leaked not in failed["result_summary"]
        assert _agent_states(events, "developer")[-1] == "error"  # not left "coding" forever

        snap = client.get(f"/v1/sessions/{sid}/snapshot", headers=headers).json()
        tasks = {t["task_id"]: t["status"] for t in snap["tasks"]}
        assert tasks[parent] == "failed" and tasks[f"{parent}-oauth"] == "failed"
        assert tasks[f"{parent}-research"] == "completed"  # finished work is not rewritten
        assert all(a["state"] in ("idle", "complete", "error") for a in snap["agents"])
