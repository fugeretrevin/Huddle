using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>Make sure the production scene is the one open (a timed-out test run can leave an untitled scene).</summary>
public static class SceneCheck
{
    const string Main = "Assets/MuseDesk/Scenes/MainMRScene.unity";
    public static string Ensure()
    {
        var s = SceneManager.GetActiveScene();
        if (s.path == Main) return "ok: " + s.path;
        if (UnityEngine.Application.isPlaying) return "playing in " + s.path + " (not switching)";
        EditorSceneManager.OpenScene(Main);
        return "reopened " + Main + " (was '" + s.path + "')";
    }
}
