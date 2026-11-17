using UnityEngine;
using MuseDesk.Wall;

/// <summary>rc21 Play Mode probes for the Screen key (now in the Focus key's slot) and the faster screen stream. Read-only except Open.</summary>
public static class Rc21ScreenProbe
{
    static WallHabitatController C() { return Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include); }

    /// <summary>Where the Screen key and the Focus key are; the Focus key must be at a tiny scale and the Screen key at the Focus slot.</summary>
    public static string Keys()
    {
        if (!Application.isPlaying) return "not in Play Mode";
        var c = C(); return c == null ? "no WallHabitatController" : c.DriverScreenKeyReport();
    }

    /// <summary>Press the Screen key (opens or closes the panel).</summary>
    public static string Toggle()
    {
        if (!Application.isPlaying) return "not in Play Mode";
        var c = C(); if (c == null) return "no WallHabitatController";
        c.PressScreen();
        return "open=" + c.ScreenOpen;
    }

    /// <summary>Frames uploaded and the measured uploads per second (open the panel first, wait a few seconds, call this).</summary>
    public static string Stats()
    {
        if (!Application.isPlaying) return "not in Play Mode";
        var c = C(); if (c == null) return "no WallHabitatController";
        return "open=" + c.ScreenOpen + " frames=" + c.ScreenFrames + " fps=" + c.ScreenFps.ToString("F1") + " problem=" + c.ScreenProblem;
    }
}
