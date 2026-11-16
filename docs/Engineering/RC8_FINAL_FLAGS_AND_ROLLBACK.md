# Final flags and rollback (rc9-feedback-1)

Owner decisions, 2026-11-16: keep every feature ON, including `largeAgentPortal` (the keep-out guard is wired and
PLAY-verified); hands-free voice needs a name first; the Wall Manager is the dedicated model; the office does not move at
start-up (only the panels re-seat).

## Release flags (`Assets/MuseDesk/Resources/ReleaseConfig.asset`)

| Flag | Value | Note |
|---|---|---|
| floorMode, ceilingMode, ceilingPullGesture | ON | Floor and Ceiling in the unified dock |
| ceilingParticles, ceilingPortalCone, placementScanGrids | ON | |
| voice | ON | mic starts **muted**; the mic key unmutes; hands-free sentences must start with a name (WakeWord) |
| wallHabitat, wallManager, agentSelect, agentBtw | ON | |
| largeAgentPortal | ON | owner decision; PortalComfortGuard closes it if the spot stops being clear |
| wallElevatorVfx | ON | |
| reducedMotion | OFF (toggle available) | |
| onboardingHints | ON | |
| buildLabel | `rc9-feedback-1` | |

## Code switches (rollback without a rebuild of the logic)

| Switch | Where | Off = |
|---|---|---|
| `SoftwareFactoryVisualController.ShowFactory` (false) | static | true shows the factory again (also run `RemoveFactoryV1.Revert`) |
| `WallHabitatController.useSelectGrabV2` (true) | scene | the previous select grab |
| `WallHabitatController.usePresentationLifecycle` (true) | scene | the previous visit/portal path (also skips the keep-out guard) |
| `WallHabitatController.managerSource` (Agent_Manager prefab) | scene | empty = Developer clone with lavender cap (`WallManagerModelV1.Revert`) |
| `NearUserKit` start-up reach | scene | see `UnifiedModeDockV1.Revert` |

## Scene builders applied (all idempotent, each has Revert)

In order: `RemoveFactoryV1`, `PaperKeysTrimV1`, `MicToggleV1`, `AgentSelectGrabV1`, `UnifiedModeDockV1`,
`PaperHoldCenterV1`, `WallManagerModelV1`. Run with `scripts\run_guarded.ps1 -File AgentScripts\<Name>.cs -Entry <Name>.Revert`
in reverse order to undo.

## APK rollback order

1. `Builds/MuseDesk_rc9_feedback1.apk` (this candidate), SHA-256 `97AA4094DAC81EE3AD6BFD9E737BA2A927DE47914492739FBDF525B709E2D2A9`
2. `Builds/MuseDesk_rc8.apk`, SHA-256 `788D4A04...E1428` (verified)
3. `Builds/MuseDesk_rc7.apk`
4. `Builds/MuseDesk_rc6_2.apk`

`BuildApkTo` refuses to write `MuseDesk.apk` or any path containing rc6, rc7 or rc8.
