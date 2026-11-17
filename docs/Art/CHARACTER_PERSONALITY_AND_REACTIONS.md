# MuseDesk — Character Personality & Reactions Guide

This document defines the distinct visual roles, behavioral readability, partial-body layering, and restrained reaction vocabulary across the four Pipkin-Sprite mascots.

---

## 1. Cast & Measured Geometry

| Mascot | Prefab Path | Triangles | Bones | Canonical Role Palette & Silhouette |
| :--- | :--- | :---: | :---: | :--- |
| **Researcher** | `Assets/MuseDesk/Art/Prefabs/Characters/Agent_Researcher.prefab` | 6,010 | 26 | Lavender beret + brass round spectacles + dual leaf sprout; curious, analytical cadence |
| **Developer** | `Assets/MuseDesk/Art/Prefabs/Characters/Agent_Developer.prefab` | 5,892 | 26 | Muted Coral vest + over-ear studio headphones + boom mic + golden visor antenna; brisk builder cadence |
| **Tester** | `Assets/MuseDesk/Art/Prefabs/Characters/Agent_Tester.prefab` | 5,568 | 26 | Mint-Teal tunic + hinged cyan inspector goggles + slate notepad accent; vigilant inspector cadence |
| **Manager** | `Assets/MuseDesk/Art/Prefabs/WallHabitat/Agent_Manager.prefab` | 6,520 | 26 | Slate-Blue tailored waistcoat + brass compass belt + peaked cap + single brass monocle + portfolio backpack; composed studio coordinator cadence |

Visual validation scene & render:
- Scene: `Assets/MuseDesk/Scenes/ArtGalleries/Gallery_01_Characters_And_Personalities.unity`
- Render: `Screenshots/UltimatePolish/01_Gallery_Characters_Personalities.png`

---

## 2. Personality-Driven Role & Living-World Clips (12 New Clips)

Every character rig shares all 77 clips (`65` core + `12` living-world clips) cleanly so engineering can swap roles or trigger cross-role reactions without missing clip lookups:

- **User Proximity & Social Beats:**
  - `React_Glance` (`0.92s` auto-return): Subtle chest/head turn + widening eye accent (`1.06x`), ideal for subtle awareness when the user gazes or walks near a workspace.
  - `React_Wave_Greet` (`1.33s` auto-return): Cheerful studio wave without exaggerated bouncing.
  - `React_Poke_Surprise` (`1.00s` auto-return): Small crouched recoil (`Hips y = -5 mm`), startled eye widen (`1.22x`), brief hop (`+11 mm`), and quick composed settle.
  - `React_Interrupt_Listen` (`0.92s` auto-return): Crisp head turn & attentive brow lift when the user interrupts mid-task.
  - `React_Assign_Ack` (`0.83s` auto-return): Clean forward nod and right-hand confirm tap.
  - `Carry_Suspended_Idle` (`1.67s` loop): Gentle weightless foot/arm/accessory sway when an agent is lifted or relocated by the user.
  - `Return_To_Work` (`0.92s` auto-return): Measured forward bow and hand-to-work return beat.

- **Role-Distinct Work Signatures:**
  - `Researcher_Compare_Papers` (`2.00s` loop): Shifts focus left-to-right between two held documents with raised-brow scrutiny cues.
  - `Developer_Adjust_Keyboard` (`1.50s` auto-return): Slight crouch/re-center with brisk left-right wrist alignment tap.
  - `Tester_Note_Take` (`1.83s` loop): Left-hand pad hold paired with right-hand checklist ticks and micro head-up check.
  - `Manager_Welcome_User` (`1.50s` auto-return): Open-arm welcome gesture with courteous studio bow.
  - `Manager_Acknowledge_Worker` (`1.17s` auto-return): Coordinator glance downward/sideward paired with calm confirming hand beat.

---

## 3. Partial-Body Layering (`UpperBody` & `HeadGaze`)

To let runtime (`AgentAnimatorBridge` / procedural motion) layer subtle glance or hand gestures on top of walking, sitting, or `Idle_Breathing` without jarring torso snaps:
- Both `MuseDesk_Agent.controller` and `MuseDesk_WallAgent.controller` expose:
  - Layer `1`: `UpperBody` masked by `Assets/MuseDesk/Art/Controllers/Mask_UpperBody.mask` (default weight `0f`).
  - Layer `2`: `HeadGaze` masked by `Assets/MuseDesk/Art/Controllers/Mask_HeadGaze.mask` (default weight `0f`).
- **Safety guarantee:** Because default weights are `0f`, existing base-layer state driving (`Idle_Breathing` ↔ `Walk_Loop` via `MoveSpeed`, `PlayOffer()`, `PlaySuccess()`, etc.) behaves identically until runtime explicitly raises layer weight and crossfades a state on layer `1` or `2`.
