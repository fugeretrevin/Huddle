"""Board -> pipeline bridge: an executable task CARD launches the real run and reflects it.

Every test drives the real app through TestClient. Nothing blocks on a live WebSocket
read: state is polled through snapshots and bounded event replays.
"""
from __future__ import annotations

import time

import pytest
from fastapi.testclient import TestClient

from app.main import create_app
from app.settings import Settings
from app.workers import (
    CHANGES_REQUESTED_MESSAGE,
    DeveloperWorker,
    RESEARCH_PROVENANCE_LABEL,
    TesterWorker,
)
from app.workspace_guard import TestResult

PASS = TestResult(passed=3, failed=0, returncode=0, output="3 passed")
FAIL = TestResult(passed=2, failed=1, returncode=1, output="2 passed, 1 failed")
PRODUCERS = ["demo", "live"]
TERMINAL = ("completed", "failed", "cancelled")


# ─────────────────────── helpers ───────────────────────
def _app(producer: str, tmp_path, db_path: str = ":memory:"):
    if producer == "live":
        return create_app(Settings(
            db_path=db_path, producer="live", producer_step_seconds=0.0,
            require_pairing=True, workspace_root=str(tmp_path / "ws"),
            task_timeout_seconds=60))
    return create_app(Settings(db_path=db_path, producer_step_seconds=0.0,
                               require_pairing=True, producer_source_mode="demo"))


def _fake_tester(monkeypatch, *results: TestResult) -> None:
    queue = list(results)
    monkeypatch.setattr(TesterWorker, "run", lambda self, guard, ws, timeout=90: queue.pop(0))


def _open(client):
    r = client.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local",
                                          "client": "quest"})
    assert r.status_code == 201, r.text
    b = r.json()
    return b["session_id"], b["token"], {"Authorization": f"Bearer {b['token']}"}


def _wait(pred, what: str, timeout: float = 30.0):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        got = pred()
        if got:
            return got
        time.sleep(0.05)
    pytest.fail(f"timed out waiting for {what}")


def _snap1(client, sid, h) -> dict:
    r = client.get(f"/v1/sessions/{sid}/snapshot", headers=h)
    assert r.status_code == 200, r.text
    return r.json()


def _snap2(client, sid, h) -> dict:
    r = client.get(f"/v2/sessions/{sid}/snapshot", headers=h)
    assert r.status_code == 200, r.text
    return r.json()


def _card(client, sid, h, card_id) -> dict:
    return {t["task_id"]: t for t in _snap2(client, sid, h)["tasks"]}[card_id]


def _events1(client, sid, token, h) -> list[dict]:
    last = _snap1(client, sid, h)["last_seq"]
    with client.websocket_connect(f"/v1/sessions/{sid}/events?after_seq=0&token={token}") as ws:
        return [ws.receive_json() for _ in range(last)]


def _events2(client, sid, token, h) -> list[dict]:
    last = _snap2(client, sid, h)["last_seq"]
    with client.websocket_connect(f"/v2/sessions/{sid}/events?after_seq=0&token={token}") as ws:
        return [ws.receive_json() for _ in range(last)]


def _create(client, sid, h, key, *, template="auth_demo", title="Add OAuth login", **extra):
    body = {"schema_version": 2, "title": title, "priority": 2, "idempotency_key": key, **extra}
    if template is not None:
        body["task_template"] = template
    return client.post(f"/v2/sessions/{sid}/tasks", headers=h, json=body)


def _assign(client, sid, h, card, agent, key, revision=None):
    return client.post(
        f"/v2/sessions/{sid}/tasks/{card['task_id']}/assign", headers=h,
        json={"schema_version": 2, "target_agent_id": agent,
              "expected_revision": card["revision"] if revision is None else revision,
              "idempotency_key": key})


def _cmd(client, sid, h, card_id, verb, revision, key):
    return client.post(f"/v2/sessions/{sid}/tasks/{card_id}/{verb}", headers=h,
                       json={"schema_version": 2, "expected_revision": revision,
                             "idempotency_key": key})


def _pending(client, sid, h) -> str:
    def find():
        for d in _snap1(client, sid, h)["decisions"]:
            if d["state"] == "requested":
                return d["decision_id"]
        return None
    return _wait(find, "a pending decision")


def _respond(client, sid, h, did, action, key):
    return client.post(f"/v1/sessions/{sid}/decisions/{did}/respond", headers=h,
                       json={"schema_version": 1, "action": action, "idempotency_key": key})


def _wait_card(client, sid, h, card_id, states=TERMINAL) -> dict:
    return _wait(lambda: (lambda c: c if c["task_state"] in states else None)(
        _card(client, sid, h, card_id)), f"card to reach {states}")


def _runs(client, sid, h) -> list[dict]:
    return [t for t in _snap1(client, sid, h)["tasks"] if t["parent_task_id"] is None]


def _launch(client, sid, h, key="bridge-create-000001"):
    """Create an executable card, assign it to the researcher, wait for the decision."""
    card = _create(client, sid, h, key).json()
    started = _assign(client, sid, h, card, "researcher", key + "-assign")
    assert started.status_code == 200, started.text
    return started.json(), _pending(client, sid, h)


# ─────────────────────── the main path ───────────────────────
@pytest.mark.parametrize("producer", PRODUCERS)
def test_assigning_an_executable_card_to_the_researcher_runs_the_pipeline(tmp_path, producer):
    """Real workers in live mode (real files, real pytest): no fakes on this path."""
    with TestClient(_app(producer, tmp_path)) as client:
        sid, token, h = _open(client)

        created = _create(client, sid, h, "bridge-create-main01")
        assert created.status_code == 201, created.text
        card = created.json()
        assert card["task_template"] == "auth_demo" and card["executable"] is True
        assert card["task_state"] == "queued" and card["revision"] == 0
        assert card["run_task_id"] is None
        assert _runs(client, sid, h) == []          # creating a card starts nothing

        started = _assign(client, sid, h, card, "researcher", "bridge-assign-main01")
        assert started.status_code == 200, started.text
        running = started.json()
        assert running["task_state"] == "running" and running["agent_id"] == "researcher"
        assert running["revision"] == 2 and running["run_task_id"]

        # replaying the same key returns the same result and starts nothing
        again = _assign(client, sid, h, card, "researcher", "bridge-assign-main01")
        assert again.status_code == 200 and again.json() == running
        (run,) = _runs(client, sid, h)
        assert run["task_id"] == running["run_task_id"]
        assert run["board_task_id"] == card["task_id"] and run["template"] == "auth_demo"

        did = _pending(client, sid, h)
        waiting = _wait_card(client, sid, h, card["task_id"], ("awaiting_approval",))
        assert waiting["revision"] == 3

        r = _respond(client, sid, h, did, "approve", "bridge-approve-main1")
        assert r.status_code == 200 and r.json()["applied"] is True
        done = _wait_card(client, sid, h, card["task_id"])
        assert done["task_state"] == "completed" and done["revision"] == 5
        _wait(lambda: _runs(client, sid, h)[0]["status"] in TERMINAL, "the run to end")

        # approval idempotency holds for a card-launched run
        r = _respond(client, sid, h, did, "approve", "bridge-approve-main1")
        assert r.status_code == 200 and r.json()["applied"] is False
        r = _respond(client, sid, h, did, "reject", "bridge-reject-main01")
        assert r.status_code == 409 and r.json()["error_code"] == "DECISION_ALREADY_RESOLVED"

        # ── the v2 stream: dense, and the card's own events in order ──
        ev2 = _events2(client, sid, token, h)
        assert [e["seq"] for e in ev2] == list(range(1, len(ev2) + 1))
        mine = [e for e in ev2 if e.get("task_id") == card["task_id"]
                and e["type"] != "artifact.created"]
        assert [e["type"] for e in mine] == [
            "task.created", "task.assigned", "task.started", "decision.requested",
            "task.resumed", "task.completed"]
        assert [e["payload"]["revision"] for e in mine] == [0, 1, 2, 3, 4, 5]
        assert [e["payload"].get("task_state") for e in mine] == [
            "queued", None, "running", "awaiting_approval", "running", "completed"]
        assert mine[0]["payload"]["task_template"] == "auth_demo"
        assert mine[0]["payload"]["executable"] is True
        for e in mine[2:]:
            assert e["payload"]["run_task_id"] == running["run_task_id"]
            assert e["agent_id"] == "researcher"
        assert mine[3]["payload"]["decision_id"] == did
        assert mine[3]["payload"]["research_provenance"] == "template"
        assert mine[4]["payload"]["decision_id"] == did
        assert mine[5]["payload"]["result_summary"]
        assert len({e["event_id"] for e in ev2}) == len(ev2)

        # ── v2 artifacts reference the CARD, and precede its completion ──
        arts = [e for e in ev2 if e["type"] == "artifact.created"]
        assert arts and all(a["task_id"] == card["task_id"] for a in arts)
        assert all(a["seq"] < mine[5]["seq"] for a in arts)
        if producer == "live":
            assert sorted(a["payload"]["kind"] for a in arts) == ["code_diff", "test_report"]
            assert all(a["payload"]["source_mode"] == "live" for a in arts)
            assert all(a["payload"]["work_provenance"] == "sandbox_execution" for a in arts)
            assert (tmp_path / "ws" / sid / "oauth_login.py").exists()
        else:
            # scripted content stays labelled as scripted, and claims no execution
            assert all(a["payload"]["source_mode"] == "demo" for a in arts)
            assert all("work_provenance" not in a["payload"] for a in arts)
        page = client.get(f"/v2/sessions/{sid}/artifacts/{arts[0]['artifact_id']}", headers=h)
        assert page.status_code == 200 and page.json()["task_id"] == card["task_id"]
        assert len(_snap2(client, sid, h)["artifacts"]) == len(arts)

        # ── the v1 stream links back to the card ──
        ev1 = _events1(client, sid, token, h)
        assert [e["seq"] for e in ev1] == list(range(1, len(ev1) + 1))
        dec = next(e for e in ev1 if e["type"] == "decision.requested")
        assert dec["payload"]["board_task_id"] == card["task_id"]
        art1 = next(e for e in ev1 if e["type"] == "artifact.created")
        assert art1["payload"]["board_task_id"] == card["task_id"]
        fin = next(e for e in ev1 if e["type"] == "task.completed"
                   and e["task_id"].endswith("-oauth"))
        assert fin["payload"]["board_task_id"] == card["task_id"]
        assert fin["payload"]["task_state"] == "completed"

        # ── a card whose run has started can never start another ──
        r = _assign(client, sid, h, done, "researcher", "bridge-assign-main02")
        assert r.status_code == 409 and r.json()["code"] == "TASK_NOT_ASSIGNABLE"
        assert len(_runs(client, sid, h)) == 1
        assert len(_events2(client, sid, token, h)) == len(ev2)


@pytest.mark.parametrize("producer", PRODUCERS)
@pytest.mark.parametrize("action", ["reject", "request_changes"])
def test_rejected_run_cancels_the_card_with_no_artifact(tmp_path, monkeypatch, producer, action):
    _fake_tester(monkeypatch, PASS)
    with TestClient(_app(producer, tmp_path)) as client:
        sid, token, h = _open(client)
        card, did = _launch(client, sid, h)
        assert _respond(client, sid, h, did, action, "bridge-resolve-rej01").status_code == 200
        done = _wait_card(client, sid, h, card["task_id"])
        assert done["task_state"] == "cancelled"

        ev2 = _events2(client, sid, token, h)
        assert [e["seq"] for e in ev2] == list(range(1, len(ev2) + 1))
        assert not [e for e in ev2 if e["type"] == "artifact.created"]
        assert _snap2(client, sid, h)["artifacts"] == []
        mine = [e for e in ev2 if e.get("task_id") == card["task_id"]]
        assert [e["type"] for e in mine] == [
            "task.created", "task.assigned", "task.started", "decision.requested",
            "task.completed"]          # never 'running' again: nothing ran
        end = mine[-1]["payload"]
        assert end["task_state"] == "cancelled" and end["revision"] == done["revision"] == 4
        if action == "request_changes":
            # TASK C: promises nothing (no "revised proposal")
            assert end["result_summary"] == CHANGES_REQUESTED_MESSAGE
            ev1 = _events1(client, sid, token, h)
            texts = " ".join(str(e["payload"]) for e in ev1).lower()
            assert "revised proposal" not in texts and "pending" not in texts
            assert CHANGES_REQUESTED_MESSAGE in " ".join(
                e["payload"].get("progress_message", "") for e in ev1
                if e["type"] == "agent.status_changed" and e["agent_id"] == "developer")
        if producer == "live":
            assert not (tmp_path / "ws" / sid / "oauth_login.py").exists()


@pytest.mark.parametrize("producer", PRODUCERS)
def test_failed_run_fails_the_card(tmp_path, monkeypatch, producer):
    if producer == "live":
        _fake_tester(monkeypatch, PASS, FAIL)     # the tests on the new code fail
    else:
        async def boom(self, *a, **k):
            raise RuntimeError("scripted branch blew up")
        from app.orchestrator import Orchestrator
        monkeypatch.setattr(Orchestrator, "_apply_branch", boom)
    with TestClient(_app(producer, tmp_path)) as client:
        sid, token, h = _open(client)
        card, did = _launch(client, sid, h)
        _respond(client, sid, h, did, "approve", "bridge-resolve-fail1")
        done = _wait_card(client, sid, h, card["task_id"])
        assert done["task_state"] == "failed"
        end = [e for e in _events2(client, sid, token, h)
               if e.get("task_id") == card["task_id"] and e["type"] != "artifact.created"][-1]
        assert end["type"] == "task.failed" and end["payload"]["task_state"] == "failed"
        assert end["payload"]["revision"] == done["revision"]


def test_live_worker_exception_fails_the_card(tmp_path, monkeypatch):
    _fake_tester(monkeypatch, PASS)

    def explode(self, guard, ws):
        raise OSError("disk full")
    monkeypatch.setattr(DeveloperWorker, "implement_oauth", explode)
    with TestClient(_app("live", tmp_path)) as client:
        sid, token, h = _open(client)
        card, did = _launch(client, sid, h)
        _respond(client, sid, h, did, "approve", "bridge-resolve-exc01")
        done = _wait_card(client, sid, h, card["task_id"])
        assert done["task_state"] == "failed"
        end = [e for e in _events2(client, sid, token, h)
               if e.get("task_id") == card["task_id"]][-1]
        assert end["type"] == "task.failed" and end["payload"]["code"] == "RUN_FAILED"
        warn = next(e for e in _events1(client, sid, token, h) if e["type"] == "system.warning")
        assert warn["payload"]["board_task_id"] == card["task_id"]


# ─────────────────────── refusals ───────────────────────
@pytest.mark.parametrize("producer", PRODUCERS)
@pytest.mark.parametrize("agent", ["developer", "tester"])
def test_executable_card_assigned_to_a_later_agent_is_refused(tmp_path, producer, agent):
    with TestClient(_app(producer, tmp_path)) as client:
        sid, token, h = _open(client)
        card = _create(client, sid, h, "bridge-create-role01").json()
        r = _assign(client, sid, h, card, agent, "bridge-assign-role01")
        assert r.status_code == 409
        body = r.json()
        assert body["code"] == body["error_code"] == "ROLE_INCOMPATIBLE"
        assert "Researcher" in body["message"] and body["message"] == body["user_message"]
        assert body["canonical_task"] == card
        assert _card(client, sid, h, card["task_id"]) == card      # unchanged
        assert _runs(client, sid, h) == []                         # no run
        assert [e["type"] for e in _events2(client, sid, token, h)] == ["task.created"]
        # the refused key was not burned: the right agent can use the card afterwards
        assert _assign(client, sid, h, card, "researcher", "bridge-assign-role02").status_code == 200


@pytest.mark.parametrize("producer", PRODUCERS)
def test_second_executable_card_is_refused_while_a_run_is_in_progress(tmp_path, monkeypatch,
                                                                      producer):
    _fake_tester(monkeypatch, PASS, PASS, PASS, PASS)
    with TestClient(_app(producer, tmp_path)) as client:
        sid, token, h = _open(client)
        first, did = _launch(client, sid, h)
        second = _create(client, sid, h, "bridge-create-second").json()

        r = _assign(client, sid, h, second, "researcher", "bridge-assign-second")
        assert r.status_code == 409
        assert r.json()["code"] == r.json()["error_code"] == "RUN_IN_PROGRESS"
        assert _card(client, sid, h, second["task_id"]) == second   # unchanged
        assert len(_runs(client, sid, h)) == 1
        # the card-less v1 entry point is refused by the same rule
        v1 = client.post(f"/v1/sessions/{sid}/tasks", headers=h, json={
            "schema_version": 1, "task_template": "auth_demo",
            "idempotency_key": "bridge-v1-start-0001"})
        assert v1.status_code == 409 and v1.json()["error_code"] == "RUN_IN_PROGRESS"

        _respond(client, sid, h, did, "approve", "bridge-resolve-sec01")
        _wait_card(client, sid, h, first["task_id"])
        _wait(lambda: _runs(client, sid, h)[0]["status"] in TERMINAL, "the run to end")
        # same key, once the first run is over: accepted, and it starts the second run
        r = _assign(client, sid, h, second, "researcher", "bridge-assign-second")
        assert r.status_code == 200 and r.json()["task_state"] == "running"
        assert len(_runs(client, sid, h)) == 2


@pytest.mark.parametrize("producer", PRODUCERS)
def test_pipeline_card_cannot_be_paused_or_reassigned_mid_run(tmp_path, monkeypatch, producer):
    _fake_tester(monkeypatch, PASS)
    with TestClient(_app(producer, tmp_path)) as client:
        sid, token, h = _open(client)
        card, did = _launch(client, sid, h)
        waiting = _wait_card(client, sid, h, card["task_id"], ("awaiting_approval",))

        r = _cmd(client, sid, h, card["task_id"], "pause", waiting["revision"], "bridge-pause-run001")
        assert r.status_code == 409 and r.json()["code"] == "TASK_NOT_PAUSABLE"
        assert "no safe pause point" in r.json()["message"]
        r = _assign(client, sid, h, waiting, "developer", "bridge-assign-run001")
        assert r.status_code == 409 and r.json()["code"] == "UNSAFE_TRANSFER"
        assert _card(client, sid, h, card["task_id"]) == waiting
        types = [e["type"] for e in _events2(client, sid, token, h)]
        assert "task.pause_requested" not in types and "task.paused" not in types


def test_running_pipeline_card_refuses_pause(tmp_path):
    """The ``running`` phase (before the decision), driven at service level so the card
    is observed in that state deterministically."""
    import asyncio
    from app.phase2.errors import CommandError
    from app.phase2.task_graph import TaskGraphService
    from tests.conftest import p2_fresh

    class Launcher:
        started = []
        def run_in_progress(self, sid): return bool(self.started)
        def create_run(self, sid, template): return "task-run0000001"
        def start(self, sid, run, template, board_task_id=None):
            self.started.append((run, board_task_id))
        def abort(self, run): raise AssertionError("not expected")

    async def body():
        store, bus, _ = p2_fresh()
        g = TaskGraphService(store, bus, "demo", launcher=Launcher())
        t = g.create_task("ses-x", "run it", None, 1, [], None, task_template="auth_demo")
        card = await g.assign("ses-x", t.task_id, "researcher", 0)
        assert card.task_state.value == "running" and Launcher.started == [
            ("task-run0000001", t.task_id)]
        with pytest.raises(CommandError) as ei:
            await g.pause("ses-x", t.task_id, card.revision)
        assert ei.value.code == "TASK_NOT_PAUSABLE" and ei.value.status == 409
        with pytest.raises(CommandError) as ei:
            await g.assign("ses-x", t.task_id, "researcher", card.revision)
        assert ei.value.code == "TASK_RUNNING"
        assert store.get_task_v2(t.task_id)["task_state"] == "running"
        assert len(Launcher.started) == 1
    asyncio.run(body())


# ─────────────────────── plain (non-executable) cards ───────────────────────
@pytest.mark.parametrize("producer", PRODUCERS)
def test_plain_card_never_claims_to_run(tmp_path, producer):
    with TestClient(_app(producer, tmp_path)) as client:
        sid, token, h = _open(client)
        card = _create(client, sid, h, "bridge-create-plain1", template=None,
                       title="Write the README").json()
        assert card["executable"] is False and card["task_template"] is None

        a = _assign(client, sid, h, card, "researcher", "bridge-assign-plain1").json()
        assert a["task_state"] == "queued" and a["agent_id"] == "researcher"
        assert a["run_task_id"] is None
        assert _runs(client, sid, h) == [] and _snap1(client, sid, h)["decisions"] == []

        p = _cmd(client, sid, h, card["task_id"], "pause", a["revision"], "bridge-pause-plain01")
        assert p.status_code == 200 and p.json()["task_state"] == "paused"
        r = _cmd(client, sid, h, card["task_id"], "resume", p.json()["revision"],
                 "bridge-resume-plain1")
        # has an agent, but nothing runs it: back to queued, not 'running'
        assert r.status_code == 200 and r.json()["task_state"] == "queued"
        assert r.json()["agent_id"] == "researcher"

        ev2 = _events2(client, sid, token, h)
        assert [e["type"] for e in ev2] == ["task.created", "task.assigned", "task.paused",
                                            "task.resumed"]
        assert ev2[-1]["payload"]["task_state"] == "queued"
        assert ev2[0]["payload"]["executable"] is False
        states = {e["payload"].get("task_state") for e in ev2}
        assert "running" not in states and "pause_requested" not in states
        assert _runs(client, sid, h) == []
        assert all(a["state"] == "idle" for a in _snap1(client, sid, h)["agents"])


# ─────────────────────── create-time validation ───────────────────────
def test_unknown_task_template_is_rejected(client):
    sid, token, h = _open(client)
    r = _create(client, sid, h, "bridge-create-tmpl01", template="deploy_to_prod")
    assert r.status_code == 422
    assert r.json()["code"] == r.json()["error_code"] == "UNKNOWN_TASK_TEMPLATE"
    assert "deploy_to_prod" in r.json()["user_message"]
    snap = _snap2(client, sid, h)
    assert snap["tasks"] == [] and snap["last_seq"] == 0


def test_executable_card_cannot_be_locked_to_a_later_role(client):
    sid, token, h = _open(client)
    r = _create(client, sid, h, "bridge-create-lock01", role_lock="tester")
    assert r.status_code == 422 and r.json()["code"] == "ROLE_INCOMPATIBLE"
    assert _snap2(client, sid, h)["tasks"] == []


def test_unknown_or_foreign_dependency_is_rejected(client):
    sid, token, h = _open(client)
    other_sid, _, other_h = _open(client)
    theirs = _create(client, other_sid, other_h, "bridge-create-other1", template=None).json()

    for key, dep in (("bridge-create-dep001", "t2-doesnotexist"),
                     ("bridge-create-dep002", theirs["task_id"])):
        r = _create(client, sid, h, key, template=None, dependencies=[dep])
        assert r.status_code == 422, r.text
        assert r.json()["code"] == r.json()["error_code"] == "UNKNOWN_DEPENDENCY"
        assert dep in r.json()["message"]
    snap = _snap2(client, sid, h)
    assert snap["tasks"] == [] and snap["last_seq"] == 0       # nothing stranded, no event

    # a real dependency in the same session is still accepted (and blocks)
    mine = _create(client, sid, h, "bridge-create-dep003", template=None).json()
    ok = _create(client, sid, h, "bridge-create-dep004", template=None,
                 dependencies=[mine["task_id"]])
    assert ok.status_code == 201 and ok.json()["task_state"] == "blocked"


@pytest.mark.parametrize("producer", PRODUCERS)
def test_dependency_completion_unblocks_dependents(tmp_path, monkeypatch, producer):
    _fake_tester(monkeypatch, PASS, PASS)
    with TestClient(_app(producer, tmp_path)) as client:
        sid, token, h = _open(client)
        first = _create(client, sid, h, "bridge-create-chain1").json()
        plain = _create(client, sid, h, "bridge-create-chain2", template=None,
                        title="Write docs", dependencies=[first["task_id"]]).json()
        nxt = _create(client, sid, h, "bridge-create-chain3", title="Second run",
                      dependencies=[first["task_id"]]).json()
        assert plain["task_state"] == nxt["task_state"] == "blocked"

        # a blocked executable card cannot be started
        r = _assign(client, sid, h, nxt, "researcher", "bridge-assign-chain3")
        assert r.status_code == 409 and r.json()["code"] == "DEPENDENCIES_NOT_READY"
        assert _card(client, sid, h, nxt["task_id"]) == nxt and _runs(client, sid, h) == []

        assert _assign(client, sid, h, first, "researcher", "bridge-assign-chain1").status_code == 200
        _respond(client, sid, h, _pending(client, sid, h), "approve", "bridge-resolve-chn01")
        assert _wait_card(client, sid, h, first["task_id"])["task_state"] == "completed"

        for dep in (plain, nxt):
            now = _wait_card(client, sid, h, dep["task_id"], ("queued",))
            assert now["revision"] == 1
        ev2 = _events2(client, sid, token, h)
        assert [e["seq"] for e in ev2] == list(range(1, len(ev2) + 1))
        for dep in (plain, nxt):
            (un,) = [e for e in ev2 if e.get("task_id") == dep["task_id"]
                     and e["type"] == "task.resumed"]
            assert un["payload"]["task_state"] == "queued" and un["payload"]["revision"] == 1
            assert un["payload"]["unblocked_by"] == first["task_id"]

        # and the released executable card can now really be started
        _wait(lambda: _runs(client, sid, h)[0]["status"] in TERMINAL, "the run to end")
        nxt_now = _card(client, sid, h, nxt["task_id"])
        r = _assign(client, sid, h, nxt_now, "researcher", "bridge-assign-chain4")
        assert r.status_code == 200 and r.json()["task_state"] == "running"


# ─────────────────────── restart ───────────────────────
@pytest.mark.parametrize("producer", PRODUCERS)
def test_restart_mid_run_fails_the_card(tmp_path, monkeypatch, producer):
    _fake_tester(monkeypatch, PASS, PASS)
    db = str(tmp_path / "restart.sqlite3")

    with TestClient(_app(producer, tmp_path, db)) as client:   # process #1
        sid, token, h = _open(client)
        card, did = _launch(client, sid, h)
        before = _wait_card(client, sid, h, card["task_id"], ("awaiting_approval",))
        seq_before = _snap2(client, sid, h)["last_seq"]

    with TestClient(_app(producer, tmp_path, db)) as client:   # process #2: fresh memory
        after = _card(client, sid, h, card["task_id"])
        assert after["task_state"] == "failed"
        assert after["revision"] == before["revision"] + 1
        assert after["run_task_id"] == card["run_task_id"]

        ev2 = _events2(client, sid, token, h)
        assert [e["seq"] for e in ev2] == list(range(1, len(ev2) + 1))
        (new,) = [e for e in ev2 if e["seq"] > seq_before]
        assert new["type"] == "task.failed" and new["task_id"] == card["task_id"]
        assert new["payload"]["task_state"] == "failed"
        assert new["payload"]["revision"] == after["revision"]
        assert new["payload"]["code"] == "RUN_INTERRUPTED"

        # v1 side agrees, and the orphaned decision cannot be approved into nothing
        (run,) = _runs(client, sid, h)
        assert run["status"] == "failed" and run["board_task_id"] == card["task_id"]
        r = _respond(client, sid, h, did, "approve", "bridge-resolve-rst01")
        assert r.status_code == 409
        assert _card(client, sid, h, card["task_id"]) == after

        # the failed card cannot be restarted, but the session can run a new card
        r = _assign(client, sid, h, after, "researcher", "bridge-assign-rst002")
        assert r.status_code == 409 and r.json()["code"] == "TASK_NOT_ASSIGNABLE"
        again, _ = _launch(client, sid, h, key="bridge-create-rst003")
        assert again["task_state"] == "running"

    with TestClient(_app(producer, tmp_path, db)) as client:   # process #3: reported once
        ev2 = _events2(client, sid, token, h)
        assert [e["seq"] for e in ev2] == list(range(1, len(ev2) + 1))
        assert len([e for e in ev2 if e["type"] == "task.failed"]) == 2
        assert {t["task_state"] for t in _snap2(client, sid, h)["tasks"]} == {"failed"}


# ─────────────────────── provenance (TASK B) + the unchanged v1 path ───────────────────────
@pytest.mark.parametrize("producer", PRODUCERS)
def test_research_is_labelled_a_template_and_v1_start_is_unchanged(tmp_path, monkeypatch,
                                                                   producer):
    _fake_tester(monkeypatch, PASS, PASS)
    with TestClient(_app(producer, tmp_path)) as client:
        sid, token, h = _open(client)
        r = client.post(f"/v1/sessions/{sid}/tasks", headers=h, json={
            "schema_version": 1, "task_template": "auth_demo",
            "idempotency_key": "bridge-v1-start-0002"})
        assert r.status_code == 201
        assert r.json() == {"schema_version": 1, "task_id": r.json()["task_id"],
                            "status": "running", "session_id": sid,
                            "task_template": "auth_demo", "template": "auth_demo"}
        did = _pending(client, sid, h)
        _respond(client, sid, h, did, "approve", "bridge-resolve-v1001")
        _wait(lambda: _runs(client, sid, h)[0]["status"] in TERMINAL, "the run to end")

        ev1 = _events1(client, sid, token, h)
        dec = next(e for e in ev1 if e["type"] == "decision.requested")["payload"]
        assert dec["research_provenance"] == "template"
        assert dec["research_provenance_label"] == RESEARCH_PROVENANCE_LABEL
        assert dec["source_mode"] == producer          # source_mode values are unchanged
        researching = next(e for e in ev1 if e["type"] == "agent.status_changed"
                           and e["agent_id"] == "researcher"
                           and e["payload"]["state"] == "researching")["payload"]
        assert researching["progress_message"] == "Preparing a template recommendation"
        assert "work_provenance" not in researching

        # card-less run: no card linkage anywhere, no card on the board
        assert all("board_task_id" not in e["payload"] for e in ev1)
        assert _runs(client, sid, h)[0]["board_task_id"] is None
        assert _snap2(client, sid, h)["tasks"] == []

        if producer == "live":
            # developer and tester statuses are separately identifiable as real work
            worked = [e["payload"] for e in ev1 if e["type"] == "agent.status_changed"
                      and e["payload"]["state"] in ("coding", "testing")]
            assert worked and all(p["work_provenance"] == "sandbox_execution" for p in worked)
            art = next(e for e in ev1 if e["type"] == "artifact.created")["payload"]
            assert art["work_provenance"] == "sandbox_execution" and art["source_mode"] == "live"
            # v2 artifacts of a card-less run keep the v1 task id, as before
            v2art = [e for e in _events2(client, sid, token, h) if e["type"] == "artifact.created"]
            assert v2art and all(a["task_id"].endswith("-oauth") for a in v2art)
        else:
            assert not [e for e in ev1 if "work_provenance" in e["payload"]]
            assert _events2(client, sid, token, h) == []
