# MuseDesk — Phase 3 Wall Habitat ("Workshop Treehouse") Asset & Socket Manifest

> Generated from verified Blender batch runs and Unity pipeline (`MuseDesk.Editor.ArtPipeline.WallHabitatArtSetup.BuildAll`, Unity `6000.6.8f1`) on branch `art/wall-habitat`.

## 1. Overview & Deliverables Summary

- **Blender Sources & Generators**:
  - `ArtSource/Scripts/wall_habitat_common.py`
  - `ArtSource/Scripts/generate_wall_habitat_core.py` (8 modular core structural & placement assets)
  - `ArtSource/Scripts/generate_wall_habitat_mechanical.py` (8 elevator, printer & task-card assets)
  - `ArtSource/Scripts/generate_wall_habitat_interactions.py` (3 perch, selection & large portal assets)
  - `ArtSource/Scripts/generate_wall_manager_agent.py` (`Agent_Manager` mascot on shared 26-bone Pipkin rig with 65 shared clips)
- **Unity Output Directories**:
  - Models: `Assets/MuseDesk/Art/Models/WallHabitat/` (20 `.fbx` models)
  - Prefabs: `Assets/MuseDesk/Art/Prefabs/WallHabitat/` (**24 prefabs**: 20 individual + 4 assembled composites)
  - Animator Controllers: `Assets/MuseDesk/Art/Controllers/WallHabitat/` (**7 controllers**)
  - Visual Helper Scripts (art-only): `Assets/MuseDesk/Scripts/Art/` (`WallHabitatSocketRegistry.cs`, `WallHabitatElevatorVisualController.cs`, `WallHabitatPlacementGhostController.cs`, `WallHabitatAgentSelectionVisual.cs`, `WallHabitatLargePortalVisualController.cs`)
  - Pipeline Editor Setup: `Assets/MuseDesk/Editor/ArtPipeline/WallHabitatArtSetup.cs`
  - Preview Scene & Renders: `Assets/MuseDesk/Scenes/WallHabitatPreviewScene.unity`, `Screenshots/WallHabitat/Unity/01..06*.png`
  - Machine Inventory: `docs/Art/WALL_HABITAT_INVENTORY.json` (`totalPrefabs: 24`, `totalMissingScripts: 0`, `totalBrokenSockets: 0`, `totalMissingMaterials: 0`, `missingRequired3TierSockets: 0`, `missingRequired2TierSockets: 0`)

---

## 2. Prefab Inventory & Quest 3 Geometry Metrics

Exact bounds, vertex/triangle counts, materials, sockets, and wired Animator state counts from `docs/Art/WALL_HABITAT_INVENTORY.json`:

| Prefab Name | Verts | Tris | Mats | Sockets (Unit Scale) | Clips | World Bounds (`W x H x D`) |
|---|---:|---:|---:|---:|---:|---|
| `WallHabitat_Complete_3Tier` *(Composite)* | 91,548 | 83,530 | 30 | 65 (`True`) | 0 | `0.975 x 0.794 x 0.267 m` |
| `WallHabitat_Complete_2Tier` *(Composite)* | 82,178 | 77,650 | 30 | 53 (`True`) | 0 | `0.770 x 0.564 x 0.267 m` |
| `WallHabitat_DeskVisit_Complete` *(Composite)* | 7,338 | 8,338 | 13 | 7 (`True`) | 0 | `0.140 x 0.220 x 0.140 m` |
| `WallHabitat_LargePortal_WithProxy` *(Composite)* | 13,648 | 13,024 | 17 | 11 (`True`) | 0 | `0.775 x 1.000 x 0.540 m` |
| `Agent_Manager` | 4,152 | 6,520 | 12 | 0 (`True`) | 65 | `0.140 x 0.245 x 0.103 m` |
| `WallHabitat_Backplate_Full` | 8,693 | 5,588 | 10 | 30 (`True`) | 0 | `0.975 x 0.776 x 0.213 m` |
| `WallHabitat_Backplate_Compact` | 7,945 | 5,156 | 10 | 24 (`True`) | 0 | `0.768 x 0.546 x 0.213 m` |
| `WallHabitat_CubicleBay` | 4,921 | 3,176 | 7 | 15 (`True`) | 0 | `0.231 x 0.240 x 0.236 m` |
| `WallHabitat_CubicleBay_Compact` | 4,921 | 3,176 | 7 | 15 (`True`) | 0 | `0.181 x 0.240 x 0.227 m` |
| `WallHabitat_HallwaySegment` | 2,292 | 1,544 | 4 | 8 (`True`) | 0 | `0.140 x 0.060 x 0.087 m` |
| `WallHabitat_MidPlanningNook` | 6,894 | 4,416 | 13 | 16 (`True`) | 0 | `0.456 x 0.218 x 0.236 m` |
| `WallHabitat_ManagerLookout` | 8,297 | 5,512 | 11 | 17 (`True`) | 0 | `0.517 x 0.256 x 0.252 m` |
| `WallHabitat_PlacementGhost` | 3,214 | 1,840 | 2 | 0 (`True`) | 0 | `0.992 x 0.774 x 0.251 m` |
| `WallHabitat_ElevatorSpine_3Tier` | 7,884 | 4,908 | 10 | 26 (`True`) | 8 | `0.165 x 0.733 x 0.219 m` |
| `WallHabitat_ElevatorSpine_2Tier` | 6,156 | 3,876 | 9 | 21 (`True`) | 6 | `0.165 x 0.503 x 0.219 m` |
| `WallHabitat_ElevatorCar` | 4,065 | 2,720 | 6 | 10 (`True`) | 5 | `0.112 x 0.190 x 0.112 m` |
| `WallHabitat_TaskPrinter` | 2,950 | 1,980 | 7 | 5 (`True`) | 4 | `0.082 x 0.071 x 0.067 m` |
| `WallHabitat_TaskCard_Researcher` | 1,536 | 988 | 4 | 0 (`True`) | 0 | `0.042 x 0.005 x 0.056 m` |
| `WallHabitat_TaskCard_Developer` | 1,536 | 988 | 4 | 0 (`True`) | 0 | `0.042 x 0.005 x 0.056 m` |
| `WallHabitat_TaskCard_Tester` | 1,536 | 988 | 4 | 0 (`True`) | 0 | `0.042 x 0.005 x 0.056 m` |
| `WallHabitat_TaskCard_Manager` | 1,536 | 988 | 3 | 0 (`True`) | 0 | `0.042 x 0.005 x 0.056 m` |
| `WallHabitat_DeskVisitPerch` | 1,771 | 1,188 | 5 | 7 (`True`) | 4 | `0.140 x 0.022 x 0.140 m` |
| `WallHabitat_SelectionHalo` | 1,746 | 1,140 | 2 | 0 (`True`) | 0 | `0.112 x 0.259 x 0.112 m` |
| `WallHabitat_LargeAgentPortal` | 8,286 | 6,144 | 8 | 11 (`True`) | 4 | `0.601 x 0.097 x 0.540 m` *(rest pose; `Portal_Active_Idle` expands arch to full frame as seen on `WallHabitat_LargePortal_WithProxy`)* |

> [!NOTE]
> All modular Wall Habitat pieces reuse shared palette materials (`Mat_WarmWood`, `Mat_DarkWood`, `Mat_CreamCeramic`, `Mat_WarmBeige`, `Mat_FeltSlate`, `Mat_Brass`, `Mat_BrushedMetal`, `Mat_ChamberGlass`, role tints, `Mat_PaperWarm`) plus restrained portal/ghost materials (`Mat_Wall_PortalRim`, `Mat_Wall_Ghost_Valid`, `Mat_Wall_Ghost_Review`, `Mat_Wall_Ghost_Invalid`).

---

## 3. Contract & Priority B Navigation/Handoff Sockets (`WallHabitatSocketRegistry`)

`WallHabitatSocketRegistry.ResolveAllSockets()` enforces unit world scale (`allSocketsUnitScale: true` across all 24 prefabs) by normalizing bone/empty hierarchy during `WallHabitatArtSetup.NormalizeWallSockets` (`HasBoneChild` protects rigged armatures from 1/100 scale collapse).

### Required Contract Sockets (Verified Zero Missing in 3Tier & 2Tier)
- **Root & Mounting**: `WallContact`, `PlacementBoundsRef`, `RaySelectCollider`, `AttentionBeaconSocket`, `CourierDispatchSocket`
- **Structural Module Mounts**: `ResearchCubicleSocket`, `DeveloperCubicleSocket`, `TestingCubicleSocket`, `PlanningNookSocket` *(3-Tier only)*, `ManagerLookoutSocket`, `ElevatorSpineSocket`, `TaskPrinterSocket`
- **Elevator**: `ElevatorCarSocket`, `ElevatorStop_Lower`, `ElevatorStop_Mid` *(3-Tier only)*, `ElevatorStop_Manager`, `ElevatorBoardPose_Lower`, `ElevatorBoardPose_Mid` *(3-Tier only)*, `ElevatorBoardPose_Manager`, `CabAgentStandPose`
- **Manager & Cards**: `ManagerDeskSocket`, `TaskCardSpawnSocket`, `TaskCardSlot_0..2`
- **Desk Visit & Large Portal**: `DeskPerchSurfaceMount`, `AgentPerchPose`, `DeskTargetMount`, `PortalFloorContact`, `AgentHomePose`, `PortalThresholdLine`, `DialoguePanelSocket`

### Priority B Extended Navigation, Elevator Clearance & Portal Parity Sockets
- **Walkway / Pathing Nodes**: `PathNode_LowerWalk_0..3`, `PathNode_MidDeck`, `PathNode_ManagerDeck`, `PathNode_Entry`, `PathNode_ShaftFront`
- **Elevator Entry/Exit & Doorway Envelopes**: `ElevatorEntry_Lower`, `ElevatorExit_Lower`, `ElevatorEntry_Mid`, `ElevatorExit_Mid`, `ElevatorEntry_Manager`, `ElevatorExit_Manager`, `DoorClearance` *(on Backplate, CubicleBay, MidPlanningNook, ManagerLookout, ElevatorSpine, ElevatorCar)*
- **Stand Poses & Handoff Nodes**: `AgentStand_Cubicle`, `AgentStand_Nook_0`, `AgentStand_Nook_1`, `AgentStand_Manager`, `Handoff_In`, `Handoff_Out`, `Handoff_Mid`, `Handoff_Manager`, `Handoff_Printer`, `CardOutput_Tray`, `CardOutput_Manager`
- **Portal Anchor Parity**: `PortalSource_Wall` (`WallHabitat_Backplate_*`), `PortalSource_Floor` (`WallHabitat_LargeAgentPortal`)

---

## 4. Unity Preview Scene & Verified Captures

Preview Scene: `Assets/MuseDesk/Scenes/WallHabitatPreviewScene.unity`  
Unity Batch Captures: `Screenshots/WallHabitat/Unity/`
1. `01_WallHabitat_3Tier_Front.png` — Front elevation of 3-Tier Treehouse (`MidPlanningNook` leveled rail + hanging tokens, `ManagerLookout` chair clearance, 3 lower cubicles, elevator shaft, TaskPrinter)
2. `02_WallHabitat_3Tier_ThreeQuarter.png` — 3/4 view showing wall-flush contact, balconies, elevator depth, and porch wing beacon/courier
3. `03_WallHabitat_2Tier_Compact.png` — Compact 2-Tier layout for constrained physical walls
4. `04_WallHabitat_DeskVisit_And_Cards.png` — `WallHabitat_DeskVisit_Complete` (`/btw` desk perch with `Agent_Researcher`) + 4 upright role-colored `WallHabitat_TaskCard_*` badges
5. `05_WallHabitat_LargePortal_Front.png` — `WallHabitat_LargePortal_WithProxy` sampled in `Portal_Active_Idle` (~1.0 m `Agent_Developer` + off-to-the-side `DialoguePanelSocket` card)
6. `06_WallHabitat_PlacementGhost_States.png` — `ValidTeal`, `ReviewAmber`, and `InvalidRed` wall placement ghost states with surface depth pins
