# Instructions for coding agents: MuseDesk Phase 2

**Read both:** the original repo `AGENTS.md` and `MASTER_IMPLEMENTATION_PLAN.md`, then `MASTER_PHASE2_IMPLEMENTATION.md`. New instructions below are supplementary. If Phase 1 still has broken critical-path behavior, stop Phase 2 and report the blocker.

## Golden rules

1. Phase 2 is *additive*: preserve the existing Unity project, one XR rig, passthrough, Phase 1 decision approval, backend approval gate, auth and v1 event contract.
2. DO NOT modify the original `contracts/event.schema.json` enum to insert v2 event names. Create new v2 contracts and expose v2 snapshot/event endpoints with a clearly defined migration adapter.
3. Server is sole source of truth for assignment, completion, task dependencies, approvals, test results and handoffs. Unity may animate, never execute or approve a business transition by collision alone.
4. Preserve explicit `source_mode: live|demo`; the offline Quest experience says `SCRIPTED DEMO` at all times, including expanded artifacts and diagrams.
5. Tool execution stays inside the sample repo/worktree with allowlisted commands and timeouts. No arbitrary shell, filesystem traversal, credentials, publishing, money actions or automatic bypass of approval gates.
6. Do not put room scans, camera data or precise local room geometry into network payloads. Layout data stays local to the device.
7. Do not invent Meta/Unity SDK symbols. Inspect packages actually installed, use Unity CLI/Pipeline and test resulting C# in Editor; then test on Quest 3.
8. Add feature flags defaulted off. Preserve `phase1-stable` tag and a clean rollback path.
9. Make commands idempotent with same-session key plus payload hash; on conflict reconcile to server state. Do not automatically duplicate jobs or handoffs after event replay.
10. All user-facing 'test passed' statements need real stored test evidence, or a conspicuous scripted-demo label.

## Locking/ownership

- P2-00 alone changes v2 contracts, DB migrations and cross-cutting gateway adapters.
- P2-01 owns board + task assignment; P2-02 owns artifact viewer/API; P2-03 owns handoffs; P2-07 owns factory props; P2-06 owns focus; P2-05 owns room layout; P2-04 owns diagrams; P2-99 owns the integration scene and release QA.
- Tickets may add isolated prefabs and tests; only P2-99 changes the main XR scene or shared project settings after reviewing owner PRs.
- Each ticket must produce a small runnable change with test evidence, actual files changed, API/schema changes and known limits. Do not claim completion on scaffolding alone.

## Agent finish report template

```text
Ticket:
What now works:
Exact files changed:
Contract/API changes:
How to run:
Tests and actual outputs:
Quest-device verification (yes/no):
Phase 1 regressions (none / list):
Known limitations:
Integration/rollback notes:
```
