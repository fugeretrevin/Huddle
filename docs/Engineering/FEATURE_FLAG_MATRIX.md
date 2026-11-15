# Feature flag matrix (ReleaseConfig)

Date: 2026-11-15. Branch `feature/living-world-polish`. Written by the A16-docs workstream from docs and code reads only. Nothing was built, installed or run.

**Single source:** `Assets/MuseDesk/Resources/ReleaseConfig.asset` (committed values below, read 2026-11-15). Class: `Assets/MuseDesk/Scripts/Config/MuseDeskReleaseConfig.cs:11`. Applier: `Assets/MuseDesk/Scripts/Config/ReleaseConfigApplier.cs` (`Awake` at execution order -10000, `Apply` at lines 64-136).

**Evidence keys:** WORN = a person wore the headset and the doc records it. PLAY = Editor Play Mode with drivers or simulated hands, no HMD. AUTO = EditMode or pytest only. NOT_TESTED = no evidence. "Tests only" in the table means AUTO.

## 1. The flags

| Flag | Committed default (asset) | What it gates when on / off | Turned on by | Evidence (source) |
|---|---|---|---|---|
| `floorMode` | 1 (on) | Floor Office mode and its key. Off: no Floor key, floor code idle (`FINAL_FEATURE_FLAG_DECISION.md` table) | Committed asset (rc6-2 competition, polish_flagsoff and polish_validation all carry it as committed) | WORN on rc6-1 with open issues: Gate B "works, but too strict" (`RC6_HARDWARE_QA.md:29`). B5-B8 not itemised as passed. rc6-2 and later: PLAY only (`RC6_HARDWARE_QA.md:102`) |
| `ceilingMode` | 1 (on) | Ceiling Pod mode and key. Also disables the pull gesture (`ReleaseConfigApplier.cs:92`) | Committed asset | WORN on rc6-1 with three open problems (`RC6_HARDWARE_QA.md:31-33`). Gate C not passed. rc6-2 and later: PLAY only |
| `ceilingPullGesture` | 1 (on) | `CeilingPullGesture` component (reach up, pinch, pull down 12 cm) | Committed asset | PLAY (`HANDOFF_NEXT_SESSION.md` ceiling ideas round, `CeilingPullDrive`). Gate C's C6 pull row is not itemised in the rc6-1 worn summary: treat as unverified on device |
| `ceilingParticles` | 1 (on) | Aperture, tether and arrival particles (`CeilingDeploymentVFXController.particlesEnabled`). Handoff says turn these off first if fill rate hurts | Committed asset | PLAY only. Fill-rate cost flagged as unverified on device (`HANDOFF_NEXT_SESSION.md`, Antigravity VFX notes) |
| `ceilingPortalCone` | 1 (on) | Holographic portal cone between ceiling and pod | Committed asset | PLAY only. Not separately worn |
| `placementScanGrids` | 1 (on) | Scan-reveal grids for Floor and Ceiling placement (`CeilingScanReveal`, `FloorScanReveal`) | Committed asset | Worn comment only: "scan guide cards could be cleaner" (`RC6_HARDWARE_QA.md:34`). Grid behaviour PLAY |
| `voice` | 1 (on) | Voice command centre, mic key, voice chip on paper, pinch-to-talk. Voice gesture is built but reported off (`HANDOFF_NEXT_SESSION.md`) | Committed asset | PLAY (`VoiceDrive.Say`, 346 voice tests AUTO). Never worn. Mic needs internet on the headset (`HANDOFF_NEXT_SESSION.md` check list item 3) |
| `wallHabitat` | 0 (off) | Wall mode: habitat on a real wall. Gates every other Wall flag (`ReleaseConfigApplier.cs:84-88`) | Validation build only: `WallValidationConfig.On()` (`AgentScripts/WallValidationConfig.cs:19`) | NOT worn. AUTO (30+ placement tests, 31 offline logic), PLAY, SYNTH (`WALL_HABITAT_STATUS.md` table; `QUEST_EVIDENCE_MATRIX.md` WALL-01..05). MRUK wall read has never run on device |
| `wallManager` | 0 (off) | Manager office, plan-review paper (objective, steps, risks, provenance), Approve/Revise/Reject keys or the desk stamps (a held Approve/Reject stamp pressed on the paper calls the same guarded commit/reject; the mark is drawn only after the backend confirms), card printing and pick-up, blocked/failed reasons with Retry, summary receipt, plan picture rebuilt from `GET manager/status` after a resync. Needs `wallHabitat` | Validation build only | PLAY (offline demo board, live backend 3-card run) and backend pytest. NOT worn. Stamp-on-paper, Focus/deferred nods and resync rebuild: compiled, 36/36 LivingOffice EditMode tests; driver methods and `SimulateSessionReset` NOT compiled, NO Play run (see `LIVING_OFFICE_SHOWCASE.md`). Manager is a Developer clone with a cap and badge (`WALL_HABITAT_STATUS.md`) |
| `agentSelect` | 0 (off) | Select Agents: far pinch on an agent, small desk visit. Needs `wallHabitat` | Validation build only | PLAY with simulated rays only; real ISDK hands never used (`WALL_HABITAT_STATUS.md`; `QUEST_EVIDENCE_MATRIX.md` SIDE-01) |
| `agentBtw` | 0 (off) | Read-only /btw questions during a visit. Needs `agentSelect` | Validation build only | AUTO (pytest store byte-compare), PLAY. NOT worn. Claude provider never called (no key) |
| `largeAgentPortal` | 0 (off) | "Meet Larger" 1 m portal avatar. Needs `agentSelect` | Validation build only | SYNTH and PLAY. NOT worn. Avatar video NOT_TESTED (`QUEST_EVIDENCE_MATRIX.md` PORT-02) |
| `wallElevatorVfx` | 0 (off) | Elevator door/indicator light effects. Tooltip says "Cosmetic" (`MuseDeskReleaseConfig.cs:46`). **Docs say not implemented** (see section 4) | Forced false by `WallValidationConfig.Set` (line 24) | Not evidenced. Treat as no effect until a reader is found |
| `reducedMotion` | 0 (off) | Build default Reduced Motion: shorter transitions, no overshoot. A saved per-device PlayerPrefs value (`MuseDesk.ReducedMotion`) overrides it (`ReleaseConfigApplier.cs:70`) | Committed off. Validation build: left off (`RELEASE_POLISH_CHANGELOG.md:10`) | AUTO (`MotionTests`) and PLAY. NOT worn |
| `reducedMotionToggle` | 0 (off) | Shows the in-app "Calm motion" key (`MotionToggleKey.cs:68`). Hidden by scale when off | Validation build: `WallValidationConfig.On()` line 25. Competition build: `Off()` | PLAY (S9c). NOT worn (`NEXT_DEVICE_TEST.md` step 17) |
| `buildLabel` | `rc6-dev` | Only logged in the `[MuseDesk.Release]` line. Not updated for any release. `WallValidationConfig` appends `+wall` on On() and strips it on Off() | Not a flag | `RC6_SUBMISSION_CHECKLIST.md:11` (label change still unchecked) |

Desk mode has no flag and is always available (`MuseDeskReleaseConfig.cs:8`).

## 2. Which build turns which flag on

| Build | APK | Flags as set | Source |
|---|---|---|---|
| rc6-2 competition | `Builds/MuseDesk.apk` = `Builds/MuseDesk_rc6_2.apk`, SHA-256 `173C813F4840DD3A7DCA81430E1914B1A9BEBF0A3B4C3375CF1E7FD53DB9B963` | Floor, Ceiling, pull, particles, cone, grids, voice ON; all Wall flags OFF; reduced motion OFF. Called "All flags on" in the pre-Wall era. Wall flags did not exist in rc6-2 | `RC6_HARDWARE_QA.md:102`; `WALL_HABITAT_STATUS.md:35`. **Inferred** from the doc and the committed asset. The APK's own config was not read |
| polish_flagsoff | `Builds/MuseDesk_polish_flagsoff.apk`, SHA-256 `77463C50…1087CE` | Every Wall flag OFF. Equal to the committed asset in this branch (reduced motion OFF, toggle OFF) | `RELEASE_POLISH_CHANGELOG.md:11`, line 46 |
| polish_validation | `Builds/MuseDesk_polish_validation.apk`, SHA-256 `635BEAFA…0CB09` | `WallValidationConfig.On()`: wallHabitat, wallManager, agentSelect, agentBtw, largeAgentPortal ON; wallElevatorVfx OFF; reducedMotionToggle ON; reduced motion OFF; label gets `+wall` | `RELEASE_POLISH_CHANGELOG.md:10`; `WallValidationConfig.cs:15-31` |
| rc6-1 (worn) | `Builds/MuseDesk_rc6.apk`, SHA-256 `F560AA6F…16BCA` | "all flags on (validation build)" | `RC6_HARDWARE_QA.md:13` |

Note: the APK hashes come from docs. They were not re-hashed in this pass.

## 3. Dependency rules (enforced in code)

- Every Wall flag needs `wallHabitat` (`ReleaseConfigApplier.cs:84-88`).
- `agentBtw` and `largeAgentPortal` also need `agentSelect` (`ReleaseConfigApplier.cs:86-87`).
- `ceilingPullGesture` is off when `ceilingMode` is off (`ReleaseConfigApplier.cs:92`).
- Backend has its own gates, separate from the Unity flags: env `MUSEDESK_P3_SIDE_CONVERSATION_ENABLED`, `MUSEDESK_P3_MANAGER_ENABLED`, `MUSEDESK_P3_PLAN_RUNNER_ENABLED`. With these off the routes return 404 `FEATURE_DISABLED` (`WALL_HABITAT_RELEASE_FLAGS.md:8`, `WALL_HABITAT_STATUS.md`).

## 4. Contradictions and drift

1. `WALL_HABITAT_RELEASE_FLAGS.md:42` says the validation helper switches "the six flags". The code also sets `reducedMotionToggle` (`WallValidationConfig.cs:25`). The S9c doc records this. The Wall doc is stale.
2. `MuseDeskReleaseConfig.cs:46` describes `wallElevatorVfx` as a cosmetic effect. `WALL_HABITAT_RELEASE_FLAGS.md:37` and `WallValidationConfig.cs:24` say it is not implemented. `ReleaseConfigApplier.cs:88` passes it through. Whether any component reads `wallElevatorVfxEnabled` is **unverified**.
3. `HANDOFF_NEXT_SESSION.md:3` and `WALL_HABITAT_STATUS.md` name the Wall tag `wall-habitat-1`. `IMPLEMENTATION_AUDIT.md:3` says the polish branch starts from `ce19b28` (`wall-habitat-2`). The tags are not reconciled.
4. `RC6_HARDWARE_QA.md:13` calls rc6-1 "all flags on (validation build)". `FINAL_FEATURE_FLAG_DECISION.md:34` says ON in a build is "not a release decision". Both are true, but the rc6-2 competition build is the same flag set and is not a worn release.
5. `ReleaseConfig.asset` still says `buildLabel: rc6-dev`. The competition build is named rc6-2 in docs. The label was never changed.
