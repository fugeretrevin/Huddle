using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MuseDesk.Diagnostics;

/// <summary>
/// Adds the PerfScenarioMarker (scenario + heartbeat logging for on-device performance capture) to the
/// always-on "AppServices" object, and turns on Player Settings "Frame Timing Stats" (cheap) so the
/// heartbeat can report CPU and GPU frame time. Idempotent; saves the scene.
///   powershell scripts\run_guarded.ps1 -File AgentScripts\BuildPerfMarkersV1.cs -Entry BuildPerfMarkersV1.Apply
/// Revert() disables the component (not destroyed) and leaves Frame Timing Stats as it is.
/// </summary>
public static class BuildPerfMarkersV1
{
    static GameObject FindHost()
    {
        var scene = EditorSceneManager.GetActiveScene();
        return scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).FirstOrDefault(g => g.name == "AppServices");
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildPerfMarkersV1: stop Play Mode first.";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildPerfMarkersV1: no open scene.";
        var host = FindHost();
        if (host == null) return "BuildPerfMarkersV1: object \"AppServices\" not found; nothing changed.";

        var m = host.GetComponent<PerfScenarioMarker>();
        bool added = false;
        if (m == null) { m = Undo.AddComponent<PerfScenarioMarker>(host); added = true; }
        m.enabled = true;
        EditorUtility.SetDirty(m);

        bool statsBefore = PlayerSettings.enableFrameTimingStats;
        if (!statsBefore) PlayerSettings.enableFrameTimingStats = true;

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return "BuildPerfMarkersV1: PerfScenarioMarker " + (added ? "added to" : "already on") + " " + host.name
               + "; enableFrameTimingStats was " + statsBefore + (statsBefore ? " (unchanged)" : ", now true (CHANGED)")
               + "; scene saved=" + saved;
    }

    public static string Revert()
    {
        var host = FindHost();
        var m = host != null ? host.GetComponent<PerfScenarioMarker>() : null;
        if (m == null) return "BuildPerfMarkersV1.Revert: nothing to revert.";
        m.enabled = false;
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        return "BuildPerfMarkersV1.Revert: marker disabled; scene saved=" + EditorSceneManager.SaveScene(scene);
    }
}
