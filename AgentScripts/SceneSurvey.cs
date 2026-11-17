using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SceneSurvey
{
    public static string Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();
        foreach (var root in scene.GetRootGameObjects())
            Walk(root.transform, 0, sb);
        return sb.ToString();
    }
    static void Walk(Transform t, int depth, StringBuilder sb)
    {
        if (depth > 3) return;
        sb.AppendLine(new string(' ', depth*2) + t.name);
        foreach (Transform c in t) Walk(c, depth+1, sb);
    }
}
