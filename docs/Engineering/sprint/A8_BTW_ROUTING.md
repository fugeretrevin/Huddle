# A8 - /btw routing, proposed actions, honesty labels

Endpoint unchanged: `POST /v2/sessions/{sid}/agents/{agent}/side-question`. The desk-visit
avatar is a presentation of the same logical agent; /btw never pauses, edits or starts work.

## Intents (deterministic, `phase3/side_intent.py`)
Negated clauses ("not asking about the tests") are ignored when scoring.

| intent | trigger examples | real context attached |
|---|---|---|
| test_failure | "explain that test failure" | task, test_report artifacts, failure events |
| blocking | "what is blocking you", "stuck" | task, blocked/failed events, recent events |
| file_change | "what changed in this file", "diff" | task, code_diff artifacts |
| completed | "what have you completed" | task, completed events, artifacts |
| why | "why this approach" | task, events, artifacts (plans/research) |
| current_task | "what are you working on" | task, recent events |
| status | anything else | task, recent events, artifacts |
| change_request | stop / change X / skip the tests / approve it / deploy | no context to a model; never executed |

Context is bounded: 5 events, 5 artifacts, 140 chars/item, 1800 chars total to a model.
Empty context is stated ("No test report or failure is recorded for ..."), never invented.

## Change requests -> `proposed_action`
Status `amendment_suggested`; nothing is applied, the model is not called.
```
proposed_action: { kind, negated, target_task_id, target_agent, text,
                   requires_confirmation: true, applied: false, summary }
```
kind: `stop | approve | deploy | skip_tests | run | change`; negated forms (e.g.
"don't approve") become `withhold_approval | keep_running | keep_tests | hold | keep_as_is`
with `negated: true` - the client must never show "Approve" for them.
`proposed_amendment` is kept as a legacy alias.

## Response additions (all additive)
`label` ("Model-written" | "From task data (no model)" | "[SCRIPTED DEMO]" | null when
unavailable), `intent`, `context_used` (list of section names), `truncated`, `deduped`,
`proposed_action`. No-model replies are prefixed in `reply` with the label; demo replies
keep the `[SCRIPTED DEMO]` prefix; model replies are unprefixed (show `label` as a chip).

New `status` values: `rate_limited` (12 questions / 30 s per visit; not cached, retry later),
`model_declined` (provider refusal; reply is a safe message plus recorded state, label
"From task data (no model)", not cached). Existing `timeout`, `busy`, `unavailable` unchanged.
Model replies over 1200 chars are cut at a sentence boundary and end with "[truncated]".

## Scoping and dedupe
- A task id from another session is `unavailable`; a task owned by a different agent than
  the one in the URL is also `unavailable` (the `manager` may ask about any task of its session).
- Identical question (case/punctuation-insensitive) in the same visit, same task revision,
  within 4 s returns the prior answer with `deduped: true` (no second model call). The existing
  `idempotency_key` replay is unchanged.

Files: `backend/app/phase3/side.py`, `side_intent.py`, `tests/phase3/test_p3_side_routing.py`.
