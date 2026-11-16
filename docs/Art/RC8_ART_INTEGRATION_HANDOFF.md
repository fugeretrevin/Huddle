# MuseDesk RC8 — Art Integration Handoff & Validation Gallery

**Source Branch:** `art/rc8-final-support` (branched from `4d976fa` `feature/rc8-feedback-1`, parent `rc8` `911465e`)  
**Scope Guarantee:** Zero edits to `MainMRScene.unity`, `Scripts/**`, `backend/`, or `ProjectSettings/`. All existing character rigs, 14 canonical sockets, 77 shared clips, 3-layer AnimatorControllers, matte role trim palette, and refitted `0 cm` Floor/Ceiling decks are preserved.

---

## 1. Delivered Art & Meta Changes (`art/rc8-final-support`)

### Blender Source & Character FBX Updates (Grounded Walk / Turn / Carry Feet)
- [generate_characters.py](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/ArtSource/Scripts/generate_characters.py)
- `ArtSource/Blender/Characters/Agent_Researcher.blend`, `Agent_Developer.blend`, `Agent_Tester.blend`
- `ArtSource/Blender/WallHabitat/Agent_Manager.blend`
- `Assets/MuseDesk/Art/Models/Characters/Agent_Researcher.fbx`, `Agent_Developer.fbx`, `Agent_Tester.fbx`
- `Assets/MuseDesk/Art/Models/WallHabitat/Agent_Manager.fbx`

### Unity Importer `.fbx.meta` Loop Serialization (`loopTime` Fix)
- `Assets/MuseDesk/Art/Models/WallHabitat/Agent_Manager.fbx.meta` (`0 -> 77` clips, `0 -> 34` loops)
- `WallHabitat_ElevatorSpine_3Tier.fbx.meta` (`8` clips, `2` loops)
- `WallHabitat_ElevatorSpine_2Tier.fbx.meta` (`6` clips, `2` loops)
- `WallHabitat_ElevatorCar.fbx.meta` (`5` clips, `2` loops)
- `WallHabitat_ElevatorCar_Compact.fbx.meta` (`5` clips, `2` loops)
- `WallHabitat_TaskPrinter.fbx.meta` (`4` clips, `2` loops)
- `WallHabitat_DeskVisitPerch.fbx.meta` (`4` clips, `2` loops)
- `WallHabitat_LargeAgentPortal.fbx.meta` (`4` clips, `2` loops)
- `Spatial/SuspendedOfficePod.fbx.meta` (`13` clips, `4` loops)
- `Spatial/CeilingMount.fbx.meta` (`4` clips, `2` loops)

### Art Documentation & Audit Reports
- [ANIMATION_MANIFEST.md](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/Art/ANIMATION_MANIFEST.md) (updated stale `65` -> `77` clip cross-reference)
- [RC8_FINAL_ART_ISSUES.md](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/Art/RC8_FINAL_ART_ISSUES.md)
- [RC8_CLIP_AND_SOCKET_VALIDATION.md](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/Art/RC8_CLIP_AND_SOCKET_VALIDATION.md)
- [RC8_RENDER_COST_AUDIT.md](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/Art/RC8_RENDER_COST_AUDIT.md)
- [rc8_audit_metrics.txt](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/rc8_audit_metrics.txt)

---

## 2. Open Engineering Action Items (Outside Art Scope)

1. **[P0 Scene Binding — `MainMRScene.unity`]:** `HeldPaperController.agentHoldPoint` currently binds `PaperHoldPoint` (`{fileID: 203000318}`) at `localPosition: {x: 0.055, y: 0.125, z: 0.13}` under `Agent_Researcher`. This shifts `DecisionDocument` `+5.5 cm` laterally left of Researcher's outstretched hands in `Offer_Document*` (`0.0016 m` from Developer's PC tower). Update your guarded scene builder to center X (`{x: 0, y: 0.105, z: 0.085}`) or bind `DocumentOffer` / `Document_Attach`.
2. **[P2 VFX Gate — `Scripts/Wall/`]:** Gate `WallHabitatElevatorVisualController.arrivalParticles` with `ReleaseConfig.wallElevatorVfx`.

---

## 3. Verification Status: Editor vs. Worn Headset

- **Unity Editor Validation Gallery (`Editor, not headset`):** Captured after all Blender & `.fbx.meta` fixes.
- **EditMode Suite (`Editor, not headset`):** `unity test --mode EditMode` passed **1367 / 1367** tests (`0` failed, `0` skipped, `7.15 s`), including `AgentClipLoopFlagTests`, `AgentRootMotionGuardTests`, and `RoleColourTests`.
- **Quest 3 Worn QA:** **NOT WORN IN THIS SESSION.** `Art` branch delivers verified assets & Editor evidence only; headset confirmation on RC8/RC9 rests with Quest test owner.

---

## 4. Validation Gallery Links (`Editor, not headset` — Never Headset Captures)

### Desk V2 Skinned Clips & Held Props (`Editor, not headset`)
- [desk_01_seat_reading_eye.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/desk_v2/desk_01_seat_reading_eye.png) — Seated reading/typing/inspecting (`Editor, not headset`)
- [desk_02_res_exit_walk_side.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/desk_v2/desk_02_res_exit_walk_side.png) — Researcher `Walk_Carry_Paper` grounded at `footY=+0.0015 m` (`Editor, not headset`)
- [desk_02_res_exit_walk_plan.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/desk_v2/desk_02_res_exit_walk_plan.png) — Orthographic aisle plan view (`Editor, not headset`)
- [desk_03_res_offer_eye.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/desk_v2/desk_03_res_offer_eye.png) — Shows `PaperHoldPoint` `+5.5 cm` scene offset (`Editor, not headset`)
- [desk_03_res_offer_side.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/desk_v2/desk_03_res_offer_side.png) — `Offer_Document` side profile (`Editor, not headset`)
- [desk_04_handoff_res_to_dev.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/desk_v2/desk_04_handoff_res_to_dev.png) — Researcher -> Developer handoff (`Editor, not headset`)
- [desk_05_handoff_dev_to_tester.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/desk_v2/desk_05_handoff_dev_to_tester.png) — Developer -> Tester handoff (`Editor, not headset`)
- [desk_06_tester_cheer.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/desk_v2/desk_06_tester_cheer.png) — Tester `React_Completion_Cheer` (`Editor, not headset`)

### Interaction Props (`Editor, not headset`)
- [inter_01_stamps_and_trays.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/interaction/inter_01_stamps_and_trays.png) — Stamps, trays & receipt (`Editor, not headset`)
- [inter_02_decision_paper_close.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/interaction/inter_02_decision_paper_close.png) — `DecisionDocument` + `DetailsSheet` (`Editor, not headset`)

### Wall Habitat Full & Compact Elevator (`Editor, not headset`)
- [wall_full_wide.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/wall_elevator/wall_full_wide.png) (`Editor, not headset`)
- [wall_full_elevator_ride_side.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/wall_elevator/wall_full_elevator_ride_side.png) (`Editor, not headset`)
- [wall_full_manager_doors_open.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/wall_elevator/wall_full_manager_doors_open.png) (`Editor, not headset`)
- [wall_compact_wide.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/wall_elevator/wall_compact_wide.png) (`Editor, not headset`)
- [wall_compact_elevator_ride_side.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/wall_elevator/wall_compact_elevator_ride_side.png) (`Editor, not headset`)
- [wall_compact_manager_doors_open.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/wall_elevator/wall_compact_manager_doors_open.png) (`Editor, not headset`)

### Large Portal, Desk-Visit Perch, Ceiling/Floor & Role Trim Contrast (`Editor, not headset`)
- [portal_high_vs_lowfx.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/portal_visit/portal_high_vs_lowfx.png) (`Editor, not headset`)
- [desk_visit_perch.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/portal_visit/desk_visit_perch.png) (`Editor, not headset`)
- [floor_deployed_overview.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/ceiling_floor/floor_deployed_overview.png) (`Editor, not headset`)
- [ceiling_deployed_eye.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/ceiling_floor/ceiling_deployed_eye.png) (`Editor, not headset`)
- [ceiling_stowed_wings.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/ceiling_floor/ceiling_stowed_wings.png) (`Editor, not headset`)
- [palette_dark_passthrough.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/palette_passthrough/palette_dark_passthrough.png) (`Editor, not headset`)
- [palette_light_passthrough.png](file:///C:/dev/MuseDesk/MuseDesk/.worktrees/deck-refit/docs/review_screenshots/rc8_validation/palette_passthrough/palette_light_passthrough.png) (`Editor, not headset`)

---

## 5. Reversible Cherry-Pick / Merge Note

All art and documentation changes live exclusively on `art/rc8-final-support` (based on `4d976fa`). To roll back cleanly, reset or omit this branch; no code, scene, or ProjectSettings commits were touched.
