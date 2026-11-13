# MuseDesk UX redesign: shared brief for implementation agents

Written 2026-11-12 (late). Every agent on the redesign reads this first. It fixes the things that must be identical across all the work: coordinates, design tokens, project gotchas and working rules. The product brief this implements is the "Complete Spatial UX Redesign" assignment; the state before it is `docs/PROJECT_STATE_FOR_REVIEW.md` with screenshots in `docs/review_screenshots/state_2026-11-12_night/`.

## 1. Goal in one paragraph

Today the app is a functional prototype: three cute robot agents at miniature desks, surrounded by a large task board, floating black status panels, a big floating mode sign, a guide sign, and about ten loose mechanical keys. The redesign makes the three agents and their office the focus, shows controls only when they are relevant, and makes the decision paper (the signature interaction) beautiful and simple. Nothing in the backend or the event model changes: this is presentation and interaction only.

## 2. Hard working rules

- **Code only.** Do NOT run any `unity ...` command and do not open, edit or create `.unity`, `.prefab`, `.asset`, `.mat`, `.meta`, `.controller` or `.fbx` files. One coordinating session owns the single open Unity Editor: it compiles, runs your builder script, looks at captures, and fixes what breaks. Do not commit, stash, checkout or reset.
- **Stay in your files.** Each agent has an explicit list of files it may edit or create. Read anything; change nothing else. If you need a change elsewhere, describe it exactly in your report.
- **Scene changes go in ONE idempotent Editor builder script** in `AgentScripts/` (name given in your assignment): a `public static class` with `public static string Apply()` that finds objects by name in the open scene, changes them, marks the scene dirty, saves once at the end with `EditorSceneManager.SaveScene`, and returns a human-readable report. It must be safe to run twice. Read `AgentScripts/FinalPass.cs`, `AgentScripts/FeedbackPass4.cs` and `AgentScripts/PolishPass2.cs` first and reuse their idioms (helper functions for finding objects, creating a quad/shape, cloning a TMP label, styling text, cloning a poke key and rewiring its UnityEvent, creating a material asset). Copy the helpers into your own script; do not edit those files.
- **You cannot compile.** Verify every type, member and enum name against the source (project code under `Assets/MuseDesk`, SDK source under `Library/PackageCache`). Keep `using` lists exact. No new packages. C# features only as already used in the codebase. Re-read each file after you edit it. Files are mixed LF/CRLF: use your Edit tool, never shell text replacement.
- **Honesty invariants (binding, from `docs/AGENTS.md`).** Scripted demo activity is never presented as live work: every card, paper and artifact shows LIVE or SCRIPTED DEMO in words. The backend (or the labelled demo gateway) is the authority: the UI shows an outcome only after the reducer applies the confirming event. Never invent completed work for effect.
- **No per-frame garbage.** No `Find*`, `GetComponent*`, `Camera.main`, LINQ, string building or allocation in `Update`/`LateUpdate`. Cache; rebuild text only on change.
- **Report candidly.** End with: files changed/created; public API added (exact signatures); what your builder creates and wires; every line you are unsure compiles; what you could not verify and what a person in the headset should look for; what you deliberately left out.

## 3. Coordinates and scale (do not deviate)

Everything lives under `WorkspaceRoot` (scale 1). `WorkspacePlacement` puts it in front of a seated user.

- Root-local axes: **+Z points at the user**, +Y up, and the **user's right is -X** (their left is +X).
- The user's eye is at about root-local **(0, 0.30, 0.62)**, looking toward -Z and slightly down.
- Comfortable reach: within **0.55 m** of the eye for anything touched often; never beyond 0.65 m.
- TextMeshPro text and Interaction SDK poke keys present their **local -Z** to the reader / finger. To face the eye, a child's +Z must point away from the eye: `Quaternion.LookRotation((pos - eye) flattened or not, Vector3.up)`.
- Unity's built-in Quad is visible from its -Z side: do not rotate quads 180 degrees to "face the user".

Current layout (root-local metres):

| Thing | Position | Notes |
|---|---|---|
| Researcher / Developer / Tester stations | x = -0.30 / 0.00 / +0.30, z about 0 | Agents `Agent_Researcher`, `Agent_Developer`, `Agent_Tester` under `ResearchStation`, `DevStation`, `QAStation`. Agents are 0.31 m tall and face +Z. Desk tops about y = 0.15 |
| Software factory | (0, 0, 0.25) | In front of the desks, 0.12 m tall |
| Stamps on a rest pad | (±0.036, 0.014, 0.415) | `StampApprove`, `StampReject` |
| Paper hand-over spot | `DeliveryZone` (-0.20, 0, 0.17) | The Researcher walks here |
| Inbox / out-tray | `InboxTray` (-0.32, 0, 0.25), `OutboxTray` (0.32, 0, 0.25) | Decided papers glide to the out-tray |
| Task board | `TaskBoard` (0.58, 0.40, 0.48), yawed to the eye | 0.62 m wide. Has a `FaceUser` billboard |
| Loose keys | `StartKey`, `ReplayKey`, `FocusToggle`, `TipsKey`, `FiledKey` under `Phase6` | Front-right arc and front-left |
| Guide sign | `GuideSign` near (0, 0.05, 0.35) | One line of first-use guidance |
| Status panels | `Monitor` under each station at y about 0.35 | Black floating panels with `FaceUser` + `WorkstationStatusDisplay` |
| Name tags | `NameTag` under each agent | `FaceUser` |
| Mode sign | `AppServices/DiagnosticsText` | Large floating text, `DiagnosticsOverlay` |

## 4. Design tokens

### Colour (sRGB hex; use these exact values)

| Token | Hex | Use |
|---|---|---|
| Paper | `#F7F2E3` | Document and card faces |
| Ink | `#1F2530` | Primary text on paper |
| Ink soft | `#4B5563` | Secondary text on paper (never lighter than this on paper) |
| Plate | `#1B1F27` | Dark sign and plate backgrounds |
| On plate | `#F3F4F6` | Text on dark plates |
| Housing | `#2A2F3A` | Key housings, frames |
| Teal | `#2FA39A` | General interaction, primary affordance, hover |
| Lavender | `#8C7BD6` | Secondary / reflective actions (why, details) |
| Info blue | `#4C85DB` | Information, the Researcher's role colour |
| Success | `#2E9B5F` | Confirmed success, approve, the Developer's role colour |
| Coral | `#D9534A` | Reject, failure |
| Amber | `#E0A030` | Pending human decision, SCRIPTED DEMO band, the Tester's role colour |

State is never shown by colour alone: every coloured state also has words or a distinct shape.

### Typography

One family: **Inter** (SIL Open Font License), TextMeshPro asset at `Assets/MuseDesk/Art/Fonts/Inter SDF.asset` (created by the coordinating session; LiberationSans is its fallback). Builders do not set fonts; the coordinator applies the font to every label afterwards. Use TMP `FontStyles.Bold` for weight.

Labels are authored at **transform scale 0.01**, where TMP font size N renders glyphs about **N millimetres** tall and a rect of (W, H) is W x H centimetres. (TextMeshPro auto-sizing does not work at font sizes below 1, which is why everything uses this convention.)

| Style | Size (mm) | Weight | Use |
|---|---|---|---|
| Display | 13 | Bold | The one question on a paper |
| Heading | 10 | Bold | Card titles, panel titles |
| Body | 7.5 | Regular | Summaries, explanations |
| Label | 6.5 | Bold | Key labels, chips, column headers |
| Caption | 5.5 | Regular | Provenance, metadata. **Nothing smaller than 5.5 mm** |

Rules: enable auto-sizing with min = 0.72 x max; `TextOverflowModes.Ellipsis`; wrap body text, never wrap labels; line length at most about 45 characters; text sits **0.5 mm in front of the surface it is printed on** (never floating further: more than about 1 mm reads as detached in stereo); ASCII letters, digits and basic punctuation only (no check marks, arrows, bullets or other symbols).

### Shape and material

- Soft rounded rectangles. The Interaction SDK ships a rounded-panel quad material used by every poke key's `ButtonVisual` (`RoundedBoxProperties` + `MaterialPropertyBlockEditor`); for new plates, clone an existing plate/quad object in the scene rather than inventing geometry. Plain quads are acceptable for small plates.
- Materials are URP **Unlit** (text plates, paper, labels) or **Simple Lit** (3D keys). Opaque wherever possible. No new transparent materials except the existing hover frame / halo ones. No shadows, no emission keywords, no per-object material instances at runtime: tint with a `MaterialPropertyBlock`.
- Key housings and caps: reuse the existing `Housing3D` / `Cap3D` structure by cloning an existing key (for example `FocusToggle`).

## 5. Project gotchas that have already cost builds

1. **Rig bones are inside an armature scaled by 100.** Anything parented under a character bone must divide its scale and collider sizes by the parent's `lossyScale`. Prefer parenting to the agent's root (scale 1) and following a bone's position.
2. **Interaction SDK interactables that are inactive at start-up do not register.** A poke key or grabbable under an object that starts inactive will not work when shown. Either keep it active and hide it another way (scale to near zero, move it away), or add the project's `ReRegisterInteractable` component (read `Assets/MuseDesk/Scripts/Workspace/ReRegisterInteractable.cs`) and list the interactables.
3. **Poke keys** are wired through `InteractableUnityEventWrapper._whenSelect` persistent calls; clone an existing key and rewire (see `FinalPass.Key` / `Rewire`). A key's press visual travels from its rest offset to the poke surface plane; `PokeVisualRestGuard` (on each `ButtonVisual`) holds the rest pose and must be given the new rest position if you move a `ButtonVisual`. Do not move `ButtonVisual`, `Cap3D` or `Housing3D` relative to their key.
4. **A hand grab needs a trigger collider** on the grabbable; several grabbables are triggers by design.
5. **Text depth.** Text more than about 1 mm in front of its surface looks detached on the headset and looks fine in an Editor capture. Measure the surface (mesh bounds in the parent's space) and place text 0.5 mm proud.
6. **Card and board geometry is not centred on its pivot.** Measure mesh bounds; do not assume.
7. **Never use `??` or `?.` on Unity objects**; use explicit `== null`.
8. **Reducer events are the only source of truth for visuals.** `SessionController.Reducer` (v1: `AgentChanged`, `DecisionRequested`, `DecisionResolved`, `ArtifactCreated`, `TaskCompleted`, `WorkDelivered`, `TaskFailed`, `SessionReset`, `SnapshotLoaded`) and the board's `Phase2Bootstrap.Reducer` (v2: `TaskChanged`, `SnapshotLoaded`, ...). Views bind lazily in `Update` (`if (!_bound && controller != null) Bind();`) and unsubscribe in `OnDisable`. Every view must clear itself on `SessionReset` (demo replay).

## 6. Who owns what

| Agent | Area | Builder |
|---|---|---|
| Board | Compact task dock, expandable board, simplified cards, contextual card toolbar, assignment feedback | `AgentScripts/BuildTaskDockV2.cs` |
| Paper | Decision paper redesign (small and expanded states, two primary actions, secondary chips, stamp flow, transitions) | `AgentScripts/BuildPaperV2.cs` |
| Office | Agent nameplates replacing floating panels, mode plaque, decor curation, removal of loose keys, completion receipt with Replay, onboarding, audio feedback, archive via the out-tray | `AgentScripts/BuildOfficeV2.cs` |
| QA | Automated XR visual checks, design documents | `AgentScripts/XrVisualQa.cs` |

The coordinating session runs the builders in this order: Office, Board, Paper, then QA, and applies the font last.
