using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Reload
{
    // OpenScene(Single) silently discards unsaved in-memory changes (no GUI prompt),
    // reloading the clean on-disk MainMRScene.
    public static string FromDisk()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity",
            OpenSceneMode.Single);
        var sb = new StringBuilder();
        sb.AppendLine($"reloaded {scene.name} dirty={scene.isDirty}");
        foreach (var r in scene.GetRootGameObjects()) sb.AppendLine("ROOT: " + r.name);
        var cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Length;
        sb.AppendLine($"cameras={cams}");
        return sb.ToString();
    }
}
