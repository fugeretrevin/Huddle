# Final spatial release audit

2026-11-13 (updated for rc6-1). Deadline 2026-11-18, noon Pacific.

## Current build

| Item | Value |
|---|---|
| Branch | `release/final-refinement` |
| Commit | `b469a6a` (tag `rc6-1`) |
| APK | `Builds/MuseDesk_rc6.apk` (85.3 MB), SHA-256 `F560AA6FD49C11EA5C6E11041A7ED436A91E065A825ED328325A07C80DB16BCA` |
| Installed on the Quest 3 | Yes, 2026-11-13 ~17:30. **Not worn.** |
| Release config | `Assets/MuseDesk/Resources/ReleaseConfig.asset`, label `rc6-dev`: floor, ceiling, pull, particles, cone, scan grids and voice all **ON** (validation build, not a release decision: see `FINAL_FEATURE_FLAG_DECISION.md`) |
| Start-up mode | Desk on a fresh install; a saved Floor or Ceiling spot is restored only if it still validates and its route is clear |

Rollback points: `rc6-baseline` (`beb01a8`), `rc-feedback-2` (`3aa7fc2`, `Builds/MuseDesk_rc_feedback2.apk`), `rc-feedback-1` (`6fbedcc`, `Builds/MuseDesk_rc_feedback1.apk`), `rc-spatial-1` (`8a5f931`, `Builds/MuseDesk_rc_spatial.apk`). Older APKs: `MuseDesk_rc_refinement.apk`, `MuseDesk_pre_final_refinement.apk`, `MuseDesk_pre_ux_redesign.apk`.

## Evidence summary

Codes as in `RC6_HARDWARE_QA.md` and `QUEST_HARDWARE_VALIDATION_MATRIX.md`: AUTO, PLAY, APK, LOG, WORN, NT, FAIL.

| Area | AUTO | PLAY | APK | LOG | WORN |
|---|---|---|---|---|---|
| Compile and full EditMode suite: 701 of 701 over three runs, 2026-11-13 (`scripts/tests.ps1` has a hard timer; the earlier one-run hang did not reproduce) | yes | | | | |
| Smoke checks and XR visual QA: last recorded for rc-feedback-1 (78 of 78; 0 FAIL, 89 WARN). Not re-run for rc6-1 | | | | | |
| Desk loop (start, paper, details, approve, completion, receipt, viewer, archive, board, run again, reject) | | yes | yes | no | **no** (the previous desk build was worn; rc6-1 is not) |
| Floor: hand ownership, drag, release leaves the outline, Place here, route blocked, Stop, room change (`RC6_GESTURE_CONFLICT_TESTS.md`) | yes | yes, synthetic room | yes | no | **no** |
| Ceiling: pull down and push up, target lost, held paper, Stop, lower, raise, VFX, pod audio | yes | yes, synthetic room | yes | no | **no** |
| Guided look-at placement card (0.6 s dwell, Start here after 8 s, Cancel) | yes | yes | yes | no | **no** |
| Real room scan (MRUK) feeding placement | | no | yes | no | **no** |
| Live backend | Server side: live e2e 71 of 71 on 2026-11-13 (a script plays the client) | no | yes | server only | **no**: the Quest client has not had a live run since round 1 |
| Device performance | | | | no | **no**: no numbers yet |

**Nothing since `rc-feedback-1` has been verified by a person wearing the headset.** The spatial work from `rc-spatial-1` through rc6-1 has been worn only in part (the ceiling pod, basic, on `rc-spatial-1`). Unattended launches are blocked by the headset lock screen, which is not bypassed.

## What was reconciled in this pass

**Art.** Antigravity's spatial package is merged and in use: `FloorOfficePlatform`, `FloorCourierStation`, `CeilingMount`, `SuspendedOfficePod` (its built-in shelf; the separate `CeilingInspectionShelf` prefab is not placed), `WorkspaceAttentionBeacon`, `DocumentCourier`, both placement ghosts, `AgentConversationPerch`, `SpatialMailbox`. Ceiling VFX (iris mount, portal cone, halo, rings, particles) is integrated through `CeilingVfxBridge`; particles can be switched off separately (`ceilingParticles`).
- Two of Antigravity's art controllers are deliberately not used because they move transforms the logic owns: `CeilingWorkspacePodController` and `DocumentCourierController`. There is one pod state machine (`CeilingPodMachine`) and one writer of the office's pose away from the desk (`WorkspaceTransitionController`).
- Refined office art adopted where it fits by measurement: polished factory (same rig, controller and clips), three nameplates, mode plaque. Skipped with numbers in `docs/Design/ART_ADOPTION_NOTES.md`: trays, dock, receipt, task card, board frame. Paper folio: adopted, then switched off again because its lit material rendered grey against the unlit cream plates. Revert all: `BuildOfficeArtV3.Revert`; per element: its `Enable...` constant.
- Scene: 0 missing scripts; all art sockets unit scale; the office stays at scale 1 in every mode.

**Scene safety.** The Unity Editor is shared with Antigravity, which opens its own preview scenes. Every production step ran through `scripts/run_guarded.ps1` (opens `MainMRScene` and runs the step in one Editor call; refuses if another scene has unsaved changes). Antigravity's work was done in a separate worktree (`.worktrees/`, not tracked here).

**Git.** Background maintenance prints "bad object refs/stash" (a stale stash reference from before this work). Commits succeed. Nothing was cleaned up or rewritten.

## Spatial UX as built

- **Ambient.** With the office on the floor or ceiling, the user's desk holds: task dock, mode plaque, Focus key, one mode key, two summon keys. Stamps, receipt, mailbox, board and viewer appear only when there is something for them. Stamps leave with a pop after a decision (`rc-feedback-1`).
- **Mode console.** One wedge console (Desk / Floor / Ceiling selector, context keys, status line, Focus, mic). Grab bars move panels up and down within a desk-relative band; height is kept (`rc-feedback-2`).
- **Guided placement.** Floor or Ceiling (key or voice) shows a card, "Look at the floor" / "Look up at the ceiling". After a 0.6 s gaze hold the preview opens where the user looked. "Start here" appears after 8 s; Cancel restores everything.
- **Attention.** One beacon over the office, with a small mirror near the user: "Needs you" (decision), "Done" (delivery, leaves after 20 s), "Problem" (failure). Driven only by reducer events. Focus mode holds back non-urgent items.
- **Engaged.** A decision reaches the user by courier from the Researcher's dispatch station; stamps appear at the desk; the same approval flow runs. The office itself only moves when asked.
- **Signature moment per mode.** Desk: the Researcher walks up with the paper. Floor: the courier flies the paper over. Ceiling: the courier, or the user pulls the pod down.

## Movement safety

The relaxed check on the last 45 cm of the ceiling descent was wrong and is gone. It accepted a path where the office passed through an obstacle in the final 0.3 m. Details and numbers: `ROOM_PLACEMENT_FINDINGS.md`.

For every move of the office (to the floor, to the ceiling dock, descent, retraction, back to the desk):
- The route is planned and **swept before anything moves**: the office's box sampled at most every 5 cm, against detected furniture (8 cm margin, 30 cm more on the high part), a keep-out around the seated user, and the room outline. For the ceiling, the mast and the mount are in the volume too.
- Straight where clear; otherwise up, across above head height, and down. For the floor, `FloorRoutePlanner` also tries side-step shapes, each through the same sweep. Never a diagonal fallback.
- A route that cannot be shown clear is refused with words that say what to do next. Nothing teleports.
- **Floor placement (RC6):** letting go does not move the office. It leaves the footprint in its staged state (ready, refused with a reason, or needs confirmation). "Place here" re-plans and re-sweeps the route at the press. The placement is saved on arrival only.
- A room change during a desk or floor move re-sweeps the route and sends the office back if it is now blocked. A pod move is not re-swept mid-move (open).
- The mover travels exactly the swept poses (asserted by tests that record every pose written).
- **Stop** (the Cancel key becomes Stop during any move) halts and reverses along the path already travelled. While a paper is held it only halts.
- Each descent re-validates against the current head pose and room. After a recenter the office is only ever written to a pose validated after it; otherwise it holds and asks to be placed again.

## Hand ownership (RC6)

One owner per hand. Priority: something held > close-range grab, poke or UI ray > floor placement drag > ceiling pull > ambient.
- A pinch arms the floor drag or the ceiling pull only when it is fresh, held for 0.15 s, with nothing held in either hand, and with no grab, poke or UI hover for 0.30 s before it.
- Tracking loss freezes and then cancels. A lost hand is never a release.
- The ceiling pull needs the pod under the hand ray or gaze, and fires at 12 cm of pull down.
- Scenarios and the fixtures that check them: `RC6_GESTURE_CONFLICT_TESTS.md`. Feel is untested on the head.

Limits, stated plainly: a clear sweep means clear of what the room scan contains. Lamps, fans, monitors and anything not scanned are invisible to it. The user's Confirm, watching the first moves, and Stop are part of the safety design.

## Remaining bugs and risks

| # | Item | Severity | Reproduce | Status |
|---|---|---|---|---|
| 1 | Nothing from `rc-feedback-1` onward has been worn (rc6-1 installed, not worn) | Blocks release claims | n/a | Needs the owner: `RC6_HARDWARE_QA.md` passes A to C |
| 2 | Real room scans untested: floors, ceilings, furniture volumes may arrive differently from the synthetic rooms; fail-closed rules may refuse often (for example the real desk volume enclosing the desk office) | High for Floor / Ceiling | Enable a mode in a scanned room | NT. If Desk return is refused when the scan disappears, `returnToDeskWithoutRoom` on the director is the escape hatch (off by default) |
| 3 | Beacon showed "Needs you" once in a ceiling session with no decision pending | Medium | Seen once in Play Mode; not reproduced in two later sessions | Open; watch for it |
| 4 | Floor platform base sits 3 cm below the floor plane | Low, visual | Floor mode, look at the platform edge | Open: needs a height offset in the floor settings |
| 5 | Pod deck is 5 cm narrower than the office; arch may stand close behind the desks | Low, visual | Ceiling mode, lowered | Open: `PodShiftZ` in `BuildSpatialArtV1.cs` |
| 6 | Moves take at most 2.5 s, so an up-and-over of about 3 m is brisk | Medium, comfort | Floor confirm | Open: raise `maxMoveSeconds` after seeing it |
| 7 | Hovering paper is a 7 cm cover re-positioned each frame: grab may be fiddly | Medium | Floor mode, decision | NT |
| 8 | Hand-drop of a card on an agent never tested by hand | Medium | Desk, board | NT (`RC6_HARDWARE_QA.md` A6) |
| 9 | Polished paper folio off (lit against unlit materials) | Low | n/a | Open: needs an unlit paper material |
| 10 | Scripted demo usually skips the Developer-to-Tester carry | Low | Desk run | By design: delivery supersedes a stale handoff |
| 11 | Voice is ON in rc6-1 (`voice` flag); needs internet (Wit.ai); no text-to-speech | Low | Mic key on the console | NT on device in this build |
| 12 | Conversation perch shows a summary; it does not move the character | Low | Status key in a remote mode | Deferred by design |
| 13 | Performance recorder: nothing captured yet. Waiting for the rc6-1 wear | n/a | n/a | `scripts/quest_perf_watch.ps1 -Label rc6_worn` (starts when MuseDesk opens, records 30 min, writes `Logs/quest/perf_rc6_worn.csv`) |
| 14 | Ceiling effects (particles, portal cone) and two scan grids over passthrough: fill rate not measured | Medium | Ceiling mode, pod lowered | Open. Switch off in `ReleaseConfig.asset` (`ceilingParticles`, `ceilingPortalCone`, `placementScanGrids`) if the frame rate is tight |
| 15 | Hand thresholds (0.15 s arm, 0.30 s clear window, 0.25 s pull target grace, 12 cm pull) and the guide card (0.6 s dwell, 8 s Start here, pose) are untested by hand | Medium | Floor and Ceiling, `RC6_HARDWARE_QA.md` B and C | NT |
| 16 | A pod move in flight is not re-swept when the room changes | Medium, safety | Change the room scan during a descent | Open |
| 17 | The full EditMode suite hung once earlier (not reproduced in three runs of 701) | Low | n/a | Watch; use `scripts/tests.ps1` |

## Recommended release configuration

Decide after the headset pass, by the rule in `FINAL_FEATURE_FLAG_DECISION.md`. Planning assumption: **Desk + Floor**, with Ceiling shipped only if it has been lowered and raised several times in the real room without surprise. rc6-1 has every flag on for validation; that is not the release setting.

## What the owner does next

1. Start `powershell -ExecutionPolicy Bypass -File scripts\quest_perf_watch.ps1 -Label rc6_worn` on the PC (it waits for MuseDesk to open and records for 30 minutes).
2. Wear the headset and follow `docs/Engineering/RC6_HARDWARE_QA.md`: Desk (A1 to A9), Floor (B1 to B8, including B4 with a paper in the other hand), Ceiling (C1 to C8; keep a finger near Stop on the first descents). Stay about 20 s in each state so the perf rows have data. About 15 minutes.
3. Optional: live backend pass D1 to D5 (PC tethered).
4. Report by row number, one word per row (ok / bad, what you saw). Every defect goes in the defect log with commit `b469a6a`, is fixed, rebuilt, and the same step repeated.
