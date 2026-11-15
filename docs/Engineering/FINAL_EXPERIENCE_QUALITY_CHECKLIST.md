# Final experience quality checklist

Date: 2026-11-15. Branch `feature/living-world-polish`. Scope: the sprint quality standard, one row per item.

**Source of the standard:** the brief for this workstream. No file named "quality standard" exists in `docs/` (Grep, 2026-11-15). The categories (movement, interactions, visual, backend, performance, release) come from the brief. The rows are drawn from `docs/Polish/MOTION_AND_INTERACTION_DESIGN_SYSTEM.md`, `docs/Polish/IMPLEMENTATION_AUDIT.md`, `docs/Polish/QUEST_EVIDENCE_MATRIX.md`, `docs/Polish/NEXT_DEVICE_TEST.md` and `docs/Engineering/RC6_HARDWARE_QA.md`.

**Evidence key:** NOT_TESTED (no evidence), AUTO (EditMode or pytest only), PLAY (Editor Play Mode, drivers or simulated hands, no HMD), WORN (a person wore the headset; the doc records it). WORN is used only where a doc records it. Current worn record: the owner wore **rc6-1** (`RC6_HARDWARE_QA.md:24-38`). No worn record exists for rc6-2, the polish branch, or any polish APK. Polish APKs have not been installed (`RELEASE_POLISH_CHANGELOG.md:13`).

**How to verify:** on the headset, use `docs/Polish/NEXT_DEVICE_TEST.md` (steps 1-19) and the RC6 pass list in `RC6_HARDWARE_QA.md`. Use `scripts\quest_perf_watch.ps1` for frame timing. For AUTO, run `scripts\tests.ps1 <Fixture>` (fixtures one at a time) and `pytest` in `backend/`.

## 1. Movement

| # | Item | How to verify | Evidence today |
|---|---|---|---|
| M-1 | Agents start with a smooth velocity profile, not at full speed | Watch the Researcher walk to you (NEXT_DEVICE_TEST step 2); check the start | AUTO (`HabitatMotionTests` 22, `MotionTests`); desk: PLAY. Worn: NOT_TESTED |
| M-2 | Agents turn before walking, no turn while walking above 50 degrees | Route corner on the desk; watch for turn-in-place | AUTO (`AgentWalkCornersTests` 6); PLAY (S9a). Worn: NOT_TESTED |
| M-3 | No foot sliding (stride matched to ground speed) | Video of the walk cycle; step 2 | PLAY code only. Visual NOT_TESTED |
| M-4 | Gentle stop; no snap on arrival | Step 2 | AUTO profile; PLAY. Worn NOT_TESTED |
| M-5 | Desk walks do not clip props (clearance at 0.08 m collider radius) | Step 18; the Play Mode driver `DeskClearanceDrive` | **FAIL in PLAY: 6 of 7 routes overlap a prop** (R1/R2 -0.066 m, R3 to R7 -0.004 to -0.010 m). Owner decision open (`S9C_SETTINGS_SAFETY_UI_CHANGES.md`). rc6-1 worn complaint: "agents walk through their own desks" (`RC6_HARDWARE_QA.md:30`), still open |
| M-6 | Elevator: doors fully open before boarding, lined up with the floor | Wall step 9 | AUTO (every-tick clearance), PLAY round trip. Worn NOT_TESTED |
| M-7 | Handoff token arcs between hands, no pop, no jump | Step 5 | PLAY (S3S4). Worn NOT_TESTED. rc6-1 Gate A (A4) passed the older token, before the timed arc was added |
| M-8 | Card glides into its slot on every lane change | Step 6 (normal drop) | PLAY (S3S4). Worn NOT_TESTED |
| M-9 | Paper reveals with a grow, never a blink | Step 3 | PLAY (S3S4). Worn NOT_TESTED |
| M-10 | One writer per transform (no fights between director, walker and animator) | Watch for jitter during handoffs and recenter | Code audit (`IMPLEMENTATION_AUDIT.md` M10); PLAY. Worn NOT_TESTED |
| M-11 | Reduced Motion shortens transitions and removes overshoot | Step 16 (PlayerPrefs or ReleaseConfig) | AUTO (`MotionTests`); PLAY. Worn NOT_TESTED |
| M-12 | No Lerp-by-dt motion that never ends | Code review of motion paths | AUTO partial: M1 foundation done; "adoption ongoing" (`IMPLEMENTATION_AUDIT.md` M1). **Unverified** that all paths are converted |

## 2. Interactions

| # | Item | How to verify | Evidence today |
|---|---|---|---|
| I-1 | Double-press Approve sends one decision | Step 4 | AUTO (S2 guard); PLAY. Worn NOT_TESTED (`QUEST_EVIDENCE_MATRIX.md` UI-04) |
| I-2 | Double-poke Pause sends one command; "Already sending" shows once | Step 7 | AUTO/PLAY (S2). Worn NOT_TESTED |
| I-3 | Drag cancels on tracking loss, does not assign | Step 6 (hand behind back) | AUTO (S2); PLAY. Worn NOT_TESTED (`QUEST_EVIDENCE_MATRIX.md` XR-02) |
| I-4 | Hand ownership precedence: AgentSelect > Placement > Summon; Holding beats all far claims | Hand-pinch while holding a paper (wall step 11) | AUTO (`HandOwnershipTests` 10 per changelog); worn NOT_TESTED (XR-01) |
| I-5 | Voice gesture cannot open the mic while holding a paper | Pinch at mouth while holding paper | AUTO/code (S2 I5). Gesture is off in rc6-2 per `HANDOFF_NEXT_SESSION.md`. NOT_TESTED |
| I-6 | Far-pinch Select Agents is discoverable; no accidental pick | Wall step 11 | PLAY with simulated rays only. Real hands never used (`WALL_HABITAT_STATUS.md`). NOT_TESTED |
| I-7 | Placement release-to-place works; refusal shows a reason | Gate B steps B5-B5c | Worn on rc6-1: "works, but too strict" (`RC6_HARDWARE_QA.md:29`). Fixed in rc6-2 by PLAY only |
| I-8 | Card text fits inside the card | Look at the board | PLAY (`TextFitDrive`); worn NOT_TESTED on rc6-2 |
| I-9 | Recenter mid-ride and mid-portal leaves nothing floating | Wall step 15 | UNIT, PLAY. Worn NOT_TESTED (XR-03) |
| I-10 | Pokes and gaze reactions once, not on every head move | Gate A A9 | Worn on rc6-1: Gate A all good (`RC6_HARDWARE_QA.md:28`). Gaze chip and wave on rc6-2: PLAY only |

## 3. Visual

| # | Item | How to verify | Evidence today |
|---|---|---|---|
| V-1 | One state palette: running teal, needs-you amber, done green, problem coral | Look at board, agent glows, displays, Wall | AUTO/code (`OfficeTokens.StateColor`, S8). Worn NOT_TESTED |
| V-2 | Card role colours do not overlap state colours | Look at Tester and Developer cards | **Open** (`RELEASE_POLISH_CHANGELOG.md`, Med; `UI_AND_VISUAL_CONSISTENCY_AUDIT.md` #2) |
| V-3 | Provenance label legible on every surface (LIVE / SCRIPTED DEMO) | Steps 1, 5, 8, 9, 12 | PLAY (S9c footer on bubble; receipt, archive, voice HUD earlier). Worn NOT_TESTED |
| V-4 | Text readable at seated distance; not cut off | Read every label from the eye point | Worn on rc6-1: Gate A A1-A9 "all good" (`RC6_HARDWARE_QA.md:28`); also "everything sits slightly too high" (`RC6_HARDWARE_QA.md:35`), which was a fix item for rc6-2. Not verified on rc6-2 |
| V-5 | Disabled Wall keys visibly disabled | Wall key | AUTO/code (U2). Worn NOT_TESTED |
| V-6 | Fill rate of the ceiling particles and portal cone is acceptable | Ceiling descent with particles on, then off | NOT_TESTED. Flag `ceilingParticles` exists to turn it off |
| V-7 | Audio levels and cues (door, ding, print tick, landing) | Listen on headset | **Unheard**: `RELEASE_POLISH_CHANGELOG.md` U4 "PARTIAL (unheard)" |

## 4. Backend

| # | Item | How to verify | Evidence today |
|---|---|---|---|
| B-1 | Task state shown only after backend acknowledgement | Start a task offline and live; check dock state | PLAY (live run, Wall plan cards). AUTO for reducer |
| B-2 | Approval cannot be bypassed (backend gate) | Approve a paper; watch the backend record | AUTO (`backend` pytest, 207 passing at last full run per changelog) |
| B-3 | Idempotent commands: same key, same result | Double-send in backend | AUTO (`test_board_bridge.py`, `test_p3_*`) |
| B-4 | No command without a stable key | Check `FocusModeController.cs:164` and `ArchitectureDiagramController.cs:109` | **Gap**: counter-based keys per call (`INTEGRATION_CONTRACT.md` section 8). NOT_TESTED |
| B-5 | Scripted output labelled `SCRIPTED DEMO` / "Model-written, no tools" | Demo run offline; Manager plan offline | AUTO (`test_p3_runner.py:62, 88`); PLAY |
| B-6 | Claude provider works | Real API call | **NOT_TESTED**: no key; never called (`WALL_HABITAT_STATUS.md`) |
| B-7 | Live backend works on the headset | Run `docs/Engineering/RUN_AND_DEPLOY.md`; `adb reverse`; check LIVE labels | rc6-1 pass 2 is defined (`RC6_HARDWARE_QA.md:88`); no result recorded. NOT_TESTED |
| B-8 | Wall and Manager routes return 404 FEATURE_DISABLED when flags are off | Backend env off; call route | AUTO (`WALL_HABITAT_RELEASE_FLAGS.md`); not re-run in this pass |

## 5. Performance

| # | Item | How to verify | Evidence today |
|---|---|---|---|
| P-1 | 72 FPS in Desk, Floor, Ceiling | `scripts\quest_perf_watch.ps1 -Label desk -CaptureMinutes 4` | rc6-1 worn session reported 72 FPS throughout, worst case deployed pod 4.7 ms app GPU (`RC6_HARDWARE_QA.md:36`). **rc6-2 and polish: NOT_TESTED** |
| P-2 | Wall structure is cheap (49 boxes merged into 7 renderers) | Renderer count in Editor | PLAY count only. Device FPS NOT_TESTED (`QUEST_EVIDENCE_MATRIX.md` PERF-01) |
| P-3 | 10 to 15 minute soak without drops | Worn soak with perf watch | NOT_TESTED (PERF-02) |
| P-4 | Low-effect tier works | Worn | NOT_TESTED (PERF-03) |
| P-5 | No frame hitch from motion (`MaxStep` 1/20 s) | Perf capture during a walk | AUTO (cap in code). Device NOT_TESTED |

## 6. Release

| # | Item | How to verify | Evidence today |
|---|---|---|---|
| R-1 | Competition APK unchanged | Hash `Builds/MuseDesk.apk` = `173C813F4840DD3A7DCA81430E1914B1A9BEBF0A3B4C3375CF1E7FD53DB9B963` | APK (hash recorded in docs; not re-hashed in this pass). Not installed on polish builds |
| R-2 | Flags-off build is release-safe | Install `Builds/MuseDesk_polish_flagsoff.apk`; step startup | APK (built, 0 errors). **Startup on device NOT_TESTED** (`QUEST_EVIDENCE_MATRIX.md` RELEASE-01) |
| R-3 | Committed asset is the release state | Read `ReleaseConfig.asset`; check Wall flags are 0 and `buildLabel` | Read 2026-11-15: all Wall flags 0; `buildLabel: rc6-dev` (not the release name). Build label change unchecked (`RC6_SUBMISSION_CHECKLIST.md:11`) |
| R-4 | Validation build turns on only the Wall set | `WallValidationConfig.Status()` in Editor after On() | Code read (`WallValidationConfig.cs`); not run |
| R-5 | Competition build made with `Off()` | Check the APK's label has no `+wall` | NOT_TESTED |
| R-6 | Rollback path works | Reinstall `Builds/MuseDesk_rc6_2.apk` | NOT_TESTED on device |
| R-7 | Docs match code | This pass's contradictions list | See `FEATURE_FLAG_MATRIX.md` section 4 (5 items) |
| R-8 | Demo walkthrough is current | Run `DEMO_SCRIPT_AND_SHOT_LIST.md` section 1 on the headset | NOT_TESTED. `FINAL_DEMO_WALKTHROUGH.md` describes the 2026-11-12 layout |

## Rules for changing a row to WORN

Only the person who wore the headset marks a row WORN, with the build hash, date and the step number. Do not infer WORN from PLAY or AUTO.
