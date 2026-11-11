# MuseDesk

Meta Quest 3 mixed-reality workspace for supervising real AI agents. Three miniature
AI coworkers (Researcher, Developer, Tester) sit on your real desk in passthrough, do
actual sandboxed work, and physically hand you graspable "decision papers" when they
need a human approval. Target: **Meta VR Start Developer Competition 2026** (Productivity /
Best Agentic Interaction), deadline **Nov 18 2026, noon Pacific**.

> Full spec/plan lives in [`docs/musedesk_implementation_pack/`](docs/musedesk_implementation_pack).
> Agent rules and invariants: [`docs/AGENTS.md`](docs/AGENTS.md).

## Repository layout

```
backend/        Python FastAPI companion (TASK-04): REST + WebSocket, SQLite, approval gate
contracts/      Frozen JSON schemas shared by backend + Unity client
Assets/MuseDesk/ Unity client: scene, scripts, settings (TASK-01 foundation)
AgentScripts/   Headless Editor builders (scene scaffold, Meta blocks, project setup)
docs/           Engineering specs, setup guide, task tickets
samples/        Disposable sandbox sample repo for live workers (TASK-05, TBD)
```

## Toolchain (pinned)

- Unity **6000.6.8f1**, Meta XR All-in-One SDK **209.0.0**, URP 17.6, OpenXR 1.18.
- Python **3.12** for the backend.
- Build: IL2CPP + ARM64, package id `com.musedesk.quest` (placeholder — change before submission).

## Run the backend

```powershell
cd backend
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e ".[dev]"
.\.venv\Scripts\python.exe -m pytest -q          # 10 tests
.\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8765 --reload
```

For a USB-tethered Quest: `adb reverse tcp:8765 tcp:8765`.

## Build the Quest app

Open the project in Unity, scene `Assets/MuseDesk/Scenes/MainMRScene.unity`, platform
Android/Meta Quest, then **Build and Run** to a connected Quest 3. On-device, complete
**Settings → Environment Setup → Space Setup** so MRUK can detect your desk (there is a
manual hand-placed fallback if scene data is denied).

## Status

| Area | State |
|---|---|
| Backend (TASK-04) | ✅ Implemented, 10/10 tests passing, live smoke-tested |
| Contracts | ✅ Frozen at `contracts/` |
| Unity XR foundation (TASK-01) | ✅ Scene builds; one rig + passthrough + hands(poke/grab) + MRUK + placement + diagnostics. On-device validation pending |
| Live workers (TASK-05) | ⏳ Not started (backend producer is deterministic `demo`) |
| Decision papers UX (TASK-02), characters (TASK-03), integration/offline (TASK-07) | ⏳ Not started |

See `backend/README.md` for API details.
