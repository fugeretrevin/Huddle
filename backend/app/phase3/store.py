"""Additive Phase 3 SQLite storage: one new table in the shared database.

``manager_plans_v3`` is created with ``CREATE TABLE IF NOT EXISTS`` (no existing table is
altered). Also holds the one cross-table helper commit rollback needs, because
``Phase2Store`` has no delete.
"""
from __future__ import annotations

import json
from typing import Any

from ..storage import Storage

SCHEMA_V3 = """
CREATE TABLE IF NOT EXISTS manager_plans_v3 (
    plan_id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    source_request_id TEXT NOT NULL,
    status TEXT NOT NULL,
    revision INTEGER NOT NULL,
    plan_json TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_manager_plans_v3_session ON manager_plans_v3(session_id, created_at);
"""


class Phase3Store:
    def __init__(self, storage: Storage) -> None:
        self._s = storage
        with self._s._lock:  # noqa: SLF001 - same shared-connection coupling as Phase2Store
            self._s._conn.executescript(SCHEMA_V3)
            self._s._conn.commit()

    def insert_plan(self, plan: dict[str, Any], now: str) -> None:
        with self._s._lock:
            self._s._conn.execute(
                "INSERT INTO manager_plans_v3(plan_id, session_id, source_request_id, status,"
                " revision, plan_json, created_at, updated_at) VALUES (?,?,?,?,?,?,?,?)",
                (plan["plan_id"], plan["session_id"], plan["source_request_id"], plan["status"],
                 plan["revision"], json.dumps(plan), now, now))
            self._s._conn.commit()

    def get_plan(self, plan_id: str) -> dict[str, Any] | None:
        with self._s._lock:
            row = self._s._conn.execute(
                "SELECT plan_json FROM manager_plans_v3 WHERE plan_id=?", (plan_id,)).fetchone()
        return json.loads(row["plan_json"]) if row else None

    def latest_plan(self, session_id: str) -> dict[str, Any] | None:
        with self._s._lock:
            row = self._s._conn.execute(
                "SELECT plan_json FROM manager_plans_v3 WHERE session_id=?"
                " ORDER BY created_at DESC, rowid DESC LIMIT 1", (session_id,)).fetchone()
        return json.loads(row["plan_json"]) if row else None

    def save_plan(self, plan: dict[str, Any], expected_revision: int, now: str) -> bool:
        """Compare-and-set on revision. True if written."""
        with self._s._lock:
            cur = self._s._conn.execute(
                "UPDATE manager_plans_v3 SET status=?, revision=?, plan_json=?, updated_at=?"
                " WHERE plan_id=? AND revision=?",
                (plan["status"], plan["revision"], json.dumps(plan), now,
                 plan["plan_id"], expected_revision))
            self._s._conn.commit()
            return cur.rowcount == 1

    def delete_tasks_v2(self, task_ids: list[str]) -> int:
        """Rollback helper: remove task rows this commit created (nothing else)."""
        if not task_ids:
            return 0
        with self._s._lock:
            q = ",".join("?" * len(task_ids))
            cur = self._s._conn.execute(f"DELETE FROM tasks_v2 WHERE id IN ({q})", task_ids)
            self._s._conn.commit()
            return cur.rowcount
