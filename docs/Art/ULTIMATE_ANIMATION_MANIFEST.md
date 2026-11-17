# MuseDesk — Ultimate Animation Manifest (77 Shared Clips, AvatarMasks & Character Sockets)

This manifest records the implemented Generic animation library, AvatarMasks, AnimatorController layers, and character attachment sockets built inside branch `art/living-world-polish` (worktree `.worktrees/living-world-art`).

All clips are authored in Blender (`ArtSource/Scripts/generate_characters.py`) at 24 fps, exported cleanly onto all 4 mascot FBXs (`Agent_Researcher.fbx`, `Agent_Developer.fbx`, `Agent_Tester.fbx`, `Agent_Manager.fbx`), and configured in Unity via `MuseDeskArtPipeline`, `WallHabitatArtSetup`, and `CharacterMaskAndSocketSetup`. Root bone translation/rotation stays zero-keyed across all clips so NavMesh / coroutine / elevator / carry locomotion owns world displacement cleanly.

---

## 1. Rig & Attachment Sockets (26 Bones + 14 Canonical Sockets)

- **Armature Hierarchy (Generic rig, 26 bones):** `Root`, `Hips`, `Spine`, `Chest`, `Neck`, `Head`, `Eye_L`, `Eye_R`, `Brow_L`, `Brow_R`, `Accessory_Root`, `Accessory_Tip`, `Shoulder_L/R`, `UpperArm_L/R`, `Forearm_L/R`, `Hand_L/R`, `Thigh_L/R`, `Shin_L/R`, `Foot_L/R`.
- **Unit World Scale Enforcement:** Both `MuseDeskArtPipeline` and `CharacterMaskAndSocketSetup` enforce unit `lossyScale` `(1, 1, 1)` on all 14 sockets so attached cards, stamps, and props never skew under parent bone scaling.

| Category | Socket Name | Parent Bone | Blender Offset `(x, y, z)` | Intended Usage |
| :--- | :--- | :--- | :--- | :--- |
| Legacy | `RightHand_Attach` | `Hand_R` | `(-0.047, -0.016, 0.045)` | Right-hand tool/stamp attachment |
| Legacy | `LeftHand_Attach` | `Hand_L` | `(0.047, -0.016, 0.045)` | Left-hand prop attachment |
| Legacy | `Head_Attach` | `Head` | `(0.000, 0.000, 0.194)` | Overhead thought/selection indicator anchor |
| Legacy | `Back_Attach` | `Chest` | `(0.000, 0.040, 0.095)` | Backpack / cape / satchel anchor |
| Legacy | `Document_Attach` | `Chest` | `(0.000, -0.072, 0.092)` | Standard chest document hold anchor |
| Section 16 | `DocumentHold` | `Chest` | `(0.000, -0.056, 0.090)` | Tucked in-arms carrying anchor (`Walk_Carry_Paper`) |
| Section 16 | `DocumentOffer` | `Chest` | `(0.000, -0.088, 0.104)` | Extended presentation anchor (`Offer_Document*`) |
| Section 16 | `TaskCardHold` | `Hand_R` | `(-0.047, -0.020, 0.048)` | Task card / blueprint pickup & delegation card slot |
| Section 16 | `ArtifactHold` | `Chest` | `(0.000, -0.068, 0.086)` | Token / box / factory output carry slot |
| Section 16 | `HeadLookTarget` | `Head` | `(0.000, -0.036, 0.156)` | Eye-height gaze origin (`HeadGaze` layer target) |
| Section 16 | `ConversationFocus` | `Head` | `(0.000, -0.060, 0.155)` | Forward social focus point (`Portal` / `DeskVisit`) |
| Section 16 | `FootGroundReference` | `Hips` | `(0.000, 0.000, 0.000)` | Ground alignment / hover offset reference |
| Section 16 | `ElevatorPassengerRoot` | `Hips` | `(0.000, 0.000, 0.000)` | `ElevatorCarSocket` boarding & ride alignment |
| Section 16 | `PortalPresentationRoot` | `Hips` | `(0.000, 0.000, 0.000)` | Large portal emergence anchor |

---

## 2. AvatarMasks & Controller Layer Architecture

Generated at:
- `Assets/MuseDesk/Art/Controllers/Mask_UpperBody.mask` — Disables `""`, `Armature`, `Armature/Root`, `Armature/Root/Hips`, `Thigh_*`, `Shin_*`, `Foot_*`, and hips sockets; enables `Spine` upward (`Chest`, `Shoulder_*`, `UpperArm_*`, `Forearm_*`, `Hand_*`, `Neck`, `Head`, eyes, brows, accessories, and upper-body/head sockets).
- `Assets/MuseDesk/Art/Controllers/Mask_HeadGaze.mask` — Enables only `Neck`, `Head`, `Eye_*`, `Brow_*`, `Accessory_*`, `Head_Attach`, `HeadLookTarget`, and `ConversationFocus`.

Both `Assets/MuseDesk/Art/Controllers/MuseDesk_Agent.controller` and `Assets/MuseDesk/Art/Controllers/WallHabitat/MuseDesk_WallAgent.controller` preserve their existing `Base Layer` (`MoveSpeed`, `Idle_Breathing` ↔ `Walk_Loop`, and all 77 state names) and add two non-intrusive override layers defaulting to **weight `0.0`** with an empty pass-through default state (`UpperBody_Empty`, `HeadGaze_Empty`):
- **`UpperBody` (`Mask_UpperBody.mask`):** Contains 15 upper-body gesture/hold states (`Hold_Document`, `Offer_Document_Present`, `Offer_Document`, `Offer_Document_Release`, `Carry_Package`, `Waving`, `Pointing`, `React_Wave_Greet`, `React_Assign_Ack`, `React_Interrupt_Listen`, `Manager_Delegate_Point`, `Manager_Acknowledge_Worker`, `Manager_Welcome_User`, `Researcher_Compare_Papers`, `Tester_Note_Take`). Six one-shots return automatically to `UpperBody_Empty`.
- **`HeadGaze` (`Mask_HeadGaze.mask`):** Contains 9 subtle glance/nod/listening states (`React_Glance`, `Nodding`, `Shaking_Head`, `Listening`, `React_Interrupt_Listen`, `DeskVisit_Listening`, `Portal_Converse_Loop`, `Thinking`, `Waiting_For_Input`). Four one-shots return automatically to `HeadGaze_Empty`.

---

## 3. Complete 77-Clip Inventory (24 fps)

### Group A — Basic Movement (12 Clips)
| Clip Name | Frames | Duration (s) | Mode | Purpose |
| :--- | :---: | :---: | :--- | :--- |
| `Idle_Breathing` | 48 | 2.00 | Loop | Restrained breathing, weight shift, blink |
| `Idle_Fidget` | 60 | 2.50 | Loop | Gentle look around and ear/accessory shift |
| `Happy_Idle` | 40 | 1.67 | Loop | Upbeat post-completion idle sway |
| `Walk_Start` | 12 | 0.50 | One-shot | Cushion & push-off from stand into stride |
| `Walk_Loop` | 24 | 1.00 | Loop | Two-step walk cycle with counter-swing |
| `Walk_Stop` | 14 | 0.58 | One-shot* | Stride settle back to rest |
| `Walk_Carry_Paper` | 24 | 1.00 | Loop | Two-handed paper carry stride |
| `Turn_Left` | 20 | 0.83 | One-shot* | 90-degree step-and-pivot left |
| `Turn_Right` | 20 | 0.83 | One-shot* | 90-degree step-and-pivot right |
| `Sit_Down` | 24 | 1.00 | One-shot | Transition onto workstation chair |
| `Seated_Idle` | 48 | 2.00 | Loop | Subtle seated weight/leg/hand motion |
| `Stand_Up` | 24 | 1.00 | One-shot | Stand from chair back to standing rest |

### Group B — Communication & Stamp Reactions (12 Clips)
| Clip Name | Frames | Duration (s) | Mode | Purpose |
| :--- | :---: | :---: | :--- | :--- |
| `Talking` | 36 | 1.50 | Loop | Conversational chest & forearm gestures |
| `Listening` | 48 | 2.00 | Loop | Attentive head tilt + confirmation nod |
| `Thinking` | 60 | 2.50 | Loop | Hand-to-chin contemplation |
| `Confused` | 48 | 2.00 | Loop | Asymmetric brow + shoulder shrug |
| `Excited` | 32 | 1.33 | Loop | Wide eyes & happy bounce |
| `Worried` | 40 | 1.67 | Loop | Tense posture, pulled-back arms |
| `Pointing` | 36 | 1.50 | Loop | Extended directional arm cue |
| `Waving` | 36 | 1.50 | Loop | Friendly open wave |
| `Nodding` | 28 | 1.17 | One-shot | Double agreement nod |
| `Shaking_Head` | 28 | 1.17 | One-shot | Side-to-side head shake |
| `React_Approve` | 30 | 1.25 | One-shot* | Happy hop + fist pump when stamp approves |
| `React_Reject` | 30 | 1.25 | One-shot* | Sagging sigh & head shake when stamp rejects |

### Group C — Working Loops (6 Clips)
| Clip Name | Frames | Duration (s) | Mode | Purpose |
| :--- | :---: | :---: | :--- | :--- |
| `Typing` | 24 | 1.00 | Loop | Alternating keyboard strikes |
| `Reading` | 48 | 2.00 | Loop | Left-to-right document scan |
| `Writing` | 36 | 1.50 | Loop | Focused right-hand writing strokes |
| `Inspecting` | 48 | 2.00 | Loop | Leaning close with magnifying eye squint |
| `Using_Workstation` | 48 | 2.00 | Loop | Dual-monitor / terminal interaction |
| `Waiting_For_Input` | 48 | 2.00 | Loop | Calm upward gaze awaiting user prompt |

### Group D — Document Interactions & Handoffs (12 Clips)
| Clip Name | Frames | Duration (s) | Mode | Purpose |
| :--- | :---: | :---: | :--- | :--- |
| `Pick_Up_Document` | 24 | 1.00 | One-shot | Bend & gather document to `DocumentHold` |
| `Hold_Document` | 40 | 1.67 | Loop | Steady two-handed chest hold |
| `Offer_Document_Present` | 20 | 0.83 | One-shot | "Here you go" dip & forward extension |
| `Offer_Document` | 48 | 2.00 | Loop | Attentive waiting hold while offering paper |
| `Offer_Document_Release` | 20 | 0.83 | One-shot | Warm release & settle when user takes paper |
| `Receive_Document` | 28 | 1.17 | One-shot | Reach out & tuck received document |
| `Put_Document_Down` | 24 | 1.00 | One-shot | Place document cleanly onto surface |
| `Carry_Package` | 24 | 1.00 | Loop | Two-handed box/token carry walk |
| `Exchange_Package` | 36 | 1.50 | One-shot | Courier package transfer beat |
| `Handoff_Give` | 28 | 1.17 | One-shot* | Step-forward right-arm pass to peer |
| `Handoff_Receive` | 28 | 1.17 | One-shot* | Reach & hug-in receive from peer |
| `Celebrate_Handoff` | 36 | 1.50 | One-shot | Small celebration hop post-handoff |

### Group E — Result Reactions (7 Clips)
| Clip Name | Frames | Duration (s) | Mode | Purpose |
| :--- | :---: | :---: | :--- | :--- |
| `Success` | 36 | 1.50 | One-shot | Two-arm celebration hop |
| `React_Completion_Cheer` | 34 | 1.42 | One-shot* | Tester token cheer & happy bounce |
| `Failure` | 40 | 1.67 | One-shot | Bowed head & drooped posture |
| `Blocked` | 40 | 1.67 | Loop | Crossed-arm impatient foot tap |
| `Error` | 32 | 1.33 | One-shot | Startled recoil + rapid head shake |
| `Task_Completed` | 40 | 1.67 | One-shot | Calm satisfied exhale & nod |
| `Cheer` | 36 | 1.50 | Loop | Rhythmic celebratory loop |

### Group F — Wall Habitat, Elevator, Selection, Desk Visit & Portal (16 Clips)
| Clip Name | Frames | Duration (s) | Mode | Purpose |
| :--- | :---: | :---: | :--- | :--- |
| `Elevator_Queue_Idle` | 48 | 2.00 | Loop | Calm wait at landing doors |
| `Elevator_Step_Board` | 24 | 1.00 | One-shot* | Step smoothly over elevator threshold |
| `Elevator_Ride_Idle` | 48 | 2.00 | Loop | Handrail hold + panoramic glance |
| `Elevator_Step_Exit` | 24 | 1.00 | One-shot* | Step out cleanly onto target tier |
| `Select_Highlighted_Wave` | 32 | 1.33 | One-shot | Distance-selection recognition wave |
| `DeskVisit_Perch_Idle` | 48 | 2.00 | Loop | Relaxed stance on miniature desk perch |
| `DeskVisit_Listening` | 40 | 1.67 | Loop | Attentive head tilt toward user |
| `DeskVisit_Explain` | 48 | 2.00 | Loop | Measured two-hand explanation gesture |
| `Portal_Emerge_Step` | 32 | 1.33 | One-shot* | Step forward through portal arch |
| `Portal_Converse_Loop` | 48 | 2.00 | Loop | Natural enlarged-mode dialogue loop |
| `Portal_Return_Step` | 28 | 1.17 | One-shot* | Polite bow/wave, turn 180° & step back |
| `Manager_Lookout_Idle` | 48 | 2.00 | Loop | Calm balcony oversight scan |
| `Manager_Review_Plan` | 48 | 2.00 | Loop | Clipboard review & drafting gesture |
| `Manager_Operate_Printer` | 36 | 1.50 | One-shot | Press printer cue & gather fresh card |
| `Manager_Delegate_Point` | 36 | 1.50 | One-shot | Point clearly toward assigned cubicle bay |
| `Manager_Receive_Report` | 32 | 1.33 | One-shot | Accept status report & nod approval |

### Group G — New Living-World Interaction, Personality & Role Clips (12 Clips)
| Clip Name | Frames | Duration (s) | Mode | Layer / Purpose |
| :--- | :---: | :---: | :--- | :--- |
| `React_Glance` | 22 | 0.92 | One-shot* | Quick curious head glance (`Base` or `HeadGaze`) |
| `React_Poke_Surprise` | 24 | 1.00 | One-shot* | Tactile hop-startle when poked by user |
| `React_Wave_Greet` | 32 | 1.33 | One-shot* | Warm greeting wave (`Base` or `UpperBody`) |
| `React_Interrupt_Listen` | 22 | 0.92 | One-shot* | Turn head cleanly into user interruption (`Base`/`UpperBody`/`HeadGaze`) |
| `React_Assign_Ack` | 20 | 0.83 | One-shot* | Crisply nod & salute confirmation (`Base` or `UpperBody`) |
| `Carry_Suspended_Idle` | 40 | 1.67 | Loop | Weightless gentle leg/arm sway while carried by user |
| `Researcher_Compare_Papers` | 48 | 2.00 | Loop | Left/right document comparison scan (`Base` or `UpperBody`) |
| `Developer_Adjust_Keyboard` | 36 | 1.50 | One-shot* | Re-center wrists & posture at terminal |
| `Tester_Note_Take` | 44 | 1.83 | Loop | Clipboard/stylus checklist stroke (`Base` or `UpperBody`) |
| `Manager_Welcome_User` | 36 | 1.50 | One-shot* | Warm open-arm welcome + studio bow (`Base` or `UpperBody`) |
| `Manager_Acknowledge_Worker` | 28 | 1.17 | One-shot* | Measured side-nod & hand acknowledgment (`Base` or `UpperBody`) |
| `Return_To_Work` | 22 | 0.92 | One-shot* | Settle back toward workstation focus |

*\*Auto-returns automatically to `Idle_Breathing` when played on `Base Layer`.*
