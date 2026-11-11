using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Read-only diagnostic: dumps the actual interaction rig so we know WHY grab/poke fail on-head.
/// Finds hand-tracking sources, interactors, and the paper/cube interactable wiring.
/// </summary>
public static class DiagRig
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToList();
        var sb = new StringBuilder();

        sb.AppendLine("=== HAND / TRACKING SOURCES ===");
        DumpByTypeName(all, sb, new[] {
            "OVRHand", "OVRSkeleton", "HandVisual", "Hand", "OVRCameraRig",
            "OVRManager", "HandSkeleton", "FromOVRHandDataSource", "FromOpenXRHandDataSource",
            "OVRInput", "SyntheticHand", "DataModifier"
        });

        sb.AppendLine();
        sb.AppendLine("=== INTERACTORS (what does the grabbing/poking) ===");
        DumpByTypeName(all, sb, new[] {
            "HandGrabInteractor", "TouchHandGrabInteractor", "PokeInteractor",
            "GrabInteractor", "RayInteractor", "DistanceHandGrabInteractor",
            "HandRef", "InteractorGroup"
        });

        sb.AppendLine();
        sb.AppendLine("=== DecisionDocument (the paper) ===");
        var paper = all.FirstOrDefault(g => g.name == "DecisionDocument");
        DumpInteractable(paper, sb);

        sb.AppendLine();
        sb.AppendLine("=== Orange cube control (Grabbable Item) ===");
        var cube = all.FirstOrDefault(g => g.name.Contains("Grabbable Item") || (g.name.Contains("Grabbable") && g != paper));
        DumpInteractable(cube, sb);

        return sb.ToString();
    }

    static void DumpByTypeName(System.Collections.Generic.List<GameObject> all, StringBuilder sb, string[] typeNames)
    {
        foreach (var go in all)
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                var tn = c.GetType().Name;
                if (typeNames.Contains(tn))
                {
                    var active = go.activeInHierarchy ? "ACTIVE" : "inactive";
                    var en = (c is Behaviour b) ? (b.enabled ? "on" : "OFF") : "-";
                    sb.AppendLine($"  {tn,-28} on '{Path(go)}' [{active}, enabled={en}]");
                }
            }
        }
    }

    static void DumpInteractable(GameObject go, StringBuilder sb)
    {
        if (go == null) { sb.AppendLine("  (not found)"); return; }
        sb.AppendLine($"  GameObject: '{Path(go)}' active={go.activeInHierarchy}");
        foreach (var c in go.GetComponents<Component>())
        {
            if (c == null) { sb.AppendLine("    <MISSING SCRIPT>"); continue; }
            var t = c.GetType();
            var en = (c is Behaviour b) ? (b.enabled ? "on" : "OFF") : ((c is Collider col) ? (col.enabled ? "on" : "OFF") : "-");
            sb.AppendLine($"    {t.Name} [{en}]");
            if (c is Rigidbody rb)
                sb.AppendLine($"       kinematic={rb.isKinematic} gravity={rb.useGravity}");
            if (c is Collider cc)
                sb.AppendLine($"       isTrigger={cc.isTrigger}");
            if (c is BoxCollider bc)
                sb.AppendLine($"       size={bc.size} center={bc.center}");
            if (t.Name == "FaceUser")
            {
                var f = t.GetField("forward", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                sb.AppendLine($"       FaceUser.forward={(f != null ? f.GetValue(c) : "?")}");
            }
            if (t.Name == "PlaceInFrontOnStart" || t.Name == "HeldPaperController")
            {
                var f = t.GetField("faceUser", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (f != null) sb.AppendLine($"       faceUser={f.GetValue(c)}");
            }
            // Oculus interactable: dump _pointableElement / _rigidbody targets
            if (t.Name.Contains("HandGrabInteractable") || t.Name.Contains("PointableElement") || t.Name.Contains("Grabbable") || t.Name.Contains("PokeInteractable"))
            {
                DumpRefField(c, "_pointableElement", sb);
                DumpRefField(c, "_rigidbody", sb);
                DumpRefField(c, "<PointableElement>k__BackingField", sb);
                DumpIntField(c, "_supportedGrabTypes", sb);
            }
        }
    }

    static void DumpRefField(Component c, string field, StringBuilder sb)
    {
        var f = FindField(c.GetType(), field);
        if (f == null) return;
        var v = f.GetValue(c);
        string tgt = "null";
        if (v is Component vc) tgt = $"{vc.GetType().Name} on '{vc.gameObject.name}'";
        else if (v != null) tgt = v.ToString();
        sb.AppendLine($"       {field} -> {tgt}");
    }

    static void DumpIntField(Component c, string field, StringBuilder sb)
    {
        var f = FindField(c.GetType(), field);
        if (f == null) return;
        sb.AppendLine($"       {field} = {f.GetValue(c)}");
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

    static string Path(GameObject go)
    {
        var p = go.name;
        var t = go.transform.parent;
        int depth = 0;
        while (t != null && depth < 4) { p = t.name + "/" + p; t = t.parent; depth++; }
        return p;
    }
}
