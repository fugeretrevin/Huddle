using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FrameWS
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var tray = all.FirstOrDefault(t => t.name == "PhysicsTray");
        var pivot = tray != null ? tray.position : new Vector3(0, 1.05f, 0.45f);
        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            sv.LookAt(pivot, Quaternion.Euler(25f, 180f, 0f), 0.8f);
            sv.Repaint();
        }
        return $"framed on {pivot}";
    }
}
