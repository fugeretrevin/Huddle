"""Task graph / DAG service (P2-01 foundation).

Server is the sole authority for assignment, priority, dependency readiness and
cooperative pause. Every mutation is guarded by an optimistic ``expected_revision``
check and bumps the task ``revision`` so a stale client command can never silently
overwrite newer state. No process is ever killed; pause is cooperative.
"""
from __future__ import annotations

import json
import uuid

from .errors import CommandError, NotFoundError
from .events import Phase2EventBus, utcnow_iso
from .models import (
    SAFE_ASSIGN_STATES,
    TERMINAL_STATES,
    EventTypeV2,
    TaskCardV2,
    TaskStateV2,
)
from .store import Phase2Store


def _card(row: dict) -> TaskCardV2:
    return TaskCardV2(
        task_id=row["id"], title=row["title"], task_state=row["task_state"],
        agent_id=row["agent_id"], priority=row["priority"], revision=row["revision"],
        dependencies=json.loads(row["dependency_json"]), source_mode=row["source_mode"],
    )


class TaskGraphService:
    def __init__(self, store: Phase2Store, bus: Phase2EventBus, source_mode: str) -> None:
        self._store = store
        self._bus = bus
        self._mode = source_mode

    # ─────────────────────── creation / queries ───────────────────────
    def create_task(self, session_id: str, title: str, agent_id: str | None,
                    priority: int, dependencies: list[str], role_lock: str | None) -> TaskCardV2:
        task_id = f"t2-{uuid.uuid4().hex[:10]}"
        now = utcnow_iso()
        state = TaskStateV2.blocked if dependencies else TaskStateV2.queued
        self._store.create_task_v2(task_id, session_id, title, state.value, agent_id,
                                   priority, dependencies, role_lock, self._mode, now)
        return _card(self._store.get_task_v2(task_id))

    def dependencies_ready(self, session_id: str, task_row: dict) -> bool:
        deps = json.loads(task_row["dependency_json"])
        if not deps:
            return True
        for dep_id in deps:
            dep = self._store.get_task_v2(dep_id)
            if dep is None or dep["task_state"] != TaskStateV2.completed.value:
                return False
        return True

    def _require(self, task_id: str) -> dict:
        row = self._store.get_task_v2(task_id)
        if row is None:
            raise NotFoundError(f"unknown task {task_id}")
        return row

    def _guard_revision(self, row: dict, expected_revision: int) -> None:
        if row["revision"] != expected_revision:
            raise CommandError(
                "REVISION_CONFLICT",
                f"task revision is {row['revision']}, command expected {expected_revision}",
                status=409, canonical=_card(row).model_dump(mode="json"),
            )

    # ─────────────────────── commands ───────────────────────
    async def assign(self, session_id: str, task_id: str, target_agent: str,
                    expected_revision: int) -> TaskCardV2:
        row = self._require(task_id)
        self._guard_revision(row, expected_revision)
        state = TaskStateV2(row["task_state"])
        if state in TERMINAL_STATES:
            raise CommandError("TASK_NOT_ASSIGNABLE",
                               f"task is {state.value}; cannot reassign",
                               status=409, canonical=_card(row).model_dump(mode="json"))
        if state == TaskStateV2.running:
            raise CommandError("TASK_RUNNING",
                               "task is running (live work in progress); pause before reassigning",
                               status=409, canonical=_card(row).model_dump(mode="json"))
        if state not in SAFE_ASSIGN_STATES and state != TaskStateV2.blocked:
            raise CommandError("UNSAFE_TRANSFER",
                               f"task state {state.value} is not a safe reassignment checkpoint",
                               status=409, canonical=_card(row).model_dump(mode="json"))
        if row["role_lock"] and row["role_lock"] != target_agent:
            raise CommandError("ROLE_INCOMPATIBLE",
                               f"task is locked to role {row['role_lock']}",
                               status=409, canonical=_card(row).model_dump(mode="json"))
        updated = self._store.update_task_v2(task_id, agent_id=target_agent, now=utcnow_iso())
        card = _card(updated)
        await self._bus.publish(session_id, EventTypeV2.task_assigned,
                                {"target_agent_id": target_agent, "revision": card.revision,
                                 "source_mode": self._mode},
                                task_id=task_id, agent_id=target_agent)
        return card

    async def set_priority(self, session_id: str, task_id: str, priority: int,
                          expected_revision: int) -> TaskCardV2:
        row = self._require(task_id)
        self._guard_revision(row, expected_revision)
        if TaskStateV2(row["task_state"]) in TERMINAL_STATES:
            raise CommandError("TASK_NOT_ASSIGNABLE", "task is terminal; cannot reprioritize",
                               status=409, canonical=_card(row).model_dump(mode="json"))
        updated = self._store.update_task_v2(task_id, priority=priority, now=utcnow_iso())
        card = _card(updated)
        await self._bus.publish(session_id, EventTypeV2.task_priority_changed,
                                {"priority": priority, "revision": card.revision,
                                 "source_mode": self._mode}, task_id=task_id)
        return card

    async def pause(self, session_id: str, task_id: str, expected_revision: int) -> TaskCardV2:
        row = self._require(task_id)
        self._guard_revision(row, expected_revision)
        state = TaskStateV2(row["task_state"])
        if state in TERMINAL_STATES:
            raise CommandError("TASK_NOT_PAUSABLE", f"task is {state.value}",
                               status=409, canonical=_card(row).model_dump(mode="json"))
        if state in (TaskStateV2.paused, TaskStateV2.pause_requested):
            return _card(row)  # idempotent no-op
        if state == TaskStateV2.running:
            # Cooperative: request only; not displayed as paused until a checkpoint.
            updated = self._store.update_task_v2(task_id, task_state=TaskStateV2.pause_requested.value,
                                                 now=utcnow_iso())
            card = _card(updated)
            await self._bus.publish(session_id, EventTypeV2.task_pause_requested,
                                    {"revision": card.revision, "source_mode": self._mode},
                                    task_id=task_id)
            return card
        # queued / blocked: no live process, pause immediately.
        updated = self._store.update_task_v2(task_id, task_state=TaskStateV2.paused.value,
                                             now=utcnow_iso())
        card = _card(updated)
        await self._bus.publish(session_id, EventTypeV2.task_paused,
                                {"revision": card.revision, "source_mode": self._mode},
                                task_id=task_id)
        return card

    async def reach_checkpoint(self, session_id: str, task_id: str) -> TaskCardV2:
        """Cooperative pause checkpoint: a task in ``pause_requested`` reaches a safe
        point and becomes ``paused``. Emitted by the orchestrator, never by a client
        collision."""
        row = self._require(task_id)
        if TaskStateV2(row["task_state"]) != TaskStateV2.pause_requested:
            return _card(row)
        updated = self._store.update_task_v2(task_id, task_state=TaskStateV2.paused.value,
                                             now=utcnow_iso())
        card = _card(updated)
        await self._bus.publish(session_id, EventTypeV2.task_paused,
                                {"revision": card.revision, "checkpoint": True,
                                 "source_mode": self._mode}, task_id=task_id)
        return card

    async def resume(self, session_id: str, task_id: str, expected_revision: int) -> TaskCardV2:
        row = self._require(task_id)
        self._guard_revision(row, expected_revision)
        state = TaskStateV2(row["task_state"])
        if state not in (TaskStateV2.paused, TaskStateV2.pause_requested):
            raise CommandError("TASK_NOT_PAUSED", f"task is {state.value}; cannot resume",
                               status=409, canonical=_card(row).model_dump(mode="json"))
        new_state = TaskStateV2.running if row["agent_id"] else TaskStateV2.queued
        updated = self._store.update_task_v2(task_id, task_state=new_state.value, now=utcnow_iso())
        card = _card(updated)
        await self._bus.publish(session_id, EventTypeV2.task_resumed,
                                {"revision": card.revision, "task_state": new_state.value,
                                 "source_mode": self._mode}, task_id=task_id)
        return card
