"""Task graph / DAG service (P2-01 foundation).

Server is the sole authority for assignment, priority, dependency readiness and
pause. Every mutation is guarded by an optimistic ``expected_revision`` check and
bumps the task ``revision`` so a stale client command can never silently overwrite
newer state. No process is ever killed.

Two kinds of card exist and the card says which it is (``executable``):
- an EXECUTABLE card (``task_template="auth_demo"``) launches the real pipeline run
  when it is assigned to the researcher; the run then drives the card's state
  (see ``board_bridge``);
- every other card is a plain board card. Nothing executes it, so it is never shown
  as ``running`` or ``pause_requested`` by a command.
"""
from __future__ import annotations

import uuid

from .board_bridge import BoardRunBridge, card_from_row
from .errors import CommandError, NotFoundError
from .events import Phase2EventBus, utcnow_iso
from .models import (
    EXECUTABLE_TEMPLATES,
    PIPELINE_FIRST_AGENT,
    SAFE_ASSIGN_STATES,
    TERMINAL_STATES,
    EventTypeV2,
    TaskCardV2,
    TaskStateV2,
)
from .store import Phase2Store

_card = card_from_row


def _canon(row: dict) -> dict:
    return _card(row).model_dump(mode="json")


class TaskGraphService:
    def __init__(self, store: Phase2Store, bus: Phase2EventBus, source_mode: str,
                 launcher=None) -> None:
        self._store = store
        self._bus = bus
        self._mode = source_mode
        # ``app.run_launcher.RunLauncher``; None in service-level unit tests, where an
        # executable card then cannot be started (EXECUTOR_UNAVAILABLE).
        self._launcher = launcher
        self._board = BoardRunBridge(store, bus)

    # ─────────────────────── creation / queries ───────────────────────
    def create_task(self, session_id: str, title: str, agent_id: str | None,
                    priority: int, dependencies: list[str], role_lock: str | None,
                    task_template: str | None = None) -> TaskCardV2:
        if task_template is not None and task_template not in EXECUTABLE_TEMPLATES:
            raise CommandError(
                "UNKNOWN_TASK_TEMPLATE",
                f"Unknown task template '{task_template}'. Supported: "
                + ", ".join(sorted(EXECUTABLE_TEMPLATES)) + ".", status=422)
        if task_template is not None and role_lock not in (None, PIPELINE_FIRST_AGENT):
            raise CommandError(
                "ROLE_INCOMPATIBLE",
                "This task starts with the Researcher, so it cannot be locked to "
                f"the {role_lock} role.", status=422)
        # A dependency must be an existing card of THIS session. An unknown or foreign
        # id was previously accepted and left the new card ``blocked`` forever.
        unknown = []
        for dep_id in dependencies:
            dep = self._store.get_task_v2(dep_id)
            if dep is None or dep["session_id"] != session_id:
                unknown.append(dep_id)
        if unknown:
            raise CommandError(
                "UNKNOWN_DEPENDENCY",
                "Unknown dependency task id(s) for this session: " + ", ".join(unknown) + ".",
                status=422)

        task_id = f"t2-{uuid.uuid4().hex[:10]}"
        now = utcnow_iso()
        ready = all(self._store.get_task_v2(d)["task_state"] == TaskStateV2.completed.value
                    for d in dependencies)
        state = TaskStateV2.queued if ready else TaskStateV2.blocked
        self._store.create_task_v2(task_id, session_id, title, state.value, agent_id,
                                   priority, dependencies, role_lock, self._mode, now,
                                   task_template=task_template)
        return _card(self._store.get_task_v2(task_id))

    async def announce_created(self, session_id: str, card: TaskCardV2) -> None:
        """Emit ``task.created`` for a freshly created card. The Unity board does not
        synthesize a card from the POST response; it waits for this event (exactly as
        its offline demo gateway emits one), so without it a created task never
        appears until the next snapshot resync. Kept separate from ``create_task`` so
        the route emits it once inside the idempotent command (a replayed key does
        not re-emit)."""
        await self._bus.publish(
            session_id, EventTypeV2.task_created,
            {"title": card.title, "task_state": card.task_state.value,
             "priority": card.priority, "revision": card.revision,
             "dependencies": card.dependencies, "task_template": card.task_template,
             "executable": card.executable, "source_mode": card.source_mode},
            task_id=card.task_id, agent_id=card.agent_id,
        )

    def dependencies_ready(self, session_id: str, task_row: dict) -> bool:
        return self._board.dependencies_ready(task_row)

    def _require(self, session_id: str, task_id: str) -> dict:
        # tasks_v2.id is a GLOBAL primary key, so the lookup must also be scoped to the
        # authenticated session: otherwise a valid token for session B could mutate a
        # task that belongs to session A just by naming its id in B's URL.
        row = self._store.get_task_v2(task_id)
        if row is None or row["session_id"] != session_id:
            raise NotFoundError(f"unknown task {task_id}")
        return row

    def _guard_revision(self, row: dict, expected_revision: int) -> None:
        if row["revision"] != expected_revision:
            raise CommandError(
                "REVISION_CONFLICT",
                f"task revision is {row['revision']}, command expected {expected_revision}",
                status=409, canonical=_canon(row),
            )

    # ─────────────────────── commands ───────────────────────
    async def assign(self, session_id: str, task_id: str, target_agent: str,
                    expected_revision: int) -> TaskCardV2:
        row = self._require(session_id, task_id)
        self._guard_revision(row, expected_revision)
        state = TaskStateV2(row["task_state"])
        executable = row.get("task_template") in EXECUTABLE_TEMPLATES
        if state in TERMINAL_STATES:
            raise CommandError("TASK_NOT_ASSIGNABLE",
                               f"task is {state.value}; cannot reassign",
                               status=409, canonical=_canon(row))
        if state == TaskStateV2.running:
            raise CommandError("TASK_RUNNING",
                               "This task's pipeline is already running; it cannot be "
                               "reassigned." if row.get("run_task_id") else
                               "task is running (live work in progress); pause before reassigning",
                               status=409, canonical=_canon(row))
        if state not in SAFE_ASSIGN_STATES and state != TaskStateV2.blocked:
            raise CommandError("UNSAFE_TRANSFER",
                               f"task state {state.value} is not a safe reassignment checkpoint",
                               status=409, canonical=_canon(row))
        if row["role_lock"] and row["role_lock"] != target_agent:
            raise CommandError("ROLE_INCOMPATIBLE",
                               f"task is locked to role {row['role_lock']}",
                               status=409, canonical=_canon(row))
        if executable and not row.get("run_task_id"):
            return await self._assign_and_launch(session_id, row, target_agent)
        updated = self._store.update_task_v2(task_id, agent_id=target_agent, now=utcnow_iso())
        card = _card(updated)
        await self._bus.publish(session_id, EventTypeV2.task_assigned,
                                {"target_agent_id": target_agent, "revision": card.revision,
                                 "source_mode": self._mode},
                                task_id=task_id, agent_id=target_agent)
        return card

    async def _assign_and_launch(self, session_id: str, row: dict,
                                 target_agent: str) -> TaskCardV2:
        """Assigning an executable, not-yet-started card to the researcher STARTS the
        pipeline run. Every refusal below leaves the card untouched and starts nothing."""
        task_id = row["id"]
        state = TaskStateV2(row["task_state"])
        if target_agent != PIPELINE_FIRST_AGENT:
            raise CommandError("ROLE_INCOMPATIBLE",
                               "This task starts with the Researcher. Assign it to the "
                               "Researcher to run it.",
                               status=409, canonical=_canon(row))
        if state == TaskStateV2.blocked or not self._board.dependencies_ready(row):
            raise CommandError("DEPENDENCIES_NOT_READY",
                               "This task is blocked: it depends on tasks that have not "
                               "completed yet.",
                               status=409, canonical=_canon(row))
        if state == TaskStateV2.paused:
            raise CommandError("TASK_PAUSED",
                               "This task is paused. Resume it before starting it.",
                               status=409, canonical=_canon(row))
        if self._launcher is None:
            raise CommandError("EXECUTOR_UNAVAILABLE",
                               "No pipeline executor is available to run this task.",
                               status=503, canonical=_canon(row))
        if self._launcher.run_in_progress(session_id):
            raise CommandError("RUN_IN_PROGRESS",
                               "A task is already running in this session. Wait for it "
                               "to finish (or answer its pending decision) first.",
                               status=409, canonical=_canon(row))

        template = row["task_template"]
        run_task_id = self._launcher.create_run(session_id, template)
        try:
            self._store.link_run_v2(task_id, run_task_id)
            updated = self._store.update_task_v2(task_id, agent_id=target_agent,
                                                 now=utcnow_iso())
            await self._bus.publish(session_id, EventTypeV2.task_assigned,
                                    {"target_agent_id": target_agent,
                                     "revision": updated["revision"],
                                     "source_mode": self._mode},
                                    task_id=task_id, agent_id=target_agent)
            card = await self._board.run_started(task_id, run_task_id)
            self._launcher.start(session_id, run_task_id, template, board_task_id=task_id)
        except Exception:
            # Never leave a run that nobody drives: it would block the session.
            self._launcher.abort(run_task_id)
            await self._board.run_failed(run_task_id, "RUN_FAILED", "the run could not be started")
            raise
        return card

    async def set_priority(self, session_id: str, task_id: str, priority: int,
                          expected_revision: int) -> TaskCardV2:
        row = self._require(session_id, task_id)
        self._guard_revision(row, expected_revision)
        if TaskStateV2(row["task_state"]) in TERMINAL_STATES:
            raise CommandError("TASK_NOT_ASSIGNABLE", "task is terminal; cannot reprioritize",
                               status=409, canonical=_canon(row))
        updated = self._store.update_task_v2(task_id, priority=priority, now=utcnow_iso())
        card = _card(updated)
        await self._bus.publish(session_id, EventTypeV2.task_priority_changed,
                                {"priority": priority, "revision": card.revision,
                                 "source_mode": self._mode}, task_id=task_id)
        return card

    async def pause(self, session_id: str, task_id: str, expected_revision: int) -> TaskCardV2:
        row = self._require(session_id, task_id)
        self._guard_revision(row, expected_revision)
        state = TaskStateV2(row["task_state"])
        if state in TERMINAL_STATES:
            raise CommandError("TASK_NOT_PAUSABLE", f"task is {state.value}",
                               status=409, canonical=_canon(row))
        if row.get("run_task_id"):
            # The pipeline run is in progress (running / awaiting approval). It has no
            # safe pause point, so refuse rather than park the card in ``pause_requested``
            # with nothing that could ever honour the request.
            raise CommandError("TASK_NOT_PAUSABLE",
                               "This task's pipeline is in progress and has no safe pause "
                               "point. Answer its decision or wait for it to finish.",
                               status=409, canonical=_canon(row))
        if state == TaskStateV2.paused:
            return _card(row)  # idempotent no-op
        # No executor runs this card, so there is nothing to wait for: pause is
        # immediate from any state. It is never left in ``pause_requested``.
        updated = self._store.update_task_v2(task_id, task_state=TaskStateV2.paused.value,
                                             now=utcnow_iso())
        card = _card(updated)
        await self._bus.publish(session_id, EventTypeV2.task_paused,
                                {"revision": card.revision, "task_state": card.task_state.value,
                                 "source_mode": self._mode},
                                task_id=task_id)
        return card

    async def reach_checkpoint(self, session_id: str, task_id: str) -> TaskCardV2:
        """A task in ``pause_requested`` becomes ``paused``. No command produces
        ``pause_requested`` any more; this settles a row written by an older build."""
        row = self._require(session_id, task_id)
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
        row = self._require(session_id, task_id)
        self._guard_revision(row, expected_revision)
        state = TaskStateV2(row["task_state"])
        if state not in (TaskStateV2.paused, TaskStateV2.pause_requested):
            raise CommandError("TASK_NOT_PAUSED", f"task is {state.value}; cannot resume",
                               status=409, canonical=_canon(row))
        # Resuming never claims ``running``: nothing executes a card on resume (an
        # executable card starts only when it is assigned to the researcher). The card
        # returns to the waiting state its dependencies allow.
        new_state = (TaskStateV2.queued if self._board.dependencies_ready(row)
                     else TaskStateV2.blocked)
        updated = self._store.update_task_v2(task_id, task_state=new_state.value, now=utcnow_iso())
        card = _card(updated)
        await self._bus.publish(session_id, EventTypeV2.task_resumed,
                                {"revision": card.revision, "task_state": new_state.value,
                                 "source_mode": self._mode}, task_id=task_id)
        return card
