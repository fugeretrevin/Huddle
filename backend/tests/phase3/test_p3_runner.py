"""Plan runner: works only committed-plan cards, in dependency order, honestly labelled."""
from __future__ import annotations

import asyncio
import json
from types import SimpleNamespace

import pytest

from app.phase3.provider import AnthropicProvider, ProviderRefused, build_provider
from app.settings import Settings
from tests.phase3.p3helpers import FakeProvider, event_types, make_task, open_session, p3_client
from tests.phase3.test_p3_manager import commit, propose


def _client(mode="live", provider=None, **kw):
    return p3_client(mode=mode, provider=provider, p3_runner_demo_step_seconds=0.0, **kw)


def _committed(c, sid, h, objective="Build the login flow"):
    # Plan with no model (template), then restore the test's provider for the work step.
    prov, c.app.state.p3_provider = c.app.state.p3_provider, None
    r = propose(c, sid, h, objective)
    c.app.state.p3_provider = prov
    assert r.status_code == 201, r.text
    plan = r.json()
    r = commit(c, sid, h, plan)
    assert r.status_code == 200, r.text
    return plan, r.json()["tasks"]


def tick(c) -> int:
    return c.portal.call(c.app.state.p3_runner.tick)


def state(c, task_id) -> str:
    return c.app.state.phase2_store.get_task_v2(task_id)["task_state"]


def artifacts(c, sid, task_id):
    return [a for a in c.app.state.phase2_store.list_artifacts_v2(sid)
            if a["task_id"] == task_id and a["kind"] != "completion"]   # not the plan summary


def test_demo_mode_works_chain_in_order_with_scripted_label_and_handoffs():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        manual = make_task(c, sid, h, title="Hand-made card")
        _, cards = _committed(c, sid, h)
        a, b, t = (x["task_id"] for x in cards)
        assert [state(c, x) for x in (a, b, t)] == ["queued", "blocked", "blocked"]

        assert tick(c) == 1
        assert [state(c, x) for x in (a, b, t)] == ["completed", "queued", "blocked"]
        assert tick(c) == 1 and tick(c) == 1
        assert [state(c, x) for x in (a, b, t)] == ["completed"] * 3
        assert tick(c) == 0                                # nothing left
        assert state(c, manual["task_id"]) == "queued"     # never touched

        for x in (a, b, t):
            arts = artifacts(c, sid, x)
            assert len(arts) == 1 and arts[0]["source_mode"] == "demo"
            assert "SCRIPTED DEMO" in json.loads(arts[0]["pages_json"])[0]["content"]
        assert artifacts(c, sid, t)[0]["kind"] == "implementation_plan"   # never a test_report

        evs = c.app.state.phase2_store.events_after_v2(sid, 0)
        handoffs = [(e["payload"]["from_agent_id"], e["payload"]["to_agent_id"], e["payload"]["kind"])
                    for e in evs if e["type"] == "handoff.created"]
        assert handoffs == [("researcher", "developer", "blueprint"),
                            ("developer", "tester", "test_request")]
        seq = [(e["type"], e.get("task_id")) for e in evs if e.get("task_id") == a
               and e["type"] in ("task.started", "task.completed")]
        assert seq == [("task.started", a), ("task.completed", a)]


def test_live_mode_uses_model_with_work_purpose_and_dependency_inputs():
    prov = FakeProvider("Findings here.\nSummary: use the existing session store.")
    with _client(provider=prov) as c:
        sid, h = open_session(c)
        _, cards = _committed(c, sid, h)
        planner_calls = len(prov.calls)
        tick(c)
        tick(c)
        work = prov.calls[planner_calls:]
        assert [w["purpose"] for w in work] == ["work", "work"]
        assert "Findings here." in work[1]["messages"][0]["content"]   # step b sees step a
        art = artifacts(c, sid, cards[0]["task_id"])[0]
        page = json.loads(art["pages_json"])[0]["content"]
        assert page.startswith("Model-written, no tools")
        assert art["summary"].endswith("use the existing session store.")
        done = [e for e in c.app.state.phase2_store.events_after_v2(sid, 0)
                if e["type"] == "task.completed"]
        assert done[0]["payload"]["work_mode"] == "model_text"


def test_live_mode_without_model_leaves_cards_queued_and_warns_once():
    with _client() as c:
        sid, h = open_session(c)
        _, cards = _committed(c, sid, h)
        assert tick(c) == 0 and tick(c) == 0
        assert state(c, cards[0]["task_id"]) == "queued"
        assert event_types(c, sid).count("system.warning") == 1


def test_model_failure_fails_card_and_dependents_stay_blocked():
    with _client(provider=FakeProvider(fail=True)) as c:
        sid, h = open_session(c)
        _, cards = _committed(c, sid, h)
        tick(c)
        assert state(c, cards[0]["task_id"]) == "failed"
        assert state(c, cards[1]["task_id"]) == "blocked"
        assert artifacts(c, sid, cards[0]["task_id"]) == []
        assert tick(c) == 0


def test_model_timeout_fails_card():
    with _client(provider=FakeProvider(delay=1.0), p3_work_timeout_seconds=0.05) as c:
        sid, h = open_session(c)
        _, cards = _committed(c, sid, h)
        tick(c)
        assert state(c, cards[0]["task_id"]) == "failed"


class PausingProvider(FakeProvider):
    """Pauses the card being worked while the 'model' is answering."""

    def __init__(self, client, sid, headers):
        super().__init__("Done.\nSummary: ok")
        self.c, self.sid, self.h = client, sid, headers
        self.target = None

    async def complete(self, *, system, messages, max_tokens=600, purpose="side"):
        if purpose == "work" and self.target:
            s2 = self.c.app.state.phase2_store
            s2.update_task_v2(self.target, task_state="paused", now="2026-11-15T00:00:00Z")
        return await super().complete(system=system, messages=messages, purpose=purpose)


def test_card_paused_while_worked_discards_result():
    with _client() as c:
        sid, h = open_session(c)
        prov = PausingProvider(c, sid, h)
        c.app.state.p3_provider = prov
        _, cards = _committed(c, sid, h)
        prov.target = cards[0]["task_id"]
        tick(c)
        assert state(c, cards[0]["task_id"]) == "paused"
        assert artifacts(c, sid, cards[0]["task_id"]) == []
        assert "task.completed" not in event_types(c, sid)


def test_optional_pipeline_gate_holds_runner():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        _, cards = _committed(c, sid, h)
        runner = c.app.state.p3_runner
        runner._pipeline_busy = lambda s: s == sid   # noqa: SLF001
        assert tick(c) == 0
        assert state(c, cards[0]["task_id"]) == "queued"


def test_recover_fails_cards_left_running():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        _, cards = _committed(c, sid, h)
        s2 = c.app.state.phase2_store
        s2.update_task_v2(cards[0]["task_id"], task_state="running", now="2026-11-15T00:00:00Z")
        assert c.portal.call(c.app.state.p3_runner.recover) == 1
        assert state(c, cards[0]["task_id"]) == "failed"


def test_uncommitted_plan_cards_do_not_exist_and_nothing_runs():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        assert propose(c, sid, h).status_code == 201
        assert tick(c) == 0
        assert c.app.state.phase2_store.list_tasks_v2(sid) == []


# ─────────────────────── provider ───────────────────────
class _FakeMessages:
    def __init__(self, resp):
        self.resp, self.kwargs = resp, None

    async def create(self, **kw):
        self.kwargs = kw
        return self.resp


def _fake_client(text="Hello", stop="end_turn"):
    resp = SimpleNamespace(stop_reason=stop, content=[
        SimpleNamespace(type="thinking", thinking=""), SimpleNamespace(type="text", text=text)])
    beta = _FakeMessages(resp)
    plain = _FakeMessages(resp)
    return SimpleNamespace(beta=SimpleNamespace(messages=beta), messages=plain), beta, plain


def test_anthropic_provider_request_shape():
    client, beta, _ = _fake_client(" Answer. ")
    p = AnthropicProvider(model="claude-opus-5-5", efforts={"side": "low", "work": "medium"},
                          client=client)
    out = asyncio.run(p.complete(system="S", messages=[{"role": "user", "content": "q"}],
                                 max_tokens=400, purpose="side"))
    assert out == "Answer."
    kw = beta.kwargs
    assert kw["model"] == "claude-opus-5-5" and kw["max_tokens"] >= 4000
    assert kw["output_config"] == {"effort": "low"} and kw["system"] == "S"
    assert kw["fallbacks"] == "default" and kw["betas"] == ["server-side-fallback-2026-07-01"]
    assert "thinking" not in kw and "temperature" not in kw


def test_anthropic_provider_without_fallbacks_uses_plain_endpoint():
    client, beta, plain = _fake_client()
    p = AnthropicProvider(model="m", efforts={}, client=client, use_fallbacks=False)
    asyncio.run(p.complete(system="S", messages=[], purpose="work"))
    assert beta.kwargs is None and plain.kwargs["output_config"] == {"effort": "medium"}


def test_anthropic_provider_refusal_raises():
    client, _, _ = _fake_client(stop="refusal")
    p = AnthropicProvider(model="m", efforts={}, client=client)
    with pytest.raises(ProviderRefused):
        asyncio.run(p.complete(system="S", messages=[]))


def test_build_provider_needs_name_and_key(monkeypatch):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    base = dict(db_path=":memory:")
    assert build_provider(Settings(**base, model_provider=None)) is None
    assert build_provider(Settings(**base, model_provider="anthropic", provider_api_key=None)) is None
    assert build_provider(Settings(**base, model_provider="other", provider_api_key="k")) is None
    p = build_provider(Settings(**base, model_provider="anthropic", provider_api_key="sk-test"))
    assert isinstance(p, AnthropicProvider) and p.model == "claude-opus-5-5"
    monkeypatch.setenv("ANTHROPIC_API_KEY", "sk-env")
    assert isinstance(build_provider(Settings(**base, model_provider="claude")), AnthropicProvider)
