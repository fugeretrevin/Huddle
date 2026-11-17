# Quest user acceptance QA (owner script, rc16)

Build: `Builds/MuseDesk_rc16_real_work.apk`, label `rc16-real-work`, SHA-256 `D25583D1…E22C1753`, **installed and hash-verified** on the Quest (2026-11-16). Rollbacks: rc15 (`047DD408…`), rc14 (`3CD37ED9…`), rc13, rc11.
Before putting it on, run `scripts\quest_perf_watch.ps1 -Label rc16_worn` in a terminal. It logs frame rate and hitches.
Mark each line PASS / FAIL / NOTE. Only these observations can make anything WORN.

## 0. rc15 worn results (owner, 2026-11-16 evening)

| rc15 check | Result |
|---|---|
| Ceiling rugs no longer overlap | **PASS** |
| Researcher walks to the pod's front shelf with the paper | **PASS** |
| Select-agents beam, teal on an agent | **PASS** |
| Meet → Open here: visitor on its disc | **PASS** |
| Visitor head turns smoothly, body never rolls | **PASS** |
| Original hidden while it visits; back after Send back | not reported |
| Hands-free voice: "Researcher, why?" | **FAIL**: nothing happened; the mic flickered between on and hearing |
| Performance (watcher, 352 s, every mode) | **PASS**: avg 72.2 fps, p5 71, 0 Unity exceptions (`Logs/quest/perf_rc15_worn.csv`) |

Voice cause (found on the PC): Wit.ai works. Token valid, ~0.3 s answers (`AgentScripts/WitProbe.cs`). But a fast "Researcher
why" can come back as "Research your why". Hands-free only acts on sentences that START with a name, and dropped this one
silently. The flicker is the hands-free loop re-arming after each silent window (normal).

## 1. rc16 changes (wear these first)

| # | Do | Expect |
|---|---|---|
| 1 | Desk: look at each agent's desk | the agent stands at its desk, the monitors face the AGENT, the chair is behind/under the agent (none on your side) |
| 2 | Move your head left/right and up/down over the desk | each nameplate turns and tilts toward you smoothly, never snaps or flips; it stops turning past ~60° |
| 3 | Ceiling: lower the pod; look at the desks and plates | same as 1–2 from below (plates tilt down toward you) |
| 4 | Settings → mic ON; say "Researcher, why?" (also try it fast) | a reply card appears (offline: the scripted answer, labelled) |
| 5 | Mic ON; say "why researcher" | a quiet card: "Heard: … Start with a name" (nothing acted on) |
| 6 | Mic ON; chat normally for a minute | nothing pops up for chatter that names no agent |
| 7 | Not reported on rc15: pinch an agent to the perch, then Send back | gone from its wall desk while visiting, back after |

## 2. First-time user test (hand the headset to someone else)

Say only: "Use MuseDesk to ask your team to complete a task." Do not help. Note:
- time to first action;
- wrong interactions, failed grabs, missed targets;
- unreadable text;
- whether they:
  - found Start or the Manager;
  - understood the paper;
  - stamped it;
  - noticed work moving;
  - found the board;
  - asked an agent something;
  - received the receipt;
  - understood the SCRIPTED DEMO label.

## 3. Live chain from the headset (real work)

1. On the PC, run `scripts\start_wall_backend.ps1`. It prints "Starting LIVE with the Meta model API" and "Developer/Tester plan
   cards write REAL code …".
2. Run `adb reverse tcp:8765 tcp:8765`.
3. On the Quest: Wall → Manager → pick the login goal. The paper shows the plan's steps, owners and risks, and the footer
   reads **"LIVE model · code + tests really run in a sandbox"**.
4. Revise once.
5. Stamp Approve (OK the risky steps). APPROVED appears only after the cards exist; then the cards print and the owners ride
   the elevator.
6. Select the Developer → perch → "Developer, what are you working on?". The reply should name its real card.
7. Watch the Manager status page:
   - Developer rows read `Code written: <files> · pytest N passed, 0 failed`;
   - Tester rows read `Tests run: N passed, 0 failed`;
   - Research rows read "Written, not run · Research write-up: …" (model text, labelled).
8. On the PC, open the newest folder under `backend\sandbox_workspaces\plans\` and run pytest. The counts must match the last
   card.
9. Desk: Start → Researcher paper → stamp Approve. The receipt shows **6 passed / 0 failed** and two files. The code here is
   still the fixed OAuth starter.

Record:
- the time from Approve to the first card;
- any refusal text;
- any card that failed and how it reads;
- anything labelled LIVE that was not real.

## 4. Performance

With the watcher running, do each of these in turn:
- Desk idle, 30 s;
- Board open;
- paper held;
- Wall preview, then Wall active;
- Meet portal open, 60 s;
- Ceiling lowered, 30 s;
- then a 15-minute idle soak.

Pass = steady 72 fps, and hitches under 150 ms outside scene changes. rc15 passed everything except the soak, which was not run.
