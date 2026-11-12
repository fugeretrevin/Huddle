# MuseDesk — 3D Asset Library, Rigging, Animation & Environment Integration Guide

## 1. Overview & Architecture

This document describes the complete, original 3D asset library, skeletal rigs, animation clips, miniature workstations, Kanban task board, document props, software factory, spatial diagram kit, decorations, and Unity prefabs created for **MuseDesk** on **Meta Quest 3** (`Unity 6000.6.8f1`, URP `17.6`).

All assets are 100% original ("**Pipkin Sprites & Cozy Tactile Miniature Studio**") and designed to integrate non-destructively with the existing Quest 3 passthrough, hand tracking, `SessionController`, and `Phase2Bootstrap` systems.

---

## 2. Directory Organization

```text
C:\dev\MuseDesk\MuseDesk\
├── ArtSource\
│   ├── Blender\
│   │   ├── Characters\        (Agent_Researcher.blend, Agent_Developer.blend, Agent_Tester.blend)
│   │   ├── Workstations\      (Workstation_Research.blend, Workstation_Developer.blend, Workstation_Testing.blend)
│   │   ├── TaskBoard\         (6 editable .blend files)
│   │   ├── Documents\         (18 editable .blend files)
│   │   ├── Factory\           (SoftwareFactory_Complete.blend)
│   │   ├── SpatialDiagrams\   (14 editable .blend files)
│   │   └── Decorations\       (16 editable .blend files)
│   └── Scripts\
│       ├── generate_characters.py    (Procedural characters + 22-bone rig + 39 NLA clips)
│       ├── generate_workstations.py  (Procedural Research, Developer & Testing workstations)
│       ├── generate_props.py         (TaskBoard, Documents, Diagrams, Decorations)
│       ├── generate_factory.py       (Rigged Software Factory + 10 stage clips)
│       ├── export_assets.py          (Master Blender build orchestrator)
│       └── validate_assets.py        (Automated mesh/bone/clip validator -> ASSET_MANIFEST.json)
├── Assets\MuseDesk\
│   ├── Art\
│   │   ├── Models\            (61 exported Unity-ready .fbx files)
│   │   ├── Materials\         (33 shared URP Lit palette materials)
│   │   ├── Controllers\       (MuseDesk_Agent.controller, MuseDesk_Factory.controller)
│   │   └── Prefabs\
│   │       ├── Characters\    (Agent_Researcher, Agent_Developer, Agent_Tester)
│   │       ├── Workstations\  (Workstation_Research, Workstation_Developer, Workstation_Testing)
│   │       ├── TaskBoard\     (TaskBoard_KanbanFrame, TaskCard_Grabbable, Badges, Links)
│   │       ├── Documents\     (18 paper, approval, tray, and collaboration prefabs)
│   │       ├── Factory\       (SoftwareFactory_Complete)
│   │       ├── Diagrams\      (14 node/connector prefabs + SpatialDiagram_Demo_Layout)
│   │       ├── Decorations\   (16 modular office decoration prefabs)
│   │       └── Workspace\     (MuseDesk_Workspace_Root — full assembled miniature office)
│   ├── Scripts\Art\
│   │   ├── AgentAnimatorBridge.cs
│   │   ├── WorkstationVisualController.cs
│   │   └── SoftwareFactoryVisualController.cs
│   ├── Editor\ArtPipeline\
│   │   └── MuseDeskArtPipeline.cs
│   └── Scenes\
│       ├── MainMRScene.unity              (Preserved production scene)
│       ├── ArtShowcaseScene.unity         (Full office + 61-asset gallery showcase)
│       └── MRWorkstationTestScene.unity   (Desk-scale miniature office test scene)
└── Docs\Art\
    ├── ART_DIRECTION.md
    ├── RIG_SPECIFICATION.md
    ├── ANIMATION_MANIFEST.md
    ├── ASSET_MANIFEST.json
    └── INTEGRATION_GUIDE.md
```

---

## 3. Reproducible Build Commands

### Rebuild All Blender Sources & FBX Exports
```powershell
blender-launcher.exe -b --factory-startup -P C:\dev\MuseDesk\MuseDesk\ArtSource\Scripts\export_assets.py
```

### Rebuild All Unity Materials, Controllers, Prefabs & Showcase Scenes
From inside Unity Editor:
- Menu bar: **`MuseDesk -> Art Pipeline -> Build Complete Phase 2 Art Library & Scenes`**

Or via the Unity Pipeline CLI:
```powershell
unity command --project-path C:\dev\MuseDesk\MuseDesk script run --file Assets/MuseDesk/Editor/ArtPipeline/MuseDeskArtPipeline.cs --entry MuseDesk.Editor.ArtPipeline.MuseDeskArtPipeline.BuildAll --timeout 180
```

---

## 4. Runtime Integration Contracts for Claude Code

### A. Rigged Characters (`AgentAnimatorBridge`)
Each character prefab (`Agent_Researcher`, `Agent_Developer`, `Agent_Tester`) includes:
- `Animator` wired to `MuseDesk_Agent.controller` (all 39 shared clips).
- `AgentAnimatorBridge` component with automatic `MoveSpeed` locomotion blending and direct state playback:
  ```csharp
  var bridge = agentRoot.GetComponent<MuseDesk.Art.AgentAnimatorBridge>();
  bridge.PlayAnimationState("Typing");          // Crossfades to any of the 39 clips
  bridge.PlayAnimationState("Offer_Document");
  bridge.AttachPropToSocket(paperTransform, "RightHand_Attach");
  ```
- **5 Standard Attachment Transforms**:
  - `RightHand_Attach` (`Hand_R`)
  - `LeftHand_Attach` (`Hand_L`)
  - `Head_Attach` (`Head`)
  - `Back_Attach` (`Chest`)
  - `Document_Attach` (`Chest` front two-handed presentation socket)

### B. Workstations (`WorkstationVisualController`)
Each workstation prefab (`Workstation_Research`, `Workstation_Developer`, `Workstation_Testing`) includes `WorkstationVisualController` exposing the exact named anchors:
- **Developer Workstation**:
  - `AgentWorkPosition`, `AgentSeatPosition`, `TaskDropZone`, `DocumentPickupPoint`, `ArtifactOutputPoint`, `MonitorScreen_Left`, `MonitorScreen_Right`, `HandoffReceivePoint`
  - Dynamic `TextMeshPro` components (`MonitorLeft_DynamicTMP`, `MonitorRight_DynamicTMP`) mounted on the screen transforms.
- **Research Workstation**:
  - `AgentWorkPosition`, `AgentSeatPosition`, `TaskDropZone`, `DocumentPickupPoint`, `ArtifactOutputPoint`, `BlueprintSpawnPoint`, `HandoffReceivePoint`
- **Testing Workstation**:
  - `AgentWorkPosition`, `AgentSeatPosition`, `TaskDropZone`, `DocumentPickupPoint`, `ArtifactOutputPoint`, `ChamberSlotPoint`, `AcceptedTrayPoint`, `RejectedTrayPoint`, `HandoffReceivePoint`
  - 5-State Status Lights (`Idle`, `Testing`, `Passed`, `Failed`, `Blocked`) controlled via:
    ```csharp
    workstationController.SetTestingVisualState(WorkstationVisualController.TestingVisualState.Testing);
    ```

### C. Physical Kanban Task Board & Cards
- **`TaskBoard_KanbanFrame.prefab`**:
  - 4 Columns (`Queued`, `InProgress`, `Blocked`, `Completed`) with `HeaderLabel_<Column>_Anchor`, `ColumnDropZone_<Column>`, and 16 snap sockets (`Socket_Queued_1`..`4`, `Socket_InProgress_1`..`4`, `Socket_Blocked_1`..`4`, `Socket_Completed_1`..`4`).
- **`TaskCard_Grabbable.prefab`**:
  - Includes `BoxCollider`, role-tint header (`Card_RoleHeaderStrip`), owner disc (`Card_OwnerIndicator`), priority gem (`Card_PriorityIndicator`), status dot (`Card_StatusMarker`), and dynamic `TextMeshPro` labels at `Card_Title_Anchor` and `Card_Subtitle_Anchor`.

### D. Paper, Documents & Approval Props
- All 18 document/handoff prefabs under `Assets/MuseDesk/Art/Prefabs/Documents/` have clean surfaces with no baked text and named anchors (`Content_Surface_Anchor`, `Title_Anchor`, `Stamp_Target_Anchor`, `Grab_Pivot_Anchor`).
- `Doc_ExpandedFrame.prefab` includes physical button pads (`Btn_Approve`, `Btn_Defer`, `Btn_Reject`) and corresponding anchors (`Btn_Approve_Anchor`, `Btn_Defer_Anchor`, `Btn_Reject_Anchor`).

### E. Miniature Software Factory (`SoftwareFactoryVisualController`)
- `SoftwareFactory_Complete.prefab` includes `Animator` (`MuseDesk_Factory.controller`) and `SoftwareFactoryVisualController` exposing all 10 stage animations:
  1. `PlayBlueprintPrint()` (`1_Blueprint_Print`)
  2. `PlayBlueprintTransfer()` (`2_Blueprint_Transfer`)
  3. `PlayArtifactAssemble()` (`3_Artifact_Assemble`)
  4. `PlayDevelopmentComplete()` (`4_Development_Complete`)
  5. `PlayArtifactTransport()` (`5_Artifact_Transport`)
  6. `PlayTestingActivate()` (`6_Testing_Activate`)
  7. `PlayTestSuccess()` (`7_Test_Success`)
  8. `PlayTestFailed()` (`8_Test_Failed`)
  9. `PlayRejectedReturn()` (`9_Rejected_Return`)
  10. `PlayCompletedDelivery()` (`10_Completed_Delivery`)

### F. Global Scene Composition (`MuseDesk_Workspace_Root.prefab`)
- Located at `Assets/MuseDesk/Art/Prefabs/Workspace/MuseDesk_Workspace_Root.prefab`.
- Configured at true Meta Quest 3 desk scale (~1.1m wide x 0.55m deep) with no opaque floor hiding the user's real desk:
  - Left (`x = -0.34m`): `ResearchStation_Group` (`Workstation_Research` + `Agent_Researcher`)
  - Center (`x = 0.00m`): `DeveloperStation_Group` (`Workstation_Developer` + `Agent_Developer`)
  - Right (`x = +0.34m`): `TestingStation_Group` (`Workstation_Testing` + `Agent_Tester`)
  - Back (`z = +0.26m`): `TaskBoard_KanbanFrame` with sample `TaskCard_Grabbable` cards
  - Connecting Line (`z = -0.15m`): `SoftwareFactory_Complete`
  - User Side (`x = -0.22m, z = -0.25m`): `UserDocumentInbox_Zone` (`Doc_InboxTray`, `Doc_ApprovalPaper`, `Doc_ApprovalStamp`, `Doc_SignaturePen`, `Doc_ExpandedFrame`)
  - Back-Right (`x = +0.36m, z = +0.24m`): `CompletedProjectShelf_Zone` (`Decor_ProjectDisplayShelf`, `Decor_Trophy`, `Decor_ProgressAward`, `Doc_DocumentArchive`)
  - Front-Right (`x = +0.30m, z = -0.26m`): `SpatialDiagram_Demo_Layout` (`Frontend → API Gateway → Auth → Database`)
