"""Honest termination of a run that can no longer make progress.

Two callers:
- an orchestrator whose pipeline raised (``code="RUN_FAILED"``);
- backend startup, for runs persisted as in-flight whose in-memory driver (the asyncio
  task and the decision waiter) died with the previous process (``RUN_INTERRUPTED``).

In both cases the durable state and the event stream are brought to the same terminal
truth: unfinished tasks of the run become ``failed``, a still-pending decision becomes
``expired`` (nothing is left to act on an answer, so it must not stay answerable),
agents left in a working/waiting state become ``error``, and the client is told why.
Only existing event types and enum values are used.
"""
from __future__ import annotations

from .events import EventBus, utcnow_iso
from .models import AgentState, DecisionState, EventType, TaskState
from .storage import Storage

_TERMINAL_TASK = {TaskState.completed.value, TaskState.failed.value, TaskState.cancelled.value}
_SETTLED_AGENT = {AgentState.idle.value, AgentState.complete.value, AgentState.error.value}


async def fail_run(storage: Storage, bus: EventBus, session_id: str, parent_task_id: str, *,
                   code: str, reason: str, agent_message: str, mode: str) -> None:
    now = utcnow_iso()
    run_tasks = [t for t in storage.list_tasks(session_id)
                 if t["id"] == parent_task_id or t["parent_task_id"] == parent_task_id]
    run_task_ids = {t["id"] for t in run_tasks}
    # Children first, the parent last, so the parent's task.failed closes the run.
    to_fail = sorted((t for t in run_tasks if t["status"] not in _TERMINAL_TASK),
                     key=lambda t: t["id"] == parent_task_id)
    for t in to_fail:
        storage.set_task_status(t["id"], TaskState.failed.value, now)

    for a in storage.list_agents(session_id):
        if a["state"] in _SETTLED_AGENT:
            continue
        storage.set_agent_state(session_id, a["agent_id"], AgentState.error.value, now)
        await bus.publish(session_id, EventType.agent_status_changed,
                          {"state": AgentState.error.value,
                           "task_state": TaskState.failed.value,
                           "progress_message": agent_message, "source_mode": mode},
                          agent_id=a["agent_id"])

    for d in storage.list_decisions(session_id):
        if d["resolved_at"] is not None or d["task_id"] not in run_task_ids:
            continue
        if storage.expire_decision(d["id"], now):
            await bus.publish(session_id, EventType.decision_resolved,
                              {"state": DecisionState.expired.value, "action": None,
                               "resolved_at": now, "comment": agent_message,
                               "source_mode": mode},
                              decision_id=d["id"])

    await bus.publish(session_id, EventType.system_warning,
                      {"code": code, "message": reason, "task_id": parent_task_id,
                       "source_mode": mode}, task_id=parent_task_id)
    for t in to_fail:
        await bus.publish(session_id, EventType.task_failed,
                          {"task_id": t["id"], "task_state": TaskState.failed.value,
                           "result_summary": f"Run stopped: {reason}",
                           "source_mode": mode},
                          task_id=t["id"], agent_id=t["agent_id"])


async def reconcile_interrupted_runs(storage: Storage, bus: EventBus, mode: str) -> int:
    """Called once at startup. A run's driver lives only in process memory, so any run
    the database still shows as in flight was cut off by the previous shutdown/crash.
    It cannot be resumed (the waiter and, for live runs, the workspace are gone), so it
    is reported as failed rather than left 'working' forever. Returns the run count."""
    runs = storage.list_unfinished_runs()
    for run in runs:
        await fail_run(storage, bus, run["session_id"], run["id"],
                       code="RUN_INTERRUPTED",
                       reason="the backend restarted while this run was in progress; "
                              "start the task again",
                       agent_message="Run interrupted: backend restarted", mode=mode)
    return len(runs)
