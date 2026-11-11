using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;

/// <summary>
/// Settle the TMP readable-face question with ZERO ambiguity: spawn our own asymmetric text at
/// identity rotation (faces world +Z) and photograph it from both sides. No headset needed.
/// </summary>
public static class MirrorTest
{
    static Camera MakeCam()
    {
        var existing = GameObject.Find("TempMirrorCam");
        if (existing != null) Object.DestroyImmediate(existing);
        var go = new GameObject("TempMirrorCam");
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
        cam.fieldOfView = 45f;
        cam.nearClipPlane = 0.01f;
        return cam;
    }

    static TextMeshPro MakeText()
    {
        var existing = GameObject.Find("TempMirrorText");
        if (existing != null) Object.DestroyImmediate(existing);
        var go = new GameObject("TempMirrorText");
        go.transform.position = new Vector3(0f, 1.3f, 0.5f);
        go.transform.rotation = Quaternion.identity; // forward = world +Z
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = "Rabcdef";    // asymmetric string; mirror is obvious if letters reverse
        tmp.fontSize = 1.2f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        var rt = go.GetComponent<RectTransform>();
        if (rt != null) rt.sizeDelta = new Vector2(1.2f, 0.3f);
        return tmp;
    }

    // Camera on +Z side (in front of where transform.forward points), looking back at text.
    public static string PlusZ()
    {
        var tmp = MakeText();
        var t = tmp.transform;
        var cam = MakeCam();
        Vector3 pos = t.position + t.forward * 1.2f;
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.LookRotation(t.position - pos, Vector3.up);
        return $"text at {t.position} forward={t.forward}; cam on +Z side at {pos}";
    }

    // Camera on -Z side.
    public static string MinusZ()
    {
        var t = GameObject.Find("TempMirrorText")?.transform;
        if (t == null) { MakeText(); t = GameObject.Find("TempMirrorText").transform; }
        var cam = MakeCam();
        Vector3 pos = t.position - t.forward * 0.4f;
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.LookRotation(t.position - pos, Vector3.up);
        return $"cam on -Z side at {pos}";
    }

    public static string Cleanup()
    {
        foreach (var n in new[] { "TempMirrorCam", "TempMirrorText" })
        {
            var g = GameObject.Find(n);
            if (g != null) Object.DestroyImmediate(g);
        }
        return "cleaned up";
    }
}
