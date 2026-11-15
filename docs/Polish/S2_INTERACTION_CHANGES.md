# Sprint 2: one interaction owner per hand, no duplicate consequential actions

Branch feature/living-world-polish. Source audit: docs/Polish/audit/B_INTERACTION_OWNERSHIP.md.
Evidence levels: AUTO = EditMode test (compiled and run in the Editor). Nothing here is verified on a headset or in Play Mode.
Paths relative to Assets/MuseDesk/.

## 1. Tracking loss cancels a card drag (audit C2)
- Root cause: Phase2/Runtime/Board/TaskDragController.cs Update treated `!held` as a release; ISDK drops a grab when its hand is lost, so a dropout over an agent pad sent an assign.
- Fix: new pure helper Scripts/Spatial/Core/GrabTrackingWatch.cs (Begin / Sample / Decide). TaskDragController reads both hands' `HandVisual.Hand.IsTrackedDataValid` (same signal as CeilingPullGesture, FloorAimController, Wall) once per held frame. Hands tracked when the hold began are watched; if one is untracked now or was untracked within 0.25 s before the grab ended, the end is a Cancel: halos off, card returns home, no command, note "Hand lost. Nothing sent". If hand visuals cannot be found (no rig, simulated hold) hands count as tracked, so the old behaviour holds there.
- Tests: AUTO GrabTrackingWatchTests (4: tracked release, untracked at end, lost just before deselect, hand not in play ignored).
- Residual risk: which hand grabbed is unknown, so a non-grabbing watched hand that drops within 0.25 s of a genuine release also cancels (safe direction: card returns home, user re-drops). The 0.25 s window and ISDK's real deselect timing are not device-verified. The controller wiring itself is code-reviewed, not device-verified.

## 2. One command in flight per task card, stable keys (audit C7, section 4)
- Root cause: Phase2/Runtime/Board/BoardPresenter.cs OnPokeAssign / OnPokePauseResume / OnPokeCyclePriority minted a fresh key per poke and had no lock; a late 409 then overwrote the first command's success text via ShowResult.
- Fix: new pure Scripts/Spatial/Core/CommandLedger.cs. The lock is taken inside RequestAssignAsync and RunCardCommandAsync, so pad/chip pokes, the drag and the dock Start key all share it; a second command on the same card is ignored and the card shows "Already sending" (code BUSY, never reaches the server). Keys come from `StableKey(task, action, param)`: reused by a retry (transport error, cancel), dropped only after the server answered (ok or refused). The drag path uses the same key. A RevisionConflict that arrives after another command was accepted during its flight no longer replaces the success text (kept as a guard; the lock normally prevents the overlap).
- Tests: AUTO CommandLedgerTests (3: lock per target, key stable for retry and new after a definitive answer, approve/reject share a lock). BoardPresenterLogicSpecTests (22) and DemoGatewayDecisionSpecTests (5) still pass. BoardPresenter wiring: code-reviewed, not device-verified.
- Residual risk: if the shield skips ShowResult the card's pending (amber) state expires on its own after 8 s. Dock Start and Phase2Bootstrap still mint their own keys (not stable across retries); they do get the lock. Reusing a key after a transport failure assumes the gateway replays an identical command; if the card revision changed meanwhile the server may answer 409, which is shown honestly.

## 3. Decisions answered at most once in flight (audit section 4)
- Root cause: Networking/SessionController.cs Respond (was :454-471) minted a new key per call, had no in-flight check (ApprovePending/RejectPending, DemoDirector and StampTool's fallback bypassed DocumentDeliveryController.BeginResponse), and swallowed OperationCanceledException without RespondFailed, leaving the paper on "Saving".
- Fix: every entry point ends in SessionController.Respond, which now uses a CommandLedger: one lock per decision id (approve and reject share it; a second is ignored with a log line, not RespondFailed), key stable per (decision id, action) and kept after success. A cancel now raises RespondFailed ("Cancelled. Your decision was NOT saved. Try again.") unless the session itself is shutting down; failure message unchanged. New `IsRespondingTo(decisionId)`; DocumentDeliveryController.BeginResponse consults it. StampTool.Fire no longer marks the decision "handled" when a response is already in flight, so a failed first response does not leave the stamp dead. Backend unchanged.
- Tests: AUTO CommandLedgerTests (shared lock and key rules). The SessionController change itself is code-reviewed, not device-verified.
- Residual risk: the key is reused for the same (decision, action) forever, which relies on the server treating a replay as idempotent (it does for the demo gateway per Phase2 tests; live behaviour not re-verified). A cancel during a live reconnect now shows a retry message where it used to show nothing.

## 4. Hand claim precedence and owner-aware registry (audit C1, C3)
- Root cause: Scripts/Spatial/Core/HandOwnership.cs numbered Placement 3 < Summon 4 < AgentSelect 6 (lower wins), so agent selection lost to placement; and the registry compared claim kinds only (`cur == claim`), so two arbiters with the same kind both succeeded.
- Fix: lower number still wins. Renumbered HandClaim: AgentSelect 3, Placement 4, Summon 5 (HandOwner: AgentSelect 3, Placement 4, Summon 5, Ambient 6); HandOwnershipArbiter.Owner maps claim to owner by switch, no numeric cast. Holding and CloseInteraction still outrank every far claim (derived, unchanged). The registry now stores (claim, owner object); the arbiter passes itself; a different owner of the same kind is refused, a non-owner cannot release, preempted arbiters notice via `Holds`. The optional `owner` parameter defaults to null so existing callers and tests are source compatible. Wall call sites (HandClaim.AgentSelect / Placement by name) are unchanged.
- Tests: AUTO HandOwnershipTests 10 pass, including new FarClaimPrecedence_AgentSelect_Then_Placement_Then_Summon and Registry_IsOwnerAware_TwoArbitersSameKindCannotBothHoldAHand. Also passing: CeilingPullOwnershipTests 6, FloorAimOwnershipTests 4, CeilingPullTests 14, AgentVisitPortalManagerTests 19 (its AgentSelect claim test), WallPlacementTests 26.
- Residual risk: audit item C4 (a claimed far gesture ignoring poke/ray hover) was not part of this brief and is unchanged. With the new order the Wall aim (Placement) can now be preempted by an AgentSelect claim from another arbiter; within one arbiter the two cannot coexist.

## 5. VoiceGestureActivator respects holds and claims (audit C5)
- Root cause: Scripts/Voice/VoiceGestureActivator.cs Update used raw fingertip distance with no arbiter, holding or claim check.
- Fix: before measuring the pinch it returns (and zeroes the hold timer) while `HandInteractionProbe.AnyHeld` (either hand, includes the paper's transition hold) or either hand has a claim in `HandClaimRegistry.Shared`. `gestureEnabled` stays false by default.
- Tests: none (MonoBehaviour reading scene-side probes; no unit seam). Code-reviewed, not device-verified.
- Residual risk: it blocks on either hand, not only the pinching hand (the tip list does not say which hand it is); conservative.

## 6. SummonController ray select debounce (audit C9)
- Root cause: Scripts/Spatial/SummonController.cs OnBeaconRay / OnAgentRay raised Bring / Ask on every Select transition.
- Fix: repeats within 0.6 s on the same target (beacon, or the same agent index) are ignored. Unscaled time, no per-frame allocation (the per-agent array is allocated once).
- Tests: none (event handler tied to ISDK state changes). Code-reviewed, not device-verified.
- Residual risk: a deliberate second ask of the same agent inside 0.6 s is dropped.

## Test run (EditMode, one fixture at a time)
HandOwnershipTests 10, GrabTrackingWatchTests 4, CommandLedgerTests 3, CeilingPullOwnershipTests 6, FloorAimOwnershipTests 4, CeilingPullTests 14, SpatialIsolationTests 3, HabitatMotionTests 22, AgentVisitPortalManagerTests 19, WallPlacementTests 26, BoardPresenterLogicSpecTests 22, DemoGatewayDecisionSpecTests 5: all pass, 0 failures. SpatialIsolationTests failed once on a comment in CommandLedger.cs that named a forbidden type; reworded, then passed.
Not run: Play Mode, any build, any headset session.
