# Desk choreography and layout

Branch `feature/living-world-polish`. **Editor and Play Mode only (AUTO + PLAY). Nothing here has been seen on a headset.** `Builds/` was not touched; the competition APK (rc6-2) does not contain this.

Owner's decision: the desks may move apart and the layout may change so that agent walks stop clipping.

## What changed, in one paragraph

The three desks are spread so there is a 0.20 m aisle between the Researcher's desk and the Developer's. The Researcher now steps back from its desk, walks behind it, comes out through the aisle and offers the paper at the aisle mouth, 14 degrees to the right of straight ahead. After an approval it walks back through the aisle and hands the work to the Developer behind the desks; both turn to face each other. The Developer hands to the Tester the same way. Every walk is an authored lane of scene transforms (`WorkspaceRoot/DeskLanes`); no position lives in a runtime script. All seven routes keep at least 0.019 m at the 0.080 m collider radius in Desk mode (before: six of seven overlapped, the worst by 0.066 m).

Frame for every number: `WorkspaceRoot`-local metres, +z toward the user, -x the user's right.

## Layout: old and new

| Object | Old (x, y, z) | New (x, y, z) |
|---|---|---|
| `ResearchStation` (desk, agent, its children) | -0.300, 0, 0 | -0.366, 0, 0.025 |
| `OfficeV2/Nameplate_Researcher` | -0.300, 0.091, 0.108 | -0.366, 0.091, 0.133 |
| `DevStation` | 0, 0, 0.050 | 0.122, 0, 0.050 |
| `OfficeV2/Nameplate_Developer` | 0, 0.103, 0.150 | 0.122, 0.103, 0.150 |
| `QAStation` | 0.300, 0, 0 | 0.385, 0, 0 |
| `OfficeV2/Nameplate_Tester` | 0.300, 0.096, 0.108 | 0.385, 0.096, 0.108 |
| `DeliveryZone` (`AgentRegistry.deliveryAnchor`) | -0.200, 0, 0.170 | -0.124, 0, 0.120 |
| `Phase6/Decor_BookStack` | -0.490, 0, -0.050 | -0.432, 0, 0.212 |
| `Phase6/Decor_MiniPlant` | -0.485, 0, -0.120 | -0.462, 0, 0.262 |
| `Phase6/Decor_DeskLamp` | 0.465, 0, 0.070 | -0.405, 0, 0.275 |

New lane points under `WorkspaceRoot/DeskLanes` (x, z): `Res_Exit_1` (-0.285, -0.047), `Res_Exit_2` (-0.124, -0.032), `ResToDev_FromHome_2` (-0.200, -0.042), `ResToDev_FromDelivery_1` (-0.124, 0.020), `ResToDev_Stand` (-0.075, -0.030), `DevToTester_Stand` (0.185, -0.045).

Wiring: Researcher `AgentMovement.exitWaypoints` = `Res_Exit_1`, `Res_Exit_2` (were `WalkExit_1`, `WalkExit_2`, which stay in the scene unused). `AgentHandoffController.lanes` = researcher to developer (stand `ResToDev_Stand`; from home via `Res_Exit_1`, `ResToDev_FromHome_2`; from the delivery spot via `ResToDev_FromDelivery_1`, 1.1 s) and developer to tester (stand `DevToTester_Stand`, straight). `stopDistance` and `backOffset` are untouched and only used by a pair with no lane.

Not moved: the factory line, both trays, the stamps and their pad, the shelf, the clock, every near-user panel (task dock and board, mode console, plaque, receipt), the remote courier platform. No collider, grab or poke component was edited.

Desk extents after the move (body band): Researcher x -0.500..-0.224, z 0.054..0.186; Developer x -0.024..0.226; Tester x 0.239..0.489. The decor is now a small cluster beside the Inbox tray: the lamp's old place is under the widened Tester desk, and the books and plant stood in the old lane.

## Routes and clearance (Desk mode, radius 0.080 m)

`scripts\drv.ps1 DeskClearanceDrive Report -Max 6000`. "Before" is the old layout measured with the same (extended) driver, so the two columns are comparable. Positive is clear; the target is 0.015.

| Route | Before | After | After: polyline |
|---|---:|---:|---|
| R1 Researcher home to delivery | **-0.066** | **+0.020** | (-0.366, 0.025) (-0.285, -0.047) (-0.124, -0.032) (-0.124, 0.120); 0.42 m, one stop-and-turn at `Res_Exit_2` |
| R2 delivery to home (rejected) | -0.066 | +0.020 | R1 reversed |
| R3 Researcher home to Developer | +0.005 | +0.020 | home, `Res_Exit_1`, `ResToDev_FromHome_2`, stand; 0.32 m, no stop |
| R4 delivery to Developer (after an approval) | +0.008 | +0.020 | (-0.124, 0.120) (-0.124, 0.020) (-0.075, -0.030); 0.17 m, no stop |
| R5 Developer stand to home | +0.005 | +0.020 | R3 reversed |
| R6 Developer home to Tester | -0.004 | +0.019 | (0.122, 0.050) (0.185, -0.045); 0.11 m |
| R7 Tester stand to home | -0.004 | +0.019 | R6 reversed |
| R8 walk home after the approval handoff | **-0.061** | (is R5) | Before: a straight line from the handoff stop to home, through the Researcher's own desk. S9c did not list it. This is the "agents walk through their own desks" complaint |

Where the real agents walked (Play Mode trace, every frame, `DeskClearanceDrive TraceFor` / `TraceReport`): scripted demo plus every route, a walk sent home part-way and a handoff requested during the walk out. Researcher 4.26 m, minimum **0.015** at (-0.291, -0.041); Developer 0.23 m, minimum **0.019**. The 0.015 is the first sample outside the 10 cm home exemption; on the authored polyline the same place reads 0.020 because samples are 1 cm apart.

Three things about the measurement, stated plainly:

1. **Own desk at home.** At its home slot an agent stands at its desk by design (the disc overlaps the desk in plan by 0.051 to 0.063; that is the "unexempted" figure in the report and it is the same before and after). The driver exempts the walker's own station and nameplate for the first and last 10 cm of a route that starts or ends at home, and nowhere else. S9c exempted only what the disc already touched, which made an own nameplate 8 mm away count; that is why S9c's R3/R5 read -0.010 and this table's "before" reads +0.005.
2. **The driver sees more than S9c's did.** The factory line is a skinned mesh and was invisible to it. Skinned meshes are now skinned on the CPU and included, as are the near-user panels where they stand. Text sub-meshes are excluded.
3. **Corners.** Corners sharper than `walkTurnLimit` (50 degrees) are stop-and-turn points and are sampled. The lanes are shaped so only `Res_Exit_2` is one (corner angles: 47 at `Res_Exit_1`, 45 and 2 on the handoff lane, 44 at `ResToDev_FromDelivery_1`).

## Handoff geometry

| Handoff | Courier stands | Receiver home | Centre distance | Facing (Play Mode, yaw in the office frame) |
|---|---|---|---:|---|
| Researcher to Developer | (-0.075, -0.030) | (0.122, 0.050) | 0.213 m | courier 70, receiver -100: face to face |
| Developer to Tester | (0.185, -0.045) | (0.385, 0) | 0.205 m | courier 85, receiver -92: face to face |

Both stand points are in the open strip behind the desks, at least 0.095 m from every mesh, so the token's 3 cm arc between the two hands crosses no furniture. The receiver turns on the spot for the exchange and turns back to its station pose when the beat is over (damped, then exact). Scripted demo timing after Approve: walk 1.06 s, exchange 1.0 s, walk home 1.8 s; the exchange is reached before the completion sequence starts, so it is seen (as before).

## Reach

The paper is taken from the Researcher's hand at the delivery spot. Seated eye at (0, 0.30, 0.62).

| | Old spot (-0.200, 0.170) | New spot (-0.124, 0.120) |
|---|---:|---:|
| Horizontal distance from the eye | 0.49 m | 0.52 m |
| Bearing | 24 degrees right | 14 degrees right |
| Distance from a right shoulder at about (-0.18, 0.10, 0.62) | 0.45 m | 0.50 m |

3 to 5 cm farther, nearer the centre line, still behind the factory line as before. The stamps, the board and the trays did not move.

## Footprint: Desk, Floor, Ceiling

Validated office footprint (`SpatialWorkspaceDirector.footprint`, used by every movement sweep): 1.0 x 0.7 m centred 0.12 m toward the user, so x -0.50..0.50, z -0.23..0.47. **Not changed.**

- **Desk.** Static meshes now span x -0.500..0.489 (before -0.520..0.496: the books and the lamp were the outliers). Walking agents stay inside x -0.446..0.465 (before, the delivery lane at x = -0.47 put the body out to -0.55). Everything is inside the footprint.
- **Floor.** Platform deck x -0.46..0.46 with a low rail at the back and sides. All seven routes clear the platform (minimum +0.016; before, R1/R2 went through the side rail at -0.080). **Cost:** the outer desks overhang the deck edge, the Researcher's by 4.0 cm and the Tester's by 2.9 cm (before: both inside), and the Developer no longer sits on its floor pad (the pads are part of the platform art, fitted to the old positions).
- **Ceiling.** Pod deck x -0.44..0.44, with the arch posts standing at x -0.24..-0.15 and 0.15..0.24, z -0.14..-0.07. **Not clean.** The strip behind the Researcher's desk is 0.124 m wide between the desk and the west arch post, so the Researcher's lanes brush the post foot: R1/R2 -0.046 (before -0.080, through the side rail), **R3/R5 -0.049 (before +0.005: a regression in this mode)**, R4 +0.007, R6/R7 +0.019. The outer desks overhang the deck by 6.0 and 4.9 cm, and the Developer is off its pad.

Why not fix Floor and Ceiling here: an aisle needs 0.776 m of desks plus 0.19 m, about 0.98 m; the decks are 0.92 and 0.88 m and cannot grow without leaving the validated footprint (`BuildCeilingPolishV1` already says so). Options, none taken: (a) move the Researcher's station another 5 cm toward the user and re-place the Inbox tray, which opens the strip enough to clear the arch post; (b) re-fit the pod and platform art to the new station positions; (c) keep the compact layout in Floor and Ceiling and switch at mode change (new runtime code and tests). The Wall is separate and unaffected.

## Code

- `Agents/Core/AgentWalkPath.cs`: `WaypointsBehind`, `BuildFrom`, `PointSegmentDistance`. A walk that starts on its route continues from where it stands and never goes back to an earlier corner (a walk sent home part-way, or sent out again on the way home).
- `Agents/AgentMovement.cs`: `WalkRoute(destination, via, homeVia, onArrived, seconds)`; the way home is remembered when the walk out starts; `ReturnHome` while already returning keeps walking; `FacePoint` / `StopFacing` (heading only, on the spot); `IsReturning`, `IsFacingPoint`, `BuildRoute`. `DeliverTo` and `BuildPath` behave as before.
- `Agents/AgentHandoffController.cs`: `lanes` (stand, `fromHome`, `fromDelivery`, `fromDeliverySeconds`), `faceEachOther`, `homeFirstMaxSeconds`, `deliverySpotTolerance`. A courier that is neither at home nor on the delivery spot walks home first along its lane, then hands off (bounded; past the bound the visual is skipped). A pair with no lane behaves exactly as before, so `Revert()` restores the old behaviour.
- `Agents/FaceUserWhileDelivering.cs`: yields while a handoff has the agent facing a colleague.
- `Agents/Core/WalkClearance.cs`: `Evaluate` overload with own-station flags.
- Drivers: `DeskClearanceDrive` (Edit and Play Mode; skinned meshes; near-user panels; lanes; `Map`, `MapOf`, `TraceFor`, `TraceReport`), `DeskChoreographyDrive` (send agents on each walk; freeze at the exchange), `DeskLayoutShots` (Editor captures), `DeskLayoutProbe` (read-only positions and bounds).

Presentation only: nothing here reads or writes task, session or backend state. The office-move safety code (transition machine, sweeps, footprint) is untouched.

## Evidence

- **AUTO.** `AgentWalkPathTests` 12 (7 new, one checks that the way home from every point of the walk out is exactly the way already walked), `WalkClearanceTests` 14 (2 new), `AgentWalkCornersTests` 6. Groups run one fixture at a time: interaction 18 fixtures / 173 passed, board 4 / 69, motion 4 / 88, spatial 33 fixtures / 276 passed, **1 failed**: `SpatialIsolationTests.SceneSideScripts_NameNoGatewayReducerOrSessionType` ("HintCatalog.cs mentions SessionController"), a file from Sprint A15 that this work does not touch.
- **PLAY** (Editor, no HMD). Desk demo start, paper, take, approve, completed: State 'Completed', receipt 'Delivered', 'SCRIPTED DEMO' throughout, no console error from the app (the one entry in the console is a CLI command-not-found from before this session). Trace figures above. Floor and Ceiling entered with the synthetic room, clearance read in each, back to Desk with all three agents at home.
- **Revert checked.** After `Revert()` the scene differs from HEAD only by the four new serialized fields of `AgentHandoffController` at their defaults; `Apply()` after that reproduces the layout and the record.

## Screenshots (Editor renders on a flat background; not headset)

`docs/review_screenshots/polish/`:

- Before, Edit Mode: `desk_layout_before_plan.png`, `desk_layout_before_seat.png`, `desk_layout_before_right.png`
- After, Edit Mode: `desk_layout_after_plan.png`, `desk_layout_after_seat.png`, `desk_layout_after_right.png` (the task board is shown open here; in Play it is hidden until its key is pressed)
- After, Play Mode: `desk_layout_after_play_idle_seat.png`, `desk_layout_after_play_delivery_seat.png`, `desk_layout_after_play_delivery_right.png`, `desk_layout_after_play_exchange_res_dev_back.png`, `desk_layout_after_play_exchange_res_dev_seat.png`, `desk_layout_after_play_exchange_dev_tester_seat.png`
- Floor and Ceiling, Play Mode, before and after: `desk_layout_before_play_floor_right.png`, `desk_layout_after_play_floor_right.png`, `desk_layout_before_play_ceiling_right.png`, `desk_layout_after_play_ceiling_right.png`

"plan" is straight down with the user at the bottom; "seat" is from the seated eye; "right" is a three-quarter view from the user's right.

## Revert

```
scripts\run_guarded.ps1 -File AgentScripts\DeskChoreographyLayoutV1.cs -Entry DeskChoreographyLayoutV1.Revert
```

Puts every recorded local position back exactly (`AgentScripts/DeskChoreographyLayoutV1.record.txt`, round-trip floats), restores `exitWaypoints` to `WalkExit_1`, `WalkExit_2`, clears the lanes and removes `DeskLanes`. With no lanes the handoff code takes its old path. `Apply` is idempotent and places everything from the record, never from the current pose. Keep the record file with the scene: without it `Apply` and `Revert` refuse to run on an applied scene.

## Needs a headset look

1. The Developer is 12 cm left of centre and the Researcher's aisle is in front of the right hand: does the office still read as one composition from the seat?
2. With the task board open, is the Tester (8.5 cm farther left) hidden behind the board's edge? In the Edit Mode capture half of it is.
3. Taking the paper at the new spot: 3 to 5 cm farther than before, over the factory line. Comfortable without leaning?
4. The walk itself: the turn on the spot before the first step back from the desk, the one stop at the aisle, foot slide on the short diagonal.
5. The exchange: both agents turning to each other (the receiver turns about 100 degrees and back), and whether the token is seen between the hands.
6. Floor mode: desk ends over the platform edge and the Developer off its pad. Ceiling mode: the same, plus the Researcher brushing the west arch post on its way to and from the Developer.
7. The decor cluster by the Inbox tray next to the mode console: crowded or cute?
