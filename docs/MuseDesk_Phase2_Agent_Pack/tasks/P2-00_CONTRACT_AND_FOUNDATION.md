# P2-00 — Contract, compatibility and integration foundation

**Owner boundary:** Schema/Backend  
**Priority:** Foundation  
**Dependencies:** P2-01, P2-02, P2-03, P2-04, P2-05, P2-06, P2-07  
**Reference:** `MASTER_PHASE2_IMPLEMENTATION.md` and frozen `contracts/phase2/`.

## Goal

v2 snapshot, WS events, DTOs, route stubs, migration, feature flags and demo gateway adapter. This is an implementation assignment, not permission to change Phase 1 behavior, add unreviewed packages, or bypass the approval gate.

## Preflight

1. Read original `AGENTS.md` plus `AGENTS_PHASE2.md`; inspect the real repository and feature versions.
2. Confirm relevant Phase 1 services compile and existing tests pass; do not assume specifications perfectly match current code.
3. Identify your owned files; request an integration owner for changes to main scene, package manifest or shared v2 schema.
4. Build against P2-00's single authoritative DTOs and event names; log any proposed incompatibility before coding.

## Implementation deliverables

- Implement v2 snapshot, ws events, dtos, route stubs, migration, feature flags and demo gateway adapter.
- Write automated tests for the happy path, one failure, a duplicate/replay event, and serialization of DTOs.
- Implement a scripted-demo equivalent behind `source_mode=demo`, with truthful visual labels and same UI path.
- Keep feature behind its `p2_...` flag and provide integration wiring notes (prefab/component name and intended scene parent).
- Compile/check in the actual project, and document Quest manual steps. If you cannot test on Quest, report that explicitly.

## Exit criterion

The v1 approval and demo continue unchanged; unit tests validate v2 example schemas and actual v2 snapshot/replay.

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

1. Run a baseline audit: record Unity version, installed Interaction SDK/MRUK packages, current rig hierarchy, `IAgentGateway` shape, backend schema and routes. Write `docs/PHASE1_COMPATIBILITY_AUDIT.md` in the code repository.
2. Add new `schema_version=2` DTOs for task cards, handoffs, artifacts, focus, diagrams and v2 event envelope. Keep original strict v1 schema untouched.
3. Add additive SQLite migrations for task `revision`, handoff records, focus policy, inbox and diagram metadata; preserve all Phase 1 records and snapshots.
4. Build a v2 snapshot projector with consistent `last_seq` and a v2 event WS that replays canonical history; v1 subscribers still receive only v1 types.
5. Implement `Phase2GatewayAdapter`, `DemoGatewayV2`, `Phase2Reducer` in Unity and verify reducers ignore duplicates and request snapshot upon gaps.
6. Add feature flag config defaults OFF; render all v2 display data with explicit `source_mode`.

### Acceptance detail

A v1 client with a v1-only event validator can still complete approval; a v2 client can load a snapshot, receive exactly one `handoff.created` v2 event, disconnect, reconnect and end with identical state. V1 test fixtures and the 4 supplied v2 example fixtures validate.
