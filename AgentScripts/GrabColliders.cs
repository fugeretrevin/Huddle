using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Compare the grab-volume colliders of the working cube vs the paper's HandGrabInteractable.</summary>
public static class GrabColliders
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToList();
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

        // Try all plausible collider-list field/prop names across the hierarchy.
        foreach (var fname in new[] { "_colliders", "Colliders", "_boundsCollider", "_physicsGrabbable" })
        {
            var f = FindField(t, fname);
            if (f != null)
            {
                var v = f.GetValue(hgi);
                sb.AppendLine($"  field {fname} = {Describe(v)}");
            }
            var p = t.GetProperty(fname, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.GetIndexParameters().Length == 0)
            {
                try { sb.AppendLine($"  prop  {fname} = {Describe(p.GetValue(hgi))}"); } catch { }
            }
        }

        // The Grabbable's collider list too.
        var grab = go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "Grabbable");
        if (grab != null)
        {
            foreach (var fname in new[] { "_targetTransform", "_physicsGrabbable" })
            {
                var f = FindField(grab.GetType(), fname);
                if (f != null) sb.AppendLine($"  Grabbable.{fname} = {Describe(f.GetValue(grab))}");
            }
        }

        // All colliders actually on the object
        var cols = go.GetComponents<Collider>();
        sb.AppendLine($"  Colliders ON object: {cols.Length}");
        foreach (var c in cols)
            sb.AppendLine($"     {c.GetType().Name} isTrigger={c.isTrigger} enabled={c.enabled}");
    }

    static string Describe(object v)
    {
        if (v == null) return "null";
        if (v is Component vcomp) { try { return $"{vcomp.GetType().Name}@{vcomp.gameObject.name}"; } catch { return "<unassigned ref>"; } }
        if (v is IEnumerable en && !(v is string))
        {
            var items = en.Cast<object>().Select(o =>
            {
                if (o is Component c) return $"{c.GetType().Name}@{c.gameObject.name}";
                return o?.ToString() ?? "null";
            }).ToList();
            return $"[{items.Count}] {string.Join(", ", items)}";
        }
        if (v is Component vc) return $"{vc.GetType().Name}@{vc.gameObject.name}";
        return v.ToString();
    }

    static FieldInfo FindField(Type t, string name)
    {
        for (var cur = t; cur != null; cur = cur.BaseType)
        {
            var f = cur.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }
}
