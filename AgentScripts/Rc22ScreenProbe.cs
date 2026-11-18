using System.Text;
using UnityEngine;
using MuseDesk.Wall;

/// <summary>Play Mode probes for the PC Screen panel (rc22): upright panel + corner resize handle.</summary>
public static class Rc22ScreenProbe
{
    static WallHabitatController C() => Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include);

    public static string Open()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C(); if (c == null) return "no WallHabitatController";
        if (!c.ScreenOpen) c.PressScreen();
        return "open=" + c.ScreenOpen + " " + Report();
    }

    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C(); if (c == null) return "no WallHabitatController";
        var p = c.ScreenPanel;
        if (p == null) return "no panel (call Open)";
        var e = p.eulerAngles;
        var sb = new StringBuilder();
        sb.Append("open=").Append(c.ScreenOpen).Append(" width=").Append(c.ScreenWidth.ToString("F3"));
        sb.Append(" euler=").Append(e.ToString("F2")).Append(" (pitch ").Append(e.x.ToString("F2")).Append(", roll ").Append(e.z.ToString("F2")).Append(")");
        sb.Append(" pos=").Append(p.position.ToString("F3"));
        var g = p.Find("GrabHandle"); var s = p.Find("SizeHandle");
        if (g != null) sb.Append(" grab=").Append(g.position.ToString("F3"));
        if (s != null) sb.Append(" size=").Append(s.position.ToString("F3"));
        return sb.ToString();
    }

    public static string DragUp()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C(); if (c == null || !c.ScreenOpen) return "panel not open (call Open)";
        var cam = Camera.main;
        Vector3 toUser = cam != null ? cam.transform.position - c.ScreenPanel.position : Vector3.zero;
        toUser.y = 0f;
        Vector3 d = Vector3.up * 0.3f + (toUser.sqrMagnitude > 1e-6f ? toUser.normalized * 0.3f : Vector3.zero);
        bool ok = c.DriverDragScreen(d);
        return "drag=" + ok + " " + Report();
    }

    public static string Resize()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C(); if (c == null) return "no WallHabitatController";
        bool ok = c.DriverResizeScreen(1.1f);
        return "resize=" + ok + " " + Report();
    }

    public static string Shot()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C(); if (c == null || c.ScreenPanel == null) return "panel not open (call Open)";
        var cam = Camera.main;
        var go = GameObject.Find("__CineCam");
        if (go == null) { go = new GameObject("__CineCam"); go.AddComponent<Camera>(); }
        var cc = go.GetComponent<Camera>();
        Vector3 eye = cam != null ? cam.transform.position : c.ScreenPanel.position + Vector3.back;
        go.transform.position = eye;
        go.transform.rotation = Quaternion.LookRotation((c.ScreenPanel.position - eye).normalized, Vector3.up);
        cc.fieldOfView = 60f; cc.nearClipPlane = 0.05f;
        return "cine at " + eye.ToString("F2") + " " + Report();
    }
}
