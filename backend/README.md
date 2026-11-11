# MuseDesk backend (TASK-04)

Typed FastAPI service: health, sessions, tasks, decisions, questions, artifacts,
snapshot, and an ordered WebSocket event channel backed by durable SQLite.
Field names are frozen against `../contracts/*.schema.json`.

## Status

- ✅ REST + WebSocket implemented and covered by `pytest` (10 tests pass).
- ✅ Per-session bearer auth; cross-session access rejected (401).
- ✅ Durable events, monotonic per-session `seq`, snapshot + gapless replay.
- ✅ Idempotent decision responses; conflicting second action → `409` canonical state.
- ✅ Deterministic `auth_demo` producer drives the full P0 timeline (the TASK-04
  → XR handoff). It runs no real tools, so it honestly stamps `source_mode="demo"`.
- ⏳ TASK-05 will replace the scripted producer with real sandboxed workers
  (`source_mode="live"` only for actually executed work) **without changing any
  endpoint field names**.

## Run

```powershell
cd backend
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e ".[dev]"
Copy-Item .env.example .env   # optional; defaults are fine for USB dev
.\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8765 --reload
```

Then from the PC: `curl.exe http://127.0.0.1:8765/health` → `{"status":"ok","schema_version":1,...}`.

For a USB-tethered Quest: `adb reverse tcp:8765 tcp:8765`, then point the client
at `http://127.0.0.1:8765` / `ws://127.0.0.1:8765`.

## Test

```powershell
cd backend
.\.venv\Scripts\python.exe -m pytest -q
```

## Endpoints (v1)

| Method | Route | Auth |
|---|---|---|
| GET | `/health` | no |
| POST | `/v1/sessions` | no |
| GET | `/v1/sessions/{id}/snapshot` | bearer |
| POST | `/v1/sessions/{id}/tasks` | bearer |
| GET | `/v1/sessions/{id}/tasks` | bearer |
| POST | `/v1/sessions/{id}/decisions/{did}/respond` | bearer |
| POST | `/v1/sessions/{id}/questions` | bearer |
| GET | `/v1/sessions/{id}/artifacts/{aid}` | bearer |
| WS | `/v1/sessions/{id}/events?after_seq=N&token=...` | token |

## Module map

- `app/settings.py` — typed env config (`MUSEDESK_*`), no secrets in source.
- `app/models.py` — frozen Pydantic models + enums.
- `app/storage.py` — SQLite; events are the source of truth (persist-before-broadcast).
- `app/events.py` — per-session ordered fan-out bus + replay.
- `app/auth.py` — per-session bearer tokens, stored hashed.
- `app/orchestrator.py` — deterministic `auth_demo` producer + real approval gate.
- `app/main.py` — FastAPI app factory, routes, WebSocket.
