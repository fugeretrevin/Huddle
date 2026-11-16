# MuseDesk RC8 — Final Art, Rig, Motion & VFX Issues Log

**Audit Branch:** `art/rc8-final-support` (branched from `4d976fa` / `feature/rc8-feedback-1`, parent `rc8` `911465e`)  
**Scope:** Strictly Art, Rig, Animation, Material, Prefab, VFX, Validation Gallery, and Art Documentation. Zero edits made to `MainMRScene.unity`, `Scripts/**`, `backend/`, or `ProjectSettings/`.  
**Verification Basis:** Unity Editor batchmode inspection + Blender source rebuild (`Editor, not headset` — no live Quest 3 capture is claimed here).

---

## Summary Table

| ID | Severity | Component / File | Finding | Status |
| :--- | :---: | :--- | :--- | :--- |
| **ART-P0-01** | **P0 (Scene)** | `MainMRScene.unity` (`HeldPaperController.agentHoldPoint` -> `PaperHoldPoint` `{fileID: 203000318}`) | `PaperHoldPoint` sits at `localPosition: {x: 0.055, y: 0.125, z: 0.13}` under `Agent_Researcher`, shifting `DecisionDocument` `+5.5 cm` off-center from Researcher's outstretched hands during `Offer_Document*` and leaving only **`0.0016 m` (`1.6 mm`)** gap to `Developer_Static_CharcoalVisor` (PC tower). | **Open for Engineering / Scene Builder** (`MainMRScene.unity` untouched per scope constraint) |
| **ART-P1-01** | **P1 (Fixed)** | `Assets/MuseDesk/Art/Models/WallHabitat/Agent_Manager.fbx.meta` + 9 animated Wall/Spatial `.fbx.meta` files | Empty `clipAnimations: []` caused `metaLoops=0, assetLoops=0` in Unity (e.g. `Manager_Lookout_Idle`, `Manager_Review_Plan`, `ElevatorDoors_Idle_Closed`, `ElevatorWinch_Spin`, `Cabin_Idle_Closed`, `Cabin_Travel_Bob`, `Pod_Deployed_Idle`, `Pod_Docked_Idle`, `Portal_Active_Idle` stopped after one cycle). | **Fixed on `art/rc8-final-support`** (serialized `clipAnimations` & `loopTime` flags via `SaveAndReimport()`; verified `assetLoops` matches) |
| **ART-P1-02** | **P1 (Fixed)** | `ArtSource/Scripts/generate_characters.py` + 4 character `.blend`/`.fbx` exports | Phase scan (`t=0, 0.25, 0.50, 0.75`) caught boot sole dip below `y=0` in `Walk_Start (-0.0146 m)`, `Walk_Loop (-0.0085 m)`, `Walk_Stop (-0.0141 m)`, `Walk_Carry_Paper (-0.0108 m)`, `Turn_Left (-0.0132 m)`, `Turn_Right (-0.0132 m)`, and `Carry_Package (-0.0042 m)`. | **Fixed on `art/rc8-final-support`** (raised `Hips` stride/turn curves cleanly; `02c_Res_Exit2_WalkCarry` moved from `-0.0104 m` to `+0.0015 m`) |
| **ART-P2-01** | **P2 (VFX/Code)** | `Scripts/Wall/WallHabitatElevatorVisualController.cs` vs `ReleaseConfig.asset` | `ReleaseConfig.asset` (`rc8-living-office`) enables `wallElevatorVfx=True`, `ceilingParticles=True`, `ceilingPortalCone=True`, `reducedMotion=False`. Runtime `arrivalParticles` gating by `wallElevatorVfx` remains open in `Scripts/Wall/`. | **Documented for Engineering** (`Scripts/**` untouched per scope constraint) |
| **ART-P2-02** | **P2 (Fixed)** | `docs/Art/ANIMATION_MANIFEST.md` | Section 1 heading/intro & Section 4 table still referenced `65` clips while repo ships `77` shared clips documented in `ULTIMATE_ANIMATION_MANIFEST.md`. | **Fixed on `art/rc8-final-support`** |

---

## Detailed Defect Notes & Fixes

### 1. [Open — Engineering Scene Binding] `ART-P0-01`: `PaperHoldPoint` `+5.5 cm` Lateral Offset in `MainMRScene.unity`
- **Observed Evidence (`Editor, not headset`):**
  - Visually confirmed in [desk_03_res_offer_eye.png](../review_screenshots/rc8_validation/desk_v2/desk_03_res_offer_eye.png) and [desk_02_res_exit_walk_plan.png](../review_screenshots/rc8_validation/desk_v2/desk_02_res_exit_walk_plan.png).
  - Measured in `MainMRScene.unity`: `HeldPaperController.agentHoldPoint` references `{fileID: 203000318}` (`PaperHoldPoint`, child of `Agent_Researcher` `{fileID: 913268389}`) with `localPosition: {x: 0.055, y: 0.125, z: 0.13}`.
  - Skinned bounds measurement (`03a_Res_OfferPresent`, `03b_Res_OfferHold`): `HeldPaper` `boundsXZ=[-0.076..-0.004, 0.215..0.247]`, `minGap=0.0016 m` (`1.6 mm`) against Developer's PC tower (`Developer_Static_CharcoalVisor`). Visually, the card floats to Researcher's right (viewer's left) rather than centered between Researcher's outstretched hands.
- **Recommended Engineering Fix (Guarded Scene Builder / `MainMRScene.unity`):**
  - Either re-center `PaperHoldPoint` to `{x: 0, y: 0.105, z: 0.085}` or bind `HeldPaperController.agentHoldPoint` to the character rig's canonical `DocumentOffer` / `Document_Attach` socket (`Agent_Researcher` already exposes both at `X = 0` with unit `lossyScale`).

### 2. [Fixed — Art Metas] `ART-P1-01`: Missing `clipAnimations` & `loopTime` Flags on `Agent_Manager.fbx` + 9 Wall/Spatial FBXs
- **Root Cause:** Although `Assets/MuseDesk/Editor/ModelBoundsFixer.cs` lists all Wall/Spatial/Manager looping clips in `LoopingClips`, assigning `importer.clipAnimations` inside `OnPreprocessModel()` during an implicit import did not serialize `clipAnimations` into `.fbx.meta` without an explicit `SaveAndReimport()`. As a result, `Agent_Manager.fbx.meta` and the 9 Wall/Spatial animated `.fbx.meta` files had `clipAnimations: []` (`metaLoops=0, assetLoops=0`), causing Unity to import every clip with `isLooping=false`.
- **Fix Applied & Verified:** Serialized `clipAnimations` from `defaultClipAnimations` with the canonical `ModelBoundsFixer` looping flags and called `SaveAndReimport()` on:
  - `Assets/MuseDesk/Art/Models/WallHabitat/Agent_Manager.fbx.meta`: `0 -> 77` clips, `0 -> 34` `loopTime: 1` flags (`assetLoops=34`).
  - `WallHabitat_ElevatorSpine_3Tier.fbx.meta`: `8` clips, `2` loops (`assetLoops=2`).
  - `WallHabitat_ElevatorSpine_2Tier.fbx.meta`: `6` clips, `2` loops (`assetLoops=2`).
  - `WallHabitat_ElevatorCar.fbx.meta`: `5` clips, `2` loops (`assetLoops=2`).
  - `WallHabitat_ElevatorCar_Compact.fbx.meta`: `5` clips, `2` loops (`assetLoops=2`).
  - `WallHabitat_TaskPrinter.fbx.meta`: `4` clips, `2` loops (`assetLoops=2`).
  - `WallHabitat_DeskVisitPerch.fbx.meta`: `4` clips, `2` loops (`assetLoops=2`).
  - `WallHabitat_LargeAgentPortal.fbx.meta`: `4` clips, `2` loops (`assetLoops=2`).
  - `SuspendedOfficePod.fbx.meta`: `13` clips, `4` loops (`assetLoops=4`).
  - `CeilingMount.fbx.meta`: `4` clips, `2` loops (`assetLoops=2`).
- **Regression Check Note:** `AgentClipLoopFlagTests.ManagerLoopingClipsKeepLoopTime_IfPresent` previously passed vacuously because `clipAnimations` was empty; after this fix it actively verifies `Agent_Manager` looping clips.

### 3. [Fixed — Source Animation] `ART-P1-02`: Locomotion & Turn Boot-Sole Grounding
- **Root Cause:** Stride contact and bent-knee turn keyframes in `ArtSource/Scripts/generate_characters.py` dropped `Hips` to `loc.y = -0.005..+0.001`, causing flexed thigh/shin/foot chains (`Foot_R` / `Foot_L` pitch) to dip `-4.2 mm` to `-14.6 mm` below `y = 0`.
- **Fix Applied:** Raised `Hips` vertical curves symmetrically in `generate_characters.py` (`Walk_Start`, `Walk_Loop`, `Walk_Stop`, `Walk_Carry_Paper`, `Turn_Left`, `Turn_Right`, `Carry_Package`), fixed a malformed tuple keyframe typo at `Walk_Carry_Paper` frame 19 `"Foot_R": {"rot": (0,0,0)}`, and regenerated all 4 character `.blend`/`.fbx` assets via `generate_characters.py` and `generate_wall_manager_agent.py`.
- **Before vs. After (`minFootY` across phases `0.0, 0.25, 0.50, 0.75`):**
  - `Walk_Start`: `-0.0146 m -> 0.0000 m`
  - `Walk_Loop`: `-0.0085 m -> +0.0005 m`
  - `Walk_Stop`: `-0.0141 m -> -0.0021 m`
  - `Walk_Carry_Paper`: `-0.0108 m -> +0.0012 m` (`02c_Res_Exit2_WalkCarry` at `t=0.50`: `-0.0104 m -> +0.0015 m`)
  - `Turn_Left`: `-0.0132 m -> 0.0000 m`
  - `Turn_Right`: `-0.0132 m -> 0.0000 m`
  - `Carry_Package`: `-0.0042 m -> +0.0018 m`

---

## Verified Clean (No Art Change Needed)

- **Role Trim Passthrough Contrast (`#A9B8F0` / `#D2A6E8` / `#F0A9C4` / `#4B4F9C`):** All four mascots and role cards maintain clear visual separation and silhouette identity against both dark (`#1C2026`) and pale warm (`#E8E5DC`) passthrough backdrops (`Editor, not headset`).
- **Paper, DetailsSheet, Stamps & Receipt:** `DecisionDocument` renders clean cream matte with `DetailsSheet` offset `localPos=(0.0000, -0.2080, 0.0040)` behind the card (zero z-fighting). `StampApprove` / `StampReject` sit cleanly at `(±0.0360, 0.0140, 0.4150)` (`contactR=0.050`, `dwell=0.12`), and `ApprovalStampController` waits for `decision.resolved` (`AGENTS.md` #2 preserved).
- **Floor & Ceiling Refit (`rc8`):** Stations sit fully on both refitted decks (`0 cm` overhang verified in [floor_deployed_overview.png](../review_screenshots/rc8_validation/ceiling_floor/floor_deployed_overview.png) and [ceiling_deployed_eye.png](../review_screenshots/rc8_validation/ceiling_floor/ceiling_deployed_eye.png)).
- **Wall Habitat Full & Compact + Large Portal + Desk-Visit Perch:** Sockets remain intact (`Dev SeatPose y=-0.4000` Full / `-0.2600` Compact), elevator cabin aligns cleanly at intermediate travel and Manager doors open, `LargePortal` (`mountLocalY=0.0230`) and `DeskVisitPerch` (`mountY=0.0000`) ground agent boots cleanly.
