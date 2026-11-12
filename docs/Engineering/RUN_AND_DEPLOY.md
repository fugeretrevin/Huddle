# MuseDesk: run and deploy

Date: 2026-11-12. Windows 11, PowerShell, Unity 6000.6.8f1, Quest 3 over USB. Run every command from the repository root (`C:\dev\MuseDesk\MuseDesk`) unless it says otherwise.

## 1. What runs where

| Piece | Where | Needed for |
|---|---|---|
| Quest app `com.musedesk.quest` | Headset | Everything |
| Backend (FastAPI on `127.0.0.1:8765`) | PC | Live mode only |
| `adb reverse tcp:8765 tcp:8765` | PC to headset over USB | Live mode only |

The app tries the backend for 5 seconds at launch. If it cannot connect it runs the bundled scripted demo and says so on the status sign ("DEMO · Simulated work"). The demo needs no PC.

## 2. ADB

ADB ships with Unity. Set this once per shell:

```powershell
$adb = "C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
& $adb devices          # expect: 2G97C5ZHCK037K  device
```

## 3. Backend

First-time setup:

```powershell
cd backend
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e ".[dev]"
```

Run it. `MUSEDESK_PRODUCER` selects what drives the agents:

```powershell
cd backend

# Live: real sandboxed work (pytest runs, a real code change behind the approval gate).
$env:MUSEDESK_PRODUCER = "live"
.\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8765

# Scripted: the same event shapes from a timeline, every event labelled source_mode "demo".
$env:MUSEDESK_PRODUCER = "demo"
.\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8765
```

Check it: `curl.exe http://127.0.0.1:8765/health` returns `{"status":"ok",...}`.

Tests:

```powershell
cd backend
.\.venv\Scripts\python.exe -m pytest -p no:cacheprovider     # 110 passed on 2026-11-12
```

Things to know:

- One run per session. Starting a second task while one is in progress returns 409 `RUN_IN_PROGRESS`.
- If the backend restarts while a decision is waiting, the run is reported as failed at start-up and the decision is marked expired. Start a new task.
- Live workspaces are created under `backend/sandbox_workspaces` and the database is `backend/data/musedesk.sqlite3`. Both are safe to delete while the backend is stopped.

## 4. Unity Editor

Open the project in Unity 6000.6.8f1. The production scene is `Assets/MuseDesk/Scenes/MainMRScene.unity` (the only scene in the build list).

Drive it from the command line with the `unity` CLI:

```powershell
unity command editor status                     # status: ready
unity command editor set_auto_tick --enabled true
unity command script recompile
unity command script recompile_status           # failed: false
unity command console status                    # consoleErrors: 0
```

If `unity command editor status` cannot reach the Editor, restart the Editor; its pipeline server only starts at launch.

Play Mode without a headset stalls because XR cannot initialise. Use Scene View captures instead (section 7).

## 5. Tests on the Unity side

```powershell
# Real runtime types: reducers, demo gateways, voice matching. 54 checks on 2026-11-12.
unity command script run --file AgentScripts/RuntimeSmokeTests.cs --entry RuntimeSmokeTests.Run

# NUnit contract tests (constants and mappings).
unity command test run --mode editor
```

## 6. Build and install

```powershell
# Build from inside the open Editor (about 3 to 15 minutes). Writes Builds/MuseDesk.apk.
unity command script run --file AgentScripts/BuildApk.cs --entry BuildApk.Run --timeout 900
# expect: RESULT=Succeeded apk=Builds/MuseDesk.apk ...

& $adb -s 2G97C5ZHCK037K install -r Builds\MuseDesk.apk
& $adb -s 2G97C5ZHCK037K shell monkey -p com.musedesk.quest -c android.intent.category.LAUNCHER 1
```

The Editor does not answer other commands while it is building.

Logs:

```powershell
& $adb -s 2G97C5ZHCK037K logcat -c
& $adb -s 2G97C5ZHCK037K logcat -s Unity:V AndroidRuntime:E
```

Useful log lines: `[MuseDesk] session ready (...)`, `[MuseDesk] Workspace placed in front of user (...)`, `[MuseDesk.P2] ...` for the task board.

## 7. Scene tooling

These run against the open scene. The first group changes nothing.

```powershell
# Reports
unity command script run --file AgentScripts/SceneHealth.cs  --entry SceneHealth.Run     # missing scripts, unassigned references, button wiring
unity command script run --file AgentScripts/ButtonProbe.cs  --entry ButtonProbe.Run     # which way each poke button and label faces
unity command script run --file AgentScripts/LayoutBounds.cs --entry LayoutBounds.Run    # footprint of each workspace group

# Capture what the seated user sees
unity command script run --file AgentScripts/ViewFrom.cs --entry ViewFrom.PlusZ          # also .Board, .Right, .Stamps
unity command capture scene_view --source screen --output-path Screenshots/eye_front.png

# Re-apply the layout and button polish (both idempotent; both save the scene)
unity command script run --file AgentScripts/ArrangeForUserSide.cs    --entry ArrangeForUserSide.Apply
unity command script run --file AgentScripts/PolishBoardAndButtons.cs --entry PolishBoardAndButtons.Apply
```

Layout convention: `WorkspaceRoot` +Z points at the seated user, the user's right is root -X, and the eye is at about root-local (0, 0.30, 0.62). Text and poke buttons face the user with their local -Z.

## 8. Demonstration script

### Standalone (no PC)

1. Put the headset on, launch MuseDesk, and look straight ahead until the office appears. It places itself once the headset is worn and steady (up to 10 seconds). Taking the headset off and on, or using the system recenter, places it again.
2. The sign above the office reads "DEMO · Simulated work". The three agents start working.
3. After about 7 seconds the Researcher walks over with a paper and an exclamation mark.
4. Grab the paper (pinch or whole hand). It expands. "Details" shows the rationale; "Ask why" shows a demo-labelled answer.
5. Approve by poking Approve, or by picking up the green stamp and pressing it on the paper. Reject with the Reject button or the red stamp.
6. Approved: the Researcher carries the work to the Developer, the Developer builds, the Tester runs the tests, a token appears on the shelf, and the completion chime plays. Rejected: no token, no chime, the Developer waits.
7. Task board on your left: grab a card and drop it on an agent to assign it. Poke "+ New" for a new card. Grab a card, then poke "Pause" or "Priority".
8. "Focus" (right side): updates are held in the inbox instead of interrupting. Turn it off to read them.

### Live (PC tethered)

1. Start the backend with `MUSEDESK_PRODUCER=live` (section 3).
2. `& $adb -s 2G97C5ZHCK037K reverse tcp:8765 tcp:8765`
3. Launch the app. The sign reads the live mode label and cards read LIVE.
4. Same flow as above. The tester really runs pytest in the sandbox; approving really writes the change; the artifact panel opens with the real diff and test output.
5. To show honest failure handling: stop the backend. The sign reads "LIVE · Reconnecting to desktop…" and nothing is simulated. Start it again; the run is reported as failed and the decision as expired.

## 9. Settings that change behaviour

| Setting | Where | Current | Effect |
|---|---|---|---|
| `preferOfflineDemo` | `Assets/MuseDesk/Resources/RuntimeSettings.asset` | off | On: skip the 5 s connection attempt and go straight to the demo |
| `apiBaseUrl` / `webSocketBaseUrl` | same | `127.0.0.1:8765` | Backend address (reached through `adb reverse`) |
| `verbose` | `DiagnosticsOverlay` on `AppServices` | off | On: full developer HUD instead of the two-line status sign |
| `autoResolveIfIdle`, `loop` | `DemoDirector` on `AppServices` | off | Kiosk options for an unattended booth |
| `voiceEnabled` | `VoiceDecisionController` on `AppServices` | on | Needs a Wit.ai token and network; does nothing without them |

## 10. Final APK

`Builds/MuseDesk.apk`, package `com.musedesk.quest`, version 1.0 (code 1). This is a development-signed sideload build. For store or competition upload, follow the Meta Developer Dashboard signing guidance and bump `AndroidBundleVersionCode`.
