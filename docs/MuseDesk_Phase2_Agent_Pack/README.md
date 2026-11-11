# MuseDesk Phase 2 — Agent Implementation Pack

**Add-on only.** This is a post-Phase-1 extension package, NOT a replacement Unity project or implemented feature set. It was prepared against the original MuseDesk Phase 1 specification and the existing v1 event envelope.

## Start here

1. Read `MASTER_PHASE2_IMPLEMENTATION.md` (architecture, all seven epics, acceptance and validation).
2. Read `AGENTS_PHASE2.md` (non-negotiable agent rules) AND the original repo's `AGENTS.md` and `docs/API_AND_EVENT_CONTRACT.md`.
3. Read `docs/INTEGRATION_PLAYBOOK.md`, then perform Ticket **P2-00** before allowing the others to build against frozen contracts.
4. Assign each `tasks/P2-XX_*.md` in isolated branches/worktrees; do not let two agents edit the main Unity scene or schema files concurrently.
5. Validate the sample schemas and fixtures with `python examples/validate_contracts.py` after installing `jsonschema`.
6. Run real Quest validation and keep original Phase 1 smoke tests green.

## Priority

**P2-00 → P2-01 → P2-02 → P2-03 → P2-07 → P2-06 → P2-05 → P2-04.**  This is a suggested *integration order*, not necessarily an implementation assignment order. Stop after the first fully working vertical slice if the release deadline is close.

## Deliverables

- Master human-readable spec (5,600+ words), plus DOCX outside this folder.
- Nine scoped implementation tickets and integration instructions.
- v2 event/command/artifact/diagram/snapshot JSON schema examples.
- A scripted Phase 2 demo fixture and a schema-check helper.
- Copy/paste agent launch prompts and Quest-side smoke tests.

## Safety and platform assumptions

Windows PC companion executes tools only in a disposable, explicitly selected sandbox. Quest is the spatial UI. Standalone Quest demo is explicitly labeled **SCRIPTED DEMO**. A scene animation does not authorize tool execution. Refer to the actual installed Meta XR/Unity SDK APIs before writing C#; names under `Assets/MuseDesk/Phase2` in the plan are proposed project module names, not Meta package API names.
