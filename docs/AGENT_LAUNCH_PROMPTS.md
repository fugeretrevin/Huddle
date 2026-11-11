# Copy-paste coding agent launch prompts and coordination workflow

Use one prompt per agent working in separate Git branches or separate local worktrees. Always attach this whole plan directory/repository to the coding agent. The agents are IMPLEMENTERS of the project, not the three characters inside it.

## Team lead: required starting instruction

> You are engineering MuseDesk, a Quest 3 mixed-reality AI coworker application. Before coding, read `AGENTS.md`, `MASTER_IMPLEMENTATION_PLAN.md`, `docs/API_AND_EVENT_CONTRACT.md`, `contracts/*.schema.json` and your assigned task file. Respect module ownership, pinned Meta XR packages, hands-first requirements, explicit offline demo labels, and backend approval gating. Work only in your assigned files and branch. Implement the smallest working vertical slice, compile/test, document results and uncertainty. Do not claim a Quest test you did not run.

## Prompt A — XR foundation

> Implement `tasks/TASK-01_XR_FOUNDATION.md`. Own only the Unity project configuration, XR rig, passthrough, MRUK placement, root scene, and platform build setup. Use current Meta official docs/Meta VR CLI instead of guessing APIs. In the first PR provide a clean Quest APK running passthrough with working hands and a grabbable object. Preserve `Packages/packages-lock.json` and Unity `.meta` files. Provide exact deploy steps, Quest screenshots, errors, and any headset permission requirements. If Quest hardware is unavailable, say which parts are unverified.

## Prompt B — Decision documents

> Implement `tasks/TASK-02_DOCUMENTS_AND_HAND_UX.md`. Own document interaction prefabs and scripts only. Develop against a mocked `IAgentGateway`, using fields from the frozen event contract. Implement offer, grab, expand, contextual text question, approve/reject/request-changes, pending/acknowledged/error states and idempotency. Do not modify XR root rig or backend. Report device hand-grab success results and visibility checks. Do not portray a local button animation as confirmed approval.

## Prompt C — Characters and stations

> Implement `tasks/TASK-03_CHARACTERS_AND_STATIONS.md`. Own role-specific character and workstation prefabs and animation scripts, not the central Unity scene. Use small, cohesive, licensed assets. Implement state-dependent idle/research/coding/test/waiting/complete animations and one researcher document delivery path. Every work animation must be triggered by a reported task state rather than arbitrary time. Provide a low-overhead Quest-ready visual proof and station anchor hookup instructions.

## Prompt D — FastAPI protocol

> Implement `tasks/TASK-04_API_AND_PERSISTENCE.md`. Own backend REST/WS routes, storage, contracts and test harness. Build a typed FastAPI service with per-session auth, SQLite durable events, monotonic sequence IDs, snapshot/replay, idempotent approvals and correct HTTP conflict handling. Freeze JSON field names across the client and backend. Start with a deterministic fake producer to unblock the XR team, and provide curl tests, pytest results and a documented launch command. No unrestricted command execution.

## Prompt E — Actual workers and gate

> Implement `tasks/TASK-05_WORKERS_ORCHESTRATOR.md`. Use `docs/API_AND_EVENT_CONTRACT.md` and integrate with backend APIs without changing public payloads. Build three truthful worker roles in a disposable sample repo; ensure at least two independent tasks actually run concurrently. The research agent must pause a dependent step via persisted approval gate, and valid approve/reject choices must produce distinct real effects, diffs and test results. Provide worker timeout, path-sandbox, allowlist, failure and duplicate-decision tests. Never modify personal project files outside an explicitly selected workspace.

## Prompt F — Voice (only after P0 is done)

> Implement `tasks/TASK-06_CONTEXTUAL_VOICE_OPTIONAL.md`. Add a push-to-talk microphone flow with permission prompt, short transcription, contextual question binding and optional TTS/captions. Preserve a fully working hands-first text question alternative. Never embed API keys on Quest or interpret voice automatically as approval. Do not destabilize already working core UX.

## Prompt G — Integration and offline judge mode

> Implement `tasks/TASK-07_INTEGRATION_AND_QA.md`. Own the two `IAgentGateway` implementations, event reducer, connection/replay logic and offline demonstration fixture. Produce a Quest APK that runs the entire labeled simulated workflow without connecting to the developer PC; also demonstrate a real PC-connected task and artifact. Run the specified hand-only, permission-denied, disconnect, duplicate approval and FPS tests. Provide a reproducible release candidate checklist.

## Prompt H — Submission

> Implement `tasks/TASK-08_SUBMISSION_AND_DEMO.md`. Verify Meta VR Start 2026 rules against official sources. Prepare the `Competition` release channel, test that a judge can install/play the APK without access to the team's PC, record under-three-minute truthful Quest footage, and draft Devpost description with links and screenshots. Do not call scripted results real background coding. Publish before Nov 18, 2026 noon Pacific.

## Parallelization recipe (4 implementers)

**Day 1:** A builds Quest, D freezes schema and serves simple event, C builds prefabs independently, project lead does account/device/channel setup. B begins against mocked gateway after A provides prefab/hand rig interface.

**Day 2–3:** A/B integrate first paper; D/E integrate approval and sample worker; C adds delivery. Gate: Quest gesture sends approved decision and server resumes task.

**Day 4–5:** G integrates replay, completion artifact and fully independent offline demo. Others fix bugs and assist with evidence.

**Day 6–7:** F only if P0 stable; H and G prepare release, clean-install test and video. Freeze features.

## Handoff and merge cadence

At each integration checkpoint, require: branch/commit SHA; files owned; compiled/tested evidence; exact JSON and endpoints; screenshot or Quest video where appropriate; known issues; concrete API changes (prefer none). Merge one Unity scene owner at a time. Never run two coding agents writing into the same checkout while the Unity Editor is open; use branches/worktrees and a single integrator.
