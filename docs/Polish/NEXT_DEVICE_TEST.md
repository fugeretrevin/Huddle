# Next device test (about 30 minutes, one person, Quest 3)

Install the polish validation build. The exact file and hash are in `RELEASE_POLISH_CHANGELOG.md`.

```
adb -s 2G97C5ZHCK037K install -r Builds\MuseDesk_polish_validation.apk
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

## What to send back

The PASS/FAIL list, any clip of something that looked wrong (with the step number), and the `quest_perf_watch` output folder.
