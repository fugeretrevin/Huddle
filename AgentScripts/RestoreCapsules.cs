using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Re-add the ISDK HandPhysicsCapsules to both rendered hands (hand collision / push) WITHOUT
/// touching the grab-wired props or tray. Capsule-only restore of what RemoveCapsules deleted.
/// </summary>
public static class RestoreCapsules
{
    static Type FindType(string n) => AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
        .FirstOrDefault(t => t.Name == n);

    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToList();
        var sb = new StringBuilder();
        var hpcType = FindType("HandPhysicsCapsules");
        if (hpcType == null) return "HandPhysicsCapsules type not found";

        foreach (var pair in new[] { ("OVRHandVisualLeft", "ComprehensiveInteractorsLeft"), ("OVRHandVisualRight", "ComprehensiveInteractorsRight") })
        {
            var hvGo = all.FirstOrDefault(g => g.name == pair.Item1);
            if (hvGo == null) { sb.AppendLine($"{pair.Item1} not found"); continue; }
            var prev = hvGo.transform.Find("HandPhysicsPush");
            if (prev != null) UnityEngine.Object.DestroyImmediate(prev.gameObject);

            var hv = hvGo.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "HandVisual");
            if (hv == null) { sb.AppendLine($"no HandVisual on {pair.Item1}"); continue; }
            var handObj = GetField(hv, "_hand")?.GetValue(hv) as UnityEngine.Object;
            var feature = all.Where(g => g.name == "Features" && FullPath(g).Contains(pair.Item2))
                .SelectMany(g => g.GetComponents<Component>())
                .FirstOrDefault(c => c != null && c.GetType().Name == "JointsRadiusFeature");

            var child = new GameObject("HandPhysicsPush");
            child.transform.SetParent(hvGo.transform, false);
            var hpc = child.AddComponent(hpcType);
            SetField(hpc, "_handVisual", hv);
            if (handObj != null) SetField(hpc, "_hand", handObj);
            if (feature != null) SetField(hpc, "_jointsRadiusFeature", feature);
            SetBool(hpc, "_asTriggers", false);
            SetMaskAll(hpc, "_mask");
            sb.AppendLine($"restored capsules on {pair.Item1} (hand={(handObj ? "ok" : "NULL")}, feature={(feature ? "ok" : "NULL")})");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("SAVED.");
        return sb.ToString();
    }

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
        long acc = 0; foreach (var v in Enum.GetValues(f.FieldType)) acc |= Convert.ToInt64(v);
        f.SetValue(o, Enum.ToObject(f.FieldType, acc));
    }
    static string FullPath(GameObject go) { var p = go.name; var t = go.transform.parent; while (t != null) { p = t.name + "/" + p; t = t.parent; } return p; }
}
