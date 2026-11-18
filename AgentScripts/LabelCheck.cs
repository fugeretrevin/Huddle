using UnityEditor;
using UnityEngine;

/// <summary>The build label the Editor has loaded (after an optional import of the release config).</summary>
public static class LabelCheck
{
    public static string Run(bool reimport)
    {
        const string path = "Assets/MuseDesk/Resources/ReleaseConfig.asset";
        if (reimport) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var cfg = AssetDatabase.LoadAssetAtPath<MuseDesk.Config.MuseDeskReleaseConfig>(path);
        return cfg != null ? "label=" + cfg.buildLabel + " reducedMotionToggle=" + cfg.reducedMotionToggle : "no config";
    }
}
