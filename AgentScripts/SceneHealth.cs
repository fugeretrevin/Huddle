using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Read-only scene health report: missing scripts, unassigned references on MuseDesk
/// components, poke-button wiring, FaceUser settings. Changes nothing.</summary>
public static class SceneHealth
{
    static string Path(Transform t)
    {
        var parts = new List<string>();
        while (t != null) { parts.Add(t.name); t = t.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder();
        sb.AppendLine($"scene={scene.path} dirty={scene.isDirty} roots={scene.rootCount}");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        sb.AppendLine($"gameObjects={all.Count}");

        int missing = 0;
        foreach (var t in all)
        {
            int m = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            if (m > 0) { missing += m; sb.AppendLine($"MISSING SCRIPT x{m}: {Path(t)}"); }
        }
        sb.AppendLine($"missingScripts={missing}");

        sb.AppendLine("--- MuseDesk components with unassigned object references ---");
        foreach (var t in all)
            foreach (var mb in t.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                string ns = mb.GetType().Namespace ?? "";
                if (!ns.StartsWith("MuseDesk")) continue;
                var so = new SerializedObject(mb);
                var it = so.GetIterator();
                var nulls = new List<string>();
                bool enter = true;
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.name != "m_Script")
                    {
                        if (it.objectReferenceValue == null)
                            nulls.Add(it.name);
                    }
                }
                string state = (mb.enabled ? "" : " [disabled]") + (t.gameObject.activeInHierarchy ? "" : " [inactive]");
                sb.AppendLine($"{mb.GetType().Name}{state} @ {Path(t)}" + (nulls.Count > 0 ? "  NULL: " + string.Join(",", nulls) : ""));
            }

        sb.AppendLine("--- poke buttons (InteractableUnityEventWrapper) ---");
        foreach (var t in all)
            foreach (var mb in t.GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb.GetType().Name != "InteractableUnityEventWrapper") continue;
                var so = new SerializedObject(mb);
                var calls = so.FindProperty("_whenSelect.m_PersistentCalls.m_Calls");
                var targets = new List<string>();
                if (calls != null)
                    for (int i = 0; i < calls.arraySize; i++)
                    {
                        var c = calls.GetArrayElementAtIndex(i);
                        var tgt = c.FindPropertyRelative("m_Target").objectReferenceValue;
                        targets.Add((tgt != null ? tgt.GetType().Name : "NULL") + "." + c.FindPropertyRelative("m_MethodName").stringValue);
                    }
                sb.AppendLine($"{Path(t)} lossy={t.lossyScale.x:F3} active={t.gameObject.activeInHierarchy} -> {(targets.Count > 0 ? string.Join(" | ", targets) : "NO CALLS")}");
            }

        sb.AppendLine("--- cameras / audio listeners ---");
        foreach (var c in all.SelectMany(t => t.GetComponents<Camera>())) sb.AppendLine($"Camera {Path(c.transform)} tag={c.tag} enabled={c.enabled}");
        sb.AppendLine("audioListeners=" + all.SelectMany(t => t.GetComponents<AudioListener>()).Count());
        return sb.ToString();
    }
}
