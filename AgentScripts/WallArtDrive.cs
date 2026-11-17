using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using MuseDesk.Wall;

/// <summary>
/// Play Mode helper for the Wall art hookup (Editor evidence only). Info() reports whether the habitat uses art, the art
/// hierarchy size and the elevator controller state; Shot("name.png;ex,ey,ez;tx,ty,tz;fov") renders the habitat from a
/// temporary hidden camera at habitat-local coordinates to docs/review_screenshots/wall/.
/// </summary>
public static class WallArtDrive
{
    static Transform Habitat()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if ((t.name == "WallHabitat_Full" || t.name == "WallHabitat_Compact") && t.gameObject.activeInHierarchy) return t;
        return null;
    }

    public static string Info()
    {
        var h = Habitat();
        if (h == null) return "no habitat (Wall not active?)";
        var sb = new StringBuilder();
        sb.AppendLine("habitat " + h.name + " world " + h.position.ToString("F3") + " scale " + h.lossyScale.x.ToString("F3"));
        foreach (Transform c in h.transform)
            sb.AppendLine("  child " + c.name + " active=" + c.gameObject.activeSelf + " renderers=" + c.GetComponentsInChildren<Renderer>(false).Length);
        var lift = h.GetComponentInChildren<MuseDesk.Art.WallHabitatElevatorVisualController>(true);
        sb.AppendLine("art elevator controller: " + (lift != null) + (lift != null ? " pivotLocalY=" + (lift.ElevatorCarPivot != null ? lift.ElevatorCarPivot.localPosition.y.ToString("F3") : "null") +
            " spineAnim=" + (lift.SpineAnimator != null) + " cabAnim=" + (lift.CabAnimator != null) : ""));
        var cab = h.Find("ElevatorCabin");
        if (cab != null) sb.AppendLine("proxy cabin localY " + cab.localPosition.y.ToString("F3") + " renderersEnabled=" + cab.GetComponentsInChildren<Renderer>(false).Count(r => r.enabled));
        foreach (var a in h.GetComponentsInChildren<Animator>(false))
            if (a.name.StartsWith("Avatar_") || a.transform.root != null && a.transform.parent != null && a.transform.parent.name.StartsWith("Avatar_")) { }
        foreach (Transform c in h.transform)
            if (c.name.StartsWith("Avatar_")) sb.AppendLine("  " + c.name + " local " + c.localPosition.ToString("F3") + " scale " + c.localScale.x.ToString("F3"));
        return sb.ToString();
    }

    public static string Shot(string spec)
    {
        var h = Habitat();
        if (h == null) return "no habitat";
        var f = spec.Split(';');
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        Vector3 V(string s) { var p = s.Split(','); return new Vector3(float.Parse(p[0], ci), float.Parse(p[1], ci), float.Parse(p[2], ci)); }
        string path = "docs/review_screenshots/wall/" + f[0];
        var go = new GameObject("WallArtTempCam") { hideFlags = HideFlags.HideAndDontSave };
        RenderTexture rt = null; Texture2D tex = null;
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 6f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.20f, 0.22f, 0.26f);
            cam.fieldOfView = float.Parse(f[3], ci);
            Vector3 eye = h.TransformPoint(V(f[1])), target = h.TransformPoint(V(f[2]));
            go.transform.position = eye;
            go.transform.rotation = Quaternion.LookRotation(target - eye, h.up);
            rt = new RenderTexture(1400, 900, 24); cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(1400, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1400, 900), 0, 0); tex.Apply();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null; cam.targetTexture = null;
        }
        finally
        {
            if (rt != null) Object.DestroyImmediate(rt);
            if (tex != null) Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
        }
        return "saved " + path;
    }
}
