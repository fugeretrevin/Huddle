# RC6 performance report

2026-11-13. **No device numbers yet.** `RC6_PERFORMANCE.csv` is deliberately not created until a real capture exists.

## Why there are none

The headset's lock screen keeps the app from starting unless someone is wearing it, and the lock is not bypassed. Every device number must come from a worn session.

## How the numbers will be produced (ready, waiting)

1. `scripts/quest_perf_watch.ps1 -Label rc6_worn` has been running since about 17:30. It waits for MuseDesk to open, then records for 30 minutes:
   - the compositor's per-second VrApi line (FPS, app GPU time, CPU/GPU utilisation, stale frames, temperature), with timestamps;
   - memory (PSS) every 30 s;
   - the app's own log.
2. The app (from `rc6-1`) logs `[MuseDesk.Perf] scenario=<name>` on every state change, plus a 5 s heartbeat. The heartbeat carries main-thread CPU and GPU frame time from `FrameTimingManager` (Frame Timing Stats is now on) and managed and total memory.
3. At the end of the capture, `scripts/quest_perf_split.ps1` assigns each second to the scenario active then. It writes `Logs/quest/perf_rc6_worn.csv`, which becomes `RC6_PERFORMANCE.csv`.

The scenarios map to the required list as follows.

| Required | Marker |
|---|---|
| Desk idle | `desk_idle` |
| Desk task execution | `desk_task_running` |
| Paper grabbed / expanded | `paper_held`, `paper_expanded` |
| Task board open | `board_open` |
| Floor placement preview | `floor_preview` |
| Floor Office active | `floor_active` (and `floor_moving`) |
| Ceiling scan preview | `ceiling_scan` |
| Ceiling pod descending | `ceiling_descending` |
| Ceiling pod deployed with effects | `ceiling_deployed` |
| Sustained operation | the `all` row and the temperature trend over 30 min |

Limits:
- The heartbeat values need at least 5 s in a state.
- Paper and board states are reported only in Desk mode.
- The splitter has only been run on synthetic data.
- The heartbeat GPU time read about 0.1 ms in the Editor and must be checked against VrApi `App=` on the device before it is quoted.

## Target

Stable 72 FPS at 72 Hz, the configured rate. Read p5 FPS and stale frames, not only the average.

## If a scenario is under target

Act in this order, and only on what the numbers show. Every step except 6 is a release-config flip (`AgentScripts/BuildReleaseConfigV1.cs`) plus a rebuild, with no code change.

1. `ceilingParticles` off.
2. `ceilingPortalCone` off.
3. Scan grids: they are shown only during placement by design. If `floor_preview` or `ceiling_scan` is the slow row, set `placementScanGrids` off.
4. Reduce scan-grid density: a shader parameter (not yet needed).
5. Other overdraw.
6. A CPU-heavy script only if the heartbeat shows the main thread is the bottleneck.

Pod movement, the mount and lock animations, the halo and the document and task interactions stay in all cases.

Before and after comparisons use the same headset, the same build profile and the same walk-through (`RC6_HARDWARE_QA.md` pass 1).

## Earlier history

`FINAL_PERFORMANCE_RESULTS.md`: no app numbers have ever been captured. Older VrApi files under `Logs/quest/` are of the system compositor idling and must not be quoted.
