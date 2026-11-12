# MuseDesk competition readiness

Date: 2026-11-12 (evening). Submission deadline: 2026-11-18, noon Pacific. Build: `Builds/MuseDesk.apk`, package `com.musedesk.quest`, version 1.0 (code 1), from commit `3d7cb76` or later on `integration/audit-repair`.

## Verdict

**Not ready to submit yet, for one reason: the final build has not been run on the headset.** The functional gaps the review identified are closed in code and pass every automated and live-server check. The last several hours of interaction, animation and layout work have never been seen by a person in the headset, and past experience on this project is that every serious interaction bug was found there.

The next step is the fifteen-minute checklist in `FINAL_XR_VALIDATION.md` section 4.

## 1. Where each priority stands

| Priority | Item | State | Evidence |
|---|---|---|---|
| P0 | Launches, passthrough, hand tracking, no crash | Unknown for this build; true for the builds run earlier today | Device log (older builds) |
| P0 | Approval cannot be triggered by accident | A stamp requires a paper that was taken and readable; one response in flight per decision; backend still decides | Compiled; backend idempotency live-server tested |
| P0 | No unsafe execution | Sandbox unchanged; sandboxed tests no longer inherit secrets; one run per session | Automated |
| P1 | Decision paper quality | Rebuilt: readable hierarchy, provenance band, separate stamp target, keys inside the frame, filing animation | Capture |
| P1 | Reachable task interaction | Assign dock under the board; hover and drop-zone feedback | Capture |
| P1 | Complete standalone demo | Immediate start, Start and Replay keys, seeded board, sample artifacts that page, reject shows no success | Automated |
| P1 | One card type runs real work | `auth_demo` card assigned to the Researcher launches the real pipeline; artifacts attach to the card; restart recovers | Automated, live server (56 of 56) |
| P2 | Research provenance | Labelled a template recommendation on the event, the paper and the archive | Automated, live server |
| P2 | Hand-off correctness | Queueing, ownership and reset handled | Compiled; not seen |
| P2 | Artifact linking | v2 artifacts carry the card id; v1 events carry `board_task_id` | Live server |
| P2 | Frame-rate stability | **Not measured** | None |
| P3 | Mascot liveliness | State clips, gaze, breathing | Compiled; not seen |
| P3 | First-use guidance | Event-driven prompt sign, hideable | Automated wiring; capture |
| P3 | Visual cleanup | Explain and Voice keys hidden; prop tray reduced; test cube hidden | Capture |
| P4 | Extras | Decision archive added (it is small and makes decisions persistent). No diagram or voice work | Compiled |

## 2. What the app can do

### Standalone (what a judge gets; no PC, no network, no account)

- Starts in the scripted demo at once, labelled "DEMO · Simulated work" and "SCRIPTED DEMO" on every card, paper and artifact.
- The user starts a run by dropping the runnable card on the Researcher (at the agent or at the dock under the board), by pressing Start, or by waiting 40 seconds.
- Researcher works, walks over, hands a paper. The user takes it, reads it, can ask why and open details, then approves or rejects by key or by stamp.
- Approve: Researcher to Developer to Tester hand-offs, factory stages, a labelled sample diff and test report in the artifact panel, a shelf token, the card in Completed, the decision filed.
- Reject: the card ends cancelled; no artifact, token or chime; the decision is filed as rejected.
- Replay restores everything. Focus mode holds routine updates. Thirteen desk props can be pushed.

Everything above is deterministic and labelled as scripted. Nothing claims to be running remotely.

### Live (developer's PC tethered by USB)

- Same presentation, driven by the real backend. Cards and papers read LIVE.
- The tester really runs `pytest` in a sandbox; after approval the developer really writes the OAuth scaffold and the tester really runs the new tests. The diff and test output in the artifact panel are the real ones, attached to the card.
- The Researcher's recommendation is a fixed template and is labelled so on the paper.
- "+ New" creates another runnable card. One run at a time.
- If the backend stops, the sign shows reconnecting and nothing is simulated. If it restarts mid-run, the run and its card are reported failed and the decision expired.

## 3. Known limits to state honestly in the submission

1. Only one task type runs (the auth scaffold). Other cards are labelled "Planned".
2. No model is in the loop. Research is a template; development and testing are real but fixed.
3. No revise-and-retest loop: a failed or rejected run ends.
4. One decision per run, always delivered by the Researcher.
5. Voice needs a token and network and is not part of the submission experience.
6. Performance is unmeasured.

## 4. Launching the submitted APK

Install (from a PC with ADB; ADB ships with Unity):

```powershell
$adb = "C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
& $adb devices                                   # the headset must be listed as "device"
& $adb install -r Builds\MuseDesk.apk
```

Run:

1. Put the headset on and unlock it.
2. Open the app library, choose **Unknown Sources**, and select **MuseDesk**.
3. Allow the permissions it asks for (spatial data / scene).
4. Sit at a desk, look straight ahead and hold still for a few seconds. The office places itself in front of you. Taking the headset off and on, or the system recenter, places it again.

No PC is needed after installation.

## 5. Main demonstration, step by step

Standalone. About three minutes.

1. Launch. The sign reads "DEMO · Simulated work". The low sign between the stamps asks you to start.
2. On the board to your left, grab the card "Build the login flow (OAuth scaffold)". Drop it on the **Researcher** pad under the board. (Or press **Start**, on your right.)
3. The agents start working. The Researcher walks to you with a paper.
4. Take the paper from its hand. It unfolds. Read the question and the recommendation; the line beneath says it is a template recommendation.
5. Press **Details** for the reasoning, **Ask why** for an answer.
6. Pick up the green stamp and press it on "Stamp here" (or press **Approve**).
7. Watch: the paper is filed in the tray on your left; the Researcher hands off to the Developer; the Developer to the Tester; the factory runs; a panel on your right opens with the sample diff (page with Next and Prev); a token lands on the shelf; the card moves to Completed.
8. Press **Filed** (left) to see the record of the decision and its result.
9. Press **Replay** (right), start again, and this time **Reject**: no token, no chime, the card ends cancelled.

Live variant: start the backend with `MUSEDESK_PRODUCER=live`, run `adb reverse tcp:8765 tcp:8765`, launch the app, and do the same steps. Full instructions: `RUN_AND_DEPLOY.md`.

## 6. Before submitting

In order:

1. Run the on-head checklist and fix what it finds.
2. Take the performance measurements (`FINAL_PERFORMANCE_REPORT.md` section 4) and act only on what they show.
3. Decide what to do with the other art tool's uncommitted files (`FINAL_POLISH_CHANGELOG.md` section 10): commit them, or restore the five modified FBX files, so the submitted build comes from a known tree.
4. Record the demonstration video on the headset, showing the SCRIPTED DEMO labels, and a separate clip of live mode.
5. Bump `AndroidBundleVersionCode`, sign for upload per the Meta Developer Dashboard, and build from a clean commit.
6. Delete the deactivated test cube and debug readout from the scene.

## 7. Verification summary

| Check | Result |
|---|---|
| Backend tests | 140 passed |
| Live backend end to end | 56 of 56 |
| Unity runtime checks | 103 checks, 0 failed |
| Unity compile, scene health, Quest build, install | Clean; 0 missing scripts; build succeeded; installed |
| On the headset, this build | Not run (lock screen) |
| Performance on the headset | Not measured |
