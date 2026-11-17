"""A confirmed change (e.g. a /btw suggestion) becomes ONE new step on a committed plan, and the runner works it."""
from __future__ import annotations

from tests.phase3.p3helpers import event_types, open_session
from tests.phase3.test_p3_manager import propose
from tests.phase3.test_p3_runner import _client, _committed, state, tick


def amend(c, sid, h, plan_id, revision, summary="Make the password rule 12 characters", agent="developer",
          after=None, key="amend-key-0001"):
    return c.post(f"/v2/sessions/{sid}/manager/plans/{plan_id}/amend", headers=h, json={
        "schema_version": 2, "revision": revision, "summary": summary, "agent_id": agent,
        "after_task_id": after, "idempotency_key": key})


def test_amend_adds_one_step_after_the_agents_card_and_the_runner_works_it():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        dev = next(x for x in cards if x["agent_id"] == "developer")
        rev = c.get(f"/v2/sessions/{sid}/manager/plans/{plan['plan_id']}", headers=h).json()["revision"]
        r = amend(c, sid, h, plan["plan_id"], rev, after=dev["task_id"])
        assert r.status_code == 200, r.text
        body = r.json()
        assert body["amended"] is True and body["revision"] == rev + 1 and len(body["tasks"]) == 1
        new = body["tasks"][0]
        assert new["agent_id"] == "developer" and new["depends_on"] == [dev["task_id"]]
        assert event_types(c, sid).count("manager.plan_committed") == 2

        # replay: the same key creates nothing new
        again = amend(c, sid, h, plan["plan_id"], rev, after=dev["task_id"])
        assert again.json()["tasks"][0]["task_id"] == new["task_id"]

        for _ in range(8):
            if tick(c) == 0:
                break
        assert state(c, new["task_id"]) == "completed"
        st = c.get(f"/v2/sessions/{sid}/manager/plans/{plan['plan_id']}/status", headers=h).json()
        assert st["total"] == 4 and st["settled"] is True
        assert any(cd["title"].startswith("Change: Make the password rule") for cd in st["cards"])


def test_change_title_reads_cleanly():
    from app.phase3.manager import change_title
    assert change_title("Actually, change the password rule to 12 characters.") == "Change: the password rule to 12 characters"
    assert change_title("Developer, could you please add rate limiting?") == "Change: add rate limiting"
    assert change_title("Make the password rule 12 characters") == "Change: Make the password rule 12 characters"
    # the live /btw suggestion carries its own label
    assert change_title("Requested change (not applied): Actually, change the password rule to 12 characters") \
        == "Change: the password rule to 12 characters"


def test_amend_is_refused_for_open_plans_stale_revisions_and_unknown_agents():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        open_plan = propose(c, sid, h).json()
        assert amend(c, sid, h, open_plan["plan_id"], open_plan["revision"]).json()["error_code"] == "PLAN_NOT_COMMITTED"
        sid, h = open_session(c)                   # a fresh session: the helper reuses its propose key
        plan, _ = _committed(c, sid, h)
        rev = c.get(f"/v2/sessions/{sid}/manager/plans/{plan['plan_id']}", headers=h).json()["revision"]
        r = amend(c, sid, h, plan["plan_id"], rev - 1, key="amend-key-0002")
        assert r.status_code == 409 and r.json()["error_code"] == "REVISION_CONFLICT"
        r = amend(c, sid, h, plan["plan_id"], rev, agent="manager", key="amend-key-0003")
        assert r.status_code == 422 and r.json()["error_code"] == "UNKNOWN_AGENT"
