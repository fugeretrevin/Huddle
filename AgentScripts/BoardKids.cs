using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;
using TMPro;

public static class BoardKids
{
    public static string Run()
    {
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var sb = new StringBuilder();
        foreach (var rootName in new[] { "TaskBoard", "CardToolbar", "AssignDock", "InboxTray", "InboxReadout", "OutboxTray" })
        {
            var b = all.FirstOrDefault(t => t.name == rootName); if (b == null) continue;
            sb.AppendLine($"== {rootName} lp={b.localPosition} ls={b.localScale}");
            foreach (Transform c in b)
            {
                var r = c.GetComponent<Renderer>();
                var tmp = c.GetComponent<TMP_Text>();
                sb.AppendLine($"  {c.name} active={c.gameObject.activeSelf} lp=({c.localPosition.x:F3},{c.localPosition.y:F3},{c.localPosition.z:F3}) ls=({c.localScale.x:F3},{c.localScale.y:F3},{c.localScale.z:F3}) rend={(r != null && r.enabled)} comps={string.Join(",", c.GetComponents<MonoBehaviour>().Where(m => m != null).Select(m => m.GetType().Name))}{(tmp != null ? " text='" + tmp.text + "'" : "")}");
            }
        }
        return sb.ToString();
    }
}
