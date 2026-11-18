using System.Text;
using UnityEngine;
using MuseDesk.Wall;

/// <summary>rc23 Play Mode probe for the habitat monitor-screen fix (WallHabitatView.FixMonitorScreens).
///   scripts\drv.ps1 Rc23ScreenFixProbe Report
///   scripts\drv.ps1 Rc23ScreenFixProbe AimDevMonitor      (then capture; user side)
///   scripts\drv.ps1 Rc23ScreenFixProbe AimDevMonitorBack  (from the agent's side, for comparison)</summary>
public static class Rc23ScreenFixProbe
{
    static WallHabitatController C => Object.FindAnyObjectByType<WallHabitatController>();

    public static string Report()
    {
        var c = C; if (c == null || c.View == null) return "no view";
        var v = c.View;
        var sb = new StringBuilder("ScreenTrianglesFixed=" + v.ScreenTrianglesFixed + " renderers=" + v.ScreenFixLog.Count + "\n");
        foreach (var kv in v.ScreenFixLog)
        {
            if (kv.Key == null) continue;
            var mf = kv.Key.GetComponent<MeshFilter>();
            sb.Append(kv.Key.name).Append(" mesh=").Append(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-")
              .Append(" local=").Append(v.root.InverseTransformPoint(kv.Key.bounds.center).ToString("F3"))
              .Append(" ").Append(kv.Value).Append('\n');
        }
        return sb.ToString();
    }

    static string Aim(float z)
    {
        var c = C; if (c == null || c.View == null) return "no view";
        Transform root = c.View.root;
        Renderer target = null;
        foreach (var kv in c.View.ScreenFixLog) if (kv.Key != null && kv.Key.name.Contains("Developer_MainMonitor")) target = kv.Key;
        if (target == null) return "no fixed Developer main monitor";
        Vector3 at = target.bounds.center;
        Vector3 pos = at + root.TransformDirection(new Vector3(0.03f, 0.03f, z));
        var go = GameObject.Find("__CineCam") ?? new GameObject("__CineCam");
        var cam = go.GetComponent<Camera>(); if (cam == null) cam = go.AddComponent<Camera>();
        cam.enabled = true; cam.depth = 100f; cam.stereoTargetEye = StereoTargetEyeMask.None; cam.nearClipPlane = 0.005f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.2f, 0.22f, 0.25f);
        cam.transform.position = pos; cam.transform.rotation = Quaternion.LookRotation(at - pos, Vector3.up); cam.fieldOfView = 35f;
        return "aimed at " + root.InverseTransformPoint(at).ToString("F3") + " from habitat-local z offset " + z + " fixed=" + c.View.ScreenTrianglesFixed;
    }

    /// <summary>From the user's side (+Z, the open front): should show the dark back of the monitor.</summary>
    public static string AimDevMonitor() { return Aim(0.22f); }

    /// <summary>From the agent's side (-Z of the monitor): should show the glowing screen.</summary>
    public static string AimDevMonitorBack() { return Aim(-0.10f); }
}
