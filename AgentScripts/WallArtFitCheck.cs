using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using MuseDesk.Spatial.Wall;

/// <summary>
/// Wall art fit check (the tool HabitatArtManifest.cs refers to). Read-only: loads an Antigravity Wall prefab, finds the
/// sockets that correspond to the stage layout's sockets and prints their positions relative to the prefab's own
/// WallContact next to the layout's numbers (HabitatLayoutBuilder, habitat-local metres: x centred, y centred on the backplate,
/// z out of the wall). The layout is what walkers and the elevator use; art that does not match to within Tolerance must not
/// replace the proxy under walkers. Edit Mode only; changes nothing.
///   scripts\drv.ps1 WallArtFitCheck Report Full        (Complete_3Tier against the Full layout)
///   scripts\drv.ps1 WallArtFitCheck Report Compact     (Complete_2Tier against the Compact layout)
/// </summary>
public static class WallArtFitCheck
{
    const string Dir = "Assets/MuseDesk/Art/Prefabs/WallHabitat/";
    public const float Tolerance = 0.01f;     // 1 cm: the layout clearance is 5 mm, so this is already generous

    // layout socket -> art socket
    static readonly (string layout, string art)[] Map =
    {
        ("WallContact", "WallContact"),
        ("ResearchCubicleSocket", "ResearchCubicleSocket"),
        ("DeveloperCubicleSocket", "DeveloperCubicleSocket"),
        ("TesterCubicleSocket", "TestingCubicleSocket"),
        ("ManagerOfficeSocket", "ManagerLookoutSocket"),
        ("ElevatorCarSocket", "ElevatorCarSocket"),
        ("ElevatorStop_Lower", "ElevatorStop_Lower"),
        ("ElevatorStop_Mid", "ElevatorStop_Mid"),
        ("ElevatorStop_Manager", "ElevatorStop_Manager"),
        ("TaskPrinterSocket", "TaskPrinterSocket"),
        ("AttentionBeaconSocket", "AttentionBeaconSocket"),
        ("CourierDispatchSocket", "CourierDispatchSocket"),
        ("SharedMeetingSocket", "PlanningNookSocket"),
    };

    public static string Report(string kindName)
    {
        var kind = kindName == "Compact" ? WallLayoutKind.Compact : WallLayoutKind.Full;
        string prefabName = kind == WallLayoutKind.Full ? "WallHabitat_Complete_3Tier" : "WallHabitat_Complete_2Tier";
        var layout = HabitatLayoutBuilder.Build(kind);
        var root = PrefabUtility.LoadPrefabContents(Dir + prefabName + ".prefab");
        var sb = new StringBuilder();
        try
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            Transform Find(string n) => all.FirstOrDefault(t => t.name == n);
            var wc = Find("WallContact");
            Vector3 origin = wc != null ? root.transform.InverseTransformPoint(wc.position) : Vector3.zero;
            var b = Bounds(root);
            sb.AppendLine(prefabName + " vs layout " + kind + ": layout size " + layout.size.ToString("F3") + ", art bounds size " + b.size.ToString("F3")
                + " (min " + b.min.ToString("F3") + " max " + b.max.ToString("F3") + "), art WallContact at prefab-root " + origin.ToString("F3"));
            sb.AppendLine("socket: layout | art (relative to art WallContact) | delta | verdict (tolerance " + Tolerance.ToString("F3") + " m)");
            int fit = 0, miss = 0, nofit = 0;
            foreach (var (ls, art) in Map)
            {
                HabitatSocket s;
                bool inLayout = layout.TryGetSocket(ls, out s);
                var t = Find(art);
                if (!inLayout && t == null) continue;
                if (!inLayout) { sb.AppendLine("  " + ls + ": not in this layout (art has " + art + ")"); continue; }
                if (t == null) { sb.AppendLine("  " + ls + ": layout " + s.position.ToString("F3") + " | art socket '" + art + "' MISSING"); miss++; continue; }
                Vector3 a = root.transform.InverseTransformPoint(t.position) - origin;
                // The layout's y is centred on the backplate; the layout WallContact is (0,0,0), so compare in layout-local with the art contact as origin.
                Vector3 d = a - s.position;
                bool ok = d.magnitude <= Tolerance;
                if (ok) fit++; else nofit++;
                sb.AppendLine("  " + ls + " <- " + art + ": layout " + s.position.ToString("F3") + " | art " + a.ToString("F3") + " | delta " + d.ToString("F3") + " (" + d.magnitude.ToString("F3") + " m) | " + (ok ? "FIT" : "NO FIT"));
            }
            // floors: layout floorY vs art stop Y relative to the art's lowest stop
            sb.AppendLine("summary: fit " + fit + ", no fit " + nofit + ", missing " + miss);
            // Delta after removing the best common offset (is it a pure translation?)
            var deltas = new List<Vector3>();
            foreach (var (ls, art) in Map)
            {
                HabitatSocket s; var t = Find(art);
                if (t == null || !layout.TryGetSocket(ls, out s) || ls == "WallContact") continue;
                deltas.Add((root.transform.InverseTransformPoint(t.position) - origin) - s.position);
            }
            if (deltas.Count > 0)
            {
                Vector3 mean = Vector3.zero; foreach (var d in deltas) mean += d; mean /= deltas.Count;
                float resid = deltas.Max(d => (d - mean).magnitude);
                sb.AppendLine("after the best single translation " + mean.ToString("F3") + ", the worst remaining socket error is " + resid.ToString("F3") + " m (" + (resid <= Tolerance ? "a pure offset would fit" : "not a translation: the art is a different layout") + ")");
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return sb.ToString();
    }

    static Bounds Bounds(GameObject root)
    {
        var rs = root.GetComponentsInChildren<Renderer>(true);
        var bb = rs.Length > 0 ? rs[0].bounds : new Bounds(root.transform.position, Vector3.zero);
        foreach (var r in rs) bb.Encapsulate(r.bounds);
        var inv = root.transform.worldToLocalMatrix;
        return new Bounds(inv.MultiplyPoint3x4(bb.center), bb.size);
    }

    /// <summary>List every socket-like child (names ending in Socket, Pose, Stop_*, PathNode_*, Handoff_*) of a prefab, prefab-root local.</summary>
    public static string Sockets(string prefabName)
    {
        var root = PrefabUtility.LoadPrefabContents(Dir + prefabName + ".prefab");
        var sb = new StringBuilder();
        try
        {
            var wc = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "WallContact");
            Vector3 o = wc != null ? root.transform.InverseTransformPoint(wc.position) : Vector3.zero;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith("Socket") || t.name.EndsWith("Pose") || t.name.Contains("Stop_") || t.name.StartsWith("PathNode") || t.name.StartsWith("Handoff") || t.name.StartsWith("Elevator"))
                    sb.AppendLine(t.name + " " + (root.transform.InverseTransformPoint(t.position) - o).ToString("F3"));
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return sb.ToString();
    }
}
