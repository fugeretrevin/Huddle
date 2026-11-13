using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial;

/// <summary>
/// Play Mode driver for the ceiling scan grid, using the synthetic room (Editor).
///   CeilingScanDrive.Preview   enable flags + synthetic room, open the Ceiling preview
///   CeilingScanDrive.Next      next candidate (wipe restarts)
///   CeilingScanDrive.Confirm / Cancel   the grid fades out over 0.3 s
///   CeilingScanDrive.Report    state of grid and preview (call a few times over ~1 s to see the wipe)
/// </summary>
public static class CeilingScanDrive
{
    static T One<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);

    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var s = One<CeilingScanReveal>();
        var d = One<SpatialWorkspaceDirector>();
        if (s == null) return "no CeilingScanReveal (run BuildCeilingScanV1.Apply)";
        var sb = new StringBuilder();
        sb.Append($"grid: shown={s.Shown} fade={s.Fade:F2} wipeT={s.WipeSeconds:F2}s radius={s.RevealRadiusNow:F2}m red={s.Red} centre={s.Centre:F3}");
        if (s.Quad != null) sb.Append($" quad={s.Quad.position:F3} scale={s.Quad.localScale.x:F2}");
        if (d != null && d.Core != null)
        {
            var c = d.Core; var p = c.LastCeiling;
            sb.Append($" | director: mode={d.Mode} previewing={d.IsPreviewing} pending={c.PendingMode} candidate={c.CandidateIndex}/{c.CandidateCount} valid={p.valid} shownValid={c.ShownValid} ceilingHeight={p.ceilingHeight:F3} dock={p.dockPosition:F3} mastTop={p.mastTop:F3}");
        }
        sb.Append(s.material == null ? " | MATERIAL MISSING" : "");
        return sb.ToString();
    }

    public static string Preview()
    {
        if (!Application.isPlaying) return "not playing";
        var d = One<SpatialWorkspaceDirector>(); if (d == null) return "no director";
        d.SetFlags(true, true); d.UseSyntheticTestRoom(); d.RequestCeiling();
        return Report();
    }

    public static string Next() { One<SpatialWorkspaceDirector>().NextCandidate(); return Report(); }
    public static string Confirm() { One<SpatialWorkspaceDirector>().ConfirmPlacement(); return Report(); }
    public static string Cancel() { One<SpatialWorkspaceDirector>().Cancel(); return Report(); }
    public static string Desk() { One<SpatialWorkspaceDirector>().RequestDesk(); return Report(); }
}
