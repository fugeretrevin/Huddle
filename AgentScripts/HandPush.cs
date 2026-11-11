using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Implements feedback #6: hands physically interact with objects instead of gliding through.
///  - Adds Oculus ISDK HandPhysicsCapsules (solid, _asTriggers=false) to both tracked hands so the
///    finger/palm capsules push non-kinematic rigidbodies.
///  - Spawns a small rimmed tray of pushable props (a mug + a block) within reach on the desk.
/// Additive + defensive: if any ISDK field can't be resolved it logs and skips, never throws, and
/// never touches the existing (working) grab interactors.
/// </summary>
public static class HandPush
{
    static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
        .FirstOrDefault(t => t.Name == name);

    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
        var sb = new StringBuilder();

        var hpcType = FindType("HandPhysicsCapsules");
        if (hpcType == null) sb.AppendLine("CAPSULES: HandPhysicsCapsules type not found — skipping capsules");
        else
        {
            AddCapsules(all, hpcType, "OVRHandVisualLeft", "ComprehensiveInteractorsLeft", sb);
            AddCapsules(all, hpcType, "OVRHandVisualRight", "ComprehensiveInteractorsRight", sb);
        }

        BuildTray(all, roots, sb);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("SAVED scene.");
        return sb.ToString();
    }

    static void AddCapsules(System.Collections.Generic.List<GameObject> all, Type hpcType,
        string handVisualName, string interactorsName, StringBuilder sb)
    {
        var hvGo = all.FirstOrDefault(g => g.name == handVisualName);
        if (hvGo == null) { sb.AppendLine($"CAPSULES: '{handVisualName}' not found"); return; }

        // Idempotent: remove any previous child we added.
        var prev = hvGo.transform.Find("HandPhysicsPush");
        if (prev != null) UnityEngine.Object.DestroyImmediate(prev.gameObject);

        var hv = hvGo.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "HandVisual");
        if (hv == null) { sb.AppendLine($"CAPSULES: no HandVisual on '{handVisualName}'"); return; }

        // Source the IHand from the HandVisual's own serialized _hand.
        var handObj = GetField(hv, "_hand")?.GetValue(hv) as UnityEngine.Object;

        // Matching JointsRadiusFeature from this hand's Features node.
        var feature = all.Where(g => g.name == "Features" && FullPath(g).Contains(interactorsName))
            .SelectMany(g => g.GetComponents<Component>())
            .FirstOrDefault(c => c != null && c.GetType().Name == "JointsRadiusFeature");

        var child = new GameObject("HandPhysicsPush");
        child.transform.SetParent(hvGo.transform, false);
        var hpc = child.AddComponent(hpcType);

        SetField(hpc, "_handVisual", hv);
        if (handObj != null) SetField(hpc, "_hand", handObj);
        if (feature != null) SetField(hpc, "_jointsRadiusFeature", feature);
        SetBool(hpc, "_asTriggers", false);     // solid -> pushes rigidbodies
        SetMaskAll(hpc, "_mask");

        sb.AppendLine($"CAPSULES: added on '{handVisualName}' (hand={(handObj ? "ok" : "NULL")}, feature={(feature ? "ok" : "NULL")})");
    }

    static void BuildTray(System.Collections.Generic.List<GameObject> all, GameObject[] roots, StringBuilder sb)
    {
        var ws = all.FirstOrDefault(g => g.name == "WorkspaceRoot");
        Transform parent = ws != null ? ws.transform : null;

        var existing = all.FirstOrDefault(g => g.name == "PhysicsTray");
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

        var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material Mat(Color c) { var m = new Material(lit); m.color = c; m.SetColor("_BaseColor", c); return m; }

        var tray = new GameObject("PhysicsTray");
        if (parent != null) tray.transform.SetParent(parent, false);
        tray.transform.position = new Vector3(0.16f, 1.02f, 0.40f);  // within reach, right of the grab cube

        var trayMat = Mat(new Color(0.55f, 0.55f, 0.60f));
        // floor
        MakeBox(tray.transform, "Floor", new Vector3(0, 0, 0), new Vector3(0.26f, 0.02f, 0.26f), trayMat, true);
        // rim walls
        MakeBox(tray.transform, "Rim_N", new Vector3(0, 0.03f, 0.12f), new Vector3(0.26f, 0.06f, 0.02f), trayMat, true);
        MakeBox(tray.transform, "Rim_S", new Vector3(0, 0.03f, -0.12f), new Vector3(0.26f, 0.06f, 0.02f), trayMat, true);
        MakeBox(tray.transform, "Rim_E", new Vector3(0.12f, 0.03f, 0), new Vector3(0.02f, 0.06f, 0.26f), trayMat, true);
        MakeBox(tray.transform, "Rim_W", new Vector3(-0.12f, 0.03f, 0), new Vector3(0.02f, 0.06f, 0.26f), trayMat, true);

        // props
        MakeBlock(tray.transform, "Prop_Block", new Vector3(0.05f, 0.07f, 0.03f), 0.05f, Mat(new Color(0.30f, 0.55f, 0.95f)), 0.14f, sb);
        MakeMug(tray.transform, "Prop_Mug", new Vector3(-0.05f, 0.08f, -0.02f), Mat(new Color(0.95f, 0.55f, 0.30f)), 0.20f, sb);

        sb.AppendLine($"TRAY: built at {tray.transform.position} under {(parent ? parent.name : "scene root")}");
    }

    static GameObject MakeBox(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool collider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        var r = go.GetComponent<Renderer>(); if (r) r.sharedMaterial = mat;
        if (!collider) { var c = go.GetComponent<Collider>(); if (c) UnityEngine.Object.DestroyImmediate(c); }
        return go;
    }

    static void MakeBlock(Transform parent, string name, Vector3 localPos, float s, Material mat, float mass, StringBuilder sb)
    {
        var go = MakeBox(parent, name, localPos, new Vector3(s, s, s), mat, true);
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass; rb.linearDamping = 0.1f; rb.angularDamping = 0.6f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        AddDelayed(go, sb);
    }

    static void MakeMug(Transform parent, string name, Vector3 localPos, Material mat, float mass, StringBuilder sb)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = new Vector3(0.05f, 0.045f, 0.05f);
        var r = go.GetComponent<Renderer>(); if (r) r.sharedMaterial = mat;
        // Cylinder primitive ships a MeshCollider (not usable for dynamic) — replace with a capsule.
        var mc = go.GetComponent<Collider>(); if (mc) UnityEngine.Object.DestroyImmediate(mc);
        var cap = go.AddComponent<CapsuleCollider>();
        cap.direction = 1; cap.radius = 0.5f; cap.height = 2f;  // local (scaled by transform)
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass; rb.linearDamping = 0.1f; rb.angularDamping = 0.6f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        AddDelayed(go, sb);
    }

    static void AddDelayed(GameObject go, StringBuilder sb)
    {
        var t = FindType("DelayedPhysics");
        if (t != null) go.AddComponent(t);
        else sb.AppendLine("TRAY: DelayedPhysics type not found (props will be dynamic immediately)");
    }

    // ---- reflection helpers ----
    static FieldInfo GetField(object o, string name)
    {
        for (var t = o.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }
    static void SetField(object o, string name, object val) { var f = GetField(o, name); if (f != null) f.SetValue(o, val); }
    static void SetBool(object o, string name, bool val) { var f = GetField(o, name); if (f != null && f.FieldType == typeof(bool)) f.SetValue(o, val); }
    static void SetMaskAll(object o, string name)
    {
        var f = GetField(o, name);
        if (f == null || !f.FieldType.IsEnum) return;
        long acc = 0;
        foreach (var v in Enum.GetValues(f.FieldType)) acc |= Convert.ToInt64(v);
        f.SetValue(o, Enum.ToObject(f.FieldType, acc));
    }

    static string FullPath(GameObject go)
    {
        var p = go.name; var t = go.transform.parent;
        while (t != null) { p = t.name + "/" + p; t = t.parent; }
        return p;
    }
}
