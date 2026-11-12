# MuseDesk — Character Rig & Attachment Specification

## 1. Shared Armature Architecture

All three MuseDesk characters (`Agent_Researcher`, `Agent_Developer`, `Agent_Tester`) use an identical, Unity-compatible **Generic** skeletal hierarchy (`Armature`) so all 39 animation clips in the shared library play across every agent without retargeting errors.

### Bone Hierarchy (`22 Deforming Bones + 5 Attachment Nodes`)

```text
Armature
└── Root                        (0, 0, 0)         - Ground-level character root (in-place locomotion)
    └── Hips                    (0, 0.048, 0)     - Center of mass / pelvic squash-and-stretch driver
        ├── Spine               (0, 0.072, 0)     - Lower torso bend & breathing
        │   └── Chest           (0, 0.098, 0)     - Upper torso, collar, and backpack mount
        │       ├── Neck        (0, 0.120, 0)     - Head pivot & expressive head tilts
        │       │   └── Head    (0, 0.132, 0)     - Main head shell & visor cradle
        │       │       ├── Eye_L          (-0.018, 0.150, 0.038) - Left eye blink/scale/look
        │       │       ├── Eye_R          ( 0.018, 0.150, 0.038) - Right eye blink/scale/look
        │       │       ├── Brow_L         (-0.019, 0.165, 0.040) - Left eyebrow emotion angle/height
        │       │       ├── Brow_R         ( 0.019, 0.165, 0.040) - Right eyebrow emotion angle/height
        │       │       └── Accessory_Root ( 0.000, 0.192, 0.000) - Base of head ornament / visor / antenna
        │       │           └── Accessory_Tip (0.000, 0.218, 0.005) - Secondary follow-through tip
        │       ├── Shoulder_L  (-0.034, 0.108, 0)
        │       │   └── UpperArm_L (-0.042, 0.104, 0)
        │       │       └── Forearm_L (-0.046, 0.078, 0)
        │       │           └── Hand_L (-0.048, 0.056, 0.006)
        │       └── Shoulder_R  ( 0.034, 0.108, 0)
        │           └── UpperArm_R ( 0.042, 0.104, 0)
        │               └── Forearm_R ( 0.046, 0.078, 0)
        │                   └── Hand_R ( 0.048, 0.056, 0.006)
        ├── Thigh_L             (-0.020, 0.042, 0)
        │   └── Shin_L          (-0.020, 0.022, 0)
        │       └── Foot_L      (-0.020, 0.006, 0.008)
        └── Thigh_R             ( 0.020, 0.042, 0)
            └── Shin_R          ( 0.020, 0.022, 0)
                └── Foot_R      ( 0.020, 0.006, 0.008)
```

---

## 2. Mandatory Prop & Document Attachment Transforms

Every character prefab exposes five standardized attachment transforms parented to their corresponding skeletal bones:

| Attachment Name | Parent Bone | Local Offset (Meters) | Purpose |
|---|---|---|---|
| `RightHand_Attach` | `Hand_R` | `(0.000, -0.010, 0.012)` | Pens, magnifying glass, testing wand, small tools |
| `LeftHand_Attach` | `Hand_L` | `(0.000, -0.010, 0.012)` | Notebooks, clipboards, secondary held props |
| `Document_Attach` | `Chest` (or `Hand_R` bridge) | `(0.000, 0.005, 0.075)` | Offered decision papers (`HeldPaperController.agentHoldPoint`), carried blueprints, packages |
| `Head_Attach` | `Head` | `(0.000, 0.055, 0.000)` | Status halos, hats, focus-mode badges |
| `Back_Attach` | `Chest` | `(0.000, -0.005, -0.042)` | Carried scroll tubes, battery packs, toolkits |

---

## 3. Compatibility with Existing MuseDesk Runtime Components

To preserve 100% compatibility with existing Phase 1 and Phase 2 scripts without breaking `AgentView`, `AgentMovement`, `AgentTilt`, `AgentArmNudge`, `RubberBandReturn`, or `HeldPaperController`:

1. **`AgentView.bob`**: Points to the `VisualRoot` / `Bob` container holding the character's `Armature` and `SkinnedMeshRenderer`, allowing `AgentTilt` and subtle procedural layering to compose cleanly.
2. **`AgentAnimatorBridge`**: Reads `AgentView`'s state (`idle`, `researching`, `coding`, `testing`, `reporting`, `approaching`, `waiting`, `complete`, `error`), `AgentMovement.IsMoving`, and `HeldPaperController` / handoff state to drive the Unity `Animator` parameters (`StateId`, `IsMoving`, `IsCarrying`, `IsOffering`, `TriggerSuccess`, `TriggerFailure`, `TriggerHandoff`, etc.) while keeping `AgentView` as the single reducer subscriber.
3. **`PaperHoldPoint` & `Document_Attach`**: Both exist on the character so `HeldPaperController.agentHoldPoint` can bind to `Document_Attach` (or `PaperHoldPoint` aliased at the same pose).

