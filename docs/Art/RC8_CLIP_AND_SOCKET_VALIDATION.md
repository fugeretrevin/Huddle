# MuseDesk RC8 — Clip, Controller, Socket & Skinned Clearance Validation

**Branch:** `art/rc8-final-support` (`4d976fa` + grounded-foot & `.fbx.meta` loop fixes)  
**Measurement Source:** Unity Editor batchmode inspection (`docs/review_screenshots/rc8_validation/rc8_audit_metrics.txt`), **"Editor, not headset"**.

---

## 1. Animator Controllers & Layer Architecture

| Controller | Layers & Default Weights | States per Layer | Parameters | Notes |
| :--- | :--- | :---: | :--- | :--- |
| `Assets/MuseDesk/Art/Controllers/MuseDesk_Agent.controller` | `Base Layer` (`w=0.00`), `UpperBody` (`w=0.00`), `HeadGaze` (`w=0.00`) | 77 / 16 / 7 | `MoveSpeed:Float`, `IsCarryingPaper:Bool` | `AgentAnimatorBridge` drives layer 0 in DIRECT mode; additive/masked layers stay `w=0` unless explicitly blended (no layer fighting). |
| `Assets/MuseDesk/Art/Controllers/WallHabitat/MuseDesk_WallAgent.controller` | `Base Layer` (`w=0.00`), `UpperBody` (`w=0.00`), `HeadGaze` (`w=0.00`) | 77 / 16 / 7 | `MoveSpeed:Float`, `IsCarryingPaper:Bool` | Exact parity with Desk agent controller across all 77 shared states. |

---

## 2. FBX Clip & `loopTime` Importer Verification (Post-Fix)

| FBX Asset | `defaultClipAnimations` | Serialized `clipAnimations` | `metaLoops` | Runtime `isLooping` (`assetLoops`) | Loaded Clips |
| :--- | :---: | :---: | :---: | :---: | :---: |
| `Characters/Agent_Researcher.fbx` | 77 | 77 | 34 | 34 | 77 |
| `Characters/Agent_Developer.fbx` | 77 | 77 | 34 | 34 | 77 |
| `Characters/Agent_Tester.fbx` | 77 | 77 | 34 | 34 | 77 |
| `WallHabitat/Agent_Manager.fbx` | 77 | **77** (was 0) | **34** (was 0) | **34** (was 0) | 77 |
| `WallHabitat/WallHabitat_ElevatorSpine_3Tier.fbx` | 8 | **8** (was 0) | **2** (was 0) | **2** (was 0) | 8 |
| `WallHabitat/WallHabitat_ElevatorSpine_2Tier.fbx` | 6 | **6** (was 0) | **2** (was 0) | **2** (was 0) | 6 |
| `WallHabitat/WallHabitat_ElevatorCar.fbx` | 5 | **5** (was 0) | **2** (was 0) | **2** (was 0) | 5 |
| `WallHabitat/WallHabitat_ElevatorCar_Compact.fbx` | 5 | **5** (was 0) | **2** (was 0) | **2** (was 0) | 5 |
| `WallHabitat/WallHabitat_TaskPrinter.fbx` | 4 | **4** (was 0) | **2** (was 0) | **2** (was 0) | 4 |
| `WallHabitat/WallHabitat_DeskVisitPerch.fbx` | 4 | **4** (was 0) | **2** (was 0) | **2** (was 0) | 4 |
| `WallHabitat/WallHabitat_LargeAgentPortal.fbx` | 4 | **4** (was 0) | **2** (was 0) | **2** (was 0) | 4 |
| `Spatial/SuspendedOfficePod.fbx` | 13 | **13** (was 0) | **4** (was 0) | **4** (was 0) | 13 |
| `Spatial/CeilingMount.fbx` | 4 | **4** (was 0) | **2** (was 0) | **2** (was 0) | 4 |

All 4 character `.fbx.meta` files share the exact 34 looping clips: `Idle_Breathing`, `Idle_Fidget`, `Happy_Idle`, `Walk_Loop`, `Walk_Carry_Paper`, `Seated_Idle`, `Talking`, `Listening`, `Thinking`, `Excited`, `Worried`, `Waving`, `Typing`, `Reading`, `Writing`, `Inspecting`, `Using_Workstation`, `Waiting_For_Input`, `Hold_Document`, `Offer_Document`, `Carry_Package`, `Blocked`, `Cheer`, `Elevator_Queue_Idle`, `Elevator_Ride_Idle`, `DeskVisit_Perch_Idle`, `DeskVisit_Listening`, `DeskVisit_Explain`, `Portal_Converse_Loop`, `Manager_Lookout_Idle`, `Manager_Review_Plan`, `Carry_Suspended_Idle`, `Researcher_Compare_Papers`, `Tester_Note_Take`.

---

## 3. Desk V2 Skinned Clip & Held-Prop Clearance (Post-Fix)

Measured in `MainMRScene.unity` using baked `SkinnedMeshRenderer` vertices against all active static obstacle meshes (excluding the agent's own seated station when inspecting seated clips and ignoring `< 1.2 cm` boot-sole desk contact for lateral gap):

| Choreographed Beat | `footY` (m) | `topY` (m) | Min Skinned Body Gap | Held Prop Clearance |
| :--- | :---: | :---: | :--- | :--- |
| `01_Res_Seated_Reading` | `0.0000` | `0.2334` | `0.0135 m` @ `Nameplate_PlateRim_Art` | — |
| `02a_Res_Exit1_TurnLeft` | `0.0085` | `0.2516` | `0.0804 m` @ `Nameplate_PlateRim_Art` | `HeldPaper` `minGap=0.1258 m` @ `Research_Static_WarmWood` |
| `02b_Res_Exit1_WalkCarry` | `0.0092` | `0.2549` | `0.0995 m` @ `Nameplate_PlateRim_Art` | `HeldPaper` `minGap=0.1258 m` @ `Research_Static_WarmWood` |
| `02c_Res_Exit2_WalkCarry` | **`0.0015`** *(was `-0.0104`)* | `0.2490` | `0.1230 m` @ `Nameplate_PlateRim_Art` | `HeldPaper` `minGap=0.0299 m` @ `Developer_Static_CharcoalVisor` |
| `03a_Res_OfferPresent` | `0.0019` | `0.2373` | `0.0538 m` @ `Developer_Static_CharcoalVisor` | `HeldPaper` `minGap=0.0016 m` @ `Developer_Static_CharcoalVisor` *(P0 scene offset)* |
| `03b_Res_OfferHold` | `0.0011` | `0.2376` | `0.0462 m` @ `Developer_PCTowerWindow` | `HeldPaper` `minGap=0.0016 m` @ `Developer_Static_CharcoalVisor` *(P0 scene offset)* |
| `04a_Res_To_Dev_Give` | `0.0000` | `0.2352` | `0.0309 m` @ `Developer_Static_CharcoalVisor` | `ArtifactToken` `minGap=0.0693 m` @ `Developer_Static_WarmWood` |
| `04b_Dev_Receive` | `0.0000` | `0.2179` | `0.0423 m` @ `Nameplate_HousingInner_Art` | — |
| `05a_Dev_To_Tester_Give` | `0.0000` | `0.2182` | `0.0205 m` @ `Testing_Static_Tester_SlateBlue` | `ArtifactToken` `minGap=0.0557 m` @ `Showcase_Frame` |
| `06_Tester_CompletionCheer` | `0.0039` | `0.2218` | `0.0291 m` @ `Nameplate_PlateRim_Art` | — |

### Full-Cycle Walk & Turn Phase Scan (`t = 0.00, 0.25, 0.50, 0.75`)

| Clip | Before Fix (`minFootY`) | After Fix (`minFootY`) | Status |
| :--- | :---: | :---: | :--- |
| `Walk_Start` | `-0.0146 m` | `0.0000 m` | Grounded |
| `Walk_Loop` | `-0.0085 m` | `+0.0005 m` | Grounded |
| `Walk_Stop` | `-0.0141 m` | `-0.0021 m` | ~2 mm settle @ `t=0.75` |
| `Walk_Carry_Paper` | `-0.0108 m` | `+0.0012 m` | Grounded |
| `Turn_Left` | `-0.0132 m` | `0.0000 m` | Grounded |
| `Turn_Right` | `-0.0132 m` | `0.0000 m` | Grounded |
| `Carry_Package` | `-0.0042 m` | `+0.0018 m` | Grounded |

---

## 4. Wall Habitat, Portal, Desk-Visit Perch & Props Sockets

- **Canonical Character Sockets (14 per character, unit world scale enforced):** `RightHand_Attach`, `LeftHand_Attach`, `Head_Attach`, `Back_Attach`, `Document_Attach`, `DocumentHold`, `DocumentOffer`, `TaskCardHold`, `ArtifactHold`, `HeadLookTarget`, `ConversationFocus`, `FootGroundReference`, `ElevatorPassengerRoot`, `PortalPresentationRoot`.
- **Wall Habitat `Full` (3-Tier):** `Developer.SeatPose` `y = -0.4000 m`, skinned agent bounds `Y = [-0.4000 .. -0.1826]` (flush on deck). `HabitatArtManifest` reports `useHabitatArt=True`, `version=wall-habitat-fit-310e070`.
- **Wall Habitat `Compact` (2-Tier):** `Developer.SeatPose` `y = -0.2600 m`, skinned agent bounds `Y = [-0.2600 .. -0.0426]` (flush on deck).
- **Large Agent Portal (`HighFX` & `LowFX`):** `AgentHomePose` `mountLocalY = 0.0230 m`, scaled (`3.8x`) `Agent_Researcher` bounds `Y = [0.0197 .. 0.9142]` during `Portal_Converse_Loop`.
- **Desk-Visit Perch:** `mountName = WallHabitat_DeskVisitPerch`, `mountY = 0.0000 m`, `Agent_Researcher` bounds `Y = [0.0000 .. 0.2308]` during `DeskVisit_Explain`.
- **Interaction Props (`MainMRScene.unity`):**
  - `DecisionDocument`: `heldScale = 0.22`, `DetailsSheet` `localPos = (0.0000, -0.2080, 0.0040)` (`active=True`).
  - `StampApprove`: `rootLocal = (-0.0360, 0.0140, 0.4150)`, `contactR = 0.050`, `dwell = 0.12`.
  - `StampReject`: `rootLocal = (0.0360, 0.0140, 0.4150)`, `contactR = 0.050`, `dwell = 0.12`.
  - `CompletionReceipt`: `rootLocal = (-0.1500, 0.0498, 0.3700)` (`active=True`).
