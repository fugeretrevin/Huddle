using System.Linq;
using UnityEngine;
using MuseDesk.Spatial;

/// <summary>Press a unified-dock key exactly as a finger poke would (ModeConsole.PressX) and report the console state.
///   scripts\drv.ps1 ConsoleDrive Press Floor | Ceiling | Wall | Desk | Next | Confirm | Cancel | Pod | Bring | Status | Stop
///   scripts\drv.ps1 ConsoleDrive State</summary>
public static class ConsoleDrive
{
    static ModeConsole C => Object.FindAnyObjectByType<ModeConsole>(FindObjectsInactive.Include);

    public static string Press(string key)
    {
        var c = C; if (c == null) return "no ModeConsole";
        var m = typeof(ModeConsole).GetMethod("Press" + key);
        if (m == null) return "no Press" + key;
        m.Invoke(c, null);
        return "pressed " + key + " | " + State();
    }

    public static string State()
    {
        var c = C; if (c == null) return "no ModeConsole";
        return "mode=" + SpatialWorkspaceState.Mode + " transitioning=" + SpatialWorkspaceState.IsTransitioning + " | " + c.Describe();
    }
}
