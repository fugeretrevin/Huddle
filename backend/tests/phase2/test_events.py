"""v2 event store: monotonic seq, dedupe, idempotency, replay (MASTER §3.3, §13)."""
from __future__ import annotations

import asyncio

from app.phase2.models import EventTypeV2
from app.phase2.store import body_hash
from tests.conftest import p2_fresh

SID = "ses-test"


def test_seq_is_monotonic_and_gapless():
    async def body():
        store, bus, svc = p2_fresh()
        for i in range(5):
            await bus.publish(SID, EventTypeV2.task_progress, {"i": i})
        seqs = [e["seq"] for e in store.events_after_v2(SID, 0)]
        assert seqs == [1, 2, 3, 4, 5]
        assert store.last_seq_v2(SID) == 5
    asyncio.run(body())


def test_canonical_event_id_deduped():
    async def body():
        store, bus, svc = p2_fresh()
        e1 = await bus.publish(SID, EventTypeV2.handoff_created, {"x": 1}, event_id="EVT-1")
        e2 = await bus.publish(SID, EventTypeV2.handoff_created, {"x": 1}, event_id="EVT-1")
        assert e1.seq == e2.seq  # same logical event, no second append
        assert store.last_seq_v2(SID) == 1
    asyncio.run(body())


def test_replay_after_seq_only_returns_newer():
    async def body():
        store, bus, svc = p2_fresh()
        for i in range(4):
            await bus.publish(SID, EventTypeV2.task_progress, {"i": i})
        tail = store.events_after_v2(SID, 2)
        assert [e["seq"] for e in tail] == [3, 4]
    asyncio.run(body())


def test_idempotency_store_roundtrip_and_body_hash():
    store, bus, svc = p2_fresh()
    bh = body_hash({"route": "x", "idempotency_key": "k", "a": 1})
    store.save_idempotent_v2(SID, "k1", "route", bh, {"ok": True})
    got = store.get_idempotent_v2(SID, "k1")
    assert got["body_hash"] == bh
    assert got["result"] == {"ok": True}
    # same body → same hash; different body → different hash
    assert body_hash({"a": 1, "b": 2}) == body_hash({"b": 2, "a": 1})
    assert body_hash({"a": 1}) != body_hash({"a": 2})


def test_events_wire_has_no_null_ids():
    async def body():
        store, bus, svc = p2_fresh()
        env = await bus.publish(SID, EventTypeV2.focus_changed, {"mode": "focus"})
        wire = env.wire()
        # optional id fields are omitted when unset (schema ids are non-nullable strings)
        for k in ("task_id", "agent_id", "artifact_id", "handoff_id"):
            assert k not in wire
        assert wire["schema_version"] == 2
    asyncio.run(body())
