# P2-05 — Scene-aware anchored desk workstations

**Owner boundary:** Unity layout only  
**Priority:** Optional  
**Dependencies:** P2-00  
**Reference:** `MASTER_PHASE2_IMPLEMENTATION.md` and frozen `contracts/phase2/`.

## Goal

MRUK placement adapter, manual safe fallback, station-relative poses, saved local layout, decor milestones. This is an implementation assignment, not permission to change Phase 1 behavior, add unreviewed packages, or bypass the approval gate.

## Preflight

1. Read original `AGENTS.md` plus `AGENTS_PHASE2.md`; inspect the real repository and feature versions.
2. Confirm relevant Phase 1 services compile and existing tests pass; do not assume specifications perfectly match current code.
3. Identify your owned files; request an integration owner for changes to main scene, package manifest or shared v2 schema.
4. Build against P2-00's single authoritative DTOs and event names; log any proposed incompatibility before coding.

## Implementation deliverables

- Implement mruk placement adapter, manual safe fallback, station-relative poses, saved local layout, decor milestones.
- Write automated tests for the happy path, one failure, a duplicate/replay event, and serialization of DTOs.
- Implement a scripted-demo equivalent behind `source_mode=demo`, with truthful visual labels and same UI path.
- Keep feature behind its `p2_...` flag and provide integration wiring notes (prefab/component name and intended scene parent).
- Compile/check in the actual project, and document Quest manual steps. If you cannot test on Quest, report that explicitly.

## Exit criterion

Stable placement after restart; permission denied fallback works; no room geometry leaves Quest.

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

1. Inventory existing `WorkspacePlacement` and MRUK initialization. Create an *adapter* to those services; do not add a second scene manager or XR rig.
2. Seek appropriate horizontal desk/table planes from available scene data and verify bounds/clearance. When unavailable, offer hand-driven manual placement with distance/scale safety clamps.
3. Store `StationLayout` locally in versioned JSON with non-sensitive station-relative poses and available local anchor handles. Never serialize or transmit scanned room mesh.
4. Add hand-accessible recenter/move workspace settings and `Reset layout` operation. If anchor cannot be found at reboot, require explicit re-placement.
5. Populate station decorations only from real artifact milestones, not wall clock. Respect reduced-motion mode.

### Acceptance detail

Test room setup available, scene permission denied, desk not detected, local layout malformed, anchor stale and recenter after user turns. With no room data, entire task flow remains operable in a fixed manual desk layout.
