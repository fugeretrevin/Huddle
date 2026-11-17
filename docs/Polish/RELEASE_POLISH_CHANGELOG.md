# Living-World polish: changelog and report (2026-11-15, overnight, unattended)

**Branch** `feature/living-world-polish` (from `feature/wall-habitat` @ `ce19b28`). **Restore tag** `pre-living-world-polish`. **Tag** `polish-1`.
**Competition build untouched:** `Builds/MuseDesk.apk` = `MuseDesk_rc6_2.apk`, SHA-256 `173C813F…B9B963`. Nothing is merged into `release/final-refinement`.

## APKs (built 2026-11-15 01:37–01:38 from `2b07752` + ReleaseConfig field)

| File | Flags | SHA-256 |
|---|---|---|
| `Builds/MuseDesk_polish_validation.apk` | Wall, Manager, Select, /btw, Portal ON; Reduced Motion off | `635BEAFA99469344CB351E278E3FE05087D5C681311F67FF5BD6E1DA36F0CB09` |
| `Builds/MuseDesk_polish_flagsoff.apk` | all Wall flags OFF (release-safe check) | `77463C50B7E17B5E70CDB620AA6C123FD25705F80CC66325E9F061E9DF1087CE` |

Both built with 0 errors. **Neither was installed:** nobody was wearing the headset, and the competition install was left alone.

## APKs polish2 (built 2026-11-15 19:12 and 19:17 from the Wall-art hookup commit `6e6a597`)

Built with `AgentScripts/BuildApkTo.cs` (named output path; refuses `Builds/MuseDesk.apk` and anything `rc6`), after `WallValidationConfig.On()` / `.Off()`. The committed `ReleaseConfig` was restored afterwards (all Wall flags 0, reducedMotionToggle 0, onboardingHints 0; `git diff` clean). `Builds/` is git-ignored, so the APKs are not committed. Both succeeded with 0 errors. **Neither was installed and nothing in them has been worn.**

| File | Flags | SHA-256 |
|---|---|---|
| `Builds/MuseDesk_polish2_validation.apk` | Wall, Manager, Select, /btw, Portal, Calm motion key, onboarding hints ON; Wall art manifest filled (`useHabitatArt` true) | `BF31B98F862CE667168DA7CA69C7AEE00D313972E601AA7CD1A9E99709D36560` |
| `Builds/MuseDesk_polish2_flagsoff.apk` | all Wall flags OFF, hints OFF (release-safe check) | `2160B001320A7461466C07AFC1110DDC12F09349340FDCEAFB4B7B4F0F8215A5` |

Competition build check: `Builds/MuseDesk.apk` and `MuseDesk_rc6_2.apk` both still `173C813F4840DD3A7DCA81430E1914B1A9BEBF0A3B4C3375CF1E7FD53DB9B963`. Device steps: `NEXT_DEVICE_TEST.md` section D (all NOT WORN).

## Commits

| Commit | Sprint | What |
|---|---|---|
| `27b0507` | S0, S1 | Four audits; `MuseDesk.Motion` (eases, tokens, `MotionClock`, `ProfiledMover`, `Damped`, Reduced Motion, `MuseMotionProfile`); Wall walker velocity profile, turn in place, early lease; elevator accelerate and brake, eased doors; stride matching; desk clips from real task state; nod on confirmed completion |
| `c6d74e4` | S2 | Tracking loss cancels a card drag; one in-flight command per card and per decision with stable idempotency keys; a cancelled Respond clears "Saving"; claim precedence AgentSelect > Placement > Summon with an owner-aware registry; voice gesture and summon ray guards (builder agent) |
| `d0ac9e7` | docs | Implementation audit, design system, agent spec, evidence matrix |
| `3352dae` | S5, S6 | Wall keys and cards reveal on tokens (never scale 0), visible disabled keys, larger provenance footer, habitat grows out of the wall, damped carry, exact timed perch landing, eased portal grow, portal avatar listens instead of fake talking |
| `b2813c2` | S3, S4 | Desk: board card glide and reveal; agent walk speed profile, turn in place, damped facing, walk-rate floor; handoff token arc flight; paper reveal and hide; single-writer guards (builder agent) |
| `34c911e` | S8 | `WallAudio` (door, ding, print tick, portal open and close, landing); Wall key click; Wall colours = tokens; Reduced Motion setting (ReleaseConfig + per-device pref) |
| `2b07752` | S8 | One state palette (`OfficeTokens.StateColor`) for board, agent glows, workstation displays and the Wall |

| S9b commit | S9b | Desk Turn_Left/Right from `TurnInPlaceChanged`; confirmed-completion `Nodding`; ambient idle gestures off under Focus; hand reactions quiet while holding or carrying; held card defers its recycle hide and its drag is cancelled; `PaperArrived` / `CardLanded` cues, Focus-muted; monitor colours on the palette (`S9B_CHARACTER_AUDIO_CHANGES.md`) |

| S9c commit | S9c | In-app "Calm motion" key `MotionToggleKey` (flag `reducedMotionToggle`, 0 committed); desk walk clearance driver and geometry helper (found 6 of 7 authored routes failing at the 0.08 collider radius; fixed later by the desk layout V1/V2, all 7 now pass); hover colour on the Teal token; LIVE / SCRIPTED DEMO footer on the agent speech bubble (receipt, archive and voice HUD already had it); `WallUi.MakeText` font fallback to Inter with a one-time warning (`S9C_SETTINGS_SAFETY_UI_CHANGES.md`) |

## Commits 363bf84..8c2ca85 (added 2026-11-15, oldest first)

| Commit | Subject |
|---|---|
| `95418d5` | Polish S9a: remaining pop-ins - card recycle and details sheet ease on panelHide/panelAppear (cancelable), RebuildAll diffs by task id, desk routed corners brake and turn in place (TurnInPlaceDirection), attention sign lift damped |
| `ffbcedd` | Polish S9b: desk Turn_Left/Right from TurnInPlaceChanged, confirmed-completion Nodding, ambient idle gestures off under Focus, hand reactions quiet while holding or carrying, held card defers recycle hide and cancels its drag, PaperArrived/CardLanded cues (Focus-muted), monitor colours on the palette |
| `9a6fbc6` | Polish S9c: Calm motion toggle key (flag reducedMotionToggle, 0 committed), desk walk clearance driver, Teal hover, bubble provenance footer, Inter font fallback |
| `54a4139` | Sprint A16: feature flag matrix, demo script + shot list, integration contract, experience quality checklist (docs only) |
| `b2a1cee` | Sprint A1: TransformOwnership lease registry, OwnedMover, tests, ownership + root-motion doc |
| `38ffefb` | Sprint A8: /btw intent routing, proposed actions, honesty labels, dedupe and rate limit |
| `72a8d21` | Sprint A3: ReactionArbiter + AttentionPolicy pure core, EditMode tests, adapter plan |
| `aa887ed` | Merge Sprint A8: /btw read-only intent routing, proposed actions, labels, dedupe |
| `694dae4` | Sprint A12: voice intent classifier, agent addressing, per-agent listening events |
| `820a350` | Sprint A14: batch EditMode runner, test catalog, backend junit runner, real-type drift tests, TESTING.md |
| `edb4378` | Sprint A9: portal spot planner, avatar scale, presentation lifecycle + tests (uncompiled) |
| `21be48e` | Sprint A4b: stable idempotency keys for focus and diagram requests |
| `3ea85fa` | Sprint QA: EditMode sweep results |
| `109d901` | Sprint A10: manager plan schema, failure blocking, retry, status, synthesis |
| `6943f80` | Merge Sprint A10: Manager plan schema validation, dependency blocking, retry, synthesis, status |
| `c81872c` | Sprint A7: elevator dispatcher, wait spots, floor route graph, tests |
| `110742b` | Sprint A6: type scale, text fit, plan review layout (pure, uncompiled) + tests + adapter doc |
| `8dbb122` | Merge Sprint A1: TransformOwnership |
| `f6a3cad` | Sprint A15: first-use onboarding coach, hint catalog, tests, doc (uncompiled) |
| `591d671` | Sprint A16: integrate A1 (metas) |
| `71f23d0` | Merge Sprint A3: ReactionArbiter |
| `31f210c` | Sprint A16: integrate A3 (metas) |
| `e487756` | Merge Sprint A9: portal visit lifecycle |
| `7a0b45a` | Sprint A16: integrate A9 (metas) |
| `0f2d81e` | Merge Sprint A7: elevator dispatcher |
| `0c5a14f` | Sprint A11: backend reliability - terminal task immutability, decision timeout, run outcome parity, workspace root guard, sanitised 500s, v1 snapshot provenance |
| `9da8188` | Sprint A16: integrate A7 (metas, rider speed zeroed on boarding, 1 test ignored) |
| `f3cb017` | Merge Sprint A4b: LatestWinsToggle |
| `d6f8edf` | Sprint A16: integrate A4b (metas) |
| `8c5b8a9` | Merge Sprint A12: voice core |
| `5b8636c` | Sprint A16: integrate A12 (metas) |
| `6ad2a73` | Merge Sprint A14: test tooling |
| `6e0eb26` | Sprint A16: integrate A14 (gitignore TestResults) |
| `136e1d7` | Merge Sprint A6: presentation layout |
| `b1b3dae` | Sprint A16: integrate A6 (metas) |
| `4a815b2` | Merge Sprint A15: onboarding coach |
| `37a0496` | Sprint A16: integrate A15 (metas) |
| `d60b4b0` | Sprint A16: integrate A13 (PerfProfileDrive + doc; HeldPaperController/StationDropZone fixes already present on HEAD via GrabState/MPB, kept HEAD) |
| `c09fa31` | Sprint A16: PerfProfileDrive wrappers/file rows, FocusToggleDrive, HaloCheckDrive |
| `1d01fd1` | Merge Sprint A11: backend reliability |
| `6be865c` | Sprint A16: tolerant /btw A8 fields in Wall parser (label, negated proposed_action) + test |
| `ee6101f` | Sprint A16: integration map + after-integration results |
| `c41dfb1` | Living Office: ElevatorDispatcher behind the habitat walkers (wait spot, face door, FIFO ride lane, card pick-up and receipt acts) + EditMode tests |
| `d9a7673` | Living Office: PlanBoard (real card states, reasons, retry lock), plan-paper mapper and pager, manager revise + commit event, status/retry parsing, offline roll-up + tests |
| `0b8f842` | art: complete Phase 3 Wall Habitat and unified 65-clip experience polish |
| `92842d9` | Living Office: Wall wiring - dispatcher-run cabin, plan-review paper (A6 layout, pages, stamp after confirmation, revise/reject), card tray + pick-up by elevator, real blocked/failed reasons + Retry, summary receipt to the desk, A3 reactions, A12 listening + voice Manager request, LivingOfficeDrive |
| `a203ee4` | Living Office: plan status carries the recorded result (+pytest for the shapes the wall parses), voice Manager hook only while the flag is on, showcase doc |
| `4c69bd3` | Living Office: paper re-laid out at 10 Hz, driver pacing and stamp check, expected timings in the doc |
| `c035c35` | Desk layout: authored walk lanes, route joining and handoff facing |
| `7f87991` | Desk layout: spread the desks, aisle delivery, handoff lanes (builder, scene, drivers, doc) |
| `5c1703e` | Merge commit '0b8f842' into art/living-world-polish |
| `642ace5` | art: living-world polish — 77-clip library, masks, sockets, audio cues, Quest VFX library, and 4 validation galleries |
| `8e9d4d8` | Fix SpatialIsolationTests: HintCatalog comments no longer name scene-side SessionController (A15 regression; comment-only) |
| `3839809` | Merge branch 'feature/living-world-polish' into worktree-agent-a55d86ba31d43eb88 |
| `f00a187` | Merge Living Office showcase (Wall Habitat + Manager end to end) |
| `51f5166` | Sprint A10 wiring: GET manager/status, GET plan status, POST card retry (idempotent, manager_gate) + route tests; backend 302 passed |
| `9449b2f` | Living Office: driver reports through a file (each script run is a fresh assembly) |
| `df9675d` | Merge Living Office: driver report file |
| `d93f08e` | Living Office: paper grows up from the old card's bottom edge (clear of the stamps), driver checks the APPROVED stamp, no doubled 'Plan:' in the offline roll-up |
| `56fbb03` | Merge Living Office: PLAY fixes |
| `ab144ef` | Living Office: generated .meta files |
| `dd69980` | Living Office: showcase doc with Editor evidence (EditMode, PLAY offline/failure/live, flag-off check) |
| `bf4345c` | Merge Living Office: evidence doc |
| `d59237c` | Fix SpatialIsolationTests: HintCatalog comments no longer name SessionReducer (comment-only) |
| `b6789c6` | Merge art/living-world-polish (Antigravity art: 77-clip desk agents, Wall habitat prefabs, VFX, audio) |
| `1e64175` | Art merge: ArtMergeDrive (desk animator state log, shots), merge plan, desk screenshots |
| `2ec5d62` | Desk layout V2: Researcher station +70 mm toward the user, tray and decor with it, five lane points; Ceiling clears (min 0.017), Desk and Floor stay >= 0.018 |
| `0bf7341` | Wall art: fit check tool, result doc; art does not fit HabitatStage (mirrored layout), manifest stays empty; integration map row |
| `26d3f43` | Desk demo: completion waits for queued reported handoffs (Developer to Tester no longer dropped at 1x); HandoffWaitPolicy + tests, trace driver |
| `d49b389` | Art guard: EditMode test that Agent_* FBX clips keep loopTime; result doc section |
| `8c2ca85` | WallArtFitCheck: accept contract socket names (TesterCubicleSocket, ManagerOfficeSocket) before legacy art names |

Detail per change: `S9C_SETTINGS_SAFETY_UI_CHANGES.md`, `S9B_CHARACTER_AUDIO_CHANGES.md`, `S2_INTERACTION_CHANGES.md`, `S3S4_DESK_MOTION_CHANGES.md`, `IMPLEMENTATION_AUDIT.md`.

## Tests

- **EditMode, fixture by fixture:** 37 of 59 fixtures ran in one regression sweep, all passing with 0 failures. Two of the 37 are the dormant fixtures that sit behind a define, as before. The sweep was **stopped by the host for low system memory** before the last 21 fixtures, `PlacementGuideLogicTests` onward. These were run individually after their own changes and passed at that time: SpatialIsolation (3), HabitatMotion (22), Motion (32, which includes HabitatMotion), WallPlacement (26), AgentVisitPortalManager (19), WallOfflineLogic (31), HandOwnership (10), GrabTrackingWatch (4), CommandLedger (3), BoardPresenterLogicSpec (22), DemoGatewayDecisionSpec (5), AgentWalkPath (5). The rest (several Spatial*, Stamp, Voice, Wave, SmallLogic, SessionReducerRuntime) **were not re-run after the polish changes**.
- **Backend:** 207/207 pytest.
- **Play Mode (Editor, no HMD):**
  - Wall visit: perch landing to d = 0.000.
  - Wall plan: three cards worked offline and live.
  - Desk demo run: start, paper, approve, completed.
  - The console showed 0 captured errors in each run. One desk run had one uncaptured Editor error with no entry (likely XR start-up without a headset; unexplained).
- **Device:** none. **Nothing in this pass is worn-verified.** See `QUEST_EVIDENCE_MATRIX.md` and `NEXT_DEVICE_TEST.md`.

## Feature flags (ReleaseConfig, committed)

`wallHabitat 0, wallManager 0, agentSelect 0, agentBtw 0, largeAgentPortal 0, wallElevatorVfx 0, reducedMotion 0, reducedMotionToggle 0` (the last is new in S9c: the in-app Calm motion key, hidden by scale when 0; `WallValidationConfig.On()` turns it on for a validation build, `Off()` for the competition build). Desk, Floor and Ceiling are on as in rc6-2.

## Remaining defects and open items (severity)

| Sev | Item |
|---|---|
| High | No headset validation of any motion, interaction or audio change (the whole of `NEXT_DEVICE_TEST.md`) |
| Med | 21 EditMode fixtures not re-run after the changes (memory stop) |
| Med | Antigravity Wall art and the Manager model are uncommitted work in their worktree; production still uses proxy boxes and the Developer-clone Manager |
| Med | Card role colours (Tester amber, Developer green) still overlap state colours: a prefab and identity decision for the owner (`UI_AND_VISUAL_CONSISTENCY_AUDIT.md` #2) |
| Low | ~~No in-app toggle for Reduced Motion~~ DONE in S9c behind `reducedMotionToggle` (0 in the release): needs a worn check (`NEXT_DEVICE_TEST.md` step 16) |
| **Resolved (Editor/PLAY)** | **Desk walk clearance (S9c): was 6 of 7 routes overlapping a prop at r 0.08 m.** Superseded by the V2 layout (DESK_CHOREOGRAPHY_LAYOUT.md, V2): all 7 routes pass in Desk, Floor and Ceiling, minimum 0.017 m at r 0.080 (target 0.015). Editor/PLAY evidence only, not worn; outer-desk deck overhang in Floor/Ceiling is still open. History: R1/R2 were -0.066 m, R3 to R7 -0.004 to -0.010 m. |
| Low | ~~Card recycle and details-sheet `SetActive` pops; RebuildAll re-reveals every card on a resync~~ FIXED in S9a (see `S9A_POPIN_CHANGES.md`) |
| Low | ~~Desk routed-walk corners above the turn limit turn while walking (Wall stops and turns)~~ FIXED in S9a (brake, turn in place, continue; `AgentMovement.TurnInPlaceDirection`) |
| Med | `Listening` clip not wired on the desk: no voice signal says the mic is open AND addressed to one agent (needs agent-targeted voice routing) |
| Low | Desk page-turn sound still missing; `Delivered` chime now silent under Focus |
| Low | Two key visual systems not merged (open by decision). ~~Off-palette hover cyan; provenance gaps~~ FIXED in S9c |
| Info | Claude provider wired but no real API call has been made (no key yet) |

## Competition build rc7-living-office (2026-11-15, owner decision: everything goes in)

Tag `pre-competition-enable` = the state before this pass; tag `competition-rc7` = the result. Rollback APK `Builds/MuseDesk_rc6_2.apk` (SHA-256 `173C813F...B9B963`) was left untouched.

- **Role colours** (soft pastels, shown with the role name, never confusable with the saturated state tokens): Researcher Periwinkle `#A9B8F0`, Developer Orchid `#D2A6E8`, Tester Rose `#F0A9C4`, Manager Indigo `#4B4F9C` (OnPlate text). Source of truth `OfficeTokens.RoleColor` (`Scripts/Workspace/OfficeUi.cs`); mirrored in Core `PlanPalette` (RoleResearcher..RoleManager); `RoleColourTests` (5) check parity, mapping (unknown = Neutral) and the distance from every state token. Routed through it: task cards (`CardRoleTint`, card text is now Ink on every strip), agent nameplate strips (`AgentNameplate`, property block), status chip (`AgentAttention`), decision panel role chip, perch, Wall placeholder avatars and the Manager cap. No prefab or scene edit.
- **Committed ReleaseConfig, all features ON:** wallHabitat, wallManager, agentSelect, agentBtw, largeAgentPortal, wallElevatorVfx, reducedMotionToggle, onboardingHints = 1 (plus Floor, Ceiling, pull, particles, cone, grids, voice as before). `reducedMotion` stays 0 (a user preference default). `buildLabel` = `rc7-living-office`. `wallElevatorVfx`: the applier passes it into `WallFeatureFlags.wallElevatorVfxEnabled`, but no component reads that flag, so it gates nothing yet. `WallValidationConfig.On()` still forces it false (older helper, not edited); the committed asset is the source for rc7.
- **Verified (Editor, 0 failures):** compile clean; EditMode groups interaction 204, motion 95, wall 198, board 80, spatial 277, real 50, voice 420, plus TypeScale, TextFit, PlanReviewLayout, AgentClipLoopFlag, AgentRootMotionGuard, HandoffWaitPolicy, WalkClearance, OnboardingCoach, SpatialIsolation (122), RoleColourTests 5. Play with the committed config: desk demo at timeScale 1 start, paper, approve, Completed; onboarding presenter active; 0 console errors. LivingOfficeDrive RunOffline, RunOfflineWithFailure and RunOfflineStamp all steps OK, 0 rider/doorway violations, 0 console errors. DeskClearanceDrive Report: Desk min 0.032, Floor min 0.018, Ceiling min 0.017 (all above 0.015). Editor screenshot: `docs/review_screenshots/polish/role_colours_desk.png`. Editor renders only; not worn.
- **APK:** `Builds/MuseDesk_rc7.apk` (built with `BuildApkTo`, 91.8 MB) SHA-256 `80E23F3B54FD97F60C9C2BCEC0510041BAB05C8B91EF947027EA845C55A93E72`; `Builds/MuseDesk.apk` is a copy (identical hash, owner-authorized replacement). `Builds/MuseDesk_rc6_2.apk` still `173C813F4840DD3A7DCA81430E1914B1A9BEBF0A3B4C3375CF1E7FD53DB9B963`. `Builds/` is git-ignored. Not installed, not worn.

## Competition build rc8-living-office (2026-11-15)

Tag `pre-deck-refit` = state before; tag `competition-rc8` = result. Merged `art/deck-refit` (Floor platform and Ceiling pod decks refit to desk layout V2, 0 cm overhang; matte role-colour mascot trims `Mat_RoleTrim_*`) and removed 7 legacy per-instance material overrides on the three desk agents (`AgentRoleTrimOverridesV1`, record in `docs/Polish/AgentRoleTrimOverrides_record.txt`). `buildLabel` = `rc8-living-office`; all flags as in rc7. Evidence: Editor/Play only, nothing worn, nothing installed.

| APK | Role | SHA-256 |
|---|---|---|
| `Builds/MuseDesk_rc8.apk` | competition build (BuildApkTo: Succeeded, 0 errors, 1299.8 MB) | `788D4A0401EB274EC39751E539C778F3CCC01D1AB9AC1638A03AB15A4C7E1428` |
| `Builds/MuseDesk.apk` | copy of rc8 | `788D4A0401EB274EC39751E539C778F3CCC01D1AB9AC1638A03AB15A4C7E1428` |
| `Builds/MuseDesk_rc7.apk` | first rollback (untouched) | `80E23F3B54FD97F60C9C2BCEC0510041BAB05C8B91EF947027EA845C55A93E72` |
| `Builds/MuseDesk_rc6_2.apk` | second rollback (untouched) | `173C813F4840DD3A7DCA81430E1914B1A9BEBF0A3B4C3375CF1E7FD53DB9B963` |