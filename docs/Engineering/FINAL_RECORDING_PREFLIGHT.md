# Final recording preflight

The procedure before a filming session and between takes. Shots: `FINAL_VIDEO_SHOT_LEDGER.md`. Tools:
`FINAL_CAPTURE_INSTRUCTIONS.md`.

## 1. Engineering gate (once, PC, about 40 min, no headset)

`scripts\preflight.ps1`
- **Groups:** compile, EditMode, backend pytest, the Play Mode drives (Meet, Select-grab, Dock, the Manager stamp journey,
  the scripted-failure journey), Floor → Desk → Ceiling (pod down/up) → Desk, portal open/touch/ask/send back, Apply change,
  desk walk clearance, desk paper hold.
- **Ledger:** `Logs\preflight\<commit>_<time>.md`.
- **Run it with the backend OFF.** The offline drives fail if the app goes live mid-run.
- **Results:**
  - "BLOCKED (Editor clock)" means Unity's Play Mode clock stalled (a known Editor problem): restart Unity and run again. It is
    not a product failure.
  - Meta SDK Editor-only messages (no headset over Link) are counted apart from real errors.

## 2. Before the headset session (2 minutes)

| Step | Command | Expect |
|---|---|---|
| Device + build | `scripts\record_preflight.ps1` | `Installed build OK` (BD0204F4…), battery ≥ 60%, ≥ 5 GB free |
| Capture size (optional) | `scripts\record_preflight.ps1 -CaptureProps` | `1920x1080`; then record a 10 s test clip and check it |
| Frame-rate log | `scripts\quest_perf_watch.ps1 -Label rc17_film` (separate window) | "MuseDesk running … recording" once the app opens |
| Free PC memory | close Unity Play Mode, browsers, Discord | `PC free RAM OK` (≥ 2 GB). At 0.5 GB the system stopped the backend once |

## 3. Offline takes (shots 2, 5, 6, 7, 9, 10)

- Backend OFF. Every label in frame reads **SCRIPTED DEMO**.
- **Between takes:** `scripts\record_preflight.ps1 -Restart` force-stops and relaunches MuseDesk. That gives a fresh scripted
  demo: agents at home, no papers, no visitors, the office at the desk. Put the headset back on and wait until the office is
  idle (about 5 s).
- **Inside a take:** the receipt's **Run again** resets the desk demo without a restart. **Send back / Return** clears a
  visitor. **Desk** brings the office home from Floor or Ceiling.
- **Nothing is automated inside a take.** Every Start, stamp, pinch, Apply and Open here is pressed by you.

## 4. Live takes (shots 3, 4, 6-live, 8)

1. `scripts\start_wall_backend.ps1`. It must print the LIVE lines.
2. `adb reverse tcp:8765 tcp:8765`.
3. `scripts\record_preflight.ps1`. Expect `Backend OK` and `adb reverse OK`.
4. Restart MuseDesk (`-Restart`). The dock reads **LIVE**.
5. Record the whole run without stopping (6–7 min). Note the plan id on the PC:
   `backend\sandbox_workspaces\plans\` newest folder.
6. Afterwards: the PC cutaway (capture instructions §C). The same plan's evidence must go into `FINAL_LIVE_DEMO_EVIDENCE.md`.
   Claude can write it from the run's folder and the backend status.

## 5. In-engine shots (shot 5 and close-ups)

`AgentScripts/CineCam.cs` in Editor Play Mode (instructions §B). The run it films must be real: `LivingOfficeDrive RunOffline`
(SCRIPTED DEMO) or `RunLive` (backend on).

## 6. After the session

1. `scripts\record_preflight.ps1 -Pull` copies the clips to `Captures\quest\<date>`.
2. Claude reads `Logs\quest\perf_summary_rc17_film.txt` and `perf_rc17_film.csv` into `RC17_FINAL_HARDWARE_QA.md` §D.
3. Fill in the results ledger in `RC17_FINAL_HARDWARE_QA.md`; Claude triages P0/P1/P2.
