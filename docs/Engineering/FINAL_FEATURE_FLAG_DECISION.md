# Final feature flag decision

2026-11-13. Deadline 2026-11-18, noon Pacific.

## The flags

One asset is the single source of truth: `Assets/MuseDesk/Resources/ReleaseConfig.asset` (`MuseDeskReleaseConfig`, `Scripts/Config/`). The scene object `Spatial/ReleaseConfig` holds a `ReleaseConfigApplier` (execution order -10000) that reads it at start-up, applies it, and logs one line: `[MuseDesk.Release] <buildLabel> floor=.. ceiling=.. pull=.. particles=.. cone=.. grids=.. voice=..`. The applier overrides `director.flags`; the values serialized on the director are only a mirror.

| Flag | Default | Effect when off |
|---|---|---|
| `floorMode` | ON | No Floor key; the floor code does nothing (director `floorModeEnabled`) |
| `ceilingMode` | ON | No Ceiling key; the ceiling code does nothing (director `ceilingModeEnabled`); also turns the pull gesture off |
| `ceilingPullGesture` | ON | `CeilingPullGesture` component disabled (no pinch-and-pull, no cue) |
| `ceilingParticles` | ON | Aperture, tether and arrival particle systems never play or emit (`CeilingDeploymentVFXController.particlesEnabled`) |
| `ceilingPortalCone` | ON | The portal cone stays inactive (`portalConeEnabled`); the halo and ripple rings stay |
| `placementScanGrids` | ON | `CeilingScanReveal` and `FloorScanReveal` disabled and their quads hidden; the placement guide card and keys are unaffected |
| `voice` | ON | `VoiceCommandCenter`, `WitVoiceSource`, `VoiceGestureActivator` disabled, `VoiceDecisionController.voiceEnabled` and `gestureEnabled` forced false (the paper's Voice chip goes), mic key shown in its Off colour |
| `buildLabel` | `rc6-dev` | Only logged |

Desk mode has no flag. It is always available, is the start-up mode on a fresh install, and is what the app falls back to when a saved spot no longer validates or a placement fails.

With both mode flags off the spatial system only publishes "Desk" and shows nothing: the app is then the desk release candidate.

To change a release (edits the asset only; rebuild afterwards):

```
scripts\run_guarded.ps1 -File AgentScripts\BuildReleaseConfigV1.cs -Entry BuildReleaseConfigV1.Preset_DeskFloor
```

Other entries: `Preset_All`, `Preset_DeskOnly`, `Show`; from code `BuildReleaseConfigV1.SetFlags("floor=1 ceiling=0 voice=0 label=rc6-floor")` (keys: floor ceiling pull particles cone grids voice label). `BuildReleaseConfigV1.Apply` creates the asset and the scene object if missing; `Revert` deactivates the applier. Play Mode check: `AgentScripts/ReleaseConfigDrive.cs` (`Report`, `Set`, `ProbeVfx`, `Reset`; applies a runtime copy, never the asset).

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
