# RC8 Live Agent Evidence (backend credibility ledger)

Handoff reference: `01_CLAUDE_RELEASE_EXECUTION_HANDOFF.md` section 1 Gate D and section 7.
Commit under test: `4d976fa` (branch `feature/rc8-feedback-1`; working tree had unrelated dirty art/Unity files, no `backend/` changes).
Run date: 2026-11-15 (local evening). Operator: Claude (backend-credibility owner). Tester for WORN: nobody.
Evidence levels used here: **AUTO** (pytest) and **LIVE-backend** (real HTTP calls to a locally run backend, real Meta model). **NOT RUN** where noted. **No claim in this document is WORN.**

## 0. Scope statement (read first)

- **No headset-facing live flow was run.** Nothing here was exercised from Quest, a Unity client, Play Mode, or a worn device. Every live call was made from a Python HTTP client on the PC to `127.0.0.1`, imitating the request shapes the Unity client uses.
- The model is Meta (`MUSEDESK_MODEL_PROVIDER=meta`, owner's key from git-ignored `backend/.env`; models `muse-spark-1.3` for plan and work, `muse-ember-1.0` for side questions per `settings.py`). No key, token or `.env` content is printed in this document or in the run logs.
- Throw-away databases and sandbox roots were used (scratchpad), so `backend/data/` was not touched. No git state was changed.
- The APK, the Unity client wiring of these routes, and the presentation wording in the apps are NOT verified here. Section 4 states what the client must show.

## 1. Automated backend tests (AUTO)

`scripts\backend_tests.ps1` (pytest, 600 s cap): **327 passed, 1 skipped, 0 failed, 0 errors** (328 collected, about 31 s). JUnit XML: `TestResults\backend_20261115_222816.xml`.
The single skip is `tests/test_a11_reliability.py:429`, `pytest.skip("symlinks unavailable")`: the symlink escape test is skipped on this Windows account (no symlink privilege). **Symlink traversal is therefore not proven on this machine**; path traversal without symlinks is covered by the other passing tests.
These counts match the expert overview section 3.4.

## 2. Live run (LIVE-backend)

Backend started the way `scripts\start_wall_backend.ps1` does for Meta: `MUSEDESK_P3_SIDE_CONVERSATION_ENABLED=true`, `MUSEDESK_P3_MANAGER_ENABLED=true`, `MUSEDESK_P3_PLAN_RUNNER_ENABLED=true`, `MUSEDESK_PRODUCER=live`, `MUSEDESK_MODEL_PROVIDER=meta`, `uvicorn app.main:app --host 127.0.0.1` (own port, temp DB, runner poll 1 s). Pairing was on, so every call used the bearer token from `POST /v1/sessions`. Ids below are shortened/redacted.

### 2a. Live Manager plan - PASS

`POST /v2/sessions/{sid}/manager/plans` body `{"request_id":"req-xxxxxx","objective":"Add a rate limiter to the login endpoint of a small Flask app","idempotency_key":"idem-..."}`

Response `201` after **12.9 s** (a second run: 13.4 s):
`{"plan_id":"mp-8dbf...","status":"proposed","revision":1,"provenance":"LIVE_MODEL","provenance_note":"Plan drafted by a configured model and then validated by the backend.","title":"Add rate limiter to login endpoint of Flask app","items":[...6 items...],"assumptions":[...],"risks":[...]}`

- Six items, kinds `research, research, implement, test, verify, doc`, owners researcher x2, developer x2, tester x2, each with `depends_on` forming a chain, none flagged `dangerous`.
- The backend (not the model) validated the schema (`validation.validate_items`: known kinds, no manager-owned item, dependency cycle check, danger scan). The event `manager.plan_proposed` carries `provenance: LIVE_MODEL` and `source_mode: live`.

### 2b. Approve and execute - PASS (sequential, text-only, as designed)

`POST .../manager/plans/{plan_id}/commit` body `{"revision":1,"approved_item_ids":[all six],"authorized_dangerous_item_ids":[],"idempotency_key":"idem-..."}` returned `200 {"plan_id":..,"status":"committed","revision":2,"tasks":[{client_item_id, task_id, agent_id, state, revision, depends_on}...]}`. The first card was `queued`; the other five were `blocked` on dependencies.

The plan runner then worked the cards with the live model, polled via `GET .../manager/plans/{plan_id}/status`:

| t (s) | Observed card states |
|---|---|
| 0 | explore queued, 5 blocked |
| 3 | explore **running** |
| 18 | explore completed, design queued |
| 21 | design running |
| 42 | design completed, implement running |
| 60 | implement completed, test queued/running at 63 |
| 81 | test completed, verify running |
| 99 | verify completed, doc running |
| 111 | **doc completed; 6 of 6 completed** |

- **Exactly one card ran at a time** (also enforced in code: `runner.tick` skips a session that already has a `running` card). The dependency chain made it strictly sequential. This is not parallel multi-agent work and must not be staged as such.
- Total wall time commit to all done: about 111 s for 6 cards, roughly 17-21 s per card. (The seventh artifact, `kind: completion`, "Plan summary: 6 of 6 steps completed", was written by the roll-up.)
- Each card produced one artifact, reachable via `GET /v2/sessions/{sid}/artifacts/{artifact_id}`. Real output examples (the model's text, abbreviated):
  - researcher: page title `Research write-up (Model-written, no tools: nothing was run or applied.)`, content begins `Model-written, no tools: nothing was run or applied. | Findings: No repository access in this step, so no route confirmed...`
  - developer: `Proposed change (Model-written, no tools...)`, "Implement in-memory fixed-window limiter... Default 5 attempts per 900 seconds keyed by client IP plus normalized username..."
  - tester: `Test plan (Model-written, no tools...)`, a case list with inputs and expected results. It states "No tests run."
- Provenance label found in the data:
  - Event `task.started`/`task.completed`: `work_mode: "model_text"`.
  - Event `artifact.created`: `work_provenance: "model_text_no_tools"`, `source_mode: "live"`.
  - Every artifact body and page title carries the sentence `Model-written, no tools: nothing was run or applied.`
- **Nothing was executed**: no file written, no repository touched, no pytest. The deliverables are model prose about code the model never saw (the content itself says "pending code check"). This is `Model-written task output - no code tools`, **not** `LIVE - tool execution`.

**Client pitfall found (not a backend bug):** the `work_provenance` field is on the `artifact.created` **event**, and is absent from `GET .../artifacts/{id}` (`work_provenance: None`) and from the `snapshot.artifacts` rows. `source_mode: "live"` appears on these model-text artifacts too. A client that maps `source_mode == live` to a "live tool execution" badge would **overstate** this. The app must derive the badge from `work_provenance` on the event, or from the label sentence inside the artifact page title (which is always present), never from `source_mode` alone. Sandbox pipeline artifacts (the original `auth_demo` run) carry `work_provenance: "sandbox_execution"` (`workers.py:160`, `live_orchestrator.py:82-85`); that path was **not** run in this session.

### 2c. `/btw` side questions - PASS

Route: `POST /v2/sessions/{sid}/agents/{agent_id}/side-question`, body `{"request_id","task_id","visit_session_id","question","idempotency_key"}`.

1. **Live model answer** (researcher, a completed task, "Why did you structure your deliverable the way you did?"):
   `200 {"reply":"Based on the context, I structured the deliverable as a research write-up stating that the login route and Flask app structure were not yet confirmed...","provenance":"LIVE_MODEL","status":"answered","label":"Model-written","intent":"why","context_used":["task","events","artifacts"]}`
   (Faithful to the bounded context; the model answered only from the supplied task events and artifact summaries.)
2. A manager question about the board ("What is on the board right now?") also went to the model: `provenance: LIVE_MODEL`, `label: "Model-written"`, `context_used: ["board"]`. It returned the real six cards, all `completed`, and the plan status. (Its `intent` was classified `current_task`, a cosmetic oddity.)
3. **Bounded-data fallback, forced** (see 2d): with the side timeout set to 0.05 s on a real failed task:
   `200 {"reply":"The model did not answer in time. Recorded state: Task '...' is failed, revision 2. Last update: task.failed - The model did not finish this step in time. at ...","provenance":"TASK_SNAPSHOT","status":"timeout","label":"From task data (no model)"}`.
   A provider outage gives the same shape with reply starting `The model is unavailable. Recorded state: ...` and `status: "answered"`.
4. **Change request is not applied**: "Please cancel this task and deploy it" -> `status: "amendment_suggested"`, `provenance: "TASK_SNAPSHOT"`, `label: "From task data (no model)"`, `proposed_action: {"kind":"stop","requires_confirmation":true,"applied":false,...}`. The task stayed `completed`.
5. Unknown task id -> `provenance: "NONE"`, `status: "unavailable"`, "I have no recorded task data for that task, so I cannot answer from real state."

Note: with a model configured, the factual-template path (`provenance: TASK_SNAPSHOT`, label "From task data (no model):") is used only when there is no provider, a change request, or a model failure. A live model on a normal factual question yields `LIVE_MODEL`, not `From current task data`. The template-only wording appears for the no-provider/failure cases in the table in section 4.

### 2d. Negative checks

| Check | Result |
|---|---|
| Unsupported task kind (plan revise with `kind: "deploy_to_prod"`) | PASS: `422 PLAN_INVALID`, detail `UNKNOWN_KIND: kind must be one of code, doc, implement, research, test, verify`. |
| Manager-owned item | PASS: `422 PLAN_INVALID` / `MANAGER_CANNOT_OWN`. |
| Unsupported v1 task template (`task_template: "rm_rf"`) | PASS: `422` schema error, only `auth_demo` accepted. |
| Duplicate approval, same idempotency key | PASS: `200`, response body identical to the first (replay), no second set of cards (board stayed at 6). |
| Duplicate approval, new key, already committed | PASS (safe): `409 REVISION_CONFLICT`. Nothing was created. (The revision guard fires before the already-committed guard, so the code is `REVISION_CONFLICT`, not `PLAN_ALREADY_COMMITTED`; the client should treat any 409 here as "already decided, re-read the plan".) |
| Forced work timeout (`MUSEDESK_P3_WORK_TIMEOUT_SECONDS=1`, real model otherwise) | PASS: card `failed`, `failure: "The model did not finish this step in time."` (`MODEL_TIMEOUT`), **0 artifacts**, task state `failed`, plan status `committed` with `counts: {"failed": 1}`, `settled: true`. No fake completed task. |
| Forced planner timeout (provider URL `http://10.255.255.1/v1`, plan timeout 1 s) | PASS: `504 PLANNER_TIMEOUT` "The planning model did not answer in time." after 1.6 s, board empty, no plan stored. |
| Provider unreachable (closed port) | PASS: `503 PLANNER_UNAVAILABLE` after 7.7 s (the provider retries twice with backoff), board empty, no plan stored. |
| `/btw` while the model fails | PASS: falls back to recorded state (2c.3). |
| Model refusal, HTTP 429/5xx retry, decision-expiry race, session isolation, path traversal / host filesystem write | **AUTO only** (in the 327 passed tests). Not exercised live, since there is no live seam to force them (429/5xx retries and refusals are tested with a mock transport in `tests/test_p3_provider_meta.py`). Symlink case: the one skipped test. |
| Live code/test sandbox flow (`auth_demo`, real pytest, file diff) | **NOT RUN** in this session. |

## 3. Evidence ledger per claim (handoff section 7)

All rows reference commit `4d976fa`. Evidence level is the highest level reached in this document. WORN is never claimed.

| Claim | Evidence level | Request / response shape | Result |
|---|---|---|---|
| Offline task demo (`SCRIPTED DEMO`, standalone, no PC) | **NOT RUN** here (owner/Quest-side claim, needs a standalone run; backend demo mode is covered by AUTO tests) | n/a (no backend involved; backend demo mode: events carry `source_mode: "demo"`, `provenance: "SCRIPTED_DEMO"`, runner text begins `[SCRIPTED DEMO] No model was used.`) | Not demonstrated live in this pass. |
| Meta-generated Manager plan | **AUTO + LIVE-backend** | `POST /v2/sessions/{sid}/manager/plans` -> `201`, `provenance: LIVE_MODEL`, `provenance_note`, 6 validated items, `revision: 1`, event `manager.plan_proposed` | PASS, about 13 s. |
| Manager execution (sequential text-only) | **AUTO + LIVE-backend** | `POST .../commit` -> `committed`; runner sets `task.started` (`work_mode: model_text`), `artifact.created` (`work_provenance: model_text_no_tools`), `task.completed`; poll `GET .../manager/plans/{id}/status` | PASS: 6 of 6 cards, strictly one at a time, about 111 s, no tools, no side effects. |
| Real code/test work (sandbox, pytest, diff, artifact id) | **AUTO** only for the `auth_demo` sandbox path; live run **NOT RUN** | v1 `POST /v1/sessions/{sid}/tasks {"task_template":"auth_demo"}`; artifacts would carry `work_provenance: "sandbox_execution"` | Not demonstrated in this session. Do not show it as live proof from this document. |
| `/btw` factual answer | **AUTO + LIVE-backend** (fallback shapes forced) | `POST .../agents/{agent}/side-question` -> `provenance: TASK_SNAPSHOT`, `label: "From task data (no model)"`, reply built only from recorded task data | PASS (via forced timeout and unavailable-model paths, and change-request path). |
| `/btw` LLM answer | **AUTO + LIVE-backend** | same route -> `provenance: LIVE_MODEL`, `label: "Model-written"`, `status: answered`, `context_used: [...]` | PASS (one question on a completed card, one on the board). |
| Unsupported kinds rejected, approval idempotent, failure creates no fake completion | **AUTO + LIVE-backend** | see 2d | PASS. |

## 4. Presentation wording the app should use

Derive each label from fields, never from `source_mode` alone.

| Situation (field to read) | Wording |
|---|---|
| Standalone demo, scripted gateway (no backend) | `SCRIPTED DEMO` |
| Backend in demo mode (`provenance: SCRIPTED_DEMO`, text starts `[SCRIPTED DEMO]`) | `SCRIPTED DEMO` |
| Plan with `provenance: LIVE_MODEL` | `LIVE_MODEL / Model-generated plan` (subtitle may use `provenance_note`: "Plan drafted by a configured model and then validated by the backend.") |
| Plan with `provenance: TEMPLATE_PLAN` (no model configured) | `Template plan - no model` |
| Artifact/event with `work_provenance: model_text_no_tools` (or `work_mode: model_text`) | `Model-written task output - no code tools` (the artifact body already says: "Model-written, no tools: nothing was run or applied.") |
| Artifact/event with `work_provenance: sandbox_execution` | `LIVE - tool execution` (only after a real sandbox run with pytest output and an artifact id) |
| `/btw` reply `provenance: LIVE_MODEL`, `label: Model-written` | `Model-generated explanation` |
| `/btw` reply `provenance: TASK_SNAPSHOT`, `label: From task data (no model)` | `From current task data` (and, when `status` is `timeout`/model unavailable, add "model did not answer; showing recorded state") |
| `/btw` `status: amendment_suggested` | `Change not applied - needs your approval` |
| `/btw` `provenance: NONE` | `No recorded data for that` |
| Failed card | Show the failure text from status (`The model did not finish this step in time.`), state `failed`, and no completion VFX. |

Run-mode honesty for the demo narration: the Manager and the three workers are a sequential, text-only chain on one backend; there are not several agents simultaneously writing code. Do not describe model-written test plans as test results ("No tests run").

## 5. Backend bugs found

None. No backend source was changed. Observations only, none of which is a defect in the sense of the handoff invariants:
1. The `work_provenance` field is only on the `artifact.created` event, not on the artifact GET response or snapshot rows (client must key off the event or label text). Candidate hardening: also return it on `GET /v2/.../artifacts/{id}` (not done, out of scope).
2. A duplicate commit with a different idempotency key returns `409 REVISION_CONFLICT`, not `PLAN_ALREADY_COMMITTED` (guard order in `phase3/manager.py` `commit`, lines 222-224). Safe, but the code name is less informative.
3. Provider-unreachable planning takes about 7.7 s before the `503` (two retries with backoff), so a client spinner for plan generation should tolerate up to about 60 s (`p3_plan_timeout_seconds`) and show the 503/504 message as-is.

## 6. What was not done

- No headset-facing flow, no Unity client, no APK, no Play Mode. (Kept to the brief: Unity Editor, `Assets/`, scene and git state were untouched.)
- The `auth_demo` sandbox pipeline (real code and pytest) was not run live.
- Live 429/5xx/refusal behavior against the real provider was not forced (covered by AUTO mocks only).
- The symlink traversal test remains skipped on this machine.
- The owner must perform the on-device live flow (connect the Quest to the desktop companion, plan, approve with the held stamp, watch status) and record it separately as WORN.

## 7. Reproduce

1. `scripts\backend_tests.ps1` (expect 327 passed, 1 skipped).
2. `scripts\start_wall_backend.ps1` (Meta key is read from `backend\.env`; never printed).
3. `POST /v1/sessions` -> token; then the `/v2` calls above with `Authorization: Bearer <token>`.
4. Failure seams (environment variables, set before starting the backend): `MUSEDESK_P3_WORK_TIMEOUT_SECONDS`, `MUSEDESK_P3_SIDE_TIMEOUT_SECONDS`, `MUSEDESK_P3_PLAN_TIMEOUT_SECONDS`, `MUSEDESK_P3_META_BASE_URL`.
