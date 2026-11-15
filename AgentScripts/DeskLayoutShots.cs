using System.Linq;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>
/// Editor captures of the desk office for the layout doc (docs/Polish/DESK_CHOREOGRAPHY_LAYOUT.md). A TEMPORARY camera
/// (hidden, destroyed at once, never saved) renders WorkspaceRoot to PNG. Works in Edit Mode and Play Mode. These are
/// Editor renders on a flat background: never headset evidence.
///
///   Snap("before")  ->  docs/review_screenshots/polish/desk_layout_{tag}_plan.png   straight down, user at the bottom
///                       docs/review_screenshots/polish/desk_layout_{tag}_seat.png   from the seated eye (root-local 0, 0.30, 0.62)
///                       docs/review_screenshots/polish/desk_layout_{tag}_right.png  three-quarter view from the user's right
/// </summary>
public static class DeskLayoutShots
{
    const string Dir = "docs/review_screenshots/polish/";

    static Transform Root() =>
        EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "WorkspaceRoot");

    public static string Snap(string tag)
    {
        var root = Root();
        if (root == null) return "no WorkspaceRoot";
        if (string.IsNullOrEmpty(tag)) tag = "shot";
        string a = One(root, Dir + "desk_layout_" + tag + "_plan.png", new Vector3(0f, 1.25f, 0.12f), new Vector3(0f, 0f, 0.12f), -root.forward, true, 0.40f);
        string b = One(root, Dir + "desk_layout_" + tag + "_seat.png", new Vector3(0f, 0.30f, 0.62f), new Vector3(0f, 0.04f, 0.10f), Vector3.up, false, 62f);
        string c = One(root, Dir + "desk_layout_" + tag + "_right.png", new Vector3(-0.62f, 0.42f, 0.56f), new Vector3(-0.05f, 0.04f, 0.08f), Vector3.up, false, 50f);
        return a + " | " + b + " | " + c;
    }

    /// <summary>One picture from a chosen camera: "tag;eyeX,eyeY,eyeZ;targetX,targetY,targetZ;fov" (root-local metres), saved as
    /// docs/review_screenshots/polish/desk_layout_{tag}.png.</summary>
    public static string SnapAt(string spec)
    {
        var root = Root();
        if (root == null) return "no WorkspaceRoot";
        var f = (spec ?? "").Split(';');
        if (f.Length < 3) return "use \"tag;ex,ey,ez;tx,ty,tz;fov\"";
        Vector3 V(string s) { var p = s.Split(','); var c = System.Globalization.CultureInfo.InvariantCulture; return new Vector3(float.Parse(p[0], c), float.Parse(p[1], c), float.Parse(p[2], c)); }
        float fov = f.Length > 3 ? float.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture) : 40f;
        return One(root, Dir + "desk_layout_" + f[0] + ".png", V(f[1]), V(f[2]), Vector3.up, false, fov);
    }

    static string One(Transform root, string path, Vector3 eyeLocal, Vector3 targetLocal, Vector3 up, bool ortho, float sizeOrFov)
    {
        var go = new GameObject("DeskLayoutTempCam") { hideFlags = HideFlags.HideAndDontSave };
        RenderTexture rt = null; Texture2D tex = null;
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 6f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.20f, 0.22f, 0.26f);
            cam.orthographic = ortho;
            if (ortho) cam.orthographicSize = sizeOrFov; else cam.fieldOfView = sizeOrFov;
            Vector3 eye = root.TransformPoint(eyeLocal), target = root.TransformPoint(targetLocal);
            go.transform.position = eye;
            go.transform.rotation = Quaternion.LookRotation(target - eye, ortho ? root.TransformDirection(up) : up);
            int w = 1400, h = ortho ? 900 : 900;
            rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
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
