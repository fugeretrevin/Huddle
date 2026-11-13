# Final refinement pass: addendum to the agent brief

2026-11-12. Read `docs/Design/REDESIGN_BRIEF_FOR_AGENTS.md` first: every rule in it still binds (code only, no `unity` commands, no scene or asset file edits by hand, no git, stay in your files, one idempotent builder with `public static string Apply()`, verify every API against source, honesty invariants, no per-frame garbage, candid report). Then read `docs/PROJECT_OVERVIEW_FOR_REVIEW_3.md`, `docs/Design/MUSEDESK_DESIGN_SYSTEM.md`, `docs/Design/SPATIAL_INTERACTION_GUIDELINES.md` and `docs/Design/UX_REDESIGN_CHANGELOG.md`.

This pass is refinement of a working product. Do not redesign, do not add features, do not change the backend, contracts, gateways' behaviour or the reducers' events. Every existing behaviour must keep working. Deactivate rather than delete scene objects.

## State of the scene (built by the last pass)

Builders already applied, in order: `BuildOfficeV2`, `BuildTaskDockV2`, `BuildPaperV2`, `IntegrateV2`, `SetupInterFont.Apply`. Read the ones that touch your area: they are the source of truth for object names, structure and wiring. Your builder runs after them and must not assume it runs before or after another refinement builder, except that the **Kit** builder runs last.

The owner has worn the build and said it looks really good. The expert review then listed what still reads as unfinished. Findings measured in the scene:

- **Faint text in the screenshots is mostly a Scene View artifact** (Scene View fog; the scene itself has fog off) plus thin Inter Regular with synthetic bold. Text colours on the paper are already solid Ink `#1F2530`. Real Inter SemiBold and Bold font assets are being added by the coordinator; `FontStyles.Bold` will then render true bold. So: fix hierarchy and size, do not chase opacity.
- **The stamp covering the paper in the screenshot is a preview artifact** (the preview activated a child that is normally inactive). At rest the stamps sit on `Doc_StampRestPad` at root-local (±0.036, 0.014, 0.415), 9.3 cm tall.
- **The huge panel at the edge of the view is real**: `WorkspaceRoot/ArtifactPanel` (`ArtifactViewer`) is 0.50 x 0.60 m at root-local (-0.52, 0.44, 0.21), and opens by itself when work is delivered.
- `ArchiveReadout` is a floating dark panel at (0.34, 0.20, 0.27), shown for 8 s after poking the out-tray plate.
- `CompletionReceipt` is a flat paper plate at (-0.15, 0.055, 0.37) with a Run again key.
- Poke keys have no hover or press colour: `InteractableColorVisual` writes `_Color`, which URP Simple Lit ignores (it reads `_BaseColor`).
- `Btn_Approve` and `Btn_Reject` are a square key scaled (2.133, 0.833, 0.680), so the rim is uneven.
- With the board open, `NewTaskButton`, `PauseResumeButton` and `PriorityButton` are 0.60 to 0.64 m from the eye; the board is 0.62 m wide at (0.58, 0.40, 0.48) and mostly empty with four cards. Card text: title 10 mm, state 7.5 mm, role and mode 6.5 mm.

## Shared conventions for this pass

- **Plates stay plain Unity quads when you create them.** Name the object with a suffix `Plate`, `Sheet`, `Band`, `Strip` or `Chip`, give it one of the existing UI materials or a new one under `Assets/MuseDesk/Art/Materials/UI` (create through `AssetDatabase` as the earlier builders do). The Kit builder, which runs last, replaces quad meshes on those plates with rounded-corner meshes of the same size. Do not build your own rounded geometry.
- **Keys**: clone an existing key and keep its internal structure (`Visuals/ButtonVisual` with `Cap3D`, `Housing3D`, `PokeVisualRestGuard`). Do not scale a key non-uniformly: if you need a wide key, give the key root a uniform scale for its height and add a component-free child marker named `WideKey_<ratio>` (for example `WideKey_2.5`) under the key root; the Kit builder swaps in wide cap and housing geometry and resizes the poke surface. Until then the key is square, which is fine.
- **Key state colours** are handled by the Kit agent's `KeyStateVisual` component, added to every key by the Kit builder. If your code needs a key to look disabled, hide it by scale as now.
- **Show and hide by scale**, never `SetActive`, for anything containing Interaction SDK interactables. Reuse `MuseDesk.Workspace.ScaleEase` (in `OfficeUi.cs`) or the board's `ScaleReveal`.
- **Tokens**: `MuseDesk.Workspace.OfficeTokens` has the colour constants. Type sizes as in the first brief, with one change for this pass: primary reading text (the question, summaries, card titles, viewer body) should be as large as the layout honestly allows; 5.5 mm is a floor for metadata only.
- **Motion**: 120 ms press, 250 ms show/hide ease-out, 350 ms unfold, 600 to 900 ms travel. Calm; no particles, no flashing, no new transparent materials.
- **Audio**: `MuseDesk.Feedback.FeedbackController.PlayCue(FeedbackCue)` exists (KeyPress, PaperTaken, Attention, Approve, Reject, Changes, Delivered, Error). Call it; do not add audio sources.
- Fonts are applied by the coordinator after all builders.

## Ownership for this pass

| Agent | Files it may edit or create | Builder |
|---|---|---|
| Paper | `Assets/MuseDesk/Scripts/Documents/*` except `DecisionArchive.cs` | `AgentScripts/BuildPaperV3.cs` |
| Receipt | `Assets/MuseDesk/Scripts/Workspace/CompletionReceipt.cs`, `ArchiveStackPoke.cs`, `Assets/MuseDesk/Scripts/Documents/DecisionArchive.cs`, `Assets/MuseDesk/Phase2/Runtime/Board/ArtifactViewer.cs`, new files in `Assets/MuseDesk/Scripts/Workspace/` prefixed `Receipt` or `Artifact` | `AgentScripts/BuildReceiptV3.cs` |
| Board | `Assets/MuseDesk/Phase2/Runtime/Board/*` except `ArtifactViewer.cs` | `AgentScripts/BuildBoardV3.cs` |
| Life | `Assets/MuseDesk/Scripts/Agents/*`, `Assets/MuseDesk/Scripts/Art/*`, `Assets/MuseDesk/Scripts/Networking/SessionController.cs` (additive only), `Assets/MuseDesk/Scripts/Workspace/ModePlaque.cs`, `FirstUseGuide.cs`, `AgentNameplate.cs`, `Assets/MuseDesk/Scripts/Feedback/FeedbackController.cs` | `AgentScripts/BuildLifeV3.cs` |
| Kit | new files in `Assets/MuseDesk/Scripts/Workspace/` prefixed `Key` or `Kit`, `Assets/MuseDesk/Scripts/Workspace/PokeVisualRestGuard.cs` | `AgentScripts/BuildKitV3.cs` |

The coordinating session runs: Paper, Receipt, Board, Life, then Kit, then fonts, then `AgentScripts/XrVisualQa.cs` and a Play Mode run through `AgentScripts/PlayDrive.cs`. Tell the coordinator in your report which public methods a Play Mode driver should call to exercise your work, and which `XrVisualQa` name lists or allow-list entries need updating.
