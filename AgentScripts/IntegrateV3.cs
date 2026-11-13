using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>Coordinator fix-ups after the refinement builders (Paper, Receipt, Board, Life, Kit).
/// Idempotent; saves the scene.</summary>
public static class IntegrateV3
{
    static Transform Find(string n) => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == n);

    public static string Apply()
    {
        var sb = new StringBuilder();
        // Approve / Reject: white bold label on the coloured cap (as the owner saw and liked), not ink.
        var white = new Color32(0xF3, 0xF4, 0xF6, 255);
        foreach (var k in new[] { "Btn_Approve", "Btn_Reject" })
        {
            var t = Find(k); if (t == null) { sb.AppendLine("missing " + k); continue; }
            foreach (var l in t.GetComponentsInChildren<TMP_Text>(true))
            {
                l.color = white; l.fontStyle = FontStyles.Bold; EditorUtility.SetDirty(l);
                var ksv = l.GetComponentInParent<MuseDesk.Workspace.KeyStateVisual>(true);
                sb.AppendLine($"{k}/{l.name} -> white bold");
            }
        }
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return sb.ToString();
    }
}
