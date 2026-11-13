# MuseDesk — Spatial Workspace Expansion Asset Manifest & Integration Guide

**Branch:** `art-final-release-qa` (isolated worktree at `.worktrees/final-art-qa`, branched from `release/final-refinement` @ `beb01a8`)  
**Unity Version Tested:** `6000.6.8f1`  
**Inventory Generated:** `2026-11-13T22:39:16Z` (`docs/Art/SPATIAL_WORKSPACE_INVENTORY.json`)  
**Validation Summary:** `totalPrefabs = 16`, `totalMissingScripts = 0`, `totalBrokenSockets = 0`, `totalMissingMaterials = 0`, `allSocketsUnitScale = true`

---

## 1. Overview & Architecture

This additive art expansion lets MuseDesk's three miniature agents (`Agent_Researcher`, `Agent_Developer`, `Agent_Tester`), workstations, and software factory inhabit either a **Floor Office Platform** or a **Retractable Ceiling Workspace Pod**, while delivering documents and focused 1-on-1 conversations directly to the seated user without requiring the user to crouch or sacrifice physical desk space.

### Guarantees & Quest 3 Budgets
- **Additive Only:** Zero modifications to `MainMRScene.unity`, `backend/`, schemas, or existing desktop-mode prefabs/characters/animations.
- **Canonical Orientation:** All 12 spatial `.fbx` assets export with `+Z` forward (toward seated user), `+Y` up, bottom-center pivot `(0, 0, 0)` (or ceiling-contact top center `CeilingAnchor = (0, 0, 0)`), and exact real-world meter scale.
- **Socket Scale Safety:** `SpatialScaleUtility.MountPreservingWorldScale` and `SpatialWorkspaceArtSetup` normalize all non-bone sockets directly under prefab roots with `lossyScale == (1, 1, 1)` so animated armature bones (`Armature` `100x` / `-90 X`) never warp child sockets.
- **No Runtime Trajectory Baking:** Ceiling world vertical travel (`DockedPose` -> `InspectionPose`) and floor courier Bezier trajectories (`DispatchToUserPose` / `ReturnToStation`) are driven purely at runtime from room/user poses; Blender `.fbx` clips contain strictly local mechanical motion.
- **Material & Lighting Efficiency:** Reuses MuseDesk's shared URP palette materials (`Mat_CreamCeramic`, `Mat_WarmWood`, `Mat_FeltSlate`, `Mat_Brass`, `Mat_BrushedMetal`, `Mat_CharcoalVisor`, `Mat_Status*`, `Mat_Researcher_*`, `Mat_Developer_*`, `Mat_Tester_*`, `Mat_Spatial_SlateIcon`, `Mat_Spatial_Ghost`) modulated via `MaterialPropertyBlock` (no runtime dynamic lights and no instanced `material` leaks).

---

## 2. Source & Generated Asset Inventory

### 2.1 Blender Procedural Generators (`ArtSource/Scripts/`)
| Script | Generates |
|---|---|
| `spatial_common.py` | Shared URP-matched Blender Principled materials, beveled/rounded geometry primitives, NLA clip pushing, `.blend` + `.fbx` export (`+Z` forward via `_rotate_roots_for_unity_plus_z`), Eevee previews |
| `generate_spatial_floor_and_preview.py` | `FloorOfficePlatform`, `FloorOfficePlatform_Compact`, `PlacementGhostPreview_Floor`, `PlacementGhostPreview_Ceiling` |
| `generate_spatial_ceiling_pod.py` | `CeilingMount` (0.44 m 4-leaf Iris Docking Bay, 4 clips), `SuspendedOfficePod` (13 clips), `CeilingInspectionShelf` |
| `generate_spatial_beacon_and_mailbox.py` | `WorkspaceAttentionBeacon` (7 clips + 4 accessibility symbolic icon meshes), `SpatialMailbox` |
| `generate_spatial_courier_and_perch.py` | `FloorCourierStation` (4 clips), `DocumentCourier` (6 clips), `AgentConversationPerch` (5 clips) |
| `export_spatial_assets.py` | Headless batch entry point regenerating all 12 `.blend` files (`ArtSource/Blender/Spatial/`), 12 `.fbx` files (`Assets/MuseDesk/Art/Models/Spatial/`), and Eevee previews (`Screenshots/SpatialWorkspace/Blender/`) |

### 2.2 Unity Prefabs (16 Reusable Prefabs — `Assets/MuseDesk/Art/Prefabs/Spatial/`)

Exact post-import counts measured by `SpatialWorkspaceArtSetup.BuildAll` (`docs/Art/SPATIAL_WORKSPACE_INVENTORY.json`):

| Prefab | Renderers (Skinned) | Colliders | Tris | Verts | Bounds (`W x H x D m`) | Sockets | Controller Component |
|---|---|---|---|---|---|---|---|
| `FloorOfficePlatform.prefab` | 3 (0) | 2 | 11,536 | 16,176 | `0.720 x 0.049 x 0.486` | 11 | `FloorOfficePlatformController` |
| `FloorOfficePlatform_Compact.prefab` | 3 (0) | 2 | 11,568 | 16,248 | `0.580 x 0.047 x 0.406` | 11 | `FloorOfficePlatformController` |
| `CeilingMount.prefab` | 1 (1) | 1 | 4,580 | 6,876 | `0.440 x 0.118 x 0.440` | 6 | `Animator` (`MuseDesk_CeilingMount`) |
| `CeilingInspectionShelf.prefab` | 1 (0) | 1 | 1,436 | 2,112 | `0.240 x 0.021 x 0.125` | 2 | — |
| `SuspendedOfficePod.prefab` | 2 (1) | 1 | 9,300 | 13,440 | `0.657 x 0.277 x 0.393` | 12 | `Animator` (`MuseDesk_CeilingPod`) |
| `WorkspaceAttentionBeacon.prefab` | 10 (10) | 1 | 2,304 | 2,970 | `0.064 x 0.103 x 0.052` | 2 | `SpatialBeaconVisualController` |
| `SpatialMailbox.prefab` | 2 (0) | 1 | 2,416 | 3,726 | `0.140 x 0.076 x 0.095` | 5 | `SpatialMailboxController` |
| `FloorCourierStation.prefab` | 2 (2) | 1 | 1,724 | 2,581 | `0.139 x 0.031 x 0.138` | 3 | `Animator` (`MuseDesk_FloorCourierStation`) |
| `DocumentCourier.prefab` | 3 (3) | 1 | 4,676 | 6,191 | `0.142 x 0.074 x 0.090` | 3 | `Animator` (`MuseDesk_DocumentCourier`) |
| `AgentConversationPerch.prefab` | 3 (3) | 1 | 2,900 | 3,877 | `0.218 x 0.076 x 0.164` | 3 | `AgentConversationPerchController` |
| `PlacementGhostPreview_Floor.prefab` | 2 (0) | 0 | 1,656 | 2,592 | `0.720 x 0.024 x 0.580` | 2 | `PlacementGhostPreviewController` |
| `PlacementGhostPreview_Ceiling.prefab` | 2 (0) | 0 | 1,064 | 1,472 | `0.620 x 0.143 x 0.435` | 1 | `PlacementGhostPreviewController` |
| `Spatial_FloorCourierSystem.prefab` | 5 (5) | 2 | 6,400 | 8,772 | `0.146 x 0.099 x 0.138` | 6 | `DocumentCourierController` |
| `Spatial_CeilingWorkspaceAssembly.prefab` | 21 (12) | 3 | 16,536 | 23,654 | `0.700 x 1.000 x 0.600` | 18 | `CeilingWorkspacePodController`, `CeilingDeploymentVFXController` |
| `Spatial_FloorOffice_Complete.prefab` | 82 (19) | 10 | 64,382 | 61,042 | `0.720 x 0.185 x 0.486` | 17 | `FloorOfficePlatformController` (+ child `DocumentCourierController`, `SpatialBeaconVisualController`, `SpatialMailboxController`) |
| `Spatial_CeilingPod_Complete.prefab` | 85 (16) | 8 | 60,678 | 56,778 | `0.700 x 1.000 x 0.600` | 18 | `CeilingWorkspacePodController`, `CeilingDeploymentVFXController` (+ child `SpatialBeaconVisualController`, `SpatialMailboxController`) |

---

## 3. Animator Controllers & 39 Local Spatial Animation Clips

Controllers live in `Assets/MuseDesk/Art/Controllers/Spatial/`:

1. **`MuseDesk_CeilingPod.controller` (13 clips — matches all 13 `PodState` phases)**
   - `Pod_Docked_Idle` (looping)
   - `Pod_Attention_Ack`
   - `Pod_Unlock`
   - `Pod_Begin_Descent`
   - `Pod_Descending` (looping)
   - `Pod_Stabilize`
   - `Pod_Shelf_Open`
   - `Pod_Deployed_Idle` (looping)
   - `Pod_Shelf_Close`
   - `Pod_Begin_Ascent`
   - `Pod_Returning_Dock` (looping)
   - `Pod_Dock_Lock`
   - `Pod_Mechanical_Settle`

2. **`MuseDesk_CeilingMount.controller` (4 clips — drives `Bone_SpoolDrum`, `Bone_CollarLatch_L/R`, `Bone_SuspCollar`, and 4-leaf `Bone_Iris_F/B/L/R` shutter blades)**
   - `Mount_Docked_Idle` (looping, iris blades closed into anti-clip docking cradle)
   - `Mount_Unlock` (iris shutters open radially, latches release)
   - `Mount_Spool_Spin` (looping with iris shutters held open)
   - `Mount_Lock` (iris shutters close back into seated cradle, latches engage)

3. **`MuseDesk_AttentionBeacon.controller` (7 clips)**
   - `Beacon_Idle` (looping)
   - `Beacon_Working` (looping)
   - `Beacon_Completed`
   - `Beacon_Suggestion` (looping)
   - `Beacon_Blocking_Decision` (looping)
   - `Beacon_Error` (looping)
   - `Beacon_Incoming`

4. **`MuseDesk_FloorCourierStation.controller` (4 clips)**
   - `Station_Idle` (looping)
   - `Station_Load_Lift`
   - `Station_Dispatch_Release`
   - `Station_Dock_Receive`

5. **`MuseDesk_DocumentCourier.controller` (6 clips)**
   - `Courier_Docked_Idle` (looping)
   - `Courier_Load_Document`
   - `Courier_Fly_Loop` (looping)
   - `Courier_Arrive_Present`
   - `Courier_Waiting_Idle` (looping)
   - `Courier_Return_Dock`

6. **`MuseDesk_ConversationPerch.controller` (5 clips)**
   - `Perch_Folded_Idle` (looping)
   - `Perch_Unfold_Arrive`
   - `Perch_Listening_Idle` (looping)
   - `Perch_Speaking_Pulse`
   - `Perch_Fold_Return`

---

## 4. Required Sockets & Transform Anchors

All sockets are verified `allSocketsUnitScale == true` (`lossyScale == (1, 1, 1)`):

- **Floor Office (`FloorOfficePlatform` / `FloorOfficePlatform_Compact`)**:
  `WorkspaceRoot`, `FloorContact`, `ResearchStationSocket`, `DeveloperStationSocket`, `TesterStationSocket`, `FactorySocket`, `DispatchStationSocket`, `StatusBeaconSocket`, `MailboxSocket`, `NavigationBounds` (trigger `BoxCollider`), `InteractionPresentationSocket`, plus `Floor_NameplateTMP` (`"MUSEDESK LAB"`).
- **Retractable Ceiling Workspace (`CeilingMount`, `SuspendedOfficePod`, `CeilingInspectionShelf`)**:
  - Mount: `CeilingAnchor`, `CeilingMountRoot`, `SuspensionTop`, `SuspensionBottom`, `DockedPose`, `InspectionPose`
  - Pod: `PodRoot`, `DockingConnector`, `ResearchStationSocket`, `DeveloperStationSocket`, `TesterStationSocket`, `FactorySocket`, `InfoDisplaySocket` (`Pod_TaskDisplayTMP`), `AttentionBeaconSocket`, `MailboxSocket`, `InspectionShelf`, `AgentInteractionSocket`, `DocumentPresentationSocket`
- **Attention Beacon (`WorkspaceAttentionBeacon`)**:
  `BeaconRoot`, `IconSocket` (hosts `IconQuestion`, `IconDecisionDoc`, `IconWarningTri`, `IconCheckBadge`), `LabelSocket`
- **Mailbox (`SpatialMailbox`)**:
  `MailboxRoot`, `CompletedDocSocket`, `SuggestionDocSocket`, `PendingDecisionSocket`, `DocumentPickupPoint`, plus `Mailbox_CountTMP`
- **Courier System (`FloorCourierStation`, `DocumentCourier`)**:
  `CourierDockPoint`, `AgentLoadPoint`, `DocumentReceiverPoint`, `CourierRoot`, `DocumentAttachSocket`, `PresentationColliderCenter`
- **Agent Conversation Perch (`AgentConversationPerch`)**:
  `PerchRoot`, `AgentStandSocket`, `DocumentPresentationSocket`, `ConversationIndicator`

---

## 5. Runtime Controllers (`Assets/MuseDesk/Scripts/Art/`)

1. **`SpatialScaleUtility`** (`SpatialScaleUtility.cs`)
   - `MountPreservingWorldScale(Transform child, Transform socket, Vector3 desiredWorldScale, bool resetLocalPose = true)`
   - `MountKeepingCurrentWorldScale(Transform child, Transform socket, bool resetLocalPose = true)`
2. **`FloorOfficePlatformController`** (`FloorOfficePlatformController.cs`)
   - `SetValidatedUniformScale(float requestedScale)` (clamped to `[minUniformScale, maxUniformScale]`, keeping `FloorContact` anchored)
   - `SetPoseOnFloor(Vector3 floorPosition, Quaternion yawRotation, float uniformScale = 1f)`
   - `SetStatusStripColor(Color baseColor, float emissionIntensity = 0.25f)` via `MaterialPropertyBlock`
3. **`CeilingWorkspacePodController`** (`CeilingWorkspacePodController.cs`)
   - Implements full `PodState` sequence: `DockedIdle` -> `Acknowledging` -> `Unlocking` -> `BeginningDescent` -> `Descending` (interpolates `podRoot.position` between `DockedPose` and `InspectionPose` and scales `dynamicTetherRod`) -> `Stabilizing` -> `OpeningShelf` -> `DeployedIdle`; reverses via `ClosingShelf` -> `BeginningAscent` -> `ReturningToDock` -> `DockLocking` -> `Settling` -> `DockedIdle`.
   - **Skipped-Event Recovery:** Animation event hooks (`OnAnimUnlockComplete`, `OnAnimBeginDescentDone`, `OnAnimStabilizeComplete`, `OnAnimShelfOpened`, `OnAnimShelfClosed`, `OnAnimBeginAscentDone`, `OnAnimDockLocked`, `OnAnimSettleComplete`) are optional accelerators — every transitional phase carries a deterministic timeout ceiling and smoothly completes or switches direction cleanly if `Deploy()`, `Retract()`, or `SnapToPoseImmediate(bool deployed)` interrupts mid-flight.
   - Room configuration: `ConfigureRoomPoses(Vector3 dockedWorldPos, Vector3 inspectionWorldPos, Quaternion yawRotation)`.
4. **`CeilingDeploymentVFXController`** (`CeilingDeploymentVFXController.cs`)
  - Automatically subscribes to `CeilingWorkspacePodController.StateChanged` (zero extra gameplay plumbing needed) and drives the articulated ceiling ascension/descension VFX rig on `Spatial_CeilingWorkspaceAssembly.prefab` / `Spatial_CeilingPod_Complete.prefab` using Quest-3 Single-Pass Instanced URP shaders (`MuseDesk/Spatial/PortalCone` in `Assets/MuseDesk/Art/Shaders/SpatialPortalCone.shader`, `Mat_Spatial_PortalCone.mat`, reversible `Mat_Spatial_PortalCone_Lite.mat`, `Mat_Spatial_SoftParticle.mat`, `Tex_SoftGlowDot.png`, `VFX_PortalConeMesh.asset`, `VFX_HoloRingMesh.asset`) and `MaterialPropertyBlock`:
    - `VFX_CeilingProjectionHalo` & `VFX_CeilingRippleRing` flush against the ceiling bay (`TriggerCeilingRipple` pulses cyan on unlock, warm gold on lock).
    - `VFX_CeilingPortalCone` Fresnel light column spanning `SuspensionTop` -> `DockingConnector` with directional scanline flow (downward Cyan on descent, upward Warm Gold on ascent).
    - `VFX_TetherFlowParticles`, `VFX_CeilingApertureParticles`, `VFX_AirCushionRing`, and `VFX_ArrivalBurstParticles`.
    - Reversible Quest 3 budget presets via `SetQualityPreset(CeilingVfxQualityPreset)` (`Full`, `LowCostNoParticles`, `MinimalIrisHalo`) — mirrored in `CeilingVfxBridge.qualityPreset`.
    - `ResolveReferences()` and `ApplySnapshotImmediate(PodState state, float progress01, float ripplePhase01)` for deterministic EditMode & scene validation.
5. **`SpatialBeaconVisualController`** (`SpatialBeaconVisualController.cs`)
   - `SetState(BeaconState state, bool immediate = false)` supporting `WorkingNormally`, `CompletedWork`, `OptionalSuggestion`, `BlockingDecision`, `Error`, `Disconnected`, `Inactive`, pairing color-restrained peripheral glows (`MaterialPropertyBlock`) with 3D accessibility icon meshes and mechanical flag rotation.
   - `PulseIncoming()` triggers `Beacon_Incoming` arrival squash/pulse.
6. **`DocumentCourierController`** (`DocumentCourierController.cs`)
   - Drives 7-step lifecycle (`DockedIdle`, `LoadingDocument`, `Departing`, `TravelingToUser`, `Presenting`, `WaitingForInteraction`, `ReturningToStation`) along a runtime-evaluated quadratic Bezier arc to any `Pose` via `DispatchToUserPose(Pose targetPresentationPose, Action onArrived = null)` and `ReturnToStation(Action onReturned = null)`, plus `AttachDocument` / `DetachDocument` / `SnapDockedImmediate`.
7. **`AgentConversationPerchController`** (`AgentConversationPerchController.cs`)
   - Controls `PerchState` (`Folded`, `Arriving`, `Listening`, `Speaking`, `Returning`) with `AttachAgent`, `AttachDocument`, `TriggerUnfoldArrive`, `SetListening`, `TriggerSpeakingPulse`, `TriggerFoldReturn`, and `RecoverToDeployedIdle`.
8. **`SpatialMailboxController`** (`SpatialMailboxController.cs`)
   - `UpdateCounts(int completed, int suggestions, int pendingDecisions)` updates `Mailbox_CountTMP` and toggles per-slot visual document indicators cleanly; `MountDocumentVisual(Transform docTransform, int slotType, Vector3 desiredWorldScale)` attaches document prefabs into `CompletedDocSocket` / `SuggestionDocSocket` / `PendingDecisionSocket`.
9. **`PlacementGhostPreviewController`** (`PlacementGhostPreviewController.cs`)
   - `SetValid(bool valid, string labelOverride = null)` and `TriggerConfirmPulse()` swap `Mat_Spatial_Ghost` colors (`_BaseColor` / `_EmissionColor`) via `MaterialPropertyBlock` cleanly for floor and ceiling MR placement previews.

---

## 6. Validation Scene & Test Verification

- **Editor Pipeline & Validation Scene:** `MuseDesk.Editor.ArtPipeline.SpatialWorkspaceArtSetup.BuildAll` (`Assets/MuseDesk/Editor/ArtPipeline/SpatialWorkspaceArtSetup.cs`) rebuilds all 16 prefabs, builds `Assets/MuseDesk/Scenes/SpatialWorkspaceValidationScene.unity`, writes `docs/Art/SPATIAL_WORKSPACE_INVENTORY.json`, and captures 7 Unity URP RenderTexture views into `Screenshots/SpatialWorkspace/Unity/`:
  - `01_Validation_Overview.png`
  - `02_FloorOffice_Complete.png`
  - `03_CeilingPod_Docked_vs_Deployed.png`
  - `04_AttentionBeacon_7States.png`
  - `05_Courier_And_Perch_Showcase.png`
  - `06_Mailbox_And_GhostPreviews.png`
  - `07_CeilingVFX_Full_vs_LowCost.png`
- **Unity EditMode Tests (`unity test --mode EditMode`):** `677 / 677 passed`, `0 failed`, `0 skipped`.

---

## 7. Quick Integration Recipe for Unity Runtime Engineers (Fable / Gameplay Layer)

1. **Placing Floor Mode:** Spawn `Spatial_FloorOffice_Complete.prefab` (or `PlacementGhostPreview_Floor.prefab` during raycast preview) and call `FloorOfficePlatformController.SetPoseOnFloor(floorHitPoint, facingUserYaw, validatedScale)`.
2. **Placing Ceiling Mode:** Raycast ceiling anchor point and seated-side inspection pose, spawn `Spatial_CeilingPod_Complete.prefab`, call `CeilingWorkspacePodController.ConfigureRoomPoses(dockedWorldPos, inspectionWorldPos, facingUserYaw)`, then drive `Deploy()` / `Retract()`.
3. **Connecting Events to Spatial Notification & Delivery:**
   - Bind task/decision status changes to child `SpatialBeaconVisualController.SetState(...)` + `SpatialMailboxController.UpdateCounts(...)`.
   - Call child `DocumentCourierController.DispatchToUserPose(comfortPose, onArrived)` to fly documents from `FloorCourierStation` to the user without bending down; call `ReturnToStation()` once reviewed/approved.
   - Spawn `AgentConversationPerch.prefab` beside the user for focused 1-on-1 agent check-ins via `AttachAgent(...)`, `SetListening(true)`, and `TriggerFoldReturn()`.

