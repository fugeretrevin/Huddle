# Wall Habitat: architecture

Phase 3, branch `feature/wall-habitat`. Wall is an additive fourth workspace mode. Desk, Floor and Ceiling are unchanged when the Wall flags are off, and every Wall flag is off by default.

## One picture

```
FastAPI backend (authoritative)
  /v2 task board (unchanged)            <- Manager commit creates cards here (create_task, deps, agent)
  /v2 .../agents/{id}/side-question     read-only /btw (backend/app/phase3/side.py)
  /v2 .../manager/plans (+commit)       propose / revise / reject / commit (backend/app/phase3/manager.py)
           |  v1 + v2 WS events (unchanged reducers)
Quest / Unity
  SessionController, Phase2Bootstrap (unchanged)
  WallEventBridge  ---- real reducer events --->  HabitatStage (pure)  ---> WallHabitatView (proxy art, avatar clones)
  WallBackendGateway  <--- /btw, plan, commit ---  WallHabitatController (mode host, keys, cards, hands)
  SpatialWorkspaceDirector  --(IWallModeHost)-->  WallHabitatController
     publishes SpatialWorkspaceState Mode=Wall, IsRemote=true  -> NearUserKit, RemoteDocumentDelivery work as in Floor
```

## Files

| Layer | File | What it is |
|---|---|---|
| Pure core (`MuseDesk.Spatial.Core`, EditMode-tested) | `Scripts/Spatial/Core/Wall/WallGeometry.cs` | `RoomWall`, `WallOpening`, `IWallGeometry`, `SyntheticWallRoom`, `WallMath` (wall ray cast, segment and box tests) |
| | `WallPlacementEvaluator.cs` | Wall spot scoring, candidates, ray placement, manual (no scan) spot, reasons in words |
| | `WallPlacementCodec.cs` | Saved spot (room id, wall id, u, y, layout, pose relative to the room anchor), restore with every check run again |
| | `WallModeFlow.cs` | `WallFeatureFlags`; Off, then Previewing, then Active (or Relocating); Place here is explicit; a room change suspends the habitat and never moves it |
| | `HabitatLayout.cs` | Full (3 levels) and Compact (2 levels) layouts from one builder: sockets, collision boxes, nav graph; `HabitatLayoutValidator` |
| | `HabitatMotion.cs` | `HabitatReservations` (zone reservations), `HabitatElevatorMachine` |
| | `HabitatWalker.cs` | The one writer of an avatar's root: corridor-segment leases, elevator boarding, soft transfer fallback |
| | `HabitatStage.cs` | Real event to visual act: dedupe, coalesce, deadlines, one elevator trip at a time (travel lock) |
| | `AgentVisitFlow.cs` | `AgentPresentationRegistry`, Select Agents and desk visit flow, `SideConversationSession` |
| | `PortalPlacement.cs` | Portal floor spot evaluator (swept column), `PortalFlow` |
| | `ManagerPlanFlow.cs` | Plan card states, the dangerous-item confirm step, stable idempotency keys, print gating |
| | `WallOfflineLogic.cs` | Offline honest fallbacks, JSON DTO mapping |
| Runtime (Assembly-CSharp) | `Scripts/Wall/WallHabitatController*.cs` | Mode host, ghost, dock keys, stage ticking, desk hiding, persistence, recenter and pause; Interaction partial (hands, Select Agents, visit, portal); Manager partial |
| | `WallHabitatView.cs` | Proxy art built from the layout; visual-only avatar clones; elevator, doors, beacon, status lights, printed cards |
| | `WallUi.cs` | Runtime clones of the ModeConsole poke key, text, `WallCard` |
| | `WallBackendGateway.cs`, `WallEventBridge.cs`, `WallContracts.cs` | Backend calls (live and offline), reducer events to `HabitatEvent`, task context |
| | `WorkspaceVisualSuppressor.cs` | Hides desk-office renderers only (no scripts or colliders touched) |
| | `HabitatArtManifest.cs` | Prefab swap-in by socket id (empty: proxy art) |
| Existing, edited additively | `SpatialWorkspaceState.cs` | `WorkspaceMode.Wall = 3` |
| | `SpatialWorkspaceDirector.cs` | `wallHost`; `RequestMode(Wall)` goes to the host; any other mode leaves Wall first; `PublishWallChanged`; `RestoreSettled` |
| | `MrukRoomGeometry.cs` | Also `IWallGeometry`: `WallAnchors`, plus DOOR_FRAME / WINDOW_FRAME rects projected onto their wall |
| | `RoomPlacementService.cs` | `Walls`, `SetWallDemand`; the synthetic test room now has its four walls |
| | `HandOwnership.cs` | `HandClaim.AgentSelect = 6` (and `HandOwner.AgentSelect`) |
| | `MuseDeskReleaseConfig.cs`, `ReleaseConfigApplier.cs` | Six Wall flags, all false |
| Scene | `AgentScripts/BuildWallHabitatV1.cs` | Adds `Spatial/WallHabitat` (controller, gateway, bridge); `Revert()` removes it |

## Coordinates (stated once)

- Wall local and habitat root: +Z is the wall normal pointing into the room, toward the user. +Y is world up. +X is `LookRotation(normal, up) * right`. There is never a negative scale.
- From the user, habitat +X is on their left, the same as the project rule "user's right is root −X". The elevator spine is at +X.
- MRUK wall anchors: `transform.forward` points into the room, as `MRUKRoom`'s own outline code assumes. The wall rectangle comes from `PlaneRect`.
- The root origin is the centre of the backplate on the wall surface, offset 4 mm into the room. The volume spans z from 0 to the depth.
- NearUserRig space, for the dock, perch and cards: +Z toward the user, the user's right is −X, the desk top is at y = 0, and the eye is at about (0, 0.30, 0.62).

## Invariants and where they are enforced

| Invariant | Enforced by | Tested |
|---|---|---|
| One logical worker per agent; projections run nothing | `WallHabitatView.CloneVisual` strips every MonoBehaviour, collider, rigidbody, audio source and text before activation | PLAY (no agent logic on clones; task state unchanged) |
| Visual relocation never changes task state | No Wall code path calls a task-mutating API, except the Manager commit (user-approved) | PLAY `WallDrive Tasks` before/after; backend side-question tests (byte-identical store) |
| Proxy: one per agent, one away at a time, visit and portal exclusive | `AgentPresentationRegistry` | AUTO |
| Card only after the backend confirms a canonical id | `ManagerPlanFlow`: id in the commit result AND seen as `task.created` | AUTO + PLAY |
| Plan commit is idempotent and revision-checked | backend `manager.py`; client keeps a stable key per selection | AUTO (backend + client) |
| Portal and habitat placement confirmed explicitly, clearance checked | `WallModeFlow.PlaceHere`, `PortalFlow.Confirm` on a valid spot; evaluators | AUTO |
| Tracking loss cancels, never commits | `HandOwnershipArbiter` (Cancelled is never Released); `AgentVisitFlow.CancelCarry` | AUTO |
| No character walks through desks, partitions, shaft, railings or the wall | Layout validator (every node and walk edge, capsule against boxes); walkers only move along validated edges; cabin and door widths checked; corridor leases; soft transfer instead of a blocked walk | AUTO (every-tick geometry asserts in simulations) |
| Desk, Floor and Ceiling unchanged with the flags off | Every director change is guarded by `wallHost` with the flag on | Regression fixtures (see PLACEMENT_TESTS) |
| Simulated output is labelled | `SCRIPTED_DEMO`, `OFFLINE_SNAPSHOT`, `TEMPLATE_PLAN`, `TASK_SNAPSHOT` on the cards | AUTO + PLAY |

## Habitat layout numbers (validated)

| | Full | Compact |
|---|---|---|
| Size W x H x D (m) | 1.05 x 0.90 x 0.26 | 0.74 x 0.62 x 0.24 |
| Floors (feet y) | Lower −0.400, Mid −0.110, Manager 0.150 | Lower −0.260, Manager 0.000 |
| Character capsule r / h (m) | 0.032 / 0.17 | 0.028 / 0.15 |
| Clearance kept from every box | 5 mm | 5 mm |
| Validator issues | 0 | 0 |

## Art contract alignment (Antigravity)

Status: no Antigravity Wall Habitat art exists in this branch yet. Its worktree `.worktrees/wall-habitat` (branch `art/wall-habitat`) was created on 2026-11-14 at 11:46 and was not touched. Everything visible is labelled proxy art built from the layout boxes.

To integrate:
1. Antigravity's modules must fit inside the layout boxes in `HabitatLayout.cs`. The boxes are authoritative for collision; art never adds colliders (the manifest disables them).
2. Socket ids must match `HabitatLayoutBuilder.RequiredHabitatSockets` and `RequiredAgentSockets` exactly, with no naming drift.
3. Add manifest entries (socket, prefab, offset, proxies to hide) with `enabled=false`. Check the fit with a Scene capture, then enable.
4. Manager model: replace the tinted Developer clone. Set `agentSources` for the Manager, or extend `WallHabitatView` with a fourth source.
5. Elevator and door clips are optional. Motion is time-based from `HabitatElevatorMachine`, so a missing clip cannot stall anything.
