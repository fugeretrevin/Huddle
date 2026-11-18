# Handoff for the next session

## UPDATE 2026-11-18 (demo day): HOW TO START UP (read this first; deadline 2026-11-18 12:00 PT)

**State:** the Quest runs **rc25** (`Builds/MuseDesk_rc25_release.apk`, `77B7B50A…D63C`, versionCode 9, release-signed).
HEAD `0e88c79` on `feature/rc8-feedback-1` (local only, not pushed). The owner started recording the demo on 2026-11-18 morning;
the backend was stopped afterwards on their request. Next build would be rc26, versionCode 10 (`BuildApkTo.cs` protects rc6–rc25).

**Start the live backend (Meta model, real code in the restricted workspace, Screen panel + app windows ON):**
```powershell
# from the repo root; its OWN window (Claude Code may kill its own background shells under memory pressure)
$root=(Get-Location).Path; $log="$root\Logs\backend_live_demo.log"
Start-Process powershell -ArgumentList "-NoExit","-ExecutionPolicy","Bypass","-Command",
  "`$host.UI.RawUI.WindowTitle='MuseDesk backend (LIVE)'; & '$root\scripts\start_wall_backend.ps1' *>&1 | Tee-Object -FilePath '$log'"
```
- Expect in the log: "Screen panel: ON", "App windows: ON", "Starting LIVE with the Meta model API", "Application startup complete".
  The red `NativeCommandError` line is just uvicorn writing INFO to stderr (harmless).
- Check: `Invoke-WebRequest -UseBasicParsing http://127.0.0.1:8765/health` → 200.
- Flags: `-NoScreen` (no screen panel, no app windows), `-TextOnly` (model text only, no code run), `-Demo` (scripted demo).
- The owner can also just run `powershell -ExecutionPolicy Bypass -File scripts\start_wall_backend.ps1` in their own terminal.

**Connect the Quest (USB):**
```powershell
$adb="C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
& $adb -s 2G97C5ZHCK037K reverse tcp:8765 tcp:8765     # -s is needed: a stale "emulator-5562 offline" is also listed
& $adb -s 2G97C5ZHCK037K shell dumpsys package com.musedesk.quest | Select-String versionCode   # 9 = rc25
```
- Then **restart MuseDesk on the Quest** if it was open while the backend was down (it drops to the offline SCRIPTED DEMO
  and only goes live on a fresh launch).
- Before filming: the Screen panel mirrors this PC's main screen. Tell the owner to hide private windows.

**Stop the backend:** close the "MuseDesk backend (LIVE)" window, or find the listener and stop its tree:
`Get-NetTCPConnection -LocalPort 8765 -State Listen` → OwningProcess (python) → its parent python (venv launcher) →
its parent powershell. Also stop any `python … app_window_host …` process (an open app window). Confirm /health fails.

**rc25 app windows (owner asked how to use them):** in the headset press **Screen** (dock) → **Open app** (panel's lower
left) → the PC opens the newest program the agents built (`build_ui(ui)` window, else a `main(argv)` run panel) and the
panel shows only that window → fingertip taps reach it (press in, pull back ~2.5 cm before the next tap) → **Close app**
(same key) or the red Close app button in the window. Picks the session's newest work, else the newest on the PC
(last one: tic-tac-toe, `backend\sandbox_workspaces\plans\mp-0781687d3f`). Real fingertip taps are NOT yet owner-confirmed.

**Offline Play drives / preflight in the Editor need the backend OFF** (otherwise the Editor goes live mid-run).

## UPDATE 2026-11-17 night (read this first; deadline 2026-11-18 12:00 PT)

- **On the Quest:** the newest rcNN-release in `Builds/` (see the bottom of the newest `RC2x_QA.md`; rc23 = 28DCF3ED,
  versionCode 7; rc24 if built). All release-signed (CN=MuseDesk), installed with `adb install -r`, hash-verified on device.
  Next build: next rc number and versionCode +1; `AgentScripts/BuildApkTo.cs` protects every rc path already built.
- **Owner checklists (what changed, line by line):** `RC20_QA.md`, `RC21_QA.md`, `RC22_QA.md` (19 items), plus rc23 polish
  (board arrows at the column foot, small talk, arm clearance, one-sided monitors on the desk AND in the Wall habitat,
  clutter removed) and the change-request fix below. WORN: only what the owner said (transcribed in those files).
- **Live (Meta model, PC backend):** `live_real_work.py` 13/13 on a new goal (to-do module; change "also let the user
  delete a task by its number" now recognised as a change request: `side_intent.py` gained feature-request verbs);
  `live_adhoc_task.py` 5/5 (a task the user adds and assigns is worked for real). Backend 419 passed, 1 skipped.
- **Start the live backend for the owner:** `Start-Process powershell` running `scripts\start_wall_backend.ps1` in its OWN
  window (Claude Code kills its own background shells under memory pressure), then `adb reverse tcp:8765 tcp:8765`.
  Offline Play drives need it OFF.
- **Editor gotcha (new):** the Play clock sometimes freezes (frames advance, `Time.unscaledTime` stuck, ClockProbe dt=0).
  Stop + start Play once; if it is stuck at frame 1, the owner restarts Unity (Claude may not quit the Editor).
- **Probe gotcha:** `scripts\drv.ps1` cannot pass arguments with spaces or `|`; use no-arg probe methods. Each
  `script run` is a fresh assembly, so probe statics do not survive between calls.
- **Agents used this session (all report AUTO only):** they may not enter Play Mode or build; the lead verifies in Play
  (one at a time), runs `scripts\preflight.ps1 -Only <group>` group by group in the FOREGROUND, then builds.
- **Antigravity soft textures:** rejected by the owner (kept rc20 matte); backup in `Logs/antigravity_soft_backup/`.
- **Not done / known:** clothing clipping on agents needs a Blender re-weight; the desk drop has no MRUK desk raycast; the
  Manager's own monitor (inside a combined Wall mesh) is still double-sided.
- **Next:** owner wears the newest build -> transcribe -> fix P0/P1 only -> film (`FINAL_VIDEO_SHOT_LEDGER.md`) ->
  submission by the owner (`FINAL_SUBMISSION_CHECKLIST.md`). Never push/merge/submit without the owner.

## UPDATE 2026-11-17 day

- **The owner wore rc18** and gave six notes (verbatim in `RC18_MORNING_QA.md`; no per-line PASS/FAIL, so nothing marked WORN).
- **rc19 INSTALLED:** `Builds/MuseDesk_rc19_release.apk`, label rc19-release, release-signed, versionCode 3,
  `F197F662…5EFD57`, hash-verified on the Quest (installed with `-r`, settings kept). Protected in `BuildApkTo.cs`; the next
  build is rc20 with versionCode 4. Owner check: **`RC19_QA.md`** (one line per note).
  - plan paper: a tick key per work item (no Keep/Skip keys); tubes on the back wall, fainter; results / problem reports /
    cross-floor hand-offs ride the tubes, the summary flies straight from the printer (elevator only for questions);
    portal 1.2x, platform lower; less gloss (27 materials x0.82 smoothness, sky reflections 0.55, key light 0.85).
- **Live real build on a NEW objective (expense tracker): 13/13** (`RC19_QA.md`). It exposed a gap: a change step rewrote a
  test file and the folder went 51 -> 45 tests silently. **Fixed in the backend (no APK needed):** a step that leaves fewer
  tests is asked once to restore them, else its card says "N fewer tests than before (a -> b)". Backend 375 passed.
- The live script takes any objective: `MUSEDESK_REAL_OBJECTIVE`, `MUSEDESK_REAL_CHANGE`.
- Repo note: `git commit` printed "bad object refs/stash" (background repack failed). Commits are fine; not touched.

## UPDATE 2026-11-17 overnight (read this first; the older text below is the rc17 state)

- **Release key: DONE.** The owner ran `scripts\make_release_keystore.ps1` (a NEW key; there was never an old password).
  `%USERPROFILE%\.musedesk\` holds the keystore + `signing.json` (never print, copy or commit them).
- **Quest:** now runs a RELEASE-signed build (the rc17 code, `Builds/MuseDesk_signtest_rc17code.apk`, 38AE41EE…, verified).
  So rc18 and later install with `adb install -r` (no uninstall, no reset).
- **rc17 worn by the owner (2026-11-17):** "all the core things seem like they work" + 18 polish notes (ledger in
  `RC17_FINAL_HARDWARE_QA.md`). All 18 addressed overnight in rc18; the owner's morning checklist is
  **`RC18_MORNING_QA.md`** (what changed, line by line, and what to check).
- **rc18 INSTALLED:** `Builds/MuseDesk_rc18_release.apk` (label rc18-release, release-signed, versionCode 2,
  FEEFEFC1…A3D421, hash-verified on the Quest). rc18 is protected in `BuildApkTo.cs`; the next build is rc19 with versionCode 3.
- **Not done on purpose:** full-screen colour grading (passthrough alpha risk, could not be checked on the headset overnight).
- Next: owner wears rc18 (`RC18_MORNING_QA.md`), then filming and submission as below (steps 5 and 6).

---

(Written 2026-11-16 ~23:45, before the owner restarted the PC.)

**Deadline:** 2026-11-18 12:00 PT (Meta VR Start Developer Competition 2026, Productivity track).
**Repo:** https://github.com/fugeretrevin/MuseDesk, branch `feature/rc8-feedback-1`.
**HEAD:** `1d28a9d` plus this handoff commit. The handoff commit is local only; push when the owner agrees. `main` is not merged.

## Where we are

- **On the Quest:** rc17 `Builds/MuseDesk_rc17_production.apk`, SHA-256
  `BD0204F4556E6A301379A09F0ED986B9797273A989FC71AA222EEA0A17B602AD`, installed and hash-verified.
- **Rollbacks:** rc16 `D25583D1`, rc15 `047DD408`, rc14 `3CD37ED9`, rc13 `3DAF3AE6`.
- **Automated (all green):**
  - `scripts\preflight.ps1` 13/13 (ledger in `Logs/preflight/`, which is git-ignored);
  - EditMode 1636/1636;
  - backend pytest 371 passed / 1 skipped.
- **Live (Meta model):** `backend/scripts/live_real_work.py` 13/13 (plan `mp-5f997c5bbd`; Apply change made the password
  minimum 12; 32 passed after 2 real fixes; independent pytest re-runs matched). Also `live_e2e` 71/71 and `live_journey`
  17/17. Details: `docs/Engineering/FINAL_LIVE_DEMO_EVIDENCE.md`.
- **WORN:** nothing on rc16/rc17 yet.
  - rc15 worn PASS: rugs, ceiling shelf hand-over, Select beam, portal, visitor head/body; 72.2 fps.
  - rc15 worn FAIL: voice (fixed in rc16, not yet worn).
- **Not in the rc17 APK:** P-1, the receipt-flyer fix (`WallHabitatController.Manager.cs`). It goes into rc18.

## Blockers and next steps (in order)

1. **P0, owner: release signing.** rc17 is DEBUG-signed, versionCode 1, and a Meta release channel refuses it.
   - The owner runs this in their own PowerShell window: `powershell -ExecutionPolicy Bypass -File scripts\make_release_keystore.ps1`.
   - It writes `%USERPROFILE%\.musedesk\musedesk-release.keystore` and `signing.json`. Back them up and never commit them.
2. **Owner wears rc17:** `docs/Engineering/RC17_FINAL_HARDWARE_QA.md` (B1–B8, D performance, E live from the Quest).
   - Start `scripts\quest_perf_watch.ps1 -Label rc17_worn` first.
   - Transcribe the owner's PASS/FAIL into its results ledger. Never mark WORN without the owner's words.
3. Triage P0/P1 only, fix, run `scripts\preflight.ps1` (backend OFF), get 13/13.
4. **Build rc18 (release-signed):**
   `unity command script run --file AgentScripts/BuildApkTo.cs --entry BuildApkTo.RunSigned --args '[\"Builds/MuseDesk_rc18_release.apk\", 2]' --timeout 2400`
   - **Uninstall first:** the signature changes, so `install -r` fails. That resets the saved panel layout and mic preference.
   - Verify the hash on the device. The owner then smokes B1 + B2 + leaving Wall mid-receipt.
5. **Film:**
   - order and sources: `FINAL_VIDEO_SHOT_LEDGER.md`;
   - procedure: `FINAL_RECORDING_PREFLIGHT.md`;
   - tools: `FINAL_CAPTURE_INSTRUCTIONS.md`.
   Offline takes first, then the live takes, then the PC cutaway.
6. **Submission (owner only):** `FINAL_SUBMISSION_CHECKLIST.md`. It has the capability wording and the judge instructions.
   Claude never submits or publishes.

## After the restart (environment)

- **Unity Editor:** reopen the project; check `unity status`. The PID changes; the old one was 30296.
  - If drivers time out, run `AgentScripts/ClockProbe.cs`. A stalled Play clock means restarting Unity.
  - **Never run EditMode tests while Play Mode is on** (deadlock). If stuck, focus Unity and press Ctrl+P.
- **Backend (live):** `scripts\start_wall_backend.ps1` (prints "Starting LIVE with the Meta model API" and the real-work line),
  then `adb reverse tcp:8765 tcp:8765`.
  - Claude Code stopped it twice for low memory (0.5 GB free with Unity open). Prefer running it in the owner's own terminal.
- **Offline Play drives need the backend OFF.** Otherwise the app goes live mid-run and the drives fail.
- **Quest:** adb serial `2G97C5ZHCK037K`, package `com.musedesk.quest`. adb is at
  `C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`.
- **Check the device before a take:** `scripts\record_preflight.ps1`. `-Restart` gives a clean take, `-Pull` copies the clips.

## Key tools

| Tool | Use |
|---|---|
| `scripts\compile.ps1`, `scripts\tests.ps1 -Timeout 900` | compile; EditMode |
| `scripts\preflight.ps1 [-Only group]` | every automated group + ledger (compile, editmode, backend, drives, spatial, portal, amend, desk) |
| `scripts\regress.ps1`, `scripts\amend_check.ps1 [-Live]` | Play drive sweep; Apply-change check |
| `scripts\drv.ps1 Class Method Arg` | one Play driver call |
| `AgentScripts/CineCam.cs` | capture-only in-engine camera (Shot / Dolly / Off; 7 presets) |
| `AgentScripts/LogTally.cs` | real console errors vs Editor-only Meta SDK noise |
| `backend\scripts\live_real_work.py`, `live_e2e.py`, `live_journey.py` | live suites (backend running) |

## Standing rules (owner)

- **Never** print, copy or move `ProjectSettings/wit.config`, `Assets/MuseDesk/Voice/WitConfig_MuseDesk.asset`,
  `backend/.env`, or the release keystore / `signing.json`.
- **Never** mark WORN without an owner report.
- **Never** submit, publish, push or merge without the owner.
- **Protect the rollback APKs.** The builder refuses existing files and the paths of rc6–rc17.
- **Git:** no destructive git; never `git commit -a`. Leave the pre-existing dirty files uncommitted: the Inter SDF fonts,
  `AgentTalk_Paper.mat`, the URP asset, `generate_workstations.py`, the `.blend1` files, `docs/0*_`, `README_START_HERE.md`,
  `AgentScripts/_GuardedRun.cs`.
- Never edit `Assets/` scripts during Play Mode or an APK build.
- **Owner decisions:** keep Floor, Ceiling and the large portal ON; hands-free voice needs a name first; the Wall Manager uses
  its dedicated model; the office never moves at start-up; judges get the labelled offline demo.
- **Honesty:**
  - SCRIPTED DEMO and LIVE labels are always true.
  - Research is model text only (no web).
  - The code runner is a "restricted workspace", not a sandbox.
  - Never show live work as instant.

## Other docs

- **Full project overview for an expert:** `PROJECT_STATE_FOR_EXPERT_REVIEW.md` is rc15-era. The latest state is in this file
  plus the FINAL_* docs.
- **Fix history:** `END_TO_END_GAPS_AND_FIXES.md` (F-1…F-18), `FINAL_POLISH_FIX_LOG.md`.
- **Go/no-go:** `FINAL_RELEASE_GO_NO_GO.md`. GO for filming on rc17; submission = signed rc18.
