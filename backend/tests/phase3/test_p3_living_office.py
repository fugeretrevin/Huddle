"""Living Office glue: what the Wall reads to draw a committed plan truthfully.

The Unity wall shows, per card: its state, the backend's blocked reason, the failure reason,
the recorded result, and (when the plan has settled) the summary artifact. These tests pin the
shapes it parses: ``plan_status`` cards, the ``task.progress`` blocked event, the
``manager.plan_committed`` event and the ``completion`` artifact event.
"""
from __future__ import annotations

from tests.phase3.p3helpers import open_session
from tests.phase3.test_p3_plan_ops import FlakyProvider, _status, run, svc, synth
from tests.phase3.test_p3_runner import _client, _committed, state, tick


def _events(c, sid, type_):
    return [e for e in c.app.state.phase2_store.events_after_v2(sid, 0) if e["type"] == type_]


def test_status_card_carries_result_only_for_completed_cards():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        a, b, t = (x["task_id"] for x in cards)
        st = run(c, lambda: _status(c, sid, plan["plan_id"]))
        assert all("result" not in card for card in st["cards"]), "nothing has run yet"
        waiting = {card["task_id"]: card for card in st["cards"]}
        assert waiting[b]["state"] == "blocked" and waiting[b]["blocked_by"] == []
        assert waiting[b]["blocked_reason"].startswith("Waiting for ")       # merely waiting: no failure behind it
        assert tick(c) == 1
        st = run(c, lambda: _status(c, sid, plan["plan_id"]))
        by = {card["task_id"]: card for card in st["cards"]}
        assert by[a]["state"] == "completed" and by[a]["result"]
        art = [x for x in c.app.state.phase2_store.list_artifacts_v2(sid) if x["task_id"] == a][-1]
        assert by[a]["result"] == art["summary"], "the result is the recorded artifact summary, not new text"
        assert "result" not in by[b] and "result" not in by[t]


def test_failed_card_status_has_failure_not_result_and_dependents_say_why():
    with _client(provider=FlakyProvider(fail_work=99)) as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        a, b, t = (x["task_id"] for x in cards)
        tick(c)
        st = run(c, lambda: _status(c, sid, plan["plan_id"]))
        by = {card["task_id"]: card for card in st["cards"]}
        assert by[a]["state"] == "failed" and by[a]["failure"] and "result" not in by[a]
        assert "boom" not in by[a]["failure"], "provider internals never reach the client"
        assert by[b]["blocked_by"] == [a] and "Retry" in by[b]["blocked_reason"]
        # The stream event the wall listens to carries the same reason and root.
        ev = [e for e in _events(c, sid, "task.progress") if e.get("task_id") == b][0]
        assert ev["payload"]["blocked_reason"] == by[b]["blocked_reason"]
        assert ev["payload"]["blocked_by"] == [a]
        assert ev["payload"]["task_state"] == "blocked"
        # Settled behind a failure: a summary exists and says so.
        assert st["settled"] is True
        st2 = run(c, lambda: _status(c, sid, plan["plan_id"]))
        assert st2["synthesis"] and st2["synthesis"]["completed"] == 0 and st2["synthesis"]["total"] == 3


def test_commit_event_lists_only_canonical_cards_with_owner():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        ev = _events(c, sid, "manager.plan_committed")[0]["payload"]
        assert ev["plan_id"] == plan["plan_id"]
        assert [x["task_id"] for x in ev["tasks"]] == [x["task_id"] for x in cards]
        for entry in ev["tasks"]:
            row = c.app.state.phase2_store.get_task_v2(entry["task_id"])
            assert row is not None, "every announced id is a real board card"
            assert entry["agent_id"] == row["agent_id"] and entry["client_item_id"]
        assert not _events(c, sid, "manager.plan_proposed")[0]["payload"].get("tasks"), \
            "a proposed plan announces no cards"


def test_summary_artifact_event_is_a_completion_anchored_on_a_plan_card():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        ids = [x["task_id"] for x in cards]
        for _ in ids:
            tick(c)
        assert [state(c, x) for x in ids] == ["completed"] * 3
        tick(c)                                             # the sweep settles the plan if the last card did not
        arts = synth(c, sid)
        assert len(arts) == 1
        ev = [e for e in _events(c, sid, "artifact.created") if e["payload"]["kind"] == "completion"]
        assert len(ev) == 1, "one summary, announced once"
        assert ev[0]["task_id"] in ids, "the wall only shows a completion artifact anchored on a plan card"
        assert ev[0]["payload"]["summary"].startswith("[SCRIPTED DEMO] Plan summary: 3 of 3")
        st = svc(c).plan_status(sid, plan["plan_id"])
        assert st["synthesis"]["artifact_id"] == arts[0]["id"] and st["synthesis"]["task_id"] == ev[0]["task_id"]
        assert st["synthesis"]["mode"] == "scripted_demo"
