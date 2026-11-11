using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Workspace;

/// <summary>One-shot diagnostic for the on-device bug list: paper grab wiring, facing, interactors,
/// and whether agents carry a persistent FaceUser.</summary>
public static class InspectAll
{
    static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(n)).FirstOrDefault(t => t != null);

    public static string Go()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var sb = new StringBuilder();

        // ── DecisionDocument ──
        var doc = roots.FirstOrDefault(r => r.name == "DecisionDocument");
        if (doc == null) { sb.AppendLine("NO DecisionDocument"); }
        else
        {
            sb.AppendLine($"== DecisionDocument (active={doc.activeSelf}) ==");
            foreach (var c in doc.GetComponents<Component>())
                sb.AppendLine("  comp: " + c.GetType().FullName);
            var face = doc.GetComponent<FaceUser>();
            if (face) sb.AppendLine($"  FaceUser.forward={face.forward} enabled={face.enabled}");
            var grab = doc.GetComponent(T("Oculus.Interaction.Grabbable"));
            var hand = doc.GetComponent(T("Oculus.Interaction.HandGrab.HandGrabInteractable"));
            sb.AppendLine($"  Grabbable present={grab != null}  HandGrabInteractable present={hand != null}");
            DumpRefs("  Grabbable", grab, doc, sb);
            DumpRefs("  HandGrab", hand, doc, sb);
        }

        // ── source rig ──
        var src = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                       .FirstOrDefault(t => t.name.Contains("Grabbable Item"));
        sb.AppendLine($"== source 'Grabbable Item' present={src != null} active={(src ? src.gameObject.activeInHierarchy : false)} ==");

        // ── interactors in scene (hands / controllers) ──
        foreach (var tn in new[] {
            "Oculus.Interaction.HandGrab.HandGrabInteractor",
            "Oculus.Interaction.GrabInteractor",
            "Oculus.Interaction.PokeInteractor",
            "Oculus.Interaction.DistanceHandGrabInteractor",
            "Oculus.Interaction.Input.Hmd" })
        {
            var ty = T(tn);
            int n = ty == null ? -1 : roots.SelectMany(r => r.GetComponentsInChildren(ty, true)).Count();
            sb.AppendLine($"  interactor {tn.Split('.').Last()}: {(ty == null ? "TYPE-MISSING" : n.ToString())}");
        }

        // ── agents: persistent FaceUser? ──
        foreach (var v in roots.SelectMany(r => r.GetComponentsInChildren<AgentView>(true)))
        {
            var f = v.GetComponentsInChildren<FaceUser>(true);
            sb.AppendLine($"  agent {v.agentId}: FaceUser count in subtree={f.Length}" +
                (f.Length > 0 ? " on [" + string.Join(",", f.Select(x => x.gameObject.name)) + "]" : ""));
        }

        return sb.ToString();
    }

    static string Where(UnityEngine.Object val, GameObject doc)
    {
        if (val == null) return "NULL";
        if (val is Component vc) return (vc.gameObject == doc ? "SELF" : "SRC:" + vc.gameObject.name) + "(" + val.GetType().Name + ")";
        if (val is GameObject vg) return (vg == doc ? "SELF" : "SRC:" + vg.name);
        return val.GetType().Name;
    }

    static void DumpRefs(string tag, Component c, GameObject doc, StringBuilder sb)
    {
        if (c == null) return;
        foreach (var f in c.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                     .OrderBy(f => f.Name))
        {
            object raw = f.GetValue(c);
            if (raw is UnityEngine.Object uo)
                sb.AppendLine($"{tag}.{f.Name} -> {Where(uo, doc)}");
            else if (raw is System.Collections.IEnumerable en && !(raw is string))
            {
                var items = en.Cast<object>().Where(o => o is UnityEngine.Object).Cast<UnityEngine.Object>().ToList();
                if (items.Count > 0)
                    sb.AppendLine($"{tag}.{f.Name}[{items.Count}] -> {string.Join(", ", items.Select(i => Where(i, doc)))}");
            }
        }
    }
}
