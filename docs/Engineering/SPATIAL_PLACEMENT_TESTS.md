# Spatial placement tests

2026-11-13. Written by the Spatial-Placement agent. **None of these tests has been run, and the test file has not been compiled.** It is fenced off until the coordinator makes two edits to the test asmdef (below). Expected results were worked out by hand from the code; a failure on first run is as likely to be a wrong expectation as a wrong implementation.

File: `Assets/MuseDesk/Tests/EditMode/SpatialPlacementTests.cs`. EditMode, NUnit, no scene, no `GameObject`. 48 tests in 7 fixtures.

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
| 1 | Valid floor and ceiling | `ValidRoom_GivesRankedCandidatesBesideTheUser`, `Evaluation_IsDeterministic`, `ValidCeiling_GivesDockInspectionPoseAndDescent` | Candidates exist, are ranked best first, sit on the floor height, 1.0 to 2.2 m away, outside the forward cone, at least 0.6 m from the feet and 0.15 m from the desk, face the user, and each passes `ValidatePose`. The same input gives the same list. The ceiling dock is not overhead, hangs from the given ceiling height, the inspection pose is 0.5 to 0.7 m to the side and not in front, and the descent distance is the real distance |
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
