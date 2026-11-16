using UnityEditor;
using UnityEngine;

/// <summary>Aim the Scene View at the placed Wall habitat (front, three-quarter or close on the elevator) for a capture.
///   scripts\drv.ps1 WallShot Front | ThreeQuarter | Lift | Desk</summary>
public static class WallShot
{
    static Transform Root()
    {
        var go = GameObject.Find("WallHabitatRoot");
        return go != null ? go.transform : null;
    }

    static string Aim(Vector3 pivot, Quaternion rot, float size)
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return "no scene view";
        sv.LookAt(pivot, rot, size, false, true);
        sv.Repaint();
        return "aimed at " + pivot.ToString("F2") + " size " + size;
    }

    public static string Front()
    {
        var r = Root(); if (r == null) return "no habitat";
        Vector3 c = r.position + r.up * 0.0f;
        return Aim(c, Quaternion.LookRotation(-r.forward, Vector3.up), 0.55f);
    }

    public static string ThreeQuarter()
    {
        var r = Root(); if (r == null) return "no habitat";
        Vector3 dir = (-r.forward + r.right * 0.7f + Vector3.down * 0.25f).normalized;
        return Aim(r.position, Quaternion.LookRotation(dir, Vector3.up), 0.55f);
    }

    public static string Lift()
    {
        var r = Root(); if (r == null) return "no habitat";
        Vector3 c = r.position + r.right * 0.40f;
        return Aim(c, Quaternion.LookRotation(-r.forward, Vector3.up), 0.3f);
    }

    public static string Desk()
    {
        var go = GameObject.Find("WorkspaceRoot");
        if (go == null) return "no workspace";
        var t = go.transform;
        Vector3 eye = t.TransformPoint(new Vector3(0, 0.30f, 0.62f));
        return Aim(t.position + t.up * 0.12f, Quaternion.LookRotation((t.position - eye).normalized, Vector3.up), 0.45f);
    }
}
