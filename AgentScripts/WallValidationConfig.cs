using UnityEditor;
using MuseDesk.Config;

/// <summary>
/// Switch the six Wall Habitat flags (and, since Polish S9c, the reducedMotionToggle key flag) in Resources/ReleaseConfig
/// for a VALIDATION build, and back.
/// The competition build must be made with Off(). Status() prints the current summary.
/// </summary>
public static class WallValidationConfig
{
    public static string On() { return Set(true); }
    public static string Off() { return Set(false); }
    public static string Status() { var c = MuseDeskReleaseConfig.Load(); return c != null ? c.Summary() : "ReleaseConfig missing"; }

    static string Set(bool on)
    {
        var c = MuseDeskReleaseConfig.Load();
        if (c == null) return "ReleaseConfig missing";
        c.wallHabitat = on;
        c.wallManager = on;
        c.agentSelect = on;
        c.agentBtw = on;
        c.largeAgentPortal = on;
        c.wallElevatorVfx = false;            // not implemented
        c.reducedMotionToggle = on;           // Polish S9c: the in-app Calm motion key is part of the validation build only
        if (on && !c.buildLabel.Contains("+wall")) c.buildLabel += "+wall";
        if (!on) c.buildLabel = c.buildLabel.Replace("+wall", "");
        EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssets();
        return c.Summary();
    }
}
