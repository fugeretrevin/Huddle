using System.Text;
using TMPro;
using UnityEngine;

/// <summary>Where is the text that reads X? Path, root-local position and the sibling texts of its card.</summary>
public static class FindText
{
    public static string Run(string contains)
    {
        var sb = new StringBuilder();
        var ws = GameObject.Find("WorkspaceRoot");
        foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (!t.gameObject.scene.IsValid() || t.text == null || !t.text.Contains(contains)) continue;
            string p = t.name; Transform card = t.transform.parent;
            for (Transform q = t.transform.parent; q != null; q = q.parent) p = q.name + "/" + p;
            sb.AppendLine(p + " active=" + t.gameObject.activeInHierarchy + " text='" + t.text.Replace("\n", " / ") + "'");
            if (card != null)
            {
                Vector3 lp = ws != null ? ws.transform.InverseTransformPoint(card.position) : card.position;
                sb.AppendLine("  card " + card.name + " wsLocal=" + lp.ToString("F3") + " localPos=" + card.localPosition.ToString("F3") + " parent=" + (card.parent != null ? card.parent.name : "-"));
                foreach (var s in card.GetComponentsInChildren<TMP_Text>(true)) sb.AppendLine("    text " + s.name + " '" + s.text.Replace("\n", " / ") + "'");
            }
        }
        return sb.Length > 0 ? sb.ToString() : "no text containing '" + contains + "'";
    }
}
