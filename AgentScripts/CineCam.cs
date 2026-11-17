using System.Threading.Tasks;
using UnityEngine;
using MuseDesk.Wall;
using MuseDesk.Spatial.Wall;
using MuseDesk.Workspace;

/// <summary>
/// Capture-only cinematic camera for in-engine film shots (FINAL_CAPTURE_INSTRUCTIONS.md §B). Editor Play Mode only: it lives in
/// AgentScripts (never in the APK), creates its own "__CineCam" at run time and never touches the XR rig or the main camera. It
/// renders above the XR camera in the Game view (depth 100), so the Game view can be recorded at 16:9.
///   CineCam.Shot  '["wall_wide"]'                 desk_wide | desk_agents | wall_wide | wall_manager | elevator | printer | portal_side
///   CineCam.Dolly '["wall_wide","wall_manager",8]'   a slow eased move between two presets (seconds)
///   CineCam.Off                                    remove it
/// The scene state it films is whatever the run really reached (offline SCRIPTED DEMO or a live session).
/// </summary>
public static class CineCam
{
    const string Name = "__CineCam";
    static int _dollyId;

    static Transform Find(string name)
    {
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            if (t.name == name && t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy) return t;
        return null;
    }

    static Camera Cam()
    {
        // No "??" here: Unity's missing-component placeholder is not C# null.
        var go = GameObject.Find(Name);
        if (go == null) go = new GameObject(Name);
        var cam = go.GetComponent<Camera>();
        if (cam == null) cam = go.AddComponent<Camera>();
        cam.enabled = true;
        cam.depth = 100f;                                   // above the XR camera in the Game view only
        cam.stereoTargetEye = StereoTargetEyeMask.None;     // never drawn into the headset
        cam.nearClipPlane = 0.01f; cam.farClipPlane = 30f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.20f, 0.22f, 0.25f);
        return cam;
    }

    /// <summary>Where a preset puts the camera (pos, look-at, field of view). False when its subject is not in the scene.</summary>
    static bool Preset(string name, out Vector3 pos, out Vector3 look, out float fov)
    {
        pos = look = Vector3.zero; fov = 40f;
        Transform ws = Find("WorkspaceRoot"), wall = Find("WallHabitatRoot");
        var c = Object.FindAnyObjectByType<WallHabitatController>();
        Vector3 at;
        switch (name)
        {
            case "desk_wide":
                if (ws == null) return false;
                look = ws.TransformPoint(new Vector3(0f, 0.08f, 0.02f)); pos = ws.TransformPoint(new Vector3(0.05f, 0.34f, 0.70f)); fov = 50f; return true;
            case "desk_agents":      // Developer + Tester two-shot from just above the desk front
                if (ws == null) return false;
                look = ws.TransformPoint(new Vector3(0.24f, 0.12f, 0.06f)); pos = ws.TransformPoint(new Vector3(0.10f, 0.20f, 0.46f)); fov = 46f; return true;
            case "wall_wide":
                if (wall == null) return false;
                look = wall.position; pos = look + wall.forward * 1.35f + Vector3.up * 0.05f; fov = 42f; return true;
            case "wall_manager":
                if (wall == null || c == null || !c.TryGetAvatarCenter(HabitatRole.Manager, out at)) return false;
                look = at; pos = at + wall.forward * 0.42f + wall.right * 0.10f + Vector3.up * 0.03f; fov = 38f; return true;
            case "elevator":
                if (wall == null) return false;
                // The shaft sits in the left third with the floors beside it (no empty Editor background in frame).
                look = wall.position + wall.right * 0.26f; pos = look + wall.forward * 0.95f - wall.right * 0.12f + Vector3.up * 0.02f; fov = 40f; return true;
            case "printer":
                if (wall == null || c == null || c.View == null) return false;
                look = c.View.SocketWorld("TaskPrinterSocket", new Vector3(0f, 0.02f, 0f));
                pos = look + wall.forward * 0.34f + wall.right * 0.06f + Vector3.up * 0.06f; fov = 36f; return true;
            case "portal_side":
                if (c == null || c.Portal == null || c.Portal.State == PortalState.Hidden) return false;
                Vector3 spot = c.Portal.Spot.position;
                Camera main = Camera.main;
                Vector3 toUser = main != null ? main.transform.position - spot : Vector3.forward; toUser.y = 0f; toUser.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, toUser);
                look = spot + Vector3.up * 0.62f; pos = spot + side * 2.1f + toUser * 0.7f + Vector3.up * 1.0f; fov = 42f; return true;
        }
        return false;
    }

    public static string Shot(string preset)
    {
        if (!Application.isPlaying) return "enter Play Mode first";
        Vector3 pos, look; float fov;
        if (!Preset(preset, out pos, out look, out fov)) return "nothing to frame for " + preset + " (is that part of the scene active?)";
        _dollyId++;                                          // stops a running dolly
        var cam = Cam();
        cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
        cam.fieldOfView = fov;
        PlatesFace(cam.transform.position);
        return "CineCam on " + preset;
    }

    public static async Task<string> Dolly(string from, string to, float seconds)
    {
        if (!Application.isPlaying) return "enter Play Mode first";
        Vector3 p0, l0, p1, l1; float f0, f1;
        if (!Preset(from, out p0, out l0, out f0) || !Preset(to, out p1, out l1, out f1)) return "a preset has nothing to frame";
        int id = ++_dollyId;
        var cam = Cam();
        float t0 = Time.realtimeSinceStartup, dur = Mathf.Max(1f, seconds);
        while (id == _dollyId && Application.isPlaying)
        {
            float u = Mathf.Clamp01((Time.realtimeSinceStartup - t0) / dur);
            float e = u * u * (3f - 2f * u);                   // ease in/out
            Vector3 p = Vector3.Lerp(p0, p1, e), l = Vector3.Lerp(l0, l1, e);
            if (cam == null) return "camera removed";
            cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(l - p, Vector3.up));
            cam.fieldOfView = Mathf.Lerp(f0, f1, e);
            PlatesFace(p);
            if (u >= 1f) return "dolly " + from + " -> " + to + " done";
            await Task.Yield();
        }
        return "dolly stopped";
    }

    /// <summary>Desk nameplates turn toward the viewer (AgentNameplate). In the Editor the "viewer" is the headset's default pose,
    /// so while filming they are turned to the film camera instead, with the same clamped tilt; Off hands them back.</summary>
    static void PlatesFace(Vector3 viewer)
    {
        foreach (var plate in Object.FindObjectsByType<AgentNameplate>(FindObjectsInactive.Exclude))
        {
            plate.faceViewer = false;
            Transform t = plate.transform, parent = t.parent;
            Vector3 to = viewer - t.position;
            if (parent != null) to = parent.InverseTransformDirection(to);
            Quaternion rest = Quaternion.Euler(0f, t.localEulerAngles.y, 0f);
            t.localRotation = AgentNameplate.TiltToward(rest, to, 180f, plate.maxPitch);
        }
    }

    public static string Off()
    {
        _dollyId++;
        foreach (var plate in Object.FindObjectsByType<AgentNameplate>(FindObjectsInactive.Exclude)) plate.faceViewer = true;
        var go = GameObject.Find(Name);
        if (go != null) Object.Destroy(go);
        return "CineCam off";
    }
}
