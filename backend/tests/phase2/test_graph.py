"""Task DAG / assignment / priority / pause unit tests (MASTER §13.2, §13.3)."""
from __future__ import annotations

import asyncio

import pytest

from app.phase2.errors import CommandError
from tests.conftest import p2_fresh

SID = "ses-test"


def test_dependency_readiness_and_assign_happy():
    async def body():
        store, bus, svc = p2_fresh()
        g = svc["graph"]
        research = g.create_task(SID, "research", None, 2, [], None)
        dev = g.create_task(SID, "develop", None, 2, [research.task_id], None)
        # dev depends on research which is not completed → blocked + deps not ready
        assert dev.task_state.value == "blocked"
        assert g.dependencies_ready(SID, store.get_task_v2(dev.task_id)) is False
        # assign the queued research task
        card = await g.assign(SID, research.task_id, "researcher", research.revision)
        assert card.agent_id == "researcher"
        assert card.revision == research.revision + 1
        # exactly one task.assigned event appended to the v2 log
        types = [e["type"] for e in store.events_after_v2(SID, 0)]
        assert types.count("task.assigned") == 1
    asyncio.run(body())


def test_assign_stale_revision_conflicts_with_canonical():
    async def body():
        store, bus, svc = p2_fresh()
        g = svc["graph"]
        t = g.create_task(SID, "t", None, 1, [], None)
        await g.assign(SID, t.task_id, "developer", 0)  # revision now 1
        with pytest.raises(CommandError) as ei:
            await g.assign(SID, t.task_id, "tester", 0)  # stale expected_revision
        assert ei.value.code == "REVISION_CONFLICT"
        assert ei.value.status == 409
        assert ei.value.canonical["revision"] == 1  # client can reconcile
    asyncio.run(body())


def test_role_locked_task_rejects_wrong_agent():
    async def body():
        store, bus, svc = p2_fresh()
        g = svc["graph"]
        t = g.create_task(SID, "dev-only", None, 1, [], "developer")
        with pytest.raises(CommandError) as ei:
            await g.assign(SID, t.task_id, "tester", 0)
        assert ei.value.code == "ROLE_INCOMPATIBLE"
        # no state change / no event
        assert store.get_task_v2(t.task_id)["agent_id"] is None
        assert store.events_after_v2(SID, 0) == []
    asyncio.run(body())


def test_running_task_cannot_be_reassigned():
    async def body():
        store, bus, svc = p2_fresh()
        g = svc["graph"]
        t = g.create_task(SID, "t", "developer", 1, [], None)
        store.update_task_v2(t.task_id, task_state="running", bump_revision=False, now="now")
        with pytest.raises(CommandError) as ei:
            await g.assign(SID, t.task_id, "tester", 0)
        assert ei.value.code == "TASK_RUNNING"
    asyncio.run(body())


def test_completed_task_cannot_be_reassigned():
    async def body():
        store, bus, svc = p2_fresh()
        g = svc["graph"]
        t = g.create_task(SID, "t", None, 1, [], None)
        store.update_task_v2(t.task_id, task_state="completed", bump_revision=False, now="now")
        with pytest.raises(CommandError) as ei:
            await g.assign(SID, t.task_id, "tester", 0)
        assert ei.value.code == "TASK_NOT_ASSIGNABLE"
    asyncio.run(body())


def test_priority_bump_and_bounds():
    async def body():
        store, bus, svc = p2_fresh()
        g = svc["graph"]
        t = g.create_task(SID, "t", None, 1, [], None)
        card = await g.set_priority(SID, t.task_id, 3, 0)
        assert card.priority == 3 and card.revision == 1
        types = [e["type"] for e in store.events_after_v2(SID, 0)]
        assert "task.priority_changed" in types
    asyncio.run(body())


def test_pause_is_immediate_and_resume_returns_to_queued():
    """Was ``test_cooperative_pause_then_checkpoint_then_resume``. A plain board card has
    no executor, so the old expectations were dishonest: pause of a 'running' card
    parked it in ``pause_requested`` forever (nothing ever reached a checkpoint), and
    resume of a card with an agent claimed ``running`` although nothing ran it."""
    async def body():
        store, bus, svc = p2_fresh()
        g = svc["graph"]
        t = g.create_task(SID, "t", "developer", 1, [], None)
        store.update_task_v2(t.task_id, task_state="running", bump_revision=False, now="now")
        # no executor to wait for: pause goes straight to paused, never pause_requested
        card = await g.pause(SID, t.task_id, 0)
        assert card.task_state.value == "paused"
        # resume returns the card to queued even though an agent is assigned
        card = await g.resume(SID, t.task_id, card.revision)
        assert card.task_state.value == "queued" and card.agent_id == "developer"
        events = store.events_after_v2(SID, 0)
        types = [e["type"] for e in events]
        assert "task.pause_requested" not in types
        assert types == ["task.paused", "task.resumed"]
        assert events[-1]["payload"]["task_state"] == "queued"
    asyncio.run(body())


def test_legacy_pause_requested_row_can_still_be_settled():
    async def body():
        store, bus, svc = p2_fresh()
        g = svc["graph"]
        t = g.create_task(SID, "t", "developer", 1, [], None)
        # a row written by an older build
        store.update_task_v2(t.task_id, task_state="pause_requested", bump_revision=False,
                             now="now")
        card = await g.reach_checkpoint(SID, t.task_id)
        assert card.task_state.value == "paused"
        store.update_task_v2(t.task_id, task_state="pause_requested", bump_revision=False,
                             now="now")
        card = await g.pause(SID, t.task_id, card.revision)   # pause also settles it
        assert card.task_state.value == "paused"
        card = await g.resume(SID, t.task_id, card.revision)
        assert card.task_state.value == "queued"
    asyncio.run(body())


def test_pause_of_queued_task_is_immediate():
    async def body():
        store, bus, svc = p2_fresh()
        g = svc["graph"]
        t = g.create_task(SID, "t", None, 1, [], None)  # queued
        card = await g.pause(SID, t.task_id, 0)
        assert card.task_state.value == "paused"
    asyncio.run(body())
