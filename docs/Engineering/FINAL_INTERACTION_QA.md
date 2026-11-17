# Final interaction QA

2026-11-13. Build `Builds/MuseDesk.apk` (also kept as `Builds/MuseDesk_rc_refinement.apk`), installed on the Quest 3 at 00:55.

## What was actually tested

| Check | Result |
|---|---|
| Unity compile | Clean |
| EditMode tests | 68 passed, 0 failed (33 existing, 35 new for the spatial reach code) |
| Runtime smoke checks | 108, 0 failed |
| XR visual QA script | 0 FAIL, 53 WARN (`docs/Engineering/qa_runs/xr_visual_qa_final_refinement.txt`) |
| APK | Built, 0 errors; installed |
| Editor Play Mode, scripted demo | Whole loop, below. No errors from project code after one fix |

**Play Mode loop** (each step calls the method the key or grab is wired to):
idle, Start, paper offered, paper taken, Details open and closed, Approve, outcome on the sheet, completion sequence, receipt, View, viewer open, Next, Close, Filed plate, archive card, board open, card selected, simulated hold over a pad, board closed, Run again, Reject, Stopped receipt, failure sequence preview.

One defect found this way and fixed: the key state component tried to start an animation on keys that were being hidden.

**Not tested: anything with real hands.** Nobody has worn this build. The unattended launch is still blocked by the headset lock screen, which was not bypassed.

## Remaining QA warnings

- 27: deactivated keys and props that would not register if re-shown. They are never shown.
- 9: scale hazards under hidden-by-scale groups.
- 6: nameplate text behind its own desk's bounding box (boxes, not shapes).
- 4: reach, for decor that is never needed.
- The rest: small text at distance on side nameplates and the focus-mode inbox readout.

## On-head checklist, in order

Open MuseDesk and sit as usual. `scripts/quest_perf_watch.ps1` can be left running on the PC to record performance while you do this.

**A. First look (30 s)**
1. Three agents, nameplates upright and readable, plaque reading SCRIPTED DEMO then "Offline: demo". No "Connecting..." that stays.
2. Guide line: "Your AI team is ready." then "Press Start on the task dock."
3. Text looks dark and solid, not pale. Plate corners are rounded.

**B. Keys (1 min)**
4. Hover a finger over Start, Board and Focus: the cap lightens. Press: it darkens and travels; the face stays in front of its housing.
5. Press Start.

**C. Paper (2 min)**
6. The Researcher brings a small DECISION cover. Take it. It unfolds.
7. Read: header, question, summary, recommendation with its reason, provenance line. Is anything too small?
8. Approve and Reject are wide keys with even rims. Hover and press each end of one: does the far end register?
9. Why, Details, Changes: three evenly spaced chips. Press Details: a sheet slides out from under the paper. Press again: it slides back. No flicker where the sheets overlap.
10. Pick up the green stamp. Near the stamp box the box turns teal. Press it on the box: pale APPROVE, then dark APPROVED. Let go: the stamp glides back to its pad.

**D. Completion (1 min)**
11. Watch the Tester finish: lamp, a small celebration, the others look over, factory delivers, one chime, shelf token appears. Anything abrupt or doubled?
12. Receipt at front right: Delivered, summary, tests. Press View.
13. Viewer opens centred. Readable? Next and Close press? Close it.
14. Poke Filed 1 on the out-tray: archive card rises, closes after 8 s.

**E. Board (2 min)**
15. Press Board: it grows from the dock to your front left. Comfortable to look at? All keys reachable without leaning?
16. Card titles readable. Touch a card: teal edge, lifts slightly; toolbar shows Pause and Priority.
17. Grab a planned card: pads appear. Hold it over Developer: the pad grows and lights. Release: words say what happened and the card glides to its place. **This is the first real test of hand-drop assignment.**
18. Press Board again: it returns into the dock; nothing pokeable remains where it was.

**F. Second run (1 min)**
19. Run again on the receipt. This time Reject. The Researcher lowers the paper and walks home without a failure animation; receipt reads Stopped.

**G. Animation, while watching**
20. Feet keep up with the ground when an agent walks (no sliding, no walking on the spot).
21. Heads never twist past the shoulder.
22. Agents end exactly on their spots, facing you.

Report anything by step number.
