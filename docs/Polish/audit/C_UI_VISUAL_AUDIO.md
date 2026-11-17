# C. UI / visual / audio design system audit

Scope: Assets/MuseDesk/** (read-only, static grep/read; nothing run). Evidence level for every row is AUTO-read (code inspection) only. Nothing here is WORN. Items I did not open are marked NT.

## 1. Design tokens

Existing token source to build on: `OfficeTokens` in `Assets/MuseDesk/Scripts/Workspace/OfficeUi.cs:10-37` (Paper, Ink, InkSoft, Plate, OnPlate, Housing, Teal, Lavender, InfoBlue, Success, Coral, Amber, plus `Tint()` that uses a MaterialPropertyBlock). Header cites docs/Design/REDESIGN_BRIEF_FOR_AGENTS.md section 4. Used by about 16 files (KeyStateVisual, VoiceHud, FloorAimController, ModePlaque...). It is a static class with no ScriptableObject and no semantic names (no NeedsYou, Problem, Completed).

Canonical values: Teal 2FA39A, Amber E0A030, Success 2E9B5F, Coral D9534A, Lavender 8C7BD6, InfoBlue 4C85DB.

### Where the same semantic has different values

| Semantic | Canonical | Divergent definition (file:line) | Severity | Fix |
|---|---|---|---|---|
| Amber / needs-you | E0A030 | `WallHabitatView.cs:21` E8A33C; `TaskCardView.cs:423` (0.90,0.70,0.20); `AgentView.cs:193` (0.95,0.65,0.25); `CardRoleTint.cs:25` (0.95,0.65,0.25); `AgentView.cs:195` Waiting (0.85,0.80,0.35) olive-yellow; `DecisionPanel.cs:53` demoBand (0.86,0.55,0.16); `ApprovalStampController.cs:61`/`DecisionPanel.cs:124` Changes/Warn (0.60,0.36,0.05) dark; `GrabHoverHighlight.cs:26` held gold | Med | Point all at OfficeTokens.Amber (Hex-equal float copies exist at `FloorOfficePlatformController.cs:14`, `SpatialBeaconVisualController.cs:28`, `SpatialMailboxController.cs:26`, `TaskDock.cs:53`, `TaskCardView.cs:58`) |
| Completed / success green | 2E9B5F | `WallHabitatView.cs:21` Green 5DBB7A; `TaskCardView.cs:420` (0.30,0.70,0.35); `AgentView.cs:192` Coding green (0.30,0.80,0.45) and `:197` Complete (0.40,0.85,0.55); `WorkstationStatusDisplay.cs:45` Done (0.22,0.76,0.45); `CeilingDeploymentVFXController.cs:30` mint; `StationDropZone.cs:30` (0.30,0.80,0.45); `ApprovalStampController.cs:59`/`DecisionPanel.cs:122` ink green (0.10,0.42,0.24) (acceptable: text-on-paper variant, but undocumented) | High | One Success + one SuccessInk token |
| Problem / coral | D9534A | `WallHabitatView.cs:21` E0654F; `TaskCardView.cs:421` (0.80,0.30,0.30); `AgentView.cs:198` (0.90,0.35,0.35); `WorkstationStatusDisplay.cs:46` Alert (0.95,0.45,0.25) is orange-ish; `CeilingScanReveal.cs:31`, `FloorScanReveal.cs:35` (0.86,0.27,0.24); `StationDropZone.cs:32`; `TaskCardView.cs:137` NoticeError (0.66,0.18,0.15) | High | Token |
| Select / teal | 2FA39A | `WallHabitatView.cs:19` ok; `CeilingDeploymentVFXController.cs:27` (0.184,0.72,0.70); `DecisionPanel.cs:54` liveBand (0.13,0.50,0.44); `GrabHoverHighlight.cs:25` hover is cyan (0.35,0.85,0.95); `PokeKeyStateVisual.cs:28-32` hover 47BDB4 / pressed 217A73 (hardcoded, not derived) | Low-Med | Derive from token |
| Optional / lavender | 8C7BD6 | `WallHabitatView.cs:20` A99BD1; `SpatialBeaconVisualController.cs:27` (0.549,0.482,0.839) = 8C7BD6 ok; `AgentView.cs:194` Reporting (0.70,0.70,0.95) | Low | Token |
| Working / info blue | 4C85DB | `AgentView.cs:191`, `CardRoleTint.cs:23` (0.30,0.55,0.95); `WorkstationStatusDisplay.cs:44` (0.25,0.62,0.95); `TaskCardView.cs:422` (0.30,0.55,0.85); `AgentConversationPerchController.cs:24` ok | Med | Token |

Further evidence of copy-paste rather than a shared class: `SpatialBeaconVisualController.cs:25-31`, `SpatialMailboxController.cs:25-28`, `FloorOfficePlatformController.cs:13-16`, `AgentConversationPerchController.cs:23-26`, `PlacementGhostPreviewController.cs:14-15`, `DocumentCourierController.cs:129-227` (inline literals repeated 7 times) all re-declare the same Teal/Amber/Green/Coral as float triples. `WallHabitatView.cs:19-23` has its own Hex() helper and palette (the Wall Habitat palette is a parallel one: Ivory, Wood, Brass, Glass, Grey exist only there). `WorkspacePlacementDebugView.cs:26-46` Gizmos only (ignore).

Role colours conflict: Researcher is blue, Developer green, Tester amber in `CardRoleTint.cs:23-25` and `AgentView.cs:191-193` (those are role/state-coloured), yet amber also means "needs you" elsewhere and green also means "completed". A Tester card tinted amber reads as "needs you". Severity High (semantic collision, not just value drift). Fix: give roles a distinct hue family or a non-colour marker.

## 2. Typography

Font assets: `Assets/MuseDesk/Art/Fonts/Inter SDF.asset`, `Inter-Medium SDF.asset`, `Inter-Bold SDF.asset` (only these three under Assets/MuseDesk). Which asset a given scene or prefab text uses is NT (scene/prefab YAML not read). Code-side:

| Surface | Source | Size / wrap / overflow | Finding |
|---|---|---|---|
| Wall cards (WallCard title/body/footer) | `WallUi.cs:74-90`, `:135-137` | scale 0.01, fontSize 6.5 / 4.6 / 3.6 (mm-ish), Normal wrap, Ellipsis, no autosize | Font injected from `WallHabitatController.cs:43` (`font`); null leaves TMP default font (Liberation) silently. 3.6 mm footer is the provenance line: tiny (about 0.5 deg at 0.6 m? borderline) and grey (0.35,0.38,0.42) on cream Paper, contrast about 5:1 but not a token. Ellipsis can hide the provenance text. Med |
| Wall keys | `WallUi.cs:66` | `enableAutoSizing=false` on a clone of the ModeConsole key label; long labels have no overflow handling set | Med: labels from `WallHabitatController.*.cs` are free text; no ellipsis/shrink |
| Agent speech bubble | `AgentSpeechBubble.cs:192-195`, `AgentTalkRig.cs:102-106` | fontMm, no autosize, Normal, Ellipsis | OK |
| Agent status chip | `AgentTalkRig.cs:139-145` | 7.5 autosize 6.3-7.5, NoWrap | Smallest legible; autosize range of 1.2 mm is effectively fixed. Low |
| Agent talk plate font | `AgentAttention.cs:274-276` borrows `plate.roleText.font` | | Implicit coupling to scene plate. Low |
| Architecture diagram labels | `ArchitectureDiagramController.cs:270` | fontSize 6 | Check wrap/overflow: none set (NT) |
| Artifact viewer body | `ArtifactViewer.cs:132-133` | autosize off, overflow Page | Only surface using Page; paging UI needed (NT) |
| Editor-built labels | `SpatialWorkspaceArtSetup.cs:1345-1350` (36, NoWrap, Inter), `MuseDeskArtPipeline.cs:438-441` | | Build-time only; different defaults from runtime (NoWrap vs Normal) |
| Task cards, DecisionPanel, ModePlaque, nameplates, VoiceHud | no fontSize/overflow lines found in code, so set in prefabs | NT | Cannot confirm sizes/autosize; needs prefab YAML pass |

Convention present: scale 0.01, fontSize ~ mm, cm sizeDelta (`WallUi.cs:73`). Not enforced elsewhere (ArchitectureDiagramController uses 6 without a stated scale). No shared "TextStyle" helper; only `WallUi.MakeText`.

## 3. Materials

- Good: no runtime `renderer.material` leaks found. A grep for `.material` (non-plural) in Assets/MuseDesk/**/*.cs returned only comments (`CardRoleTint.cs:64`, `TaskCardView.cs:255,428`) saying not to. About 45 files use MaterialPropertyBlock correctly.
- Runtime `new Material(...)`: `AgentAttention.cs:323` ("AgentTalk (runtime)", one per call? verify it is cached, NT); `WallHabitatController.cs:382` fallback; `WallHabitatView.cs:215` one proxy material per colour key (bounded by palette size, fine); Editor-only elsewhere.
- Shared material library: `Art/Materials/Mat_DS_*.mat` (Paper, Plate, Housing, AccentTeal/Amber/Blue/Lavender/Success...) and `Buttons/Button_Cap_<hex>.mat` per accent. Good basis; but hex-named button cap materials (42A866, 8C6BCC, B25C57, D14C45, ED9E38, 4C85DB) do not match token values (e.g. 8C6BCC vs Lavender 8C7BD6, 42A866 vs Success 2E9B5F, D14C45 vs Coral D9534A, ED9E38 vs Amber E0A030). Med: key caps are off-palette by a few steps.
- Wall Habitat renders a flat colour box set through one shared material + MPB (`WallHabitatView.cs:25-35`), statically merged to 7 renderers per recent commit: good for draw calls.
- Overdraw: transparent/emissive status (hover highlight `GrabHoverHighlight.cs:25-26` alpha 0.22/0.36, ghost `PlacementGhostPreviewController.cs:14-15` alpha 0.36/0.42, scan reveals alpha 0.6, DropZone alpha 0.24, portal cone and soft particle materials in `SpatialWorkspaceArtSetup.cs:163-234`) are all alpha-blended. Surface type of each .mat is NT (grep of `_Surface` was inconclusive). Suggest: profile on device before deciding; use dithering/alpha-clip for the large scan/portal quads if GPU time is tight.

## 4. Interaction feedback states

Shared engine: `KeyStateVisual` (`Scripts/Workspace/KeyStateVisual.cs`) handles Normal, Hover (lighten), Pressed(Select, darken), Disabled (blend to Housing, `:124,148,156`), plus `FlashRefused` (coral, `:181`) and `FlashConfirmed` (success, `:184`) and a pending-start state (`:257-263`). A second, parallel implementation exists: `PokeKeyStateVisual` (`Art/Scripts/PokeKeyStateVisual.cs:5-12`, enum Normal/Hover/Pressed/Active/Disabled, hardcoded colours `:28-39`). Two key visual systems with different palettes. Med.

| Surface | hover | pressed | disabled | pending | committed | refused | Notes |
|---|---|---|---|---|---|---|---|
| ModeConsole keys (`ModeConsole.cs:211-266`) | yes | yes | yes (`SetDisabled`) | partly | n/a | no | Only caller of `SetDisabled` in the repo (grep) |
| PlacementGuide Place key | yes | yes | n/a | no | no | yes `PlacementGuide.cs:268` FlashRefused | Only surface using FlashRefused |
| Wall dock keys (`WallUi.WallKey`, `WallUi.cs:11-32`) | inherited from cloned template | inherited | NO visual: `Show(on, enabledToo)` only turns off the PokeInteractable (`:28`); key looks live but is dead | no | no | no | High: silent dead key; fix by calling the clone's KeyStateVisual.SetDisabled and FlashRefused/FlashConfirmed |
| Task cards (`TaskCardView.cs:56-59,135-139`) | grab only | grab | no | edgePending amber | edge/notice OK green | edgeRefused coral | Cards use own edge colours not the key system |
| Task dock chips (`TaskDock.cs:53-58`) | n/a | n/a | n/a | startingChip | LIVE/DEMO | n/a | |
| Paper Approve/Reject keys, stamp (`ApprovalStampController.cs:41-63`, `DecisionPanel.cs:120-130`) | stamp target near/idle (`:41-44`) | stamp thunk | `_nope` clip (`StampTool.cs:374`) | no | APPROVED/REJECTED/CHANGES text + cue | nope clip, no visual flash | Uses its own ink/plate colours |
| Grab targets (`GrabHoverHighlight.cs`) | cyan 0.22 + tick | gold held 0.36 | n/a | n/a | n/a | n/a | Cyan hover is off-palette (token hover is teal) |

## 5. Audio

No audio asset files exist for the app: Assets/**/*.wav|ogg|mp3 returns only two Meta Voice SDK sample files (`Assets/Samples/Meta - Voice SDK - Immersive Voice Commands/85.0.1/TTS Lip Sync/Audio/AsyncComplete.wav` and the TTS Voices copy), not used by MuseDesk code. Everything MuseDesk plays is synthesised at runtime with `AudioClip.Create`.

| Cue | Where | Trigger | Spatialization |
|---|---|---|---|
| KeyPress 1175 Hz blip | `FeedbackController.cs:99` | any registered PokeInteractable Select (`:258`), also `AgentPresence.cs:292`, `AgentAttention.cs:690` | 3D `spatialBlend=1`, min 0.6 max 6 m, Doppler 0 (`:88-94`); volume 0.25 default (`:46`) |
| PaperTaken | `:100` | paper IsTaken flips (`:142`) | same |
| Attention (two notes) | `:101` | DecisionRequested (`:175`), `AttentionBeacon.cs:339` | same |
| Approve / Changes / Reject | `:102-104` | DecisionResolved (`:177-183`), also `ApprovalStampController.cs:180-185` | same (possible double fire with stamp: owned by both; NT) |
| Delivered (rising fifths) | `:105` | WorkDelivered; `CompletionSequence.cs:332,366` sequences it | same |
| Error | `:106` | TaskFailed, RespondFailed, `AttentionBeacon.cs:344`, `CompletionSequence.cs:400` | same |
| stamp_poof, stamp_thunk, stamp_nope | `StampTool.cs:171,362,374` | stamp leaves/arrives, thunk, refused | 3D (`:120`) |
| hover_tick | `GrabHoverHighlight.cs:158-161` | grab hover | 3D (`:147`) |
| ceiling_unlock, ceiling_lock, ceiling_travel (loop) | `CeilingPodAudioCues.cs:178-180`, `CeilingPodAudio.cs:28-66,199` | pod unlock/lock; travel hum is the ONLY looping sound (`:65`) | 3D |

Missing (grep found no cue): card click / card grab-release, page rustle (paper open/flip/turn), elevator motor loop and elevator ding (Wall Habitat), portal whoosh (Wall portal), a distinct completion chime for wall/board cards (Delivered exists for the factory only), wall key click (KeyPress registers only keys present at Start via `RegisterKeys`; runtime-cloned `WallKey` clones: check re-registration, NT), error/refused on wall keys. Wall Habitat destroys all cloned AudioSources (`WallHabitatView.cs:354`), so avatar/portal audio is intentionally absent. Severity Med; fix: add FeedbackCue entries + synthesized blips in FeedbackController (pattern already there), keep volume 0.25.

## 6. Settings

- No Reduced Motion setting and no general comfort/quiet/volume setting. `RuntimeSettings.cs` (Config) has no matching fields; the only "comfort" hit is a reach tooltip (`RuntimeSettings.cs:31`).
- Closest: `comfortMode` bool, Wall Habitat only: `WallHabitatController.cs:68` (public field, default false), read at `:282` (suppresses pulse), `WallHabitatController.Interaction.cs:74` (pushes to PortalFlow), `PortalPlacement.cs:176,238` (`Dur(t)` shortens durations by `timings.comfortScale`). Test: `AgentVisitPortalManagerTests.cs:225`. Not exposed in any in-headset UI, not persisted.
- Focus mode exists and is the quiet mode: `FocusModeController` (`Scripts/Workspace/FocusModeController.cs:24,105`; `.Active` static). Read by `AgentGaze.cs:274`, `AgentIdleLife.cs:186,203`, `AgentAttention.cs:492,993`, `CompletionSequence.cs:290,405`, `AgentHandoffController.cs:156`, `AttentionController.cs:82`, `VoiceOffice.cs:331`; voice commands at `VoiceIntentParser.cs:541-551`. It quietens agent behaviour; it does not mute FeedbackController audio (no read of `Active` in FeedbackController, verified by absence in grep) and does not reduce motion.
- PlayerPrefs is used only for layout (`WorkspaceLayoutStore.cs:95-104`, `MovablePanel`).

## 7. Provenance labels

| Surface | Label | file:line | Gap |
|---|---|---|---|
| Task card | LIVE / SCRIPTED DEMO, always visible | `TaskCardView.cs:31,567-571` | Unknown/missing source_mode shown as SCRIPTED DEMO (safe default) |
| Task dock | LIVE / SCRIPTED DEMO / STARTING chip | `TaskDock.cs:353` | ok |
| Decision paper | mode band + provenance text, e.g. "Template recommendation. Not live AI research." | `DecisionPanel.cs:160-204`, `Phase2DemoGateway.cs:244-246` | ok |
| Artifact viewer | provenanceLabel | `ArtifactViewer.cs:78-79,417-432` | ok |
| Wall visit card | "Answer from the AI model" for LIVE_MODEL, "SCRIPTED DEMO" for demo | `WallHabitatController.Interaction.cs:402-404` | The live label wording differs from "LIVE" used elsewhere. Low. |
| Wall Manager plan card | footer from `WallHabitatController.Manager.cs:178` | text not verified (NT); commit log says offline plan is labelled |
| Wall card footer font | 3.6 mm grey `WallUi.cs:137` | | Provenance is smallest text on the card; can ellipsise |
| Agent speech bubble, status chip, workstation display, nameplates, receipts (`CompletionReceipt.cs`), archive (`DecisionArchive.cs`), voice HUD | no provenance label found in code | | Med: agent talk/bubble text from backend carries no LIVE/DEMO marker (NT for prefab text) |
| Gateway ModeLabel | "LIVE · v2 (desktop)" / "DEMO · scripted (v2)" | `Phase2LiveGateway.cs:25`, `Phase2DemoGateway.cs:33` | Different phrasing from card labels; unify |

## Prioritised fixes (suggested)

1. Promote `OfficeTokens` to the single source: add semantic aliases (NeedsYou=Amber, Completed=Success, Problem=Coral, Optional=Lavender, Select=Teal, Working=InfoBlue) and an Ink-on-paper variant set; replace the ~25 duplicates listed in section 1. Keep Wall palette extras (Wood, Brass) as theme extensions.
2. Fix role-tint vs state-colour collision (`CardRoleTint.cs:23-25`).
3. Wire WallKey disabled and refused/confirmed visuals (`WallUi.cs:22-29`).
4. Make provenance footer on Wall cards larger/token-coloured and non-ellipsised.
5. Add missing cues (card, page, elevator, portal, wall completion) to FeedbackController.
6. Add a single `ComfortSettings` (reduced motion + quiet audio) read by Wall, pulses, CompletionSequence, FeedbackController, persisted via PlayerPrefs; reuse existing `comfortMode` plumbing and `FocusModeController.Active` hooks.
7. Unify key visuals (KeyStateVisual vs PokeKeyStateVisual) and re-derive key cap materials from tokens.
8. Prefab/scene pass (NT here) for font assets, sizes and autosize on cards, paper, nameplates and plaques.
