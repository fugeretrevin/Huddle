# RC6 hardware QA

Evidence levels: AUTO (tests), PLAY (Editor Play Mode drivers), APK (built and installed), WORN (a person wore the headset and saw it). Only the wearer can mark a row WORN.

## Pass 1: `rc-feedback-2` as installed (the "before" pass)

| Field | Value |
|---|---|
| Git commit | `3aa7fc2` (tag `rc-feedback-2`); docs-only `beb01a8` on top, tag `rc6-baseline` |
| APK | `Builds/MuseDesk_rc_feedback2.apk`, SHA-256 `D6422EE5BD45E868011C43E41997BBB66476F13B384F28025D95DCBF082AEFEF` |
| Package | `com.musedesk.quest` |
| Device | Quest 3 `2G97C5ZHCK037K`, OS build `52641960034500520` |
| Scene | `MainMRScene` (only scene in build) |
| Date | 2026-11-13 |
| Perf capture | `scripts/quest_perf_watch.ps1 -Label rcfb2_worn` waiting since ~14:00; starts when MuseDesk opens, records 30 min |
| Wearer | (owner) |

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
| B5 | Let go on a ready spot | (this build moves immediately; the next build will ask "Place here") Office moves without passing through you or furniture | |
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

## Pass 3: RC6 build

Filled in after the gesture-ownership and confirm-placement build is installed.
