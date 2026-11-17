"""A8: /btw intent routing, proposed actions, labelling, scoping, dedupe and rate limit."""
from __future__ import annotations

import asyncio

import pytest

from app.phase3.provider import ProviderRefused
from app.phase3.side import MAX_CONTEXT_CHARS, MAX_REPLY, is_change_request
from app.phase3.side_intent import classify, classify_intent
from tests.phase3.p3helpers import (FakeProvider, ask, make_task, open_session, p3_client,
                                    side_stack, store_state)

REQ = lambda **k: {"request_id": "r1", "agent_id": "developer", "visit_session_id": "v1",  # noqa: E731
                   "question": "status?", **k}


@pytest.mark.parametrize("q,intent", [
    ("What are you working on?", "current_task"),
    ("what are you doing right now", "current_task"),
    ("Why this approach?", "why"),
    ("why did you pick that", "why"),
    ("What have you completed so far?", "completed"),
    ("what is blocking you?", "blocking"),
    ("are you stuck?", "blocking"),
    ("Explain that test failure", "test_failure"),
    ("why are the tests failing", "test_failure"),
    ("What changed in this file?", "file_change"),
    ("show me the diff", "file_change"),
    ("how is it going", "status"),
    # negation-safe: negated clause is ignored
    ("I'm not asking about the tests, what are you working on?", "current_task"),
    ("it's not blocked, what changed in the diff", "file_change"),
    ("Why was the test skipped?", "why"),
])
def test_intent_classifier(q, intent):
    c = classify(q)
    assert not c.is_change and c.intent == intent == classify_intent(q)


@pytest.mark.parametrize("q,kind,negated", [
    ("Stop", "stop", False),
    ("please stop working on this", "stop", False),
    ("Change the approach to use Redis", "change", False),
    ("skip the tests", "skip_tests", False),
    ("Approve it", "approve", False),
    ("deploy it now", "deploy", False),
    ("don't approve it", "withhold_approval", True),
    ("Do not approve this yet", "withhold_approval", True),
    ("I don't want you to skip the tests", "keep_tests", True),
    ("never stop", "keep_running", True),
    ("Don't use Redis", "change", False),
    # rc14: spoken lead-ins with a comma (E2E journey D6)
    ("Actually, change the password rule to 12 characters.", "change", False),
    ("Developer, change the password rule to 12 characters", "change", False),
    ("OK, stop for now", "stop", False),
    ("Manager, don't approve it", "withhold_approval", True),
])
def test_change_requests_and_negation(q, kind, negated):
    c = classify(q)
    assert c.is_change and c.action_kind == kind and c.negated is negated


def test_questions_about_changes_stay_read_only():
    for q in ["Why did you not approve it?", "Did you skip the tests?", "Have you stopped?",
              "What would approving do?", "Are the tests passing?",
              "Developer, what are you working on right now?", "Actually, why did you pick that?",
              "So, have all the tests passed?", "Tester, what failed?"]:
        assert not is_change_request(q), q


def test_proposed_action_object_is_never_applied():
    prov = FakeProvider()
    with p3_client(provider=prov) as c:
        sid, h = open_session(c)
        t = make_task(c, sid, h, "Build login", agent="developer")
        before = store_state(c, sid)
        j = ask(c, sid, h, question="skip the tests", task_id=t["task_id"]).json()
        a = j["proposed_action"]
        assert j["status"] == "amendment_suggested" and j["intent"] == "change_request"
        assert a["kind"] == "skip_tests" and a["target_task_id"] == t["task_id"]
        assert a["requires_confirmation"] is True and a["applied"] is False
        assert a["text"] == "skip the tests" and a["negated"] is False
        assert j["proposed_amendment"]["requires_approval"] is True  # legacy alias
        j2 = ask(c, sid, h, question="don't approve it", task_id=t["task_id"],
                 key="ask-key-00000002", request_id="sq-2").json()
        assert j2["proposed_action"]["kind"] == "withhold_approval"
        assert j2["proposed_action"]["negated"] is True
        assert prov.calls == [] and store_state(c, sid) == before


def _seed_artifacts(c, sid, task_id):
    s2 = c.app.state.phase2_store
    for i, (kind, summary) in enumerate([("test_report", "12 passed, 1 failed: test_login_expiry"),
                                         ("code_diff", "auth.py +40 -3; session.py +8 -0"),
                                         ("implementation_plan", "Use JWT because sessions are stateless")]):
        s2.create_artifact_v2(f"art{i}", sid, task_id, kind, summary, "live", "h", [],
                              "2026-11-15T00:00:00Z")


def test_context_selected_by_intent_and_no_model_label():
    with p3_client(mode="live") as c:
        sid, h = open_session(c)
        t = make_task(c, sid, h, "Build login", agent="developer")
        _seed_artifacts(c, sid, t["task_id"])
        n = [0]

        def q(text):
            n[0] += 1
            return ask(c, sid, h, question=text, task_id=t["task_id"], key=f"k-{n[0]:010d}",
                       request_id=f"r{n[0]}").json()

        j = q("explain that test failure")
        assert j["intent"] == "test_failure" and "test_login_expiry" in j["reply"]
        assert "auth.py" not in j["reply"] and "test_report" in j["context_used"]
        assert j["label"] == "From task data (no model)"
        assert j["reply"].startswith("From task data (no model):")
        j = q("what changed in this file?")
        assert j["intent"] == "file_change" and "auth.py +40 -3" in j["reply"]
        assert "test_login_expiry" not in j["reply"]
        j = q("why this approach?")
        assert "JWT" in j["reply"]
        j = q("what is blocking you?")
        assert "Nothing is recorded as blocking 'Build login'" in j["reply"]
        j = q("what are you working on?")
        assert j["reply"].count("I'm on 'Build login'") == 1


def test_empty_context_says_so_and_never_fabricates():
    with p3_client(mode="live") as c:
        sid, h = open_session(c)
        t = make_task(c, sid, h, "Build login", agent="developer")
        j = ask(c, sid, h, question="explain the test failure", task_id=t["task_id"]).json()
        assert "No test report or failure is recorded" in j["reply"]
        j = ask(c, sid, h, question="what changed in the diff?", task_id=t["task_id"],
                key="ask-key-00000002", request_id="b").json()
        assert "No diff summary is recorded" in j["reply"]
        j = ask(c, sid, h, agent="tester", key="ask-key-00000003", request_id="c").json()
        assert j["status"] == "unavailable" and "no recorded task data" in j["reply"]


def test_demo_labelled_and_never_calls_model():
    prov = FakeProvider()
    with p3_client(mode="demo", provider=prov) as c:
        sid, h = open_session(c)
        t = make_task(c, sid, h, "Build login", agent="developer")
        j = ask(c, sid, h, question="what are you working on?", task_id=t["task_id"]).json()
        assert j["label"] == "[SCRIPTED DEMO]" and j["reply"].startswith("[SCRIPTED DEMO]")
        assert j["provenance"] == "SCRIPTED_DEMO" and prov.calls == []


def test_model_answer_labelled_and_context_bounded():
    prov = FakeProvider(reply="Working on login.")
    with p3_client(mode="live", provider=prov) as c:
        sid, h = open_session(c)
        t = make_task(c, sid, h, "Build login", agent="developer")
        s2 = c.app.state.phase2_store
        for i in range(8):
            s2.create_artifact_v2(f"a{i}", sid, t["task_id"], "test_report", "x" * 400, "live",
                                  "h", [], "2026-11-15T00:00:00Z")
        j = ask(c, sid, h, question="explain the test failure", task_id=t["task_id"]).json()
        assert j["label"] == "Model-written" and j["provenance"] == "LIVE_MODEL"
        sent = prov.calls[0]["messages"][-1]["content"]
        ctx = sent.split("Context:\n", 1)[1].split("\n\nQuestion:", 1)[0]
        assert len(ctx) <= MAX_CONTEXT_CHARS + 30 and "Intent: test_failure" in ctx
        s = prov.calls[0]["system"].lower()
        assert "read-only" in s and "only from the context" in s
        assert prov.calls[0]["purpose"] == "side"


def test_long_model_reply_truncated_and_flagged():
    async def body():
        svc, _, graph = side_stack(FakeProvider(reply=("Sentence one. " * 400)))
        graph.create_task("s", "T", "developer", 1, [], None)
        r = await svc.ask("s", REQ())
        assert r["truncated"] is True and r["reply"].endswith("[truncated]")
        assert len(r["reply"]) <= MAX_REPLY + 20
    asyncio.run(body())


def test_refusal_gives_safe_message_and_is_not_cached():
    class Refuser(FakeProvider):
        async def complete(self, **kw):
            raise ProviderRefused("no")

    async def body():
        svc, _, graph = side_stack(Refuser())
        graph.create_task("s", "T", "developer", 1, [], None)
        r = await svc.ask("s", REQ())
        assert r["status"] == "model_declined" and "will not guess" in r["reply"]
        assert r["provenance"] == "TASK_SNAPSHOT" and r["label"] == "From task data (no model)"
    asyncio.run(body())


def test_task_of_another_agent_or_session_rejected():
    with p3_client() as c:
        sid, h = open_session(c)
        sid2, h2 = open_session(c)
        mine = make_task(c, sid, h, "Mine", agent="researcher", key="mk-task-00000001")
        other = make_task(c, sid2, h2, "Theirs", agent="developer", key="mk-task-00000002")
        j = ask(c, sid, h, agent="developer", task_id=other["task_id"]).json()
        assert j["status"] == "unavailable" and j["task_snapshot"] is None
        j = ask(c, sid, h, agent="developer", task_id=mine["task_id"],
                key="ask-key-00000002", request_id="b").json()
        assert j["status"] == "unavailable"          # researcher's task, developer asked
        j = ask(c, sid, h, agent="manager", task_id=mine["task_id"],
                key="ask-key-00000003", request_id="c").json()
        assert j["status"] == "answered"


def test_identical_rapid_questions_are_deduped_without_second_model_call():
    prov = FakeProvider()

    async def body():
        t = [100.0]
        svc, _, graph = side_stack(prov)
        svc._clock = lambda: t[0]
        graph.create_task("s", "T", "developer", 1, [], None)
        a = await svc.ask("s", REQ(request_id="a", question="What are you working on?"))
        b = await svc.ask("s", REQ(request_id="b", question="what are you working on??"))
        assert b["deduped"] is True and b["request_id"] == "b" and b["reply"] == a["reply"]
        assert len(prov.calls) == 1
        t[0] += 10   # window passed -> a fresh answer
        c = await svc.ask("s", REQ(request_id="c", question="What are you working on?"))
        assert c["deduped"] is False and len(prov.calls) == 2
    asyncio.run(body())


def test_rate_limit_per_visit():
    async def body():
        t = [0.0]
        svc, _, graph = side_stack(FakeProvider())
        svc._clock = lambda: t[0]
        graph.create_task("s", "T", "developer", 1, [], None)
        for i in range(12):
            r = await svc.ask("s", REQ(request_id=f"r{i}", question=f"q{i}?"))
            assert r["status"] == "answered"
        r = await svc.ask("s", REQ(request_id="x", question="one more?"))
        assert r["status"] == "rate_limited" and r["provenance"] == "NONE"
        other = await svc.ask("s", REQ(request_id="y", visit_session_id="v2", question="hi?"))
        assert other["status"] == "answered"       # another visit has its own budget
        t[0] += 31
        assert (await svc.ask("s", REQ(request_id="z", question="again?")))["status"] == "answered"
    asyncio.run(body())
