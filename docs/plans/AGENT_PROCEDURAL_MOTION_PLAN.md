# Agent Procedural Motion & Hand-Tracking Interactivity — Implementation Plan

**Status:** PLAN ONLY (no code changed). Scope: give the three rigged Generic-avatar agents
(researcher/developer/tester) lively, reactive secondary motion — a push-reactive lean, head/gaze
look-at, idle sway, and a proximity greet — **on top of the Animator, without breaking any clip.**

Grounded in the real files: `AgentView.cs`, `AgentMovement.cs`, `RubberBandReturn.cs`, `AgentTilt.cs`,
`AgentAnimatorBridge.cs` (`Scripts/Agents/`), and `AgentScripts/BuildAgentCharacters.cs`.

---

## 0. The rig, as it actually is (why this is the crux)

- The FBX (Pipkin-Sprite) imports as **Generic** (`animationType = Generic`, `avatarSetup =
  CreateFromThisModel`) — **not Humanoid.** This is decisive (see §2).
- The **`Animator` sits on the prefab ROOT** (same GameObject as `AgentView`, `AgentMovement`,
  `AgentAnimatorBridge`, the `CapsuleCollider`). `applyRootMotion=false`,
  `cullingMode=AlwaysAnimate`, `updateMode=Normal` (default).
- Under the root is the **`Armature`** skeleton. Clips animate bones by **relative path from the
  Animator's GameObject** (e.g. `Armature/Hips/Spine/Chest/...`). The rig has **named bones** — we
  already resolve `Document_Attach` at runtime (`BuildAgentCharacters.SwapAgentsIntoMainScene`), which
  proves the hierarchy is discoverable by name.
- On rigged agents the builder sets `AgentView.bob/bodyRenderer/armL/armR = null` — **the Animator
  owns all visuals.** `AgentView.Update()` early-returns (`if (bob == null) return;`). So none of
  AgentView's procedural bob/arm code runs for these agents.

---

## 1. Why the two prior AgentTilt approaches broke (precisely)

`AgentTilt.cs` exists and is correct — **for the primitive agents.** It leans `AgentView.bob` in
`LateUpdate` and relies on `AgentView.Update()` rewriting `bob.localRotation` from scratch each frame,
so the lean is a pure additive layer with no accumulation. On the **rigged** agents that contract is
gone:

1. **"Tilt the root fights facing."** `AgentTilt.Awake()` falls back to `bob = transform` (the root)
   when `AgentView.bob` is null — which it is on rigged agents. The root is also rotated by
   `FaceUserWhileDelivering` and by `AgentMovement` (`transform.rotation = LookRotation(dir)` while
   walking, `Slerp` toward the user while delivering). Writing a lean onto the root's rotation and
   having those systems also write the root's rotation produces a fight / popping, and a lean of the
   *whole* agent (feet included) looks like it's tipping over, not leaning.
2. **"Tilt the Armature breaks clip paths."** If you instead insert a tilt pivot *between* the root
   and the Armature (root → TiltPivot → Armature), every clip curve authored as `Armature/...`
   now resolves to `TiltPivot/Armature/...` **relative to the Animator** → the paths no longer match
   and the bones stop deforming. (Equivalent: reparenting the Armature changes the Animator-relative
   path.) Moving the Animator onto the pivot is possible but fragile and re-breaks the avatar/path
   mapping the builder set up.

**Neither prior approach touched the right layer.** The correct layer is: let the Animator write the
clip pose to the real bones, then **post-multiply a small rotation onto a chosen bone's
`localRotation` in `LateUpdate`.** This does **not** change any clip path (the clip still targets the
same bone transform; we just rotate it *after* the Animator wrote it). This is the standard Unity
pattern and the basis of the recommendation below.

---

## 2. Recommended approach — post-Animator bone override in `LateUpdate`

> **One-paragraph summary:** Add a single component `AgentProceduralMotion` on each agent root that,
> in `LateUpdate` (after the Animator has evaluated the clip), rotates the **Spine/Chest bone** (for
> the lean + idle sway) and optionally the **Head bone** (for look-at) by small amounts and then
> *removes its own contribution the next frame before re-applying* so it can never accumulate. The
> lean magnitude+direction is driven by the **same push signal `RubberBandReturn` already computes**
> (expose it read-only), so the body displaces and leans from one source of truth and springs back
> together. It is purely cosmetic, never writes task/agent state, drops to zero while walking, and
> works for a Generic rig because it writes bone `Transform`s directly.

### Why direct bone writes (not IK, not additive layers, not Animation Rigging)

| Option | Verdict for us |
| --- | --- |
| **Direct `Transform.localRotation` in LateUpdate** | **CHOSEN.** Works on Generic rigs; no new packages; mirrors the proven `AgentTilt` additive idea but on a real bone. |
| `OnAnimatorIK` + `Animator.SetBoneLocalRotation` | **Impossible here** — `OnAnimatorIK` is only called for a **Humanoid** avatar; `SetBoneLocalRotation` only works in that IK pass. Our rig is Generic. (Unity docs/issue tracker, sources below.) |
| Animator **additive layer** with a "lean" clip, blended by a float | Possible, but we'd have to author/propagate lean clips into the shared controller and per-character FBX; and a layer with a clip on the same bones can override scripts *even at weight 0* (forum report). Heavier + riskier than one LateUpdate component. |
| Insert a tilt **pivot** / move Animator | Breaks clip paths or the avatar mapping (see §1.2). Rejected. |
| **Animation Rigging** package (Rig Builder + constraints) | Cleanest "proper" route and supports Generic, but adds a package dependency, rig setup, and a learning/verification surface we can't afford to debug blind 6 days out. Note as a **post-deadline upgrade.** |

### Critical ordering facts (confirmed)

- With `updateMode = Normal` (our case), the Animator writes all bone values **after every
  `Update()` and immediately before `LateUpdate()`**. So in `LateUpdate` the bone already holds the
  clip pose — override it there.
- **Do NOT switch the Animator to `AnimatePhysics`.** Under AnimatePhysics the animated values are
  not written in time for LateUpdate and the override silently stops working (you'd then be forced
  into `OnAnimatorIK`, which we can't use on Generic). Leave `updateMode = Normal`.
- Keep **`cullingMode = AlwaysAnimate`** (already set). If the Animator is culled it stops writing
  the bone, and our "remove previous contribution" step (below) would operate on a stale pose. Always
  animating guarantees a fresh clip pose each frame.
- **Execution order:** set `[DefaultExecutionOrder(300)]` on the component so, if any other
  `LateUpdate` ever also touches these bones, ours composes last. **Do not** call
  `MonoImporter.SetExecutionOrder` from inside a `script run` — that hung the Editor before; the
  attribute is the only safe mechanism.

### The accumulation-safe write (the one subtle bit)

Because a clip might theoretically not key our chosen bone (write-defaults off + unkeyed bone), do
**not** assume the Animator rewrites it every frame. Use the store-and-undo pattern (confirmed by the
forum guidance to "store the value and reapply"):

```csharp
// _deltaApplied: the parent-frame rotation we added last frame (identity at start).
// Runs in LateUpdate, AFTER the Animator.
Quaternion clipPose = Quaternion.Inverse(_deltaApplied) * bone.localRotation; // undo our last add
Quaternion delta     = ComputeDeltaThisFrame();                                // lean+sway (parent frame)
bone.localRotation   = delta * clipPose;                                       // re-apply on clean pose
_deltaApplied        = delta;
```

Pre-multiplying (`delta * clipPose`) tilts the bone in its **parent's** frame, which is what we want:
the lean "away" direction is computed in the bone-parent frame so it's independent of the agent's
world facing (same trick `AgentTilt`/`RubberBandReturn` use with `InverseTransformDirection`).

---

## 3. Driving the lean from the existing push (single source of truth)

`RubberBandReturn.Update()` already computes `_offset` — a parent-local displacement vector, clamped
to `maxOffset`, that springs back via `springback`. **Expose it read-only** and let the lean read it,
so the body slide and the body lean are the same gesture and decay together:

- Add to `RubberBandReturn`:
  ```csharp
  /// <summary>Current cosmetic push displacement in parent-local space (|.| ≤ maxOffset). Read-only.</summary>
  public Vector3 PushOffsetLocal => _offset;
  public float MaxOffset => maxOffset;
  ```
- In `AgentProceduralMotion.ComputeDeltaThisFrame()`:
  - `Vector3 pushParent = rubberBand.PushOffsetLocal;` (direction the body was shoved, parent frame).
  - Convert that root-parent-frame vector into the **lean bone's parent frame** (world→localParent):
    `world = root.parent.TransformDirection(pushParent)` then
    `leanDir = bone.parent.InverseTransformDirection(world)`, flatten `y`, normalize.
  - `float mag = Mathf.Clamp01(pushParent.magnitude / rubberBand.MaxOffset);`
  - `axis = Vector3.Cross(Vector3.up, leanDir).normalized;` `angle = mag * maxLeanDeg;`
  - `lean = Quaternion.AngleAxis(angle, axis);`
- Lean springs back automatically because `_offset` springs back — no second spring to tune.

Fallback if you'd rather not couple: `AgentProceduralMotion` can re-sample fingertip proximity itself
using `HandFingertips.Get()` exactly as `RubberBandReturn` does (lines 71–96) — but coupling to
`PushOffsetLocal` is DRY and keeps slide+lean perfectly in phase. **Recommended: couple.**

---

## 4. Idle secondary motion (subtle, state-gated)

The `Idle_Breathing` clip already breathes, so keep additive idle motion *small* and only when
standing (not walking, not mid-reaction):

- **Weight-shift sway:** `swayAngle = Mathf.Sin(Time.time * 0.5f) * 1.5f` degrees of Z-roll on the
  Spine, plus a ~0.3 Hz, ≤1.5° pitch. Fold this into the same `delta` as the lean (sum the small
  rotations) so there's one write to the bone.
- Gate: when `movement.IsMoving` → `delta = identity` (let the Walk clip own the body, same as
  `AgentTilt`). When a one-shot reaction is playing (optional: check a bool the bridge can expose),
  damp sway to ~0 so it doesn't muddy Success/Offer poses.
- Amplitudes are deliberately tiny; the goal is "alive," not "drunk."

---

## 5. Hand-tracking interactivity tiers (what makes it feel great)

Three independent features, cheap to ship incrementally. Tier A is the deferred lean; B and C are new
"aliveness" wins that reuse what's already here.

**Tier A — Push lean (bone override).** §2–§3. The flagship: push an agent, its torso leans away and
springs back. Couples to `RubberBandReturn`.

**Tier B — Head/gaze look-at (bone override).** In the same `LateUpdate`, after writing the Spine
lean, rotate the **Head bone** a clamped amount toward a target (`Camera.main` head, or the nearest
`HandFingertips` point when a hand is close), so agents glance at the user / follow a reaching hand.
Generic-safe (direct bone write, clamped cone ±~35°, slerped). Huge life-for-effort. Uses the same
accumulation-safe write on the Head bone (track a separate `_headDeltaApplied`).

**Tier C — Proximity "greet" reaction (Animator trigger, ZERO bone math).** When a fingertip enters a
friendly radius (larger than the push radius), fire an existing one-shot via the Animator — the clip
set already has `Waving`, `Excited`, `Cheer`, `Happy_Idle`. Add one trigger param (e.g. `TrGreet`) +
an AnyState→`OS_Greet`→return transition in `BuildController()` (mirrors the existing OneShots
pattern), and call `animator.SetTrigger` from a small proximity detector with a cooldown. Lowest risk
(no post-Animator writing at all), and reads as the agents noticing you. **Great first win.**

> Order of delivery suggestion: **C → B → A** (ascending risk; each independently shippable and
> on-head-testable).

---

## 6. Coexistence with FaceUser / AgentMovement / the bridge

- **Facing:** `FaceUserWhileDelivering` and `AgentMovement` rotate the **root**. Our lean/look-at
  rotate **bones under the Armature** in their parent-local frames → independent of root facing.
  Converting the push/look direction through `bone.parent.InverseTransformDirection` each frame keeps
  "away"/"toward" correct even as the root turns.
- **Walking:** gate on `movement.IsMoving` → zero delta (Walk clip owns the body). Same guard
  `AgentTilt` and `RubberBandReturn` already use.
- **Reactions:** one-shot clips (Offer/Success/Handoff) still play fully; the small Spine lean/sway is
  damped to near-zero during them (optional bridge bool) so hero poses stay clean.
- **The bridge is untouched.** `AgentAnimatorBridge` keeps driving `StateId`/`IsMoving`/triggers;
  `AgentProceduralMotion` only reads `movement.IsMoving` and writes bones post-Animator.
- **Retire `AgentTilt` on rigged agents** (leave it for any primitive agents): `AgentProceduralMotion`
  supersedes it. Don't run both on the same bone.

---

## 7. Builder-script implementation checklist (`AgentScripts/BuildAgentProceduralMotion.cs`)

Editor builder, idempotent, run via `unity command script run`. Does **not** modify clip paths.

1. **Discover the bones** (we can't hardcode Pipkin names blind). For each agent root, dump the
   Armature and resolve by name with fallbacks:
   ```csharp
   Transform FindBone(Transform root, params string[] names){
     var all = root.GetComponentsInChildren<Transform>(true);
     foreach (var n in names){ var hit = all.FirstOrDefault(t => t.name == n); if (hit) return hit; }
     foreach (var n in names){ var hit = all.FirstOrDefault(t => t.name.ToLower().Contains(n.ToLower())); if (hit) return hit; }
     return null;
   }
   // lean bone candidates: "Chest","Spine2","Spine1","Spine","UpperChest"
   // head bone candidates: "Head","head","Bip01 Head"
   ```
   **Add a one-off diagnostic entry** `DumpArmature()` that prints the full bone hierarchy of
   `Agent_Researcher.fbx` so we pick the exact lean/head bone names *before* wiring (mirrors the
   existing `AgentScripts/ProbeBone.cs`-style probes). Prefer the **Chest/upper-Spine** for the lean
   (feet stay planted); `Hips` would tip the legs too.
2. Add `[DefaultExecutionOrder(300)] AgentProceduralMotion` to each agent root; set `leanBone`,
   `headBone`, `rubberBand = GetComponent<RubberBandReturn>()`, `movement = GetComponent<AgentMovement>()`.
3. Add the `PushOffsetLocal`/`MaxOffset` getters to `RubberBandReturn` (runtime script edit under
   `Assets/`, not a builder).
4. **Tier C only:** extend `BuildController()` with a `TrGreet` param + `OS_Greet` state
   (clip `Waving`/`Excited`) and the AnyState/return transitions; add a `ProximityGreeter` component
   + its trigger call. Re-run `BuildController` (it's idempotent — deletes+rebuilds the controller).
5. Save scene + assets. Update `AgentScripts`/memory notes.

Default tuning to start: `maxLeanDeg = 10`, head look cone `35°`, greet radius `0.12 m`, greet
cooldown `6 s`, sway `1.5°`.

---

## 8. Verification plan (Editor can't prove interaction)

- **Editor Play Mode stalls without an HMD** (XR can't init; `playerLoopTicking:false`) — so grab/
  poke/look-at won't tick there. Do **not** report "works" from Scene View.
- **Compile + isolated eval:** verify the builder runs green and the components are attached
  (`unity command script run` + a small inspector dump). Confirm bone names resolved (non-null).
- **Non-XR math check:** in the isolated `CharacterIntegrationTest.unity` (plain camera, no XR rig),
  you *can* enter Play Mode. Drive `RubberBandReturn._offset`/`PushOffsetLocal` via a tiny test poker
  (or temporarily feed a synthetic push) and confirm the Spine visibly leans and springs back, the
  Head look-at tracks the camera, and nothing accumulates when idle. This validates the *math/ordering*
  without hands.
- **On-head truth:** build APK, push each agent (lean away + spring back, feet planted), reach toward
  one (head follows), approach one (greet fires once, then cooldown). Confirm walk still overrides
  (no lean mid-walk) and hero reaction poses stay clean. Watch FPS (3 agents × post-Animator writes is
  negligible, but confirm ≥ 60).

---

## 9. Rollback

- Every feature is a **single component + read-only getter**; removing the component restores exact
  prior behavior. The builder should be idempotent (destroy prior `AgentProceduralMotion` before
  adding) so re-runs are clean.
- `BuildController` changes for Tier C are additive params/states; reverting = re-run the current
  `BuildController` (git has the committed controller/scene as the restore point, per
  `SwapAgentsIntoMainScene`'s own note).
- Keep Tiers independent so a bad one-head result on A doesn't block shipping B/C.
- If post-Animator writing ever misbehaves on device (e.g. a clip with write-defaults off fights the
  override), the fallback is Tier C only (pure Animator triggers) — zero bone math, guaranteed safe.

---

## Sources

- [Rotate bone in LateUpdate not overriding animator? — Unity Discussions](https://discussions.unity.com/t/rotate-bone-in-lateupdate-not-overriding-animator/219617)
- [Animator controller override issue with LateUpdate() — Unity Discussions](https://discussions.unity.com/t/animator-controller-override-issue-with-lateupdate/735807)
- [Changing a limb rotation while animating — Unity Discussions](https://discussions.unity.com/t/changing-a-limb-rotation-while-animating/770920)
- [Update bone positions after animation with "Animate Physics" — Unity Discussions](https://discussions.unity.com/t/update-bone-positions-after-animation-with-animate-physics/640416)
- [MonoBehaviour.OnAnimatorIK — Unity ScriptReference (only called for Humanoid)](https://docs.unity3d.com/ScriptReference/MonoBehaviour.OnAnimatorIK.html)
- [Animator.SetBoneLocalRotation — Unity ScriptReference (human bone, IK pass)](https://docs.unity3d.com/kr/2023.2/ScriptReference/Animator.SetBoneLocalRotation.html)
- [Hips local rotation overridden when SetBoneLocalRotation set in OnAnimatorIK — Unity Issue Tracker](https://issuetracker.unity3d.com/issues/hips-local-rotation-gets-overridden-when-animator-dot-setbonelocalrotation-is-set-on-onanimatorik)
- [Introducing Animation Rigging (Unite 2019) — procedural runtime rig alternative](https://www.slideshare.net/unity3d/introducing-animation-rigging-for-unity-20193-unite-copenhagen-2019)
