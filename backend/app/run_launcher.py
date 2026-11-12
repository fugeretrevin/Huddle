"""The single way a pipeline run is created and started.

Used by BOTH entry points so they launch exactly the same run through the configured
producer (scripted or live):
- ``POST /v1/sessions/{id}/tasks`` (card-less "start demo"), and
- assigning an executable v2 task card to the researcher (``board_task_id`` set).
"""
from __future__ import annotations

import uuid

from .events import utcnow_iso
from .models import TaskState
from .storage import Storage


class RunLauncher:
    def __init__(self, storage: Storage, orchestrator) -> None:
        self._storage = storage
        self._orchestrator = orchestrator

    def run_in_progress(self, session_id: str) -> bool:
        """One run per session at a time (a second concurrent run shared and wiped the
        first run's sandbox workspace and interleaved states on the same three agents)."""
        return bool(self._storage.list_unfinished_runs(session_id))

    def create_run(self, session_id: str, template: str) -> str:
        """Persist the run's parent task as ``running`` and return its id."""
        now = utcnow_iso()
        run_task_id = f"task-{uuid.uuid4().hex[:10]}"
        self._storage.upsert_task(run_task_id, session_id, None, None,
                                  TaskState.running.value, template, [], now, now)
        return run_task_id

    def start(self, session_id: str, run_task_id: str, template: str,
              board_task_id: str | None = None) -> None:
        if template != "auth_demo":  # the only pipeline that exists
            raise ValueError(f"no pipeline for template {template!r}")
        self._orchestrator.start_auth_demo(session_id, run_task_id,
                                           board_task_id=board_task_id)

    def abort(self, run_task_id: str) -> None:
        """A run that was created but could not be started must not block the session."""
        self._storage.set_task_status(run_task_id, TaskState.failed.value, utcnow_iso())
