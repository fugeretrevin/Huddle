using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

/// <summary>Builds the Android APK to a NAMED path (never Builds/MuseDesk.apk). Entry: BuildApkTo.Run with the output path.</summary>
public static class BuildApkTo
{
    public static string Run(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Replace('\\', '/').EndsWith("Builds/MuseDesk.apk") || path.Contains("rc6")) return "REFUSED: protected path";
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) return "FAILED: no enabled build scenes";
        var opts = new BuildPlayerOptions { scenes = scenes, locationPathName = path, target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android, options = BuildOptions.None };
        var s = BuildPipeline.BuildPlayer(opts).summary;
        return "RESULT=" + s.result + " apk=" + path + " sizeMB=" + (s.totalSize / (1024f * 1024f)).ToString("F1") + " errors=" + s.totalErrors + " timeSec=" + (s.buildEndedAt - s.buildStartedAt).TotalSeconds.ToString("F0");
    }
}
