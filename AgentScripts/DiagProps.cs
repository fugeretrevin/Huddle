using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DiagProps
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var sb = new StringBuilder();
        foreach (var agent in new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" })
        {
            var a = all.FirstOrDefault(t => t.name == agent);
            if (a == null) continue;
            sb.AppendLine($"=== {agent} renderers (name | localScale | flat?) ===");
            foreach (var r in a.GetComponentsInChildren<Renderer>(true))
            {
                var t = r.transform; var ls = t.localScale;
                bool flat = Mathf.Min(ls.x, Mathf.Min(ls.y, ls.z)) < 0.01f;
                sb.AppendLine($"  {t.name} ls=({ls.x:F3},{ls.y:F3},{ls.z:F3}) {(flat ? "FLAT" : "")}");
            }
        }
        return sb.ToString();
    }
}
