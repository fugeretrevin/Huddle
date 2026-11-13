# RC6 hardware QA

Evidence levels: AUTO (tests), PLAY (Editor Play Mode drivers), APK (built and installed), WORN (a person wore the headset and saw it). Only the wearer can mark a row WORN.

## Pass 1: `rc6-1` (installed 2026-11-13 ~17:30)

`rc-feedback-2` was never worn: the owner had not opened it by the time RC6 was built, and RC6 replaces the exact floor and ceiling interactions it would have tested. So the one worn pass is on RC6. `rc-feedback-2` stays as the rollback (`Builds/MuseDesk_rc_feedback2.apk`, SHA-256 `D6422EE5BD45E868011C43E41997BBB66476F13B384F28025D95DCBF082AEFEF`, commit `3aa7fc2`).

| Field | Value |
|---|---|
| Git commit | `b469a6a` (tag `rc6-1`); rollback tags `rc6-baseline` (`beb01a8`), `rc-feedback-2` |
| APK | `Builds/MuseDesk_rc6.apk` (85.3 MB), SHA-256 `F560AA6FD49C11EA5C6E11041A7ED436A91E065A825ED328325A07C80DB16BCA` |
| Release config | `rc6-dev`: all flags on (validation build) |
| Package | `com.musedesk.quest` |
| Device | Quest 3 `2G97C5ZHCK037K`, OS build `52641960034500520` |
| Scene | `MainMRScene` (only scene in build) |
| Perf capture | `scripts/quest_perf_watch.ps1 -Label rc6_worn` waiting; starts when MuseDesk opens, records 30 min, writes `Logs/quest/perf_rc6_worn.csv` split by scenario |
| Wearer | (owner) |

Please stay a little while (about 20 s) in each state (idle, board open, paper held, floor preview, floor placed, ceiling scan, pod lowered) so each gets its own row of numbers.

Go through in this order. One word per row is enough (ok / bad + what you saw). Takes about 15 minutes.

### Desk (Gate A)

| # | Do | Look for | Result |
|---|---|---|---|
| A1 | Launch from the app list with no PC backend running | Office appears on the desk within ~10 s; sign reads DEMO | |
| A2 | Wait | Researcher walks over with a paper and "!" | |
| A3 | Grab the paper | It expands; text readable | |
| A4 | Stamp Approve | Stamp poofs; Researcher → Developer → Tester; factory; token; chime | |
| A5 | Next decision: stamp Reject | No token, Developer waits | |
| A6 | Open the board, grab a card, drop it on an agent | Card assigned; card text inside the card | |
| A7 | Nameplates | Centred, one line | |
| A8 | Grab bar on a panel: move it up and down | Moves, stays where left | |
| A9 | Poke a character, look at one, wave | Reacts once; does not keep reacting to normal head movement | |

### Floor (Gate B)

| # | Do | Look for | Result |
|---|---|---|---|
| B1 | Press Floor | "Look at the floor" card appears | |
| B2 | Look at an open floor spot ~1 m away | After ~0.6 s the grid and the preview appear where you looked | |
| B3 | Point and drag the preview to a reasonable open spot | Says ready; or a reason you understand | |
| B4 | **Before letting go, hold a paper in the other hand** and pinch-drag with the free hand | Note anything odd | |
| B5 | Let go on a ready spot | Nothing moves yet; outline stays solid teal; "Place here" appears | |
| B5b | Press Place here | Office moves without passing through you or furniture; outline turns green | |
| B5c | Drag to a blocked spot and let go | Outline coral/amber with a short reason; no Place here | |
| B6 | Decision from the floor office | Paper reachable; approve works | |
| B7 | Press Desk | Office comes back | |
| B8 | Press Floor, then Cancel | Everything back as before | |

### Ceiling (Gate C)

| # | Do | Look for | Result |
|---|---|---|---|
| C1 | Press Ceiling | "Look up at the ceiling" card; grid starts where you look | |
| C2 | Confirm a dock spot | Mount appears on the ceiling | |
| C3 | Console key to lower | Unlock click, hum, descent, lock chime; effects | |
| C4 | Interact while lowered | Paper reachable | |
| C5 | Raise it again | Retracts and docks | |
| C6 | Reach up, pinch the docked pod, pull down ~12 cm | Lowers; felt deliberate? | |
| C7 | Pinch a paper/stamp near you while the pod is docked overhead | Pod must **not** move | |
| C8 | Press Stop while it moves | Halts and goes back | |

### Notes

- Anything that surprised you:
- Comfort (gaze dwell 0.6 s, card position, sound volume):
- Frame drops you noticed (where):

## Pass 2: live backend (PC tethered)

Run after pass 1. Start the backend (`backend`, `MUSEDESK_PRODUCER=live`, port 8765, see `RUN_AND_DEPLOY.md` §3), run `adb reverse tcp:8765 tcp:8765`, relaunch.

| # | Look for | Result |
|---|---|---|
| D1 | Sign reads LIVE; cards read LIVE | |
| D2 | Task starts; research paper shows its real source (template research labelled as such) | |
| D3 | Approve; Developer writes the change; Tester runs real pytest | |
| D4 | Artifact panel shows the real diff and test output | |
| D5 | Stop the backend: sign reads "Reconnecting"; nothing simulated. Start it: run reported failed, decision expired | |

## Pass 3: fix round

Filled in after any fixes from pass 1.
