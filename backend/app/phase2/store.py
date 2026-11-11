"""Additive Phase 2 SQLite storage.

Reuses the Phase 1 :class:`app.storage.Storage` connection and lock so v2 data
lives in the same database file and shares atomicity, but every table here is
NEW (``*_v2``). No Phase 1 table or row is read or written. In particular the v2
event log is a separate ``events_v2`` table with its OWN monotonic per-session
``seq`` — so a v1 subscriber replaying the v1 ``events`` table never observes a
v2-only event.
"""
from __future__ import annotations

import hashlib
import json
from typing import Any

from ..storage import Storage

SCHEMA_V2 = """
CREATE TABLE IF NOT EXISTS tasks_v2 (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    title TEXT NOT NULL,
    task_state TEXT NOT NULL,
    agent_id TEXT,
    priority INTEGER NOT NULL DEFAULT 1,
    revision INTEGER NOT NULL DEFAULT 0,
    dependency_json TEXT NOT NULL DEFAULT '[]',
    role_lock TEXT,
    source_mode TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS handoffs_v2 (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    from_agent_id TEXT NOT NULL,
    to_agent_id TEXT NOT NULL,
    task_id TEXT NOT NULL,
    artifact_id TEXT NOT NULL,
    kind TEXT NOT NULL,
    state TEXT NOT NULL,
    source_mode TEXT NOT NULL,
    created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS artifacts_v2 (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    task_id TEXT NOT NULL,
    kind TEXT NOT NULL,
    summary TEXT NOT NULL,
    source_mode TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    created_at TEXT NOT NULL,
    pages_json TEXT NOT NULL DEFAULT '[]'
);
CREATE TABLE IF NOT EXISTS focus_v2 (
    session_id TEXT PRIMARY KEY,
    mode TEXT NOT NULL DEFAULT 'normal',
    until TEXT,
    quiet_priorities_json TEXT NOT NULL DEFAULT '[]',
    revision INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT
);
CREATE TABLE IF NOT EXISTS inbox_v2 (
    item_id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    kind TEXT NOT NULL,
    summary TEXT NOT NULL,
    priority INTEGER NOT NULL DEFAULT 1,
    critical INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    source_mode TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS diagrams_v2 (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    template TEXT NOT NULL,
    source_mode TEXT NOT NULL,
    nodes_json TEXT NOT NULL DEFAULT '[]',
    edges_json TEXT NOT NULL DEFAULT '[]',
    created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS events_v2 (
    session_id TEXT NOT NULL,
    seq INTEGER NOT NULL,
    event_id TEXT NOT NULL UNIQUE,
    type TEXT NOT NULL,
    data_json TEXT NOT NULL,
    occurred_at TEXT NOT NULL,
    PRIMARY KEY (session_id, seq)
);
CREATE TABLE IF NOT EXISTS idempotency_v2 (
    session_id TEXT NOT NULL,
    key TEXT NOT NULL,
    route TEXT NOT NULL,
    body_hash TEXT NOT NULL,
    result_json TEXT NOT NULL,
    PRIMARY KEY (session_id, key)
);
"""


def body_hash(body: Any) -> str:
    """Stable hash of a command body for idempotency (session, route, key, body)."""
    canonical = json.dumps(body, sort_keys=True, separators=(",", ":"), default=str)
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


class Phase2Store:
    def __init__(self, storage: Storage) -> None:
        self._s = storage
        with self._s._lock:  # noqa: SLF001 - intentional shared-connection coupling
            self._s._conn.executescript(SCHEMA_V2)
            self._s._conn.commit()

    # ─────────────────────── events (own monotonic seq) ───────────────────────
    def append_event_v2(self, session_id: str, event_id: str, type_: str,
                        data_json: str, occurred_at: str) -> int:
        with self._s._lock:
            row = self._s._conn.execute(
                "SELECT COALESCE(MAX(seq),0) AS m FROM events_v2 WHERE session_id=?",
                (session_id,),
            ).fetchone()
            seq = int(row["m"]) + 1
            self._s._conn.execute(
                "INSERT INTO events_v2(session_id, seq, event_id, type, data_json, occurred_at)"
                " VALUES (?,?,?,?,?,?)",
                (session_id, seq, event_id, type_, data_json, occurred_at),
            )
            self._s._conn.commit()
        return seq

    def rewrite_event_v2(self, session_id: str, seq: int, data_json: str) -> None:
        with self._s._lock:
            self._s._conn.execute(
                "UPDATE events_v2 SET data_json=? WHERE session_id=? AND seq=?",
                (data_json, session_id, seq),
            )
            self._s._conn.commit()

    def events_after_v2(self, session_id: str, after_seq: int) -> list[dict[str, Any]]:
        with self._s._lock:
            rows = self._s._conn.execute(
                "SELECT data_json FROM events_v2 WHERE session_id=? AND seq>? ORDER BY seq",
                (session_id, after_seq),
            ).fetchall()
        return [json.loads(r["data_json"]) for r in rows]

    def last_seq_v2(self, session_id: str) -> int:
        with self._s._lock:
            row = self._s._conn.execute(
                "SELECT COALESCE(MAX(seq),0) AS m FROM events_v2 WHERE session_id=?",
                (session_id,),
            ).fetchone()
        return int(row["m"]) if row else 0

    def event_exists(self, event_id: str) -> bool:
        with self._s._lock:
            row = self._s._conn.execute(
                "SELECT 1 FROM events_v2 WHERE event_id=?", (event_id,)
            ).fetchone()
        return row is not None

    # ─────────────────────── idempotency ───────────────────────
    def get_idempotent_v2(self, session_id: str, key: str) -> dict[str, Any] | None:
        with self._s._lock:
            row = self._s._conn.execute(
                "SELECT body_hash, result_json FROM idempotency_v2 WHERE session_id=? AND key=?",
                (session_id, key),
            ).fetchone()
        if row is None:
            return None
        return {"body_hash": row["body_hash"], "result": json.loads(row["result_json"])}

    def save_idempotent_v2(self, session_id: str, key: str, route: str,
                          bhash: str, result: dict[str, Any]) -> None:
        with self._s._lock:
            self._s._conn.execute(
                "INSERT OR IGNORE INTO idempotency_v2(session_id, key, route, body_hash, result_json)"
                " VALUES (?,?,?,?,?)",
                (session_id, key, route, bhash, json.dumps(result)),
            )
            self._s._conn.commit()

    # ─────────────────────── tasks ───────────────────────
    def create_task_v2(self, task_id: str, session_id: str, title: str, task_state: str,
                       agent_id: str | None, priority: int, dependencies: list[str],
                       role_lock: str | None, source_mode: str, now: str) -> None:
        with self._s._lock:
            self._s._conn.execute(
                "INSERT INTO tasks_v2(id, session_id, title, task_state, agent_id, priority,"
                " revision, dependency_json, role_lock, source_mode, created_at, updated_at)"
                " VALUES (?,?,?,?,?,?,?,?,?,?,?,?)",
                (task_id, session_id, title, task_state, agent_id, priority, 0,
                 json.dumps(dependencies), role_lock, source_mode, now, now),
            )
            self._s._conn.commit()

    def get_task_v2(self, task_id: str) -> dict[str, Any] | None:
        with self._s._lock:
            row = self._s._conn.execute(
                "SELECT * FROM tasks_v2 WHERE id=?", (task_id,)
            ).fetchone()
        return dict(row) if row else None

    def list_tasks_v2(self, session_id: str) -> list[dict[str, Any]]:
        with self._s._lock:
            rows = self._s._conn.execute(
                "SELECT * FROM tasks_v2 WHERE session_id=? ORDER BY created_at, id",
                (session_id,),
            ).fetchall()
        return [dict(r) for r in rows]

    def update_task_v2(self, task_id: str, *, task_state: str | None = None,
                      agent_id: str | None = None, priority: int | None = None,
                      bump_revision: bool = True, now: str) -> dict[str, Any] | None:
        """Mutate a task and (optionally) bump its revision atomically."""
        with self._s._lock:
            row = self._s._conn.execute(
                "SELECT * FROM tasks_v2 WHERE id=?", (task_id,)
            ).fetchone()
            if row is None:
                return None
            cur = dict(row)
            new_state = task_state if task_state is not None else cur["task_state"]
            new_agent = agent_id if agent_id is not None else cur["agent_id"]
            new_prio = priority if priority is not None else cur["priority"]
            new_rev = cur["revision"] + (1 if bump_revision else 0)
            self._s._conn.execute(
                "UPDATE tasks_v2 SET task_state=?, agent_id=?, priority=?, revision=?, updated_at=?"
                " WHERE id=?",
                (new_state, new_agent, new_prio, new_rev, now, task_id),
            )
            self._s._conn.commit()
            return self.get_task_v2(task_id)

    # ─────────────────────── handoffs ───────────────────────
    def create_handoff_v2(self, handoff_id: str, session_id: str, from_agent: str,
                         to_agent: str, task_id: str, artifact_id: str, kind: str,
                         state: str, source_mode: str, now: str) -> bool:
        """Insert iff absent. Returns True if inserted (exactly-once), False if the
        handoff_id already exists."""
        with self._s._lock:
            cur = self._s._conn.execute(
                "INSERT OR IGNORE INTO handoffs_v2(id, session_id, from_agent_id, to_agent_id,"
                " task_id, artifact_id, kind, state, source_mode, created_at)"
                " VALUES (?,?,?,?,?,?,?,?,?,?)",
                (handoff_id, session_id, from_agent, to_agent, task_id, artifact_id,
                 kind, state, source_mode, now),
            )
            self._s._conn.commit()
            return cur.rowcount == 1

    def get_handoff_v2(self, handoff_id: str) -> dict[str, Any] | None:
        with self._s._lock:
            row = self._s._conn.execute(
                "SELECT * FROM handoffs_v2 WHERE id=?", (handoff_id,)
            ).fetchone()
        return dict(row) if row else None

    def set_handoff_state_v2(self, handoff_id: str, state: str) -> None:
        with self._s._lock:
            self._s._conn.execute(
                "UPDATE handoffs_v2 SET state=? WHERE id=?", (state, handoff_id)
            )
            self._s._conn.commit()

    def list_handoffs_v2(self, session_id: str) -> list[dict[str, Any]]:
        with self._s._lock:
            rows = self._s._conn.execute(
                "SELECT * FROM handoffs_v2 WHERE session_id=? ORDER BY created_at, id",
                (session_id,),
            ).fetchall()
        return [dict(r) for r in rows]

    # ─────────────────────── artifacts ───────────────────────
    def create_artifact_v2(self, artifact_id: str, session_id: str, task_id: str,
                          kind: str, summary: str, source_mode: str, content_hash: str,
                          pages: list[dict[str, Any]], now: str) -> None:
        with self._s._lock:
            self._s._conn.execute(
                "INSERT INTO artifacts_v2(id, session_id, task_id, kind, summary, source_mode,"
                " content_hash, created_at, pages_json) VALUES (?,?,?,?,?,?,?,?,?)",
                (artifact_id, session_id, task_id, kind, summary, source_mode,
                 content_hash, now, json.dumps(pages)),
            )
            self._s._conn.commit()

    def get_artifact_v2(self, artifact_id: str) -> dict[str, Any] | None:
        with self._s._lock:
            row = self._s._conn.execute(
                "SELECT * FROM artifacts_v2 WHERE id=?", (artifact_id,)
            ).fetchone()
        return dict(row) if row else None

    def list_artifacts_v2(self, session_id: str) -> list[dict[str, Any]]:
        with self._s._lock:
            rows = self._s._conn.execute(
                "SELECT * FROM artifacts_v2 WHERE session_id=? ORDER BY created_at, id",
                (session_id,),
            ).fetchall()
        return [dict(r) for r in rows]

    # ─────────────────────── focus ───────────────────────
    def get_focus_v2(self, session_id: str) -> dict[str, Any] | None:
        with self._s._lock:
            row = self._s._conn.execute(
                "SELECT * FROM focus_v2 WHERE session_id=?", (session_id,)
            ).fetchone()
        return dict(row) if row else None

    def upsert_focus_v2(self, session_id: str, mode: str, until: str | None,
                       quiet_priorities: list[int], revision: int, now: str) -> None:
        with self._s._lock:
            self._s._conn.execute(
                "INSERT INTO focus_v2(session_id, mode, until, quiet_priorities_json, revision, updated_at)"
                " VALUES (?,?,?,?,?,?)"
                " ON CONFLICT(session_id) DO UPDATE SET mode=excluded.mode, until=excluded.until,"
                " quiet_priorities_json=excluded.quiet_priorities_json, revision=excluded.revision,"
                " updated_at=excluded.updated_at",
                (session_id, mode, until, json.dumps(quiet_priorities), revision, now),
            )
            self._s._conn.commit()

    # ─────────────────────── inbox ───────────────────────
    def add_inbox_v2(self, item_id: str, session_id: str, kind: str, summary: str,
                    priority: int, critical: bool, source_mode: str, now: str) -> None:
        with self._s._lock:
            self._s._conn.execute(
                "INSERT OR IGNORE INTO inbox_v2(item_id, session_id, kind, summary, priority,"
                " critical, created_at, source_mode) VALUES (?,?,?,?,?,?,?,?)",
                (item_id, session_id, kind, summary, priority, 1 if critical else 0,
                 now, source_mode),
            )
            self._s._conn.commit()

    def list_inbox_v2(self, session_id: str) -> list[dict[str, Any]]:
        with self._s._lock:
            rows = self._s._conn.execute(
                "SELECT * FROM inbox_v2 WHERE session_id=? ORDER BY created_at, item_id",
                (session_id,),
            ).fetchall()
        return [dict(r) for r in rows]

    # ─────────────────────── diagrams ───────────────────────
    def create_diagram_v2(self, diagram_id: str, session_id: str, template: str,
                         source_mode: str, nodes: list[dict[str, Any]],
                         edges: list[dict[str, Any]], now: str) -> None:
        with self._s._lock:
            self._s._conn.execute(
                "INSERT INTO diagrams_v2(id, session_id, template, source_mode, nodes_json,"
                " edges_json, created_at) VALUES (?,?,?,?,?,?,?)",
                (diagram_id, session_id, template, source_mode, json.dumps(nodes),
                 json.dumps(edges), now),
            )
            self._s._conn.commit()

    def get_diagram_v2(self, diagram_id: str) -> dict[str, Any] | None:
        with self._s._lock:
            row = self._s._conn.execute(
                "SELECT * FROM diagrams_v2 WHERE id=?", (diagram_id,)
            ).fetchone()
        return dict(row) if row else None
