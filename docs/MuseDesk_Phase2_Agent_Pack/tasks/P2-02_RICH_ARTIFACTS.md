# P2-02 — Real diff, research and test document viewer

**Owner boundary:** ArtifactService + Unity viewer  
**Priority:** Core  
**Dependencies:** P2-00  
**Reference:** `MASTER_PHASE2_IMPLEMENTATION.md` and frozen `contracts/phase2/`.

## Goal

Sanitized paged artifact routes, diff/test/research model, rich paper presenter, contextual selection refs. This is an implementation assignment, not permission to change Phase 1 behavior, add unreviewed packages, or bypass the approval gate.

## Preflight

1. Read original `AGENTS.md` plus `AGENTS_PHASE2.md`; inspect the real repository and feature versions.
2. Confirm relevant Phase 1 services compile and existing tests pass; do not assume specifications perfectly match current code.
3. Identify your owned files; request an integration owner for changes to main scene, package manifest or shared v2 schema.
4. Build against P2-00's single authoritative DTOs and event names; log any proposed incompatibility before coding.

## Implementation deliverables

- Implement sanitized paged artifact routes, diff/test/research model, rich paper presenter, contextual selection refs.
- Write automated tests for the happy path, one failure, a duplicate/replay event, and serialization of DTOs.
- Implement a scripted-demo equivalent behind `source_mode=demo`, with truthful visual labels and same UI path.
- Keep feature behind its `p2_...` flag and provide integration wiring notes (prefab/component name and intended scene parent).
- Compile/check in the actual project, and document Quest manual steps. If you cannot test on Quest, report that explicitly.

## Exit criterion

A real sandbox diff and failed test report expand correctly with safe paths; no confusion between live and demo.

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

1. Normalize allowlisted sample workspace outputs as typed `code_diff`, `research_report`, `test_report`, `implementation_plan` or `completion` artifacts. Keep artifact hash and session owner, sanitize terminal escape codes, redact sensitive tokens, strip absolute paths.
2. Create paginated read-only endpoints returning a stable page index and stable `line_ref`/`evidence_ref`; enforce page size and maximum output length server-side.
3. Extend the existing Phase 1 paper's `Expand` option into a typed `ArtifactDocumentPresenter` with type-specific panels (file summary, diff hunk, test result, evidence). No new approval side effects.
4. Support contextual question with selected artifact/line stable reference; backend checks selection belongs to authorized artifact and returns an evidence-linked answer or a clear limitation.
5. Integrate example failed test artifact and one real generated diff from the sample repository.

### Acceptance detail

A failed test must show failure, count, name and sanitized error; an unrun test cannot display green. A 1MB raw diff does not create a 1MB Unity Canvas—must paginate and virtualize. Empty/malformed content shows safe fallback. Replaying artifact event only updates same view by ID.
