using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>Read-only: measures the REAL world size of every TMP text under the board, artifact
/// panel, poke buttons and decision paper (inactive included), so text scaling is fixed from numbers.</summary>
public static class TextAudit
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder();
        var roots = scene.GetRootGameObjects();
        var all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();

        foreach (var rootName in new[] { "TaskBoard", "ArtifactPanel", "FocusToggle", "ExplainToggle", "DecisionDocument" })
        {
            var root = all.FirstOrDefault(t => t.name == rootName);
            if (root == null) { sb.AppendLine($"## {rootName}: not found"); continue; }
            sb.AppendLine($"## {rootName} lossyScale={V(root.lossyScale)} worldPos={V(root.position)}");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "Backing" || r.name == "Face" || r.name == "ButtonVisual").Take(6))
                sb.AppendLine($"   [quad] {Path(r.transform, root)} worldSize={V(Vector3.Scale(r.transform.lossyScale, Vector3.one))}");
            foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
            {
                tmp.ForceMeshUpdate(true, true);
                var ls = tmp.transform.lossyScale;
                var tb = tmp.textBounds.size;
                var rect = tmp.rectTransform.rect.size;
                sb.AppendLine($"   {Path(tmp.transform, root)} | \"{Trunc(tmp.text)}\" font={tmp.fontSize:F3} scale={ls.x:F4} " +
                              $"=> textWorld=({tb.x * ls.x:F3} x {tb.y * ls.y:F3}) rectWorld=({rect.x * ls.x:F3} x {rect.y * ls.y:F3}) " +
                              $"wrap={tmp.textWrappingMode} overflow={tmp.overflowMode} active={tmp.gameObject.activeInHierarchy}");
            }
        }
        return sb.ToString();
    }

    static string Path(Transform t, Transform root)
    {
        string p = t.name;
        while (t.parent != null && t != root) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
    static string Trunc(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length <= 18 ? s : s.Substring(0, 18) + "..").Replace("\n", " ");
    static string V(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";
}
