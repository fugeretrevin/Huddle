# Final performance results

Updated 2026-11-13.

## There are still no device performance numbers

Three unattended launch attempts (2026-11-12 evening, 2026-11-12 night, 2026-11-13) all ended the same way: the app process never started because the headset lock screen holds the display. The lock was not bypassed. The VrApi lines saved from those attempts (`Logs/quest/vrapi_ux_redesign.txt`, `vrapi_baseline_rc.txt`) describe the system compositor idling at 90 FPS and must not be quoted as app performance.

## How the numbers will be collected

`scripts/quest_perf_watch.ps1` is a passive recorder. It never launches the app and never touches the proximity sensor or the lock screen. It waits until MuseDesk is running on the headset, then records the compositor's per-second line and memory for up to 8 minutes and writes `Logs/quest/perf_summary_<label>.txt` with average, minimum, 5th and 95th percentile for FPS, app GPU time, GPU and CPU utilisation, stale frames and memory.

A copy has been waiting since 2026-11-13 00:20 (label `worn_baseline`, 4 hour window). To run it yourself:

```
powershell -ExecutionPolicy Bypass -File scripts\quest_perf_watch.ps1 -Label rc1 -CaptureMinutes 8
```

then put the headset on, open MuseDesk and go through the checklist in `FINAL_INTERACTION_QA.md`, including opening the board and one full run. That covers idle, hand interaction, animation and the open board in one capture.

Draw calls are not in the VrApi line. They need a development build with the profiler attached, which has not been made.

## What changed that affects cost (not measured)

Likely cheaper since the redesign: 20 decor props off; board collapsed and its interactables disabled until opened; billboarded status panels gone; the 50 x 60 cm viewer panel no longer shown; key colour components that re-applied a property block every frame are disabled, and the replacement holds no block at rest.

Likely more expensive: three dynamic font atlases (Inter Medium, Inter Bold, LiberationSans fallback) instead of one; 50 rounded plates have more triangles than quads (tens each); wide keys from models; the completion sequence runs an extra 2.5 s of animation on delivery.

No transparent materials were added in either pass.

## Still deferred

Foveated rendering, and any tuning, until there is a measurement to tune against.
