# Handoff for the next session (written 2026-11-16 ~23:45, before the owner restarted the PC)

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
