# MuseDesk final XR validation

Date: 2026-11-12 (evening). Build under test: commit `3d7cb76` plus the build made from it, `Builds/MuseDesk.apk`, package `com.musedesk.quest`.

## Headline

**The final build has not been run on the headset.** It is installed. Launching it was blocked by the headset's lock screen, which needs a person. Everything that could be verified without a person in the headset was verified and is listed in section 2. Everything else is in the checklist in section 4, which takes about fifteen minutes.

Evidence levels used here, and they are not interchangeable:

| Level | Meaning |
|---|---|
| **Automated** | A test or script ran and passed on this machine during this pass |
| **Live server** | A real backend process, driven over HTTP and WebSocket the way the Quest drives it |
| **Capture** | An Editor capture from the seated eye point. Shows appearance only |
| **Device log** | Read from `adb logcat` while the app ran on the Quest |
| **On head** | A person wearing the headset saw it and said so |

## 1. What happened when validating on the device

1. At the start of this pass the headset was being worn and another app (the browser) was in the foreground, so MuseDesk was not launched over the wearer.
2. Later the headset went idle. The proximity sensor was overridden so the app could run with nobody wearing it (`am broadcast -a com.oculus.vrpowermanager.prox_close`), and MuseDesk was started with `am start`.
3. The system refused the launch. The log line is: `Launch is blocked because: a Reprojected OS dialog is currently showing. Caching launch`. The foreground was `com.oculus.os.vrlockscreen` and a screen capture was black. The headset is locked.
4. The lock screen was not bypassed. The new build was installed (installing does not need the headset unlocked), the proximity override was removed (`automation_disable`), and the app was left stopped.

Consequences: no launch check, no runtime log, no performance numbers and no screenshot exist for this build. The 81 performance lines collected during the attempt came from the lock screen, not from MuseDesk, and are not used anywhere.

Earlier device evidence, from older builds, is recorded in `END_TO_END_TEST_RESULTS.md` section 3: the app launched and ran for twenty minutes with hands tracked, and a physical stamp approval reached the approval path. Those builds predate almost everything in `FINAL_POLISH_CHANGELOG.md`.

## 2. Verified without the headset

| Check | Level | Result |
|---|---|---|
| Backend test suite | Automated | **140 passed** |
| Live backend: approve run, reject run, and a card created, assigned, approved and completed over HTTP | Live server | **56 of 56** |
| Unity runtime checks on the real types (reducers, both demo gateways, the board-to-run bridge, artifacts, reset, voice matching) | Automated | **103 checks, 0 failed** |
| Unity NUnit contract tests | Automated | 33 passed (constants and mappings only) |
| Unity compile | Automated | 0 errors |
| Scene health | Automated | 0 missing scripts; every poke key wired to a method that exists; all workspace keys face the seated eye |
| Quest build | Automated | `RESULT=Succeeded`, 0 errors |
| Install on the Quest | Device | `Success` |
| Decision paper layout, task card text, assign dock, guide sign, 3D keys | Capture | Appearance as intended (`docs/review_screenshots/02`, `04` to `08`) |

What the automated checks do establish about the main flow:

- **Card to real work.** Assigning the runnable card to the Researcher starts exactly one run; a replayed assign starts none; the Developer and Tester are refused; a second run is refused while one is in progress. Live: the tester really ran 3 baseline tests, the approved run really wrote two files and passed 6 tests, and the diff and test report are attached to the card.
- **Approval.** One valid decision is applied; a replay applies nothing; a conflicting second answer is refused; nothing is built before the decision; reject writes nothing and the card ends cancelled.
- **Provenance.** The decision event says `research_provenance: "template"` with a human label; live artifacts say `work_provenance: "sandbox_execution"`; every demo event and artifact says `source_mode: "demo"`.
- **Demo.** The seeded board has one runnable and three planned cards; the scripted run follows develop, artifact, test, complete; sample artifacts page correctly; a reset in mid-run produces no late events and restores the board.
- **Restart.** A run interrupted by a backend restart is reported failed, its decision expired, and its card failed.

What they do not establish: anything a hand does, anything an eye sees in the headset, and frame rate.

## 3. Status of each item in the validation list

"Not run" means not seen on the headset in this pass. The second column is the best evidence that exists.

| Item | Best evidence | On head |
|---|---|---|
| Workspace position and orientation | Capture; placement logic compiled | Not run |
| Desk-scale placement | Capture | Not run |
| Three agents facing the user | Capture | Not run |
| Task board reach and readability | Capture; estimated 0.52 m from the eye | Not run |
| Grab and drop of task cards | Automated for the command path; hover frame and halos compiled | Not run |
| Assign dock | Capture | Not run |
| Physical approval stamp | Device log from an older build (a stamp approval was applied). The new gating has not run on device | Not run on this build |
| Hand-over and paper pickup | On head on 2026-11-11 (older build) | Not run on this build |
| Paper expansion and text readability | Capture | Not run |
| 3D poke keys | Capture; wiring automated | Not run. The cap rides on the SDK press visual, which only moves on device |
| Researcher to Developer hand-off | Compiled | Not run |
| Developer to Tester hand-off | Compiled | Not run |
| Factory success and failure states | Compiled | Not run |
| Focus mode | Compiled; backend policy automated | Not run |
| Pushable prop physics | Starting overlaps computed in the Editor | Not run |
| Mascot animation by state, gaze, breathing | Compiled; bones found on all three rigs | Not run. This is new behaviour on every agent |
| First-use guide, Start, Replay, Filed keys | Automated wiring; capture | Not run |
| Runtime errors and warnings | None available for this build | Not run |
| Performance and stutter | None | Not run. See `FINAL_PERFORMANCE_REPORT.md` |

## 4. On-head checklist

Unlock the headset, sit at a desk, launch MuseDesk from Unknown Sources. No PC is needed. Say what you see for each line; "yes" or a one-line description of what was wrong is enough.

**A. Launch (1 minute)**

1. Look straight ahead and keep still. Does the office appear in front of you within about 10 seconds, on the desk plane, roughly 60 cm away?
2. Are the three agents facing you? Can you read their name tags and the status signs above them?
3. Does the sign above the office say "DEMO" and does the low sign between the stamps say "Welcome to your agent office", then ask you to start?
4. Is any text mirrored, clipped, or overlapping?

**B. Start through the board (2 minutes)**

5. The board is on your left. Can you read the four cards? Does the top card say "Ready: drop on Researcher" and the others "Planned"?
6. Reach toward the top card. Does a frame light up behind it before you grab?
7. Grab it and move it over the "Developer" pad under the board. Is the pad's outline red? Move it over "Researcher". Is it green?
8. Drop it on "Researcher". Does the card say "Researcher started it" and move to Working? Do the agents start moving (Researcher reading, Tester inspecting)?

**C. The decision (4 minutes)**

9. Does the Researcher walk to your side of the desks with a paper and an exclamation mark, and look at you?
10. Before taking the paper, press the green stamp against it in the Researcher's hand. **Nothing should happen.** Does anything happen?
11. Reach for the paper. Does a frame light up? Take it. Does it unfold smoothly to a readable sheet?
12. Read it at a comfortable distance. Is the band "SCRIPTED DEMO   needs your decision" readable? The title? The summary? The line "Template recommendation. Not live AI research."?
13. Are the four keys (Approve, Ask why, Details, Reject) inside the sheet? Press Details. Does the explanation appear on the left without covering the "Stamp here" circle? Press Ask why. Does an answer appear?
14. Do the keys look three-dimensional and does the cap sink into its housing under your finger?
15. Pick up the green stamp and press it on "Stamp here". Does the paper say "Saving your decision…" then "Approved", with an APPROVED mark?
16. Let go of the paper. Does it shrink and glide to the tray on your left?

**D. After approval (3 minutes)**

17. Does the Researcher walk to the Developer and hand something over, then walk home and turn back to its desk? Does the Developer type? Does the Developer then walk to the Tester?
18. Does the factory line do something at each step, and does a token appear on the shelf at the end with a chime?
19. Does a panel open on your right with a code diff that begins "SCRIPTED SAMPLE"? Do Next and Prev page it, and Close close it?
20. Does the card end in Completed? Press "Filed" (left). Does it show what you approved and the result?

**E. Replay and reject (2 minutes)**

21. Press Replay (right). Does everything clear and the board return to four cards?
22. Press Start. When the paper comes, take it and press Reject (or use the red stamp). Is there **no** shelf token, **no** chime and no PASSED lamp? Does the card end as cancelled, and does the guide say "Rejected. Nothing was built."?

**F. Everything else (3 minutes)**

23. Push the mug, the plant and the books with your hand. Do they move, stay on the desk, and put themselves back after a few seconds? Does anything fly off?
24. Press Focus. Start a run. Do updates collect in the inbox on your right instead of interrupting? Does the decision paper still come?
25. Take the headset off and put it back on. Does the office re-place itself in front of you?
26. Any stutter, judder when you move your head, or heat? Roughly when?

Before taking the headset off, with it still connected by USB, pull the log and performance lines for that session (this does not restart the app):

```powershell
$adb = "C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
New-Item -ItemType Directory -Force Logs\quest | Out-Null
& $adb -s 2G97C5ZHCK037K shell "logcat -d -s VrApi:I | grep FPS=" > Logs\quest\vrapi_onhead.txt
& $adb -s 2G97C5ZHCK037K shell "logcat -d -v time -s Unity:V | grep -E '\[MuseDesk|Exception|NullReference'" > Logs\quest\unitylog_onhead.txt
```

`scripts\quest_run.ps1` is the unattended form (it restarts the app); see `FINAL_PERFORMANCE_REPORT.md`.

## 5. Known risks going into that test

In rough order of likelihood:

1. **Mascot animation.** The bridge now cross-fades real clips for the first time. A wrong clip, a pop when walking stops, or a head that over-rotates on a head-down clip are all plausible.
2. **Board-launched start in the demo.** The card, the scripted session and the reset path are joined by new code that has only run with zero-length delays in tests.
3. **Immediate demo with a background live attempt.** With a PC tethered, the app may switch from demo to live a moment after launch. That switch has never run on device.
4. **Stamp gating.** If `ReadyForDecision` never becomes true (for example the paper never finishes unfolding), the stamps would silently do nothing. The buttons would still work.
5. **Modified art files.** Five FBX files used by the scene were changed by another tool during this pass (the Kanban frame, the task card, the approval stamp, two paper models). If their size or pivot changed, card text and the board layout may no longer line up.
6. **Key label size** on the new keys and dock pads: sized by calculation, seen only in a capture.

## 6. Additions after this document was written (2026-11-12, night)

A further pass by Antigravity, then verified and committed as `2b8f074` on branch `antigravity/visual-polish`. The build on the headset is from that tree. Re-verified at that commit: Unity compiles, 0 missing scripts, **108** runtime checks pass, **145** backend tests pass, live backend end to end **71 of 71** (now including a request-changes round that produces a revised decision in the same run, and the bounded cancel).

None of the following has been seen on the headset. Add these to the checklist in section 4:

27. **Changes key.** The paper has a fifth key, "Changes". Press it. Does the Researcher go back to work and bring a second paper titled as a revision, still labelled a template recommendation? Approve that one: does the build then run? In a separate run, press Changes three times: does the run stop with "no further revisions are available"?
28. **Stamps.** New stamp models on a rest pad at front centre. Do they sit on the pad, grab cleanly, and still stamp only a paper you have taken?
29. **Keys.** New keycap and housing models. Do the caps sink when pressed and return to rest, with no cap left floating after a press?
30. **Carried paper and token.** Is the paper in the Researcher's hand small (about 6.5 cm), and the hand-off token a sensible size? (They were 100 times too large because the rig's hand bones are scaled by 100.)
31. **Desk props.** Can you pick up, carry and throw the mug, plant, books and so on, from near and from a distance, and do they return?
32. **Distance grab.** Point at a task card or the offered paper from out of reach and pinch. Does it come to your hand? Does that ever steal a grab you meant for something near?
33. **Fingers on surfaces.** When your fingers close on a card, the paper or a prop, do the rendered fingers rest on its surface instead of sinking in? **If the hands flicker, jitter or look wrong at any time, say so first**: this solver is new and is the most likely thing to make hands look worse. It is `DynamicFingerCollision` on `[BuildingBlock] Camera Rig`; disabling that component (or setting `DynamicFingerCollision.GloballyEnabled = false`) restores plain tracked hands.
34. **Touch an agent.** Rest a fingertip on an agent's head for a moment. Does a small card appear with its state, progress and task?
35. **Voice.** Press the Voice key on the paper. Is there a microphone permission prompt? With Wi-Fi on, do "approve", "reject", "changes" and "why" work, and does "don't approve" do nothing? Voice needs network and does nothing offline.
36. **Stability.** Does the office stay put (no slow drift, no re-placing itself while you work, no falling)? Locomotion was disabled and automatic re-placement on recenter was turned off.
37. **Merged models.** The three desks and the board frame were swapped for merged-mesh versions. Does anything on them look wrong or missing (lamps, monitors, the tester's scanner ring)?
