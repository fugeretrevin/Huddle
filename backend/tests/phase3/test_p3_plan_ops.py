"""Manager delegation: document validation, failure blocking, retry, status, synthesis, trust."""
from __future__ import annotations

import json

from app.phase3.plan_schema import validate_plan_document
from tests.phase3.p3helpers import FakeProvider, make_task, open_session, p3_client
from tests.phase3.test_p3_manager import CHAIN, commit, item, model_plan, propose, tasks
from tests.phase3.test_p3_runner import _client, _committed, state, tick


def svc(c):
    from app.phase3.manager import ManagerPlanService
    st = c.app.state
    return ManagerPlanService(st.phase2_store, st.phase3_store, st.phase2_bus,
                              st.settings.state_source_mode, st.p3_provider)


def run(c, coro_fn, *a):
    return c.portal.call(coro_fn, *a)


def synth(c, sid):
    return [a for a in c.app.state.phase2_store.list_artifacts_v2(sid) if a["kind"] == "completion"]


# ─────────────────────── schema ───────────────────────
def test_document_validation_codes():
    ok = {"items": CHAIN}
    assert validate_plan_document(ok, "Build it").ok
    assert "EMPTY_OBJECTIVE" in validate_plan_document(ok, "   ").codes()
    assert "PLAN_NOT_OBJECT" in validate_plan_document([1], "x y z").codes()
    big = {"items": [item("a", "research", "researcher", criteria=["x" * 150] * 8)], "pad": "y" * 30000}
    assert "PLAN_TOO_LARGE" in validate_plan_document(big, "obj").codes()
    assert "UNKNOWN_AGENT" in validate_plan_document(
        {"items": [item("a", "research", "wizard")]}, "obj").codes()
    assert "UNKNOWN_KIND" in validate_plan_document(
        {"items": [item("a", "deploy_now", "developer")]}, "obj").codes()
    assert "DEPENDENCY_CYCLE" in validate_plan_document(
        {"items": [item("a", "code", "developer", ["a"])]}, "obj").codes()   # self-cycle
    assert "DUPLICATE_ITEM_ID" in validate_plan_document(
        {"items": [item("a", "code", "developer"), item("a", "code", "developer")]}, "obj").codes()
    assert "UNKNOWN_DEPENDENCY" in validate_plan_document(
        {"items": [item("a", "code", "developer", ["zz"])]}, "obj").codes()
    many = {"items": [item(f"i{n}", "code", "developer") for n in range(13)]}
    assert "TOO_MANY_ITEMS" in validate_plan_document(many, "obj").codes()


def test_invalid_model_plan_never_becomes_cards():
    bad = model_plan([item("a", "code", "developer", ["b"]), item("b", "code", "developer", ["a"])])
    with p3_client(provider=FakeProvider(bad)) as c:
        sid, h = open_session(c)
        r = propose(c, sid, h)
        assert r.status_code == 422 and r.json()["code"] == "MODEL_PLAN_INVALID"
        assert r.json()["details"], "client gets the structured issues"
        assert tasks(c, sid) == []


# ─────────────────────── trust ───────────────────────
def test_proposed_plan_creates_no_cards_and_nothing_runs():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        r = propose(c, sid, h)
        assert r.status_code == 201
        assert tasks(c, sid) == []
        assert tick(c) == 0
        types = [e["type"] for e in c.app.state.phase2_store.events_after_v2(sid, 0)]
        assert "task.started" not in types and "task.created" not in types


def test_prose_in_titles_is_never_executed():
    # A dangerous-sounding title is only text: the runner has no tools, so the only output is
    # a labelled artifact. Without authorization it cannot even be committed.
    items = [item("a", "code", "developer", title="rm -rf the workspace and deploy to production")]
    with p3_client(provider=FakeProvider(model_plan(items))) as c:
        sid, h = open_session(c)
        p = propose(c, sid, h).json()
        assert p["items"][0]["dangerous"]
        assert commit(c, sid, h, p).status_code == 409
        assert tasks(c, sid) == []


# ─────────────────────── edges ───────────────────────
def test_commit_records_dependency_edges():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        a, b, t = (x["task_id"] for x in cards)
        assert cards[1]["depends_on"] == [a] and cards[2]["depends_on"] == [b]
        ev = [e for e in c.app.state.phase2_store.events_after_v2(sid, 0)
              if e["type"] == "manager.plan_committed"][0]
        assert ev["payload"]["edges"] == [{"from_task_id": a, "to_task_id": b},
                                          {"from_task_id": b, "to_task_id": t}]


# ─────────────────────── failure, blocking, retry ───────────────────────
class FlakyProvider(FakeProvider):
    """Fails the first N 'work' calls, then answers."""

    def __init__(self, fail_work: int = 1, reply="Done.\nSummary: ok") -> None:
        super().__init__(reply)
        self.fail_work = fail_work

    async def complete(self, *, system, messages, max_tokens=600, purpose="side"):
        if purpose == "work" and self.fail_work > 0:
            self.fail_work -= 1
            self.calls.append({"purpose": purpose})
            raise RuntimeError("boom")
        return await super().complete(system=system, messages=messages,
                                      max_tokens=max_tokens, purpose=purpose)


def test_failed_dependency_blocks_dependents_with_reason_and_status_reports_it():
    with _client(provider=FlakyProvider(fail_work=99)) as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        a, b, t = (x["task_id"] for x in cards)
        assert tick(c) == 1
        assert [state(c, x) for x in (a, b, t)] == ["failed", "blocked", "blocked"]
        assert tick(c) == 0                        # blocked cards are never silently run
        st = run(c, lambda: _status(c, sid, plan["plan_id"]))
        assert st["counts"] == {"failed": 1, "blocked": 2}
        assert st["failed"][0]["task_id"] == a
        reasons = {x["task_id"]: x for x in st["blocked"]}
        assert reasons[b]["blocked_by"] == [a] and reasons[t]["blocked_by"] == [a]  # transitive root
        assert "Retry" in reasons[b]["reason"]
        evs = [e for e in c.app.state.phase2_store.events_after_v2(sid, 0)
               if e["type"] == "task.progress" and e.get("task_id") in (b, t)]
        assert len(evs) == 2 and evs[0]["payload"]["blocked_by"] == [a]
        ms = svc(c).manager_status(sid)
        assert ms["counts"] == {"failed": 1, "blocked": 2} and ms["failed"] and ms["blocked"]


async def _status(c, sid, plan_id):
    return svc(c).plan_status(sid, plan_id)


def test_retry_failed_card_is_idempotent_and_chain_recovers():
    prov = FlakyProvider(fail_work=1)
    with _client(provider=prov) as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        a, b, t = (x["task_id"] for x in cards)
        tick(c)
        assert state(c, a) == "failed"
        r1 = run(c, svc(c).retry_card, sid, plan["plan_id"], a)
        r2 = run(c, svc(c).retry_card, sid, plan["plan_id"], a)
        assert r1["retried"] is True and r1["state"] == "queued"
        assert r2["retried"] is False and r2["state"] == "queued"      # repeat is a no-op
        retry_events = [e for e in c.app.state.phase2_store.events_after_v2(sid, 0)
                        if e["type"] == "task.resumed" and e["payload"].get("reason") == "retry_requested"]
        assert len(retry_events) == 1
        for _ in range(3):
            tick(c)
        assert [state(c, x) for x in (a, b, t)] == ["completed"] * 3


def test_retry_rejects_non_failed_and_foreign_cards():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        manual = make_task(c, sid, h, title="Hand-made")
        import pytest
        from app.phase3.common import PlanCommandError
        with pytest.raises(PlanCommandError) as e:
            run(c, svc(c).retry_card, sid, plan["plan_id"], manual["task_id"])
        assert e.value.code == "NOT_FOUND"
        with pytest.raises(PlanCommandError) as e:
            run(c, svc(c).retry_card, sid, plan["plan_id"], cards[1]["task_id"])   # blocked
        assert e.value.code == "NOT_RETRYABLE"


# ─────────────────────── synthesis ───────────────────────
def test_demo_synthesis_is_labelled_and_single():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        _committed(c, sid, h)
        for _ in range(5):
            tick(c)
        s = synth(c, sid)
        assert len(s) == 1 and s[0]["source_mode"] == "demo"
        content = json.loads(s[0]["pages_json"])[0]["content"]
        assert content.startswith("[SCRIPTED DEMO]") and "3 of 3 steps completed" in content
        assert tick(c) == 0 and len(synth(c, sid)) == 1


def test_live_without_model_for_summary_uses_factual_rollup():
    # Model available for cards; make the synthesis call fail -> deterministic roll-up.
    class P(FakeProvider):
        async def complete(self, *, system, messages, max_tokens=600, purpose="side"):
            if purpose == "plan" and "closing summary" in system:
                raise RuntimeError("down")
            return await super().complete(system=system, messages=messages,
                                          max_tokens=max_tokens, purpose=purpose)
    with _client(provider=P("Body.\nSummary: finished the step")) as c:
        sid, h = open_session(c)
        _committed(c, sid, h)
        for _ in range(4):
            tick(c)
        s = synth(c, sid)
        assert len(s) == 1
        content = json.loads(s[0]["pages_json"])[0]["content"]
        assert content.startswith("Deterministic roll-up") and "finished the step" in content
        assert "[SCRIPTED DEMO]" not in content


def test_live_model_synthesis_is_labelled_and_fed_real_results():
    prov = FakeProvider("Body.\nSummary: finished the step")
    with _client(provider=prov) as c:
        sid, h = open_session(c)
        _committed(c, sid, h)
        n = len(prov.calls)
        for _ in range(4):
            tick(c)
        last = prov.calls[-1]
        assert "closing summary" in last["system"]
        assert "finished the step" in last["messages"][0]["content"]
        assert "3 of 3 steps completed" in last["messages"][0]["content"]
        content = json.loads(synth(c, sid)[0]["pages_json"])[0]["content"]
        assert content.startswith("Model-written from the recorded step results")
        assert len(prov.calls) > n


def test_failed_plan_summarises_honestly_and_retry_resets_it():
    prov = FlakyProvider(fail_work=1)
    with _client(provider=prov) as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        a = cards[0]["task_id"]
        tick(c)
        # Plan is settled (first failed, rest blocked on it): the model is fed only the real facts.
        s = synth(c, sid)
        assert len(s) == 1
        facts = prov.calls[-1]["messages"][0]["content"]
        assert "0 of 3 steps completed, 1 failed, 2 blocked" in facts
        run(c, svc(c).retry_card, sid, plan["plan_id"], a)
        assert svc(c).plan_status(sid, plan["plan_id"])["synthesis"] is None   # stale one cleared
        for _ in range(4):
            tick(c)
        assert len(synth(c, sid)) == 2
        assert "3 of 3 steps completed" in prov.calls[-1]["messages"][0]["content"]
