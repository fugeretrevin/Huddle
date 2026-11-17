# rc18 morning QA (owner, on the headset): what changed overnight and what to check

**Build:** `Builds/MuseDesk_rc18_release.apk`, RELEASE-signed (your new key, versionCode 2). Hash in the commit that installs it.
The Quest already runs a release-signed build, so rc18 installs over it (no uninstall, no reset).
**Rollbacks:** rc17 `BD0204F4` (debug-signed: needs an uninstall to go back), rc16, rc15.
**Rule:** only your words make anything WORN. Mark each line PASS / FAIL / NOTE.

Every line below answers one of your rc17 notes (2026-11-17, transcribed in `RC17_FINAL_HARDWARE_QA.md`).
Evidence so far for all of them: PLAY (Unity Editor, synthetic room) and EditMode, not WORN.

## Desk (2 min)

| # | Your note | What rc18 does | Check | Result |
|---|---|---|---|---|
| 1 | Chairs at the desks but the agents stand | The chairs are gone from all three desks | No chair behind any agent | |
| 2 | Back of the Researcher's computer clips | Its "computer" is the small whiteboard at the desk's front edge; the turning nameplate swung into it. The plate no longer turns and sits 8 mm further forward | Look at the Researcher's desk from your seat and from the side: nothing passes through the board | |
| 3 | Name tags clip when they turn | Nameplates are fixed: square to each desk, a slight 12° lean back, no turning | Lean left/right: plates stay put, nothing clips | |
| 4 | Paper not attached to the hand when the agent leans | The sheet now reads the hand AFTER the lean / arm nudge each frame (it read the pose before) | Start; when the Researcher holds the paper out, reach toward it so it leans: the paper moves with the hand | |
| 5 | "!" a little high | Lowered from 0.40 to 0.315 above the feet (about 7 cm above the head) | When an agent asks, the "!" sits just above its head | |
| 6 | Props should face the right way and not jump around | Every desk prop is turned to face your seat (the clock showed its back, the lamp pointed away). Props no longer run physics: they never move on their own; you can still pick one up and it glides home (no hop) | The clock face and lamp face you; watch 1 minute: nothing hops | |
| 7 | Handoff too quick between agents | The walk to the next agent takes at least 2.2 s, they turn to each other for a 0.4 s beat, the token arcs over 0.7 s (was 0.42), the hold is 1.8 s (was 1.0) | Approve: Researcher → Developer → Tester reads as calm, separate steps | |
| 8 | Calm motion option can go | The Calm motion key is removed (release flag off) | Not on the dock | |

## Wall (5 min)

| # | Your note | What rc18 does | Check | Result |
|---|---|---|---|---|
| 9 | The little "1 done" boxes are unclear | The mailbox count boxes are off in every mode | Not shown on Floor / Ceiling / Wall | |
| 10 | Cards handed out one by one is slow; "paper tubes" | Three glass pneumatic tubes run from the Manager's printer, down beside the tree, along the ground-floor ceiling and onto each desk. On approve, every card rolls up, shoots down its owner's tube at the same time and unrolls onto the desk (about 2 s, no elevator trips) | Manager → plan → Approve: all three cards arrive together | |
| 11 | Manager paper should be movable | A small grab handle under the plan paper: pinch near it and drag; it keeps facing you and reopens where you left it | Move the plan paper | |
| 12 | Plan summary should be movable | The same handle on the plan summary (receipt) card | Move the summary | |
| 13 | Select: "hand was over a key" when it wasn't | While pointing to select, a hand ray that only sweeps over a key or card no longer counts. A fingertip near a key, or a ray actually pressing something, still blocks (safety kept) | Select agents → point at an agent past the dock → pinch: picks up | |
| 14 | Send back pops them into their spot | They teleport in at their desk: a short teal light column rises, they grow out of it | Meet → Open here → Send back; also Return from the perch | |
| 15 | Wall should work from anywhere | From Floor or Ceiling at rest, Wall brings the office home (the normal move), then the wall preview opens by itself | Spaces → Floor → Place → Wall | |

## Spaces panel (1 min)

| # | Your note | What rc18 does | Check | Result |
|---|---|---|---|---|
| 16 | Can't angle the side panel; it wouldn't face me | While you hold its grab bar, turning your wrist left/right turns the panel (up to 75° either way), on top of it facing your head as you move it | Grab the bar, twist the wrist: the panel turns to face you | |

## Voice (2 min, Wi-Fi on, mic ON)

| # | Your note | What rc18 does | Check | Result |
|---|---|---|---|---|
| 17 | Talk more naturally, not "X, why" | While an agent is WITH you (perched on the desk, or out of the portal), just talk to it: "hey, why are you doing this instead of that?". It goes to that agent's side conversation (LIVE: the real model answers as that agent; offline: the scripted reply) and shows on its card. Commands (approve, stop...) still work; naming ANOTHER agent still goes to that agent. With nobody visiting, name-first is unchanged (chatter does nothing) | Select the Developer → perch → "what have you changed so far?" (no name) | |

## Look (1 min)

| # | Your note | What rc18 does | Check | Result |
|---|---|---|---|---|
| 18 | "It all looks kinda dull" | The scene had NO light: every material was shaded only by a flat grey ambient. rc18 adds one soft warm key light that follows your heading (always from over your shoulder, so whatever faces you is lit) and a gradient ambient (cool sky, warm ground). Agents have form and highlights, wood reads warm, brass and teal pop | Desk and Wall look warmer and three-dimensional; frame rate holds (watcher) | |

**Not done, on purpose:** full-screen colour grading. On Quest passthrough a post-processing pass can overwrite the
passthrough alpha (a black background) and nobody could check that on the headset overnight. If you want it after seeing
the lighting, it is a 20-minute change plus one look on the headset.

## Performance

Start `scripts\quest_perf_watch.ps1 -Label rc18_worn` before putting it on. Pass = steady 72 fps. rc17 worn baseline:
72.6 avg, app GPU 7.7 ms of 13.9 ms (the key light adds one per-pixel light, no shadows).
