# A10 Manager delegation (backend)

Base: feature/living-world-polish (9a6fbc6). Backend only; nothing Unity-side changed.

## What works
- Plan document validation (`phase3/plan_schema.py`, wraps `validation.py`): empty objective, non-object plan, size cap (24000 chars), unknown agent/kind/permission, role/kind mismatch, duplicate ids, dangling deps, self/longer cycles, >12 items. Failure returns 422 with `details[]` (`PLAN_INVALID` / `MODEL_PLAN_INVALID`); nothing becomes a card.
- Dependencies: the runner only starts a card that is `queued` with all deps `completed`. A failed/cancelled dependency leaves dependents `blocked`; one `task.progress` event per dependent carries `blocked_reason`, `blocked_by` (root failed task ids, transitive), `depends_on`. `manager.plan_committed` now has `edges[{from_task_id,to_task_id}]`; each `committed_tasks[]` entry has `depends_on` (task ids).
- Retry (`PlanOps.retry_card`, `ManagerPlanService.retry_card`): failed -> queued (or blocked if deps not done), emits `task.resumed` reason `retry_requested`. Repeat is a no-op (`retried:false`). Non-failed/blocked/foreign cards: `NOT_RETRYABLE` / `NOT_FOUND`. Clears a stale synthesis.
- Status (`plan_status`, `manager_status`): counts by state, per-card blocked reason, failures, `settled`, `synthesis`.
- Synthesis: when every card is terminal (or blocked behind a failure) one `completion` artifact is created, once. Demo: `[SCRIPTED DEMO]` roll-up. Live with model (purpose `plan`, no tools): labelled "Model-written from the recorded step results"; model only sees the recorded roll-up. No model or model failure: "Deterministic roll-up of the recorded step results". Anchored on the last completed card (a task id is required).
- Trust: proposing creates no cards and runs nothing; commit still demands approval and explicit authorization for dangerous items; runner has no tools.

## Tests
`tests/phase3/test_p3_plan_ops.py` (12). Existing runner test filters out `completion` artifacts. Full suite 219 passed (baseline 207).

## Integration items (routes.py / contracts not mine)
Add, behind `manager_gate`, in `routes.py`:
- `GET  /v2/sessions/{sid}/manager/status` -> `ManagerPlanService(...).manager_status(sid)`
- `GET  /v2/sessions/{sid}/manager/plans/{plan_id}/status` -> `plan_status(sid, plan_id)`
- `POST /v2/sessions/{sid}/manager/plans/{plan_id}/tasks/{task_id}/retry` (body: `idempotency_key`) -> `idempotent(..., svc.retry_card(sid, plan_id, task_id))`

## Unity-facing
- Unity should tolerate `task.progress` with `blocked_reason`/`blocked_by` on a blocked card and show the reason.
- A `completion` artifact now appears on the plan's last completed card (the plan summary; its title says Plan summary and carries its label).
- `provider.PURPOSES` lacks `synthesis`; synthesis reuses `plan` effort.
