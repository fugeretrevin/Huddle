using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// =============================================================================
// GrabPosesDraft  --  DRAFT Editor builder for Meta ISDK 209 "dynamic grab poses"
// =============================================================================
// Purpose: give a grabbable a SURFACE-BASED HandGrabPose so the grab snaps to the
// OUTSIDE of the object (object sticks to the hand, wrist aligns) WITHOUT any
// recorded hand pose. This is "Approach B" in docs/DYNAMIC_GRAB_POSES.md.
//
// IMPORTANT HONEST CAVEAT (verified from package source, see the .md section 0):
//   A grab surface alone snaps the GRIP/WRIST only. It does NOT curl the fingers
//   around the geometry. True Alyx-style finger wrap needs a recorded HandPose
//   (joint rotations) -- see HandGrabStateVisual.cs lines 100-133. This script
//   deliberately sets _usesHandPose=false (surface-only, no recording).
//
// This file is a DRAFT. It uses ONLY real ISDK 209 type/field/method names found
// in Library/PackageCache/com.meta.xr.sdk.interaction@75f85e8aa6bd. Everything is
// done by reflection because the AgentScripts assembly does not reference
// Oculus.Interaction (same pattern as FixGrabWire.cs / GrabColliders.cs).
//
// The other engineer runs/tests this. Lines that need in-editor verification are
// marked // VERIFY, and geometry that must be eyeballed is marked // TUNE.
// =============================================================================
public static class GrabPosesDraft
{
    // Full type names confirmed in the installed package.
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

    // Known grabbables in the MuseDesk scene (names from existing AgentScripts).
    // Add/adjust as the scene evolves. Shape picks the surface kind.
    static readonly (string name, Shape shape)[] Targets = new[]
    {
        ("[BuildingBlock] Grabbable Item", Shape.Box),      // orange cube
        ("DecisionDocument",               Shape.Box),      // flat paper (thin box)
        ("Mug",                            Shape.Cylinder),  // VERIFY: mug/cylinder may not exist yet
    };

    /// <summary>Process every known target. Returns a human-readable log.</summary>
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
            try
            {
                if (AddSurfacePose(go, shape, sb)) touched++;
            }
            catch (Exception e)
            {
                sb.AppendLine($"  ERROR: {e.GetType().Name}: {e.Message}");
            }
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

        // Reference transform every pose/surface measures against (= _rigidbody.transform).
        var relativeTo = GetProp(interactable, "RelativeTo") as Transform;
        if (relativeTo == null) { sb.AppendLine("  RelativeTo is null (no Rigidbody?)"); return false; }

        // Don't double-add if a surface pose already exists.
        var listField = FindField(interactable.GetType(), "_handGrabPoses");
        var poseList = listField?.GetValue(interactable) as IList;
        if (poseList == null) { sb.AppendLine("  ERROR: _handGrabPoses not found"); return false; }
        if (poseList.Count > 0)
        {
            sb.AppendLine($"  already has {poseList.Count} pose(s); leaving as-is (VERIFY)");
            return false;
        }

        // ---- 1. Create the HandGrabPose child --------------------------------
        var poseType = ResolveType(T_HandGrabPose);
        if (poseType == null) { sb.AppendLine("  ERROR: HandGrabPose type unresolved"); return false; }

        var poseGO = new GameObject("HandGrabPose_Surface");
        poseGO.transform.SetParent(go.transform, false);
        // TUNE: start at object centre. For the cylinder this point's distance from the
        // axis becomes the radius; nudge it out to the mug wall in the Scene view.
        poseGO.transform.position = relativeTo.position;
        poseGO.transform.rotation = relativeTo.rotation;

        var pose = poseGO.AddComponent(poseType);

        // relativeTo + surface-only (no recorded fingers)
        Invoke(pose, "InjectRelativeTo", new[] { typeof(Transform) }, new object[] { relativeTo });
        SetField(pose, "_usesHandPose", false);   // surface-only path (see .md section 0)

        // ---- 2. Build the surface component ----------------------------------
        object surface = shape switch
        {
            Shape.Cylinder => BuildCylinder(poseGO, relativeTo, sb),
            Shape.Box      => BuildBox(poseGO, relativeTo, sb),
            _              => BuildCollider(poseGO, go, sb),
        };
        if (surface == null) { sb.AppendLine("  ERROR: surface build failed"); return false; }

        // ---- 3. Wire surface into the pose -----------------------------------
        // InjectOptionalSurface(IGrabSurface) sets both _surface and runtime _snapSurface.
        if (!Invoke(pose, "InjectOptionalSurface", new[] { ResolveType(T_IGrabSurface) }, new object[] { surface }))
        {
            // Fallback: set the serialized field directly (SnapSurface getter falls back to _surface).
            SetField(pose, "_surface", surface as UnityEngine.Object);
            sb.AppendLine("  (used _surface field fallback)");
        }

        // ---- 4. Add the pose to the interactable's list ----------------------
        poseList.Add(pose);   // live List<HandGrabPose>; cast is implicit via reflection

        // ---- 5. Engage on approach (optional but recommended) ----------------
        // AttractOnHover = wrist+fingers conform during hover; closest to Alyx.
        var attract = EnumValue(T_HandAlignType, "AttractOnHover");
        if (attract != null) SetProp(interactable, "HandAlignment", attract);  // public setter exists

        EditorUtility.SetDirty(interactable);
        EditorUtility.SetDirty(pose);
        sb.AppendLine($"  added {surface.GetType().Name} + HandGrabPose (usesHandPose=false); HandAlignment=AttractOnHover");
        return true;
    }

    // ---- surface builders ----------------------------------------------------

    // Simplest, geometry-free: snap grip to nearest point on an existing collider.
    static object BuildCollider(GameObject poseGO, GameObject target, StringBuilder sb)
    {
        var type = ResolveType(T_ColliderSurface);
        if (type == null) { sb.AppendLine("  ColliderGrabSurface unresolved"); return null; }
        var surface = poseGO.AddComponent(type);
        // VERIFY: first non-trigger collider may be a visual collider, not the grab volume.
        var col = target.GetComponentsInChildren<Collider>(true).FirstOrDefault(c => !c.isTrigger)
               ?? target.GetComponentInChildren<Collider>(true);
        if (col == null) { sb.AppendLine("  no Collider to snap to"); return null; }
        Invoke(surface, "InjectCollider", new[] { typeof(Collider) }, new object[] { col });
        return surface;
    }

    // Flat rectangle: good for paper and for face-grabbing the cube.
    static object BuildBox(GameObject poseGO, Transform relativeTo, StringBuilder sb)
    {
        var sType = ResolveType(T_BoxSurface);
        var dType = ResolveType(T_BoxData);
        if (sType == null || dType == null) { sb.AppendLine("  BoxGrabSurface/Data unresolved"); return null; }
        var surface = poseGO.AddComponent(sType);

        var data = Activator.CreateInstance(dType);
        // Public fields on BoxGrabSurfaceData: widthOffset(0..1), snapOffset(Vector4),
        // size(Vector3, Y ignored), eulerAngles(Vector3).
        SetPublicField(data, "widthOffset", 0.5f);
        SetPublicField(data, "size", new Vector3(0.1f, 0f, 0.1f)); // TUNE to object footprint
        SetPublicField(data, "eulerAngles", Vector3.zero);          // TUNE orientation

        Invoke(surface, "InjectData", new[] { dType }, new object[] { data });
        Invoke(surface, "InjectRelativeTo", new[] { typeof(Transform) }, new object[] { relativeTo });
        return surface;
    }

    // Cylinder wall: mug/can. Radius is derived from the pose transform offset (TUNE above).
    static object BuildCylinder(GameObject poseGO, Transform relativeTo, StringBuilder sb)
    {
        var sType = ResolveType(T_CylinderSurface);
        var dType = ResolveType(T_CylinderData);
        if (sType == null || dType == null) { sb.AppendLine("  CylinderGrabSurface/Data unresolved"); return null; }
        var surface = poseGO.AddComponent(sType);

        var data = Activator.CreateInstance(dType);
        // Public fields on CylinderSurfaceData: startPoint, endPoint (axis caps, local to
        // relativeTo), arcOffset, arcLength (degrees).
        SetPublicField(data, "startPoint", new Vector3(0f, 0.06f, 0f));  // TUNE: +half height
        SetPublicField(data, "endPoint",   new Vector3(0f, -0.06f, 0f)); // TUNE: -half height
        SetPublicField(data, "arcOffset", 0f);
        SetPublicField(data, "arcLength", 360f);  // full wall; try ~200 to bias outward

        Invoke(surface, "InjectData", new[] { dType }, new object[] { data });
        Invoke(surface, "InjectRelativeTo", new[] { typeof(Transform) }, new object[] { relativeTo });
        // NOTE: radius = distance from axis to poseGO.transform. Because poseGO sits at the
        // centre by default the radius ~0; the engineer must slide poseGO out to the wall. // TUNE
        return surface;
    }

    // ---- reflection helpers (mirrors FixGrabWire.cs / GrabColliders.cs) -------

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

    // For the [Serializable] *Data classes whose fields are plain public instance fields.
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

    // Invoke a method with an explicit signature; returns true on success.
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
