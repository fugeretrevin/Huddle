# Next device test (about 30 minutes, one person, Quest 3)

Install the polish2 validation build (it includes everything in the older polish build). The exact file and hash are in `RELEASE_POLISH_CHANGELOG.md`. Nothing in this file has been done on a headset yet.

```
adb -s 2G97C5ZHCK037K install -r Builds\MuseDesk_polish2_validation.apk
```

(Use the adb in the Unity Editor's platform-tools.) Afterwards, put the competition build back:

```
adb -s 2G97C5ZHCK037K install -r Builds\MuseDesk_rc6_2.apk
```

Optional, so frame timing is captured while you test:

```
scripts\quest_perf_watch.ps1 -Label polish -CaptureMinutes 12
```

Mark each line PASS or FAIL, add a few words, and copy the results into `QUEST_EVIDENCE_MATRIX.md`.

## A. Desk regression (must not be worse than rc6-2)

1. Cold start with Wi-Fi off. The desk demo appears; no setup is needed. (CORE-01)
2. Start the task. The Researcher walks over: a smooth start, a turn before walking, no feet sliding, and a gentle stop with no snap on arrival. (MOT-01)
3. Take the paper. It appears with a short grow (no blink) and is readable without leaning. Open the details; the sheet slides out. (UI-04, UI-01)
4. Press Approve twice quickly. Only one decision is sent: the paper shows Saving, then one result. (CORE-02)
5. Watch the handoffs. The small token appears in one agent's hand, arcs to the other's and fades out, with no pop and no jump. (MOT-04)
6. Open the board. Drag a card toward another lane, then **hide that hand behind your back** before letting go. The card must go home with "Hand lost. Nothing sent" and nothing assigned. Repeat with a normal drop: the card glides into its slot. (UI-03, XR-02)
7. Poke Pause on a card twice fast. You see "Already sending" once; one command goes out. (UI-02)

## B. Wall Habitat (validation build only)

8. Enter Wall, preview on a real wall, Place here. The treehouse grows out of the wall (about half a second). (WALL-01)
9. Watch a worker ride the elevator: doors fully open before boarding, a gentle cabin start and stop, lined up with the floor before the doors open. You hear a soft door slide and a small ding. (WALL-04)
10. Workers at their desks: running = working pose, waiting for you = attentive, blocked = thinking, no task = calm idle. A completion gives one small nod, not a party. (MOT-03)
11. Select Agents, pinch a worker from about 1.5 m and carry it. It should follow the hand smoothly without jitter. Let go over the pad: a short landing and a tick. Ask one question, then Return. (SIDE-01, SIDE-03)
12. Meet larger, confirm. A soft whoosh, a gentle grow, and it stands listening (not talking). Send it back. (PORT-01/02)
13. Manager: ask for a plan and approve it. Cards print with a tick, only after the board shows them. (MGR-02)
14. Turn Focus on. The habitat goes quiet. (S8)
15. Recenter mid-ride and mid-portal. Nothing is left floating or between floors. (XR-03)

## C. Comfort

16. Optional: set Reduced Motion (`ReleaseConfig.reducedMotion`, or PlayerPrefs `MuseDesk.ReducedMotion = 1`). Transitions get shorter, there is no overshoot, and everything still makes sense.
17. **Calm motion key (validation build only; the release has it hidden).** Under the mode console, below the Focus key, there is a small key reading `Calm motion OFF`. Poke it: the label turns to `ON`, the cap turns Teal, and the next walk, card glide and paper unfold are visibly quicker with no overshoot. Poke again: back to `OFF`, Paper cap. Quit the app and start it again: it remembers the last choice. Check that the three label lines are readable at seated distance and that the key is not hit by accident when you reach for Focus. (UI-05)
18. **Desk walk clearance.** Start the task and watch the Researcher walk to you, then approve and watch the handoffs. Note any step where a body clips a desk edge, the Inbox tray, the book stack, a nameplate or a station prop (the Play Mode check predicts the delivery lane, the corner by the book stack and the stops in front of the Developer and Tester; `S9C_SETTINGS_SAFETY_UI_CHANGES.md`). (MOT-05)
19. **Bubble footer.** Poke an agent: its speech bubble shows the sentence and a small `SCRIPTED DEMO` line under it (`LIVE` only on a live run), readable and not cut off. (UI-06)

## D. polish2 additions (validation build `MuseDesk_polish2_validation.apk`; EVERYTHING BELOW IS NOT WORN YET)

Install `Builds\MuseDesk_polish2_validation.apk` instead of the polish one (hashes in `RELEASE_POLISH_CHANGELOG.md`). Every line is NOT WORN: only Editor and Play Mode evidence exists. Mark PASS or FAIL.

20. **Wall art.** Enter Wall, place the habitat. It is now the real treehouse art (three tiers, glass elevator on your left, cubicles along the bottom), not grey boxes. Agents stand on the floors (no floating, no sinking), nothing pokes through a railing or desk, the elevator car lines up with each floor and its doors open and close in step with the riders. If it looks wrong, set `useHabitatArt` false on `Wall/HabitatArtManifest` to get the proxies back (needs a rebuild; report what you saw first). Try the small-space layout too if a wall allows it.
21. **Living Office loop.** On the Manager, ask for a plan. The plan-review paper opens labelled `SCRIPTED DEMO` (or LIVE on a live run). Revise once, then Approve: the paper gets an APPROVED stamp **only after the board shows the cards** (a Reject shows REJECTED the same way, with nothing created). Cards print, each owner rides up, takes its card, carries it to its desk and works. When all are done the summary paper comes down to the desk as a receipt.
22. **Retry.** In the failure demo the Developer card shows its real blocked or failed reason (`Scripted failure for the demonstration. No model was used and nothing was run.`) and the Tester shows `Blocked: it depends on ...`. Poke Retry twice fast: one retry goes out and the second is refused. The card goes back to queued.
23. **Onboarding hints.** On first use of each area a short hint appears once and does not come back after you dismiss or use the feature. It must not cover the paper or the keys.
24. **Calm motion key** (item 17 again, now with the Wall art): with it ON the elevator and walkers move noticeably quicker with no overshoot.
25. **Desk layout V2 routes.** Start the desk task and watch the Researcher and the other agents walk the new routes (delivery lane, the corner by the book stack, stops in front of the Developer and Tester). Note any step where a body clips a desk edge, the Inbox tray, a prop or a nameplate. (MOT-05)
26. **Per-agent Listening.** Hold the voice gesture while pointing at one agent: only that agent turns to listen (a small listening pose), the others do not, and it stops when you let go. It must never look like the agent is talking.

## What to send back

The PASS/FAIL list, any clip of something that looked wrong (with the step number), and the `quest_perf_watch` output folder.
