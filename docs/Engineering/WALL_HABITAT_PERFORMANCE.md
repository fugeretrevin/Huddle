# Wall Habitat: performance

**No device measurements exist yet.** The numbers below are counts and design choices, not frame times.

## Budget reasoning (from code, unmeasured)

- **Renderers.** Wall mode hides the desk office: 464 renderers were switched off in PLAY. The proxy habitat adds about 110 small unlit-style boxes (Simple Lit, no shadows) plus 4 avatar clones; about 95 are active at rest. The visit adds one clone and the portal one clone.
- **Batching (done 2026-11-15).** After the art manifest runs, the static structure boxes are merged into one mesh per colour with its own material (no property block, SRP-batcher compatible): PLAY measured 49 boxes into 7 renderers; active renderers under the placed habitat 34 (was about 95). Moving parts stay separate. Rollback: `WallHabitatView.BatchStaticStructure = false`.
- **CPU.** The stage tick is pure C#. There are 4 walkers and 1 elevator; Dijkstra runs only when a trip starts. Status colours refresh at 2 Hz. The desk-renderer rescan runs at 1 Hz with a reused list. There are no per-frame allocations in the steady state.
- **Avatar animators.** They use `CullUpdateTransforms`.
- **Effects.** No transparent effects or particles. The beacon pulses by colour only, and comfort mode stops the pulse.

## To measure on a worn Quest 3 (UNVERIFIED)

Use the existing watcher: `scripts\quest_perf_watch.ps1 -Label wall -CaptureMinutes 4`.

1. Wall idle: habitat placed, demo running.
2. Elevator trip plus Manager printing three cards.
3. Portal open with the 1.2 m avatar while the habitat animates.

The target is a stable 72 Hz, matching rc6 (worst GPU 4.7 ms on Desk). Record the results in this file.
