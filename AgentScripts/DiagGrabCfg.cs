using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Dump HandGrabInteractable pose/alignment config for paper vs cube to find why paper won't grab.</summary>
public static class DiagGrabCfg
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
        var sb = new StringBuilder();
        Dump(all, "DecisionDocument", sb);
        sb.AppendLine();
        Dump(all, "[BuildingBlock] Grabbable Item", sb);
        return sb.ToString();
    }

    static void Dump(System.Collections.Generic.List<GameObject> all, string name, StringBuilder sb)
    {
        sb.AppendLine($"=== {name} ===");
        var go = all.FirstOrDefault(g => g.name == name);
        if (go == null) { sb.AppendLine("  not found"); return; }
        var hgi = go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "HandGrabInteractable");
        if (hgi == null) { sb.AppendLine("  no HandGrabInteractable"); return; }
        var t = hgi.GetType();
        sb.AppendLine($"  enabled={(hgi as MonoBehaviour)?.enabled}");

        // Dump every serialized-ish field name + value (shallow) so we can eyeball differences.
        for (var cur = t; cur != null && cur.Name != "MonoBehaviour"; cur = cur.BaseType)
        {
            foreach (var f in cur.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                // skip events/delegates
                if (typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                object v;
                try { v = f.GetValue(hgi); } catch { v = "<err>"; }
                sb.AppendLine($"    {cur.Name}.{f.Name} = {Short(v)}");
            }
        }
    }

    static string Short(object v)
    {
        if (v == null) return "null";
        if (v is UnityEngine.Object uo) return uo == null ? "null(UO)" : $"{v.GetType().Name}";
        if (v is System.Collections.ICollection col) return $"[{col.Count}]";
        var s = v.ToString();
        return s.Length > 60 ? s.Substring(0, 60) : s;
    }
}
