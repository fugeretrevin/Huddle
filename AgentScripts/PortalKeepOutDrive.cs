using System.Reflection;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;
using MuseDesk.Wall;

/// <summary>
/// Play Mode driver for the P0 portal comfort keep-out (PortalComfortGuard + the keep-out-aware PortalSpotPlanner).
/// Evidence level: PLAY (Editor, synthetic room, simulated holds), never on-head. Use with WallDrive:
///   WallDrive.Enable, Enter, Place, Select, Pinch Researcher, ToPerch, Open, MeetLarger   (portal preview)
///   PortalKeepOutDrive.Report                 planner + guard view of the live room and head
///   PortalKeepOutDrive.ScreenRight            add a SCREEN to the user's right (synthetic room): right spots refused
///   PortalKeepOutDrive.Hold / Release         simulate a held paper (transition hold): "Open here" blocked / restored
///   WallDrive.PortalOpen                      confirm; then PortalKeepOutDrive.BlockSpot puts a box on the spot:
///                                             the guard closes the portal within ~0.4 s (2 failed checks)
///   PortalKeepOutDrive.NoSpace                a 1.8 m wide synthetic room: no large spot, the small-visitor fallback
/// The guard runs only once the coordinator wired it into WallHabitatController (field _comfortGuard); Report says so.
/// </summary>
public static class PortalKeepOutDrive
{
    static T One<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static WallHabitatController C => One<WallHabitatController>();

    static SeatedUser User()
    {
        var cam = Camera.main;
        return cam != null ? new SeatedUser(cam.transform.position, cam.transform.forward) : new SeatedUser(new Vector3(0f, 1.2f, 0f), Vector3.forward);
    }

    static IRoomGeometry Room() { var c = C; return c != null && c.roomService != null ? c.roomService.Room : null; }

    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C;
        if (c == null) return "no WallHabitatController";
        SeatedUser u = User();
        IRoomGeometry room = Room();
        var b = new PortalRoomBounds();
        bool ok = PortalSpotAdapter.Build(room, room as IWallGeometry, u, b, null);
        var s = new PortalSpotSettings { bodyRadius = c.portalSettings.footprintRadius, avatarHeight = c.portalSettings.maxAvatarHeight };
        var spots = PortalSpotAdapter.ListSpots(u, b, s);
        var sb = new StringBuilder();
        sb.Append("room=" + (room != null ? room.Availability.ToString() : "none") + " bounds=" + ok + " screens=" + b.screensFromScan + " desks=" + b.desksFromScan + " doors=" + b.doorsFromScan + " vols=" + b.volumes.Count);
        sb.Append(" | head=" + u.head.ToString("F2") + " fwd=" + u.forward.ToString("F2"));
        sb.Append(" | large spots=" + spots.Count);
        if (spots.Count > 0) sb.Append(" best=" + spots[0].position.ToString("F2") + " score=" + spots[0].score.ToString("F2"));
        else sb.Append(" (" + PortalSpotPlanner.Plan(u, b, s).reason + ")");
        sb.Append(" | portal=" + c.Portal.State + " spot=" + c.Portal.Spot.position.ToString("F2") + " valid=" + c.Portal.Spot.valid + " h=" + c.Portal.Spot.avatarHeight.ToString("F2") + " msg='" + c.Portal.Message + "'");
        sb.Append(" | held=" + MuseDesk.Spatial.HandInteractionProbe.AnyHeld);
        var f = typeof(WallHabitatController).GetField("_comfortGuard", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var g = f != null ? f.GetValue(c) as PortalComfortGuard : null;
        if (g == null) sb.Append(" | guard NOT WIRED (see the coordinator line)");
        else sb.Append(" | guard session=" + g.InSession + " checks=" + g.Checks + " blocks=" + g.Blocks + " closures=" + g.Closures + " last=" + g.LastAction + " '" + g.LastReason + "' lastCheck=" + (g.LastCheck.valid ? "valid" : g.LastCheck.reason));
        return sb.ToString();
    }

    /// <summary>A second monitor 1.3 m to the user's right (synthetic room only).</summary>
    public static string ScreenRight()
    {
        var syn = Room() as SyntheticRoomGeometry;
        if (syn == null) return "needs the synthetic room (WallDrive.Enable) | " + Report();
        SeatedUser u = User();
        Vector3 p = u.head + u.Right * 1.3f + u.forward * 0.2f;
        float yaw = Mathf.Atan2(u.forward.x, u.forward.z) * Mathf.Rad2Deg;
        syn.AddFurniture("SCREEN", new Vector3(p.x, u.head.y - 0.1f, p.z), new Vector3(0.6f, 0.4f, 0.08f), yaw + 90f);
        syn.RaiseChanged();
        return "screen added right | " + Report();
    }

    /// <summary>Drop a box on the current portal spot (the room scan "changed" under an open portal).</summary>
    public static string BlockSpot()
    {
        var syn = Room() as SyntheticRoomGeometry;
        var c = C;
        if (syn == null || c == null) return "needs the synthetic room | " + Report();
        Vector3 at = c.Portal.Spot.position;
        syn.AddFurniture("STORAGE", at + new Vector3(0.1f, 0.5f, 0f), new Vector3(0.4f, 1f, 0.4f));
        syn.RaiseChanged();
        return "box on the spot | " + Report();
    }

    /// <summary>A 1.8 m wide room around the head: no clear side space for a large avatar.</summary>
    public static string NoSpace()
    {
        var d = One<SpatialWorkspaceDirector>();
        if (d == null) return "no SpatialWorkspaceDirector";
        SeatedUser u = User();
        float floorY = u.head.y - 1.15f;
        d.UseSyntheticRoom(SyntheticRoomGeometry.Rectangle(u.head.x, u.head.z, 1.8f, 4f, floorY, floorY + 2.5f));
        return "narrow room | " + Report();
    }

    /// <summary>Simulate a held paper through the transition hold (HandInteractionProbe.AnyHeld reads it). Always Release after.</summary>
    public static string Hold() { SpatialWorkspaceState.AcquireTransitionHold(); return "holding | " + Report(); }
    public static string Release() { SpatialWorkspaceState.ReleaseTransitionHold(); return "released | " + Report(); }
}
