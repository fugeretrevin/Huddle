# MuseDesk — Character & Factory Animation Manifest

## 1. Shared Character Animation Library (39 Clips)

All 39 clips are authored at **24 FPS** on the shared **Pipkin Sprite** armature (`Armature`) and baked into each character's `.fbx` export (`Agent_Researcher.fbx`, `Agent_Developer.fbx`, `Agent_Tester.fbx`), as well as wired into `MuseDesk_Agent.controller`.

### A. Basic Movement (9 Clips)
| Clip Name | Frames | Duration | Loop | Description |
|---|---|---|---|---|
| `Idle_Breathing` | 1–48 | 2.00s | Yes | Gentle chest expansion, subtle head tilt, rhythmic antenna sway, natural blink at frame 28 |
| `Idle_Fidget` | 1–60 | 2.50s | Yes | Curious weight shift left-to-right, looks around room, taps mittens together |
| `Happy_Idle` | 1–40 | 1.67s | Yes | Bouncy upbeat rhythm, cheerful arched brows, warm head bob |
| `Walk_Loop` | 1–24 | 1.00s | Yes | In-place contact-pass-contact stride with opposing arm swing, hip bounce, and antenna follow-through |
| `Turn_Left` | 1–20 | 0.83s | No | In-place 90-degree anticipatory pivot step left with head lead |
| `Turn_Right` | 1–20 | 0.83s | No | In-place 90-degree anticipatory pivot step right with head lead |
| `Sit_Down` | 1–24 | 1.00s | No | Drops hips to seated height, bends knees forward, rests hands near desk edge |
| `Seated_Idle` | 1–48 | 2.00s | Yes | Comfortable seated breathing, slight foot swing and head tilt |
| `Stand_Up` | 1–24 | 1.00s | No | Leans forward with anticipation, pushes up from seat to standing idle |

### B. Communication (10 Clips)
| Clip Name | Frames | Duration | Loop | Description |
|---|---|---|---|---|
| `Talking` | 1–36 | 1.50s | Yes | Expressive hand gestures, rhythmic head nods and tilts, lively brow articulation |
| `Listening` | 1–48 | 2.00s | Yes | Attentive forward lean, tilted head, occasional confirming nod |
| `Thinking` | 1–60 | 2.50s | Yes | Right hand raised to chin, left hand supporting elbow, upward gaze, furrowed brow |
| `Confused` | 1–48 | 2.00s | No | Deep quizzical head tilt, asymmetric brow raise, palms shrugged outward |
| `Excited` | 1–32 | 1.33s | Yes | Rapid cheerful bounces, wide eyes, raised fluttering hands |
| `Worried` | 1–40 | 1.67s | Yes | Slight crouch, hands clasped near chest, concerned inward-angled brows, subtle shiver |
| `Pointing` | 1–36 | 1.50s | No | Extends right arm forward/upward toward diagram or board while looking at target |
| `Waving` | 1–36 | 1.50s | Yes | Friendly high right-hand wave with warm body sway |
| `Nodding` | 1–28 | 1.17s | No | Two clear affirmative vertical head nods with cheerful brow lift |
| `Shaking_Head` | 1–28 | 1.17s | No | Clear horizontal head shake with concerned brow |

### C. Working (6 Clips)
| Clip Name | Frames | Duration | Loop | Description |
|---|---|---|---|---|
| `Typing` | 1–24 | 1.00s | Yes | Rapid alternating forearm/hand keystrokes at desk height, focused forward lean, scanning head motion |
| `Reading` | 1–48 | 2.00s | Yes | Both hands holding book/page in front of chest, head scanning left-to-right across lines |
| `Writing` | 1–36 | 1.50s | Yes | Left hand steadies notebook, right hand makes brisk scribbling strokes across page |
| `Inspecting` | 1–48 | 2.00s | Yes | Leans in closely, raises right hand (magnifying glass/loupe pose), squints eyes carefully |
| `Using_Workstation` | 1–48 | 2.00s | Yes | Alternates between left control reach, center adjustment, and right monitor/instrument check |
| `Waiting_For_Input` | 1–48 | 2.00s | Yes | Patient stance with hands clasped in front, gentle expectant upward look toward user |

### D. Object Interactions & Handoffs (8 Clips)
| Clip Name | Frames | Duration | Loop | Description |
|---|---|---|---|---|
| `Pick_Up_Document` | 1–24 | 1.00s | No | Reaches down to desk surface with both hands and lifts document to chest height |
| `Hold_Document` | 1–40 | 1.67s | Yes | Holds document/paper steadily at chest height with gentle breathing |
| `Offer_Document` | 1–40 | 1.67s | Yes | Extends both arms forward and slightly upward toward user (`Document_Attach`), expectant gaze |
| `Receive_Document` | 1–28 | 1.17s | No | Reaches out with open hands, grasps incoming item, brings it to chest with a grateful nod |
| `Put_Document_Down` | 1–24 | 1.00s | No | Lowers held item smoothly onto workstation surface and returns to neutral |
| `Carry_Package` | 1–24 | 1.00s | Yes | In-place walk cycle while upper body cradles a blueprint/package in front of chest |
| `Exchange_Package` | 1–36 | 1.50s | No | Steps forward, extends package to coworker, releases, and nods |
| `Celebrate_Handoff` | 1–36 | 1.50s | No | Happy little hop and high-five/wave gesture after completing a coworker handoff |

### E. Result Reactions (6 Clips)
| Clip Name | Frames | Duration | Loop | Description |
|---|---|---|---|---|
| `Success` | 1–36 | 1.50s | No | Anticipation crouch followed by joyful hop, both arms raised in V-victory pose |
| `Failure` | 1–40 | 1.67s | No | Shoulders slump, head droops down with sad brows, slow defeated sigh |
| `Blocked` | 1–40 | 1.67s | Yes | Crossed/halted arm pose, tapping foot impatiently, looking toward blocker |
| `Error` | 1–32 | 1.33s | No | Startled jolt back, hands to sides of head, rapid shake |
| `Task_Completed` | 1–40 | 1.67s | No | Proud chest puff, satisfied nod, dusts off hands and smiles |
| `Cheer` | 1–36 | 1.50s | Yes | Continuous double-arm pump celebration with rhythmic jumps |

---

## 2. Software Factory & Workstation Animations

Driven deterministically by canonical backend state via `FactoryController` and `Animator` parameters:
- `Blueprint_Generate`: Printer head sweeps across schematic bed and feeds rolled blueprint to output tray.
- `Build_Active`: Developer build chamber rings rotate and manipulator arm solders/assembles artifact cube.
- `Conveyor_Move`: Track rollers spin and carrier platform glides between Research → Dev → Test → Output Shelf.
- `Test_Chamber_Scan`: Tester inspection ring sweeps vertically with diagnostic pulse.
- `Test_Pass`: Green beacon illuminates and gate routes artifact to Completion Shelf.
- `Test_Fail`: Red warning lamp pulses and reject chute routes failure report back toward Developer.
