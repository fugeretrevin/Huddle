# P2-04 — Template-based 3D architecture diagrams

**Owner boundary:** ExplanationService + Unity diagram renderer  
**Priority:** Stretch  
**Dependencies:** P2-00, P2-02  
**Reference:** `MASTER_PHASE2_IMPLEMENTATION.md` and frozen `contracts/phase2/`.

## Goal

Three diagram templates with schema limits, evidence links, pooled nodes/edges and text fallback. This is an implementation assignment, not permission to change Phase 1 behavior, add unreviewed packages, or bypass the approval gate.

## Preflight

1. Read original `AGENTS.md` plus `AGENTS_PHASE2.md`; inspect the real repository and feature versions.
2. Confirm relevant Phase 1 services compile and existing tests pass; do not assume specifications perfectly match current code.
3. Identify your owned files; request an integration owner for changes to main scene, package manifest or shared v2 schema.
4. Build against P2-00's single authoritative DTOs and event names; log any proposed incompatibility before coding.

## Implementation deliverables

- Implement three diagram templates with schema limits, evidence links, pooled nodes/edges and text fallback.
- Write automated tests for the happy path, one failure, a duplicate/replay event, and serialization of DTOs.
- Implement a scripted-demo equivalent behind `source_mode=demo`, with truthful visual labels and same UI path.
- Keep feature behind its `p2_...` flag and provide integration wiring notes (prefab/component name and intended scene parent).
- Compile/check in the actual project, and document Quest manual steps. If you cannot test on Quest, report that explicitly.

## Exit criterion

Known task DAG displays with correct nodes/edges and no arbitrary scene commands; malformed graph rejected.

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

1. Backend accepts `auth_flow`, `task_dependency_graph`, and `module_dependency_graph` only. Convert known sample workflow to typed graph; reject untrusted scene commands and arbitrary code.
2. Validate 12-node/18-edge max, allowed icon enum, existing endpoint IDs, graph depth and task-DAG constraints before persisting or serving.
3. Add deterministic graph layout using simple level layers for task DAG or a fixed three-column preset for authentication; do not require runtime generative mesh creation.
4. Create pooled `DiagramNodeView`/`DiagramEdgeView` with pinch select, evidence panel and bounded global scale; fallback to text if malformed.
5. Wire `Show architecture` from inspectable artifact; ensure diagram displays live/demo and evidence source clearly.

### Acceptance detail

Test malformed JSON, unknown edge endpoint, unbounded text, overly large graph, and unsupported template. Assert neither Unity nor backend executes user-provided `instructions` fields or external URLs as commands.
