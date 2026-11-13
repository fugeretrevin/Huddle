# Handoff for the next session

2026-11-13, about 11:50. Deadline 2026-11-18, noon Pacific.

## Where things stand

| Item | Value |
|---|---|
| Branch | `release/final-refinement` |
| Build commit | `6fbedcc`, tag `rc-feedback-1` |
| APK | `Builds/MuseDesk.apk` (89 MB), copy `Builds/MuseDesk_rc_feedback1.apk`; **installed on the Quest 3** 2026-11-13 11:45 |
| Previous build | `8a5f931` / `rc-spatial-1` (`Builds/MuseDesk_rc_spatial.apk`); restore point before this round: tag `pre-headset-feedback-1` |
| Flags | Desk always; Floor ON; Ceiling ON (validation only) |

**Nothing in this build has been worn.** Everything below is Editor Play Mode with driver scripts (no hands, no passthrough, synthetic room).

## What this round added (owner's headset feedback on `rc-spatial-1`)

| Feedback | What was built | Evidence |
|---|---|---|
| Stamp stays on the desk after stamping | Both stamps leave with a pop and puff about 2 s after a decision and pop back for the next one | PLAY: Waiting then Hidden after approve; 12 tests |
| Floor placement needs bending down | Point at the floor, footprint highlight, pinch and drag, red with a reason where it cannot go, pinch-hold to confirm | PLAY: drag, invalid spot refused, hold-confirm moved the office; 20 tests |
| Ceiling: props lag, props out of place | Loose props pinned to the office during every move; plant, lamp, books, clock hidden in Ceiling mode (they stand outside the pod deck) | PLAY: 0.0 mm drift over a 1.76 m move; 7 tests |
| "Needs you" marker tilted and central | Always upright; hangs under the pod's front edge when docked | PLAY: tilt 0.00 deg, placement BelowPod |
| Control cards intersect the lowered office | Grab bar under the dock (with board), plaque, mode console and receipt; poses saved; reset by 1.5 s pinch on a bar or by voice | PLAY: drag and save, panels keep their place across mode switches; 37 tests |
| Mode keys look cheap and stacked | One wedge console: Desk / Floor / Ceiling selector, context row (Next, Confirm, Cancel, Stop, Lower/Raise, Bring, Status), status line, Focus key, mic key | PLAY: context row changes with mode; capture `docs/review_screenshots/feedback1/` |
| Voice and interactivity | Voice command centre (mic key on the console, local intent parser, undo window for approve/reject, ask an agent for status); characters glance up, react to pokes, show a status chip on gaze, wave back, speech bubbles | PLAY via `VoiceDrive.Say` and `AgentInteractionDrive`; 346 voice tests, 28 interaction tests |

Automated: every EditMode fixture passes when run one at a time (about 600 tests). Smoke checks 78 of 78. XR visual QA 0 FAIL, 89 WARN.

## Check on the headset first

1. Desk: approve with the stamp, watch it poof; next run, stamps come back.
2. Mode console at front right: does it look right, are the keys reachable, do the grab bars drag and stay?
3. Mic key on the console: press, say "start", "Developer, what are you working on", "approve" with a paper in hand (1.5 s undo), "floor mode". Needs internet on the headset. Does the mic permission prompt appear only on first press?
4. Poke a character; look at one for a second; wave.
5. Floor: press Floor, point at the floor, pinch-drag the footprint, pinch-hold to confirm.
6. Ceiling: props ride with the pod, marker under the pod, drag the dock clear of the lowered office.

## Known problems and loose ends

- **Full EditMode run hung the Editor** (main thread stuck over 10 min; Editor was killed and restarted). Each fixture passes alone; the cause is not found. Run fixtures with `scripts\tests.ps1 <FixtureName>` until it is.
- Voice readout position above the mic slot was never looked at; it may overlap the console or receipt.
- Console key travel is 5.2 mm, not the 2 mm asked for.
- Console was 21 mm below the desk plane after its builder; `AgentScripts/FixConsoleHeight.cs` lifts it (run after `BuildModeConsoleV1`, then re-run `BuildMovablePanelsV1`).
- Spoken approve in the scripted demo said "Not sent" although it was sent; patched in `VoiceOffice.Respond`, compiled, **not re-run in Play Mode**.
- Floor drag: no wrist-twist rotation (office always faces the user). A pinch meant for a paper can grab the footprint if the ray lands on it.
- Pod deck stays 5 cm narrower than the office (widening would leave the validated footprint).
- A knocked-over prop pops home when a move starts.
- Character arm poses, squash axis, wave and high-five thresholds were written blind. Old touch summary card is switched off (poke bubble replaces it).
- Voice gesture (pinch at the mouth) is built but off. No TTS.
- `SpatialDrive.Report` no longer finds TaskDock in the kit (it moved to `Panel_Dock`).
- Git still prints "bad object refs/stash" on maintenance; untouched. `.blend1` files stay uncommitted.
- No device performance numbers yet. Recorder: `scripts\quest_perf_watch.ps1 -Label desk -CaptureMinutes 4` (start by hand).

## Waiting to be integrated: Antigravity ceiling VFX (not in `rc-feedback-1`)

Branch `art-spatial-workspace-expansion`, commit `cbd606b`, in `.worktrees/spatial-art` (the earlier merge took `53f7129`). By Antigravity's own report, not checked here:
- `CeilingMount` rebuilt with a 4-leaf iris docking bay (4 bones `Bone_Iris_F/B/L/R`, 0.44 m canopy, 4,580 tris); `DockedPose` moved to y = -0.388 m.
- `SpatialPortalCone.shader` (additive, single-pass instanced), `Mat_Spatial_PortalCone`, `Mat_Spatial_SoftParticle`, cone and ring meshes, `Tex_SoftGlowDot.png`.
- `CeilingDeploymentVFXController.cs`: halo, ripple, portal cone, tether motes, aperture particles, air-cushion ring, arrival burst. It binds to `CeilingWorkspacePodController.StateChanged`.

Integration notes for whoever does it:
- This project does **not** use `CeilingWorkspacePodController` (it moves transforms the logic owns). The VFX controller needs a small adapter driven by `CeilingPodMachine` states through `CeilingPodArt`, or its `ApplySnapshotImmediate(...)` called from there.
- The mount changed size and `DockedPose` moved: `mountOffset`, `mastAttachHeight` and the mount/mast part of the swept volume must be re-measured (`BuildCeilingPolishV1` measures them; re-run it after the art is placed) and the ceiling safety tests re-run.
- `BuildSpatialArtV1` placed the old mount; write a new builder (`BuildCeilingVfxV1`) rather than re-running it.
- Additive transparents over passthrough and two more particle systems cost fill rate: check on the headset before keeping them on.
- Tag a restore point before merging; merge only the art paths, as last time.

Antigravity's follow-up ideas, in the order worth doing: (4) three-stage spatial audio for unlock, travel and lock; (1) pinch-and-pull on the tether to lower the pod; (2) look up and pinch to summon; (3) scan-reveal grid on the real ceiling during placement; (5) bounce light on real furniture (most cost, least certain on Quest).

## Scene build order (append-only; never re-run older builders after newer)

... `IntegrateSpatial`, `BuildSpatialArtV1`, then this round: `BuildStampPoofV1`, `BuildFloorAimV1`, `BuildAgentInteractionV1`, `BuildModeConsoleV1`, `BuildMovablePanelsV1`, `BuildCeilingPolishV1`, `FixConsoleHeight`, `BuildMovablePanelsV1` (again), `BuildVoiceV2`.

## Tools

- Builders: `scripts\run_guarded.ps1 -File AgentScripts\X.cs -Entry X.Apply` (the Editor is shared with Antigravity).
- Play Mode drivers: `scripts\drv.ps1 <Class> <Method> [arg]` with `PlayDrive`, `SpatialDrive`, `StampPoofDrive`, `FloorAimDrive`, `CeilingPolishDrive`, `PanelsDrive`, `VoiceDrive`, `AgentInteractionDrive`. A string argument with spaces does not pass through `drv.ps1` yet.
- Tests: `scripts\tests.ps1 <FixtureName>`.
- Agents: `.claude/agents/` has `musedesk-scout` (Haiku), `musedesk-builder` (Sonnet), `musedesk-architect` (Opus). Route by cost.
- Records: `QUEST_HARDWARE_VALIDATION_MATRIX.md` (defect log has this round's seven items as "In progress"; mark them after the owner re-tests), `FINAL_SPATIAL_RELEASE_AUDIT.md` (describes `rc-spatial-1`; not yet updated for this build).
