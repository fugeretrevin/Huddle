using System.Globalization;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial;

/// <summary>
/// Play Mode driver for the worn-pass-1 placement numbers. RUNTIME ONLY: Apply writes the new code
/// defaults into the running director's settings (the core holds the same objects); nothing is saved,
/// and leaving Play Mode restores the scene values. The scene itself is updated by
/// BuildWornPassLooseningV1. Use with SpatialDrive (Enable, Ceiling, Confirm, PodDown, PodUp, Stop, Desk).
///   scripts\drv.ps1 WornPassDrive Apply   |   scripts\drv.ps1 WornPassDrive Report
/// </summary>
public static class WornPassDrive
{
    static T One<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static string F(float v) { return v.ToString("F2", CultureInfo.InvariantCulture); }

    public static string Apply()
    {
        if (!Application.isPlaying) return "not playing";
        var d = One<SpatialWorkspaceDirector>();
        if (d == null) return "no director";
        var c = d.ceilingSettings; var f = d.floorSettings;
        var dc = new CeilingPlacementSettings(); var df = new FloorPlacementSettings();
        c.inspectDropBelowEye = dc.inspectDropBelowEye;
        c.inspectWideSideOptions = (float[])dc.inspectWideSideOptions.Clone();
        c.forwardOptions = (float[])dc.forwardOptions.Clone();
        c.shallowBaseAboveKeepOut = (float[])dc.shallowBaseAboveKeepOut.Clone();
        c.shallowMaxAboveEye = dc.shallowMaxAboveEye;
        c.minDescent = dc.minDescent;
        c.compactObstacleLabels = (string[])dc.compactObstacleLabels.Clone();
        c.lowObstacleMaxHeight = dc.lowObstacleMaxHeight;
        c.lowObstacleMargin = dc.lowObstacleMargin;
        f.lowObstacleMaxHeight = df.lowObstacleMaxHeight;
        f.lowObstaclePad = df.lowObstaclePad;
        return "applied (runtime only): drop " + F(c.inspectDropBelowEye) + ", wide " + c.inspectWideSideOptions.Length
               + " options, forward " + c.forwardOptions.Length + " options, shallow " + c.shallowBaseAboveKeepOut.Length
               + ", compact " + string.Join("/", c.compactObstacleLabels) + ", floor low clutter pad " + F(f.lowObstaclePad);
    }

    /// <summary>Synthetic room only: a sock (4 cm high) 8 cm beyond the far edge of the previewed floor
    /// footprint, then a room event (the preview is rebuilt). Sock = low clutter, must not block.</summary>
    public static string SockBesidePreview() { return BesidePreview("OTHER", 0.04f, 0.08f); }

    /// <summary>The same place, a 1.8 m shelf instead: must still block.</summary>
    public static string ShelfBesidePreview() { return BesidePreview("STORAGE", 1.8f, 0.08f); }

    static string BesidePreview(string label, float height, float gap)
    {
        if (!Application.isPlaying) return "not playing";
        var d = One<SpatialWorkspaceDirector>();
        if (d == null || d.roomService == null) return "no director or room service";
        var room = d.roomService.Room as SyntheticRoomGeometry;
        if (room == null) return "not a synthetic room (run SpatialDrive Enable first)";
        if (!d.IsPreviewing) return "no preview open (SpatialDrive Floor first)";
        RootPose spot = d.Core.PreviewPose;
        OrientedRect r = SpatialGeometry.Footprint(spot.position, spot.rotation, spot.scale, d.footprint, d.floorSettings.footprintCenterOffset);
        Vector2 c = r.center - r.axisY * (r.half.y + gap + 0.05f);
        room.AddFurniture(label, new Vector3(c.x, spot.position.y + height * 0.5f, c.y), new Vector3(0.25f, height, 0.10f), spot.rotation.eulerAngles.y);
        room.RaiseChanged();
        RootPose now = d.Core.PreviewPose;
        return label + " " + F(height) + " m tall, " + F(gap) + " m beyond the spot at " + spot.position.ToString("F2")
               + "; after the room event the preview is at " + now.position.ToString("F2")
               + (Vector3.Distance(now.position, spot.position) < 0.01f ? " (same spot)" : " (moved)")
               + " valid=" + d.Core.PreviewValid + " status='" + d.Core.StatusText + "'";
    }

    public static string ClearFurnitureExceptDesk()
    {
        if (!Application.isPlaying) return "not playing";
        var d = One<SpatialWorkspaceDirector>();
        var room = d != null && d.roomService != null ? d.roomService.Room as SyntheticRoomGeometry : null;
        if (room == null) return "not a synthetic room";
        d.UseSyntheticTestRoom();
        return "synthetic room rebuilt (desk only)";
    }

    /// <summary>Where the office is relative to the head: height against the eyes and the horizontal
    /// distance from the head's vertical axis to the office's footprint (the keep-out is 0.35 m).</summary>
    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var d = One<SpatialWorkspaceDirector>();
        if (d == null) return "no director";
        var core = d.Core;
        SeatedUser u = d.CurrentUser;
        RootPose p = core.Mover.CurrentPose;
        OrientedRect r = SpatialGeometry.Footprint(p.position, p.rotation, p.scale, d.footprint, d.ceilingSettings.footprintCenterOffset);
        float axis = r.DistanceToPoint(new Vector2(u.head.x, u.head.z));
        var sb = new StringBuilder();
        sb.Append("mode=").Append(core.Mode).Append(" pod=").Append(core.Pod.State)
          .Append(" baseVsEye=").Append(F(p.position.y - u.head.y))
          .Append(" footprintFromHeadAxis=").Append(F(axis))
          .Append(" route=").Append(core.LastRoute.count).Append(" legs-points")
          .Append(" status='").Append(core.StatusText).Append("'");
        return sb.ToString();
    }
}
