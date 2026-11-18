using System.Text;
using UnityEngine;
using MuseDesk.Wall;

/// <summary>Play Mode probes for rc25: the Screen panel's Open app / Close app key and taps on the agents' app.</summary>
public static class Rc25AppProbe
{
    static WallHabitatController C() => Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include);

    public static string Report()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C(); if (c == null) return "no WallHabitatController";
        var sb = new StringBuilder();
        sb.Append("screenOpen=").Append(c.ScreenOpen).Append(" frames=").Append(c.ScreenFrames).Append(" showsApp=").Append(c.ScreenShowsApp)
          .Append(" taps=").Append(c.AppTapsSent).Append(" caption='").Append(c.AppCaption).Append("' problem='").Append(c.ScreenProblem).Append("'");
        var key = GameObject.Find("Key_App");
        if (key != null)
        {
            var label = key.GetComponentInChildren<TMPro.TMP_Text>(true);
            sb.Append(" key='").Append(label != null ? label.text : "?").Append("' keyScale=").Append(key.transform.lossyScale.x.ToString("F4"))
              .Append(" keyPos=").Append(key.transform.position.ToString("F3"));
        }
        else sb.Append(" key=none");
        if (c.ScreenPanel != null) sb.Append(" panel=").Append(c.ScreenPanel.position.ToString("F3")).Append(" w=").Append(c.ScreenWidth.ToString("F2"));
        return sb.ToString();
    }

    public static string OpenScreen()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C(); if (c == null) return "no WallHabitatController";
        if (!c.ScreenOpen) c.PressScreen();
        return Report();
    }

    public static string PressApp()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C(); if (c == null) return "no WallHabitatController";
        c.PressApp();
        return "pressed " + Report();
    }

    public static string Tap(string uv)
    {
        if (!Application.isPlaying) return "not playing";
        var c = C(); if (c == null) return "no WallHabitatController";
        var parts = (uv ?? "0.5,0.5").Split(',');
        float u = 0.5f, v = 0.5f;
        if (parts.Length == 2) { float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out u);
                                 float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v); }
        bool ok = c.DriverTapApp(u, v);
        return "tap(" + u + "," + v + ")=" + ok + " " + Report();
    }

    /// <summary>Editor only: run the session's live handshake now (the Editor starts in the offline demo). Settings untouched.</summary>
    public static string GoLive()
    {
        if (!Application.isPlaying) return "not playing";
        var s = Object.FindAnyObjectByType<MuseDesk.Networking.SessionController>();
        if (s == null) return "no SessionController";
        if (s.IsLive) return "already live";
        var m = typeof(MuseDesk.Networking.SessionController).GetMethod("TryGoLiveAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (m == null) return "no TryGoLiveAsync";
        m.Invoke(s, new object[] { System.Threading.CancellationToken.None });
        return "handshake started";
    }

    public static string Live()
    {
        var s = Object.FindAnyObjectByType<MuseDesk.Networking.SessionController>();
        return s == null ? "no SessionController" : "isLive=" + s.IsLive;
    }

    /// <summary>__CineCam from the eye side, a little further back so the key and caption are in view.</summary>
    public static string Aim()
    {
        var c = C(); if (c == null || c.ScreenPanel == null) return "no panel";
        Transform t = c.ScreenPanel;
        var go = GameObject.Find("__CineCam") ?? new GameObject("__CineCam");
        var cam = go.GetComponent<Camera>(); if (cam == null) cam = go.AddComponent<Camera>();
        cam.enabled = true; cam.depth = 100f; cam.stereoTargetEye = StereoTargetEyeMask.None; cam.nearClipPlane = 0.01f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.2f, 0.22f, 0.25f);
        float d = 0.75f + c.ScreenWidth * 0.6f;
        cam.transform.position = t.position - t.forward * d;
        cam.transform.rotation = Quaternion.LookRotation(t.position - cam.transform.position, Vector3.up);
        cam.fieldOfView = 55f;
        return "aimed";
    }
}
