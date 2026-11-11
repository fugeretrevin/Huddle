using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class InspectProps
{
    public static string Dump()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder();
        foreach (var name in new[] { "Poke Interaction", "Grabbable Item", "WorkspaceRoot" })
        {
            var go = Find(scene, name);
            if (go == null) { sb.AppendLine($"[{name}] NOT FOUND"); continue; }
            sb.AppendLine($"[{go.name}] active={go.activeInHierarchy} worldPos={go.transform.position} " +
                          $"lossyScale={go.transform.lossyScale}");
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                var comps = t.GetComponents<Component>()
                    .Where(c => c != null && c is not Transform)
                    .Select(c => c.GetType().Name);
                var tag = t == go.transform ? "" : "  ";
                sb.AppendLine($"{tag}  {t.name} [{string.Join(",", comps)}]");
            }
        }
        return sb.ToString();
    }

    static GameObject Find(UnityEngine.SceneManagement.Scene scene, string name)
    {
        foreach (var r in scene.GetRootGameObjects())
        {
            if (r.name == name) return r;
            var found = r.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == name);
            if (found != null) return found.gameObject;
        }
        return null;
    }
}
