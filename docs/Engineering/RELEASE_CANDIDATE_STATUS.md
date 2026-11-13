# Release candidate status

2026-11-13. Deadline 2026-11-18, noon Pacific.

## Where things are

| Item | State |
|---|---|
| Branch | `release/final-refinement` |
| Latest APK | `Builds/MuseDesk.apk` (87 MB), copy `Builds/MuseDesk_rc_refinement.apk`; installed on the Quest 3 |
| Restore points | Tag `pre-final-refinement` + `Builds/MuseDesk_pre_final_refinement.apk`; tag `pre-ux-redesign` + `Builds/MuseDesk_pre_ux_redesign.apk` |
| Automated checks | Compile clean; 68 EditMode tests; 108 smoke checks; XR visual QA 0 FAIL |
| Editor Play Mode | Full scripted-demo loop passes |
| On the headset | This build has not been worn. The previous build was worn by the owner, who reported only the nameplate tilt (since fixed) |
| Performance | Not measured. Passive recorder is waiting for the headset to be worn |
| Live mode | Not re-run since the redesign. No backend, contract or gateway behaviour changed; `SessionController` gained read-only connection properties |

## Definition of done, item by item

| Requirement | Status |
|---|---|
| Office appears correctly in passthrough | Seen by the owner on the previous build |
| All three characters animate correctly | Code reviewed and corrected; not watched on the headset |
| Important controls within reach | Measured in the scene: all keys within 0.55 m, including the open board. Not tried by hand |
| Document text readable | Sizes raised and weights fixed; not read on the headset |
| Approval stamp works | Worked on earlier builds; the new target, provisional mark and return are untested by hand |
| Paper can be grabbed and inspected | Worked on earlier builds; new layout untested by hand |
| Approve / reject end to end | Passes in Play Mode (demo) |
| Board supports its execution workflow | Start from the dock passes in Play Mode. Hand-drop assignment has never been tested by hand |
| Cards readable, no overflow | Measured: no overflow; titles 8 to 10.7 mm |
| Artifact viewer opens sensibly | Yes, in Play Mode: centred, on request only |
| Receipt properly positioned | Yes, in Play Mode |
| Handoffs function | Researcher to Developer plays. Developer to Tester is usually skipped in the scripted demo |
| Factory responds to real state | Yes, driven by events; the delivery clip is now sequenced after the test result |
| Live / demo labelling correct | Yes: plaque, dock, paper, cards, receipt, viewer, archive each state the mode from their own data |
| Standalone demo without a PC | Yes |
| No critical on-device defects | Unknown until worn |
| Performance measured | No |
| APK builds and launches | Builds and installs. Launch not confirmed for this build |
| Main demonstration completes on-head | Not yet |

## What a person needs to do

1. Start `scripts/quest_perf_watch.ps1 -Label rc1` on the PC.
2. Wear the headset, open MuseDesk, and follow `docs/Engineering/FINAL_INTERACTION_QA.md` (about 8 minutes).
3. Report by step number.

## Known issues

- The open artifact viewer covers the Developer's face.
- Antigravity's refined models (paper folio, receipt ticket, card, dock plate, plates, factory, archive tray) are imported but not yet used in the scene; only its wide and chip key models are.
- The Unity Editor is shared with Antigravity, which switches it to preview scenes. Production steps go through `scripts/run_guarded.ps1`, which opens `MainMRScene` and runs the step in one Editor call and refuses if another scene has unsaved changes.
- Git reports "bad object refs/stash" during background maintenance. Commits succeed; the stale stash reference predates this work.

## In progress, not in this build

Room-aware workspace modes (Floor Office, Ceiling Pod): code for attention cues, remote paper delivery and summoning is written and compiles; placement code is being written. Nothing spatial is in the scene yet, and Desk mode is unchanged.
