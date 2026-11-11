# P2-07 — Truthful miniature code factory

**Owner boundary:** Unity props/animation with existing events  
**Priority:** Polish after core  
**Dependencies:** P2-00, P2-02, P2-03  
**Reference:** `MASTER_PHASE2_IMPLEMENTATION.md` and frozen `contracts/phase2/`.

## Goal

Blueprint bench/code bench/test rig/output shelf prefabs, pooled visual state machine, failure/recovery path. This is an implementation assignment, not permission to change Phase 1 behavior, add unreviewed packages, or bypass the approval gate.

## Preflight

1. Read original `AGENTS.md` plus `AGENTS_PHASE2.md`; inspect the real repository and feature versions.
2. Confirm relevant Phase 1 services compile and existing tests pass; do not assume specifications perfectly match current code.
3. Identify your owned files; request an integration owner for changes to main scene, package manifest or shared v2 schema.
4. Build against P2-00's single authoritative DTOs and event names; log any proposed incompatibility before coding.

## Implementation deliverables

- Implement blueprint bench/code bench/test rig/output shelf prefabs, pooled visual state machine, failure/recovery path.
- Write automated tests for the happy path, one failure, a duplicate/replay event, and serialization of DTOs.
- Implement a scripted-demo equivalent behind `source_mode=demo`, with truthful visual labels and same UI path.
- Keep feature behind its `p2_...` flag and provide integration wiring notes (prefab/component name and intended scene parent).
- Compile/check in the actual project, and document Quest manual steps. If you cannot test on Quest, report that explicitly.

## Exit criterion

Test rig green only on verified passing test artifact; rerun/reconnect does not duplicate business events.

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

1. Make low-poly `BlueprintBench`, `CodeBench`, `TestRig`, `OutputShelf` station prefabs sharing materials and draw calls.
2. Map canonical task/artifact states to six pure `FactoryVisualStateMachine` statuses. Split data projection from animation scheduler so presentation cannot change task or test outcome.
3. Create conveyor/courier path visuals as short waypoints within desk bounds. Add `Skip animation`, motion reduction and safe resume from fresh snapshot.
4. Bind `tests_failed` to a red test rig + failed test artifact and return handoff; bind `tests_passed` to green output shelf only after stored passing test report. Ensure rerun gives a new attempt ID.
5. Each visual proxy exposes `Inspect` linking to real task/artifact panel and clear live/demo label.

### Acceptance detail

Simulate failed run, fix, successful rerun: red appears first, then green only after passing test; no duplicate artifacts after reconnect. Turn animations OFF and the factory remains understandable via status labels.
