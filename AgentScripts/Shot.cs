using System.IO;
using UnityEngine;

/// <summary>
/// Render a PNG from a temporary camera (Play Mode or Edit Mode). Targets: "wall" (front of the placed habitat),
/// "wall34" (three-quarter), "lift" (elevator close-up), "desk" (the seated user's eye on the desk office), "dock"
/// (the near-user mode console), "user" (what a seated user facing forward sees). Writes to %TEMP%\muse_shots\<name>.png.
///   scripts\drv.ps1 Shot Take wall
/// </summary>
public static class Shot
{
    /// <summary>Position a persistent "__ShotCam" (disabled, so it never draws to the screen) for
    /// `unity command capture game_view --camera __ShotCam --output-path ...`. URP ignores Camera.Render() here.</summary>
    public static string Aim(string target)
    {
        Vector3 pos, look; float fov = 40f;
        if (!Frame(target, out pos, out look, ref fov)) return "nothing to frame for " + target;
        var go = GameObject.Find("__ShotCam");
        if (go == null) go = new GameObject("__ShotCam");
        var cam = go.GetComponent<Camera>();
        if (cam == null) cam = go.AddComponent<Camera>();
        cam.enabled = true; cam.depth = -100f;   // under the XR camera: only the off-screen capture uses it
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.01f; cam.farClipPlane = 30f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.33f, 0.34f, 0.36f);
        return "aimed __ShotCam at " + target;
    }

    public static string Take(string target)
    {
        Vector3 pos, look; float fov = 40f;
        if (!Frame(target, out pos, out look, ref fov)) return "nothing to frame for " + target;
        var go = new GameObject("__ShotCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.01f; cam.farClipPlane = 30f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.33f, 0.34f, 0.36f);
        var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); tex.Apply();
        RenderTexture.active = null;
        string dir = Path.Combine(Path.GetTempPath(), "muse_shots"); Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, target + ".png");
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
        return "saved " + file;
    }

    static bool Frame(string target, out Vector3 pos, out Vector3 look, ref float fov)
    {
        pos = look = Vector3.zero;
        Transform ws = Find("WorkspaceRoot"), wall = Find("WallHabitatRoot"), dock = Find("ModeConsole");
        Camera main = Camera.main;
        Vector3 eye = main != null ? main.transform.position : (ws != null ? ws.TransformPoint(new Vector3(0, 0.30f, 0.62f)) : Vector3.up);
        switch (target)
        {
            case "wall":
                if (wall == null || !wall.gameObject.activeInHierarchy) return false;
                look = wall.position + wall.up * 0.0f; pos = look + wall.forward * 1.25f; return true;
            case "wall34":
                if (wall == null || !wall.gameObject.activeInHierarchy) return false;
                look = wall.position; pos = look + (wall.forward * 1.0f + wall.right * 0.65f + Vector3.up * 0.15f); return true;
            case "lift":
                if (wall == null || !wall.gameObject.activeInHierarchy) return false;
                look = wall.position + wall.right * 0.42f; pos = look + wall.forward * 0.7f; fov = 35f; return true;
            case "desk":
                if (ws == null) return false;
                look = ws.TransformPoint(new Vector3(0, 0.08f, 0f)); pos = ws.TransformPoint(new Vector3(0, 0.30f, 0.62f)); fov = 55f; return true;
            case "dock":
                if (dock == null) return false;
                look = dock.position; pos = eye; fov = 45f; return true;
            case "pod":     // the office wherever it is (lowered ceiling pod), from the user's eye
                if (ws == null) return false;
                look = ws.TransformPoint(new Vector3(0, 0.05f, 0f)); pos = eye; fov = 50f; return true;
            case "podtop":  // the same office from above and in front, to see its deck layout
                if (ws == null) return false;
                look = ws.TransformPoint(new Vector3(0, 0.0f, 0f)); pos = ws.TransformPoint(new Vector3(0, 0.75f, 0.55f)); fov = 50f; return true;
            case "user":
                look = eye + (main != null ? main.transform.forward : Vector3.forward); pos = eye; fov = 80f; return true;
        }
        return false;
    }

    static Transform Find(string name)
    {
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            if (t.name == name && t.gameObject.scene.IsValid()) return t;
        return null;
    }
}
