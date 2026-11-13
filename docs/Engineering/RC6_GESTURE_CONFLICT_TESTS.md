# RC6 gesture conflict tests

2026-11-13. Hand ownership (one owner per hand) and deliberate floor placement. Evidence levels: **AUTO** = EditMode test, pure logic; **PLAY** = Editor Play Mode driver with simulated hands (no headset, no real hand tracking, synthetic room). **Nothing here has been worn.** The Interaction SDK side (which interactor hovers or holds what, read by `HandInteractionProbe`) is exercised only by simulated flags in both levels.

Fixtures (run one at a time with `scripts\tests.ps1 <Fixture>`): `HandOwnershipTests` 8, `CeilingPullOwnershipTests` 6, `FloorAimOwnershipTests` 4, `PlaceHereKeyTests` 2, `FloorAimSessionTests` 9, `FloorPlaceHereTests` 13 (replaces `FloorReleaseToPlaceTests`). Drivers: `FloorAimDrive`, `CeilingPullDrive` (`scripts\drv.ps1 <Class> <Method>`).

## Ownership rules and thresholds

Priority: 1 something held (either hand, or a paper's transition hold) > 2 close-range grab / poke / UI ray (hover or select, and for `nearClearSeconds` after) > 3 placement control (floor footprint drag) > 4 targeted summon (ceiling pull) > 5 ambient.

| Setting (`HandOwnershipSettings`) | Value | Why |
|---|---|---|
| `minPinchSeconds` | 0.15 s | ISDK reports a grab/poke hover 1-3 frames after the hand reaches an object and pinch strength takes ~50-100 ms; 11 frames lets ISDK claim the pinch first and stays under ~0.2 s drag-start latency |
| `nearClearSeconds` | 0.30 s | the hand must have been tracked and free of any hold/hover/UI for this long BEFORE the pinch begins (and stay so while it debounces); covers hover flicker while fingers close; re-aiming from a desk object to the floor or the pod takes longer |
| `minOpenSeconds` | 0.08 s | a hand must be seen open 6 frames for the next pinch to be fresh; a 1-2 frame dropout does not make a new pinch |
| `releaseDebounce` | 0.12 s | a claimed pinch must read open (tracked) this long to be a release (same as the old floor aim) |
| `lostGraceSeconds` | 0.20 s | untracked within this: frozen; longer, or back open: cancelled, never a release |
| Pull `targetLostSeconds` / `targetKeepFactor` | 0.25 s / 1.5x radius | the hand ray swings as it pulls down; a short gap and wider radius are allowed before the pull is cancelled |

## Scenarios

| Scenario | Expected | Evidence | Result |
|---|---|---|---|
| Pinch while holding a paper / stamp / card (this hand) | never arms floor drag or pull | AUTO `HandOwnershipTests.PinchWhileHoldingAPaperStampOrCard_NeverArms`, `FloorAimOwnershipTests.APinchWhileHoldingAPaperStampOrCard_NeverGrabsTheFootprint`, `CeilingPullOwnershipTests.APinchWhileHoldingAPaper_DoesNotArm_AndSaysSo` | pass |
| Pinch while the OTHER hand holds something | never arms | AUTO `HandOwnershipTests.PinchWhileHoldingAPaperStampOrCard_NeverArms` (case b) | pass |
| Held paper (transition hold + grab) pinch on the footprint, drag 0.5 m | grabs 0, footprint and root unchanged | PLAY `FloorAimDrive.HeldPaperPinch`: owner=Holding block=Holding grabsAdded=0 footprintMoved=0.000 rootMoved=False | pass |
| Held paper pinch on the docked pod, pull 14 cm | no arm, pod Docked, words "Put down what you are holding first." | PLAY `CeilingPullDrive.HeldPaperPinch`, `.Held`: armed=Idle owner=Holding firedNow=0 | pass |
| Pinch while hovering a close object (incl. hover reported 3 frames late; hover ended 0.1 s before pinch) | spent, never arms | AUTO `HandOwnershipTests.PinchWhileHoveringACloseObject_NeverArms_EvenWhenTheHoverIsLate`, `FloorAimOwnershipTests.APinchWhileHoveringAClose_Object_OrOverAUiKey_NeverGrabsTheFootprint`; PLAY `CeilingPullDrive.BusyHand` (block=CloseInteraction, firedNow=0) | pass |
| Poke on a UI key, then pinch | spent (block Ui) | AUTO `HandOwnershipTests.APokeOnAUiKey_ThenAPinch_DoesNotArm`, `FloorAimOwnershipTests` (ui case); PLAY `CeilingPullDrive.UiThenPinch` (block=Ui, firedNow=0) | pass |
| Pull with no target (hand ray and gaze off the pod), or pinch begun off the pod and swept onto it | never arms | AUTO `CeilingPullOwnershipTests.APullWithNoTarget_NeverArms`; PLAY `CeilingPullDrive.NotTargeted` (armed=Idle, firedNow=0) | pass |
| Target lost mid-pull | cancelled (TargetLost), nothing fires; a gap under 0.25 s is tolerated | AUTO `CeilingPullOwnershipTests.TheTargetLostMidPull_Cancels_AndNothingFires`; PLAY `CeilingPullDrive.TargetLost` (end=TargetLost, pod Docked->Docked) | pass |
| Tracking lost mid-drag | drag called off, preview back where it was, never a release | AUTO `FloorAimSessionTests.LosingTheHand_DuringADrag_CallsItOff_ThePreviewGoesBackWhereItWas`, `.AHandThatIsLostAndComesBackOpen_IsACancel_NotARelease`, `HandOwnershipTests.TrackingLost_FreezesThenCancels_AndIsNeverARelease` | pass |
| Tracking lost mid-pull (past the full distance) | cancelled (Lost), never fires | AUTO `CeilingPullOwnershipTests.TrackingLostMidPull_Cancels_AndIsNeverARelease`; PLAY `CeilingPullDrive.TrackingLostMidPull` (p=1.00, afterLoss=Lost, firedNow=0) | pass |
| Fresh pinch required (after any cancel; pinch already closed; 1-frame open flicker) | not ready until open >= 0.08 s then closed | AUTO `HandOwnershipTests.AfterAnyCancel_AFreshPinchIsRequired`, `CeilingPullOwnershipTests.AfterACancel_TheNextPullNeedsAFreshPinch`, `FloorAimSessionTests.ATap_APinchThatWasAlreadyClosed_AndAPinchThatBeganElsewhere_DoNothing` | pass |
| Something picked up mid-drag / mid-pull | cancelled | AUTO `FloorAimOwnershipTests.PickingSomethingUpMidDrag_CallsTheDragOff`, `CeilingPullOwnershipTests.APinchWhileHoldingAPaper_DoesNotArm_AndSaysSo` (case b) | pass |
| One owner per hand (placement outranks summon) | summon preempted, cannot take a hand the placement holds | AUTO `HandOwnershipTests.OneOwnerPerHand_PlacementOutranksSummon` | pass |
| Release without confirm (drag + let go) does not move | footprint stays, stage NeedsConfirmation, Place here key shown, 0 poses written | AUTO `FloorAimSessionTests.PinchOnTheFootprint_ThenMove_DragsIt_LettingGoOnlyLeavesItThere_AndPlaceHereMovesIt`, `FloorPlaceHereTests.LettingGoOnAClearSpot_MovesNothing_PlaceHereMovesItAlongTheSweptRoute`; PLAY `FloorAimDrive.DragAndRelease` (rootMoved=False, stageAfter=NeedsConfirmation, placeKey=shown), `.ReleaseBesideShoulder` | pass |
| Place here moves along the validated route | route re-planned at press, every written pose on the swept route | AUTO same two tests + `ReasonableSpotsBesideTheShoulder_WereRefused_AndAreNowPlaced`; PLAY `FloorAimDrive.PlaceHere` via the card key: 4-pose route swept clear, root at the spot, stage Placed | pass |
| Confirm with the route now blocked does not move | refused, footprint stays (RouteBlocked), key hidden, reason shown | AUTO `FloorPlaceHereTests.ARouteBlockedAtConfirmTime_MovesNothing_AndTheSpotStaysWithTheReason`; PLAY `FloorAimDrive.ReleaseWhereBlocked` (stageWhileDragging=RouteBlocked, rootMoved=False) | pass |
| Confirm during a drag | refused | AUTO `FloorPlaceHereTests.ConfirmOrPlaceHere_DuringADrag_IsRefused` | pass |
| Confirm / Desk while anything is held | refused with words; Stop is never blocked | AUTO `FloorPlaceHereTests.PlaceHere_WithAPaperOrAnythingInHand_IsRefused_AndKeepsTheSpot` | pass |
| Confirm then Stop reverses | goes back along the travelled poses, mode back to Desk, stage Returning | AUTO `FloorPlaceHereTests.PlaceHere_ThenStop_ReversesAlongTheTravelledPoses_AndNothingIsSaved` | pass |
| Persisted only after success | no record while previewing or moving or after a stop; saved on arrival | AUTO `FloorPlaceHereTests.ThePlacement_IsPersistedOnlyAfterTheOfficeArrived`, `.PlaceHere_ThenStop_...`; existing `SpatialPersistenceTests` 5 | pass |
| Room scan changes mid-move and blocks the route | re-swept, goes back | AUTO `FloorPlaceHereTests.ARoomChangeDuringTheMove_ThatBlocksTheRoute_SendsItBack` | pass |
| Hold a pinch still on the footprint | nothing (the hold-to-confirm is gone) | AUTO `FloorAimSessionTests.HoldingAPinchStillOnTheFootprint_NeverConfirms`; PLAY `FloorAimDrive.HoldStill` (officeRootMoved=False) | pass |
| Twitch / flick | never moves | AUTO `FloorAimSessionTests.ATwitch_OrAQuickFlick_NeverMovesAnything`; PLAY `.TwitchRelease`, `.FlickRelease` (rootMoved=False) | pass |
| Accidental gesture never relocates the workspace | five pinch shapes, 0 poses written; only Place here moves | AUTO `FloorAimOwnershipTests.AnAccidentalGesture_NeverRelocatesTheWorkspace` | pass |
| Look-at guide (CentrePreviewOn) only previews | preview centred, stage NeedsConfirmation, 0 writes; never ends a user drag | AUTO `FloorPlaceHereTests.TheGuideHook_CentresThePreviewWhereTheUserLooks_AndOnlyPreviews` | pass |
| Card keys during a floor preview (also when the preview was opened without the guide) | Place here only while CanPlaceHere; Other spot, Cancel always | AUTO `PlaceHereKeyTests` 2; PLAY `FloorAimDrive.Report` after `Open` (guide=Previewing placeKey=shown anotherKey=shown) | pass |
| Ceiling pull positive path still works | pull down fires, push up fires | PLAY `CeilingPullDrive.PullDown` (Docked->PreparingToDescend), `.PushUp` (back to Docked) | pass |

Regression fixtures re-run after the change, all pass: `FloorAimDirectorTests` 9, `FloorAimMathTests` 5, `CeilingPullTests` 14, `SpatialDirectorTests` 15, `SpatialPersistenceTests` 5, `SpatialSweptMovesTests` 17, `SpatialCeilingSweptPathTests` 19, `SpatialCeilingPodTests` 8, `SpatialCeilingEvaluatorTests` 5, `SpatialFloorEvaluatorTests` 7, `SpatialTransitionMachineTests` 5, `SpatialIsolationTests` 3, `ModeConsoleLogicTests` 9, `PlacementGuideLogicTests` 13, `SpatialReach*` 32, `CeilingPolishAttentionTests` 7, `PanelReachTests` 9, `PanelHeightTests` 8.

## Not verified

- Real Interaction SDK state: which interactors the rig has, their hand side, and whether `HasInteractable` / `HasSelectedInteractable` flip in time on device (`HandInteractionProbe.Describe()` lists them in Play Mode).
- Feel: 0.15 s arm delay, 0.3 s clear window, card at 0.55 m and its key reach, target keep radius during the pull. Needs a wearer.
