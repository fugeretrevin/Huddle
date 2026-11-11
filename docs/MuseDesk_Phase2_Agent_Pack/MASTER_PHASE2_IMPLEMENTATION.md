# MuseDesk Phase 2 — Living Agent Workspace

**Complete feature specification, implementation plan, and human/agent handoff**  
**Version:** 1.0 — November 11, 2026  
**Status:** IMPLEMENTATION SPECIFICATION ONLY; not validated code  
**Parent:** MuseDesk Phase 1 Implementation Pack (AGENTS.md, MASTER_IMPLEMENTATION_PLAN.md, v1 event/schema contracts)  
**Target:** Quest 3 / Unity 6, Meta XR, Windows FastAPI local companion and standalone scripted demo

## 0. Read this first: the goal and scope

**Vision.** Make autonomous software work tangible: users physically delegate tasks to miniature coworkers, see authentic handoffs, inspect evidence-rich artifacts, and manage an animated software factory without sacrificing productivity.

**Scope.** This is an ADD-ON for the current MuseDesk implementation. Do not rebuild the XR rig, replace the Phase 1 approval UI, restructure the backend, or add unverified packages just to implement an extension. Preserve baseline app behavior, data provenance, offline-demo parity, per-session event sequence and backend authority.

**Seven feature tracks:**

1. **Physical task orchestration:** a tactile task board; move cards onto workstations to assign real tasks.
2. **Rich artifact inspection:** inspect actual diffs, test outputs and research evidence inside grab-and-expand papers.
3. **Agent collaboration:** visualize genuine typed coordination and task dependency handoffs between agents.
4. **Spatial explanations:** turn verified structured graphs into small, interactive spatial diagrams.
5. **Room-adaptive workstations:** place and persist user-relative station layouts with graceful scene fallback.
6. **Focus and interruption management:** route routine updates to an inbox; interrupt only when necessary.
7. **Miniature software factory:** a single expressive visual pipeline for research → coding → tests → output.

**Cross-cutting rule:** every observable outcome corresponds to a canonical state/event in the server or a conspicuously marked scripted demo fixture. Animations are *projections*, never execution engines.

### 0.1 Integration preconditions (do not start Phase 2 prematurely)

- [ ] Quest build launches directly in hands-only passthrough mode and places the existing desk workspace.
- [ ] Phase 1 `LiveAgentGateway` and `DemoAgentGateway` both complete one task → decision → approval → result cycle.
- [ ] Reconnect restores correct snapshot; duplicate approval command does not duplicate execution.
- [ ] Existing Researcher, Developer, and Tester views respond to real backend state changes.
- [ ] Versioned v1 contract is committed and tests are green; parent repo is tagged `phase1-stable`.
- [ ] No Unity compilation errors, one XR rig, actual Quest hand grabbing reliable, baseline frame time measured.

If any precondition fails, fix Phase 1 before proceeding. Phase 2 feature flags default OFF; turn on one feature at a time in live and demo modes.

### 0.2 Prioritization and timing

| Track | Initial development slice | Standalone demo | Practical value | Risk | Gate |
|---|---|---|---|---|---|
| P2-01 Board/task drop | 1–2 focused days | Required | Very high | Medium | First |
| P2-02 Rich artifacts | 1–2 days | Required | Very high | Medium | Second |
| P2-03 Real handoffs | 1–2 days | Required | High | Medium | Third |
| P2-07 Software factory | 1–2 days with prior assets | Required | Medium–high | Medium | Fourth |
| P2-06 Focus/inbox | 0.5–1.5 days | Required | Very high | Medium | Fifth |
| P2-05 Room adaptivity | 1–3 days | Fallback required | Medium | High | Sixth |
| P2-04 3D explanations | 2–4 days | Fixed sample | High | High | Last |

*Estimates are directional for a functioning team; not promises. Shared dependencies, asset work, device QA and regression can substantially increase elapsed time. For the current hackathon, deliver one vertical extension (board → handoff → artifact), then add factory polish if stability allows.*

### 0.3 Success criteria

- **Task honesty:** the displayed assignee, status, priority and artifacts are consistent with the authoritative task graph after reconnect.
- **Tactile clarity:** a user can pick up a card, move it, drop it on a desk, see `Sending...`, then see `Assigned` only after server acknowledgment.
- **Human control:** unapproved or high-risk tool actions cannot execute due to a purely local hand gesture.
- **Reviewability:** real code changes include at least a file summary and a paged/sanitized diff; tests display actual counts and failures.
- **Dependency integrity:** a developer-to-tester handoff cannot begin before prerequisite outputs exist.
- **Focus integrity:** nonurgent document deliveries queue, urgent decisions remain visible through safe alternative indicators.
- **Environmental robustness:** an unrecognized room still offers stable, seated, hand-accessible manual placement.
- **Presentation:** the complete scenario runs hands-only in offline demo with an always-visible `SCRIPTED DEMO` distinction, and in live mode with real sandboxed outputs.

## 1. UX story: one end-to-end scenario

1. User opens MuseDesk, selects LIVE LOCAL (or SCRIPTED DEMO), sees three characters on the desk and the task board.
2. User grabs the `Add OAuth login` card from the backlog, drops it onto Researcher's workstation. A confirmation cue shows pending, then the Researcher starts examining notes only after the backend acknowledges assignment.
3. Researcher generates a recommendation artifact and a blocking decision. The paper arrives through the existing Phase 1 delivery system; user expands it, asks a question and approves it.
4. Backend releases the Developer task, emits a real typed handoff with an artifact reference; Researcher walks to Developer and passes a blueprint. Developer begins implementation; the board updates.
5. Developer finishes and creates a diff; user inspects a paged diff document, optionally comments, and authorizes a permitted next action only through the existing decision gate.
6. Tester receives a `test_request` handoff, performs tests in the sandbox and emits a result (e.g. 7 passed, 1 failed). The miniature test machine shows a red warning; the actual failed test appears in the document.
7. Tester sends a failure handoff to Developer. After the fix, a new test attempt succeeds; the factory shelf gains a verified build artifact.
8. User activates focus mode; routine outputs accumulate in a visible inbox while blocking decisions remain distinguishable, accessible, and subject to interruption policy.
9. User asks `show me how the login flow works`; the agent creates a typed graph, which Quest renders as a small explorable diagram. The same scene can run offline, explicitly marked scripted.

**MVP cut:** omit voice and free-form 3D generation if necessary; preserve task assignment → real handoff → rich artifact → factory demonstration.

## 2. System architecture and ownership

```text
Quest: Unity presentation
  WorkspaceRoot (existing)           EventReducer (existing)
  |-- BoardPresenter / CardInteractor / DropZone
  |-- ArtifactDocumentPresenter / DiffPager / SelectionRef
  |-- HandoffAnimator / FactoryLinePresenter
  |-- FocusInboxPresenter / FocusModeControls
  |-- RoomLayoutStore / WorkspacePlacementAdapter
  |-- DiagramPresenter (typed templates)
                  | authoritative v2 snapshot/events + commands
                  v
Windows companion: FastAPI / scheduler / storage
  |-- TaskGraphService (DAG, assignment, priority, pause checkpoints)
  |-- HandoffService (typed messages, artifact refs)
  |-- ArtifactService (sanitized summary/diff/test/research pages)
  |-- FocusPolicyService (queue + severity decisions)
  |-- ExplanationService (validated diagram JSON; no free-form scene commands)
  |-- EventStore (persistent seq, replay, idempotency)
  |-- Existing Phase 1 approval gate and allowlisted sandbox tools
                  |
                  v
      Isolated sample repository / worktrees / pinned tests

Offline Quest: DemoGatewayV2 -> same view models, scripted fixture, labeled demo.
```

**Unity boundaries.** New presenters depend on `IWorkspaceSnapshotProvider`, `ITaskCommandGateway`, `IArtifactGateway`, `IVisualEventSink`, and `ILocalLayoutStore`. Keep existing `IAgentGateway` (Phase 1) in place; use adapters to avoid forcing all Phase 1 code to migrate at once. All game-object mutations happen on Unity's main thread; network callbacks enqueue reducer actions for the main thread. A single central `InteractionCoordinator` prevents grabbing and reparenting the same object simultaneously.

**Backend boundaries.** Each command is validated, authorized, applied in one transaction, and recorded in the session event store before broadcast. Task scheduler changes only after transaction commit; external tool actions use an outbox/worker queue to prevent double-starting after crash. Persist task revision and event sequence. No state is inferred from mascot animation position.

### 2.1 Repo/module structure (additive suggested paths)

```text
quest/Assets/MuseDesk/Phase2/
  Runtime/Board/{TaskCardView,BoardPresenter,StationDropZone,TaskDragController}.cs
  Runtime/Artifacts/{ArtifactDocumentPresenter,DiffPager,SelectionAnchor}.cs
  Runtime/Handoffs/{HandoffPresenter,FactoryVisualStateMachine}.cs
  Runtime/Focus/{FocusInboxPresenter,NotificationPolicyView}.cs
  Runtime/Room/{RoomLayoutStore,StationLayoutController}.cs
  Runtime/Diagrams/{DiagramPresenter,DiagramNodeView,DiagramEdgeView}.cs
  Runtime/Contracts/{Phase2Models,Phase2GatewayAdapter,Phase2Reducer}.cs
  Editor/{Phase2PrefabBuilder,Phase2ValidationMenu}.cs
  Prefabs/{TaskCard,TaskBoard,StationDropZone,FactoryConveyor,ArtifactPanel,DiagramNode}.prefab
  Fixtures/phase2_demo.json
backend/app/phase2/
  {models,routes,task_graph,handoffs,artifacts,focus,explanations,events}.py
backend/tests/phase2/
  {test_graph,test_reassign,test_focus,test_handoffs,test_artifacts,test_idempotency}.py
contracts/phase2/{snapshot_v2,event_v2,command_v2,artifact_v2,diagram_v2}.schema.json
```

Names are proposed; code agents must inspect current repo before creating modules. Never create a second XR rig, duplicate backend server, or alter Phase 1 scene without an integration review.

## 3. Contract strategy: keep Phase 1 functioning

The original pack defines strict `schema_version:1` events and enumerated event types, e.g. `task.progress`, `decision.requested`, `artifact.created`. **Do not silently add new event types to v1 schema.** Implement a versioned Phase 2 subscription and snapshot; preserve all v1 routes, fields, and behavior.

- Maintain `/v1/sessions`, `/v1/sessions/{sid}/snapshot`, and `/v1/sessions/{sid}/events` exactly as existing clients expect.
- Add `GET /v2/sessions/{sid}/snapshot` and `WS /v2/sessions/{sid}/events?after_seq=N`. V2 server can read same event store but projects events into v2 envelopes; v1 viewers receive only supported v1 events.
- Add `/v2/sessions/{sid}/tasks/{tid}/assign`, `/priority`, `/pause`, `/resume`, `/focus`, `/artifacts/...`, `/diagrams/...` as Phase 2 endpoints.
- Keep `decision.respond` on v1 until a deliberate migration. V2 clients call existing approval endpoint, not a new approximation.
- `source_mode` appears on all visible artifacts, tasks and collab visual messages; `live` is never inferred from connection alone.
- Authentication and authorization remain server-side; v2 uses the same per-session auth mechanism as v1. Never expose LLM credentials or workspace paths to Quest.
- The Phase 2 v2 WS supports durable monotonic `seq`, snapshot rehydration, and deduplication; use canonical event IDs across versions and do not emit the same logical event twice.

### 3.1 Phase 2 canonical entities

| Entity | Required fields | Notes |
|---|---|---|
| TaskCard | `task_id, title, task_state, agent_id, priority, revision, dependencies[], source_mode` | `agent_id` nullable when unassigned; client shows canonical task state |
| Handoff | `handoff_id, from_agent_id, to_agent_id, task_id, artifact_id, kind, state, source_mode` | valid kinds: `blueprint`, `implementation`, `test_request`, `test_failure`, `review` |
| Artifact | `artifact_id, task_id, kind, summary, source_mode, content_pages, created_at` | `diff`, `test_report`, `research`, `diagram`, `completion` |
| FocusPolicy | `mode, until, quiet_priorities[], updated_at, revision` | modes `normal`, `focus`, `do_not_disturb`; critical rules enforced on server |
| Diagram | `diagram_id, template, nodes[], edges[], artifact_refs[], source_mode` | strictly typed validated layout, not raw Unity instructions |
| StationLayout | `schema_version, local_anchor_ref, layout_revision, station_poses[], scale` | local Quest storage; avoid uploading room geometry |
| VisualCue | `cue_id, cue_kind, canonical_ref, status` | nonauthoritative, idempotent, can be skipped safely |

Additional normalized data fields must use JSON Schema definitions in `contracts/` and matching Python/C# DTOs; code generation may produce DTO scaffolds but agents must compile/test them.

### 3.2 Command semantics

- **Assign:** `POST /v2/sessions/{sid}/tasks/{tid}/assign` with `target_agent_id`, `expected_revision`, `idempotency_key`. Allowed only if compatible role/permissions and queued/paused at safe checkpoint. Reject with `409` if stale revision, completed, unsafe transfer, or live tool still executing. Return canonical `task` and emit `task.assigned`.
- **Prioritize:** `POST .../tasks/{tid}/priority` with integer `priority` in range 0–3, `expected_revision`, `idempotency_key`. Scheduler reorders *queued* work; never preempts running subprocess automatically.
- **Pause/resume:** `POST .../tasks/{tid}/pause` or `/resume`. `pause_requested` must not be displayed as paused until a cooperative checkpoint is reached; cancel requires separate explicit policy. Do not kill unsandboxed processes.
- **Focus:** `POST /v2/sessions/{sid}/focus` with `mode`, optional `until`, `idempotency_key`. Service decides which notifications to enqueue vs present, never suppresses critical authorization information entirely.
- **Artifacts:** `GET /v2/sessions/{sid}/artifacts/{aid}?page=0&page_size=...`, sanitized/chunked. Selection queries use stable `line_ref` or `evidence_id`, not arbitrary unsanitized code execution.
- **Explain:** `POST /v2/sessions/{sid}/diagrams` with `artifact_id` or `task_id`, supported `template`, `idempotency_key`. Backend returns schema-validated graph within complexity limits; malformed outputs fail into explanatory text panel.
- **Collaboration:** agent-internal `HandoffService.create(...)` validates both agents, dependencies, artifact existence and permissions; Quest does not mint arbitrary authoritative handoffs.

Error response: `{ "code": "REVISION_CONFLICT", "message": "...", "canonical_task": { ... } }`; client reconciles snapshot, clears local pending state and retains readable error. All commands are idempotent by session, route, key and body hash.

### 3.3 Events and reducer

V2 event envelope: `schema_version:2`, `event_id`, `seq`, `session_id`, `occurred_at`, `type`, optional `task_id/agent_id/artifact_id/handoff_id`, `payload`. New types: `task.assigned`, `task.priority_changed`, `task.pause_requested`, `task.paused`, `task.resumed`, `handoff.created`, `handoff.received`, `handoff.failed`, `artifact.page_ready`, `focus.changed`, `inbox.updated`, `diagram.created`, `station.milestone`. Include mappings for every original v1 event in v2 projection.

Reducer order: load snapshot with `last_seq`, then subscribe `after_seq=last_seq`; only apply events with `seq > lastAppliedSeq`; if gap detected fetch snapshot and restart; persist no speculative state. Duplicated `handoff.created` must not cause duplicate physical courier animation.

## 4. Track P2-01 — Physical task orchestration

### Requirements

- Board with `Backlog`, `Working`, `Needs me`, `Completed` regions. Status columns are visual projections; station drop zones assign an eligible task to a specific agent.
- Task card short title (<=48 chars recommended), status marker, role icon, compact priority and live/demo source label; card details expand on pinch/inspect.
- Hand near grab and distance grab fallback. Drag floats slightly above board, follows physical hand with damped movement; visible potential-target highlight while hovering over desk.
- Release over a valid target shows pending state, sends one idempotent assign command, and snaps to canonical destination **only after** success. Drop over invalid target returns card to origin without a server call.
- Reassignment permitted only for unstarted or safely paused tasks; blocked tasks display why not eligible; running work cannot be teleported to another agent.
- Task dependency arrows and `blocked by ...` text. Dragging an active task in the board does not change actual dependency or create a false scheduler action.
- Accessible no-drag alternative: open card and select `Assign to Researcher/Developer/Tester` using poke controls (hands only).

### Unity implementation

1. Create `TaskBoardPresenter` that subscribes to `Phase2TaskStore.OnChanged`, recycles card views by task ID and uses object pooling.
2. Create `TaskCardView` with stable ID, data revision, focused state and a `Grabbable`/Interaction SDK-compatible component wired through installed SDK Quick Actions.
3. `StationDropZone` uses collider trigger volumes with type compatibility and a visible target halo; implement a deterministic nearest-valid-zone resolver.
4. `TaskDragController`: states `Idle → Held → Candidate → Pending → Confirmed/Error → Idle`. Network command begins only on release; cancel on tracking lost/release outside zone or app background.
5. On `409` stale revision, animate back, refresh canonical snapshot, show one-line reason; never synthesize success before response.
6. Build station labels and board contents with local-world text facing user and comfortable pinch targets; use current SDK package's tested prefabs rather than hardcoded obsolete Meta APIs.

### Backend implementation

Task graph service validates task state, assigned role and dependency readiness; commits reassignment and event atomically. Scheduler uses dependency graph, locks or version checks for concurrent assignments, and cooperative pause checkpoints. Include a task intent queue so jobs don't double-start after retries.

### Acceptance tests

- Assign queued Researcher task via hand drop: one server command, exactly one state transition, correct badge, survives reconnect.
- Drop developer-only task onto Tester: rejected locally and server-side; no execution changes.
- Drop outside valid desk: card returns, no request issued.
- Assign with stale `expected_revision`: error + canonical reconciliation, no duplicate job.
- Loss of hand tracking while holding card: safe cancel, no phantom assignment.
- Offline scripted mode executes visually identical assignment sequence with visible `DEMO` tag.

## 5. Track P2-02 — Rich inspectable artifacts

### Requirements

Artifacts: `code_diff`, `test_report`, `research_report`, `implementation_plan`, `completion`. Existing approval document remains the interaction entry point; an optional `Open Artifact` button reveals the detailed panel. Header always shows artifact type, task, author agent, source mode, timestamp and freshness.

- Collapsed paper: <= 2 lines summary, one risk/outcome icon, no dense code.
- Expanded document: comfortable readable viewport, pagination, file tree, search optional, code diff syntax coloring optional; show exact change counts if available.
- Diff view: file path sanitized and relative to sandbox root, hunks with stable `file_id`, `hunk_id`, `line_ref`; show additions/removals, truncation banner, no leaked credentials.
- Test view: test runner, duration, passed/failed/skipped counts, names and error summary, link to full sanitized stack excerpt.
- Research view: recommendation, alternatives, confidence/uncertainty text, cited evidence references; unknown sources flagged.
- User can ask contextual question while artifact open, passing `artifact_id` and selected line/evidence reference. Backend response quotes only allowed provided evidence and admits unavailable context.
- Existing signed approval process must remain separate from artifact browsing: looking at or closing document does not authorize actions.

### Implementation

Backend transforms raw diff/test output into normalized artifact records, caps response sizes, strips ANSI controls, resolves path privacy, redacts sensitive patterns, and paginates at stable page boundaries. Hash content to detect mutation and show `stale` if source revision changes; store artifact metadata by session and workspace scope.

Unity `ArtifactDocumentPresenter` chooses renderer by kind. Reuse Phase 1 grab/expand controller; `DiffPager` renders virtualized hunks (not every line into a giant Canvas), uses line selection IDs; network fetch occurs on expand/page scroll with cancellable request and cache. Unsupported formatting falls back to sanitized monospaced text.

### Acceptance tests

- Actual sample repo diff contains changed file names and correct added/deleted lines; no absolute host paths.
- Test artifact distinguishes 0 tests run from passing; failed assertion visible.
- Large diff is paginated without frame hitch; corrupt/invalid artifact renders friendly error.
- Selected-line follow-up asks backend with stable reference; reopening artifact shows same hash/lines.
- Offline fixture artifact clearly shows scripted values and behaves identically under hands-only interactions.

## 6. Track P2-03 — Authentic agent-to-agent collaboration

### Requirements

A typed `handoff` is created when a real dependency/output crosses agent ownership. Valid examples: Researcher → Developer blueprint, Developer → Tester implementation/test request, Tester → Developer failure report. Every handoff has unique ID, task, artifact reference, timestamp, delivery state and source mode.

- Agent courier animations are visualizations of `handoff.created`; backend stores canonical handoff immediately.
- A `handoff.received` is an orchestration state, not a collision trigger; receiving agent may begin work when dependency conditions permit even if animation is shortened or skipped.
- If the Quest is busy, handoff becomes an inbox/event-history entry and can be replayed visually optionally—but never replay its tool action.
- Optional speech snippets reflect real message summaries and must be short, muted in focus mode, and distinguish transcript from generated flavor text.
- Visual timeline is reconstructable from event store without needing character movement.

### Implementation

Backend `HandoffService` validates source artifact and target task edge, writes one handoff, raises v2 event, advances scheduler through ordinary dependency gates; exactly-once business state via unique `handoff_id` and idempotency key. Unity `HandoffPresenter` queues only visual actions and uses predefined desk-relative waypoints; handles collision, tracking loss and focus mode with instant or shortened handoff transitions. Avoid NavMesh baking for room surfaces in MVP.

### Acceptance tests

- Valid Researcher blueprint unlocks Developer only when approved dependency is satisfied.
- Duplicate handoff event cannot create two tasks or two queue entries.
- Invalid artifact/recipient pair is rejected before backend state change.
- Animations interrupted by headset sleep resume to canonical status without blocking workflow.
- Handoff history renders truthful status even if no animation played.

## 7. Track P2-04 — Structured spatial explanations

### Requirements

Support **exactly three fixed templates first**: `auth_flow`, `task_dependency_graph`, `module_dependency_graph`. Backend returns a validated node-edge graph, not arbitrary Unity code, GameObject descriptions, scene command lists, remote URLs or scripts.

- Diagram node IDs stable, names <=60 characters, small controlled icon enum and text; edge type `data_flow`, `depends_on`, `calls`, `tests`.
- Limits: default <=12 nodes, <=18 edges, max depth 4, maximum text lengths; reject cycles where template mandates DAG (task dependencies), but allow cycles in code-call diagram if explicitly flagged.
- User can pinch a node to highlight linked edges and open evidence; two-hand pinch scales entire graph between configured min/max; `Reset layout` returns to desk-friendly view.
- Voice can request a diagram, but a hands-only template list is mandatory. Fallback when graph generation fails: readable textual explanation and original artifact refs.

### Implementation

Use deterministic extraction from known auth demo workflow and task graph first. Optional LLM produces structured candidate graph with server-side JSON schema validation, ID consistency checks and bounds; no direct model→Unity command channel. Layout via fixed templates or lightweight graph layout offline; cache diagram by artifact hash, template and revision. Quest uses simple meshes, pooled nodes, lines and billboards; keep occlusion, clipping and legibility conservative.

### Acceptance tests

- Invalid edge endpoint, excessive node count, unsupported icon or malformed JSON rejected.
- Selecting node highlights matching evidence and does not access arbitrary filesystem paths.
- No network connection still allows one explicit scripted sample diagram.
- Diagram remains within a comfortable seated reach; reset works after scale/rotate.

## 8. Track P2-05 — Adaptive room-aware workstations

### Requirements

Keep the existing MRUK setup. Place `WorkspaceRoot` relative to a recognized desk/table-like surface if scene data provides a suitable plane, otherwise let user manually place on a safe horizontal region. Never assume desks exist, never require full-room navigation, and do not anchor content to uncertain/out-of-reach surfaces.

- Stations maintain stable relative positions within a bounded layout and do not intersect physical obstacles visible to user.
- Show a `Move workspace` affordance; hand pinch and move entire layout; optional scaling from 0.7–1.3x within safe clamp.
- Save local station arrangement per installation/room anchor reference; avoid uploading scene mesh or exact room map to server.
- If stored anchor is unavailable or changes, ask to re-place; no silent teleport across a room.
- Progress decorations represent canonical milestones, not arbitrary time elapsed; example: blueprint shelf fills when a research artifact is verified, test machine lights only on actual test report.
- Focus mode reduces motion and suppresses nonessential animations.

### Implementation

Create `WorkspacePlacementAdapter` around installed MRUK APIs, `StationLayoutController` with local coordinates, `LocalLayoutStore` persisted in a versioned JSON/PersistentDataPath file (only non-sensitive poses and anchor handles). Validate available clearance; use manually adjustable workspace root fallback. Keep collider volumes inside constrained tabletop box. Do not create duplicate anchor manager or another camera rig.

### Acceptance tests

- Room scene recognized: workspace stable after turning head and reopening scene.
- Room scene unavailable/denied: manual placement works, tasks/hand interactions unaffected.
- Reset/delete local layout provides safe fresh placement.
- Characters never approach actual walking space; all interaction in arm's reach.

## 9. Track P2-06 — Focus mode and interruption management

### Requirements

Modes: `normal`, `focus` (timed or manual), `do_not_disturb` (manual). Backend evaluates event severity against explicit policy. `routine` and `inbox` updates are aggregated during focus; `blocking` decisions appear via low-intrusion icon/inbox and optional approach per setting; `critical` action requests remain clearly accessible with explicit user acknowledgement, never silently approved or discarded.

- Physical small `Focus` toggle on board and hands-only time options (15/30/60 minutes + until turned off); voice is optional.
- Agents continue *authorized* background work; task state and event store continue normally.
- At end of focus, generate digest with counts, new artifacts, blocked decisions, failures and linked task IDs; digest is based on event IDs not model hallucination.
- User can open full inbox at any time; missed critical notices still surfaced on entry.
- Reduced motion mode: minimize agent walkups, particles and speech; not a substitute for accessibility settings.

### Implementation

`FocusPolicyService` determines notification delivery (visual only), not whether work executes. Persist policy revision server-side and emit v2 `focus.changed`; Quest applies local preferences (audio level, motion) and queues nonurgent presentation events by event ID. On reconnection, rebuild inbox from canonical event store, not ephemeral Unity objects.

### Acceptance tests

- Focus mode on: two nonurgent task completions produce two inbox entries and zero approach animations.
- Blocking approval remains discoverable and cannot auto-execute.
- Critical alert is visible and accessible even when muted; no unsolicited shell action executes.
- Quit/relaunch restores focus state from snapshot until expired.
- One event replay after reconnect produces only one inbox item.

## 10. Track P2-07 — Miniature software factory

### Requirements

Build a **single visual metaphor** tying P2-01/02/03 together. Stations: `Blueprint Bench` (Researcher), `Code Bench` (Developer), `Test Rig` (Tester), `Output Shelf` (completed artifact). User sees labeled conveyor/track segments, but backend flow is ordinary task graph.

- Blueprint appears only when a research artifact is created; paper moves through bounded waypoint animation after genuine handoff.
- Code model/box appears only after implementation artifact created. Test rig operates only when tester task is running; red/green outcomes derive from actual test report.
- Failed test returns a **failed report artifact** to Developer via an authoritative handoff; successful verified result goes to output shelf.
- Every factory object has a small `Inspect` action revealing the underlying task and artifact. `Skip animation` still leaves correct result.
- Persist high-level state in canonical snapshot; do not store arbitrary machine animation phases as business truth.

### Implementation

`FactoryVisualStateMachine` maps `(task status, handoffs, artifact kind, test outcome)` to a small set of `VisualCue`s: `blueprint_ready`, `in_implementation`, `ready_for_testing`, `tests_failed`, `tests_passed`, `released`. `FactoryLinePresenter` manages pooled physical proxies, waypoint tweens, station lights and optional conveyor SFX. Use low-poly material sharing and pooled objects to minimize Quest draw calls. For live mode, bind all outputs to real IDs, not generic positive animations.

### Acceptance tests

- `tests_failed` never illuminates green output shelf.
- A re-run with new test report updates visual without duplicating final artifact.
- Offline visual fixture includes a failure-and-recovery branch with permanent demo label.
- Restart midway reconstructs visuals from snapshot and does not replay old tool actions.
- Factory remains meaningful with motion disabled (labels + status indicators).

## 11. Detailed cross-feature state and interaction rules

### 11.1 Board card state

`Ready → Grabbed → HoveringValid/HoveringInvalid → PendingServer → Confirmed/Rejected → Ready`. On loss of hand tracking, app suspend or network failure during grab, card returns to anchor and command is never submitted unless a confirmed release already occurred. Network timeout after release uses idempotent retry of the *same* key; show pending or unknown until canonical state fetched.

### 11.2 Visual event pipeline

`Persisted Event → version projection → Unity event reducer → feature-specific view model → optional animation queue → scene presenter`. Animations can coalesce, truncate, skip, or time out. A UI warning explains real backend state if visuals fail; no animation may commit business state.

### 11.3 Dependency scheduler

Tasks have `dependencies[]` referencing existing task IDs and are processed as a DAG for the demo workflow. `ready = all(prerequisite.status == completed) and any_required_decisions_approved`; dispatch under row lock/transaction. Reject cycle on create/update. Track `revision` on every task mutation. Independent tasks may run concurrently, but dependent tasks cannot start early. Reassigning work cannot bypass human approvals or tool allowlist. Failed dependencies lead to `blocked` presentation and explicit retry/replan, never automatic optimistic success.

### 11.4 Document and privacy guarantees

No raw file system paths; no secrets, API keys or environment dumps; redact logs; render bounded sanitized markdown/plain text only. Every artifact and visualization has `source_mode`, provenance metadata, linked task, UTC creation time and sanitized content hash. External research citations must be real or marked `unverified`; no fake sources in live mode.

### 11.5 Audio, motion and accessibility

Default to gentle audio, low-motion animation and hand-usable controls. Provide persistent `Reduce motion`, `Mute agent chatter`, `Recenter workspace`, `Open inbox`, `Skip animation`. Keep critical text readable from seated use; test against Quest display rather than judging in Unity Editor alone. Hand pinch targeting must not require fine motor precision or controllers.

## 12. Delivery phases, parallel work, and dependency gates

### Phase A: integration freeze and foundation (first)

- Tag Phase 1 stable. Inventory actual project hierarchy/installed Meta packages, existing scene prefabs, and backend routes; create integration compatibility matrix.
- Agent P2-00 produces v2 JSON schemas, DTOs, compatibility adapters, migrations, contract tests, a v2 snapshot/event endpoint and feature flag defaults.
- Prove one v2 event reaches Quest without affecting v1 approval flow. Do not parallelize incompatible schema definitions.

**Exit:** Phase 1 smoke tests pass unchanged; Phase 2 sample v2 event and snapshot roundtrip; demo v2 gateway parity verified.

### Phase B: highest-value vertical slice (second)

- P2-01 builds board and assignment with real server ack.
- P2-02 builds rich artifact inspection, including one real diff and one test report.
- P2-03 adds one authentic Researcher→Developer handoff.
- Integration agent runs Quest live + offline test and reconciles task status after reconnect.

**Exit:** user hand-assigns task → researcher artifact → approval → developer begins → tester receives handoff → user reviews real report.

### Phase C: factory and focus (third)

- P2-07 builds animated factory on real events, including failed tests and recovery.
- P2-06 adds focus/inbox policy and digest.
- QA measures frame time and actual hand interaction reliability on Quest.

**Exit:** factory correctly represents pass/fail in both modes; focus does not lose decisions; no significant frame-time regression.

### Phase D: spatial polish (last, optional before competition)

- P2-05 adapts workstation positions to scene and persists layout with fallback.
- P2-04 implements one deterministic diagram with evidence-linked nodes.
- Polish, capture demo, verify standalone APK and offline truth labeling.

**Exit:** no feature relies on having your Windows PC connected; project runs with hand tracking and safe stationary placement.

### 12.1 Agent staffing and ownership

| Role/ticket | Exclusive area | Integration requirement |
|---|---|---|
| P2-00 Contract/lead | contracts, snapshots, migrations, adapter | freeze first |
| P2-01 Board | board prefabs + task graph assign routes | integrate through contracts |
| P2-02 Artifacts | artifact routes + viewer prefabs | reuse Phase 1 paper UI |
| P2-03 Handoffs | handoff service + courier visuals | use real artifact IDs |
| P2-07 Factory | factory prefabs + visual state machine | depends on P2-03 |
| P2-06 Focus | inbox UX + focus service | depends on event store |
| P2-05 Room | local anchor/layout + station prefabs | isolate from core rig |
| P2-04 Diagrams | graph builder + diagram prefabs | last / separate path |
| P2-99 QA | integration scene, automated tests, Quest evidence | gate each merge |

Agents should use separate branches or Git worktrees; never concurrently edit the root scene, package manifest or the same contract file. Merge one feature at a time into an integration branch, compile in Unity and verify on actual Quest after each merge. Prefer Editor scripts/Unity CLI Pipeline for prefab generation; check the current installed SDK API rather than relying on guessed class names.

## 13. Testing and quality plan

### 13.1 Test layers

1. **Contract tests:** JSON schemas parse and fixture instances validate. v1 contracts remain unchanged.
2. **Backend unit tests:** task DAG, priority bounds, assignment version guards, pause semantics, safe handoffs, event replay, focus inbox and sanitizer.
3. **Backend integration tests:** real sandboxed sample repo creates diff and test report; stale commands, duplicate keys and unauthorized sessions rejected.
4. **Unity edit-mode tests:** reducer event dedupe, card drag state transitions, presentation state mapping, factory pass/fail projection, diagram parser bounds.
5. **Unity play-mode / Editor automation:** prefab setup, button handlers, object pooling, no duplicate XR rigs, no missing references; Editor simulation is not Quest proof.
6. **Quest 3 device QA:** hands-only interaction, MRUK room/no-room fallback, recenter, reconnect, sleep/wake, 15-minute session, frame timing, reading comfort, focus mode, offline demo launch.

### 13.2 Quantified acceptance targets (internal goals, not official claims)

- 10/10 duplicate idempotency retries cause one canonical command application.
- 10/10 invalid/drop-outside events cause no server mutation.
- >=9/10 valid card assignments completed by a new user in a controlled test setup.
- 100% of visual test outcomes match real stored test outcome on known fixture attempts.
- 100% of test artifact views identify live vs scripted source accurately.
- Zero missed or silently auto-approved critical decisions under focus mode.
- Interactive Quest target: >=72 FPS sustained in representative usage; profile CPU/GPU on device and defer expensive visual flourishes if necessary.
- Full offline scripted scenario demonstrable from fresh install without PC, tokens or internet.

Meta recommends Interaction SDK for hand interactions and MRUK for scene understanding; live Quest validation remains mandatory. Consult current official docs when actual package versions differ from your Unity project.

### 13.3 Adversarial failure checklist

- Client sends an assignment for task it does not own; reject.
- Task card is dropped after task already completed; reject with 409 and reconcile.
- Task's `revision` changes during drag; no silent overwrite.
- Same handoff event delivered 5 times; only one UI cue and one task transition.
- Device loses network after sending approve; idempotent decision survives retry.
- User denies scene permission; manual desk placement still operates.
- Host writes test logs containing token-like values; sanitizer masks them.
- User asks arbitrary diagram system to execute code; reject.
- Focus mode hides low-priority alerts but does not hide pending dangerous approvals.
- Factory green success light never appears for a failed or untested task.

## 14. Release, demo and rollback

Feature flags: `p2_board`, `p2_artifacts`, `p2_handoffs`, `p2_factory`, `p2_focus`, `p2_room`, `p2_diagrams`. Default off in live builds until tested; enable separately for demo fixtures once validated. If any track fails, disable it without impacting Phase 1 flow. Maintain tagged release and original scene backup. The fallback deliverable is Phase 1 plus a safe board/inbox, not a broken broad feature set.

**Suggested demonstration sequence (~90 seconds):** task card drag → researcher walks to desk and presents decision → approval triggers blueprint courier → developer creates diff → test rig runs and fails → tester brings fault note → developer fixes → success shelf lights → user expands test report → focus mode on/off. Use only authentic live artifacts in live mode; if using fixture, show `SCRIPTED DEMO` continuously.

**Post-demo value tests:** compare conventional task dashboards and MuseDesk on task completion time, attention interruptions, correct approval decisions, perceived workload and preference. Record results rather than assuming animated workspaces inherently improve productivity.

## 15. Agent completion contract

Every coding ticket is complete only if it supplies: implemented source paths, actual compiled results, test transcript, evidence from live Quest or explicit `not device-tested`, updated docs/fixtures, backward-compatibility confirmation, and rollback note. Do not claim a function is working simply because a class exists. The human integrator retains responsibility for headset testing, credentials, review of privileged actions, and final demo.

**Final stop condition for integration agent:** after Phase B, hand the team a 2-minute test: pick a card up, assign it, receive a research paper, approve, watch the authoritative handoff, inspect a diff and real test results; repeat with Wi-Fi disabled and clear scripted labels. Only then add the remaining eye candy.

## 16. Source references (official platform documentation, checked Nov 11, 2026)

- Meta Hand Tracking overview: https://developers.meta.com/vr/documentation/unity/unity-handtracking-overview/
- Meta Interaction SDK overview: https://developers.meta.com/vr/documentation/unity/unity-isdk-interaction-sdk-overview/
- Meta MR Utility Kit overview: https://developers.meta.com/vr/documentation/unity/unity-mr-utility-kit-overview/
- Meta MRUK scene data and fallbacks: https://developers.meta.com/vr/documentation/unity/unity-mr-utility-kit-manage-scene-data/
- Meta Quest performance guide: https://developers.meta.com/vr/documentation/unity/unity-perf/
- Unity Pipeline package: https://docs.unity.com/en-us/unity-cli/unity-pipeline/unity-pipeline-package

Package APIs evolve: resolve against installed versions rather than treating every proposed class name as a Unity/Meta-provided class.
