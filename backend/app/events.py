"""In-memory fan-out bus layered on durable storage.

Ordering guarantee: for a given session, persist(seq) and enqueue-to-subscribers
happen under one per-session asyncio lock, so subscribers observe events in the
same monotonically increasing seq order they were committed. A client that misses
events while disconnected recovers them from storage.events_after().
"""
from __future__ import annotations

import asyncio
import uuid
from collections import defaultdict
from datetime import datetime, timezone
from typing import Any

from .models import EventEnvelope, EventType, SCHEMA_VERSION
from .storage import Storage


def utcnow_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


class EventBus:
    def __init__(self, storage: Storage) -> None:
        self._storage = storage
        self._subscribers: dict[str, set[asyncio.Queue]] = defaultdict(set)
        self._locks: dict[str, asyncio.Lock] = defaultdict(asyncio.Lock)

    async def publish(
        self,
        session_id: str,
        type_: EventType,
        payload: dict[str, Any],
        *,
        task_id: str | None = None,
        agent_id: str | None = None,
        decision_id: str | None = None,
    ) -> EventEnvelope:
        async with self._locks[session_id]:
            event_id = f"evt-{uuid.uuid4().hex[:12]}"
            occurred_at = utcnow_iso()
            # Persist first WITHOUT seq so we can embed the assigned seq in the
            # stored/broadcast envelope identically.
            seq = self._storage.append_event(
                session_id, event_id, type_.value, data_json="{}", occurred_at=occurred_at
            )
            envelope = EventEnvelope(
                schema_version=SCHEMA_VERSION,
                event_id=event_id,
                seq=seq,
                session_id=session_id,
                occurred_at=occurred_at,
                type=type_,
                task_id=task_id,
                agent_id=agent_id,
                decision_id=decision_id,
                payload=payload,
            )
            # Rewrite the stored row with the full serialized envelope.
            self._rewrite_event(session_id, seq, envelope)

            dead: list[asyncio.Queue] = []
            for q in self._subscribers.get(session_id, set()):
                try:
                    q.put_nowait(envelope)
                except asyncio.QueueFull:
                    dead.append(q)
            for q in dead:
                self._subscribers[session_id].discard(q)
            return envelope

    def _rewrite_event(self, session_id: str, seq: int, envelope: EventEnvelope) -> None:
        with self._storage._lock:  # noqa: SLF001 - intentional single-writer coupling
            self._storage._conn.execute(
                "UPDATE events SET data_json=? WHERE session_id=? AND seq=?",
                (envelope.model_dump_json(), session_id, seq),
            )
            self._storage._conn.commit()

    def subscribe(self, session_id: str, maxsize: int = 1000) -> asyncio.Queue:
        q: asyncio.Queue = asyncio.Queue(maxsize=maxsize)
        self._subscribers[session_id].add(q)
        return q

    def unsubscribe(self, session_id: str, q: asyncio.Queue) -> None:
        self._subscribers.get(session_id, set()).discard(q)

    def replay(self, session_id: str, after_seq: int) -> list[dict[str, Any]]:
        return self._storage.events_after(session_id, after_seq)
