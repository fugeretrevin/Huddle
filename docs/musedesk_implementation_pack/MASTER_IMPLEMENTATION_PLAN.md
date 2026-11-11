# MuseDesk — Complete Implementation Blueprint

**Working title:** MuseDesk (replace before submission)  
**Document date:** November 10, 2026  
**Target hardware:** Meta Quest 3, with Quest 3S as secondary validation  
**Platform:** Unity 6 / Meta XR / OpenXR, Windows desktop companion, Python agent backend  
**Primary use case:** human supervision of multiple real AI tasks in a desk-scale mixed-reality workspace  
**Submission target:** Meta VR Start Developer Competition 2026, Productivity / Best Agentic Interaction  
**Deadline:** November 18, 2026, noon Pacific / 3 PM Eastern (confirm contest status before submission)  
**Status:** specification and engineering handoff; not implemented or validated.

> **One-sentence concept:** Three small, expressive AI coworkers occupy miniature stations on your real desk; they carry out actual work and bring you graspable documents whenever human judgment is necessary.

## 0. Executive decision and non-negotiable scope

Build **one fully functioning vertical slice**, not a general-purpose augmented-reality operating system. The user starts an authentication-feature task. A researcher evaluates a decision; a developer scaffolds a change; a tester independently prepares or runs tests. The researcher physically delivers a document to the user, the user asks for context, grabs and enlarges it, approves or rejects it, and the approval produces a real, visible change in backend workflow. The application ends with a real artifact summary and test result.

An unconnected Quest must remain usable by judges in **offline guided demo mode**, distinctly marked as a simulation. Local PC mode runs actual tools inside a sandbox and has real output artifacts. A hosted mode is optional if a reliable per-session sandbox is ready. Never call offline scripted output autonomous work.

### Decision record

| Question | Decision | Rationale |
|---|---|---|
| Unity or WebXR? | Unity 6 + Meta XR All-in-One | Native hand interaction, animated characters, passthrough, room geometry and submission APK |
| Where does agent work execute? | Windows companion; optional hosted worker | Quest is UI, not unrestricted code execution environment |
| How is work communicated? | Versioned JSON commands/events over HTTP + WebSockets | Deterministic integration and independently testable subsystems |
| Must headset be linked to PC? | Not for standalone judged demo; yes/optional for live PC mode | Judges must be able to launch the APK independently |
| How many agents? | Three visual roles; only execute independently where dependencies allow | Demonstrate concurrency without false activity claims |
| How do agents navigate? | Bounded desk-plane paths and animation waypoints | Fast and robust; full room-scale pathfinding is unnecessary |
| How do users approve? | Physical grab → expand → approve/reject, optional rubber stamp | Reliable hands-first completion; signature is cosmetic, not legal e-signing |
| Voice? | P1 enhancement; contextual text question fallback is P0 | Speech reliability should not jeopardize core interaction |
| Agent framework? | Small explicit state machine first; LangGraph only if helpful | Deadline rewards inspectability and low integration cost |
| First coding task? | Safe sample repository and fixed authentication decision | Repeatable demonstration and controlled permissions |

## 1. Product requirements document (PRD)

### 1.1 Audience and problem

**Initial persona:** developer coordinating several coding or research agents while seated at a desk. Current agent workflows spread status across terminals, logs, chat threads, and review tools. Users must continually poll progress, identify blocked tasks, and decide whether to approve changes. The product attempts to make the *state of autonomous work* legible spatially and to surface only decisions that actually need human attention.

**User promise:** delegate multiple subtasks, know which agent is working or blocked at a glance, inspect a decision in one or two hand interactions, ask why, and safely approve or reject with visible consequences.

**Out of scope:** substituting for a full IDE; coding inside the Quest; unrestricted shell access; systemwide 3D companions over arbitrary apps; legal digital signatures; enterprise account management; multiuser collaboration; complex long-term agent memory; full-room robot navigation.

### 1.2 User stories and acceptance criteria

| ID | Story | Priority | Acceptance condition |
|---|---|---|---|
| US-01 | As a seated user I can launch into passthrough with agents on a desk | P0 | Quest app launches, environment visible, agents appear after setup or manual placement |
| US-02 | I can understand the three agent roles | P0 | Name, role, working state recognizable without opening a menu |
| US-03 | I can begin a prepared task | P0 | One hands-first start control; receives session and task IDs |
| US-04 | I can see real progress | P0 | Animation/state updates tied to backend events, not timers |
| US-05 | A blocked agent approaches with a paper | P0 | One pending approval triggers approach; no duplicate papers |
| US-06 | I can pick up and expand the paper | P0 | Hand grab, reliable enlargement, detail legibility, reset or release |
| US-07 | I can approve, reject or request changes | P0 | Each option changes durable decision state once, with confirmation |
| US-08 | My choice changes downstream execution | P0 | Approve unblocks work; reject follows branch, all logged |
| US-09 | I can ask for explanation | P0 | Text-based contextual question UI; voice as P1 |
| US-10 | I can see resulting code/test artifact | P0 | Document includes real diff/test paths/summary in live mode |
| US-11 | It works without my Windows PC | P0 | Self-contained *labeled demo* on Quest with same UX and contracts |
| US-12 | I can recover from disconnect | P0 | Retry/resync restores sessions; no duplicate approvals |
| US-13 | I can use hands end to end | P0 | No Touch controller or keyboard required for core demo |
| US-14 | I can communicate by voice | P1 | Push-to-talk, transcript, contextual answer, visible failure fallback |
| US-15 | Characters exchange task objects | P2 | Physical handoff tied to a completed dependency |
| US-16 | I can assign tasks by placing cards on a station | P2 | Explicit owner reassignment and backend acknowledgment |

### 1.3 Performance, interaction, and quality requirements

- **Performance:** competition minimum 60 fps on Quest hardware; target stable 72Hz passthrough refresh if possible. Measure in an on-device release-like build, never assume Editor FPS applies.
- **Start time:** target under 60 seconds from app entry to first meaningful user action, excluding one-time room permission/setup.
- **Interaction geometry:** all mandatory controls within comfortable seated reach (~30–65 cm in front of the user), not requiring room-scale movement; offer reposition/scale.
- **Text:** paper summary must be readable at a glance; expanded card should favor large text, short blocks, and scrolling, not dense fake code.
- **Safety:** no irreversible filesystem changes without an explicit permission gate; use a sample repository in an isolated workspace.
- **Resilience:** no dependency on live LLM availability to complete the standalone demo; offline fallback is transparent.
- **Privacy:** never ship provider API keys in Unity assets/APK, never transmit spatial meshes or raw microphone data without consent.
- **Accessibility:** do not depend solely on speech or on a signature gesture; every action has a simple hand-accessible button alternative.
- **Observability:** log event identifiers, state changes, task IDs, tool calls, approval transitions, exception details; redact secrets and tokens.

## 2. The demonstration journey

1. The user puts on Quest 3, launches MuseDesk, grants requested spatial-data permission, and points to a desk or manually places a virtual workbench.
2. Three mascots and miniature role-specific stations appear. The researcher has a small stack of books; the developer uses a laptop; the tester has a test console.
3. The user presses **Start sample task: build authentication flow**. The UI states whether it is **LIVE PC MODE** or **OFFLINE DEMO MODE**.
4. Backend starts three nonconflicting jobs: research OAuth vs local-password tradeoffs; developer scaffolds a nonsecret UI stub in its isolated branch/worktree; tester prepares or runs a baseline test on another isolated branch/worktree. Do not portray blocked dependent steps as running.
5. Researcher reaches `awaiting_approval` and walks along a short deterministic waypoint path to the delivery point. A paper appears in the character's hands.
6. The user pinches the paper. On pickup it becomes an independently manipulated object. Pinch again to expand or select **Expand**; a detailed near-field panel shows summary, rationale, alternatives, risk, and affected work.
7. User asks: “Why not store passwords?” A contextual response is produced via text; push-to-talk speech can replace typing in P1.
8. User approves via prominent hand-first **Approve** button or optional stamp. The UI shows **Sending → Confirmed** only upon backend acknowledgment.
9. The approved decision unblocks the appropriate implementation step; the developer's station changes animation, a task handoff is rendered, and eventual real diff/test summary arrives.
10. A completion report clearly distinguishes passed/failed tests, affected files and whether results are live or scripted; user may reset demo or end session.

### Offline judged demo requirements

- Self-contained `IDemoAgentGateway` implements the *same* session/task/approval interface as live `IBackendGateway`.
- Simulated results are **clearly marked** and never claim to have actually edited the judge's computer.
- All hand tracking, spatial placement, grab, expand, approval and status visualization run normally offline; this proves the XR experience independent of third-party services.
- Sample demo assets and event timeline are included in the Unity build, not fetched from a private LAN host.
- If a reliable hosted sandbox can be deployed, offer **Cloud Live Mode** as optional; it must have quotas, isolated per-user repositories, short session expiry, and no private credentials.

## 3. High-level architecture

```text
                     ┌──────────────────────────────────────────┐
                     │ QUEST 3: Unity / Meta XR / OpenXR      │
                     │ Passthrough + room scene + hand input   │
                     │ 3D mascots + paper UI + optional voice │
                     │ QuestPresentationState + AgentGateway  │
                     └────────────────┬─────────────────────────┘
                                      │ JSON HTTP commands + WS events
                  ┌───────────────────┴─────────────────────┐
                  │                                         │
         LIVE / CONNECTED MODE                    OFFLINE JUDGE MODE
                  │                                         │
        ┌─────────▼────────────────┐              ┌─────────▼─────────────┐
        │ WINDOWS PC COMPANION     │              │ Unity demo gateway    │
        │ FastAPI + WebSocket      │              │ local event engine    │
        │ Pydantic + SQLite        │              │ scripted artifacts    │
        │ Approval / task scheduler│              │ clearly labeled       │
        └─────────┬────────────────┘              └───────────────────────┘
                  │
       ┌──────────┴──────────────────────────────┐
       │ Research worker / Developer worker      │
       │ Tester worker / optional speech adapter │
       │ Tool allowlist / approval gate           │
       └──────────┬──────────────────────────────┘
                  │
        isolated sample repo/worktrees
        code diffs / tests / logs / decisions
```

### 3.1 Unity client modules

- `AppBootstrap`: loads config, initializes XR, decides demo/live mode, enforces display of mode.
- `WorkspacePlacement`: waits for MRUK scene, selects table surface, validates clearance; manual fallback.
- `SpatialWorkstation`: anchor-relative station layout and reposition controls.
- `AgentRegistry`: one `AgentView` per logical agent ID; no hardcoded UI state independent of server.
- `AgentView` and `AgentAnimator`: maps backend task phases to animations and waypoints.
- `DocumentDeliveryController`: one approach/paper queue at a time, delivery handoff and return.
- `DocumentInteractionController`: grab/expand/rotate/reset, accessible approve/reject/change controls.
- `DecisionPanel`: condensed summary; detail view; risk; alternatives; supporting references.
- `TaskBoard`: shows queued/running/blocked/completed and the data source (demo/live).
- `ConversationPanel`: question input UI, optional voice transcript, answer display.
- `IAgentGateway`: client contract; implementations `LiveAgentGateway` and `DemoAgentGateway`.
- `SessionStore`: reconnection and snapshot reconciliation.
- `DiagnosticsOverlay`: endpoint, mode, network health, scene permission, last event ID, FPS.

### 3.2 Windows companion modules

- `api.py`: versioned FastAPI REST endpoints and WebSocket fanout.
- `models.py`: Pydantic request/response models matching `contracts/`.
- `storage.py`: SQLite task, decision, event, and artifact persistence.
- `orchestrator.py`: deterministic state machine with independent worker queues.
- `workers/researcher.py`: produces decision-ready research with source/reason metadata.
- `workers/developer.py`: modifies only explicitly approved sample working directory/worktree.
- `workers/tester.py`: executes a hardcoded allowlist of tests in a sandbox.
- `gate.py`: verifies tool permissions and pauses tasks on approval boundaries.
- `events.py`: event sequence, persistence, replay and broadcast.
- `speech.py`: optional STT/TTS provider adapters; no mandatory always-on service.
- `settings.py`: typed environment config, no secrets in source code.
- `demo_fixture.py`: test scripts mirroring the standalone client fixture.

### 3.3 State model

**Agent state:** `idle`, `researching`, `coding`, `testing`, `reporting`, `approaching`, `waiting`, `complete`, `error`. These are visual projections of backend task states. Animation-only transitory states (`approaching`) are client-owned but cannot make false execution claims.

**Task state:** `queued` → `running` → (`awaiting_approval` or `completed` or `failed`); `awaiting_approval` → (`running` on approve, `cancelled` on reject, `running` after request-changes revision if supported); `cancelled` terminal. `blocked` may be a derived state for a task waiting on another task. `completed`, `failed`, and `cancelled` terminal unless a new task ID is explicitly created.

**Decision state:** `requested` → `presented` → `approved` / `rejected` / `changes_requested` / `expired`. A decision must have exactly one terminal resolution. The server is the source of truth.

**Interrupt priority:** `routine` (no human needed), `inbox` (nonurgent), `blocking` (delivery), `critical` (explicit confirm, never default approve). A demo should include one `blocking` document and optionally a nonurgent completion report.

## 4. Backend API and event contracts

The normative, examples, and validation rules live in `docs/API_AND_EVENT_CONTRACT.md` and `contracts/*.schema.json`.

**REST:** `GET /health`, `POST /v1/sessions`, `GET /v1/sessions/{id}/snapshot`, `POST /v1/sessions/{id}/tasks`, `GET /v1/sessions/{id}/tasks`, `POST /v1/sessions/{id}/decisions/{decision_id}/respond`, `POST /v1/sessions/{id}/questions`, `GET /v1/sessions/{id}/artifacts/{artifact_id}`.

**Live updates:** `/v1/sessions/{id}/events` via WebSocket; replay from last known monotonically increasing `seq`. Define a versioned event envelope with `schema_version`, `event_id`, `seq`, `session_id`, `occurred_at`, `type`, `task_id`, `agent_id`, optional `decision_id`, and `payload`.

**Core event types:** `session.ready`, `agent.status_changed`, `task.created`, `task.started`, `task.progress`, `decision.requested`, `decision.resolved`, `task.completed`, `task.failed`, `artifact.created`, `session.reset`, `system.warning`.

**Command safety:** a decision response always has `decision_id`, `action` enum, and a unique `idempotency_key`. Replaying a response must return the prior result, not execute action twice. Reject unknown or expired decisions. Auth required in live modes.

**Failure handling:** persistent write before WS broadcast; initial snapshot after connect; replay events after disconnect; heartbeat and exponential backoff; visual errors remain accessible via a task board even if animations are missed.

## 5. Data security and execution policy

- All live coding work is restricted to a **user-selected sample repository**, preferably an isolated copy under a generated temp workspace. Never grant access to the whole home directory.
- Tool allowlist: list files, read files within workspace, write files within workspace, limited `git diff/status`, run the project's pinned test command. Deny arbitrary shell by default.
- Dangerous operations (`rm -rf`, repository deletion, network publishing, deployment, payment, credential change) are not enabled in MVP. The user interface cannot override backend policy.
- Research may read public online information only if a configured source adapter is available; source provenance is displayed, and missing provenance should be labeled.
- Secrets remain on the PC or secured cloud runtime. The APK holds only nonsecret configuration, and any session token is short-lived.
- Local dev transport: prefer USB + `adb reverse` for localhost testing. For wireless testing, bind only to trusted LAN, permit a firewall rule restricted to Private network/subnet, and use pairing authentication. Never port-forward unrestricted local backend to the internet.
- Hosted mode: TLS/WSS, per-session bearer auth, rate limits, resource quotas, isolated processes/containers, cleanup TTL and safe logging.
- Ask runtime permission for scene/spatial data only when using scene understanding, and microphone only when using voice. Respect denial and supply fallback UX.
- No deceptive behavior: **OFFLINE DEMO / scripted** is visible whenever live agent execution is unavailable.

## 6. Quest XR scene and interactions

### 6.1 Scene hierarchy (target design)

```text
MainMRScene
 ├── XR Origin / comprehensive interaction rig [ONE ONLY]
 ├── Passthrough
 ├── MRUK scene manager + permission flow
 ├── WorkspaceRoot [positioned relative to desk anchor]
 │    ├── ResearchStation + ResearcherAgent
 │    ├── DevStation + DeveloperAgent
 │    ├── QAStation + TesterAgent
 │    ├── DeliveryZone
 │    ├── Inbox / TaskBoard
 │    └── DocumentAnchor
 ├── UI (world-space canvases)
 │    ├── StartPanel / ModeBadge
 │    ├── ExpandedDocument
 │    ├── ConversationPanel
 │    └── DiagnosticsOverlay (debug only)
 └── AppServices
      ├── GatewayRouter
      ├── EventDispatcher
      ├── AnimationCoordinator
      ├── AudioController
      └── SessionState
```

### 6.2 Surface placement

On start, wait for MRUK scene load. Find flat horizontal scene anchors representing a table or other safe play surface; use user-targeted selection if more than one. Reserve a roughly 70–100 cm-wide miniature station arrangement when physical space permits; scale down to smaller surfaces. If spatial data is denied, missing, or scene scan is incomplete, show a hand-dragged virtual tray anchored in front of user. Never spawn a decision outside reachable area. Keep paper delivery at comfortable near-hand height and no objects below the physical table.

### 6.3 Workstation composition

Researcher station: book pile, scrolling notebook, small lamp. Developer station: laptop with abstract progress screen (not remote monitor streaming), keyboard, tiny compile lights. Tester station: clipboard, test matrix LEDs, mini console. All share a single low-poly, cohesive art style and a stable root transform relative to WorkspaceRoot. Workstation props should NOT obstruct the grab interactors.

### 6.4 Document state/UX

`queued_for_delivery` → `offered` → `held` → `expanded` → `responding` → `resolved` / `error`. A paper is not interactive until offered. Offer one decision paper at a time. A user may dismiss it to the inbox without approving, reopen and then respond. The summary should be at most 120 characters, with a single recommended choice and a clear “more details” affordance. Details: context, alternatives, impact, risks, references/diff. `Approve`, `Reject`, and `Ask for Changes` actions remain visible. Stamp/signature is optional celebration, not the only path to a response. After submission, lock the paper until server confirmation; on error restore controls, show retry option, preserve idempotency key.

### 6.5 Locomotion and animation

Use 2–3 preauthored waypoint splines on the miniature desk, e.g., station → central walkway → delivery zone. Time of movement 1–3 seconds, with turn/look-at and prop-holding animation; do not pretend the agent can climb furniture. Renderer uses state-machine animation: idle, thinking, working, carrying, offering, celebrating, returning, blocked. Arrival triggers actual document offer; leaving ends delivery. No collisions with physical table edge or user's hands; ensure reachable destination even if desk geometry is noisy.

## 7. Live agent workflow

**Sample repo:** generate a disposable Python/FastAPI or web-demo application with explicit small TODOs, tests, and fixtures. Avoid modifying the actual user's main project in MVP.

**Parallelism** (true where possible): researcher independently evaluates authorization mechanisms; developer prepares UI scaffolding that does not depend on selected auth provider; tester runs baseline/unit tests for current sample utilities. After research decision approved, orchestrator schedules provider-specific implementation. Any dependency-inhibited task shows `blocked` instead of a fake animated work loop.

**Approval gate:** before executing an action tied to the unresolved auth decision, pause and emit `decision.requested`; persist alternatives, risk, recommendation and relevant artifact references. Only a server-validated `approve` resumes the branch. `reject` cancels or enters alternate proposal; `request_changes` creates a revised decision with parent reference. Provide a clear rejection path in the demo.

**Artifacts:** every completion emits a human-readable summary, affected file list, diff/snippet (capped, sanitized), test command, test counts/results, elapsed duration, and data-origin flag `live` or `demo`.

## 8. Detailed delivery milestones

| Date (2026) | Milestone | Gate / verification | Owner |
|---|---|---|---|
| Nov 10 | Toolchain + Quest connected | `adb devices` = `device`; blank Quest APK runs with hand tracking | XR lead |
| Nov 11 | Passthrough + workbench + gateway | Scene visible, manual/table placement, backend event changes object | XR + backend |
| Nov 12 | Documents + interaction | Grab, expand, approve/reject work 9/10 trials on Quest | Interaction lead |
| Nov 13 | Stateful approvals | API persistence; response unblocks task; duplicate response safe | Backend lead |
| Nov 14 | Three agents + independent jobs | Three distinct states; at least two nonconflicting active jobs | Backend + character |
| Nov 15 | Live artifact result + standalone demo | Disconnected judged APK still runs full labeled UX | Integration lead |
| Nov 16 | Voice if stable; polish otherwise | Speech fallback, task board, error paths; no broken P0 | Feature owners |
| Nov 17 | Device and reviewer QA | 3 successful runs, adequate fps, clean install, video recorded | All |
| Nov 18 | Competition submission | Competition release uploaded and accessible; public <3min video; Devpost form complete before deadline | Submission owner |

**Stop rules:** If no headset build by Nov 11, stop character art and fix setup. If no working approval by Nov 13, stop voice and inter-agent animation. If no standalone demo by Nov 15, stop live-provider work until judge mode passes. Freeze feature development Nov 17; repair bugs and submission only.

## 9. Agent assignment model and integration gates

Split work into bounded, file-owned tickets; each has inputs, outputs, acceptance conditions, and an integration branch. **Agent instructions live in `tasks/` and `AGENTS.md`.** Suggested allocation:

- **Track A: XR foundation (TASK-01).** Own Unity project configuration, scene/placement, `AppBootstrap` and rig; cannot modify event models.
- **Track B: Documents and UX (TASK-02).** Own grab, paper controller, decision panel, accessible controls; use fake `IAgentGateway` until integration.
- **Track C: Character/world design (TASK-03).** Own low-poly agents, stations, animation projection, path logic; no backend changes.
- **Track D: API/contracts (TASK-04).** Own schemas, FastAPI, database, WS transport, integration tests; no Unity assets.
- **Track E: Orchestrator/workers (TASK-05).** Own task/decision machine, sandbox, fixture, real artifact; depend on frozen API contracts.
- **Track F: Voice (TASK-06, optional).** Own short push-to-talk pipeline, text fallback and captions.
- **Track G: Integration & QA (TASK-07).** Own client/backend adapter, reconnection and built APK test.
- **Track H: Competition delivery (TASK-08).** Own release channel, demo video, eligibility and final checks.

**Dependency ordering:** freeze `contracts/` and interface signatures first; XR/Backend/Character run in parallel behind mocks. Integrate a single event at Nov 11, full decision transaction at Nov 13, artifacts at Nov 15. Do not let agents independently invent new JSON field names or Unity rig conventions.

**Review protocol:** open one PR per ticket; include changed files, screenshots/video or tests, before/after, limitations and a command to verify. Merge only after one person checks the acceptance tests. Unity scene/prefab edits have a single owner at a time; keep `.meta` files and `Packages/packages-lock.json` in version control.

## 10. Testing plan

### Contract/backend tests

- Session creation returns unique session ID and correct mode.
- Multiple task events maintain strictly increasing `seq` per session.
- Exactly one decision is created and the correct agent delivers it.
- Approve transitions task once; resubmitted identical idempotency key does not repeat work.
- Conflicting second decision response returns explicit `409 Conflict` or canonical prior outcome.
- Reject does not accidentally execute approved work.
- `request_changes` produces revised state, not an uncontrolled tool invocation.
- Reconnect snapshot and replay represent exactly one current state.
- Timeout/cancel/delete results are stable; secrets never appear in events or diffs.
- Sandboxed task cannot read or modify files outside configured root.

### On-headset tests

- `Launch → see desk → start task → grab paper → expand → approve → completion` with hands only.
- Repeat with reject path and question path.
- Test different lighting/desk heights, occlusion, edge of view, missing spatial permission, missing microphone permission, offline/disconnected backend, and nonresponsive backend.
- Watch for accidental grab, unintended approval, text too small, document clipped behind desk, animations outside view, double-character spawn, resets after app backgrounding.
- Measure on-device FPS and memory using MVDH performance tooling; acceptance threshold ≥60 fps per competition. Aim for stable 72Hz passthrough where feasible.
- Use a clean APK install and a second headset/user if available.

### Demonstration repeatability

Run 3 uninterrupted full paths, then cold start, then simulated disconnect, then offline judged mode. Record exact software versions, build hash, test device and any known limitations. Capture video from Quest hardware and avoid calling mocked work live.

## 11. Submission requirements and operational caveat

The contest requires a **new `Competition` release channel** in the Meta VR Developer Dashboard for Unity APK submissions, a publicly viewable YouTube/Vimeo demo under three minutes, project description, track/division selection, and a working build. Verify Start program membership and eligibility for every team member and publish by Nov 18 at noon Pacific. Do not assume a local-only backend will be reachable by judges; offline mode must work without PC. Detailed launch checklist lives in `docs/DEPLOYMENT_AND_SUBMISSION.md`.

## 12. Risk register

| Risk | Probability | Severity | Mitigation |
|---|---|---|---|
| Setup/version conflicts across XR SDK packages | Medium | High | Official MVDH template; pin editor/package lock; one XR rig; project validation |
| Hand grabbing unreliable | Medium | High | First Hand/Interaction sample first; large target, visible grab affordance, fallback button |
| MRUK desk not found | High | Medium | Manual tray placement and permission-denied fallback |
| Real workflow blocked by LLM/service outages | Medium | High | Immutable offline demo; recorded fixture; retries/timeouts |
| Judges cannot connect to personal PC | Certain | Critical | Offline judged mode or reliable hosted worker |
| Text unreadable in MR | Medium | High | Short paper summary, larger expanded panel, tested font sizing |
| Parallel agents conflict on repository | High without isolation | High | Separate worktrees; tool allowlist and dependency graph |
| Unexpected tool commands | Medium | Critical | Sandboxing, server-side approval gates, fixed test allowlist |
| Large Unity binary conflicts with many AI agents | High | Medium | Single scene/prefab ownership; `.meta` and lock files committed |
| Speech permission/latency failure | Medium | Medium | P1 only; text question and caption fallback |
| Deadline missed by excessive features | High | Critical | Strict gates, Nov 17 freeze, dedicated submission owner |

## 13. Definition of Done

Project is ready only when:

1. A novice can follow `docs/QUEST3_AND_WINDOWS_SETUP.md` and install an APK on Quest 3.
2. A Quest-only clean launch can complete the full **clearly labeled offline demo** without PC, controllers, or hidden passwords.
3. Connected mode performs at least one **real** approved/rejected task branch using a sandboxed sample repository, with actual diff/test artifact.
4. All state on the physical objects is consistent with backend state and survives temporary disconnection.
5. The app respects permission denial, task failure and safe stop conditions.
6. On-device testing confirms the performance, comfort, and reliability gates.
7. Complete submission, accessible release, video, and description meet the competition rules.

## 14. Documentation map

- `README.md`: orientation, first action, folder index.
- `AGENTS.md`: mandatory rules for coding agents and merge workflow.
- `docs/QUEST3_AND_WINDOWS_SETUP.md`: all machine/headset setup steps and troubleshooting.
- `docs/ARCHITECTURE_AND_XR_DESIGN.md`: exact modules, ownership, scene hierarchy and state projection.
- `docs/API_AND_EVENT_CONTRACT.md`: schemas, endpoints, event examples and semantics.
- `docs/UX_AND_INTERACTIONS.md`: detailed interaction design, copy, states, accessibility.
- `docs/SECURITY_TESTING_AND_EVALUATION.md`: tools sandbox, privacy, integration/acceptance tests.
- `docs/DEPLOYMENT_AND_SUBMISSION.md`: reviewer-ready build and publishing.
- `tasks/TASK-01` … `TASK-08`: agent work packages with acceptance and dependencies.
- `contracts/*.schema.json`: language-neutral event/task/decision structural schemas.
- `.env.example`: configuration variable template (no production secrets).

## 15. Verified references (retrieved November 10, 2026)

1. Meta Unity software setup — https://developers.meta.com/vr/documentation/unity/unity-software-setup/
2. Meta Unity project setup — https://developers.meta.com/vr/documentation/unity/unity-project-setup/
3. Meta manual OpenXR project setup — https://developers.meta.com/vr/documentation/unity/unity-tutorial-hello-world-manual-steps/
4. Meta interaction setup — https://developers.meta.com/vr/documentation/unity/unity-handtracking-interactions/
5. Meta hands setup — https://developers.meta.com/vr/documentation/unity/unity-handtracking-hands-setup/
6. Meta MRUK scene management — https://developers.meta.com/vr/documentation/unity/unity-mr-utility-kit-manage-scene-data/
7. Meta spatial data permission — https://developers.meta.com/vr/documentation/unity/unity-spatial-data-perm/
8. Meta developer tools/AI — https://developers.meta.com/vr/essentials/metavr-install/
9. Meta simulator setup — https://developers.meta.com/vr/documentation/unity/unity-simulate-xrsim/
10. Meta VR Start competition 2026 official rules — https://start-developer-competition-26.devpost.com/rules
11. Meta VR Start special awards — https://start-developer-competition-26.devpost.com/details/special-awards
12. Meta VR Start FAQ — https://start-developer-competition-26.devpost.com/details/faqs

**Version note:** Meta SDK menus and package versions change. Freeze the official toolchain version after first successful on-device build and let the package lock file—not prose—define the team's exact dependency versions.
