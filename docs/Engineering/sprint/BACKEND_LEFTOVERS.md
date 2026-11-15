# Backend leftovers

Base 8c2ca85. Tests: `backend/tests/test_backend_leftovers.py` (9 new). pytest 302 -> 311 passed, 1 skipped.

## 1. v2 task create dependencies
Already fixed on this base (A11 note was stale): `TaskGraphService.create_task` rejects unknown and
cross-session ids with 422 `UNKNOWN_DEPENDENCY`, nothing created, no event
(`test_unknown_or_foreign_dependency_is_rejected`). Cycles cannot be formed through create: a new id
cannot already be depended on, and every dependency must pre-exist. Manager commit validates its own
graph (`validate_items`, topological order). No change.

## 2. Decision-timeout race (fixed)
`wait_for_decision` now decides with one compare-and-set, `Storage.expire_decision` (UPDATE ... WHERE
resolved_at IS NULL). Either the expiry wins (decision `expired`, a late answer gets 409) or an answer
already won and its action is honoured. `fail_run(expired_decision_id=...)` publishes the owed
`decision.resolved(expired)` event. Tests force both interleavings.

## 3. Phase 3 audit
Fixed:
- Overlapping requests with the same idempotency key both missed the stored result and both ran
  (two plans from one propose). New `phase2/keylock.py` holds a per-(session,key) lock across
  check -> run -> save in `phase3.common.idempotent` and the v2 `_idempotent_command` (also covers v2
  task create/assign/etc.).
- Commit rollback stored and broadcast `str(exc)` in `plan.failure`; now code plus a fixed message.
- `manager/status` now also lists open (proposed/approved) plans (item 4).

Verified OK: all plan routes scope by session (404 for foreign plan, status, commit, reject); same key +
different body -> 409 `IDEMPOTENCY_KEY_REUSE` on propose/revise/commit; same key + same body replays
without duplicate tasks; rejected/committed/failed plans cannot be revised, committed or rejected again
(409); retry is a no-op unless the card is failed; side conversation memory/dedupe/rate state is keyed
by session; runner provider errors are logged only, never returned.

## 4. Plan resync (additive)
`GET /v2/sessions/{sid}/manager/status` adds `open_plans` (full proposed/approved plan documents:
plan_id, revision, status, items with proposed_agent/depends_on, assumptions, risks).
Each entry in `plans` (and `GET .../plans/{id}/status`) adds `revision`, `objective`, `items`,
`synthesis_artifact_id`. Already present: cards with state, agent_id, depends_on, blocked_reason,
blocked_by, result/failure, `synthesis`, `settled`, `plan_status`.
