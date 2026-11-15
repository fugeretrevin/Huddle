# Living-World polish: changelog and report (2026-11-15, overnight, unattended)

**Branch** `feature/living-world-polish` (from `feature/wall-habitat` @ `ce19b28`). **Restore tag** `pre-living-world-polish`. **Tag** `polish-1`.
**Competition build untouched:** `Builds/MuseDesk.apk` = `MuseDesk_rc6_2.apk`, SHA-256 `173C813F…B9B963`. Nothing is merged into `release/final-refinement`.

## APKs (built 2026-11-15 01:37–01:38 from `2b07752` + ReleaseConfig field)

| File | Flags | SHA-256 |
|---|---|---|
| `Builds/MuseDesk_polish_validation.apk` | Wall, Manager, Select, /btw, Portal ON; Reduced Motion off | `635BEAFA99469344CB351E278E3FE05087D5C681311F67FF5BD6E1DA36F0CB09` |
| `Builds/MuseDesk_polish_flagsoff.apk` | all Wall flags OFF (release-safe check) | `77463C50B7E17B5E70CDB620AA6C123FD25705F80CC66325E9F061E9DF1087CE` |

Both built with 0 errors. **Neither was installed:** nobody was wearing the headset, and the competition install was left alone.

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

Detail per change: `S9B_CHARACTER_AUDIO_CHANGES.md`, `S2_INTERACTION_CHANGES.md`, `S3S4_DESK_MOTION_CHANGES.md`, `IMPLEMENTATION_AUDIT.md`.

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

`wallHabitat 0, wallManager 0, agentSelect 0, agentBtw 0, largeAgentPortal 0, wallElevatorVfx 0, reducedMotion 0`. Desk, Floor and Ceiling are on as in rc6-2.

## Remaining defects and open items (severity)

| Sev | Item |
|---|---|
| High | No headset validation of any motion, interaction or audio change (the whole of `NEXT_DEVICE_TEST.md`) |
| Med | 21 EditMode fixtures not re-run after the changes (memory stop) |
| Med | Antigravity Wall art and the Manager model are uncommitted work in their worktree; production still uses proxy boxes and the Developer-clone Manager |
| Med | Card role colours (Tester amber, Developer green) still overlap state colours: a prefab and identity decision for the owner (`UI_AND_VISUAL_CONSISTENCY_AUDIT.md` #2) |
| Low | No in-app toggle for Reduced Motion (config and PlayerPrefs only) |
| Low | ~~Card recycle and details-sheet `SetActive` pops; RebuildAll re-reveals every card on a resync~~ FIXED in S9a (see `S9A_POPIN_CHANGES.md`) |
| Low | ~~Desk routed-walk corners above the turn limit turn while walking (Wall stops and turns)~~ FIXED in S9a (brake, turn in place, continue; `AgentMovement.TurnInPlaceDirection`) |
| Med | `Listening` clip not wired on the desk: no voice signal says the mic is open AND addressed to one agent (needs agent-targeted voice routing) |
| Low | Desk page-turn sound still missing; `Delivered` chime now silent under Focus |
| Low | Two key visual systems not merged; off-palette hover cyan; provenance gaps on agent bubble and receipts |
| Info | Claude provider wired but no real API call has been made (no key yet) |
