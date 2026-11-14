"""Read-only /btw side conversation (Phase 3)."""
from __future__ import annotations

import asyncio

from tests.phase3.p3helpers import (FakeProvider, ask, make_task, open_session, p3_client,
                                    side_stack, store_state)

REQ = lambda **k: {"request_id": "r1", "agent_id": "developer", "visit_session_id": "v1",  # noqa: E731
                   "question": "status?", **k}


def test_flag_disabled_returns_feature_disabled():
    with p3_client(p3_side_conversation_enabled=False) as c:
        sid, h = open_session(c)
        r = ask(c, sid, h)
        assert r.status_code == 404 and r.json()["code"] == "FEATURE_DISABLED"
        d = c.delete(f"/v2/sessions/{sid}/agents/developer/visits/v1", headers=h)
        assert d.status_code == 404 and d.json()["code"] == "FEATURE_DISABLED"


def test_requires_bearer_token():
    with p3_client() as c:
        sid, _ = open_session(c)
        assert ask(c, sid, {}).status_code == 401


def test_snapshot_answer_is_read_only_and_grounded():
    with p3_client() as c:
        sid, h = open_session(c)
        t = make_task(c, sid, h, "Build login", agent="developer")
        before = store_state(c, sid)
        r = ask(c, sid, h, task_id=t["task_id"])
        assert r.status_code == 200, r.text
        j = r.json()
        assert j["status"] == "answered" and j["provenance"] == "TASK_SNAPSHOT"
        assert "Task 'Build login' is queued, revision 0" in j["reply"]
        assert j["task_snapshot"] == {"task_id": t["task_id"], "title": "Build login",
                                      "state": "queued", "agent_id": "developer", "revision": 0}
        assert j["proposed_amendment"] is None and j["stale_context"] is False
        assert store_state(c, sid) == before  # tasks + events + last_seq byte-identical


def test_agent_without_explicit_task_uses_its_own_latest():
    with p3_client() as c:
        sid, h = open_session(c)
        make_task(c, sid, h, "Research A", agent="researcher", key="mk-task-00000001")
        make_task(c, sid, h, "Dev B", agent="developer", key="mk-task-00000002")
        j = ask(c, sid, h, agent="developer").json()
        assert j["task_snapshot"]["title"] == "Dev B"


def test_unknown_agent_and_unknown_task_unavailable():
    with p3_client() as c:
        sid, h = open_session(c)
        make_task(c, sid, h)
        j = ask(c, sid, h, agent="janitor").json()
        assert j["status"] == "unavailable" and j["provenance"] == "NONE"
        j = ask(c, sid, h, agent="developer", task_id="t2-nope", key="ask-key-00000002").json()
        assert j["status"] == "unavailable" and j["task_snapshot"] is None
        # agent with no data at all
        j = ask(c, sid, h, agent="tester", key="ask-key-00000003").json()
        assert j["status"] == "unavailable"


def test_other_sessions_task_is_unavailable():
    with p3_client() as c:
        sid, h = open_session(c)
        sid2, h2 = open_session(c)
        t = make_task(c, sid2, h2)
        j = ask(c, sid, h, task_id=t["task_id"]).json()
        assert j["status"] == "unavailable"


def test_stale_revision_flagged_and_current_returned():
    with p3_client() as c:
        sid, h = open_session(c)
        t = make_task(c, sid, h, agent="developer")
        r = c.post(f"/v2/sessions/{sid}/tasks/{t['task_id']}/priority", headers=h,
                   json={"schema_version": 2, "priority": 2, "expected_revision": 0,
                         "idempotency_key": "prio-key-00000001"})
        assert r.status_code == 200 and r.json()["revision"] == 1
        j = ask(c, sid, h, task_id=t["task_id"], context_revision=0).json()
        assert j["stale_context"] is True and j["context_revision"] == 1
        assert "revision 1" in j["reply"]
        j = ask(c, sid, h, task_id=t["task_id"], context_revision=1, key="ask-key-00000002").json()
        assert j["stale_context"] is False


def test_change_request_becomes_amendment_without_mutation():
    prov = FakeProvider()
    with p3_client(provider=prov) as c:
        sid, h = open_session(c)
        t = make_task(c, sid, h, agent="developer")
        before = store_state(c, sid)
        for i, q in enumerate(["Cancel this task", "please switch to Postgres instead",
                               "Can you delete the old migrations?", "Deploy it now",
                               "Use Redis for the cache"]):
            j = ask(c, sid, h, question=q, task_id=t["task_id"], key=f"amend-key-{i:08d}").json()
            assert j["status"] == "amendment_suggested", q
            assert j["proposed_amendment"]["requires_approval"] is True
            assert "not applied" in j["proposed_amendment"]["summary"].lower()
            assert "did not apply" in j["reply"]
        assert prov.calls == []                     # never even sent to the model
        assert store_state(c, sid) == before


def test_questions_are_not_mistaken_for_changes():
    from app.phase3.side import is_change_request
    for q in ["Why did you use OAuth?", "What is the status?", "Why was the test skipped?",
              "How does the change affect login?", "Do you think we should deploy?"]:
        assert not is_change_request(q), q


def test_concurrency_cap_returns_busy():
    async def body():
        prov = FakeProvider(delay=0.2)
        svc, s2, graph = side_stack(prov, p3_side_max_concurrency=1, p3_side_timeout_seconds=5)
        graph.create_task("s", "Task", "developer", 1, [], None)
        a = asyncio.create_task(svc.ask("s", REQ()))
        await asyncio.sleep(0.05)
        b = await svc.ask("s", REQ(request_id="r2"))
        assert b["status"] == "busy" and b["provenance"] == "NONE"
        assert (await a)["status"] == "answered"
        # capacity returns afterwards
        assert (await svc.ask("s", REQ(request_id="r3")))["status"] == "answered"
    asyncio.run(body())


def test_slow_provider_times_out_with_snapshot():
    async def body():
        svc, s2, graph = side_stack(FakeProvider(delay=1.0), p3_side_timeout_seconds=0.05)
        graph.create_task("s", "Slow", "developer", 1, [], None)
        r = await svc.ask("s", REQ())
        assert r["status"] == "timeout" and r["provenance"] == "TASK_SNAPSHOT"
        assert "Task 'Slow' is queued" in r["reply"]
    asyncio.run(body())


def test_provider_failure_falls_back_to_snapshot():
    async def body():
        svc, s2, graph = side_stack(FakeProvider(fail=True))
        graph.create_task("s", "Fails", "developer", 1, [], None)
        r = await svc.ask("s", REQ())
        assert r["status"] == "answered" and r["provenance"] == "TASK_SNAPSHOT"
        assert "unavailable" in r["reply"]
    asyncio.run(body())


def test_timeout_and_busy_are_not_cached_for_idempotent_retry():
    prov = FakeProvider(delay=1.0)
    with p3_client(provider=prov, p3_side_timeout_seconds=0.05) as c:
        sid, h = open_session(c)
        make_task(c, sid, h, agent="developer")
        assert ask(c, sid, h).json()["status"] == "timeout"
        prov.delay = 0.0
        assert ask(c, sid, h).json()["status"] == "answered"   # same key retried, not replayed


def test_provenance_per_mode():
    # scripted demo: never calls the model, labelled SCRIPTED_DEMO
    prov = FakeProvider()
    with p3_client(mode="demo", provider=prov) as c:
        sid, h = open_session(c)
        make_task(c, sid, h, agent="developer")
        j = ask(c, sid, h).json()
        assert j["provenance"] == "SCRIPTED_DEMO" and j["reply"].startswith("[SCRIPTED DEMO]")
        assert prov.calls == []
    # live, no model: grounded snapshot
    with p3_client(mode="live") as c:
        sid, h = open_session(c)
        make_task(c, sid, h, agent="developer")
        assert ask(c, sid, h).json()["provenance"] == "TASK_SNAPSHOT"
    # live + model
    prov = FakeProvider(reply="It is queued and has not started.")
    with p3_client(mode="live", provider=prov) as c:
        sid, h = open_session(c)
        make_task(c, sid, h, agent="developer")
        j = ask(c, sid, h).json()
        assert j["provenance"] == "LIVE_MODEL" and j["reply"] == "It is queued and has not started."
        assert "Question:" in prov.calls[0]["messages"][-1]["content"]
        assert "tools" not in prov.calls[0]


def test_model_context_has_no_secrets():
    prov = FakeProvider()
    with p3_client(provider=prov) as c:
        sid, h = open_session(c)
        make_task(c, sid, h, "Rotate token=abcd1234efgh5678 in C:\\Users\\me\\secrets.txt",
                  agent="developer")
        ask(c, sid, h)
        sent = prov.calls[0]["messages"][-1]["content"]
        assert "abcd1234efgh5678" not in sent and "C:\\Users" not in sent


def test_visit_memory_bounded_and_cleared_by_delete():
    prov = FakeProvider()
    with p3_client(provider=prov) as c:
        sid, h = open_session(c)
        make_task(c, sid, h, agent="developer")
        svc = c.app.state.p3_side
        for i in range(10):
            r = ask(c, sid, h, question=f"q{i}?", key=f"hist-key-{i:08d}", request_id=f"r{i}")
            assert r.json()["status"] == "answered"
        svc = c.app.state.p3_side
        assert svc.history_len(sid, "visit-1") == 8                 # last 8 exchanges only
        assert len(prov.calls[-1]["messages"]) == 8 * 2 + 1         # history + current
        d = c.delete(f"/v2/sessions/{sid}/agents/developer/visits/visit-1", headers=h)
        assert d.status_code == 200 and d.json()["cleared_exchanges"] == 8
        assert svc.history_len(sid, "visit-1") == 0
        ask(c, sid, h, question="fresh?", key="hist-key-fresh0001")
        assert len(prov.calls[-1]["messages"]) == 1                 # no leaked history
        assert c.delete(f"/v2/sessions/{sid}/agents/developer/visits/nope", headers=h).json()[
            "cleared_exchanges"] == 0


def test_idempotent_replay_and_key_reuse():
    prov = FakeProvider()
    with p3_client(provider=prov) as c:
        sid, h = open_session(c)
        t = make_task(c, sid, h, agent="developer")
        a = ask(c, sid, h, task_id=t["task_id"], key="same-key-0000001")
        b = ask(c, sid, h, task_id=t["task_id"], key="same-key-0000001")
        assert a.json() == b.json() and len(prov.calls) == 1
        diff = ask(c, sid, h, question="something else?", task_id=t["task_id"],
                   key="same-key-0000001")
        assert diff.status_code == 409 and diff.json()["code"] == "IDEMPOTENCY_KEY_REUSE"


def test_manager_answers_about_board_and_plan():
    with p3_client() as c:
        sid, h = open_session(c)
        make_task(c, sid, h, "A", key="mk-task-00000001")
        make_task(c, sid, h, "B", key="mk-task-00000002")
        j = ask(c, sid, h, agent="manager", question="What is on the board?").json()
        assert j["status"] == "answered" and j["task_snapshot"] is None
        assert "2 task(s)" in j["reply"] and "2 queued" in j["reply"]
        assert j["context_revision"] > 0
