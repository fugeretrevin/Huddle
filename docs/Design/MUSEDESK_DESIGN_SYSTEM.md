# MuseDesk design system

Written 2026-11-12 (late) by the QA agent of the UX redesign. Source of truth for the shared values is `docs/Design/REDESIGN_BRIEF_FOR_AGENTS.md`; the tokens below are copied from it verbatim. Spatial rules (zones, reach, depth, key construction) are in `docs/Design/SPATIAL_INTERACTION_GUIDELINES.md`. The automated check for most numeric rules here is `AgentScripts/XrVisualQa.cs`.

How to read this document:

- A value with no mark is either a token fixed by the brief or something read from the code or builder scripts in this repository.
- **(target)** means a recommendation that was not verified as built when this was written. The redesign builders (`BuildOfficeV2`, `BuildTaskDockV2`, `BuildPaperV2`) were being written in parallel, so every size for a component they create is a target until `XrVisualQa.Run` and a person in the headset confirm it.
- Nothing in this document has been confirmed on the headset by this agent.

## 1. Principles

1. **The office is the product.** Three agents at their desks are what the user came to see. Anything that is not an agent, a desk, or the thing the user is deciding on must justify the space it takes. Before the redesign the scene had a large board, three floating black panels, a floating mode sign, a guide sign and about ten loose keys around the agents; that is the picture this principle exists to prevent.
2. **Controls appear when relevant.** A control is visible only while its action is possible and useful. At idle the target is at most 6 poke keys in view (`XrVisualQa` check 11 counts them). Management views (the full board, the archive) stay collapsed until asked for.
3. **Words, not colour alone.** Every coloured state also has a word or a distinct shape. Approve is green and says "Approve"; a pending decision is amber and says what is pending. A user who cannot tell teal from green loses nothing.
4. **Honesty labels.** Scripted demo activity is never presented as live work. Every card, paper and artifact shows LIVE or SCRIPTED DEMO in words. The interface shows an outcome only after the reducer applies the confirming event. A template recommendation says it is a template. These are binding invariants from `docs/AGENTS.md`, not style choices.
5. **Physical, small and calm.** Things are objects on a desk: paper, stamps, keys, plates. Nothing glows, nothing floats without a reason, nothing moves unless something happened.
6. **Measured, then seen.** A rule in this system counts as met only when it has been measured in the scene and seen on the headset. Editor captures are flat: they do not show depth offsets, stereo detachment, reach, or press travel.

## 2. Colour tokens

sRGB hex. Use these exact values.

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

### Pairings and contrast

Contrast ratios below are computed from the hex values with the WCAG 2.x relative-luminance formula. They describe the tokens, not what the headset shows: passthrough behind an unlit surface and the display's own brightness change perceived contrast.

| Text on background | Ratio | Use |
|---|---|---|
| Ink on Paper | 13.7:1 | All primary text on paper and cards |
| Ink soft on Paper | 6.8:1 | Secondary text on paper. Nothing lighter on paper |
| On plate on Plate | 15.0:1 | All text on dark plates |
| On plate on Housing | 12.2:1 | Text on frames |
| Ink on Amber | 6.8:1 | SCRIPTED DEMO band, pending labels |
| Ink on Teal | 5.0:1 | Primary key label |
| Ink on Success | 4.4:1 | Approve key label. Bold only |
| Ink on Lavender | 4.3:1 | Secondary chip label. Bold only |
| Ink on Info blue | 4.2:1 | Information chip label. Bold only |
| Ink on Coral | 3.9:1 | Reject key label. Bold, Label size or larger only |

Rules:

- Text on a coloured key cap is always **Ink**, never white. White is 2.3:1 on Amber and 3.1:1 on Teal; on Success, Lavender, Info blue and Coral white is between 3.5:1 and 4.0:1, no better than Ink, so one rule covers every cap.
- None of the coloured caps reaches 4.5:1 with either text colour except Teal and Amber. That is why cap labels are Bold at Label size (6.5 mm) or larger and why a cap never carries Caption or Body text. If a cap label proves hard to read on the headset, put the word on a Paper-coloured cap with a coloured frame instead (target).
- Role colours (Info blue, Success, Amber) identify an agent on its nameplate and its cards. Success and Amber therefore carry two meanings each (Developer / approve, Tester / pending). The word always disambiguates: a nameplate says the role name, a state says the state.
- Coral is reserved for reject and failure. Do not use it for emphasis.
- No other colours. A new need gets a word, not a new hue.

## 3. Typography

One family: **Inter** (SIL Open Font License), TextMeshPro asset `Assets/MuseDesk/Art/Fonts/Inter SDF.asset`, with LiberationSans as its fallback. Builders do not set fonts; the coordinating session applies the font to every label after the builders run. Weight is TMP `FontStyles.Bold` or regular; no other weights.

Labels are authored at **transform scale 0.01**, where TMP font size N renders glyphs about **N millimetres** tall and a rect of (W, H) is W x H centimetres. TextMeshPro auto-sizing does not work at font sizes below 1, which is why everything uses this convention.

| Style | Size (mm) | Weight | Use |
|---|---|---|---|
| Display | 13 | Bold | The one question on a paper |
| Heading | 10 | Bold | Card titles, panel titles |
| Body | 7.5 | Regular | Summaries, explanations |
| Label | 6.5 | Bold | Key labels, chips, column headers |
| Caption | 5.5 | Regular | Provenance, metadata. **Nothing smaller than 5.5 mm** |

Rules:

- Enable auto-sizing with min = 0.72 x max. The minimum of the smallest style is still 5.5 mm: a Caption does not auto-size down (set min = max), and a Label's floor of 0.72 x 6.5 = 4.7 mm is a last resort for an unexpectedly long runtime string, not a design size. `XrVisualQa` warns when the auto-size floor is under 5.5 mm and fails when the size in use is under 4.5 mm.
- `TextOverflowModes.Ellipsis` everywhere, so an over-long string is cut cleanly instead of spilling off its surface.
- Wrap body text. Never wrap labels (`TextWrappingModes.NoWrap`).
- Line length at most about 45 characters.
- Text sits **0.5 mm in front of the surface it is printed on**. Never further: more than about 1 mm reads as detached in stereo.
- ASCII letters, digits and basic punctuation only. No check marks, arrows, bullets or other symbols: they are missing from the font atlas or render as boxes.
- Sentence case for sentences, upper case only for the two provenance words LIVE and SCRIPTED DEMO and for short stamps (APPROVED, REJECTED).
- Far text needs more than the minimum. Beyond 0.6 m from the eye a capital letter should subtend at least about 0.6 degrees, which is about 9 mm of em height at 0.6 m and 11 mm at 0.75 m (the distance of the agents). Use Heading size or larger for anything read at the agents' distance (target).
- One alignment per surface. Labels on keys, chips and header blocks are centred on the block they sit on, measured from the mesh, not from the pivot.

Why these rules exist: card text overflowed its card on 2026-11-12 (afternoon); labels were sized by calculation and seen only in captures; three key labels did not draw at all in an Editor capture. Size, overflow and position are now measured by script.

## 4. Shape and material

- Soft rounded rectangles. The Interaction SDK ships a rounded-panel quad material used by every poke key's `ButtonVisual` (`RoundedBoxProperties` + `MaterialPropertyBlockEditor`); for new plates, clone an existing plate/quad object in the scene rather than inventing geometry. Plain quads are acceptable for small plates.
- Corner radius about 15 percent of the shorter side for plates and chips, 3 mm on paper and cards (target).
- Materials are URP **Unlit** (text plates, paper, labels) or **Simple Lit** (3D keys). Opaque wherever possible.
- No new transparent materials except the existing hover frame / halo ones. `XrVisualQa` check 11 lists every transparent non-text material so a new one is noticed.
- No shadows, no emission keywords, no per-object material instances at runtime: tint with a `MaterialPropertyBlock`.
- Key housings and caps reuse the existing `Housing3D` / `Cap3D` structure by cloning an existing key (for example `FocusToggle`).
- Unity's built-in Quad is visible from its -Z side. Do not rotate a quad 180 degrees to "face the user".
- Thickness: paper and cards are thin (about 1 mm); plates 2 to 3 mm (target); nothing in the interface is a floating zero-thickness panel if it can be touched.

## 5. Component catalogue

Sizes are world millimetres. "Face" is the pressable or printed front. All text sizes refer to the typography table.

### 5.1 Poke key

Structure (as built, from `PolishPass2` and `FeedbackPass4`): a key root carrying `PokeInteractable`; a fixed `Housing3D` under the root; a moving `ButtonVisual` (with `PokeInteractableVisual` and `PokeVisualRestGuard`) that carries `Cap3D` and the label. The poke surface plane is at key-local z = 0 and the user is on -Z. `ButtonVisual` rests at key-local z = -0.22 and travels to the surface plane when pressed. The housing rim sits just behind where the cap face stops, so the cap is still proud of the rim at full press.

| Variant | Face (mm) | Travel (mm) | Label | Cap colour | Use |
|---|---|---|---|---|---|
| Primary | 50 x 50 as built for the existing keys (key scale 0.05); 56 x 32 for a wide primary (target) | 11 as built (0.22 x 0.05 m); allowed range 3 to 15 | Label 6.5 Bold, Ink, one or two words | Teal, or Success for Approve, Coral for Reject | The one or two actions that move the flow forward |
| Secondary | 40 x 26 (target) | 6 to 9 (target) | Label 6.5 Bold, Ink | Lavender or Paper | Ask why, Details, Changes |
| Chip | 34 x 22 (target) | 4 to 6 (target) | Label 6.5 Bold, Ink | Paper with a Housing frame | Filters, small toggles, toolbar actions on a card |

Rules for all variants:

- Smaller face dimension at least 22 mm; never under 18 mm.
- At least 8 mm between the faces of neighbouring keys.
- At full press the cap face stays in front of the housing rim.
- The label rides on the cap (it is a child of `ButtonVisual`), 0.5 mm proud of the cap face, never on the surface plane behind the cap.
- The key normal points at the seated eye within 60 degrees.
- A key that is not available is hidden (by scale, see the spatial guidelines), not greyed out. If it must stay visible while disabled, it changes its word ("Saving..."), not only its tint.

States:

| State | Visual | Other feedback |
|---|---|---|
| Rest | Cap proud of the housing | none |
| Hover (finger within the hover distance) | Cap tint moves 15 percent toward Teal via `MaterialPropertyBlock` (target) | none |
| Pressed | Cap follows the finger to the surface plane | Press click at the moment of select |
| Confirmed | The thing the key acts on changes after the reducer event; the key itself does not claim success | Confirm tone when the event arrives |
| Busy | Label changes to a word such as "Saving..." | none |
| Unavailable | Hidden | none |

Do: clone an existing key and rewire it. Give `PokeVisualRestGuard` the new rest position if `ButtonVisual` is moved. Don't: move `ButtonVisual`, `Cap3D` or `Housing3D` relative to their key; scale a key non-uniformly; put two words of different meaning on one key.

Defects that shaped this component: flat square buttons (2026-11-12 afternoon); keys that stopped pressing because a guard reset the cap every frame; caps ending about 1 cm inside the housing at full press so the face vanished.

### 5.2 Plate

A dark sign surface for a line or two of text that is not a control.

- Plate colour background, On plate text, Unlit, opaque.
- Height 22 to 30 mm for one line of Body or Label text; width to fit, at most 220 mm (target).
- Padding 4 mm on every side between the text rect and the plate edge (target).
- Text 0.5 mm proud of the plate face.
- Faces the seated eye; yaw-only billboard only if it is free-standing (see the billboard rules in the spatial guidelines).

Do: one message per plate. Don't: stack plates; float a plate above an agent's head (that is what the nameplate replaces).

### 5.3 Nameplate

Replaces the floating black `Monitor` status panels and the `NameTag` above each agent. It sits on the front edge of the agent's desk, attached to the station (not to the agent, so it does not move with a walk).

- About 110 x 30 mm, tilted back 20 to 30 degrees so its face points at the seated eye (target).
- Left: a role colour block about 8 mm wide (Info blue, Success, Amber). Right: the role name in Heading 10 Bold and one status line in Body 7.5 (target).
- The status line is the agent's real progress line from the reducer. In demo mode it is the scripted line; the SCRIPTED DEMO label lives on the mode plaque and on every card and paper, so the nameplate does not repeat it.
- At 0.64 to 0.75 m from the eye the role name must pass the 0.6 degree rule: 10 mm is the minimum at that distance, larger is better.

States: idle ("Ready"), working (the progress line), waiting for the user ("Needs your decision" with an Amber block), failed (word plus Coral block). Each state is a word first.

### 5.4 Mode plaque

Replaces the large floating `DiagnosticsText` mode sign. One small plate that always says which mode the app is in.

- About 90 x 22 mm, Plate background, low and to one side of the office, inside the user's view but outside the working area (target).
- Text: "LIVE" or "SCRIPTED DEMO" in Label 6.5 Bold. In demo mode the plaque carries an Amber band or border; in live mode a Success one. The word is the label; the colour is secondary.
- Never hidden, never collapsed. It is the one piece of chrome that is always present, because of the honesty invariant.

### 5.5 Paper (the decision document)

The signature interaction. As built before the redesign: carried by the Researcher at about 7.5 cm (`heldScale` 0.22 of a 34 cm sheet), riding in the right hand by position from the agent root; taking it unfolds it to a 34 cm sheet in 0.35 s; a stamp counts only on a paper the user has taken and had open for 0.8 s; a decided paper glides to the out-tray.

Redesign targets:

- Small state (in the agent's hand): about 75 mm tall, Paper colour, shows only an Amber corner or band so it reads as "needs you". No text that cannot be read at that size.
- Expanded state (in the user's hand): about 260 x 340 mm (target; 340 mm tall as built).
- Top band: SCRIPTED DEMO (Amber, Ink text) or LIVE (Success, Ink text), Label 6.5 Bold, full width, 14 mm tall (target).
- The one question in Display 13 Bold, Ink, at most three lines.
- A summary in Body 7.5, Ink, at most 45 characters per line.
- The recommendation with its provenance line in Caption 5.5, Ink soft: "Template recommendation. Not live AI research." when that is the case.
- Two primary keys only: Approve (Success) and Reject (Coral), at the bottom, at least 8 mm apart, each at least 22 mm in its smaller dimension.
- Secondary chips (Ask why, Details, Changes, Voice) in a row above the primary keys, Lavender or Paper.
- A marked stamp target circle about 45 mm across, labelled "Stamp here" (target).
- Details and the Ask-why answer appear on demand and never cover the stamp target.

States: offered (small, in the agent's hand), taken (unfolding), open (readable, decision possible after 0.8 s), saving ("Saving your decision..."), decided (APPROVED or REJECTED mark, then filing), revised (a new paper titled as a revision).

Do: keep every piece of text on the sheet surface, 0.5 mm proud. Don't: parent the paper or anything on it under a rig bone; show a decided state before the backend confirms; let a key overhang the sheet edge.

Defects that shaped it: the paper invisible in the agent's hand (parented to a bone inside a 100x armature); keys outside the sheet; stamps counting on a paper still in the agent's hand.

### 5.6 Card (task card)

- Paper colour face, about 60 x 40 mm at the board's authored scale (target). As built before the redesign the card template is scaled to fit its lane (scale at most 1.3) and rows are spaced at the card height plus 8 mm.
- Content, top to bottom: a role colour strip with the owner word; the title in Heading 10 Bold (two lines at most, ellipsis); the state word in Label 6.5 Bold; LIVE or SCRIPTED DEMO in Caption 5.5. Nothing else on the resting card (target). Priority and id move to the contextual toolbar.
- A card is never wider than its lane and never taller than the row pitch (`XrVisualQa` check 10).
- All text on the card's printed surface, 0.5 mm proud. The card mesh is not centred on its pivot: measure the mesh.

States: resting; hover (the existing hover frame lights behind it); held (follows the hand, toolbar hidden); pending ("Saving..."); confirmed (the canonical state word from the backend, for example "Researcher started it"); refused (a reason in words, for example "Starts with the Researcher"); planned ("Planned" in words: it will not run).

Don't: show a card as started before the backend says so; shrink text to fit more fields.

### 5.7 Dock (task dock)

The compact, always-reachable summary of the board (`TaskDock`). It lives in zone B.

- About 180 x 60 mm, Plate background, tilted to the eye (target).
- Shows the next runnable card (or a count per column in words: "1 ready, 3 planned") and one primary key to open the full board (target).
- The full board (`TaskBoard`) is zone C: collapsed until asked for, hidden by scale so its interactables stay registered.
- Assignment feedback appears on the dock in words ("Researcher started it"), not only as a colour flash.

### 5.8 Receipt (completion receipt)

Shown once when a run ends.

- Paper colour, about 120 x 150 mm, appears in zone B near the out-tray (target).
- States the outcome in words first: "Completed", "Rejected. Nothing was built.", or "Failed", then one line of what was produced, then LIVE or SCRIPTED DEMO.
- One primary key: Replay (demo) or a dismiss key. No success styling for a rejected or failed run.
- Appears only after the reducer applies `TaskCompleted`, `TaskFailed` or the resolved rejection.

### 5.9 Halo and hover frame

The only transparent materials allowed.

- Hover frame: a Teal outline that lights behind a grabbable (card, paper, stamp) when a hand is close enough to grab. It is a child of the grabbable and sits behind its face.
- Halo: a ring on a drop target. Teal when a drop would be accepted, Coral with a word ("Starts with the Researcher") when it would be refused. Because the two are distinguished by colour, the word is mandatory.
- Neither blocks a poke or a grab (no colliders), and neither is counted as a surface for text depth.

## 6. Motion

Durations and easing. "As built" values are code defaults read from the scripts; a scene may override them.

| Motion | Duration | Easing | Status |
|---|---|---|---|
| Key press visual | follows the finger (no animation) | none | as built (Interaction SDK) |
| Key hover tint, chip state change | 120 ms | ease-out | (target) |
| Expand / collapse of a panel, dock to board | 250 ms | ease-out | (target) |
| Paper unfold on take | 350 ms | ease-out | as built (`HeldPaperController.expandSeconds` = 0.35) |
| Stamp press | 160 ms | ease-in then settle | as built (`ApprovalStampController.pressSeconds` = 0.16) |
| Filing a decided paper to the out-tray | 600 to 900 ms | ease-in-out | (target); the code default is `DocumentDeliveryController.fileSeconds` = 0.55 |
| Receipt appear | 250 ms | ease-out | (target) |
| Agent turn to the user | follows the animator | none | as built |

Rules:

- Scale and position only. No fades that need a transparent material.
- Nothing moves toward the user's face. Things arrive from the office side and stop inside zone B.
- No motion loops at idle except the agents' own breathing and work clips.
- A collapse must end at a hidden-by-scale state, not at an inactive object (see the hide-by-scale rule).
- Motion never implies an outcome. The filing animation starts after the reducer applies the decision, not when the key is pressed.
- No per-frame allocation in any animation code.

## 7. Audio feedback

One short sound per kind of event, spatialised at the object that caused it. All (target): no audio mapping was verified as built when this was written, apart from the completion chime mentioned in the on-head checklist.

| Event | Sound | Notes |
|---|---|---|
| Key select | Short mechanical click, about 40 ms | At the key. Played on select, not on hover |
| Grab (paper, card, stamp) | Soft paper or wood pick-up | At the object |
| Paper unfold | Paper rustle, about 300 ms | Matches the 350 ms unfold |
| Stamp lands | Solid thump | Only when the stamp was accepted as a decision attempt |
| Decision confirmed by the backend | Two-note rising tone | When the reducer applies `DecisionResolved`, never on press |
| Rejection confirmed | Single low neutral tone | Not an error buzzer: reject is a valid decision |
| Card assigned and started | Soft tick at the agent | When the backend confirms |
| Command refused | Two short low notes | With the reason in words on the card or dock |
| Agent needs you | Gentle chime at the agent, once | With the exclamation mark and the walk-up |
| Run completed | The completion chime | Only for a real `TaskCompleted`; never after a reject |

Rules: sound is never the only signal (every sound accompanies a visible word or motion); nothing loops; nothing plays for a scripted event that would not play for the live one; confirm sounds wait for the confirming event.

## 8. Accessibility and comfort

| Rule | Value |
|---|---|
| Minimum text size | 5.5 mm em height; 4.5 mm is a hard failure |
| Far text | At least about 0.6 degrees per capital height beyond 0.6 m |
| Minimum key face | 22 mm in the smaller dimension; 18 mm is a hard failure |
| Minimum spacing between keys | 8 mm between faces |
| Key travel | 3 to 15 mm |
| Seated reach | Within 0.55 m of the eye for anything touched often; never beyond 0.65 m without a distance grab |
| Contrast | Use only the pairings in section 2; Ink on every coloured cap |
| Colour-only state | Not allowed: every state has a word or a distinct shape |
| Hands first | Every main-flow action works with bare hands, without controllers and without voice |
| Voice | Optional. Needs network and a token; never the only way to do anything |
| Motion | No flashing, no motion toward the face, no idle loops outside the agents |
| Reading posture | Text the user must read is between 10 degrees above and 35 degrees below eye level (target) |
| One-handed use | The paper can be held in one hand and decided with the other, or decided by stamp with one hand while it rests |

Not verified: contrast against real passthrough, legibility of 5.5 mm text on the headset at 0.5 m, and whether 22 mm keys are comfortable for a bare fingertip with hand-tracking jitter. These need a person in the headset.

## 9. Do and don't summary

Do:

- Start from an existing object (key, plate, quad) and clone it.
- Measure the surface from its mesh and put text 0.5 mm proud of it.
- Say the state in a word.
- Show LIVE or SCRIPTED DEMO on every card, paper, artifact and receipt.
- Hide by scale; keep Interaction SDK objects active.
- Run `XrVisualQa.Run` after every builder and read the FAIL list.

Don't:

- Add a floating panel, a new colour, a new font, a new transparent material or a symbol glyph.
- Use text smaller than 5.5 mm or lighter than Ink soft on paper.
- Parent anything under a rig bone.
- Leave a control visible when its action is not possible.
- Show success before the reducer event.
- Treat an Editor capture as proof of depth, reach, press travel or legibility.
