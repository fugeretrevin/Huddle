using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>Read-only: footprint of each top-level workspace group in WorkspaceRoot-local space.</summary>
public static class LayoutBounds
{
    public static string Run()
    {
        var ws = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "WorkspaceRoot");
        if (ws == null) return "no WorkspaceRoot";
        var sb = new StringBuilder();
        sb.AppendLine("name | x[min..max] y[min..max] z[min..max] (root-local metres)");
        void Report(Transform t, string label)
        {
            var rs = t.GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0) { sb.AppendLine($"{label} | (no active renderers) lp={t.localPosition}"); return; }
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            foreach (var r in rs)
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var l = ws.InverseTransformPoint(c);
                    mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
                }
            }
            sb.AppendLine($"{label} | x[{mn.x:F2}..{mx.x:F2}] y[{mn.y:F2}..{mx.y:F2}] z[{mn.z:F2}..{mx.z:F2}]");
        }
        foreach (Transform c in ws)
        {
            if (!c.gameObject.activeSelf) continue;
            Report(c, c.name);
            if (c.name == "Phase6" || c.name == "TaskBoard" || c.name.EndsWith("Station"))
                foreach (Transform g in c)
                    if (g.gameObject.activeSelf && g.GetComponentsInChildren<Renderer>(false).Length > 0)
                        Report(g, "   " + c.name + "/" + g.name);
        }
        return sb.ToString();
    }
}
