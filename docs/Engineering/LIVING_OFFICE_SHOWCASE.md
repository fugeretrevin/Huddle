# The Living Office showcase (Wall Habitat + Manager, end to end)

Branch `worktree-agent-a55d86ba31d43eb88`, merged into `feature/living-world-polish`. Written 2026-11-15.

**Competition rule:** everything here is behind the existing Wall flags (`wallHabitat`, `wallManager`, `agentSelect`, `agentBtw`, `largeAgentPortal`), all `0` in the committed ReleaseConfig. `Builds/` was not touched. No scene or prefab was changed, so there is no builder: the habitat, the paper, the cards and the receipt are built at run time by `WallHabitatController`.

## 1. Vision

A miniature AI organisation lives in the user's wall. The user asks the Manager for an objective. The Manager drafts a plan that appears as a physical plan-review paper. The user approves it with the stamp (or revises or rejects it). Task cards print in the Manager's upper office. Each assigned agent takes the elevator up for its card and carries it down to its cubicle, then visibly works. Handoffs between floors travel by elevator. Blocked and failed cards show the real reason with a Retry poke. When the plan settles, the Manager's summary comes down to the desk as a receipt.

Rule for every step: the backend (or, offline, the demo board, labelled `[SCRIPTED DEMO]`) is the authority. Nothing is animated that it has not confirmed.

## 2. Gap analysis (before this work)

| # | Vision step | Before | State before |
|---|---|---|---|
| 1 | Ask the Manager by hand poke or voice | A dock key opened a card with three preset goals. No pinch on the Manager, no voice route (the A12 classifier recognised "Manager, ..." but nothing consumed it). | Real (key) / missing (pinch, voice) |
| 2 | Plan-review paper with objective, work breakdown, assignments, dependencies, outputs, risks, provenance | A small text card: title, up to 4 steps (hard cap), provenance words. Objective, expected outputs, assumptions and risks were not parsed from the backend reply or shown. A6 `PlanReviewLayout` was unused. | Placeholder |
| 3 | Approve with the stamp; revise; reject | Approve and Reject keys. No stamp. Reject was shown as done before the backend answered. Revise existed on the backend only. | Real (approve) / wrong order (reject) / missing (revise, stamp) |
| 4 | Cards print in the Manager's office | Printer printed one card per canonical committed id that had reached the board. The card then flew through the air straight to the desk. `manager.plan_committed` was not listened to. | Real (print gate) / placeholder (delivery) |
| 5 | Each agent takes the elevator to its cubicle and works | Desk clips follow real task state (real). Nobody fetched a card. One elevator trip at a time, picked in role order, with the original one-rider machine; A7 `ElevatorDispatcher` was unused. | Real (clips) / missing (dispatcher, pick-up) |
| 6 | Handoffs between floors by elevator | Real: `handoff.created` drives a carry trip; Researcher completion and failures go up to the Manager. | Real |
| 7 | Blocked or failed cards show the real reason with Retry | Status light colour only. `blocked_reason`, `blocked_by`, the failure text and the retry route were not used by Unity. | Missing |
| 8 | Synthesis comes down to the desk as a receipt | Backend creates the `completion` artifact. Unity did nothing with it in Wall mode. Offline had no summary at all. | Missing |
| 9 | Honest labels | Present on the old card. | Real |
| 10 | Reactions and Listening on wall avatars | A nod on completion without arbitration; no per-agent Listening. | Placeholder |

## 3. What was built

### Elevator (A7 applied)

- `Core/Wall/HabitatLift.cs`: `IHabitatElevator` (what a walker and the view need) and `DispatchElevator`, the adapter that puts `ElevatorDispatcher` behind it. `HabitatElevatorMachine` implements the same interface and is unchanged in behaviour. A stage runs exactly one of them (`HabitatStage(layout, useDispatcher)`); the controller field `useElevatorDispatcher` (default on) is the rollback switch.
- `HabitatWalker`: with the dispatcher, a rider stands up to the aisle beside its desk, waits there until the dispatcher lets it approach, walks to the floor's wait spot (the queue node), reports, turns to face the door (within 8 degrees), reports, and steps in only when the doors are fully open for it. It rides at exactly the cabin's position, steps out when told, and reports the end of its journey. Every abnormal path (reset, soft transfer, new trip) closes its ticket.
- `HabitatStage`: a first-come-first-served ride lane. While the lane is busy, the next rider in line stands by at its aisle, off the corridor.
- View: a riding avatar is a child of the cabin with a fixed local pose; its local scale is unchanged (the cabin has unit scale).

**Decision: one round trip at a time, not shared rides.** The corridor is single file (a 0.064 m capsule in a 0.08 m corridor) and a rider leaving the cabin must walk through the wait spot. Two riders on the same floor, one waiting and one leaving, deadlock (the lease system refuses rather than overlaps, so it would end in a timeout and a fade home). So the dispatcher only ever holds the lane owner's ticket; other requests wait in order in the adapter and their riders wait off the corridor. Capacity above 1 and extra corridor wait spots are deliberately not used. Queueing is visible as "next rider stands at its aisle".

### Manager plan paper (A6 applied)

- `Scripts/Wall/WallPlanPaper.cs`: a 0.22 x 0.30 m paper drawn row by row from `PlanReviewLayout` with the legibility floor for a 0.45 m reading distance. `Core/Ui/PlanPaperPager.cs` pages it; nothing is shrunk and the hard cap of 4 steps is gone. The provenance footer is on every page and comes only from the plan's source field.
- Real fields only: title, what the user asked, objective, work items with owner, dependencies and expected outputs (`acceptance_criteria`), assumptions, risks. Risky steps appear under Risks with the backend's reasons. The Unity DTOs now parse `objective`, `assumptions`, `risks`.
- Keys: three step ticks (a window that moves with Next page), Approve (becomes Confirm risk when a risky ticked step needs its separate OK), Revise, Reject, Close.
- Stamp: `APPROVED` is drawn only after the commit succeeded; `REJECTED` only after the reject was confirmed. While the commit is in flight the footer says it is waiting.
- Revise: drops the unticked steps and asks the backend for a new revision (`/revise`); offline the demo plan is revised locally. It creates nothing.
- The Manager can be asked by a pinch while pointing at the Manager in the habitat (same hand arbiter as Select Agents) and by voice ("Manager, build the login flow"), which only requests a plan. The voice hook is registered only while the Manager flag is on.

### Cards

- A committed card (canonical id from the commit, seen on the board) prints into the Manager's tray. The commit can arrive as the HTTP reply or as `manager.plan_committed` on the stream; whichever is first wins and the other is ignored.
- The owner is told (`CardPrinted`, a real committed card) and makes a `CollectCard` trip: up with empty hands, cards go to its hands at the Manager's desk, down, onto its desk. If the trip cannot run (dropped, reset), the card goes down by the chute in front of the decks, so a card is never stuck. Cards for the Manager or an unknown owner stay in the tray.
- The card's tint is its real task state colour.

### Blocked, failed, Retry

- `Core/Wall/LivingOfficeFlow.cs` `PlanBoard`: cards exist only for committed ids; updates for unknown ids or older revisions are ignored; a blocked reason is kept only while blocked and a failure reason only while failed; with no reason reported it says so.
- Sources: the stream (`task.progress` with `blocked_reason` / `blocked_by`, `task.failed` `result_summary`) and a status read every 2.5 s while the plan is unsettled (`GET .../manager/plans/{pid}/status`). Offline, the demo board answers the same status shape with the backend's wording.
- Status page on the paper: each card with its state and the reason, and a Retry key per failed card. Retry takes an in-flight lock per card and a stable idempotency key per failed revision (`CommandLedger`); a transport failure reuses the key, a definitive answer forgets it. The card stays failed on the wall until the backend says otherwise.
- If the live backend has no model, its `NO_MODEL_CONFIGURED` warning is shown on the status page.

### Synthesis receipt

- `artifact.created` with kind `completion`, anchored on one of the plan's cards (or the same artifact reported by status), triggers a `DeliverReceipt` trip: the Manager rides down to the dispatch spot, the paper leaves from there and lands near the user as a receipt card. If the trip cannot run, it leaves from the Manager's office.
- The receipt shows the artifact's summary and text, fetched from the gateway. Its footer is the backend's own first line (scripted, roll-up, or model-written, no tools).
- Offline: the gateway publishes one labelled roll-up of the recorded step results when the offline plan settles (`Phase2DemoGateway.TryPublishPlanSummary`), and a new one after a retry.

### Reactions and Listening

- A3: one `ReactionArbiter` for the four wall characters. Walking or riding takes the body slot, so a completion nod or a worried beat never plays over a walk. Only confirmed completions and failures request a reaction.
- A12: a wall avatar shows the Listening clip only while `VoiceAddressing` says that agent is the one addressed.

### Not done

- A9 `PresentationLifecycle` / `PortalSpotPlanner` are not applied to the visit and portal.
- The physical stamp tool is not wired to the plan paper. Approve is a poke key; the stamp mark is drawn by the paper.
- Reactions ignore Focus mode (the arbiter's `SetFocus` is not fed).
- The art is still proxy art.

## 3b. Follow-ups (2026-11-15)

- **Stamp on the plan paper:** a held desk Approve or Reject stamp pressed on the paper's stamp spot (`PlanStampGate`: 0.06 m, 0.12 s dwell, 1 s cooldown) calls the same guarded `ApprovePlan` / `RejectPlan` as the keys (risk step, in-flight lock, idempotency key). APPROVED / REJECTED is still drawn only after backend confirmation. The stamps are shown while the plan awaits approval (`NearUserKit.ExternalStampDemand` + `StampPoof.Summon`). A press is ignored during a Wall carry or aim; a held stamp outranks far hand claims.
- **Focus:** the Wall arbiter now gets `SetFocus`: completion nods are silenced, confirmed failure worry stays. No other ambient or social reaction exists on Wall avatars.
- **Deferred nods:** a confirmed completion nod for an agent away on a trip waits (`DeferredNods`, max 20 s) until it is home and free; Focus drops it.
- **Resync rebuild:** after a session reset the plan picture is rebuilt from `GET /v2/sessions/{sid}/manager/status` (offline: the demo board). Only into an idle Manager; nothing reprints; an existing summary is marked seen, not re-delivered.
- **Verification state:** compiled, and 36 of 36 `LivingOffice` EditMode tests passed on the last full compile (13 new). NOT compiled: `LivingOfficeDrive.RunOfflineStamp` / `ResyncCheck` / `ResyncStatus` and `SimulateSessionReset`. NO Play runs of these follow-ups, no wall fixtures batch, no flags-off desk demo. NOT worn.

## 4. Files

Created: `Core/Wall/HabitatLift.cs`, `Core/Wall/LivingOfficeFlow.cs`, `Core/Ui/PlanPaperPager.cs`, `Scripts/Wall/WallPlanPaper.cs`, `Tests/EditMode/Wall/LivingOfficeElevatorTests.cs`, `Tests/EditMode/Wall/LivingOfficeFlowTests.cs`, `AgentScripts/LivingOfficeDrive.cs`, `backend/tests/phase3/test_p3_living_office.py`. Their `.meta` files were generated by the Editor and committed on `feature/living-world-polish`.

Changed: `Core/Wall/HabitatMotion.cs`, `HabitatWalker.cs`, `HabitatStage.cs`, `ManagerPlanFlow.cs`, `WallOfflineLogic.cs`; `Scripts/Wall/WallContracts.cs`, `WallEventBridge.cs`, `WallBackendGateway.cs`, `WallHabitatView.cs`, `WallHabitatController.cs`, `.Interaction.cs`, `.Manager.cs`; `Phase2/Runtime/Contracts/Phase2DemoGateway.cs` (three additive plan-card methods and a read-only snapshot); `Scripts/Voice/VoiceCommandCenter.cs` (Manager request hook); `AgentScripts/WallDrive.cs` (one null-safe line); `backend/app/phase3/plan_ops.py` (status cards carry `result` for completed cards).

## 5. How to run

Offline (no backend), in Play Mode in the Editor:

1. `LivingOfficeDrive.RunOffline` then poll `LivingOfficeDrive.Status`. It turns the flags on for the session, uses the synthetic room, and runs: place the habitat, pinch the Manager, plan paper, revise, reject, ask again, approve, print, pick-ups by elevator, work, summary, receipt.
2. `LivingOfficeDrive.RunOfflineWithFailure` adds a labelled scripted failure of the Developer's card: failure and blocked reasons, the failure summary, Retry (pressed twice), recovery, a second receipt.

Pacing: three pick-ups in a row took 96 s in Play Mode (Full layout); the pure simulation gives 29 to 37 s per pick-up and about 39 s for the Manager's receipt trip. The driver sets the scripted work time to 28 s per card so work and pick-ups overlap. Measured runs: offline 170 s, with failure 242 s, live (demo backend) 135 s. The report is also written to `Library/LivingOfficeDrive.txt`.

The report gives the time of each step, the console error count, and per-frame observations: riders attached to the cabin, nobody in the doorway with doors not fully open, avatar scale unchanged.

Live: start the backend (`scripts\start_wall_backend.ps1`), go live, then `LivingOfficeDrive.RunLive`. With the backend in demo mode the cards are worked by its scripted runner and everything is labelled `[SCRIPTED DEMO]`. In live mode with no model, the cards stay queued and the status page shows the backend's warning. The status and retry routes must be registered (`GET /v2/sessions/{sid}/manager/plans/{pid}/status`, `POST .../plans/{pid}/tasks/{tid}/retry` with `{idempotency_key}`); without them the wall still follows the stream events, but the "waiting for ..." reasons and Retry are unavailable.

By hand in a validation build: Wall key, Place here, then the Manager dock key or a pinch on the Manager.

## 6. Evidence per step

Levels: AUTO = EditMode tests (Unity test runner) or pytest; PLAY = Editor Play Mode with the driver (synthetic room, no headset); NT = not tested. Nothing here has been worn.

Runs on 2026-11-15 on `feature/living-world-polish` in the main tree, Unity 6000.6.8f1:

- Compile: no errors.
- EditMode: filter `LivingOffice` 23 passed, 0 failed. `MuseDesk.Tests.EditMode.Wall` 168 passed, 0 failed.
- `SpatialIsolationTests`: 2 passed, 1 FAILED: `SceneSideScripts_NameNoGatewayReducerOrSessionType` reports "HintCatalog.cs mentions SessionReducer". That file is the A15 onboarding catalogue (comments name `SessionReducer`); it is not part of this work and no Living Office file contains a forbidden token (checked by scanning the same folder with the same token list). It needs a comment-only fix by its owner.
- Backend: `test_p3_living_office.py` 4 passed; full suite 300 passed, 1 skipped (before the routes commit `51f5166`).
- PLAY `LivingOfficeDrive.RunOffline`: 13 of 13 steps, 170 s, 0 console errors.
- PLAY `LivingOfficeDrive.RunOfflineWithFailure`: 16 of 16 steps, 242 s, 0 console errors.
- PLAY `LivingOfficeDrive.RunLive` against a local backend started with `scripts\start_wall_backend.ps1 -Demo` (scripted, no model): 13 of 13 steps, 135 s, 0 console errors. Plan `mp-...`, summary artifact `a2-...` came from the backend.
- PLAY flag-off check: a fresh Play session with the committed ReleaseConfig shows all Wall flags false and no Wall dock; the desk demo (Start, decision paper, Approve, completion receipt) ran as before.

| Step | Evidence |
|---|---|
| Elevator rules in pure logic (wait spot, face door, doors fully open to cross, rider pose equals cabin, one rider, FIFO lane, exactly-once, reset mid-ride) | AUTO, poses recorded every tick |
| Elevator in the scene: dispatcher runs the cabin; riders attached to the cabin; doorway only with doors fully open; avatar scale unchanged; no ride failed, no rule violation, no fade home | PLAY, sampled every frame in all three runs (for example offline: 7052 riding frames, 0 detached; 3971 doorway frames, 0 with doors not fully open; 0 frames with a changed scale; 10 rides arrived, 0 failed) |
| Ask the Manager by a ray on the Manager | PLAY through `PokeManagerByRay` (the driver's stand-in). The real pinch path through the hand arbiter: NT |
| Plan paper: real fields, pages, smallest row 4.4 mm against a 4.3 mm floor at the assumed 0.45 m | PLAY (values read back from the paper). Legibility on head: NT |
| Proposed plan creates no cards | PLAY (board 0 cards until approve), AUTO, pytest |
| Revise (drop a step, revision 2, still no cards) | PLAY offline and live, AUTO |
| Reject, `REJECTED` stamp only after confirmation | PLAY offline and live |
| Approve, `APPROVED` stamp only after the cards exist | PLAY offline and live |
| Cards print for committed ids only; each owner collects and carries its card (all three "carried", none by chute, 96 s for three) | PLAY offline and live |
| Chute fallback for a dropped pick-up; `manager.plan_committed` arriving before the HTTP reply | AUTO only (stage drop event, flow test). PLAY: NT |
| Real task states on the status page and card tints | PLAY (states read from the board in the report). Tint seen on head: NT |
| Scripted failure offline: failure reason and blocked reason shown in the backend's wording | PLAY |
| Retry: accepted, card re-queued, plan finishes, second receipt | PLAY offline. The second press in the driver was refused because the card was no longer failed (the offline board answers at once); the in-flight lock and key reuse are AUTO only. Live retry route: NT |
| Summary: Manager trip, receipt flight, receipt card with the backend's text | PLAY offline and live |
| Live with no model (`NO_MODEL_CONFIGURED` warning on the status page) | NT |
| Live with a real model (model-written labels) | NT (no API key) |
| A3 reactions | PLAY: arbitration ran (offline run 0 played, 3 held because every completion landed during a walk; failure run 2 played, 2 held). Not looked at on head |
| A12 Listening, voice "Manager, ..." | NT (needs Wit and a live mic). Compile only |
| Recenter, pause, leaving Wall mode during a ride or a receipt flight | AUTO for the stage reset. PLAY: NT |
| Headset | NT |

Known observations from PLAY:

- Because the scripted work and the pick-up trips overlap, most completion nods are held and then dropped as stale. Truthful, but few nods are seen.
- An ordinary illustration act that waits more than 60 s behind pick-ups is dropped by design (1 or 2 per run).
- A stream resync or a session reset clears the wall's plan picture (the cards stay on the board); it is not rebuilt from the status route yet.
- Cards on the desks are small from the seat; whether they read at 1 to 2 m needs the headset.
## 7. Headset checklist

1. Flags off build: no Wall dock, voice "Manager, ..." is not understood as before, no new objects.
2. Place the habitat. Pinch while pointing at the Manager from the seat: does the lavender ring show and the paper open, without false triggers during normal use?
3. Plan paper at the desk: is every row readable at the seated distance (smallest row vs the 0.45 m assumption)? Do the two key rows clear the text? Is the paper clear of the dock, the visit card and the decision paper?
4. Step ticks, Confirm risk as a separate press, Revise, Reject, Approve: one press each with a fingertip, no double fire.
5. The stamp appears only after the cards exist. Check with the backend slowed or unreachable: no stamp.
6. Tray and pick-ups from 1 to 2 m: can the card in the tray, in the hands and on the desk be seen? Does the rider visibly wait at its aisle, face the door, and stay glued to the cabin while it moves? No walking through closed doors.
7. Three pick-ups in a row: is the wait acceptable, or should the scripted pacing change?
8. Failure run: are the failure and blocked reasons readable? Retry twice quickly: one retry only.
9. Receipt: does the paper's flight from the wall to the desk feel comfortable (and shorter with Reduced Motion)? Is the receipt readable and clear of the dock?
10. Recenter and leaving Wall mode during a ride and during a receipt flight: nobody left between floors, cards end on desks, no stuck paper.
11. Voice with a live mic: "Manager, ..." opens the paper; only the addressed avatar shows Listening.
12. Frame time with three pick-up trips and the paper open.
