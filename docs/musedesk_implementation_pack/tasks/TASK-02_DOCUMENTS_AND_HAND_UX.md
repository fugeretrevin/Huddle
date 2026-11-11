# TASK-02 — Physical decisions, papers and hands-first UI

**Owner:** interaction/UX agent. **Priority:** P0. **Dependencies:** XR rig placeholder from TASK-01, decisions contract. **Never edit:** backend worker logic or central scene concurrently.

## Goal

User can grab a physically offered document, expand readable details, ask a question, and intentionally approve/reject/request changes; UI waits for a real (mock or live) `DecisionResult` before celebrating.

## Implement

1. `DecisionPaper.prefab`: stylized page, lightweight collider/rigidbody, Interaction SDK grab components, highlight affordance.
2. `DocumentDeliveryController`: listens for `decision.requested`, enqueues exactly one paper; `agent_id` and `decision_id` refer to payload; no duplicates from replay.
3. Paper state machine `queued_for_delivery→offered→held→expanded→responding→resolved/error` with error/timeout recovery and dropped paper reset.
4. `DecisionPanel`: title, 120-character summary, recommendation, rationale, alternatives, risks, linked impact; large high-contrast text.
5. Hand **Approve**, **Reject**, **Request Changes** buttons; on action create an idempotency key, call gateway, lock UI until confirmation, then display result.
6. `ConversationPanel` usable without voice: hand-operable text question presets or virtual keyboard; gateway `AskAsync` response and transcript.
7. Optional stamp animation **after** successful gateway response; not a legal digital signature and not required for access.

## Deliverables

Prefabs and controllers under assigned Unity folders; test scene or standalone prefab; deterministic fake gateway fixture for development; video of 10 grab/approval attempts.

## Acceptance

- [ ] Hands-only grasp/expand/approve/reject from Quest, 9/10 successful interaction trials.
- [ ] No approval happens before gateway acknowledgment.
- [ ] Duplicate event never spawns duplicate papers.
- [ ] Duplicate tap sends same action/key and backend effect remains once.
- [ ] User can recover dropped/out-of-reach paper.
- [ ] All text legible on Quest and controls reachable while seated.
- [ ] Offline demo source label visible on every expanded decision.
