# MuseDesk repair and integration log

Date: 2026-11-12. Branch `integration/audit-repair`. Each entry: what was broken, why, what changed, and how it was checked.

Check levels used below:

- **compile**: Unity recompiled with 0 errors.
- **smoke**: covered by `AgentScripts/RuntimeSmokeTests.cs`, which runs the real runtime types through the Unity pipeline.
- **pytest**: covered by the backend suite.
- **capture**: confirmed in a Scene View capture taken from the seated eye point.
- **device**: confirmed on the Quest. Only entries in `END_TO_END_TEST_RESULTS.md` carry this.

Nothing here is claimed as verified on the headset unless it says so.

## Commits

| Commit | Contents |
|---|---|
| `1b0d9f0` | Work recovered after the shutdown: backend audit fixes, stamp home pose, live token, settled placement, card console script |
| `41e1429` | Unity repair pass (sections 1 to 3 below) and the runtime smoke tests |
| `61576ce` | Layout for the settled facing, button and board polish, backend repair pass |
| `9413929` | Device-log fixes (section 7) and the first three engineering documents |
| `fa98477`, `51169b8` | On-head feedback pass (section 8): pushable props, 3D keys, card text layout |

## 1. Approval workflow

| Problem | Root cause | Change | Check |
|---|---|---|---|
| Stamps drifted off the desk | `StampTool` stored its rest pose in world space, before `WorkspacePlacement` moved the root | Rest pose stored relative to the parent | compile |
| Paper stuck on "Saving your decision…" after a network failure; stamp could never be used again | `SessionController.Respond` caught every exception and only logged; `DecisionPanel.SetError` had no caller; the stamp's one-per-decision guard was never cleared | `SessionController.RespondFailed` event; the paper shows the reason and restores its buttons; both stamps re-arm | compile |
| A second decision arriving within 3 s of the first being resolved had its paper hidden | The dismiss timer was not cancelled by a new request | Timer handle kept and stopped on a new decision | compile |
| Previous APPROVED mark still showing on the next decision | `ApprovalStampController` sits on the paper, which is inactive between decisions, so it missed the request that reactivated it | Clears itself in `OnEnable` | compile |
| Second demo cycle could be neither stamped nor resolved | Constant decision id and a resolution that was never reset | Each cycle gets its own decision id and a fresh resolution | smoke |
| Reject looked like success | The demo's reject branch emitted `task.completed` | Reject and request-changes emit no completion and no artifact | smoke |
| "Ask why" gave no sign it was working | No interim state | Paper expands and shows "Asking…" until the answer arrives | compile |
| Expired decision (backend restarted) showed a raw state | No case for `expired` | Paper says the request expired and nothing was run | compile |

## 2. Agents, handoffs, factory, stations

| Problem | Root cause | Change | Check |
|---|---|---|---|
| Researcher's handoff walk cut off after 1.2 s | `AgentRegistry` sent it home while `AgentHandoffController` was walking it to the developer | Handoff controller owns the agent while busy (`AnyBusy`); the registry stands down | compile |
| Developer to tester handoff never played offline | `TryHandoff` returned when busy | Handoffs queue (bounded at 4) and drain in order; state released on disable | compile |
| Carried token appeared only at the give beat and stayed in the receiver's hand | Token was shown once and never hidden; scale came from the bone | Token rides in the courier's hand, moves to the receiver, then is put away; world size preserved | compile |
| Agents stayed turned along their walk direction | Only the home position was recorded | Home rotation recorded; the last quarter of the walk home turns back to the station | compile |
| Paper delivery walked into the factory | Target was a point 0.4 m in front of the head, which lands inside the conveyor | Researcher walks to the `DeliveryZone` anchor, placed on the user's side of the desks and clear of the factory | capture |
| Approval lit the factory's test-pass gate | `decision.resolved approved` was mapped to `PlayTestSuccess` | Approval plays the blueprint transfer. Test success plays only when the artifact carried passing test counts. Development-complete plays when the developer finishes coding | compile |
| Factory clips restarted on every progress update | Handler ran on every `agent.status_changed` | Acts on state transitions only | compile |
| Shelf token, completion chime and factory delivery fired for baseline tests and cancelled branches | They listened to every `task.completed` | New reducer event `WorkDelivered`: a completion that references an artifact. Those three consumers use it | smoke |
| Tester lamp stuck on Testing; PASSED shown for any completed task | No branch for `complete`; pass was tied to `task.completed` | Lamp follows the tester's own run: testing to complete is a pass, testing to error is a fail | compile |
| Lamps differed only by size, and overwrote their authored scale | `localScale` set to an absolute value | Scale relative to the authored size, plus colour and emission through a property block | compile |
| Monitors showed only generic text | `progress_message` was dropped by the reducer | Progress carried through the reducer, `AgentRegistry` and `AgentView` to the monitor | smoke |
| Demo pipeline skipped testing | The scripted approve branch went from artifact straight to completed | Tester runs and completes before `task.completed`, the same order as the live pipeline | smoke |

## 3. Session, board, focus, voice, persistence

| Problem | Root cause | Change | Check |
|---|---|---|---|
| Task board always the demo board, even live | `LiveAgentGateway` never stored the session token | Token stored and exposed | compile |
| After a restart or resume, agents showed idle and a pending decision had no paper | `LoadSnapshot` raised no event; the pending decision id was only set by a live event | Reducer raises `SnapshotLoaded`; agents and the paper are projected from the snapshot; `PendingDecisionId` adopted from it | smoke |
| No sign of a lost connection | `ConnectionChanged` had no subscribers | Status sign reads "LIVE · Reconnecting to desktop…" and returns to the mode label on reconnect | compile |
| A failure during start-up hung every dependent system silently | Unprotected `async void Start` | Caught, logged, shown on the status sign | compile |
| Handlers reading `PendingDecisionId` during `decision.requested` could see the previous value | Set by a late subscriber | Set before the event is applied | compile |
| New task card was blank with priority P0 | Demo `task.created` carried only `source_mode`; the reducer ignored priority on create | Demo sends title, state, priority and revision like the live backend; reducer reads priority on create | smoke |
| A card command gave no feedback | `AssignResolved` had no subscribers | Card state line shows "Sending…", then the confirmation or a plain-language reason for 2 to 4 s | compile |
| Per-frame boxing on every card | `PropertyInfo.GetValue` each frame | Typed delegate bound once | compile |
| Artifact viewer skipped page one | Viewer 1-based, backend and demo 0-based | 0-based internally, 1-based on the label | compile |
| Held-back updates vanished when focus was turned off | Log cleared on exit | Log kept until focus is next enabled, headed "While you were focused:"; lines carry the real summary | compile |
| Layout never saved | `Save` had no caller | Saves on application pause. Saved layouts are tied to the build, so a new build's authored layout is not overridden by stale data | compile |
| "Don't approve" and "not sure" approved | Substring matching with the yes list tested first | `WitVoiceSource.Classify`: questions first, then negation, whole-word matching, approve only for a short unambiguous phrase | smoke (18 phrases) |
| Spoken questions were answered into the void | Empty callback | Answer shown on the decision paper; a question that names an agent is routed to that agent | smoke |
| Cancelled branch recorded locally as completed | Reducer set `completed` on any `task.completed` | Uses the payload's `task_state` | compile |

## 4. Scene layout for the settled facing

The user confirmed on 2026-11-12 that **the agents should face them**. `WorkspaceRoot` +Z points at the user; the user's right is root -X. Applied by `AgentScripts/ArrangeForUserSide.cs` (idempotent; re-run to re-apply).

| Item | Root-local position | Distance from the eye |
|---|---|---|
| Placement depth / drop | 0.62 m / 0.30 m | |
| Task board (user's left, eye level, angled in) | (0.46, 0.36, 0.38) | 0.52 m |
| Artifact panel (user's right, above the inbox) | (-0.50, 0.40, 0.30) | 0.60 m |
| Approve stamp / reject stamp | (-0.13, 0.02, 0.40) / (0.13, 0.02, 0.40) | 0.38 m |
| Focus / Explain buttons | (-0.42, 0.05, 0.37) / (-0.31, 0.05, 0.42) | 0.55 m / 0.45 m |
| Hand-over spot | (-0.20, 0, 0.17) | 0.58 m |

Also in that pass:

- `CardActionConsole` wired on the task board: Pause and Priority buttons beside "+ New", plus a line naming the selected card. The 1.8 cm per-card pokes are switched off.
- Four decor props moved out of the hand-over spot and the stamp area.
- The orange grab-test cube deactivated and `GrabReadout` disabled. Both are still in the scene and can be re-enabled.

Check: capture from the seated eye point (`Screenshots/eye_front.png`, `eye_board.png`, `eye_right.png`). All workspace poke buttons face the eye (dot product 0.83 to 0.97, `AgentScripts/ButtonProbe.cs`).

## 5. Buttons and board (`AgentScripts/PolishBoardAndButtons.cs`)

- Labels are single-line, about 2 cm tall, and use glyphs the font has: `+ New`, `Pause`, `Priority`, `< Prev`, `Next >`, `Close`, `Focus`, `Explain`.
- `PrevPoke` press visual re-centred on its surface.
- Artifact Prev / Next / Close moved under the panel root, so they appear only when an artifact is open.
- `PreviewCard_*` and `DependencyLink_*` placeholders deactivated.
- The diagnostics text is now a two-line status sign (mode, then what is happening). The full developer HUD is still available through `DiagnosticsOverlay.verbose`.

Check: capture.

## 6. Backend (93 to 110 tests)

All in `backend/`. New tests are in `tests/test_audit_repair.py` (17), `tests/test_audit_fixes.py` and `tests/phase2/test_audit_fixes_v2.py`.

| Problem | Change | Test |
|---|---|---|
| Restart with a pending decision left the run stuck: respond reported success but nothing ran | Start-up reconciles interrupted runs: unfinished tasks are marked failed, agents set to error, the pending decision expired, matching events published (`app/run_recovery.py`) | `test_restart_with_pending_decision_reports_the_run_failed`, `test_restart_does_not_touch_finished_runs` |
| A failed live run was reported as completed; worker exceptions were swallowed | `task.failed` with a sanitized message; agents settle to error | `test_live_failed_run_emits_task_failed_not_completed`, `test_live_worker_exception_is_reported_and_sanitized` |
| Cancelled branch announced as a bare `task.completed` | Payload carries `task_state` (`completed`, `failed`, `cancelled`) | `test_cancelled_branch_event_carries_cancelled_task_state` |
| Two runs at once in one session | 409 `RUN_IN_PROGRESS`; a replay of the original key still returns the original result | `test_second_task_is_refused_while_a_run_is_in_progress` |
| Idempotency key from another route accepted | 409 `IDEMPOTENCY_KEY_REUSE` on task create and on respond | `test_task_create_with_a_key_used_by_another_route_is_409`, `test_respond_with_key_from_another_request_is_409_not_a_replay` |
| Scripted producer swallowed exceptions | Shared `fail_run`; also emits `task.failed` for each unfinished child | `test_demo_producer_exception_is_reported` |
| WebSocket handler outlived its client | Shared `pump_events` races the queue against the disconnect (`app/ws_stream.py`) | `test_event_pump_returns_when_the_client_disconnects_while_idle` |
| Sandboxed tests could read backend secrets | Child environment drops `MUSEDESK_*` and anything named like a key, token, secret, password or credential | `test_sandboxed_tests_cannot_read_backend_credentials` |
| Timeout path could raise | Output decoded and capped | `test_test_run_timeout_is_reported_as_timed_out` |
| v2 task create emitted no event; v2 commands and diagrams not scoped to the owning session | `task.created` with card fields; session checks | `test_create_task_emits_task_created_with_card_fields`, `test_task_commands_are_scoped_to_the_owning_session`, `test_diagram_rejects_another_sessions_artifact` |

Additive payload changes the Unity client now reads: `task_state` on `task.completed` / `task.failed`; `decision.resolved` with state `expired`. No fields were renamed and no event types were added.

## 7. Device-log fixes

Found by reading `adb logcat` while the first build of the day ran on the Quest.

| Problem | Root cause | Change | Check |
|---|---|---|---|
| A warning with a stack trace was logged every frame (241 times in about a minute) | MRUK `EnableWorldLock` was on and fighting the tracking space; off-desk placement never uses scene anchors | World lock turned off (`AgentScripts/PerfQuickWins.cs`) | To be confirmed in the next device log |
| Check mark rendered as a box | `✓`, `✗`, `↻` are not in the TMP font | Replaced with words on the paper and the monitors | compile |
| Per-frame boxing in three scripts | `PropertyInfo.GetValue` on `SelectingPointsCount` every frame | Shared `GrabState.BindSelectingPoints` binds a typed delegate once; used by `StampTool`, `HeldPaperController`, `GrabbableRigidbody`, `TaskCardView` | compile |
| Debug logging in the grab path | `MDGRAB` logs left in `HeldPaperController` | Removed | compile |
| Card status tint created a material per card | `renderer.material` | Property block | compile |
| Physics at 50 Hz, pushable props with no interpolation | Project defaults | Fixed timestep 1/72 s, maximum step 0.1 s, interpolation on | not measured |
| Miniatures clip when leaning in | Near clip 0.1 m | 0.05 m | not checked on head |

## 8. On-head feedback pass (`AgentScripts/PolishPass2.cs`)

The developer reported three things from the headset on 2026-11-12: hands pass straight through objects, text overflows on the task board, and the buttons are flat squares. All three changes below are **not yet re-checked on head**.

### Hands and objects

A real hand cannot be stopped, so the goal is that things react to it.

- New `PushableProp` component. Thirteen small desk objects (mug, plant, books, clock, toolbox, package box, decor stamp, pen, rolled blueprint, lamp, award, and the two toy props) are now dynamic rigid bodies with box colliders fitted to their meshes. The hand-tracking physics capsules, which were already in the scene, push and topple them.
- They cannot be flung: depenetration and linear speed are capped. They stay kinematic for 3.5 s after launch and whenever the workspace is re-placed, so moving the office does not scatter them.
- They tidy themselves: a prop left still and out of place for 4 s glides home with a small hop; one that strays more than 45 cm goes home at once.
- The three desks and the factory have static box colliders, so pushed props stop against them.
- The toy-prop tray moved from behind the desks (0.75 m away, and inside the QA desk) to the user's left front.
- Check: starting overlaps computed in the Editor. One remains (coffee mug and rolled blueprint, 2 cm) and will resolve itself with a nudge at start-up.

Unchanged: the desks, board and agent bodies do not move, and the rendered hand is not held at surfaces. Agents still lean away from a fingertip.

### Task board text

- Root cause: the title was a 2.4 cm glyph in a 7.5 cm box with wrapping and no clipping, and five more 1.0 to 1.3 cm lines shared a 5 cm tall card.
- Cards are now 12 x 7.5 cm (template scale 1.5). Layout: owner and priority in the header, title (two lines, shrinks, then ellipsis), state, the LIVE / SCRIPTED DEMO label, and the id as small reference text. Every label has a box and an overflow mode.
- A second cause surfaced while fixing it: TextMeshPro auto-sizing does not work at font sizes like 0.05 (labels authored at unit scale); the text vanishes. Such labels are now at 1/100 scale with 100x the font size.
- Same treatment for the artifact panel (the body was a 2.6 cm glyph, about 17 characters per line), the decision paper, the monitors, the inbox and the status sign: text shrinks to fit, then truncates, and can no longer spill out of its panel.
- Check: capture with five sample cards, including one long title (`docs/review_screenshots/02_task_board.png`).

### 3D buttons

- All 16 poke buttons are now keys: a dark housing fixed to the surface (back plate and four walls) and a coloured cap that rides on the Interaction SDK press visual, so it travels into the housing under the finger. One travel distance for every key.
- Cap colours by function: approve green, reject red, ask / explain blue, focus violet, new task teal, close muted red, others neutral.
- Labels ride on the cap face (a label left on the surface plane would be hidden by the cap), are dark for contrast, bold, and auto-sized to the key.
- Materials: `Assets/MuseDesk/Art/Materials/Buttons/` (URP Simple Lit, GPU instancing on). This adds about 6 renderers per button.
- Check: captures (`04_board_buttons_3d.png`, `05_desk_buttons_3d.png`).

## 9. Test tooling added

- `AgentScripts/RuntimeSmokeTests.cs`: 54 checks against the real `SessionReducer`, `DemoAgentGateway`, `Phase2DemoGateway`, `Phase2Reducer`, `WitVoiceSource.Classify` and `VoiceDecisionController.AgentFor`. It exists because the NUnit runtime suite cannot reference `Assembly-CSharp`.
- `AgentScripts/SceneHealth.cs`, `SceneProbe.cs`, `ButtonProbe.cs`, `LayoutBounds.cs`, `TextProbe.cs`, `ViewFrom.cs`: read-only scene reports and eye-point captures.
- `AgentScripts/Preview.cs`: puts sample cards on the board and shows the decision paper for captures, then reloads the scene from disk so nothing is saved.
- `backend/scripts/live_e2e.py`: drives a real running backend (live producer) through an approve run and a reject run over HTTP and WebSocket. Start the backend on a spare port, then `backend\.venv\Scripts\python.exe backend\scripts\live_e2e.py http://127.0.0.1:<port>`. Its 36 checks are listed in `END_TO_END_TEST_RESULTS.md`.
