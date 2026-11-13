# Final performance results

2026-11-12.

## There are no device performance numbers

No frame-time, CPU, GPU or memory measurement of MuseDesk on the Quest 3 exists, for this build or any earlier one. The unattended run on 2026-11-12 collected 60 VrApi lines, but the app process never started (lock screen), so those lines describe the system compositor idling at 90 FPS, not the app. They are in `Logs/quest/vrapi_ux_redesign.txt` and must not be quoted as app performance.

To measure: unlock the headset, run `scripts/quest_run.ps1 -Label perf -Seconds 120` while going through one full demo run, and read `Logs/quest/vrapi_perf.txt` (FPS, App and GPU time, CPU and GPU level, memory).

## What the redesign changed that affects cost (not measured)

Likely cheaper:
- 20 decor props and the physics tray are deactivated, with their renderers, colliders and rigidbodies.
- The board is collapsed at start and its interactables are disabled while collapsed.
- Three billboarded status panels and three billboarded name tags are gone.
- New views rebuild text only on reducer events; `CardRoleTint` no longer instantiates a material per card.

Likely more expensive:
- One dynamic font atlas (Inter, 1024 x 1024) in addition to LiberationSans, which stays as the fallback.
- The paper has more quads and labels (cover, pending and outcome plates, details sheet). Only one group is active at a time.
- Eight short audio clips are synthesised once at start-up.

New plates are opaque unlit quads tinted through property blocks. No transparent materials were added.

## Still deferred

Foveated rendering (deferred at the user's request) and any measurement-led tuning.
