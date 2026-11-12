using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FrameBoard
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var board = all.FirstOrDefault(t => t.name == "TaskBoard");
        // Activate the card template so we can see a sample card look.
        var tmpl = all.FirstOrDefault(t => t.name == "CardTemplate");
        if (tmpl != null) tmpl.gameObject.SetActive(true);
        var pivot = board != null ? board.position : new Vector3(-0.1f, 1.05f, 0.42f);
        var sv = SceneView.lastActiveSceneView;
        if (sv != null) { sv.LookAt(pivot, Quaternion.Euler(10f, 180f, 0f), 0.55f); sv.Repaint(); }
        return $"framed on board {pivot}, template active={tmpl != null}";
    }
    public static string HideTemplate()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var tmpl = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "CardTemplate");
        if (tmpl != null) tmpl.gameObject.SetActive(false);
        EditorSceneManager.SaveScene(scene);
        return "template hidden + saved";
    }
}
