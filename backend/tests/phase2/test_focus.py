"""Focus / inbox unit tests: critical never suppressed (MASTER §9, §13.3)."""
from __future__ import annotations

import asyncio

from tests.conftest import p2_fresh

SID = "ses-test"


def test_normal_mode_presents_everything():
    async def body():
        store, bus, svc = p2_fresh()
        f = svc["focus"]
        assert f.decide(SID, 0, critical=False) == "present"
        assert f.decide(SID, 3, critical=True) == "present"
    asyncio.run(body())


def test_do_not_disturb_queues_low_but_not_critical():
    async def body():
        store, bus, svc = p2_fresh()
        f = svc["focus"]
        await f.set_focus(SID, "do_not_disturb", None, [])
        # low-priority chatter is queued to the inbox
        d, info = await f.route_notification(SID, "progress", "minor update", 0, critical=False)
        assert d == "queue" and "item_id" in info
        # a pending dangerous approval is ALWAYS presented, even in DND
        d2, _ = await f.route_notification(SID, "decision", "approve deploy?", 3, critical=True)
        assert d2 == "present"
        inbox = store.list_inbox_v2(SID)
        assert len(inbox) == 1  # only the non-critical item was queued
        assert all(i["critical"] == 0 for i in inbox)
        types = [e["type"] for e in store.events_after_v2(SID, 0)]
        assert "focus.changed" in types and "inbox.updated" in types
    asyncio.run(body())


def test_focus_mode_quiets_configured_priorities_only():
    async def body():
        store, bus, svc = p2_fresh()
        f = svc["focus"]
        await f.set_focus(SID, "focus", None, [1])  # only priority 1 is quiet
        assert f.decide(SID, 1, critical=False) == "queue"
        assert f.decide(SID, 2, critical=False) == "present"
        assert f.decide(SID, 1, critical=True) == "present"
    asyncio.run(body())


def test_focus_revision_monotonic():
    async def body():
        store, bus, svc = p2_fresh()
        f = svc["focus"]
        p1 = await f.set_focus(SID, "focus", None, [])
        p2 = await f.set_focus(SID, "normal", None, [])
        assert p2.revision == p1.revision + 1
    asyncio.run(body())
