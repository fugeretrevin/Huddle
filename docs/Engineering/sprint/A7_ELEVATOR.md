# Sprint A7: elevator and multi-floor core

Status: code and EditMode tests written, NOT compiled, NOT run (no Editor in this worktree). Wall flags stay OFF.

## The 13 steps: enforced before vs added

| # | Step | Before (walker + `HabitatElevatorMachine`) | Now (`ElevatorDispatcher` phase) |
|---|---|---|---|
| 1 | request | `Request`, coalesced | `Request`, rejects invalid floors once (Failed/InvalidRequest) |
| 2 | path validated | layout validator at build time; `FindRoute` unchecked per trip | `FloorRouteGraph.Query` (validated edges only, or no-route + fallback) |
| 3 | walk to entry | walker lease to the cabin segment | `ToWaitSpot` -> `ReportAtWaitSpot`; spot is validated and exclusive |
| 4 | face the door | implicit only (turn in place at the landing corner, speed scaled by alignment) | explicit `Facing` phase (`ReportFacingDoor`); boarding needs it |
| 5 | door opens | `MayBoard` needs `Boarding` state (doors 100% open) | `MayEnterCabin` needs cabin `Open` and `DoorOpen01 >= 1` |
| 6 | enter | walker checks `MayBoard` each step | `Boarding` -> `ReportEnteredCabin`; early/late entry = refused + `Violations++` |
| 7 | door closes | after `ReportBoarded` | cabin cannot close with anyone Boarding/Exiting; never moves with doors open |
| 8 | move | profiled mover | same profile |
| 9 | settle | `arriveDwell` | `Settling` |
| 10 | door opens | yes | `Open`, exits authorised before any boarding |
| 11 | exit | `MustExit` | `Exiting` -> `ReportExitedCabin` |
| 12 | walk to destination | walker | `ToDestination` -> `ReportReachedDestination` |
| 13 | release nav ownership | implicit (reservations) | `NavigationReleased(agent)` event, every outcome, once |

`HabitatMotion.cs`, `HabitatWalker.cs`, `HabitatStage.cs` were NOT modified. Changing the walker's segment stops
(for an explicit queue stop and face-door gate) would shift timing of the 22 existing tests and I could not run them.
The dispatcher is a new authority the view can adopt; the existing machine keeps working unchanged.

## API (namespace `MuseDesk.Spatial.Wall`, assembly `MuseDesk.Spatial.Core`)

- `ElevatorDispatcher(float[] floorY, IList<WaitSpot> spots, int startFloor)`; `settings.capacity` (N), FIFO tickets, cabin phases `Idle/Moving/Settling/DoorsOpening/Open/DoorsClosing/OutOfService`.
  - `Request(agent, from, to)` -> ticket id (0 = rejected). `Cancel(agent)` (false once inside).
  - Reports: `ReportAtWaitSpot`, `ReportFacingDoor`, `ReportEnteredCabin`, `ReportExitedCabin`, `ReportReachedDestination`.
  - Queries: `TryGetPhase`, `TryGetWaitSpot`, `MayEnterCabin`, `MustExit`, `IsRiding`, `DoorFullyOpen`, `CabinY`, `DoorOpen01`, `AboardCount`, `Violations`, `RiderPosition(cabinNode, cabinY)`.
  - `Interrupt()` (recenter/pause: nearest landing, riders let out, Failed/Interrupted), `DoorsJammed` + `ClearFault()`.
  - Events: `RideCompleted(RideResult)` exactly once per request (Arrived / Failed with `RideFailure` + `RideFallback` / Cancelled); `NavigationReleased(agent)`; `StrandedRiderReleased(agent, floor)`.
- `ElevatorWaitSpots.ForLayout(layout)`: queue node + corridor spots per floor, outside the doorway, clearance-validated.
- `FloorRouteGraph(layout)`: edge classes (`Walkway`, `WalkwayToElevator`, `ElevatorBoarding`, `ElevatorRide`, `LevelToCubicle`, `CubicleToHandoff`); `Query(from, to, home)` -> `FloorRoute` (`found`, `polyline`, `edgeClasses`, `hasRide`) or `reason` (`UnknownNode/Disconnected/EdgeBlocked/RideUnavailable`) + `fallback` (`ReturnHome`/`HoldAtStart`); `SetEdgeBlocked`, `SetRidesAvailable(false)` when the cabin is out of service.

Fallbacks: a failed ride is `ReturnHomeViaWalkway` (rider outside the cabin, or at an open door) or `HoldInCabin` (doors cannot open; released by `StrandedRiderReleased`). Nothing ever walks through closed doors.

## Tests (`Assets/MuseDesk/Tests/EditMode/Wall/ElevatorSequenceTests.cs`, 22)

Walker never inside the cabin volume unless boarded while doors < 100% (every tick); cabin-relative position while riding (x, z, y == cabin); faced door within the turn limit; no walking while the cabin travels; 13 phases in order; early entry refused; wait spots validated, outside doorway, unique; capacity 2 with FIFO; calls from several floors; exits before boardings; cancel once and spot freed; no-show timeout and recovery; board timeout at the door; stuck doors (out of service, hold in cabin, release); interrupt lands at a floor; invalid requests; exactly-once across a mixed workload; route graph edges valid and classified, validated polyline with sampled clearance, closed elevator / blocked corridor / unknown node / invalid edge give no-route with a fallback.

## What the view layer (`Scripts/Wall/WallHabitatController*`) must call

1. Build `FloorRouteGraph` and `ElevatorDispatcher` (with `ElevatorWaitSpots.ForLayout`) from the same `HabitatLayout`; tick the dispatcher once per frame with the same dt.
2. Per trip: `FloorRouteGraph.Query` first; on `found == false` follow `fallback` (stay or walk home) instead of moving.
3. For a ride: `Request`, walk to `TryGetWaitSpot`, report at spot, turn to the door and report facing; step in only when `MayEnterCabin`, then `ReportEnteredCabin` and parent the avatar to the cabin (position = `RiderPosition`, no independent motion, scale untouched); on `MustExit` walk out, `ReportExitedCabin`, walk on, `ReportReachedDestination`.
4. Subscribe `RideCompleted`: on `Failed` apply `fallback`; on `NavigationReleased` drop reservations for the agent.
5. Mirror the real door mechanism into `DoorsJammed`; on `Interrupt` events (recenter, mode exit) call `Interrupt()`.
6. The old machine is untouched; do not run both for one cabin.

## Risks

- Uncompiled: expect a typo-level fix or two. The two walker-based tests assert existing behaviour I only read, not ran; a failure there means a real gap in the walker (report it, do not loosen the assertion).
- Capacity > 1 is not yet exercised by the existing walker/stage (single rider).
- Extra corridor wait spots can block corridor traffic; the walker's lease system is not aware of them.
