# Art integration result

Branch `feature/living-world-polish`, 2026-11-15. **Editor and Play Mode only (AUTO + PLAY). Nothing here has been on a headset.** `Builds/` untouched, nothing pushed or tagged. Wall, Manager and Portal flags stay 0 in the committed `ReleaseConfig`.

## 1. What merged

`git merge --no-ff art/living-world-polish` (642ace5) into d59237c, merge commit `b6789c6`. Before merging, the diff against the common base (7f87991) was checked to touch nothing in `MainMRScene.unity`, `Scripts/Wall`, `Scripts/Spatial`, `Scripts/Agents`, backend, `ProjectSettings`, `Packages`. 283 files, mostly art (FBX, controllers, prefabs, wav, docs, Blender sources, gallery scenes).

Compile: clean (`compilationFailed` false) after the merge.

Editor re-import side effect: Unity rewrote the three `Agent_*.fbx.meta` files (32 clips per file lost `loopTime: 1`). Reverted with `git checkout` of those three files before any commit, and they did not change again after a forced refresh and recompile. Not staged. Worth knowing: if an importer pass ever rewrites them, the looping clips (Idle_Breathing, Walk_Loop, Typing ...) lose their loop flag.

## 2. Test counts (one group at a time, `scripts\tests_batch.ps1`)

| Group | Fixtures | Passed | Failed |
|---|---:|---:|---:|
| interaction | 18 | 173 | 0 |
| motion | 4 | 88 | 0 |
| wall | 8 | 168 (1 skipped, the known [Ignore]) | 0 |
| board | 4 | 69 | 0 |
| spatial | 33 | 277 | 0 (`SpatialIsolationTests` passes: 3/3) |
| real | 3 | 50 | 0 |
| ui (no group of that name; `PlanReviewLayoutTests`, `TextFitTests`, `TypeScaleTests`) | 3 | 53 | 0 |

There is no art test group.

## 3. Desk agents on the new controller

Desk agents use `MuseDesk_Agent.controller` (now 3 layers, 2 parameters) and the regenerated FBXs through their prefab variants. Play Mode, scripted demo: start, take paper, approve, completed (`ArtMergeDrive Start` / `Report` samples every desk agent's Animator each Editor frame and logs each distinct Base Layer state, by name, with the Play time). Two runs (one at 4x for the long waits, one at 1x), same result:

- Researcher: Idle_Breathing, Reading, Waiting_For_Input, Turn_Right, Walk_Loop, Offer_Document, Success, Exchange_Package, Turn_Left, Task_Completed.
- Developer: Idle_Breathing, Typing, Waiting_For_Input, Receive_Document, Turn_Right, Walk_Loop, Exchange_Package, Turn_Left, Task_Completed.
- Tester: Idle_Breathing, Inspecting, Task_Completed, Receive_Document, Success.

Every sampled state hash resolved to a state name in the controller (no unknown hash), so nothing fell through `HasState` to Idle. Console: 0 errors, no missing-state or animator warnings. The two extra layers (UpperBody, HeadGaze) stay at weight 0 and nothing drives `IsCarryingPaper`; they are inert on the desk.

Screenshots (Editor render, flat background; `ArtMergeDrive Shot`): `docs/review_screenshots/polish/art_merge_desk.png` (three-quarter from the right, Edit Mode, same camera as `desk_layout_after_right.png`), `art_merge_desk_seat.png`, `art_merge_desk_play_paper.png`, `art_merge_desk_play_exchange.png`. Compared side by side with `desk_layout_after_right.png`: same positions, same scale, no T-pose, feet on the floor (agent bottom y -0.006 for all three, heights 0.22 to 0.24 m idle), only a small expression difference on the Researcher.

One observation, not caused by the art (it predates it and the handoff code is untouched): in the scripted demo at timeScale 1 the Developer-to-Tester handoff was not seen in two runs; at 4x it was, in two. See `docs/Polish/DESK_CHOREOGRAPHY_LAYOUT.md`, V2 section.

## 4. Audio and the "Confirmed" hooks: decisions

- Audio owners stay as they are: `FeedbackController`, `StampTool`, `WallAudio`, `CeilingPodAudio` (all procedural). No Antigravity wav is wired, no Antigravity audio component is attached to any production prefab. The 12 wavs sit in `Assets/MuseDesk/Art/Audio/` unused.
- Wavs that could later serve cues we do not have yet, each only after the real event: `SFX_TaskAssign` (a backend-confirmed card assignment), `SFX_ProjectComplete` (a real completed plan or run), `SFX_CourierDepart` and `SFX_CourierArrive` (check `CeilingPodAudioCues` first, the ceiling courier may already own these). The rest (`ButtonPress`, `PaperPickup`, `StampPress`, `PortalOpen/Close`, `ElevatorArrival`, `ManagerPrinter`, `TaskFailure`) duplicate a procedural cue we already own.
- Antigravity's suggested stamp/approve "*Confirmed" visual hooks (`MusePhysicalCardFeedback.PlayApprovalConfirmed` / `PlayRejectionConfirmed`, `MuseVfxPoolPlayer` on stamp events) are **not wired**: no caller exists, and a local stamp press must not play a "confirmed" effect before the backend acknowledges (AGENTS invariant 2). Likewise `WallHabitatElevatorVisualController` fires an arrival effect whenever a cab finishes moving and `WallHabitatManagerDelegationVisualController` has no real-event trigger; neither is attached to anything in the scene.

## 5. Ceiling clearance (Desk layout V2)

Full detail and tables in `docs/Polish/DESK_CHOREOGRAPHY_LAYOUT.md` (V2 section). Summary: the Researcher's station moves a further 70 mm toward the user (+z; 50 mm reached only 0.011 m in the Ceiling), the Inbox tray and decor move with it, five lane points move. Minimum clearance at r = 0.080 m, target 0.015:

| | Desk | Floor | Ceiling | (Ceiling before) |
|---|---:|---:|---:|---:|
| worst route | 0.018 | 0.018 | 0.017 | -0.049 |

All seven routes pass in all three modes. Play trace: Researcher min 0.017, Developer 0.019. Outer-desk overhang in Floor and Ceiling (4.0 / 2.9 and 6.0 / 4.9 cm) is **not** reduced: an inward shift closes the aisle or the Developer-Tester gap. Delivery spot unchanged (0.52 m from the eye). `Revert` checked.

## 6. Wall art hookup: not done, on purpose

The 24 Wall prefabs do not fit the stage geometry, so `HabitatArtManifest` stays empty (`artVersion` `proxy-0`), nothing was changed in `Scripts/Wall` or `Scripts/Spatial`, and no rollback switch was needed. The tool the manifest code names, `AgentScripts/WallArtFitCheck.cs`, now exists (`scripts\drv.ps1 WallArtFitCheck Report Full|Compact`, read-only, Edit Mode). It loads `WallHabitat_Complete_3Tier` / `_2Tier`, finds the sockets that correspond to the layout's, and prints them relative to the prefab's own `WallContact` next to `HabitatLayoutBuilder`'s numbers (habitat-local metres, tolerance 1 cm). Result for the Full layout (Compact is the same story):

| Socket (layout <- art) | Layout | Art | Distance |
|---|---|---|---:|
| ResearchCubicleSocket | (-0.352, -0.400, 0.092) | (0.345, -0.345, 0.015) | 0.703 m |
| DeveloperCubicleSocket | (-0.065, -0.400, 0.092) | (0.115, -0.345, 0.015) | 0.204 m |
| TesterCubicleSocket <- TestingCubicleSocket | (0.222, -0.400, 0.092) | art socket missing in the composite | - |
| ManagerOfficeSocket <- ManagerLookoutSocket | (0.215, 0.150, 0.092) | missing in the composite | - |
| ElevatorCarSocket | (0.440, -0.400, 0.092) | (-0.345, -0.333, 0.185) | 0.793 m |
| ElevatorStop_Lower / _Mid / _Manager | x 0.440, y -0.400 / -0.110 / 0.150 | x -0.268, y -0.333 / -0.103 / 0.129 | 0.71 m |
| TaskPrinterSocket | (-0.230, 0.200, 0.092) | (-0.095, 0.171, 0.110) | 0.139 m |
| AttentionBeaconSocket | (-0.250, 0.410, 0.130) | (-0.440, -0.337, 0.110) | 0.771 m |
| CourierDispatchSocket | (-0.455, -0.400, 0.210) | (-0.440, -0.339, 0.175) | 0.072 m |

Overall size: layout 1.050 x 0.900 x 0.260 m, art 0.975 x 0.794 x 0.267 m (Compact 0.740 x 0.620 x 0.240 against 0.770 x 0.564 x 0.267). The art puts the elevator shaft at the **left** (x about -0.27) and the cubicles on the right; the stage layout puts the shaft at the **right** end (x +0.44) and the cubicles on the left. After removing the best single translation the worst remaining socket error is 0.94 m (Full) and 0.64 m (Compact), so it is a different, mirrored layout, not an offset. Floor heights are 5 to 7 cm off even before that. Moving walkers and the elevator onto it would break the validated clearance (5 mm) and the rider checks.

Two ways forward, both outside this brief: regenerate the art against `HabitatLayoutBuilder` (mirror in x, 1.05 x 0.90 x 0.26, shaft at x 0.44, stops at y -0.400 / -0.110 / 0.150, cubicle sockets at x -0.352 / -0.065 / 0.222, and name the sockets `TesterCubicleSocket` and `ManagerOfficeSocket`), or change the stage layout to the art's and re-run `HabitatLayoutValidator` and the Living Office drive. The art's small stand-alone pieces (task cards, desk-visit perch, large portal, selection halo, placement ghost) were not evaluated for replacing proxies; they are not part of the walker/elevator geometry.

Verification that the merge did not disturb the Wall: `LivingOfficeDrive.RunOffline`, PLAY: **13 of 13 steps, 0 failed, 0 console errors**; rides arrived 8 / failed 0 / cancelled 0 / violations 0; riders not attached to the cabin 0 frames; doorway frames with doors not fully open 0; avatar scale preserved. Screenshot of the proxy habitat during the run, after the merge: `docs/review_screenshots/wall/living_office_art_merge_proxy.png` (named for what it shows: proxies, not art; there is no `living_office_art.png` because no art was hooked up).

## 7. Files

Created: `AgentScripts/ArtMergeDrive.cs` (Play: `Start`, `Report`, `Stop`, `Shot`, `Heights`), `AgentScripts/DeskV2Probe.cs` (Play scratch probe, unsaved), `AgentScripts/WallArtFitCheck.cs`. Changed: `AgentScripts/DeskChoreographyLayoutV1.cs` and `.record.txt` (V2 tables, record top-up for `InboxTray`), `Assets/MuseDesk/Scenes/MainMRScene.unity` (via the builder only), `docs/Polish/DESK_CHOREOGRAPHY_LAYOUT.md`, `docs/Engineering/PARALLEL_SPRINT_INTEGRATION_MAP.md`, this file.

## 8. Needs the headset

1. Desk agents: do the 77-clip animations look right at seat distance (nothing popping or sliding on the new Turn and Walk clips)?
2. Ceiling pod: the Researcher's walk behind its desk past the arch post, and the desk now 7 cm nearer the user; the Inbox tray on the pod's deck edge.
3. Floor and Ceiling: the outer desks still overhang the deck by 3 to 6 cm.
4. Whether the scripted-demo Developer-to-Tester handoff reads well at normal speed (it plays in PLAY; see section 9).
5. Everything about the Wall art is untested on device and unhooked.

## 9. Desk demo handoff and the loop-flag guard

**Desk demo handoff (fixed).** The scripted demo emits `artifact.created` about 1.2 s before `task.completed`. At timeScale 1 the Researcher to Developer exchange is still running then, so the Developer to Tester handoff was only queued, and `CompletionSequence.OnWorkDelivered` called `AgentHandoffController.AbandonPending()`, which cleared the queue: a handoff the event stream really reported was never shown (at 4x the first one had finished, so the second played). Now `OnWorkDelivered` does not drop it; the completion sequence waits for the whole chain of reported handoffs (new `handoffChainWaitMax`, 15 s, pure rule in `Agents/Core/HandoffWaitPolicy.cs`) and only retires what is still unfinished after that. Backend and reducer state are untouched; only the celebration starts later, and no handoff is invented. The demo timeline was not changed.

PLAY, three consecutive desk demo runs at timeScale 1 (`DeskDemoHandoffDrive` trace): Developer to Tester Approaching, Exchanging and Returning in every run; the completion sequence then ran (WaitHandoff, Success, done); celebration started 3.95 s, 3.99 s and 3.98 s after `task.completed` (handoff chain finished first); 0 console errors. The scene file gained one line, the serialized `handoffChainWaitMax: 15`.

**Loop-flag guard.** `AgentClipLoopFlagTests` (Editor-only `MuseDesk.Tests.AgentInteraction`) reads `ModelImporter.clipAnimations` of Agent_Researcher, Agent_Developer and Agent_Tester (Agent_Manager only if its clips exist) and fails if any of 17 looping clips (Idle_Breathing, Walk_Loop, Typing, Reading, Inspecting, Waiting_For_Input, Thinking and ten more from `LoopingCharacterClips`) lost `loopTime`. If it fails after a reimport, `git checkout -- <Agent_*.fbx.meta>`.

Tests: AgentClipLoopFlagTests 5/5, HandoffWaitPolicyTests 4/4, DeskPolishLogicTests 22/22, AgentWalkPathTests 12/12. New driver: `AgentScripts/DeskDemoHandoffDrive.cs` (`Start`, `Report`, `Stop`).
