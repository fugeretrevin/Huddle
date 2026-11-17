# rc17 final hardware QA (owner script)

This is the authoritative worn-test script. It replaces `QUEST_USER_ACCEPTANCE_QA.md` for rc17; that file keeps the rc15 results.

- **Build:** `Builds/MuseDesk_rc17_production.apk`, label `rc17-production`.
  - SHA-256 `BD0204F4556E6A301379A09F0ED986B9797273A989FC71AA222EEA0A17B602AD`, commit `7220f0a`.
  - Installed on the Quest and hash-verified (2026-11-16).
- **Rollbacks:** rc16 `D25583D1`, rc15 `047DD408`, rc14 `3CD37ED9`, rc13 `3DAF3AE6`.
- **Rule:** only the owner's observations below can make anything WORN. Mark each line PASS / FAIL / NOTE, with a few words.
  - Automated and Editor results are in `FINAL_POLISH_FIX_LOG.md` and `Logs/preflight/`.

## Before you put it on (2 minutes)

1. On the PC, start the frame-rate recorder: `scripts\quest_perf_watch.ps1 -Label rc17_worn`.
   - It waits for MuseDesk to open, then records for up to 30 minutes.
2. **Offline first (B1–B8).** Leave the backend OFF. The app runs the labelled SCRIPTED DEMO.
3. **The live session (section E) comes after,** with the backend on.
4. Seated at your real desk, with good light and the real monitor in front of you.
5. Say what you see out loud if you're recording: it makes the notes easy afterwards.

## B1: Cold launch

| # | Do | Expect | Result |
|---|---|---|---|
| 1 | Open MuseDesk; look around the desk | The office and dock appear at your desk; nothing jumps or relocates by itself | |
| 2 | Find the Researcher, Developer and Tester | Each stands at its own desk with its monitors facing it (rc16 change) and its chair behind it; no chair on your side | |
| 3 | Lean left/right, then stand up and sit down | Nameplates turn and tilt toward you smoothly; they stop at about 60° and never flip | |
| 4 | Look at the dock | Within easy reach; it does not cover your real monitor | |
| 5 | Poke an agent | It reacts (a glance or a wave), then goes back to work | |

## B2: Desk workflow (offline, scripted)

| # | Do | Expect | Result |
|---|---|---|---|
| 1 | Press **Start** | The dock reads "In progress"; the Researcher works | |
| 2 | Wait for the Researcher to bring the paper | It walks around its desk (no walking through furniture: rc16 desks moved) and holds the paper out until you take it; no give/take-back loop | |
| 3 | Take the paper, expand it, press **More** | Readable text at arm's length; the SCRIPTED DEMO label is visible | |
| 4 | Press the **Approve** stamp | One clear target; the APPROVED mark appears only after confirmation | |
| 5 | Watch the handoffs | Researcher → Developer → Tester read as a sequence; the bubbles are labelled | |
| 6 | Open the receipt | It names files and test counts, labelled SCRIPTED DEMO | |

## B3: Wall Habitat and Manager (offline)

| # | Do | Expect | Result |
|---|---|---|---|
| 1 | Spaces → **Wall**; look at a real wall; adjust; **Place** | The ghost follows; a refused spot says why; the treehouse grows on the wall and is not buried in your monitor | |
| 2 | Press **Manager**, pick the goal | The cream plan paper is readable; steps, owners and risks are shown | |
| 3 | Stamp **Approve** | The cards print only after confirmation | |
| 4 | Watch the owners collect their cards | The elevator doors and riders line up; no teleporting home; no clipping | |
| 5 | Open the board / status page | The cards match the plan steps; the states advance | |

## B4: Select an agent

| # | Do | Expect | Result |
|---|---|---|---|
| 1 | Press **Select agents**; point at the Developer | A steady beam, teal on the agent: "Pinch to pick up Developer" | |
| 2 | Pinch near a key on purpose, then on the agent | No key is grabbed by mistake; the agent comes with your hand | |
| 3 | Carry it to the perch; release | Grounded landing; it looks at you; its desk on the wall is empty while it visits | |
| 4 | Press **Status?** | A readable, labelled answer | |
| 5 | Press **Return** | It goes home; no copy is left behind | |

## B5: Large portal

| # | Do | Expect | Result |
|---|---|---|---|
| 1 | **Meet** → pick an agent → a proposed spot → **Open here** | A comfortable spot, not in your work area | |
| 2 | Watch it emerge | It steps out of the light column onto the hovering disc | |
| 3 | Move your head; reach toward it; poke gently | The gaze follows naturally (not a constant stare); a subtle lean; no spin, roll or limb flip (rc15 PASS: recheck) | |
| 4 | Ask a question; **Send back** | It returns into the column; the original reappears at its desk; no duplicate | |

## B6: Floor and Ceiling (current build)

| # | Do | Expect | Result |
|---|---|---|---|
| 1 | Spaces → **Floor**; look down; Place | The office lands on the floor deck; papers still reach you | |
| 2 | **Desk** | It returns to your desk | |
| 3 | Spaces → **Ceiling**; look up; Place; lower the pod | The rugs don't overlap (rc15 PASS); the desks face their agents (new); the Researcher hands over at the front shelf | |
| 4 | Raise the pod; press Stop mid-move once; then **Desk** | It halts and resumes cleanly; it comes home | |

## B7: Voice (real microphone, Wit.ai; Quest on Wi-Fi)

Settings → mic **ON**. Each phrase must start with the name. Say each one twice: once normally, once fast.

| # | Say | Expect | Result |
|---|---|---|---|
| 1 | "Researcher, why?" (failed on rc15) | A reply card | |
| 2 | "Developer, what are you working on?" | An answer about its task | |
| 3 | "Manager, what's the status?" | A status answer | |
| 4 | "Tester, come here." | (Wall placed) the Meet preview for the Tester | |
| 5 | "why researcher" (no name first) | The quiet hint "Start with a name"; nothing acts | |
| 6 | Talk normally for 30 s about lunch | Nothing pops up | |
| 7 | Mute / unmute | The key shows the state; muted hears nothing | |
| 8 | Wi-Fi off, then press the mic | "Voice needs internet. The keys still work." | |

## B8: Apply change (offline first; live in E)

| # | Do | Expect | Result |
|---|---|---|---|
| 1 | After B3's plan: Select the Developer → perch → say or ask "Developer, can you change the password minimum to 12 characters?" | The card: "… NOT applied …", with an **Apply change** key in place of "Next?" | |
| 2 | Press **Apply change** once, then look for the key again | "Change added as a new step"; the key is gone (no duplicates) | |
| 3 | Watch the Manager's tray | A "Change: …" card prints; the Developer collects it; it completes (scripted offline) | |

## D: Performance (watcher running throughout)

Pass = steady at the headset rate (72 fps at 72 Hz), and no hitch over 150 ms outside scene changes.
- **Covered by B1–B8:** Desk idle, board open, paper and stamp, Wall + elevator, visit, portal, Floor, Ceiling.
- **Still to do:** after B8, leave the app on the Desk for **15 minutes**. Then force-close it and reopen it (restart and resume).
- **Afterwards on the PC:** `Logs\quest\perf_summary_rc17_worn.txt` and `Logs\quest\perf_rc17_worn.csv` (per scenario).

## E: Live workflow from the Quest (the backend on the PC)

1. **PC:** `scripts\start_wall_backend.ps1`.
   - It must print "Starting LIVE with the Meta model API" and "Developer/Tester plan cards write REAL code …".
   - Free RAM first: close Unity Play Mode and anything big. The PC was at 2.5 GB free and Claude Code stopped the backend once.
2. **PC:** `adb reverse tcp:8765 tcp:8765`. Restart MuseDesk; the dock reads **LIVE**.
3. **Wall → Manager → the login goal.**
   - The paper's footer reads "LIVE model · code + tests really run in a sandbox".
   - Revise once, then stamp **Approve**.
4. **Watch the status page for 6–7 minutes.**
   - Developer rows: `Code written: … · pytest N passed, 0 failed`.
   - Tester rows: `Tests run: N passed …`.
   - Research rows: "Written, not run · Research write-up …".
5. **Select the Developer:**
   - Ask "Developer, what have you changed?". The answer should name real files.
   - Ask "Developer, can you change the password minimum to 12 characters?". The card should show **Apply change**.
   - Press it. A new card prints and is worked for real.
6. **Desk Start:**
   - The Researcher's paper reads "Model-written recommendation (no web research, no tools)".
   - Approve. The receipt names model-written files and real counts.
7. **PC:** open the newest `backend\sandbox_workspaces\plans\<plan>` folder and run `..\..\..\.venv\Scripts\python.exe -I -B -m pytest -q`. The counts must equal the last card's.

## Results ledger (owner fills in; Claude transcribes)

| Section | Date | Result | Notes / bugs (P0 / P1 / P2) |
|---|---|---|---|
| B1 Cold launch | 2026-11-17 | PASS (core) + NOTE | Owner: "all the core things seem like they work". Agents stand, but chairs sit at the desks (remove them). Back of the Researcher's computer clips. Nameplates clip through things when they turn: make them stationary, facing forward on the desk, no tilt. Props should face the right way and not jump around. (P1 polish) |
| B2 Desk workflow | 2026-11-17 | PASS (core) + NOTE | The handed paper is not fully attached to the hand: when the agent leans, the paper does not move. "!" sits a little too high above the agent. The handoff between agents goes too quickly: make it smoother. (P1 polish) |
| B3 Wall + Manager | 2026-11-17 | PASS (core) + NOTE | The little Wall boxes ("1 done") are unclear: remove them. Manager paper and plan summary should be movable. The Manager handing out the three cards one by one is slow after picking a plan: owner chose paper tubes delivering them. (P1) |
| B4 Select agent | 2026-11-17 | PASS (core) + NOTE | Finicky: said the hand was over a key when the owner is fairly sure it was not. (P1) |
| B5 Portal | 2026-11-17 | PASS (core) + NOTE | Send back works, but the agent pops into its wall spot: add the teleport effect at the desk (owner's choice). (P1) |
| B6 Floor / Ceiling | 2026-11-17 | PASS (core) + NOTE | The Spaces side panel can't be angled ergonomically: make it movable and turnable left/right so it faces the owner. Wall should be reachable from any mode, not just Desk. Calm motion option can go. (P1) |
| B7 Voice | 2026-11-17 | PASS (core) + NOTE | "voice seems like it works". Wanted for live: natural speech ("hey, why are you doing this instead of this") rather than "X, why". Owner chose: no name needed when talking to the agent you're with. (P1 feature) |
| B8 Apply change | 2026-11-17 | PASS (core) | Covered by "all the core things seem like they work"; no specific note. |
| D Performance + 15-min soak + restart | | PENDING | Recorder `rc17_worn` ran during the session; summary in `Logs/quest/`. Owner did not report on the soak/restart. |
| E Live from the Quest | | PENDING | Not run yet. |
| Look | 2026-11-17 | NOTE | "It all looks kinda dull": owner chose a lighting/material pass, then light colour grading if the frame rate holds. |

**Triage rules** (from the brief):
- **P0:** crash, launch failure, broken decision loop, wrong approval, unreachable UI, falsely reported result, serious grab failure, uncomfortable portal placement, sustained frame loss. Fix before filming.
- **P1:** only if it is on the filmed path (visible clipping, foot sliding, unreadable paper, broken emergence, a voice line used in the film).
- **P2:** do not touch before submission.
