using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>Read-only: structure of a ModeConsole key (to clone it for the Wall controls) and the console's placement.</summary>
public static class WallKeyProbe
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var console = all.FirstOrDefault(t => t.GetComponent<MuseDesk.Spatial.ModeConsole>() != null);
        var sb = new StringBuilder();
        if (console == null) return "no console";
        var mc = console.GetComponent<MuseDesk.Spatial.ModeConsole>();
        sb.AppendLine("console " + Path(console) + " pos=" + console.position.ToString("F3") + " rot=" + console.eulerAngles.ToString("F0") + " lossy=" + console.lossyScale.ToString("F3"));
        var rs = console.GetComponentsInChildren<Renderer>(true); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        sb.AppendLine("console bounds c=" + b.center.ToString("F3") + " s=" + b.size.ToString("F3"));
        var k = mc.statusKey.root;
        sb.AppendLine("statusKey " + Path(k) + " local=" + k.localPosition.ToString("F3") + " scale=" + k.localScale.ToString("F3") + " shown=" + mc.statusKey.shownScale.ToString("F3"));
        Dump(k, 0, sb);
        var dk = mc.deskKey.root;
        sb.AppendLine("deskKey " + Path(dk) + " local=" + dk.localPosition.ToString("F3"));
        if (mc.statusLabel != null) sb.AppendLine("statusLabel " + Path(mc.statusLabel.transform) + " fs=" + mc.statusLabel.fontSize + " text='" + mc.statusLabel.text + "'");
        return sb.ToString();
    }

    static void Dump(Transform t, int d, StringBuilder sb)
    {
        if (d > 4) return;
        sb.AppendLine(new string(' ', 2 * d + 2) + t.name + " [" + (t.gameObject.activeSelf ? "on" : "off") + "] l=" + t.localPosition.ToString("F4") + " s=" + t.localScale.ToString("F3") + " :: " +
            string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name)));
        foreach (Transform c in t) Dump(c, d + 1, sb);
    }

    static string Path(Transform t) { string p = t.name; while (t.parent != null) { t = t.parent; p = t.name + "/" + p; } return p; }
}
