# Wall Habitat: release flags

## Where the flags live

| Place | Flags | Default |
|---|---|---|
| `Assets/MuseDesk/Resources/ReleaseConfig.asset` (`MuseDeskReleaseConfig`) | `wallHabitat`, `wallManager`, `agentSelect`, `agentBtw`, `largeAgentPortal`, `wallElevatorVfx` | **all false** |
| Backend env | `MUSEDESK_P3_SIDE_CONVERSATION_ENABLED`, `MUSEDESK_P3_MANAGER_ENABLED` | false (routes return 404 `FEATURE_DISABLED`) |

`ReleaseConfigApplier` enforces the dependencies:
- Every other Wall flag needs `wallHabitat`.
- `agentBtw` and `largeAgentPortal` also need `agentSelect`.

Switching `wallHabitat` off while in Wall mode leaves the mode safely. The visit and the portal return, the agents re-home, and the desk office shows again.

Names follow `03_INTEGRATION_CONTRACT.json` `feature_flags`.

## Effect of all flags off (the competition build)

- `SpatialWorkspaceDirector.wallHost` is set, but `RequestMode(Wall)` returns false and nothing else changes. The director never publishes Wall.
- `RoomPlacementService.SetWallDemand(false)`: the MRUK reader is driven only by the Floor/Ceiling flags, exactly as before.
- No Wall dock, ghost, habitat, cards or hand gesture. The `WallHabitatController.Update` early-outs. The bridge only listens to reducer events.

## Decision for 2026-11-18 (competition)

**Keep every Wall flag OFF in the submission APK.** None of the hardware gates below has been run on a worn Quest. The feature lives on `feature/wall-habitat` and has not been merged to `release/final-refinement`.

## Gates before turning a flag on (all UNVERIFIED on hardware)

| Flag | Must pass on a worn Quest 3 |
|---|---|
| `wallHabitat` | Wall recognised; the ghost lines up with the real wall; place, cancel, move and restore after relaunch and recenter by hand; no visible clipping with furniture, door, window or monitor; elevator rides read clearly; Desk/Floor/Ceiling still work; frame rate holds (see PERFORMANCE) |
| `agentSelect` | Far pinch is discoverable; no accidental pick while taking a paper, stamp or card; lost tracking returns the agent; the perch drop works |
| `agentBtw` | The answer reflects the current task, or honestly says unavailable; the task keeps running; close and repeat |
| `largeAgentPortal` | Preview lands in real clear space beside the seated user; the 1 m avatar is comfortable and readable; it closes and returns; frame rate holds |
| `wallManager` | The plan card is readable; approve creates the right real cards; risky items need the second confirm; printer cards appear only for real cards |
| `wallElevatorVfx` | Not implemented (reserved). Elevator doors and indicators are plain geometry. |

## Validation builds

- Release-safe: flags off (this file's default).
- Validation: `AgentScripts/WallValidationConfig.cs` `On()` / `Off()` switches the six flags in the ReleaseConfig asset for a validation APK. Rebuild with `Off()` before any submission build.
