# Final feature flag decision

2026-11-13. Deadline 2026-11-18, noon Pacific.

## The flags

On the `SpatialWorkspaceDirector` component (scene object `Spatial`), field `flags`:

| Flag | In the current build | Effect when off |
|---|---|---|
| `floorModeEnabled` | **ON** | No Floor key; the floor code does nothing |
| `ceilingModeEnabled` | **ON** | No Ceiling key; the ceiling code does nothing |

Desk mode has no flag. It is always available, is the start-up mode on a fresh install, and is what the app falls back to when a saved spot no longer validates or a placement fails.

With both flags off the spatial system only publishes "Desk" and shows nothing: the app is then the desk release candidate.

To change them: set the two booleans in `AgentScripts/IntegrateSpatial.cs` (the lines that set `director.flags`), run it through `scripts/run_guarded.ps1`, and rebuild. Nothing else changes.

## Why both are ON right now

For validation only. The owner asked to try both modes on the headset, and neither can be judged without wearing it. **ON in this build is not a release decision.**

## Decision rule for the submission build

A mode ships only if its hardware gate in `QUEST_HARDWARE_VALIDATION_MATRIX.md` is passed by a person wearing the headset.

| Gate A (Desk) | Gate B (Floor) | Gate C (Ceiling) | Ship |
|---|---|---|---|
| pass | pass | pass | Desk + Floor + Ceiling |
| pass | pass | fail or untested | Desk + Floor; Ceiling off; do not show Ceiling as a feature in the video or description |
| pass | fail or untested | any | Desk only; both off |
| fail | any | any | Fix Desk first; nothing else matters until Gate A passes |

Ceiling does not ship without Floor: it depends on the same courier, beacon and near-user kit, which Gate B exercises.

## Recommendation today

**Unknown until the headset pass; plan for Desk + Floor.**

- **Desk:** the strongest position. The owner wore the previous desk build and reported one defect, since fixed. The refinement pass on top of it is unverified by hand but passes every automated and Play Mode check.
- **Floor:** the higher-value spatial mode and the lower-risk one. Its motion is along the floor to the side, its failure modes are mild (a spot that looks wrong can be changed or cancelled), and the courier delivery is the interaction most worth showing. Unknowns: what real room scans contain, grabbing the hovering paper, legibility of the office at 1 to 2 m.
- **Ceiling:** the most striking and the riskiest. An office moving near head height beside a seated person must be right. The descent is now checked as a swept volume against detected furniture, the user and walls, and refuses when it cannot be shown clear; but room scans generally do not contain lamps, fans or monitors, so the first descents must be watched, with Stop within reach. Ship it only after it has been lowered and raised several times in the real room without surprise.

## What is deliberately not behind a flag

The near-user kit, the attention controller and the remote delivery controller are inert in Desk mode by construction (they act only when the office is remote). If Floor and Ceiling are both off, none of them can activate.
