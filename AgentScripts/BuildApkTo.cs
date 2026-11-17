using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Builds the Android APK to a NAMED path (never Builds/MuseDesk.apk). Entry: BuildApkTo.Run with the output path.
/// RunSigned(path, versionCode): the same build signed with the owner's RELEASE keystore (needed for a Meta release channel;
/// Run() signs with the Android debug key). Signing details come from %USERPROFILE%\.musedesk\signing.json (written by
/// scripts\make_release_keystore.ps1, outside the repo), are applied in memory for this build only and restored afterwards,
/// so no keystore path or password is ever saved into ProjectSettings.</summary>
public static class BuildApkTo
{
    static string Refuse(string path)
    {
        string p = (path ?? "").Replace('\\', '/');
        if (string.IsNullOrEmpty(path) || p.EndsWith("Builds/MuseDesk.apk")) return "REFUSED: protected path";
        foreach (var rc in new[] { "rc6", "rc7", "rc8", "rc9", "rc10", "rc11", "rc12", "rc13", "rc14", "rc15", "rc16", "rc17", "rc18", "rc19", "rc20", "rc21" })
            if (p.Contains(rc + "_") || p.Contains(rc + ".")) return "REFUSED: protected path (" + rc + ")";
        if (File.Exists(path)) return "REFUSED: " + path + " exists (never overwrite a build)";
        return null;
    }

    public static string Run(string path)
    {
        string refuse = Refuse(path);
        if (refuse != null) return refuse;
        return Build(path);
    }

    static string Build(string path)
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) return "FAILED: no enabled build scenes";
        var opts = new BuildPlayerOptions { scenes = scenes, locationPathName = path, target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android, options = BuildOptions.None };
        var s = BuildPipeline.BuildPlayer(opts).summary;
        return "RESULT=" + s.result + " apk=" + path + " sizeMB=" + (s.totalSize / (1024f * 1024f)).ToString("F1") + " errors=" + s.totalErrors + " timeSec=" + (s.buildEndedAt - s.buildStartedAt).TotalSeconds.ToString("F0");
    }

    [Serializable] class Signing { public string keystorePath; public string keystorePass; public string keyAlias; public string keyPass; }

    public static string RunSigned(string path, int versionCode)
    {
        string refuse = Refuse(path);
        if (refuse != null) return refuse;
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".musedesk", "signing.json");
        if (!File.Exists(file)) return "FAILED: no " + file + " (run scripts\\make_release_keystore.ps1 first)";
        Signing sig;
        try { sig = JsonUtility.FromJson<Signing>(File.ReadAllText(file)); }
        catch { return "FAILED: signing.json is not readable"; }
        if (sig == null || string.IsNullOrEmpty(sig.keystorePath) || !File.Exists(sig.keystorePath)) return "FAILED: the keystore named in signing.json does not exist";
        if (versionCode < 2) return "FAILED: give a version code above every upload so far (2 or more)";

        // Remember the project's values; restore them whatever happens.
        bool useCustom = PlayerSettings.Android.useCustomKeystore;
        string ksName = PlayerSettings.Android.keystoreName, alias = PlayerSettings.Android.keyaliasName;
        int code = PlayerSettings.Android.bundleVersionCode;
        try
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = sig.keystorePath;
            PlayerSettings.Android.keystorePass = sig.keystorePass;
            PlayerSettings.Android.keyaliasName = sig.keyAlias;
            PlayerSettings.Android.keyaliasPass = sig.keyPass;
            PlayerSettings.Android.bundleVersionCode = versionCode;
            return Build(path) + " signed=release versionCode=" + versionCode;
        }
        finally
        {
            PlayerSettings.Android.useCustomKeystore = useCustom;
            PlayerSettings.Android.keystoreName = ksName;
            PlayerSettings.Android.keystorePass = "";
            PlayerSettings.Android.keyaliasName = alias;
            PlayerSettings.Android.keyaliasPass = "";
            PlayerSettings.Android.bundleVersionCode = code;
        }
    }
}
