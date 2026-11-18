# Huddle

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

## Modes

- **Offline demo** (default, `MUSEDESK_PRODUCER=demo` / client `preferOfflineDemo=true`): a
  deterministic scripted producer. Honestly stamped `source_mode="demo"`; runs with no PC.
- **Live** (`MUSEDESK_PRODUCER=live`): real sandboxed workers write real files and run real
  pytest behind the human approval gate; executed artifacts stamped `source_mode="live"`.

## Status

| Area | State |
|---|---|
| Backend REST+WS, persistence, auth, idempotency (TASK-04) | ✅ Done, tested |
| Contracts | ✅ Frozen at `contracts/` |
| Unity XR foundation (TASK-01) | ✅ Builds + installs on Quest; one rig + passthrough + hands(poke/grab) + MRUK + placement + diagnostics |
| Client↔backend integration backbone (TASK-07 core) | ✅ Gateways (live+demo), reducer, WS dispatcher, session controller; compiles clean |
| Live sandboxed workers + approval gate (TASK-05) | ✅ Done, 15/15 tests; real files + real pytest on approve, nothing on reject |
| Decision-paper UX (TASK-02), characters (TASK-03) | ⏳ Next |
| On-device validation of hands/placement/FPS | ⏳ Needs headset |

**Backend: 15/15 pytest passing.** See `backend/README.md` for API details.
