using System.IO;
using UnityEngine;

/// <summary>Render a PNG from any point to any point in a named root's local space (Edit or Play Mode).
///   unity command script run --file AgentScripts/Snap.cs --entry Snap.At --args '["WorkspaceRoot","name",px,py,pz,lx,ly,lz,fov]'</summary>
public static class Snap
{
    public static string At(string root, string name, float px, float py, float pz, float lx, float ly, float lz, float fov)
    {
        Transform r = null;
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            if (t.name == root && t.gameObject.scene.IsValid()) { r = t; break; }
        if (r == null) return "no " + root;
        var go = new GameObject("__SnapCam");
        var cam = go.AddComponent<Camera>();
        Vector3 pos = r.TransformPoint(new Vector3(px, py, pz)), look = r.TransformPoint(new Vector3(lx, ly, lz));
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.005f; cam.farClipPlane = 30f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.33f, 0.34f, 0.36f);
        var rt = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 800, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); tex.Apply();
        RenderTexture.active = null;
        string dir = Path.Combine(Path.GetTempPath(), "muse_shots"); Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, name + ".png");
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
        return "saved " + file;
    }
}
