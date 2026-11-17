# MuseDesk — Character & Factory Animation Manifest

## 1. Shared Character Animation Library (65 Base Clips; 77 Total in RC8)

All **65** base clips shown below (plus **12** living-world personality/interaction clips documented in [ULTIMATE_ANIMATION_MANIFEST.md](./ULTIMATE_ANIMATION_MANIFEST.md), for **77** shared clips total) are authored at **24 FPS** on the shared **Pipkin Sprite** 26-bone armature (`Agent_*_ArmatureData`) in `ArtSource/Scripts/generate_characters.py` (`build_shared_animation_library`) and baked identically into all four mascot `.fbx` / `.prefab` assets (`Agent_Researcher`, `Agent_Developer`, `Agent_Tester`, and `Agent_Manager`). Both `Assets/MuseDesk/Art/Controllers/MuseDesk_Agent.controller` and `Assets/MuseDesk/Art/Controllers/WallHabitat/MuseDesk_WallAgent.controller` wire this shared 77-state library.

> [!NOTE]
> Duration shown below is computed as `(end_frame - 1) / 24` seconds (Blender 1-indexed span at 24 FPS) alongside the inclusive Blender keyframe range `1–N`.

### A. Basic Movement & Cornering Locomotion (12 Clips)
| Clip Name | Frames | Duration | Loop | Description |
|---|---|---|---|---|
| `Idle_Breathing` | 1–48 | 1.96s | Yes | Gentle chest expansion, subtle head tilt, rhythmic antenna sway, natural blink at frame 29 |
| `Idle_Fidget` | 1–60 | 2.46s | Yes | Curious weight shift left-to-right, looks around room, taps mittens together |
| `Happy_Idle` | 1–40 | 1.63s | Yes | Bouncy upbeat rhythm, cheerful arched brows, warm head bob |
| `Walk_Start` | 1–12 | 0.46s | No | Push-off weight shift and stride launch into `Walk_Loop` without foot sliding |
| `Walk_Loop` | 1–24 | 0.96s | Yes | In-place contact-pass-contact stride with opposing arm swing, hip bounce, and antenna follow-through |
| `Walk_Stop` | 1–14 | 0.54s | No | Deceleration plant and gentle settle from walk stride back to standing neutral |
| `Walk_Carry_Paper` | 1–24 | 0.96s | Yes | Upper body cradles held paper cleanly in front of chest while lower body executes synchronized walk stride |
| `Turn_Left` | 1–20 | 0.79s | No | Expressive 90° left pivot step with knee lift, head lead, and counter-arm balance |
| `Turn_Right` | 1–20 | 0.79s | No | Expressive 90° right pivot step with knee lift, head lead, and counter-arm balance |
| `Sit_Down` | 1–24 | 0.96s | No | Drops hips to seated height, bends knees forward, rests hands near desk edge |
| `Seated_Idle` | 1–48 | 1.96s | Yes | Comfortable seated breathing, slight foot swing and head tilt |
| `Stand_Up` | 1–24 | 0.96s | No | Leans forward with anticipation, pushes up from seat to standing idle |

### B. Communication & Decision Reactions (12 Clips)
| Clip Name | Frames | Duration | Loop | Description |
|---|---|---|---|---|
| `Talking` | 1–36 | 1.46s | Yes | Expressive hand gestures, rhythmic head nods and tilts, lively brow articulation |
| `Listening` | 1–48 | 1.96s | Yes | Attentive forward lean, tilted head, occasional confirming nod |
| `Thinking` | 1–60 | 2.46s | Yes | Right hand raised to chin, left hand supporting elbow, upward gaze, furrowed brow |
| `Confused` | 1–48 | 1.96s | No | Deep quizzical head tilt, asymmetric brow raise, palms shrugged outward |
| `Excited` | 1–32 | 1.29s | Yes | Rapid cheerful bounces, wide eyes, raised fluttering hands |
| `Worried` | 1–40 | 1.63s | Yes | Slight crouch, hands clasped near chest, concerned inward-angled brows, subtle shiver |
| `Pointing` | 1–36 | 1.46s | No | Extends right arm forward/upward toward diagram or board while looking at target |
| `Waving` | 1–36 | 1.46s | Yes | Friendly high right-hand wave with warm body sway |
| `Nodding` | 1–28 | 1.13s | No | Two clear affirmative vertical head nods with cheerful brow lift |
| `Shaking_Head` | 1–28 | 1.13s | No | Clear horizontal head shake with concerned brow |
| `React_Approve` | 1–30 | 1.21s | No | Quick squash anticipation into celebratory hop and right fist-pump when approve stamp lands |
| `React_Reject` | 1–30 | 1.21s | No | Brief downcast slump, gentle two-beat head shake, and settled recovery when reject stamp lands |

### C. Working (6 Clips)
| Clip Name | Frames | Duration | Loop | Description |
|---|---|---|---|---|
| `Typing` | 1–24 | 0.96s | Yes | Rapid alternating forearm/hand keystrokes at desk height, focused forward lean, scanning head motion |
| `Reading` | 1–48 | 1.96s | Yes | Both hands holding book/page in front of chest, head scanning left-to-right across lines |
| `Writing` | 1–36 | 1.46s | Yes | Left hand steadies notebook, right hand makes brisk scribbling strokes across page |
| `Inspecting` | 1–48 | 1.96s | Yes | Leans in closely, raises right hand (magnifying glass/loupe pose), squints eyes carefully |
| `Using_Workstation` | 1–48 | 1.96s | Yes | Alternates between left control reach, center adjustment, and right monitor/instrument check |
| `Waiting_For_Input` | 1–48 | 1.96s | Yes | Patient stance with hands clasped in front, gentle expectant upward look toward user |

### D. Object Interactions & Coworker Handoffs (12 Clips)
| Clip Name | Frames | Duration | Loop | Description |
|---|---|---|---|---|
| `Pick_Up_Document` | 1–24 | 0.96s | No | Reaches down to desk surface with both hands and lifts document to chest height |
| `Hold_Document` | 1–40 | 1.63s | Yes | Holds document/paper steadily at chest height with gentle breathing |
| `Offer_Document_Present` | 1–20 | 0.79s | No | Crisp "here you go" presentation reach extending paper cleanly toward the user (`Document_Attach`) |
| `Offer_Document` | 1–48 | 1.96s | Yes | Seamless loop holding paper out expectantly with gentle breathing and natural blink at frames 24–30 |
| `Offer_Document_Release` | 1–20 | 0.79s | No | Satisfied little lift and graceful arm return once user takes the paper |
| `Receive_Document` | 1–28 | 1.13s | No | Reaches out with open hands, grasps incoming item, brings it to chest with a grateful nod |
| `Put_Document_Down` | 1–24 | 0.96s | No | Lowers held item smoothly onto workstation surface and returns to neutral |
| `Carry_Package` | 1–24 | 0.96s | Yes | In-place walk cycle while upper body cradles a blueprint/package in front of chest |
| `Exchange_Package` | 1–36 | 1.46s | No | Steps forward, extends package to coworker, releases, and nods |
| `Handoff_Give` | 1–28 | 1.13s | No | Steps toward coworker, extends deliverable cleanly with right hand, then nods on transfer |
| `Handoff_Receive` | 1–28 | 1.13s | No | Reaches forward with both hands, cradles received deliverable to chest with grateful nod |
| `Celebrate_Handoff` | 1–36 | 1.46s | No | Happy little hop and high-five/wave gesture after completing a coworker handoff |

### E. Result Reactions (7 Clips)
| Clip Name | Frames | Duration | Loop | Description |
|---|---|---|---|---|
| `Success` | 1–36 | 1.46s | No | Anticipation crouch followed by joyful hop, both arms raised in V-victory pose |
| `React_Completion_Cheer` | 1–34 | 1.38s | No | Tester/agent celebratory hop and double-arm V-cheer when a Software Factory token spawns |
| `Failure` | 1–40 | 1.63s | No | Shoulders slump, head droops down with sad brows, slow defeated sigh |
| `Blocked` | 1–40 | 1.63s | Yes | Crossed/halted arm pose, tapping foot impatiently, looking toward blocker |
| `Error` | 1–32 | 1.29s | No | Startled jolt back, hands to sides of head, rapid shake |
| `Task_Completed` | 1–40 | 1.63s | No | Proud chest puff, satisfied nod, dusts off hands and smiles |
| `Cheer` | 1–36 | 1.46s | Yes | Continuous double-arm pump celebration with rhythmic jumps |

### F. Wall Habitat, Elevator, Selection, Desk Visit & Large Portal (16 Clips)
| Clip Name | Frames | Duration | Loop | Description |
|---|---|---|---|---|
| `Elevator_Queue_Idle` | 1–48 | 1.96s | Yes | Polite glance toward elevator doors with tiny weight shift at floor stop |
| `Elevator_Step_Board` | 1–24 | 0.96s | No | Two-step forward board over elevator cab threshold |
| `Elevator_Ride_Idle` | 1–48 | 1.96s | Yes | Gentle handrail grip and calm side-glance while cab travels vertically |
| `Elevator_Step_Exit` | 1–24 | 0.96s | No | Confident step out of elevator cab onto tier walkway |
| `Select_Highlighted_Wave` | 1–32 | 1.29s | No | High right-hand acknowledgement wave when highlighted by gaze/ray |
| `DeskVisit_Perch_Idle` | 1–48 | 1.96s | Yes | Attentive idle on temporary `/btw` desk perch |
| `DeskVisit_Listening` | 1–40 | 1.63s | Yes | Subtle forward lean and confirming head nod while user speaks |
| `DeskVisit_Explain` | 1–48 | 1.96s | No | Expressive alternating open-hand explanation on desk perch |
| `Portal_Emerge_Step` | 1–32 | 1.29s | No | Forward emergence step and wave out of the 1.0 m Large Agent Portal |
| `Portal_Converse_Loop` | 1–48 | 1.96s | Yes | Restrained conversational gesture loop with natural blink at scaled portal size |
| `Portal_Return_Step` | 1–28 | 1.13s | No | Whole-body 180° turn via `Hips` (no spine twist) and farewell wave back into portal |
| `Manager_Lookout_Idle` | 1–48 | 1.96s | Yes | Elevated Manager lookout stance surveying lower Wall Habitat tiers |
| `Manager_Review_Plan` | 1–48 | 1.96s | Yes | Holds clipboard in left hand while reviewing task card notes |
| `Manager_Operate_Printer` | 1–36 | 1.46s | No | Turns toward Manager printer, taps control button, and presents printed card |
| `Manager_Delegate_Point` | 1–36 | 1.46s | No | Alternating right/left delegation gesture down toward team bays |
| `Manager_Receive_Report` | 1–32 | 1.29s | No | Two-handed reception and appreciative nod for upward status cards |

---

## 2. Software Factory & Workstation Animations

Driven deterministically by canonical backend state via `FactoryController` and `Animator` parameters:
- `Blueprint_Generate`: Printer head sweeps across schematic bed and feeds rolled blueprint to output tray.
- `Build_Active`: Developer build chamber rings rotate and manipulator arm solders/assembles artifact cube.
- `Conveyor_Move`: Track rollers spin and carrier platform glides between Research → Dev → Test → Output Shelf.
- `Test_Chamber_Scan`: Tester inspection ring sweeps vertically with diagnostic pulse.
- `Test_Pass`: Green beacon illuminates and gate routes artifact to Completion Shelf.
- `Test_Fail`: Red warning lamp pulses and reject chute routes failure report back toward Developer.

---

## 3. Priority 3 Stamp Juice Assets & Impact Squash (`generate_stamp_juice.py`)

| Asset / Clip | File / Controller | Frames / Stats | Notes |
|---|---|---|---|
| `Stamp_Impact_Squash` | `Doc_Stamp_Approve_Polished.prefab`, `Doc_Stamp_Reject_Polished.prefab` (`MuseDesk_Stamp.controller`) | 1–10 (`0.375s` @ 24 FPS), Loop: No | Volume-preserving squash anchored on `Stamp_SquashPivot` at pad bottom (`Y = 0`) so stamp base never clips paper on impact |
| `Doc_StampInkMark_Approve` | `Assets/MuseDesk/Art/Prefabs/Documents/Doc_StampInkMark_Approve.prefab` | 448 verts / 840 tris (opaque `Mat_StampInk_Approve`, queue `2000`) | Crisp sage-green embossed rectangle + check mark + text bars, zero transparent overdraw |
| `Doc_StampInkMark_Reject` | `Assets/MuseDesk/Art/Prefabs/Documents/Doc_StampInkMark_Reject.prefab` | 416 verts / 780 tris (opaque `Mat_StampInk_Reject`, queue `2000`) | Crisp soft-coral embossed rectangle + cross + text bars, zero transparent overdraw |

---

## 4. Wall Habitat Mechanical & Portal Controllers (`Assets/MuseDesk/Art/Controllers/WallHabitat/`)

| Controller | Driven Rigged Prefabs | Clips Wired |
|---|---|---|
| `MuseDesk_WallElevatorSpine_3Tier.controller` | `WallHabitat_ElevatorSpine_3Tier.prefab` | `ElevatorDoors_Idle_Closed`, `ElevatorDoors_Open_Lower`, `ElevatorDoors_Close_Lower`, `ElevatorDoors_Open_Mid`, `ElevatorDoors_Close_Mid`, `ElevatorDoors_Open_Manager`, `ElevatorDoors_Close_Manager`, `ElevatorWinch_Spin` |
| `MuseDesk_WallElevatorSpine_2Tier.controller` | `WallHabitat_ElevatorSpine_2Tier.prefab` | `ElevatorDoors_Idle_Closed`, `ElevatorDoors_Open_Lower`, `ElevatorDoors_Close_Lower`, `ElevatorDoors_Open_Manager`, `ElevatorDoors_Close_Manager`, `ElevatorWinch_Spin` |
| `MuseDesk_WallElevatorCar.controller` | `WallHabitat_ElevatorCar.prefab` | `Cabin_Idle_Closed`, `Cabin_Gate_Open`, `Cabin_Gate_Close`, `Cabin_Travel_Bob`, `Cabin_Arrive_Chime` |
| `MuseDesk_WallTaskPrinter.controller` | `WallHabitat_TaskPrinter.prefab` | `Printer_Idle`, `Printer_Print_TaskCard`, `Printer_Dispatch_Card`, `Printer_Queue_Pulse` |
| `MuseDesk_WallDeskPerch.controller` | `WallHabitat_DeskVisitPerch.prefab` | `Perch_Deploy`, `Perch_Active_Idle`, `Perch_Conversing_Idle`, `Perch_Dismiss` |
| `MuseDesk_WallLargePortal.controller` | `WallHabitat_LargeAgentPortal.prefab` | `Portal_Hidden_Idle`, `Portal_Open_Expand`, `Portal_Active_Idle`, `Portal_Close_Collapse` |
| `MuseDesk_WallAgent.controller` | `Agent_Manager.prefab` | Full 77-clip shared character library + `IsCarryingPaper` & Wall/Portal/Elevator parameters |
