# P2-01 — Physical task board and safe assignment

**Owner boundary:** Board + TaskGraph  
**Priority:** Core  
**Dependencies:** P2-00  
**Reference:** `MASTER_PHASE2_IMPLEMENTATION.md` and frozen `contracts/phase2/`.

## Goal

Hand grabbable card prefab, StationDropZone, card state machine, assign/priority endpoints, valid role/paused guards. This is an implementation assignment, not permission to change Phase 1 behavior, add unreviewed packages, or bypass the approval gate.

## Preflight

1. Read original `AGENTS.md` plus `AGENTS_PHASE2.md`; inspect the real repository and feature versions.
2. Confirm relevant Phase 1 services compile and existing tests pass; do not assume specifications perfectly match current code.
3. Identify your owned files; request an integration owner for changes to main scene, package manifest or shared v2 schema.
4. Build against P2-00's single authoritative DTOs and event names; log any proposed incompatibility before coding.

## Implementation deliverables

- Implement hand grabbable card prefab, stationdropzone, card state machine, assign/priority endpoints, valid role/paused guards.
- Write automated tests for the happy path, one failure, a duplicate/replay event, and serialization of DTOs.
- Implement a scripted-demo equivalent behind `source_mode=demo`, with truthful visual labels and same UI path.
- Keep feature behind its `p2_...` flag and provide integration wiring notes (prefab/component name and intended scene parent).
- Compile/check in the actual project, and document Quest manual steps. If you cannot test on Quest, report that explicitly.

## Exit criterion

Drop a queued card on a valid station, receive one acknowledged state change; stale revision and invalid target are rejected.

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

1. Backend `TaskGraphService.assign_task(task_id, target_agent_id, expected_revision, key)` validates session ownership, role compatibility, queued/paused state, dependency integrity and current revision in one transaction.
2. Add task `priority` 0–3 and `priority_changed`; scheduler reorders queued items only. Add cooperative pause/requested checkpoints (never claim a running process is paused immediately).
3. Unity creates a pooled `TaskCardView` for each canonical task ID, `TaskBoardPresenter` for four status columns, and `StationDropZone` for each desk. Use installed Interaction SDK grab interactions and a no-drag poke alternative.
4. Drag release → resolve drop target → `Pending` → submit exactly once → accept canonical assignment or bounce back with readable error; cancel lost hand or invalid zone without command.
5. Add scripted demo counterpart with a task card that is assignable and one deliberately incompatible task.

### Acceptance detail

Unit tests: invalid agent, invalid state, stale revision, duplicate idempotency key, concurrent assignment race, dependency cycle, unsupported task ID, and role-passing happy path. Quest tests: 10 valid/invalid card drops with captured command count, panel readability, hand-loss recovery and live reconnect consistency.
