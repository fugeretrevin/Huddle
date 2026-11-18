# MuseDesk Phase 3 — Wall Habitat, Agent Side-Visits, Manager & Portal
**Implementation architecture and product requirements | 2026-11-14**

## 0. Scope, source of truth, and release strategy

This is an ADDITIVE module for the existing Unity 6 / Meta Quest 3 MuseDesk project at `C:\dev\MuseDesk\MuseDesk`. The existing Desk/Floor/Ceiling spatial modes, FastAPI backend, approval system, task board, paper/stamp, character rigs, factory, attention cues, and offline scripted demonstration remain the baseline. Inspect the current Git HEAD, documentation, actual scenes and SDK versions before changing anything. Earlier reports mention SDK 209, `MainMRScene`, Unity CLI/Pipeline, `scripts/run_guarded.ps1`, a room-placement abstraction, spatial presentation director, and restored safe feature flags; these must be re-verified rather than assumed unchanged.

**Deadline constraint:** competition submission November 18, 2026, noon Pacific. On November 14 there is not enough time to introduce all Phase 3 systems safely and simultaneously verify existing modes. This expansion MUST be feature-flagged and built on a branch. A complete Wall Habitat + Manager + Agent Visits + Portal is a post-competition development target unless all hardware acceptance gates pass earlier. Do not let this work destabilize the submission build.

**Core nonnegotiable:** the same canonical logical agents and task state exist regardless of visual location. No interaction may automatically stop, restart, duplicate, reassign, or modify AI work merely because a character is lifted, enlarged, transported, animated, or placed in a different location.

## 1. Product promise

MuseDesk is a miniature living AI organization inhabiting the real room, not a floating task dashboard. In Wall Habitat mode the office resembles a beautiful, softly futuristic modular treehouse attached visually to an actual wall. Its lower tier contains the existing Researcher, Developer, and Tester work cubicles, linked by a tiny elevator and walkway. An elevated Manager office overlooks the team and coordinates tasks. Users can summon an agent onto their desk for an ephemeral, `/btw`-style conversation, or open a portal in available space to meet an enlarged representation. Most of the time the habitat stays calm, communicating through a subtle attention beacon; the user's actual workspace remains free.

### Experience principles
1. **Whimsical, not distracting:** tiny elevator motion, warm desk lights, paper deliveries, characteristic gestures; no continuous dancing, excessive particles, or audio chatter.
2. **Room-native:** wall placement fits the actual surface, excludes doors/windows/furniture where visible, and is user-confirmed. Do not claim to detect hidden obstructions.
3. **True interaction, not roleplay:** task cards represent real task state; Manager proposals only create real tasks when the backend confirms them; decisions remain authorized by the existing approval system.
4. **Temporary conversations:** selecting or resizing an avatar creates a presentation session for the existing agent, not a new working agent.
5. **Friendly recovery:** every motion can be cancelled safely; dropped avatars return home; lost tracking never counts as user confirmation.
6. **Clear choice:** users can continue to use Desk, Floor, or Ceiling modes. Wall Habitat is optional and fully reversible.

## 2. Visual direction & layout

Name: **The Workshop Treehouse**. Think miniature architectural model: soft ivory surfaces, light natural wood, muted teal and lavender trims, a little warm brass hardware, softly curved windows/frames, elegant ladders or elevator tracks, warm integrated lights. Existing original mascot silhouettes and material palette take precedence. Avoid literal rustic forest treehouse, copyrighted mascot references, high-tech military bunker, or hundreds of clutter props.

### Recommended module hierarchy
- **Top Manager Deck:** a small lookout office/study, approximately one module above the team; visible central desk, overview map, delegation printer, review tray and attention beacon. Manager is distinct but visually related to original three mascots.
- **Middle Work Deck:** optional planning balcony/meeting nook, shared blueprint station, elevator landing and task-card conveyor. Can be omitted in the compact layout.
- **Lower Work Deck:** Researcher/Developer/Tester cubicles along a horizontal row with short corridors, separate yet visually connected; workstations reused from the existing project.
- **Right or left Elevator Spine:** enclosed miniature lift with transparent/open window and two or three stop positions. Floor arrows and stop indicators convey current destination; never intersect the workstations.
- **Perimeter:** subtle wooden/metal structural frame, railings, modular sockets, and a flat offset backplate that appears wall-mounted. Surface should be very shallow and not bury cubicles in wall geometry.
- **Persistent far-view signal:** a compact beacon at the edge, readable by shape/animation rather than tiny text.

Start art budgets with wall assembly width **0.80–1.15 m**, height **0.65–1.0 m**, depth from wall **0.18–0.32 m**; compact alternative width **0.60–0.8 m**. These are design heuristics, not verified fit or SDK parameters. Place at a comfortable side-wall position, not directly over the user's monitor; a suggested center around seated eye height plus 0–0.35m, adjusted to real geometry. Only one primary habitat active at a time. Wall art must be modular and support 2-tier/3-tier variants.

**Readable at a distance:** do not attempt to make every cubicle screen readable from 1.5 m. Silhouettes, lights, icon signals, elevators and manager position provide ambient understanding. Expanded summaries appear near the user.

### Mode overview
| Workspace | Ambient representation | Main interaction path |
|---|---|---|
| Desk | mini-office on desk | direct paper handoff |
| Floor | mini-office on clear floor | courier and near-user paper |
| Ceiling | suspended pod | descend / near-user delivery |
| **Wall** | mounted treehouse | far select + temporary agent visit; manager; courier/portal |

## 3. Wall placement and room semantics

Reuse the existing MRUK room abstraction; do not write directly to MRUK-owned anchor transforms. Wall placement must query a wall plane or reasonable plane candidate with confidence and semantic support. Inspect the actually installed Meta XR APIs before implementation. Load data after scene-loaded events; react to scene updates/relocalization. Without usable room geometry, present manual preview with a visible warning and confirmation or fall back to Desk.

### Candidate generation
1. Enumerate wall anchors, polygons, heights and normals where available.
2. Exclude walls intersected by known doors/windows, furniture volumes, and recognized room bounds; unknown obstructions should be explicitly unverified.
3. Compute a rectangular habitat footprint on the wall plane with edge clearance. Account for real assembly width/height/depth including elevator and travel animations.
4. Test habitat volume swept against known geometry using a small margin. Check potential conflict with the user's monitor, seated head/arms, and nearby furniture (approximate zones are not perfect ground truth).
5. Score viewing angle, side preference, head-to-center distance, clear wall area, comfortable eye line, and ability to deliver content without long travel.
6. Display ghost-preview (outline, bracket corners, wall contact pads and facing). Red=invalid, amber=needs user review, teal=valid; icons/labels supplement colors.
7. Let user point, ray drag, shift, rotate within permitted wall plane, and switch compact/full layouts. Explicit **Place Here** confirmation, Cancel and Try Another.
8. Pin to stable spatial anchor if available. Store room ID/anchor ID, local surface pose, scale/layout/module choices; revalidate on re-entry. If invalid, show ghost preview; do not silently render through a new wall.

### Orientation
Wall local coordinate conventions must be documented once. Typically local +Z points outward toward room/user, local +Y up, local +X horizontal; check with actual Unity mesh forward and anchor normal. Keep modules at identity local scale when possible. A selected wall pose must not flip the cubicle interiors toward the wall. Avoid negative scale mirror tricks.

### Relocation and persistence
Relocation is an explicit mode with a ghost (not automatic scene root movement). Do not relocate the habitat while agents are in a non-recoverable visual handoff or the user holds a controlled object; queue or safely restore. Backend tasks unaffected. Any scene change with insufficient confidence should suspend spatial movement and preserve task context.

## 4. Modular construction system

Design `HabitatAssembly` with a pure data layout manifest and stable socket IDs. All cubicles and platforms use shared `HabitatModuleSocket` references; no code using magic child indices.

Example visual hierarchy:
```
WallHabitatRoot (placement controller owns world pose)
  WallBackplate
  ModulesRoot
    LowerDeck
      ResearchCubicleSocket
      DeveloperCubicleSocket
      TesterCubicleSocket
      HallwaySegment_L
      ElevatorStop_Lower
    MidDeck (optional)
      SharedMeetingSocket
      ElevatorStop_Mid
    ManagerDeck
      ManagerOfficeSocket
      TaskPrinterSocket
      ElevatorStop_Manager
  ElevatorSpine
    ElevatorCarPivot
    DoorLower/ DoorMid / DoorManager
    CallIndicators
  DeliverySocket
  AttentionBeaconSocket
  AgentPortalDepartureSocket
  Status/PlacementUIRoot
```

One script/controller should build layout from data and bind sockets. Build a compact 2-level option and an expanded 3-level option without constructing two incompatible codepaths. Modules are aesthetic/visual; adding a module must not create another backend agent by itself.

**No environment clipping:** every character station, seat, hallway, elevator landing and doorway has a local `AgentPose` with collision radius and head clearance. Automated bounds validation must detect overlap of agent capsules with desk/floor/wall. Walk routes live inside the habitat's local graph. Disable collision with decorative static furniture when it would cause animation jitter, but never use that to conceal visible clipping. Use per-agent motion reservation for shared corridors and elevator.

## 5. Local multi-level movement / elevators

An elevator is a narrative representation of agent coordination, not a source of backend state. Add a local `HabitatNavGraph` with nodes (`workstation`, `door`, `elevator queue`, `elevator inside`, `landing`, `meeting nook`, `manager desk`, `delivery platform`) and connections with clearance metadata. `AgentVisualChoreographyService` derives a desired route from genuine backend handoff/meeting/delegation events, then animates the agent locally. The agent's logical task does not wait for physical travel.

### Elevator lifecycle
`IdleAtFloor -> Reserved -> DoorsOpening -> Boarding -> DoorsClosing -> Traveling -> Arrived -> DoorsOpening -> Exiting -> ReleasingReservation -> IdleAtFloor` with `Interrupted/Recovering` paths. Only one rider per car in v1. Reserve elevator before approaching; queue others fairly and coalesce redundant requests. Use root-relative waypoints, not hard-coded room transforms. Cabin vertical travel follows actual landing socket heights (not baked animation distance); door/open/close clips are local. Workstations and elevators must share a coherent navigation scale. Optional floor labels show destination with icon/role color.

### Visual handoff examples
- Researcher finishes blueprint -> walks to elevator -> rides to Manager -> briefly shows blueprint -> returns or routes to Developer -> task handoff has already been confirmed by backend event.
- Manager delegates -> prints task cards -> tiny conveyor moves them to agents -> agents receive tasks and start appropriate work animations only upon authoritative assignment.
- Tester finds failure -> brings warning ticket to Manager -> Manager receives it, status updates, visual return event.

### Reliability
Event-to-animation queue with deadlines: too many events must never trap agents in perpetual travel. Interrupted animations re-home at valid sockets. Backend state remains authoritative; do not manufacture messages to justify motion. Avoid path crossing through cubicle geometry; if no valid path, use a gentle, explicitly visual transfer animation rather than walking through walls.

## 6. Temporary agent visit — `/btw` side conversation

User presses/chooses **Select Agents** from a small contextual control. Agent silhouettes gain a soft selectable outline; a ray/grab indicator appears only on valid avatars. Far-hand distance grab must take priority below an already-held object, paper, stamp or card; otherwise it is an explicit opt-in action. Selection mode auto-exits on timeout or confirmed selection.

### Two consistent presentation semantics
**Recommended:** the user retrieves a **conversation projection**, not the only working character. The home avatar stays at its workstation representing ongoing work, while a physically picked-up, softly marked miniature projection is spawned (or visually transferred) for the conversation. It uses the same agent ID and reads the same task state. It does NOT run a second autonomous process. Explicitly distinguish the projection via a subtle ring/shine; avoid making the world look like there are two fully independent workers. Alternative: temporarily hide the home visual and preserve a glowing occupied workstation; never clone task execution.

### Interaction lifecycle
`Home -> Selecting -> RayGrabbed -> BeingCarried -> PlacedOnVisitPerch -> Conversing -> Returning -> Home`, plus `Cancelled`, `TrackingLost`, and `WorkspaceRelocated`. Ray grab follows SDK-supported distance-grab behavior, not a new tracking pipeline. At grab, spawn or detach a lightweight visual projection and hold it at a manageable apparent size; do not yank an active agent rig from its AI work controller. On release, evaluate a user-confirmed **Desk Visit Zone** or an explicitly placed small perch; snap gently and lock root before talking. Invalid drop -> hover to stable position or return to home; tracking loss -> cancel, never commit. A valid visit shows concise question box, mic availability, Done/Return action.

### `/btw` semantics
- Uses `agent_id`, `task_id`, `conversation_session_id`, and latest confirmed task context; may summarize, explain, compare alternatives, report progress and identify next steps.
- **Read-only by default**: no code writes, Git changes, task cancellations, secret retrieval, new tools, workflow resumption, or task mutations.
- Questions are routed to a dedicated side-channel adapter with strict tool allowlist and deadline. Main worker continues (asynchronously); never clone the running worker process, reuse its mutable chat transcript without isolation, or block its task loop.
- To request a change, convert the instruction to a **proposed task amendment** with a review and explicit confirmation through the existing approval/task system.
- Clearly label scripted/demo answers; if model is unavailable, show a grounded snapshot of task state and fail gracefully. Never invent live status or provenance.
- Maintain short, ephemeral history for the visit (e.g., last 5–10 exchanges) and allow dismissal/clear; protect sensitive project content as appropriate.
- Voice and direct hand UI are alternatives; critical visit interactions must work without internet or voice.
- Maximum one active user visit per agent, and a bounded global number (initially one). Additional requests queue or explain why unavailable.

### Invariants
1. Task remains same state before/after purely presentational visit.
2. No concurrent duplicate backend worker created.
3. Any change request produces a separate validated action, not implicit task modification.
4. On app pause/recenter/room change, the visit closes or stabilizes safely without losing task state.
5. Portal and mini desk-visit sessions are mutually exclusive per agent.

## 7. Manager Agent — product, orchestration & trust

A new Manager role occupies an elevated corner office; the role is an actual backend orchestration participant, not only a decorative mascot. Reuse the existing worker model/registry, task board and approval lifecycle. Implement additive `manager` role and orchestration adapter only after auditing current agent coordinator.

### User experience
User approaches, summons, or ray-selects Manager and says: “Build the login flow, use the existing repo, and have Tester verify it.” Manager responds with a compact **plan card** listing steps, proposed owners, dependencies, assumptions, and risk. User can inspect/approve the plan (or choose safe auto-delegation within permissions). Upon backend confirmation, real task cards emerge from a miniature printer on the manager deck and travel to Researcher/Developer/Tester, with visually distinct ownership and progress. The Manager can provide concise summaries and blocker triage without taking over the user's whole workspace.

### Manager scope and permissions
- Read project/task snapshot and capabilities; propose DAG of work items using structured JSON.
- Validate against allowed agent roles, task types, permissions, dependencies, worktree isolation and resource limits.
- Detect dangerous operations (deployment, deletion, spending, access to credentials, network permissions) and route to explicit user authorization.
- Create tasks atomically/idempotently only after validation; do not create dummy “in progress” cards that are not backed by the real task system.
- Assign through the same task-board API; owner and state update only after backend ACK.
- Respect global concurrency caps, worker capabilities, task budgets/timeouts and dependencies.
- On failure/ambiguous intent, ask for clarification or produce a non-executing proposed plan.
- Manager may recommend reassignments, but cannot silently cancel running work, overwrite working trees, change credentials or self-approve decisions.
- Summaries from actual logs/test results; report uncertain/unsupported work clearly.
- Manager is not the actual code executor by default; existing Developer/Tester/Researcher remain responsible for execution.

### Minimal data model
`ManagerPlan {plan_id, source_request_id, title, created_at, provenance, assumptions[], items:[{client_item_id, title, kind, proposed_agent, depends_on[], acceptance_criteria[], required_permission}], status: proposed|approved|rejected|committed, revision}`.
`CommitManagerPlan` takes plan ID, revision, approved item set, idempotency key. Return canonical `task_id`s, actual assigned owner/status, errors. Task card animation plays only for confirmed tasks. Task edits route through the original task service. Do not trust model output as pre-validated executable commands.

### Modular manager work area
Manager stays one level above ordinary cubicles as an architectural/visual hierarchy, but its position must not imply unrestricted authority. Plan board, event printer, folder slots, optional miniature rotating status indicator. Its desk should offer a simple physically inspectable plan receipt. Have a clear way to summon Manager to the user for a side discussion.

## 8. Large-agent portal / scale-up conversation

A user selects an agent, then chooses **Meet Larger**. System finds a genuinely clear visible space beside the user (not on top of desk/monitor) and previews a ring/threshold. The user explicitly confirms. The selected agent is represented at approximately 0.9–1.2m tall (configurable, capped for comfort), with consistent mascot proportions and no unrealistic clipping. It can look at user, talk, gesture, point at an expandable diagram/report and return via portal. The main work continues uninterrupted.

### Rendering / animation semantics
Use a separate **presentation rig instance** (visual proxy, not duplicated logical agent) with its own transform/scaling/animator under a portal interaction root. Source rig/avatar from existing art; choose safe rig retargeting and material variants. Home visual should show an unambiguous “on visit” marker or subtle afterimage rather than an independent active worker. Only one primary interactive proxy per logical agent. Prefer one enlarged visit at a time to protect frame rate. Scale gameplay/world props independently; never globally scale the Unity XR Origin or task UI to achieve larger avatar.

### Portal placement
Reuse room geometry abstraction: candidate area on floor beside user for full body; evaluate 2D footprint, height clearance, seating, walls and furniture where known; ensure user's head/monitor not blocked. If real room data is unavailable, offer a manual preview and require confirmation, or disable portal with a helpful message. Do not place a 1.2m tall figure in an unvalidated narrow gap. Portal should be an elegant outline with sparse particles, no full-screen VFX, aggressive strobing, or opaque planes blocking passthrough. In room geometry absence allow a smaller upper-body stationary avatar within reach only if manually confirmed and safe in the UI sense.

### Portal sequence
`Hidden -> Preview -> Confirmed -> Opening -> Emerging -> Conversing -> Returning -> Closing -> Hidden` plus Cancel/Recover. Animate local emerge steps; world translation based on validated poses. No walk through real furniture, walls, user's body or headset. Test full swept capsule, not only landing position. On interruption, hide / restore proxy and free visual reservation, preserve backend session. Distinct short audio cue and soft material shimmer. Portal visibility respects focus mode / user preferences.

### Conversation and scale interactions
Manager/worker can answer read-only `/btw`, inspect relevant tasks and submit separate proposed changes. Large agent must not require users to walk around; seated interaction works from a side vantage. Dialogue text appears in legible near-user panel, not printed over the giant avatar or hidden behind its body.

## 9. Selectability, controls, attention, comfort

### Minimal interface
Idle wall habitat shows three worker cubicles, Manager lookout, elevator, beacon and a compact management dock. **Select Agents** is one subtle contextual button/affordance. When toggled, eligible agents highlight, selection ray has correct affordances, and a helper text says “Pinch to bring an agent over.” After selection, show **Place on desk**, **Meet Larger**, **Ask**, **Return** actions contextually, not all as permanent physical buttons.

### Interaction priority
Already-held item > near grab/poke > active Select-Agent proxy > explicit placement handle > targeted summon > general gaze. Separate `Select Agents` from workspace relocation and ceiling pull. Grabbing paper during a selection session should have clear priority; losing tracking cancels active motion and never confirms release/placement.

### Attention events
Worker asks approval: amber document icon; optional suggestion only if real backend produces it; completion: check; blocked/error: warning; Manager proposal ready: plan icon; Manager needs explicit permission: lock/document icon. No fake optional events; focus mode queues nonurgent updates. Avoid duplicate beacons and overlapping sounds across modes.

### Comfort
Comfortable seated reach; no forced upward arm movement; far targeting through standard SDK. Avoid life-size avatar in user's near personal space or overlapping screen. Offer motion reduction, effects disable, smaller/closer portal, and avatar reset. No constant audio chatter. Default portal never opens automatically on an incoming approval; user explicitly summons it.

## 10. Proposed runtime architecture

```
FastAPI Backend (authoritative)
  Existing Task Service & Agent Workers
  ManagerPlanService (+ optional manager model adapter)
  SideConversationService (read-only `/btw`)
  Existing Approval/Artifact/Event Services
                 | existing websocket + additive endpoints/events
Quest Unity
  Existing Session/Reducer/AgentRegistry
  Existing SpatialWorkspaceDirector (add Wall mode)
    WallPlacementEvaluator + WallHabitatRoot
    HabitatLayoutAssembler + ModuleSocketRegistry
    HabitatNavGraph + ElevatorQueue/Controller
    AgentVisualChoreographyService
    InteractionArbiter + AgentSelectionMode
    AgentVisitPresentationController (small desk visit)
    PortalPlacementService + LargeAgentPortalController
    ManagerDeskVisualController + TaskCardPrinterVisual
    Attention/Focus/Document Delivery adapters (reuse)
```

### Separation of concerns
- **Canonical state**: one backend task/agent; system of record for plan and decisions.
- **Visual home**: animated representative in the habitat. May be idle/visiting, but never executable agent instance.
- **Presentation proxy**: temporary close-up or enlarged avatar for conversational UX. Does not own task state.
- **Motion director**: single writer for each actor's root transform; rig animation drives local bones. Transitions can be cancelled and safely re-homed.
- **Room placement service**: candidate generation, preview, confirmation, anchor persistence; no task authority.
- **Communication**: backend events drive UI; queue/debounce/sequence events to avoid visual races.

### Suggested interfaces (adapt to actual code)
```
interface IWorkspaceModule { string SocketId; Bounds LocalBounds; }
interface IHabitatPlacementEvaluator { Candidate[] Evaluate(RoomSnapshot room, HabitatSpec spec, Pose user); }
interface IAgentPresentation { string AgentId; VisitState State; void BeginVisit(...); void EndVisit(...); }
interface IAgentVisualMotion { bool CanRoute(RouteRequest); void Enqueue(RouteRequest); void RecoverHome(); }
interface ISideConversationGateway { Task<SideReply> AskAsync(SideQuestion request, CancellationToken ct); }
interface IManagerGateway { Task<ManagerPlan> ProposeAsync(...); Task<CommitResult> CommitAsync(...); }
```
Do not create new abstractions where existing project interfaces already fulfill these roles.

## 11. Contracts and data safety

Use versioned additive messages. Suggested names (inspect current serializer/casing conventions before adoption):

```json
{
  "event": "manager.plan_proposed",
  "event_id": "ev-001",
  "plan_id": "mp-001",
  "revision": 1,
  "title": "Build authentication",
  "provenance": "LIVE",
  "items": [{"client_item_id":"a","title":"Investigate auth options","proposed_agent":"researcher","depends_on":[],"kind":"research"}, {"client_item_id":"b","title":"Implement auth","proposed_agent":"developer","depends_on":["a"],"kind":"code"}, {"client_item_id":"c","title":"Test auth","proposed_agent":"tester","depends_on":["b"],"kind":"test"}]
}
```

```json
{
  "event":"agent.side_question",
  "request_id":"sq-002",
  "agent_id":"developer",
  "task_id":"task-042",
  "session_id":"visit-012",
  "question":"Why is OAuth preferred?",
  "read_only":true,
  "origin":"wall-desk-visit"
}
```

```json
{
  "event":"spatial.presentation_changed",
  "agent_id":"developer",
  "presentation":"large_portal",
  "visit_id":"visit-012",
  "placement_pose_id":"local-anchor-003",
  "visual_only":true
}
```

Do NOT send spatial presentation events as task commands. These may be local UI events only unless user expects session synchronization across devices. Manager commits need idempotency and revision checking. Side conversation must not call mutate tools or modify the main agent prompt. Safeguard privacy, source-file access, and network permissions.

## 12. Data-driven state machines

**Wall placement:** `Unplaced -> Querying -> Preview -> Validating -> AwaitingConfirmation -> Anchoring -> Active -> Relocating -> Active`, with `Error/DeskFallback`.

**Agent selection:** `Off -> Targeting -> GrabbedProxy -> EvaluatingDrop -> DeskVisit/PortalChoice -> Ended` with `Cancel/TrackingLost`.

**Desk visit:** `Home -> Projecting -> HoverCarried -> AnchoredPerch -> Conversing -> Returning -> Home`.

**Manager plan:** `RequestReceived -> Proposing -> PlanReady -> AwaitingApproval -> Committing -> TasksCreated -> Delegating -> Monitoring`, with `Rejected/Failed` branches.

**Portal:** `Unavailable -> PlacementPreview -> Confirmed -> Opening -> Entering -> Conversing -> Exiting -> Closing -> Ready`.

**Elevator:** as described in §5. Event handlers must be idempotent and resistant to duplicate, late or out-of-order updates. Animations are allowed to lag authoritative state but never falsely claim success.

## 13. Tests & acceptance gates

### Automated
- Room wall candidate scoring: windows/doors/furniture/short wall/no room data/rotated room/anchor loss.
- Wall world-to-local transform math; correct outward facing; persistence and reset.
- Habitat modular socket invariants: unique IDs, bounds, no cubicle overlaps, elevator landings valid, path graph connected.
- Elevator queue, cancellation, reservations, failed animation recovery, zero backend state mutation.
- Selection arbiter: paper grabbing beats agent select; lost tracking cancels; invalid drop home; agent not selectable outside mode.
- Desk visit: one canonical agent; side question read-only; no task state mutation; close/recenter safe.
- Manager: DAG cycle detection, unsupported role, dangerous action approval, duplicate commit, stale revision, create card only on ACK, idempotency.
- Portal: clear pose check, swept capsule, cancellation and restoring presentation state, scale bounds, no duplicate workers.
- Shared regressions across Desk/Floor/Ceiling/Wall, existing approval/documents, demo provenance and focus.

### Unity Play Mode
- Wall candidate ghost and confirmation.
- Modular 2-level and 3-level layout; simulate task handoff and elevator journey.
- Select Developer from far wall, bring small avatar to desk, ask read-only question, return; Developer's task unchanged.
- Manager proposes a multi-task plan; approval creates actual board cards; agent visual handoffs match confirmed assignments.
- Portal creates enlarged Developer in clear region; talk and dismiss; original work continues.
- Artificial obstruction blocks portal or habitat placement and explains the reason.

### Quest human-worn gates (do not claim passed from Editor)
1. Wall is recognized and preview aligns with actual wall; original Desk mode still works.
2. No obvious intersection with furniture/door/window or real monitor; visual anchoring stable while head moves.
3. User can place, cancel, move and restore habitat with hands only.
4. Elevator clearly shows agent entering/exiting rather than clipping through walls, floors or desk.
5. Far hand select is discoverable and no accidental near-object activation occurs.
6. Drag a temporary agent onto a desk/perch; no hidden backend task restart; avatar returns safely.
7. Side conversation actually responds to real current task or honestly indicates unavailability.
8. Manager plan proposed and committed through real task service, with correct task cards.
9. Portal preview, space clearance, emerge/dismiss tested from seated posture.
10. Stable frame rate, comfortable scale/readability, no excessive glow, no visual jitter.

**Performance targets:** profile on target Quest hardware; stable frame time at configured refresh rate, preferably 72 Hz where supported; test wall elevator, manager plan cards printing and enlarged portal concurrently. Prefer only one enlarged avatar; use distance-based animation LOD, renderer/particle toggles. No fabricated benchmark claims.

## 14. Phases, dependencies & go/no-go

### Phase 0: protect baseline
Freeze and tag known working release. Create isolated Wall Habitat branch; keep feature flag OFF in competition APK until validated. Audit actual spatial director, MRUK, Interaction SDK, agent registry, backend capabilities. Establish demo fixtures.

### Phase 1: art/test harness
Antigravity delivers modular habitat, elevator, manager concept/rig/props, portal ring and enlarged avatar skinning tests, visit perch and indicators. Claude builds synthetic wall fixture, socket contract validator, local nav graph and placement evaluator against simple placeholder meshes.

### Phase 2: wall mode
Integrate real art; wall placement/preview/confirm, anchoring/persistence/fallback. All 3 existing workers can remain at wall; elevator runs a synthetic visual coordination test. No manager backend yet.

### Phase 3: `/btw` visits
Add Select Agents, ray-grab projection, desk placement, read-only side question, Return and tracking-loss recovery. Real main task continues, provably unchanged.

### Phase 4: Manager
Add fourth role and visual manager office; propose/validate/approve/commit plan as actual task cards. Confirm dependencies and backpressure; visual delegation animation does not gate execution.

### Phase 5: portal
Add validated clear-space preview and larger projection, animation, conversation and restoration. Disabled by default until Quest-verified.

### Phase 6: hardware polish + deployment
Tune scale, hand gestures, navigation, animation, text, audio, frame time, fallback. Produce before/after footage; enable only tested features.

**Competition constraint:** as of Nov 14, prioritize the existing Desk/Floor/Ceiling release gates and submission. Consider previewing Wall Habitat via concept art/experimental branch only; do not show experimental footage as if it were a verified shipped feature. The complete Wall Habitat should not crowd out final video, APK distribution, and honest provenance checks.

## 15. Deliverables

**Antigravity:** Blender sources, FBX, animation clips, Unity-ready art prefabs and preview scene, art style sheet, original Manager model/rig, dynamic asset manifest with socket coordinates, no changes to production scene/backend. Must verify actual models and visual previews.

**Claude:** merged feature branch after art approval, mode-specific Unity components, room placement, modular habitat composer, elevator/nav, selection/projection, side conversation and manager backend integration, portal placement/presentation, test fixtures, performance report, Quest hardware checklist, release-flag decision.

**Shared handoff:** exact socket names, transforms, animation IDs, material/renderer state hooks, collider layers and scale assumptions; no silent naming drift. Use versioned manifest and automated validation.

## 16. Acceptance summary — definition of done

1. Wall placement fits and anchors to a recognized/user-confirmed surface; no unavoidable real-world clipping; fallback works.
2. Treehouse looks visually coherent; modular levels and elevator are functional and grounded by actual events.
3. User can select and carry a temporary agent onto a desk; conversation is contextual, read-only by default, and non-disruptive.
4. Manager can produce a validated plan whose approved items become real task cards and actual delegated work.
5. Enlarged portal agent is visually compelling, uses a safe confirmed free location, and returns without changing main task.
6. Full app remains functional in Desk/Floor/Ceiling; no fake execution or duplicated logical agents.
7. Test evidence clearly distinguishes automated, Editor, APK install and human-worn proof.
8. Feature enabled only after actual Quest headset performance and interaction validation.
