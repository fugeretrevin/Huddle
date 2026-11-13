using System.Text;
using UnityEngine;
using MuseDesk.Spatial;

/// <summary>
/// Play Mode driver for the guided placement step (PlacementGuide). The gaze is injected, the camera
/// is never moved: LookUp / LookDown / LookLevel set a direction the guide reads instead of the
/// camera's forward. Typical run (after SpatialDrive.Enable()):
///   PlacementGuideDrive.PressCeiling()  -> card up, state Guiding
///   PlacementGuideDrive.LookUp(60)      -> hold ~0.6 s (real time), the guide requests the ceiling
///   PlacementGuideDrive.Report()
///   PlacementGuideDrive.Cancel() / SpatialDrive.Confirm() / SpatialDrive.Desk()
/// </summary>
public static class PlacementGuideDrive
{
    static T One<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);

    static Vector3 Dir(float pitchDeg)
    {
        var cam = Camera.main;
        Vector3 f = cam != null ? cam.transform.forward : Vector3.forward;
        f.y = 0f;
        f = f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
        float r = pitchDeg * Mathf.Deg2Rad;
        return f * Mathf.Cos(r) + Vector3.up * Mathf.Sin(r);
    }

    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var g = One<PlacementGuide>();
        if (g == null) return "no PlacementGuide in the scene (run BuildPlacementGuideV1.Apply)";
        var sb = new StringBuilder(500);
        sb.AppendLine(g.Describe());
        var cs = One<CeilingScanReveal>();
        if (cs != null) sb.AppendLine($"  ceilingScan shown={cs.Shown} fade={cs.Fade:F2} sweeping={cs.Sweeping} sweepFrom={cs.SweepFrom:F2} centre={cs.Centre:F2} red={cs.Red}");
        var fs = One<FloorScanReveal>();
        if (fs != null) sb.AppendLine($"  floorScan shown={fs.Shown} fade={fs.Fade:F2} sweeping={fs.Sweeping} sweepFrom={fs.SweepFrom:F2} centre={fs.Centre:F2} red={fs.Red}");
        var d = One<SpatialWorkspaceDirector>();
        if (d != null) sb.Append($"  director mode={d.Mode} previewing={d.IsPreviewing} status='{d.StatusText}'");
        return sb.ToString();
    }

    /// <summary>Press the console's Floor key as a hand would (goes through the guide).</summary>
    public static string PressFloor()
    {
        var c = One<ModeConsole>();
        if (c == null) return "no ModeConsole";
        c.PressByName("Floor");
        return Report();
    }

    public static string PressCeiling()
    {
        var c = One<ModeConsole>();
        if (c == null) return "no ModeConsole";
        c.PressByName("Ceiling");
        return Report();
    }

    public static string PressDesk()
    {
        var c = One<ModeConsole>();
        if (c == null) return "no ModeConsole";
        c.PressByName("Desk");
        return Report();
    }

    public static string LookUp(float degrees = 60f) { var g = One<PlacementGuide>(); if (g == null) return "no guide"; g.InjectGaze(Dir(Mathf.Abs(degrees))); return Report(); }
    public static string LookDown(float degrees = 60f) { var g = One<PlacementGuide>(); if (g == null) return "no guide"; g.InjectGaze(Dir(-Mathf.Abs(degrees))); return Report(); }
    public static string LookLevel() { var g = One<PlacementGuide>(); if (g == null) return "no guide"; g.InjectGaze(Dir(0f)); return Report(); }
    public static string ClearGaze() { var g = One<PlacementGuide>(); if (g == null) return "no guide"; g.InjectGaze(null); return Report(); }

    /// <summary>The card's Cancel key.</summary>
    public static string Cancel() { var g = One<PlacementGuide>(); if (g == null) return "no guide"; g.PressCancel(); return Report(); }

    /// <summary>The card's "Start here" key (only does anything once it has been offered, 8 s after the card appears).</summary>
    public static string PressStart() { var g = One<PlacementGuide>(); if (g == null) return "no guide"; g.PressStart(); return Report(); }

    /// <summary>Make the next Floor or Ceiling guide time out at once (the Start key is offered after a short wait).</summary>
    public static string ShortTimeout(float seconds = 1f)
    {
        var g = One<PlacementGuide>(); if (g == null) return "no guide";
        g.timeoutSeconds = seconds;
        return "timeout " + seconds + " s (applies to the next guide)";
    }

    /// <summary>Simulates a full ceiling flow synchronously where possible: press, look up, report. Hold the look for 0.6 s of real time before Report().</summary>
    public static string BeginCeilingLookUp() { PressCeiling(); return LookUp(60f); }
    public static string BeginFloorLookDown() { PressFloor(); return LookDown(60f); }
}
