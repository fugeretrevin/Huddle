using System.Linq;
using UnityEngine;

/// <summary>rc18 checks in Play Mode: teleport-in beams, tubes, dragged cards, wall-after-desk.</summary>
public static class Rc18Probe
{
    /// <summary>Floor -> Wall: flags on (wall + floor/ceiling), synthetic room, request Floor and confirm.</summary>
    public static string FloorSetup()
    {
        var w = Object.FindObjectsByType<MuseDesk.Wall.WallHabitatController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        var d = Object.FindObjectsByType<MuseDesk.Spatial.SpatialWorkspaceDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        w.SetFlags(new MuseDesk.Spatial.Wall.WallFeatureFlags { wallHabitatEnabled = true, wallManagerEnabled = true, agentSelectEnabled = true, agentBtwEnabled = true, largeAgentPortalEnabled = true, wallElevatorVfxEnabled = false });
        d.SetFlags(true, true); d.UseSyntheticTestRoom();
        d.RequestFloor();
        return Run();
    }
    public static string Confirm() { Object.FindObjectsByType<MuseDesk.Spatial.SpatialWorkspaceDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).First().ConfirmPlacement(); return Run(); }
    /// <summary>Press the dock's Wall key, exactly as a finger would.</summary>
    public static string PressWallKey()
    {
        var mc = Object.FindObjectsByType<MuseDesk.Spatial.ModeConsole>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (mc == null) return "no ModeConsole";
        mc.PressWall();
        return "pressed Wall | " + Run();
    }

    public static string Run()
    {
        var w = Object.FindObjectsByType<MuseDesk.Wall.WallHabitatController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        var d = Object.FindObjectsByType<MuseDesk.Spatial.SpatialWorkspaceDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        int tubes = Resources.FindObjectsOfTypeAll<Transform>().Count(t => t.gameObject.scene.IsValid() && t.name.StartsWith("CardTube_"));
        return "beams=" + (w != null ? w.DeskBeamsStarted : -1) + " showing=" + (w != null && w.DeskBeamShowing)
               + " tubes=" + tubes + " dragged=" + (w != null ? w.DraggedCard : "-")
               + " wallAfterDesk=" + (d != null && d.WallAfterDeskPending)
               + " mode=" + MuseDesk.Spatial.SpatialWorkspaceState.Mode + " wallActive=" + (w != null && w.WallActive) + " wallPreview=" + (w != null && w.WallPreviewing);
    }
}

