using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>Read-only: every TextMeshPro under the task board, decision paper, artifact panel and
/// inbox, with its box size, font size, wrapping and overflow mode, and world height.</summary>
public static class TextProbe
{
    public static string Run()
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var sb = new StringBuilder();
        foreach (var rootName in new[] { "TaskBoard", "DecisionDocument", "ArtifactPanel", "InboxTray", "Monitor", "DiagnosticsText", "NameTag" })
        {
            foreach (var root in all.Where(t => t.name == rootName).Take(1))
            {
                sb.AppendLine("== " + rootName);
                foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    var rt = tmp.rectTransform;
                    string path = tmp.transform.parent != null && tmp.transform.parent != root ? tmp.transform.parent.name + "/" + tmp.name : tmp.name;
                    string txt = (tmp.text ?? "").Replace("\n", "|");
                    if (txt.Length > 28) txt = txt.Substring(0, 28) + "~";
                    sb.AppendLine($"  {path}: box=({rt.sizeDelta.x:F3},{rt.sizeDelta.y:F3}) font={tmp.fontSize:F1} auto={tmp.enableAutoSizing}[{tmp.fontSizeMin:F1}-{tmp.fontSizeMax:F1}] wrap={tmp.textWrappingMode} overflow={tmp.overflowMode} align={tmp.alignment} lossy={tmp.transform.lossyScale.x:F3} worldBox=({rt.sizeDelta.x * tmp.transform.lossyScale.x * 100:F1}cm x {rt.sizeDelta.y * tmp.transform.lossyScale.y * 100:F1}cm) glyphH~{tmp.fontSize * 0.1f * tmp.transform.lossyScale.y * 100:F2}cm \"{txt}\"");
                }
            }
        }
        return sb.ToString();
    }
}
