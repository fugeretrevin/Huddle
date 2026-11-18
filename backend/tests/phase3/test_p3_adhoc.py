"""Cards the USER creates and assigns are worked for real (rc21 owner: "the developer only works
when the manager is doing things"), and the backlog goes highest priority first."""
from __future__ import annotations

import json
import subprocess
import sys

from tests.phase3.p3helpers import FakeProvider, ask, event_types, open_session, p3_client
from tests.phase3.test_p3_runner import _committed, artifacts, state, tick
from tests.phase3.test_p3_sandbox_work import BUGGY_CODE, ScriptedProvider

_n = [0]


def _client(provider=None, mode="live", **kw):
    kw.setdefault("p3_adhoc_start_delay_seconds", 0.0)
    return p3_client(mode=mode, provider=provider, p3_runner_demo_step_seconds=0.0, **kw)


def _exec(tmp_path, provider, **kw):
    return _client(provider, p3_execute_code=True, workspace_root=str(tmp_path), **kw)


def _key(tag):
    _n[0] += 1
    return f"adhoc-{tag}-{_n[0]:08d}"


def create(c, sid, h, title="Write a function that reverses a string, with tests", agent=None,
           priority=1, deps=None, template=None):
    body = {"schema_version": 2, "title": title, "priority": priority,
            "dependencies": deps or [], "idempotency_key": _key("create")}
    if agent:
        body["agent_id"] = agent
    if template:
        body["task_template"] = template
    r = c.post(f"/v2/sessions/{sid}/tasks", headers=h, json=body)
    assert r.status_code == 201, r.text
    return r.json()


def assign(c, sid, h, card, agent):
    r = c.post(f"/v2/sessions/{sid}/tasks/{card['task_id']}/assign", headers=h, json={
        "schema_version": 2, "target_agent_id": agent, "expected_revision": card["revision"],
        "idempotency_key": _key("assign")})
    assert r.status_code == 200, r.text
    return r.json()


def set_priority(c, sid, h, card, priority):
    r = c.post(f"/v2/sessions/{sid}/tasks/{card['task_id']}/priority", headers=h, json={
        "schema_version": 2, "priority": priority, "expected_revision": card["revision"],
        "idempotency_key": _key("prio")})
    assert r.status_code == 200, r.text
    return r.json()


def events(c, sid, task_id=None, type_=None):
    return [e for e in c.app.state.phase2_store.events_after_v2(sid, 0)
            if (task_id is None or e.get("task_id") == task_id)
            and (type_ is None or e["type"] == type_)]


def marked(c, task_id) -> bool:
    return c.app.state.phase3_store.adhoc_entry(task_id) is not None


# ─────────────────────── real work ───────────────────────
def test_owner_flow_create_assign_priority_runs_real_developer_work(tmp_path):
    """The owner's rc21 log: create (201) -> assign (200) -> priority (200), then nothing."""
    prov = ScriptedProvider()
    with _exec(tmp_path, prov) as c:
        sid, h = open_session(c)
        card = create(c, sid, h)
        card = assign(c, sid, h, card, "developer")
        card = set_priority(c, sid, h, card, 2)
        tid = card["task_id"]
        assert state(c, tid) == "queued" and marked(c, tid)

        assert tick(c) == 1
        assert state(c, tid) == "completed"
        assert prov.calls[0] == "developer"
        folder = tmp_path / "tasks" / tid
        assert (folder / "login_rules.py").exists() and (folder / "test_login_rules.py").exists()

        seq = [e["type"] for e in events(c, sid, tid)]
        assert seq == ["task.created", "task.assigned", "task.priority_changed", "task.started",
                       "artifact.created", "task.completed"]
        started = events(c, sid, tid, "task.started")[0]["payload"]
        assert started["adhoc"] is True and started["work_mode"] == "sandbox"
        assert started["task_state"] == "running" and "plan_id" not in started
        done = events(c, sid, tid, "task.completed")[0]
        assert done["agent_id"] == "developer"
        p = done["payload"]
        assert p["task_state"] == "completed" and p["revision"] == card["revision"] + 2
        assert p["tests_passed"] == 2 and p["tests_failed"] == 0 and p["tests_ran"] is True
        assert p["workspace"] == f"tasks/{tid}" and p["adhoc"] is True
        assert "pytest 2 passed, 0 failed" in p["result_summary"]
        art = artifacts(c, sid, tid)[0]
        assert art["id"] == p["artifact_id"] and art["kind"] == "code_diff"
        page0 = json.loads(art["pages_json"])[0]
        assert "restricted workspace" in page0["title"] and "sandbox" not in page0["title"].lower()

        # An independent pytest run of the folder agrees with the card.
        out = subprocess.run([sys.executable, "-m", "pytest", "-q", "-p", "no:cacheprovider", str(folder)],
                             cwd=str(folder), capture_output=True, text=True, timeout=120)
        assert "2 passed" in out.stdout, out.stdout + out.stderr

        assert tick(c) == 0                                  # worked once, never again

        # /btw to the Developer sees the real result (recorded data, no model).
        c.app.state.p3_provider = None
        r = ask(c, sid, h, "developer", "What is the status?", key=_key("ask"))
        assert r.status_code == 200, r.text
        assert "2 passed" in r.json()["reply"] and r.json()["task_id"] == tid


def test_create_with_agent_researcher_gets_model_text_even_with_execution_on(tmp_path):
    prov = ScriptedProvider()
    with _exec(tmp_path, prov) as c:
        sid, h = open_session(c)
        card = create(c, sid, h, title="Compare two ways to store sessions", agent="researcher")
        assert marked(c, card["task_id"])
        assert tick(c) == 1
        assert state(c, card["task_id"]) == "completed"
        art = artifacts(c, sid, card["task_id"])[0]
        assert art["kind"] == "research_report"
        assert json.loads(art["pages_json"])[0]["content"].startswith("Model-written, no tools")
        p = events(c, sid, card["task_id"], "task.completed")[0]["payload"]
        assert p["work_mode"] == "model_text" and p["adhoc"] is True
        assert "work" in prov.calls and "developer" not in prov.calls
        assert not (tmp_path / "tasks").exists()


def test_text_only_mode_developer_writes_text_and_no_files(tmp_path):
    prov = FakeProvider("Approach.\nSummary: reverse with slicing.")
    with _client(prov, workspace_root=str(tmp_path)) as c:
        sid, h = open_session(c)
        card = assign(c, sid, h, create(c, sid, h), "developer")
        assert tick(c) == 1
        assert state(c, card["task_id"]) == "completed"
        p = events(c, sid, card["task_id"], "task.completed")[0]["payload"]
        assert p["work_mode"] == "model_text"
        assert p["result_summary"] == "Proposed change: reverse with slicing."
        assert prov.calls[0]["purpose"] == "work"
        assert "reverses a string" in prov.calls[0]["messages"][0]["content"]
        assert not (tmp_path / "tasks").exists()


def test_tester_card_tests_the_latest_developer_folder(tmp_path):
    prov = ScriptedProvider()
    with _exec(tmp_path, prov) as c:
        sid, h = open_session(c)
        dev = assign(c, sid, h, create(c, sid, h), "developer")
        assert tick(c) == 1
        tst = assign(c, sid, h, create(c, sid, h, title="Test the string reverser"), "tester")
        assert tick(c) == 1
        assert state(c, tst["task_id"]) == "completed"
        p = events(c, sid, tst["task_id"], "task.completed")[0]["payload"]
        assert p["workspace"] == f"tasks/{dev['task_id']}" and p["tests_passed"] == 4
        assert not (tmp_path / "tasks" / tst["task_id"]).exists()


# ─────────────────────── failure ───────────────────────
def test_model_failure_fails_the_card_with_a_reason():
    with _client(FakeProvider(fail=True)) as c:
        sid, h = open_session(c)
        card = assign(c, sid, h, create(c, sid, h), "developer")
        assert tick(c) == 1
        assert state(c, card["task_id"]) == "failed"
        p = events(c, sid, card["task_id"], "task.failed")[0]["payload"]
        assert p["code"] == "MODEL_UNAVAILABLE" and p["adhoc"] is True
        assert p["result_summary"] == "The model could not work this task."
        assert artifacts(c, sid, card["task_id"]) == []
        assert tick(c) == 0


def test_failing_tests_fail_the_card_with_real_counts(tmp_path):
    with _exec(tmp_path, ScriptedProvider(dev=BUGGY_CODE, fix=BUGGY_CODE)) as c:
        sid, h = open_session(c)
        card = assign(c, sid, h, create(c, sid, h), "developer")
        assert tick(c) == 1
        assert state(c, card["task_id"]) == "failed"
        p = events(c, sid, card["task_id"], "task.failed")[0]["payload"]
        assert p["code"] == "TESTS_FAILED" and "0 passed, 2 failed" in p["result_summary"]


def test_recover_fails_an_adhoc_card_left_running():
    with _client(FakeProvider()) as c:
        sid, h = open_session(c)
        card = assign(c, sid, h, create(c, sid, h), "developer")
        c.app.state.phase2_store.update_task_v2(card["task_id"], task_state="running",
                                                now="2026-11-17T00:00:00Z")
        assert c.portal.call(c.app.state.p3_runner.recover) == 1
        assert state(c, card["task_id"]) == "failed"
        assert events(c, sid, card["task_id"], "task.failed")[0]["payload"]["code"] == "RUN_INTERRUPTED"


# ─────────────────────── what is NOT run ───────────────────────
def test_unassigned_template_and_unconfigured_cards_are_not_run():
    prov = FakeProvider()
    with _client(prov) as c:
        sid, h = open_session(c)
        loose = create(c, sid, h, title="Nobody owns this")
        assert not marked(c, loose["task_id"])
        pipe = create(c, sid, h, title="Auth demo", template="auth_demo")
        pipe = assign(c, sid, h, pipe, "researcher")          # launches the v1 pipeline instead
        assert not marked(c, pipe["task_id"])
        c.app.state.p3_provider = None                        # no model: nothing is recorded
        nomodel = assign(c, sid, h, create(c, sid, h, title="No model"), "developer")
        assert not marked(c, nomodel["task_id"])
        c.app.state.p3_provider = prov
        assert tick(c) == 0
        assert prov.calls == []
        assert state(c, loose["task_id"]) == "queued" and state(c, nomodel["task_id"]) == "queued"


def test_plan_cards_are_not_double_run_when_the_user_reassigns_one():
    prov = FakeProvider("Done.\nSummary: ok.")
    with _client(prov) as c:
        sid, h = open_session(c)
        _, cards = _committed(c, sid, h)
        first = c.app.state.phase2_store.get_task_v2(cards[0]["task_id"])
        assign(c, sid, h, {"task_id": first["id"], "revision": first["revision"]}, "developer")
        assert not marked(c, first["id"])
        assert tick(c) == 1
        started = events(c, sid, first["id"], "task.started")
        assert len(started) == 1 and started[0]["payload"]["plan_id"]
        assert "adhoc" not in started[0]["payload"]


def test_demo_mode_does_nothing_new():
    prov = FakeProvider()
    with _client(prov, mode="demo") as c:
        sid, h = open_session(c)
        card = assign(c, sid, h, create(c, sid, h), "developer")
        assert not marked(c, card["task_id"])
        before = event_types(c, sid)
        assert tick(c) == 0
        assert state(c, card["task_id"]) == "queued"
        assert event_types(c, sid) == before and prov.calls == []


def test_start_delay_keeps_the_card_queued_until_it_passes():
    with _client(FakeProvider(), p3_adhoc_start_delay_seconds=60.0) as c:
        sid, h = open_session(c)
        card = assign(c, sid, h, create(c, sid, h), "developer")
        assert tick(c) == 0 and state(c, card["task_id"]) == "queued"


# ─────────────────────── backlog order ───────────────────────
def _started_order(c, sid):
    return [e["task_id"] for e in events(c, sid, type_="task.started")]


def test_three_developer_cards_are_worked_highest_priority_first():
    with _client(FakeProvider("Done.\nSummary: ok.")) as c:
        sid, h = open_session(c)
        normal = assign(c, sid, h, create(c, sid, h, title="Normal", priority=1), "developer")
        urgent = assign(c, sid, h, create(c, sid, h, title="Urgent", priority=3), "developer")
        high = assign(c, sid, h, create(c, sid, h, title="High", priority=2), "developer")
        for _ in range(3):
            assert tick(c) == 1
        assert _started_order(c, sid) == [urgent["task_id"], high["task_id"], normal["task_id"]]


def test_equal_priority_goes_in_creation_order_and_a_blocked_card_waits():
    with _client(FakeProvider("Done.\nSummary: ok.")) as c:
        sid, h = open_session(c)
        low = assign(c, sid, h, create(c, sid, h, title="Low first", priority=0), "developer")
        a = assign(c, sid, h, create(c, sid, h, title="A", priority=2), "developer")
        b = assign(c, sid, h, create(c, sid, h, title="B", priority=2), "developer")
        top = create(c, sid, h, title="Urgent but blocked", priority=3, deps=[low["task_id"]])
        assert top["task_state"] == "blocked"
        top = assign(c, sid, h, top, "developer")
        for _ in range(4):
            assert tick(c) == 1
        assert _started_order(c, sid) == [a["task_id"], b["task_id"], low["task_id"], top["task_id"]]


def test_user_card_and_plan_cards_share_one_backlog():
    with _client(FakeProvider("Done.\nSummary: ok.")) as c:
        sid, h = open_session(c)
        _, cards = _committed(c, sid, h)                     # first plan card: queued, Normal
        mine = assign(c, sid, h, create(c, sid, h, title="Hotfix", priority=3), "developer")
        assert tick(c) == 1
        assert _started_order(c, sid) == [mine["task_id"]]
        # A newer High user card goes before the older Normal plan card ...
        high = assign(c, sid, h, create(c, sid, h, title="High", priority=2), "developer")
        assert tick(c) == 1
        assert _started_order(c, sid)[-1] == high["task_id"]
        # ... and a plan card the user raises to Urgent goes before a newer High user card.
        later = assign(c, sid, h, create(c, sid, h, title="Later", priority=2), "developer")
        plan_first = c.app.state.phase2_store.get_task_v2(cards[0]["task_id"])
        set_priority(c, sid, h, {"task_id": plan_first["id"], "revision": plan_first["revision"]}, 3)
        assert tick(c) == 1
        assert _started_order(c, sid)[-1] == plan_first["id"]
        assert state(c, later["task_id"]) == "queued"
