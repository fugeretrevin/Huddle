# Wall Habitat: tests

## How to run

- Unity: `scripts\tests.ps1 MuseDesk.Tests.EditMode.Wall` (97 tests). Run the whole regression one fixture at a time (see `docs/Engineering/HANDOFF_NEXT_SESSION.md`).
- Backend: `cd backend; .venv\Scripts\python.exe -m pytest -q` (194, of which 49 are Phase 3).
- Play Mode: start Play Mode, then `scripts\wall_flow.ps1 -Part place|visit|portal|manager|elevator` (driver `AgentScripts/WallDrive.cs`).

## Results on 2026-11-14

| Suite | Result |
|---|---|
| Wall EditMode (`WallPlacementTests`, `HabitatMotionTests`, `AgentVisitPortalManagerTests`, `WallOfflineLogicTests`) | 97 / 97 |
| All other EditMode fixtures, one at a time | 724 / 724. `SpatialIsolationTests` first failed on a comment naming a gateway type in `WallOfflineLogic.cs`; fixed, now 3 / 3. Three fixtures are dormant behind `MUSEDESK_TESTS_RUNTIME_REF`, as before. |
| Backend pytest | 194 / 194 |
| Play Mode Desk, Floor, Ceiling with Wall flags off | Floor preview, confirm, return to Desk, Ceiling confirm and dock: same as before |
| Play Mode Wall walkthrough | See `WALL_HABITAT_INTERACTION_QA.md` |

## What the Wall tests cover

**Placement (WallPlacementTests)**

| Case | Expected result |
|---|---|
| Good side wall | Valid, faces into the room, flat, on the wall surface |
| World/local round trip | Exact |
| Straight ahead | `OverMonitor` |
| Candidates | Side walls first, all valid, spaced apart |
| Short wall | `WallTooSmall` |
| Narrow corner | `WallTooSmall` |
| Door or window | Avoided |
| Tall furniture against the wall | Blocks the spot |
| Desk below the spot | Does not block |
| User behind the wall | `FacingAway` |
| Spot too close to the user | Refused |
| Spot too high or too low for the seated eye | Refused |
| Rotated room (30°) | Normals point inward; candidates found |
| Partial scan (two walls) | Still works |
| No permission or no scan | No candidates; manual spot is amber with "nothing was checked" |
| Return wall cutting the volume | `HitsOtherWall` |
| Ray placement | Works |
| Persistence | Same-room restore; wall-id change found by anchor pose; different room, lost wall, newly blocked spot and garbage JSON all refused |
| Flow | Flag off refuses; Place here is explicit and refused while holding; invalid spot cannot be placed; relocate then cancel keeps the old spot; room change suspends without moving; layout toggle |

**Layout and motion (HabitatMotionTests)**

| Case | Expected result |
|---|---|
| Both layouts | Validate with 0 issues |
| Sockets | Unique; contract sockets present |
| Desk planted in the corridor | Caught by the validator |
| Route to the Manager | Uses the elevator, reached through the queue spot |
| Neighbouring nodes | Zone-reserved |
| Elevator | Full cycle at real landing heights, FIFO, coalescing, no-show eviction, interrupt mid-travel stops at a landing |
| Stage | Researcher to Manager on both layouts; two elevator trips serialised; three-way same-floor crossing; duplicates ignored; manager delegation is not a walk; event burst capped; stale acts dropped; reset mid-ride; blocked corridor soft-transfers instead of walking through; every tick asserts no capsule enters a box and no two characters overlap |

**Visit, portal, Manager and claim (AgentVisitPortalManagerTests)**

| Case | Expected result |
|---|---|
| Registry | One away; visit and portal exclusive |
| Grab | Only when armed |
| Drop | Valid drop perches; invalid drop and tracking loss never perch; timeout and interruptions return; second visit refused; hand over to the portal |
| /btw session | One in flight; late replies ignored; history cap; timeout wording |
| Portal spots | Beside the user, not in front or behind |
| Portal refusals | Furniture, low ceiling, in front, too close, near a wall; low ceiling scales down |
| Portal fallback | No room: small confirmed fallback only |
| Portal flow | Confirm only on a valid spot; interrupt frees the reservation; comfort mode is shorter |
| Manager | Proposed plan never prints; dependency and danger rules; stable idempotency key; prints only committed ids seen on the board, once; failures create nothing |
| AgentSelect claim | Never takes a pinch that belongs to a paper; holding something cancels it |

**Backend (backend/tests/phase3)**

| Case | Expected result |
|---|---|
| Side question | Store and event log identical before and after; unknown agent or task; stale revision; change request becomes `amendment_suggested` with no mutation; busy; timeout; provenance per mode; visit memory delete; idempotency |
| Manager | Cycle; bad dependency; manager as owner; kind/role mismatch; dangerous needs authorisation; unapproved dependency; stale revision 409; double commit idempotent; failure on the 2nd create leaves zero tasks and plan `failed`; `task.created` only after commit; canonical dependency ids; template provenance; garbage model output rejected |
