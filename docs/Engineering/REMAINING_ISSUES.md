# MuseDesk remaining issues

Date: 2026-11-12. Everything known to be open after the repair pass, with impact and the work left.

> **Update, 2026-11-12 evening.** A later pass closed several items below: the task board now launches the real pipeline for one card type, cards no longer stick in `pause_requested`, dependencies are validated, artifacts attach to cards, the research template is labelled, the standalone demo opens the artifact viewer, drop zones highlight, the stamp is gated on a taken paper, the decision paper was re-laid out, and agents now play a clip per state. See `FINAL_POLISH_CHANGELOG.md`. What is still open, and the on-head checklist for the current build, are in `FINAL_XR_VALIDATION.md` and `FINAL_COMPETITION_READINESS.md`. The tables below are kept as the record of the earlier state. Severity uses P0 (blocker) to P4 (optional).

No P0 is known. The largest open risk is that most of the repair pass has not been seen on the headset (section 1).

## 1. Needs a person in the headset

These are implemented and pass every check available without a headset. None of them is confirmed on device.

| # | Check | Why it matters |
|---|---|---|
| 1 | The office appears in front of you with the agents facing you, about 0.6 m away and 0.3 m below eye level | The whole layout was rearranged for this on 2026-11-12 |
| 2 | Stamps are within easy reach, stay on the desk, and return to their spot when released | Stamps previously drifted to a stale position |
| 3 | The task board (on your left) is readable and its cards can be grabbed and dragged to an agent | Board moved from above the head to the side; reach to the far agent is about 0.75 m |
| 4 | "+ New", "Pause", "Priority" on the board, and "Focus", "Explain" on your right, depress and act when poked | All were re-oriented or re-labelled; Pause and Priority are new |
| 5 | The Researcher walks to a spot on your side of the desks and hands over the paper within reach | Hand-over spot is new; estimated 0.58 m from the eye |
| 6 | After approving: Researcher walks to the Developer, then Developer to Tester, with the token carried and no agent left facing the wrong way | Handoff queueing and return rotation are new |
| 7 | After rejecting: no shelf token, no completion chime, no PASSED lamp | Previously all three fired |
| 8 | Frame rate holds; no visible stutter | Never measured (section 4) |
| 9 | Small desk props move when your hand pushes them, do not fly off, and put themselves back after a few seconds | New on 2026-11-12; thirteen dynamic bodies plus the hand capsules is also a frame-rate question |
| 10 | Card text stays inside the cards and is readable at the board's distance; a long title wraps, shrinks, then truncates | New layout; only checked in an Editor capture |
| 11 | Keys look three-dimensional and the cap sinks into its housing under your finger; labels are readable | New; the cap rides on the SDK press visual, which only moves on device |

One piece of device evidence exists: during the 14:42 session on the Quest, with hands tracked, the log recorded `self-stamp Approve` followed by `decision demo-auth-approval-1 -> approved (applied=True)`. That shows the physical stamp reaching the approval path on device. It was read from the log; nobody reported what it looked like.

## 2. Backend, found and not fixed

| Sev | Issue | Impact | Why it was left |
|---|---|---|---|
| P2 | Task-board cards are not backed by an executor. Assigning, resuming or pausing a card changes the card, but no agent does that work. Resuming a card with an agent sets it to `running` (labelled `live` under the live producer); a later pause stays `pause_requested` forever | The board manages cards, not real work. Scenario B can only pass at the "command accepted and reflected" level | Needs a product decision (should a card start a pipeline run?) and coordinated Unity changes |
| P2 | In live mode the researcher's recommendation is a fixed string, yet its status and the decision event are labelled `live` (disclosed only as `provenance: "deterministic"`) | Sits uneasily with the "never present scripted work as live" rule. The developer and tester work is real | A test asserts the current label; changing it changes what the paper badge shows. Needs a decision |
| P3 | "Changes requested" says a revised proposal is pending, but nothing produces one. The developer stays in `waiting` after a reject or a request for changes | Scenario E's revise-and-retest loop does not exist; a failed test run ends the run | Requires new orchestrator behaviour |
| P3 | v2 task create accepts unknown or foreign dependency ids; such a card is `blocked` forever | A mistyped dependency silently strands a card | Small, but nothing re-evaluates dependencies yet either |
| P3 | Live artifacts reference the v1 task id, which has no card | The artifact panel opens, but an artifact cannot be tied to a card | Follows from the first row |
| P3 | Session tokens never expire; the WebSocket token travels in the query string | Acceptable on `127.0.0.1` over USB; not for a network deployment | Out of scope for a tethered demo |
| P4 | Events are persisted in two commits; a crash between them corrupts replay | Rare | |
| P4 | A subscriber more than 1,000 events behind is dropped silently | Rare | |
| P4 | Live sandbox workspaces are only cleaned on graceful shutdown | Disk clutter under `backend/sandbox_workspaces` | |

## 3. Unity, found and not fixed

| Sev | Issue | Impact | Work left |
|---|---|---|---|
| P2 | The artifact viewer never opens in the standalone demo: there is no `phase2_demo` fixture, so no v2 artifact event is raised | The panel is only demonstrable in live mode | Author a demo fixture with a labelled sample artifact |
| P2 | Scenario E (failure and revision) has no client flow, because the backend has none (section 2) | A failed test shows the red lamp and the reject chute, then stops | |
| P3 | No drop-zone highlight when a card hovers over an agent (`StationDropZone.halo` unassigned) | Dragging gives no "this will assign" cue; the result only shows on the card afterwards | Add a halo ring under each agent and assign it |
| P3 | Decision delivery is always the Researcher, whatever agent raised the decision | Correct for today's orchestrators only | Route by `agent_id` |
| P3 | The stamp zone is live on the blank paper while the agent still holds it | A decision can be stamped unread | Decide whether that is intended; if not, gate the zone on the paper being taken |
| P3 | The shelf is not rebuilt from a snapshot after a restart | Completed work from before a restart is not shown | Count delivered completions in the snapshot |
| P3 | Architecture diagram labels are about 3.6 mm tall and nodes cannot be selected by hand | The "Explain" button shows a diagram that is hard to read | Enlarge and add poke selection |
| P3 | Agents have no listening or speaking visual; thinking, blocked and failed share other states' visuals | Voice has no on-character feedback | |
| P3 | `RuntimeSettings.preferOfflineDemo` is off, so every launch waits up to 5 s for a backend before starting the demo | Slower start for a judge with no PC | Turn it on for the standalone submission build |
| P3 | MRUK still loads the room and asks for scene permission at launch although off-desk placement never uses the table | An unnecessary permission prompt | Disable load-on-startup, or use the table height for placement |
| P2 | Hands still pass through the desks, the board and the agents' bodies, and the rendered hand is not held at a surface | Only small props, keys and (by leaning) agents react to a hand | Options: limit the rendered hand at surfaces (the parked `DynamicFingerCollision` needs the `WhenHandVisualUpdated` hook), and let agents be pushed further |
| P3 | Decision paper: small body text with weak contrast, the stamp target overlaps the "Ask why" key, and "Details" hangs below the paper's edge | The flagship document is the least polished surface | Re-lay out the paper: larger dark text, keys in one row inside the frame, stamp target in its own area |
| P3 | The toy-prop tray is large and close on the user's left front | Visually heavy next to the task board | Shrink or remove the tray; the props can sit on the desk directly |
| P3 | The agents themselves are 0.64 to 0.77 m from the eye | Beyond comfortable seated reach for dragging a card onto the far agent | Consider a closer placement depth after an on-head check |
| P3 | 3D keys add about 6 renderers each (16 keys) | Adds to the draw-call concern in section 4 | Merge each housing into one mesh |
| P4 | `Scripts/Art/AgentAnimatorBridge.cs` duplicates `Scripts/Agents/AgentAnimatorBridge.cs` and allocates per frame; only the latter is used in the scene | Dead code | Delete or merge |
| P4 | The NUnit runtime suite (`Phase2RuntimeTests.cs`) is compiled out | Real-type tests run only through `RuntimeSmokeTests.cs` | Move the runtime into an assembly definition |
| P4 | `[BuildingBlock] Grabbable Item` (test cube) and `GrabReadout` are deactivated, not deleted | None at runtime | Delete before submission |

## 4. Performance, not yet addressed

Static findings only; nothing has been measured on device. In likely order of payoff:

1. **About 520 separate renderers.** Merge meshes per material in the Blender export (each workstation is 53 to 60 meshes), or combine static children at start-up. Then measure draw calls with OVR Metrics.
2. **Foveated rendering is off** while dynamic resolution may go to 1.6x. Enable it and set a level; consider capping dynamic resolution at 1.0 to 1.2.
3. **The Quest quality level uses a URP asset inside `Library/PackageCache`.** Create `Assets/Settings/URP_Quest.asset`, assign it, and turn off main-light shadows, additional lights and cookies (the scene has no lights).
4. **All materials are URP/Lit.** Switch to Simple Lit or turn off specular highlights and environment reflections.
5. **Hand physics capsules are on the Default layer** with a full collision matrix. Move them to the existing `FingerCollide` layer and trim the matrix.
6. `renderer.material` in `TaskCardView`, `CardRoleTint` and `StationDropZone` creates material instances. Use a property block.
7. FBX import: Read/Write on, no mesh compression, Generic rig on static props.
8. Stack traces for Log and Warning were set to None through `PlayerSettings`, but the Meta Quest build profile carries its own value. Confirm in a device log that ordinary log lines no longer carry stack traces.

Applied already: MRUK world lock off (it logged a warning with a stack trace every frame on device), per-frame reflection reads replaced, physics at 72 Hz with interpolation on the pushable props, near clip 5 cm, debug readout disabled.

## 5. Voice

Voice needs a Wit.ai client token and network access, so it cannot work in the standalone demo. With a token: matching is negation-safe, the answer appears on the paper, and a question that names an agent goes to that agent. Still missing: a listening indicator on the agent, cancelling a question in flight, and questions when no decision is pending. Not tested on device.

## 6. Scenario status

| Scenario | Status |
|---|---|
| A. Fresh launch | App launches on the Quest and runs the demo session. Placement in front of the user not yet confirmed by a person |
| B. Task assignment | Command path and card feedback work; no agent starts working on a card (section 2) |
| C. Human approval | Backend and client paths covered by tests; stamp approval observed in a device log; full on-head pass not done |
| D. Complete pipeline | Covered by backend tests (live) and smoke tests (demo); choreography not seen on head |
| E. Failure and revision | Failure is reported honestly; revision does not exist |
| F. Backend disconnection | Client shows reconnecting and resumes from the last event; a restart fails the run cleanly. Not exercised with a headset |
| G. Focus mode | Covered by code and backend tests; not exercised on head |
| H. App restart | Snapshot projection and layout save implemented; not exercised on device |

Details and evidence: `END_TO_END_TEST_RESULTS.md`.
