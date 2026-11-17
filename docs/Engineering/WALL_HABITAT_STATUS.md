# Wall Habitat: status (2026-11-15)

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
- **Claude is wired but has never been called.** `AnthropicProvider` (official SDK, `claude-opus-5-5`, effort per purpose, server-side refusal fallback) is built at startup when `MUSEDESK_MODEL_PROVIDER=anthropic` and a key are set. Verified with fake clients (pytest) and the real SDK against a local stub server; no real API call has been made (no key yet).
- **The live backend was exercised from Unity in the Editor (2026-11-15), in scripted mode.** PLAY: Unity went live against a local backend, placed the habitat, proposed a plan (`mp-…`), committed 3 cards, the backend plan runner worked them in order, the habitat animated the handoffs, and a `/btw` answer came back from the backend. Not on a headset.
- **Plan cards are worked as text only.** The plan runner (flag `MUSEDESK_P3_PLAN_RUNNER_ENABLED`) has the model write each step's deliverable with no tools: nothing is executed, applied or tested. Offline, the demo board works them with labelled scripted deliverables.
- **There are no device performance numbers.** The static structure is now merged (49 boxes into 7 renderers, PLAY).
- **The art is proxy art.** Antigravity's modules are not delivered. The Manager is a Developer clone with a cap and badge.
- **APKs were built, not installed.** I did not replace the competition build on the headset.

## APKs (2026-11-15 00:59, include the model provider, plan runner and merged structure)

| File | Flags | Use |
|---|---|---|
| `Builds/MuseDesk_wall_validation.apk` | wall, manager, select, btw, portal ON | Headset validation only |
| `Builds/MuseDesk_wall_flagsoff.apk` | all OFF | Proves the branch builds release-safe |
| `Builds/MuseDesk.apk` | rc6-2 (unchanged) | Competition |

Install the validation build with: `adb -s 2G97C5ZHCK037K install -r Builds\MuseDesk_wall_validation.apk` (platform-tools adb from the Unity Editor). Reinstall `Builds\MuseDesk_rc6_2.apk` afterwards.
