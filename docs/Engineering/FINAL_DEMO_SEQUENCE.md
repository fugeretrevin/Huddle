# Final demo sequence

2026-11-13. The sequence to show a judge or record for the video. It is written for the scripted demo (no PC needed; every surface says SCRIPTED DEMO). Each part is marked with its evidence level: **PLAY** = run in Editor Play Mode only; **WORN** = done on the headset. Do not record or describe a part as working until it is WORN.

Only show the modes that pass their hardware gate (`QUEST_HARDWARE_VALIDATION_MATRIX.md`, `FINAL_FEATURE_FLAG_DECISION.md`). Part 1 stands alone as the complete demo.

## Part 1: Desk (about 90 s) — PLAY

1. Sit down. The office appears in front of you: Researcher, Developer, Tester at their desks, nameplates reading Idle, the plaque reading SCRIPTED DEMO. One line: "Your AI team is ready." then "Press Start on the task dock."
2. Press **Start** on the dock at your front left. The dock reads "In progress: Researcher is on it".
3. The Researcher walks to the front holding a small paper marked DECISION.
4. Take the paper. It unfolds: who it is from, the question, a short summary, the recommendation with its reason, and "Template recommendation. Not live AI research."
5. Optional: press **Details**; a sheet slides out from under the paper.
6. Pick up the green stamp and press it on the stamp box (or press **Approve**). A pale APPROVE turns into a dark APPROVED when the decision is confirmed; the paper glides to the out-tray.
7. The Developer writes the code; the Tester runs the tests. On delivery: the lamp, the Tester's small celebration, the other two look over, the factory delivers, one chime, a token appears on the shelf.
8. A receipt stands at your front right: Delivered, the summary, tests passed. Press **View**: the result opens above the office. **Close** it.
9. Optional: press **Board** on the dock. The board grows out of the dock at your front left; touch a card to select it; press Board again to put it away.

## Part 2: Floor Office (about 60 s) — PLAY, synthetic room only

10. Press **Floor** beside the Focus key. An outline appears on the floor to your side: "Detected floor spot 1 of 6. Next, Confirm or Cancel."
11. Press **Confirm**. The office glides to the floor and settles on its platform. Your desk now holds only the dock, the plaque, the Focus key and three small keys.
12. Press **Run again** / **Start**. The agents work on the floor, in the corner of your eye.
13. When a decision is needed, a "Needs you" marker rises over the office, and a small courier lifts the paper from the Researcher's dispatch station and flies it to just in front of you. The stamps appear on your desk.
14. Take the paper from the courier, decide. The courier returns.
15. On delivery the receipt appears at your desk as before.
16. Press **Desk**: the office glides back in front of you with everything as it was.

## Part 3: Ceiling Pod (about 60 s) — PLAY, synthetic room only

17. Press **Ceiling**. An outline shows where the pod will come down, beside you. Press **Confirm**. The office rises to a mount near the ceiling, off to one side.
18. Press **Lower**: the pod unlocks, descends on its mast, steadies beside you, and its shelf opens. Look in; the agents are working.
19. Press **Raise** while holding the paper: it waits. Put the paper back or decide, and it retracts and docks.
20. With the pod docked, a decision still reaches you by courier.
21. Press **Desk** to finish.

## Live variant

With the desktop server running and reachable, the plaque reads LIVE and the same steps drive the real pipeline in the sandbox. The paper still says so when its recommendation came from a template. Not re-run on the headset since the redesign.

## If something goes wrong mid-demo

- A paper that did not arrive: it snaps to in front of you after 3 s by design; if not, press **Status** beside the summon keys.
- The office somewhere you do not want it: press **Stop** during a move (the Cancel key reads Stop while the office is moving; it goes back to where it started), or **Desk** once it is at rest.
- Anything else: **Run again** on the receipt restarts the scripted run; closing and reopening the app returns to Desk if the saved spot no longer validates.
