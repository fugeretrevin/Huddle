using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Config;
using MuseDesk.Motion;
using MuseDesk.Workspace;

/// <summary>
/// Play Mode driver for the Polish S9c "Calm motion" key. The flag is forced on through a RUNTIME COPY of the release
/// config (the asset is never touched, so the committed flag stays 0). Always finish with Cleanup(): it removes the saved
/// per-device choice this driver made and puts the original config back.
///
///   scripts\drv.ps1 MotionToggleDrive State
///   scripts\drv.ps1 MotionToggleDrive FlagOn         key appears (shown scale), label Calm / motion / OFF
///   scripts\drv.ps1 MotionToggleDrive Press          one press: flips MotionSettings.ReducedMotion, saves it, label ON
///   scripts\drv.ps1 MotionToggleDrive Press          again: back to OFF
///   scripts\drv.ps1 MotionToggleDrive Hover          shows the key's hover state (KeyStateVisual.SimulateState)
///   scripts\drv.ps1 MotionToggleDrive Cleanup
/// </summary>
public static class MotionToggleDrive
{
    static MotionToggleKey Key() => Object.FindFirstObjectByType<MotionToggleKey>(FindObjectsInactive.Include);

    public static string State()
    {
        if (!Application.isPlaying) return "not playing";
        var k = Key();
        var a = ReleaseConfigApplier.Active;
        var sb = new StringBuilder();
        sb.Append("flag=" + (a != null && a.reducedMotionToggle) + " reducedMotion=" + MotionSettings.ReducedMotion
                  + " saved=" + PlayerPrefs.GetInt(ReducedMotionPreference.Key, -1));
        if (k == null) return sb.Append(" | NO MotionToggleKey in the scene").ToString();
        var root = k.keyRoot != null ? k.keyRoot : k.transform;
        var cap = k.cap != null ? k.cap.sharedMaterial : null;
        var focus = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == "FocusToggle");
        sb.Append(" | key shown=" + k.Shown + " scale=" + root.localScale.x.ToString("F5") + " label='" + (k.label != null ? k.label.text.Replace("\n", "/") : "?")
                  + "' cap=" + (cap != null ? cap.name : "?") + " capColour=" + (k.visual != null ? ColorUtility.ToHtmlStringRGB(k.visual.BaseColor) : "?")
                  + " active=" + k.gameObject.activeInHierarchy + " pos=" + root.localPosition.ToString("F3"));
        if (focus != null) sb.Append(" | FocusToggle localPos=" + focus.localPosition.ToString("F3") + " scale=" + focus.localScale.x.ToString("F5") + " parent=" + focus.parent.name);
        return sb.ToString();
    }

    /// <summary>Applies a runtime copy of the active config with reducedMotionToggle = true.</summary>
    public static string FlagOn()
    {
        if (!Application.isPlaying) return "not playing";
        // Statics do not survive from one `script run` to the next: always work from the asset (never modified) and a fresh copy.
        var asset = MuseDeskReleaseConfig.Load();
        if (asset == null) return "no ReleaseConfig asset";
        var copy = Object.Instantiate(asset);
        copy.reducedMotionToggle = true;
        ReleaseConfigApplier.Apply(copy);
        return State();
    }

    /// <summary>The release as committed: the asset's own flags again (flag off).</summary>
    public static string FlagOff()
    {
        if (!Application.isPlaying) return "not playing";
        var asset = MuseDeskReleaseConfig.Load();
        if (asset != null) ReleaseConfigApplier.Apply(asset);
        return State();
    }

    /// <summary>The press a finger makes (the key's own handler).</summary>
    public static string Press()
    {
        if (!Application.isPlaying) return "not playing";
        var k = Key();
        if (k == null) return "no MotionToggleKey";
        bool before = MotionSettings.ReducedMotion;
        k.Press();
        return "press: " + before + " -> " + MotionSettings.ReducedMotion + " | " + State();
    }

    public static string Hover()
    {
        var k = Key();
        if (k == null || k.visual == null) return "no key / visual";
        k.visual.SimulateState(Oculus.Interaction.InteractableState.Hover);
        return "hover shown; cap colour now " + ColorUtility.ToHtmlStringRGB(k.visual.CurrentColor);
    }

    /// <summary>Frames the Scene view on the key and its neighbours from the front (the key's face looks toward -Z local),
    /// for a screenshot. Moves only the Scene view camera. Arg-less: distance 0.28 m, tilted 15 degrees down.</summary>
    public static string View()
    {
        var k = Key();
        if (k == null) return "no MotionToggleKey";
        var sv = UnityEditor.SceneView.lastActiveSceneView;
        if (sv == null) return "no scene view";
        var t = k.keyRoot != null ? k.keyRoot : k.transform;
        var focus = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.name == "FocusToggle");
        Vector3 target = focus != null ? (t.position + focus.position) * 0.5f : t.position;
        var cam = Camera.main;
        Vector3 dir = cam != null ? (cam.transform.position - target).normalized : (-t.forward + Vector3.up * 0.25f).normalized;   // from the user's seat
        Vector3 eye = target + dir * 0.30f;
        sv.orthographic = false;
        sv.cameraSettings.fieldOfView = 60f;
        sv.LookAt(target, Quaternion.LookRotation(target - eye, Vector3.up), 0.30f * Mathf.Sin(30f * Mathf.Deg2Rad), false, true);
        sv.Repaint();
        return "eye=" + eye.ToString("F3") + " target=" + target.ToString("F3");
    }

    /// <summary>Renders a close-up of the Focus key and the Calm motion key with a TEMPORARY camera (destroyed at once; driver only,
    /// nothing in the build) to a PNG. Arg: output path.</summary>
    public static string Snap(string path)
    {
        var k = Key();
        if (k == null) return "no MotionToggleKey";
        var t = k.keyRoot != null ? k.keyRoot : k.transform;
        var focus = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.name == "FocusToggle");
        Vector3 target = focus != null ? (t.position + focus.position) * 0.5f : t.position;
        var cam0 = Camera.main;
        Vector3 from = cam0 != null ? (cam0.transform.position - target).normalized : -t.forward;
        // The key faces -Z local: view it from the side its face looks toward.
        Vector3 face = -t.forward;
        if (Vector3.Dot(from, face) < 0.3f) from = (face + Vector3.up * 0.2f).normalized;
        var go = new GameObject("S9cTempCam");
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 40f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.18f, 0.2f, 0.24f);
            go.transform.position = target + from * 0.30f;
            go.transform.rotation = Quaternion.LookRotation(target - go.transform.position, Vector3.up);
            var rt = new RenderTexture(900, 600, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(900, 600, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 900, 600), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null; cam.targetTexture = null;
            Object.Destroy(rt); Object.Destroy(tex);
        }
        finally { Object.Destroy(go); }
        return "saved " + path + " camPos=" + (target + from * 0.30f).ToString("F3");
    }

    public static string Cleanup()
    {
        if (!Application.isPlaying) { PlayerPrefs.DeleteKey(ReducedMotionPreference.Key); return "not playing; saved choice removed"; }
        PlayerPrefs.DeleteKey(ReducedMotionPreference.Key);
        PlayerPrefs.Save();
        var asset = MuseDeskReleaseConfig.Load();
        if (asset != null) ReleaseConfigApplier.Apply(asset);
        return "cleaned: " + State();
    }
}
