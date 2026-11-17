"""Focus / interruption-management service (P2-06 foundation).

The server decides which notifications are presented vs queued to the inbox. A
critical authorization notification is NEVER suppressed, regardless of focus
mode — focus can hide low-priority chatter but never a pending dangerous
approval (MASTER §3.2, §13.3).
"""
from __future__ import annotations

import uuid

from .events import Phase2EventBus, utcnow_iso
from .models import EventTypeV2, FocusMode, FocusPolicyV2, FocusSnapshotV2
from .store import Phase2Store


class FocusService:
    def __init__(self, store: Phase2Store, bus: Phase2EventBus, source_mode: str) -> None:
        self._store = store
        self._bus = bus
        self._mode = source_mode

    def get_policy(self, session_id: str) -> FocusPolicyV2:
        row = self._store.get_focus_v2(session_id)
        if row is None:
            return FocusPolicyV2()
        import json
        return FocusPolicyV2(mode=row["mode"], until=row["until"],
                             quiet_priorities=json.loads(row["quiet_priorities_json"]),
                             updated_at=row["updated_at"], revision=row["revision"])

    def snapshot_projection(self, session_id: str) -> FocusSnapshotV2:
        p = self.get_policy(session_id)
        return FocusSnapshotV2(mode=p.mode, revision=p.revision, until=p.until)

    async def set_focus(self, session_id: str, mode: str, until: str | None,
                       quiet_priorities: list[int]) -> FocusPolicyV2:
        prior = self.get_policy(session_id)
        now = utcnow_iso()
        revision = prior.revision + 1
        self._store.upsert_focus_v2(session_id, mode, until, quiet_priorities, revision, now)
        await self._bus.publish(session_id, EventTypeV2.focus_changed,
                                {"mode": mode, "until": until, "revision": revision,
                                 "source_mode": self._mode})
        return FocusPolicyV2(mode=FocusMode(mode), until=until, quiet_priorities=quiet_priorities,
                             updated_at=now, revision=revision)

    def decide(self, session_id: str, priority: int, *, critical: bool) -> str:
        """Return ``"present"`` or ``"queue"`` for a notification of given priority.

        Critical notifications are ALWAYS presented. Otherwise, in ``focus`` mode a
        priority listed in ``quiet_priorities`` is queued; in ``do_not_disturb`` all
        non-critical notifications are queued; in ``normal`` everything is presented.
        """
        if critical:
            return "present"
        policy = self.get_policy(session_id)
        if policy.mode == FocusMode.do_not_disturb:
            return "queue"
        if policy.mode == FocusMode.focus:
            default_quiet = policy.quiet_priorities or [0, 1]
            return "queue" if priority in default_quiet else "present"
        return "present"

    async def route_notification(self, session_id: str, kind: str, summary: str,
                               priority: int, *, critical: bool) -> tuple[str, dict]:
        """Apply the focus policy to one notification. On ``queue`` the item is
        persisted to the inbox and an ``inbox.updated`` event is emitted; on
        ``present`` nothing is queued and the caller presents it normally."""
        decision = self.decide(session_id, priority, critical=critical)
        if decision == "queue":
            item_id = f"ib-{uuid.uuid4().hex[:10]}"
            now = utcnow_iso()
            self._store.add_inbox_v2(item_id, session_id, kind, summary, priority,
                                     critical, self._mode, now)
            # ``item`` is the full InboxItemV2 the snapshot would carry. The Unity reducer
            # reads payload["item"] to upsert its inbox projection; the flat fields are
            # kept for any consumer that read the original shape.
            item = {"item_id": item_id, "kind": kind, "summary": summary,
                    "priority": priority, "critical": critical, "created_at": now,
                    "source_mode": self._mode}
            await self._bus.publish(session_id, EventTypeV2.inbox_updated,
                                    {"item": item, "item_id": item_id, "kind": kind,
                                     "summary": summary, "priority": priority,
                                     "critical": critical, "source_mode": self._mode})
            return decision, {"item_id": item_id}
        return decision, {}
