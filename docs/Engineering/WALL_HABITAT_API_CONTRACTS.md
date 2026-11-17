# Wall Habitat API contracts (Phase 3 backend)

Status: implemented and covered by backend tests (`backend/tests/phase3/`, 49 tests); NOT exercised from Unity or a headset. Code: `backend/app/phase3/`. Schemas: `contracts/phase3/*.schema.json`. All routes are additive under `/v2`, use the same per-session bearer auth as Phase 2 (`Authorization: Bearer <token>` from `POST /v1/sessions`), and report errors as `{code, message, error_code, user_message[, canonical_plan][, details]}`.

## Flags

| Setting (env `MUSEDESK_` prefix) | Default | Effect |
|---|---|---|
| `P3_SIDE_CONVERSATION_ENABLED` | `false` | side-question and visit DELETE routes answer 404 `FEATURE_DISABLED` |
| `P3_MANAGER_ENABLED` | `false` | all `/manager/...` routes answer 404 `FEATURE_DISABLED` |
| `P3_SIDE_TIMEOUT_SECONDS` | 12 | hard model deadline for a side answer |
| `P3_SIDE_MAX_CONCURRENCY` | 4 | global cap, then `status: busy` |
| `P3_SIDE_HISTORY_EXCHANGES` | 8 | ephemeral per-visit memory |
| `P3_PLAN_TIMEOUT_SECONDS` | 20 | planner model deadline |

Why default off: the p2 flags default off and only gate the client, with the `/v2` surface always mounted. The Phase 3 spec says the competition release keeps Wall Habitat dark, so the p3 flags default off and are enforced on the server as well. Auth is checked before the flag (an unauthenticated caller gets 401, not a feature probe). Enable both with `MUSEDESK_P3_SIDE_CONVERSATION_ENABLED=true MUSEDESK_P3_MANAGER_ENABLED=true`.

## Model provider

The backend has no model adapter (`model_provider` / `provider_api_key` settings are declared but nothing uses them). Phase 3 defines the seam `app.phase3.provider.ModelProvider` (`async complete(*, system, messages, max_tokens) -> str`, no tools) and reads it from `app.state.p3_provider`, which is `None` by default. Nothing in this repo sets it, so today:

- side answers are `TASK_SNAPSHOT` (built only from stored data), or `SCRIPTED_DEMO` in scripted mode;
- Manager plans are `TEMPLATE_PLAN`, or `SCRIPTED_DEMO` in scripted mode.

`LIVE_MODEL` is reachable only after someone wires a provider. In scripted mode (`state_source_mode == "demo"`) a provider is never called, even if configured.

## 1. Side conversation (read-only `/btw`)

`POST /v2/sessions/{sid}/agents/{agent_id}/side-question`, `agent_id` in `researcher|developer|tester|manager`.

Request:
```json
{"request_id":"sq-1","task_id":"t2-abc (optional)","visit_session_id":"visit-12",
 "question":"Why is this blocked?","context_revision":3,"idempotency_key":"<8-200 chars>"}
```
Reply (HTTP 200 for every domain outcome, including unavailable/busy/timeout):
```json
{"request_id":"sq-1","agent_id":"developer","task_id":"t2-abc","visit_session_id":"visit-12",
 "reply":"Task 'Build login' is running, revision 4. Last update: task.started at ...",
 "provenance":"TASK_SNAPSHOT","status":"answered","context_revision":4,"stale_context":true,
 "task_snapshot":{"task_id":"t2-abc","title":"Build login","state":"running","agent_id":"developer","revision":4},
 "proposed_amendment":null}
```

`status`: `answered`, `unavailable` (unknown agent, unknown or foreign task, agent with no task data; provenance `NONE`), `timeout` (model too slow; reply carries the recorded snapshot; provenance `TASK_SNAPSHOT`), `amendment_suggested`, `busy` (global cap hit; provenance `NONE`).

`provenance`: `LIVE_MODEL`, `TASK_SNAPSHOT`, `SCRIPTED_DEMO` (reply also starts with `[SCRIPTED DEMO]`), `NONE` (no answer was produced). A model failure falls back to `TASK_SNAPSHOT` and the reply says the model was unavailable.

Behaviour:
- No `task_id`: a worker uses its own latest non-terminal task (else latest); `manager` summarises the whole board plus the latest plan, with `task_snapshot: null` and `context_revision` = the v2 event `last_seq`.
- `context_revision` older than the task's current revision: still answered from current state, `stale_context: true`, current revision returned.
- Change requests ("cancel", "switch to X", "instead", "delete", "deploy", "use Redis"...): `amendment_suggested`, `proposed_amendment: {summary, requires_approval: true}`, reply states it was NOT applied. The text is never sent to a model. Detection is a heuristic on imperative phrasing; a miss still cannot change anything because this route has no write path.
- Read-only guarantee: no task or event writes, no events, no mutating service, no agent transcript access, model gets no tools and a bounded sanitised context (task card, last 5 event summaries, artifact kind and summary, no paths or secret patterns). Only side effect: the idempotency record and process-memory visit history.
- Idempotency: same as other v2 commands (key + route + body hash; same key with a different body is 409 `IDEMPOTENCY_KEY_REUSE`). `busy`, `timeout` and `unavailable` are NOT cached, so retrying the same key can succeed.
- Visit memory: last 8 exchanges per (session, `visit_session_id`), in process memory only (lost on restart), capped at 256 visits.

`DELETE /v2/sessions/{sid}/agents/{agent_id}/visits/{visit_session_id}` clears that memory. Always 200: `{"visit_session_id","agent_id","cleared_exchanges":N}`. Call it on Return/Done, tracking loss and mode switch.

## 2. Manager plans

Plan object (`manager_plan.schema.json`):
```json
{"plan_id":"mp-1a2b3c4d5e","source_request_id":"req-1","title":"...","objective":"...",
 "created_at":"...Z","provenance":"TEMPLATE_PLAN","provenance_note":"Planning is template-based ...",
 "assumptions":[],"risks":[],
 "items":[{"client_item_id":"a","title":"...","kind":"research","proposed_agent":"researcher",
           "depends_on":[],"acceptance_criteria":[],"required_permission":"read_repo",
           "dangerous":false,"danger_reasons":[]}],
 "status":"proposed","revision":1,"reject_reason":null,"failure":null,"committed_tasks":[]}
```
`provenance`: `LIVE_MODEL`, `TEMPLATE_PLAN` (live mode, no model: show "template plan, not AI"), `SCRIPTED_DEMO`. `status`: `proposed|approved|rejected|committed|failed` (`approved` is accepted by the state machine but no route sets it today; commit goes straight from `proposed`). `revision` increases by one on every change (revise, reject, commit, fail).

| Route | Body | Result |
|---|---|---|
| `POST /v2/sessions/{sid}/manager/plans` (201) | `{request_id, objective(3-500), idempotency_key}` | plan, revision 1 |
| `GET .../manager/plans/{plan_id}` | | plan (404 `NOT_FOUND` for unknown or other session) |
| `POST .../plans/{plan_id}/revise` | `{revision, items?, title?, idempotency_key}` | plan, revision+1. `items` replaces the whole list. |
| `POST .../plans/{plan_id}/reject` | `{revision, reason, idempotency_key}` | plan, status `rejected` |
| `POST .../plans/{plan_id}/commit` | `{revision, approved_item_ids[], authorized_dangerous_item_ids[], idempotency_key}` | `{plan_id, status:"committed", revision, tasks:[{client_item_id, task_id, agent_id, state, revision}]}` |

Validation (one function, used at propose, revise and commit; model output is never trusted): unique client ids (1-40 chars `[A-Za-z0-9_.-]`); at most 12 items; title 1-120 chars; `depends_on` must name items in the plan; no cycles (the cycle is reported as `a -> b -> a`); `proposed_agent` in researcher/developer/tester (`manager` is rejected as `MANAGER_CANNOT_OWN`); `kind` to role map: research->researcher, code|implement->developer, test|verify->tester, doc->researcher|developer (else `ROLE_KIND_MISMATCH`); `required_permission` in `none|read_repo|workspace_write|run_tests`. `dangerous` and `danger_reasons` are computed by the backend (client or model values ignored) from title, criteria and permission: deployment, deletion, spending, credentials, external network post, push to remote. Heuristic keyword match: it errs toward flagging.

Validation failures: 422 `PLAN_INVALID` (revise/commit/template) or `MODEL_PLAN_INVALID` (model output), with `details: [{code, message, item_id?}]`. Codes: `EMPTY_PLAN, TOO_MANY_ITEMS, ITEM_MALFORMED, BAD_ITEM_ID, DUPLICATE_ITEM_ID, BAD_TITLE, BAD_CRITERIA, BAD_DEPENDS_ON, MANAGER_CANNOT_OWN, UNKNOWN_AGENT, UNKNOWN_KIND, ROLE_KIND_MISMATCH, UNKNOWN_PERMISSION, UNKNOWN_DEPENDENCY, DEPENDENCY_CYCLE, NOT_JSON`. A model plan that fails validation is rejected and nothing is stored; there is no silent fallback to the template planner.

Other errors: 503 `PLANNER_UNAVAILABLE`, 504 `PLANNER_TIMEOUT` (provider configured but failed), 422 `NOTHING_TO_REVISE`, `NO_ITEMS_APPROVED`, `UNKNOWN_ITEM`.

Commit rules, checked in this order, nothing is created on any refusal:
1. `revision` != plan revision: 409 `REVISION_CONFLICT` with `canonical_plan`.
2. Plan already committed: 409 `PLAN_ALREADY_COMMITTED`; rejected or failed: 409 `PLAN_NOT_OPEN`. (Replaying the SAME idempotency key returns the original result instead.)
3. Stored items re-validated.
4. An approved item depending on an unapproved item: 409 `DEPENDENCY_NOT_APPROVED`, `details[].item_id`.
5. An approved dangerous item missing from `authorized_dangerous_item_ids`: 409 `AUTHORIZATION_REQUIRED`, `details[].item_id`. Authorizing a non-dangerous or unapproved item has no effect.
6. Cards are created in dependency order through the existing `TaskGraphService.create_task` (`depends_on` mapped to canonical task ids, `agent_id` = proposed agent, priority 1, no role lock, no `task_template`). If any create raises, every row this commit created is deleted, the plan becomes `failed` (`failure: {code, message}`), `manager.plan_failed` is emitted and the call returns 500 `COMMIT_FAILED`. The failure is not cached as an idempotent result.
7. Only after all cards exist and the plan is saved as `committed`: one `task.created` event per card (existing `announce_created`), then `manager.plan_committed`.
A partial approval commits only the approved, dependency-closed subset; the plan is then `committed` and the other items are not retained for later.

What this does and does not execute: dependencies gate exactly as for any v2 card (a card whose dependencies are not `completed` is created `blocked`; the first is `queued`). Plan cards are plain board cards (no `task_template`). In this backend only `task_template: "auth_demo"` cards are executable, so committed plan cards are tracked on the board but no agent runs them automatically. Do not present them as running.

## Events (v2 stream, additive)

Added to `EventTypeV2` and to the enum in `contracts/phase2/event_v2.schema.json`. Common payload: `{plan_id, revision, status, title, provenance, agent_id:"manager", source_mode}`. The envelope has no `task_id`/`agent_id` (the manager is not a registry agent).

| type | extra payload |
|---|---|
| `manager.plan_proposed` | `objective, items[], assumptions[], risks[], provenance_note` |
| `manager.plan_revised` | `items[]` |
| `manager.plan_rejected` | `reason` |
| `manager.plan_committed` | `tasks[{client_item_id,task_id,agent_id,state,revision}], task_ids{client_item_id: task_id}` |
| `manager.plan_failed` | `reason{code,message}` |

Order on commit: N x `task.created` (dependency order) then `manager.plan_committed`. Side questions emit no events at all.

## What Unity must send and handle

Send:
- Always a fresh `idempotency_key` (>= 8 chars) per user action; reuse the same key only to retry the identical request.
- Side question: `visit_session_id` stable for the visit, `task_id` when known, `context_revision` = the revision last shown. Call the visit DELETE on Return, cancel, tracking loss, recenter and mode switch.
- Commit: the `revision` of the plan the user actually saw, the ids the user ticked, and `authorized_dangerous_item_ids` ONLY for items the user explicitly confirmed on a separate "this is dangerous" step.

Handle:
- Show `provenance` honestly: `SCRIPTED_DEMO` and `TASK_SNAPSHOT` are not AI answers; `TEMPLATE_PLAN` must read as a template, with `provenance_note`.
- `status` other than `answered` (`unavailable`, `busy`, `timeout`) must show the `reply` text and never imply a live answer. `amendment_suggested` must show that nothing was changed and offer the normal flow.
- `stale_context: true`: refresh the shown revision.
- Treat a manager card as real only after `manager.plan_committed` or the commit response; animate printed cards only for `task_ids` in it. Never create a card from a `proposed` plan.
- `REVISION_CONFLICT`: replace the local plan with `canonical_plan` and ask again. `FEATURE_DISABLED` (404): hide the feature.
- Reducer (`Phase2Reducer.cs`): unknown event types already advance the seq and fall to `default: break`, so the new events cannot break it. To use them, add `Phase2EventTypes` constants for the five `manager.*` types, DTOs for the plan and side reply, a `ManagerPlanEvent` callback like `DiagramCreated`, and a case per type. The existing `task.created` path already adds the cards. Plan cards may arrive with `agent_id` set at creation and no `task.assigned` event.

## Not done / limits

No real model adapter is shipped. Manager is not an entry in the v1 agent registry (`/v1` agents are still three); it exists only as the `manager` agent id for side questions and as the plan author. Plans live in the new `manager_plans_v3` table (additive); visit memory and per-plan commit locks are in process memory.
