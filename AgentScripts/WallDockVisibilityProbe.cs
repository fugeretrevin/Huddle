using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Wall;

/// <summary>
/// Play Mode check (owner rc9 report: "the panel for placing the wall or choosing floor or ceiling was entirely gone"):
/// with Wall mode active, is the unified dock (ModeConsole) actually VISIBLE and POKABLE? Checks renderers (enabled +
/// active), every key's PokeInteractable enabled state, and whether the console sits under the office root that Wall
/// mode suppresses. Run after WallDrive Enable / Enter / Place (or from Desk to compare).
///   scripts\drv.ps1 WallDockVisibilityProbe Report
/// </summary>
public static class WallDockVisibilityProbe
{
    static string Path(Transform t) { var s = t.name; for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s; return s; }

    public static string Report()
    {
        var sb = new StringBuilder();
        var c = Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include);
        var console = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.gameObject.scene.IsValid() && t.name == "ModeConsole");
        if (console == null) return "no ModeConsole";
        sb.AppendLine("wall active=" + (c != null && c.WallActive) + " previewing=" + (c != null && c.WallPreviewing));
        sb.AppendLine("console " + Path(console) + " activeInHierarchy=" + console.gameObject.activeInHierarchy + " lossyScale=" + console.lossyScale.x.ToString("F3"));
        Transform office = c != null && c.officeRoot != null ? c.officeRoot : MuseDesk.Spatial.SpatialWorkspaceState.WorkspaceRoot;
        sb.AppendLine("office root=" + (office != null ? Path(office) : "none") + " consoleUnderOffice=" + (office != null && console.IsChildOf(office)));
        if (c != null && c.neverHide != null) sb.AppendLine("neverHide=[" + string.Join(", ", c.neverHide.Where(t => t != null).Select(t => t.name + (console.IsChildOf(t) ? " (contains console)" : ""))) + "]");
        var rs = console.GetComponentsInChildren<Renderer>(true);
        int on = rs.Count(r => r.enabled && r.gameObject.activeInHierarchy);
        sb.AppendLine("renderers " + on + "/" + rs.Length + " visible");
        var pokes = console.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m != null && m.GetType().Name == "PokeInteractable").ToList();
        sb.AppendLine("poke keys " + pokes.Count(p => p.enabled && p.gameObject.activeInHierarchy) + "/" + pokes.Count + " enabled");
        var keys = console.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Console_") || t.name.StartsWith("Key_") || t.name.Contains("Toggle")).ToList();
        foreach (var k in keys.Take(24))
        {
            var kr = k.GetComponentsInChildren<Renderer>(true);
            var kp = k.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(m => m != null && m.GetType().Name == "PokeInteractable");
            sb.AppendLine($"  {k.name,-22} scale={k.localScale.x:F3} renderers {kr.Count(r => r.enabled && r.gameObject.activeInHierarchy)}/{kr.Length} poke={(kp == null ? "-" : kp.enabled ? "on" : "OFF")}");
        }
        var cam = Camera.main;
        if (cam != null) sb.AppendLine("distance head->console " + (console.position - cam.transform.position).magnitude.ToString("F2") + " m");
        return sb.ToString();
    }
}
