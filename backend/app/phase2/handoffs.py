"""Typed agent-to-agent handoff service (P2-03 foundation).

A handoff is authoritative orchestration state, minted only by the backend after
validating both agents, the source artifact and the target task edge. Business
state is exactly-once by unique ``handoff_id`` — a duplicated ``handoff.created``
event (e.g. replay) never creates a second record, a second queue entry or a
second courier animation.
"""
from __future__ import annotations

import uuid

from .errors import CommandError, NotFoundError
from .events import Phase2EventBus, utcnow_iso
from .models import AgentIdV2, EventTypeV2, HandoffKind, HandoffState, HandoffV2
from .store import Phase2Store

_VALID_AGENTS = {a.value for a in AgentIdV2}
_VALID_KINDS = {k.value for k in HandoffKind}


def _model(row: dict) -> HandoffV2:
    return HandoffV2(
        handoff_id=row["id"], from_agent_id=row["from_agent_id"],
        to_agent_id=row["to_agent_id"], task_id=row["task_id"],
        artifact_id=row["artifact_id"], kind=row["kind"], state=row["state"],
        source_mode=row["source_mode"],
    )


class HandoffService:
    def __init__(self, store: Phase2Store, bus: Phase2EventBus, source_mode: str) -> None:
        self._store = store
        self._bus = bus
        self._mode = source_mode

    async def create(self, session_id: str, from_agent: str, to_agent: str, task_id: str,
                    artifact_id: str, kind: str, *, handoff_id: str | None = None) -> HandoffV2:
        # Validate the edge BEFORE any state change.
        if from_agent not in _VALID_AGENTS or to_agent not in _VALID_AGENTS:
            raise CommandError("INVALID_AGENT", f"unknown agent in handoff {from_agent}->{to_agent}",
                               status=400)
        if from_agent == to_agent:
            raise CommandError("INVALID_HANDOFF", "handoff source and target are the same agent",
                               status=400)
        if kind not in _VALID_KINDS:
            raise CommandError("INVALID_HANDOFF_KIND", f"unknown handoff kind {kind}", status=400)
        if self._store.get_artifact_v2(artifact_id) is None:
            raise CommandError("UNKNOWN_ARTIFACT", f"artifact {artifact_id} does not exist",
                               status=400)
        task = self._store.get_task_v2(task_id)
        if task is None or task["session_id"] != session_id:
            raise CommandError("UNKNOWN_TASK", f"task {task_id} does not exist in session",
                               status=400)

        hid = handoff_id or f"h2-{uuid.uuid4().hex[:10]}"
        now = utcnow_iso()
        inserted = self._store.create_handoff_v2(
            hid, session_id, from_agent, to_agent, task_id, artifact_id, kind,
            HandoffState.created.value, self._mode, now,
        )
        if not inserted:
            # Exactly-once: a handoff with this id already exists → return canonical,
            # emit nothing (no duplicate courier cue).
            existing = self._store.get_handoff_v2(hid)
            return _model(existing)

        await self._bus.publish(
            session_id, EventTypeV2.handoff_created,
            {"from_agent_id": from_agent, "to_agent_id": to_agent, "kind": kind,
             "source_mode": self._mode},
            task_id=task_id, agent_id=from_agent, artifact_id=artifact_id,
            handoff_id=hid, event_id=f"evt2-handoff-{hid}",
        )
        return _model(self._store.get_handoff_v2(hid))

    async def mark_received(self, session_id: str, handoff_id: str) -> HandoffV2:
        row = self._store.get_handoff_v2(handoff_id)
        if row is None or row["session_id"] != session_id:
            raise NotFoundError(f"unknown handoff {handoff_id}")
        if row["state"] == HandoffState.received.value:
            return _model(row)
        self._store.set_handoff_state_v2(handoff_id, HandoffState.received.value)
        await self._bus.publish(
            session_id, EventTypeV2.handoff_received,
            {"source_mode": self._mode}, task_id=row["task_id"],
            agent_id=row["to_agent_id"], handoff_id=handoff_id,
            event_id=f"evt2-handoff-recv-{handoff_id}",
        )
        return _model(self._store.get_handoff_v2(handoff_id))
