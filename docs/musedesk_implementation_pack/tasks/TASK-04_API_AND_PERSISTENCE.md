# TASK-04 — FastAPI, durable events and network gateway

**Owner:** backend API agent. **Priority:** P0. **Inputs:** `docs/API_AND_EVENT_CONTRACT.md`, `contracts/*.schema.json`. **Do not edit:** Unity scene files, worker implementation.

## Goal

Provide a fully testable Python server with health, sessions, tasks, decisions, questions, artifacts, snapshots, and a real-time ordered WebSocket event channel.

## Implement

1. Create `backend/pyproject.toml`, typed `app/main.py`, `models.py`, `storage.py`, `events.py`, `auth.py`, `settings.py` and pytest harness. Use FastAPI/Pydantic/SQLite with explicit transaction boundaries.
2. Implement documented endpoints and schemas. Version `schema_version=1` and reject incompatible clients gracefully.
3. Allocate unique session IDs, short-lived auth tokens; hash stored tokens where feasible. Auth local test clients or use explicit configurable dev-only authentication.
4. Persist events before WS broadcast. Sequence monotonically by session; reconnect with snapshot + replay; resilient to consumer reconnection.
5. Decision response uses `idempotency_key`. Database constraint/transaction prevents double resolution. Emit `decision.resolved` exactly once and return canonical state for retries.
6. API supports worker events via internal service interface (callback/queue), so TASK-05 can integrate without changing endpoint field names.
7. `GET /health` must not reveal secrets. Add structured redacted logs and simple diagnostics.
8. Write integration tests for event ordering, deduplication, auth, invalid request, reconnect, conflicting approval and safe error payload.

## Deliverables

Runnable backend module with exact installation/run instructions, sample `.env`, schema validation tests, API examples and test output.

## Acceptance

- [ ] `python -m uvicorn app.main:app --host 127.0.0.1 --port 8765` starts.
- [ ] All endpoints work according to contract, no arbitrary hidden path.
- [ ] WebSocket emits event sequence that client can replay without gaps/duplicates.
- [ ] Duplicate decision response never dispatches work twice.
- [ ] Unauthorized sessions cannot read or resolve other sessions.
- [ ] Tests pass from a clean Python venv and CI-compatible command.

## First handoff to XR team

One local test command creates session and issues `decision.requested`. XR receives message and renders paper. Freeze this early before the full orchestrator exists.
