# RC6 performance report

2026-11-13. **First real device numbers.** Build `rc6-1`, worn by the owner, Quest 3 at 72 Hz.

## Result: no optimisation needed

One app session: process 9561, 16:33:33–16:38:56, 319 seconds. The device log holds it from launch. The recorder script was not running; the lines were read out of the device log afterwards with the same commands. Only the app's own seconds are counted; VrApi also logs the home shell. Data: `RC6_PERFORMANCE.csv`.

| Scenario | Seconds | FPS avg | FPS p5 | App GPU ms avg / p95 | CPU main ms avg / p95 |
|---|---|---|---|---|---|
| Desk idle (includes the launch second) | 30 | 69.9 | 65 | 3.77 / 4.82 | 6.63 / 11.47 |
| Desk task running | 8 | 72.3 | 70 | 2.84 / 3.19 | 5.71 |
| Board open | 3 | 72.7 | 72 | 3.78 / 4.67 | (under 5 s) |
| Floor preview (grid on) | 61 | 72.1 | 70 | 3.11 / 4.66 | 5.51 / 6.05 |
| Floor office active | 12 | 72.6 | 72 | 3.12 / 3.65 | 5.60 |
| Ceiling scan (grid on) | 33 | 72.3 | 72 | 3.31 / 4.10 | 5.65 / 5.99 |
| Ceiling descending | 3 | 72.7 | 72 | 3.64 / 4.73 | 5.92 |
| **Ceiling deployed, all effects** | 96 | 72.2 | 69 | **4.73 / 5.97** | 6.39 / 6.99 |
| Whole session | 319 | 72.1 | 71 | 3.86 / 5.86 (max 6.15) | 6.02 / 6.87 |

- **Frame budget at 72 Hz is 13.9 ms.** Worst app GPU time is 6.15 ms and worst main-thread average is 7 ms, so there is more than 2× headroom. Particles and the portal cone stay on.
- **Below 70 FPS: 12 of 319 seconds.** These are mostly the launch second and short dips at mode changes (ceiling scan minimum 63, deployed minimum 64). 146 stale frames in total, most in desk idle at launch.
- **Memory:** Unity total allocated at most 391 MB. Device free memory was about 3.5 GB at the end of the session.
- **Temperature:** rose from 31 °C to 38 °C over 5 minutes. A longer soak is still needed for thermal behaviour.
- **Exceptions:** none. There is one benign startup line: Unity probing for Play Asset Delivery (`AssetPackManager` not found).
- **Not captured:**
  - paper held or expanded (no marker seconds in this session);
  - a 15-minute sustained run;
  - Unity-side GPU time (`FrameTimingManager` reported none on device; VrApi `App=` is used instead).

## How the numbers were produced

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
