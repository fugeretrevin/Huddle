"""Backend leftovers: decision-timeout atomicity, idempotency under overlap, Phase 3
isolation / error hygiene, and the Manager status resync fields."""
from __future__ import annotations

import asyncio

from app.decision_wait import wait_for_decision
from app.phase2.keylock import idempotency_lock
from app.storage import Storage
from tests.phase3.p3helpers import open_session
from tests.phase3.test_p3_manager import commit, propose, revise  # noqa: F401
from tests.phase3.test_p3_runner import _client, _committed, tick


# --------------------------------------------------------------------------- 2. timeout race
class RacyStorage:
    """Delegates to a real Storage but lets a human answer land at a chosen instant."""

    def __init__(self, real, hook_before_expire=None, hook_after_get=None):
        self._r, self._before, self._after_get = real, hook_before_expire, hook_after_get

    def __getattr__(self, n):
        return getattr(self._r, n)

    def expire_decision(self, decision_id, resolved_at):
        if self._before:
            self._before()
        return self._r.expire_decision(decision_id, resolved_at)

    def get_decision(self, decision_id):
        row = self._r.get_decision(decision_id)
        if self._after_get:
            self._after_get()
        return row


def test_answer_landing_before_expiry_wins_and_run_is_not_failed(monkeypatch):
    """Forced interleaving: the timer fires, the answer is recorded, THEN the expiry runs.
    Exactly one outcome: the answer is honoured (not discarded)."""
    from tests.conftest import make_settings
    from app.main import create_app
    from fastapi.testclient import TestClient

    app = create_app(make_settings())
    with TestClient(app) as c:
        st = c.app.state.storage
        body, h = _open(c)
        sid = body["session_id"]
        st.create_decision("d-race", sid, "t1", "researcher", "s", "r", {})
        racy = RacyStorage(st, hook_before_expire=lambda: st.resolve_decision(
            "d-race", "approved", "approve", "2026-01-01T00:00:01Z", "k"))

        async def go():
            fut = asyncio.get_running_loop().create_future()
            return await wait_for_decision(racy, fut, "d-race", 0.01)

        assert c.portal.call(go) == "approve"
        row = st.get_decision("d-race")
        assert row["status"] == "approved" and row["resolved_action"] == "approve"


def test_expiry_winning_makes_late_answer_fail_consistently():
    from tests.conftest import make_settings
    from app.main import create_app
    from fastapi.testclient import TestClient

    app = create_app(make_settings())
    with TestClient(app) as c:
        st = c.app.state.storage
        body, h = _open(c)
        sid = body["session_id"]
        st.create_decision("d-race2", sid, "t1", "researcher", "s", "r", {})

        async def go():
            fut = asyncio.get_running_loop().create_future()
            return await wait_for_decision(st, fut, "d-race2", 0.01)

        assert c.portal.call(go) is None
        # State is already `expired`; a late answer cannot also resolve it.
        assert st.get_decision("d-race2")["status"] == "expired"
        assert st.resolve_decision("d-race2", "approved", "approve", "t", "k") is False
        assert st.get_decision("d-race2")["status"] == "expired"


def _open(c):
    r = c.post("/v1/sessions", json={"schema_version": 1, "mode": "live_local", "client": "quest"})
    assert r.status_code == 201, r.text
    b = r.json()
    return b, {"Authorization": f"Bearer {b['token']}"}


# --------------------------------------------------------------------------- 3. idempotency overlap
def test_overlapping_same_key_executes_once():
    runs = []

    async def go():
        async def command():
            async with idempotency_lock("s", "k-1"):
                if not runs:                      # the "stored result" check
                    await asyncio.sleep(0.05)     # first request is slow
                    runs.append(1)

        await asyncio.gather(command(), command())

    asyncio.run(go())
    assert runs == [1]


def test_propose_same_key_overlapping_creates_one_plan():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        store3 = c.app.state.phase3_store
        orig = c.app.state.phase2_bus.publish

        async def slow_publish(*a, **kw):
            await asyncio.sleep(0.05)             # force the two requests to interleave
            return await orig(*a, **kw)

        c.app.state.phase2_bus.publish = slow_publish

        async def both():
            import httpx
            tr = httpx.ASGITransport(app=c.app)
            async with httpx.AsyncClient(transport=tr, base_url="http://t") as ac:
                body = {"request_id": "r", "objective": "Build the login flow",
                        "idempotency_key": "overlap-key-0001"}
                return await asyncio.gather(
                    *[ac.post(f"/v2/sessions/{sid}/manager/plans", headers=h, json=body)
                      for _ in range(2)])

        r1, r2 = c.portal.call(both)
        assert r1.status_code == 201 and r2.status_code == 201
        assert r1.json()["plan_id"] == r2.json()["plan_id"]
        assert len(store3.plans_for_session(sid)) == 1


# --------------------------------------------------------------------------- 3. phase 3 audit
def test_key_reuse_with_different_body_is_409_on_every_manager_command():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        r = propose(c, sid, h)
        assert r.status_code == 201
        plan = r.json()
        # propose: same key, different objective
        r2 = propose(c, sid, h, objective="Something else entirely")
        assert r2.status_code == 409 and r2.json()["code"] == "IDEMPOTENCY_KEY_REUSE"
        # revise then reuse the key with a different title
        base = f"/v2/sessions/{sid}/manager/plans/{plan['plan_id']}/revise"
        ok = c.post(base, headers=h, json={"revision": 1, "title": "A title",
                                           "idempotency_key": "rev-key-00000001"})
        assert ok.status_code == 200, ok.text
        bad = c.post(base, headers=h, json={"revision": 1, "title": "B title",
                                            "idempotency_key": "rev-key-00000001"})
        assert bad.status_code == 409 and bad.json()["code"] == "IDEMPOTENCY_KEY_REUSE"
        # commit then reuse its key for a different approved set
        plan = c.get(f"/v2/sessions/{sid}/manager/plans/{plan['plan_id']}", headers=h).json()
        ids = [i["client_item_id"] for i in plan["items"]]
        cm = commit(c, sid, h, plan, approved=ids, key="commit-reuse-001")
        assert cm.status_code == 200, cm.text
        again = commit(c, sid, h, plan, approved=ids, key="commit-reuse-001")
        assert again.status_code == 200 and again.json() == cm.json()   # replay
        bad = commit(c, sid, h, plan, approved=ids[:1], key="commit-reuse-001")
        assert bad.status_code == 409 and bad.json()["code"] == "IDEMPOTENCY_KEY_REUSE"
        assert len(c.app.state.phase2_store.list_tasks_v2(sid)) == len(ids)   # no duplicates


def test_terminal_plans_cannot_be_reopened():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        plan = propose(c, sid, h).json()
        base = f"/v2/sessions/{sid}/manager/plans/{plan['plan_id']}"
        rj = c.post(f"{base}/reject", headers=h, json={"revision": 1, "reason": "no",
                                                       "idempotency_key": "reject-key-0001"})
        assert rj.status_code == 200
        rejected = rj.json()
        assert commit(c, sid, h, rejected, key="commit-after-rej1").status_code == 409
        assert c.post(f"{base}/revise", headers=h, json={
            "revision": rejected["revision"], "title": "again",
            "idempotency_key": "revise-after-rej"}).status_code == 409
        assert c.get(base, headers=h).json()["status"] == "rejected"
        assert c.app.state.phase2_store.list_tasks_v2(sid) == []


def test_other_sessions_cannot_read_or_command_a_plan():
    with _client(mode="demo") as c:
        a, ha = open_session(c)
        b, hb = open_session(c)
        plan = propose(c, a, ha).json()
        base = f"/v2/sessions/{b}/manager/plans/{plan['plan_id']}"
        assert c.get(base, headers=hb).status_code == 404
        assert c.get(f"{base}/status", headers=hb).status_code == 404
        assert commit(c, b, hb, plan, key="commit-foreign-01").status_code == 404
        assert c.post(f"{base}/reject", headers=hb, json={
            "revision": 1, "idempotency_key": "reject-foreign-1"}).status_code == 404
        assert c.get(f"/v2/sessions/{b}/manager/status", headers=hb).json()["open_plans"] == []


def test_commit_failure_does_not_leak_exception_text():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        plan = propose(c, sid, h).json()
        secret = r"C:\secret\path\db.sqlite boom"
        svc_cls = type(c.app.state.phase2_store)
        orig = svc_cls.create_task_v2

        def boom(self, *a, **kw):
            raise RuntimeError(secret)

        svc_cls.create_task_v2 = boom
        try:
            r = commit(c, sid, h, plan, key="commit-boom-0001")
        finally:
            svc_cls.create_task_v2 = orig
        assert r.status_code == 500
        assert "secret" not in r.text
        stored = c.app.state.phase3_store.get_plan(plan["plan_id"])
        assert "secret" not in str(stored["failure"])
        assert c.app.state.phase2_store.list_tasks_v2(sid) == []


# --------------------------------------------------------------------------- 4. resync
def test_manager_status_resyncs_proposed_and_committed_plans():
    with _client(mode="demo") as c:
        sid, h = open_session(c)
        proposed = propose(c, sid, h, key="prop-key-open-001").json()
        st = c.get(f"/v2/sessions/{sid}/manager/status", headers=h).json()
        assert [p["plan_id"] for p in st["open_plans"]] == [proposed["plan_id"]]
        op = st["open_plans"][0]
        assert op["status"] == "proposed" and op["revision"] == proposed["revision"]
        assert op["items"] and all("proposed_agent" in i for i in op["items"])
        assert st["plans"] == []

        committed, cards = _committed(c, sid, h)
        while tick(c):
            pass
        st = c.get(f"/v2/sessions/{sid}/manager/status", headers=h).json()
        p = next(x for x in st["plans"] if x["plan_id"] == committed["plan_id"])
        assert p["plan_status"] == "committed" and p["revision"] >= 2
        assert [i["client_item_id"] for i in p["items"]] == \
            [i["client_item_id"] for i in committed["items"]]
        assert [c_["task_id"] for c_ in p["cards"]] == [t["task_id"] for t in cards]
        assert all(c_["state"] == "completed" and c_["agent_id"] for c_ in p["cards"])
        assert p["settled"] and p["synthesis_artifact_id"] == p["synthesis"]["artifact_id"]
        # the proposed-only plan is still listed as open, the committed one is not
        assert committed["plan_id"] not in [o["plan_id"] for o in st["open_plans"]]

