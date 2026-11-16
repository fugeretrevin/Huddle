# RC8 → RC10 performance results (Quest 3)

Competition rule: at least **60 fps on Quest hardware**. The app runs at 72 Hz (13.9 ms budget; 60 fps = 16.7 ms).
Every number below is from the device. Editor numbers are never used here.

## How the numbers are captured

- `scripts\quest_perf_watch.ps1 -Label <name>`: passive. It waits until MuseDesk is running on the headset, then records
  the compositor's per-second `VrApi` lines and the app's `[MuseDesk.Perf]` lines.
- `scripts\quest_perf_split.ps1 -Label <name>`: splits that capture by scenario into `Logs\quest\perf_<name>.csv`. Columns:
  fps avg/min/p5, app GPU ms avg/p95, main-thread CPU ms avg/p95, memory, stale frames, thermal note.
- The app's own marker (`PerfScenarioMarker`) logs the scenario it is in, plus a heartbeat every 5 s: CPU main-thread ms,
  fps, memory. `gpu=0.00` means the platform gave Unity no GPU time; use the VrApi `App=` column for GPU.
- **rc10 polish (commit `f6a2927`)** adds `wall_preview`, `wall_active`, `wall_visit` and `wall_portal`. Before this, a Wall
  session was logged under the desk scenario names.

## Measured so far

| Build | Scenario | Source | fps | CPU main ms (avg / worst 5 s window) | Memory | Level | Result |
|---|---|---|---|---|---|---|---|
| rc6_2 | desk, floor, ceiling | `RC6_PERFORMANCE_REPORT.md`, `Logs\quest\perf_rc6_worn.csv` | see report | see report | see report | WORN | PASS (rc6) |
| rc9 | desk_idle | owner session 2026-11-16, app heartbeat | 72.0 (steady) | 6.0-7.3 / ~10 | 384-396 MB | WORN | PASS |
| rc9 | board_open | same | 72.0 | 5.6-7.3 / ~10 | 396-397 MB | WORN | PASS |
| rc9 | desk_task_running | same | 72.0 | 6.7-7.0 / ~11 | 389-391 MB | WORN | PASS |
| rc9 | paper_held | same | 72.0 (69.4 in the window the paper was taken) | 7.5-7.8 / 150 at the grab | 390 MB | WORN | PASS (one hitch at the grab) |
| rc9 | **Wall habitat placed, Full layout** (logged as `desk_task_running`, after `[MuseDesk.Wall] Habitat placed ... layout=Full`) | same | **72.0 for 13 heartbeats (~65 s)** | 5.7-7.0 / ~11 | 389-391 MB | WORN | PASS |
| rc9 | app launch that restored the Wall | same | 35.1 for one 5 s window | 15.6 / 1372 (load) | 383 MB | WORN | start-up hitch only. rc10 no longer restores at launch (Desk-first). |

Hitches: single frames of 60-150 ms when a scenario changes (board opened, paper grabbed). Each costs at most one
heartbeat window (~1-2 fps lost) and none persists.

## Not measured yet (rc10)

| Scenario | Why it matters | Status |
|---|---|---|
| Wall with Antigravity's forest/treehouse art (rc10) | new art since the rc9 reading above | NOT RUN |
| Meet / large portal (`wall_portal`) | handoff: disable the portal if it is over budget | NOT RUN |
| Floor courier, Ceiling descent (rc10) | measured in rc6 only | NOT RUN |
| Manager plan + printing + elevator | busiest Wall moment | NOT RUN |
| 15-minute soak, thermal | leaks, stalls, heat | NOT RUN |

**Next action (owner):** run `powershell scripts\quest_perf_watch.ps1 -Label rc10_worn` before putting the headset on,
then use Desk → Wall (Manager run, Meet) → Floor → Ceiling. Afterwards run
`powershell scripts\quest_perf_split.ps1 -Label rc10_worn` and paste the table here.

If the portal is under 60 fps, set `largeAgentPortal` to false and rebuild (`RC8_FINAL_FLAGS_AND_ROLLBACK.md`). If the Wall
is slow, the elevator VFX flag (`wallElevatorVfx`) is the cheapest switch.
