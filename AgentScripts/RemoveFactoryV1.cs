using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Removes the Software Factory / conveyor belt from the open scene, reversibly (owner feedback on rc8).
/// Every top-most object whose name contains Factory, Belt or Conveyor (Desk, Floor, Ceiling, Wall variants
/// alike) is deactivated and renamed with a "_Removed" suffix; serialized references from OTHER objects into
/// those trees are nulled so nothing re-enables them. Source art and prefabs are untouched.
/// Safe to run twice. Revert() un-renames and re-activates, and re-wires FactoryEventController.factory.
/// The runtime off-switch is SoftwareFactoryVisualController.ShowFactory (false by default).
/// </summary>
public static class RemoveFactoryV1
{
    const string Suffix = "_Removed";

    static bool Matches(string n)
    {
        if (n.EndsWith(Suffix)) return false;
        return n.IndexOf("Factory", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Belt", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Conveyor", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool InsideRemoved(Transform t)
    {
        for (var p = t; p != null; p = p.parent) if (p.name.EndsWith(Suffix)) return true;
        return false;
    }

    static List<Transform> AllTransforms(Scene s)
    {
        var list = new List<Transform>();
        foreach (var r in s.GetRootGameObjects()) list.AddRange(r.GetComponentsInChildren<Transform>(true));
        return list;
    }

    public static string Apply()
    {
        var scene = SceneManager.GetActiveScene();
        var sb = new StringBuilder();
        sb.AppendLine("RemoveFactoryV1 on " + scene.name);

        // 1. Find top-most matches (a match inside another match is covered by its ancestor).
        var tops = new List<Transform>();
        foreach (var t in AllTransforms(scene))
        {
            if (!Matches(t.name) || InsideRemoved(t)) continue;
            bool covered = false;
            for (var p = t.parent; p != null; p = p.parent) if (Matches(p.name)) { covered = true; break; }
            if (!covered) tops.Add(t);
        }

        // 2. Null serialized references from objects OUTSIDE the matched trees into them.
        int nulled = 0;
        foreach (var t in AllTransforms(scene))
        {
            bool inTop = false;
            foreach (var top in tops) if (top != null && t.IsChildOf(top)) { inTop = true; break; }
            // The factory's own event controller keeps its object but loses the visual reference too.
            foreach (var mb in t.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                var so = new SerializedObject(mb);
                var it = so.GetIterator();
                bool changed = false;
                while (it.NextVisible(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    Transform target = null;
                    var o = it.objectReferenceValue;
                    if (o is Component c) target = c.transform; else if (o is GameObject g) target = g.transform;
                    if (target == null || target.gameObject.scene != scene) continue;
                    // Keep references to the event controller (CompletionSequence.factoryEvents reads its test evidence).
                    if (o is MuseDesk.Art.FactoryEventController) continue;
                    bool pointsIn = false;
                    foreach (var top in tops) if (target.IsChildOf(top)) { pointsIn = true; break; }
                    if (!pointsIn) continue;
                    // Same-tree references (a factory part pointing at another) are harmless; only cut incoming ones,
                    // plus FactoryEventController.factory, which is what drives the clips.
                    if (inTop && !(mb.GetType().Name == "FactoryEventController")) continue;
                    sb.AppendLine("  null ref: " + t.name + "/" + mb.GetType().Name + "." + it.propertyPath + " -> " + o.name);
                    it.objectReferenceValue = null; changed = true; nulled++;
                }
                if (changed) so.ApplyModifiedProperties();
            }
        }

        // 3. Deactivate + rename.
        // A top that carries FactoryEventController stays active (its evidence listener must keep running);
        // its visual children are deactivated and renamed instead, and its own renderers are disabled.
        int removed = 0;
        foreach (var top in tops)
        {
            if (top.GetComponent<MuseDesk.Art.FactoryEventController>() != null)
            {
                foreach (var r in top.GetComponents<Renderer>()) { r.enabled = false; EditorUtility.SetDirty(r); }
                var kids = new List<Transform>();
                for (int i = 0; i < top.childCount; i++) kids.Add(top.GetChild(i));
                foreach (var k in kids)
                {
                    if (k.name.EndsWith(Suffix)) continue;
                    sb.AppendLine("  removed child: " + Path(k) + (k.gameObject.activeSelf ? " (was active)" : " (was inactive)"));
                    k.gameObject.SetActive(false);
                    k.name = k.name + Suffix;
                    EditorUtility.SetDirty(k.gameObject);
                    removed++;
                }
                continue;
            }
            sb.AppendLine("  removed: " + Path(top) + (top.gameObject.activeSelf ? " (was active)" : " (was inactive)"));
            top.gameObject.SetActive(false);
            top.name = top.name + Suffix;
            EditorUtility.SetDirty(top.gameObject);
            removed++;
        }

        sb.AppendLine($"removed={removed} refsNulled={nulled}" + (removed == 0 ? " (nothing left to remove: already applied)" : ""));
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return sb.ToString();
    }

    public static string Revert()
    {
        var scene = SceneManager.GetActiveScene();
        var sb = new StringBuilder("RemoveFactoryV1.Revert\n");
        int n = 0;
        foreach (var t in AllTransforms(scene))
        {
            if (!t.name.EndsWith(Suffix)) continue;
            t.name = t.name.Substring(0, t.name.Length - Suffix.Length);
            t.gameObject.SetActive(true);
            EditorUtility.SetDirty(t.gameObject);
            sb.AppendLine("  restored: " + Path(t)); n++;
        }
        foreach (var fec in Object.FindObjectsByType<MuseDesk.Art.FactoryEventController>(FindObjectsInactive.Include))
        {
            if (fec.factory == null) { fec.factory = fec.GetComponent<MuseDesk.Art.SoftwareFactoryVisualController>(); EditorUtility.SetDirty(fec); }
            foreach (var r in fec.GetComponents<Renderer>()) { r.enabled = true; EditorUtility.SetDirty(r); }
        }
        sb.AppendLine($"restored={n}. Also set SoftwareFactoryVisualController.ShowFactory = true in code. Other nulled refs are not auto-restored.");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return sb.ToString();
    }

    static string Path(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }
}
