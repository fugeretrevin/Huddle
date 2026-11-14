using System.Linq;
using System.Text;
using MuseDesk.Spatial.Wall;

/// <summary>Read-only: builds both habitat layouts and prints the validator's findings and key numbers.</summary>
public static class WallCoreProbe
{
    public static string Run()
    {
        UnityEditor.AssetDatabase.Refresh();
        var sb = new StringBuilder();
        foreach (WallLayoutKind k in new[] { WallLayoutKind.Full, WallLayoutKind.Compact })
        {
            var L = HabitatLayoutBuilder.Build(k);
            var issues = HabitatLayoutValidator.Validate(L);
            sb.AppendLine(k + ": floors=" + L.floors.Count + " sockets=" + L.sockets.Count + " boxes=" + L.boxes.Count + " nodes=" + L.nav.nodes.Count + " edges=" + L.nav.edges.Count + " issues=" + issues.Count);
            foreach (var f in L.floors) sb.AppendLine("  floor " + f.id + " y=" + f.floorY.ToString("F3") + " clear=" + f.Clearance.ToString("F3") + " x=[" + f.xMin.ToString("F3") + "," + f.xMax.ToString("F3") + "]");
            foreach (var i in issues.Take(20)) sb.AppendLine("  ISSUE " + i.what);
        }
        return sb.ToString();
    }
}
