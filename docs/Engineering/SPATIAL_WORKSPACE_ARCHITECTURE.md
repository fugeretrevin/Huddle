# Spatial workspace: placement side architecture

2026-11-13. Written by the Spatial-Placement agent. Status: **code written; never run; never seen on a headset.** The agent could not compile. The only compile evidence is indirect: the open Editor rebuilt `Library/ScriptAssemblies/MuseDesk.Spatial.Core.dll` and `Assembly-CSharp.dll` after the runtime files were written (see section 11). The tests and the builder have not been compiled at all.

The spatial workspace is optional and flag-gated. It lets the miniature office leave the desk: onto the detected floor beside the seated user (Floor mode) or to a dock under the real ceiling from which it descends beside the user (Ceiling mode). This document covers the placement half: deciding where the office goes and moving it. The reach half (attention cues, bringing a paper to the user) is in `SPATIAL_WORKSPACE_INTEGRATION.md`. The two halves share only `SpatialWorkspaceState.cs` and the static events in `SummonRequests.cs`.

Placement is presentation only. No file here references the session, a gateway or a reducer, so a mode change cannot start, cancel or reset work. A test asserts that (section 9).

## 1. What is gated

`SpatialWorkspaceDirector.flags` holds `floorModeEnabled` and `ceilingModeEnabled`. **Both are false by default** and the builder leaves them false.

With both false:

- the director publishes Desk state and the root once in `OnEnable`, lays the views out as hidden once in `Start`, and returns at the top of every `Update` after two bool compares;
- `MrukRoomGeometry` is a disabled component: it does not subscribe to MRUK;
- the mode keys and status plate are hidden by scale and the strip's `LateUpdate` returns at once;
- the ghost and the pod visuals are inactive;
- `WorkspaceTransitionController` never writes the root, and `WorkspacePlacement` is not touched;
- every request (`RequestMode(Floor)`, `RequestMode(Ceiling)`, `Confirm`, `BringPodDown`, the `SummonRequests` events) returns without doing anything.

Each flag gates its own mode: with only Floor on, a Ceiling request does nothing. Switching off the flag of the active mode returns the office to Desk.

`allowScaleChange` (false by default) is a third switch: off, the office always keeps scale 1; on, the floor search also tries `floorSettings.scaleOptions`, limited to `minScale`..`maxScale` (0.8 to 1.5).

## 2. Assemblies

All MuseDesk runtime code is in `Assembly-CSharp` (no asmdef), and MRUK's assembly (`meta.xr.mrutilitykit`, `autoReferenced: true`) is visible from it, as `WorkspacePlacement.cs` already shows. The EditMode test assembly cannot reference `Assembly-CSharp`.

So the logic that needs tests lives in a new assembly, **`MuseDesk.Spatial.Core`** (`Assets/MuseDesk/Scripts/Spatial/Core/MuseDesk.Spatial.Core.asmdef`, `autoReferenced: true`, no references, UnityEngine types only). `Assembly-CSharp` sees it automatically. It cannot see `Assembly-CSharp`, so it mirrors the contract's `WorkspaceMode` as `PlacementMode` (same integer values) and takes `TransitionHeld` as a delegate.

If the asmdef ever causes trouble, deleting that one file (and its `.meta`) folds the Core sources into `Assembly-CSharp` with no source change; only the tests lose their reference.

## 3. Components

Core assembly (`Scripts/Spatial/Core/`, plain C#, no scene access):

| File | What it is |
|---|---|
| `SpatialRoomGeometry.cs` | `IRoomGeometry`, `RoomAvailability`, `RoomPlane`, `FurnitureBox`, `SeatedUser`, `RootPose`, `OrientedRect`, `SpatialGeometry`, `SyntheticRoomGeometry`, `SpatialFeatureFlags`, `PlacementMode` |
| `FloorPlacementEvaluator.cs` | Floor candidates, ranking, `ValidatePose`; `FloorPlacementSettings`, `FloorCandidate`, `FloorEvaluationReport` |
| `CeilingPlacementEvaluator.cs` | Ceiling dock, inspection pose, descent check, `ValidateDock`; `CeilingPlacementSettings`, `CeilingPlacement` |
| `WorkspaceTransitionMachine.cs` | The one state machine that moves the root; `ISpatialClock`, `ManualClock`, `UnityUnscaledClock` |
| `CeilingPodMachine.cs` | The pod state machine |
| `WorkspacePlacementCodec.cs` | `SavedPlacement`, `IPlacementStore` (+ memory and PlayerPrefs stores), load / save / resolve |
| `SpatialDirectorCore.cs` | Modes, preview-then-confirm, flag gating, recenter and pause, restore |

Scene side (`Scripts/Spatial/`, in `Assembly-CSharp`):

| File | What it is |
|---|---|
| `MrukRoomGeometry.cs` | `IRoomGeometry` over MRUK. **The only file that touches MRUK** |
| `RoomPlacementService.cs` | Owns the room source, swaps in a synthetic room, keeps a summary in words |
| `WorkspaceTransitionController.cs` | Wraps the transition machine around `WorkspaceRoot`; agent-busy check; ownership guard |
| `CeilingPodController.cs` | Wraps the pod machine; stretches the mast, places the mount and the shelf |
| `SpatialWorkspaceDirector.cs` | Flags, wiring, the public API, the only writer of `SpatialWorkspaceState` |
| `WorkspaceGhostPreview.cs` | The teal footprint outline with a facing arrow |
| `SpatialModeStrip.cs` | The contextual mode keys and status line |
| `WorkspacePlacementPersistence.cs` | PlayerPrefs store for the saved placement |
| `WorkspacePlacementDebugView.cs` | Gizmo view of room, candidates and descent path; off by default; Editor only |

Builder: `AgentScripts/BuildSpatialPlacementV1.cs`. Tests: `Assets/MuseDesk/Tests/EditMode/SpatialPlacementTests.cs`.

A deviation from the assignment, stated plainly: `RoomPlacementService` caches a room **summary**, not the candidate list. Candidates depend on where the user sits, so they are computed in `SpatialDirectorCore` when a preview is opened and again when the room raises an event during a preview. Nothing is evaluated per frame either way.

## 4. Who owns the root transform

| Situation | Writer of `WorkspaceRoot` |
|---|---|
| Desk mode, at rest | `WorkspacePlacement`, exactly as before |
| A mode change in flight (either direction) | `WorkspaceTransitionController` only |
| Floor mode | `WorkspaceTransitionController` only |
| Ceiling mode, including every pod move | `WorkspaceTransitionController` only (the pod asks it) |

### The hand-over with `WorkspacePlacement`, with no edit to its file

Read from `Workspace/WorkspacePlacement.cs`, off-desk mode (the mode in use):

- `PlaceOffDeskRoutine` writes the root once, at start-up (`:120-123`), then sets `State = PlacedManual` (`:126`).
- After that the only write path is `OnReplaceRequested` (`:167-172`), on `HMDMounted` or `RecenteredPose`, and it returns unless `autoReplaceOnRecenter` is true (`:170`).

So the director does two things, both through public members:

1. It refuses to leave Desk until `deskPlacement.State` is `PlacedManual` or `PlacedFromTable`.
2. While the office is away it sets the public field `autoReplaceOnRecenter` to false and restores the saved value when the office is back in Desk and at rest.

Disabling the component was rejected: re-enabling it runs `OnEnable`, which re-places the office from the current head pose (`:70-75`), and Desk must be restored exactly.

What this does not cover: `WorkspacePlacement.ResetWorkspaceInFront()` and `ConfirmManualPlacement()` are public and write the root. Only `AppBootstrap.OnResetWorkspacePressed` / `OnConfirmPlacementPressed` call them, and no key is known to be wired to those today. If one is pressed during a spatial mode, the ownership guard in `WorkspaceTransitionController.LateUpdate` puts the root back and logs one warning. Edit 1 in section 10 would close the gap properly.

MRUK world lock is not touched anywhere.

### Desk is restored exactly

On the first Confirm out of Desk the core stores the root's world pose. `RequestMode(Desk)` eases back to it and the last write is that stored pose, bit for bit. Tests assert equality, not closeness. After a recenter in a spatial mode the stored desk pose is a stale world pose, the same as the desk itself would be with `autoReplaceOnRecenter` off.

## 5. Audit: what fights a moving or scaled root

Agents are children of their stations, which are children of the root. At rest they move with it. Walking is different.

| Script | Stores | Root moves mid-walk | Root scaled |
|---|---|---|---|
| `AgentMovement` | Home as **local** pose (`:17-18`, `:41-42`); walk start as **world** `_startWorld` (`:26`, `:49`, `:64`, `:73`); target read live from `_dest.position` or `_parent.TransformPoint(_homeLocal)` (`:84-86`) | The lerp runs from a stale world start to a live target: the agent trails behind the office and catches up as `k` reaches 1, then is exact (`:111`, `:116-117`). Ugly, self-healing | Walk bob is world metres (`:89`), negligible |
| `AgentMovement.DeliverInFrontOfUser` | World `_destPos` (`:61-63`) | Walks to a point that no longer relates to the office. Only used when `AgentRegistry.deliveryAnchor` is null | Same |
| `AgentHandoffController` | `HandoffDest` is a **scene-root** object at a world position (`:170-176`), `stopDistance` in world metres (`:28`, `:175`) | The courier walks to where the receiver used to be, plays the exchange there, then walks home correctly | At scale 1.5 the courier stops 12 cm (office units) short instead of 18; at 0.8 it stops further away |
| `AgentRegistry` | `deliveryAnchor` is a child of the root; coroutines wait on `IsMoving` | Follows the anchor live; inherits the `AgentMovement` trail | Fine |
| `RubberBandReturn`, `StampTool`, `PushableProp`, `DelayedPhysics` | Local homes | Fine | Fine |
| `HeldPaperController`, `DocumentDeliveryController` | World-space motion of the paper while carried, held or filed | A taken paper is a scene root and stays with the user (intended). The filing glide reads `fileTarget` live | Covered by the reach side |
| Non-kinematic rigidbodies under the root | Physics pose | Teleported with the transform; velocities kept. Not seen on a headset | Unknown |

Conclusion: moving the root with children is safe when every agent is standing, and not safe mid-walk. So `WorkspaceTransitionController.AgentsBusy()` is true while `AgentHandoffController.AnyBusy` or any cached `AgentMovement.IsMoving`, and the transition machine **waits** in `WaitingForAgents` before it moves anything. The wait is bounded (`maxAgentWaitSeconds`, 6 s). Past the bound the move is given up, the root has not moved, the mode goes back to what it was, and the status line says to try again. An agent that starts walking after the move began is not stopped: that walk shows the trail described above.

Scale is the reason `allowScaleChange` is off by default: nothing in the scene has been run at a root scale other than 1.

## 6. State machines

Both are driven by `Tick()` and an injected clock: no coroutines, no `Time` in the logic.

### Transition (`WorkspaceTransitionMachine`)

```
Idle --TryMove--> WaitingForAgents --agents idle--> Moving --done--> Idle
          |              |
          |              +--bound exceeded--> Idle (MoveFinished(false), root untouched)
          +--agents idle already--> Moving
```

- `TryMove` returns `AlreadyRunning`, `Held` (transition hold set), `AlreadyThere` or `Started`.
- Moving eases position, rotation and uniform scale with smoothstep and writes the exact target last.
- `FinishNow()` (app pause): a running move lands on its target at once; a waiting one is dropped.
- `Snap(pose)` (recenter): write a pose now and end any move.
- A hold acquired after a move has started does not stop it. Stopping in mid-air seemed worse.

### Ceiling pod (`CeilingPodMachine`)

```
Docked -> PreparingToDescend -> Descending -> Stabilizing -> Engaged
Engaged -> PreparingToRetract -> Retracting -> ReturningToDock -> Docked
any travelling state -> ErrorRecovery -> (Retracting ->) Docked
```

- Every move goes through the transition machine, so the hold and the agent wait apply.
- `RequestDescend` is refused while held. `RequestRetract` while engaged is accepted and **waits** while held (`RetractPending`, status "Waiting to go up: put the paper down first"); the hold is checked again at the end of the wind-up.
- A move that cannot start within `startTimeoutSeconds`, or that was given up, leads to `ErrorRecovery`: up to three eased trips back to the dock, then a snap to the dock, never while held, and waiting up to 20 s for agents before the snap.
- `OnRecenter(dock, inspection)`: travel ends; the office is put beside the user if it was coming down or engaged, at the dock otherwise.
- `OnPause()`: travel ends at its destination.
- Entering Ceiling mode from Desk uses the same machine: `Configure` then `RequestRetract`, so the first trip up is `PreparingToRetract -> Retracting -> ReturningToDock -> Docked`.

### Director (`SpatialDirectorCore`)

Mode is Desk, Floor or Ceiling; phase is Idle or Previewing.

- `RequestMode(Floor | Ceiling)` opens a preview. **Nothing moves.**
- `NextCandidate`, `Nudge`, `Rotate`, `ResetPlacement` edit the preview. A detected spot that is nudged is re-checked against the room; if it fails, the outline turns coral, the status says why, and `Confirm` is refused.
- `Confirm` is the only call that moves the office. It is refused while held or while anything is moving.
- `Cancel` leaves everything as it was.
- `RequestMode(Desk)` returns to the stored desk pose.
- The published mode changes at Confirm, with `IsTransitioning` true until arrival. If the move is given up before the root moved, the mode reverts.

`IsRemote` is true in Floor mode and in Ceiling mode except while the pod is `Engaged`. `IsTransitioning` is true while the transition machine runs (including the agent wait) and in every pod state other than `Docked` and `Engaged`.

## 7. Room data and honesty

`MrukRoomGeometry` copies what it needs from MRUK into plain data when MRUK raises scene-loaded, room-created, room-updated or room-removed, and at no other time. It never calls `LoadSceneFromDevice` on its own: only `RequestSceneLoad(bool allowSpaceSetup)` does, and nothing in the built scene calls that.

Availability, as determined today:

| Value | When |
|---|---|
| `NotLoadedYet` | No MRUK instance yet, or MRUK not initialised |
| `PermissionDenied` | On device, `USE_SCENE` not granted (checked with `Permission.HasUserAuthorizedPermission`); or an explicit load returned `NoScenePermission` |
| `NoRoomSetup` | MRUK initialised with no room; or an explicit load returned `NoRoomsFound` |
| `InvalidFloor` | A room with no floor outline of at least three points |
| `NoCeiling` | (ceiling availability only) a usable floor but no ceiling outline |
| `Available` | A room with a usable floor |

MRUK raises scene-loaded only on success, so without an explicit load request "no room on the headset" and "still loading" both read as `NotLoadedYet`. The words shown are then "Room scan not loaded yet", which is true in both cases.

Rules the code keeps:

- A floor position is called detected only if it came from the room data and passes the checks. The words are "Detected floor spot 2 of 4".
- With no usable room data, or no clear spot, Floor mode offers a hand-placed position and says "Room scan not available: place it by hand" or "No clear floor spot found: place it by hand. This spot is not checked against your room." Its height is a guess (eye height minus 1.15 m) and can be nudged up and down. After Confirm the status stays "Placed by hand (not checked against your room)". It is not saved.
- Ceiling mode has no manual fallback. Without a real ceiling outline it is refused with the reason.
- The synthetic room used in the Editor says "Synthetic test room" in the service's summary.

Furniture is every anchor with a volume except floor, ceiling and global mesh, turned into an upright box (the local axis nearest to vertical gives the height). Plane-only anchors (wall art, door and window frames) are ignored.

## 8. Placement rules, as implemented

Floor (`FloorPlacementSettings` defaults): candidates on both sides at 50, 70, 90, 110 and 130 degrees from the user's forward, at 1.0 to 2.2 m in 0.3 m steps. Rejected: off the floor outline; footprint (1.0 x 0.7 m, centred 0.12 m toward the user, times scale) within 0.10 m of the outline; within 0.15 m of a furniture box that overlaps the office's height; within 0.6 m of the user's feet; within 35 degrees of forward; all three sight lines from the head blocked by furniture. Score: visibility 0.35, clearance 0.25, side preference 0.15, distance from 1.4 m 0.15, look-down angle inside 15 to 50 degrees 0.10. At most six candidates, at least 0.35 m apart, each with an explanation string. The root's +Z points at the user.

Ceiling (`CeilingPlacementSettings` defaults): docks on the preferred side first, 1.0, 1.2, 0.85 and 1.4 m to the side and 0.3, 0.6 and 0 m forward. Rejected: closer than 0.8 m horizontally to the head; not under a ceiling outline; footprint within 0.35 m of the outline; ceiling less than 0.6 m above the eyes; overlapping furniture. The root hangs 0.5 m below the ceiling. Inspection pose: 0.6 m to the side (then 0.7, then 0.5), 0.15 m forward, 0.35 m below the eyes, clear of furniture at that height. The straight segment from dock to inspection pose must miss every furniture box grown 0.30 m sideways and 0.05 m vertically. The first dock that passes is used.

Near-user pose: 0.45 m ahead of and 0.25 m below the eyes, +Z toward the user (the scene's facing convention). Published when a mode is confirmed, on recenter and when the headset is put back on.

## 9. Persistence

`WorkspacePlacementPersistence` stores one JSON record in PlayerPrefs under `musedesk.spatial.placement.v1`: version, build GUID, mode, room id, pose relative to the room reference, scale, and the dock relative to the room reference.

- Room id: `MRUKRoom.Anchor.Uuid`. Room reference: the pose of the first floor anchor. Whether that UUID and pose are stable across sessions on a device is **not verified**.
- Saved when a mode is confirmed, when a move arrives, and on app pause. Desk mode deletes the record.
- On start, with a flag on, the director waits for the desk placement and for the room (at most `restoreTimeoutSeconds`, 15 s), then loads and validates: version, build, room id, floor or dock checks against the current room, distance from the user, flag, hold. Any failure leaves the office in Desk and deletes the record.
- Nothing else is stored. No task, session or backend state.

## 10. Edits wanted in files this agent does not own

Required for the tests to run (nothing else is required):

**`Assets/MuseDesk/Tests/EditMode/MuseDesk.Tests.EditMode.asmdef`**

```json
"references": [
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner",
    "MuseDesk.Spatial.Core"
],
...
"versionDefines": [
    { "name": "com.meta.xr.mrutilitykit", "expression": "", "define": "MUSEDESK_SPATIAL_TESTS" }
],
```

The define can instead be added to the Editor's Scripting Define Symbols. Until both are done `SpatialPlacementTests.cs` compiles to nothing.

Optional, each closes a gap described above:

1. **`WorkspacePlacement.cs`**: at the top of `ResetWorkspaceInFront()` (`:317`) and `ConfirmManualPlacement()` (`:307`), and before the write in `PlaceOffDeskRoutine` (`:120`):
   `if (MuseDesk.Spatial.SpatialWorkspaceState.Mode != MuseDesk.Spatial.WorkspaceMode.Desk || MuseDesk.Spatial.SpatialWorkspaceState.IsTransitioning) return;` (`yield break;` in the coroutine, after `_placing = false;`).
2. **`AgentMovement.cs`**: store the walk start in the root's space instead of world space (`_startWorld`, `:26`), so a walk survives a moving root. Then the agent wait could go.
3. **`AgentHandoffController.cs`** (`:170-176`): parent `HandoffDest` to the workspace root and multiply `stopDistance` by the root's scale.
4. **`WorkspaceLayoutStore.cs`**: nothing needed.

`SummonRequests.BringOfficeHere` / `SendOfficeBack` (reach side) are subscribed to by the director: Ceiling lowers / raises the pod; Floor returns to the desk pose and remembers the floor spot.

## 11. Compile evidence and doubts

The agent ran no Unity command. The Editor that the coordinator has open recompiled on its own while the files were being written, and the timestamps in `Library/ScriptAssemblies` say:

- `MuseDesk.Spatial.Core.dll` was rebuilt after the last edit to the Core sources.
- `Assembly-CSharp.dll` was rebuilt after the scene-side files were first complete. The director was edited once more after that (the `SummonRequests` subscription and the parked floor spot); see the final report for whether a later rebuild was observed.

That is evidence the runtime code compiles. It is not evidence that it works. The test file is fenced off and the builder is outside `Assets`, so neither has been compiled.

MRUK calls, each checked against `Library/PackageCache/com.meta.xr.mrutilitykit@26f06c35cd18/Core/Scripts/`:

| Call in `MrukRoomGeometry.cs` | Verified against |
|---|---|
| `MRUK.Instance` | `MRUK.cs:565` |
| `IsInitialized` | `MRUK.cs:250` |
| `RegisterSceneLoadedCallback(UnityAction)` | `MRUK.cs:375` |
| `SceneLoadedEvent`, `RoomCreatedEvent`, `RoomUpdatedEvent`, `RoomRemovedEvent` (`AddListener` / `RemoveListener`) | `MRUK.cs:263`, `:275`, `:288`, `:300` |
| `GetCurrentRoom()`, `Rooms` | `MRUK.cs:442`, `:556` |
| `LoadSceneFromDevice(bool, bool, SceneModel)` returning `Task<LoadDeviceResult>`; `LoadDeviceResult.NoScenePermission`, `NoRoomsFound` | `MRUK.cs:1050`, `:125`, `:130` |
| `MRUKRoom.FloorAnchors`, `CeilingAnchors`, `Anchors` | `MRUKRoom.cs:162`, `:177`, `:139` |
| `MRUKRoom.Anchor.Uuid` | `MRUKRoom.cs:104`; `OVRAnchor.cs:1201` |
| `MRUKAnchor.PlaneBoundary2D`, `VolumeBounds`, `Label`, `HasAnyLabel(SceneLabels)` | `MRUKAnchor.cs:133`, `:126`, `:114`, `:721` |
| Boundary points are in the anchor's local XY plane | `MRUKRoom.cs:886` |
| `OVRPermissionsRequester.ScenePermission` | core SDK `OVRPermissionsRequester.cs:97` |

`FloorAnchor` and `CeilingAnchor` (singular) are `[Obsolete]` in this version (`MRUKRoom.cs:155`, `:170`) and are not used. `IsPositionInRoom` and MRUK's raycasts are not used: the evaluators work on the copied outlines so they can be tested.

## 12. What only a headset can verify

- Everything about real rooms: whether MRUK's floor and ceiling outlines and furniture volumes arrive as assumed (local XY boundary, one near-vertical axis per volume), and whether the candidates land on clear floor.
- Whether the room anchor UUID and the floor anchor pose are stable across sessions (persistence), and whether the anchors have their new poses two frames after `RecenteredPose` (recenter).
- The office on the floor at 1 to 2 m: legibility, look-down comfort, whether scale 1 is too small.
- The ceiling pod: comfort of the descent, the inspection pose beside the user, the placeholder mast.
- Poke keys that are re-positioned at run time (the strip moves between the Focus key and the near-user pose): press travel and `PokeVisualRestGuard`.
- Rigidbodies and Interaction SDK interactables under a root that is moved by 1 to 2 m in a second.
- Whether anything still writes the root in a spatial mode (watch for the ownership warning in the log).
