using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// =============================================================================
// PART II (PRIMARY, updated goal):  DynamicFingerCollisionDraft
// -----------------------------------------------------------------------------
// DYNAMIC finger collision -- NO recorded poses. Each finger curls with the real
// tracked motion but STOPS on contact with a grabbable's colliders, so fingertips
// rest on the surface instead of passing through. Shape- & position-agnostic.
//
// Approach 1 from docs/DYNAMIC_GRAB_POSES.md (Part II): visual-only override of the
// ISDK HandVisual joint transforms in LateUpdate (after HandVisual.UpdateSkeleton
// writes the tracked pose). This does NOT fight the ISDK grab transformer (that moves
// the OBJECT, not the hand joints) nor tracking (we overwrite post-update each frame).
//
// Everything is reflection-based: AgentScripts cannot reference Oculus.Interaction by
// name (same constraint/pattern as FixGrabWire.cs / GrabColliders.cs). Real ISDK 209
// type/member names used (verified in Library/PackageCache/com.meta.xr.sdk.interaction
// @75f85e8aa6bd):
//   Oculus.Interaction.HandVisual            -> Transform GetTransformByHandJointId(HandJointId)
//                                               IList<Transform> Joints
//   Oculus.Interaction.Input.HandJointId     -> enum (HandWristRoot, HandIndex1.., *Tip ..)
//   Oculus.Interaction.Input.IHand           -> bool GetJointPose(HandJointId, out Pose)   (optional)
//
// The other engineer attaches this to a GameObject in the rig and runs/tests it.
// Lines needing in-editor/on-device checks are marked // VERIFY, geometry to eyeball // TUNE.
//
// HONEST STATUS: this is a DRAFT solver. The clamp (how far to un-curl a joint on
// contact) is a first-order approximation: on first penetrating bone it rotates that
// joint so the bone points at the surface and freezes the rest of the finger. The
// per-joint curl AXIS/SIGN and script-execution-order vs ISDK's own LateUpdate writers
// are the main unknowns to confirm on-device.
// =============================================================================
[DisallowMultipleComponent]
public class DynamicFingerCollisionDraft : MonoBehaviour
{
    [Tooltip("Layer(s) the grabbable colliders live on. Keep it narrow for cheap casts.")]
    public LayerMask grabbableMask = ~0;

    [Tooltip("Approx bone thickness used for the sphere casts (m). ISDK's JointsRadiusFeature is more accurate.")]
    public float boneRadius = 0.008f;

    [Tooltip("Only run the solver when a grabbable collider is within this distance of the wrist (m). <=0 disables the gate.")]
    public float proximityGate = 0.18f;

    [Tooltip("Slerp factor to smooth the per-joint correction frame to frame (0=snap, 1=frozen).")]
    [Range(0f, 0.95f)] public float smoothing = 0.4f;

    const string T_HandVisual = "Oculus.Interaction.HandVisual";
    const string T_HandJointId = "Oculus.Interaction.Input.HandJointId";

    // Per-finger joint chains (proximal -> tip), by enum member NAME so we stay
    // decoupled from the assembly. Names from HandJointUtils.FingerToJointList.
    static readonly string[][] FingerChains =
    {
        new[] { "HandThumb0", "HandThumb1", "HandThumb2", "HandThumb3", "HandThumbTip" },
        new[] { "HandIndex1", "HandIndex2", "HandIndex3", "HandIndexTip" },
        new[] { "HandMiddle1", "HandMiddle2", "HandMiddle3", "HandMiddleTip" },
        new[] { "HandRing1", "HandRing2", "HandRing3", "HandRingTip" },
        new[] { "HandPinky0", "HandPinky1", "HandPinky2", "HandPinky3", "HandPinkyTip" },
    };

    // Resolved per HandVisual instance.
    class HandRig
    {
        public Component handVisual;
        public MethodInfo getTransform;        // Transform GetTransformByHandJointId(HandJointId)
        public object wristJointId;            // boxed HandJointId.HandWristRoot
        public object[][] fingerJointIds;      // boxed enum values per finger chain
        public Dictionary<Transform, Quaternion> lastWorld = new Dictionary<Transform, Quaternion>();
    }

    readonly List<HandRig> _rigs = new List<HandRig>();
    Type _handJointIdType;
    bool _ready;
    readonly RaycastHit[] _hitScratch = new RaycastHit[1]; // reserved; SphereCast(out) is already GC-free

    void Start()
    {
        try { Resolve(); }
        catch (Exception e) { Debug.LogError($"[DynamicFingerCollisionDraft] resolve failed: {e.Message}"); enabled = false; }
        if (!_ready) { enabled = false; }
    }

    void Resolve()
    {
        var hvType = ResolveType(T_HandVisual);
        _handJointIdType = ResolveType(T_HandJointId);
        if (hvType == null || _handJointIdType == null)
        {
            Debug.LogError("[DynamicFingerCollisionDraft] Oculus.Interaction types not found in loaded assemblies.");
            return;
        }

        var getT = hvType.GetMethod("GetTransformByHandJointId",
            BindingFlags.Instance | BindingFlags.Public, null, new[] { _handJointIdType }, null);
        if (getT == null) { Debug.LogError("[DynamicFingerCollisionDraft] GetTransformByHandJointId not found."); return; }

        object wrist = EnumVal("HandWristRoot");

        // Pre-box the enum values for each finger chain.
        var chains = new object[FingerChains.Length][];
        for (int f = 0; f < FingerChains.Length; f++)
        {
            var ids = new List<object>();
            foreach (var name in FingerChains[f])
            {
                var v = EnumVal(name);
                if (v != null) ids.Add(v);   // skip any member missing in this skeleton variant
            }
            chains[f] = ids.ToArray();
        }

        // VERIFY: FindObjectsOfType(Type) is fine in Unity 6 (deprecation warning only).
        // In the OVRComprehensiveInteractionRig there may be several HandVisuals (raw/synthetic/
        // grab-visual) -- this drives ALL of them. Narrow to the rendered ones if needed.
        foreach (var obj in UnityEngine.Object.FindObjectsOfType(hvType, true).Cast<Component>())
        {
            _rigs.Add(new HandRig
            {
                handVisual = obj,
                getTransform = getT,
                wristJointId = wrist,
                fingerJointIds = chains,
            });
        }

        _ready = _rigs.Count > 0;
        Debug.Log($"[DynamicFingerCollisionDraft] ready={_ready} handVisuals={_rigs.Count}");
    }

    // Visual-only: run AFTER HandVisual.UpdateSkeleton (which runs on WhenHandUpdated during
    // Update). LateUpdate overwrites the rendered joints for this frame.
    // VERIFY on-device: if an ISDK component writes joints in a LATER LateUpdate, raise this
    // component's Script Execution Order so it runs last.
    void LateUpdate()
    {
        if (!_ready) return;
        foreach (var rig in _rigs) SolveHand(rig);
    }

    void SolveHand(HandRig rig)
    {
        Transform wrist = GetJoint(rig, rig.wristJointId);
        if (wrist == null) return;

        // Cheap proximity gate: skip the whole hand when no grabbable is near.
        if (proximityGate > 0f && !Physics.CheckSphere(wrist.position, proximityGate, grabbableMask, QueryTriggerInteraction.Ignore))
            return;

        foreach (var jointIds in rig.fingerJointIds)
        {
            if (jointIds.Length < 2) continue;

            for (int k = 0; k < jointIds.Length - 1; k++)
            {
                Transform j0 = GetJoint(rig, jointIds[k]);
                Transform j1 = GetJoint(rig, jointIds[k + 1]);
                if (j0 == null || j1 == null) continue;

                Vector3 p0 = j0.position;
                Vector3 p1 = j1.position;
                Vector3 delta = p1 - p0;
                float len = delta.magnitude;
                if (len < 1e-5f) continue;
                Vector3 dir = delta / len;

                // Sweep this bone segment against the grabbable colliders.
                if (Physics.SphereCast(p0, boneRadius, dir, out RaycastHit hit, len, grabbableMask, QueryTriggerInteraction.Ignore))
                {
                    // Desired bone endpoint: rest the bone tip ON the surface (offset by radius along normal).
                    Vector3 desiredEnd = hit.point + hit.normal * boneRadius;   // TUNE: normal*radius offset
                    Vector3 desiredDir = (desiredEnd - p0);
                    if (desiredDir.sqrMagnitude < 1e-8f) break;
                    desiredDir.Normalize();

                    // Rotate joint k so the bone points at the surface instead of through it.
                    // Rotating the parent rigidly lifts the remaining (frozen) finger off the object.
                    Quaternion correction = Quaternion.FromToRotation(dir, desiredDir);
                    Quaternion target = correction * j0.rotation;

                    // Frame-to-frame smoothing to kill jitter.
                    if (smoothing > 0f && rig.lastWorld.TryGetValue(j0, out Quaternion prev))
                        target = Quaternion.Slerp(target, prev, smoothing);

                    j0.rotation = target;
                    rig.lastWorld[j0] = target;

                    break; // first contact along the finger wins; deeper joints stay frozen
                }
            }
        }
    }

    Transform GetJoint(HandRig rig, object boxedJointId)
    {
        if (boxedJointId == null) return null;
        return rig.getTransform.Invoke(rig.handVisual, new[] { boxedJointId }) as Transform;
    }

    object EnumVal(string member)
    {
        try { return Enum.Parse(_handJointIdType, member); }
        catch { return null; }   // member not present in this skeleton variant
    }

    static Type ResolveType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(fullName, false);
            if (t != null) return t;
        }
        return null;
    }
}


// =============================================================================
// PART I (COMPLEMENTARY, optional):  GrabPosesDraft  -- surface-based GRIP/WRIST snap
// -----------------------------------------------------------------------------
// This is the earlier surface-based builder. It does NOT curl fingers (see Part I of
// the .md, section 0). It is kept because it fixes a DIFFERENT problem: where the grip
// lands and how the WRIST aligns to the object's outside (no pinch-point snap). It
// pairs well with DynamicFingerCollisionDraft above (surface = grip/wrist, solver =
// fingers). Run it only if you also want the grip-snap behaviour.
//
// Adds one HandGrabInteractable HandGrabPose with _usesHandPose=false + an IGrabSurface
// (ColliderGrabSurface / BoxGrabSurface / CylinderGrabSurface). Real ISDK 209 names,
// all via reflection. See docs/DYNAMIC_GRAB_POSES.md sections 1-3 for the API citations.
// Editor-only (UnityEditor APIs), so it is guarded so the file still compiles into a
// player build that carries the runtime DynamicFingerCollisionDraft above.
// =============================================================================
#if UNITY_EDITOR
public static class GrabPosesDraft
{
    const string T_Interactable = "Oculus.Interaction.HandGrab.HandGrabInteractable";
    const string T_HandGrabPose = "Oculus.Interaction.HandGrab.HandGrabPose";
    const string T_IGrabSurface = "Oculus.Interaction.Grab.GrabSurfaces.IGrabSurface";
    const string T_ColliderSurface = "Oculus.Interaction.Grab.GrabSurfaces.ColliderGrabSurface";
    const string T_CylinderSurface = "Oculus.Interaction.Grab.GrabSurfaces.CylinderGrabSurface";
    const string T_CylinderData = "Oculus.Interaction.Grab.GrabSurfaces.CylinderSurfaceData";
    const string T_BoxSurface = "Oculus.Interaction.Grab.GrabSurfaces.BoxGrabSurface";
    const string T_BoxData = "Oculus.Interaction.Grab.GrabSurfaces.BoxGrabSurfaceData";
    const string T_HandAlignType = "Oculus.Interaction.HandGrab.HandAlignType";

    enum Shape { Collider, Box, Cylinder }

    static readonly (string name, Shape shape)[] Targets = new[]
    {
        ("[BuildingBlock] Grabbable Item", Shape.Box),      // orange cube
        ("DecisionDocument",               Shape.Box),      // flat paper (thin box)
        ("Mug",                            Shape.Cylinder),  // VERIFY: mug/cylinder may not exist yet
    };

    public static string Run()
    {
        var sb = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToList();

        int touched = 0;
        foreach (var (name, shape) in Targets)
        {
            var go = all.FirstOrDefault(g => g.name == name)
                  ?? all.FirstOrDefault(g => g.name.Contains(name));
            if (go == null) { sb.AppendLine($"SKIP  {name}: not in scene"); continue; }
            sb.AppendLine($"=== {go.name}  ({shape}) ===");
            try { if (AddSurfacePose(go, shape, sb)) touched++; }
            catch (Exception e) { sb.AppendLine($"  ERROR: {e.GetType().Name}: {e.Message}"); }
            sb.AppendLine();
        }

        if (touched > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            sb.AppendLine($"saved scene; {touched} interactable(s) updated");
        }
        return sb.ToString();
    }

    static bool AddSurfacePose(GameObject go, Shape shape, StringBuilder sb)
    {
        var interactable = go.GetComponents<Component>()
            .FirstOrDefault(c => c != null && c.GetType().FullName == T_Interactable);
        if (interactable == null) { sb.AppendLine("  no HandGrabInteractable"); return false; }

        var relativeTo = GetProp(interactable, "RelativeTo") as Transform;
        if (relativeTo == null) { sb.AppendLine("  RelativeTo is null (no Rigidbody?)"); return false; }

        var listField = FindField(interactable.GetType(), "_handGrabPoses");
        var poseList = listField?.GetValue(interactable) as IList;
        if (poseList == null) { sb.AppendLine("  ERROR: _handGrabPoses not found"); return false; }
        if (poseList.Count > 0)
        {
            sb.AppendLine($"  already has {poseList.Count} pose(s); leaving as-is (VERIFY)");
            return false;
        }

        var poseType = ResolveType(T_HandGrabPose);
        if (poseType == null) { sb.AppendLine("  ERROR: HandGrabPose type unresolved"); return false; }

        var poseGO = new GameObject("HandGrabPose_Surface");
        poseGO.transform.SetParent(go.transform, false);
        poseGO.transform.position = relativeTo.position;   // TUNE (cylinder: slide out to the wall)
        poseGO.transform.rotation = relativeTo.rotation;

        var pose = poseGO.AddComponent(poseType);
        Invoke(pose, "InjectRelativeTo", new[] { typeof(Transform) }, new object[] { relativeTo });
        SetField(pose, "_usesHandPose", false);   // surface-only path (no finger curl here)

        object surface = shape switch
        {
            Shape.Cylinder => BuildCylinder(poseGO, relativeTo, sb),
            Shape.Box      => BuildBox(poseGO, relativeTo, sb),
            _              => BuildCollider(poseGO, go, sb),
        };
        if (surface == null) { sb.AppendLine("  ERROR: surface build failed"); return false; }

        if (!Invoke(pose, "InjectOptionalSurface", new[] { ResolveType(T_IGrabSurface) }, new object[] { surface }))
        {
            SetField(pose, "_surface", surface as UnityEngine.Object);
            sb.AppendLine("  (used _surface field fallback)");
        }

        poseList.Add(pose);

        var attract = EnumValue(T_HandAlignType, "AttractOnHover");
        if (attract != null) SetProp(interactable, "HandAlignment", attract);

        EditorUtility.SetDirty(interactable);
        EditorUtility.SetDirty(pose);
        sb.AppendLine($"  added {surface.GetType().Name} + HandGrabPose (usesHandPose=false); HandAlignment=AttractOnHover");
        return true;
    }

    static object BuildCollider(GameObject poseGO, GameObject target, StringBuilder sb)
    {
        var type = ResolveType(T_ColliderSurface);
        if (type == null) { sb.AppendLine("  ColliderGrabSurface unresolved"); return null; }
        var surface = poseGO.AddComponent(type);
        var col = target.GetComponentsInChildren<Collider>(true).FirstOrDefault(c => !c.isTrigger)
               ?? target.GetComponentInChildren<Collider>(true);
        if (col == null) { sb.AppendLine("  no Collider to snap to"); return null; }
        Invoke(surface, "InjectCollider", new[] { typeof(Collider) }, new object[] { col });
        return surface;
    }

    static object BuildBox(GameObject poseGO, Transform relativeTo, StringBuilder sb)
    {
        var sType = ResolveType(T_BoxSurface);
        var dType = ResolveType(T_BoxData);
        if (sType == null || dType == null) { sb.AppendLine("  BoxGrabSurface/Data unresolved"); return null; }
        var surface = poseGO.AddComponent(sType);

        var data = Activator.CreateInstance(dType);
        SetPublicField(data, "widthOffset", 0.5f);
        SetPublicField(data, "size", new Vector3(0.1f, 0f, 0.1f)); // TUNE to object footprint
        SetPublicField(data, "eulerAngles", Vector3.zero);          // TUNE orientation

        Invoke(surface, "InjectData", new[] { dType }, new object[] { data });
        Invoke(surface, "InjectRelativeTo", new[] { typeof(Transform) }, new object[] { relativeTo });
        return surface;
    }

    static object BuildCylinder(GameObject poseGO, Transform relativeTo, StringBuilder sb)
    {
        var sType = ResolveType(T_CylinderSurface);
        var dType = ResolveType(T_CylinderData);
        if (sType == null || dType == null) { sb.AppendLine("  CylinderGrabSurface/Data unresolved"); return null; }
        var surface = poseGO.AddComponent(sType);

        var data = Activator.CreateInstance(dType);
        SetPublicField(data, "startPoint", new Vector3(0f, 0.06f, 0f));  // TUNE: +half height
        SetPublicField(data, "endPoint",   new Vector3(0f, -0.06f, 0f)); // TUNE: -half height
        SetPublicField(data, "arcOffset", 0f);
        SetPublicField(data, "arcLength", 360f);

        Invoke(surface, "InjectData", new[] { dType }, new object[] { data });
        Invoke(surface, "InjectRelativeTo", new[] { typeof(Transform) }, new object[] { relativeTo });
        return surface; // TUNE: radius = distance from axis to poseGO.transform (slide out to wall)
    }

    // ---- reflection helpers ----
    static Type ResolveType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(fullName, false);
            if (t != null) return t;
        }
        return null;
    }

    static object EnumValue(string enumFullName, string member)
    {
        var t = ResolveType(enumFullName);
        if (t == null || !t.IsEnum) return null;
        try { return Enum.Parse(t, member); } catch { return null; }
    }

    static FieldInfo FindField(Type t, string name)
    {
        for (var cur = t; cur != null; cur = cur.BaseType)
        {
            var f = cur.GetField(name, BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }

    static void SetField(object obj, string name, object value)
    {
        var f = FindField(obj.GetType(), name);
        if (f != null) f.SetValue(obj, value);
    }

    static void SetPublicField(object obj, string name, object value)
    {
        var f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
        if (f != null) f.SetValue(obj, value);
    }

    static object GetProp(object obj, string name)
    {
        var p = obj.GetType().GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return p?.GetValue(obj);
    }

    static void SetProp(object obj, string name, object value)
    {
        var p = obj.GetType().GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.CanWrite) p.SetValue(obj, value);
    }

    static bool Invoke(object obj, string method, Type[] sig, object[] args)
    {
        if (sig.Any(t => t == null)) return false;
        var m = obj.GetType().GetMethod(method,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, sig, null);
        if (m == null) return false;
        m.Invoke(obj, args);
        return true;
    }
}
#endif
