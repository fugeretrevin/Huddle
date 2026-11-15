# Polish S9b: desk character clips and desk sounds

Code only plus Editor Play Mode evidence. Nothing here is verified on a headset. Release flags unchanged.

## Which AnimatorBridge is live

`MuseDesk.Agents.AgentAnimatorBridge` (`Scripts/Agents/AgentAnimatorBridge.cs`) is the one on the desk agent prefabs (the Play run logged its clips). The class of the same name in `Scripts/Art/` (`MuseDesk.Art`, socket resolver) is not used for clips and was not touched.

## Changes

| # | Issue | Cause | Fix | Test / evidence |
|---|---|---|---|---|
| 1 | Desk agent walks on the spot while `AgentMovement` turns it (start turn, sharp routed corner) | Bridge knew only IsMoving; S9a exposed `TurnInPlaceDirection` but nothing played it | Bridge subscribes to `TurnInPlaceChanged`. While moving AND turning in place, `DesiredBase` is `Turn_Left` / `Turn_Right` (offset 0, walk-rate matching off), then back to `Walk_Loop` through the normal crossfade. Rule is `DeskTurnRule.Select`, same as the Wall | `DeskPolishLogicTests` (5). Play: `TURN Agent_Researcher dir=-1 clip=Turn_Left`, `dir=0 clip=Walk_Loop`, also Turn_Right on the return walk |
| 2 | Nodding clip unused on the desk | none | `AgentRegistry` listens to reducer `TaskCompleted` and calls `bridge.PlayConfirmedNod()` for `e.AgentId`. Refused when: walking, holding the paper out, any one-shot is playing, Focus is on, or within 6 s of that agent's last nod (`NodGate`) | `DeskPolishLogicTests` (4). Play: injected bookkeeping completion gave `Nodding`; a second one 1 s later was refused; one under Focus was refused |
| 3 | Idle gestures kept going under Focus | `IdleGestures` only slowed them and dropped `Stretch` | `IdleGesturePolicy`: the random ambient gestures (look at worker, stretch, look at colleague) are skipped under Focus. The status-backed PresentPaper (WaitingForUser) still shows, at the focus repeat (x `focusIdleSlowdown`, 2.5) | `DeskPolishLogicTests` (2). Play: Focus on for 14 s at 4x, `ambientSuppressed` rose 0 to 5, no gesture; paper stayed offered |
| 4 | Poke, wave, glance, flinch, high five fire while the user holds a paper, stamp, card or panel, or a Select Agents carry is on (audit B C8) | raw fingertips only | `HandReactionGate.Busy(HandInteractionProbe.AnyHeld, Shared.Owner(0/1) == AgentSelect)` read once per frame (read only). All five reactions are skipped while busy; the edge flags still track so nothing fires the instant the hands free up | `DeskPolishLogicTests` (5). Not drivable without hands: unverified in Play |
| 5 | A card held when its task leaves was dropped (colliders off in `BeginHide`) | S9a hide disables colliders at once | `TaskCardView.BeginHide` defers while `IsHeld` (`DeferredHide`), starts on the first free frame, `CancelHide` (task came back) cancels it, a disabled card finishes its hide. `TaskDragController` treats `TaskGone` like a lost hand: no hover, on release `CancelHeldGrab`, nothing sent. `BoardPresenter.RequestAssignAsync` and `RunCardCommandAsync` refuse a `TaskGone` card (`TASK_GONE`) | `DeskPolishLogicTests` (3). Play: held card stayed active with a collider while `pending=True`, drag `Held`; after release drag `Idle`, card pooled |
| 6 | Desk sounds missing | only Wall had cues | `FeedbackCue.PaperArrived` (agent stands at the user holding the paper out, decision still pending: from `AgentRegistry.OfferWhenArrived`) and `FeedbackCue.CardLanded` (a card that rested on the board settles in a different lane after the board changed it: reducer-driven, never on a drag, never for a new card). Procedural, spatial from the workspace, quiet (gain 0.55 and 0.4). `DeskCuePolicy`: these two and `Delivered` are silent under Focus, with per-cue repeat limits (landed 0.35 s, arrived 1.5 s) | `DeskPolishLogicTests` (3). Play: `CardLanded` on Start, `PaperArrived` once, `Delivered` on completion |
| 7 | Monitor colours | `WorkstationStatusDisplay` mapped Waiting, WaitingForUser, Approaching to Coral | `ColorFor(Activity)`: WaitingForUser and Approaching = Amber, plain Waiting = Neutral, Error = Coral | compile; not asserted by a test |

Already existing, not duplicated: `PaperTaken` (paper picked up), `Attention` (decision requested), approve / reject / changes, `Delivered` (WorkDelivered, the completed chime), `Error`. U4 "paper picked up" and "completed chime" were therefore not re-added. `Delivered` now respects Focus.

## Decisions

- **Nod versus the completion sequence.** A `task.completed` that carries an `artifact_id` is a delivered completion: `CompletionSequence` and the Complete state already play the Tester's Success and the colleagues' `Task_Completed` nod. The extra `Nodding` is not played for those (no stacking). `Nodding` plays only for a completion without an artifact (a bookkeeping completion, which live runs emit) and not for `task_state` other than `completed`. The demo's only completion carries an artifact, so the demo run shows no `Nodding` (verified: `nodsPlayed=0`, developer showed `Task_Completed`). Evidence for the nod used a driver-injected completion into the Editor reducer.
- **Listening: not implemented.** There is no signal for "the mic is open and addressed to this agent". `VoiceOffice.AgentsListen` and `AgentGaze.SetListening` tilt every agent's head whenever the mic is open for any voice command, and `AgentAttention.Address` is the reverse direction (agent speaks to the user). Playing `Listening` on all three, or on one picked arbitrarily, would be invented. Needs a voice intent that names the agent (voice routing change) before this can be wired. The bridge already maps the state string `"listening"` to the clip if a real source ever sets it.
- `AgentPresence` has no random gesture timers beyond `NextIdleAt` (initial offset); `AgentIdleLife` fidgets were already rarer under Focus and are unchanged.

## Risks and unverified

- Turn clips are 0.83 s and do not loop: a long turn holds the last frame (same as the Wall). Turn rate versus clip length not judged.
- No hands in the Editor: gating of poke and wave while holding, and a real grab of a card during a removal, are unverified. The simulated hold exercised the same `IsHeld` path.
- Sound levels, pitch and audibility are unheard; `PaperArrived` follows `Attention` by the walk time (about 2 to 4 s).
- `FeedbackController` now mutes `Delivered` under Focus (was audible). Intended; flag if the owner wants it back.
- Files: `Scripts/Agents/Core/DeskPolishLogic.cs`, `AgentAnimatorBridge.cs`, `AgentRegistry.cs`, `AgentAttention.cs`, `Scripts/Feedback/FeedbackController.cs`, `Scripts/Art/WorkstationStatusDisplay.cs`, `Phase2/Runtime/Board/{TaskCardView,TaskDragController,BoardPresenter}.cs`, test `Tests/EditMode/AgentInteraction/DeskPolishLogicTests.cs`, driver `AgentScripts/DeskS9bDrive.cs`.
