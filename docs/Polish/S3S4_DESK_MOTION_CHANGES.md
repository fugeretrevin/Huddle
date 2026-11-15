# S3/S4 Desk, Floor and Ceiling motion changes

Branch feature/living-world-polish. Presentation only: no state, wire or server behaviour changed. Compiled (no CS errors) and
run in Play Mode once (demo task start, take paper, approve: 0 console errors). Nothing is headset-verified. Motion library
used throughout: `MuseDesk.Motion` (MotionClock, ProfiledMover, Damped, MuseMotionProfile tokens).

Evidence levels: AUTO = EditMode test, PLAY = ran in Play Mode, REVIEW = code-reviewed only. No new EditMode test: every
change lives in Assembly-CSharp (outside MuseDesk.Spatial.Core, the only runtime assembly the tests reference).
Regression fixtures that pass: SpatialIsolationTests 3, HandOwnershipTests 10, BoardPresenterLogicSpecTests 22,
DemoGatewayDecisionSpecTests 5, MotionTests 32, AgentWalkPathTests 5.

## M5/M6 Board cards
- Root cause: `BoardPresenter.cs:181-193` (Upsert/Rent) and `TaskCardView.cs:456-494` (ReturnHome glided only if `_displaced`;
  MoveToRegion kept the world pose only for displaced/held cards), so lane changes and lane-neighbour reflow snapped, and a
  new card appeared at full size.
- Fix (`TaskCardView.cs`, `BoardPresenter.cs`): a card that has rested in a slot (`_placed`) and whose slot changed glides
  with the `cardRelease` token (InOutCubic, replaces the old 0.2 s ease-out for drops too); `MoveToRegion` keeps the world pose
  for placed cards and marks them displaced so the layout eases them; a glide already heading to the same slot is not restarted
  by repeat layouts; `BeginReveal()` grows a new card from 0.001 with `panelAppear` (scale multiplier in UpdateMotion). A held
  card is never touched: layout still skips `IsHeld`, UpdateMotion's held branch cancels glide and reveal.
- Evidence: REVIEW + PLAY smoke (demo run, no errors; screenshot `docs/review_screenshots/polish/s3_desk_motion_play.png`).
- Risk: RebuildAll (snapshot/resync) re-reveals every card. Card recycle (disappear) and the details-sheet SetActive are not
  changed (not in scope). The glide moves the card through other lane cards' space for 0.28 s.

## M7 Desk agent walk
- Root cause: `AgentMovement.cs:107-165`: SmoothStep on normalised time, `Slerp(dt*k)` facing that never converges, hard snap of
  position and rotation at arrival.
- Fix: distance along the same polyline (`AgentWalkPath`, waypoints untouched) driven by `ProfiledMover` (accelerate over
  `startSeconds`, brake to rest exactly on the target over `stopSeconds`, cruise speed set so the walk still takes about
  `_dur`); yaw by `Damped.FollowAngle(turnSmoothTime)`; walks that start more than `walkTurnLimit` off heading turn in place
  first (bounded 1 s); no arrival snap, position lands exactly via the profiler; after a walk home the station pose is reached
  by a damped turn then set exactly. Delivery arrival hands rotation to FaceUserWhileDelivering as before; callbacks and
  `AtHome` fire at position arrival as before. Re-targeting mid-walk keeps current speed. Walk bob now scales with speed and
  follows distance.
- Walk cycle: `AgentAnimatorBridge.UpdateWalkRate` already follows `PlanarSpeed`; the lower clamp (0.7) made a slow start play
  0.7x strides. New `walkRateFloor` (0.2) lower bound, and slowing follows the body immediately (speeding up still eases).
- Evidence: REVIEW + PLAY smoke (walks ran, no errors). Speed curve not measured numerically.
- Risk: corners on routed walks above `walkTurnLimit` turn while walking (no stop at corners). Cruise speed can be up to about
  1.2x `maxRoutedSpeed` (old SmoothStep peak was 1.5x average). The 0.2 floor and the in-place stepping during the start turn
  need a headset look. Upright agents assumed (yaw-only rotation writes, exact `localRotation` at the end).

## M8 Handoff token
- Root cause: `AgentHandoffController.cs:236-267`: SetActive pop at the source hand, re-parent to the receiver's hand in one
  frame, SetActive(false) pop.
- Fix: attach at the source hand collapsed (scale set before activation) and grow with `panelAppear`; at the exchange a
  0.42 s InOutCubic world-space arc (3 cm lift) from the giver's hand to the receiver's hand socket, then re-parent in place
  (no jump); the flight counts toward `exchangeHold` so total timing is unchanged; hide shrinks with `panelHide` then
  deactivates. Abandon/reset/OnDisable fallbacks unchanged (they still deactivate directly and StopAllCoroutines).
- Evidence: REVIEW only (handoff did not run in the PLAY session).
- Risk: a token left parented to the frame if a coroutine is killed mid-flight is re-parented by the next AttachProp.

## M9 Decision paper
- Root cause: `DocumentDeliveryController.cs:75,108-109,153,162` SetActive blink on show and hide.
- Fix: `ShowPaper()` keeps the OnEnable re-trigger, then reveals with `panelAppear`, skipped if a hand holds the paper
  (`HeldPaperController.IsHeld`); aborts if the user grabs mid-reveal. Full-size papers use a new `RevealFactor` on
  `HeldPaperController` (its LateUpdate scale lock would otherwise fight); in-hand papers scale the transform directly. Hide
  (when not filed) shrinks with `panelHide`, waits while held, and aborts if the decision is still pending
  (`_responding`, `IsRespondingTo`, or `PendingDecisionId == current`). The existing filing glide path is unchanged. Session
  reset still hides at once (the whole session is cleared).
- Evidence: REVIEW + PLAY smoke (TakePaper and Approve ran, no errors; `s3_desk_paper_play.png`).
- Risk: hide waits unbounded while held (intended). Paper cover/sheet SetActive swap is untouched.

## M10 Two writers
- (a) `WorkspacePlacement.RefaceUser`: loop now exits when mode is not Desk or a transition runs. REVIEW.
- (b) `RubberBandReturn`: it already yielded while `IsMoving`; added `[DefaultExecutionOrder(20)]` so it always runs after
  AgentMovement and re-homes from the true arrival pose. AgentMovement's post-walk settle writes rotation only. REVIEW.
- (c) `PushableProp`: while `SpatialWorkspaceState.IsTransitioning` it only freezes its body and tracks the parent (the
  carrier owns the pose), then settles home once after the transition, instead of treating the office travel as per-frame
  "jumps". `WorkspacePropCarrier` untouched. REVIEW. Risk: a prop held by a hand during a transition is frozen kinematic.

## Not done
- Not run: handoff flight, board lane change under backend events, floor/ceiling transitions, headset.
- RefaceUser still ends with a slerp (no exact final yaw); not requested.
