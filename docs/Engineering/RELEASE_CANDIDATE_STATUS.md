# Release candidate status

2026-11-13 (updated for rc6-1). Deadline 2026-11-18, noon Pacific.

## Where things are

| Item | State |
|---|---|
| Branch | `release/final-refinement` |
| Latest build | `rc6-1`, commit `b469a6a`, APK `Builds/MuseDesk_rc6.apk` (85.3 MB); installed on the Quest 3 2026-11-13 ~17:30 |
| Restore points | Tags `rc6-baseline` (`beb01a8`), `rc-feedback-2` (`3aa7fc2`, `Builds/MuseDesk_rc_feedback2.apk`), `rc-feedback-1` (`Builds/MuseDesk_rc_feedback1.apk`), `rc-spatial-1` (`Builds/MuseDesk_rc_spatial.apk`); also `pre-final-refinement` and `pre-ux-redesign` with their APKs |
| Automated checks | Full EditMode suite 701 of 701, three runs on 2026-11-13 (the earlier one-run hang did not reproduce). Smoke checks (78 of 78) and XR visual QA (0 FAIL, 89 WARN) are last recorded for `rc-feedback-1`; not re-run for rc6-1 |
| Editor Play Mode | Scripted-demo loop passes (earlier build). Spatial Play Mode drivers for RC6 pass (`docs/Engineering/RC6_GESTURE_CONFLICT_TESTS.md`) |
| On the headset | rc6-1 has not been worn. The owner wore the desk build and `rc-feedback-1`; the owner's round-1 feedback was produced from that wear |
| Performance | Not measured. Recorder ready: `scripts/quest_perf_watch.ps1 -Label rc6_worn` |
| Live mode | Server side: live e2e 71 of 71 on 2026-11-13. The Quest client has not had a live run since round 1. No backend or contract change in RC6 |
| Release config | `Assets/MuseDesk/Resources/ReleaseConfig.asset`: all flags ON in rc6-1 (validation only, see `FINAL_FEATURE_FLAG_DECISION.md`) |

## Definition of done, item by item

| Requirement | Status |
|---|---|
| Office appears correctly in passthrough | Seen by the owner on an earlier build; not re-checked on rc6-1 |
| All three characters animate correctly | Code reviewed and corrected; not watched on the headset. Poke, gaze and wave reactions added in `rc-feedback-1`, not worn |
| Important controls within reach | Measured in the scene: all keys within 0.55 m, including the open board. Not tried by hand |
| Document text readable | Sizes raised and weights fixed; not read on the headset |
| Approval stamp works | Worked on earlier builds. The stamp pop-away (`rc-feedback-1`) is not worn |
| Paper can be grabbed and inspected | Worked on earlier builds; new layout untested by hand |
| Approve / reject end to end | Passes in Play Mode (demo); server side live 71 of 71 |
| Board supports its execution workflow | Start from the dock passes in Play Mode. Hand-drop assignment has never been tested by hand (`RC6_HARDWARE_QA.md` A6) |
| Cards readable, no overflow | Measured: labels 2.3 mm or more inside the card (`rc-feedback-2`, Play Mode). Not read on the headset |
| Artifact viewer opens sensibly | Yes, in Play Mode: centred, on request only |
| Receipt properly positioned | Yes, in Play Mode |
| Handoffs function | Researcher to Developer plays. Developer to Tester is usually skipped in the scripted demo |
| Factory responds to real state | Yes, driven by events; the delivery clip is sequenced after the test result |
| Live / demo labelling correct | Yes: plaque, dock, paper, cards, receipt, viewer, archive each state the mode from their own data |
| Standalone demo without a PC | Scripted demo works offline; not re-checked on the headset since rc6-1 |
| Floor and Ceiling placement | AUTO and PLAY only. Floor: release leaves the outline, "Place here" moves along a re-swept route. Ceiling: pull down and push up. Not worn |
| No critical on-device defects | Unknown until worn |
| Performance measured | No |
| APK builds and launches | Builds and installs. Launch of rc6-1 not yet confirmed by the owner |
| Main demonstration completes on-head | Not yet |

## What a person needs to do

1. Start `scripts/quest_perf_watch.ps1 -Label rc6_worn` on the PC (it waits for MuseDesk to open).
2. Wear the headset, open MuseDesk, and follow `docs/Engineering/RC6_HARDWARE_QA.md` (Desk A1 to A9, Floor B1 to B8, Ceiling C1 to C8; about 15 minutes).
3. Report by step number.

## Known issues

- The open artifact viewer covers the Developer's face.
- Refined art: polished factory, three nameplates and the mode plaque are adopted. Trays, dock, receipt, task card and board frame are skipped (measurements in `docs/Design/ART_ADOPTION_NOTES.md`). The paper folio is off (its lit material greys against the unlit plates).
- Voice is ON in rc6-1 and needs internet (Wit.ai). No text-to-speech.
- Ceiling particles, portal cone and scan grids have no fill-rate numbers. Each can be switched off in `ReleaseConfig.asset`.
- The Unity Editor is shared with Antigravity, which switches it to preview scenes. Production steps go through `scripts/run_guarded.ps1`, which opens `MainMRScene` and runs the step in one Editor call and refuses if another scene has unsaved changes.
- Git reports "bad object refs/stash" during background maintenance. Commits succeed; the stale stash reference predates this work.

## Room-aware modes (Floor Office, Ceiling Pod)

In rc6-1 with both mode flags on, for validation only. Code, Play Mode drivers and automated tests are in place; none of it has been worn. Ship decision by the rule in `FINAL_FEATURE_FLAG_DECISION.md`; detail in `FINAL_SPATIAL_RELEASE_AUDIT.md` and `ROOM_PLACEMENT_FINDINGS.md`.
