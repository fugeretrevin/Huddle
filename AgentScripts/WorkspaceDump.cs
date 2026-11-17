using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>Read-only: dumps the WorkspaceRoot subtree with world pos + renderer world-bounds so
/// crowding / overlaps / occluders can be found. Also flags objects near the user eyeline.</summary>
public static class WorkspaceDump
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var ws = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name == "WorkspaceRoot");
        if (ws == null) return "WorkspaceRoot not found";
        var sb = new StringBuilder();
        sb.AppendLine("WorkspaceRoot world=" + V(ws.position));
        Walk(ws, 0, sb);
        return sb.ToString();
    }

    static void Walk(Transform t, int depth, StringBuilder sb)
    {
        string indent = new string(' ', depth * 2);
        var r = t.GetComponent<Renderer>();
        string bounds = "";
        if (r != null) bounds = $" bnds c={V(r.bounds.center)} s={V(r.bounds.size)}";
        sb.AppendLine($"{indent}{t.name} [{(t.gameObject.activeInHierarchy ? "on" : "off")}] " +
                      $"w={V(t.position)}{bounds}");
        for (int i = 0; i < t.childCount; i++)
        {
            // Don't descend into the inactive card template internals or deep rig bones (noise).
            var c = t.GetChild(i);
            if (c.name == "CardTemplate" || c.name == "Armature" || c.name.StartsWith("mixamorig"))
            {
                sb.AppendLine($"{indent}  {c.name} [..] w={V(c.position)} (not expanded)");
                continue;
            }
            if (depth < 4) Walk(c, depth + 1, sb);
        }
    }

    static string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
}
