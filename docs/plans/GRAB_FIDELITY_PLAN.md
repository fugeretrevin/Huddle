# Grab Fidelity Plan — Quest 3 hand tracking polish

Target: make grabbing the **decision paper** and **task cards** feel genuinely great (finger-wrap
poses, snap-to-pose, two-handed inspect, poke/pinch clarity, visual+audio+haptic feedback). This is
the "Best Agentic Interaction" award surface. **Plan only — no code changed.**

Verified against the current Meta Interaction SDK docs (ISDK v83/v201+ reference pages), Nov 2026.

---

## 0. TL;DR — the one finding that matters + top 3 upgrades

**MOST IMPORTANT FINDING — can finger-wrap poses be authored without the headset?**
**Partly. _Recording_ a pose needs Link (live hand tracking in Play Mode); _editing_ one does not.**
Our `[[grab-poses-finding]]` memory ("finger-wrap needs an on-head recorded HandPose") is correct
about *recording from scratch*, but it missed a **device-free bootstrap path**:

1. **Duplicate an existing `HandGrabPose`** from the ISDK sample scenes (`PoseExamples` /
   `HandGrabExamples`) — or any pose we record once — into the paper/card prefab.
2. **Hand-tune the fingers entirely in Edit Mode**: selecting a `HandGrabPose` shows a GhostHand with
   circular flexion/abduction handles you drag in the Scene view (no device, no Play Mode).
3. **Mirror L/R with one click** ("Create Mirrored HandGrabInteractable").

So we can ship a *decent* finger-wrap pose with **zero headset time**, and a *perfect* one with a
single ~5-minute Link recording pass. Recommendation: bootstrap device-free now; refine on-head later.

**Top 3 fidelity upgrades (in priority order):**
1. **Finger-wrap snap pose on the decision paper** with `HandAlignment = AttractOnHover` — the hand
   visibly forms the grip as you reach for the sheet. Biggest single "this is real" win.
2. **Grab feedback trio** — hover highlight (`InteractableUnityEventWrapper.WhenHover` → emission),
   grab sound (reuse our `FeedbackController`), and a haptic pulse (`HapticClipPlayer` on select).
3. **Two-handed inspect on the paper** (`TwoGrabRotateTransformer`) + **fix poke-vs-grab on the cards**
   by moving the poke buttons off the grab body.

---

## 1. What we already have (do not rebuild)

- **Reflection-based grab rig** (no hard ISDK dependency): `HeldPaperController`, `GrabbableRigidbody`,
  and `TaskCardView` all read `Oculus.Interaction.Grabbable.SelectingPointsCount` by reflection.
- **Copied grab components**: `AddGrabbablePaper` / `BuildBoard.BuildCardTemplate` copy the proven
  `[BuildingBlock] Grabbable Item` cube's **`Grabbable` + `HandGrabInteractable`** onto the target and
  repoint `_pointableElement` (base-class field on `PointerInteractable<,>`) and `_rigidbody`.
- **Hard-won gotchas**: grab collider **must** be `isTrigger = true`; ISDK interactables inactive at
  init silently fail to register → `ReRegisterInteractable` toggles `enabled` off/on a frame after
  activation (we just extended it to the card's new poke `PokeInteractable`s).
- **Known limitation**: the copied cube grab is a **generic wrist grab with no `HandGrabPose`** — so
  fingers do NOT wrap. That is exactly the gap this plan closes.
- **Redundancy to watch**: `Grabbable` has a built-in **`Kinematic while selected` (default true)**.
  Our `GrabbableRigidbody` *also* toggles kinematic manually. Keep ours only for the zero-velocity
  release behavior; otherwise let the Grabbable own it (see §8 risk note).

---

## 2. Finding A — pose authoring: in-editor vs on-head (detailed)

| Step | Needs a headset/Link? | How |
|---|---|---|
| Record a brand-new pose from your real hand | **YES** (Link, Play Mode, Android target) | `Window > Meta > Interaction SDK > Hand Grab Pose Recorder`; set Hand (OVRHands), target, GhostProvider; Play → wrap hand → press **Record** (Space) → **Save To Collection** before exiting Play. |
| Load a saved pose, tune fingers | **No** | `Load From Collection` in Edit Mode; drag circular flexion/abduction handles on the GhostHand. Fingers set to `Free` in `HandPose.FingersFreedom` show no handles. |
| Bootstrap from a **sample** pose (device-free) | **No** | Duplicate a `HandGrabPose` GameObject (+ its GrabSurface) out of `PoseExamples`/`HandGrabExamples`, parent under our interactable, then tune handles. |
| Scale variants for big/small hands | **No** | `ScaledHandGrabPosesKeys` slider (0.8x / 1.2x) → "Add HandGrabPose Key at X scale", or duplicate the pose GameObject and change local scale. |
| Mirror to the other hand | **No** | "Create Mirrored HandGrabInteractable", or duplicate + flip `Handedness`. |
| Constrain *where* it snaps | **No** | Add `CylinderGrabSurface` / `BoxGrabSurface` / `SphereGrabSurface` (all `IGrabSurface`) to the pose and assign to the pose's **[Optional] Surface** field. |

**Pragmatic recommendation:** do the device-free bootstrap (duplicate a sample flat/sheet pose, tune
in Edit Mode) to ship fidelity immediately. Budget one optional 5-min Link recording session to
capture a natural paper grip and a natural card pinch, then tune those in Edit Mode. The paper is flat
→ a **`BoxGrabSurface`** along its edge makes the snap feel right regardless of exactly where you grab.

---

## 3. Plan B — add snap hand-poses to the paper and cards

**Decision paper (`DecisionDocument` root, built by `AddGrabbablePaper`):**
- Keep the copied `HandGrabInteractable`. Add **1–2 `HandGrabPose` children**:
  - A **palm/edge grip** pose (hold the sheet like paper) with a `BoxGrabSurface` running along the
    paper's long edge so the wrist can slide along it.
  - Set `HandAlignment = AttractOnHover` so the fingers progressively form the grip as the hand nears
    (more lifelike than `AlignOnGrab`, which only snaps the instant you grab).
  - `Supported Grab Type = All` (pinch a corner *or* palm the sheet).
  - `MovementProvider = MoveTowardsTargetProvider` (default 0.1s tween) — smooth, not rigid.
- This layers cleanly on `HeldPaperController` (still reads `SelectingPointsCount`; the pose only
  changes how the *hand* looks/snaps, not our grab-state logic).

**Task cards (`CardTemplate` in `BuildBoard`, pooled):**
- Cards are tiny (0.08×0.05). Add **one pinch `HandGrabPose`** (grab a card like a Post-it) with
  `HandAlignment = AlignOnGrab` and `Supported Grab Type = Pinch`.
- Pose finger data is cloned with the template on `Instantiate`, like our other serialized refs, so
  every pooled card inherits it. `ReRegisterInteractable` already re-registers the interactable on
  activation; **verify the `HandGrabInteractable`/`HandGrabPose` is in that re-register set too** (we
  currently register `Grabbable` + `HandGrabInteractable` + the poke `PokeInteractable`).

---

## 4. Plan C — two-handed / rotate affordances

- **Paper inspect (high value):** add a **`TwoGrabRotateTransformer`** to the paper's `Grabbable` so a
  second hand rotates the sheet around its vertical axis to examine it — a natural "turn it over to
  read" gesture. Pair with the existing expand-on-grab.
- Keep single-hand move on the default `GrabFreeTransformer`. The `Grabbable` accepts a one-grab and a
  two-grab transformer simultaneously (one-hand = move, two-hand = rotate).
- **Cards: skip two-handed** — too small to benefit; single pinch is correct.
- Optional stretch: `TwoGrabRotateTransformer` on the **architecture diagram** host for "grab and spin
  the diagram," reusing the same component.

---

## 5. Plan D — poke vs grab disambiguation on the task cards (ACTION NEEDED)

We just added pause/priority **poke buttons as children of a grabbable card**. ISDK separates these by
interactor (`PokeInteractor` vs `HandGrabInteractor`), but **physically**, a finger reaching for a tiny
button on a grabbable card will often trip the grab trigger first. Options, best first:

1. **Move the pokes off the grab body (recommended).** Put pause/priority on a small **"control tab"
   that extends ~2–3 cm beyond the card's grab `BoxCollider`**, so poking never overlaps the grab
   volume. (We already offset them to the right edge at x≈0.066; push them clear of the 0.08-wide
   collider and shrink the grab collider's X so they don't overlap.)
2. **Show pokes only when docked, not held.** Gate the poke buttons' active state on
   `TaskCardView.IsHeld == false` (hide controls while the card is in-hand). Cheap, removes the
   conflict entirely during manipulation.
3. **Reserve grab types:** set the card's `Supported Grab Type = Palm` and keep poke as the pinch/point
   gesture — separates the two by hand shape.
4. **Expanded-card controls:** only reveal pause/priority on an inspected/expanded card (`detailsRoot`),
   keeping the resting card a clean grab target.

Recommend **(1) + (2) together**: controls on a tab outside the grab volume, hidden while held.

---

## 6. Plan E — grab feedback (visual + audio + haptic)

All three hang off `InteractableUnityEventWrapper`, which we already wire by reflection
(`_whenSelect`) in `BuildPhase6`/`BuildSceneHooks`. It exposes **`WhenHover`, `WhenSelect`,
`WhenUnhover`, `WhenUnselect`** (each fires once per state change).

- **Visual hover highlight:** on `WhenHover`, enable an emission/outline on the paper/card face
  (toggle a `_EMISSION` keyword or swap to a highlighted material); clear on `WhenUnhover`. A
  `MaterialPropertyBlockEditor`-style tint or our existing `CardRoleTint` material is the hook.
- **Audio:** on `WhenSelect`, call our existing **`FeedbackController`** (procedural tones) for a pick-up
  "tick"; a softer tone on `WhenUnselect`. No new assets needed.
- **Haptics:** use the **Haptics SDK** `HapticClipPlayer` — `new HapticClipPlayer(clip)` then `.Play(hand)`
  on `WhenSelect`. Clips are authored in **Meta Haptics Studio** (`.haptic` files). For a zero-asset
  start, a short `OVRInput`/controller-free pulse isn't available for hands, so ship a tiny bundled
  `.haptic` "grab tick" (one clip covers paper + cards). Keep `priority` default (128); only one clip
  renders per hand at a time, so a single short pulse is ideal.
- Wire a small `GrabFeedback` MonoBehaviour (hover→highlight, select→sound+haptic) and bind the four
  events via the same reflection recipe we use for poke buttons — **no hard ISDK reference**.

---

## 7. Plan F — builder-script implementation checklist

Poses carry complex finger data that should NOT be generated procedurally — **author/copy them in the
Editor**, and let builders do the wiring. Concretely:

1. **One-time Editor authoring (manual, device-free):** duplicate a sample `HandGrabPose` onto the
   `DecisionDocument` prefab and the `CardTemplate`; tune finger handles; add `BoxGrabSurface` to the
   paper pose; set `HandAlignment` (`AttractOnHover` paper / `AlignOnGrab` card) and `Supported Grab
   Type`. Mirror L/R. Save the prefabs.
2. **`BuildGrabFidelity.cs` (new builder):**
   - Add the one-grab + two-grab transformers to the paper `Grabbable` (reflection: find/attach
     `TwoGrabRotateTransformer`, set its axis).
   - Attach a `GrabFeedback` component and bind `WhenHover/Select/Unhover/Unselect` on the
     `InteractableUnityEventWrapper` via `UnityEventTools.AddVoidPersistentListener` (same recipe as
     `WirePoke`).
   - Ensure the paper/card `HandGrabInteractable` + any new interactable is in the
     `ReRegisterInteractable.interactables` set.
   - Rework the card poke layout per §5 (move the control tab outside the grab collider; gate on
     `IsHeld`).
3. **`GrabFeedback.cs` (new runtime, `MuseDesk.Workspace`):** reflection-free of ISDK types; public
   methods `OnHover/OnUnhover/OnGrab/OnRelease`; references `FeedbackController` + a `HapticClipPlayer`.
4. Keep everything **idempotent** and runnable via `unity command script run` like our other builders.

---

## 8. Plan G — verification plan (editor vs on-head)

**Editor-verifiable (do first):**
- Compile green; `BuildGrabFidelity` runs clean.
- Scene View: GhostHand pose looks like a real grip on paper/card; poke control tab sits clearly
  outside the grab `BoxCollider` bounds (gizmo check).
- Event wiring: confirm `_whenSelect`/`_whenHover` persistent-call counts via `SerializedObject`.

**On-head only (Editor Play Mode stalls without HMD — our standing rule):**
- Fingers actually wrap on reach (AttractOnHover), snap feels right, `BoxGrabSurface` lets you grab
  anywhere along the paper edge.
- Poke vs grab: pause/priority poke reliably without triggering a grab; grabbing the card body never
  fires a poke.
- Two-hand rotate on the paper; haptic pulse fires once per grab; hover highlight on/off is crisp.
- FPS ≥ 60 with poses + feedback active across several cards.

**Optional Link recording session (one pass):** record a natural paper grip + card pinch, Save To
Collection, then tune in Edit Mode. Everything else is device-free.

---

## 9. Plan H — risk / rollback

- **`Kinematic while selected` double-control:** Grabbable defaults this true while `GrabbableRigidbody`
  also toggles kinematic. If a prop misbehaves, set the Grabbable's field false and let our script own
  it (or vice-versa) — do not have both fight. Low risk for the paper/cards (kinematic bodies anyway).
- **Pose cloned onto pooled cards:** if a pooled card's pose fails to register, it falls back to the
  current generic wrist grab (still grabbable) — graceful degradation, not a break. Verify via
  `ReRegisterInteractable`.
- **AttractOnHover cost:** progressive finger solving is slightly heavier than AlignOnGrab. If FPS dips,
  downgrade the paper to `AlignOnGrab` (still snaps on grab) and keep cards there.
- **Everything additive + idempotent:** new builder + new `GrabFeedback`/`HandGrabPose` children.
  Rollback = delete the pose children + the `GrabFeedback` component + revert the card tab; the proven
  reflection grab path is untouched.
- **Haptic asset dependency:** if no `.haptic` clip is bundled in time, ship visual+audio feedback only
  and add haptics last — they're independent.

---

## Sources
- [Create a Hand Grab Pose (PC)](https://developers.meta.com/vr/documentation/unity/unity-isdk-creating-handgrab-poses/)
- [Hand Grab Interactions](https://developers.meta.com/vr/documentation/unity/unity-isdk-hand-grab-interaction/)
- [HandGrabInteractable Class (v83)](https://developers.meta.com/vr/reference/interaction/v83/class_oculus_interaction_hand_grab_hand_grab_interactable/)
- [HandGrabPose Class (v203)](https://developers.meta.com/horizon/reference/interaction/v203/class_oculus_interaction_hand_grab_hand_grab_pose/)
- [Event Wrappers (InteractableUnityEventWrapper)](https://developers.meta.com/vr/documentation/unity/unity-isdk-event-wrappers/)
- [InteractableUnityEventWrapper Class (v83)](https://developers.meta.com/horizon/reference/interaction/v83/class_oculus_interaction_interactable_unity_event_wrapper/)
- [Haptics SDK for Unity](https://developers.meta.com/horizon/documentation/unity/unity-haptics-sdk/)
- [Add Haptics](https://developers.meta.com/vr/documentation/unity/unity-haptics-sdk-integrate/)
- [HapticClipPlayer Class (v71)](https://developers.meta.com/vr/reference/upm-haptics/v71/class_oculus_haptics_haptic_clip_player/)
- [HandGrabUtils Class](https://developers.meta.com/horizon/reference/unity/v65/class_oculus_interaction_hand_grab_hand_grab_utils/)
