# rc20 owner check (on the headset): your rc19 live-test notes

**Build:** `Builds/MuseDesk_rc20_release.apk`, label `rc20-release`, release-signed (your key), versionCode 4.
Rollback: rc19 `F197F662` (same key, versionCode 3: going back may need an uninstall).
**Rule:** only your words make anything WORN. Mark each line PASS / FAIL / NOTE.
Your notes are transcribed word for word at the bottom of `RC19_QA.md`.

**What the backend log showed for your test:** "Manager, build a program to output stars to the console" was sent as
`POST /tasks` + `assign` (a board card), not `POST /manager/plans`. The voice grammar read "build ..." as its old "new task"
command before the Manager could take it. That card is not a plan step, so nobody worked it.

| # | Your note | What rc20 does | Check | Result |
|---|---|---|---|---|
| 1 | "Manager build a program to output stars..." became a board task, not done, not started | A sentence that names the Manager always goes to the Manager, even when it also sounds like "new task". The live Manager plans ANY goal with the model (not the three presets): its prompt now says to plan the user's own goal, size it (a small program 2 to 4 steps), name the real Python modules, and state the context it chose (inputs, outputs, file names) in the plan's assumptions so you can check it before approving. If you say it away from the Wall, it tells you to open the Wall instead of making a board card | On the Wall: "Manager, build a program that prints a triangle of stars". The paper shows a plan for THAT; approve; Developer/Tester write and run it | |
| 2 | Voice stopped between words; "Start building?" half filled | The microphone now waits 2.2 s of silence between words (was 1 s) and allows a sentence up to 15 s (was 6) | Say a long goal with a pause in the middle | |
| 3 | The "Heard:" panel cut into the paper | When the voice readout would overlap the Manager paper, the visit card or the summary, it steps out 5 cm toward you (its keys move with it) | Talk while the Manager paper is open | |
| 4 | "We don't need the LIVE tags" | No LIVE tag anywhere: agent chips, speech bubbles, the visit card, the plan paper (it now says "Code and tests really run in a restricted workspace on your PC"), the decision paper band, the completion receipt, the perch card, the voice readout. The SCRIPTED DEMO label stays in the offline demo (honesty rule) | Live: no "LIVE" word anywhere | |
| 5 | The agents should speak, not only answer on the card | Text to speech (Wit, the same Wit app the microphone uses; nothing new to set up). Each agent has its own voice: Manager Rebecca, Developer Charlie, Researcher Rosie, Tester Cooper. Spoken: an agent's answer to your question (from its card), the Manager presenting a plan ("Here is my plan: ... 3 steps. Untick anything you don't want, then stamp Approve."), and the Manager when the summary lands ("All done. ..."). The first sentences only; the card keeps the full text. Stops when you send the agent back | Ask the Developer a question: you hear the answer | |
| 6 | The visit card should be movable | It has the same grab handle as the plan paper and the summary: pinch near the bar under it and move it; it opens there next time | Move the card | |
| 7 | Pipes go through floors and walls; "add holes" | Every place a tube passes a floor slab, the roof or a bay wall has a brass collar with a dark ring (the hole) on each face (11 on the Full wall), placed against the art's real slabs | Look where the tubes pass the floors | |
| 8 | "The blocks above the agents' heads are kinda weird" | The floating status cubes are gone. Each desk has a small round lamp on the back wall above it (brass plate, coloured glass dome) in the same state colour. The "away" bar is on the wall too | Look above the desks | |
| 9 | "Still plasticy"; soft textures? | The agents' body and clothes are matte now: smoothness 0.28 (was 0.45 to 0.62) and no sky reflections on those 10 materials (the sheen that read as plastic). Visors, lenses and glows stay shiny so the bodies read softer by contrast. List: `Logs/rc20_matte_applied.txt` | Do they still read as plastic? | |
| 10 | "A way for the computer screen to be a panel" | A Screen key on the right end of the dock opens your PC's main screen as a panel in front of you (0.72 m wide, about 1 m away, a dark bezel), updated about 5 times a second, with the mouse pointer. Grab bar under it to move it. It is a view only (you cannot click the PC through it). The PC sends the screen only to the paired headset over USB, only while the panel is open, and saves nothing; `start_wall_backend.ps1 -NoScreen` turns it off | Press Screen | |

## Automated evidence (PLAY/AUTO, not WORN)

- Compile clean. EditMode 1641/1641 (new: "manager build a program to output stars to the console" is a Manager request).
- Backend 378 passed, 1 skipped (new: the screen route is off by default, needs a paired session, and returns real pixels
  from this PC: 640 wide, zlib BGRA).
- Play: the Manager's plan line is spoken (`AgentSpeech` created, said=1); a visit question answered and spoken by the
  Developer (said=2); the visit card dragged 0.112 m by its handle; the readout clearance gives 0.050 m toward the eye when it
  overlaps the paper, 0 when clear; 11 tube collars; status lamps and collars captured; the Screen panel showed this PC's
  screen (27 frames in about 5 s, upright, with the pointer), its key on the dock's right end.

## Results

(Filled in from runs and from your words only.)

- **Play regression (2026-11-17, backend off), PLAY not WORN:** Meet, Select-grab, Unified dock, Living Office stamp journey
  (86 s), failure-path journey: 5/5 PASS, 0 real console errors; Floor -> Desk -> Ceiling (pod) -> Desk PASS 0 errors on
  its own run (an earlier attempt in the same batch as the portal group FAILED with ~790k errors, all from Meta MRUK's
  Editor room loader `OnSceneAnchorRemoved`, none from MuseDesk code; rerun clean); portal Meet -> Open -> ask -> Send back
  PASS (lift 0.069); Apply change PASS; desk walk 7/7 PASS; desk paper offer PASS. Ledgers `Logs/preflight/11f4372_20261117_15*.md`.
  A first batch was stopped by Claude Code for low memory (not a failure); groups were then run one at a time.
- **Live, a NEW spoken-style goal (LIVE Meta model, PC backend; not on the headset):** "build a program that prints a
  triangle of stars to the console", change "also let the user choose the character instead of a star". Three runs, each
  **13/13** (`Logs/live_real_work_rc20_stars*.txt`). The Manager sized the plan to the goal (2 or 3 steps, no padding
  research step) and named the module (`triangle.py`); tests never dropped (30 -> 36, 12 -> 15, 18 -> 25).
  - **Checked by hand:** the program prints the triangle; the change made the character a real option of `main(argv)`, with
    tests. **Limit found and kept on purpose:** the restricted workspace does not allow `sys` or `argparse`, so the agents'
    programs cannot read the real command line: `python triangle.py --char #` still prints stars; the options work through
    `main([...])` (what the tests call). The Developer prompt now says so plainly instead of asking for argparse.
- **Build:** `Builds/MuseDesk_rc20_release.apk`, SHA-256 `AB11FE6C3DF4EC98D0CE30BCBFAF714549AE3F9D3E0E9F9E13BB047AC4BD478A`,
  signer `CN=MuseDesk, O=MuseDesk`, versionCode 4, label `rc20-release` found in the APK (no rc19 label left).
- **Installed on the Quest (2026-11-17) with `adb install -r`** (settings kept); on-device sha256 matches; versionCode=4.
  rc20 is now a protected path in `BuildApkTo.cs` (the next build is rc21, versionCode 5).
- Live backend started for the headset test (own window "MuseDesk backend (LIVE)", screen panel ON); `adb reverse tcp:8765` set.
