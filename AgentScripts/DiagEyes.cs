using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DiagEyes
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
        var sb = new StringBuilder();

        var researcher = all.FirstOrDefault(g => g.name == "Agent_Researcher");
        if (researcher == null) return "Agent_Researcher not found";

        sb.AppendLine("=== Agent_Researcher full child tree (name | localPos | localScale) ===");
        foreach (var t in researcher.GetComponentsInChildren<Transform>(true))
        {
            int depth = 0; var p = t; while (p != researcher.transform && p.parent != null) { p = p.parent; depth++; }
            sb.AppendLine($"  {new string(' ', depth*2)}{t.name} | lp={V(t.localPosition)} ls={V(t.localScale)} | {(t.gameObject.activeSelf?"on":"OFF")}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Eye-like objects (name contains eye/pupil) across all agents ===");
        foreach (var go in all.Where(g => g.name.ToLower().Contains("eye") || g.name.ToLower().Contains("pupil")))
            sb.AppendLine($"  '{Path(go)}' worldPos={V(go.transform.position)} lossyScale={V(go.transform.lossyScale)}");

        return sb.ToString();
    }
    static string V(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";
    static string Path(GameObject go){var p=go.name;var t=go.transform.parent;int d=0;while(t!=null&&d<4){p=t.name+"/"+p;t=t.parent;d++;}return p;}
}
