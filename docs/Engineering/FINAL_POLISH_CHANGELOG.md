# MuseDesk final polish changelog

Date: 2026-11-12 (evening pass). Branch `integration/audit-repair`. This follows `REPAIR_AND_INTEGRATION_LOG.md` and covers the work done after the expert review of `PROJECT_SUMMARY_FOR_REVIEW.md`.

Evidence levels are the same as in `END_TO_END_TEST_RESULTS.md`: **automated**, **live server**, **device log**, **on head**. Where a line says only "capture", it was checked in an Editor capture from the seated eye point, which shows appearance and nothing about interaction. Validation status for each item is collected in `FINAL_XR_VALIDATION.md`.

## 1. The task board now starts real work

Before: assigning a card changed the card and nothing else. Now one card type launches the real pipeline.

### Backend (`backend/app/phase2/board_bridge.py`, `run_launcher.py`, and changes in `routes.py`, `task_graph.py`, `store.py`, both orchestrators, `run_recovery.py`)

- A card can carry `task_template`. The only executable template is `auth_demo`. Cards report `executable` and, once started, `run_task_id`.
- Assigning an executable card to the **Researcher** launches the same run `POST /v1/.../tasks` launches, through the same producer (real sandbox in live mode). The card then follows the run on the v2 stream: `task.started` (running), `decision.requested` (awaiting approval, with the decision id), `task.resumed` (running, after approval), `artifact.created` (now referencing the card), then `task.completed`, `task.failed`, or `task.completed` with `task_state: cancelled` after a rejection.
- Refusals leave the card unchanged and start nothing:

| Situation | Code |
|---|---|
| Assign an executable card to the Developer or Tester | `ROLE_INCOMPATIBLE` |
| Assign while another run is in progress | `RUN_IN_PROGRESS` |
| Assign a paused or blocked executable card | `TASK_PAUSED`, `DEPENDENCIES_NOT_READY` |
| Pause a card whose pipeline is running | `TASK_NOT_PAUSABLE` |
| Create with an unknown template or dependency | `UNKNOWN_TASK_TEMPLATE`, `UNKNOWN_DEPENDENCY` |

- Replaying an assign never starts a second run. A finished card cannot start another.
- Cards with no template stay honest: `executable: false`; resume returns them to `queued` (it used to say `running` with nothing running); pause goes straight to `paused` (it could stick in `pause_requested` forever).
- A dependent card is released to `queued` when its dependency completes.
- Restart: a card whose run was interrupted is marked `failed` with a v2 event, alongside the existing v1 recovery.
- Linkage: v1 events of a card-launched run carry `board_task_id`; v2 artifacts carry the card's id.
- Contract: `contracts/phase2/snapshot_v2.schema.json` gained the three card fields. Nothing was renamed and no event type was added.

Evidence: **automated** 140 backend tests (30 new, in `tests/test_board_bridge.py`). **Live server** 56 of 56, including a card created over HTTP, assigned, approved, completed with a real diff and a real test report attached to the card.

### Unity

- `Phase2DemoGateway` mirrors the same rules offline, driven by the scripted session's real events through `DemoBoardRunBridge` (no timers). Same error codes and messages.
- The reducer follows `decision.requested` on the v2 stream and honours `task_state` on completion, so a cancelled card is not shown as completed.
- Cards say what they are: a queued runnable card reads "Ready: drop on Researcher"; a card nothing runs reads "Planned". After a drop the card says "Researcher started it", or "Planned for Tester. Not run yet", or the reason it was refused.
- Drop zones: an executable card shows the green halo only on the Researcher and red on the others.

Evidence: **automated** 103 runtime checks on the real types (49 new), all passing.

## 2. Research provenance

The Researcher's recommendation is a fixed template. It is now labelled as such everywhere it appears:

- `decision.requested` carries `research_provenance: "template"` and `research_provenance_label: "Template recommendation. Not live AI research."` in both producers.
- The Researcher's progress line reads "Preparing a template recommendation".
- The decision paper prints the label under the recommendation; the archive repeats it.
- Live developer and tester events and artifacts carry `work_provenance: "sandbox_execution"`, so real work is separately identifiable.
- "Changes requested" no longer promises a revised proposal: "Changes requested: run stopped. Start the task again after revising it."

No model call was added; no API key is needed. Evidence: **automated**, **live server** (the label is asserted on the card's decision event).

## 3. Standalone demo

- **Starts immediately.** The scripted session is adopted at launch; a desktop connection is attempted in the background. If it succeeds before the user has started anything, the app switches to live; otherwise it stays in the demo for that launch. The old path waited up to 5 seconds first.
- **Nothing starts by itself for 40 seconds.** The user is guided to start it. If they do nothing, the scripted run starts automatically.
- **Start and Replay keys** (`SessionController.StartDemo`, `ResetDemo`). Replay cancels the run, clears every view (paper, stamps, lamps, factory, hand-offs, shelf, alerts) and restores the seeded board. A run generation counter stops late events from a cancelled run arriving afterwards.
- **Seeded board:** one runnable card, "Build the login flow (OAuth scaffold)", and three planned cards.
- **Sample artifacts:** approving produces a labelled code diff (3 pages) and test report (2 pages) for the card. Every page begins "SCRIPTED SAMPLE (offline demo). Not produced by a live agent." The artifact viewer opens and pages offline.
- **Reject** ends the card `cancelled`, with no artifact, no shelf token, no completion chime.

Evidence: **automated** (runtime checks cover start, replayed start, reject, artifacts and paging, reset mid-run, the seeded board).

## 4. Decision paper

| Before | Now |
|---|---|
| White text on a cream sheet, title 6 mm | Dark ink on an unlit cream sheet; title 9 to 13 mm, summary 6.5 to 8.5 mm, auto-sized |
| Mode shown as `[BLOCKING] [DEMO]` in 3 mm text | A band across the top that reads "SCRIPTED DEMO   needs your decision" or "LIVE   needs your decision"; the colour only reinforces the words |
| Grabbing the paper dumped the full rationale | Opens on the summary and the recommendation; the longer explanation is behind the Details key |
| No statement of where the recommendation came from | "Template recommendation. Not live AI research." (or, offline, "Scripted sample. Nothing is running on a computer.") |
| Stamp target under the "Ask why" key | Its own marked circle on the right, labelled "Stamp here" |
| "Details" hung below the sheet; a Voice key was shown | Four keys in one row inside the frame: Approve, Ask why, Details, Reject. Voice is not offered (it needs a token and network) |
| Paper blinked out after a decision | It shrinks and glides to the out-tray, waiting until the user lets go |
| Linear scale-up when taken | Eases out with a slight overshoot |

Safety of the approval gesture:

- **A stamp only counts on a paper the user has taken.** `StampTool` requires `HeldPaperController.ReadyForDecision`: taken from the agent, fully unfolded, and readable for at least 0.8 s. The stamp target is hidden while the agent holds the blank sheet. Previously a stamp brushed past the agent's hand could approve an unread decision.
- **One response per decision.** Buttons and stamps share one in-flight response (`DocumentDeliveryController.BeginResponse`); a second press or a stamp landing mid-request is ignored until the backend answers or the request fails.
- The backend is still the only authority: the outcome mark and status line appear on `decision.resolved`.

Evidence: capture (`docs/review_screenshots/06_decision_paper_summary.png`, `07_decision_paper_details.png`). Gating and single-response logic: compiled, not exercised on device.

## 5. Hand interaction

- **Grab-hover feedback** (`GrabHoverHighlight`): the paper, both stamps and every task card show an indicator when a hand is within grab range and switch colour while held, with a soft tick. It reads the Interaction SDK's own hover state, with a 0.06 s enter delay and 0.18 s exit delay so tracking jitter does not flicker it.
- **Drop-zone halos:** a disc on the desk under each agent, green where a drop would be accepted, red where it would be refused.
- **Assign dock:** three labelled pads (Researcher, Developer, Tester) under the task board, at the board's distance. A card can be assigned without reaching 0.75 m across the desks.
- **Decision archive:** each decided paper adds a sheet to the out-tray. The "Filed" key pages through what was decided, the outcome, live or demo, and the resulting artifact (files, tests). Rejected decisions are filed as rejected with no result. The archive survives a replay.
- Removed from the submission experience: the Explain key (the diagram's labels are unreadable and its nodes cannot be selected) and the Voice key on the paper. Both objects remain in the scene.
- The toy-prop tray was scaled to 55%; it was the largest object in view.

Not attempted: stopping the rendered hand at surfaces, and distance grab. Real hands cannot be stopped; the effort went into the paper, stamps, cards and keys.

## 6. First-use guide

A low sign between the stamps shows one line: the next useful thing to do. It follows real events, not a timer: welcome, start (the Start key pulses), agents working, take the paper, read and decide, approved or rejected, done. A "Hide tips" key turns it off. It retires itself after the first completed run.

## 7. Mascots

Finding: the animator controller the three agents use has only a `MoveSpeed` parameter and one state per clip. The old bridge set `StateId`, `IsMoving` and trigger parameters that do not exist in it, so **the agents had only ever played their idle clip**. Earlier documents describing state-driven animation were wrong.

- `AgentAnimatorBridge` now detects the controller and cross-fades the real clips: researching plays Reading, coding Typing, testing Inspecting, waiting Waiting_For_Input, error Worried, walking Walk_Loop; reactions (Task_Completed, Error, Success, Failure, Exchange_Package, Receive_Document, Offer_Document) play once on a real state change, not on repeated events.
- `AgentGaze`: clamped, smoothed head and neck aim. An agent delivering a paper looks at the user and glances at the paper; a courier looks at the colleague it is handing to; an agent in `waiting` or `error` looks at the user; at the desk it keeps the clip's pose with occasional glances (randomised per agent, rarer in focus mode); a fingertip within 15 cm draws its eye.
- `AgentIdleLife`: subtle breathing and rare weight shifts, reduced while working and off while walking or reacting.
- Hooks for voice (`SetListening`, `SetSpeaking`) exist and are not yet called.

Evidence: compiled; the builder wired head, neck, spine and chest on all three agents. **None of this has been seen on device**, and it changes what every agent looks like in every state.

## 8. Rendering and runtime cost

- The Quest quality level now uses a project-owned URP asset, `Assets/Settings/URP_Quest.asset` (it pointed into `Library/PackageCache`). Main-light shadows, additional lights, light cookies and LOD cross-fade are off: the scene has no lights. MSAA 4x, HDR off, render scale 1 unchanged.
- Earlier the same day: MRUK world lock off (it logged every frame), per-frame reflection removed, physics at 72 Hz.

Measurements are in `FINAL_PERFORMANCE_REPORT.md`.

## 9. Files

New runtime scripts: `GrabHoverHighlight`, `FirstUseGuide`, `DecisionArchive`, `AgentGaze`, `AgentIdleLife`, `BonePoseLayer`, `DemoBoardRunBridge`. Rewritten: `DecisionPanel`, `SessionController`, `Phase2Bootstrap`, `Phase2DemoGateway`, `AgentAnimatorBridge`. New fixture: `Assets/MuseDesk/Resources/phase2_demo_artifacts.json`.

Scene builders (idempotent): `AgentScripts/FinalPass.cs` (`Paper`, `Affordances`, `Guide`, `Archive`, `DemoKeys`), `BuildAgentLiveliness.cs`, `QuestRenderSetup.cs`.

## 10. Work by another tool in the same tree

While this pass ran, a separate art tool added models under `Assets/MuseDesk/Art/Models` (affordance, feedback, stamp, key-cap and "optimized" workstation and board meshes), modified five existing FBX files (the Kanban frame, the task card, the approval stamp and two paper models), and added `Assets/MuseDesk/Art/Scripts/VisualPolishAffordanceController.cs`. That script did not compile (it subscribed to events with the wrong names and signatures) and blocked the whole project; two signatures were corrected so it compiles. None of those files are part of this pass's commits, and the new models are not placed in the scene. The modified FBX files **are** used by the scene and are included in any build made from this working tree.
