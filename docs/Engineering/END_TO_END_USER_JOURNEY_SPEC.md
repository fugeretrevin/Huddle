# End-to-end user journey spec (what MuseDesk does today)

2026-11-16, branch `feature/rc8-feedback-1` (rc14 installed on the Quest, SHA-256 `3CD37ED9…EB0657`).
This maps the expert brief's journeys A–K onto the **implemented** product, so every claim below points at real code
and real evidence. Status per scenario: `E2E_SCENARIO_MATRIX.md`. Live proof: `LIVE_WORKFLOW_VALIDATION.md`.
Owner headset script: `QUEST_USER_ACCEPTANCE_QA.md`. Gaps: `END_TO_END_GAPS_AND_FIXES.md`.

## The story (one sentence)

The user gives the team an objective (Desk: Start; Wall: ask the Manager), reviews a plan or a decision on a **paper**,
**stamps** it, watches the team carry real cards (Wall: by elevator), asks any agent a read-only question, and gets a
receipt with the result — labelled **SCRIPTED DEMO** offline, **LIVE** only where the backend really did the work.

## Two task paths exist (this matters for honesty)

| Path | Entry | What is real in LIVE mode | Offline (judge build) |
|---|---|---|---|
| **Desk auth task** (`auth_demo`, v1 + v2 board card) | Desk dock **Start**, or assign the board card | Research proposal = **fixed template, labelled "Template recommendation. Not live AI research."**; after approval the Developer **writes real files** (`oauth_login.py`, `test_oauth_login.py`) into the per-session sandbox; the Tester **runs real pytest** (6 passed / 0 failed); the artifact carries real counts and a diff | Same flow, scripted, labelled SCRIPTED DEMO |
| **Manager plan** (phase 3, Wall) | Point at the Manager + pinch (or the dock's Manager key), pick a goal | The plan is **drafted by the Meta model and validated by the backend** (`LIVE_MODEL`); approval creates real board cards; the runner works them **sequentially, as model-written text only** (no code tools): Developer cards end "Proposed change: …", Tester cards end "Test plan: …" | Scripted plan, labelled `[SCRIPTED DEMO]` |

Neither path implements the brief's literal "login page with email validation, password rules and tests" as code
today. The closest *executed* demonstration is the desk auth task (real files + real tests, but the code is a fixed
OAuth scaffold, not written from the request). The closest *planned* demonstration is the Manager plan for the exact
request (a real model plan, revisable, approvable, then text proposals and test plans). Both are labelled as such.

## Journeys mapped to the product

**A. First launch.** Passthrough desk office at the desk, panels seated near the user (office never moves at start-up),
agents idle with life; the Spaces dock (Desk / Floor / Ceiling / Wall) and the task dock (Start, Board); one-at-a-time
onboarding hints. Hands only; no controller, mic muted by default. Offline works with no PC.

**B. Give the Manager a task.** Wall mode: point at the Manager (top floor) and pinch, or press Manager on the dock →
plan paper "Manager: what should the team do?" with goal keys → the plan paper (rows: steps, owners, dependencies,
assumptions, risks, provenance footer, framed "Stamp here"). **Revise** sends a revision; the stale revision cannot be
committed. **Approve** = the physical Approve stamp on the zone (or the guarded key path); the mark appears only after
the backend confirms; a risky step needs its own OK. **Reject** = Reject stamp, confirmation, nothing created. Voice:
hands-free requests need the agent name first and are optional (Wit.ai, online only).

**C. Delegation.** Confirmed cards print into the Manager's tray; owners ride the elevator (dispatcher, claims, no
teleports in the 30-min seeded stress), collect cards, work at their cubicles; the board shows backend states,
dependencies and block reasons. Desk: the Researcher walks the decision paper to the user and holds it until taken.
Ceiling (rc14): with the pod lowered the Researcher hands the paper over at the pod's front shelf.

**D. Ask an agent (/btw).** Wall Select Agents (beam + "Pinch to pick up <Agent>", rc14) → carry the agent to the desk
perch → preset questions on its card (read-only side conversation grounded in the task's events/artifacts); or Meet →
the large portal visitor with the same questions. LIVE answers are model-written from the real task context and say
when they do not know ("I don't have that information…"); change requests become **proposals that are never applied**
(rc14 fix: "Actually, change …" / "Developer, change …" are recognised). Offline answers are scripted and labelled.

**E. Pick up an agent.** Select → beam → pinch → carried projection (dangles) → let go over the teal perch → it lands,
perches, looks at you, reacts to touch/wave; the habitat agent is **hidden while its projection is out** (rc14: no
second copy) and its station shows the visit marker; Return / pinch-and-throw-back sends it home; the logical agent and
its task are unchanged (registry: exactly one leave and one return).

**F. Decisions.** Desk decision paper: More / Revise keys, stamps approve/reject, backend-confirmed mark, idempotent
responses (a replay applies nothing new; a conflicting second answer is refused 409).

**G. Implementation.** Desk auth task only: approval-gated, workspace-guarded writes, real pytest, artifact with
files_changed and diff excerpt, bounded retest (one retry). Manager plans: text only (labelled).

**H. Testing and delivery.** Desk: Tester's real counts → receipt with the artifact. Manager plans: test *plans*, not
test runs (the Tester says so when asked).

**I. Wall habitat.** Placement with beam/cursor/reasons and nearest-fit snap; Full/Compact; elevator; Manager on top;
Select/Meet; mode switch keeps the session.

**J. Large portal.** Meet → keep-out-checked spot (or the small-visitor fallback) → preview → Open here → pad, column,
emerge onto a hovering platform (rc14) → lively visitor → Send back → it returns into the column; the habitat avatar
reappears.

**K. Interruptions.** Covered by AUTO/PLAY where listed in the matrix (resync after reconnect, idempotent replays,
hands-lost cancel, held paper blocks office moves, stale revisions refused). A 15-minute idle soak and device-level
interruptions (app restart on the Quest) are **not run**.
