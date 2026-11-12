"""Regression tests for the second backend audit pass (integration/audit-repair).
Each test names the defect it pins. Like test_audit_fixes.py, nothing here blocks on a
live WebSocket read: state is polled through bounded replays / snapshots."""
from __future__ import annotations

import asyncio
import time

import pytest
from fastapi.testclient import TestClient

from app.main import create_app
from app.orchestrator import Orchestrator
from app.settings import Settings
from app.workers import TesterWorker
from app.workspace_guard import TestResult, WorkspaceError, WorkspaceGuard
from tests.conftest import p2_fresh

PASS = TestResult(passed=3, failed=0, returncode=0, output="3 passed")
TERMINAL = ("completed", "failed", "cancelled")


# ─────────────────────── helpers ───────────────────────
def _open(client):
    r = client.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local",
                                          "client": "quest"})
    assert r.status_code == 201, r.text
    b = r.json()
    return b["session_id"], b["token"], {"Authorization": f"Bearer {b['token']}"}


def _snapshot(client, sid, headers) -> dict:
    r = client.get(f"/v1/sessions/{sid}/snapshot", headers=headers)
    assert r.status_code == 200, r.text
    return r.json()


def _replay(client, sid, token, headers) -> list[dict]:
    last = _snapshot(client, sid, headers)["last_seq"]
    with client.websocket_connect(f"/v1/sessions/{sid}/events?after_seq=0&token={token}") as ws:
        return [ws.receive_json() for _ in range(last)]


def _wait(pred, what: str, timeout: float = 15.0):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        got = pred()
        if got:
            return got
        time.sleep(0.05)
    pytest.fail(f"timed out waiting for {what}")


def _start(client, headers, sid, key):
    return client.post(f"/v1/sessions/{sid}/tasks", headers=headers,
                       json={"schema_version": 1, "task_template": "auth_demo",
                             "idempotency_key": key})


def _pending_decision(client, sid, headers) -> str:
    def find():
        for d in _snapshot(client, sid, headers)["decisions"]:
            if d["state"] == "requested":
                return d["decision_id"]
        return None
    return _wait(find, "a pending decision")


def _respond(client, headers, sid, did, action, key):
    return client.post(f"/v1/sessions/{sid}/decisions/{did}/respond", headers=headers,
                       json={"schema_version": 1, "action": action, "idempotency_key": key})


def _task_status(client, sid, headers, task_id) -> str:
    return {t["task_id"]: t["status"] for t in _snapshot(client, sid, headers)["tasks"]}[task_id]


def _wait_run_over(client, sid, headers, parent):
    _wait(lambda: _task_status(client, sid, headers, parent) in TERMINAL, "the run to end")


def _live_app(tmp_path, db_path=":memory:"):
    return create_app(Settings(
        db_path=db_path, producer="live", producer_step_seconds=0.0, require_pairing=True,
        workspace_root=str(tmp_path / "ws"), task_timeout_seconds=60))


def _demo_app(db_path=":memory:"):
    return create_app(Settings(db_path=db_path, producer_step_seconds=0.0,
                               require_pairing=True, producer_source_mode="demo"))


def _fake_tester(monkeypatch, *results: TestResult) -> None:
    queue = list(results)
    monkeypatch.setattr(TesterWorker, "run", lambda self, guard, ws, timeout=90: queue.pop(0))


# ── R1: a rejected / changes-requested branch reports its REAL terminal state ──
@pytest.mark.parametrize("producer,action", [
    ("demo", "reject"), ("live", "reject"), ("live", "request_changes")])
def test_cancelled_branch_event_carries_cancelled_task_state(tmp_path, monkeypatch,
                                                             producer, action):
    _fake_tester(monkeypatch, PASS)
    app = _live_app(tmp_path) if producer == "live" else _demo_app()
    with TestClient(app) as client:
        sid, token, headers = _open(client)
        parent = _start(client, headers, sid, "repair-create-rej001").json()["task_id"]
        oauth = f"{parent}-oauth"
        did = _pending_decision(client, sid, headers)
        assert _respond(client, headers, sid, did, action, "repair-resolve-rej01").status_code == 200
        _wait_run_over(client, sid, headers, parent)

        events = _replay(client, sid, token, headers)
        (done,) = [e for e in events if e.get("task_id") == oauth
                   and e["type"] in ("task.completed", "task.failed")]
        # the event stream and the snapshot now tell the same story about this task
        assert done["payload"]["task_state"] == "cancelled"
        assert "artifact_id" not in done["payload"]
        assert done["payload"]["source_mode"] == producer
        assert _task_status(client, sid, headers, oauth) == "cancelled"
        assert not [e for e in events if e["type"] == "artifact.created"]  # gate held


def test_terminal_task_events_carry_task_state_on_success(client):
    sid, token, headers = _open(client)
    parent = _start(client, headers, sid, "repair-create-ok0001").json()["task_id"]
    did = _pending_decision(client, sid, headers)
    _respond(client, headers, sid, did, "approve", "repair-resolve-ok001")
    _wait_run_over(client, sid, headers, parent)
    ends = [e for e in _replay(client, sid, token, headers) if e["type"] == "task.completed"]
    assert ends and all(e["payload"]["task_state"] == "completed" for e in ends)


# ── R2: one run per session; a second concurrent run is refused, not interleaved ──
def test_second_task_is_refused_while_a_run_is_in_progress(client):
    sid, token, headers = _open(client)
    first = _start(client, headers, sid, "repair-create-run001")
    assert first.status_code == 201
    parent = first.json()["task_id"]
    did = _pending_decision(client, sid, headers)

    second = _start(client, headers, sid, "repair-create-run002")
    assert second.status_code == 409 and second.json()["error_code"] == "RUN_IN_PROGRESS"
    # an exact replay of the FIRST request is still the canonical result, not a 409
    again = _start(client, headers, sid, "repair-create-run001")
    assert again.status_code == 201 and again.json() == first.json()
    assert len([t for t in _snapshot(client, sid, headers)["tasks"]
                if t["parent_task_id"] is None]) == 1

    _respond(client, headers, sid, did, "approve", "repair-resolve-run01")
    _wait_run_over(client, sid, headers, parent)
    # the refused key was not burned, and a new run is accepted once the first ended
    assert _start(client, headers, sid, "repair-create-run002").status_code == 201


def test_task_create_with_a_key_used_by_another_route_is_409(client):
    sid, token, headers = _open(client)
    parent = _start(client, headers, sid, "repair-create-key001").json()["task_id"]
    did = _pending_decision(client, sid, headers)
    _respond(client, headers, sid, did, "approve", "repair-shared-key-01")
    _wait_run_over(client, sid, headers, parent)
    # previously this echoed the decision resolution back as if it were a created task
    r = _start(client, headers, sid, "repair-shared-key-01")
    assert r.status_code == 409 and r.json()["error_code"] == "IDEMPOTENCY_KEY_REUSE"


# ── R3: backend restart with a decision pending ──
@pytest.mark.parametrize("producer", ["demo", "live"])
def test_restart_with_pending_decision_reports_the_run_failed(tmp_path, monkeypatch, producer):
    _fake_tester(monkeypatch, PASS, PASS)
    db = str(tmp_path / "restart.sqlite3")
    make = (lambda: _live_app(tmp_path, db)) if producer == "live" else (lambda: _demo_app(db))

    with TestClient(make()) as client:  # process #1: run reaches the approval gate
        sid, token, headers = _open(client)
        parent = _start(client, headers, sid, "repair-create-rst001").json()["task_id"]
        did = _pending_decision(client, sid, headers)
        seq_before = _snapshot(client, sid, headers)["last_seq"]

    with TestClient(make()) as client:  # process #2: same database, fresh memory
        snap = _snapshot(client, sid, headers)  # the session + token survive
        tasks = {t["task_id"]: t["status"] for t in snap["tasks"]}
        assert tasks[parent] == "failed"
        assert tasks[f"{parent}-research"] == "failed"   # was awaiting_approval
        assert tasks[f"{parent}-oauth"] == "failed"      # was blocked; never executed
        assert tasks[f"{parent}-test"] == "completed"    # finished work is not rewritten
        assert all(a["state"] in ("idle", "complete", "error") for a in snap["agents"])
        assert {d["decision_id"]: d["state"] for d in snap["decisions"]}[did] == "expired"

        events = _replay(client, sid, token, headers)
        assert [e["seq"] for e in events] == list(range(1, len(events) + 1))  # still dense
        new = [e for e in events if e["seq"] > seq_before]
        warn = next(e for e in new if e["type"] == "system.warning")["payload"]
        assert warn["code"] == "RUN_INTERRUPTED" and warn["task_id"] == parent
        res = next(e for e in new if e["type"] == "decision.resolved")
        assert res["decision_id"] == did and res["payload"]["state"] == "expired"
        assert [e["task_id"] for e in new if e["type"] == "task.failed"][-1] == parent
        assert not [e for e in new if e["type"] in ("artifact.created", "task.completed")]

        # the orphaned decision can no longer be "approved" into nothing
        r = _respond(client, headers, sid, did, "approve", "repair-resolve-rst01")
        assert r.status_code == 409 and r.json()["error_code"] == "DECISION_ALREADY_RESOLVED"
        assert _snapshot(client, sid, headers)["last_seq"] == len(events)
        # and the session is usable again
        assert _start(client, headers, sid, "repair-create-rst002").status_code == 201
        _pending_decision(client, sid, headers)

    with TestClient(make()) as client:  # process #3: each interrupted run is reported once
        events = _replay(client, sid, token, headers)
        assert [e["seq"] for e in events] == list(range(1, len(events) + 1))
        warns = [e for e in events if e["type"] == "system.warning"]
        assert len(warns) == 2 and len({w["payload"]["task_id"] for w in warns}) == 2


def test_restart_does_not_touch_finished_runs(tmp_path):
    db = str(tmp_path / "done.sqlite3")
    with TestClient(_demo_app(db)) as client:
        sid, token, headers = _open(client)
        parent = _start(client, headers, sid, "repair-create-fin001").json()["task_id"]
        _respond(client, headers, sid, _pending_decision(client, sid, headers), "approve",
                 "repair-resolve-fin01")
        _wait_run_over(client, sid, headers, parent)
        before = _snapshot(client, sid, headers)
    with TestClient(_demo_app(db)) as client:
        assert _snapshot(client, sid, headers) == before  # no new events, same states


# ── R4: a scripted timeline that raises is reported, not swallowed ──
def test_demo_producer_exception_is_reported(client, monkeypatch):
    async def boom(self, *a, **k):
        raise RuntimeError("scripted branch blew up")

    monkeypatch.setattr(Orchestrator, "_apply_branch", boom)
    sid, token, headers = _open(client)
    parent = _start(client, headers, sid, "repair-create-boom01").json()["task_id"]
    _respond(client, headers, sid, _pending_decision(client, sid, headers), "approve",
             "repair-resolve-boom1")
    _wait_run_over(client, sid, headers, parent)
    snap = _snapshot(client, sid, headers)
    assert _task_status(client, sid, headers, parent) == "failed"
    assert all(a["state"] in ("idle", "complete", "error") for a in snap["agents"])
    events = _replay(client, sid, token, headers)
    warn = next(e for e in events if e["type"] == "system.warning")["payload"]
    assert warn["code"] == "RUN_FAILED" and warn["source_mode"] == "demo"


# ── R5: a closed event socket releases its bus subscription ──
def test_event_pump_returns_when_the_client_disconnects_while_idle():
    """Under a real ASGI server a client close is only visible through ws.receive().
    The old loop sat in queue.get() and never looked, so the handler (and its bus
    subscription) outlived the client until the session's next event, if any.
    (Starlette's TestClient cancels the handler on close, which hid this; hence a fake
    socket here. Verified against real uvicorn during the audit.)"""
    from types import SimpleNamespace

    from app.ws_stream import pump_events

    class FakeSocket:
        def __init__(self):
            self.sent: list[dict] = []
            self.closed = asyncio.Event()

        async def send_json(self, data):
            self.sent.append(data)

        async def receive(self):
            await self.closed.wait()
            return {"type": "websocket.disconnect", "code": 1000}

    async def body():
        ws, q = FakeSocket(), asyncio.Queue()
        replay = [{"seq": 1}, {"seq": 2}, {"seq": 3}]
        pump = asyncio.create_task(pump_events(ws, q, replay, 1, lambda env: {"seq": env.seq}))
        q.put_nowait(SimpleNamespace(seq=3))   # overlap with the replay: not re-sent
        q.put_nowait(SimpleNamespace(seq=4))
        await asyncio.sleep(0.05)
        assert [e["seq"] for e in ws.sent] == [2, 3, 4]  # after_seq honoured, dense, no dupes
        assert not pump.done()                 # idle, waiting for events
        ws.closed.set()                        # client goes away; NO further event arrives
        await asyncio.wait_for(pump, timeout=2.0)
    asyncio.run(body())


@pytest.mark.parametrize("version,bus_attr", [("v1", "bus"), ("v2", "phase2_bus")])
def test_event_socket_subscribes_and_unsubscribes(client, version, bus_attr):
    sid, token, headers = _open(client)
    bus = getattr(client.app.state, bus_attr)
    with client.websocket_connect(f"/{version}/sessions/{sid}/events?after_seq=0&token={token}") as ws:
        if version == "v1":
            assert ws.receive_json()["type"] == "session.ready"
        _wait(lambda: len(bus._subscribers.get(sid, ())) == 1, "the subscription", 5.0)
    _wait(lambda: len(bus._subscribers.get(sid, ())) == 0, "the unsubscribe", 5.0)


# ── R6: sandbox hardening ──
def test_sandboxed_tests_cannot_read_backend_credentials(tmp_path, monkeypatch):
    monkeypatch.setenv("MUSEDESK_PROVIDER_API_KEY", "sk-should-not-leak")
    monkeypatch.setenv("SOME_SERVICE_TOKEN", "tok-should-not-leak")
    guard = WorkspaceGuard(str(tmp_path))
    ws = guard.create_workspace("ses-env")
    guard.write_file(ws, "test_env_probe.py",
                     "import os\n\n"
                     "def test_no_credentials_in_env():\n"
                     "    leaked = [k for k in os.environ if k.startswith('MUSEDESK_')\n"
                     "              or 'TOKEN' in k.upper() or 'KEY' in k.upper()]\n"
                     "    assert leaked == []\n"
                     "    assert os.environ.get('PYTHONDONTWRITEBYTECODE') == '1'\n")
    res = guard.run_tests(ws, timeout=60)
    assert res.returncode == 0 and res.failed == 0 and res.passed == 4, res.output


def test_test_run_timeout_is_reported_as_timed_out(tmp_path):
    guard = WorkspaceGuard(str(tmp_path))
    ws = guard.create_workspace("ses-slow")
    guard.write_file(ws, "test_slow.py",
                     "import time\n\ndef test_hangs():\n    time.sleep(60)\n")
    started = time.monotonic()
    res = guard.run_tests(ws, timeout=3)
    assert res.timed_out is True and res.returncode == -1
    assert isinstance(res.output, str) and res.output.endswith("[timeout]")
    assert time.monotonic() - started < 30  # the child was killed, not waited out


def test_drive_qualified_and_backslash_paths_are_rejected_as_workspace_errors(tmp_path):
    guard = WorkspaceGuard(str(tmp_path))
    ws = guard.create_workspace("ses-paths")
    for bad in ["Q:evil.py", "Q:\\evil.py", "..\\escape.py", "sub\\..\\..\\escape.py",
                "\\\\host\\share\\evil.py"]:
        with pytest.raises(WorkspaceError):
            guard.write_file(ws, bad, "x = 1")
    assert not (tmp_path / "escape.py").exists()


# ── R7: a handoff cannot reference another session's artifact ──
def test_handoff_rejects_another_sessions_artifact():
    from app.phase2.errors import CommandError

    async def body():
        store, bus, svc = p2_fresh()
        mine = svc["graph"].create_task("ses-a", "A task", None, 1, [], None)
        theirs = svc["graph"].create_task("ses-b", "B task", None, 1, [], None)
        page = [{"page": 0, "title": "p", "content": "ok", "line_refs": []}]
        foreign = await svc["artifacts"].create_artifact(
            "ses-b", theirs.task_id, "research_report", "plan", page)
        with pytest.raises(CommandError) as exc:
            await svc["handoffs"].create("ses-a", "researcher", "developer", mine.task_id,
                                         foreign.artifact_id, "blueprint")
        assert exc.value.code == "UNKNOWN_ARTIFACT"
        assert store.list_handoffs_v2("ses-a") == [] and store.last_seq_v2("ses-a") == 0
    asyncio.run(body())
