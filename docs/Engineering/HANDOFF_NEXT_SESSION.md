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
- `SpatialDrive.Report` finds TaskDock under `Panel_Dock` again (fixed, ceiling ideas round).
- Git still prints "bad object refs/stash" on maintenance; untouched. `.blend1` files stay uncommitted.
- No device performance numbers yet. Recorder: `scripts\quest_perf_watch.ps1 -Label desk -CaptureMinutes 4` (start by hand).

## Antigravity ceiling VFX (not in `rc-feedback-1`; INTEGRATED in the ceiling ideas round below â€” kept for the notes)

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

## Ceiling ideas round (2026-11-13 afternoon, after `rc-feedback-1`; restore point tag `pre-ceiling-vfx`)

Antigravity's `cbd606b` art paths merged (our `Art/Shaders.meta` kept). Built by four Sonnet builders, integrated here. **PLAY only, not in an APK yet, never worn.**

| Idea | What | Evidence |
|---|---|---|
| Ceiling VFX (iris mount, portal cone, halo, rings, particles) | `CeilingVfxBridge` maps `CeilingPodMachine` states onto `CeilingDeploymentVFXController.OnPodState`; switches `vfxEnabled` / `particlesEnabled` (turn particles off first if fill rate hurts) | Builder: mount inside Core's `mountClearance`/`mountDepth`; `mountOffset`, `mastAttachHeight`, `dockDrop` unchanged. PLAY: arrival burst + halo at dock. 11 tests |
| (4) Three-stage audio | `CeilingPodAudio`: unlock click, travel hum (0.9 down / 1.1 up), lock thunk+chime; procedural clips; Meta XR Audio spatializer | PLAY: cues on all legs incl. the first trip up from the desk (fixed: cue raised a frame before Ceiling mode was set is held 0.3 s). 9 tests. Volumes unheard |
| (1)+(2) Reach up and pull it down | `CeilingPullGesture`: hand ray or gaze on the pod/mast, pinch, pull down 12 cm â†’ lower; push up when engaged â†’ raise. Calls the core's `RequestPodDescend/Retract` (same sweep as the console) and shows the refusal reason | PLAY via `CeilingPullDrive`: not-targeted, early release, sideways, too slow, busy hand, held all refused; pull down and push up move the pod (false "refused" on push-up fixed). 14 tests |
| (3) Scan-reveal grid | `CeilingScanReveal` + `CeilingScanGrid.shader`: teal grid 2 mm under the real ceiling around the candidate mount point during ceiling preview, red when invalid, fades on confirm/cancel | PLAY: shown at ceiling-2 mm, wipe 0.6 s, fade to 0 after confirm. 8 tests |
| (5) Bounce light | Skipped (cost, uncertain on Quest) | â€” |

Ceiling safety fixtures re-run after the builders: Evaluator 5, Pod 8, SweptPath 19, SweptMoves 17, Director 15 â€” all pass.

Check on the headset: the effects over passthrough (fill rate), sound levels, whether a 12 cm pull feels right (thresholds in `CeilingPullGesture.settings`), the cue label reads the right way round, grid brightness. Known: a pinch aimed at a paper before the ISDK hovers it can still arm the pull (it needs 12 cm of travel to fire).

Three new pure-logic files live in `Scripts/Spatial/Core/` (`CeilingVfxMap`, `CeilingPodAudioCues`, `CeilingPullLogic`) because the EditMode asmdef only reaches Core; no existing Core file changed.

## Headset feedback 2 (2026-11-13 afternoon; restore point tag `pre-headset-feedback-2`)

Owner wore `rc-feedback-1`. Five items, PLAY-verified here, in the next APK; not yet worn.

| Feedback | Fix | Evidence |
|---|---|---|
| Floor: good spots refused; letting go on "ready" did nothing | Root cause: the only route to the floor turned overhead and swung a corner within 6-8 cm of the head axis, so the sweep (correctly) refused most spots beside/behind the shoulder. New `Core/FloorRoutePlanner` tries side-step shapes, all through the unchanged `SweepRoute`. "Ready" now includes the route. Release on a ready spot places (pinch >= 0.25 s, dragged >= 5 cm); refusals stay visible on a words plate. Chosen-spot margins: user 0.6 -> 0.45 m sides/behind, furniture 0.15 -> 0.11 m (both above the sweep's own). Lost-hand-placed-the-office bug found and fixed (a lost hand only drops). | PLAY `FloorAimDrive`: DragAndRelease placed (4-pose route, swept clear); ReleaseBesideShoulder placed (was refused); ReleaseWhereBlocked reason + no move; Twitch/Flick no place. Tests: FloorReleaseToPlace 9, FloorAimSession 9, FloorAimDirector 9, Persistence 5 + all spatial/ceiling fixtures green |
| Guided scan for Floor/Ceiling | `PlacementGuide` (scene object `LookGuide`): card "Look at the floor / Look up at the ceiling", 0.6 s gaze dwell, then the request; scan grid starts from the gaze point (`FloorScanReveal` new, `CeilingScanReveal` sweeps gaze -> mount). Floor preview opens where you looked when that spot is usable (`CentrePreviewOn(.., quietIfBlocked)`), else the automatic spot silently. "Start here" after 8 s, Cancel. Voice "floor/ceiling mode" goes through it too | PLAY `PlacementGuideDrive`: card waits at level gaze; look down 45 / up 60 opens the preview, card switches to next step. 13 tests. NOTE: first builder run attached to the old desk-placement marker `PlacementGuide` (WorkspacePlacement hides it); the builder now removes that and uses `LookGuide` |
| Task card text spills to edges | `BuildTextFitV1`: labels inside the card face with 2.5 mm margin, chips clear of the top edge, titles 2 lines + ellipsis | PLAY `TextFitDrive.ReportWorstCase` (merge with the builder file to run): all live card labels >= 2.3 mm from the edge |
| Agent name tags overflow / not centred | Nameplates: role + activity one line each, centred as a block; gaze chips centred, inside the plate | 6.6-6.9 mm margins; chips 3.5 mm (were -7 mm) |
| Move panels up/down | Height follows the hand 1:1 within -5..+45 cm of the desk plane, kept on release; near the desk up to 3 cm below the detected desk plane (`deskSinkAllowance`) | PLAY `PanelsDrive`: dock lowered 52 -> 0 mm, raised to 300 mm. PanelHeight 8 tests |

Docs still out of date: `ROOM_PLACEMENT_FINDINGS.md` "known mismatch" (wall margin now 0.11 m) and the "pinch-hold to confirm" wording above (release-to-place is primary; hold still works).

## Scene build order (append-only; never re-run older builders after newer)

... `IntegrateSpatial`, `BuildSpatialArtV1`, then this round: `BuildStampPoofV1`, `BuildFloorAimV1`, `BuildAgentInteractionV1`, `BuildModeConsoleV1`, `BuildMovablePanelsV1`, `BuildCeilingPolishV1`, `FixConsoleHeight`, `BuildMovablePanelsV1` (again), `BuildVoiceV2`, then the ceiling ideas round: `BuildCeilingVfxV1`, `BuildCeilingAudioV1`, `BuildCeilingScanV1`, `BuildCeilingPullV1`, then headset feedback 2: `BuildTextFitV1`, `BuildFloorPlaceFixV1`, `BuildPlacementGuideV1`, then rc6 gesture safety: `BuildPlaceHereV1`, then rc6-1 feedback (guide card polish, desk office 9 cm lower: `offDeskDrop` 0.30 -> 0.39): `BuildGuideCardPolishV1` (run AFTER `BuildPlaceHereV1`; check with `GuideCardPolishDrive Heights|Card`).

## Tools

- Builders: `scripts\run_guarded.ps1 -File AgentScripts\X.cs -Entry X.Apply` (the Editor is shared with Antigravity).
- Play Mode drivers: `scripts\drv.ps1 <Class> <Method> [arg]` with `PlayDrive`, `SpatialDrive`, `StampPoofDrive`, `FloorAimDrive`, `CeilingPolishDrive`, `PanelsDrive`, `VoiceDrive`, `AgentInteractionDrive`. A string argument with spaces does not pass through `drv.ps1` yet.
- Tests: `scripts\tests.ps1 <FixtureName>`.
- Agents: `.claude/agents/` has `musedesk-scout` (Haiku), `musedesk-builder` (Sonnet), `musedesk-architect` (Opus). Route by cost.
- Records: `QUEST_HARDWARE_VALIDATION_MATRIX.md` (defect log has this round's seven items as "In progress"; mark them after the owner re-tests), `FINAL_SPATIAL_RELEASE_AUDIT.md` (describes `rc-spatial-1`; not yet updated for this build).

Walk routes (rc6-1 feedback, agents clipped through the Researcher's desk): after BuildPlaceHereV1, run BuildWalkRoutesV1 (Researcher exit waypoints + moves Decor_MiniPlant behind the agents; Revert() undoes). Check with scripts\drv.ps1 WalkClipDrive Measure (edit or Play) and WalkClipDrive Delivery then DeliveryResult (Play).

