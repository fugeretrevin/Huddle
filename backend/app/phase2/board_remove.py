"""Remove a card from the board, or clear the whole board (owner, rc21: "we also need a way to
remove tasks and clear the board").

A removed card is never deleted: it becomes ``cancelled`` (terminal) with ``removed=1`` and a
new revision, and the board snapshot leaves it out. The client is told with the EXISTING
``task.completed`` event carrying ``task_state="cancelled"`` and the additive
``removed: true`` (no new event type; the v2 contract has no cancel event). Event ids are
canonical per card + revision, and removing a removed card is a no-op, so nothing repeats.

What happens to work in flight:
- Plan / ad-hoc cards worked by the plan runner: the runner checks the card is still running
  before it records anything (``PlanRunner._finish``) and ``Phase2Store.update_task_v2`` never
  moves a removed card out of ``cancelled``, so a result that arrives later is discarded (the
  same path as pause). Files already written in the card's restricted workspace stay on disk.
- A card that launched the ``auth_demo`` pipeline: its pending decision is WITHDRAWN (the
  decision row becomes ``expired``, ``decision.resolved`` says why, and a late answer gets
  409). The waiting run is then woken down the not-approved branch, so nothing gated is built,
  the run ends and the session can start another; nothing waits for the decision timeout.
  A decision the run raises AFTER the card was removed is withdrawn the same way
  (``BoardRunBridge.on_removed_decision``). A pipeline step already in progress finishes on the
  v1 stream; the card ignores it (terminal) and no artifact is announced for it.
- Dependents of a removed card are not changed. They can never start (their dependency is not
  completed); plan cards then show as blocked by the removed step in the plan status.
"""
from __future__ import annotations

import logging

from ..events import utcnow_iso as v1_now
from ..models import DecisionAction, DecisionState, EventType
from .board_bridge import card_from_row
from .errors import NotFoundError
from .events import Phase2EventBus, utcnow_iso
from .models import EventTypeV2, TaskCardV2, TaskStateV2
from .store import Phase2Store

log = logging.getLogger("musedesk.phase2.board_remove")

REMOVED_SUMMARY = "Removed from the board."
WITHDRAWN_COMMENT = "Withdrawn: the task was removed from the board."


async def withdraw_run_decisions(storage, bus, orchestrator, session_id: str,
                                 run_task_id: str, mode: str) -> list[str]:
    """Withdraw every unanswered decision of the v1 run ``run_task_id`` and wake its waiter
    down the not-approved branch. Returns the withdrawn decision ids. Never raises."""
    withdrawn: list[str] = []
    try:
        run_ids = {t["id"] for t in storage.list_tasks(session_id)
                   if t["id"] == run_task_id or t["parent_task_id"] == run_task_id}
        for d in storage.list_decisions(session_id):
            if d["task_id"] not in run_ids or d["resolved_at"] is not None:
                continue
            now = v1_now()
            if not storage.expire_decision(d["id"], now):
                continue                      # answered a moment ago: that answer stands
            withdrawn.append(d["id"])
            await bus.publish(session_id, EventType.decision_resolved,
                              {"state": DecisionState.expired.value, "action": None,
                               "resolved_at": now, "comment": WITHDRAWN_COMMENT,
                               "withdrawn": True, "source_mode": mode},
                              decision_id=d["id"])
            if orchestrator is not None:
                # Never approve on the user's behalf: the run takes the branch where nothing
                # gated is built and ends.
                orchestrator.notify_resolved(d["id"], DecisionAction.reject.value)
    except Exception:  # noqa: BLE001 - removing a card must never fail on run bookkeeping
        log.exception("withdrawing the decisions of run %s failed", run_task_id)
    return withdrawn


class BoardRemover:
    def __init__(self, store: Phase2Store, bus: Phase2EventBus, source_mode: str, *,
                 storage=None, v1_bus=None, orchestrator=None) -> None:
        self._store = store
        self._bus = bus
        self._mode = source_mode
        self._storage = storage
        self._v1_bus = v1_bus
        self._orch = orchestrator

    async def remove(self, session_id: str, task_id: str) -> TaskCardV2:
        row = self._store.get_task_v2(task_id)
        if row is None or row["session_id"] != session_id:
            raise NotFoundError(f"unknown task {task_id}")
        if row.get("removed"):
            return card_from_row(row)           # already removed: same card, nothing sent
        previous = row["task_state"]
        updated = self._store.remove_task_v2(task_id, utcnow_iso())
        if updated is None:                     # removed concurrently
            return card_from_row(self._store.get_task_v2(task_id))
        card = card_from_row(updated)
        await self._bus.publish(
            session_id, EventTypeV2.task_completed,
            {"revision": card.revision, "task_state": TaskStateV2.cancelled.value,
             "result_summary": REMOVED_SUMMARY, "removed": True, "previous_state": previous,
             "source_mode": self._mode},
            task_id=task_id, agent_id=card.agent_id,
            event_id=f"evt2-removed-{task_id}-r{card.revision}")
        run_id = row.get("run_task_id")
        if run_id and self._storage is not None and self._v1_bus is not None \
                and previous not in ("completed", "failed", "cancelled"):
            await withdraw_run_decisions(self._storage, self._v1_bus, self._orch, session_id,
                                         run_id, self._mode)
        return card

    async def clear(self, session_id: str, include_running: bool = True) -> list[str]:
        """Remove every card on the board (all states; ``running`` only when
        ``include_running``). Returns the ids removed by this call, in board order."""
        removed: list[str] = []
        for row in self._store.list_tasks_v2(session_id):
            if not include_running and row["task_state"] == TaskStateV2.running.value:
                continue
            before = row["revision"]
            card = await self.remove(session_id, row["id"])
            if card.revision != before:
                removed.append(row["id"])
        return removed
