# P2-99 — Integration, Quest QA and release gate

**Owner boundary:** Main Unity scene + release validation  
**Priority:** Continuous gate  
**Dependencies:** All completed tracks  
**Reference:** `MASTER_PHASE2_IMPLEMENTATION.md` and frozen `contracts/phase2/`.

## Goal

Prefab wiring, all flags, Phase 1 regression, live/offline demo playback, headset profiling, rollback. This is an implementation assignment, not permission to change Phase 1 behavior, add unreviewed packages, or bypass the approval gate.

## Preflight

1. Read original `AGENTS.md` plus `AGENTS_PHASE2.md`; inspect the real repository and feature versions.
2. Confirm relevant Phase 1 services compile and existing tests pass; do not assume specifications perfectly match current code.
3. Identify your owned files; request an integration owner for changes to main scene, package manifest or shared v2 schema.
4. Build against P2-00's single authoritative DTOs and event names; log any proposed incompatibility before coding.

## Implementation deliverables

- Implement prefab wiring, all flags, phase 1 regression, live/offline demo playback, headset profiling, rollback.
- Write automated tests for the happy path, one failure, a duplicate/replay event, and serialization of DTOs.
- Implement a scripted-demo equivalent behind `source_mode=demo`, with truthful visual labels and same UI path.
- Keep feature behind its `p2_...` flag and provide integration wiring notes (prefab/component name and intended scene parent).
- Compile/check in the actual project, and document Quest manual steps. If you cannot test on Quest, report that explicitly.

## Exit criterion

Complete hands-first scenario in both modes on Quest 3; no Phase 1 regressions, measured performance, test evidence.

## Fault injection

- Trigger session reconnect immediately after command or event; expected result: no extra tool execution or duplicate UI object.
- Try invalid/missing payload fields; expected result: validation error and safe fallback, not crash.
- Turn off network or switch to scripted demo; expected result: preserved interactive affordance and truthful source label.
- Enable reduced-motion; expected result: functionality remains accessible without relying on animation completion.

## Do not do

- Never permit a Unity collider/animation handler to modify authoritative task or decision state directly.
- Never expose workstation absolute paths, secrets, raw logs or room meshes to the headset.
- Never declare acceptance based on screenshots alone; backend task ID and tests must confirm the corresponding state.
- Never overwrite the root scene, original Phase 1 prefabs, or v1 contract without approved integration plan.

## Required PR handoff

Report: files changed, exact command(s) to run, test results, known limitations, source mode behavior, schema/API changes, approval safety, and a 30-second Quest test plan.

## Feature-specific step-by-step plan

### Ordered implementation checklist

1. Merge modules in priority order, keeping a v1 baseline branch and feature flags to allow safe rollback.
2. Use Unity Editor automation (e.g. Unity CLI/Pipeline with current Editor) to create/wire prefabs and scene components; never leave broken serialized references or duplicate XR rigs.
3. Build and deploy actual Quest APK; verify hands-only operation, real passthrough, scene fallback, focus, reconnect, artifact pagination and reading comfort.
4. Confirm backend worker sandbox and approval gate cannot be bypassed by task assignment or couriers.
5. Disconnect all external services, launch standalone demo, and complete all permitted interactions under permanent `SCRIPTED DEMO` label.
6. Capture a short user journey with real task IDs/artifact evidence, Quest FPS logs, and known limitations; disable any unfinished feature.

### Acceptance detail

Existing Phase 1 task-approval workflow still runs without change after merged build. Full Phase 2 minimum path: assign queued card → researcher decision → approval → developer handoff → real diff → tester results → factory outcome, both live and scripted modes.
