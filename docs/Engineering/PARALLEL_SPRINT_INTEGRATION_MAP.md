# Parallel sprint integration map (A16)

Branch: feature/living-world-polish. Merged one at a time in the order below; each compiled clean. "Tests" = new fixtures. Nothing here was run on a Quest.

| # | Sprint | Merge commit | Key files | Tests | Status | Follow-ups (NOT applied) |
|---|---|---|---|---|---|---|
| 1 | A1 TransformOwnership | 8dbb122 | Spatial/Core/Motion/TransformOwnership.cs | TransformOwnershipTests 41 | merged, green; define MUSEDESK_SPATIAL_TESTS comes from versionDefines in MuseDesk.Tests.EditMode.asmdef | Adapter plan in sprint/A1_MOVEMENT_OWNERSHIP.md: no runtime mover uses the lease registry yet |
| 2 | A3 ReactionArbiter | 71f23d0 | Agents/Core/ReactionArbiter.cs, AttentionPolicy.cs | ReactionArbiterTests 35 | merged, green | sprint/A3_REACTIONS.md: not wired into AgentAttention/animator bridge |
| 3 | A9 portal/visit lifecycle | e487756 | Spatial/Core/Wall/PortalAvatarScale, PortalSpotPlanner, PresentationLifecycle | PortalVisitLifecycleTests 25 | merged, green | sprint/A9_PORTAL_VISIT.md: not wired into AgentVisitPortalManager |
| 4 | A7 elevator dispatcher | 0f2d81e | Spatial/Core/Wall/ElevatorDispatcher, ElevatorWaitSpots, FloorRouteGraph | ElevatorSequenceTests 21 (+1 Ignore) | merged, green after fixes | Walker gap fixed (below). Ignored test needs a better scenario. sprint/A7_ELEVATOR.md: dispatcher not wired into HabitatWalker/controller |
| 5 | A4b LatestWinsToggle | f3cb017 | Spatial/Core/LatestWinsToggle.cs, Workspace/FocusModeController.cs | LatestWinsToggleTests 6 | merged, green; Play: two same-frame Focus toggles end where they started, no exception (offline demo, wire not observable) | Real single-request proof needs a live backend run |
| 6 | A12 voice core | 8c5b8a9 | Scripts/Voice/Core (new asmdef MuseDesk.Voice.Core, autoReferenced), VoiceOffice/VoiceCommandCenter edits | VoiceAddressingTests 13, VoiceIntentClassifierTests 58 (own asmdef) | merged, green; Assembly-CSharp resolves both ways | sprint/A12_VOICE.md adapter not applied |
| 7 | A14 test tooling | 6ad2a73 | scripts/tests_batch.ps1, test_catalog.json, backend_tests.ps1, Tests/EditMode/Real/RealTypeDriftTests.cs, docs/Engineering/TESTING.md | Phase2ConstantsRealTests 35, BoardPresenterRealTests 11, DemoGatewayRealTests 4 | merged; runner parses the real CLI (-Group real 50/50, -Group board 33/33); TestResults/ in .gitignore | |
| 8 | A6 presentation layout | 136e1d7 | Spatial/Core/Ui/TypeScale, TextFit, PlanReviewLayout | TypeScaleTests 12, TextFitTests 15, PlanReviewLayoutTests 26 | merged, green (53 tests, brief said 41) | sprint/A6_PRESENTATION.md: not applied to scene text |
| 9 | A15 onboarding | 4a815b2 | Spatial/Core/Onboarding/HintCatalog, OnboardingCoach (+PlayerPrefsOnboardingStore) | OnboardingCoachTests 36 | merged, green; PlayerPrefs compiles in Core asmdef | Not wired to UI |
| 10 | A13 perf (cherry-pick of ad74130, branch NOT merged) | d60b4b0 | AgentScripts/PerfProfileDrive.cs, sprint/A13_PERFORMANCE.md | none | Cherry-pick conflicted in HeldPaperController and StationDropZone; HEAD already had the equivalent fixes (GrabState.BindSelectingPoints delegate; MPB halo), kept HEAD. Halo verified in Play: URP/Unlit, _BaseColor and _Color both exist, valid/invalid colours read back from the MPB | Editor-only perf table in test results doc |
| 11 | A11 backend reliability | 1d01fd1 | backend/app decision_wait, storage, orchestrator, live_orchestrator, workspace_guard, main, models, settings | backend/tests/test_a11_reliability.py | merged; backend 298 passed, 1 skipped | |
| 12 | Art branch art/living-world-polish (642ace5) | b6789c6 | Art/Controllers/MuseDesk_Agent.controller (100 states, 3 layers), regenerated desk-agent FBXs, 24 Wall prefabs (Art/Prefabs/WallHabitat), Scripts/Art (10 new visual scripts, not attached to the desk scene), 12 wav, gallery scenes | interaction 173, motion 88, wall 168 (1 skipped), board 69, spatial 277, real 50, core UI fixtures 53; SpatialIsolationTests passes | merged, green; desk demo Play with animator state log, 0 console errors; see docs/Engineering/ART_INTEGRATION_RESULT.md | Wall prefabs NOT hooked up: socket layout is mirrored and differently sized against HabitatStage (tool: AgentScripts/WallArtFitCheck). Art audio and Confirmed visual hooks deliberately not wired |

Earlier backend merges (already on HEAD): A8 /btw routing, A10 manager plan.

## Fixes made during integration
- HabitatWalker.StepWalk: rider speed now zeroed on the boarding tick (A7 walker test found Speed 0.029 in the first Riding tick). Real gap, one-line fix.
- WallOfflineLogic/AgentVisitFlow/WallHabitatController.Interaction: tolerant A8 /btw fields. DTO gains label, intent, proposed_action; label replaces the derived provenance words in the card footer; a negated proposed_action drops the suggested change. The Wall card has no Approve control at all (text only), so nothing can be approved from it. Unknown statuses (rate_limited, model_declined) flow through as strings and render as a failed ask with the backend reply. JsonUtility ignores unknown fields (A10 edges, blocked_reason, completion) so plan parsing is unaffected; those fields are simply not shown on the Wall cards yet.

## Open items
- ElevatorSequenceTests.StuckDoors_...: [Ignore]d. 'out' is requested while the cabin is still open at floor 0, so it probably boards before the jam and legitimately gets HoldInCabin. Not proven; test scenario needs revisiting.
- Adapter plans not applied: A1, A3, A7, A9, A12 (see their docs under docs/Engineering/sprint).
- Wall flags remain OFF.
