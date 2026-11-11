# Normative API and event contract (v1)

The `contracts/` JSON Schema files are machine-checkable. This document specifies behavior and examples. **All client/backend teams must agree to these field names before implementation.**

## Transport and authentication

- Development API base: `http://127.0.0.1:8765` via USB+`adb reverse`, otherwise restricted trusted LAN. Public/hosted API: HTTPS with WSS.
- REST JSON with `Content-Type: application/json`, UTF-8, ISO 8601 UTC times, UUIDs or collision-resistant string IDs. `schema_version` = `1`.
- REST live requests use a per-session bearer token; do not ship a durable admin token or LLM key inside APK. Offline demo gateway handles all calls internally.
- WebSocket endpoint `GET /v1/sessions/{session_id}/events?after_seq=...`. Prefer a token-bearing WS subprotocol or supported header in production; developer-only ephemeral query token is tolerable if redacted from logs. Plan the precise supported auth approach during TASK-04.
- Event `seq` is monotonically increasing *within a session*, persisted before broadcast. Client ignores previously applied `seq`. After reconnect, fetch snapshot and resume after snapshot `last_seq`.
- Idempotency keys on any mutating action, especially decisions and tasks. Duplicate same action/key returns canonical previous response. Conflicting terminal action is `409` with canonical state.

## API routes

| Method & route | Request | Result | Notes |
|---|---|---|---|
| `GET /health` | none | `{ "status":"ok", "schema_version":1 }` | read-only |
| `POST /v1/sessions` | `{ "mode":"live_local", "client":"quest", "schema_version":1 }` | session ID, temporary token, created timestamp, last seq | demo mode bypasses network |
| `GET /v1/sessions/{id}/snapshot` | auth | task list, agents, decisions, `last_seq` | reconnect truth |
| `POST /v1/sessions/{id}/tasks` | `task_template`, unique `idempotency_key` | task object | initial use `auth_demo` |
| `GET /v1/sessions/{id}/tasks` | auth | task array | debugging |
| `POST /v1/sessions/{id}/decisions/{did}/respond` | `action`, `idempotency_key`, optional `comment` | decision resolution | exactly once |
| `POST /v1/sessions/{id}/questions` | `agent_id`, `decision_id` optional, `question` | answer text, evidence refs, mode | short contextual Q&A |
| `GET /v1/sessions/{id}/artifacts/{aid}` | auth | artifact metadata + displayable text | sanitized text only, no binary repo leaks |
| `WS /v1/sessions/{id}/events` | `after_seq` | event messages | server heartbeat/ping, reconnect |

## Example 1: create a task

```json
{
  "schema_version": 1,
  "task_template": "auth_demo",
  "idempotency_key": "quest-create-7bb0bd2d"
}
```

Result conceptually includes `task_id`, `status="queued"`, and `session_id`. A parent task may have child tasks with independent `agent_id` and dependency lists.

## Example 2: decision requested event

```json
{
  "schema_version": 1,
  "event_id": "evt-000018",
  "seq": 18,
  "session_id": "ses-demo-001",
  "occurred_at": "2026-11-10T18:00:00Z",
  "type": "decision.requested",
  "task_id": "task-research-001",
  "agent_id": "researcher",
  "decision_id": "decision-auth-001",
  "payload": {
    "title": "Choose authentication approach",
    "summary": "Use OAuth instead of managing passwords?",
    "recommendation": "approve",
    "rationale": "Avoid maintaining custom password storage in the first version.",
    "alternatives": [
      {"id": "oauth", "label": "OAuth", "tradeoff": "External identity dependency"},
      {"id": "passwords", "label": "Email/password", "tradeoff": "More security maintenance"}
    ],
    "risk_level": "medium",
    "priority": "blocking",
    "allowed_actions": ["approve", "reject", "request_changes"],
    "source_mode": "live"
  }
}
```

The title/summary are human-authored or backend-generated. They are **not** authority to execute a tool. The server validates decision state and scope separately.

## Example 3: resolve a decision

```json
{
  "schema_version": 1,
  "action": "approve",
  "idempotency_key": "quest-resolve-decision-auth-001-01",
  "comment": "Proceed with OAuth; no passwords in this prototype."
}
```

Success returns `decision_id`, `state="approved"`, `resolved_at`, and `applied=true`. If the same key is replayed, `applied=false` with the canonical prior resolution. If already resolved with a different action, return HTTP 409 with canonical state.

## Example 4: task status event

```json
{
  "schema_version": 1,
  "event_id": "evt-000019",
  "seq": 19,
  "session_id": "ses-demo-001",
  "occurred_at": "2026-11-10T18:00:01Z",
  "type": "agent.status_changed",
  "task_id": "task-dev-001",
  "agent_id": "developer",
  "payload": {
    "state": "coding",
    "task_state": "running",
    "progress_message": "Implementing selected OAuth scaffold",
    "source_mode": "live"
  }
}
```

Use separate `task.completed` or `artifact.created` event to communicate artifact metadata. Do not send raw secrets, full tool logs, or arbitrary filesystem paths to the headset.

## Event payload minimums

| Event type | Required payload fields |
|---|---|
| `session.ready` | `mode`, `agents` |
| `task.created` | `task_id`, `task_state`, `agent_id` |
| `task.started` | `task_id`, `agent_id` |
| `task.progress` | `task_id`, `message` |
| `agent.status_changed` | `state`, `task_state` |
| `decision.requested` | `title`, `summary`, `rationale`, `allowed_actions`, `risk_level`, `source_mode` |
| `decision.resolved` | `state`, `action`, `resolved_at` |
| `artifact.created` | `artifact_id`, `artifact_type`, `summary`, `source_mode` |
| `task.completed` | `task_id`, `result_summary`, `source_mode` |
| `task.failed` | `task_id`, `error_code`, `user_message` |
| `system.warning` | `code`, `message` |

## Persistence (SQLite, suggested)

- `sessions(id PK, mode, created_at, last_seq, token_hash, expires_at)`
- `agents(session_id, agent_id, role, display_name, state, updated_at)`
- `tasks(id PK, session_id, agent_id, parent_task_id NULL, status, template, dependency_json, created_at, updated_at)`
- `decisions(id PK, session_id, task_id, agent_id, status, summary, recommendation, details_json, resolved_at, response_key UNIQUE NULL)`
- `events(session_id, seq, event_id UNIQUE, type, data_json, occurred_at, PRIMARY KEY(session_id, seq))`
- `artifacts(id PK, session_id, task_id, type, summary, content_json, mode, created_at)`
- `idempotency(session_id, key, method, route, result_json, PRIMARY KEY(session_id, key))`

Schema migrations can be SQL or Alembic. Use a single database transaction to record approved decision and resumed task scheduling intent before releasing any tool action.

## Client reducer and animation projection

Each Unity presentation reducer applies `event.type` to a **local snapshot** only if its `seq` is new. Then UI reacts to state. E.g., `decision.requested` shows document at researcher station; `decision.resolved` removes it; `agent.status_changed` changes pose/animation. Never send an approval by updating Unity local state directly. On reconnect, reconcile server snapshot and dispose obsolete paper objects.

## Timeouts and failure codes

- Startup connection timeout: 4–8 seconds before offering demo mode.
- Heartbeat: ~15 seconds or WebSocket standard ping; reconnect exponential 0.5, 1, 2, 4, 8 seconds (cap); do not lose submitted decision ID.
- Tool job timeout: task-specific, e.g. 90 seconds for deterministic demo code task; on timeout log failure and show safe retry or cancel.
- Suggested error codes: `INVALID_REQUEST`, `UNAUTHORIZED`, `DECISION_EXPIRED`, `DECISION_ALREADY_RESOLVED`, `WORKSPACE_DENIED`, `TOOL_TIMEOUT`, `TOOL_FAILURE`, `SERVICE_UNAVAILABLE`.

## Schema compatibility rules

- No field renaming after initial integration without updating `contracts/`, server models, Unity model, fixtures, and contract tests in same PR.
- Unknown event types should log warning and not crash Quest.
- Unknown optional payload fields should be ignored, not serialized into sensitive logs.
- Breaking changes increment `schema_version` and require explicit client version check.
- All artifacts must carry `source_mode: live|demo`; do not rely solely on UI mode label.
