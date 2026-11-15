# UI and visual consistency audit

Source evidence: [audit/C_UI_VISUAL_AUDIO.md](audit/C_UI_VISUAL_AUDIO.md) (code reading, file:line). This page records what was found, what this pass changed, and what is left for someone with the headset or the art owner.

## Design system in force

- **Tokens:** `OfficeTokens` (`Scripts/Workspace/OfficeUi.cs`), with Paper, Ink, InkSoft, Teal, Amber, Success, Coral, Lavender and InfoBlue. Tinting goes through a `MaterialPropertyBlock`, never `renderer.material` (no leaks found).
- **Type:** Inter, Inter Medium and Inter Bold SDF only. World-space labels sit at scale 0.01, with sizes in millimetres (`WallUi.MakeText` follows this).
- **Motion:** `MuseDesk.Motion` tokens (see `MOTION_AND_INTERACTION_DESIGN_SYSTEM.md`).
- **Sound:** procedural one-shots (`FeedbackController`, `StampTool`, `CeilingPodAudio`, and now `WallAudio`). There are no audio files.

## Findings and status

| # | Finding (audit C) | Change in this pass | Status |
|---|---|---|---|
| 1 | Success green has ≥7 values; amber and coral drift | Wall state colours now **are** `OfficeTokens` (Teal, Amber, Coral, Success, Lavender, Paper, Ink) in `WallHabitatView`. Board/agent copies (`TaskCardView:420-423`, `AgentView:192-197`, `WorkstationStatusDisplay:45-46`, `StationDropZone:30`) not yet migrated | PARTIAL |
| 2 | Role tint collides with state colour (Tester = amber, Developer = green) | Not changed: the role colours are serialized on the card prefab and tied to character identity. **Recommendation:** soft pastel role accents that differ from the saturated state tokens, always shown with the role name | OPEN (owner decision) |
| 3 | A disabled Wall key looks live | `WallKey` dims its label (alpha 0.38) while disabled | DONE (code, PLAY) |
| 4 | Two key visual systems (`KeyStateVisual` vs `PokeKeyStateVisual`) | Not merged: both are on-head-proven; merging risks the submission keys | OPEN |
| 5 | Wall provenance footer is the smallest text (3.6 mm, grey) | 4.2 mm, InkSoft | DONE |
| 6 | Wall palette duplicated in ~8 controllers | Wall view now uses the tokens; other controllers unchanged | PARTIAL |
| 7 | Off-palette hover cyan (`GrabHoverHighlight:25`) | not changed | OPEN |
| 8 | `WallUi.MakeText` falls back to the default font silently | not changed (the scene builder always injects Inter) | OPEN (low) |
| 9 | Wall keys pop between scale 0 and full; zero scale breaks poke maths | Keys reveal and hide on `panelAppear`/`panelHide` and never go below `ScaleEase.HiddenFactor` | DONE |
| 10 | Wall cards appear by SetActive | Eased scale reveal (0.94 → 1) and tuck-away | DONE |
| 11 | Missing sounds: card, page, elevator, portal, chime; Wall keys silent | `WallAudio`: door slide, arrival ding, printer tick, portal open and close, landing tick. Wall keys get the standard key click. Muted in Focus mode | DONE (code; audibility needs the headset) |
| 12 | No global Reduced Motion setting | `MotionSettings.ReducedMotion` exists and is honoured by every token; **no UI toggle or persistence yet** | PARTIAL |
| 13 | Provenance gaps: agent bubble, receipts, archive, voice HUD | not changed | OPEN |

## Not verifiable here

Stereo text depth, legibility at seated distance, colour contrast on the Quest 3 display and sound loudness all need the headset (QA rows UI-01, UI-02 and PERF-03).
