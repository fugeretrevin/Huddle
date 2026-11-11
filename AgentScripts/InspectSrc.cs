using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Dump the working 'Grabbable Item' rig: its components and the Grabbable transformer refs.</summary>
public static class InspectSrc
{
    public static string Go()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var src = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name.Contains("Grabbable Item"))?.gameObject;
        if (src == null) return "no source";
        var sb = new StringBuilder();
        sb.AppendLine($"'{src.name}' children:");
        foreach (var t in src.GetComponentsInChildren<Transform>(true))
        {
            sb.AppendLine($"  [{t.name}]");
            foreach (var c in t.GetComponents<Component>())
                sb.AppendLine($"      {c.GetType().FullName}");
        }
        // Grabbable transformer refs
        var grab = src.GetComponents<Component>().FirstOrDefault(c => c.GetType().FullName == "Oculus.Interaction.Grabbable");
        if (grab != null)
        {
            sb.AppendLine("Grabbable fields:");
            foreach (var f in grab.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                var v = f.GetValue(grab) as UnityEngine.Object;
                if (v != null) sb.AppendLine($"  {f.Name} -> {v.GetType().Name}");
            }
        }
        return sb.ToString();
    }
}
