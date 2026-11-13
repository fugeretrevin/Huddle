# Room placement findings

2026-11-13. Written by the Spatial-Safety agent; **section 0 and the notes marked "pass 2" were added the same day by the Spatial-Safety-2 agent**, which could edit the director and the machines and closed the gaps listed in section 5. Covers what the floor and ceiling placement rules are now, the audit of the relaxed descent check and what replaced it, the numbers from the synthetic rooms, what the mover does that the check does not cover, and what only a real room can tell.

**Nothing here has been compiled or run in Unity, and nothing has been tried on a headset.** The numbers and the test results below come from compiling the real `MuseDesk.Spatial.Core` sources and the real test file outside Unity against a hand-written stand-in for the `UnityEngine` math types and for NUnit (see "How the numbers were produced"). That is good evidence for the geometry and weak evidence for anything that depends on Unity itself.

**A clear swept test means "clear of what the room scan recognised". It does not mean "clear".** Scene geometry may be incomplete, out of date or wrong. The user's Confirm, and a way to cancel or retract that is always available, are part of the safety design and are not replaced by this check.

## 0. Safety pass 2: every move is swept, every move can be stopped

Same caveat as the rest of this file: **not compiled in Unity, not run in Unity, not seen on a headset.** The Core sources and the test file compile outside Unity with 0 errors and 0 warnings. The last run of the tests there was 84 of 84 passing (67 earlier ones, two of them changed, and 17 new). One edit was made after that run (the director keeps the previewed ceiling dock apart from the confirmed one, `_previewCeiling`); it compiles, but the machine's application control policy then blocked the rebuilt test binary, twice, and that was not worked around. So the final sources are compile-checked and the 84 results are from one edit earlier.

**The office is a hologram.** Nothing here can hurt anyone. "Clear" means the picture does not pass through recognised furniture or the user. It still means "clear of what the scan recognised", never "clear".

### What moves how

| Move | Route | Who drives it |
|---|---|---|
| Desk, floor spot or inspection pose to the ceiling dock (first trip) | straight up without turning until the office base is 0.25 m above the eyes, then one straight leg to the dock (the turn happens on that leg) | the pod (`BeginArrival`): PreparingToRetract, Retracting, ReturningToDock, Docked |
| Dock to inspection pose and back | one straight leg, as before, now with the mast | the pod |
| Desk to floor spot, floor spot to desk, inspection pose to desk | the straight line if its sweep is clear; otherwise up, across at 0.25 m above the eyes, down | the director |
| Dock to desk or floor spot | across to the point above the destination, then straight down (the straight line is tried first for a floor spot) | the director |

All of them go through `CeilingPlacementEvaluator.PlanRoute` and `SweepRoute` immediately before the move starts. No route clear: the move is refused with words, nothing is teleported, no margin is made smaller. `WorkspaceTransitionMachine.TryMoveRoute` refuses a route that does not start where the root is (2 mm), and gives a move up if the root was moved while it waited for agents.

### The rule for the desk end (item 1), and why

The desk pose breaks the margins by design: the office sits 0.6 to 0.7 m in front of the seated user with its near edge 0.13 to 0.15 m from the head's vertical axis (keep-out radius 0.35 m), and it usually stands on or against the user's real desk and screen, which the scan reports as solid boxes. Demanding the margins there refuses every move. Ignoring them there would allow anything. The rule chosen (`SweepRoute`, options `fromDesk` / `toDesk`):

- A margin that is **already broken at the desk pose** (the user keep-out, one particular furniture box, the room outline) is **excused until the office first keeps it**. From then on it applies in full: the office may not come back.
- While excused the office **may not make it worse**. User: never closer to the head's axis than its desk distance less `deskApproachAllowance` (0.05 m), and never closer than half its desk distance. Furniture: not closer to that box and not deeper into its height range, by more than `deskContactTolerance` (0.01 m). Wall: not closer.
- After `deskStartRegion` (2.5 m) of travel from the desk end every margin applies, kept or not.
- A box that was NOT inside its margin at the desk pose is never excused.
- A path that ends at the desk is swept as the same path starting there.

Why not a fixed "first N cm are free": the office would be free to move toward the user inside those centimetres. Why not a tighter cylinder: the desk pose is 0.13 m from the axis, so the cylinder would have to be about a head wide and would then mean nothing on the rest of the path. Why the allowance is not zero: the office turns to face the user from its new place while it leaves, and in the test office that brings its near edge from 0.130 m to roughly 0.10 m from the axis for a moment, above head height, before it moves away (a hand calculation, not a measured number; the tests only show that the trip is accepted with 0.05 m).

Candid limits of this rule: 2.5 m is nearly the length of a whole first trip, so the distance bound does little; the real protection is "never worse, and never back in once out". A tall cabinet the desk office already overlaps stays excused while the office rises through it. Whether 0.05 m is right is a headset question.

### Two legs instead of a diagonal (item 1)

Adopted. On a diagonal from the desk to the dock the office turns about 70 degrees and slides sideways while it is still at the user's head height, 0.13 to 0.3 m from the head. With the rise first, everything below "eyes + 0.25 m" is a pure vertical move in front of the user: no turn, no approach, in view. A test records every pose written and checks both things. The diagonal is not tried as a fallback for a trip to the dock.

New since pass 1: above the keep-out cylinder the office must stay `userOverheadRadius` (0.20 m) from the head's axis at any height. It never passes straight over the head. One earlier test asserted the opposite and was changed.

### Floor moves and the way back (item 2)

Swept with the same margins (the numbers live in `ceilingSettings`, which is now also "the path settings"). In the test office the straight line from the desk pose to the first floor spot is refused: the office would sink through the edge of the user's own desk (only the first spot was looked at). Up-and-over is clear and is used; so is the way back. With no desk volume the straight line is used. A bin between is crossed over; a 2.2 m cabinet between is a refusal. The larger clearance (0.405 m per sample) applies wherever the office base is above the top of the user keep-out.

The floor preview only offers spots that can be reached by a clear route from where the office is, a nudged spot whose route is blocked turns invalid with the reason, and Confirm plans again for where the user is then.

Hand-placed spots (no usable scan): the user rules are always applied; furniture is applied when a scan exists; the room outline is not (the spot is by definition not vouched for). The status keeps saying "not checked against your room".

Known mismatch: floor spots are accepted 0.10 m from a wall, the sweep wants more than 0.105 m. A spot in that 5 mm band is offered by the evaluator and then filtered out or refused for "too close to a wall".

### Re-validation (item 3)

`RequestPodDescend` runs `ValidateDock` for the head pose and room of that moment, gives the pod the fresh inspection pose, or refuses with the reason and what to do. `RequestPodRetract` from beside the user sweeps the way up first. A room event while docked re-runs the check and puts the result in the status line. Confirm re-validates the previewed dock. Not done: nothing re-checks a move that is already in flight when the room changes.

### Recenter and restore (item 4)

After a recenter the office is written only to a pose validated after the recenter: the floor spot (floor checks plus chair clearance), the dock and a fresh inspection pose (`ValidateDock`), or the desk pose recomputed from the room reference. Otherwise any move is ended where it is (`WorkspaceTransitionMachine.Abort`, `CeilingPodMachine.Hold`), `PlacementNeedsConfirmation` becomes true, the pod refuses to move the office, the record is not saved, and the words say "Return to Desk, then place it again". Desk is itself a swept route from wherever the office stopped. `TryRestore` sweeps the way to the saved spot or dock and refuses with the reason. The pod's `ErrorRecovery` no longer travels a line of its own from an arbitrary point: at the dock it rests, at the inspection pose it uses the validated path, anywhere else it holds.

Dead ends this creates, on purpose, and worth knowing before a demo:

- If the room scan disappears while the office is on the floor or at the ceiling, the way back to the desk cannot be checked and is refused ("Press Desk again when the room scan is back"). `SpatialWorkspaceDirector.returnToDeskWithoutRoom` (default off) allows that one move with the user rules only.
- If the way back is blocked by furniture the user can only clear the way or sit back and press Desk again.
- After a recenter with no room reference the stored desk pose is a stale world pose; the route to it is still swept against the room as it is.

### Stop (item 5)

`SpatialDirectorCore.StopMove()` / `SpatialWorkspaceDirector.StopMove()`; the Cancel key calls `CancelPlacement()`, which leaves a preview or, with no preview, stops the move.

| When | What Stop does |
|---|---|
| A mode change in flight (director-driven) | halts, goes back along the poses already travelled (eased), ends exactly on the start pose; the mode and the saved-placement bookkeeping go back too |
| First trip up, wind-up | called off, nothing had moved, Ceiling mode is left |
| First trip up, travelling | goes back to where it started, then Ceiling mode is left |
| Pod: wind-up before coming down | back to Docked |
| Pod: coming down | Descending -> Retracting -> ReturningToDock -> Docked, back up the same path |
| Pod: wind-up before going up | called off, stays Engaged |
| Pod: going up | nothing: it is moving away from the user and is left to finish |
| Settling, at rest, previewing, already going back | nothing |
| Waiting for agents | the move is dropped; nothing had moved |

Held paper: a taken paper is a scene root and is never dragged by the office. What a reverse would do is take the office away from the paper the user still has to file. So **while a transition hold is set, Stop only halts the office where it is**; it goes back when the paper is put down, and the status says so. The halt itself is never refused.

Requests during a move queue nothing: "Send back" while the pod comes down is the same Stop; "Bring" while it goes up is refused; a mode request while anything moves is refused with "Press Stop, or wait". One exception, kept: "Send back" during the half second the pod settles beside the user is remembered and acted on when it has settled.

Pause (`OnPause`) still finishes a move at its destination at once (or at its start if it had been stopped). Both ends were validated; the path between is skipped, which for a hologram sweeps nothing.

### The mast and the mount (item 6)

- Mast: at every sample of a path with the mast option, a line from the top centre of the office box to the mount point (above the dock's footprint centre, at ceiling height) is tested against every furniture box grown by `mastRadius` (0.03 m) plus the sample margin. Used for the pod's path, the first trip and leaving Ceiling mode.
- Under the ceiling: on the high part of such a path the office box is extended up to the ceiling, so a recognised object hanging in the gap above the office blocks it.
- Mount: no recognised volume within `mountClearance` (0.30 m) of the mount point in the top `mountDepth` (0.30 m) under the ceiling (`MountBlocked`, in the dock search and in `ValidateDock`).
- The dock counts of the table in section 3 did not change with these rules (same 13 rooms, run before the last edit).

**Ceiling lamps, fans, pendant lights, smoke detectors and beams are generally NOT in Quest scene data.** These rules protect only against geometry the scan recognised. In an ordinary room they will find nothing, and the office will hang where a pendant lamp is if there is one. The preview outline, the user's Confirm and Stop are the safeguard for ceiling fixtures; the code is not.

### New numbers (all code defaults; nothing serialized needs to change)

`CeilingPlacementSettings`: `userOverheadRadius` 0.20, `deskStartRegion` 2.5, `deskApproachAllowance` 0.05, `deskContactTolerance` 0.01, `overRouteAboveHead` 0.25, `mastRadius` 0.03, `mountClearance` 0.30, `mountDepth` 0.30. `WorkspaceTransitionMachine.minReverseSeconds` 0.4. `SpatialWorkspaceDirector.returnToDeskWithoutRoom` false. `SpatialModeStrip.cancelKeyLabel` (found under the Cancel key when empty), `stopWords` "Stop".

## 1. The rules now

### Floor mode (unchanged)

`FloorPlacementEvaluator`, `FloorPlacementSettings` defaults. Not touched by this work.

- Candidates on both sides at 50, 70, 90, 110 and 130 degrees from the user's forward, 1.0 to 2.2 m away in 0.3 m steps, on a detected floor outline only.
- Rejected: off the floor outline; footprint (1.0 x 0.7 m, centred 0.12 m toward the user, times scale) within 0.10 m of the outline; within 0.15 m of a furniture box that overlaps the office's height; within 0.6 m of the point under the user's head; within 35 degrees of forward; all three sight lines from the head blocked.
- Pass 2: the move to a floor spot and back is now a swept route (section 0).

### Ceiling mode

`CeilingPlacementEvaluator`, `CeilingPlacementSettings` defaults.

Dock (unchanged): 1.0, 1.2, 0.85, 1.4 m to the side and 0.3, 0.6, 0 m forward, preferred side first (24 candidates). Rejected when closer than 0.8 m horizontally to the head ("not overhead"), not under a ceiling outline, footprint within 0.35 m of that outline, ceiling less than 0.6 m above the eyes, or the docked office overlaps a furniture box. The root hangs 0.5 m below the ceiling.

Inspection pose (the search is wider, the acceptance is stricter): tried in this order, at most 12 usable poses per side.

1. raise: 0, then 0.05, then 0.10 m above "0.35 m below the eyes" (`inspectRaiseOptions`);
2. forward: 0.15, then 0, then -0.15 m from the head (`inspectForward` + `inspectForwardOffsets`);
3. sideways: 0.6, 0.7, 0.5 m (`inspectSideOptions`, as before), then 0.85, 0.9, 0.8 m (`inspectWideSideOptions`).

A pose is usable when the office standing there overlaps no furniture (the old footprint test, kept) and, tested as a path of zero length, passes the swept test below.

Descent path, swept volume, fail closed (`CeilingPlacementEvaluator.SweepPath`):

- The path is `RootPose.Lerp(dock, inspection, k)` for k from 0 to 1: a straight line with the yaw slerped. This is the same function `WorkspaceTransitionMachine.Tick` uses, so the poses tested are the poses travelled (section 4).
- The office's box (the footprint rectangle from `SpatialGeometry.Footprint` at the interpolated position and yaw, from its base to base + 0.45 m) is tested at every `sweepStep` (0.05 m) of travel or less, both end poses included. "Travel" is the translation plus the arc of the footprint corner farthest from the root (0.69 m lever arm) while the office turns. There is no untested stretch.
- Each sample must keep `sweepMarginHorizontal` (0.08 m) from every furniture box that is at its height or within `sweepMarginVertical` (0.05 m) of it. Half a step (0.025 m) is added to the horizontal margin of every sample, and the sample's vertical extent is stretched by half the height change per step each way, so the motion between two samples is covered. A sample is therefore tested with 0.105 m; the guarantee for the continuous motion is 0.08 m.
- High part: while the office base is more than `highClearanceAboveInspection` (0.5 m) above the lower end of the path, `descentClearance` (0.30 m) is added, giving 0.405 m per sample. Up there the user cannot see or stop the office.
- User keep-out: no sample may come within `userKeepOutRadius` (0.35 m, plus the half step) of the head's vertical axis while any part of it is between the floor and `userKeepOutAboveHead` (0.15 m) above the head. If there is no floor outline under the user the cylinder has no lower end.
- Room outline: every sample must be inside the floor outline around the user (or, failing that, a ceiling outline over either end) by the same horizontal margin. No outline: the path counts as blocked.
- Touching a margin counts as blocked. A path that cannot be checked (no room, no outline, more than 4096 samples) counts as blocked.
- For each dock the usable inspection poses are tried in order and the first whose whole path is clear is taken. The first dock with a clear path is used.
- `ValidateDock` (restoring a saved dock, and the recenter) runs the same search and the same sweep. It now also refuses a dock that furniture overlaps.

When nothing survives the answer is "no", with the counts in words: "... N dock blocked by furniture, N path blocked by furniture (first: LABEL), N path too close to you, N path too close to a wall, N no room beside you (LABEL in the way | too close to you | too close to a wall)". To get more candidates, lengthen the search lists. Do not shrink the margins.

`CeilingPlacement.path` (`CeilingPathCheck`) describes the path that was validated, or the first one refused: start, end, yaw at both ends, sample count, travel, the padding used, the smallest clearance found to furniture (with its label), to the user and to the outline, what blocked it, the blocking label and where along the path. `Describe()` gives it as one line for a log or a debug view. `EvaluateAll` returns every surviving dock.

## 2. Audit of the relaxed check

What was there on 2026-11-12:

- (a) dock footprint against furniture boxes, no margin;
- (b) inspection footprint against furniture boxes, no margin;
- (c) `PathBlocked`: one line segment from dock position to inspection position against furniture boxes grown 0.30 m sideways and 0.05 m vertically, with the segment shortened by 0.45 m at the inspection end.

Findings:

1. **The reviewer's objection is correct and reproducible.** `BoxOnTheFinalApproach_PassedTheRelaxedRule_AndIsRefusedNow` builds the case: a 0.30 x 0.10 m box 3 cm beyond the office's far edge at the inspection pose, 0.95 to 1.15 m high. (a), (b) and the shortened (c) all accept it. The office comes down from further out and its edge passes through the box during the last 0.3 m. The unshortened line would have refused it, so the blind stretch is exactly what let it through.
2. **The centre line was never a volume test, shortened or not.** The office is 0.5 m wide each side of the line and reaches 0.47 m toward the user; the line was grown by 0.30 m. `BoxOnlyTheOfficeEdgeHits_IsRefused` puts a 6 cm box inside the office's volume half way down, 0.45 m from the line: both the shortened and the full line accept it.
3. **Why the full line rejected everything, precisely.** In the Editor room (`RoomPlacementService.UseSyntheticTestRoom`) the desk top is 0.40 m below the eyes and the inspection height is 0.35 m below the eyes. Growing the desk 0.05 m upward put its top exactly at the height of the end of the line, and growing it 0.30 m sideways put its near edge exactly under the end of the line (0.45 - 0.30 = 0.15 m forward). The end point sat on a corner of the grown box. That is two coincidences of the default numbers, not a property of furnished rooms: in the test room (`SpatialRooms.Office`, desk top 0.45 m below the eyes) the full line accepted all 24 docks. The honest fix was a test with the office's real extent and explicit margins, plus a search that can raise or move the pose.
4. **The inspection pose itself was the larger problem.** At 0.6 m to the side and 0.15 m forward, facing the user, the footprint's near edge is 0.15 m from the head's vertical axis, at 0.35 m below the eyes up to 0.10 m above them. That is inside the user's shoulder. None of yesterday's accepted paths passes the user keep-out (table below). With a 0.35 m radius the root has to be at least 0.845 m from the head (0.47 m from root to near edge, 0.35 m radius, 0.025 m half step), which is why the search now goes on to 0.85 and 0.9 m.

## 3. Numbers from the synthetic rooms

24 dock candidates per room. User at (0, 1.2, 0) looking along +Z. "Test office" is `SpatialRooms.Office`: 5 x 5 m, ceiling 2.6 m, a 1.4 x 0.7 x 0.75 m desk centred 0.8 m ahead.

| Room | Relaxed rule (12 Nov) | Full centre line (before the relaxation) | Swept test, search not widened | Swept test, widened search (now) |
|---|---|---|---|---|
| A. Empty 5 x 5 m, ceiling 2.6 m | 24 | 24 | 0 | 24 |
| B. Test office | 24 | 24 | 0 | 24 |
| C. `UseSyntheticTestRoom` (4 x 4 m, floor 1.15 m below the eyes, ceiling 2.5 m above it, desk centred 0.75 m ahead) | 20 | 0 | 0 | 20, inspection raised 5 cm |
| D. Test office + monitor box on the desk, 0.6 m wide, 0.75 to 1.2 m high | 24 | 24 | 0 | 24, 0.26 m clear of it |
| E. Test office + two-monitor box, 1.3 m wide, same height | 24 | 24 | 0 | 24, 0.19 m clear of it |
| F. Test office + 2 m tall shelf beside the user on the right | 12 | 12 | 0 | 12, all on the left |
| G. Test office + tall shelves on both sides | 0 | 0 | 0 | 0 |
| H. Test office, ceiling 2.0 m | 24 | 24 | 0 | 24 |
| I. Test office, ceiling 1.85 m | 24 | 24 | 0 | 24 |
| J. Test office + floor lamp (0.3 m square, 2 m tall) at the desk's right end | 20 | 20 | 0 | 12, all on the left |
| K. Test office in a 3 x 3 m room | 2 | 2 | 0 | 2 |
| L. Test office + a 0.45 m tall box at each end of the desk's near edge | 0 | 0 | 0 | 0 |
| M. Test office + a 0.5 m tall box along the desk's whole near edge | 0 | 0 | 0 | 0 |

Reading the table:

- "Search not widened" is the swept test with the real margins and only the inspection options that existed yesterday (0.6, 0.7, 0.5 m to the side). It is 0 everywhere because of the user keep-out (rooms A, B, D, E, H, I, J, K), the shelf (F, G) or, in C, the desk top being exactly the vertical margin below the pose. That is the case the assignment describes, and the answer was to widen the search.
- Yesterday's accepted paths, judged by today's test to yesterday's inspection pose: none passes in any room. With the keep-out switched off to isolate furniture, the swept test still refuses 8 of 24 in room E (the office passes within the margin of the monitors), 8 of 20 in room J (the lamp), and all 20 in room C (the office would rest exactly 5 cm above the desk, which counts as touching).
- In room J the relaxed rule and the full line both accepted 8 docks on the lamp's side. The swept test refuses all 12 on that side and uses the left.
- Rooms G, L and M have no candidate under any rule. They fail closed with "no room beside you (STORAGE | SCREEN in the way)".
- With `userKeepOutRadius` at 0.20 m instead of 0.35 m, the pose at 0.7 m to the side passes without the wide options in rooms A, B, D, E (18 of 24), F, H, I, J, K (not in C, where the desk top is the obstacle). The default was given as 0.35 m; whether the office is still comfortable to reach at 0.85 m is a design question for the headset, not something these numbers answer.
- Cost of one `Evaluate` on the development PC: about 0.14 ms, both when the first dock is accepted and when all 24 are refused with 15 furniture boxes. Not measured on Quest.

Room C has a tie worth knowing about: the desk top is exactly `sweepMarginVertical` below the default inspection height, so whether the unraised pose is accepted is decided by float rounding of the head height. Either way the office ends at least 5 cm above the desk; with the tie broken toward "blocked" it ends 10 cm above it.

### How the numbers were produced

Unity commands were not allowed in this task. The real files in `Assets/MuseDesk/Scripts/Spatial/Core/` and the real `SpatialPlacementTests.cs` were compiled with the C# compiler from the .NET SDK that ships inside the Unity install (language version 9), against a hand-written stand-in for `Vector2`, `Vector3`, `Quaternion`, `Mathf`, `Pose`, `Bounds`, `JsonUtility` and the NUnit calls the tests use. Result: 67 of 67 tests pass there (the 48 that existed, one of them changed, and 19 new). The "relaxed" and "full centre line" columns come from a copy of the removed code run against the same rooms.

What that does not prove: that the files compile in Unity's assemblies, that Unity's `Quaternion` functions agree with the stand-in to the last bit, or that the real NUnit resolves every assertion overload the same way. An eight-way mutation check of the new code was started (remove the padding, restore the blind stretch, remove the user test, shrink the margin, drop the high clearance, drop the vertical margin, remove the wall test, try only the first inspection pose). Three mutants ran. Two were caught by the tests (the blind stretch by 7 tests, the wall test by 1). The third (only the first inspection pose is tried) was not caught; `BlockedWayToTheFirstInspectionPose_TriesTheNextPose_NeverTheBlockedOne` was added for it and has not been re-checked against that mutant. The other five could not be run because the machine's application control policy blocked the freshly built binaries, and that was not worked around.

## 4. Does the mover travel what is swept?

Yes for descent and retraction, with these facts read from the code:

- `CeilingPodMachine.Tick` starts the descent with `_mover.TryMove(InspectionPose, seconds)` and the retraction with `_mover.TryMove(DockPose, seconds)`.
- `WorkspaceTransitionMachine.Tick` writes `RootPose.Lerp(_from, _to, eased)` with `eased = k*k*(3 - 2k)`: one straight segment for position, `Quaternion.SlerpUnclamped` for rotation, no separate vertical and horizontal legs. The easing changes when each pose is reached, not which poses are reached.
- `_from` is the root's pose when the move starts. The pod only starts a descent from `Docked` and a retraction from `Engaged`, where the root is at the dock or inspection pose to within 2 mm and 0.5 degrees.
- Both poses are yaw-only, so the slerp is a plain yaw interpolation along the shorter arc, and the footprint at each sample is exact.

`ThePodTravels_ExactlyTheValidatedPath_DownAndUp` records every pose written to the root during a descent and a retraction and checks each against `PathPose` at the same fraction (1 mm, 0.5 degrees). `GoingUp_IsTheSamePathReversed` checks that the reverse sweep tests the same poses and returns the same numbers.

## 5. What the check did not cover after pass 1 (items 1 to 6 and 8 are closed by pass 2, see section 0; item 7 stands)

1. **The first trip up to the dock is not swept.** `SpatialDirectorCore.Confirm` and `TryRestore` call `_pod.Configure(...)` then `_pod.RequestRetract()`, which moves the office in a straight line from wherever it is (the desk pose, or a floor spot) to the dock. Nothing validates that line. Fix in `SpatialDirectorCore.cs`: before `_pod.Configure`, call `CeilingPlacementEvaluator.SweepPath(_room, user, _mover.CurrentPose, _ceiling.DockPose, footprint, ceilingSettings, out check)` and refuse the Confirm when it is false; or route the first trip desk -> inspection pose -> dock, both of which can be swept.
2. **Leaving Ceiling mode and every Floor move are not swept.** `ReturnToDesk` and the Floor branch of `Confirm` move straight between the desk pose and the dock, inspection pose or floor spot. Same fix, same file. The start pose at the desk will usually be inside the 0.35 m user keep-out (the desk pose has the office at the user's chest by design), so a sweep for those moves needs its own rule for the desk end; that is a design decision, not made here.
3. **The path is validated at Confirm, not at descent.** `SpatialDirectorCore.RequestPodDescend` reuses the inspection pose computed when the mode was confirmed. If the user has moved their chair, or the room changed, the descent goes to a stale pose. Fix in `SpatialDirectorCore.cs`: in `RequestPodDescend`, call `ValidateDock(_room, _user(), _pod.DockPose, footprint, ceilingSettings, out fresh)`; on success `_pod.Configure(fresh.DockPose, fresh.InspectionPose)` then descend; on failure refuse with `fresh.explanation`. `OnRoomChanged` in Ceiling mode should do the same re-validation.
4. **Recenter fails open.** In `SpatialDirectorCore.OnRecenter`, when `ValidateDock` returns false the code still calls `_pod.OnRecenter(dock, inspect)` with the old inspection pose, and if the pod was coming down it snaps the office to that pose. Fix: when validation fails, snap to the dock (or return to Desk) and say why.
5. **There is no stop during a descent.** `CeilingPodMachine.RequestRetract` during `PreparingToDescend`, `Descending` or `Stabilizing` only sets `RetractPending`; the office finishes coming down, stabilises, and only then goes back up. An "always-available Cancel" does not exist for the 1.2 to 4 seconds of travel (about 2.8 s in the test office). Fix in `CeilingPodMachine.cs` and `WorkspaceTransitionMachine.cs`: let a retract request during `Descending` reverse the move from the current pose (the mover needs a way to abandon a move in place; today it can only finish at the target). The way back from any point on the path is on the validated path, so no new check is needed.
6. **`ErrorRecovery` and `Snap`.** Recovery to the dock from a point on the path stays on the path. `Snap` (recenter) teleports and so sweeps nothing.
7. **Scene values.** `MainMRScene.unity` stores `ceilingSettings` with the old fields. The new fields are not in the scene file and should take their code defaults when the scene loads; that is the usual Unity behaviour for fields added to a serialisable class but it was not verified. If the Inspector shows `inspectWideSideOptions`, `inspectForwardOffsets` or `inspectRaiseOptions` empty, Ceiling mode will answer "no room beside you (too close to you)" in every room until they are filled in (0.85, 0.9, 0.8 / 0, -0.15, -0.3 / 0, 0.05, 0.10).
8. **Docs now out of date** (not editable in this task): `SPATIAL_WORKSPACE_ARCHITECTURE.md` section 8 (the Ceiling paragraph still describes the line test and the 0.5 to 0.7 m inspection pose) and `SPATIAL_PLACEMENT_TESTS.md` (48 tests, and its description of `ValidCeiling_GivesDockInspectionPoseAndDescent`).

## 6. Limits of the swept test itself

- The office is a box: footprint times `officeHeight` above the root. The 12 mm shelf under the office sits below the root; the 0.05 m vertical margin covers it. Pass 2: the mast is a line with a 0.03 m radius and the mount a clearance square (section 0). The mount point is taken above the footprint centre; `CeilingPodController.mountOffset` in the scene is not read by the check.
- Agents, papers and anything else that sticks out of the footprint or above 0.45 m are not part of the box.
- Furniture is an upright box. A table is a solid block from its underside to its top, so the legroom under a desk counts as blocked (conservative), and an L-shaped or round piece is whatever box the scan gave it.
- The user is a cylinder under the head position at the moment of the check. Arms, a raised hand, legs stretched to the side, a second person and pets are not in it. The cylinder ends 0.15 m above the head; pass 2: above it the office must stay 0.20 m from the head's axis (`userOverheadRadius`), so it never crosses directly over the user.
- The inspection pose rests over the corner of the user's own desk in most rooms: 0.10 m above it in the test office, 0.10 m in the Editor room after the raise. Anything standing on that part of the desk that the scan does not report is inside the office.
- Walls are the floor outline. Doors, windows and openings are ignored, and a wall-mounted object without a volume is not furniture.

## Not yet known: needs a real room

Everything below can only be answered with Quest scene data on a headset. None of it has been looked at.

- **How furniture volumes actually arrive.** `MrukRoomGeometry.ReadVolume` assumes every volume anchor has one local axis near vertical and turns it into an upright box. Whether MRUK reports a desk as one volume, a volume plus a plane, or a plane only (a plane-only desk is ignored today: `VolumeBounds.HasValue` is required) is not known. If the user's desk arrives as a plane, the office will be allowed to rest inside it.
- **Missing or partial scans.** A room scanned without its furniture, a room scanned months ago, a scan of a different room loaded by mistake, a room with several floor anchors, a floor outline that does not contain the place where the user sits (the sweep then falls back to a ceiling outline, or refuses). What the headset reports in each case, and whether "refuse" is what the user sees, is untested.
- **Objects the scan does not contain at all.** Monitors, laptops, lamps, plants, cups, cables, shelves on the wall, ceiling lights and fans, pendant lamps, open cupboard doors, a second chair, people and pets. The dock hangs 0.5 m below the ceiling exactly where pendant lamps and fans are. No check in this code can see any of these. This is why the user's confirmation of the previewed spot and a Cancel / Retract that works at any moment have to stay in the design, and why item 5 of section 5 matters.
- **How accurate the volumes are.** The margins (0.08 m, 0.05 m, 0.30 m high up) were chosen on paper. Whether scanned boxes are within a few centimetres of the real furniture, or off by ten, decides whether those margins mean anything.
- **Whether the labels are useful to show.** The explanation names the first blocking volume by its MRUK label ("TABLE", "STORAGE", "SCREEN", "OTHER"). Whether the user can tell which object that is has not been tried.
- **Where the user really is.** The keep-out is centred under the headset. Seated posture, leaning, and the chair itself (only avoided if the scan reports it) are not modelled, and the head pose is read once, at Confirm.
- **Comfort and reach at the new inspection pose.** 0.85 m to the side instead of 0.6 m, about 80 degrees off forward. Whether the office is readable and its keys reachable there, and whether 0.35 m is the right radius, needs a person in a headset.
- **Cost on the device.** One evaluation is a fraction of a millisecond on a PC. It runs when a preview opens, on Next, on a room event and on recenter, never per frame; it has not been timed on Quest.
- **Recenter and restart.** Whether a saved dock still passes `ValidateDock` after a restart depends on the room reference and the user's seat being where they were; both are untested on a device.
