# MuseDesk — Deck Refit & Mascot Role Trims Report

**Branch:** `art/deck-refit` (branched from `feature/living-world-polish` at `4c3c9f2`)  
**Worktree:** `C:\dev\MuseDesk\MuseDesk\.worktrees\deck-refit`  
**Scope Constraints Respected:** Zero edits to `MainMRScene.unity`, `Scripts/**`, `backend/`, or `ProjectSettings/`.  
**Verification Context:** All screenshots and clearance metrics below were captured in **Editor, not headset**.

---

## 1. Floor Office Platform & Ceiling Pod Deck Refit (V2 Layout)

### Root-Cause Coordinate & Fit Findings
- In `MainMRScene.unity` (`WorkspaceRoot`-local frame):
  - `SpatialArt_FloorPlatform` is at `(0, 0, +0.1350)` with `FloorOfficePlatform` at `(0, -0.0298, 0)`, scale `(1.3529, 1.3529, 1.3529)`.
  - `SpatialArt_CeilingPod` is at `(0, 0, +0.1000)` with `SuspendedOfficePod` at `(0, -0.0352, 0)`, scale `(1.4663, 1.4663, 1.5294)`.
  - Measured V2 station/tray footprints (`WorkspaceRoot`-local):
    - `ResearchStation`: origin `(-0.3660, 0.0950)`, mesh bounds `x[-0.4999, -0.2238]`, `z[0.1236, 0.2557]`
    - `DevStation`: origin `(0.1220, 0.0500)`, mesh bounds `x[-0.0236, 0.2260]`, `z[0.0670, 0.2158]`
    - `QAStation`: origin `(0.3850, 0.0000)`, mesh bounds `x[0.2386, 0.4890]`, `z[0.0170, 0.1658]`
    - `InboxTray`: origin `(-0.3200, 0.3200)`, mesh bounds `x[-0.3590, -0.2810]`, `z[0.2720, 0.3680]`
    - `OutboxTray`: origin `(0.3200, 0.2500)`, mesh bounds `x[0.2810, 0.3590]`, `z[0.2020, 0.2980]`
    - `FloorCourierStation`: origin `(-0.2000, 0.3300)`

### Changes Implemented
- **`ArtSource/Scripts/generate_spatial_floor_and_preview.py` (`FloorOfficePlatform`, `PlacementGhostPreview_*`):**
  - Expanded deck to `width=0.788, depth=0.490` with `rim=0.014` (`WoodDeck` `0.760 × 0.462`, Unity `BoxCollider` `x[-0.5168, 0.5168]`, `z[-0.1802, 0.4502]`) so outer V2 desks, `InboxTray`, and `OutboxTray` sit completely inside the deck with `0.00 cm` overhang.
  - Centered `ResearchPad` (`x=-0.2676, y=+0.0222`), `DeveloperPad` (`x=+0.0776, y=-0.0074`), `TesterPad` (`x=+0.2691, y=-0.0443`), and `DispatchBayRing` (`x=-0.1478, y=+0.1441`) under their V2 stations (`ResearchStation`, `DevStation`, `QAStation`, and `FloorCourierStation`).
  - Pushed back and side railings clear of V2 desks and walk routes; updated `PlacementGhostPreview_Floor` and `PlacementGhostPreview_Ceiling` footprints to match.
- **`ArtSource/Scripts/generate_spatial_ceiling_pod.py` (`SuspendedOfficePod`):**
  - Expanded `MainHull` to `w=0.728, d=0.435` centered at `cy=+0.0125` (`WoodDeck` `0.700 × 0.407`, Unity `BoxCollider` `x[-0.5132, 0.5132]`, `z[-0.1921, 0.4304]`), eliminating outer desk overhang and bringing `InboxTray` (`zMax=0.3680`) cleanly inside the wood deck while staying inside the validated `[-0.230, +0.470]` envelope.
  - Centered `PodResearchPad` / `ResearchStationSocket` (`x=-0.2469, y=+0.0425`), `PodDeveloperPad` / `DeveloperStationSocket` (`x=+0.0716, y=+0.0163`), and `PodTesterPad` / `TesterStationSocket` (`x=+0.2482, y=-0.0163`) on the V2 stations, and aligned stabilizer wing trim colors (`Wing_L` Tester side mint, `Wing_R` Researcher side lavender).
  - Kept `DockingConnector` plumb at `(0, -0.120, 0.258)` Blender-local (`z = -0.0835 m` `WorkspaceRoot`-local matching `MainMRScene` `mountOffset`), pushed `ArchBaseSocket` (`±0.170, -0.174`), `ArchTopCollar` (`±0.116, -0.126`), and `ArchStrut` (`±0.143, -0.150, rot=(-11.5°, ±12.5°, 0)`) cleanly back behind the Researcher-to-Developer aisle, moved `InfoDisplay` and `BeaconPad` clear of `OutboxTray`, and tucked `ShelfRail_L/R` below the deck plane.
- **`Assets/MuseDesk/Editor/ArtPipeline/SpatialWorkspaceArtSetup.cs`:**
  - Updated `BuildFloorPlatformPrefab` (`platW = 0.764f, platD = 0.466f, plaqueZ = 0.2505f`) and `BuildSuspendedPodPrefab` (`bc.center = (0f, 0.020f, 0.0125f), bc.size = (0.700f, 0.040f, 0.407f)`).
  - Rebuilt spatial prefabs (`SpatialWorkspaceArtSetup.BuildAll()`: `16 prefabs, missingScripts=0, brokenSockets=0, missingMats=0`).

### Before vs After Deck Fit & Walk Clearance (`>= 0.015 m`)

Evaluated in Unity Editor batchmode against `MainMRScene.unity` using the same `WalkClearance.Evaluate` route logic (`R1`–`R7`) as `DeskClearanceDrive` (`scripts\drv.ps1` was not run directly because it targets an interactive Play Mode session).

| Mode | Metric | Before (`report_before.txt`) | After (`report_after.txt`) |
| --- | --- | --- | --- |
| **Desk** | Routes `< 0.015 m` / min gap | `0` / `0.0177 m` (R3, R5) | `0` / `0.0177 m` (R3, R5) |
| **Floor** | Deck `BoxCollider` (`WorkspaceRoot`) | `x[-0.4600, 0.4600] z[-0.1626, 0.4326]` | `x[-0.5168, 0.5168] z[-0.1802, 0.4502]` |
| **Floor** | Station/tray max overhang | `3.99 cm` (`ResearchStation -X`), `2.90 cm` (`QAStation +X`) | `0.00 cm` (`ResearchStation`, `DevStation`, `QAStation`, `InboxTray`, `OutboxTray`) |
| **Floor** | Routes `< 0.015 m` / min gap | `0` / `0.0177 m` (R3, R5) | `0` / `0.0177 m` (R3, R5) |
| **Ceiling** | Deck `BoxCollider` (`WorkspaceRoot`) | `x[-0.4399, 0.4399] z[-0.1676, 0.3676]` | `x[-0.5132, 0.5132] z[-0.1921, 0.4304]` |
| **Ceiling** | Station/tray max overhang | `6.00 cm` (`ResearchStation -X`), `4.91 cm` (`QAStation +X`), `0.04 cm` (`InboxTray +Z` on edge) | `0.00 cm` (`ResearchStation`, `DevStation`, `QAStation`, `InboxTray`, `OutboxTray`) |
| **Ceiling** | Routes `< 0.015 m` / min gap | `0` / `0.0175 m` (`SuspendedOfficePod_Mesh`) | `0` / `0.0177 m` (`Research_Static_WarmWood`, all 7 routes PASS) |

All 7 routes (`R1`–`R7`) pass `>= 0.015 m` in Desk (`min=0.0177 m`), Floor (`min=0.0177 m`), and Ceiling (`min=0.0177 m`) modes:
- `R1` Researcher home -> delivery: `0.0181 m` (PASS)
- `R2` Researcher delivery -> home: `0.0181 m` (PASS)
- `R3` Researcher home -> Developer: `0.0177 m` (PASS)
- `R4` Researcher delivery -> Developer: `0.0199 m` (PASS)
- `R5` Researcher Developer stand -> home: `0.0177 m` (PASS)
- `R6` Developer home -> Tester: `0.0190 m` (PASS)
- `R7` Developer Tester stand -> home: `0.0190 m` (PASS)

---

## 2. Mascot Accent Trims -> Soft, Matte Role Colours

- Added 9 dedicated non-emissive matte URP Lit materials (`Roughness 0.72–0.78`, `Metallic 0.00`, `Emissive false`) to `Assets/MuseDesk/Editor/ArtPipeline/MuseDeskArtPipeline.cs` (`PaletteSpecs`) and `Assets/MuseDesk/Art/Materials/`:
  - **Researcher (`#A9B8F0`):** `Mat_RoleTrim_Researcher` (`#A9B8F0`), `Mat_RoleTrim_Researcher_Soft` (`#C2CDF6`) — applied to capelet, beret, ear pods, wrist cuffs, and sprout stem/leaves in `ArtSource/Scripts/generate_characters.py`.
  - **Developer (`#D2A6E8`):** `Mat_RoleTrim_Developer` (`#D2A6E8`), `Mat_RoleTrim_Developer_Soft` (`#E6CEF4`) — applied to hoodie vest, chest badge, headphones, mic tip, ear pods, wrist cuffs, cyber fin, and crest beacon.
  - **Tester (`#F0A9C4`):** `Mat_RoleTrim_Tester` (`#F0A9C4`), `Mat_RoleTrim_Tester_Soft` (`#D889A8`), `Mat_RoleTrim_Tester_Lens` (`#F8CADC`) — applied to utility vest, belt trim, headband, loupe barrels/lenses, calibration dish & probe, analyzer pack, ear pods, and wrist cuffs.
  - **Manager (`#4B4F9C`):** `Mat_RoleTrim_Manager` (`#4B4F9C`), `Mat_RoleTrim_Manager_Soft` (`#797DC2`) — applied to waistcoat, architect cap, compass dial rim, monocle rim, ear pods, wrist cuffs, and crest beacon orb in `ArtSource/Scripts/generate_wall_manager_agent.py`.
- Re-exported `Agent_Researcher.fbx`, `Agent_Developer.fbx`, `Agent_Tester.fbx`, and `Agent_Manager.fbx` (preserving 26 bones, all 77 animation clips, loop flags, and sockets), and synced `Assets/MuseDesk/Art/Prefabs/WallHabitat/Agent_Manager.prefab` materials.
- **Note for Engineering (`MainMRScene.unity` locked on art branch):** `MainMRScene.unity` contains per-instance `m_Materials.Array.data[*]` overrides on `Agent_Researcher`, `Agent_Developer`, and `Agent_Tester` pointing to legacy status/station materials (`Mat_Researcher_Lavender`, `Mat_Researcher_Teal`, `Mat_Developer_Coral`, `Mat_Tester_Mint`, etc., which are also shared by station/factory props). Because `MainMRScene.unity` cannot be edited here, those legacy scene overrides remain intact; reverting the `m_Materials` overrides on those 3 scene instances lets them inherit the `Mat_RoleTrim_*` prefab materials verified in the mascot lineup screenshot.

---

## 3. EditMode Verification

Ran full Unity EditMode suite (`unity test --mode EditMode`):
- Result: `total=1361, passed=1360, failed=0, inconclusive=0, skipped=1` (1 pre-existing `[Ignore]` test: `MuseDesk.Tests.EditMode.Wall.ElevatorSequenceTests.StuckDoors_TakeTheCabinOutOfService_WaitersWalkHome_RiderHolds_ThenIsReleased`).
- `MuseDesk.Tests.AgentInteraction.AgentClipLoopFlagTests`: `5/5` Passed (`DeskAgentLoopingClipsKeepLoopTime(0..2)`, `DeskAgentsHaveTheCoreLoopingClips`, `ManagerLoopingClipsKeepLoopTime_IfPresent`).
- `MuseDesk.Tests.AgentInteraction.AgentRootMotionGuardTests`: `7/7` Passed across all `Prefabs/Agents/`, `Art/Prefabs/Characters/`, and `Art/Prefabs/WallHabitat/Agent_Manager.prefab`.

---

## 4. Editor Screenshots & Raw Reports (**Editor, not headset**)

- **Floor (Editor, not headset):**
  - Before plan: [floor_before_plan.png](../review_screenshots/deck_refit/floor_before_plan.png)
  - Before right 3/4: [floor_before_right.png](../review_screenshots/deck_refit/floor_before_right.png)
  - After plan: [floor_after_plan.png](../review_screenshots/deck_refit/floor_after_plan.png)
  - After right 3/4: [floor_after_right.png](../review_screenshots/deck_refit/floor_after_right.png)
- **Ceiling Pod (Editor, not headset):**
  - Before plan: [ceiling_before_plan.png](../review_screenshots/deck_refit/ceiling_before_plan.png)
  - Before right 3/4: [ceiling_before_right.png](../review_screenshots/deck_refit/ceiling_before_right.png)
  - After plan: [ceiling_after_plan.png](../review_screenshots/deck_refit/ceiling_after_plan.png)
  - After right 3/4: [ceiling_after_right.png](../review_screenshots/deck_refit/ceiling_after_right.png)
- **Mascots Lineup (Editor, not headset):**
  - Before lineup: [mascots_before_lineup.png](../review_screenshots/deck_refit/mascots_before_lineup.png)
  - After lineup: [mascots_after_lineup.png](../review_screenshots/deck_refit/mascots_after_lineup.png)
- **Raw Measurement Reports (Editor, not headset):**
  - [report_before.txt](../review_screenshots/deck_refit/report_before.txt)
  - [report_after.txt](../review_screenshots/deck_refit/report_after.txt)
