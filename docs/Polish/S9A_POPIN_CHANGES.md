# S9a Remaining pop-ins

Branch feature/living-world-polish. Presentation only: no task state, wire or server behaviour changed, no scene or prefab
touched, ReleaseConfig untouched. Compiled in the Editor (`compilationFailed` false) and run in Play Mode on the desk demo.
Nothing is headset-verified. Motion library used throughout: `MuseDesk.Motion` (MuseMotionProfile tokens `panelHide`,
`panelAppear`, `Damped.Follow/FollowAngle`, `ProfiledMover`, `MotionSettings`). New pure logic lives in
`Scripts/Spatial/Core/Motion/PresenceFader.cs` (`PresenceFader`, `PresenceDiff`) and `Scripts/Agents/Core/AgentWalkCorners.cs`,
both in assemblies the EditMode tests can reach.

Evidence levels: AUTO = EditMode test, PLAY = ran in Play Mode, REVIEW = code-reviewed only.

## 1. Card recycle pop
- Issue: a card whose task left the board disappeared at full size (`BoardPresenter.Recycle`: `SetActive(false)`).
- Cause: recycle was a direct deactivate.
- Fix: `Recycle(taskId, view)` keeps the card in a `_leaving` map and calls `TaskCardView.BeginHide()`: scale eases to
  `ScaleEase.HiddenFactor` (0.001, never zero) on `panelHide`, colliders under the card (grab and poke) are switched off at once
  and remembered. When the hide finishes the card raises `HideFinished`; only then does the presenter deactivate and pool it.
  A leaving card is not in `_views` (so commands and the toolbar cannot reach it), is not in the pool, gets no layout slot and
  is not moved by layout. If the same task id comes back (next `Upsert`) while it is still `Hiding`, `CancelHide()` grows it
  back from its CURRENT scale on `panelAppear` (proportional time, no jump), colliders return, and its pending completion
  can no longer fire (completion is only reported while the phase is Hiding, and the handler also checks `_leaving`). A card
  that already finished hiding is pooled and a returning task rents a fresh view with the normal reveal. `ResetPresentation`
  (run from OnDisable) snaps the pooled card whole again: full scale, colliders on, details sheet hidden.
- Test: AUTO `PresenceTests` (hide ends exact and reports once; show during hide continues from the current factor and never
  reports a hide completion; interactable only when fully shown; reduced motion). PLAY: remove-then-rebuild showed the card
  `leaving`, then pooled (inactive) within a second; remove-then-restore in the same frame revived the same instance.

## 2. Details-sheet pop
- Issue: `TaskCardView.SetExpanded` toggled `detailsRoot.SetActive`.
- Cause: SetActive on an object that holds Interaction SDK poke keys.
- Fix: a second `PresenceFader`. Expanding activates the root once, still at the hidden scale (pokes off), then grows on
  `panelAppear`; colliders return only when fully shown. Collapsing turns colliders off at once and shrinks on `panelHide`; it
  is never deactivated again (hidden = scale `HiddenFactor`). `ApplyDetailsScale` multiplies the authored local scale, so the
  final state is exact. A root that is authored active is treated as shown until told otherwise.
- Test: AUTO via `PresenceFader`. PLAY: the live card prefab has no caller that opens the sheet (only Recycle collapses it), so
  the expand path ran in no Play Mode scenario. REVIEW only for expand.

## 3. RebuildAll re-reveal
- Issue: a snapshot or resync recycled every card and re-created them, so every card re-revealed (and flashed).
- Fix: `PresenceDiff.Compute(currentIds, reducerIds)` gives added / removed / kept. Only removed ids are recycled (hide, see 1);
  kept cards keep their view, transform and glide to any new slot through the existing `Upsert` / `MoveToRegion` /
  `cardRelease` path; only an id that was not on the board (and is not reviving) gets `BeginReveal`.
- Test: AUTO `PresenceTests` diff cases (new, gone, kept, identical snapshot, duplicates, empties). PLAY: a resync kept all
  four card instances at their scale, with no reveal.

## 4. Desk routed-walk corners
- Issue: a corner sharper than `walkTurnLimit` was taken while walking (the Wall walker brakes and turns in place).
- Fix: `AgentWalkCorners.NextStopDistance` finds the next corner sharper than `MuseMotionProfile.walkTurnLimit` (50 degrees)
  beyond the current distance. The existing `ProfiledMover` now targets that corner distance instead of the end, so the agent
  brakes to rest EXACTLY on the authored waypoint, turns on the spot (`Damped.FollowAngle`, bounded to 1 s, done within 2
  degrees), then continues to the next stop. The polyline, waypoints and positions are unchanged. Cruise speed, `walkRateFloor`
  and the S3/S4 damped facing are unchanged; the walk takes longer by the stops and turns (about stopSeconds + startSeconds
  + the turn per sharp corner). A re-target mid-walk cancels a corner turn. Agents stand still during the turn with
  `PlanarSpeed` 0 (as for the existing start turn).
- API for a later Turn_Left / Turn_Right task (`Assets/MuseDesk/Scripts/Agents/AgentMovement.cs`):
  `public int TurnInPlaceDirection { get; }` (-1 left, +1 right, 0 none), `public bool IsTurningInPlace`,
  `public event Action<int> TurnInPlaceChanged` (new direction, 0 when the turn ends or is abandoned). It covers the turn
  before the first step and corner turns. Positive = turning right (yaw increasing). No animator is driven here.
- Test: AUTO `AgentWalkCornersTests` (angles, next stop sequence, below-limit corners walked through, polyline unchanged, turn
  sign across the 0/360 seam, out heading). PLAY: demo run showed turn events on Researcher and Developer
  (`[S9a TURN] ... dir=-1/+1` then `dir=0`) with 0 console errors. Speed and stop precision were not measured numerically.

## 5. Attention sign y step
- Issue: `AvoidLift` (and the clear-of-neighbour rule) set the sign's y directly, so it could jump several centimetres in one frame.
- Fix: `AgentAttention.SmoothLift` smooths only the OFFSET above the head-following base position with `Damped.Follow`
  (0.12 s, halved under Reduced Motion, `SafeStep` inside). The sign still tracks the head at once; the smoothed lift is
  re-seeded when a sign first appears or its anchor jumps. Rotation and the rest of the position are untouched.
- Test: REVIEW + PLAY smoke (no errors). No numeric check of the y step. (A first version indexed an unsized array and threw
  every frame; fixed before this commit, the Play run after it was clean.)

## Risks / not verified
- All Editor only. No headset look. Card hide timing, the overlap of a shrinking card with a neighbour sliding into its old
  slot, and the corner stop feel need a worn check.
- Colliders under a hiding card are disabled directly (`Collider.enabled`), not the ISDK interactable components; a hand that
  already holds the card when its task leaves loses it when the colliders go off.
- A hide does not wait for the card to be released (the previous recycle was instant, so this is not worse).
- Walks with sharp corners are longer than before; the delivery timings in demo runs shift by the stop and turn time.
- The pooled card keeps its details sheet at `HiddenFactor` scale while active, with colliders off.
- `AgentScripts/PopInDrive.cs` is a Play Mode driver only (Cards, Resync, RemoveRestoreEarly, HideThenRevive, Turns, HookTurns).
