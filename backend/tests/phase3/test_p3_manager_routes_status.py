"""HTTP wiring for the Manager status and retry routes (Sprint A10 domain functions)."""
from __future__ import annotations

from tests.phase3.p3helpers import open_session
from tests.phase3.test_p3_plan_ops import FlakyProvider
from tests.phase3.test_p3_runner import _client, _committed, state, tick


def test_status_and_retry_routes_over_http():
    with _client(provider=FlakyProvider(fail_work=1)) as c:
        sid, h = open_session(c)
        plan, cards = _committed(c, sid, h)
        pid = plan["plan_id"]
        a = cards[0]["task_id"]
        tick(c)
        assert state(c, a) == "failed"

        r = c.get(f"/v2/sessions/{sid}/manager/plans/{pid}/status", headers=h)
        assert r.status_code == 200, r.text
        assert r.json()["counts"].get("failed") == 1

        r = c.get(f"/v2/sessions/{sid}/manager/status", headers=h)
        assert r.status_code == 200, r.text
        assert r.json()["failed"][0]["task_id"] == a

        url = f"/v2/sessions/{sid}/manager/plans/{pid}/tasks/{a}/retry"
        body = {"idempotency_key": "retry-key-0001"}
        r1 = c.post(url, json=body, headers=h)
        r2 = c.post(url, json=body, headers=h)          # same key: replayed, not re-run
        assert r1.status_code == 200, r1.text
        assert r1.json()["retried"] is True and r2.json() == r1.json()
        assert state(c, a) == "queued"


def test_status_routes_require_session_token():
    with _client(provider=FlakyProvider(fail_work=0)) as c:
        sid, h = open_session(c)
        plan, _ = _committed(c, sid, h)
        r = c.get(f"/v2/sessions/{sid}/manager/plans/{plan['plan_id']}/status")
        assert r.status_code in (401, 403)
