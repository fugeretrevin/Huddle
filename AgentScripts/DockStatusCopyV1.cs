using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Spatial;

/// <summary>
/// The dock's status line at the desk read "Spaces" (UnifiedModeDockV1), which looks like a title, not a status.
/// It now says where the office is, like the other modes: "Office at your desk" (Floor: "Office on the floor", ...).
/// Idempotent; Revert() puts "Spaces" back. Saves the scene.
///
///   powershell scripts\run_guarded.ps1 -File AgentScripts\DockStatusCopyV1.cs -Entry DockStatusCopyV1.Apply
/// </summary>
public static class DockStatusCopyV1
{
    const string Old = "Spaces", New = "Office at your desk";

    public static string Apply() { return Set(New); }
    public static string Revert() { return Set(Old); }

    static string Set(string words)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "DockStatusCopyV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "DockStatusCopyV1: no open scene.\n";
        var sb = new StringBuilder("== DockStatusCopyV1 ==\n");
        int n = 0;
        foreach (var c in Object.FindObjectsByType<ModeConsole>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c == null || c.gameObject.scene != scene) continue;
            Undo.RecordObject(c, "Dock desk status words");
            string was = c.deskWords;
            c.deskWords = words;
            EditorUtility.SetDirty(c);
            if (c.statusLabel != null && (c.statusLabel.text == Old || c.statusLabel.text == New))
            {
                Undo.RecordObject(c.statusLabel, "Dock status label");
                c.statusLabel.text = words;
                EditorUtility.SetDirty(c.statusLabel);
            }
            sb.AppendLine("  " + c.name + ": deskWords '" + was + "' -> '" + words + "'; label='" + (c.statusLabel != null ? c.statusLabel.text : "none") + "'");
            n++;
        }
        if (n == 0) return sb.AppendLine("  no ModeConsole in the scene.").ToString();
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("  scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }
}
