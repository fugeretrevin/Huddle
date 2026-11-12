# Dynamic Grab Poses (Alyx-style hand wrap) in Meta ISDK 209

Target: MuseDesk, Unity 6, Quest 3, Meta XR SDK **209.0.0**
(`com.meta.xr.sdk.interaction@75f85e8aa6bd`, assembly `Oculus.Interaction`).

Goal: when the hand approaches a grabbable (cube, cylinder/mug, decision paper) the
hand visually conforms to the **outside** of the object and the object sticks to the
hand, instead of snapping to a pinch point or clipping through.

> **UPDATED GOAL (supersedes the recorded-pose direction below).** The user does NOT want
> pre-recorded/static hand poses. They want **DYNAMIC finger collision**: each finger
> curls and **stops at the object's real surface wherever the hand lands** — shape- and
> position-agnostic, no authoring pass, fingers must not pass through the object.
> See **PART II: DYNAMIC FINGER COLLISION** at the bottom of this doc for the research,
> real APIs, the custom algorithm, and risks. Parts 0-4 (surface-based grip snap) remain
> valid and complementary — a grab surface fixes the *grip/wrist* while the Part II solver
> fixes the *fingers* — but Part II is the primary deliverable now.

Everything below was read directly out of the installed package source. Every class /
field name is real and the file path where it was confirmed is cited. **No invented APIs.**

---

## 0. The one thing to understand first (honest caveat)

There are **two separate behaviours** people lump together as "dynamic grab pose":

1. **Grip / wrist snap** — *where on the object the grab point lands and how the wrist is
   oriented.* This is what a **grab surface** (`IGrabSurface`) controls. It needs **no
   recorded hand pose**. The object sticks to a sensible point on its surface and the
   wrist aligns to the surface. This is the no-recording path and it works in 209.

2. **Finger conforming** — *the fingers visually curling around the geometry (the thing
   that makes Alyx look good).* In ISDK this is driven **only** by a recorded
   `HandPose` (an array of joint `Quaternion` rotations). ISDK does **not** procedurally
   curl fingers to arbitrary geometry.

Proof, from `Runtime/Scripts/Grab/Visuals/HandGrabStateVisual.cs` (lines 100-133):

```csharp
if (fingersConstraint > 0f && grabTarget.HandPose != null)   // <-- HandPose REQUIRED
{
    UpdateFingers(grabTarget.HandPose, grabSource.GrabbingFingers(), fingersConstraint);
}
else
{
    FreeFingers();   // no HandPose  => fingers just follow raw hand tracking
}
```

and `HandGrabTarget.HandPose` (in `.../HandGrab/HandGrabTarget.cs`, line 46):

```csharp
public HandPose HandPose => _handGrabResult.HasHandPose ? _handGrabResult.HandPose : null;
```

`HasHandPose` is only set true when the chosen `HandGrabPose` actually carries joint
rotations (`GrabPoseFinder.UsesHandPose => _handGrabPoses[0].HandPose != null`,
`.../HandGrab/GrabPoseFinder.cs` line 54).

**Conclusion:** a grab surface alone gives you a correct, non-clipping grip point + wrist
alignment and the object sticking to the hand — a big upgrade over the current pinch-snap
— but it will **not** curl the fingers around the cube/mug. Full Alyx-style finger wrap
requires at least one recorded `HandPose`. Those can be authored in minutes in Play Mode
with live hand tracking (no mocap rig needed) and saved to survive Play-Mode exit. See
Approach A below. Be clear with the user about this tradeoff before committing.

---

## 1. Exact API surface (real names, namespaces, serialized fields)

### HandGrabInteractable
`Runtime/Scripts/Interaction/Models/HandGrab/HandGrabInteractable.cs`
Namespace: `Oculus.Interaction.HandGrab`

| Member | Type | Notes |
|---|---|---|
| `_handGrabPoses` | `List<HandGrabPose>` (serialized, line 176) | **This is the list to populate.** FormerlySerializedAs `_handGrabPoints`. |
| `HandGrabPoses` | `List<HandGrabPose>` getter (line 182) | Returns the **live** list — you can `.Add()` to it. |
| `_handAligment` | `HandAlignType` (serialized, line 151) | NOTE the misspelling `_handAligment` in the serialized field. Default `AlignOnGrab`. |
| `HandAlignment` | `HandAlignType` get/set (line 157) | Public setter exists. |
| `_rigidbody` | `Rigidbody` (serialized, line 50) | |
| `RelativeTo` | `Transform` getter (line 188) | Returns `_rigidbody.transform`. This is the reference transform all poses/surfaces measure against. |
| `_scoringModifier` | `PoseMeasureParameters` (line 86) | Optional, default `new PoseMeasureParameters(0.8f)`. |
| `InjectOptionalHandGrabPoses(List<HandGrabPose>)` | method (line 397) | Clean DI setter for the whole list. |
| `InjectRigidbody(Rigidbody)` | method (line 377) | |

### HandAlignType (enum)
Defined in `Runtime/Scripts/Interaction/Models/HandGrab/IHandGrabState.cs` (lines 31-37):

```csharp
public enum HandAlignType { None, AlignOnGrab, AttractOnHover, AlignFingersOnHover }
```

- `AlignOnGrab` (current project value): hand conforms only **after** the grab completes.
- `AttractOnHover`: wrist **and** fingers conform during hover/approach (closest to Alyx).
- `AlignFingersOnHover`: only fingers conform on hover (wrist stays on real hand).

Driven in `HandGrabStateVisual.ConstrainingForce` (lines 73-98). To get the
"wraps as the hand approaches" feel you want `AttractOnHover` **or**
`AlignFingersOnHover`, not `AlignOnGrab`.

### HandGrabPose (MonoBehaviour)
`Runtime/Scripts/Interaction/Models/HandGrab/HandGrabPose.cs`
Namespace: `Oculus.Interaction.HandGrab`

| Member | Type | Notes |
|---|---|---|
| `_surface` | `UnityEngine.Object` (serialized, line 60) `[Interface(typeof(IGrabSurface))]` | Holds the grab surface. |
| `SnapSurface` | `IGrabSurface` getter (line 69) | `=> _snapSurface ?? _surface as IGrabSurface;` so setting the serialized `_surface` is enough. |
| `_relativeTo` | `Transform` (serialized, line 80) | Must equal the interactable's `RelativeTo`. |
| `_usesHandPose` | `bool` (serialized, line 83) | **Default `true`.** Set to **`false`** for the surface-only / no-recording path. |
| `_handPose` | `HandPose` (line 88, non-OpenXR) | Recorded joint data. |
| `_targetHandPose` | `HandPose` (line 92, used when `ISDK_OPENXR_HAND` is defined) | **Build-define-dependent — verify which define this project uses (see Uncertainties).** |
| `HandPose` getter (line 119) | `HandPose` | Returns `_usesHandPose ? (_targetHandPose or _handPose) : null`. |
| `InjectAllHandGrabPose(Transform relativeTo)` | method (line 319) | Sets `_relativeTo`. |
| `InjectRelativeTo(Transform)` | method (line 328) | |
| `InjectOptionalSurface(IGrabSurface)` | method (line 337) | Sets both `_surface` and the runtime `_snapSurface`. Preferred over raw field-set. |
| `InjectOptionalHandPose(HandPose)` | method (line 347) | Also sets `_usesHandPose`. |

### IGrabSurface (interface) + concrete surfaces
Interface: `Runtime/Scripts/Grab/SnapSurfaces/IGrabSurface.cs`
Namespace for interface **and all surfaces**: `Oculus.Interaction.Grab.GrabSurfaces`
(note: the folder on disk is `SnapSurfaces/` but the C# namespace is `...Grab.GrabSurfaces`).

> IMPORTANT naming correction vs. the brief: the real class names end in **`GrabSurface`**,
> not `Surface`. There is **no** `CylinderSurface`, `SphereGrabSurface` is spelled exactly
> that, and there is no `BoxSurface`. The real set is below.

| Real class | File | Serialized data class / key fields |
|---|---|---|
| `CylinderGrabSurface` | `Grab/SnapSurfaces/CylinderGrabSurface.cs` | `_data : CylinderSurfaceData` (`startPoint` `Vector3`, `endPoint` `Vector3`, `arcOffset` `float`, `arcLength` `float`), `_relativeTo : Transform`. Radius is **derived** from the distance between the cylinder axis and this `HandGrabPose`'s own transform position. |
| `BoxGrabSurface` | `Grab/SnapSurfaces/BoxGrabSurface.cs` | `_data : BoxGrabSurfaceData` (`widthOffset` `float 0..1`, `snapOffset` `Vector4`, `size` `Vector3` — **Y ignored**, `eulerAngles` `Vector3`), `_relativeTo : Transform`. A flat rectangle; ideal for the paper and for face-grabbing the cube. |
| `ColliderGrabSurface` | `Grab/SnapSurfaces/ColliderGrabSurface.cs` | `_collider : Collider` only. Snaps the grip to the nearest point on an existing collider. **Simplest**, zero geometry math — good default for cube and paper. |
| `SphereGrabSurface` | `Grab/SnapSurfaces/SphereGrabSurface.cs` | `_data : SphereGrabSurfaceData` (`centre` `Vector3`), `_relativeTo : Transform`. Rotation-agnostic; good for ball-like props. |
| `BezierGrabSurface` | `Grab/SnapSurfaces/BezierGrabSurface.cs` | Spline of control points; for complex handles (overkill here). |

All surfaces are `MonoBehaviour` and implement `IGrabSurface`, so they are **added as
components** (usually onto the same GameObject as the `HandGrabPose`). Each exposes
`InjectData(...)` + `InjectRelativeTo(Transform)` (and an `InjectAll*Surface(...)`).

How a surface is used at runtime: `HandGrabPose.CalculateBestPose` →
`CompareNearPoses` (line 296) → if `SnapSurface != null`,
`SnapSurface.CalculateBestPoseAtSurface(worldPoint, offset, out bestWorldPose, ...)`
returns the nearest valid grip pose on the surface. With one pose in the list,
`GrabPoseFinder.FindBestPose` (line 93) calls it directly.

### HandGrabPoseData / HandGrabInteractableData (for recorded poses)
`Runtime/Scripts/Interaction/Models/HandGrab/HandGrabUtils.cs`
Namespace: `Oculus.Interaction.HandGrab`, nested in static class `HandGrabUtils`.

- `struct HandGrabPoseData { Pose gripPose; HandPose handPose; float scale; }` (line 85)
- `struct HandGrabInteractableData { List<HandGrabPoseData> poses; GrabTypeFlags grabType; HandAlignType handAlignment; PoseMeasureParameters scoringModifier; GrabbingRule pinchGrabRules; GrabbingRule palmGrabRules; }` (line 37)
- Helpers: `CreateHandGrabPose(Transform parent, Transform relativeTo)` (line 124),
  `CreateHandGrabInteractable(...)` (line 112), `SaveData(...)` (line 207),
  `LoadData(...)` (line 230), `LoadHandGrabPose(...)` (line 250).
- Persisted via `HandGrabInteractableDataCollection : ScriptableObject`
  (`.../HandGrab/HandGrabInteractableDataCollection.cs`), `CreateAssetMenu`
  "Meta/Interaction SDK/Pose Authoring/HandGrabInteractable Data Collection".

### HandPose
`Runtime/Scripts/Interaction/Models/HandGrab/HandPose.cs`, namespace `Oculus.Interaction.HandGrab`.
`[Serializable] class HandPose` with `_handedness : Handedness`,
`_fingersFreedom : JointFreedom[]`, `_jointRotations : Quaternion[]`
(length `FingersMetadata.HAND_JOINT_IDS.Length`). Not a component.

---

## 2. Approaches, ranked

### Approach A — Recorded HandPose per object (surface optional). *Highest fidelity.*
Author one `HandGrabPose` per object with real finger joint rotations, captured live:
enter Play Mode with hand tracking, shape your hand around the prop, use ISDK's
**HandGrabPoseWizard** / "Quick actions" on the `HandGrabInteractable` to record, then
save into a `HandGrabInteractableDataCollection` so it survives Play-Mode exit, and
reload at edit time.

- Pros: the **only** path that curls the fingers (true Alyx look). Works great with
  `AttractOnHover`.
- Cons: needs an authoring pass in-editor per object/hand (right hand; mirror for left via
  `HandGrabUtils.MirrorHandGrabPose`). Not pure-code; the other engineer must do the live
  capture. Per-object scale poses recommended for multiple hand sizes.
- No motion-capture needed — ISDK captures from the live tracked hand. "No mocap" ✓, but
  "no in-editor authoring" ✗.

### Approach B — Surface-based snap, no recorded pose. *Recommended default now.* ✓
Add one `HandGrabPose` with `_usesHandPose = false` and an `IGrabSurface`:
`ColliderGrabSurface` (simplest) or a shaped surface (`Box`/`Cylinder`) per object.

- Pros: **zero recording**, pure code via reflection, both hands work automatically
  (`GrabPoseFinder.SupportsHandedness` returns true when `!UsesHandPose`,
  `GrabPoseFinder.cs` line 70). Object sticks to a correct point on its outside; wrist
  aligns; supports sliding along the surface (`_slippiness`). Fixes the pinch-snap and
  clip-through problems.
- Cons: **fingers do NOT curl** (see section 0). The palm/wrist conforms, fingers stay on
  tracked data. Good, not perfect.
- Per-shape mapping:
  - **Cube**: `BoxGrabSurface` sized to a face (or `ColliderGrabSurface` for grab-anywhere).
  - **Cylinder / mug**: `CylinderGrabSurface` with `startPoint`/`endPoint` along the local
    axis (±half-height) and `arcLength` 360 (or ~200 to bias to the outer wall). Radius is
    auto-derived from the pose transform's offset from the axis.
  - **Flat paper**: `BoxGrabSurface` (thin rectangle) or `ColliderGrabSurface`.

### Approach C — HandAlignment tweak only. *Cheapest, smallest win.*
Just change `HandAlignment` from `AlignOnGrab` to `AttractOnHover`. With **no** pose/surface
this only changes *when* alignment applies; with nothing to align to it does little on its
own. Only meaningful **combined** with A or B. Treat as a modifier, not a standalone fix.

### Recommendation for MuseDesk's 3 shapes
Ship **B now** (surface-based, code-only, no recording) so grabs immediately feel better
on cube, mug and paper, and set `HandAlignment = AttractOnHover` so it engages on approach.
If the user wants the full finger-wrap after seeing B, add **A** for the hero props
(cube + mug) as a short in-editor capture pass. Use `ColliderGrabSurface` as the universal
fallback and upgrade cube→`Box`, mug→`Cylinder` for cleaner wrist alignment.

---

## 3. Wiring steps (Editor builder via reflection)

The `AgentScripts` builders cannot reference `Oculus.Interaction` by name (see
`FixGrabWire.cs`, `GrabColliders.cs` — everything is reflection by full type name). So the
builder:

1. Resolve types by full name across loaded assemblies:
   `Oculus.Interaction.HandGrab.HandGrabInteractable`,
   `Oculus.Interaction.HandGrab.HandGrabPose`,
   `Oculus.Interaction.Grab.GrabSurfaces.ColliderGrabSurface` /
   `...CylinderGrabSurface` / `...BoxGrabSurface` (+ their `*Data` classes).
2. Find the `HandGrabInteractable` component on the target grabbable.
3. Read its `RelativeTo` (property) — the reference transform for pose + surface.
4. `GameObject poseGO = new GameObject("HandGrabPose_Surface")`, parent under the
   interactable's transform, local pose identity (then nudge to the grip location).
5. `poseGO.AddComponent(handGrabPoseType)`; set `_relativeTo` via `InjectRelativeTo` and
   set `_usesHandPose = false` (field).
6. `poseGO.AddComponent(surfaceType)`:
   - Collider: `InjectCollider(theObjectsCollider)` (or `InjectAllColliderGrabSurface`).
   - Cylinder/Box: construct the `*Data` object (`Activator.CreateInstance`), set its public
     fields, then `InjectData(data)` + `InjectRelativeTo(RelativeTo)`.
7. Wire surface into pose: `handGrabPose.InjectOptionalSurface((IGrabSurface)surface)`.
8. Add the pose to the list: get `_handGrabPoses` (`List<HandGrabPose>`) and call its
   `Add(pose)` — or rebuild the list and call `InjectOptionalHandGrabPoses(list)`.
9. Optionally set `HandAlignment = AttractOnHover` via the public setter.
10. `EditorUtility.SetDirty` the interactable + pose, mark scene dirty, save.

The draft implementing exactly this is `AgentScripts/GrabPosesDraft.cs`. Points that MUST
be verified in-editor are commented inline there and summarised below.

---

## 4. Uncertainties the other engineer must verify in-editor

1. **OpenXR hand define.** `HandGrabPose` stores the pose in `_handPose` when
   `ISDK_OPENXR_HAND` is **not** defined and in `_targetHandPose` when it **is**
   (`HandGrabPose.cs` lines 108-126, 349-355). This only matters for Approach A (recorded
   poses); the surface path (B) sets `_usesHandPose = false` and ignores both. Confirm
   which define is active before authoring hand poses. I could not determine the active
   scripting define from the package source alone.
2. **HandGrabPose transform placement & cylinder radius.** The cylinder radius and the box
   reference grip are derived from the `HandGrabPose` transform relative to `RelativeTo`.
   The draft places it at a guessed offset; the real grip point/radius must be eyeballed
   and nudged in the Scene view. Marked `// TUNE` in the draft.
3. **`AddComponent(Type)` + component order / `[DisallowMultipleComponent]`.** Confirm the
   surfaces/pose add cleanly at edit time (they are plain `MonoBehaviour`s, so expected OK).
4. **Which collider to feed `ColliderGrabSurface`.** The draft grabs the first non-trigger
   `Collider` under the object; verify it's the visual-matching one (the grab **volume**
   collider may be a larger trigger — see `GrabColliders.cs`).
5. **Does `AttractOnHover` feel right vs `AlignFingersOnHover`.** Subjective; try both.
6. I verified the runtime read path end-to-end (`GrabPoseFinder` → `HandGrabPose` →
   `IGrabSurface` → `HandGrabStateVisual`). I did **not** run it on-device; the finger-wrap
   limitation (section 0) is inferred from source, not observed — confirm visually.

---
---

# PART II — DYNAMIC FINGER COLLISION (fingers curl & stop at the real surface)

Goal restated: no recorded poses. When the hand grabs/approaches an object, every finger
curls with the user's real motion but **stops on contact with the object's colliders** so
the fingertips rest on the surface and never intersect it. Shape-agnostic, position-agnostic.

## II.0 TL;DR / recommendation

- **Meta ISDK 209 has NO turnkey per-finger collision/conform hand.** The pieces that
  sound like it do something else (details + file paths in II.1):
  - `HandPhysicsCapsules` spawns kinematic capsule colliders that **follow** the tracked
    bones; they can push other rigidbodies but do **not** constrain the visual fingers.
  - `HandPokeLimiterVisual` stops the **whole wrist** at a poke surface (not per-finger),
    and its finger limiting is done inside an opaque **native** component.
  - `SyntheticHand` is the real write surface: it can override **per-joint** rotations and
    has a `JointFreedom.Constrained` mode that already **clamps over-flexion** past a given
    max rotation. This is the closest built-in primitive, but *you* must compute the
    per-joint max (the contact rotation); it does not sense colliders itself.
- **So: a small CUSTOM solver is required.** Recommended = **Approach 1 (visual-only
  LateUpdate override of the `HandVisual` joint transforms)**: simplest, fully controllable,
  does not fight the ISDK grab transformer or tracking, works whether or not grabbing.
  The draft `DynamicFingerCollisionDraft` in `AgentScripts/GrabPosesDraft.cs` implements it.
- **80/20:** only run the solver when a grabbable is near/hovered (gate by distance or a
  `LayerMask`), and only cast a couple of spheres per finger. No per-frame GC.

## II.1 What the built-ins actually do (real names + file paths)

### HandPhysicsCapsules — colliders only, NOT a visual constraint
`Runtime/Scripts/Input/Hands/HandPhysicsCapsules.cs`, namespace `Oculus.Interaction.Input`.
- Per bone it creates a kinematic `Rigidbody` + `CapsuleCollider` (`CreateJointRigidbody`
  line 244 sets `isKinematic = true`, `useGravity = false`; `CreateCollider` line 264).
- Every hand update it just drives those bodies to the tracked bone pose
  (`UpdateRigidbodies` lines 330-364: `jointbody.MovePosition(bonePose.position)` /
  `MoveRotation(...)` from `Hand.GetJointPose`). It reads the hand; it never writes back.
- Public surface: `IList<BoneCapsule> Capsules`, `class BoneCapsule { HandJointId StartJoint,
  EndJoint; Rigidbody CapsuleRigidbody; CapsuleCollider CapsuleCollider; }` (line 404),
  `_jointsRadiusFeature : JointsRadiusFeature` (per-bone radius via `GetJointRadius(jointId)`).
- Verdict: useful as a **radius source** and for pushing dynamic props, but it will **not**
  stop the rendered fingers. Confirmed it only spawns/follows colliders.

### HandPokeLimiterVisual — whole-wrist stop, finger-lock is native
`Runtime/Scripts/Interaction/Models/Poke/Visuals/HandPokeLimiterVisual.cs`,
namespace `Oculus.Interaction`.
- On `PokeInteractor.IsPassedSurface` it calls `_syntheticHand.LockWristPose(...)`
  (line 146) to hold the whole hand at the poke surface — **not** per-finger.
- The finger limiting is triggered via `NativeMethods.isdk_NativeComponent_Activate(
  0x506f6b654c696d74)` (line 120; the magic number is ASCII "PokeLimt"). That logic is
  **inside the native ISDK runtime** and is not reachable/parameterizable per-finger from
  C#. So the concept does not cleanly extend to arbitrary grabbables per-finger.

### SyntheticHand — the real per-joint write API (+ built-in over-flex clamp)
`Runtime/Scripts/Input/Hands/DataModifiers/SyntheticHand.cs`, namespace `Oculus.Interaction.Input`.
`SyntheticHand : Hand` is a `DataModifier<HandDataAsset>` in the hand pipeline; everything
downstream (`HandVisual`, grab visuals) reads the modified `IHand`. Key public methods:
- `OverrideJointRotation(HandJointId jointId, Quaternion rotation, float overrideFactor)` (line 350)
- `OverrideFingerRotations(HandFinger finger, Quaternion[] rotations, float overrideFactor)` (line 334)
- `OverrideAllJoints(in Quaternion[] jointRotations, float overrideFactor)` (line 319)
- `SetJointFreedom(HandJointId, JointFreedom, bool skipAnimation)` (line 424) /
  `SetFingerFreedom(HandFinger, JointFreedom, bool)` (line 407)
- `LockJoint(in HandJointId, Quaternion, float)` (line 391), `FreeAllJoints()` (line 446)
- `enum WristLockMode { Position, Rotation, Full }`; `LockWristPose(...)` (line 480).
- **`JointFreedom` enum** (`Runtime/Scripts/Input/Hands/FingersMetadata.cs` line 25):
  `Free, Constrained, Locked`.
  - `Constrained` (handled in `UpdateJointsRotation`, lines 225-262) treats the overridden
    rotation as a **max flex**: the tracked joint moves freely until it would flex *past*
    that max (`OverFlex`, line 579), then it is clamped. **This is exactly a "stop the
    finger at a limit" mode** — but the limit (max rotation) must be supplied by us.
  - `Locked` hard-sets the joint to the overridden rotation (× overrideFactor).
- Smoothing: joint/wrist lock/unlock are eased by `ProgressCurve`s, so transitions are soft.

### HandVisual — direct per-joint transform access (simplest write path)
`Runtime/Scripts/Interaction/Visuals/HandVisual.cs`, namespace `Oculus.Interaction`.
- `IList<Transform> Joints` (line 115) — indexed by `(int)HandJointId`.
- `Transform GetTransformByHandJointId(HandJointId)` (line 334),
  `Pose GetJointPose(HandJointId, Space)` (line 343).
- `event Action WhenHandVisualUpdated` (line 100) — fires at the end of `UpdateSkeleton()`
  (line 282), which each hand update sets every joint transform from
  `Hand.GetJointPosesLocal(...)` (line 319: `Joints[i].SetPose(localJoints[i], Space.Self)`).
- `UpdateSkeleton` runs off `Hand.WhenHandUpdated` (during the hand data's Update). So if we
  overwrite joint transforms in **LateUpdate** (or in a `WhenHandVisualUpdated` handler that
  runs after), our rotations win for that frame's render. This is the visual-only hook.

### Joint model / helpers (needed by any solver)
`Runtime/Scripts/Input/Hands/OVRHandPrimitives.cs`, namespace `Oculus.Interaction.Input`:
- `enum HandJointId` (line 219): wrist=`HandWristRoot`(0); thumb `HandThumb0..3`;
  `HandIndex1..3`, `HandMiddle1..3`, `HandRing1..3`, `HandPinky0..3`; tips
  `HandThumbTip/HandIndexTip/HandMiddleTip/HandRingTip/HandPinkyTip`; `HandMaxSkinnable`(19),
  `HandEnd`(24). Tips are position-only (good spherecast targets).
- `HandJointUtils.FingerToJointList` (line 264): per-finger ordered joint arrays
  (indexed by `(int)HandFinger`; `HandFinger { Thumb=0, Index, Middle, Ring, Pinky }`).
- `HandJointUtils.JointParentList` (line 313), `JointChildrenList` (line 347),
  `IsFingerTip(HandJointId)` (line 428), `GetHandFingerTip(HandFinger)` (line 420),
  `GetHandFingerProximal(HandFinger)` (line 440).
- Local joint axes for a RIGHT hand (`OVRHandPrimitives.cs` `Constants`, lines 54-79):
  `RightDistal = +x` (bone points away from palm along local +X), `RightProximal = -x`,
  `RightPalmar = -y`, `RightDorsal = +y`, `RightThumbSide = +z`, `RightPinkySide = -z`.
  Left-hand mirror set at lines 84-109. **Curling a finger ≈ rotating a joint about its
  local pinky/thumb-side axis** — VERIFY axis sign per joint on-device.
- Read tracked joint world poses with `IHand.GetJointPose(HandJointId, out Pose)`
  (`Runtime/Scripts/Input/Hands/IHand.cs` line 104). Per-bone thickness from
  `JointsRadiusFeature.GetJointRadius(HandJointId)` (used by HandPhysicsCapsules line 199).

## II.2 Approaches, ranked

### Approach 1 — Visual-only clamp on `HandVisual` joint transforms (RECOMMENDED) ✓
A MonoBehaviour per hand. Each `LateUpdate` (after `UpdateSkeleton`): for each finger, walk
joints proximal→distal; sphere-cast each bone segment (radius = joint radius) against the
grabbable colliders; on the first penetrating bone, rotate that joint back so the bone rests
on the hit point and freeze the remaining distal joints. Write results straight into
`HandVisual.Joints[(int)jointId].rotation`.

- Pros: dead simple; total control; **no conflict** with the ISDK grab transformer (which
  moves the *object*, not the hand joints) or with tracking (we overwrite post-update);
  works hovering or grabbing; shape/position agnostic (reads real colliders).
- Cons: purely cosmetic — the `HandPhysicsCapsules` colliders still follow raw tracking, so
  physics contact and the visual can diverge slightly; must beat ISDK's own LateUpdate hooks
  via script execution order; the clamp math (how much to un-curl) is approximate.
- This is what the draft implements.

### Approach 2 — `SyntheticHand` per-joint override + `JointFreedom.Constrained` (sanctioned)
Find the hand's `SyntheticHand`; each frame compute a per-joint **max flex** rotation at the
contact point and feed it via `OverrideJointRotation(jointId, maxRot, 1f)` +
`SetJointFreedom(jointId, Constrained)`. SyntheticHand's `UpdateJointsRotation` then clamps
the tracked curl to that max with smoothing.

- Pros: official data-level path; smoothing curves for free; everything downstream
  (visual + capsules fed from this hand, if wired that way) sees the constrained pose;
  cooperates with the rest of ISDK.
- Cons: **fights `HandGrabStateVisual` while a HandGrab is selected** — that component also
  calls `OverrideAllJoints` / sets freedoms every frame (see Part I; `HandGrabStateVisual.cs`
  lines 100-144). Last writer per frame wins, so you'd have to run after it or only when not
  grabbing. Must locate the correct `SyntheticHand` for each hand. The `Constrained` math is
  in local joint coordinates (`OverFlex`) — correct max-rotation construction is fiddly.
- Best when you want the wrap on **approach/hover** (not during an ISDK grab).

### Approach 3 — Physics-driven fingers (configurable joints) — NOT recommended here
Give each finger bone a real `Rigidbody` + `ConfigurableJoint` driven toward the tracked
pose, let PhysX resolve collisions against the object. Highest fidelity contact, but heavy
to tune, perf-risky on Quest, and it conflicts hard with ISDK's hand pipeline. Overkill.

## II.3 The custom algorithm (Approach 1, concrete)

Per hand, cached once: the `HandVisual`, its `IHand`, the `HandJointId` values, the
per-finger joint arrays, and per-bone radii. Each `LateUpdate`:

```
for each finger f in {Thumb, Index, Middle, Ring, Pinky}:
    joints = FingerToJointList[f]            // proximal..tip, world transforms via HandVisual
    for k in 0 .. joints.Length-2:           // each bone = joint[k] -> joint[k+1]
        p0 = Joints[joint[k]].position
        p1 = Joints[joint[k+1]].position
        dir = normalize(p1 - p0); L = |p1 - p0|
        r  = radius(joint[k])                 // JointsRadiusFeature or a constant ~0.008 m
        if SphereCast(p0, r, dir, out hit, L, grabbableMask):   // bone would penetrate
            # clamp: rotate joint[k] about its curl axis so the bone endpoint sits at hit.point,
            # then STOP — leave joints[k+1..] frozen at their current (contact) rotations so the
            # finger does not curl deeper into the object.
            angle = SignedCurlAngleToRest(p0, p1, hit, Joints[joint[k]])   // see TUNE note
            Joints[joint[k]].rotation = Quaternion.AngleAxis(-angle, curlAxisWorld) * Joints[joint[k]].rotation
            break    # first contact along the finger wins
```

Notes:
- **Which colliders:** a serialized `LayerMask` (put grabbables on a layer) or a cached list
  of the hovered `HandGrabInteractable.Colliders`. Use `Physics.SphereCast`/`CheckSphere`
  against that mask only — never the whole scene.
- **Curl axis:** per `OVRHandPrimitives.Constants`, curl is rotation about the local
  pinky/thumb-side axis (`RightPinkySide = -z` on the right hand). The draft derives a world
  curl axis from the joint transform; the exact axis+sign is the main **// TUNE / VERIFY**
  point on-device.
- **Perf:** ≤ (4 bones × 5 fingers) = 20 spherecasts/hand/frame worst case, early-break on
  first contact per finger, `SphereCastNonAlloc`/no allocations, gated so it only runs when a
  grabbable is within range. Comfortably within Quest budget.
- **Smoothing:** optionally `Slerp` each corrected joint from last frame to kill jitter.

## II.4 Feasibility & risk (be honest)

- **Does overriding joint rotations fight the grab transformer?** No for Approach 1 — the
  ISDK grab *movement* (`IMovement`/`MoveTowardsTargetProvider`) moves the grabbed **object's
  transform**, not the hand's finger joints, so writing finger transforms in LateUpdate is
  orthogonal. It CAN fight **`HandGrabStateVisual`** (Approach 2, and even Approach 1 if that
  component runs in a later LateUpdate) because that also writes finger joints while a
  HandGrab is selected. Mitigation: set Script Execution Order so our solver runs last, or
  disable the solver while `HandGrabInteractor` is actively selecting (let ISDK pose the hand
  then), i.e. only run the dynamic collision during **hover/approach**, not during a locked
  grab. That is also the clean 80/20 scope.
- **Does it fight tracking?** No — we read the tracked pose and only *reduce* curl on
  penetration, overwriting after the visual update each frame; nothing feeds back into
  tracking.
- **What breaks / watch-outs:** capsule colliders (`HandPhysicsCapsules`) still follow raw
  tracking, so a pushed rigidbody and the clamped visual can disagree; fast motion can pop a
  finger through a thin collider in one frame (use `SphereCast` sweep, not just `CheckSphere`,
  and consider continuous checks); concave/mesh colliders need convex or primitive proxies
  for reliable `ClosestPoint`/casts; left-hand axis mirroring must be handled; the clamp angle
  math is approximate and needs eyeballing.
- **Simpler 80/20 that is viable:** only enable the solver when a `HandGrabInteractable` is
  hovered or the hand is within N cm of a grabbable's collider, clamp **only the index +
  middle + thumb** (the readable ones), and freeze-distal on first contact rather than solving
  a perfect per-joint rest. That alone sells the "fingers stop on the object" read for the
  demo at a fraction of the cost/risk.

## II.5 Must-verify on-device
1. The per-joint **curl axis and sign** (left vs right hand) — the single biggest unknown.
2. That writing `HandVisual.Joints[i].rotation` in LateUpdate actually survives to render
   (script execution order vs ISDK's own LateUpdate visual writers).
3. Which `HandVisual` / `IHand` / `SyntheticHand` instances belong to each hand in the
   `OVRComprehensiveInteractionRig` (there are often raw + synthetic + grab-visual hands).
4. Grabbable colliders are convex/primitive and on a dedicated layer for cheap casts.
5. Bone radius source (`JointsRadiusFeature`) vs a hand-tuned constant.
6. Whether to gate to hover-only to avoid fighting `HandGrabStateVisual` during a locked grab.
