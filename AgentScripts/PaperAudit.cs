using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>Read-only: dumps the DecisionDocument subtree (pose, active, colors, text, components).</summary>
public static class PaperAudit
{
    public static string Run() => Dump("DecisionDocument", 3);
    public static string Card() => Dump("CardTemplate", 2);

    static string Dump(string name, int maxDepth)
    {
        var root = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == name);
        if (root == null) return name + " not found";
        var sb = new StringBuilder();
        sb.AppendLine($"{name}: activeSelf={root.gameObject.activeSelf} pos={V(root.position)} euler={V(root.eulerAngles)} scale={V(root.localScale)}");
        sb.AppendLine("  components: " + string.Join(", ", root.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name)));
        Walk(root, 1, maxDepth, sb);
        return sb.ToString();
    }

    static void Walk(Transform t, int depth, int maxDepth, StringBuilder sb)
    {
        foreach (Transform c in t)
        {
            string ind = new string(' ', depth * 2);
            string extra = "";
            var tmp = c.GetComponent<TMP_Text>();
            if (tmp != null)
                extra += $" TMP[\"{Trunc(tmp.text)}\" font={tmp.fontSize} color={C(tmp.color)} align={tmp.alignment} rect={tmp.rectTransform.sizeDelta}]";
            var r = c.GetComponent<Renderer>();
            if (r != null && tmp == null && r.sharedMaterial != null)
            {
                var m = r.sharedMaterial;
                Color col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : (m.HasProperty("_Color") ? m.color : Color.magenta);
                extra += $" MAT[{m.shader.name.Split('/').Last()} {C(col)}]";
            }
            var others = c.GetComponents<Component>().Where(x => x != null && !(x is Transform) && !(x is TMP_Text) && !(x is Renderer) && !(x is MeshFilter) && !(x is CanvasRenderer))
                .Select(x => x.GetType().Name).ToArray();
            if (others.Length > 0) extra += " {" + string.Join(",", others) + "}";
            sb.AppendLine($"{ind}{c.name} [{(c.gameObject.activeSelf ? "on" : "OFF")}] lp={V(c.localPosition)} le={V(c.localEulerAngles)} ls={V(c.localScale)}{extra}");
            if (depth < maxDepth && !c.name.StartsWith("[BuildingBlock]")) Walk(c, depth + 1, maxDepth, sb);
        }
    }

    static string Trunc(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length <= 22 ? s : s.Substring(0, 22) + "..").Replace("\n", " ");
    static string V(Vector3 v) => $"({v.x:0.###},{v.y:0.###},{v.z:0.###})";
    static string C(Color c) => $"rgba({c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##})";
}
