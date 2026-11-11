# Mandatory instructions for implementation agents

You are implementing **MuseDesk**, a Quest 3 Unity mixed-reality human-in-the-loop agent supervision experience. Read `MASTER_IMPLEMENTATION_PLAN.md`, `docs/API_AND_EVENT_CONTRACT.md`, the JSON schemas, and your assigned `tasks/TASK-XX_*.md` before changing code.

## Absolute invariants

1. **Never misrepresent scripted demo activity as live coding/research.** Every state/artifact has `source_mode` and the visible app labels mode.
2. **Backend authorizes task execution.** Unity animation/pressing a local button is not proof an action is approved. Wait for acknowledged backend state before displaying a confirmed decision.
3. **No unsafe PC access.** Tool execution remains in isolated sample workspace and a narrow allowlist. No arbitrary shell, deletion outside root, credential access, deployment, or payment actions.
4. **One schema.** Do not rename JSON fields or change event enums without coordinated updates to `contracts/`, backend and Unity model/fixtures/tests.
5. **One XR rig.** Do not add a duplicate OVRCameraRig, ordinary Main Camera, or a second interaction framework to scene.
6. **No random package upgrades.** Use locked Unity Editor and `Packages/packages-lock.json`; update dependencies only in designated integration PR.
7. **Hands-first.** All P0 flows must be operable without controllers or voice.
8. **Judges need standalone use.** Every P0 UI path must function on Quest without developer's PC in clearly labeled demo mode.
9. **Test on actual Quest.** Editor simulator tests are not proof of passthrough, hands, performance, or judge readiness.
10. **No invented SDK APIs.** Use installed Meta XR package docs/Meta VR CLI, compile, and verify. If an API differs from spec, document compatibility notes.

## Ownership rules

- XR foundation agent owns root `MainMRScene.unity`, rig, packages and project settings. No other agent edits these without explicit coordination.
- Interaction agent owns `DecisionPaper`, `DocumentInteractionController`, `DecisionPanel` and associated prefabs; integrates via neutral interfaces.
- Character agent owns mascot/station prefabs and agent animation C# scripts, not central scene.
- Backend API agent owns routes, storage and `contracts/` after coordinated schema freeze.
- Orchestrator agent owns workers, sandbox and workflow state machine; cannot bypass approval gate.
- QA/integration agent owns gateway wiring, smoke-test scripts, release assembly and test evidence.

## Commit/PR format

Each deliverable must have:

- `Summary`: what works now (not intentions).
- `Files changed`: list and module ownership.
- `How to run`: exact machine/Quest steps.
- `Tests`: results and manual verification; identify what was not run.
- `Known limitations`: concrete not vague.
- `Contract changes`: explicit none or the coordinated schema migration.
- Screenshots/short Quest capture where relevant.

Never claim complete until your specific ticket's acceptance criteria pass. If blocked, submit a smaller compiled/verified slice and a concise blocker report rather than inventing completion.

## Approved implementation order

P0 events first → document lifecycle → real approval transaction → artifact result → standalone offline route → three-agent animation polish → voice/extra gestures.
