# Final spatial performance

2026-11-13.

## There are no measurements

No frame rate, frame time or memory figure for MuseDesk on the Quest 3 exists for any build. Four unattended launch attempts (2026-11-12 twice, 2026-11-13 twice) all stopped at the headset lock screen, which is not bypassed. The VrApi lines saved from those attempts describe the system compositor idling, not the app, and are not quoted here. A passive recorder (`scripts/quest_perf_watch.ps1`) has been waiting for the headset to be worn since 2026-11-13 00:20; nothing has been captured.

**Nothing in this file is a result until the table below is filled from a real capture.**

## Capture plan (about 12 minutes, wearing the headset)

On the PC, one capture per scenario so the numbers can be compared:

```
powershell -ExecutionPolicy Bypass -File scripts\quest_perf_watch.ps1 -Label desk     -CaptureMinutes 4
powershell -ExecutionPolicy Bypass -File scripts\quest_perf_watch.ps1 -Label floor    -CaptureMinutes 4
powershell -ExecutionPolicy Bypass -File scripts\quest_perf_watch.ps1 -Label ceiling  -CaptureMinutes 4
```

Start one, open MuseDesk, do the scenario, then close the app before starting the next (the recorder stops when the app exits).

| Label | What to do while it records |
|---|---|
| `desk` | Idle 30 s. Start a run. Take the paper, hold and move it for 20 s. Approve. Watch the completion. Open the board, grab a card, move it around, drop it. Open the viewer. Leave it idle to the end |
| `floor` | Switch to Floor and confirm. Start a run. Watch the courier bring the paper. Take it, decide. Watch completion from the seat. Leave idle to the end |
| `ceiling` | Switch to Ceiling and confirm. Lower the pod, raise it, lower it again. Start a run with it docked. Take the couriered paper, decide. Leave idle to the end |

Each capture writes `Logs/quest/perf_summary_<label>.txt` (average, minimum, 5th and 95th percentile) and the raw per-second lines in `Logs/quest/vrapi_<label>.txt`.

## Results (to be filled from the captures)

| Scenario | Target Hz | FPS avg | FPS p5 | App GPU ms avg | App GPU ms p95 | GPU util avg | CPU util avg | Stale frames | Memory PSS MB |
|---|---|---|---|---|---|---|---|---|---|
| Desk | | | | | | | | | |
| Floor | | | | | | | | | |
| Ceiling | | | | | | | | | |

Moments to pick out of the raw per-second lines by time: paper grab, courier flight, pod descent, board expansion.

What the recorder cannot give: draw calls and CPU frame time broken down by system. Those need a development build with the Unity profiler attached, which has not been made. If the table shows a problem, that is the next step.

## What changed that affects cost (reasoning, not measurement)

Added by the spatial work:
- Models: floor platform (11.6k triangles), suspended pod, ceiling mount, courier, courier station, beacon, perch, mailbox, two ghost previews. Only the ones for the active mode are shown; in Desk mode none are.
- The polished factory adds about 2.9k triangles over the old one; three nameplate plates and the mode plaque add about 2k.
- Ghost previews use Antigravity's transparent material, only while previewing.
- Room geometry is read on room events, never per frame. One ceiling evaluation took about 0.14 ms on the PC; not measured on Quest.
- The courier, beacon and pod animators run only while shown.

Removed or reduced earlier: 20 decor props, the always-open board, billboarded status panels, per-frame key colour updates.

Total active renderers and triangles in each mode have not been counted on device.

## Deferred

Foveated rendering and any tuning, until there is a number to tune against.
