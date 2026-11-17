using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MuseDesk.Spatial;

/// <summary>
/// Worn pass 1 (rc6-1 on the owner's head), scene part: the placement numbers the owner asked to be
/// less strict, written into SpatialWorkspaceDirector in MainMRScene. The scene serialises
/// ceilingSettings and floorSettings, so a changed code default does not reach the build by itself.
///
/// Changed existing values (old -> new, both the code default now):
///   ceilingSettings.inspectDropBelowEye      0.35 -> 0.25 (the normal lowering stops 10 cm higher)
///   ceilingSettings.inspectWideSideOptions   0.85, 0.9, 0.8 -> 0.9, 0.85, 1.0, 0.8 (further out first)
///   ceilingSettings.forwardOptions           0.3, 0.6, 0 -> 0.3, 0.6, 0, -0.3 (a dock a little behind the shoulder line)
/// New values (written explicitly so the scene does not depend on how missing fields load):
///   ceilingSettings.shallowBaseAboveKeepOut  0.05, 0.10   ceilingSettings.shallowMaxAboveEye 0.25
///   ceilingSettings.minDescent               0.15         ceilingSettings.compactObstacleLabels SCREEN
///   ceilingSettings.lowObstacleMaxHeight     0.15         ceilingSettings.lowObstacleMargin 0.03
///   floorSettings.lowObstacleMaxHeight       0.15         floorSettings.lowObstaclePad 0.06
/// Every other number (user keep-out, sweep margins, furniture and wall margins) is left as it is and
/// reported. Nothing is created, moved or rewired; no feature flag is touched. Idempotent; saves once.
///   scripts\run_guarded.ps1 -File AgentScripts\BuildWornPassLooseningV1.cs -Entry BuildWornPassLooseningV1.Apply
/// </summary>
public static class BuildWornPassLooseningV1
{
    static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

    static string L(float[] a)
    {
        if (a == null) return "null";
        var sb = new StringBuilder();
        for (int i = 0; i < a.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(F(a[i])); }
        return "{" + sb + "}";
    }

    static string L(string[] a) { return a == null ? "null" : "{" + string.Join(", ", a) + "}"; }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildWornPassLooseningV1: stop Play Mode first.";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildWornPassLooseningV1: no open scene.";
        var director = Object.FindAnyObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);
        if (director == null) return "BuildWornPassLooseningV1: no SpatialWorkspaceDirector in " + scene.name + "; nothing changed.";
        if (director.ceilingSettings == null || director.floorSettings == null) return "BuildWornPassLooseningV1: director settings are null; nothing changed.";

        var c = director.ceilingSettings;
        var f = director.floorSettings;
        var dc = new CeilingPlacementSettings();
        var df = new FloorPlacementSettings();
        var log = new StringBuilder("BuildWornPassLooseningV1 on " + scene.name + ":\n");

        Undo.RecordObject(director, "Worn pass 1 placement numbers");
        log.Append("  inspectDropBelowEye ").Append(F(c.inspectDropBelowEye)).Append(" -> ").Append(F(dc.inspectDropBelowEye)).Append('\n');
        c.inspectDropBelowEye = dc.inspectDropBelowEye;
        log.Append("  inspectWideSideOptions ").Append(L(c.inspectWideSideOptions)).Append(" -> ").Append(L(dc.inspectWideSideOptions)).Append('\n');
        c.inspectWideSideOptions = (float[])dc.inspectWideSideOptions.Clone();
        log.Append("  forwardOptions ").Append(L(c.forwardOptions)).Append(" -> ").Append(L(dc.forwardOptions)).Append('\n');
        c.forwardOptions = (float[])dc.forwardOptions.Clone();

        log.Append("  shallowBaseAboveKeepOut ").Append(L(c.shallowBaseAboveKeepOut)).Append(" -> ").Append(L(dc.shallowBaseAboveKeepOut)).Append('\n');
        c.shallowBaseAboveKeepOut = (float[])dc.shallowBaseAboveKeepOut.Clone();
        log.Append("  shallowMaxAboveEye ").Append(F(c.shallowMaxAboveEye)).Append(" -> ").Append(F(dc.shallowMaxAboveEye)).Append('\n');
        c.shallowMaxAboveEye = dc.shallowMaxAboveEye;
        log.Append("  minDescent ").Append(F(c.minDescent)).Append(" -> ").Append(F(dc.minDescent)).Append('\n');
        c.minDescent = dc.minDescent;
        log.Append("  compactObstacleLabels ").Append(L(c.compactObstacleLabels)).Append(" -> ").Append(L(dc.compactObstacleLabels)).Append('\n');
        c.compactObstacleLabels = (string[])dc.compactObstacleLabels.Clone();
        log.Append("  ceiling lowObstacleMaxHeight ").Append(F(c.lowObstacleMaxHeight)).Append(" -> ").Append(F(dc.lowObstacleMaxHeight))
           .Append(", lowObstacleMargin ").Append(F(c.lowObstacleMargin)).Append(" -> ").Append(F(dc.lowObstacleMargin)).Append('\n');
        c.lowObstacleMaxHeight = dc.lowObstacleMaxHeight;
        c.lowObstacleMargin = dc.lowObstacleMargin;
        log.Append("  floor lowObstacleMaxHeight ").Append(F(f.lowObstacleMaxHeight)).Append(" -> ").Append(F(df.lowObstacleMaxHeight))
           .Append(", lowObstaclePad ").Append(F(f.lowObstaclePad)).Append(" -> ").Append(F(df.lowObstaclePad)).Append('\n');
        f.lowObstacleMaxHeight = df.lowObstacleMaxHeight;
        f.lowObstaclePad = df.lowObstaclePad;

        // Unchanged on purpose; reported so a reviewer can see them next to the loosened ones.
        log.Append("  unchanged: userKeepOutRadius ").Append(F(c.userKeepOutRadius))
           .Append(", userKeepOutAboveHead ").Append(F(c.userKeepOutAboveHead))
           .Append(", userOverheadRadius ").Append(F(c.userOverheadRadius))
           .Append(", sweepMarginHorizontal ").Append(F(c.sweepMarginHorizontal))
           .Append(", sweepMarginVertical ").Append(F(c.sweepMarginVertical))
           .Append(", descentClearance ").Append(F(c.descentClearance))
           .Append(", ceiling wallMargin ").Append(F(c.wallMargin))
           .Append(", placedFurnitureClearance ").Append(F(f.placedFurnitureClearance))
           .Append(", placedUserClearance ").Append(F(f.placedUserClearance))
           .Append(", placedWallMargin ").Append(F(f.placedWallMargin)).Append('\n');

        EditorUtility.SetDirty(director);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        log.Append("  scene saved=").Append(saved);
        return log.ToString();
    }
}
