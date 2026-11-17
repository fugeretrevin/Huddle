# Final flags and rollback (rc13-lively-polish)

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
| buildLabel | `rc13-lively-polish` | rc11 + Wall aim/snap, animated portal, lively visitors, restyled cards |

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
`PaperHoldCenterV1`, `WallManagerModelV1`, `DeskFirstStartV1`, `DockTidyV1`, `CopyFixV1`, `MeetKeyV1`, `DockStatusCopyV1`, `PortalArtV1`, `CardLookV1`, `PaperKeyLabelsV1`. Run with `scripts\run_guarded.ps1 -File AgentScripts\<Name>.cs -Entry <Name>.Revert`
in reverse order to undo.

## APK rollback order

1. `Builds/MuseDesk_rc13_lively_polish.apk` (this candidate), SHA-256 `3DAF3AE6CB45B4DFA24863FC51BA92E743F95A8ABBF62787359AFBA329BBF6AD`
2. `Builds/MuseDesk_rc11_polish.apk`, SHA-256 `AF34CBE53D559DA44CAD10E42B3422C18FC3480CB9C8486087D39BF83A6284CD` (rc12 was never installed)
3. `Builds/MuseDesk_rc10_overnight.apk`, SHA-256 `1B43F95AF86A2437B491437B810F9A97688DDE9393B204EA93A54117F3DD89E6`
4. `Builds/MuseDesk_rc9_feedback1.apk`, SHA-256 `97AA4094DAC81EE3AD6BFD9E737BA2A927DE47914492739FBDF525B709E2D2A9`
5. `Builds/MuseDesk_rc8.apk`, SHA-256 `788D4A04...E1428` (verified)
6. `Builds/MuseDesk_rc7.apk`
7. `Builds/MuseDesk_rc6_2.apk`

`BuildApkTo` refuses to write `MuseDesk.apk` or any path containing rc6, rc7, rc8, rc9, rc10, rc11 or rc12.
