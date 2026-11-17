# Parallel sprint: remaining work (gap analysis against the direction docs)

Date: 2026-11-15. Branch `feature/living-world-polish`. Deadline: 2026-11-18 noon Pacific.
Scope: read-only review of the direction docs against the repo docs. No code, scene, prefab or APK was touched.
Companion: `docs/Engineering/PARALLEL_SPRINT_MASTER_REPORT.md`.

**Nothing in the polish branch has been worn on a Quest.** Every "done" below is AUTO (EditMode or pytest), PLAY (Editor Play Mode with simulated hands, no HMD), or APK (built, not installed). Rows that need a person are marked NT or BLOCKED-needs-human.

## Keys

- **Status:** DONE / PARTIAL / NOT STARTED / BLOCKED-needs-human / BLOCKED-needs-art.
- **Evidence:** AUTO, PLAY, APK, LOG, WORN, NT (not tested), FAIL. WORN appears nowhere below.
- **Owner:** Claude code / Antigravity art / human headset / owner decision.
- **Size:** S (under half a day), M (about a day), L (several days or a risky runtime change).
- **Priority for 2026-11-18:** P0 protect the release (competition build, Desk approval loop, red gates). P1 polish that would be visible if the owner merges the branch. P2 experimental, flag-gated. P3 later.

Competition build: `Builds/MuseDesk.apk` = rc6-2, SHA-256 `173C813F…B9B963` (`docs/Polish/RELEASE_POLISH_CHANGELOG.md` line 4). Nothing from this branch is in it.

---

## Top 10: next things Claude can do without a human

Concrete, code-level, no headset or art needed. Ordered by release risk first.

1. **[S, P0] Settle the red SpatialIsolation fixture.** `LIVING_OFFICE_SHOWCASE.md` section 3 reports `SpatialIsolationTests.SceneSideScripts_NameNoGatewayReducerOrSessionType` FAILED ("HintCatalog.cs mentions SessionReducer"). `ART_INTEGRATION_RESULT.md` section 2 reports 3/3 passing. Run that one fixture. If it is still red, make a comment-only edit in `Assets/MuseDesk/Scripts/Spatial/Core/Onboarding/HintCatalog.cs` (line 120 is the class).
2. **[S, P0] Runtime guard for root motion.** `docs/Engineering/sprint/A1_MOVEMENT_OWNERSHIP.md` line 53: enforcement is build-time only. Add an EditMode test that loads the agent prefabs and asserts `applyRootMotion == false`, plus a one-line set in the agent controller `Awake`.
3. **[S, P0] Correct the stale desk-clearance verdict.** Four docs still say 6 of 7 desk routes FAIL at r 0.080: `docs/Polish/RELEASE_POLISH_CHANGELOG.md` (remaining-defects table, Med row), `docs/Polish/S9C_SETTINGS_SAFETY_UI_CHANGES.md` section 2, `docs/Polish/IMPLEMENTATION_AUDIT.md` row C6, `docs/Engineering/FINAL_EXPERIENCE_QUALITY_CHECKLIST.md` row M-5. `docs/Polish/DESK_CHOREOGRAPHY_LAYOUT.md` (V2 section) shows all 21 cells pass (minimum 0.017). Doc-only edit, but it stops the owner being told the wrong thing.
4. **[S, P1] Focus key idempotency.** `Assets/MuseDesk/Scripts/Workspace/FocusModeController.cs` line 161 builds the key from a per-call counter (`++_idem`). `Assets/MuseDesk/Scripts/Diagrams/ArchitectureDiagramController.cs` line 113 already goes through `CommandLedger.KeyFor(lockId, ...)`. Move Focus to the same pattern and add an EditMode test. This is gap B-4 in `FINAL_EXPERIENCE_QUALITY_CHECKLIST.md`.
5. **[S, P1] Finish the state-colour migration.** Replace the copied colours with `OfficeTokens.StateColor` in `Assets/MuseDesk/Phase2/Runtime/Board/TaskCardView.cs` (about lines 420-423), `Assets/MuseDesk/Scripts/Agents/AgentView.cs` (about 192-197), `Assets/MuseDesk/Scripts/Art/WorkstationStatusDisplay.cs` (about 45-46), `Assets/MuseDesk/Phase2/Runtime/Board/StationDropZone.cs` (about line 30). Use the same lines `docs/Polish/UI_AND_VISUAL_CONSISTENCY_AUDIT.md` rows 1 and 6 name. State colours only. Do not touch role accents (owner decision).
6. **[M, P1] Portal and visit lifecycle.** Apply `PresentationLifecycle` and `PortalFlow` (`Scripts/Spatial/Core/Wall/PresentationLifecycle.cs:134`, `PortalPlacement.cs:171`) to the live visit and portal path (`AgentVisitFlow.cs:114` and the Wall controller). The adapter list is in `docs/Engineering/sprint/A9_PORTAL_VISIT.md` ("What the view layer must call"). The 25 lifecycle tests already pass. Note: no class named `AgentVisitPortalManager` exists; only its test fixture does.
7. **[M, P1] Attention arbitration on the desk.** Apply the A3 adapter in `Assets/MuseDesk/Scripts/Agents/AgentAttention.cs` `IdleGestures` (`Random.Range` at lines 817, 825, 842, 850 become `AmbientVariation`). Route completion, failure and poke through `ReactionArbiter` (`Scripts/Agents/Core/ReactionArbiter.cs:66`). Adapter list: `docs/Engineering/sprint/A3_REACTIONS.md` lines 22-30. Spec section 6 (attention priority).
8. **[L, P1] One writer per root pose.** Apply the A1 lease protocol (`Scripts/Spatial/Core/Motion/TransformOwnership.cs:63`) to the two root-pose writers, `HabitatWalker.cs:44` and `Scripts/Agents/AgentMovement.cs:16`. Adapter plan: `docs/Engineering/sprint/A1_MOVEMENT_OWNERSHIP.md`. Spec section 5.1. Highest-risk item. Re-run `WalkClearanceTests`, `HabitatMotionTests`, `DeskClearanceDrive Report`, and `LivingOfficeDrive.RunOffline` afterwards.
9. **[M, P2] Listening only for the addressed agent.** Feed `VoiceAddressing` (`Scripts/Voice/Core/VoiceAddressing.cs:17`) into the desk animator bridge so only the addressed desk agent plays `Listening`. Desk Listening is not wired (`RELEASE_POLISH_CHANGELOG.md`, Med row). No new mic capture.
10. **[M, P2] Lerp-by-dt audit.** List every `Lerp(..., deltaTime)` or `MoveTowards` motion under `Assets/MuseDesk/Scripts` that has a fixed endpoint. Convert them to `MuseMotionProfile` timing (spec section 4). `IMPLEMENTATION_AUDIT.md` M1 says adoption is ongoing, and `FINAL_EXPERIENCE_QUALITY_CHECKLIST.md` M-12 says conversion is unverified.

---

## A. Master polish spec (`C:\Users\stoof\Downloads\00_MASTER_POLISH_SPEC.md`)

| ID | Requirement (section) | Status | Evidence | Owner | Size | Pri |
|---|---|---|---|---|---|---|
| A-01 | Team inhabits the room; coherent places, purposeful movement (1.1) | PARTIAL | Desk lanes clear at r 0.080 (`DESK_CHOREOGRAPHY_LAYOUT.md` V2); worn NT | Human headset | S | P1 |
| A-02 | One logical agent, presentation is not a second executor (1.2) | PARTIAL | `/btw` read-only AUTO/PLAY (`WALL_HABITAT_STATUS.md` M3); A9 lifecycle not applied (`PARALLEL_SPRINT_INTEGRATION_MAP.md` row 3) | Claude code | M | P1 |
| A-03 | No animation lies; live/demo provenance (1.3) | PARTIAL | Stamp only after commit (PLAY, `LIVING_OFFICE_SHOWCASE.md` section 6); desk Listening unwired; Wall art unhooked | Claude code | S | P1 |
| A-04 | Affordance, feedback, commitment, recovery for every hand action (1.4) | PARTIAL | S2 single-flight and cancel (AUTO/PLAY, `S2_INTERACTION_CHANGES.md`); worn NT | Human headset | M | P0 |
| A-05 | No popping, clipping, teleporting, stuck states; timeouts (1.5) | PARTIAL | S9a pop-ins fixed (`S9A_POPIN_CHANGES.md`); elevator timeouts AUTO; desk page-turn sound missing; worn NT | Claude code | S | P1 |
| A-06 | One motion language and design system (1.6) | PARTIAL | `OfficeTokens` used by Wall and board; copies remain (`UI_AND_VISUAL_CONSISTENCY_AUDIT.md` rows 1, 6); role colours open | Claude code + owner decision | S | P1 |
| A-07 | Habitat attaches to valid geometry or user-confirmed fallback (1.7) | PARTIAL | Placement evaluator, no-scan manual fallback, Desk fallback (AUTO 30+ tests, PLAY). MRUK wall read never run on device (`WALL_HABITAT_STATUS.md` section "Not verified") | Human headset | M | P2 |
| B-01 | Character behaviours: idle, work, read, type, test, think, wait, move, stop, turn (2 B, 6) | PARTIAL | Desk clips from real task state, 0 errors, AUTO/PLAY (`ART_INTEGRATION_RESULT.md` section 3). Thinking/Listening on desk not wired (`MOTION_AND_INTERACTION_DESIGN_SYSTEM.md` section 3) | Claude code | M | P1 |
| B-02 | Focus, talk, listen, wave, respond to touch, deliver/receive, return home (2 B) | PARTIAL | Poke, wave, handoff, return home PLAY; talk/speak not built (no TTS, `HANDOFF_NEXT_SESSION.md` line 53); worn NT | Claude code + human | M | P2 |
| B-03 | Needs-approval, optional suggestion, blocked, completion reactions (6) | PARTIAL | Completion nod and Success PLAY (S9b); blocked/failed Wall cards with reasons PLAY; suggestion beacon desk side partial | Claude code | S | P1 |
| B-04 | Attention priority; gaze not every frame (6) | PARTIAL | A3 not wired on desk (`sprint/A3_REACTIONS.md`); Wall arbiter live (`LIVING_OFFICE_SHOWCASE.md` section 3) | Claude code | M | P1 |
| C-01 | Desk, Floor, Ceiling, Wall modes (2 C) | DONE (PLAY) | XR-04 PASS (PLAY) in `QUEST_EVIDENCE_MATRIX.md`; Ceiling and Floor worn on earlier builds only (`HANDOFF_NEXT_SESSION.md`) | Human headset | - | P0 |
| C-02 | Wall cubicles, walkways, manager nook readable (2 C; 02 Priority B) | PARTIAL | Proxy boxes only; art not hooked (`ART_INTEGRATION_RESULT.md` section 6) | Antigravity art + owner decision | L | P2 |
| C-03 | Local agent paths, valid route, no walk through furniture (5.2) | PARTIAL | Clearance AUTO (`WalkClearanceTests` 12/14, `AgentWalkPathTests`), desk PLAY all lanes >= 0.017; worn NT | Human headset | S | P1 |
| C-04 | Multi-floor elevators with reservation, interlock, recovery (5.4) | PARTIAL | Dispatcher AUTO 21 tests; PLAY 10 rides, 0 failed, 0 doorway-with-doors-closed frames (`LIVING_OFFICE_SHOWCASE.md` section 6). Wall flags OFF; worn NT | Human headset | S | P2 |
| C-05 | Courier and handoff choreography (2 C, 5.3) | PARTIAL | Desk exchange and Wall carries PLAY; token arc unseen on head (M-7) | Human headset | S | P1 |
| C-06 | Ceiling deployment, unobtrusive until summoned (2 C, 3) | PARTIAL | Ceiling VFX and audio PLAY only, never worn (`HANDOFF_NEXT_SESSION.md` ceiling round); particles fill-rate unknown (V-6) | Human headset | S | P1 |
| C-07 | Portal enlarged avatar, safe volume, return (2 C, 7) | PARTIAL | Floor spot evaluator AUTO/PLAY (M5); scale-pop and rig check NT (PORT-02) | Antigravity art + Claude | M | P2 |
| C-08 | Room-relative placement and re-centering (2 C, 5.5) | PARTIAL | Revalidate on room change AUTO/PLAY; recenter mid-ride and mid-portal NT (I-9, XR-03) | Human headset | S | P1 |
| D-01 | Input ownership, state-driven animation (2 D) | PARTIAL | Hand precedence AUTO 10 tests (`HandOwnershipTests`); worn NT (XR-01) | Human headset | - | P0 |
| D-02 | Transform ownership, one writer per bone or root (5.1) | PARTIAL | Audit M10 done; lease registry not used by any runtime mover (`PARALLEL_SPRINT_INTEGRATION_MAP.md` row 1) | Claude code | L | P1 |
| D-03 | Backend idempotency (2 D, 8) | PARTIAL | Board and decisions keyed (S2); Focus still counter-keyed (`FocusModeController.cs:161`) | Claude code | S | P1 |
| D-04 | Transition cancellation, no orphan animators (4, 12) | PARTIAL | `ProfiledMover`, `Damped`, cancel on reset AUTO; no device check | Claude code | S | P2 |
| D-05 | Asset and prefab validation (2 D) | PARTIAL | `AgentClipLoopFlagTests` (d49b389), `WallArtFitCheck` (0bf7341, 8c2ca85) | Claude code | S | P2 |
| E-01 | `MuseMotionProfile` ScriptableObject (4) | DONE (AUTO) | `MotionTests` 32 pass (`IMPLEMENTATION_AUDIT.md` M1) | - | - | - |
| E-02 | Motion adoption across all paths (4) | PARTIAL | Adoption ongoing; see top-10 item 10 | Claude code | M | P2 |
| E-03 | Curve preview inspector on a test scene (01 Sprint 1) | NOT STARTED | No evidence in docs | Claude code | M | P3 |
| E-04 | Reduced Motion (4, 9) | DONE (AUTO/PLAY) | `ReducedMotionPreferenceTests` 7; in-app key behind `reducedMotionToggle` (0 in release). Worn NT | Human headset | S | P1 |
| E-05 | Foot IK on validated surfaces (5.3) | NOT STARTED | Optional in spec; stride matching only (M4) | Claude code | M | P3 |
| E-06 | Seated sockets: hips, feet, hands, chair (5.3) | PARTIAL | Workstation pose not checked (MOT-02 NT) | Human headset + Antigravity art | M | P1 |
| E-07 | Carried items detach at animation event (5.3) | PARTIAL | Handoff token arc PLAY; worn NT (M-7) | Human headset | S | P1 |
| F-01 | Board, card and paper UI (9) | PARTIAL | Cards, glide, reveal, paper PLAY (S3S4); worn NT (UI-01 to UI-04) | Human headset | M | P0 |
| F-02 | Three-tier card disclosure, expanded metadata (9) | PARTIAL | Card face and toolbar exist; expanded tier not evidenced | Claude code | M | P2 |
| F-03 | Stamp optional, never blocks reading, one submit (3, 9) | PARTIAL | Single submit AUTO/PLAY; physical stamp tool not wired to plan paper (`LIVING_OFFICE_SHOWCASE.md` section 3 "Not done") | Claude code | M | P2 |
| F-04 | Panels reposition, release focus on close (9) | DONE (PLAY) | `PanelsDrive` PLAY; `PanelHeightTests` 8 | Human headset | - | P1 |
| F-05 | Effects: symbol plus sound for success, no celebration on minor change (9) | PARTIAL | Symbols in place; sounds unheard (U4 "PARTIAL (unheard)") | Human headset | S | P1 |
| F-06 | Text fits, Inter TMP, safe padding, glyph samples (3, 02 Sprint 3) | PARTIAL | `TextFitDrive` worst case >= 2.3 mm; `TextFitTests` 15; localized and extended glyph samples not started | Claude code | S | P1 |
| F-07 | Near-field legibility on head (3, 12) | BLOCKED-needs-human | Editor only (`FINAL_EXPERIENCE_QUALITY_CHECKLIST.md` V-4) | Human headset | - | P0 |
| G-01 | Controls in seated reach, Escape/Cancel/Return Home without voice (10) | PARTIAL | Console Stop and Cancel keys PLAY; reach numbers only for the Wall delivery spot | Claude code + human | S | P1 |
| G-02 | Intent arbitration as one router (10, 02 Sprint 2) | PARTIAL | Precedence in `HandOwnershipTests` and claim registry; no single router class | Claude code | M | P2 |
| H-01 | Object pools for cards and effects, MPB for states (11) | PARTIAL | MPB halo PLAY (A13); pooling not evidenced | Claude code | S | P2 |
| H-02 | Quest performance: FPS, CPU/GPU per mode, soak (11, 12, 13 step 8) | BLOCKED-needs-human | No device numbers. Last device run was rc6-1 (`FINAL_EXPERIENCE_QUALITY_CHECKLIST.md` P-1). A13 is Editor-only | Human headset | M | P0 |
| H-03 | Stable Desk APK and flagged Wall mode (11, 14) | DONE (APK) | Flags-off build built 0 errors; device startup NT (`RELEASE_POLISH_CHANGELOG.md`) | Human headset | S | P0 |
| I-01 | Clipping: 0 visible penetrations on main paths (12) | PARTIAL | Automated and PLAY clearance; worn NT | Human headset | S | P0 |
| I-02 | Interaction: 9 of 10 per critical grab, poke, drop (12) | NOT STARTED | Needs worn tries | Human headset | S | P0 |
| I-03 | Evidence labelling, never one "passed" (12) | DONE | Docs use AUTO/PLAY/APK/NT throughout | - | - | - |
| J-01 | Required outputs, section 14 (6 docs) | DONE | `MOTION_AND_INTERACTION_DESIGN_SYSTEM.md`, `AGENT_ANIMATION_AND_NAVIGATION_SPEC.md`, `WALL_ELEVATOR_AND_PORTAL_QA.md`, `UI_AND_VISUAL_CONSISTENCY_AUDIT.md`, `RELEASE_POLISH_CHANGELOG.md`, `QUEST_EVIDENCE_MATRIX.md` all exist in `docs/Polish/` | - | - | - |
| J-02 | Before and after captures, repro clips, test scene with fixtures (14) | PARTIAL | Editor renders in `docs/review_screenshots/polish/`; no headset captures; no dedicated fixture scene | Claude code + human | M | P1 |
| K-01 | Phase 6 hardware polish (00 section 13 step 8) | NOT STARTED | Needs a person in the headset (`WALL_HABITAT_STATUS.md` M6) | Human headset | L | P3 |

## B. Claude/Fable engineering handoff (`01_CLAUDE_FABLE_ENGINEERING_HANDOFF.md`)

| ID | Requirement (sprint) | Status | Evidence | Owner | Size | Pri |
|---|---|---|---|---|---|---|
| S0-1 | Audit, baseline, rollback checkpoint (Sprint 0) | DONE | Four audits in `docs/Polish/audit/`, `IMPLEMENTATION_AUDIT.md`, tag `pre-living-world-polish`, APK hashes recorded | - | - | - |
| S0-2 | Headset baseline video (Sprint 0 step 4) | NOT STARTED | None | Human headset | S | P3 |
| S1-1 | Cancellable transitions with fixed endpoints (Sprint 1) | PARTIAL | `ProfiledMover`, `Damped`; `MotionTests` 32 | Claude code | S | P2 |
| S1-2 | Guards: zero duration, suspend, destroyed target (Sprint 1) | PARTIAL | Zero-duration and reduced-motion guards; app suspend not evidenced | Claude code | S | P2 |
| S1-3 | Developer inspector to preview curves (Sprint 1) | NOT STARTED | None | Claude code | M | P3 |
| S2-1 | One interaction owner per hand, arbitration (Sprint 2) | PARTIAL | Precedence AgentSelect > Placement > Summon (AUTO 10, `S2_INTERACTION_CHANGES.md` section 4) | Claude code | M | P1 |
| S2-2 | Four stages on every interaction (Sprint 2) | PARTIAL | Hover, engaged and committed on cards and keys; not on every control | Claude code | M | P2 |
| S2-3 | Pinch-vs-target tests, both hands, loss, double gesture (Sprint 2) | PARTIAL | Simulated-ray PLAY; real hands never used (SIDE-01) | Human headset | M | P0 |
| S3-1 | Card state chain idle to hover to held to pending to slot or refused (Sprint 3) | PARTIAL | PLAY (`S3S4_DESK_MOTION_CHANGES.md`); card return path avoiding boards and bodies not checked | Claude code | S | P1 |
| S3-2 | Paper: world pose kept, no 100x bone scaling, roll limits (Sprint 3) | PARTIAL | Reveal and hide PLAY; roll limits and scale check not evidenced | Claude code | S | P1 |
| S3-3 | Artifact viewer and receipt near user, reposition, close (Sprint 3) | PARTIAL | Movable panels PLAY; viewer worn NT (UI-05) | Human headset | S | P1 |
| S4-1 | Presentation hierarchy: baseline, navigation, interaction overlays (Sprint 4) | PARTIAL | Reaction arbiter on Wall only; desk layers inert (`ART_INTEGRATION_RESULT.md` section 3) | Claude code | M | P1 |
| S4-2 | Habitat paths, max speed, stride, arrival pose (Sprint 4) | DONE (AUTO/PLAY) | `HabitatMotionTests` 22; stride match unseen on head (M4) | Human headset | - | P1 |
| S4-3 | Handoff timeout fallback separate from backend (Sprint 4) | DONE (AUTO/PLAY) | `HandoffWaitPolicy` (26d3f43), `DeskDemoHandoffDrive` 3 of 3 runs | - | - | - |
| S4-4 | Scene test drivers per state, multi-angle screenshots (Sprint 4) | PARTIAL | Drivers exist; no driver per state; screenshots are Editor only | Claude code | M | P2 |
| S5-1 | Read the Wall manifest, not guessed sockets (Sprint 5) | PARTIAL | Manifest empty (`artVersion` proxy-0); `WallArtFitCheck` shows mirrored layout (`ART_INTEGRATION_RESULT.md` section 6) | Owner decision + Antigravity art | L | P2 |
| S5-2 | Modules align to validated scale; 0/90/180 orientation; recenter tests (Sprint 5) | NOT STARTED | No evidence for 0/90/180 | Claude code | M | P2 |
| S5-3 | Manual reposition with limits; desk fallback (Sprint 5) | DONE (AUTO/PLAY) | `WallPlacementTests` 26; manual amber mode PLAY | Human headset | - | P2 |
| S6-1 | Select Agents mode: outlines, names, presentation_session_id, proxy (Sprint 6) | PARTIAL | `AgentSelect` claim and PLAY with simulated rays; halo art unhooked | Antigravity art + Claude | M | P2 |
| S6-2 | Perch landing, confirm, cancel and return (Sprint 6) | PARTIAL | Exact perch landing PLAY (d = 0.000); confirm step worn NT | Human headset | S | P2 |
| S6-3 | Read-only `/btw`, suggestions not applied (Sprint 6) | DONE (AUTO/PLAY/pytest) | `test_p3_*`, SIDE-02 AUTO/PLAY | - | - | - |
| S6-4 | Large portal: volume, confirm, side choice, dedicated rig (Sprint 6) | PARTIAL | Floor spot and confirm AUTO/PLAY; dedicated scaled rig not delivered | Antigravity art | L | P2 |
| S6-5 | Portal test list: recenter, cancel, pause, tracking loss, backend drop, focus (Sprint 6) | PARTIAL | Cancel AUTO/PLAY (PORT-03); the rest NT | Claude code + human | M | P2 |
| S7-1 | Manager typed plan, approvals, server IDs, cards once (Sprint 7) | DONE (AUTO/PLAY live) | MGR-02 PLAY live (3 cards); `LIVING_OFFICE_SHOWCASE.md` section 6 | - | - | - |
| S7-2 | Manager model-backed (Sprint 7, 00 section 8) | BLOCKED-needs-human | No API key; provider never called (`WALL_HABITAT_STATUS.md` line 22) | Human (owner key) | S | P2 |
| S7-3 | Manager placeholder honest; Developer clone in production (Sprint 7) | BLOCKED-needs-art | `IMPLEMENTATION_AUDIT.md` C7 | Antigravity art | M | P2 |
| S8-1 | Tokens on all four modes and the Manager (Sprint 8) | PARTIAL | Wall yes; board and agent copies no (top-10 item 5) | Claude code | S | P1 |
| S8-2 | Prefab variants for thick cards, wide keys, beacons (Sprint 8) | NOT STARTED | None | Antigravity art | M | P3 |
| S8-3 | Restrained audio, intensity settings (Sprint 8) | PARTIAL | Procedural `WallAudio`, `CeilingPodAudio`; no sound intensity setting; Antigravity wavs unused (`ART_INTEGRATION_RESULT.md` section 4) | Claude code + human | S | P2 |
| S8-4 | Reduced Motion and Focus Quiet (Sprint 8) | DONE (AUTO/PLAY) | `MotionTests`; Focus mutes cues (S9b) | - | - | - |
| S8-5 | Before and after screenshots for desk, wall, floor, ceiling, near-user (Sprint 8) | PARTIAL | Desk, floor, ceiling captures exist; wall captures are proxy art; near-user partial | Claude code | S | P2 |
| S9-1 | Compile, suites, live and offline runs, APK hash and flags (Sprint 9) | DONE (AUTO/PLAY/APK) | `PARALLEL_SPRINT_TEST_RESULTS.md`; `Builds/MuseDesk_polish_validation.apk`, `..._flagsoff.apk` | - | - | - |
| S9-2 | Quest performance baseline and stress (Sprint 9) | BLOCKED-needs-human | None on device | Human headset | M | P0 |
| S9-3 | Headset validation matrix (Sprint 9, list of 14 items) | NOT STARTED | Worn checklist in `WALL_ELEVATOR_AND_PORTAL_QA.md`, `NEXT_DEVICE_TEST.md` | Human headset | M | P0 |
| S9-4 | Keep the Desk release configuration stable (Sprint 9) | DONE | `ReleaseConfig` flags 0; `Builds/MuseDesk.apk` untouched | - | - | - |
| ACC-1 | Acceptance 1: no visible clipping in core path | PARTIAL | AUTO and PLAY only | Human headset | S | P0 |
| ACC-2 | Acceptance 2: natural start, stop, turn on video | NOT STARTED | Needs 60 fps headset video (MOT-01) | Human headset | S | P1 |
| ACC-3 | Acceptance 3: 9 of 10 for documents, cards, rays | NOT STARTED | Worn | Human headset | S | P0 |
| ACC-4 | Acceptance 4: Wall placement user-confirmed and recoverable | PARTIAL | AUTO/PLAY; worn NT (WALL-01) | Human headset | S | P2 |
| ACC-5 | Acceptance 5: side visits and portal never spawn a second executor | PARTIAL | AUTO `/btw`; PLAY visit; no worn run | Human headset | S | P2 |
| ACC-6 | Acceptance 6: Manager creates only server-confirmed cards | DONE (AUTO/PLAY) | Stamp only after commit; cards only for committed ids | - | - | - |
| ACC-7 | Acceptance 7: complete the core flow without voice | PARTIAL | Hand keys and panels exist; not evidenced worn | Human headset | S | P0 |
| ACC-8 | Acceptance 8: 10 to 15 min soak, known failures flagged | NOT STARTED | None | Human headset | M | P0 |
| ACC-9 | Acceptance 9: Desk, Floor, Ceiling unchanged, rollback works | PARTIAL | PLAY regression; rollback startup NT (R-6) | Human headset | S | P0 |

## C. Antigravity handoff items that Claude must integrate (`02_ANTIGRAVITY_ART_ANIMATION_HANDOFF.md`)

The rest of this file is Antigravity's to deliver. Only the integration side is listed.

| ID | Requirement | Status | Evidence | Owner | Size | Pri |
|---|---|---|---|---|---|---|
| AG-1 | Socket manifest `PathNode_*`, `ElevatorEntry_*`, `ElevatorExit_*`, `DoorClearance`, `AgentStand_*`, `ManagerDeskSocket`, `Handoff_*`, `CardOutput_*`, `PortalSource_*`, `RaySelectCollider` (02 Priority B) | BLOCKED-needs-art | None of the 02 names were found in the delivered art. The art uses layout-style names (`ResearchCubicleSocket`, `ElevatorCarSocket`, `CourierDispatchSocket`, and so on; table in `ART_INTEGRATION_RESULT.md` section 6). Two are missing (`TesterCubicleSocket`, `ManagerOfficeSocket`). `HabitatArtManifest` is empty | Antigravity art | M | P2 |
| AG-2 | Read-only socket check against the manifest (Claude side of AG-1) | PARTIAL | `WallArtFitCheck` (0bf7341, 8c2ca85) reads the composite prefab only, not a manifest file | Claude code | S | P2 |
| AG-3 | Cutaway collision and clearance overlay scene (02 Priority B) | NOT STARTED | None | Antigravity art + Claude | M | P3 |
| AG-4 | Wall art regeneration against the layout (owner option) | BLOCKED-needs-art | Antigravity regenerating (owner brief); two options, owner choice (section 6 of `ART_INTEGRATION_RESULT.md`) | Owner decision + Antigravity | L | P2 |
| AG-5 | Portal rig variant and scaled colliders (02 Priority A, D) | BLOCKED-needs-art | `ART_INTEGRATION_RESULT.md` section 6 ("not evaluated") | Antigravity art | M | P2 |
| AG-6 | Select Agents ring or glow, desk visit perch art (02 Priority D) | PARTIAL | Perch landing proxy PLAY; halo art not evaluated | Antigravity art + Claude | S | P2 |
| AG-7 | Attention icons with shape and colour (02 Priority D) | PARTIAL | Colours on palette; shape not evidenced | Antigravity art | S | P2 |
| AG-8 | Audio kit (02 Priority E) | PARTIAL | 12 wavs unused; procedural cues in use (decision in `ART_INTEGRATION_RESULT.md` section 4); desk page-turn missing (U4) | Claude code | S | P3 |
| AG-9 | Low, Standard, High graphics variants (02 Priority E) | NOT STARTED | None; `ceilingParticles` flag exists | Antigravity art + Claude | M | P3 |
| AG-10 | Elevator and card output stations from confirmed events only (02 Priority C) | DONE (PLAY, proxy) | Cards print on committed ids only (`LIVING_OFFICE_SHOWCASE.md` section 3) | - | - | - |

## D. Quest QA acceptance matrix (`03_QUEST_QA_ACCEPTANCE_MATRIX.md`)

Source vocabulary here is PASS / FAIL / BLOCKED / NOT_TESTED. Best evidence today is in `docs/Polish/QUEST_EVIDENCE_MATRIX.md` and `docs/Engineering/FINAL_EXPERIENCE_QUALITY_CHECKLIST.md`. Every worn row is NOT_TESTED.

| ID | Test | Best evidence | Result | Owner | Size | Pri |
|---|---|---|---|---|---|---|
| CORE-01 | Cold start offline | APK, device NT | NOT_TESTED (worn) | Human | S | P0 |
| CORE-02 | Approval lifecycle | AUTO, PLAY | NOT_TESTED (worn) | Human | S | P0 |
| CORE-03 | Card to backend task | PLAY live | PASS (PLAY) | - | - | - |
| CORE-04 | Live and demo provenance | PLAY | PASS (PLAY) | - | - | - |
| UI-01 | Text fidelity | none worn | NOT_TESTED | Human | S | P0 |
| UI-02 | Key feedback | code | NOT_TESTED | Human | S | P1 |
| UI-03 | Card movement, 10 tries | code | NOT_TESTED | Human | S | P0 |
| UI-04 | Paper and stamp, 10 tries | code | NOT_TESTED | Human | S | P0 |
| UI-05 | Viewer, receipt, archive | none | NOT_TESTED | Human | S | P1 |
| MOT-01 | Start, stop, turn video | AUTO, PLAY | NOT_TESTED (video) | Human | S | P1 |
| MOT-02 | Workstation pose | none | NOT_TESTED | Human + art | M | P1 |
| MOT-03 | Gaze and reactions | code | NOT_TESTED | Human | S | P1 |
| MOT-04 | Inter-agent handoff | PLAY (Wall) | PASS (PLAY), desk NT | Human | S | P1 |
| WALL-01 | Wall detection, 2 layouts | SYNTH, PLAY | NOT_TESTED (worn) | Human | M | P2 |
| WALL-02 | Invalid placement | SYNTH | PASS (SYNTH) | - | - | - |
| WALL-03 | Multi-story path | AUTO, PLAY | PASS (AUTO/PLAY) | - | - | - |
| WALL-04 | Elevator boarding, worn | AUTO, PLAY | NOT_TESTED (worn) | Human | M | P2 |
| WALL-05 | Elevator failure | AUTO | PASS (AUTO) | - | - | - |
| SIDE-01 | Select Agents, worn | PLAY | NOT_TESTED (worn) | Human | S | P2 |
| SIDE-02 | `/btw` isolation | AUTO, PLAY | PASS (AUTO/PLAY) | - | - | - |
| SIDE-03 | Desk visit return, worn | PLAY | NOT_TESTED (worn) | Human | S | P2 |
| PORT-01 | Portal placement, 2 layouts | SYNTH, PLAY | NOT_TESTED (worn) | Human | M | P2 |
| PORT-02 | Scaled avatar video | PLAY | NOT_TESTED | Human + art | M | P2 |
| PORT-03 | Portal cancel | AUTO, PLAY | PASS (AUTO/PLAY) | - | - | - |
| MGR-01 | Plan proposals | AUTO, PLAY live | PASS (AUTO/PLAY); model NT | Human (key) | S | P2 |
| MGR-02 | Delegation, live | PLAY live | PASS (PLAY) | - | - | - |
| XR-01 | Gesture ownership, 10 tries | code | NOT_TESTED | Human | S | P0 |
| XR-02 | Tracking loss | code, PLAY | NOT_TESTED (worn) | Human | S | P0 |
| XR-03 | Recenter and room change | AUTO, PLAY | NOT_TESTED (worn) | Human | S | P1 |
| XR-04 | Four-mode regression | PLAY | PASS (PLAY); worn NT | Human | S | P0 |
| PERF-01 | FPS per mode | none on polish | NOT_TESTED | Human | M | P0 |
| PERF-02 | 10 to 15 min stress | none | NOT_TESTED | Human | M | P0 |
| PERF-03 | Low-effect tier | none | NOT_TESTED | Human | M | P2 |
| RELEASE-01 | Flags rollback, build and startup | APK | PASS (APK); startup NT | Human | S | P0 |

## E. Repo direction docs

| ID | Source | Requirement | Status | Evidence | Owner | Size | Pri |
|---|---|---|---|---|---|---|---|
| R-01 | `docs/00_MASTER_SPEC.md` (Phase 3) sections 3 to 5 | Placement, modular construction, elevators | DONE (AUTO/PLAY) | `WALL_HABITAT_STATUS.md` M1, M2 | - | - | - |
| R-02 | Same, section 6 | `/btw` side conversation | DONE (AUTO/PLAY/pytest) | M3 | - | - | - |
| R-03 | Same, section 7 | Manager | PARTIAL | Backend and PLAY done; model blocked (S7-2) | Human | S | P2 |
| R-04 | Same, section 8 | Portal | PARTIAL | As C-07, S6-4 | Art + Claude | M | P2 |
| R-05 | Same, section 9 | Selectability, attention, comfort | PARTIAL | As A-04, B-04 | Claude + human | M | P1 |
| R-06 | Same, section 13 | Quest human-worn gates | NOT STARTED | `WALL_HABITAT_STATUS.md` M6 | Human headset | L | P2 |
| R-07 | Same, section 14 phase 6 | Hardware polish and deployment | NOT STARTED | As K-01 | Human headset | L | P3 |
| R-08 | `docs/02_CLAUDE_ENGINEERING_HANDOFF.md` section 2 | MRUK wall placement, occlusion, persistence | PARTIAL | SYNTH, PLAY; MRUK wall read never on device | Human headset | M | P2 |
| R-09 | Same, section 4 | Select Agents far-grab | PARTIAL | Simulated rays only | Human headset | M | P1 |
| R-10 | Same, section 9 | Performance and presentation polish | BLOCKED-needs-human | No device numbers | Human headset | M | P0 |
| R-11 | Same, section 11 | Headset tests | NOT STARTED | Worn checklist only | Human headset | M | P0 |
| R-12 | Same, section 12 | Releases and flags-off build | DONE (APK) | Flags-off APK built; device startup NT | Human headset | S | P0 |
| R-13 | `docs/Engineering/HANDOFF_NEXT_SESSION.md` | Known problem: full EditMode run hung the Editor | PARTIAL | Each fixture runs alone (`scripts\tests.ps1`); the 62-fixture sweep in `PARALLEL_SPRINT_TEST_RESULTS.md` passed one at a time. Cause not found | Claude code | M | P2 |
| R-14 | `HANDOFF_NEXT_SESSION.md` | Voice readout position, console key travel 5.2 mm vs 2 mm asked | PARTIAL | Not looked at on head | Human + owner decision | S | P2 |
| R-15 | `docs/Engineering/sprint/A7_ELEVATOR.md` / `ElevatorSequenceTests` | `StuckDoors` test is `[Ignore]`d | PARTIAL | `PARALLEL_SPRINT_INTEGRATION_MAP.md` row 4 | Claude code | M | P3 |
| R-16 | `docs/Engineering/sprint/A12_VOICE.md` | Agent-targeted routing adapter not applied | PARTIAL | Top-10 item 9 | Claude code | M | P2 |
| R-17 | `docs/Engineering/sprint/A6_PRESENTATION.md` | `TypeScale`, `TextFit` not applied to scene text (Living Office uses `PlanReviewLayout`) | PARTIAL | `PARALLEL_SPRINT_INTEGRATION_MAP.md` row 8 | Claude code | M | P2 |
| R-18 | `docs/Engineering/sprint/A15_ONBOARDING.md` | Onboarding not wired to UI | NOT STARTED | `PARALLEL_SPRINT_INTEGRATION_MAP.md` row 9 | Claude code | M | P3 |
| R-19 | `docs/Engineering/sprint/A13_PERFORMANCE.md` | Static perf findings: `TaskCardView` instance material and six TMP writes per Bind | PARTIAL | Not applied | Claude code | S | P2 |
| R-20 | `docs/Engineering/sprint/A11_BACKEND_RELIABILITY.md` | "Remaining / not done" section | NOT VERIFIED | Section exists; its contents were not read in this pass | Claude code | S | P3 |
| R-21 | `docs/Engineering/sprint/` | A2 and A5 sprint deliverables | UNKNOWN | No A2 or A5 doc found under `docs/Engineering/sprint/`, and the A16 integration map lists neither. Confirm whether they were in the brief | Owner | S | P3 |
| R-22 | `docs/Engineering/WALL_HABITAT_STATUS.md` | Wall flags remain OFF; Wall M6 not started | DONE (by design) | ReleaseConfig flags 0 | - | - | - |
| R-23 | `docs/Engineering/FEATURE_FLAG_MATRIX.md` section 4 | Doc drift (5 items) | PARTIAL | Items 1, 3, 4, 5 still stand. Item 2 (`wallElevatorVfx`): grep finds it referenced in `WallHabitatController.cs` and `WallModeFlow.cs`; behaviour not read | Claude code | S | P2 |

## F. Owner decisions and human-only items

| ID | Item | Status | Evidence | Owner | Pri |
|---|---|---|---|---|---|
| OD-1 | Role colours (Tester amber, Developer green) overlap state colours | OPEN, owner decision | `UI_AND_VISUAL_CONSISTENCY_AUDIT.md` row 2; V-2 | Owner | P1 |
| OD-2 | Wall art: regenerate art to the layout, or change the layout to the art | OPEN | `ART_INTEGRATION_RESULT.md` section 6 (both options written out; mirror differences up to 0.94 m) | Owner | P2 |
| OD-3 | Outer desks overhang the Floor deck (4.0 and 2.9 cm) and the Ceiling deck (6.0 and 4.9 cm) | OPEN, needs art re-fit or a mode-dependent layout | `DESK_CHOREOGRAPHY_LAYOUT.md` V2 "Footprint" | Owner + Antigravity | P1 |
| OD-4 | Merge the two key visual systems | DECIDED no (S9c row 4) | `UI_AND_VISUAL_CONSISTENCY_AUDIT.md` row 4 | Owner | P3 |
| OD-5 | Whether any polish goes into the competition build | Rule: nothing merged into `release/final-refinement` | `RELEASE_POLISH_CHANGELOG.md` line 4 | Owner | P0 |
| OD-6 | Build label `rc6-dev` still in ReleaseConfig | OPEN | `FEATURE_FLAG_MATRIX.md` section 4 item 5; `FINAL_EXPERIENCE_QUALITY_CHECKLIST.md` R-3 | Human | P0 |
| HU-1 | Worn pass of every row in section D and the 14-step Wall checklist | NOT STARTED | `NEXT_DEVICE_TEST.md`, `LIVING_OFFICE_SHOWCASE.md` section 7 | Human headset | P0 |
| HU-2 | Claude model key for the Manager | NOT STARTED | `WALL_HABITAT_STATUS.md` line 22 | Human | P2 |
| HU-3 | Quest performance capture with `scripts\quest_perf_watch.ps1` | NOT STARTED | `HANDOFF_NEXT_SESSION.md` line 56 | Human | P0 |
| HU-4 | Confirm competition APK hash `173C813F…B9B963` before submission | NOT STARTED (hash not re-run in this pass) | `FINAL_EXPERIENCE_QUALITY_CHECKLIST.md` R-1 | Human | P0 |

## G. Stale or contradictory docs (fix before the owner reads them)

1. Desk clearance verdict (FAIL) in four docs, superseded by V1 and V2 (top-10 item 3).
2. `SpatialIsolationTests` status: 1 failed (`LIVING_OFFICE_SHOWCASE.md`) vs 3 of 3 (`ART_INTEGRATION_RESULT.md`).
3. `WALL_HABITAT_RELEASE_FLAGS.md:42` says the Wall validation helper turns "the six flags" on. The code also sets `reducedMotionToggle` (`FEATURE_FLAG_MATRIX.md` section 4 item 1).
4. `LIVING_OFFICE_SHOWCASE.md` section 6 says the status and retry routes "must be registered". They are registered in `backend/app/phase3/routes.py` lines 162-181. Only the live retry path is still NT.
5. `LIVING_OFFICE_SHOWCASE.md` section 3 names `AgentVisitPortalManager` in the A9 sprint doc. That is only a test fixture name (`Tests/EditMode/Wall/AgentVisitPortalManagerTests.cs`). The runtime types are `AgentVisitFlow` and `PortalFlow`.
6. `RELEASE_POLISH_CHANGELOG.md` says 21 EditMode fixtures were never re-run. `PARALLEL_SPRINT_TEST_RESULTS.md` ran all 62 one at a time (921 passed). That row is closed; the hang (R-13) is not.
7. `FEATURE_FLAG_MATRIX.md` section 4 item 3: tags `wall-habitat-1` and `wall-habitat-2` are not reconciled.
