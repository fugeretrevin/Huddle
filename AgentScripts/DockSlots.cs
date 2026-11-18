using System.Text;
using UnityEngine;

/// <summary>Edit-mode read-only: the Spaces dock's key roots and their local positions (which slots overlap).</summary>
public static class DockSlots
{
    public static string Run()
    {
        var mc = Object.FindAnyObjectByType<MuseDesk.Spatial.ModeConsole>(FindObjectsInactive.Include);
        if (mc == null) return "no ModeConsole";
        var sb = new StringBuilder();
        void K(string n, MuseDesk.Spatial.ModeConsole.ConsoleKey k)
        {
            sb.Append(n).Append(": ");
            if (k == null || k.root == null) { sb.Append("none\n"); return; }
            sb.Append(k.root.name).Append(" parent=").Append(k.root.parent != null ? k.root.parent.name : "-")
              .Append(" local=").Append(k.root.localPosition.ToString("F4")).Append('\n');
        }
        K("desk", mc.deskKey); K("floor", mc.floorKey); K("ceiling", mc.ceilingKey); K("wall", mc.wallKey);
        K("next", mc.nextKey); K("confirm", mc.confirmKey); K("cancel", mc.cancelKey); K("stop", mc.stopKey);
        K("pod", mc.podKey); K("bring", mc.bringKey); K("status", mc.statusKey); K("layout", mc.layoutKey);
        K("move", mc.moveKey); K("select", mc.selectKey); K("manager", mc.managerKey); K("meet", mc.meetKey);
        if (mc.focusKey != null) sb.Append("focus local=").Append(mc.focusKey.localPosition.ToString("F4"));
        return sb.ToString();
    }
}
