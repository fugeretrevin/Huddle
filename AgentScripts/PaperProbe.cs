using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>Read-only: the decision paper's hierarchy with local poses, sizes, text settings and
/// material colours, and the serialized wiring of its controllers.</summary>
public static class PaperProbe
{
    static string V(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

    static void Tree(Transform t, StringBuilder sb, int depth)
    {
        var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToList();
        string extra = "";
        var tmp = t.GetComponent<TMP_Text>();
        if (tmp != null)
        {
            string txt = (tmp.text ?? "").Replace("\n", "|"); if (txt.Length > 24) txt = txt.Substring(0, 24) + "~";
            extra += $" TEXT='{txt}' font={tmp.fontSize:F1} box={tmp.rectTransform.sizeDelta} col=#{ColorUtility.ToHtmlStringRGB(tmp.color)} style={tmp.fontStyle} align={tmp.alignment}";
        }
        var r = t.GetComponent<MeshRenderer>();
        if (r != null && tmp == null && r.sharedMaterial != null)
        {
            var m = r.sharedMaterial;
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : (m.HasProperty("_Color") ? m.GetColor("_Color") : Color.magenta);
            extra += $" MAT={m.name}/{m.shader.name.Split('/').Last()} col=#{ColorUtility.ToHtmlStringRGB(c)}";
        }
        var bc = t.GetComponent<BoxCollider>();
        if (bc != null) extra += $" BOX size={V(bc.size)} center={V(bc.center)} trigger={bc.isTrigger}";
        sb.AppendLine($"{new string(' ', depth * 2)}{t.name} [{(t.gameObject.activeSelf ? "on" : "OFF")}] lp={V(t.localPosition)} le={V(t.localEulerAngles)} ls={V(t.localScale)} {{{string.Join(",", comps)}}}{extra}");
        if (comps.Contains("PokeInteractable")) return;     // button internals are uniform; skip
        foreach (Transform c in t) Tree(c, sb, depth + 1);
    }

    static void Dump(Component c, StringBuilder sb)
    {
        var so = new SerializedObject(c);
        var it = so.GetIterator(); bool enter = true;
        sb.Append($"## {c.GetType().Name}: ");
        while (it.NextVisible(enter))
        {
            enter = false;
            if (it.name == "m_Script") continue;
            string v = it.propertyType switch
            {
                SerializedPropertyType.Boolean => it.boolValue.ToString(),
                SerializedPropertyType.Float => it.floatValue.ToString("F3"),
                SerializedPropertyType.Integer => it.intValue.ToString(),
                SerializedPropertyType.String => "'" + it.stringValue + "'",
                SerializedPropertyType.ObjectReference => it.objectReferenceValue != null ? it.objectReferenceValue.name : "null",
                SerializedPropertyType.Vector3 => V(it.vector3Value),
                _ => it.isArray ? "[" + it.arraySize + "]" : null,
            };
            if (v != null) sb.Append($"{it.name}={v}; ");
        }
        sb.AppendLine();
    }

    public static string Run()
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var sb = new StringBuilder();
        var doc = all.First(t => t.name == "DecisionDocument");
        sb.AppendLine($"DecisionDocument lossy={V(doc.lossyScale)} parent={(doc.parent != null ? doc.parent.name : "(root)")}");
        Tree(doc, sb, 0);
        foreach (var mb in doc.GetComponents<MonoBehaviour>())
            if (mb != null && (mb.GetType().Namespace ?? "").StartsWith("MuseDesk")) Dump(mb, sb);
        foreach (var name in new[] { "StampApprove", "StampReject" })
        {
            var s = all.FirstOrDefault(t => t.name == name);
            if (s == null) continue;
            sb.AppendLine("--- " + name);
            Tree(s, sb, 0);
            foreach (var mb in s.GetComponents<MonoBehaviour>())
                if (mb != null && (mb.GetType().Namespace ?? "").StartsWith("MuseDesk")) Dump(mb, sb);
        }
        var hold = all.FirstOrDefault(t => t.name == "PaperHoldPoint");
        if (hold != null) sb.AppendLine($"PaperHoldPoint under {hold.parent.name} lp={V(hold.localPosition)} lossy={V(hold.lossyScale)}");
        return sb.ToString();
    }
}
