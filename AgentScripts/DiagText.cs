using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Read-only: detect mirrored text (negative world scale), dump buttons + agent arm anim.</summary>
public static class DiagText
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToList();
        var sb = new StringBuilder();

        sb.AppendLine("=== ALL TMP TEXT: world scale (negative X = MIRRORED) ===");
        foreach (var go in all)
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                var tn = c.GetType().Name;
                if (tn == "TextMeshPro" || tn == "TextMeshProUGUI" || tn == "TMP_Text")
                {
                    var ls = go.transform.lossyScale;
                    var localRot = go.transform.localEulerAngles;
                    var mirrored = (ls.x < 0) ^ (ls.y < 0) ^ (ls.z < 0);
                    string txt = "";
                    var tp = c.GetType().GetProperty("text");
                    if (tp != null) { txt = (tp.GetValue(c) as string ?? ""); if (txt.Length > 20) txt = txt.Substring(0,20); }
                    sb.AppendLine($"  '{Path(go)}'");
                    sb.AppendLine($"     text='{txt}' lossyScale={ls} localRot={localRot} MIRRORED={mirrored}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== NEGATIVE-SCALE transforms anywhere (mirror source) ===");
        foreach (var go in all)
        {
            var l = go.transform.localScale;
            if (l.x < 0 || l.y < 0 || l.z < 0)
                sb.AppendLine($"  '{Path(go)}' localScale={l}");
        }

        sb.AppendLine();
        sb.AppendLine("=== POKE BUTTONS + handlers ===");
        foreach (var go in all)
        {
            bool hasPoke = go.GetComponents<Component>().Any(c => c != null &&
                (c.GetType().Name == "PokeInteractable" || c.GetType().Name == "PokeInteractableVisual"));
            if (!hasPoke) continue;
            sb.AppendLine($"  '{Path(go)}' active={go.activeInHierarchy}");
            foreach (var c in go.GetComponents<Component>())
                if (c != null) sb.AppendLine($"      {c.GetType().Name}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Button handler scripts (Approve/Reject/Ask/Details) ===");
        foreach (var go in all)
        {
            var n = go.name.ToLower();
            if (n.Contains("approve") || n.Contains("reject") || n.Contains("ask") || n.Contains("detail") || n.Contains("button"))
            {
                sb.AppendLine($"  '{Path(go)}' active={go.activeInHierarchy}");
                foreach (var c in go.GetComponents<Component>())
                    if (c != null && (c is MonoBehaviour) && c.GetType().Namespace == null)
                        sb.AppendLine($"      {c.GetType().Name} (project script)");
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== AGENT arm animation (AgentView / arms) ===");
        foreach (var go in all)
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                var tn = c.GetType().Name;
                if (tn == "AgentView" || tn == "AgentMovement" || tn.Contains("Arm") || tn.Contains("Walk"))
                    sb.AppendLine($"  {tn} on '{Path(go)}' active={go.activeInHierarchy}");
            }
        }

        return sb.ToString();
    }

    static string Path(GameObject go)
    {
        var p = go.name;
        var t = go.transform.parent; int d = 0;
        while (t != null && d < 5) { p = t.name + "/" + p; t = t.parent; d++; }
        return p;
    }
}
