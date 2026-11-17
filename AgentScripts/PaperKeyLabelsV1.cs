using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// rc12: the desk decision paper's More and Revise keys had near-black labels (#1F2530) on their indigo caps: hard to read.
/// They now use the same near-white as Approve / Reject (#F3F4F6). Idempotent; Revert() puts #1F2530 back. Saves the scene.
///   powershell scripts\run_guarded.ps1 -File AgentScripts\PaperKeyLabelsV1.cs -Entry PaperKeyLabelsV1.Apply
/// </summary>
public static class PaperKeyLabelsV1
{
    static readonly string[] Keys = { "Btn_Details", "Btn_Changes" };
    static readonly Color Light = new Color(0xF3 / 255f, 0xF4 / 255f, 0xF6 / 255f, 1f), Dark = new Color(0x1F / 255f, 0x25 / 255f, 0x30 / 255f, 1f);

    public static string Apply() { return Set(Light); }
    public static string Revert() { return Set(Dark); }

    static string Set(Color c)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "PaperKeyLabelsV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder("== PaperKeyLabelsV1 ==\n");
        int n = 0;
        foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (t == null || t.name != "Label" || !t.gameObject.scene.IsValid() || t.gameObject.scene != scene) continue;
            Transform k = t.transform;
            bool match = false;
            for (int i = 0; i < 6 && k != null; i++, k = k.parent) if (System.Array.IndexOf(Keys, k.name) >= 0) { match = true; break; }
            if (!match) continue;
            Undo.RecordObject(t, "Paper key label colour");
            t.color = c;
            EditorUtility.SetDirty(t);
            sb.AppendLine("  " + k.name + " '" + t.text + "' -> #" + ColorUtility.ToHtmlStringRGB(c));
            n++;
        }
        if (n == 0) return sb.AppendLine("  no More / Revise labels found").ToString();
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("  scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }
}
