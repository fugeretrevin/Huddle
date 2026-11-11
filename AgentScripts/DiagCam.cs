using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DiagCam
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var cams = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Camera>(true)).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"=== CAMERAS ({cams.Count}) ===");
        foreach (var c in cams)
        {
            sb.AppendLine($"  '{c.gameObject.name}' tag='{c.gameObject.tag}' active={c.gameObject.activeInHierarchy} enabled={c.enabled} depth={c.depth}");
        }
        sb.AppendLine();
        // Count how many are tagged MainCamera and active -> Camera.main resolution
        var mains = cams.Where(c => c.gameObject.CompareTag("MainCamera")).ToList();
        sb.AppendLine($"Tagged 'MainCamera': {mains.Count}");
        foreach (var m in mains)
            sb.AppendLine($"   -> '{m.gameObject.name}' active={m.gameObject.activeInHierarchy} enabled={m.enabled}");
        sb.AppendLine();
        sb.AppendLine($"Camera.main right now = {(Camera.main != null ? Camera.main.gameObject.name : "NULL")}");
        return sb.ToString();
    }
}
