# Integration and developer playbook

## Prerequisites

- Unity project is already created and running passthrough on Quest 3.
- Unity CLI with Pipeline is installed and `unity status` reports a reachable Editor (CLI commands may change; inspect `unity --help`).
- Current Phase 1 master plan and v1 contracts are available inside the repository.
- Windows FastAPI backend runs locally; the `LiveAgentGateway` and `DemoAgentGateway` are both functional.
- Start from a **new git branch** with a tagged or committed Phase 1 working state.

## How to add this pack

Copy the contents under a project planning folder, e.g. `planning/phase2/`. Do **not** copy these example JSON schemas into the authoritative `contracts/` directory or overwrite existing schemas without running P2-00 compatibility tests. Preserve the original `AGENTS.md` and add `AGENTS_PHASE2.md` for coding agents to read.

## Contract integration

1. P2-00 inspects deployed backend and Quest DTOs; compare them with v1 spec, documenting deviations.
2. P2-00 chooses a single negotiated v2 endpoint design matching the plan. No P2 feature agent may independently define event names/field names.
3. Implement the example JSON schemas with production `$id` and version; generate or hand-write Python Pydantic models and Unity C# DTOs and test serialization both directions.
4. Preserve v1 routes. Add v2 routes and a v2 event projection; validate original v1 sample fixture remains valid.
5. Add Quest `DemoGatewayV2` fixture and apply the same view-model reducer as live. Ensure no fake live transitions.
6. After baseline tests pass, enable one feature flag at a time.

## Suggested worktree setup

```powershell
# In project repository, after committing Phase 1:
git tag phase1-stable
git switch -c integration/phase2
# If team uses worktrees, each one should be separate:
git worktree add ..\MuseDesk-p2-board -b feature/p2-board
```

Never put two Claude/agent processes in the same Unity project folder editing `Assets/Scenes/MainMRScene.unity` or `Packages/manifest.json` at the same time. Editors in separate worktrees need their own Library imports; backend-only agents can work without launching the Editor.

## First 4 integration checks

1. With Phase 2 feature flags OFF, run the entire original Quest approval demo: should behave identically.
2. With `p2_board=ON`, create a queued sample task; drag it to the Researcher; ensure server returns canonical state.
3. With `p2_handoffs=ON`, approve researcher decision and see exactly one handoff event, one visual animation and one resulting developer task.
4. With `p2_artifacts=ON`, expand real diff/test report and verify source mode and actual test counts.

## Device/PC testing

- USB: first validate local backend connectivity using existing `adb reverse` helper; headset Wi-Fi is not required for a tethered dev endpoint.
- LAN: use authenticated private-network connection, short-lived session token and only a trusted subnet. Do not expose your FastAPI endpoint to public internet.
- Quest: retest hand tracking in real passthrough, seated desk placement, headset sleep/wake, scene permission denied and no backend mode.
- Offline: disconnect headset from PC/internet and launch app; demonstrate full P2-01/02/03/07 demo route with `SCRIPTED DEMO` visible.
- If `unity status` reports editor blocked by import/compilation, clear Console errors and inspect `unity pipeline editors`; don't reinstall all tools reflexively.

## Rollback strategy

All Phase 2 features have distinct flags; disable `p2_*` to return to Phase 1. V2 routes and tables are additive migrations. Do not delete/mutate Phase 1 persisted decisions. If v2 rollout fails, use the stable Phase 1 tag/build.
