using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>The Editor is shared with another tool that opens its own preview scenes. Call Ensure
/// before any production builder: it opens MainMRScene only if the scene currently open has no
/// unsaved changes, so nobody's work is discarded.</summary>
public static class SceneGuard
{
    const string Main = "Assets/MuseDesk/Scenes/MainMRScene.unity";

    public static string Ensure()
    {
        var s = EditorSceneManager.GetActiveScene();
        if (s.path == Main) return "OK main scene open" + (s.isDirty ? " (dirty)" : "");
        if (EditorApplication.isPlaying) return "BLOCKED play mode is running in " + s.path;
        if (s.isDirty) return "BLOCKED other scene has unsaved changes: " + s.path;
        string was = s.path;
        EditorSceneManager.OpenScene(Main, OpenSceneMode.Single);
        return "OK opened main scene (was " + was + ")";
    }

    public static string Which()
    {
        var s = EditorSceneManager.GetActiveScene();
        return s.path + " dirty=" + s.isDirty + " time=" + System.DateTime.Now.ToString("HH:mm:ss");
    }
}
