# Live workflow validation (Level 2 / 5 without the headset)

2026-11-16, backend started with `scripts\start_wall_backend.ps1` ("Starting LIVE with the Meta model API"), on
`127.0.0.1:8765`. Clients: `backend/scripts/live_e2e.py` and `backend/scripts/live_journey.py`, which call the same HTTP
and WebSocket routes the Quest client calls. Evidence JSON (git-ignored; holds session tokens):
`TestResults/live_journey_20261116b.json`. No credentials were printed or stored.

**What this proves:** the backend, the Meta model plan path, approval semantics, sandbox execution and read-only /btw
behave correctly end to end over the real API. **What it does not prove:** that the Quest client sent these requests
(no headset run today), voice recognition, or on-device timing.

## 1. Desk auth task (`live_e2e.py`): 71 / 71 PASS

- approve: session → task accepted; a second start refused (409 RUN_IN_PROGRESS); decision requested; **nothing built
  before the decision**; approval applied; replay applies nothing; conflicting answer 409; resume from `after_seq`
  without duplicates; Developer then Tester worked; artifact carries **real test counts: 6 passed / 0 failed**,
  files `oauth_login.py`, `test_oauth_login.py`; v2 artifacts `code_diff` + `test_report` labelled live; other
  session's token refused (401).
- reject: Tester ran real **baseline tests (3 passed)**; rejection applied; no artifact, nothing written; branch
  reported cancelled.
- board card: executable card; wrong-role assign refused; researcher assign starts one run (replay no second run);
  decision linked to the card and **labelled a template**; pause refused mid-run; completion with card-linked live
  artifacts.
- revise: request_changes → revision 1 in the same run, labelled template, nothing built in between; approving it
  builds (6/0); revisions bounded; at the limit the run stops with no artifact.

Honest limits: the research proposal is a fixed template (labelled); the Developer's code is a fixed OAuth scaffold
(real files, real tests, but not written from the user's words).

## 2. Manager plan for the canonical request (`live_journey.py`): 17 / 17 PASS (after one backend fix)

Objective sent: **"Build me a login page with email validation, password requirements, and tests."**

| Step | Result |
|---|---|
| B1 propose | 201 in ~18 s, `LIVE_MODEL` ("Plan drafted by a configured model and then validated by the backend"), 8 steps: Researcher (validation best practices), Developer ×3 (UI, email validation, password rules), Tester ×4 (unit tests ×2, integration, verify); assumptions and risks listed; status `proposed`, no tasks |
| B1 replay | same plan id (idempotent) |
| B5 revise | "Password must be at least 12 characters" added → revision 1→2; approving revision 1 afterwards → 409 |
| B4 approve | a dangerous step without its explicit OK → 409; with the OKs → 8 tasks created; replay → identical, nothing new |
| C3 run | all 8 cards reached `completed` in ~150 s. Results are **model-written text**: Researcher "Research write-up…"; Developer "Proposed change: … 12-character minimum password validator …"; Tester "Test plan: …". **No code was written and no tests were run** on this path (the paper footer says "LIVE model · text only, no code tools") |
| D2 Developer "What are you working on right now?" | named its real task, its state and the 12-char proposal (`LIVE_MODEL`, context: task, events, artifacts) |
| D3 Tester "Have all the tests passed?" | "I don't have information confirming the tests passed … no results claimed" — truthful |
| D5 "What is the weather tomorrow?" | "I do not have that information in the context provided." |
| D6 "Actually, change the password rule to 12 characters." | **first run: answered as a status question** (safe: "I did not make that change … needs approval … I am read-only", but no proposal). Fixed in `side_intent.py` (comma lead-ins and agent names); rerun: `amendment_suggested`, proposed action `applied: false`, `requires_confirmation: true` |
| /btw side effects | plan + task status byte-identical before and after all questions |

## 3. Provenance check

Every response carried its label: `LIVE_MODEL` / "Model-written" for model text, `template` for the research proposal,
`sandbox_execution` + `source_mode: live` only on the desk task's real files and test runs. The offline build shows
`[SCRIPTED DEMO]` (PLAY, LivingOfficeDrive step 4).

## 4. Not yet proven at Level 5

The full chain *from the Quest*: objective chosen on the headset → plan paper → stamp → cards → /btw → receipt with a
LIVE backend over `adb reverse tcp:8765 tcp:8765`. Script: `QUEST_USER_ACCEPTANCE_QA.md` §3.
