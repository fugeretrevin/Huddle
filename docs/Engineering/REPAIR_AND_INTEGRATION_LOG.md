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
| later commits | Performance quick wins and these documents (see `git log`) |

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

## 7. Test tooling added

- `AgentScripts/RuntimeSmokeTests.cs`: 54 checks against the real `SessionReducer`, `DemoAgentGateway`, `Phase2DemoGateway`, `Phase2Reducer`, `WitVoiceSource.Classify` and `VoiceDecisionController.AgentFor`. It exists because the NUnit runtime suite cannot reference `Assembly-CSharp`.
- `AgentScripts/SceneHealth.cs`, `SceneProbe.cs`, `ButtonProbe.cs`, `LayoutBounds.cs`, `ViewFrom.cs`: read-only scene reports and eye-point captures.
