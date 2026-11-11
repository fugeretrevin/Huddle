"""Handoff service unit tests: exactly-once, validation, dedupe (MASTER §6, §13.3)."""
from __future__ import annotations

import asyncio

import pytest

from app.phase2.errors import CommandError
from tests.conftest import p2_fresh

SID = "ses-test"


async def _seed(store, svc):
    g = svc["graph"]
    task = g.create_task(SID, "develop", "developer", 2, [], None)
    art = await svc["artifacts"].create_artifact(
        SID, task.task_id, "research_report", "blueprint",
        [{"page": 0, "title": "plan", "content": "do the thing", "line_refs": []}])
    return task, art


def test_valid_handoff_created_once():
    async def body():
        store, bus, svc = p2_fresh()
        task, art = await _seed(store, svc)
        h = await svc["handoffs"].create(SID, "researcher", "developer", task.task_id,
                                         art.artifact_id, "blueprint", handoff_id="H1")
        assert h.state.value == "created"
        created = [e for e in store.events_after_v2(SID, 0) if e["type"] == "handoff.created"]
        assert len(created) == 1
    asyncio.run(body())


def test_duplicate_handoff_id_does_not_double_create():
    async def body():
        store, bus, svc = p2_fresh()
        task, art = await _seed(store, svc)
        for _ in range(5):
            await svc["handoffs"].create(SID, "researcher", "developer", task.task_id,
                                         art.artifact_id, "blueprint", handoff_id="H1")
        assert len(store.list_handoffs_v2(SID)) == 1  # one business record
        created = [e for e in store.events_after_v2(SID, 0) if e["type"] == "handoff.created"]
        assert len(created) == 1  # one UI cue
    asyncio.run(body())


def test_invalid_recipient_rejected_before_state_change():
    async def body():
        store, bus, svc = p2_fresh()
        task, art = await _seed(store, svc)
        with pytest.raises(CommandError) as ei:
            await svc["handoffs"].create(SID, "researcher", "nobody", task.task_id,
                                         art.artifact_id, "blueprint")
        assert ei.value.code == "INVALID_AGENT"
        assert store.list_handoffs_v2(SID) == []
    asyncio.run(body())


def test_unknown_artifact_rejected():
    async def body():
        store, bus, svc = p2_fresh()
        task, _ = await _seed(store, svc)
        with pytest.raises(CommandError) as ei:
            await svc["handoffs"].create(SID, "researcher", "developer", task.task_id,
                                         "art-does-not-exist", "blueprint")
        assert ei.value.code == "UNKNOWN_ARTIFACT"
    asyncio.run(body())


def test_same_agent_handoff_rejected():
    async def body():
        store, bus, svc = p2_fresh()
        task, art = await _seed(store, svc)
        with pytest.raises(CommandError) as ei:
            await svc["handoffs"].create(SID, "developer", "developer", task.task_id,
                                         art.artifact_id, "implementation")
        assert ei.value.code == "INVALID_HANDOFF"
    asyncio.run(body())


def test_mark_received_emits_once():
    async def body():
        store, bus, svc = p2_fresh()
        task, art = await _seed(store, svc)
        await svc["handoffs"].create(SID, "researcher", "developer", task.task_id,
                                     art.artifact_id, "blueprint", handoff_id="H1")
        await svc["handoffs"].mark_received(SID, "H1")
        await svc["handoffs"].mark_received(SID, "H1")  # idempotent
        recv = [e for e in store.events_after_v2(SID, 0) if e["type"] == "handoff.received"]
        assert len(recv) == 1
        assert store.get_handoff_v2("H1")["state"] == "received"
    asyncio.run(body())
