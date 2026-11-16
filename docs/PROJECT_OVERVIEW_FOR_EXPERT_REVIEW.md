# MuseDesk: complete project overview for expert review

Date: 2026-11-15, evening. Author: Claude Code, the engineering lead for this session, working with the owner and with Antigravity, the art and animation agent.
Competition: Meta Quest developer competition. **Submission deadline: 2026-11-18, noon Pacific.**
This document gives a reviewing model the whole picture in one place: what the product is, how it is built, what changed today, what is proven and how, what is not proven, and where the risks are. Every claim below names the evidence level it rests on.

> **Evidence levels**
> - **WORN:** tested by a person wearing the Quest 3.
> - **PLAY:** Unity Editor Play Mode, with no headset. Scripted drivers run the real scene.
> - **AUTO:** EditMode or pytest tests.
> - **NOT RUN.**
>
> Today's work is PLAY and AUTO only. **The rc8 build was installed on the Quest at the end of the day, but nobody has worn it yet.** The only WORN evidence in the project is the earlier rc6-1 hardware QA (`docs/Engineering/RC6_HARDWARE_QA.md`).

---

## 1. What MuseDesk is

MuseDesk is a Quest 3 **passthrough mixed-reality** app. The user supervises a small team of AI coworkers who live in the user's real room as cute miniature mascots, and does so mostly with their hands (hands-first; voice is optional):

- **Researcher, Developer, Tester:** the desk team. They do real work through the backend: research, code changes and tests in a sandboxed sample workspace.
- **Manager:** lives in the Wall Habitat. It drafts a plan from the user's objective, prints task cards, delegates them and summarises the results.

The core loop is the human in the loop:
1. Agents work.
2. An agent walks up with a physical **decision paper**.
3. The user reads it, inspects the details, and approves or rejects with a physical **stamp**.
4. The backend authorises the decision.
5. Agents hand the work to each other.
6. A completion artifact or receipt arrives.

### Invariants (`docs/AGENTS.md`)
1. Never misrepresent scripted demo activity as live work. Every surface shows its provenance: `[SCRIPTED DEMO]`, "Model-written, no tools", or LIVE.
2. The backend authorises everything. No animation, VFX or sound may show a confirmed decision before the backend acknowledges it. This is the "no animation lies" rule.
3. No unsafe PC access. Agents run in an isolated sample workspace with an allowlist.
4. One schema, one XR rig, no random package upgrades, hands-first.
5. Judges must be able to use it standalone. Every P0 path works on the Quest with no PC, in a clearly labelled demo mode.
6. Test on the actual Quest. Editor results are not proof.

---

## 2. Architecture

| Layer | Tech | Where |
|---|---|---|
| Client | Unity 6000.6.8f1, URP, Meta XR SDK 209 / Interaction SDK, MRUK, TextMeshPro (Inter only), Quest 3 | `Assets/MuseDesk/` |
| Main scene | `Assets/MuseDesk/Scenes/MainMRScene.unity` (one owner at a time; changes only through idempotent builder scripts in `AgentScripts/` with `Apply`/`Revert`) | |
| Pure logic | asmdef assemblies with no scene types, all unit-tested: `MuseDesk.Spatial.Core` (motion, hand ownership, placement, Wall layout/stage/elevator/portal, onboarding, UI type scale), `MuseDesk.Agents.Core`, `MuseDesk.Voice.Core` | `Scripts/Spatial/Core`, `Scripts/Agents/Core`, `Scripts/Voice/Core` |
| Backend | Python FastAPI, SQLite, REST plus a WebSocket event stream; v1 session API, v2 (Phase 2 board, focus, diagrams, artifacts), Phase 3 additive routes (Manager, `/btw`, runner) | `backend/app/` |
| Model provider | `ModelProvider` abstraction with Anthropic and **Meta** providers. **Meta is configured for all agent work** (`api.meta.ai/v1`, OpenAI-style `chat/completions`): `muse-spark-1.3` for plan and work, `muse-ember-1.0` for `/btw`. The key lives only in git-ignored `backend/.env` and is never in the APK | `backend/app/phase3/provider.py`, `docs/Engineering/sprint/META_LLAMA_PROVIDER.md` |
| Offline mode | Full scripted demo with no network (`Phase2DemoGateway`, Wall offline runner), always labelled | |
| Tooling | `unity` CLI driving a shared Editor: `scripts\compile.ps1`, `scripts\tests.ps1 <Fixture>`, `scripts\tests_batch.ps1 -Group <g>` (JSON results), `scripts\drv.ps1 Class Method` (Play Mode drivers in `AgentScripts/`), `scripts\run_guarded.ps1` (scene builders), `scripts\start_wall_backend.ps1` (backend; live with Meta when `.env` configures it), `scripts\backend_tests.ps1` | |

### Shared contracts (`docs/Engineering/INTEGRATION_CONTRACT.md`)
- **Agent identity:** a presentation, such as a desk-visit avatar or a portal avatar, is never a new backend worker.
- **Task state** is backend-authoritative.
- **Hand precedence:** a held object beats `AgentSelect`, which beats `Placement`, which beats `Summon`. An owner-aware `HandClaimRegistry` enforces it.
- **One writer per transform:** a `TransformOwnership` lease protocol exists and is tested, but is not yet wired into runtime; see section 7.
- **One colour meaning everywhere:** `OfficeTokens.StateColor` (teal working, amber needs-you, green done, coral problem, lavender-grey paused, neutral idle). Role colours (`OfficeTokens.RoleColor`) are soft pastels kept clearly apart from those.
- **Motion language:** `MuseDesk.Motion` (tokens, `MotionClock`, `ProfiledMover`, `Damped`, Reduced Motion). Interactables are never scaled to 0.

---

## 3. Features by area: current state

### 3.1 Desk mode (the main competition experience)

**Agents**
- Backend-driven prefabs with Antigravity's 77-clip shared animation library: idle, work, walk start and stop, turn left and right, offer, handoff, react and more.
- Animation follows real task state, through `AgentAnimatorBridge`.

**Movement** (S3/S4, S9a, desk layout V1 and V2)
- Walks use an acceleration, cruise and brake profile.
- Agents turn in place at corners sharper than 50°, with Turn_Left and Turn_Right clips.
- Facing is damped, and the walk-cycle rate follows ground speed.
- **Desk layout V2:** stations spread out, with an aisle between Researcher and Developer, a delivery spot at the aisle mouth, and authored handoff lanes.
- All 7 routes keep at least 0.017 m clearance from the real scene meshes at agent radius 0.08, in Desk, Floor and Ceiling (PLAY: `DeskClearanceDrive`). This replaces a layout where 6 of 7 routes clipped furniture, which was a WORN complaint in rc6-1.

**Handoff and delivery**
- The handoff token arcs between hands.
- The completion celebration waits for queued handoffs that the backend has actually reported, up to 15 s. This fixed a dropped Developer→Tester handoff: PLAY, 3/3 runs.

**Reactions** (S9b + A3)
- Poke, wave and glance reactions are routed through a `ReactionArbiter` with priority classes: Critical > Task > Social > Ambient.
- The arbiter adds cooldowns, a rate cap, Focus-mode suppression and a busy-hands gate.
- Idle variation is seeded per agent.
- Gaze uses `AttentionPolicy`: at most 3 s looking at the user, then back to work, so agents never stare.
- A confirmed-completion nod plays.
- Rollback flag: `AgentAttention.useReactionArbiter`.

**Listening**
- Only the agent the user addresses by voice plays Listening (`VoiceAddressing` → `ListeningGate`).
- With no agent addressed, all three tilt their heads as before.

**Decision paper and stamp**
- Hand-held paper with a details sheet.
- Approve/reject via the physical stamp or keys, using one in-flight lock and a stable idempotency key per action.
- A cancelled response no longer leaves the paper stuck on "Saving".

**Board (Kanban)**
- Cards glide between lanes and ease in and out, including on recycle.
- A resync diffs by task id, so only cards that really changed animate.
- A held card defers its hide until release and its drag is cancelled, so no stale assign is sent.
- Tracking loss cancels a drag.

**Sound**
- Procedural audio only (`FeedbackController`, `StampTool`, `WallAudio`, `CeilingPodAudio`).
- New cues fire only on real transitions: paper arrived, card landed.
- Sounds are muted in Focus mode.

**Settings**
- Focus mode.
- A "Calm motion" key (Reduced Motion, persisted per device).
- Onboarding hints (`OnboardingCoach` + `OnboardingHintPresenter`): one hint at a time, triggered only by real events, persisted, suppressed in Focus and while hands are busy.

**Role colours** (owner delegated the choice):

| Role | Colour |
|---|---|
| Researcher | Periwinkle `#A9B8F0` |
| Developer | Orchid `#D2A6E8` |
| Tester | Rose `#F0A9C4` |
| Manager | Indigo `#4B4F9C`, white text |

They appear on cards, nameplates, the paper, the Wall, the plan paper, and as the mascots' matte accent trims (Antigravity). A test keeps every role colour away from the state colours.

### 3.2 Floor Office and Ceiling Pod (owner priority: important, not to be cut)

- The same workspace, placed on the floor platform or a retractable ceiling pod.
- Antigravity refitted both decks to desk layout V2 today: 0 cm overhang, pads centred under the stations.
- The Ceiling pod deploys and stows: PLAY.

### 3.3 Wall Habitat and the "Living Office" (experimental features, now **enabled** in the competition build)

**Wall Habitat**
- A miniature multi-storey office mounted on a recognised wall: 3-tier Full or 2-tier Compact.
- Walkers follow validated polylines, with a swept-capsule clearance check on every tick.
- The elevator runs a state machine: `ElevatorDispatcher`, FIFO lane ownership, doors must be fully open before boarding, the rider is attached to the cabin, scale is preserved, stuck-door fallback, exactly-once completion.
- **Antigravity's art is regenerated to the code layout.** Fit check: 46/46 and 44/44 sockets FIT with 0.000 m error (`AgentScripts/WallArtFitCheck.cs`).
- The art is hooked up visually through `HabitatArtManifest`. Rollback: `useHabitatArt`.
- Stage geometry, not the art, drives movement and colliders.

**Living Office loop** (`docs/Engineering/LIVING_OFFICE_SHOWCASE.md`)
1. The user asks the Manager (ray poke or voice "Manager, …").
2. The Manager drafts a plan: a backend-validated schema, live via Meta or scripted in the demo.
3. A **plan-review paper** shows the objective, items, assignments, dependencies, outputs, risks and a provenance footer (`PlanReviewLayout`, `TypeScale`).
4. The user revises, rejects, or approves with the poke key **or the physical stamp**. APPROVED/REJECTED is drawn only after the backend confirms.
5. Cards print in the Manager office.
6. Each owner rides the elevator, collects its card and carries it to its cubicle, where the desk animation follows real state.
7. Blocked and failed cards show the backend's real reason, with an idempotent **Retry**.
8. A plan synthesis comes down as a **completion receipt**: model-written, labelled, or a factual roll-up.

Reconnect: the plan picture rebuilds from `GET /v2/sessions/{sid}/manager/status`.

PLAY results: `LivingOfficeDrive` RunOffline 13/13, RunOfflineWithFailure 16/16, RunOfflineStamp 13/13, RunLive (local backend, scripted) 13/13. In every run riders stay attached and nobody is in a doorway before the doors open.

**Agent select, desk visit, `/btw`, large portal**
- The user selects a Wall agent by ray and grabs its presentation onto a desk perch, then asks a quick question.
- `/btw` is read-only:
  - an intent classifier attaches bounded real context;
  - a change request becomes a `proposed_action` with `requires_confirmation`, and is never executed ("don't approve" never yields an approve);
  - with no model, it answers from task data and says so.
- The enlarged portal avatar is placed in side space only, never in front of the face. If space is missing, a "not enough space" fallback shows a small visitor instead.
- `PresentationLifecycle` keeps one presentation per logical agent and guarantees cleanup on backend loss.
- Rollback: `usePresentationLifecycle`.

### 3.4 Backend (`backend/`, 327 passed, 1 skipped, AUTO)

**Approvals and idempotency**
- Approvals are idempotent, including same-key and different-key duplicates, approve after reject, and cross-session attempts.
- A per-session, per-key async lock prevents concurrent duplicate execution. It applies to v2 and Phase 3.

**Task state**
- Terminal task states cannot be reopened.
- Parent state mirrors the outcome.

**Decisions**
- Decisions time out (`decision_timeout_seconds`, default 3600) and settle atomically, so exactly one of answer and expiry wins.

**Safety**
- Sandbox: no credentials in the subprocess environment; path traversal and symlinks denied; an empty or "." workspace is refused, which closes a wipe-all-sessions bug.
- Errors returned to the client are sanitised: no traces or paths.
- `source_mode` provenance is on every client-visible state.

**Manager and runner**
- Manager plan schema validation: roles, kinds, cycles, dangling dependencies, size limits.
- Dependency blocking carries `blocked_reason` and `blocked_by`.
- Retry, synthesis artifact, status and resync routes.
- The runner works committed plan cards one at a time, text-only and labelled.

**Model access**
- Meta provider:
  - retries on 429/5xx while honouring `Retry-After`;
  - maps timeouts to the existing timeout path and refusals to `ProviderRefused`;
  - never puts the key in logs or errors;
  - uses the OS trust store (Python's CA bundle failed for `api.meta.ai` on this PC).
- **Live smoke: HTTP 200 on both models. A live Manager plan was drafted with `LIVE_MODEL` provenance and validated.**
- Not yet exercised live: a model-written `/btw` answer and a full live runner pass.

---

## 4. What happened today (2026-11-15), in order

All of it is on `feature/living-world-polish`: 121 commits since tag `pre-living-world-polish` (`ce19b28`).

1. **Overnight polish pass**, tag `polish-1`:
   - four ownership audits (motion, input, UI/audio, characters);
   - the `MuseDesk.Motion` library;
   - S2 input safety: tracking-loss drag cancel, stable idempotency keys and in-flight locks, spec hand precedence;
   - S3/S4 desk motion;
   - S5/S6 Wall UI and visit polish;
   - S8 Wall audio, one state palette and Reduced Motion.
2. **S9a pop-ins:** card recycle, details sheet, resync diff, corner turn-in-place, attention sign lift.
3. **S9b characters and audio:**
   - Turn clips and confirmed-completion nod;
   - Focus mode and busy-hands gating;
   - held card defers its hide on recycle;
   - paper and card sound cues;
   - status monitor colours.
4. **S9c settings and safety:**
   - Calm motion key;
   - desk walk clearance checker, which found 6 of 7 routes clipping;
   - teal hover colour, speech-bubble provenance, Wall font fallback.
5. **Parallel sprint:** 16 workstreams run by cheap agents in worktrees and integrated one at a time.
   - Core logic: movement ownership, reactions, voice intents and addressing, elevator dispatcher, portal and visit lifecycle, type scale and plan layout, onboarding.
   - Backend: reliability and security, `/btw` routing, Manager delegation.
   - Tooling and docs: test tooling, performance pass, release docs, idempotency fixes.
6. **Desk layout V1 and V2:** the owner allowed moving the desks apart to stop clipping. Done on Fable, then a Ceiling follow-up.
7. **Living Office**, the big showcase ticket, built on Fable, plus follow-ups: physical stamp on the plan paper, Wall Focus mode, deferred nods, resync rebuild.
8. **Antigravity art merges:**
   - the 77-clip library, masks, sockets, VFX and galleries;
   - the Wall Habitat regenerated to the code layout and hooked up;
   - the deck refit and matte role-colour trims. Seven stale scene material overrides were reset by an undoable builder.
9. **Fixes found along the way:**
   - FBX reimport dropping `loopTime`, now guarded by `AgentClipLoopFlagTests`;
   - dropped Developer→Tester handoff;
   - the ignored elevator test (the dispatcher was right; the test setup was fixed);
   - plan revision lost after resync;
   - Living Office driver re-runs;
   - isolation-test comment leaks;
   - root-motion guard (test plus runtime);
   - decision-timeout race.
10. **Meta provider** for all agent work, verified live.
11. **Owner decision: "everything goes into the competition build."**
    - All flags ON.
    - rc7 built, then **rc8** with the deck refit and trims.
    - **rc8 installed on the Quest:** `adb install -r` Success.

---

## 5. Builds and rollback

| APK | What | SHA-256 |
|---|---|---|
| `Builds/MuseDesk.apk` = `Builds/MuseDesk_rc8.apk` | **Competition build**, `rc8-living-office`, every flag on. **Installed on the Quest; not yet worn** | `788D4A0401EB274EC39751E539C778F3CCC01D1AB9AC1638A03AB15A4C7E1428` |
| `Builds/MuseDesk_rc7.apk` | rc7, the same minus deck refit and trims | `80E23F3B54FD97F60C9C2BCEC0510041BAB05C8B91EF947027EA845C55A93E72` |
| `Builds/MuseDesk_rc6_2.apk` | Last build with WORN QA history (Wall features off) | `173C813F4840DD3A7DCA81430E1914B1A9BEBF0A3B4C3375CF1E7FD53DB9B963` |
| `Builds/MuseDesk_polish2_validation.apk` / `_polish2_flagsoff.apk` | Intermediate validation builds | see `docs/Polish/RELEASE_POLISH_CHANGELOG.md` |

- Git tags: `competition-rc8` on `911465e`, which is the current `release/final-refinement`; `competition-rc7`, `pre-deck-refit`, `pre-competition-enable`, `polish-1`, `pre-living-world-polish`, `wall-habitat-2`.
- APKs are built with `AgentScripts/BuildApkTo.cs`, which refuses to overwrite `MuseDesk.apk` or rc6 files directly.

**Feature flags** (`Assets/MuseDesk/Resources/ReleaseConfig.asset`, committed; `docs/Engineering/FEATURE_FLAG_MATRIX.md`)
- **On:** Desk, Floor, Ceiling, voice, `wallHabitat`, `wallManager`, `agentSelect`, `agentBtw`, `largeAgentPortal`, `wallElevatorVfx` (gates nothing yet), `reducedMotionToggle`, `onboardingHints`.
- **Off by default:** `reducedMotion`, because it's a user preference.
- **Runtime rollback switches:** `useReactionArbiter`, `usePresentationLifecycle`, `useHabitatArt`, `useElevatorDispatcher`, `BatchStaticStructure`.

---

## 6. Test evidence (latest runs, 2026-11-15)

| Suite | Result | Level |
|---|---|---|
| EditMode groups | interaction 204, motion 95, wall 198, board 80, spatial 277, real 50, voice 420; plus named fixtures (TypeScale, TextFit, PlanReviewLayout, AgentClipLoopFlag, AgentRootMotionGuard, RoleColour, HandoffWaitPolicy, WalkClearance, Onboarding, SpatialIsolation). **0 failures, 0 ignored.** Antigravity's full-suite run: 1360/1361 (the 1 skip has since been fixed) | AUTO |
| Backend pytest | 327 passed, 1 skipped (symlink test; the platform can't create symlinks) | AUTO |
| Desk demo (start → paper → approve → completed) at timeScale 1 | Completes, 0 console errors; with hints on | PLAY |
| Living Office | RunOffline 13/13, RunOfflineWithFailure 16/16, RunOfflineStamp 13/13, RunLive 13/13, ResyncCheck restores the revision | PLAY |
| Desk clearance | 7/7 routes, minimum 0.017–0.018 m, in all three modes | PLAY |
| Meta live | Smoke 200 OK; live Manager plan drafted and validated | live backend |
| Device | Only rc6-1 WORN QA: Gate A pass, Gates B/C worn with open issues | WORN (old) |
| Frame rate on Quest | **Never measured** for rc7 or rc8. Editor-only: about 3.6 ms per frame, 199 draw calls at desk idle | NOT RUN |

---

## 7. Known gaps, risks and open items (please weigh these)

**Highest risk**
1. **Nothing in rc7 or rc8 has been worn.** The build turns on every experimental feature that has only PLAY evidence. A judge-facing regression is the main risk; the rollbacks are rc7, then rc6_2.
2. **Quest performance is unmeasured.** The Wall art adds about 104 renderers where the proxies were batched. VFX counts are low per Antigravity's audit. TMP text, transparencies and the portal are not profiled on device. The profiling driver `AgentScripts/PerfProfileDrive.cs` exists but has been Editor-only.
3. **Hands.** Real-hand behaviour is unverified for:
   - the new stamp-on-plan-paper path (drivers bypass the held pose);
   - onboarding hint dismissal (fingertip dwell);
   - reaction gating while holding;
   - card grab during recycle.

**Not wired yet** (tested core logic, adapter plans in `docs/Engineering/sprint/*.md`)
- `TransformOwnership` leases (A1). This is the highest-risk refactor and was deliberately parked. Single-writer safety today comes from ad-hoc guards.
- The portal and desk-visit side-spot planner ignores screen and desk-front keep-out boxes. Only the front cone applies, and `ClearsUserEnvelope` is not wired.
- Voice intents: `VoiceIntentClassifier` exists, but the older `VoiceIntentParser` still drives the actions. The two grammars could disagree.
- Antigravity's 12 `.wav` cues are not wired, because procedural audio owns the cues.
- `wallElevatorVfx` gates nothing.

**Smaller items**
- Two key-visual systems (`KeyStateVisual`, `PokeKeyStateVisual`) are not merged; both are proven on the headset.
- Some onboarding hints have no source event yet and time out harmlessly.
- After a resync, the rebuilt Wall plan picture shows status and board but not the printed cards.
- Most Wall completion nods are deferred because agents are on pick-up trips.
- Voice requires Wit.ai and a live token, so it is unavailable in the offline demo.
- The `/btw` dedupe and the idempotency lock are process-local (single uvicorn worker).

**Repo housekeeping**
- `refs/stash` is broken and prints a harmless repack error on every commit.
- Disk is low: about 10 GB free; Antigravity's `.worktrees/` uses about 17 GB.
- Pre-existing unstaged changes are intentionally left alone: Inter fonts, `AgentTalk_Paper.mat`, the URP asset, `.blend1` files, `ArtSource/Scripts/generate_workstations.py`, and untracked `docs/00_*`, `02_*`, `03_*` and `README_START_HERE.md`.

---

## 8. What only a person can do before 2026-11-18

1. **Wear rc8.** Follow `docs/Polish/NEXT_DEVICE_TEST.md`; section D covers today's features. Watch most closely:
   - Wall frame rate and the elevator doors;
   - hint placement and readability;
   - the desk V2 composition and paper reach;
   - Floor and Ceiling decks;
   - role colours on the real display;
   - reactions and Listening;
   - the extra ~4 s before the completion celebration.
2. **Decide ship or rollback:** rc8, rc7 or rc6_2.
3. **Submit to Meta.** The owner does this; Claude never publishes.
4. **Optional, live AI demo:** run `scripts\start_wall_backend.ps1` (Meta) and pair the headset.

---

## 9. Where to read next (most useful first)

| Doc | Why |
|---|---|
| `docs/Polish/NEXT_DEVICE_TEST.md` | The human test script |
| `docs/Engineering/LIVING_OFFICE_SHOWCASE.md` | The showcase loop: vision, gap analysis, evidence per step |
| `docs/Polish/DESK_CHOREOGRAPHY_LAYOUT.md` | Desk V1/V2 positions, clearance tables, revert instructions |
| `docs/Engineering/PARALLEL_SPRINT_INTEGRATION_MAP.md`, `PARALLEL_SPRINT_TEST_RESULTS.md`, `PARALLEL_SPRINT_MASTER_REPORT.md`, `PARALLEL_SPRINT_REMAINING_WORK.md` | Every branch merged, its test counts, and what's left |
| `docs/Engineering/FEATURE_FLAG_MATRIX.md`, `INTEGRATION_CONTRACT.md`, `DEMO_SCRIPT_AND_SHOT_LIST.md`, `FINAL_EXPERIENCE_QUALITY_CHECKLIST.md` | Release docs |
| `docs/Engineering/ART_INTEGRATION_RESULT.md`, `docs/Art/DECK_REFIT_REPORT.md`, `docs/Art/WALL_HABITAT_FIT_REPORT.md` | Art integration and fit evidence |
| `docs/Engineering/sprint/*.md` | Per-workstream detail, including the unapplied adapter plans |
| `docs/Polish/RELEASE_POLISH_CHANGELOG.md`, `docs/Polish/IMPLEMENTATION_AUDIT.md`, `docs/Polish/audit/*` | Polish history and audits with file:line references |
| `docs/AGENTS.md` | The invariants |

---

## 10. Questions we would most like an expert opinion on

1. Given only PLAY evidence, is shipping rc8 with every experimental feature on the right call, or should Wall, Manager and portal be off for judges (rc8 with flags off is a one-line config change and a rebuild)?
2. On Quest 3, is about 104 extra renderers for the Wall art likely to hold 72/90 Hz alongside the desk scene? Should the Wall art be static-batched, or swapped back to the batched proxies on low frame rate?
3. Is the reaction arbiter tuning sensible (about 4 non-critical reactions per 6 s per desk, 3 s user gaze, 8 s offer cooldown), or will agents feel too quiet?
4. Anything in the hand-precedence model (held > AgentSelect > Placement > Summon) that will misfire with real hands?
5. For a judge's first five minutes, which single flow should the demo script lead with: the desk approval loop, or the Living Office?
