# Dynamic Grab Poses (Alyx-style hand wrap) in Meta ISDK 209

Target: MuseDesk, Unity 6, Quest 3, Meta XR SDK **209.0.0**
(`com.meta.xr.sdk.interaction@75f85e8aa6bd`, assembly `Oculus.Interaction`).

Goal: when the hand approaches a grabbable (cube, cylinder/mug, decision paper) the
hand visually conforms to the **outside** of the object and the object sticks to the
hand, instead of snapping to a pinch point or clipping through.

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
