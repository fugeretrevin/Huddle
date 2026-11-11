# TASK-07 — Integration, Quest testing and offline judge mode

**Owner:** integration/QA lead. **Priority:** P0. **Dependencies:** TASK-01 to TASK-05 interfaces.

## Goal

Integrate XR presentation with network backend and deliver a standalone Quest APK judges can launch without the author's Windows PC.

## Implement

1. Build `LiveAgentGateway` and `DemoAgentGateway` implementing shared `IAgentGateway`; event reducer is identical regardless of mode.
2. Map JSON `event_id/seq` into local session models; resync via snapshot on reconnect; events applied on Unity main thread in order.
3. Create self-contained offline fixture (3 distinct characters/task states, one approval request, question answer, one output artifact) clearly labeled `OFFLINE DEMO · simulated`. No reference to developer local IP.
4. Build mode selector with short connection timeout, `Try Offline Demo`, `Connect to Desktop`; never silently pretend PC is connected.
5. Implement diagnostics, fallback placement, reset session and failure UI.
6. Exercise all tests from `docs/SECURITY_TESTING_AND_EVALUATION.md` on Quest; run hardware performance capture via MVDH.
7. Capture build hash, SDK versions, clean-install validation steps, and release candidate checklist.
8. Help agent owners eliminate double spawn, idempotency errors, out-of-reach paper, back-of-panel collisions and missing permissions.

## Acceptance

- [ ] Offline judged demo complete with hand interaction and no PC/network.
- [ ] Live mode receives actual events and approval changes real sample work.
- [ ] Reconnection/duplicate events are safe; no false success on transport failure.
- [ ] Three uninterrupted demo runs, clean install, Quest FPS ≥60 baseline.
- [ ] Release APK does not embed private credentials or local-only hardcoded dependency.
