# Spatial placement tests

2026-11-13. Written by the Spatial-Placement agent. **None of these tests has been run, and the test file has not been compiled.** It is fenced off until the coordinator makes two edits to the test asmdef (below). Expected results were worked out by hand from the code; a failure on first run is as likely to be a wrong expectation as a wrong implementation.

File: `Assets/MuseDesk/Tests/EditMode/SpatialPlacementTests.cs`. EditMode, NUnit, no scene, no `GameObject`. **84 tests in 9 fixtures as of safety pass 2** (48 original, 19 added by safety pass 1 in `SpatialCeilingSweptPathTests`, 17 added by safety pass 2 in `SpatialSweptMovesTests`). The opening paragraph above is the original author's and is out of date: the coordinator has since run the suite in Unity. What follows in "Safety pass 2" is the current state of the newest tests.

## Safety pass 2 (2026-11-13, Spatial-Safety-2 agent)

**Not run in Unity by this agent.** The Core sources and this file were compiled outside Unity against stand-in math and NUnit types (the harness of pass 1). Result of the last run there: 84 of 84. One later edit to `SpatialDirectorCore` (the previewed ceiling dock is kept apart from the confirmed one) compiled with 0 errors, but the rebuilt test binary was blocked by the machine's application control policy and was not run. Expect the first Unity run to be the real evidence.

Two existing tests were changed, both because they encoded behaviour that is now refused:

- `UserKeepOut_IsNeverEntered`, part 4: it asserted that a path straight across the user with the office base 0.2 m above the eyes is accepted. It is now refused (`userOverheadRadius`); the test asserts that, and the boundary of the new radius.
- `Restart_WithAShiftedTrackingOrigin_StillLandsOnTheSameRoomSpot`: the fixture moved the user 1 m but left the desk office behind, so the (then unswept) move passed through the user. The desk office now moves with the user.

New tests, fixture `SpatialSweptMovesTests`:

| Test | What it shows |
|---|---|
| `FirstTripToTheDock_IsSwept_RisesBeforeItTravels_AndTheMoverTravelsExactlyThat` | The route is desk, straight above the desk at eyes + 0.25 m, dock. Every pose written lies on it (1 mm). Below that height the office never turns and never moves sideways |
| `FirstTripToTheDock_Blocked_IsRefusedWithWords_AndNothingMoves` | A box over the desk office: Confirm is refused naming it and saying "Next", no write, Desk mode, pod off, preview still open |
| `DeskStartRule_Boundaries` | The rise from the desk pose is refused as an ordinary path and accepted as a desk path, in both directions; the approach allowance at 0.04 and 0.06 m; the region bound; no re-entry once out; a screen and a desk top the office stands against are excused for leaving and refused for approaching or sinking |
| `FloorMove_StraightWhenClear_UpAndOverWhenTheDeskIsInTheWay` | Empty room: 2-pose route. Test office: the straight sweep is blocked by TABLE and a 4-pose route at 1.45 m is used; the way back likewise; traces on the routes |
| `FloorMove_BlockedBothWays_IsRefusedWithWords_LowObstacleIsCrossedOver` | A bin between is crossed over; a 2.2 m cabinet is a refusal naming it, nothing moves; after a room event every offered spot is reachable |
| `WayBackToTheDesk_Blocked_IsRefused_AndTheOfficeStays` | Refused with "press Desk again", Floor mode kept |
| `Descent_IsRevalidated_ForWhereTheUserSitsNow_AndRefusedWhenBlocked` | Chair moved 0.3 m: new inspection pose, same dock, trace on the fresh path. A lamp added without a room event: refused naming it. A room event while docked reports it; clearing the room clears the words |
| `Recenter_WithADockThatNoLongerValidates_NeverMovesTheOffice` | Mid-descent, the reference moves 1 m: zero writes afterwards, needs confirmation, not saved, pod and director refuse to move it; Desk is a swept route or a refusal |
| `Recenter_InFloorMode_WithASpotThatNoLongerValidates_HoldsPosition` | At rest and mid-move: zero writes, position kept, needs confirmation |
| `Restore_WithABlockedWay_StaysAtTheDesk` | A saved spot that is itself clear but cannot be reached is refused |
| `Stop_DuringDescent_GoesBackUpAlongTheTravelledPath_AndEndsDocked` | State sequence, every pose on the path, height never decreases after the stop; "Send back" during a descent is the same stop; "Bring" during a retraction is refused; Stop during a retraction does nothing |
| `Stop_DuringADeskToFloorMove_ReturnsToTheDeskPose_AndTheModeStaysDesk` | Halts without a jump, ends bit-exact on the desk pose, Desk mode, nothing saved; also Floor to Desk stopped returns to the floor spot with its record intact |
| `Stop_DuringTheFirstTripUp_ReturnsToTheDesk_AndLeavesCeilingMode` | Travelling and during the wind-up |
| `Stop_IsSafeAndIdempotent_InEveryPhase` | Desk, preview, waiting for agents, Floor at rest, docked, both wind-ups, engaged; repeated presses |
| `Stop_WhileAPaperIsHeld_HaltsInPlace_AndGoesBackOnlyWhenItIsPutDown` | Mode change and pod: no write while held, then back |
| `TransitionMachine_ReversesAMultiLegRoute_Deterministically` | Off-path start refused; reverse on the second leg returns bit-exact; same inputs, same poses; a full route ends on its last pose |
| `Mast_AndTheSpaceUnderTheCeiling_AreSwept` | A box hanging above the docked office; a box on the mast line (`blockedByMast`); a control beside it; a box at the first dock's mount removes that dock and fails `ValidateDock` |

Expectations trusted least, in this order:

1. `Descent_IsRevalidated...`: the fresh inspection pose is asserted to be exactly 0.3 m further back. True only if the same search option wins for the moved user.
2. `FloorMove_...` tests assert the route has exactly 2 or 4 poses and a transit height of exactly 1.45 m; they depend on which floor candidate ranks first and on the 2.6 m ceiling not lowering the transit.
3. `Stop_DuringDescent...` and `Stop_DuringTheFirstTripUp...` use fixed times (2.0 s, 2.5 s, 1.2 s) to land in a particular state; a different travel time moves them.
4. `DeskStartRule_Boundaries`: the 0.04 / 0.06 m approach cases are 1 cm either side of the limit; exact-equality cases (bit-exact desk pose) rely on Unity's `Quaternion.Euler(0, 180, 0)` round-tripping through the mover unchanged, as it does in the stand-in.
5. Anything comparing with `Vector3.Equals`: exact in the stand-in; Unity's `Vector3.Equals` is also exact, `==` is not used.

Not covered by any test: the scene-side scripts (`SpatialModeStrip` showing the Stop key, `SpatialWorkspaceDirector`), a room event during a move, a recenter with no room reference in Ceiling mode, `returnToDeskWithoutRoom`, leaving Ceiling mode from the dock with an obstacle, and Ceiling to Ceiling re-docking.

## How to switch them on

The test assembly cannot reference `Assembly-CSharp`. The logic under test is therefore in its own assembly, `MuseDesk.Spatial.Core`, and the tests use the real types (no mirrors, no reflection).

In `Assets/MuseDesk/Tests/EditMode/MuseDesk.Tests.EditMode.asmdef`:

1. add `"MuseDesk.Spatial.Core"` to `"references"`;
2. add `{ "name": "com.meta.xr.mrutilitykit", "expression": "", "define": "MUSEDESK_SPATIAL_TESTS" }` to `"versionDefines"`, or add `MUSEDESK_SPATIAL_TESTS` to the Editor's Scripting Define Symbols.

Until both are done the file compiles to nothing and the existing suite is unaffected.

## The rig

`SpatialRig` builds the real `WorkspaceTransitionMachine`, `CeilingPodMachine` and `SpatialDirectorCore` around:

- a `ManualClock` advanced in 50 ms steps by `Run(seconds)`;
- a `RootPose` variable standing in for `WorkspaceRoot`, with a counter of writes;
- three bools standing in for `SpatialWorkspaceState.TransitionHeld`, "agents are walking" and "the desk placement has finished";
- a fixed seated user at (0, 1.2, 0) looking along +Z.

The standard room (`SpatialRooms.Office`) is 5 x 5 m, floor at 0, ceiling at 2.6 m, with a 1.4 x 0.7 x 0.75 m desk in front of the user.

## The ten fixtures of the assignment

| # | Fixture | Tests | What they prove |
|---|---|---|---|
| 1 | Valid floor and ceiling | `ValidRoom_GivesRankedCandidatesBesideTheUser`, `Evaluation_IsDeterministic`, `ValidCeiling_GivesDockInspectionPoseAndDescent` | Candidates exist, are ranked best first, sit on the floor height, 1.0 to 2.2 m away, outside the forward cone, at least 0.6 m from the feet and 0.15 m from the desk, face the user, and each passes `ValidatePose`. The same input gives the same list. The ceiling dock is not overhead, hangs from the given ceiling height, the inspection pose is 0.5 to 0.9 m to the side and not in front, and the descent distance is the real distance |
| 2 | Floor but no ceiling | `NoCeilingData_FailsWithTheReason_AndNeverGuessesAHeight`, `NoCeiling_CeilingModeIsRefused_FloorStillWorks` | The ceiling evaluator fails with "no ceiling" and the director refuses Ceiling mode in words, while Floor mode still opens |
| 3 | Small room, insufficient floor | `SmallRoom_GivesNoCandidates_AndSaysWhy`, `SmallRoom_OffersManualPlacement_AndSaysItIsUnchecked` | No candidate is returned, every generated position is accounted for by a rejection reason, and the director offers manual placement saying the spot is not checked |
| 4 | Furniture near candidates | `FurnitureOnTheBestSpot_MovesTheCandidatesAway`, `TallFurnitureBetweenUserAndSpot_HidesThatSide`, `BlockedDescent_UsesTheOtherSide` | A box on the best spot removes it and nothing is offered within clearance of it. A tall partition removes every candidate behind it (line of sight). A shelf beside the user moves the ceiling dock to the other side |
| 5 | Permission denied | `NoRoomData_NeverYieldsADetectedFloor`, `NoRoomData_OffersManualPlacement_InWords` | No position is even tried; the preview is not marked detected; the words contain "place it by hand" and not "Detected"; Ceiling mode is refused |
| 6 | Missing room data | the same two tests (they loop over `NotLoadedYet`, `NoRoomSetup`, `InvalidFloor`, and a null room) | As 5 |
| 7 | Recenter during deployment | `Recenter_DuringDescent_EndsEngagedAtTheNewPose`, `Recenter_DuringRetraction_EndsDocked`, `Recenter_InFloorMode_FollowsTheRoomReference`, `Recenter_DuringAMove_EndsTheMoveSafely` | A recenter in the middle of a descent ends with the pod `Engaged`, nothing moving, the root at the inspection pose, the dock moved with the room reference, and the pod still able to go back up. In Floor mode the root follows the room reference |
| 8 | Retraction with an active document | `Retract_WaitsWhileHeld_ThenGoesUp`, `Descend_IsRefusedWhileHeld`, `Held_RefusesToStart_AndLeavesTheRootAlone`, `Held_BlocksConfirm_AndTheReturnToDesk` | While the hold is set the pod stays `Engaged`, the root is not written once, the status mentions the paper; after release it retracts to the dock. A hold also blocks Confirm, the return to Desk and any `TryMove` |
| 9 | Backend update during movement | `CoreAssembly_HoldsNoReferenceToGatewaysOrReducers`, `SceneSideScripts_NameNoGatewayReducerOrSessionType`, `AMove_RunsToCompletionOnItsOwnInputsOnly` | The Core assembly does not reference `Assembly-CSharp` and no field, parameter or return type in it comes from the networking, Phase 2, models or documents namespaces. The scene-side placement scripts do not contain the names `SessionController`, `Reducer`, `Gateway`, `Phase2`, `MuseDesk.Networking` or `MuseDesk.Models` (a source scan). A move completes from the clock alone |
| 10 | Restart with saved placement, including invalid | `Restart_RestoresTheFloorPlacement`, `Restart_WithAShiftedTrackingOrigin_StillLandsOnTheSameRoomSpot`, `InvalidSavedPlacement_FallsBackToDesk`, `CorruptOrForeignRecords_AreRefused`, `Restart_RestoresTheCeilingDock` | A saved Floor or Ceiling placement is restored through JSON into a fresh director; it lands on the same room spot when the room sits elsewhere in world space. Furniture on the spot, another room id, a smaller room, no room data, the flag off, or a held paper each leave the office in Desk with zero root writes. Unreadable JSON, another build and another version are refused. Saving "nothing" deletes the record |

## The other tests

| Test | Proves |
|---|---|
| `FlagsOff_NothingHappens` | With both flags false every request returns false, the root is never written, and the director raises no change event |
| `OnlyTheEnabledModeCanBeRequested` | Each flag gates its own mode |
| `Floor_PreviewDoesNotMove_ConfirmDoes_DeskRestoresExactly` | Cycling candidates and waiting in the preview write nothing; Confirm moves; a second request during the move is refused; Desk returns to the bit-identical position, rotation and scale |
| `Cancel_LeavesEverythingAsItWas` | Cancel ends the preview with no write |
| `NudgedOffTheFloor_IsNotCalledDetected_AndCannotBeConfirmed` | A detected spot nudged off the floor becomes invalid and cannot be confirmed; reset restores it |
| `BusyAgentsBeyondTheBound_KeepTheOldMode` | When agents stay busy past the bound the mode reverts and the root was never written |
| `DeskNotPlacedYet_RefusesToLeaveDesk` | No preview before the desk placement has finished |
| `RoomChangeDuringPreview_RebuildsThePreview` | A room event during a preview replaces the stale spot |
| `Pause_DuringAMove_LeavesNothingMidAir`, `Pause_DuringDescent_EndsEngaged`, `FinishNow_PutsTheRootOnItsTarget` | App pause ends travel at its destination |
| `Move_EasesToTheExactTarget_AndRefusesASecondMove` | One move at a time; ends exactly on target, including scale |
| `BusyAgents_DelayTheMove_ThenItRuns`, `BusyAgents_BeyondTheBound_GiveTheMoveUp` | The agent wait and its bound |
| `FullCycle_VisitsEveryStateInOrder` | The eight pod states in order for one descent and one retraction |
| `BusyAgentsDuringDescent_RecoverToTheDock` | A descent that cannot start goes to `ErrorRecovery` without moving the root, then back to `Docked` |
| `LeavingCeilingMode_ReturnsToTheExactDeskPose` | Ceiling to Desk restores the desk pose and switches the pod off |
| `NarrowCeiling_RejectsDocksNearWalls`, `LowCeiling_IsRefused` | Wall margin and minimum ceiling height |
| `ScaleOptions_AreLimitedToTheSafeRange` | Scales outside 0.8 to 1.5 are never returned |

## What these tests do NOT prove

About real rooms:

- That MRUK delivers floors, ceilings and furniture the way `MrukRoomGeometry` assumes. `MrukRoomGeometry` has no test: it cannot be built without MRUK scene data. In particular untested: that boundary points are in the anchor's local XY plane on a device, that one local axis of every volume is near vertical, that plane-only furniture can be ignored, and what a room with several floors looks like.
- That a rectangle is a fair stand-in for a room. Every synthetic room is an axis-aligned rectangle with box furniture. Concave rooms, sloped ceilings, door openings, half-scanned rooms and furniture MRUK did not label are not represented. The concave-outline check in `RectInsidePolygon` has no test.
- That the chosen spots are good. The tests prove the rules are applied, not that 1.0 to 2.2 m, a 35 degree cone or a 15 to 50 degree look-down are comfortable.
- That the seated user is where the tests put them. The user's feet are taken as straight below the head; a chair, the user's legs and the desk the user actually sits at are only avoided if MRUK reports them.
- Anything about availability reasons on a device: permission denied, no room setup and "not loaded yet" are forced by a flag in the synthetic room, not produced by MRUK.

About the scene:

- No `MonoBehaviour` is tested: the director's wiring, flag handling in `Update`, publishing to `SpatialWorkspaceState`, the stand-down of `WorkspacePlacement`, the ownership guard, the ghost, the mode strip, the pod visuals, and PlayerPrefs storage.
- "Agents are busy" is a bool. That `AgentHandoffController.AnyBusy` and `AgentMovement.IsMoving` cover every case of a walking agent is the audit's claim, not a tested one.
- The transition hold is a bool. That the reach side acquires and releases it at the right moments is covered by its own tests, not these.
- Recenter is simulated by changing the synthetic room's reference pose and calling `OnRecenter()`. Whether Quest raises `RecenteredPose`, whether MRUK's anchors have their new poses two frames later, and whether a room reference survives a recenter at all, are untested.
- Restart is simulated by building a second director and passing the JSON through a memory store. Whether the room anchor UUID and floor anchor pose are the same in the next session on a device is untested.
- Fixture 9's source scan looks for names in text. It would not catch a backend dependency reached through a type with an innocent name.
- Timing. Durations are asserted only as "finished within N seconds" against a manual clock. Nothing is known about frame pacing or cost on the device; the evaluators allocate only when a preview opens or the room changes, which is by reading, not by measurement.

## Play Mode, by hand or from a driver

With the builder applied (`BuildSpatialPlacementV1.Apply`), on the `Spatial` object's `SpatialWorkspaceDirector`:

1. `SetFlags(true, true)`
2. `UseSyntheticTestRoom()` (a 4 x 4 m room around the camera with a desk in front; clearly labelled synthetic)
3. `RequestFloor()`, then read `StatusText`, `IsPreviewing`, `Core.CandidateCount`
4. `NextCandidate()`, `NudgeRight()`, `RotateLeft()`, `ResetPlacement()`
5. `ConfirmPlacement()`, wait about 3 s, expect `Mode == Floor`, `SpatialWorkspaceState.IsRemote`, `!IsTransitioning`
6. `RequestDesk()`, wait, expect the root back at its start pose
7. `RequestCeiling()`, `ConfirmPlacement()`, wait about 6 s (`Core.Pod.State == Docked`), `BringPodDown()`, wait about 5 s (`Engaged`), `SendPodUp()`, wait (`Docked`), `RequestDesk()`
8. Hold test: `SpatialWorkspaceState.AcquireTransitionHold()` while `Engaged`, `SendPodUp()`, expect it to stay `Engaged` with `Core.Pod.StatusText` mentioning the paper; `ReleaseTransitionHold()`, expect `Docked`
9. `SetFlags(false, false)`: expect Desk, keys hidden

Useful reads: `director.Mode`, `director.StatusText`, `director.Core.Phase`, `director.Core.PreviewPose`, `director.Core.Pod.State`, `director.Core.Mover.Phase`, `director.Core.LastFloorReport.Summary()`.

In the Editor the desk placement takes up to 4 s to finish (no headset is worn), and requests before that are refused with "The office is still being placed".
