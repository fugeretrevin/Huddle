# Parallel sprint EditMode test results

Date: 2026-11-15. Branch feature/living-world-polish, HEAD aa887ed.

**Scope: EditMode tests run in the Unity Editor only. This is not a device test.** Compile was clean (compilationFailed false, 0 console errors). Each fixture was run alone with `scripts\tests.ps1 <Fixture>`; the whole suite was never run at once.

## Result

Runnable fixtures: 62. Tests passed: 921. Failed: 0. Three fixtures (Phase2ReducerRuntimeTests, DemoAgentGatewayRuntimeTests, SessionReducerRuntimeTests) are dormant behind the `MUSEDESK_TESTS_RUNTIME_REF` define and were skipped, as in previous sweeps. Counts are by name filter, which is a substring match: MotionTests (32) includes HabitatMotionTests (22), so the total below counts those 22 twice. Unique tests = 899.

This completes the 21 fixtures the earlier sweep never reached (PlacementGuideLogicTests onward). No fixture failed, so nothing needed a re-run or diagnosis.

| Fixture | Passed | Failed | Duration | Notes |
|---|---|---|---|---|
| PlacementGuideLogicTests | 13 | 0 | 2 s |  |
| CeilingPodAudioCueTests | 5 | 0 | 2 s |  |
| CeilingPodAudioSynthTests | 4 | 0 | 2 s |  |
| CeilingVfxMapTests | 11 | 0 | 1 s |  |
| CeilingScanRevealTests | 8 | 0 | 1 s |  |
| FloorAimMathTests | 5 | 0 | 1 s |  |
| FloorAimDirectorTests | 9 | 0 | 1 s |  |
| FloorAimSessionTests | 9 | 0 | 1 s |  |
| FloorPlaceHereTests | 13 | 0 | 1 s |  |
| HandOwnershipTests | 10 | 0 | 1 s |  |
| CeilingPullOwnershipTests | 6 | 0 | 1 s |  |
| FloorAimOwnershipTests | 4 | 0 | 1 s |  |
| PlaceHereKeyTests | 2 | 0 | 1 s |  |
| CommandLedgerTests | 3 | 0 | 1 s |  |
| GrabTrackingWatchTests | 4 | 0 | 1 s |  |
| AgentWalkCornersTests | 6 | 0 | 1 s |  |
| CeilingPullTests | 14 | 0 | 1 s |  |
| PanelHeightTests | 8 | 0 | 1 s |  |
| Phase2WireContractSpecTests | 6 | 0 | 1 s |  |
| BoardPresenterLogicSpecTests | 22 | 0 | 1 s |  |
| DemoGatewayDecisionSpecTests | 5 | 0 | 1 s |  |
| AgentStatusSentenceTests | 8 | 0 | 1 s |  |
| GazeDwellTimerTests | 6 | 0 | 1 s |  |
| WaveDetectorTests | 6 | 0 | 1 s |  |
| SmallLogicTests | 8 | 0 | 1 s |  |
| CeilingPolishAttentionTests | 7 | 0 | 1 s |  |
| PanelReachTests | 9 | 0 | 1 s |  |
| PanelOverlapTests | 6 | 0 | 1 s |  |
| PanelCodecTests | 13 | 0 | 1 s |  |
| ModeConsoleLogicTests | 9 | 0 | 1 s |  |
| MotionTests | 32 | 0 | 4 s | Name filter is a substring, so this also ran HabitatMotionTests (32 total incl. its 22) |
| BubbleProvenanceTests | 3 | 0 | 1 s |  |
| Phase2ReducerRuntimeTests |  |  |  s | Dormant: gated by MUSEDESK_TESTS_RUNTIME_REF, not compiled; skipped |
| DemoAgentGatewayRuntimeTests |  |  |  s | Dormant: gated by MUSEDESK_TESTS_RUNTIME_REF, not compiled; skipped |
| SessionReducerRuntimeTests |  |  |  s | Dormant: gated by MUSEDESK_TESTS_RUNTIME_REF, not compiled; skipped |
| DeskPolishLogicTests | 22 | 0 | 1 s |  |
| WalkClearanceTests | 12 | 0 | 1 s |  |
| ReducedMotionPreferenceTests | 7 | 0 | 1 s |  |
| PresenceTests | 8 | 0 | 1 s |  |
| StampPoofTests | 12 | 0 | 2 s |  |
| VoiceAnswersTests | 5 | 0 | 2 s |  |
| SpatialReachAttentionTests | 15 | 0 | 1 s |  |
| SpatialReachHoldTests | 5 | 0 | 1 s |  |
| SpatialReachDeliveryTests | 12 | 0 | 1 s |  |
| SpatialReachVoicePhraseTests | 3 | 0 | 1 s |  |
| AgentWalkPathTests | 5 | 0 | 1 s |  |
| VoiceIntentParserTests | 341 | 0 | 2 s |  |
| FloorLowClutterTests | 5 | 0 | 1 s |  |
| CeilingScreenTests | 3 | 0 | 1 s |  |
| CeilingDockRouteTests | 3 | 0 | 1 s |  |
| CeilingSeatFrameTests | 3 | 0 | 1 s |  |
| CeilingShallowDescentTests | 4 | 0 | 2 s |  |
| SpatialFloorEvaluatorTests | 7 | 0 | 2 s |  |
| SpatialCeilingEvaluatorTests | 5 | 0 | 1 s |  |
| SpatialCeilingSweptPathTests | 19 | 0 | 1 s |  |
| SpatialTransitionMachineTests | 5 | 0 | 2 s |  |
| SpatialDirectorTests | 15 | 0 | 2 s |  |
| SpatialCeilingPodTests | 8 | 0 | 2 s |  |
| SpatialPersistenceTests | 5 | 0 | 2 s |  |
| SpatialSweptMovesTests | 17 | 0 | 1 s |  |
| SpatialIsolationTests | 3 | 0 | 1 s |  |
| HabitatMotionTests | 22 | 0 | 5 s |  |
| WallPlacementTests | 26 | 0 | 1 s |  |
| AgentVisitPortalManagerTests | 19 | 0 | 1 s |  |
| WallOfflineLogicTests | 31 | 0 | 1 s |  |

Totals: passed 921 (unique 899), failed 0.
