using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Wall;

/// <summary>
/// Owner rc9: "the user should get to choose which mode to do and have the onboarding placing process". The rc9 build
/// re-entered the saved Wall placement at launch, so the user never saw the mode choice or the placement flow. Every
/// launch now starts at the desk: SpatialWorkspaceDirector.restoreOnStart and WallHabitatController.restoreOnStart = false.
/// Saved spots are still offered first when the user picks that mode. Revert sets both back to true.
///   scripts\run_guarded.ps1 -File AgentScripts\DeskFirstStartV1.cs -Entry DeskFirstStartV1.Apply
/// </summary>
public static class DeskFirstStartV1
{
    public static string Apply() { return Set(false); }
    public static string Revert() { return Set(true); }

    static string Set(bool restore)
    {
        var sb = new StringBuilder("DeskFirstStartV1 restoreOnStart=" + restore + "\n");
        var d = Object.FindFirstObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);
        var w = Object.FindFirstObjectByType<WallHabitatController>(FindObjectsInactive.Include);
        if (d == null || w == null) return sb.Append("  missing director or wall controller").ToString();
        Undo.RecordObject(d, "Desk-first start"); Undo.RecordObject(w, "Desk-first start");
        sb.AppendLine("  director " + d.restoreOnStart + " -> " + restore + ", wall " + w.restoreOnStart + " -> " + restore);
        d.restoreOnStart = restore; w.restoreOnStart = restore;
        EditorUtility.SetDirty(d); EditorUtility.SetDirty(w);
        EditorSceneManager.MarkSceneDirty(d.gameObject.scene);
        sb.AppendLine("  scene saved=" + EditorSceneManager.SaveScene(d.gameObject.scene));
        return sb.ToString();
    }
}
