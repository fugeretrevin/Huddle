"""Phase 2 in-memory fan-out bus over the durable ``events_v2`` store.

Mirrors :class:`app.events.EventBus` but writes to the SEPARATE v2 event log so
v1 subscribers are never handed a v2-only event. Ordering guarantee is identical:
seq allocation + persist + enqueue happen under one per-session lock, so v2
subscribers observe gap-free, duplicate-free, monotonic ``seq``.
"""
from __future__ import annotations

import asyncio
import uuid
from collections import defaultdict
from datetime import datetime, timezone
from typing import Any

from .models import EventEnvelopeV2, EventTypeV2
from .store import Phase2Store


def utcnow_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


class Phase2EventBus:
    def __init__(self, store: Phase2Store) -> None:
        self._store = store
        self._subscribers: dict[str, set[asyncio.Queue]] = defaultdict(set)
        self._locks: dict[str, asyncio.Lock] = defaultdict(asyncio.Lock)

    async def publish(
        self,
        session_id: str,
        type_: EventTypeV2,
        payload: dict[str, Any],
        *,
        task_id: str | None = None,
        agent_id: str | None = None,
        artifact_id: str | None = None,
        handoff_id: str | None = None,
        event_id: str | None = None,
    ) -> EventEnvelopeV2:
        async with self._locks[session_id]:
            # Logical de-duplication: if a canonical event_id was supplied and already
            # exists, do NOT append a second copy (prevents duplicate courier cues on
            # replay/retry). Return the stored envelope instead.
            if event_id and self._store.event_exists(event_id):
                for ev in self._store.events_after_v2(session_id, 0):
                    if ev.get("event_id") == event_id:
                        return EventEnvelopeV2(**ev)
            eid = event_id or f"evt2-{uuid.uuid4().hex[:12]}"
            occurred_at = utcnow_iso()
            seq = self._store.append_event_v2(session_id, eid, type_.value,
                                              data_json="{}", occurred_at=occurred_at)
            envelope = EventEnvelopeV2(
                event_id=eid, seq=seq, session_id=session_id, occurred_at=occurred_at,
                type=type_, task_id=task_id, agent_id=agent_id, artifact_id=artifact_id,
                handoff_id=handoff_id, payload=payload,
            )
            self._store.rewrite_event_v2(session_id, seq, _dump(envelope))

            dead: list[asyncio.Queue] = []
            for q in self._subscribers.get(session_id, set()):
                try:
                    q.put_nowait(envelope)
                except asyncio.QueueFull:
                    dead.append(q)
            for q in dead:
                self._subscribers[session_id].discard(q)
            return envelope

    def subscribe(self, session_id: str, maxsize: int = 1000) -> asyncio.Queue:
        q: asyncio.Queue = asyncio.Queue(maxsize=maxsize)
        self._subscribers[session_id].add(q)
        return q

    def unsubscribe(self, session_id: str, q: asyncio.Queue) -> None:
        self._subscribers.get(session_id, set()).discard(q)

    def replay(self, session_id: str, after_seq: int) -> list[dict[str, Any]]:
        return self._store.events_after_v2(session_id, after_seq)


def _dump(env: EventEnvelopeV2) -> str:
    import json
    return json.dumps(env.wire())
