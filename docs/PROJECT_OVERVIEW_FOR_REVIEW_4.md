# MuseDesk: status for expert review (fourth pass)

2026-11-13. Branch `release/final-refinement`. Deadline 2026-11-18, noon Pacific.

This covers your two briefs: "Final Product Experience, Visual Quality & XR Reliability Pass" and "Room-Aware Spatial Workspace System". It says what is done, what is in progress right now, and what happens next.

## Evidence level, up front

Everything below compiles and passes automated checks, and both the refined desk flow and the new spatial modes were run end to end in Unity Editor Play Mode. **Nothing from either brief has been worn on the headset yet**, and there are still **no device performance numbers**: unattended launches are blocked by the headset lock screen, which we do not bypass. The spatial modes have only run against a synthetic room; real room scanning is untested.

| Check | Result |
|---|---|
| Unity compile | Clean |
| EditMode tests | 116 pass (33 existing, 48 placement, 35 attention and delivery) |
| Runtime smoke checks | 108, 0 failed |
| XR visual QA script (text depth, reach, key travel, overlaps) | 0 FAIL |
| Editor Play Mode, scripted demo | Full loop passes in Desk, Floor and Ceiling modes |
| APK | Refinement build installed; a build with the spatial modes is not made yet |

## Done: final refinement pass

Screenshots: `docs/review_screenshots/final_refinement/rc_01` to `rc_13`.

Your screenshot findings, checked against the running app:

| Reported | Verdict | Action |
|---|---|---|
| Faint text | Mostly a Scene View artifact, plus thin font | Inter Medium base and real Inter Bold |
| Stamp covering the paper | Preview artifact | None needed |
| Large panel half out of view | Real: the artifact viewer was 50 x 60 cm and opened by itself | Rebuilt |
| Details sheet detached | Real | Slides out from under the paper |
| Stretched Approve / Reject | Real | Purpose-built wide keys |
| Keys ignore hover / press | Real: wrong shader property | New key state component |

- **Decision paper:** header with sender, mode chip and request; 13 mm question; 9 mm summary; recommendation on a tinted plate with its reason; provenance always visible; outlined stamp target that lights as the stamp approaches; pale provisional mark that turns dark only on confirmation; stamps glide back to their pad; details sheet slides out and back.
- **Receipt, viewer, archive:** receipt card on a small stand with View and Run again; artifact viewer is a 28 x 22 cm sheet that opens centred only on request, with paging and provenance; archive is a paper card that rises from the out-tray.
- **Board:** opens smaller and closer with every key in reach; cards have a physical edge and hover, selected, pending and refused states; headings show counts; cards ease to their slots.
- **Keys and plates:** 29 keys with hover, press, release, refused and confirmed states; 11 wide keys (9 from Antigravity's models); 50 plates with rounded corners.
- **Characters:** 2.5 s completion sequence after confirmed delivery (lamp, Tester reacts, others acknowledge, factory delivers, token, one chime); distinct failure sequence; walk speed matched to movement; rejection no longer plays a failure animation; head turn capped.
- **State and onboarding:** the plaque shows "Connecting..." only during a real attempt; a two-line guide that hides while the paper is held.
- **Bug found:** hand-dropping a card on an agent had never sent the assignment. Fixed; untested by hand.

## Done: room-aware workspace (logic)

Additive and behind flags that are off by default; Desk mode is unchanged and is the fallback. The backend, contracts and event model are untouched; a mode change never starts or cancels work.

- **Architecture:** one director; a room abstraction with an MRUK implementation and a synthetic one for tests; pure-logic floor and ceiling evaluators; one transition controller that is the only writer of the office's pose away from the desk; a ceiling pod state machine (Docked, PreparingToDescend, Descending, Stabilizing, Engaged, PreparingToRetract, Retracting, ReturningToDock, ErrorRecovery); persistence validated against the room before restore.
- **Floor Office:** candidates in side sectors 1.0 to 2.2 m away, rejected for furniture, the user's feet, the forward cone and insufficient footprint; scored for visibility, clearance and side; ghost preview; next, nudge, rotate, confirm, cancel. In Play Mode: six candidates, office moved 1.7 m to the side, exact desk pose restored on return, placement restored on relaunch.
- **Ceiling Pod:** docks only under real ceiling geometry, never overhead; descends to beside the user; refuses to retract while the paper is held.
- **Attention:** levels from real events only (progress, completed, blocking, error). There is no backend event for optional suggestions, so that level exists but is unreachable; nothing is fabricated. Focus mode accumulates quiet updates and keeps blocking items visible.
- **Remote paper delivery:** a courier carries the same decision paper to a point 45 cm in front of the user; the approval flow is unchanged; a 3 s fallback snaps the paper within reach if the animation fails.
- **Near-user kit:** the dock, stamps, plaque, receipt, Focus key, board and viewer stay at the desk when the office leaves.
- **Summoning:** near-user Bring and Status keys; hand-ray targets on the beacon and agents (the rig has ray interactors); a summary perch for one agent.
- **Design fix found in testing:** the descent-path rule rejected every ceiling dock in a furnished room, because the spot beside the user is always next to their desk. Relaxed for the last 45 cm, which the footprint check covers.

Deviations from the brief: the conversation perch shows the agent's summary but does not move the character (it would fight the walk and handoff logic); voice summoning is wired but not practically usable (voice is live-only and the microphone is only started from the paper); the office stays at scale 1 in all modes.

## In progress now (two agents)

1. **Spatial art.** Antigravity delivered and we merged: floor platforms, ceiling mount, suspended pod, inspection shelf, seven-state beacon, courier and station, perch, mailbox, floor and ceiling ghost previews (16 prefabs, 6 animator controllers). An agent is replacing the placeholder primitives with these, driven by the existing logic.
2. **Office art.** Antigravity's polished factory, trays, nameplate and plaque plates, dock, receipt ticket, paper folio and task card. An agent is adopting each one only where measurement shows it fits the existing layout, with a one-step revert.

## Next, in order

1. Integrate both art passes; re-run the Play Mode loops in all three modes; re-run QA and tests.
2. Switch the Floor and Ceiling flags on; build and install.
3. **Owner on the headset:** the 22-step desk checklist (`docs/Engineering/FINAL_INTERACTION_QA.md`) and the spatial checklist (`SPATIAL_WORKSPACE_DEVICE_QA.md`, being written), with the passive performance recorder running (`scripts/quest_perf_watch.ps1`).
4. Fix what that finds. Likely areas: real MRUK room data, legibility of the office on the floor at 1 to 2 m, grabbing the hovering paper, keys that are moved at run time, wide-key press at the ends.
5. Performance tuning against real numbers; foveated rendering if needed.
6. Live mode run on the headset against the desktop server.
7. Final screenshots and demo video. Ship only the modes verified on the headset; flags make that a one-line choice.

## Questions for this review

1. At scale 1 the floor office is the same 0.6 m wide diorama seen from 1 to 2 m, so nameplates and the dock-size text are not readable from the seat. Should the office scale up on the floor (to what size), or rely on the beacon, the near-user dock and the courier?
2. For the Ceiling Pod, is "descend to beside the user" the right inspection pose, or should it stop higher and further out, since the user's controls stay at the desk anyway?
3. The demo's signature moment at the desk is the Researcher walking up with the paper. In remote modes the courier replaces it. Is that enough character, or should the real agent ride the courier or perch?
4. With five days left and nothing yet verified on the headset, would you cut scope to Desk plus Floor for the submission and show Ceiling only in the video?
5. Anything in `rc_04` (paper), `rc_08` (receipt), `rc_09` (viewer) or `rc_12` (board) that still reads as unfinished?

## Where to look

- Refinement: `docs/Design/FINAL_VISUAL_REFINEMENT.md`, `docs/Engineering/RELEASE_CANDIDATE_STATUS.md`.
- Spatial: `docs/Engineering/SPATIAL_WORKSPACE_ARCHITECTURE.md`, `SPATIAL_PLACEMENT_TESTS.md`, `SPATIAL_WORKSPACE_INTEGRATION.md`; code in `Assets/MuseDesk/Scripts/Spatial/`.
- Art hand-offs: `docs/Art/FINAL_ASSET_REFINEMENT_REPORT.md`, `docs/Art/SPATIAL_WORKSPACE_ASSET_MANIFEST.md`.
- Restore points: tags `pre-ux-redesign`, `pre-final-refinement`, `pre-spatial-integration`, each with a kept APK for the first two.
