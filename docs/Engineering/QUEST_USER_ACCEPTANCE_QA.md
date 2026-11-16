# Quest user acceptance QA (owner script, rc14)

Build: `Builds/MuseDesk_rc14_owner_fixes.apk`, label `rc14-owner-fixes`, SHA-256 `3CD37ED9…EB0657`, **installed and
hash-verified** on the Quest (2026-11-16). Rollback: `Builds/MuseDesk_rc13_lively_polish.apk` (then rc11).
Before putting it on: `scripts\quest_perf_watch.ps1 -Label rc14_worn` in a terminal (logcat FPS + hitches).
Mark each line PASS / FAIL / NOTE. Only these observations can make anything WORN.

## 1. rc14 fixes (your rc13 report)

| # | Do | Expect |
|---|---|---|
| 1 | Ceiling mode, lower the pod, look at the deck | the Tester's and the Developer's rugs no longer overlap or flicker |
| 2 | With the pod lowered and a decision pending (offline demo: Start) | the Researcher walks out to the pod's front shelf and holds the paper out there |
| 3 | Wall → Select agents, move your hand around | a beam from your hand; on an agent it bends to it in teal with a ring and "Pinch to pick up <name>"; elsewhere a grey beam |
| 4 | Pinch an agent, carry it to the perch | while it visits, it is **gone from its desk** on the wall (only the visit marker), back after Return |
| 5 | Meet → an agent → Open here | the visitor stands on a small hovering disc (teal rim, floor glow); its original is gone from the wall |
| 6 | Move your head and hand around the visitor, reach behind it, poke it | the head turns smoothly and never spins; the body leans a little and springs back, never rolls over |
| 7 | Send back | the visitor goes into the column; the original reappears at its desk |

## 2. First-time user test (hand the headset to someone else)

Say only: "Use MuseDesk to ask your team to complete a task." Do not help. Note time to first action, wrong
interactions, failed grabs, missed targets, unreadable text, and whether they: found Start or the Manager; understood the
paper; stamped it; noticed work moving; found the board; asked an agent something; received the receipt; understood the
SCRIPTED DEMO label.

## 3. Live chain from the headset (Level 5)

1. PC: `scripts\start_wall_backend.ps1` (prints "Starting LIVE with the Meta model API"); `adb reverse tcp:8765 tcp:8765`.
2. Quest: Wall → Manager → pick the login goal → the paper says LIVE model, shows steps/owners/risks.
3. Revise once, then stamp Approve (OK the risky steps) → APPROVED appears only after the cards exist → cards print,
   owners ride the elevator.
4. Select the Developer → perch → "What are you working on?" → a reply naming its real card.
5. Wait for the Manager status page: results read "Proposed change …" / "Test plan …" (text only: expected and labelled).
6. Desk: Start → Researcher paper → stamp Approve → receipt shows **6 passed / 0 failed** and two files.
Record: time from Approve to first card, any refusal text, anything labelled LIVE that was not real.

## 4. Performance

With the watcher running: Desk idle 30 s, Board open, paper held, Wall preview + active, Meet portal open 60 s,
Ceiling lowered 30 s, then a 15-minute idle soak. Pass = 72 fps steady, hitches under 150 ms outside scene changes.
