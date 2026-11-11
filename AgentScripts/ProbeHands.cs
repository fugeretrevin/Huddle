using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProbeHands
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToList();
        var sb = new StringBuilder();

        sb.AppendLine("=== JointsRadiusFeature instances ===");
        foreach (var go in all)
            foreach (var c in go.GetComponents<Component>())
                if (c != null && c.GetType().Name == "JointsRadiusFeature")
                    sb.AppendLine($"  on '{P(go)}'");

        sb.AppendLine();
        sb.AppendLine("=== HandVisual instances + siblings ===");
        foreach (var go in all)
            foreach (var c in go.GetComponents<Component>())
                if (c != null && c.GetType().Name == "HandVisual")
                {
                    sb.AppendLine($"  HandVisual on '{P(go)}'");
                    foreach (var sib in go.GetComponents<Component>())
                        if (sib != null) sb.AppendLine($"       sibling: {sib.GetType().Name}");
                }

        sb.AppendLine();
        sb.AppendLine("=== Grab cube transform (for prop placement) ===");
        var cube = all.FirstOrDefault(g => g.name == "[BuildingBlock] Grabbable Item");
        if (cube != null)
            sb.AppendLine($"  pos={cube.transform.position} parent={cube.transform.parent?.name}");
        var wsRoot = all.FirstOrDefault(g => g.name == "WorkspaceRoot");
        if (wsRoot != null)
            sb.AppendLine($"  WorkspaceRoot pos={wsRoot.transform.position}");
        return sb.ToString();
    }

    static string P(GameObject go)
    {
        var p = go.name; var t = go.transform.parent; int d = 0;
        while (t != null && d < 4) { p = t.name + "/" + p; t = t.parent; d++; }
        return p;
    }
}
