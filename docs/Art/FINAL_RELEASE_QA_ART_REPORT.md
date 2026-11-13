# MuseDesk — Final Visual QA, Spatial Effects Optimization & Release Asset Report

**Branch:** `art-final-release-qa` (worktree `.worktrees/final-art-qa`, branched from `release/final-refinement` @ `beb01a8`)  
**Date:** `2026-11-13`  
**Unity Editor Verified:** `6000.6.8f1` (`SpatialWorkspaceArtSetup.BuildAll` + EditMode test suite)  
**Production Scene Safety:** `Assets/MuseDesk/Scenes/MainMRScene.unity` in main repository and `.worktrees/final-art-qa`, `backend/`, `Scripts/Spatial/Core/`, and agent orchestration are untouched.  
**Hardware Evidence Honesty:** No Quest 3 headset wear session (`WORN`) or on-device Quest GPU/FPS capture exists for the spatial/ceiling VFX branch (`FINAL_SPATIAL_PERFORMANCE.md` / `PROJECT_OVERVIEW_FOR_REVIEW_5.md`). All verifications below are Blender Eevee + Unity URP Editor (`AUTO` / validation renders / EditMode unit tests); default visual quality is preserved while reversible optimization variants are ready for instant device toggling.

---

## 1. Modified Assets & Root Causes

| Asset / Script | Confirmed Defect (Before) | Minimum Useful Correction (After) | Prefab / File Path |
|---|---|---|---|
| `SuspendedOfficePod` (`generate_spatial_ceiling_pod.py` + `SuspendedOfficePod.fbx` + `SpatialWorkspaceArtSetup.cs`) | 1) In `sp_10_ceiling_docked_room.png` and before `03_CeilingPod_Docked_vs_Deployed.png`, `Bone_Wing_L` & `Bone_Wing_R` had X-oriented bones rotated around local Y, twisting/drooping wings downward below the docked pod hull; `WingHinge` rotated with the flap.<br>2) `MainHull` (`0.60 m`) left minimal deck clearance around outer station pads. | 1) Aligned `Bone_Wing_L/R` along `+Y` hinge pin axis (`roll=0`), bound `WingHinge` to `Bone_PodRoot`, and updated `pose_docked_closed` to `-56°` (L) / `+56°` (R) so wings fold cleanly **upward** along the hull rim.<br>2) Widened ceramic/wood deck (`0.630 x 0.390 m` hull, `0.586 x 0.346 m` wood deck, collider `0.60 x 0.040 x 0.35 m`) while narrowing wing flaps (`0.058 -> 0.046 m`), shrinking overall pod bounds (`0.707 -> 0.657 m` X) inside the validated `director.footprint`. | `Assets/MuseDesk/Art/Models/Spatial/SuspendedOfficePod.fbx`<br>`Assets/MuseDesk/Art/Prefabs/Spatial/SuspendedOfficePod.prefab`<br>`Assets/MuseDesk/Art/Prefabs/Spatial/Spatial_CeilingWorkspaceAssembly.prefab`<br>`Assets/MuseDesk/Art/Prefabs/Spatial/Spatial_CeilingPod_Complete.prefab` |
| `CeilingMount` + `CeilingPodArt.cs` | 1) During `Stabilizing`, `Engaged`, and `PreparingToRetract`, `CeilingPodArt.OnPodChanged()` switched `mountAnimator` back to `MountIdle` (`Mount_Docked_Idle`), closing `Bone_Iris_F/B/L/R` straight through `PodMastSection0` while the pod stayed lowered.<br>2) `Mount_Unlock` took the full `0.75 s` while `CeilingPodController.prepareSeconds` transitions after `0.40 s`, and `CeilingPodArt` queued timers (`0.5 / 0.85 / 0.7`) slightly mismatched actual clip lengths (`15/24=0.625 s`, `20/24=0.833 s`, `16/24=0.667 s`). | 1) Kept `_mountWant = MountSpool` (iris shutters open around the mast) through `Stabilizing`, `Engaged`, and `PreparingToRetract`, closing cleanly via `MountLock -> MountIdle` only on dock.<br>2) Updated `Mount_Unlock` keyframe easing to reach open clearance by frame `11` (`0.46 s`), and matched `beginSeconds = 0.625f`, `shelfSeconds = 0.833f`, `settleSeconds = 0.667f`. | `Assets/MuseDesk/Art/Models/Spatial/CeilingMount.fbx`<br>`Assets/MuseDesk/Art/Prefabs/Spatial/CeilingMount.prefab`<br>`Assets/MuseDesk/Scripts/Spatial/CeilingPodArt.cs` |
| `FloorOfficePlatform` & `_Compact` (`generate_spatial_floor_and_preview.py`) | In `sp_04_floor_office_close.png` and `sp_06_floor_beacon.png`, an empty raised `8 mm` charcoal cylinder (`BeaconPedestal`) sat next to `FloorCourierStation` because `MainMRScene` floats `AttentionBeacon` above the front edge; front perimeter status strip was thin (`3.5 mm`). | Replaced the raised charcoal puck with a flush `1.6 mm` brass/slate `BeaconInlayRing` + `BeaconInlayCore` (`StatusBeaconSocket` lowered to `deck_z + 0.002 m`) and subtly broadened `StatusLightStrip_Front` (`0.56 -> 0.62 * width`, height `3.5 -> 4.5 mm`) while preserving all socket names and `BrandPlaqueFace` coordinates (`Floor_NameplateTMP`). | `Assets/MuseDesk/Art/Models/Spatial/FloorOfficePlatform.fbx`<br>`Assets/MuseDesk/Art/Models/Spatial/FloorOfficePlatform_Compact.fbx`<br>`Assets/MuseDesk/Art/Prefabs/Spatial/FloorOfficePlatform.prefab`<br>`Assets/MuseDesk/Art/Prefabs/Spatial/FloorOfficePlatform_Compact.prefab`<br>`Assets/MuseDesk/Art/Prefabs/Spatial/Spatial_FloorOffice_Complete.prefab` |
| `DocumentCourier` (`generate_spatial_courier_and_perch.py`) | In before `05_Courier_And_Perch_Showcase.png`, `DocumentAttachSocket` (`(0, 0.008, 0.048)`, `-22°`) placed the carried paper low enough for its bottom edge to intersect `DocCradle` in `Courier_Waiting_Idle` (`Bone_DocTray -14°`). | Raised `DocumentAttachSocket` to `(0.0, 0.006, 0.056)` (`rot_deg=(-28, 0, 0)`) and widened `pose_clamps_open` (`±38° -> ±46°`), clearing both `DocCushion` and brass clamps cleanly. | `Assets/MuseDesk/Art/Models/Spatial/DocumentCourier.fbx`<br>`Assets/MuseDesk/Art/Prefabs/Spatial/DocumentCourier.prefab`<br>`Assets/MuseDesk/Art/Prefabs/Spatial/Spatial_FloorCourierSystem.prefab` |
| Ceiling VFX & Scan Grid (`SpatialPortalCone.shader`, `Mat_Spatial_PortalCone_Lite.mat`, `CeilingDeploymentVFXController.cs`, `CeilingVfxBridge.cs`, `CeilingScanReveal.cs`, `FloorScanReveal.cs`) | No Quest GPU profiling capture exists yet, so blindly stripping effects would be premature — yet the existing shader used `Cull Off` on the 3D cone (2x cone overdraw) plus per-fragment `normalize()`, `pow()`, and `sin()` bands, and `CeilingVfxBridge` could only toggle particles off while leaving all 4 additive renderers running. | Added **reversible, default-preserving optimization tiers**:<br>- `SpatialPortalCone.shader`: `_Cull` property (`0 = Off` default on `Mat_Spatial_PortalCone.mat`, `2 = Back` on `Mat_Spatial_PortalCone_Lite.mat`) + `#pragma shader_feature_local _LITE_MODE` (skips Fresnel `pow`, `normalize(viewDir)`, and `sin()` scanbands).<br>- `CeilingVfxQualityPreset` (`Full` [default], `LowCostNoParticles` [0 particles, disables `VFX_CeilingRippleRing` & `VFX_AirCushionRing`, swaps cone to `Mat_Spatial_PortalCone_Lite`], `MinimalIrisHalo` [keeps only flat `VFX_CeilingProjectionHalo` + iris & pod motion]).<br>- `CeilingScanReveal.lowCostCompact` (`1.6 m -> 1.05 m` quad = **57% fewer fragments**) and `FloorScanReveal.lowCostCompact` (`2.4 m -> 1.55 m` quad = **58% fewer fragments**, `3.5 s -> 2.2 s`). | `Assets/MuseDesk/Art/Shaders/SpatialPortalCone.shader`<br>`Assets/MuseDesk/Art/Materials/Mat_Spatial_PortalCone.mat`<br>`Assets/MuseDesk/Art/Materials/Mat_Spatial_PortalCone_Lite.mat`<br>`Assets/MuseDesk/Scripts/Art/CeilingDeploymentVFXController.cs`<br>`Assets/MuseDesk/Scripts/Spatial/CeilingVfxBridge.cs`<br>`Assets/MuseDesk/Scripts/Spatial/CeilingScanReveal.cs`<br>`Assets/MuseDesk/Scripts/Spatial/FloorScanReveal.cs` |

**Unchanged Items Verified Not Needing Edits:**
- `WorkspaceAttentionBeacon` (`AttentionBeacon.cs` already pairs distinct 3D geometry + text, restrains animation to a single pulse per change, and auto-scales to `1.5°–2.5°`).
- Interaction props (`TaskCard`, stamps, wide buttons, nameplates): latest `xr_visual_qa_spatial_build.txt` passes desktop/card checks cleanly; no headset evidence justifies changing working interaction meshes.

---

## 2. Performance & Optimization Comparison (Static / Shader / Overdraw Analysis)

> **Important:** No Quest 3 headset profiling capture exists yet; the table below reports measured asset/draw-state/coverage differences rather than fabricated headset frame timings.

| Mode / Variant | Active Additive Renderers | Active Particle Systems | Portal Cone Culling & Shader ALU | Scan Grid Quad Area | Default State |
|---|---|---|---|---|---|
| `CeilingVfxQualityPreset.Full` | 4 (`PortalCone`, `ProjectionHalo`, `RippleRing`, `AirCushionRing`) | 3 (`Aperture` 28 max, `TetherFlow` 36 max, `ArrivalBurst` 24 max) | `Cull Off` (2-sided), `normalize` + Fresnel `pow` + `sin()` bands | Ceiling `2.56 m²` (`1.6 m`²), Floor `5.76 m²` (`2.4 m`²) | **Active by default** |
| `CeilingVfxQualityPreset.LowCostNoParticles` | 2 (`PortalCone` + `ProjectionHalo`) | 0 (emission & renderers off) | `Cull Back` (~50% cone overdraw cut) + `_LITE_MODE` (simple smoothstep, 0 trig/pow/viewDir) | Optional `lowCostCompact`: Ceiling `1.10 m²` (-57%), Floor `2.40 m²` (-58%) | One-click toggle |
| `CeilingVfxQualityPreset.MinimalIrisHalo` | 1 (`ProjectionHalo` only) | 0 | Cone disabled | Optional `lowCostCompact` | One-click toggle |

Measured prefab metrics remain validated (`docs/Art/SPATIAL_WORKSPACE_INVENTORY.json`):
- `totalPrefabs = 16`, `totalMissingScripts = 0`, `totalBrokenSockets = 0`, `totalMissingMaterials = 0`, `allSocketsUnitScale = true`.
- `SuspendedOfficePod`: `9,300 tris / 13,440 verts`, bounds `0.657 x 0.277 x 0.393 m` (reduced from `0.707 m` X while gaining deck width).
- `FloorOfficePlatform`: `11,536 tris / 16,176 verts`, bounds `0.720 x 0.049 x 0.486 m`.

---

## 3. Verification Results (Post-Final Edit)

- **Unity Asset Setup & Prefab Validation (`SpatialWorkspaceArtSetup.BuildAll`):** Exited `0`, `Built 16 prefabs, missingScripts=0, brokenSockets=0, missingMats=0`.
- **Unity Visual Captures (`Screenshots/SpatialWorkspace/Unity/`):** Captured & inspected `02_FloorOffice_Complete.png`, `03_CeilingPod_Docked_vs_Deployed.png`, `05_Courier_And_Perch_Showcase.png`, and `07_CeilingVFX_Full_vs_LowCost.png`:
  - `03`: Docked pod wings now tuck upward flush with the hull rim instead of drooping downward; deployed pods sit with wider solid deck margin.
  - `05`: Document carried on `DocumentCourier` clears `DocCushion` cleanly with no bottom clipping.
  - `07`: Verified all three VFX presets (`Full`, `LowCostNoParticles`, `MinimalIrisHalo`) render cleanly under the ceiling mount.
- **Unity EditMode Test Suite (`unity test --mode EditMode`):** `result=Passed total=677 passed=677 failed=0 skipped=0 inconclusive=0 duration=1.206s`.

---

## 4. Exact Integration Instructions for Fable / Runtime Engineering

1. **Merge or cherry-pick art commit** from branch `art-final-release-qa` (`.worktrees/final-art-qa`) into the integration branch. Because `MainMRScene.unity` was NOT edited here:
   - Run `BuildCeilingVfxV1.Apply` / `SpatialWorkspaceArtSetup.BuildAll` or let Unity reimport the updated `.fbx` prefabs (`SuspendedOfficePod`, `CeilingMount`, `FloorOfficePlatform`, `DocumentCourier`).
   - On `MainMRScene`'s `CeilingPodArt` component: updated defaults (`MountSpool` during `Stabilizing` / `Engaged` / `PreparingToRetract`, plus `beginSeconds = 0.625`, `shelfSeconds = 0.833`, `settleSeconds = 0.667`) activate automatically in code; if serialized values were overridden on scene instances, reset `beginSeconds/shelfSeconds/settleSeconds` or leave Inspector overrides as desired.
   - To enable reversible GPU savings after Quest profiling (`FINAL_SPATIAL_PERFORMANCE.md`), on scene `CeilingDeploymentVFXController` assign `portalConeFullMaterial = Mat_Spatial_PortalCone.mat` and `portalConeLiteMaterial = Mat_Spatial_PortalCone_Lite.mat` (already auto-wired on prefabs rebuilt by `SpatialWorkspaceArtSetup.BuildAll`), then switch `CeilingVfxBridge.qualityPreset` (`LowCostNoParticles` or `MinimalIrisHalo`) and optionally set `CeilingScanReveal.lowCostCompact = true` / `FloorScanReveal.lowCostCompact = true`.
2. **Remaining Hardware Checks (Cannot Be Validated Without Quest 3):**
   - Run `scripts\quest_perf_watch.ps1` / Quest 3 APK test against `QUEST_HARDWARE_VALIDATION_MATRIX.md` Gates A–E.
   - Verify whether `FloorOfficePlatform`'s `-deckY * s` offset (`BuildSpatialArtV1` line 446, pushing base rim 2.2 cm below `y=0` in floor mode) feels natural in passthrough or warrants lifting `FloorCandidate` placement height in `Scripts/Spatial/Core/`.

---

## 5. Safe Rollback Instructions

- **Runtime VFX / Scan Grid Rollback (Zero Re-export Needed):**
  - Set `CeilingVfxBridge.qualityPreset = CeilingVfxQualityPreset.Full` (or `vfxEnabled = false` / `particlesEnabled = false`).
  - Set `CeilingScanReveal.lowCostCompact = false` and `FloorScanReveal.lowCostCompact = false`.
- **Full Branch / Asset Rollback:**
  - Revert the release-QA commit (`git revert <commit-hash>`) or reset art assets to `beb01a8` (`git checkout beb01a8 -- Assets/MuseDesk/Art ArtSource Scripts/Spatial AgentScripts/ Assets/MuseDesk/Editor/ArtPipeline`) and run `MuseDesk/Art Pipeline/Build Spatial Workspace Expansion (All)` cleanly.
