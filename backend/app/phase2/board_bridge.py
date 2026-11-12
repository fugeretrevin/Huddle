"""Board <-> pipeline bridge: keeps a task CARD truthful while the run it launched executes.

An executable card (``task_template="auth_demo"``) launches the same v1 run that
``POST /v1/sessions/{id}/tasks`` launches. The producer (scripted or live) reports the
run's milestones here and this module projects them onto the card, on the v2 stream,
using only existing v2 event types and task states:

    task.started        task_state=running            (run launched by the assign)
    decision.requested  task_state=awaiting_approval  (payload carries decision_id)
    task.resumed        task_state=running            (decision approved)
    task.completed      task_state=completed          (run succeeded)
    task.completed      task_state=cancelled          (decision rejected / changes requested;
                                                       there is no cancel event type, same
                                                       convention as the v1 stream)
    task.failed         task_state=failed             (run failed or was interrupted)
    task.resumed        task_state=queued             (a blocked dependent whose dependencies
                                                       are now all completed)

Every event carries the card's new ``revision`` and ``task_state``. Event ids are
canonical per run and milestone, so a repeated report can never append a second copy.
"""
from __future__ import annotations

import json

from .events import Phase2EventBus, utcnow_iso
from .models import (
    EXECUTABLE_TEMPLATES,
    TERMINAL_STATES,
    EventTypeV2,
    TaskCardV2,
    TaskStateV2,
)
from .store import Phase2Store


def card_from_row(row: dict) -> TaskCardV2:
    template = row.get("task_template")
    return TaskCardV2(
        task_id=row["id"], title=row["title"], task_state=row["task_state"],
        agent_id=row["agent_id"], priority=row["priority"], revision=row["revision"],
        dependencies=json.loads(row["dependency_json"]), source_mode=row["source_mode"],
        task_template=template, executable=template in EXECUTABLE_TEMPLATES,
        run_task_id=row.get("run_task_id"),
    )


class BoardRunBridge:
    def __init__(self, store: Phase2Store, bus: Phase2EventBus) -> None:
        self._store = store
        self._bus = bus

    @property
    def store(self) -> Phase2Store:
        return self._store

    @property
    def bus(self) -> Phase2EventBus:
        return self._bus

    def card_id_for_run(self, run_task_id: str) -> str | None:
        row = self._store.get_task_v2_by_run(run_task_id)
        return row["id"] if row else None

    # ─────────────────────── internals ───────────────────────
    async def _move(self, row: dict, state: TaskStateV2, type_: EventTypeV2, tag: str,
                    extra: dict | None = None) -> TaskCardV2:
        """Set the card's state (bumping its revision) and announce it. ``tag`` makes the
        event id canonical for this run + milestone."""
        updated = self._store.update_task_v2(row["id"], task_state=state.value,
                                             now=utcnow_iso())
        card = card_from_row(updated)
        payload = {"revision": card.revision, "task_state": card.task_state.value,
                   "task_template": card.task_template, "run_task_id": card.run_task_id,
                   **(extra or {}), "source_mode": card.source_mode}
        await self._bus.publish(
            updated["session_id"], type_, payload, task_id=card.task_id,
            agent_id=card.agent_id,
            event_id=f"evt2-run-{card.run_task_id or card.task_id}-{tag}")
        return card

    def _live_card(self, run_task_id: str) -> dict | None:
        """The card linked to this run, unless it already reached a terminal state."""
        row = self._store.get_task_v2_by_run(run_task_id)
        if row is None or TaskStateV2(row["task_state"]) in TERMINAL_STATES:
            return None
        return row

    # ─────────────────────── run milestones ───────────────────────
    async def run_started(self, card_id: str, run_task_id: str) -> TaskCardV2:
        self._store.link_run_v2(card_id, run_task_id)
        row = self._store.get_task_v2(card_id)
        return await self._move(row, TaskStateV2.running, EventTypeV2.task_started, "started")

    async def decision_requested(self, run_task_id: str, decision_id: str,
                                 extra: dict | None = None) -> None:
        row = self._live_card(run_task_id)
        if row is None:
            return
        await self._move(row, TaskStateV2.awaiting_approval, EventTypeV2.decision_requested,
                         "decision", {"decision_id": decision_id, **(extra or {})})

    async def decision_approved(self, run_task_id: str, decision_id: str) -> None:
        row = self._live_card(run_task_id)
        if row is None or row["task_state"] != TaskStateV2.awaiting_approval.value:
            return
        await self._move(row, TaskStateV2.running, EventTypeV2.task_resumed, "approved",
                         {"decision_id": decision_id})

    async def run_finished(self, run_task_id: str, final_state: str, summary: str,
                           extra: dict | None = None) -> None:
        """``final_state`` is ``completed``, ``failed`` or ``cancelled``."""
        row = self._live_card(run_task_id)
        if row is None:
            return
        state = TaskStateV2(final_state)
        type_ = EventTypeV2.task_failed if state == TaskStateV2.failed else EventTypeV2.task_completed
        await self._move(row, state, type_, "ended",
                         {"result_summary": summary, **(extra or {})})
        if state == TaskStateV2.completed:
            await self.release_dependents(row["session_id"], row["id"])

    async def run_failed(self, run_task_id: str, code: str, reason: str) -> None:
        await self.run_finished(run_task_id, TaskStateV2.failed.value,
                                f"Run stopped: {reason}", {"code": code})

    # ─────────────────────── dependencies ───────────────────────
    def dependencies_ready(self, row: dict) -> bool:
        for dep_id in json.loads(row["dependency_json"]):
            dep = self._store.get_task_v2(dep_id)
            if dep is None or dep["task_state"] != TaskStateV2.completed.value:
                return False
        return True

    async def release_dependents(self, session_id: str, completed_task_id: str) -> None:
        """A ``blocked`` card whose dependencies are now all completed becomes ``queued``."""
        for row in self._store.list_tasks_v2(session_id):
            if row["task_state"] != TaskStateV2.blocked.value:
                continue
            if completed_task_id not in json.loads(row["dependency_json"]):
                continue
            if not self.dependencies_ready(row):
                continue
            updated = self._store.update_task_v2(row["id"], task_state=TaskStateV2.queued.value,
                                                 now=utcnow_iso())
            card = card_from_row(updated)
            await self._bus.publish(
                session_id, EventTypeV2.task_resumed,
                {"revision": card.revision, "task_state": card.task_state.value,
                 "reason": "dependencies_completed", "unblocked_by": completed_task_id,
                 "source_mode": card.source_mode},
                task_id=card.task_id, agent_id=card.agent_id)

    # ─────────────────────── startup ───────────────────────
    async def fail_orphaned_cards(self, storage) -> int:
        """Startup safety net: a card still showing a run in progress whose v1 run is
        missing or already terminal has no driver, so it must not stay 'running'."""
        n = 0
        for row in self._store.list_unsettled_run_cards_v2():
            run = storage.get_task(row["run_task_id"])
            if run is not None and run["status"] not in ("completed", "failed", "cancelled"):
                continue  # still in flight: the run reconciliation owns it
            await self.run_failed(row["run_task_id"], "RUN_INTERRUPTED",
                                  "the backend restarted while this run was in progress; "
                                  "start the task again")
            n += 1
        return n
