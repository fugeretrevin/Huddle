"""SQLite durable storage. Single connection guarded by a re-entrant lock.

The event table is the source of truth: seq is allocated and the event row is
committed inside one transaction BEFORE the in-memory bus broadcasts, so a
reconnecting client can always replay a gap-free, duplicate-free stream.
"""
from __future__ import annotations

import json
import os
import sqlite3
import threading
from typing import Any

SCHEMA = """
CREATE TABLE IF NOT EXISTS sessions (
    id TEXT PRIMARY KEY,
    mode TEXT NOT NULL,
    created_at TEXT NOT NULL,
    last_seq INTEGER NOT NULL DEFAULT 0,
    token_hash TEXT NOT NULL,
    expires_at TEXT
);
CREATE TABLE IF NOT EXISTS agents (
    session_id TEXT NOT NULL,
    agent_id TEXT NOT NULL,
    role TEXT NOT NULL,
    display_name TEXT NOT NULL,
    state TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    PRIMARY KEY (session_id, agent_id)
);
CREATE TABLE IF NOT EXISTS tasks (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    agent_id TEXT,
    parent_task_id TEXT,
    status TEXT NOT NULL,
    template TEXT,
    dependency_json TEXT NOT NULL DEFAULT '[]',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS decisions (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    task_id TEXT,
    agent_id TEXT,
    status TEXT NOT NULL,
    summary TEXT NOT NULL,
    recommendation TEXT,
    details_json TEXT NOT NULL DEFAULT '{}',
    resolved_at TEXT,
    resolved_action TEXT,
    response_key TEXT
);
CREATE TABLE IF NOT EXISTS events (
    session_id TEXT NOT NULL,
    seq INTEGER NOT NULL,
    event_id TEXT NOT NULL UNIQUE,
    type TEXT NOT NULL,
    data_json TEXT NOT NULL,
    occurred_at TEXT NOT NULL,
    PRIMARY KEY (session_id, seq)
);
CREATE TABLE IF NOT EXISTS artifacts (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    task_id TEXT,
    type TEXT NOT NULL,
    summary TEXT NOT NULL,
    content_json TEXT NOT NULL DEFAULT '{}',
    mode TEXT NOT NULL,
    created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS idempotency (
    session_id TEXT NOT NULL,
    key TEXT NOT NULL,
    method TEXT NOT NULL,
    route TEXT NOT NULL,
    result_json TEXT NOT NULL,
    PRIMARY KEY (session_id, key)
);
"""


class Storage:
    def __init__(self, db_path: str) -> None:
        self.db_path = db_path
        if db_path != ":memory:":
            parent = os.path.dirname(os.path.abspath(db_path))
            os.makedirs(parent, exist_ok=True)
        self._lock = threading.RLock()
        self._conn = sqlite3.connect(db_path, check_same_thread=False)
        self._conn.row_factory = sqlite3.Row
        self._conn.execute("PRAGMA journal_mode=WAL;")
        self._conn.execute("PRAGMA foreign_keys=ON;")
        with self._lock:
            self._conn.executescript(SCHEMA)
            self._conn.commit()

    def close(self) -> None:
        with self._lock:
            self._conn.close()

    # ─────────────────────── sessions ───────────────────────
    def create_session(self, session_id: str, mode: str, created_at: str,
                        token_hash: str, expires_at: str | None) -> None:
        with self._lock:
            self._conn.execute(
                "INSERT INTO sessions(id, mode, created_at, last_seq, token_hash, expires_at)"
                " VALUES (?,?,?,?,?,?)",
                (session_id, mode, created_at, 0, token_hash, expires_at),
            )
            self._conn.commit()

    def get_session(self, session_id: str) -> dict[str, Any] | None:
        with self._lock:
            row = self._conn.execute(
                "SELECT * FROM sessions WHERE id=?", (session_id,)
            ).fetchone()
        return dict(row) if row else None

    def session_last_seq(self, session_id: str) -> int:
        with self._lock:
            row = self._conn.execute(
                "SELECT last_seq FROM sessions WHERE id=?", (session_id,)
            ).fetchone()
        return int(row["last_seq"]) if row else 0

    # ─────────────────────── agents ───────────────────────
    def upsert_agent(self, session_id: str, agent_id: str, role: str,
                     display_name: str, state: str, updated_at: str) -> None:
        with self._lock:
            self._conn.execute(
                "INSERT INTO agents(session_id, agent_id, role, display_name, state, updated_at)"
                " VALUES (?,?,?,?,?,?)"
                " ON CONFLICT(session_id, agent_id) DO UPDATE SET"
                " role=excluded.role, display_name=excluded.display_name,"
                " state=excluded.state, updated_at=excluded.updated_at",
                (session_id, agent_id, role, display_name, state, updated_at),
            )
            self._conn.commit()

    def set_agent_state(self, session_id: str, agent_id: str, state: str, updated_at: str) -> None:
        with self._lock:
            self._conn.execute(
                "UPDATE agents SET state=?, updated_at=? WHERE session_id=? AND agent_id=?",
                (state, updated_at, session_id, agent_id),
            )
            self._conn.commit()

    def list_agents(self, session_id: str) -> list[dict[str, Any]]:
        with self._lock:
            rows = self._conn.execute(
                "SELECT * FROM agents WHERE session_id=? ORDER BY agent_id", (session_id,)
            ).fetchall()
        return [dict(r) for r in rows]

    # ─────────────────────── tasks ───────────────────────
    def upsert_task(self, task_id: str, session_id: str, agent_id: str | None,
                    parent_task_id: str | None, status: str, template: str | None,
                    dependencies: list[str], created_at: str, updated_at: str) -> None:
        with self._lock:
            self._conn.execute(
                "INSERT INTO tasks(id, session_id, agent_id, parent_task_id, status, template,"
                " dependency_json, created_at, updated_at) VALUES (?,?,?,?,?,?,?,?,?)"
                " ON CONFLICT(id) DO UPDATE SET status=excluded.status,"
                " updated_at=excluded.updated_at"
                " WHERE tasks.status NOT IN ('completed','failed','cancelled')",
                (task_id, session_id, agent_id, parent_task_id, status, template,
                 json.dumps(dependencies), created_at, updated_at),
            )
            self._conn.commit()

    def set_task_status(self, task_id: str, status: str, updated_at: str) -> None:
        with self._lock:
            self._conn.execute(
                # Terminal states are final: a late/duplicate write must not reopen or
                # flip a failed/cancelled/completed task.
                "UPDATE tasks SET status=?, updated_at=? WHERE id=?"
                " AND status NOT IN ('completed','failed','cancelled')",
                (status, updated_at, task_id),
            )
            self._conn.commit()

    def get_task(self, task_id: str) -> dict[str, Any] | None:
        with self._lock:
            row = self._conn.execute("SELECT * FROM tasks WHERE id=?", (task_id,)).fetchone()
        return dict(row) if row else None

    def list_tasks(self, session_id: str) -> list[dict[str, Any]]:
        with self._lock:
            rows = self._conn.execute(
                "SELECT * FROM tasks WHERE session_id=? ORDER BY created_at, id", (session_id,)
            ).fetchall()
        return [dict(r) for r in rows]

    def list_unfinished_runs(self, session_id: str | None = None) -> list[dict[str, Any]]:
        """Top-level (parent) tasks that have not reached a terminal status, i.e. runs
        still in flight. Every run sets its parent terminal when it ends, so after a
        restart anything returned here is a run whose in-memory driver is gone."""
        sql = ("SELECT * FROM tasks WHERE parent_task_id IS NULL"
               " AND status NOT IN ('completed','failed','cancelled')")
        args: tuple = ()
        if session_id is not None:
            sql += " AND session_id=?"
            args = (session_id,)
        with self._lock:
            rows = self._conn.execute(sql + " ORDER BY created_at, id", args).fetchall()
        return [dict(r) for r in rows]

    # ─────────────────────── decisions ───────────────────────
    def create_decision(self, decision_id: str, session_id: str, task_id: str | None,
                        agent_id: str | None, summary: str, recommendation: str | None,
                        details: dict[str, Any]) -> None:
        with self._lock:
            self._conn.execute(
                "INSERT INTO decisions(id, session_id, task_id, agent_id, status, summary,"
                " recommendation, details_json) VALUES (?,?,?,?,?,?,?,?)",
                (decision_id, session_id, task_id, agent_id, "requested", summary,
                 recommendation, json.dumps(details)),
            )
            self._conn.commit()

    def get_decision(self, decision_id: str) -> dict[str, Any] | None:
        with self._lock:
            row = self._conn.execute(
                "SELECT * FROM decisions WHERE id=?", (decision_id,)
            ).fetchone()
        return dict(row) if row else None

    def resolve_decision(self, decision_id: str, state: str, action: str,
                        resolved_at: str, response_key: str) -> bool:
        """Atomically resolve a decision iff still unresolved. Returns True if this
        call performed the resolution, False if it was already resolved."""
        with self._lock:
            cur = self._conn.execute(
                "UPDATE decisions SET status=?, resolved_action=?, resolved_at=?, response_key=?"
                " WHERE id=? AND resolved_at IS NULL",
                (state, action, resolved_at, response_key, decision_id),
            )
            self._conn.commit()
            return cur.rowcount == 1

    def expire_decision(self, decision_id: str, resolved_at: str) -> bool:
        """Atomically mark a still-unresolved decision ``expired`` (its run is gone, so
        no answer could ever be acted on). Returns False if it was already resolved."""
        with self._lock:
            cur = self._conn.execute(
                "UPDATE decisions SET status='expired', resolved_at=?"
                " WHERE id=? AND resolved_at IS NULL",
                (resolved_at, decision_id),
            )
            self._conn.commit()
            return cur.rowcount == 1

    def list_decisions(self, session_id: str) -> list[dict[str, Any]]:
        with self._lock:
            rows = self._conn.execute(
                "SELECT * FROM decisions WHERE session_id=? ORDER BY id", (session_id,)
            ).fetchall()
        return [dict(r) for r in rows]

    # ─────────────────────── events ───────────────────────
    def append_event(self, session_id: str, event_id: str, type_: str,
                     data_json: str, occurred_at: str) -> int:
        """Allocate the next per-session seq and persist the event atomically.
        Returns the allocated seq."""
        with self._lock:
            row = self._conn.execute(
                "SELECT COALESCE(MAX(seq),0) AS m FROM events WHERE session_id=?",
                (session_id,),
            ).fetchone()
            seq = int(row["m"]) + 1
            self._conn.execute(
                "INSERT INTO events(session_id, seq, event_id, type, data_json, occurred_at)"
                " VALUES (?,?,?,?,?,?)",
                (session_id, seq, event_id, type_, data_json, occurred_at),
            )
            self._conn.execute(
                "UPDATE sessions SET last_seq=? WHERE id=?", (seq, session_id)
            )
            self._conn.commit()
        return seq

    def events_after(self, session_id: str, after_seq: int) -> list[dict[str, Any]]:
        with self._lock:
            rows = self._conn.execute(
                "SELECT data_json FROM events WHERE session_id=? AND seq>? ORDER BY seq",
                (session_id, after_seq),
            ).fetchall()
        return [json.loads(r["data_json"]) for r in rows]

    # ─────────────────────── artifacts ───────────────────────
    def create_artifact(self, artifact_id: str, session_id: str, task_id: str | None,
                        type_: str, summary: str, content: dict[str, Any], mode: str,
                        created_at: str) -> None:
        with self._lock:
            self._conn.execute(
                "INSERT INTO artifacts(id, session_id, task_id, type, summary, content_json,"
                " mode, created_at) VALUES (?,?,?,?,?,?,?,?)",
                (artifact_id, session_id, task_id, type_, summary, json.dumps(content),
                 mode, created_at),
            )
            self._conn.commit()

    def get_artifact(self, artifact_id: str) -> dict[str, Any] | None:
        with self._lock:
            row = self._conn.execute(
                "SELECT * FROM artifacts WHERE id=?", (artifact_id,)
            ).fetchone()
        return dict(row) if row else None

    # ─────────────────────── idempotency ───────────────────────
    def get_idempotent(self, session_id: str, key: str) -> dict[str, Any] | None:
        with self._lock:
            row = self._conn.execute(
                "SELECT result_json FROM idempotency WHERE session_id=? AND key=?",
                (session_id, key),
            ).fetchone()
        return json.loads(row["result_json"]) if row else None

    def save_idempotent(self, session_id: str, key: str, method: str, route: str,
                        result: dict[str, Any]) -> None:
        with self._lock:
            self._conn.execute(
                "INSERT OR IGNORE INTO idempotency(session_id, key, method, route, result_json)"
                " VALUES (?,?,?,?,?)",
                (session_id, key, method, route, json.dumps(result)),
            )
            self._conn.commit()
