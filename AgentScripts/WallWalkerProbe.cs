using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial.Wall;
using MuseDesk.Wall;

/// <summary>
/// Play Mode sample of every Wall walker (owner rc9: "took an elevator up to the manager, never left the elevator and
/// teleported back to their seat"). Prints state, last block reason, soft transfers (fade + teleport home), the lift,
/// and the stage's drop count. Call repeatedly.
///   scripts\drv.ps1 WallWalkerProbe Sample
/// </summary>
public static class WallWalkerProbe
{
    public static string Sample()
    {
        var c = Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include);
        if (c == null || c.Stage == null) return "no stage (enter Wall first)";
        var s = c.Stage;
        var sb = new StringBuilder("t=" + Time.unscaledTime.ToString("F1") + " dropped=" + s.Dropped + " lastDrop='" + s.LastDropReason + "' lift=" + s.lift.CabinFloor + "/" + s.lift.CabinY.ToString("F3") + " door=" + s.lift.DoorOpenEased.ToString("F2") + "\n");
        foreach (HabitatRole r in System.Enum.GetValues(typeof(HabitatRole)))
        {
            HabitatWalker w = s.Walker(r);
            if (w == null) continue;
            var act = s.Running(r);
            sb.AppendLine($"  {r,-10} {w.State,-16} anim={w.Anim,-7} pos={w.Position.ToString("F3")} speed={w.Speed:F3} soft={w.SoftTransfers} block='{w.LastBlock}' act={(act != null ? act.kind + "->" + act.target : "-")}");
        }
        return sb.ToString();
    }
}
