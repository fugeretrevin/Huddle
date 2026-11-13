# Final spatial release audit

2026-11-13, 05:45. Deadline 2026-11-18, noon Pacific.

## Current build

| Item | Value |
|---|---|
| Branch | `release/final-refinement` |
| Commit | `8a5f931` (tag `rc-spatial-1`) |
| APK | `Builds/MuseDesk.apk` (89 MB); copy `Builds/MuseDesk_rc_spatial.apk` |
| Installed on the Quest 3 | Yes, 2026-11-13 05:41 |
| Enabled features | Desk (always); Floor **ON**; Ceiling **ON** (for validation, not a release decision: see `FINAL_FEATURE_FLAG_DECISION.md`) |
| Start-up mode | Desk on a fresh install; a saved Floor or Ceiling spot is restored only if it still validates and its route is clear |

Earlier APKs kept for rollback: `MuseDesk_rc_refinement.apk` (refined Desk only, no spatial code active), `MuseDesk_pre_final_refinement.apk`, `MuseDesk_pre_ux_redesign.apk`.
Tags: `pre-ux-redesign`, `pre-final-refinement`, `pre-spatial-integration`, `pre-office-art`, `pre-spatial-art`, `pre-safety2`, `rc-spatial-1`.

## Evidence summary

Codes as in `QUEST_HARDWARE_VALIDATION_MATRIX.md`: AUTO, PLAY, APK, LOG, WORN, NT, FAIL.

| Area | AUTO | PLAY | APK | LOG | WORN |
|---|---|---|---|---|---|
| Compile, 152 EditMode tests, 108 smoke checks, 0 missing scripts, XR visual QA 0 FAIL | yes | | | | |
| Desk loop (start, paper, details, approve, completion, receipt, viewer, archive, board, run again, reject) | | yes | yes | no | **no** (previous desk build was worn; this one not) |
| Floor: preview, confirm, up-and-over move, beacon, courier delivery, decide, receipt, return, Stop | tests | yes, synthetic room | yes | no | **no** |
| Ceiling: preview, confirm, dock, lower, hold blocks retract, raise, Stop during descent, return | tests | yes, synthetic room | yes | no | **no** |
| Real room scan (MRUK) feeding placement | | no | yes | no | **no** |
| Live backend | Server side: live e2e 71 of 71 on 2026-11-13 (real server, live producer, sandbox work, approve, reject, card bridge, revise loop; a script plays the client) | no | yes | server only | **no**: the Quest client has not driven a live run since the redesign |
| Device performance | | | | no | **no** |

**Not one line of the spatial work or the final refinement pass has been verified by a person wearing the headset.** Unattended launches are blocked by the headset lock screen, which is not bypassed.

## What was reconciled in this pass

**Art.**
- Antigravity's spatial package (branch `art-spatial-workspace-expansion`, commit `53f7129`) is merged (`4ea1230`); nothing was regenerated. In use: `FloorOfficePlatform`, `FloorCourierStation`, `CeilingMount`, `SuspendedOfficePod` (its built-in shelf; the separate `CeilingInspectionShelf` prefab is not placed), `WorkspaceAttentionBeacon`, `DocumentCourier`, both placement ghosts, `AgentConversationPerch`, `SpatialMailbox`.
- Two of its art controllers are deliberately not used because they move transforms the logic owns: `CeilingWorkspacePodController` and `DocumentCourierController`. Their models and animators are driven directly. There is one pod state machine (`CeilingPodMachine`) and one writer of the office's pose away from the desk (`WorkspaceTransitionController`).
- Refined office art adopted where it fits by measurement: polished factory (same rig, controller and clips), three nameplates, mode plaque. Skipped with numbers in `docs/Design/ART_ADOPTION_NOTES.md`: trays, dock, receipt, task card, board frame. Paper folio: adopted, then switched off again because its lit material rendered grey against the unlit cream plates. Revert all: `BuildOfficeArtV3.Revert`; per element: its `Enable...` constant.
- Scene: 0 missing scripts; all art sockets unit scale; the office stays at scale 1 in every mode.

**Scene safety.** The Unity Editor is shared with Antigravity, which opens its own preview scenes. Every production step ran through `scripts/run_guarded.ps1` (opens `MainMRScene` and runs the step in one Editor call; refuses if another scene has unsaved changes). Antigravity's spatial work was done in a separate worktree (`.worktrees/`, not tracked here).

**Git.** Background maintenance prints "bad object refs/stash" (a stale stash reference from before this work). Commits succeed. Nothing was cleaned up or rewritten.

## Spatial UX as built

- **Ambient.** With the office on the floor or ceiling, the user's desk holds: task dock, mode plaque, Focus key, one mode key, two summon keys. Stamps, receipt, mailbox, board and viewer appear only when there is something for them.
- **Attention.** One beacon over the office, with a small mirror near the user: "Needs you" (decision), "Done" (delivery, leaves after 20 s), "Problem" (failure). Driven only by reducer events. The suggestion state exists in the model and is unreachable because no backend event supports it. Focus mode holds back non-urgent items.
- **Engaged.** A decision reaches the user by courier from the Researcher's dispatch station; stamps appear at the desk; the same approval flow runs. Status opens a summary stand for the latest agent. The office itself only moves when asked.
- **Signature moment per mode.** Desk: the Researcher walks up with the paper. Floor: the Researcher offers it at the dispatch station and the courier flies it over. Ceiling: the courier, or lower the pod.

## Movement safety (the audit you asked for)

The relaxed check on the last 45 cm of the ceiling descent was wrong and is gone. Reproduced: it accepted a path where the office passed through an obstacle in the final 0.3 m. Details and numbers: `ROOM_PLACEMENT_FINDINGS.md`.

Now, for every move of the office (to the floor, to the ceiling dock, descent, retraction, back to the desk):
- The route is planned and **swept before anything moves**: the office's box sampled at most every 5 cm, against detected furniture (8 cm margin, 30 cm more on the high part), a keep-out around the seated user, and the room outline. For the ceiling, the mast and the mount are in the volume too.
- Straight where clear; otherwise up, across above head height, and down. Never a diagonal fallback.
- A route that cannot be shown clear is refused with words that say what to do next. Nothing teleports.
- The mover travels exactly the swept poses (asserted by tests that record every pose written).
- **Stop** (the Cancel key becomes Stop during any move, at the near-user position) halts and reverses along the path already travelled. While a paper is held it only halts.
- Each descent re-validates against the current head pose and room. After a recenter the office is only ever written to a pose validated after it; otherwise it holds and asks to be placed again.

Limits, stated plainly: a clear sweep means clear of what the room scan contains. Lamps, fans, monitors and anything not scanned are invisible to it. The user's Confirm, watching the first moves, and Stop are part of the safety design.

## Remaining bugs and risks

| # | Item | Severity | Reproduce | Status |
|---|---|---|---|---|
| 1 | Nothing verified on the headset | Blocks release claims | n/a | Needs the owner: gates A to E |
| 2 | Real room scans untested: floors, ceilings, furniture volumes may arrive differently from the synthetic rooms; fail-closed rules may refuse often (for example the real desk volume enclosing the desk office) | High for Floor / Ceiling | Enable a mode in a scanned room | NT. If Desk return is refused when the scan disappears, `returnToDeskWithoutRoom` on the director is the escape hatch (off by default) |
| 3 | Beacon showed "Needs you" once in a ceiling session with no decision pending | Medium | Seen once in Play Mode (Ceiling, before any run); not reproduced in two later sessions | Open; watch for it |
| 4 | Floor platform base sits 3 cm below the floor plane | Low, visual | Floor mode, look at the platform edge | Open: needs a height offset in the floor settings |
| 5 | Pod deck is 5 cm narrower than the office; arch may stand close behind the desks | Low, visual | Ceiling mode, lowered | Open: `PodShiftZ` in `BuildSpatialArtV1.cs` |
| 6 | Moves take at most 2.5 s, so an up-and-over of about 3 m is brisk | Medium, comfort | Floor confirm | Open: raise `maxMoveSeconds` after seeing it |
| 7 | Hovering paper is a 7 cm cover re-positioned each frame: grab may be fiddly | Medium | Floor mode, decision | NT |
| 8 | Hand-drop of a card on an agent never tested by hand | Medium | Desk, board | NT |
| 9 | Polished paper folio off (lit against unlit materials) | Low | n/a | Open: needs an unlit paper material |
| 10 | Scripted demo usually skips the Developer-to-Tester carry | Low | Desk run | By design: delivery supersedes a stale handoff |
| 11 | Voice summoning wired but not practically usable | Low | n/a | Out of scope |
| 12 | Conversation perch shows a summary; it does not move the character | Low | Status key in a remote mode | Deferred by design |
| 13 | Passive performance recorder was stopped by the system (low memory) before anything was captured | n/a | n/a | Restart it by hand for the headset pass |

## Recommended release configuration

Decide after the headset pass, by the rule in `FINAL_FEATURE_FLAG_DECISION.md`. Planning assumption: **Desk + Floor**, with Ceiling shipped only if it has been lowered and raised several times in the real room without surprise.

## What the owner does next

1. `powershell -ExecutionPolicy Bypass -File scripts\quest_perf_watch.ps1 -Label desk -CaptureMinutes 4`, then wear the headset and run Gate A (`QUEST_HARDWARE_VALIDATION_MATRIX.md`; step detail in `FINAL_INTERACTION_QA.md`).
2. Gate B (Floor), with `-Label floor`; step detail in `SPATIAL_WORKSPACE_DEVICE_QA.md`.
3. Gate C (Ceiling), with `-Label ceiling`. Keep a finger near Stop on the first descents.
4. Report by row number. Every reported defect goes in the defect log with this build's commit, is fixed, rebuilt, and the same step repeated.
