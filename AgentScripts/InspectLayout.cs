using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>Read-only: dumps world pose of the agents, board, workspace root, and camera so the
/// board can be repositioned "behind the agents" with real numbers.</summary>
public static class InspectLayout
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine("active scene: " + scene.path);
        foreach (var n in new[] { "WorkspaceRoot", "TaskBoard", "Agent_Researcher", "Agent_Developer",
                                   "Agent_Tester", "CenterEyeAnchor", "Main Camera", "TrackingSpace",
                                   "OVRCameraRig", "CameraRig" })
        {
            var go = all.FirstOrDefault(g => g != null && g.name == n);
            if (go == null) { sb.AppendLine($"{n}: (not found)"); continue; }
            var t = go.transform;
            string parentName = t.parent != null ? t.parent.name : "<root>";
            sb.AppendLine($"{n}: active={go.activeInHierarchy} worldPos={V(t.position)} " +
                          $"worldEuler={V(t.eulerAngles)} localPos={V(t.localPosition)} parent={parentName}");
        }
        // Board children bounds hint
        var board = all.FirstOrDefault(g => g != null && g.name == "TaskBoard");
        if (board != null)
        {
            var rends = board.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                sb.AppendLine($"TaskBoard combined renderer bounds: center={V(b.center)} size={V(b.size)}");
            }
        }
        return sb.ToString();
    }

    static string V(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";
}
