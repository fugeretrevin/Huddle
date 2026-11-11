using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the Quest APK from inside the already-open Editor (no second batch-mode editor, which the
/// project lock would refuse). Writes to Builds/MuseDesk.apk. Returns a one-line result the caller
/// can grep for SUCCEEDED/FAILED. Install is done separately via adb (keeps the build headless).
/// </summary>
public static class BuildApk
{
    public static string Info()
    {
        var sc = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        return $"activeTarget={EditorUserBuildSettings.activeBuildTarget} " +
               $"scenes=[{string.Join(" ; ", sc)}] " +
               $"openScene={EditorSceneManager.GetActiveScene().path}";
    }

    public static string Run()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            var open = EditorSceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(open)) return "FAILED: no enabled build scenes and no saved open scene";
            scenes = new[] { open };
        }

        System.IO.Directory.CreateDirectory("Builds");
        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "Builds/MuseDesk.apk",
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        return $"RESULT={s.result} apk=Builds/MuseDesk.apk sizeMB={s.totalSize / (1024f * 1024f):F1} " +
               $"errors={s.totalErrors} warnings={s.totalWarnings} timeSec={(s.buildEndedAt - s.buildStartedAt).TotalSeconds:F0}";
    }
}
