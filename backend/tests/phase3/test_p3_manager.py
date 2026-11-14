"""Manager plans: validation, propose/revise/reject, atomic idempotent commit."""
from __future__ import annotations

import copy
import json

from app.phase2.task_graph import TaskGraphService
from app.phase3.validation import find_cycle, topological_order, validate_items
from tests.phase3.p3helpers import (FakeProvider, event_types, make_task, open_session,
                                    p3_client)


def item(cid, kind="code", agent="developer", deps=(), title=None, perm="workspace_write",
         criteria=("works",)):
    return {"client_item_id": cid, "title": title or f"Do {cid}", "kind": kind,
            "proposed_agent": agent, "depends_on": list(deps),
            "acceptance_criteria": list(criteria), "required_permission": perm}


CHAIN = [item("a", "research", "researcher", perm="read_repo"),
         item("b", "code", "developer", ["a"]),
         item("c", "test", "tester", ["b"], perm="run_tests")]


# ─────────────────────── pure validation ───────────────────────
def test_valid_chain_passes_and_orders():
    r = validate_items(CHAIN)
    assert r.ok and [i["dangerous"] for i in r.items] == [False] * 3
    assert topological_order({"c": ["b"], "b": ["a"], "a": []}) == ["a", "b", "c"]


def test_cycle_rejected_and_reported():
    r = validate_items([item("a", deps=["b"]), item("b", deps=["a"])])
    assert "DEPENDENCY_CYCLE" in r.codes()
    assert r.cycle and r.cycle[0] == r.cycle[-1] and set(r.cycle) == {"a", "b"}
    assert "->" in [i for i in r.issues if i.code == "DEPENDENCY_CYCLE"][0].message
    assert "DEPENDENCY_CYCLE" in validate_items([item("a", deps=["a"])]).codes()
    assert find_cycle({"a": ["b"], "b": ["c"], "c": []}) is None


def test_bad_dependency_and_duplicate_ids():
    assert "UNKNOWN_DEPENDENCY" in validate_items([item("a", deps=["zzz"])]).codes()
    assert "DUPLICATE_ITEM_ID" in validate_items([item("a"), item("a")]).codes()
    assert "EMPTY_PLAN" in validate_items([]).codes()
    assert "TOO_MANY_ITEMS" in validate_items([item(f"i{n}") for n in range(13)]).codes()


def test_manager_cannot_own_items():
    r = validate_items([item("a", "research", "manager")])
    assert "MANAGER_CANNOT_OWN" in r.codes()
    assert "UNKNOWN_AGENT" in validate_items([item("a", agent="janitor")]).codes()


def test_kind_role_mismatch_and_allowlists():
    assert "ROLE_KIND_MISMATCH" in validate_items([item("a", "code", "tester")]).codes()
    assert "ROLE_KIND_MISMATCH" in validate_items([item("a", "test", "developer")]).codes()
    assert "ROLE_KIND_MISMATCH" in validate_items([item("a", "research", "developer")]).codes()
    assert validate_items([item("a", "doc", "researcher")]).ok
    assert validate_items([item("a", "doc", "developer")]).ok
    assert "ROLE_KIND_MISMATCH" in validate_items([item("a", "doc", "tester")]).codes()
    assert "UNKNOWN_KIND" in validate_items([item("a", "magic")]).codes()
    assert "UNKNOWN_PERMISSION" in validate_items([item("a", perm="root")]).codes()
    assert "BAD_TITLE" in validate_items([item("a", title="x" * 121)]).codes()


def test_danger_detection():
    cases = {"Deploy to production": "deployment", "rm -rf the build dir": "deletion",
             "Delete old data": "deletion", "Purchase a licence": "spending",
             "Rotate the API key": "credentials", "git push to origin": "push to remote",
             "Send an email to the customer": "external network post"}
    for title, reason in cases.items():
        it = validate_items([item("a", title=title)]).items[0]
        assert it["dangerous"] and reason in it["danger_reasons"], title
    safe = validate_items([item("a", title="Build the login flow")]).items[0]
    assert not safe["dangerous"] and safe["danger_reasons"] == []
    crit = validate_items([item("a", criteria=("password stored in vault",))]).items[0]
    assert crit["dangerous"]


def test_client_cannot_clear_danger_flag():
    raw = item("a", title="Deploy to production")
    raw["dangerous"] = False
    assert validate_items([raw]).items[0]["dangerous"] is True


# ─────────────────────── http helpers ───────────────────────
def propose(c, sid, h, objective="Build the login flow", key="prop-key-00000001"):
    return c.post(f"/v2/sessions/{sid}/manager/plans", headers=h,
                  json={"request_id": "req-1", "objective": objective, "idempotency_key": key})


def commit(c, sid, h, plan, approved=None, authorized=(), key="commit-key-000001", revision=None):
    return c.post(f"/v2/sessions/{sid}/manager/plans/{plan['plan_id']}/commit", headers=h, json={
        "revision": plan["revision"] if revision is None else revision,
        "approved_item_ids": approved if approved is not None else [i["client_item_id"] for i in plan["items"]],
        "authorized_dangerous_item_ids": list(authorized), "idempotency_key": key})


def revise(c, sid, h, plan, items, key="revise-key-00000001"):
    return c.post(f"/v2/sessions/{sid}/manager/plans/{plan['plan_id']}/revise", headers=h,
                  json={"revision": plan["revision"], "items": items, "idempotency_key": key})


def tasks(c, sid):
    return c.app.state.phase2_store.list_tasks_v2(sid)


# ─────────────────────── propose ───────────────────────
def test_flag_disabled():
    with p3_client(p3_manager_enabled=False) as c:
        sid, h = open_session(c)
        r = propose(c, sid, h)
        assert r.status_code == 404 and r.json()["code"] == "FEATURE_DISABLED"


def test_template_plan_is_labelled_and_creates_nothing():
    with p3_client(mode="live") as c:
        sid, h = open_session(c)
        r = propose(c, sid, h)
        assert r.status_code == 201, r.text
        p = r.json()
        assert p["provenance"] == "TEMPLATE_PLAN" and "template-based" in p["provenance_note"]
        assert p["status"] == "proposed" and p["revision"] == 1
        assert [i["proposed_agent"] for i in p["items"]] == ["researcher", "developer", "tester"]
        assert p["items"][1]["depends_on"] == ["a"] and p["items"][2]["depends_on"] == ["b"]
        assert p["source_request_id"] == "req-1" and p["committed_tasks"] == []
        assert tasks(c, sid) == []
        assert event_types(c, sid) == ["manager.plan_proposed"]   # no task.created
        got = c.get(f"/v2/sessions/{sid}/manager/plans/{p['plan_id']}", headers=h).json()
        assert got == p


def test_scripted_mode_plan_is_labelled_scripted_demo():
    with p3_client(mode="demo", provider=FakeProvider("{}")) as c:
        sid, h = open_session(c)
        p = propose(c, sid, h).json()
        assert p["provenance"] == "SCRIPTED_DEMO" and "SCRIPTED DEMO" in p["provenance_note"]


def test_propose_idempotent_and_unknown_plan_404():
    with p3_client() as c:
        sid, h = open_session(c)
        a, b = propose(c, sid, h), propose(c, sid, h)
        assert a.json() == b.json()
        assert event_types(c, sid).count("manager.plan_proposed") == 1
        assert propose(c, sid, h, objective="Something else").status_code == 409
        assert c.get(f"/v2/sessions/{sid}/manager/plans/mp-nope", headers=h).status_code == 404
        sid2, h2 = open_session(c)
        assert c.get(f"/v2/sessions/{sid2}/manager/plans/{a.json()['plan_id']}",
                     headers=h2).status_code == 404


def model_plan(items, **extra):
    return json.dumps({"title": "Model plan", "assumptions": ["a1"], "risks": [], "items": items,
                       **extra})


def test_model_plan_valid_is_live_model():
    prov = FakeProvider(model_plan(CHAIN))
    with p3_client(provider=prov) as c:
        sid, h = open_session(c)
        p = propose(c, sid, h).json()
        assert p["provenance"] == "LIVE_MODEL" and p["title"] == "Model plan"
        assert "tools" not in prov.calls[0]


def test_model_plan_with_fenced_json_accepted():
    prov = FakeProvider("```json\n" + model_plan(CHAIN) + "\n```")
    with p3_client(provider=prov) as c:
        sid, h = open_session(c)
        assert propose(c, sid, h).status_code == 201


def test_model_garbage_and_cycle_rejected_nothing_stored():
    cases = [("not json at all", "NOT_JSON"),
             (model_plan([item("a", deps=["b"]), item("b", deps=["a"])]), "DEPENDENCY_CYCLE"),
             (model_plan([item("a", "research", "manager")]), "MANAGER_CANNOT_OWN"),
             (model_plan([item("a", "code", "tester")]), "ROLE_KIND_MISMATCH"),
             (model_plan("nope"), "EMPTY_PLAN"),
             ('["a list"]', "NOT_JSON")]
    for n, (text, code) in enumerate(cases):
        with p3_client(provider=FakeProvider(text)) as c:
            sid, h = open_session(c)
            r = propose(c, sid, h)
            assert r.status_code == 422 and r.json()["code"] == "MODEL_PLAN_INVALID", text
            assert code in [d["code"] for d in r.json()["details"]], text
            assert event_types(c, sid) == [] and tasks(c, sid) == []


def test_model_dangerous_flag_computed_by_backend_not_model():
    liar = item("a", "code", "developer", title="Deploy to production")
    liar["dangerous"] = False
    with p3_client(provider=FakeProvider(model_plan([liar]))) as c:
        sid, h = open_session(c)
        assert propose(c, sid, h).json()["items"][0]["dangerous"] is True


def test_model_failure_modes():
    with p3_client(provider=FakeProvider(fail=True)) as c:
        sid, h = open_session(c)
        r = propose(c, sid, h)
        assert r.status_code == 503 and r.json()["code"] == "PLANNER_UNAVAILABLE"
    with p3_client(provider=FakeProvider(delay=1.0), p3_plan_timeout_seconds=0.05) as c:
        sid, h = open_session(c)
        r = propose(c, sid, h)
        assert r.status_code == 504 and r.json()["code"] == "PLANNER_TIMEOUT"


# ─────────────────────── revise / reject ───────────────────────
def test_revise_bumps_revision_and_revalidates():
    with p3_client() as c:
        sid, h = open_session(c)
        p = propose(c, sid, h).json()
        bad = revise(c, sid, h, p, [item("a", deps=["b"]), item("b", deps=["a"])])
        assert bad.status_code == 422 and bad.json()["code"] == "PLAN_INVALID"
        ok = revise(c, sid, h, p, CHAIN[:2], key="revise-key-00000002")
        assert ok.status_code == 200 and ok.json()["revision"] == 2 and len(ok.json()["items"]) == 2
        stale = revise(c, sid, h, p, CHAIN, key="revise-key-00000003")      # still revision 1
        assert stale.status_code == 409 and stale.json()["code"] == "REVISION_CONFLICT"
        assert stale.json()["canonical_plan"]["revision"] == 2
        assert event_types(c, sid) == ["manager.plan_proposed", "manager.plan_revised"]


def test_reject_closes_plan():
    with p3_client() as c:
        sid, h = open_session(c)
        p = propose(c, sid, h).json()
        r = c.post(f"/v2/sessions/{sid}/manager/plans/{p['plan_id']}/reject", headers=h,
                   json={"revision": 1, "reason": "too broad", "idempotency_key": "rej-key-00000001"})
        assert r.status_code == 200
        assert r.json()["status"] == "rejected" and r.json()["revision"] == 2
        assert r.json()["reject_reason"] == "too broad"
        again = commit(c, sid, h, r.json())
        assert again.status_code == 409 and again.json()["code"] == "PLAN_NOT_OPEN"
        assert tasks(c, sid) == []
        assert event_types(c, sid)[-1] == "manager.plan_rejected"


# ─────────────────────── commit ───────────────────────
def test_commit_creates_cards_with_canonical_dependencies():
    with p3_client() as c:
        sid, h = open_session(c)
        p = propose(c, sid, h).json()
        r = commit(c, sid, h, p)
        assert r.status_code == 200, r.text
        res = r.json()
        assert res["status"] == "committed" and res["revision"] == 2
        by = {t["client_item_id"]: t for t in res["tasks"]}
        assert [t["client_item_id"] for t in res["tasks"]] == ["a", "b", "c"]   # topological
        assert [by[k]["agent_id"] for k in "abc"] == ["researcher", "developer", "tester"]
        rows = {r["id"]: r for r in tasks(c, sid)}
        assert len(rows) == 3
        for cid, dep in (("b", "a"), ("c", "b")):
            assert json.loads(rows[by[cid]["task_id"]]["dependency_json"]) == [by[dep]["task_id"]]
        # existing dependency gating: first is ready, dependents are blocked
        assert (by["a"]["state"], by["b"]["state"], by["c"]["state"]) == ("queued", "blocked", "blocked")
        types = event_types(c, sid)
        assert types[0] == "manager.plan_proposed"
        assert types[1:] == ["task.created"] * 3 + ["manager.plan_committed"]
        ev = c.app.state.phase2_store.events_after_v2(sid, 0)[-1]
        assert ev["payload"]["task_ids"] == {k: by[k]["task_id"] for k in "abc"}
        assert ev["payload"]["revision"] == 2
        created = [e for e in c.app.state.phase2_store.events_after_v2(sid, 0) if e["type"] == "task.created"]
        assert {e["task_id"] for e in created} == {t["task_id"] for t in res["tasks"]}
        # the plan now records the canonical ids
        got = c.get(f"/v2/sessions/{sid}/manager/plans/{p['plan_id']}", headers=h).json()
        assert got["status"] == "committed" and got["committed_tasks"] == res["tasks"]
        # the board snapshot shows them
        snap = c.get(f"/v2/sessions/{sid}/snapshot", headers=h).json()
        assert {t["task_id"] for t in snap["tasks"]} == set(rows)


def test_task_created_only_after_commit():
    with p3_client() as c:
        sid, h = open_session(c)
        p = propose(c, sid, h).json()
        revise(c, sid, h, p, CHAIN)
        assert "task.created" not in event_types(c, sid) and tasks(c, sid) == []
        # a rejected commit creates nothing and emits nothing
        r = commit(c, sid, h, p, revision=1)
        assert r.status_code == 409
        assert "task.created" not in event_types(c, sid)


def test_double_commit_same_key_is_idempotent():
    with p3_client() as c:
        sid, h = open_session(c)
        p = propose(c, sid, h).json()
        a = commit(c, sid, h, p)
        n_tasks, n_events = len(tasks(c, sid)), len(event_types(c, sid))
        b = commit(c, sid, h, p)
        assert a.status_code == b.status_code == 200 and a.json() == b.json()
        assert len(tasks(c, sid)) == n_tasks == 3 and len(event_types(c, sid)) == n_events
        # a *different* key on an already committed plan is refused, creating nothing
        d = commit(c, sid, h, p, key="commit-key-OTHER001", revision=2)
        assert d.status_code == 409 and d.json()["code"] == "PLAN_ALREADY_COMMITTED"
        assert len(tasks(c, sid)) == 3 and len(event_types(c, sid)) == n_events


def test_commit_stale_revision_conflict():
    with p3_client() as c:
        sid, h = open_session(c)
        p = propose(c, sid, h).json()
        revise(c, sid, h, p, CHAIN)                                   # plan now revision 2
        r = commit(c, sid, h, p, key="commit-key-000002")             # still says revision 1
        assert r.status_code == 409 and r.json()["code"] == "REVISION_CONFLICT"
        assert r.json()["canonical_plan"]["revision"] == 2
        assert tasks(c, sid) == []


def test_unapproved_dependency_rejected():
    with p3_client() as c:
        sid, h = open_session(c)
        p = propose(c, sid, h).json()
        r = commit(c, sid, h, p, approved=["b", "c"])
        assert r.status_code == 409 and r.json()["code"] == "DEPENDENCY_NOT_APPROVED"
        assert r.json()["details"][0]["item_id"] == "b"
        assert tasks(c, sid) == []
        # a dependency-closed subset is fine
        ok = commit(c, sid, h, p, approved=["a"], key="commit-key-000002")
        assert ok.status_code == 200 and [t["client_item_id"] for t in ok.json()["tasks"]] == ["a"]
        assert len(tasks(c, sid)) == 1


def test_unknown_and_empty_approval_rejected():
    with p3_client() as c:
        sid, h = open_session(c)
        p = propose(c, sid, h).json()
        assert commit(c, sid, h, p, approved=["zzz"]).json()["code"] == "UNKNOWN_ITEM"
        assert commit(c, sid, h, p, approved=[], key="commit-key-000002").json()["code"] == "NO_ITEMS_APPROVED"


def test_dangerous_item_needs_authorization():
    dangerous = [item("a", "research", "researcher", perm="read_repo"),
                 item("b", "code", "developer", ["a"], title="Deploy to production")]
    with p3_client() as c:
        sid, h = open_session(c)
        p = propose(c, sid, h).json()
        p = revise(c, sid, h, p, dangerous).json()
        assert p["items"][1]["dangerous"] and "deployment" in p["items"][1]["danger_reasons"]
        r = commit(c, sid, h, p)
        assert r.status_code == 409 and r.json()["code"] == "AUTHORIZATION_REQUIRED"
        assert r.json()["details"][0]["item_id"] == "b" and tasks(c, sid) == []
        # authorizing the wrong (non-dangerous) item does not help
        r = commit(c, sid, h, p, authorized=["a"], key="commit-key-000002")
        assert r.json()["code"] == "AUTHORIZATION_REQUIRED"
        ok = commit(c, sid, h, p, authorized=["b"], key="commit-key-000003")
        assert ok.status_code == 200 and len(tasks(c, sid)) == 2
        # not approving the dangerous item needs no authorization
    with p3_client() as c:
        sid, h = open_session(c)
        p = revise(c, sid, h, propose(c, sid, h).json(), dangerous).json()
        assert commit(c, sid, h, p, approved=["a"]).status_code == 200


def test_commit_failure_rolls_back_everything(monkeypatch):
    with p3_client() as c:
        sid, h = open_session(c)
        p = propose(c, sid, h).json()
        before_events = event_types(c, sid)
        real = TaskGraphService.create_task
        calls = {"n": 0}

        def flaky(self, *a, **k):
            calls["n"] += 1
            if calls["n"] == 2:
                raise RuntimeError("disk full")
            return real(self, *a, **k)

        monkeypatch.setattr(TaskGraphService, "create_task", flaky)
        r = commit(c, sid, h, p)
        assert r.status_code == 500 and r.json()["code"] == "COMMIT_FAILED"
        assert calls["n"] == 2
        assert tasks(c, sid) == []                                    # zero partial tasks
        after = event_types(c, sid)
        assert "task.created" not in after
        assert after == before_events + ["manager.plan_failed"]
        got = c.get(f"/v2/sessions/{sid}/manager/plans/{p['plan_id']}", headers=h).json()
        assert got["status"] == "failed" and got["failure"]["code"] == "COMMIT_FAILED"
        assert got["committed_tasks"] == []
        # a failed plan cannot be committed again, and the failure was not cached as success
        again = commit(c, sid, h, got, key="commit-key-000002")
        assert again.status_code == 409 and again.json()["code"] == "PLAN_NOT_OPEN"
        assert tasks(c, sid) == []


def test_commit_leaves_existing_board_untouched():
    with p3_client() as c:
        sid, h = open_session(c)
        existing = make_task(c, sid, h, "Existing card")
        before = copy.deepcopy(tasks(c, sid))
        p = propose(c, sid, h).json()
        commit(c, sid, h, p)
        rows = tasks(c, sid)
        assert [r for r in rows if r["id"] == existing["task_id"]] == before
        assert len(rows) == 4


def test_manager_side_question_after_commit_reports_board():
    from tests.phase3.p3helpers import ask
    with p3_client() as c:
        sid, h = open_session(c)
        p = propose(c, sid, h).json()
        commit(c, sid, h, p)
        j = ask(c, sid, h, agent="manager", question="Summarise the board").json()
        assert "3 task(s)" in j["reply"] and "committed" in j["reply"]
