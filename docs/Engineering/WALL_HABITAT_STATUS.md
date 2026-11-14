# Wall Habitat: status (2026-11-14)

Branch `feature/wall-habitat`, from `e14df8e` (rc6-2). Restore tag: `pre-wall-habitat`. Baseline APK: `Builds/MuseDesk_pre_wall_baseline.apk` (byte-identical to rc6-2). `Builds/MuseDesk.apk` is still rc6-2.

**Competition rule:** every Wall flag is OFF in ReleaseConfig. Nothing here is merged into `release/final-refinement`.

| Milestone | State | Evidence |
|---|---|---|
| M0 Baseline | Branch, tag and baseline APK done; baseline build 0 errors; backend 145 tests passing before the work | AUTO, APK |
| M1 Wall placement | Evaluator (doors, windows, furniture, other walls, edges, user, monitor cone, view angle), ghost with words, ray drag, explicit Place here, persistence and restore, room-change suspend, no-scan manual (amber), Desk fallback | AUTO 30+ tests, PLAY |
| M2 Layout + elevator | Full and Compact from one builder, validated (0 issues); nav graph; corridor leases; elevator machine; event choreography from real reducer events | AUTO (every-tick clearance asserts), PLAY (elevator round trip) |
| M3 Far-agent visit | Select Agents, AgentSelect claim, projection carry, perch, read-only /btw (backend + offline), change request becomes a suggestion, return on loss, pause, recenter or mode change | AUTO, PLAY, backend pytest |
| M4 Manager | Backend propose, revise, reject and commit (atomic, idempotent, revision-checked, dangerous items need authorisation); plan card; printer prints only canonical committed ids | backend pytest, AUTO, PLAY (offline demo board) |
| M5 Portal | Floor spot evaluator (swept column, not in front, not behind, clear of furniture and walls, low-ceiling scaling), confirm step, one at a time, comfort timings | AUTO, PLAY |
| M6 Quest polish | **Not started.** Needs a person in the headset | — |

## Not verified (honest list)

- **Nothing has been worn.** All W1-W14 checks in `WALL_HABITAT_INTERACTION_QA.md` are open.
- **No real MRUK wall data has been read.** The MRUK wall reader is compiled but has never run on a device. Wall orientation (anchor +Z into the room) follows MRUK's own code, unverified on device.
- **Real ISDK hands have not been used with Select Agents.** PLAY used simulated rays.
- **The live backend has not been exercised from Unity.** PLAY ran the offline/scripted path. Backend routes are pytest-covered, flags default off. No AI model adapter ships, so answers are `TASK_SNAPSHOT` and plans are `TEMPLATE_PLAN` until a provider is wired.
- **Committed plan cards are tracked, not executed.** This backend's workers execute only `auth_demo` templates.
- **There are no device performance numbers.**
- **The art is proxy art.** Antigravity's modules are not delivered. The Manager is a Developer clone with a cap and badge.
- **APKs were built, not installed.** I did not replace the competition build on the headset.

## APKs (2026-11-14 12:44)

| File | Flags | Use |
|---|---|---|
| `Builds/MuseDesk_wall_validation.apk` | wall, manager, select, btw, portal ON | Headset validation only |
| `Builds/MuseDesk_wall_flagsoff.apk` | all OFF | Proves the branch builds release-safe |
| `Builds/MuseDesk.apk` | rc6-2 (unchanged) | Competition |

Install the validation build with: `adb -s 2G97C5ZHCK037K install -r Builds\MuseDesk_wall_validation.apk` (platform-tools adb from the Unity Editor). Reinstall `Builds\MuseDesk_rc6_2.apk` afterwards.
