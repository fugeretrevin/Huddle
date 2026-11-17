# MuseDesk demo storyboard (2:50 film, rc16)

2026-11-16, evening. This builds on the expert's plan (`Downloads/03_QUEST_QA_DEMO_AND_SUBMISSION.md` §E), the readiness table
`JUDGE_DEMO_VALIDATION.md` and the older shot list `DEMO_SCRIPT_AND_SHOT_LIST.md` (desk walkthrough, still valid). It replaces
none of them. What changed today, and why the film can now centre on **real work**:

- **The team does real work (LIVE).** With `scripts\start_wall_backend.ps1` and the Meta model:
  - the Manager plans with the model;
  - each Developer card writes real files into a sandbox folder on the PC;
  - each Tester card writes real pytest tests;
  - pytest really runs.
  - Run 2 (`backend/scripts/live_real_work.py`, 9/9 checks):
    - 7 cards, all completed;
    - the final folder holds 116 tests, all passing;
    - an independent pytest run of the folder matched the card exactly.
  - Evidence: `Logs/live_real_work_evidence.json`.
- **Worn today (rc15), all PASS:**
  - Ceiling rugs;
  - the Researcher handing over at the pod's front shelf;
  - the Select beam;
  - "Open here";
  - the portal visitor's head and body (no spin, no roll);
  - 72 fps in every scenario (`Logs/quest/perf_rc15_worn.csv`).
- **rc16 fixes** (desks facing their agents, nameplates turning to you, "Researcher, why?" heard reliably) still need a worn check.

## The story in one line

A developer juggling agent windows gets a small physical team on the desk, gives the Manager one real goal, stamps the plan,
watches the team write and test real code, asks one of them what it is doing, and inspects the result.

## Honesty rules for the film (keep them visible)

- Show the paper's footer **"LIVE model · code + tests really run in a sandbox"** at least once, and the status rows reading
  `Code written: … · pytest N passed, 0 failed` / `Tests run: N passed, 0 failed`.
- The run takes about 6–7 minutes for 7 cards. **Compress it with an on-screen "6 minutes later" caption**: never cut so it looks instant.
- If a card fails, keep it: "failed, fixed once, passed" is a strength. Never hide a red card.
- The offline (no PC) build is **SCRIPTED DEMO**. If any offline footage is used, the label must be readable in the frame.
- The desk **Start** task's code is still a fixed starter (OAuth scaffold). Its tests are real. Do not narrate it as written from the request.
  The request-specific code comes from the **Manager plan** path.
- Research cards are model text (no web, no tools). Say "the Researcher's write-up", never "it researched the web".
- Caption the PC cutaway honestly: "the same folder on the PC: real files, real pytest".

## Storyboard

Timing targets 2:50 (judges may stop at 3:00).

**Evidence used in the "Ready?" column:**
- **WORN:** you wore it on the headset.
- **LIVE:** verified against the real backend and model.
- **PLAY:** verified in the Unity Editor only.

| # | Time | Beat | Shot (what the camera sees) | User does | On screen / caption | Ready? |
|---|---|---|---|---|---|---|
| 1 | 0:00–0:08 | Problem | PC monitor with several agent chat windows; slow pull back to the desk and headset | — | "Five agents. Five windows. Zero overview." | Film anything (no real product logos) |
| 2 | 0:08–0:20 | Meet the team | Headset view: three small agents at their desks. Each looks up and waves; nameplates turn toward you as your head moves | Poke one agent | "Your AI team, on your desk." | WORN (life, poke). Desk facing and nameplates are new in rc16: **wear rc16 first** |
| 3 | 0:20–0:34 | The office grows | Wall mode: preview on the real wall, **Place**; the treehouse grows out of the wall; agents take the elevator | Spaces → Wall → Place | "It moves into your room." | WORN rc11–rc15 |
| 4 | 0:34–0:50 | One real goal | Open the Manager; pick the login goal (or say "Manager, plan a login page with email validation, password rules and tests"). The plan paper fills: steps, owners, risks; footer "LIVE model · code + tests really run in a sandbox" | Manager → goal | "One goal. The Manager plans it with the Meta model." | LIVE + PLAY. Footer new in rc16 |
| 5 | 0:50–1:02 | Stamp it | Close on the hand pressing the stamp; APPROVED appears only after the backend confirms; cards print; owners ride the elevator to their desks | Stamp Approve (OK the risky step if asked) | "Nothing runs until you stamp it." | WORN (desk stamp), PLAY (plan stamp 13/13) |
| 6 | 1:02–1:20 | Work happens | Status page rows changing: "Code written: login_page.py, test_login_page.py · pytest 7 passed, 0 failed", then "Tests run: 61 passed, 0 failed". Cut on the "6 minutes later" caption | Watch | "Real code. Real tests. In a sandbox." | LIVE (9/9). **Film from the headset** |
| 7 | 1:20–1:36 | Ask a teammate | Wall → Select agents: the beam bends to the Developer, "Pinch to pick up Developer"; carry it to the perch; ask "Developer, what are you working on?"; the reply names its real card | Select, pinch, carry, ask | "Ask anyone, any time. Nothing changes unless you approve." | WORN (beam, rc15); LIVE (reply). Voice: wear rc16 first |
| 8 | 1:36–1:50 | Proof | PC cutaway: the sandbox folder (`backend\sandbox_workspaces\plans\<plan>`), the code file, then `pytest` printing "116 passed" | — | "The same folder on the PC: real files, real pytest." | LIVE (independent run matched) |
| 9 | 1:50–2:15 | Spatial moment (pick ONE) | **A.** Meet → Open here: the visitor rises on its hovering disc and follows you with its head as you lean around it. **B.** Ceiling: the pod lowers, the Researcher walks to the front shelf and holds out the paper | Meet / Ceiling | "Bring one closer." | **Both WORN PASS on rc15.** Pick A for wow, B for calm |
| 10 | 2:15–2:35 | Decide and finish | Desk: the Researcher brings the decision paper; read it, stamp it; the handoff; the receipt "6 passed / 0 failed" on the desk | Take paper, stamp | "You stay in charge." | WORN (desk loop). The desk code is a fixed starter: caption the tests, not the code |
| 11 | 2:35–2:50 | Close | Quiet desk, agents back at work, the plan summary receipt; title card | — | "MuseDesk: supervise AI agents as a tiny team you can see, stamp and talk to." | — |

**Fallback film:** if the live backend is unavailable on the day, use beats 1–3, 5, 7 (preset question), 9 and 10 offline. Then:
- Keep SCRIPTED DEMO visible.
- Replace beats 6 and 8 with the backend run recorded on the PC (`live_real_work.py` output plus pytest).

## Recording set-up (live segment)

1. PC: `scripts\start_wall_backend.ps1`. It must print "Starting LIVE with the Meta model API" and "Developer/Tester plan
   cards write REAL code …".
2. USB: `adb reverse tcp:8765 tcp:8765`, then start MuseDesk. The dock and paper must read **LIVE**, not SCRIPTED DEMO.
3. Before the take, run a 10-second test recording on the Quest and check that passthrough and the app both appear in the clip.
4. Optional: in a second terminal, run `scripts\quest_perf_watch.ps1 -Label rc16_film`. It records frame rate while you film.
5. After the run, for beat 8, film the PC:
   - `cd backend\sandbox_workspaces\plans\<newest folder>`;
   - `..\..\..\.venv\Scripts\python.exe -m pytest -q`.
6. Keep `Logs/live_real_work_evidence.json` and the plan id. Together they back up the numbers on screen.

## Shot list (capture order, fastest first)

| Shot | Beat | Framing | Notes |
|---|---|---|---|
| S1 | 2 | Eye height, desk centred, all three agents and plates | Move your head slowly left/right so the nameplates visibly turn |
| S2 | 3 | Wall, wide | Hold 3 s on the finished treehouse |
| S3 | 4 | Paper close, footer readable | Hold 4 s; the footer must be legible |
| S4 | 5 | Hand + stamp macro | One press |
| S5 | 6 | Status page, rows readable | Two moments: early (first row) and late (all rows) |
| S6 | 7 | Beam on the Developer, carry, perch, reply card | Speak the question clearly; start with the name |
| S7 | 8 | PC screen | Folder listing → code file → pytest result |
| S8 | 9A | Visitor on the disc, lean around it | 5 s of head-follow |
| S8b | 9B | Pod lowering, shelf hand-over | Alternative |
| S9 | 10 | Desk paper, stamp, receipt | Receipt legible |
| S10 | 11 | Wide quiet desk | End frame for the title |

## Before filming: wear rc16 for 10 minutes

From `QUEST_USER_ACCEPTANCE_QA.md`:
- Desk: each agent stands at its desk, the monitors face the agent, and there is no chair on your side.
- Nameplates turn smoothly toward your head and never flip.
- Mic on: say "Researcher, why?". A reply card appears. If you say something that names an agent but doesn't start with
  the name, the hint "Start with a name" appears instead.
- Live: the Manager paper's footer says "code + tests really run in a sandbox", and the status rows show real pytest counts.
