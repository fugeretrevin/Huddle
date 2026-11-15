# Parallel sprint: master report

Date: 2026-11-15. Branch `feature/living-world-polish` (HEAD `aa887ed` per `PARALLEL_SPRINT_TEST_RESULTS.md`). Competition deadline 2026-11-18 noon Pacific.
Remaining work and owners: `docs/Engineering/PARALLEL_SPRINT_REMAINING_WORK.md`.

## Headline

**Nothing in this sprint has been verified on the headset. Nothing is WORN.** Every result below is AUTO (EditMode or pytest), PLAY (Editor Play Mode, simulated hands, no HMD), or APK (built, not installed). The competition build is untouched: `Builds/MuseDesk.apk` = rc6-2, SHA-256 `173C813F…B9B963`. Nothing from this branch is merged into `release/final-refinement`. All Wall, Manager, Select, `/btw` and Portal flags are 0 in the committed `ReleaseConfig`.

## What was done, by workstream

| Workstream | What | Commit(s) as recorded in docs | Evidence |
|---|---|---|---|
| S0, S1: audit and motion foundation | Four audits (`docs/Polish/audit/`), `IMPLEMENTATION_AUDIT.md`. `MuseDesk.Motion` (eases, tokens, `MotionClock`, `ProfiledMover`, `Damped`, Reduced Motion, `MuseMotionProfile`). Wall walker velocity profile and turn-in-place. Elevator accelerate and brake. | `27b0507` | AUTO (`MotionTests` 32, `HabitatMotionTests` 22); PLAY (Wall) |
| S2: one owner per hand | Tracking loss cancels a card drag. One in-flight command per card and per decision, stable keys. Claim precedence AgentSelect > Placement > Summon. Voice gesture guard. | `c6d74e4` | AUTO (interaction group 173 passed); PLAY. Worn: NT |
| S3, S4: desk motion | Card glide and reveal. Desk walk profile, turn in place, damped facing. Handoff token arc. Paper reveal and hide. Single-writer guards. | `b2813c2` | PLAY. Worn: NT |
| S5, S6: Wall and visits | Keys and cards reveal on tokens. Habitat grows out of the wall. Damped carry. Exact timed perch landing (d = 0.000). Eased portal grow. | `3352dae` | AUTO (wall group 168, 1 skipped); PLAY |
| S8: palette, audio, Reduced Motion | `WallAudio` cues; one state palette (`OfficeTokens.StateColor`) for board, agent glows, displays and Wall; in-app Reduced Motion setting. | `34c911e`, `2b07752` | AUTO; PLAY. Audio: unheard |
| S9a: pop-ins | Card recycle and details-sheet pops, RebuildAll re-reveal, desk routed-walk corners above the turn limit. | Not recorded in docs | PLAY (per `S9A_POPIN_CHANGES.md`) |
| S9b: character and audio | Desk Turn_Left/Right, confirmed-completion Nodding, idle gestures off under Focus, `PaperArrived` and `CardLanded` cues. | Not recorded in docs | PLAY |
| S9c: settings and safety | Calm motion key behind `reducedMotionToggle` (0 in release). Hover on the Teal token. LIVE / SCRIPTED DEMO footer on the agent bubble. Font fallback. Desk clearance driver. | Not recorded in docs | PLAY. The clearance numbers in this doc are superseded (see Desk layout) |
| Desk layout V1 | Desks spread (0.20 m aisle). Researcher walks behind its desk and offers at the aisle mouth. Authored lanes in `WorkspaceRoot/DeskLanes`. | Not recorded in docs | PLAY. Desk routes at least +0.019 (r 0.080). Ceiling was -0.046 to -0.049 on the Researcher's lanes, which V2 fixed |
| Desk layout V2 | Researcher station +70 mm toward the user (to z 0.095), tray and decor moved with it, five lane points moved. Revert and record file kept. | `2ec5d62` | PLAY. Worst route 0.018 Desk, 0.018 Floor, 0.017 Ceiling (21 of 21 cells pass at r 0.080). Outer-desk overhang not fixed (owner decision) |
| Desk demo handoff fix | Developer to Tester handoff was dropped at timeScale 1 because completion cleared the queue. Completion now waits for the reported handoff chain (`HandoffWaitPolicy`, 15 s cap). Backend untouched. | `26d3f43` | PLAY (3 of 3 runs, handoff seen in every run); AUTO (`HandoffWaitPolicyTests` 4, `DeskPolishLogicTests` 22) |
| Art merge (`art/living-world-polish`) | 283 files, mostly art. `MuseDesk_Agent.controller` (3 layers). Desk agents on the regenerated FBXs. Wall art not hooked (socket layout mirrored and differently sized). `WallArtFitCheck` tool. Loop-flag guard. | merge `b6789c6`; `d49b389` (loop guard); `0bf7341`, `8c2ca85` (fit check, contract names) | AUTO (groups above, loop guard 5); PLAY (desk states, 0 console errors). Art: not on head |
| Living Office (Wall + Manager, end to end) | Elevator dispatcher applied (one round trip at a time). Manager plan paper with pages and real fields. Cards print on committed ids only. Carries, chute fallback. Blocked and failed reasons, Retry, synthesis receipt. | Worktree merge; backend routes `51f5166` | AUTO (`LivingOffice` 23, Wall 168); backend `test_p3_living_office` 4. PLAY: offline 13 of 13 steps, failure run 16 of 16, live 13 of 13 (scripted backend, 0 console errors) |
| A1 to A16 integration | Pure-logic sprints merged one at a time, each compiled. Several adapter plans NOT applied (A1, A3, A7 in part, A9, A12, A6 scene text). | Map: A1 `8dbb122`, A3 `71f23d0`, A9 `e487756`, A7 `0f2d81e`, A4b `f3cb017`, A12 `8c5b8a9`, A14 `6ad2a73`, A6 `136e1d7`, A15 `4a815b2`, A13 `d60b4b0` (cherry-pick), A11 `1d01fd1` | AUTO. See `PARALLEL_SPRINT_INTEGRATION_MAP.md` |
| A2, A5 | No sprint doc or merge found in the repo. | None | Not run / unknown. Confirm with owner |
| A8, A10 (earlier) | `/btw` routing and manager plan on backend. | Already on HEAD | pytest; PLAY live |

## Evidence totals

- **EditMode:** compile clean (0 CS errors). 62 fixtures, each run alone: 921 passed, 0 failed (899 unique; three fixtures dormant behind a define). `PARALLEL_SPRINT_TEST_RESULTS.md`. The whole suite was never run in one go; a full run hung the Editor in an earlier session (`HANDOFF_NEXT_SESSION.md`).
- **Backend:** pytest 298 passed, 1 skipped (A16 count). The Wall live path used a local backend in scripted mode. Claude provider never called (no key).
- **Play Mode (Editor, no HMD):** desk demo Start, paper, Approve, Completed; Living Office offline, failure and live runs; desk clearance traces (min 0.017 Researcher, 0.019 Developer).
- **APK:** `Builds/MuseDesk_polish_validation.apk` (SHA-256 `635BEA…0CB09`) and `Builds/MuseDesk_polish_flagsoff.apk` (`77463C…087CE`), built 2026-11-15 01:37 from `2b07752` plus the ReleaseConfig field. **These appear to predate the Desk V2, handoff fix, art merge and A16 merges. No rebuild is recorded.** Neither was installed.
- **Not run:** device FPS and soak (PERF-01 to 03), any worn check, live Claude model call, MRUK wall read on device, real ISDK hands with Select Agents, audio audibility.
- **WORN:** none.

## Open owner decisions

1. **Role colours.** Tester amber and Developer green overlap the state colours. The role colours are serialized on the card prefab and tied to character identity (`UI_AND_VISUAL_CONSISTENCY_AUDIT.md` row 2). Recommendation on file: soft pastel role accents.
2. **Wall art regeneration (Antigravity, in progress per owner brief).** The delivered art is mirrored against `HabitatLayoutBuilder` (shaft on the left, not the right; worst socket error 0.94 m). Two paths: regenerate the art to the layout (the mirror, sizes and socket names are spelled out in `ART_INTEGRATION_RESULT.md` section 6), or change the stage layout to the art and re-run the layout validator and the Living Office drive. Until chosen, the manifest stays empty and Wall stays proxy art.
3. **Outer-desk overhang.** Floor overhang 4.0 and 2.9 cm, Ceiling 6.0 and 4.9 cm (`DESK_CHOREOGRAPHY_LAYOUT.md` V2). An inward move closes the aisle or overlaps desks. Needs deck art re-fit, or a mode-dependent layout.
4. **Merge decision.** Whether any polish goes into the competition build. Current rule: none. Polish APKs are not on the headset.
5. **Build label.** `ReleaseConfig.asset` still reads `buildLabel: rc6-dev` (`FEATURE_FLAG_MATRIX.md` section 4).
6. **Model key.** Whether the Manager should use a live Claude model before the deadline. No key exists.

## Doc drift to fix before anyone reads the docs as current

- The desk-clearance FAIL (6 of 7 routes) still appears in `RELEASE_POLISH_CHANGELOG.md`, `S9C_SETTINGS_SAFETY_UI_CHANGES.md`, `IMPLEMENTATION_AUDIT.md` C6 and `FINAL_EXPERIENCE_QUALITY_CHECKLIST.md` M-5. `DESK_CHOREOGRAPHY_LAYOUT.md` V2 supersedes it (all cells pass).
- `SpatialIsolationTests` is reported as 1 failed in `LIVING_OFFICE_SHOWCASE.md` and 3 of 3 in `ART_INTEGRATION_RESULT.md`.

## Honest statement

The desk approval loop ran in the Editor (PLAY) and the EditMode and backend suites are green as recorded. Every motion, interaction, audio, Wall, Portal and Manager change is unseen on a Quest. Performance has never been measured on the polish build. Treat this branch as unvalidated until a person wears the build and fills in `docs/Polish/NEXT_DEVICE_TEST.md`.
