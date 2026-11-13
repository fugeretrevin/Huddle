# Art adoption notes (Office-Art, final refinement)

2026-11-13. Builder: `AgentScripts/BuildOfficeArtV3.cs` (`BuildOfficeArtV3.Apply`, `BuildOfficeArtV3.Revert`).

**Evidence level.** Written from source only. Not compiled, not run. Every decision below is a prediction from the generator and builder numbers; the builder measures the real meshes at run time and its printed table is the authority.

## What the builder does

For each element it measures the current surface and Antigravity's model (mesh triangles, in the element's own space, so objects hidden by scale are measured correctly), decides, and prints one table row: element, old size, new size, scale applied, surface offset, decision, reason.

An adopted element gets a child `<Element>_ArtBody`: the prefab, unpacked, with every text, key, script and collider removed (the preview prefabs carry sample texts and `UI_PokeKey_*` instances). The old renderer is disabled. The old object, its collider, scripts, texts and keys are not touched. Body mesh parts are renamed with the suffix `_Art` so a later `BuildKitV3` run does not treat `Nameplate_Plate`, `ModeStrip` and similar as scene plates and replace their meshes.

A failed check destroys the temporary body and leaves the element as it was. An exception inside one element reverts that element only.

`Apply` first reverts any earlier adoption, so it always decides from the un-adopted scene and is safe to run twice.

## Rules (constants at the top of the builder)

| Rule | Value |
|---|---|
| Non-uniform scale of a fitted body | at most 5 percent |
| Fit scale away from 1.0 | at most 35 percent (task card: 2 percent) |
| Model rim growing past the old outline | at most 3 mm per side |
| Text in front of the surface under it | 0.3 to 1.0 mm, or exactly as today (within 0.02 mm) |
| Scene plates, bands and rules in front of the sheet today | stay at least 0.05 mm in front of the body |
| Task dock key pockets vs the keys | 2 mm, a mismatch skips the dock |
| Decision paper stamp brackets and key pockets | 3 mm, a mismatching part is hidden |
| Triangles per body (factory exempt) | 6,000 |
| Tray stack anchor | at most 1 mm under the new surface, at most 8 mm further above it than today |

Text and plate depth is checked by sampling: 21 points per text box and 25 per scene plate, each tested against the triangles of the body and of the flat scene plates in the same group. A text that lies over a hidden-until-shown group (Cover, Pending, Outcome) is checked as well.

Two fits are tried for plates: the model's printable face onto the old face, then (if that is too non-uniform) the model's rim outline onto the old face.

## Predicted outcome per element

| Element | Model | Prediction | Numbers |
|---|---|---|---|
| Software factory | `SoftwareFactory_Complete_Polished` | Adopt | Same generator rig (14 bones, 5 sockets), so paths should match. The builder verifies every bone path, every curve path of every clip in the current controller, and the five sockets |
| Out-tray | `Doc_ArchiveTray_Polished` | Unknown | Model 145 x 112 x 48 mm. The current tray's size is not in any source I could read |
| Inbox tray | `Doc_InboxTray_Optimized` | Unknown | Model 145 x 112 x 32 mm. Same |
| Nameplates (3) | `Plate_AgentNameplate_Polished` | Adopt, rim fit | Scene plate 90 x 26 mm. Model face 110 x 30 (scale 0.818 x 0.867, 5.9 percent non-uniform: rejected), rim 114 x 34 (0.789 x 0.765, 3.2 percent: accepted). Printable face becomes 86.8 x 22.9 mm |
| Mode plaque | `Plate_ModePlaque_Polished` | Adopt, face fit | Scene 120 x 24, model 120 x 24, scale 1.000. Rim grows 2 mm per side; 1 mm remains to the guide line above |
| Task dock | `TaskDock_Polished` | Skip | Plate 160 x 90 and frame 168 x 98 match exactly. Model pockets at (4, -24) and (52, -24) mm; keys at (8, -20) and (56, -20): 5.7 mm off, limit 2 |
| Completion receipt | `Doc_ReceiptCard_Polished` | Skip | Scene sheet 140 x 105, model 140 x 96: 9.4 percent non-uniform. Also one pocket at (39, -14) for two keys at (-35, -31.5) and (35, -31.5), and a 7 mm strip for a 12 mm band |
| Decision paper, expanded | `Doc_PaperV2_Expanded_Polished` | Adopt, reduced to sheet and brass clip | 300 x 300 both, scale 1.000. See below |
| Decision paper, details | `Doc_PaperV2_DetailsSheet_Polished` | Adopt | Scene 300 x 136, model 300 x 134: scale 1.000 x 1.015 |
| Decision paper, carried cover | `Doc_PaperV2_Small_Polished` | Skip, always | See below |
| Task card | `TaskCard_V2_Polished` | Skip | Model face 76 x 48. Current art about 87.4 x 56.8 card units (from the edge plate, 89.76 x 59.20 minus 2 x 1.2): 15 percent, limit 2 |
| Board frame | `TaskBoard_KanbanFrame_V2` | Skip, always | Measured and printed, never adopted: see below |

`Plate_UtilitySign_Polished` (200 x 18 mm) is not used: the nearest scene plates are the guide line (140 x 17) and the caption strips (200 x 16, 300 x 18), all outside 5 percent.

## Element notes

### Software factory
- The body is a child of `SoftwareFactory` at identity, so it inherits the position, the 180 degree turn and the 0.68 scale.
- The new Animator gets the current controller (`MuseDesk_Factory`, built from the old FBX's clips) and, by default, the current avatar. The polished FBX is imported with no avatar and no clip list of its own, so its own clips are not used.
- Every serialized reference in the scene that points into the old model is re-pointed to the same path in the new one and printed. Expected: `SoftwareFactoryVisualController.factoryAnimator` and its five sockets. Any reference without an equivalent skips the factory.
- The old skinned renderer and the old Animator are disabled. The old bones stay in the scene.
- `SoftwareFactoryVisualController.ResolveReferences` (run in `Awake`) assigns sockets by name and keeps the last one it finds. The body is the last sibling, so the new sockets win. This depends on sibling order.
- Materials: a slot whose source material name matches a slot of the current factory takes the material the scene uses there today. This departs from "materials as delivered" on purpose, so the factory keeps matching the desks. Set `FactoryReuseSceneMaterials = false` to keep the delivered ones.
- 9,252 triangles (exempt from the limit). The current mesh is switched off, so the net cost is the difference.

### Trays
- Uniform scale so the footprint matches, centred on the old footprint, resting at the old bottom.
- Orientation is judged from both meshes (which half holds the tall wall). If they disagree the body is turned 180 degrees; if either cannot be judged it is kept as delivered and the row says CHECK BY EYE.
- Checked and printed: stack anchor height over the new surface (the out-tray model's built-in paper stack `ArchiveTray_PaperEdge` is hidden if it would bury the anchor), the `FiledPoke` plate's lower edge, and the archive card's rise point (`DecisionArchive.riseFromLocal`).
- The model's teal divider and angled front badge are kept. The badge was made to hold a "Filed" key; the scene's `FiledPoke` floats above the stack and is not moved, so the badge will read as an empty holder.

### Nameplates and mode plaque
- `AgentNameplate` does not tint the strip: the strip has a fixed per-role material and the script holds no renderer. Nothing is re-wired.
- The model's own strip is hidden and the scene's strip kept. After the rim fit the model strip would end 0.5 mm into the role text's box.
- `ModePlaque.strip` keeps pointing at the scene's strip.
- The easel foot is hidden when it would reach below the real desk surface (root-local y under -0.5 mm). Expected for the mode plaque, not for the nameplates.

### Decision paper, expanded
The model was made for the V2 layout. Against `BuildPaperV3`:

| Feature | Model | V3 scene | Result |
|---|---|---|---|
| Stamp brackets and pad | centre (91, 33.5), 86 x 67 mm | (94, 24), 80 x 78 | 10 mm off: `StampZone_Pad` and the brackets (`PaperExp_InkSoft`) hidden |
| Primary key pockets | (±70, -109), 132 x 54 | (±69.4, -115), 129 x 38 | 6 mm off |
| Chip pockets | y = -42 | y = -74 | 32 mm off, and they would lie under the provenance and status text |
| Header band | 26 mm, 0.9 mm proud | 36 mm band, chip and labels of its own | `ModeBand` hidden |

The pockets are merged into one mesh with the under-rim (`PaperExp_PaperEdge`), so hiding them removes the stepped under-sheet as well. What remains is the 2.4 mm rounded sheet and the brass clip. The clip stands 1.5 mm proud and covers the top 3.5 mm of the header band over the middle 44 mm; no text is there.

The sheet uses `Mat_DS_Paper`, a Simple Lit material; the current sheet and the tinted plates on it are Unlit. The tones may differ. Set `EnablePaper = false` if it reads worse.

### Decision paper, carried cover
Not adopted. The `Cover` group has no sheet of its own (band, tab, two under-sheets and three labels); the `Card` sheet is the paper in both states and is never hidden while held. The model's cover sheet would lie on the Card's plane with 15 mm corners over 4 mm ones, its band stands 4.2 mm proud and its tab 1.0 mm, where the scene's labels are 2.3 mm (0.5 mm carried) in front of the sheet.

To unlock it without a script change: add the Card's renderer or body to `HeldPaperController.hideWhileHeld`, put the small model under `Cover` with its band and tab hidden, and move the cover labels to the model's surfaces. That is a layout and wiring change, so it is left out.

### Task card
If the run measures the face within 2 percent after all, the card is adopted with `Card_SelectionRim` hidden. The rim is not hooked to `TaskCardView.edgePlate`: tinting would work (the script sets `_BaseColor` through a property block and the rim's material is Simple Lit), but the script also scales the plate it holds, and a 2.6 mm ring that is always drawn would change the idle look.

### Board frame
The builder prints the current frame's size, the model's size, the scale it would need, the model's four lane centres at that scale and `BoardPresenter`'s four region positions with the worst difference. It does not adopt: `BuildBoardV3` measured lanes, slots, labels and the key rail on the current mesh.

## Revert

`BuildOfficeArtV3.Revert` re-enables every renderer it disabled, re-enables the old factory Animator, points re-wired references back, destroys every `*_ArtBody` and saves. Each body carries an inactive child `ArtRestore:<n>` listing what to restore.

To turn one element off, set its `Enable...` constant to false and run `Apply` again.

To restore one element by hand: enable the old object's renderer and delete its `_ArtBody` child. For the factory also enable the root Animator and re-run `SoftwareFactoryVisualController.ResolveReferences` (or `Revert`).
