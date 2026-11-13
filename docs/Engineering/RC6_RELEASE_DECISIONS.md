# RC6 release decisions

2026-11-13. Build `rc6-1` (`b469a6a`). Decisions made, and decisions waiting on evidence.

## Made in this round

| Decision | Why |
|---|---|
| **Floor placement needs an explicit "Place here".** Release-to-place is removed, and so is pinch-and-hold confirm. | An accidental grab plus a drag must never move the office. The route is re-planned and re-swept when Place here is pressed. |
| **One owner per hand** (`Core/HandOwnership.cs`). Priority: held, then close grab/poke/UI, then the placement drag, then the ceiling summon, then ambient. | A pinch meant for a paper could arm the floor drag or the ceiling pull. It is fixed at the system level and reads the Interaction SDK's own state, so there is no second interaction framework. |
| **Ceiling pull kept, behind its own flag** (`ceilingPullGesture`). It is not needed for any core flow; the console key does the same. | Kept only if it is reliable on the headset. If wear testing shows false triggers or misses, it is turned off for the submission. |
| **No safety margin loosened in RC6.** | The 3aa7fc2 changes stand: user 0.45 m, furniture and wall 0.11 m, all above the sweep's own margins. |
| **Placement saved on arrival, not at the start of the move.** | A crash mid-move used to leave a record of a spot never reached. |
| **Route re-swept on a room change during a desk or floor move.** | It was a gap. Pod moves are still not re-swept mid-move (open, low risk: descents are short and Stop is available). |
| **One release config** (`Resources/ReleaseConfig.asset`). | Replaces the hard-coded flags in `IntegrateSpatial.cs`. Presets: `Preset_All`, `Preset_DeskFloor`, `Preset_DeskOnly`. |
| **Desk is the start-up mode; no spatial setup on first launch.** | Unchanged and confirmed: the judge path needs no room scan, voice, controllers, PC or internet. |
| **Full EditMode suite runs in one go again.** | 701/701, three runs in a row. The earlier hang did not reproduce. `tests.ps1` now has a hard timer, so it can never block indefinitely. |

## Waiting on the worn pass (`RC6_HARDWARE_QA.md`)

The rule stays as in `FINAL_FEATURE_FLAG_DECISION.md`.

| Gate | Needed to pass | Then |
|---|---|---|
| A Desk | A1–A9 ok | Required for any submission |
| B Floor | B1–B8 ok; no accidental placement | `Preset_DeskFloor` at minimum |
| C Ceiling | C1–C8 ok; perf acceptable in `ceiling_descending` and `ceiling_deployed` | `Preset_All` |
| Pull gesture | C6 felt deliberate; C7 did not move the pod | Otherwise `ceilingPullGesture` off |
| Effects | p5 FPS and stale frames in the ceiling rows | Otherwise particles off, then cone off |
| D Live | D1–D5 ok | Live mode can be shown in the video; otherwise the video shows the labelled demo only |

**Current flags in `rc6-1`: all on.** This is a validation build only, not the release decision.

## Not doing

No new features or gesture types, no art production, no backend expansion, no test-infrastructure rewrite.
