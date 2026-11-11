using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FrameAgent
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var head = all.FirstOrDefault(t => t.name == "Head" && t.GetComponentInParent<Transform>().root.name.Contains("WorkspaceRoot"));
        var dev = all.FirstOrDefault(t => t.name == "Agent_Developer");
        var pivot = dev != null ? dev.position + Vector3.up * 0.12f : new Vector3(0, 1.1f, 0.5f);
        var sv = SceneView.lastActiveSceneView;
        if (sv != null) { sv.LookAt(pivot, Quaternion.Euler(8f, 180f, 0f), 0.28f); sv.Repaint(); }
        return $"framed on {pivot}";
    }
}
