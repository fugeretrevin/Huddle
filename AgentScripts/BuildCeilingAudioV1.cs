using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MuseDesk.Spatial;

/// <summary>
/// Adds the ceiling pod's three-stage sound: ONE CeilingPodAudio next to the CeilingPodController
/// (on the same object as CeilingPodArt when there is one), wired to the controller. Runs after
/// BuildCeilingVfxV1. The mount, mast and office root are NOT stored: the component reads them from
/// the controller at play time, so a swapped CeilingMount needs nothing here.
/// Safe to run twice (re-wires the same component). Nothing is created besides the component.
/// Revert() removes it. Refuses in Play Mode and outside MainMRScene.
///   BuildCeilingAudioV1.Apply
/// </summary>
public static class BuildCeilingAudioV1
{
    static bool OpenScene(out string error)
    {
        error = null;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { error = "stop Play Mode first."; return false; }
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) { error = "no open scene."; return false; }
        if (!scene.path.Replace('\\', '/').EndsWith("Assets/MuseDesk/Scenes/MainMRScene.unity"))
        { error = "the open scene is '" + scene.path + "', not MainMRScene; nothing changed."; return false; }
        return true;
    }

    public static string Apply()
    {
        if (!OpenScene(out string err)) return "BuildCeilingAudioV1: " + err + "\n";
        var pod = Object.FindFirstObjectByType<CeilingPodController>(FindObjectsInactive.Include);
        if (pod == null) return "BuildCeilingAudioV1: no CeilingPodController in the scene; nothing changed.\n";

        var art = Object.FindFirstObjectByType<CeilingPodArt>(FindObjectsInactive.Include);
        GameObject host = art != null ? art.gameObject : pod.gameObject;

        var audio = host.GetComponent<CeilingPodAudio>();
        bool added = audio == null;
        if (added) audio = Undo.AddComponent<CeilingPodAudio>(host);
        audio.pod = pod;
        EditorUtility.SetDirty(audio);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        var sb = new StringBuilder("== BuildCeilingAudioV1 ==\n");
        sb.AppendLine($"  CeilingPodAudio {(added ? "added to" : "already on")} {host.name}; pod wired to {pod.name}");
        sb.AppendLine($"  ceilingMount: {(pod.ceilingMount != null ? pod.ceilingMount.name : "MISSING (falls back to mastBottom, then the office root)")}; mastBottom: {(pod.mastBottom != null ? pod.mastBottom.name : "missing")}; workspaceRoot: {(pod.transition != null && pod.transition.workspaceRoot != null ? pod.transition.workspaceRoot.name : "MISSING (falls back to SpatialWorkspaceState.WorkspaceRoot)")}");
        sb.AppendLine($"  spatializer plugin in project audio settings: '{AudioSettings.GetSpatializerPluginName()}' (used when not empty)");
        sb.AppendLine("  scene saved");
        return sb.ToString();
    }

    public static string Revert()
    {
        if (!OpenScene(out string err)) return "BuildCeilingAudioV1.Revert: " + err + "\n";
        var all = Object.FindObjectsByType<CeilingPodAudio>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var a in all) Undo.DestroyObjectImmediate(a);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        return $"BuildCeilingAudioV1.Revert: removed {all.Length} CeilingPodAudio component(s).\n";
    }
}
