# HANDOFF FOR CLAUDE CODE — Implement Wall Habitat, Manager, Temporary Agent Visits & Portal

You are the **principal Unity XR engineer, spatial computing architect, multi-agent orchestration engineer, UX engineer, animation integration lead, and QA lead** for MuseDesk.

This is an EXISTING Quest 3 project at `C:\dev\MuseDesk\MuseDesk`. Read **`00_MASTER_SPEC.md` in full** and use it as the product/technical source of truth, then inspect the actual repository and latest state. Antigravity provides real wall habitat, original manager, elevator, portal and visit assets according to `01_ANTIGRAVITY_ART_HANDOFF.md`. Your job is actual implementation, wiring, testing, and integration—not just planning. **Keep the feature flag off in the competition release until on-head acceptance gates are met.**

## 0. Reconnaissance and safe baseline

1. Read existing `AGENTS.md`, the latest engineering handoff / review, project architecture, Phase 1/2 and spatial mode docs, room placement scripts, current backend contracts and test fixtures. Inspect `MainMRScene.unity`, all primary prefabs, rig hierarchy, actual SDK and Unity versions.
2. Inspect current Git HEAD, branches, working trees, uncommitted user edits, release tags/APKs. Never reset or delete user work. Create isolated branch `feature/wall-habitat` (or equivalent); use existing guarded Unity scene builder (e.g. `scripts/run_guarded.ps1`) if valid. Only one agent may write production scene/prefabs at a time.
3. Run a baseline compile, backend tests, PlayMode smoke and existing Desk/Floor/Ceiling regression suite. Preserve a known-good APK. Don't silently upgrade Meta SDK during a new feature branch.
4. Verify actual Antigravity asset branch/commit, `docs/Art/WALL_HABITAT_ASSET_MANIFEST.md`, socket list and animation hooks. If art not yet available, implement test scene with simple labeled proxies and do not pretend real art has shipped.
5. Maintain brief `docs/Engineering/WALL_HABITAT_STATUS.md` showing confirmed vs unverified work, dependencies and gate status.

## 1. Architecture — additive fourth mode

Extend the existing spatial presentation system with enum value `Wall` (and appropriate serialization migration). Desk/Floor/Ceiling remain unchanged. Only one primary home office active; the backend retains one canonical agent registry. Wall mode should own a `WallHabitatRoot` and invoke established spatial event/state adapters. No duplicate `SessionController` or separate wall backend.

### Suggested Unity components (adapt to existing types)
- `WallHabitatModeAdapter` — implements existing spatial mode interface, exposes wall placement and mode activation.
- `WallPlacementEvaluator` — pure room geometry candidate scoring; not a MonoBehaviour requirement.
- `WallHabitatPlacementController` — ghost, ray adjust, confirm, cancel, anchor persistence and validation.
- `WallHabitatAssembler` — loads modular asset manifest, attaches modules to named sockets, generates 2-tier / 3-tier layouts.
- `HabitatSocketRegistry` — authoritative socket lookup by stable ID, manifest validation.
- `HabitatNavGraph` and `HabitatRoutePlanner` — strictly LOCAL to wall habitat; no full-room pathfinding.
- `HabitatElevatorController` — elevator queue/occupancy, landing target and animation synchronization.
- `AgentVisualChoreographyService` — requests visual movement for actual coordination events, separate from task logic.
- `AgentInteractionModeController` — Select Agents state, explicit activation, interaction ownership and targeting.
- `AgentVisitPresentationController` — temporary small projection and desk-perch visit lifecycle.
- `AgentVisitConversationBridge` — read-only `/btw` requests to backend, response display, timeout/cancel.
- `ManagerVisualController` — manager office, printed plan proposals/committed card visuals.
- `LargeAgentPortalController` — placement preview, safe spawn, enlarged projection, animations and cleanup.
- `WallHabitatPersistence` — anchor/layout/mode setting; no backend task state.

Keep code modular and testable. Write clear constructor/injection boundaries for pure placement, route, task and conversation logic. Avoid global GameObject.Find at runtime as a permanent integration mechanism; use serialized refs, registries or verified socket IDs.

## 2. Wall plane placement, occlusion and persistence

Use the existing MRUK wrapper and installed SDK versions; inspect API before writing calls. Subscribe to scene ready/updated/recenter. Query wall planes/polygons and recognized furniture/doors/windows where available. Never modify MRUK-owned anchor objects; instantiate separate presentation root. Use room-local pose and validate after restart. If no valid wall exists, explicitly choose manual preview or revert to Desk.

### Candidate evaluation
- Wall region big enough for proposed layout: full initial dimensions ~0.85–1.15 m wide x ~0.7–1.0 m tall x ~0.18–0.32 m deep; compact variant smaller.
- Clear of known door/window regions and furniture obstacles. Enforce inset from wall polygon edges, collision bounds and user near-head/monitor zones.
- Wall normal faces viewer / room. Reject behind-surface orientations and inverted transforms.
- Prefer side-wall region visible from seated position and away from center of actual monitor.
- Draw ghost perimeter, clearance/depth visualization and indicator. Drag/rotate/adjust with hand-ray; require clear **Place Here** confirmation separate from releasing drag.
- Restore only if spatial anchor still locates appropriately; otherwise prompt reposition. Cancel restores old mode without moving task state.
- Never assume unscanned room space is empty; show confidence/verification message.

### Code quality / tests
Use pure `RoomSnapshot` + `WallPolygon` test fixtures: good wall, short wall, narrow corner, doorway, window, desk obstruction, rotated normal, curved/partial scan, no permission, scene recenter, lost anchors, failed restore. Include unit tests for world↔local transformations and bounds. No accidental mode change on tracking loss.

## 3. Modular layout and collision-free choreography

Read the art manifest to obtain local cubicle socket and elevator stop poses. Build workspace instance from layout data, not hard-coded editor hierarchy paths. Assert required `AgentHomePose`, `WorkPose`, `SeatPose`, `ElevatorQueuePose`, `ElevatorCabinPose`, `FloorLandingPose`, `HandoffPose`, `PortalDeparturePose` for applicable modules.

Create a local nav graph with walkable node coordinates and clearance/capsule radius for each mascot. The agent root is controlled by one visual movement director; Animator controls bones, not world pose. Validate paths against local static cubicle geometry, stairs, desks, wall backplate and elevator door. Reserve narrow hallway/elevator nodes to avoid agents walking through each other. Do NOT use a character's animation to move it through desks or through the wall. On route failure, play a non-spatial substitute (e.g. send a document) or gracefully reset visual home; never deadlock backend tasks.

### Elevator queue
Implement `Idle -> Reserved -> Boarding -> DoorsClosing -> Travel -> DoorsOpening -> Exiting -> Released` with interrupt recovery. One cabin rider initially; queue simultaneous requests. Physical vertical travel to named landing sockets; don't rely on baked Blender height. Local mechanical animation clips triggered by state events, but a missing clip must not prevent visual/system recovery. If the current task changes while rider is midway, complete or safely cancel visual trip then re-home to compatible work pose. Prevent overlapping actors across doors and floor geometry.

### Event-driven choreography
Map real backend event IDs to visual acts, deduplicate and timebox them. Manager delegation events -> card printing; researcher/developer handoffs -> elevator or local route; tester failure -> warning response. When more events arrive than can be staged, coalesce/deprioritize cosmetic journeys. Always show most recent authoritative task state, even when skipped animations.

## 4. Implement Select Agents far-grab mode

UI: one unobtrusive `Select Agents` control in wall habitat's context interface. State `Off -> Armed -> Targeting -> ProxyGrabbed -> DropPreview -> DeskVisit/Return` with timeout and cancel. Armed agents glow subtly and are targeted using the existing Interaction SDK distance/ray grab; user must be able to use only hands.

**Critical technical design:** Do NOT drag the canonical working character or unparent a runtime agent controlled by its task animations. Create or reuse a **visual projection proxy** using the same mesh/rig/role ID, with one presentation ownership token. The home avatar stays at workstation with a soft 'visiting' indication (or is visually hidden with an occupied marker), while the backend agent keeps working. No duplicated worker process. Only one active proxy for an agent; limit total simultaneous visits to one initially.

### Interaction arbitration
Already-held object > near grabbable/poke > armed Select Agents > explicit wall placement > ceiling pull > ambient gaze. Reuse existing arbiter if present. The same hand must not both take a paper and initiate a wall-agent grab. Tracking loss cancels, not commits. Release only initiates placement if valid desk/perch zone and a stable tracked release. Invalid drops return to a safe hover/home pose; no falling into real desk or floor and no backend task mutation. Clear visual feedback for valid/invalid drop.

### Desk perch and `/btw`
After successful drop, snap projection to a tiny desk/side perch; show a compact context card with current agent/task and **Ask**, **Meet Larger**, **Return**. Allow direct voice if configured and an always-available text/choice fallback. Route questions via an additive read-only side conversation API. The main task continues; if context updated during conversation, show current context revision. End/Return closes proxy safely. App pause, recenter, mode switch or backend disconnect must not strand the avatar.

## 5. Backend side-conversation service (read-only, independent)

Inspect existing agent adapter, LLM provider, task snapshot and voice context routes before building. Introduce `AgentSideConversationService` only if missing. Use documented endpoint/event conventions, e.g. `POST /api/agents/{id}/side-question` (route names adaptable) with `{task_id, session_id, question, context_revision, idempotency_key}`; reply `{reply, provenance, context_revision, status}`.

Enforce strict permissions:
- May read authorized task metadata, approved summaries and safe logs/artifacts according to existing privacy policy.
- May explain the worker's current plan, output and test result with context and uncertainty.
- No tools that modify files, execute shell, spawn agents, change cards, approve actions, send arbitrary network, access credentials or alter core agent transcript.
- Independent ephemeral conversation memory; do not block running worker; bounded queue/concurrency and timeout.
- A user command that actually asks to change work becomes a separate **proposed amendment** routed through existing approval mechanism; never silently execute from `/btw`.
- Support honest offline/demo reply fixtures, clearly labelled SCRIPTED DEMO. Never invent evidence from nonexistent model activity.

Test task-state unchanged before/after `/btw`, multiple simultaneous requests, unavailable worker/context, stale snapshot, backend disconnect and long request timeout. Implement logging that avoids exposing sensitive source content.

## 6. Manager — fourth logical role and task coordination

The Manager is not merely a cosmetic desk character. Inspect current orchestrator/task board and add `manager` to known agent types where appropriate, with explicit capabilities and API contracts. Use same auth/session/permission limits as worker tasks. Do not implicitly grant Manager shell access or deploy privileges.

### Interaction flow
1. User talks to/selects Manager (wall deck, side visit or near-user summary).
2. Manager receives an objective, available workers/capabilities and a bounded current task snapshot.
3. Manager produces a structured proposed plan with titles, owners, dependency DAG, acceptance criteria, cost/risk notes and assumptions.
4. Show human-readable **Plan Card** at comfortable reading distance. Proposed items do NOT appear as running tasks.
5. User approves, rejects or revises plan where warranted. Consequential actions need individual approval according to existing policy.
6. Backend validates plan strictly; commits atomically/idempotently to actual task board. Canonical task IDs returned.
7. Only now visual printer emits solid task cards and cards travel to assigned cubicles.
8. Task workers actually execute when allowed by existing scheduler; dependencies gate real execution; UI reacts to authoritative states.
9. Manager can report progress/blockers and propose reprioritization; does not secretly approve or cancel tasks.

### Technical contracts
Implement `ManagerPlanService` behind a compatible API. Define plan schema/revision/idempotency keys and task kind capability restrictions. Reject cycles, bad IDs, unsupported tools and missing owners. Never treat LLM-generated plan as trusted executable commands. Keep read-only inspect separate from write/commit. Use a provider adapter if a model is configured; otherwise scripted demo can show a labelled template plan, while live mode must honestly expose unavailable planning capability.

Manager events: `plan_proposed`, `plan_approved`, `plan_committed`, `task_created`, `handoff_confirmed`, `plan_rejected`, `plan_failed`. Map these to real existing events where possible rather than inventing incompatible duplicates. Dedup by event ID and revision. The manager's visual task printer must never manufacture a task-board card without a corresponding canonical task ID.

### UI/animation
Manager desk on raised deck with plan printer/overview; light choreography of sending real cards through a miniature rail/elevator. Character turns to each worker, points, gestures. Task delegation visually meaningful but NEVER gates or mutates the backend workflow. Manager `/btw` questions are still read-only; explicit approved commit uses separate API.

## 7. Large-agent portal and presentation scale

Add `Meet Larger` contextual action when an agent's projection is selected. Candidate placement uses existing room geometry with a bounded floor footprint, standing clear height, known obstacles and user's personal working volume. User confirms a visible ghost ring/agent silhouette before it appears. Portal placed to the side, not through a desk, monitor, wall, chair or the user. Need ergonomic seated viewing, no forced motion/walking; if no valid large area, offer a small near-user visual instead.

### Visual and logical separation
Instantiate ONE large visual proxy at a time, based on existing rig; it shares `agent_id` and side-conversation ID, but no active worker process. Home avatar may fade or show `On Visit`; never show both doing contradictory independent work. The large rig has its own Animator and transforms; do NOT scale XR Origin, parent the live backend components to it or scale physics constraints unpredictably. Use authored scale range to target roughly 0.9–1.2m avatar height and test real Quest perspective. Light textures and materials appropriate for standalone Quest.

### States
`Closed -> Preview -> AwaitConfirm -> Opening -> Emerging -> Conversing -> Returning -> Closing -> Closed` with error/cancel. World root pose supplied by placement service; local emergence animation from art. Check full swept bounds along route, not only landing pose. Cancel/tracking loss/recenter closes projection and releases resource reservation. No portal auto-open from notifications, no full-room teleportation of actual agents. Preserve user-held document separately. Contextual controls `Ask`, `View task`, `Suggest change`, `Send Back`; mutations require explicit validated proposal/authorization. Comfort mode disables larger animation/particles.

## 8. Interactions, notifications and workspace consistency

- Idle wall habitat shows beacon and tiny team doing authentic work, not many floating text boxes.
- Proximity/dialog text in near-user inspection card only; do not attempt to read tiny cubicle screens from across room.
- `Select Agents` mode is explicit and visibly marked, auto-times out, and is mutually exclusive with workspace relocation and ceiling-pull gesture.
- Existing Desk/Floor/Ceiling modes continue to function; switching while holding a proxy or open document offers safe finish/return, not teleport.
- Focus mode suppresses optional disruptions; real blocking decisions remain available through existing approval flow.
- Attention from manager proposal uses a different icon from actual approval request; no false optional suggestions.
- Default task-related animation should be grounded in real event; if backend unavailable do not imply live execution.

## 9. Performance and presentation polish

Profile Quest standalone, especially wall habitat full 3-level layout with all four characters, moving elevator and large portal. Prefer one enlarged proxy at a time. Use material sharing, LOD and animation update throttling for distant workers, simple shaders, minimal transparent effects, no overlapping large portal VFX. Do not make the wall habitat continuously animate intensely. Capture CPU/GPU timings before tuning. Preserve smooth hand interaction, no head-tethered large UI panels.

Character movements must look physically possible: body stays above walk surface, no walking through cubicle walls or desks, elevator doors open before boarding, arrival positions line up, no giant avatar foot penetration into real furniture in known room data. If no valid visual route, gracefully skip/soft-transfer rather than fake a wall-clipping walk.

## 10. Implement in controlled milestones with gate checks

**Milestone 0 — Baseline protection:** branch, test results, manifest/SDK inspection, all flags off.

**Milestone 1 — Wall placement:** synthetic fixtures, candidate scoring, preview, explicit confirmation, anchoring/persistence, fallback. All existing modes pass.

**Milestone 2 — Modular wall layout and elevator:** mount existing three agents, instantiate manager art placeholder if needed, stable sockets, local nav graph, reservations, collision-free elevator. Basic real event visualization.

**Milestone 3 — Far-agent side visit:** selection mode, ownership priority, projection proxy, grab, desk placement, read-only `/btw`, return. Prove task remains unchanged.

**Milestone 4 — Manager orchestration:** manager agent role, structured plan API, plan approval, task board commit, visual delegation card flow, task-capability safety gates. Prove real backend dispatch.

**Milestone 5 — Large portal:** preview/clearance, one enlarged visual proxy, local art animations, inspect/ask/return, comfort fallback and frame-rate testing.

**Milestone 6 — Quest polish:** real wall scan, gestures, rear/side wall candidates, task board, every held object, recenter/restart, multi-agent elevator concurrency, audio/visual fit, performance and soak test.

After each milestone, compile, run relevant tests, take eye-point screenshots and preserve rollback. Implement directly rather than stopping at a roadmap. If art is missing, finish code with labeled proxy and manifest-driven swap, explicitly mark art dependency unverified.

## 11. Required tests

### Automated
- Valid/invalid wall and anchor restoration; no scene data gracefully falls back.
- Full and compact layout sockets unique and proper transforms; no obvious bounding box overlap or missing pivot.
- Local nav and elevator occupancy, event coalescing, route fallback, interrupted trip, two actors.
- Far selection ownership: paper and stamp take precedence; outside select mode cannot accidentally yank agent; tracking loss cancels.
- Desk visit never changes task state or spawns duplicate backend worker; invalid drop resets; app pause returns proxy.
- Manager plan DAG, approval, idempotent commit, rejected capabilities, dangerous action, backend failure rollback; task card only after commit ACK.
- Portal bad room placement, invalid movement, one-active-proxy constraint, scale and avatar restoration.
- Desk/Floor/Ceiling regression, demo honesty, task board and approval idempotency.

### On headset (must be marked unverified until human tests)
- Actual Quest wall recognition/placement and persistence after recenter/relaunch.
- View the habitat from normal seated position, verify no furniture/monitor clipping.
- Elevator agents do not move through cubicles/floors; boarding, travel and exit clearly visible.
- Select Agents via hand ray; distance grab; desk drop; no interference with stamp/paper/card grabs.
- Read-only conversation uses current task and leaves it running; close and repeat.
- Manager creates real accepted task cards/owner updates; backend permission gates verified.
- Portal placement confirmed in real clear space; 1m avatar readable, nonintrusive, comfortable; close and return.
- Sustained Quest frame rate and temperature under worst scene; record measurements, not estimates.

## 12. Releases and product constraints

This is a major feature. The competition deadline is November 18, 2026. Preserve the known-good competition APK. Do not enable Wall Habitat, Manager or Portal in the released build unless each relevant gate passes on real Quest with a clear feature flag. Best post-competition order: Wall visual mode -> `/btw` projection -> Manager -> portal. Experimental footage must be labelled as prototype and not represented as supported behavior. Never block current submission packaging for Phase 3 scope.

## 13. Deliverables / reporting

Implement directly in code and Unity. Produce:
- `docs/Engineering/WALL_HABITAT_IMPLEMENTATION_LOG.md`
- `docs/Engineering/WALL_HABITAT_ARCHITECTURE.md`
- `docs/Engineering/WALL_HABITAT_API_CONTRACTS.md`
- `docs/Engineering/WALL_HABITAT_PLACEMENT_TESTS.md`
- `docs/Engineering/WALL_HABITAT_INTERACTION_QA.md`
- `docs/Engineering/WALL_HABITAT_PERFORMANCE.md`
- `docs/Engineering/WALL_HABITAT_RELEASE_FLAGS.md`
- Updated art contract alignment checklist and exact scene/prefab paths.

End report: actual commit, build APK path, what was implemented, what was auto-tested, what was tested in Editor, what was physically validated on Quest, unresolved failures, and what is disabled. Keep all claims evidence-based.

**Critical directive:** the little coworkers should inhabit a beautifully engineered wall treehouse, visibly ride elevators to coordinate, accept manager-delegated real tasks, and be summonable for side conversations or enlarged portal meetings—while the user's actual AI work continues unaffected and no character clips through furniture or the room. Preserve the working application and execute in small, verifiable steps.
