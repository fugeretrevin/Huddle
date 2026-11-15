# B. Hand interaction: input ownership and arbitration (audit, read-only)

Scope: Assets/MuseDesk/** (Scripts, Phase2). No code was edited. Evidence is static reading only (no PLAY/WORN claims).
Paths below are relative to `Assets/MuseDesk/`.

## 1. What the existing arbiter is, and covers

`Scripts/Spatial/Core/HandOwnership.cs` has three parts.

- `HandClaimRegistry` (l.94-123): one claim slot per hand. `HandClaim` is only `Placement=3, Summon=4, AgentSelect=6` (l.28). `TryClaim` succeeds if the slot is free, already the same claim, or the new claim number is lower (l.109). A static `Shared` instance (l.97) is the app registry.
- `HandOwnershipArbiter` (l.145-): per-hand pinch gate. It only emits a `Ready` signal for a pinch that is fresh (hand seen open 0.08 s), debounced (0.15 s), and clear (nothing held anywhere, no grab/poke/ray hover for 0.30 s before the pinch began). `Ready` is valid for one tick; an unclaimed `Ready` is spent (l.300). After a claim: `Holding`, `Frozen` (tracking lost <= 0.20 s), `Released` (only when a TRACKED hand is seen open for 0.12 s), `Cancelled` (tracking lost longer, hand returns open, anything picked up, preempted by a higher claim). A lost hand is never a release (l.302-322, 326-331, 334).
- `HandInteractionProbe` (Scripts/Spatial/HandInteractionProbe.cs) READS every `IInteractor` in the scene. Grab-type (type name contains "Grab", l.137): selected => `holding`, hovering => `nearHover`. Poke or Ray: any hover or select => `uiHover` (l.112-114). It also OR-s `SpatialWorkspaceState.TransitionHeld` (a paper's transition hold; l.71, 217).

So the arbiter answers one question: "may a FAR/ambient pinch gesture arm now?" It gates gestures that MOVE THE OFFICE or select agents. It does not mediate between near ISDK interactables, nor between consequential command handlers.

Consumers (the only ones that call `TryClaim`):

| Consumer | Claim | File:line |
|---|---|---|
| Floor footprint drag | Placement | Core/FloorAimSession.cs:273 (arbiter ticked :264; controller feeds `HandInteractionProbe.Apply` at Spatial/FloorAimController.cs:165-175) |
| Ceiling pull | Summon | Core/CeilingPullLogic.cs:322-323 (ticked :303; Spatial/CeilingPullGesture.cs:187-202) |
| Wall Select Agents carry | AgentSelect | Wall/WallHabitatController.Interaction.cs:112 |
| Wall placement ghost aim | Placement | Wall/WallHabitatController.Interaction.cs:119-120 |

Everything else in section 2 does NOT consult it. Most of those are near ISDK interactables, which is by design: they are protected indirectly because the probe makes the far gestures yield to them (holding/hover blocks arming).

## 2. Inventory

Legend: Arb = consults arbiter. Trk = cancels on tracking loss. Dbn = debounce / duplicate-submit protection. Stages: H hover, E engaged, P pending, C committed, F failed/refused.

| Handler | Input | Object | Arb | Trk | Dbn | Stages |
|---|---|---|---|---|---|---|
| HeldPaperController (Documents/HeldPaperController.cs:206-225) | grab (Grabbable, reflection on SelectingPointsCount) | decision paper | no (seen by probe as holding) | n/a (grab release has no consequence; keys are not driven by it) | `ReadyForDecision` gate :147 (taken, unfolded, 0.8 s readable) | H (GrabHoverHighlight), E (expand on grab); no P/C/F needed |
| DocumentDeliveryController OnApprove/OnReject/OnRequestChanges (Documents/DocumentDeliveryController.cs:187-189) | poke keys on paper (and voice, stamp) | decision | no | n/a (poke) | `_responding` via `BeginResponse` :168-176 (clears on resolve :116, fail :82, new decision :99) | H/E (KeyStateVisual), P (`panel.SetResponding` :174), C (resolved plate), F (`SetError` :83) |
| StampTool (Documents/StampTool.cs:239-342) | grab (stamp) + proximity/dwell press | Approve and Reject stamps | no | grab drop on tracking loss => `held` false => dwell reset, nothing fires (:244-246); safe | static `s_handledDecision` :76,305,314; 0.12 s dwell; 1.0 s cooldown; shares `_delivery.ResponseInFlight` :325 | H (near highlight via NotifyStampNear :290), E (dip + thunk), P (pale provisional mark :336), C (dark mark on `decision.resolved`), F (re-arm + "nope" :129-133) |
| ApprovalStampController (Documents/ApprovalStampController.cs) | none (visual only) | paper mark | n/a | n/a | n/a | P/C/F visuals only |
| VoiceCommandCenter decision (Voice/VoiceCommandCenter.cs:684-714) | voice (not hand) | decision | n/a | n/a | undo window `_pendingDeadline` :694; id re-check :705; `Busy` state VoiceOffice.cs:285 | P (pending readout), C, F |
| VoiceGestureActivator (Voice/VoiceGestureActivator.cs:41-70) | custom pinch (raw fingertip distance, mouth zone) | microphone | NO | resets `_held` when pinch not seen (:64) | 0.5 s hold, 2.5 s cooldown :68; default OFF :23 | no visual progress; `Progress` property exists but nothing reads it (grep) |
| TaskDragController (Phase2/Runtime/Board/TaskDragController.cs:52-72, 106-136) | grab (Grabbable) | task card | no (probe sees holding) | NO for tracking loss (see C2); OnDisable/Pause/Focus only :225-227 | `_resolving` :54,157; per-attempt key :163 | H (EdgeState Hover/Selected/Held, zone highlight :83), E (Held/Candidate), P (`ShowPending` + glide :160,493), C (`ShowResult`), F (red halo, `ReturnHome`, refusal words :133, :183) |
| BoardPresenter poke commands (Phase2/Runtime/Board/BoardPresenter.cs:301-320) | poke (assign pads, pause/resume chip, priority chip on card) | task card | no | n/a | NONE per card; fresh key each poke :303,311,319 | P (`ShowPending` :432,493), C, F (`ShowResult`) |
| TaskDock.StartReadyTask (Phase2/Runtime/Board/TaskDock.cs:421-474) | poke | dock Start key | no | n/a | `_startPending` :423,450 (dock-local only) | P ("Starting" :452), C, F (refusal words :472) |
| TaskDock.ToggleBoard (:407-417) | poke | Board key | no | n/a | refuses while a card is held :410 | F (notice) |
| ModeConsole keys (Spatial/ModeConsole.cs:354-391; pokes gated :323-336) | poke | mode keys (Desk/Floor/Ceiling/Next/Confirm/Cancel/Stop/Pod/Bring/Status) | no | n/a | `Refused()` :340; `enableAt` arming delay :332; `IsTransitioning` guards :350, :191 | H (KeyStateVisual), F (`FlashRefused` :343,351,386), C = the transition itself; no P on the key |
| PlacementGuide (Spatial/PlacementGuide.cs:44, 224) | poke | Place here / guide keys | no | n/a | `IsTransitioning` guard :224 | partial (status line) |
| MovablePanel (Spatial/MovablePanel.cs:184-186, 231, 368-392) | grab (grab bar proxy) + custom long pinch | panels | no (probe sees holding) | tracking loss => grab ends => `EndDrag(false)` saves position :186,271 (benign) | suspends pokes of the panel during drag :231,373 and resumes after delay :272 | H/E (bar colour :399), reset chip after 1.5 s hold :360 |
| PanelResetChip (Spatial/PanelResetChip.cs) | poke | reset layout | no | n/a | hides on move (:356) | chip itself |
| WorkspacePropCarrier (Spatial/WorkspacePropCarrier.cs:194,259) | reads grab state of props | desk props | no | prop becomes `free` when held :197,261 | n/a | none |
| GrabbableRigidbody / PushableProp / GrabHoverHighlight / GrabReadout (Scripts/Workspace) | grab / touch | props | no | n/a | n/a | H/E |
| CeilingPullGesture (Spatial/CeilingPullGesture.cs:131-219) | far pinch + ray/gaze (custom) | pod | YES (Summon) | `Frozen` <= 0.2 s then `Cancelled` -> "Hand lost. Nothing moved" :248 | fresh pinch + 0.15 s + clear 0.30 s; cooldown in machine | H (cue on pod), E (fill), F (reason text :238-248), C (Fire :210) |
| FloorAimController / FloorAimSession (Spatial/FloorAimController.cs; Core/FloorAimSession.cs:242-323) | far pinch + ray | floor footprint | YES (Placement) | Cancelled => `CancelAimDrag` :302; Released never from tracking loss | same gate | H (cursor/ray), E (drag), C needs the separate Place here key (:309-311 "Nothing moves") |
| Wall Select Agents carry (Wall/WallHabitatController.Interaction.cs:104-126) | far pinch + ray pick | agent projection | YES (AgentSelect) | Cancelled => `EndCarry(false)` "Hand lost... Nothing changed." :126,209 | gate + `_visit.Grab` | H (`_hoverRole`), E (proxy), F (Note texts) |
| Wall placement ghost aim (same file :115-131) | far pinch + wall raycast | ghost | YES (Placement) | Cancelled drops aim :131 | gate | H/E |
| WallUi keys (Wall/WallUi.cs:14-52, 189-194) | poke | Wall panel keys | no | n/a | NONE (`Press` just calls action) | text Note only |
| PressSelectAgents (Wall/WallHabitatController.Interaction.cs:55-61) | poke | toggle | no | n/a | none (second poke disarms) | Note text |
| SummonController ray targets (Spatial/SummonController.cs:205-218) | ray select (RayInteractable) | beacon, agent stand-ins | no (probe makes far gestures yield) | n/a | none here; `SummonRequests` is plain static events (Spatial/SummonRequests.cs:35-57) | none |
| SummonController PressBring/PressStatus (:189-201) | poke | Bring/Status keys | no | n/a | `IsTransitioning` guard | none |
| AgentAttention poke/wave/glance (Agents/AgentAttention.cs:602-640, 626, 662-674) | custom (fingertip proximity, wave detector) via HandFingertips | agents | no | `!valid` resets :614-617 | `_pokeCd` 0.3 s :626; `waveCooldown` 4 s :707; `_glanceCd` | H (glance), E (bounce/wave), status bubble :701 |
| AgentTilt / AgentGaze / AgentArmNudge (Agents/*.cs, HandFingertips) | custom proximity | agents | no | n/a | n/a | cosmetic |
| FeedbackController key cues (Feedback/FeedbackController.cs:231-238) | observes all PokeInteractable state | all keys | no | n/a | cue dedupe window | audio/haptic cue |
| ArchiveStackPoke, ProjectShelfController, VoiceOffice keys | poke | misc | no | n/a | not individually inspected | not inspected |

Not found anywhere in Assets/MuseDesk/Scripts: any DistanceHandGrab, TouchHandGrab or custom pinch recognizer other than VoiceGestureActivator (the Distance/Touch grabs are on the rig, read only by the probe's type-name match, HandInteractionProbe.cs:137). No handler subscribes to ISDK `WhenPointerEventRaised`; all grab consumers poll `SelectingPointsCount` via reflection (Workspace/GrabState.cs).

## 3. Conflicts (one pinch / one hand reaching two handlers, or tracking loss counted as confirm)

C1. Registry slot is shared by VALUE, not by owner (HandOwnership.cs:109). Two arbiters claiming the same `HandClaim` on the same hand both succeed (`cur == claim`). Floor aim and Wall placement both use `Placement` through separate arbiters on `Shared` (FloorAimSession.cs:273, Wall...Interaction.cs:120). Today they cannot both be live (floor `CanAim` is false in Wall; Wall aim needs `WallPreviewing`), so it is latent, but nothing enforces it and the Wall arbiter is not even ticked outside select/aim mode (Interaction.cs:79-87).

C2. Tracking loss counts as a RELEASE for a task card drop. `TaskDragController.Update` treats `!held` as release (:66) and `OnReleased` sends the assign if the card sits over a valid zone (:116-127). The class header promises "tracking loss ... returns home and nothing is sent" (:20-21), but the only cancel hooks are OnDisable / OnApplicationPause / OnApplicationFocus (:225-227). ISDK drops the grab when the hand is lost, so `held` flips false with no way to tell it from a real release. Result: a hand dropout while a card hovers an agent pad (Candidate) submits an assign. This is the one place where "tracking loss = confirm" is concrete. (The stamp is safe: it only fires while held, StampTool.cs:244-246.)

C3. Priority order in the registry differs from the intended precedence. Numeric order is Placement(3) < Summon(4) < AgentSelect(6) (HandOwnership.cs:25-28), so placement beats summon beats agent selection. The requested order puts the agent-selection drag above placement. Today Wall mode excludes Floor/Ceiling (comment :21-24), so only Wall aim (Placement) vs Wall carry (AgentSelect) can collide: both are gated by `aimMode` / `selectMode` (Interaction.cs:77-78) which can be true together (armed Select Agents while previewing), and Placement then preempts AgentSelect (l.109) silently (the carry cancels, `HandBlock.Preempted`).

C4. Far gestures vs near ISDK are protected only at pinch START. After a claim (ceiling pull, floor drag, Wall carry) the arbiter reacts only to `holding` (grab selected) via `_anyHeld` (HandOwnership.cs:334). A poke or ray over a key during a claimed pinch is not blocked (`uiHover` is not read once Claimed), so an index finger crossing a ModeConsole / Wall key during a floor drag presses it. The floor drag survives only because `Next/Cancel` end it cleanly (FloorAimSession.cs:296), and Confirm during a drag is not arbitrated.

C5. VoiceGestureActivator uses its own raw fingertip pinch (Voice/VoiceGestureActivator.cs:41-70) with no arbiter, no holding check, no TransitionHeld check. Its own comment warns a pinch near the face is how a paper is read (:14); with a paper grabbed (pinch) held up in the zone it can open the mic. Default OFF (:23), so exposure is only when enabled.

C6. Paper grab vs ceiling pull: protected. Holding anywhere cancels or blocks arming (CeilingPullLogic.cs:317, HandOwnership.cs:334) and says "Put down what you are holding first." (CeilingPullGesture.cs:208). Paper grab vs stamp in the other hand: both are plain ISDK selects; `HandOwnership.Holding` is irrelevant here and nothing is shared. Stamp only needs `paper.ReadyForDecision` (StampTool.cs:279), so grabbing the paper with one hand and stamping with the other is the intended gesture.

C7. Card drag vs dock Start vs pad pokes target the same card without a shared guard. `TaskDragController._resolving` guards only that drag; pad/chip pokes (BoardPresenter.cs:301-320) and dock Start (TaskDock.cs:423) do not look at it or at each other. A card being dropped (Pending) can be poked, or Start pressed on, simultaneously. The gateway's `expected_revision` is the only guard.

C8. Agent poke/wave (AgentAttention.cs:626, 662) fires from raw fingertips while the user holds a paper or stamp, or while a Select Agents carry is on. Cosmetic only, but it can make an agent reach/bubble over the thing being carried; no `HandInteractionProbe` read.

C9. SummonController ray select (SummonController.cs:205-218) raises `BringOfficeHere`/`AskAgent` on any ray `Select` transition with no arbiter, no debounce, and only a mode check. A ray pinch near the ceiling pod target would be refused arming by the arbiter (the ray hover sets `uiHover`) but the select itself still fires the request; not a double-handler for the same pinch, but the two systems are independent and the outcome depends on which interactor hovers first.

## 4. Duplicate consequential actions

Decision (approve / reject / changes):
- Guard is `DocumentDeliveryController._responding` (DocumentDeliveryController.cs:168-176), shared by poke keys, StampTool (:325) and voice (VoiceOffice.cs:285). Good for double poke and poke+stamp.
- Idempotency key is NOT stable. `SessionController.Respond` calls `NextKey($"resolve-{decisionId}")` per call (Networking/SessionController.cs:460,473), so the key is `resolve-<id>-<device>-<counter>`. A second call after a lost response gets a new key, so only the server's own decision-state rule dedupes it. The `_responding` flag is cleared on `RespondFailed` (:82) and the user can re-press with a fresh key.
- Bypasses of `BeginResponse`: StampTool fallback when no delivery controller (StampTool.cs:329-330), DemoDirector (Demo/DemoDirector.cs:245-247) and any caller of the public `SessionController.ApprovePending/RejectPending/RequestChangesPending` (SessionController.cs:260-262), which does no in-flight check (`RespondPending` :264). Tracking jitter and reconnect: `OperationCanceledException` in `Respond` is swallowed (:463) without `RespondFailed`, so `_responding` can stay true and the paper stays on "Saving" until resolve or reset (stuck, not duplicate).
- StampTool: `s_handledDecision` is set before the in-flight check (:314 then :325); if the response is already in flight, the stamp marks the decision handled and returns silently; only `RespondFailed` clears it (:131).

Task commands:
- Assign by drag: one key per drop attempt (TaskDragController.cs:163), `_resolving` blocks a second drop. Good.
- Assign by pad poke, pause/resume chip, priority chip: new key per poke (BoardPresenter.cs:303,311,319) and no in-flight lock. Two quick pokes send two commands with the same `expected_revision`; the second is likely 409 `RevisionConflict` ("It just changed. Try again", :413), which then OVERWRITES the first one's success message on the card (`ShowResult` :466,531). Priority cycle computes the next value from the (stale) local card, so a double poke before the answer repeats the same target, but after the answer cycles again.
- Pause/resume is a toggle decided from local `TaskState` (:309-312): a double poke before reconcile sends pause twice, not pause then resume.
- Dock Start: `_startPending` locks the dock key (TaskDock.cs:423) and hides it (:451); but it does not know about a drag/poke assign of the same card (C7).
- Voice create task: `NextIdemKey("voice-create")` per confirm (VoiceCommandCenter.cs:764); no in-flight guard on `CreateTask`; a double Confirm key press is not guarded here (not verified in `KeyIntent`, which calls `Abort()` first, :800).
- Wall: `PressSelectAgents` toggles Arm/Disarm per poke with no debounce (Interaction.cs:58); WallUi keys have none (WallUi.cs:189). `/btw` is read-only (no state change).
- Existing idempotency keys exist and are sent on every Phase2 command (`IdempotencyKey` in Assign/Priority/Pause/Resume/TaskCreate commands, BoardPresenter.cs:330,340,348,360,489) and on respond (SessionController.cs:460). All are fresh per user action, never reused for the same logical action.

## 5. Missing feedback stages for consequential actions

- Decision keys: P and F exist (Saving, SetError). Missing: a visible refusal animation on a refused key press when not ready (keys are hidden/disabled instead of refused).
- ModeConsole mode changes (Floor/Ceiling/Desk/Confirm): H, F (`FlashRefused`) and C (the move) exist; no P between press and the transition start (`enableAt` delay, ModeConsole.cs:332, hides it); a double poke gives silent no-op (`IsTransitioning` return, :350).
- Pad/chip pokes: P is only the card's 8 s "Sending" notice (TaskCardView.cs:148-150); no per-card lock, so a second poke is accepted visually.
- Card assign over a refusing zone: F is good (red halo + words + `ReturnHome`). Tracking-loss drop: no F because the drop is treated as a real one (C2).
- SummonController ray select (Bring office / Ask agent): no hover/pending/committed/failed feedback of its own; Spatial `SummonRequests` is fire-and-forget (SummonRequests.cs:35-57).
- Wall keys: `Note(...)` text only; no pending for `Ask` (backend call) beyond the session card text (not audited here).
- Voice gesture: no hold-progress visual despite `Progress` (VoiceGestureActivator.cs:39); the user cannot tell the 0.5 s hold is counting.
- Ceiling pull / floor drag / Wall carry: F is good ("Hand lost. Nothing moved...", "Nothing changed."), all three say cancelled is not a confirm.

## 6. Recommendation: smallest change, extend the existing arbiter

Do NOT add a second system. Extend `HandClaimRegistry` and `HandClaim` in HandOwnership.cs, and let near consumers register as claimants instead of being inferred from the probe.

1. Fix the enum order to the requested precedence. Re-number `HandClaim`: `Holding(1)` is already derived (probe), then reserve `NearInteraction(2)` (derived), `AgentSelect(3)`, `Placement(4)`, `Summon(5)`, `Ambient(6)`. Touch points: HandOwnership.cs:25-28 (and the three Wall/Floor/Ceiling claim sites; tests in Tests/EditMode/HandOwnershipTests.cs and Tests/EditMode/Wall/AgentVisitPortalManagerTests.cs:341-353 assert current numbers). This resolves C3.
2. Make the registry owner-aware (claim token = claim kind + owning arbiter instance) so two arbiters with the same kind do not both succeed (C1). Small: store an `object` owner next to the `HandClaim` at HandOwnership.cs:99-123.
3. While a hand holds ANY claim, keep it "busy" for poke/ray: in `HandOwnershipArbiter.Step`, if `g.claim != None` and `s.uiHover`, set a "UiAfterClaim" block for the gesture to read (or simply suppress nothing and surface it), so the floor/ceiling/Wall gestures can decide to cancel (C4). Do not auto-cancel in the arbiter; expose the fact.
4. Give card drops the same loss rule. Add to `TaskDragController` (the only near handler with a consequential release) a tracking-loss check that reads `HandInteractionProbe` / hand `tracked` on the grabbing hand: a `held` that ends while that hand is untracked (or was untracked within `lostGraceSeconds`) calls `CancelHeldGrab()`, not `OnReleased()` (C2). This is the highest-value single fix.
5. One in-flight lock per task/decision, outside the arbiter: a `HashSet<string>` of task ids in `BoardPresenter.RunCardCommandAsync`/`RequestAssignAsync` (BoardPresenter.cs:432,493) and refuse a second command on the same card until the first returns (C7, duplicate pokes in section 4). Reuse the idempotency key for a retry of the same logical action rather than minting a new one (`SessionController.Respond` :460: key per decision id + action, not per call).
6. Route VoiceGestureActivator through the arbiter (read `HandInteractionProbe.AnyHeld` and `HandOwner`) before arming (C5).

Do not add near-grab claimants to the registry. Near grabs, poke keys and rays are already highest priority by construction (Holding and CloseInteraction beat all far claims, HandOwnership.cs:11-19), which matches "active held object > near grab/poke". The only missing rungs are 1 (renumber, AgentSelect above Placement) and 3 (a claimed far gesture knowing about poke/ray hover).

## 7. Limits of this audit

- Static read only; nothing was run.
- Not inspected in detail: ArchiveStackPoke, ProjectShelfController, GrabReadout/PushableProp (no consequential action found by name), Wall `Ask` flow, and the physics-based `DynamicFingerCollision`.
- Hand tracking behaviour of ISDK deselect on tracking loss is assumed from the project's own comments (TaskDragController.cs:20), not verified in the package.
