# Wall Habitat Fit & Verification Report

- **Branch:** `art/wall-habitat-fit` (from `feature/living-world-polish` @ `8c2ca85`)
- **Contract Source of Truth:** `Assets/MuseDesk/Scripts/Spatial/Core/Wall/HabitatLayout.cs` (`HabitatLayoutBuilder.Build(WallLayoutKind.Full | Compact)`) — unmodified.
- **Touched Scope:** Wall Habitat Blender generators (`ArtSource/Scripts/generate_wall_habitat_core.py`, `generate_wall_habitat_mechanical.py`), Wall Habitat `.blend` / `.fbx` / `.prefab` / AnimatorController / VFX bindings (`Assets/MuseDesk/Editor/ArtPipeline/WallHabitatArtSetup.cs`, `LivingWorldVfxAndGallerySetup.cs`), preview scene & Unity Editor validation renders, and `docs/Art/WALL_HABITAT_INVENTORY.json`. Zero edits to `MainMRScene.unity`, `HabitatLayout.cs`, `HabitatStage.cs`, `AgentScripts/**`, `Scripts/Wall/**`, `Scripts/Spatial/**`, `Scripts/Agents/**`, `backend/`, `ProjectSettings/`, or `Agent_*.fbx.meta`.

## Layout Alignment Summary

- **Empirical Blender-to-Unity Export Mapping:** With `_rotate_roots_for_unity_plus_z()` followed by `export_scene.fbx(axis_forward='-Z', axis_up='Y')`, every socket maps `Blender (bx, by, bz) -> Unity (ux = +bx, uy = +bz, uz = +by)`.
- **Full 3-Tier (`WallHabitat_Complete_3Tier`):** Envelope `1.050 x 0.900 x 0.260 m` (`art bounds size (1.050, 0.917, 0.264)` including the perched beacon plinth), Elevator Spine & Car on `+X` (`x = +0.440`), Researcher (`-0.352`), Developer (`-0.065`), and Tester (`+0.222`) cubicles along the lower floor (`y = -0.400`), Mid Planning Nook (`+0.163, -0.110`), Manager Lookout (`+0.215, +0.150`), TaskPrinter (`-0.230, +0.200`), and front-facing `+Z` elevator doors and walkway railings clearing every nav corridor & entryway.
- **Compact 2-Tier (`WallHabitat_Complete_2Tier`):** Envelope `0.740 x 0.620 x 0.240 m` (`art bounds size (0.740, 0.620, 0.244)`), dedicated `WallHabitat_ManagerLookout_Compact`, `WallHabitat_ElevatorSpine_2Tier`, `WallHabitat_ElevatorCar_Compact`, and scaled `WallHabitat_TaskPrinter` fitting cleanly on the two-tier footprint with Elevator on `+X` (`x = +0.295`).

## Verbatim Socket Fit Reports (`WallHabitatArtSetup.RunWallArtFitCheckFromEditor`)

### Full Layout (`WallHabitat_Complete_3Tier` vs `WallLayoutKind.Full`)

```text
WallHabitat_Complete_3Tier vs layout Full: layout size (1.050, 0.900, 0.260), art bounds size (1.050, 0.917, 0.264) (min (-0.525, -0.450, 0.000) max (0.525, 0.467, 0.264)), art WallContact at prefab-root (0.000, 0.000, 0.000)
socket: layout | art (relative to art WallContact) | delta | verdict (tolerance 0.010 m)
  WallContact <- WallContact: layout (0.000, 0.000, 0.000) | art (0.000, 0.000, 0.000) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  ElevatorCarSocket <- ElevatorCarSocket: layout (0.440, -0.400, 0.092) | art (0.440, -0.400, 0.093) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  ElevatorStop_Lower <- ElevatorStop_Lower: layout (0.440, -0.400, 0.210) | art (0.440, -0.400, 0.210) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  ElevatorStop_Mid <- ElevatorStop_Mid: layout (0.440, -0.110, 0.210) | art (0.440, -0.110, 0.210) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  ElevatorStop_Manager <- ElevatorStop_Manager: layout (0.440, 0.150, 0.210) | art (0.440, 0.150, 0.210) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  AttentionBeaconSocket <- AttentionBeaconSocket: layout (-0.250, 0.410, 0.130) | art (-0.250, 0.410, 0.130) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  CourierDispatchSocket <- CourierDispatchSocket: layout (-0.455, -0.400, 0.210) | art (-0.455, -0.400, 0.210) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  PortalDepartureSocket <- PortalDepartureSocket: layout (0.000, -0.400, 0.280) | art (0.000, -0.400, 0.280) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  ResearchCubicleSocket <- ResearchCubicleSocket: layout (-0.352, -0.400, 0.092) | art (-0.352, -0.400, 0.093) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.AgentHomePose <- Researcher.AgentHomePose: layout (-0.403, -0.400, 0.068) | art (-0.403, -0.400, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.WorkPose <- Researcher.WorkPose: layout (-0.403, -0.400, 0.068) | art (-0.403, -0.400, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.SeatPose <- Researcher.SeatPose: layout (-0.403, -0.400, 0.068) | art (-0.403, -0.400, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.EntryPose <- Researcher.EntryPose: layout (-0.273, -0.400, 0.143) | art (-0.273, -0.400, 0.143) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.HandoffPose <- Researcher.HandoffPose: layout (-0.273, -0.400, 0.210) | art (-0.273, -0.400, 0.210) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.ElevatorQueuePose <- Researcher.ElevatorQueuePose: layout (0.323, -0.400, 0.210) | art (0.323, -0.400, 0.210) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.DocumentAttach <- Researcher.DocumentAttach: layout (-0.403, -0.307, 0.098) | art (-0.403, -0.307, 0.097) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.HeadLookTarget <- Researcher.HeadLookTarget: layout (-0.403, -0.247, 0.068) | art (-0.403, -0.247, 0.067) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  DeveloperCubicleSocket <- DeveloperCubicleSocket: layout (-0.065, -0.400, 0.092) | art (-0.065, -0.400, 0.093) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.AgentHomePose <- Developer.AgentHomePose: layout (-0.117, -0.400, 0.068) | art (-0.117, -0.400, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.WorkPose <- Developer.WorkPose: layout (-0.117, -0.400, 0.068) | art (-0.117, -0.400, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.SeatPose <- Developer.SeatPose: layout (-0.117, -0.400, 0.068) | art (-0.117, -0.400, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.EntryPose <- Developer.EntryPose: layout (0.014, -0.400, 0.143) | art (0.014, -0.400, 0.143) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.HandoffPose <- Developer.HandoffPose: layout (0.014, -0.400, 0.210) | art (0.014, -0.400, 0.210) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.ElevatorQueuePose <- Developer.ElevatorQueuePose: layout (0.323, -0.400, 0.210) | art (0.323, -0.400, 0.210) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.DocumentAttach <- Developer.DocumentAttach: layout (-0.117, -0.307, 0.098) | art (-0.117, -0.307, 0.098) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.HeadLookTarget <- Developer.HeadLookTarget: layout (-0.117, -0.247, 0.068) | art (-0.117, -0.247, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  TesterCubicleSocket <- TesterCubicleSocket: layout (0.222, -0.400, 0.092) | art (0.222, -0.400, 0.093) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.AgentHomePose <- Tester.AgentHomePose: layout (0.170, -0.400, 0.068) | art (0.170, -0.400, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.WorkPose <- Tester.WorkPose: layout (0.170, -0.400, 0.068) | art (0.170, -0.400, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.SeatPose <- Tester.SeatPose: layout (0.170, -0.400, 0.068) | art (0.170, -0.400, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.EntryPose <- Tester.EntryPose: layout (0.300, -0.400, 0.143) | art (0.300, -0.400, 0.143) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.HandoffPose <- Tester.HandoffPose: layout (0.300, -0.400, 0.210) | art (0.300, -0.400, 0.210) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.ElevatorQueuePose <- Tester.ElevatorQueuePose: layout (0.323, -0.400, 0.210) | art (0.323, -0.400, 0.210) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.DocumentAttach <- Tester.DocumentAttach: layout (0.170, -0.307, 0.098) | art (0.170, -0.307, 0.098) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.HeadLookTarget <- Tester.HeadLookTarget: layout (0.170, -0.247, 0.068) | art (0.170, -0.247, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  SharedMeetingSocket <- SharedMeetingSocket: layout (0.163, -0.110, 0.092) | art (0.163, -0.110, 0.093) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  ManagerOfficeSocket <- ManagerOfficeSocket: layout (0.215, 0.150, 0.092) | art (0.215, 0.150, 0.093) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  TaskPrinterSocket <- TaskPrinterSocket: layout (-0.230, 0.200, 0.092) | art (-0.230, 0.200, 0.092) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.AgentHomePose <- Manager.AgentHomePose: layout (0.160, 0.150, 0.068) | art (0.160, 0.150, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.WorkPose <- Manager.WorkPose: layout (0.160, 0.150, 0.068) | art (0.160, 0.150, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.SeatPose <- Manager.SeatPose: layout (0.160, 0.150, 0.068) | art (0.160, 0.150, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.EntryPose <- Manager.EntryPose: layout (0.297, 0.150, 0.143) | art (0.297, 0.150, 0.143) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.HandoffPose <- Manager.HandoffPose: layout (0.297, 0.150, 0.210) | art (0.297, 0.150, 0.210) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.ElevatorQueuePose <- Manager.ElevatorQueuePose: layout (0.323, 0.150, 0.210) | art (0.323, 0.150, 0.210) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.DocumentAttach <- Manager.DocumentAttach: layout (0.160, 0.244, 0.098) | art (0.160, 0.244, 0.098) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.HeadLookTarget <- Manager.HeadLookTarget: layout (0.160, 0.303, 0.068) | art (0.160, 0.303, 0.068) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
summary: fit 46, no fit 0, missing 0
after the best single translation (0.000, 0.000, 0.000), the worst remaining socket error is 0.000 m
```

### Compact Layout (`WallHabitat_Complete_2Tier` vs `WallLayoutKind.Compact`)

```text
WallHabitat_Complete_2Tier vs layout Compact: layout size (0.740, 0.620, 0.240), art bounds size (0.740, 0.620, 0.244) (min (-0.370, -0.310, 0.000) max (0.370, 0.310, 0.244)), art WallContact at prefab-root (0.000, 0.000, 0.000)
socket: layout | art (relative to art WallContact) | delta | verdict (tolerance 0.010 m)
  WallContact <- WallContact: layout (0.000, 0.000, 0.000) | art (0.000, 0.000, 0.000) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  ElevatorCarSocket <- ElevatorCarSocket: layout (0.295, -0.260, 0.083) | art (0.295, -0.260, 0.083) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  ElevatorStop_Lower <- ElevatorStop_Lower: layout (0.295, -0.260, 0.190) | art (0.295, -0.260, 0.190) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  ElevatorStop_Manager <- ElevatorStop_Manager: layout (0.295, 0.000, 0.190) | art (0.295, 0.000, 0.190) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  AttentionBeaconSocket <- AttentionBeaconSocket: layout (-0.150, 0.240, 0.120) | art (-0.150, 0.240, 0.120) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  CourierDispatchSocket <- CourierDispatchSocket: layout (-0.300, -0.260, 0.190) | art (-0.300, -0.260, 0.190) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  PortalDepartureSocket <- PortalDepartureSocket: layout (0.000, -0.260, 0.260) | art (0.000, -0.260, 0.260) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  ResearchCubicleSocket <- ResearchCubicleSocket: layout (-0.245, -0.260, 0.083) | art (-0.245, -0.260, 0.083) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.AgentHomePose <- Researcher.AgentHomePose: layout (-0.280, -0.260, 0.060) | art (-0.280, -0.260, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.WorkPose <- Researcher.WorkPose: layout (-0.280, -0.260, 0.060) | art (-0.280, -0.260, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.SeatPose <- Researcher.SeatPose: layout (-0.280, -0.260, 0.060) | art (-0.280, -0.260, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.EntryPose <- Researcher.EntryPose: layout (-0.198, -0.260, 0.125) | art (-0.198, -0.260, 0.125) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.HandoffPose <- Researcher.HandoffPose: layout (-0.198, -0.260, 0.190) | art (-0.198, -0.260, 0.190) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.ElevatorQueuePose <- Researcher.ElevatorQueuePose: layout (0.192, -0.260, 0.190) | art (0.192, -0.260, 0.190) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.DocumentAttach <- Researcher.DocumentAttach: layout (-0.280, -0.178, 0.090) | art (-0.280, -0.178, 0.090) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Researcher.HeadLookTarget <- Researcher.HeadLookTarget: layout (-0.280, -0.125, 0.060) | art (-0.280, -0.125, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  DeveloperCubicleSocket <- DeveloperCubicleSocket: layout (-0.055, -0.260, 0.083) | art (-0.055, -0.260, 0.083) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.AgentHomePose <- Developer.AgentHomePose: layout (-0.090, -0.260, 0.060) | art (-0.090, -0.260, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.WorkPose <- Developer.WorkPose: layout (-0.090, -0.260, 0.060) | art (-0.090, -0.260, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.SeatPose <- Developer.SeatPose: layout (-0.090, -0.260, 0.060) | art (-0.090, -0.260, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.EntryPose <- Developer.EntryPose: layout (-0.008, -0.260, 0.125) | art (-0.008, -0.260, 0.125) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.HandoffPose <- Developer.HandoffPose: layout (-0.008, -0.260, 0.190) | art (-0.008, -0.260, 0.190) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.ElevatorQueuePose <- Developer.ElevatorQueuePose: layout (0.192, -0.260, 0.190) | art (0.192, -0.260, 0.190) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.DocumentAttach <- Developer.DocumentAttach: layout (-0.090, -0.178, 0.090) | art (-0.090, -0.178, 0.090) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Developer.HeadLookTarget <- Developer.HeadLookTarget: layout (-0.090, -0.125, 0.060) | art (-0.090, -0.125, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  TesterCubicleSocket <- TesterCubicleSocket: layout (0.135, -0.260, 0.083) | art (0.135, -0.260, 0.083) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.AgentHomePose <- Tester.AgentHomePose: layout (0.100, -0.260, 0.060) | art (0.100, -0.260, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.WorkPose <- Tester.WorkPose: layout (0.100, -0.260, 0.060) | art (0.100, -0.260, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.SeatPose <- Tester.SeatPose: layout (0.100, -0.260, 0.060) | art (0.100, -0.260, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.EntryPose <- Tester.EntryPose: layout (0.182, -0.260, 0.125) | art (0.182, -0.260, 0.125) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.HandoffPose <- Tester.HandoffPose: layout (0.182, -0.260, 0.190) | art (0.182, -0.260, 0.190) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.ElevatorQueuePose <- Tester.ElevatorQueuePose: layout (0.192, -0.260, 0.190) | art (0.192, -0.260, 0.190) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.DocumentAttach <- Tester.DocumentAttach: layout (0.100, -0.178, 0.090) | art (0.100, -0.178, 0.090) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Tester.HeadLookTarget <- Tester.HeadLookTarget: layout (0.100, -0.125, 0.060) | art (0.100, -0.125, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  ManagerOfficeSocket <- ManagerOfficeSocket: layout (0.080, 0.000, 0.083) | art (0.080, 0.000, 0.083) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  TaskPrinterSocket <- TaskPrinterSocket: layout (-0.130, 0.050, 0.083) | art (-0.130, 0.050, 0.082) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.AgentHomePose <- Manager.AgentHomePose: layout (0.013, 0.000, 0.060) | art (0.013, 0.000, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.WorkPose <- Manager.WorkPose: layout (0.013, 0.000, 0.060) | art (0.013, 0.000, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.SeatPose <- Manager.SeatPose: layout (0.013, 0.000, 0.060) | art (0.013, 0.000, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.EntryPose <- Manager.EntryPose: layout (0.150, 0.000, 0.125) | art (0.150, 0.000, 0.125) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.HandoffPose <- Manager.HandoffPose: layout (0.150, 0.000, 0.190) | art (0.150, 0.000, 0.190) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.ElevatorQueuePose <- Manager.ElevatorQueuePose: layout (0.192, 0.000, 0.190) | art (0.192, 0.000, 0.190) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.DocumentAttach <- Manager.DocumentAttach: layout (0.013, 0.083, 0.090) | art (0.013, 0.083, 0.090) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
  Manager.HeadLookTarget <- Manager.HeadLookTarget: layout (0.013, 0.135, 0.060) | art (0.013, 0.135, 0.060) | delta (0.000, 0.000, 0.000) (0.000 m) | FIT
summary: fit 44, no fit 0, missing 0
after the best single translation (0.000, 0.000, 0.000), the worst remaining socket error is 0.000 m
```

## Asset Inventory & Test Validation

- **Asset Audit (`docs/Art/WALL_HABITAT_INVENTORY.json`):** `26` WallHabitat prefabs audited; `totalMissingScripts: 0`, `totalBrokenSockets: 0`, `totalMissingMaterials: 0`, `missingRequired3TierSockets: 0`, `missingRequired2TierSockets: 0`.
- **Unity EditMode Tests (`unity test --mode EditMode`):** `1292` total, `1291` passed, `0` failed, `1` skipped (`MuseDesk.Tests.EditMode.Wall.ElevatorSequenceTests.StuckDoors_TakeTheCabinOutOfService_WaitersWalkHome_RiderHolds_TheRestAreBlockedOrCancelled`, pre-existing `[Ignore]`). All `AgentClipLoopFlagTests.*` and `WallPlacementTests.*` passed.

## Editor Captures (Unity Editor, NOT Headset)

> **Note:** These screenshots are offline Unity Editor camera captures from `Assets/MuseDesk/Scenes/WallHabitatPreviewScene.unity`, **not** live Meta Quest 3 headset renders.

- Front Full 3-Tier (`WallHabitat_Complete_3Tier`): `Screenshots/WallHabitat/Unity/01_WallHabitat_3Tier_Front.png`
- Three-Quarter Full 3-Tier (`WallHabitat_Complete_3Tier`): `Screenshots/WallHabitat/Unity/02_WallHabitat_3Tier_ThreeQuarter.png`
- Front Compact 2-Tier (`WallHabitat_Complete_2Tier`): `Screenshots/WallHabitat/Unity/03_WallHabitat_2Tier_Compact.png`
