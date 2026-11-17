# MuseDesk — Experience Polish & Wall Habitat Unification Report

> Branch: `art/wall-habitat` (branched cleanly from `e14df8e`)  
> Worktree: `C:\dev\MuseDesk\MuseDesk\.worktrees\wall-habitat`  
> Verified with Blender `5.3.0` (`blender-launcher.exe`) + Unity `6000.6.8f1` (`unity run` / `unity test`).  
> Strict boundaries preserved: zero edits to `MainMRScene.unity`, `backend/`, `Assets/MuseDesk/Scripts/Spatial/**`, `Assets/MuseDesk/Scripts/Agents/**`, LookGuide card, or `ProjectSettings/`.

---

## 1. Combined Integration Scope

This branch completes both the **Phase 3 Wall Habitat ("Workshop Treehouse")** deliverable and the **Whole-World Art & Animation Polish Pass**, while porting art-only commits from `.worktrees/art-polish` (`ac51d4d..fa19060`) **without reverting** `e14df8e` runtime/perf fixes (`BuildGuideCardPolishV1`, `BuildWalkRoutesV1`, `Mat_Spatial_PortalCone_Lite`).

---

## 2. Verified Fixes & Improvements

### A. Character Geometry & Rig Consistency (`ArtSource/Scripts/generate_characters.py`, `generate_wall_manager_agent.py`)
1. **Worktree-Relative Export Path**: Fixed hardcoded `PROJECT_ROOT` in `generate_characters.py` to resolve dynamically relative to `__file__`, preventing accidental writes into the main checkout.
2. **EarPod & Headphone Cheek Clipping Resolved**:
   - Repositioned `EarPod_L` / `EarPod_R` across the shared Pipkin Sprite body builder cleanly outside the head shell equator (`x = ±0.054 m`, `radius = 0.010 m`, `depth = 0.011 m`), verified in Blender close-up (`Screenshots/ArtPreviews/Agent_Researcher_earpod.png`) and Unity 4-mascot lineup (`Screenshots/ExperiencePolish/01_Mascots_All4_EarPod_Lineup.png`).
   - Expanded `Agent_Developer` headphones (`HeadphoneBand` major radius `0.054 m`, `HeadphoneCup` at `±0.055 m`, `HeadphoneGlowRing` at `±0.060 m`) for clean earpod clearance.
3. **`Portal_Return_Step` 180° Twist Fixed**:
   - Rotated `Hips` `(0, 180, 0)` across frames 1–28 instead of twisting `Chest` 180° relative to lower body, verified in `Screenshots/ArtPreviews/Agent_Developer_portal_ret.png`.
4. **Unified 65-Clip Shared Library Across All 4 Agents**:
   - Merged the 10 new/upgraded `art-polish` locomotion, offer, handoff, decision-reaction, and cheer clips (`Walk_Start`, `Walk_Stop`, `Walk_Carry_Paper`, upgraded `Turn_Left`/`Turn_Right`, `React_Approve`, `React_Reject`, `Offer_Document_Present`, upgraded `Offer_Document`, `Offer_Document_Release`, `Handoff_Give`, `Handoff_Receive`, `React_Completion_Cheer`) with the 16 Phase 3 Wall Habitat clips (`Elevator_*`, `Select_Highlighted_Wave`, `DeskVisit_*`, `Portal_*`, `Manager_*`) in `build_shared_animation_library`.
   - Removed duplicate strip appends in `generate_wall_manager_agent.py` so `Agent_Researcher`, `Agent_Developer`, `Agent_Tester`, and `Agent_Manager` share the exact same 65 actions and are wired to `MuseDesk_Agent.controller` and `MuseDesk_WallAgent.controller` with `IsCarryingPaper` transitions and non-loop vs loop flag parity (`MuseDeskArtPipeline.LoopingCharacterClips` + `ModelBoundsFixer.LoopingClips`).

### B. Priority 3 Stamp Juice (`ArtSource/Scripts/generate_stamp_juice.py`, `MuseDeskArtPipeline.cs`)
- Generated and imported `Doc_Stamp_Approve`, `Doc_Stamp_Reject` with `Stamp_Impact_Squash` (`1–10` frames @ 24 FPS, `0.375s`, volume-preserving squash anchored on `Stamp_SquashPivot` at bottom contact) and opaque embossed ink marks `Doc_StampInkMark_Approve` (448 verts / 840 tris) & `Doc_StampInkMark_Reject` (416 verts / 780 tris) using opaque `Mat_StampInk_Approve` / `Mat_StampInk_Reject` (`renderQueue=2000`, zero transparent overdraw).
- Verified via Unity `BuildStampJuice()` and visual captures `Screenshots/ArtPreviews/StampJuice_Marks_TopDown.png` & `Screenshots/ExperiencePolish/02_StampJuice_On_DecisionPaper.png`.

### C. Wall Habitat Architecture, Socket Normalization & Visual Polish (`WallHabitatArtSetup.cs`, `generate_wall_habitat_*.py`)
1. **`NormalizeWallSockets` Armature Protection Fix**:
   - Added `HasBoneChild(Transform t)` check in `WallHabitatArtSetup.cs` so rigged armatures without `"Armature"` in their object name (`WallHabitat_DeskVisitPerch`, `WallHabitat_LargeAgentPortal`, `WallHabitat_ElevatorCar`, `WallHabitat_ElevatorSpine_*`, `WallHabitat_TaskPrinter`) keep their unit scale instead of being scaled `1/100x`, and reparented non-bone sockets cleanly to prefab roots with identity rotation and unit scale (`totalBrokenSockets = 0`, `allSocketsUnitScale = true` across all 24 prefabs).
2. **`MidPlanningNook` Rail & `ManagerLookout` Clearance**:
   - Leveled the previously tilted 5° plank/rail above `WallBoard` (`floor_h + 0.162 m`) with brass mounting brackets and clean hanging role tokens.
   - Shifted the `ManagerLookout` swivel chair back (`dy - 0.072 m` seat / `dy - 0.088 m` backrest) so standing `Agent_Manager` at `ManagerDeskSocket` clears the seat without clipping (`Screenshots/WallHabitat/Unity/01_WallHabitat_3Tier_Front.png`).
3. **`WallHabitat_LargeAgentPortal` Pose & Upright `TaskCard` Orientation**:
   - Sampled `Portal_Active_Idle` on `WallHabitat_LargePortal_WithProxy` and preview scene so the full arch is visible (`05_WallHabitat_LargePortal_Front.png`).
   - Oriented preview `WallHabitat_TaskCard_*` (`Euler(-68f, 180f, 0f)`) and `LargePortal_SideDialogueCard` (`Euler(-90f, 180f, 0f)`) so the `BrassClip` sits at the top while keeping the front typography side facing the viewer (`04_WallHabitat_DeskVisit_And_Cards.png`, `05_WallHabitat_LargePortal_Front.png`).
4. **Priority B Navigation & Portal Sockets**:
   - Added `PathNode_*`, `ElevatorEntry_*`, `ElevatorExit_*`, `DoorClearance`, `AgentStand_*`, `Handoff_*`, `CardOutput_*`, `PortalSource_Wall`, and `PortalSource_Floor` to Blender anchors and `WallHabitatSocketRegistry.knownIds`.
5. **Code Quality**:
   - Split `WallHabitatControllers.cs` into four single-responsibility files in `Assets/MuseDesk/Scripts/Art/`:
     - `WallHabitatElevatorVisualController.cs`
     - `WallHabitatPlacementGhostController.cs`
     - `WallHabitatAgentSelectionVisual.cs`
     - `WallHabitatLargePortalVisualController.cs`

### D. Existing Release Assets Continuity Check
- Verified `Mat_Spatial_PortalCone_Lite.mat` remains intact (`hasPortalConeLite=True` in `ExperiencePolishCapture.cs`) and captured `Spatial_CeilingPod_Complete.prefab` cleanly in `Screenshots/ExperiencePolish/03_CeilingPod_And_PortalCone_Check.png` (no pink/broken shaders; the `missingMats=15` counter reported by `ExperiencePolishCapture` reflects pre-existing unassigned default material slots inside sub-components of the legacy scene/spatial prefabs spawned in that multi-prefab scene rather than Wall Habitat assets, which independently verified `totalMissingMaterials: 0` in `WALL_HABITAT_INVENTORY.json`).

---

## 3. Verification Captures Index
- **Blender Close-Ups & Stamp Marks**: `Screenshots/ArtPreviews/` (`Agent_Researcher_earpod.png`, `Agent_Developer_portal_ret.png`, `Agent_Researcher_walk_carry.png`, `StampJuice_Marks_TopDown.png`, `StampJuice_Approve_Impact.png`, `StampJuice_Reject_Impact.png`)
- **Wall Habitat Unity Showcase**: `Screenshots/WallHabitat/Unity/01..06*.png`
- **Experience Polish Unity Captures (`ExperiencePolishCapture.cs`)**:
  - `Screenshots/ExperiencePolish/01_Mascots_All4_EarPod_Lineup.png`
  - `Screenshots/ExperiencePolish/02_StampJuice_On_DecisionPaper.png`
  - `Screenshots/ExperiencePolish/03_CeilingPod_And_PortalCone_Check.png`
  - `Screenshots/ExperiencePolish/04_WallHabitat_SeatedEyeLevel_Check.png`
