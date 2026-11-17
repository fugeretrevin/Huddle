# MuseDesk spatial interaction guidelines

Written 2026-11-12 (late) by the QA agent of the UX redesign. For Quest 3, seated, bare hands (hand tracking), passthrough. Shared values come from `docs/Design/REDESIGN_BRIEF_FOR_AGENTS.md`. The visual language is in `docs/Design/MUSEDESK_DESIGN_SYSTEM.md`. The automated check is `AgentScripts/XrVisualQa.cs`.

Every rule below names the defect that caused it. The defects are recorded in `docs/PROJECT_STATE_FOR_REVIEW.md` (section 5), `docs/Engineering/FINAL_XR_VALIDATION.md` (sections 4 to 6) and the header comments of `AgentScripts/FeedbackPass3.cs` and `AgentScripts/FeedbackPass4.cs`. All of them passed every automated check and every Editor capture that existed at the time, and were found by a person wearing the headset.

Nothing in this document was verified on the headset by this agent. Numbers marked (target) are recommendations.

## 1. Coordinate convention

Everything lives under `WorkspaceRoot` (scale 1). `WorkspacePlacement` puts it in front of a seated user.

- Root-local axes: **+Z points at the user**, +Y up. The **user's right is -X**; their left is +X.
- The user's eye is at about root-local **(0, 0.30, 0.62)**, looking toward -Z and slightly down.
- Agents face +Z (toward the user).
- TextMeshPro text and Interaction SDK poke keys present their **local -Z** to the reader or finger. To face the eye, a child's +Z must point away from the eye: `Quaternion.LookRotation(pos - eye, Vector3.up)` (flatten the direction for a yaw-only turn).
- Unity's built-in Quad is visible from its -Z side. Do not rotate quads 180 degrees to "face the user".

Motivating defects: mirrored and backward-facing text and keys in earlier passes; the scene facing convention was only settled on 2026-11-12 after agents had been built facing away from the user. `XrVisualQa` check 4 fails any text whose readable side is more than 75 degrees off the line to the eye, and check 6 fails a key whose normal is more than 60 degrees off.

Reference layout (root-local metres, from the brief):

| Thing | Position |
|---|---|
| Researcher / Developer / Tester stations | x = -0.30 / 0.00 / +0.30, z about 0. Agents 0.31 m tall. Desk tops about y = 0.15 |
| Software factory | (0, 0, 0.25), 0.12 m tall |
| Stamps on a rest pad | (+/-0.036, 0.014, 0.415) |
| Paper hand-over spot `DeliveryZone` | (-0.20, 0, 0.17) |
| `InboxTray` / `OutboxTray` | (-0.32, 0, 0.25) / (0.32, 0, 0.25) |
| `TaskBoard` (before the redesign) | (0.58, 0.40, 0.48), yawed to the eye, 0.62 m wide |

## 2. The three zones

### Zone A: the miniature office (the focus)

- Where: z from about -0.10 to 0.30, x within +/-0.45, y from the desk plane to about 0.40. Distance from the eye 0.40 to 0.75 m.
- What lives here: the three stations and agents, the factory line, the trays, nameplates on the desk fronts.
- Interaction: looking, and occasional touch (rest a fingertip on an agent's head, push a prop). Nothing the main flow requires is pressed here, because the agents are 0.64 to 0.75 m from the eye, beyond comfortable reach.
- Rule: no control, panel or sign may stand between the eye and an agent. `XrVisualQa` check 9 warns when the line from the eye to a key or a text passes through an agent, station, factory or board box, and check 8 reports any box of one major group intersecting another.

Motivating defects: the task board overlapped the Tester's station and had to be moved twice; a guide sign crossed the bottom of the paper preview; floating black status panels and a large mode sign competed with the agents; reach to the agents themselves was flagged as a known gap (0.64 to 0.75 m).

### Zone B: contextual interactions (the reachable front band)

- Where: z from 0.36 to 0.50, x within +/-0.30, y from the desk plane to about 0.25. Distance from the eye about 0.15 to 0.50 m (the stamps are at 0.35 m), below the line of sight to the agents.
- What lives here: the stamps on their rest pad, the decision paper once taken (it is in the hand), the task dock, the completion receipt, and whatever one or two keys the current moment needs.
- Rule: a control appears here only while it is relevant and leaves when it is not. At idle at most 6 poke keys are visible in the whole scene (`XrVisualQa` check 11 lists them).
- Rule: things in zone B stay low. Anything taller than about 0.25 m at z 0.40 starts to cover the agents' faces from the seated eye (target; confirm with the occlusion check and a capture from the eye point).
- Rule: zone B is the only place a frequently pressed control may be.

Motivating defects: about ten loose keys in arcs on the right and left; controls at 0.60 m; the assign dock had to be added because reaching to the agents was not comfortable.

### Zone C: secondary management (collapsed until asked for)

- Where: to the sides, beyond +/-0.40 in x, at 0.50 to 0.65 m from the eye, yawed to face the eye.
- What lives here: the full task board, the decision archive, the artifact panel.
- Rule: collapsed at idle, and collapsed means hidden by scale (section 9), not deactivated. Opened from a control in zone B; closes itself or is closed from zone B.
- Rule: when open, it must not intersect any zone A group and must not cover an agent from the eye point.
- Rule: anything in zone C that must be grabbed (a card) has a distance grab, because the far edge of a 0.62 m board is out of reach.

Motivating defects: the board at 0.52 m then 0.60 m from the eye; the board overlapping an agent; far-grab applied to thirteen decor props instead of only the things that need it.

## 3. Reach and comfort envelopes

Measured from the seated eye point, straight-line distance.

| Envelope | Distance from the eye | What may be there |
|---|---|---|
| Too close | under 0.25 m | Nothing fixed. A held paper may come this close by the user's own choice |
| Comfortable | 0.30 to 0.55 m | Everything pressed or grabbed in the main flow |
| Stretch | 0.55 to 0.65 m | Rarely used controls only. `XrVisualQa` warns |
| Out of reach | beyond 0.65 m | No poke key. A grabbable only with a distance grab. `XrVisualQa` fails a key or a core grabbable here |

Other comfort numbers:

- Vertical: things the user reads sit between 10 degrees above and 35 degrees below eye level; things pressed often sit 15 to 40 degrees below eye level so the arm is not held up (target).
- Horizontal: frequent controls within 35 degrees either side of straight ahead; zone C may sit at up to 60 degrees and requires a head turn (target).
- Key normal within 60 degrees of the line to the eye, so the finger approaches roughly along the normal and the press registers.
- Key face at least 22 mm in the smaller dimension (18 mm is a failure), at least 8 mm between neighbouring faces. Hand tracking jitters by several millimetres at arm's length.
- Text beyond 0.6 m: a capital letter subtends at least about 0.6 degrees. That is about 9 mm em height at 0.6 m and about 11 mm at 0.75 m.
- Nothing required sits behind another object from the eye point.

Motivating defects: "controls out of reach"; reach listed as a known gap (board at 0.60 m, agents at 0.64 to 0.75 m); key label size "sized by calculation, seen only in a capture".

Limits of these numbers: the eye point is an assumption. A taller or shorter user, a different chair height or a workspace placed slightly further away all change real reach. Treat 0.55 m as a design limit with no margin left in it.

## 4. Text depth and scale

### The 0.01-scale convention

- Every label is a TextMeshPro object at **transform scale 0.01**. Font size N then renders glyphs about N mm tall, and a rect of (W, H) is W x H centimetres.
- Reason: TextMeshPro auto-sizing does not work at font sizes below 1. Labels authored at world scale with font sizes like 0.07 could not auto-size and overflowed.
- The label's lossy scale must be uniform (x equals y within 5 percent). A label under a non-uniformly scaled parent is stretched.
- Sizes: Display 13, Heading 10, Body 7.5, Label 6.5, Caption 5.5. Nothing under 5.5 mm. Auto-size minimum 0.72 x maximum; ellipsis overflow; labels do not wrap.
- When a label's parent is not at scale 1, the label's local scale is 0.01 divided by the parent's lossy scale, so the world result is still 0.01.

Motivating defects: card text overflowing its card (2026-11-12 afternoon); "same units problem as the card labels" on key labels, fixed by moving them to 1/100 scale; small text not drawn in Editor captures.

### 0.5 mm proud

- Text sits **0.5 mm in front of the surface it is printed on**, measured along the text's own normal from the actual mesh surface under it.
- 0 to 1.0 mm passes. 1.0 to 2.5 mm is a warning. More than 2.5 mm, or text behind the surface, is a failure (`XrVisualQa` check 3).
- Measure, do not assume. Card and board geometry is not centred on its pivot: the card body's front face is at card-local z = +1.0 mm and its header strip at +0.3 mm. Find the surface from the mesh (bounds or vertices in the parent's space) and subtract 0.5 mm toward the reader.
- The text plane must be parallel to the surface. A label tilted against its plate is flush at one end and floating at the other; check 3 samples five points and warns when the gap varies by more than 1 mm.
- Text closer than about 0.15 mm to its surface can z-fight on the headset. 0.5 mm is the value that has been used; do not go lower without looking on the headset.
- A label on a key rides on the cap (child of `ButtonVisual`) so the gap stays constant while the key is pressed.

Motivating defects: card text 6.5 mm in front of the card (about 1 cm after scaling), which read as detached in stereo and was invisible in a flat capture; column labels and the board title floating about 2 cm in front of their header blocks and 7 mm low; labels not centred on their header blocks; labels left on the surface plane and hidden by the cap.

## 5. Poke key construction

Structure (do not change it; clone an existing key such as `FocusToggle` and rewire):

```
Key root            PokeInteractable; the poke surface plane is key-local z = 0; the user is on -Z
  Housing3D         fixed; rim just behind where the cap face stops
  ButtonVisual      PokeInteractableVisual + PokeVisualRestGuard; rests at key-local z = -0.22
    Cap3D           the moving cap
    label           TextMeshPro, 0.5 mm proud of the cap face
```

How the press works (read from the Interaction SDK source, `PokeInteractableVisual.cs`): at `Start` the visual records its offset from `_buttonBaseTransform` along the base's -forward. While a poke interactor is present the visual is placed at the finger's depth, clamped between that rest offset and zero, so it travels from its rest position to the surface plane and no further. Full press is therefore "the visual at the surface plane", and travel in metres is the rest offset times the key's world scale.

Rules:

1. **Travel 3 to 15 mm in world units.** Under 3 mm the press is not visible; over 15 mm the finger must push through too much air. A 50 mm key at scale 0.05 with a rest offset of 0.22 travels 11 mm. A key cloned at a smaller scale travels proportionally less: check it.
2. **The cap face stays in front of the housing rim at full press.** Compute the cap's front face at rest, subtract the travel, and compare with the housing's front-most point along the same axis. `FeedbackPass4` set the rim about 0.8 mm behind the stopped face on a 50 mm key.
3. **The back of the cap stays inside the housing at rest**, so the cap is not seen floating clear of the housing from the side. With a cap body 0.276 deep, a travel of 0.30 made this impossible together with rule 2; 0.22 satisfies both.
4. **`PokeVisualRestGuard.restLocalPosition` equals the authored rest position of `ButtonVisual`**, with `hasAuthoredRest` true. If a builder moves `ButtonVisual` and does not update the guard, the guard snaps the cap back to the old position every frame.
5. **Do not move `ButtonVisual`, `Cap3D` or `Housing3D` relative to their key.** Move or scale the key root, uniformly.
6. **The label is a child of `ButtonVisual`**, non-empty, 0.5 mm proud of the cap face.
7. **Wiring** goes through `InteractableUnityEventWrapper._whenSelect` persistent calls. A cloned key keeps its template's target until rewired.
8. **The key root faces the eye**: its -Z toward the eye, within 60 degrees.
9. **Never write a per-frame guard against SDK state without reading the SDK type.** The first rest guard cast the visual's private `HashSet<PokeInteractor>` to the non-generic `ICollection`, which `HashSet` does not implement. The cast was always null, the guard concluded no finger was ever present, and it reset the cap every frame. No key visibly pressed. The current guard asks the `PokeInteractable` for its `State` and fails open.

Motivating defects: flat square buttons; keys that no longer pressed in (rule 9); a pressed cap ending about 1 cm inside its housing so the face vanished (rule 2); a cap left floating after a press (rules 3 and 4); labels hidden by the cap (rule 6); a key authored at a third of the travel of the others (rule 1).

What the script checks: `XrVisualQa` check 6 measures rules 1, 2, 3, 4, 6 and 8 and the face size for every visible key. It cannot check that the press registers, that the cap returns, or how the travel feels.

## 6. Grab rules

1. **A hand grab needs a trigger collider on the grabbable.** Several grabbables are triggers by design. A grabbable with only solid colliders, or with none, has not been grabbable in this project. `XrVisualQa` check 7 fails a grabbable with no active collider and warns for a core item with no trigger.
2. **Distance grab only on core items: the decision paper, task cards, and the two stamps.** Everything else is grabbed by hand or only pushed. `XrVisualQa` warns about a distance grab on anything else.
3. **A grabbable beyond 0.65 m from the eye must have a distance grab** or must not be needed by the flow.
4. **Hover feedback before the grab.** A core grabbable shows its hover frame when a hand is close enough to grab, so the user knows the grab will land.
5. **A grabbed object owns its transform.** Layout code does not move a card or paper while it is held (`BoardPresenter.LayoutRegion` skips a held card and records its home slot instead).
6. **Released objects go somewhere defined**: a card returns to its slot or to the lane it was assigned to; a decided paper files to the out-tray; props tidy themselves; an undecided paper that is let go stays readable and within reach rather than falling or drifting away (target: not verified as built).
7. **A stamp counts only on a paper the user has taken and had open for 0.8 s.** Stamping the paper while it is still in the Researcher's hand does nothing.
8. **No grab starts a backend action by itself.** The drop is a request; the card shows the outcome after the backend answers.

Motivating defects: hands passing through objects with no response (2026-11-12 afternoon); far-grab applied to thirteen decor props, stealing grabs meant for something near; the paper not responding to a grab when its interactables had been inactive at start-up; a stamp approving a paper the user had not read.

## 7. Rig-bone parenting

**Do not parent anything under a character bone.** The rig bones are inside an armature scaled by 100. Anything parented under a bone inherits that scale: a 6 cm object becomes 6 m, or, if someone compensates by eye, renders at a slightly wrong size and its colliders are wrong by the same factor.

- Parent to the agent's root (scale 1) and follow a bone's world position each frame. The carried paper rides on `PaperHoldPoint` / the hand socket position from the agent root, not under the hand bone.
- If something truly must sit under a bone, divide its scale and every collider size by the parent's `lossyScale`, and record why.
- A socket (an empty transform used only for its position, such as `RightHand_Attach`) under a bone is fine: it has no size.

Motivating defects: the paper not visible in the agent's hand (parented to a bone inside the 100x armature, low on the chest, 6 cm across); the carried paper and the hand-off token 100 times too large.

What the script checks: `XrVisualQa` check 5 reports every renderer, collider and text that has an ancestor with a lossy scale above 20 or below 0.02 and is not part of the imported model, and fails the uncompensated ones. It cannot tell whether a compensated object is the size the designer intended.

## 8. Billboards

`FaceUser` turns an object toward the head each frame (yaw only, upright).

1. **Billboard only free-standing things** that have no surface behind them: a name above an agent, a free-floating readout. Never billboard text that is printed on a surface, a key, a plate attached to a desk, or anything inside a grabbable. Printed things are oriented once, at build time, toward the seated eye.
2. **Yaw only.** A billboard that pitches follows every nod and never looks attached to the world.
3. **Never billboard a poke key or its parent while it can be pressed.** The key turns away from the finger as the head moves and the press axis changes mid-press. If a panel with keys must face the user, orient it once when it opens.
4. **A billboard in the Editor is not at its runtime orientation.** `FaceUser` runs in `LateUpdate` against `Camera.main`; in the Editor the object keeps whatever rotation it was saved with. Facing and occlusion results for billboarded objects are therefore warnings in `XrVisualQa`, to be confirmed on the headset.
5. **Check the sign.** `FaceUser.forward` selects which local axis ends up toward the user. TextMeshPro reads from its -Z side. A billboard set the wrong way shows mirrored or hidden text and looks correct from the Editor's default camera only by accident.
6. **Prefer no billboard.** The redesign replaces the floating status panels and name tags with nameplates fixed to the desks, which need none.

Motivating defects: text mirrored or facing away in earlier passes; a board with a `FaceUser` billboard carrying pressable keys; floating panels that read as detached from the office.

## 9. Hide by scale (Interaction SDK registration)

**Interaction SDK interactables that are inactive at start-up do not register.** A poke key or grabbable under an object that starts inactive will not work when it is later shown.

Rules:

1. To hide something that contains a poke key or a grabbable, keep it active and scale its root to near zero (0.001 or smaller), or move it away. Do not call `SetActive(false)` on it or on an ancestor.
2. Store the authored (expanded) scale in a serialized field on the component that collapses and expands the group, with a name that says so (for example `expandedScale`). `XrVisualQa.RunExpanded` looks for such a field to measure the group at its real size; without one it can only report that the group was skipped.
3. If something must start inactive (the decision paper does), add `ReRegisterInteractable` and list every interactable on it. `XrVisualQa` warns about any inactive interactable that no `ReRegisterInteractable` lists.
4. After a scale change, key visuals must re-sync. `PokeVisualRestGuard` re-reads the rest offset when its lossy scale changes; a key that expands from near zero without the guard keeps a rest offset captured at the tiny scale and does not press correctly.
5. A collapsed group must not leave anything behind: no label, collider or halo outside the scaled root.
6. Collapsed is not the same as gone for physics and layout. A collapsed group still has colliders at near-zero size at its pivot. Keep the pivot of a collapsed group away from pressable keys.

Motivating defects: the paper never responding to a grab because its interactables were inactive when the interaction service initialised; a cap left at a stale offset when a button's parent unfolded from a smaller scale.

## 10. What Editor captures cannot verify

An Editor capture is a flat, single-eye image from an assumed eye point, with no hands, no tracking and no runtime. Every item below has produced a defect that looked correct in a capture.

| Cannot be seen in a capture | The defect it hid | What partly covers it now |
|---|---|---|
| Depth between text and its surface (stereo) | Card text 6.5 mm proud; header labels 2 cm proud | `XrVisualQa` check 3 |
| True world size of things under a scaled parent | Paper under a 100x bone | `XrVisualQa` check 5 |
| Key press travel and where the cap stops | Cap inside the housing at full press | `XrVisualQa` check 6 (geometry only) |
| Whether a key presses at all | Rest guard resetting the cap every frame | Nothing. Headset only |
| Reach from a real seated body | Board at 0.60 m, agents at 0.64 to 0.75 m | `XrVisualQa` check 7 (assumed eye point) |
| Overlap seen from other angles | Board overlapping the Tester's station | `XrVisualQa` check 8 (boxes) |
| Legibility at the display's resolution | Small text; some text is not even drawn in captures | `XrVisualQa` check 1 (size only) |
| Runtime text length | Card text overflowing | `XrVisualQa` check 2 (Editor text only) |
| Billboard orientation at runtime | Mirrored or turned text | Nothing. Headset only |
| Interactable registration | Paper not grabbable | `XrVisualQa` hidden-section warning (inactive only) |
| Hand occlusion, hover frames, halos, finger solver | Hands passing through; possible hand flicker | Nothing. Headset only |
| Animation | Agents that had only ever played idle | Nothing. Headset only |
| Passthrough contrast, brightness, colour | Not yet reported | Nothing. Headset only |
| Frame rate, judder, heat | Never measured | Device log |
| Audio | Not yet reported | Nothing. Headset only |
| Stability of placement (drift, re-placing) | Office re-placing itself | Nothing. Headset only |

A passing `XrVisualQa` run means the measured geometry is inside the thresholds. It does not mean the change works on the headset.

## 11. Per-change on-head checklist

Do this for every change that touches the scene, before calling it done. It takes about five minutes. Run `XrVisualQa.Run` first and clear the FAIL list; then put the headset on, sit at a desk, and say what you see for each line.

Placement and focus

1. Does the office appear in front of you on the desk plane, about 60 cm away, and stay put for a minute?
2. Are the three agents the first thing you look at? Is anything in front of an agent's face or body?
3. Count the keys you can see without doing anything. Is it 6 or fewer?

Text

4. Read every piece of text the change touched, at the distance you would normally read it. Can you read it without leaning in?
5. Close one eye, then the other, while looking at each changed label. Does the text stay on its surface, or does it shift against it (floating)? Does any text flicker or shimmer (z-fighting)?
6. Is any text mirrored, cut off, overlapping other text, or off-centre on its block?
7. Does every card, paper, artifact and receipt you can see say LIVE or SCRIPTED DEMO?

Keys

8. Press every key the change touched, slowly. Does the cap sink into its housing, stay visible at the bottom, and return when you lift your finger?
9. Press it quickly five times. Does every press register once? Is any cap left pressed in?
10. Can you press each key with your elbow on or near the desk, without leaning forward?
11. Do you ever press a neighbouring key by accident?

Grabbing

12. Reach for each grabbable the change touched. Does a frame light up before you grab? Does the grab land the first time?
13. For the paper, cards and stamps: point from out of reach and pinch. Does it come to your hand? Does that ever steal a grab meant for something near?
14. Let go. Does the object go where it should?

Size and overlap

15. Is the paper in the Researcher's hand a sensible size for a 31 cm agent (about 7 cm), and visible?
16. Lean left and right. Does anything intersect anything else, or float clear of what it should be attached to?
17. Open and close anything that collapses (board, dock, details). Does it open to the right size, and do its keys work immediately after opening?

Honesty and outcome

18. Did anything show a result (approved, started, completed, a chime, a token) before the backend could have answered?
19. After a reject: is there no success sound, no token, no PASSED lamp?

Comfort and performance

20. Any stutter or judder when you move your head? Any hand flicker? Roughly when?
21. After three minutes: is your arm, neck or eyes tired from where things are?

Then pull the device log while the headset is still connected (commands are in `docs/Engineering/FINAL_XR_VALIDATION.md` section 4) and record what was seen, with the commit, in the on-head record table of `docs/PROJECT_STATE_FOR_REVIEW.md`. "Not seen on the headset" is a valid and required entry when that is the truth.
