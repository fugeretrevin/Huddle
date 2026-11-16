# MuseDesk RC8 — Editor Scene Render Cost & VFX Budget Audit

**Branch:** `art/rc8-final-support` (`4d976fa`)  
**Measurement Method:** Active subtree renderer / material / triangle / particle inspection in `MainMRScene.unity` (Unity Editor).  
**Important:** These are **Editor scene structural counts**, not headset GPU timings; do not extrapolate Editor counts to Quest 3 millisecond frame times. Live headset profiling must still be captured on-device in RC8 / RC9.

---

## 1. Active Scene Subtree Render Cost Comparison (`Editor, not headset`)

| Configuration Measured | Active Renderers | Breakdown (`Mesh / Skinned / TMP`) | Unique Materials | Transparent Queues (`>= 3000`) | Triangles | Particle Systems | `maxParticles` Sum |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| `Desk Idle (Workspace + NearUserRig)` | 317 | `230 / 4 / 79` | 85 | 83 *(mostly TMP/UI)* | 79,370 | 4 | 28 |
| `Desk + Floor Platform` | 322 | `232 / 6 / 80` | 86 | 84 | 92,662 | 4 | 28 |
| `Desk + Ceiling Pod` | 318 | `230 / 5 / 79` | 86 | 83 | 88,654 | 4 | 28 |
| `Wall_Complete_3Tier` (isolated prefab) | 104 | `86 / 16 / 0` | 39 | 2 | 85,886 | 2 | 28 |
| `Wall_Complete_2Tier` (isolated prefab) | 102 | `84 / 16 / 0` | 39 | 2 | 79,910 | 2 | 28 |
| `Desk + Wall_Complete_3Tier + NearUserRig` | 421 | `316 / 20 / 79` | 88 | 85 | 165,256 | 6 | 56 |
| `LargePortal_HighFX` (isolated prefab) | 2 | `0 / 1 / 0` | 8 | 2 | 6,144 | 1 | 14 |
| `LargePortal_LowFX` (isolated prefab) | 2 | `0 / 1 / 0` | 8 | 2 | 6,144 | 1 | 8 |

---

## 2. ReleaseConfig VFX Gate Inventory (`Assets/MuseDesk/Resources/ReleaseConfig.asset`)

Active `rc8-living-office` configuration flags verified in Editor:

- `floorMode = True`
- `ceilingMode = True`
- `ceilingParticles = True`
- `ceilingPortalCone = True`
- `wallHabitat = True`
- `wallManager = True`
- `largeAgentPortal = True`
- `wallElevatorVfx = True`
- `reducedMotion = False`

**Open Runtime Note for Engineering:** `WallHabitatElevatorVisualController.arrivalParticles` (`Assets/MuseDesk/Scripts/Wall/`) is not currently gated by `ReleaseConfig.wallElevatorVfx` (left untouched since `Scripts/**` is outside art scope).

---

## 3. Observations & Quest Optimization Notes

1. **No Transparency Creep in 3D Art:** Excluding `TMP_Text` / UI quads (`tmp = 79`), the Wall Habitat 3-Tier & 2-Tier prefabs each have only `2` transparent queue materials across `104` / `102` active renderers (`Mat_WindowGlass` / elevator glass), matching RC6/RC7 overdraw discipline.
2. **Low-FX Portal Variant Ready:** `WallHabitat_LargePortal_LowFx.prefab` preserves silhouette and animation (`6,144` tris, `2` renderers) while capping particle budget (`maxParticles = 8` vs `14` on `WallHabitat_LargeAgentPortal.prefab`).
3. **Future Batching Candidate (Non-Blocking for RC8):** `Wall_Complete_3Tier` adds `104` active renderers (`86` static mesh, `16` skinned) with `39` shared materials; if Quest 3 wall-habitat draw calls ever require tightening post-RC8, merging non-animated per-cubicle timber trims is a low-risk win without changing sockets or rigs.
