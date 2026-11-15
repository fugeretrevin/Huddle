# A15 First-use onboarding (pure logic and content)

Date: 2026-11-15. Written without Unity. Nothing here was compiled or run. Nothing is wired into the scene.

Files: `Assets/MuseDesk/Scripts/Spatial/Core/Onboarding/HintCatalog.cs`, `OnboardingCoach.cs`; tests `Assets/MuseDesk/Tests/EditMode/Onboarding/OnboardingCoachTests.cs` (36 tests). Namespace `MuseDesk.Spatial.Onboarding`, assembly `MuseDesk.Spatial.Core` (the only UnityEngine use is `PlayerPrefsOnboardingStore`).

Searched for existing hint code (Hint, Onboard, Tutorial, PlacementGuide). Only `PlacementGuide` / `PlacementGuideLogic` exist, and they guide Floor/Ceiling scanning, not first use. Nothing was duplicated. The coach does not replace them: it stays quiet (`HandsBusy`/Focus) and the guide keeps its own cards.

## Rules the coach enforces

- A hint is queued only by a real observed event (`Observe`). No timers start a hint on their own.
- It ends when the user does the action (`Completes`), is dismissed, or times out.
- Done hints are persisted (`IOnboardingStore`) and never return. If the user already did the action earlier in the session the hint is marked done and never shows.
- One hint visible at a time. 3 s cooldown after an onboarding hint (0.5 s after a recovery hint).
- Focus suppresses everything (visible hint hides, comes back later, no show used up).
- Busy hands suppress new hints. Hints about the held paper (`InspectPaper`, `StampDecision`, tracking-lost recovery, no-undo) are allowed while holding.
- Feature-flag gating: Wall hints need Wall; Manager needs Wall and Manager; Portal needs Wall and Portal; Move needs Spatial (floor or ceiling); Calm needs the toggle flag.
- Recovery hints preempt an onboarding hint, are capped per lifetime (3 to 5 shows), and are never marked done.
- Reduced Motion: `coach.Animate` is false; the display layer must fade or snap.
- Voice is never required. All copy names hand actions.
- Honesty: copy never says "AI", never claims a result. "Undo isn't available" is stated plainly.

## Flow (text)

```
SessionReady ──> MeetAgents ──(AgentTouched | TaskStarted)──> StartWork ──(TaskStarted)──┐
                                                                                         v
DecisionRequested ──> PaperArrives ──(PaperGrabbed)──> InspectPaper ──(DetailsOpened | DecisionSent)
                                         └──(6 s)──> StampDecision ──(DecisionSent)
DecisionResolvedApproved ──> WatchHandoff ──(HandoffObserved)
TaskCompleted ──> ReceiptDelivered ──(ReceiptViewed)──> MoveWorkspace [Spatial]
                                                    ├──> FocusMode
                                                    └──> CalmMotion [toggle flag]
Flag-gated: SessionReady ──> WallPlace [Wall]; ManagerPlanShown ──> ManagerPlan [Wall+Manager];
            AgentSelected ──> PortalMeet [Wall+Portal]
Recovery (preempts): PaperDropped, WorkspaceMovedByAccident, TrackingLostWhileHolding, UndoAttempted
```

Steps without a persisted `After` rule run on triggers only. `StartWork` needs `MeetAgents` done; `InspectPaper`, `StampDecision` need `PaperArrives`; the three optional hints need `ReceiptDelivered`.

## Hints: copy, anchor, trigger, complete, real event mapping

"Needs event" = no code event exists today; the integration layer must raise it.

| Hint | Copy | Anchor | Trigger event -> real source | Complete event -> real source |
|---|---|---|---|---|
| MeetAgents | Three agents work here. Poke one to say hi. | Agent | SessionReady -> `SessionReducer.SnapshotLoaded` (`SessionReducer.cs:36`) | AgentTouched -> needs event; or TaskStarted |
| StartWork | Poke Start on the dock to begin. | Board | SessionReady (same) | TaskStarted -> `SessionController.RunStateChanged` (`SessionController.cs:61`) when state becomes running; or `DemoAgentGateway.RunStateChanged` (`DemoAgentGateway.cs:29`) |
| PaperArrives | A paper is ready. Take it by the edge. | Paper | DecisionRequested -> `SessionReducer.DecisionRequested` (`SessionReducer.cs:23`) | PaperGrabbed -> needs event |
| InspectPaper | Read the question. Tap Why for details. | Paper | PaperGrabbed -> needs event | PaperDetailsOpened -> needs event; or DecisionSent |
| StampDecision | Press the stamp to approve, or reject. | Stamp | PaperGrabbed (+6 s read time) | DecisionSent -> needs event (call site: `SessionController.Respond`, `SessionController.cs:464`) |
| WatchHandoff | Watch the work pass to the next agent. | Agent | DecisionResolvedApproved -> `SessionReducer.DecisionResolved` (`SessionReducer.cs:24`) when state is approved (backend-acknowledged, not the local press) | HandoffObserved -> needs event (source: `SessionReducer.AgentChanged`, `SessionReducer.cs:22`) |
| ReceiptDelivered | Delivered. Your receipt is front right. | Plaque | TaskCompleted -> `SessionReducer.TaskCompleted` (`SessionReducer.cs:26`) | ReceiptViewed -> needs event |
| MoveWorkspace | Optional: move the office with the mode keys. | Plaque | TaskCompleted | WorkspaceMoved -> `WorkspacePlacement.Placed` (`WorkspacePlacement.cs:57`) or `WorkspaceTransitionMachine.MoveFinished(true)` (`WorkspaceTransitionMachine.cs:94`) |
| FocusMode | Optional: Focus quiets everything but decisions. | Board | TaskCompleted | FocusEntered -> needs event (state flag exists: `AttentionModel.Focus`, `AttentionModel.cs:50`; `AttentionModel.Changed` line 59 can derive it) |
| CalmMotion | Optional: Calm motion shortens animations. | Plaque | TaskCompleted | CalmMotionToggled -> needs event (`MotionToggleKey.cs:20`) |
| WallPlace | Look at a wall, then press Place here. | Plaque | SessionReady | WallPlaced -> needs event (`WallEventBridge.HabitatEventRaised`, `WallEventBridge.cs:106`, is the likely source) |
| ManagerPlan | Review the plan card, then approve it. | Board | ManagerPlanShown -> needs event | ManagerPlanApproved -> needs event |
| PortalMeet | Far-pinch an agent to meet them larger. | Agent | AgentSelected -> needs event | PortalOpened -> needs event |
| RecoverDroppedPaper | Put it back on the desk tray. | Paper | PaperDropped -> needs event | PaperReturned or PaperGrabbed -> needs event |
| RecoverWorkspaceMoved | Moved by accident? Press Desk to return. | Plaque | WorkspaceMovedByAccident -> needs event (heuristic: a move that began without a mode key press; `WorkspacePropCarrier.MoveBegan`, `WorkspacePropCarrier.cs:73`) | WorkspaceReturnedToDesk -> needs event |
| RecoverTrackingLost | Hands lost. Show them to continue. | Paper | TrackingLostWhileHolding -> needs event (`GrabTrackingWatch`, Core) | TrackingRegained -> needs event |
| RecoverNoUndo | Undo isn't available. Reject to stop work. | Stamp | UndoAttempted -> needs event (for example a second press after a send) | DecisionSent |

Note on RecoverNoUndo: after a decision is sent, reject cannot reverse it. The copy applies while the paper is still open. Revisit the copy once the "second press" source is chosen.

## UX acceptance checklist (for a worn test, not yet run)

- [ ] No hint appears in the first 2 s after the session starts, and never while the user holds something unrelated.
- [ ] Only one hint is on screen at any moment; the gap between two is at least 3 s.
- [ ] Each hint disappears within 1 s of the user doing the action it names.
- [ ] A hint the user already satisfied never appears afterwards (Start poked before the hint, for example).
- [ ] Relaunch: completed hints stay done; "Replay tour" brings them back.
- [ ] Focus on: no hint at all, including recovery hints. Focus off: pending hints return.
- [ ] Calm motion on: hints fade or snap; no bobbing, no scaling.
- [ ] With wall/manager/portal flags off, none of those hints exist in the run.
- [ ] Every hint is readable at seated distance, one line or two, 9 words or fewer, no jargon.
- [ ] Hint never covers the paper's question text or the stamp.
- [ ] The hint close control works with a poke; no voice needed anywhere.
- [ ] Drop a paper: recovery hint within 1 s; grab it again: hint ends.
- [ ] Lose tracking while holding: the hint shows when hands return, not while the user fumbles.
- [ ] Label rule: no hint claims live AI or a delivered result; the mode label (`SCRIPTED DEMO` / `LIVE`) stays visible and untouched.
- [ ] Permanent UI added by onboarding: none beyond the transient hint card.

## Integration plan (not applied)

1. **Host component (new, scene)**: `OnboardingHintPresenter : MonoBehaviour`, one instance, created by a new builder in `AgentScripts/` (not edited here). It owns one `OnboardingCoach`, a `PlayerPrefsOnboardingStore`, a clock returning `Time.unscaledTimeAsDouble`, and one small TMP card (scale 0.01, Paper/Ink tokens, `KeyStateVisual` for the close dot, hidden by scale not `SetActive`). `Update` calls `Tick()` and `SetContext(...)`. Show on `HintShown`, hide on `HintEnded`.
2. **Event wiring** (all in the presenter, subscribe in `OnEnable`):
   - `SessionReducer.SnapshotLoaded` (`SessionReducer.cs:36`) -> SessionReady.
   - `SessionReducer.DecisionRequested` (`:23`) -> DecisionRequested; `DecisionResolved` (`:24`, state "approved") -> DecisionResolvedApproved; `TaskCompleted` (`:26`) -> TaskCompleted.
   - `SessionController.RunStateChanged` (`SessionController.cs:61`) -> TaskStarted when the run is running.
   - `WorkspacePlacement.Placed` (`WorkspacePlacement.cs:57`) -> WorkspaceMoved.
   - `AttentionModel.Focus` (`AttentionModel.cs:50`, change signal `:59`) -> `FocusActive` in the context and FocusEntered.
   - `HandClaimRegistry` (`HandOwnership.cs:95`): HandsBusy = a Holding claim exists whose target is not the hint's target (read-only query).
   - `ReleaseConfigApplier.Apply` (`ReleaseConfigApplier.cs:64-136`) values -> `OnboardingFlags` (read the config asset; do not edit the applier).
   - Reduced Motion: read the same setting `MotionToggleKey` uses (`MotionToggleKey.cs:20`).
3. **New events needed** (small additions by their owners, one-line `Action` each): PaperGrabbed, PaperDropped, PaperReturned, PaperDetailsOpened, DecisionSent (document code), AgentTouched (agent interaction), ReceiptViewed, HandoffObserved, FocusEntered, CalmMotionToggled, WorkspaceMovedByAccident, WorkspaceReturnedToDesk, TrackingLost/Regained while holding, UndoAttempted, and the Wall events (WallPlaced, ManagerPlanShown/Approved, AgentSelected, PortalOpened). Until an event exists, its hint simply times out (2 shows) and ends done; nothing breaks.
4. **Anchor resolution**: the presenter maps `HintAnchorKind` to transforms (nearest agent, active paper, stamp, board, desk plaque) and places the card via `PanelLayoutMath` (`Core/PanelLayoutMath.cs`) so it is never behind the user.
5. **Settings**: add "Replay tour" and "Hide tips" to the existing settings surface calling `ResetProgress()` and `SkipAll()`.
6. **Tests to add after wiring** (Play Mode): a driver `OnboardingDrive` that raises each event and reads `coach.Current`.

## Risks and untested

- Nothing compiled or run. Written to the asmdef rules (Core references nothing; tests reference Core).
- Timings (delays, 6 s read time, show durations) are guesses until worn.
- `StampDecision` is timed from the paper grab, not from the user's reading; a fast reader sees a hint they do not need (it completes silently if they press first).
- `PlayerPrefsOnboardingStore` calls `PlayerPrefs.Save()` on each done mark; fine at this rate.
