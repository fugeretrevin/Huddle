# MuseDesk — Art Direction & Visual Design Guide

## 1. Creative Vision: "Pipkin Sprites" — Miniature Desktop Craft Spirits

MuseDesk's coworkers belong to an original species of miniature technological craft spirits called **Pipkin Sprites**. Designed specifically for mixed-reality passthrough on Meta Quest 3, they blend the tactile warmth of Designer Vinyl / Matte Ceramic collectibles with cozy woven textiles and precision miniature laboratory hardware.

### Core Design Pillars
1. **Original Commercial Silhouette**:
   - **Soft Pear-Pod Torso**: Rounded, bottom-heavy organic body with a low center of gravity that feels stable and huggable on a real physical desk.
   - **Oversized Expressive Head**: Slightly wider than tall, with subtle sculpted cheek puffs and a recessed dark charcoal **Visor-Cradle Faceplate** that frames high-contrast luminous eyes and animated floating eyebrows.
   - **Chunky Articulated Limbs**: Tapered, readable arms ending in rounded two-part mitten hands (thumb + palm pad) and sturdy short legs with wide, planted boots so limbs never disappear against noisy passthrough backgrounds.
   - **Signature Botanical-Tech Ornaments**: Every sprite has a kinetic dorsal pack and a role-specific head ornament driven by secondary skeletal bones (`Accessory_Root` / `Accessory_Tip`).
2. **Readable at Tabletop Scale (18–24 cm)**:
   - Proportions: ~45% head, ~38% torso, ~17% legs, total height **0.21 m (21 cm)** in Unity world units.
   - High-contrast facial reads: Glowing cream/cyan/amber eyes and tilted brow plates against a deep charcoal face inset make emotional states (Focused, Thinking, Confused, Excited, Worried, Error) immediately legible from 0.4–1.0 m seated viewing distance.

---

## 2. Color Palette & Material System

All assets share a cohesive, Quest-optimizedURP Lit material palette with subtle roughness variation (matte ceramic bodies, satin metal accents, warm natural wood desks, soft cream paper):

| Material Name | Base Color (Hex) | Roughness | Metallic | Usage |
|---|---|---|---|---|
| `Mat_CreamCeramic` | `#F4EFE6` | `0.35` | `0.00` | Shared sprite body shell, paper highlights, architectural bases |
| `Mat_CharcoalVisor` | `#1E2026` | `0.20` | `0.10` | Recessed facial visor, screen bezels, mechanical joints, boots |
| `Mat_EyeGlow` | `#FFFDF5` (Emissive) | `0.10` | `0.00` | Expressive eyes, status indicators |
| `Mat_Researcher_Lavender` | `#8E7CC3` | `0.45` | `0.00` | Researcher cloak/collar, tunic trim, notebook bindings |
| `Mat_Researcher_Teal` | `#4DB6AC` | `0.35` | `0.05` | Researcher leaf-antenna ornament, magnifying lens frame, accents |
| `Mat_Developer_Coral` | `#FF7F66` | `0.40` | `0.00` | Developer hoodie collar, headphones shell, build station accents |
| `Mat_Developer_Amber` | `#FFD166` | `0.30` | `0.10` | Developer antenna core, keycap highlights, active code indicators |
| `Mat_Tester_Mint` | `#56C596` | `0.38` | `0.00` | Tester utility vest, inspection visor rim, pass-tray accents |
| `Mat_Tester_SlateBlue` | `#4A7C9B` | `0.35` | `0.15` | Tester precision chassis, caliper accents, test bench trim |
| `Mat_WarmWood` | `#C89D7C` | `0.55` | `0.00` | Miniature desks, bookshelves, board frame, architectural bases |
| `Mat_DarkWood` | `#7A563C` | `0.60` | `0.00` | Desk legs, shelf frames, clipboard backing, stamp handle |
| `Mat_BrushedMetal` | `#9EA7B0` | `0.30` | `0.65` | Factory tracks, robotic manipulator, lamp arms, caliper blades |
| `Mat_PaperWarm` | `#FAF6EC` | `0.65` | `0.00` | Grabbable documents, scrolls, folded notes, task cards |
| `Mat_BlueprintBlue` | `#2B5B84` | `0.50` | `0.00` | Architecture blueprints, schematic tubes, planning rolls |
| `Mat_StatusGreen` | `#38C172` (Emissive) | `0.25` | `0.00` | Passed-test lamps, approval stamps, factory completion indicators |
| `Mat_StatusRed` | `#E3342F` (Emissive) | `0.25` | `0.00` | Failed-test lamps, error indicators, rejection markers |
| `Mat_StatusAmber` | `#F6993F` (Emissive) | `0.25` | `0.00` | In-progress / awaiting-input beacons, queue indicators |

---

## 3. Character Profiles

### Character A — Research Agent (`Agent_Researcher`)
- **Silhouette**: Rounded scholar cap/beret + curved twin-leaf kinetic sprout antenna on top of the head + brass-rimmed round reading spectacles perched on the visor + cozy pleated lavender scholar capelet + scroll satchel on back.
- **Primary Palette**: Warm Cream (`#F4EFE6`), Soft Lavender (`#8E7CC3`), Muted Teal (`#4DB6AC`), Warm Brass (`#D4AF37`).

### Character B — Developer Agent (`Agent_Developer`)
- **Silhouette**: Sculpted crest-hoodie head contour + oversized cushioned studio headphones with a curved mic boom + dual angled cyber-ear fins + coral tech-vest with front pocket + miniature battery-core backpack.
- **Primary Palette**: Warm Cream (`#F4EFE6`), Warm Coral (`#FF7F66`), Sunny Amber (`#FFD166`), Charcoal (`#1E2026`).

### Character C — Testing Agent (`Agent_Tester`)
- **Silhouette**: Precision inspector helmet-band with flip-down magnifying loupe visor + twin calibration radar dishes on temples + structured mint/slate utility harness + diagnostic pack on back.
- **Primary Palette**: Warm Cream (`#F4EFE6`), Mint Green (`#56C596`), Muted Slate Blue (`#4A7C9B`), Brushed Metal (`#9EA7B0`).

---

## 4. Technical Budgets for Meta Quest 3 Passthrough

- **Characters**: ~2,200–4,200 triangles per character; 1 SkinnedMeshRenderer per character sharing palette materials.
- **Workstations**: ~3,500–7,500 triangles per assembled station.
- **Props & Documents**: ~150–1,200 triangles per prop; crisp beveled edges with smooth normals for readable specular highlights without high-res textures.
- **Coordinate System**: Meter scale (`1.0 unit = 1 meter`), Y-up, +Z forward in Unity.

