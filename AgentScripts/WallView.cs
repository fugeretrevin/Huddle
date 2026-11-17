using UnityEditor;
using UnityEngine;
using MuseDesk.Wall;

/// <summary>Aim the Scene view for a capture: from the user's eye at the habitat (Eye), close-up (Close), or at the desk area (Desk).</summary>
public static class WallView
{
    static WallHabitatController C => Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include);

    static string Aim(Vector3 from, Vector3 at, float size)
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return "no scene view";
        sv.orthographic = false;
        Quaternion rot = Quaternion.LookRotation(at - from, Vector3.up);
        sv.LookAt(at, rot, size, false, true);
        sv.Repaint();
        return "aimed " + from.ToString("F2") + " -> " + at.ToString("F2");
    }

    static Vector3 Eye() { var cam = Camera.main; return cam != null ? cam.transform.position : Vector3.zero; }

    public static string Eye_() { var r = C.transform.Find("WallHabitatRoot"); return Aim(Eye(), r.position + r.forward * 0.13f, 1.6f); }
    public static string Close() { var r = C.transform.Find("WallHabitatRoot"); return Aim(r.position + r.forward * 1.0f + Vector3.up * 0.1f, r.position + r.forward * 0.13f, 0.55f); }
    public static string Desk() { var rig = C.nearUserRig; Vector3 eye = rig.TransformPoint(new Vector3(0, 0.30f, 0.62f)); return Aim(eye, rig.TransformPoint(new Vector3(0, 0.05f, 0.40f)), 0.35f); }
}
