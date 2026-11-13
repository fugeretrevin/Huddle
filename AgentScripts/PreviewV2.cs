using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>Editor-only previews of redesigned states. Never saves; call Reload to discard.</summary>
public static class PreviewV2
{
    static Transform Find(string n) => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == n);

    static void Refresh()
    {
        foreach (var t in EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TMP_Text>(false)))
            t.ForceMeshUpdate(true, true);
        SceneView.RepaintAll();
    }

    static string Paper(bool cover, bool details, bool outcome, float scale, Vector3 rootLocal)
    {
        var d = Find("DecisionDocument"); if (d == null) return "no DecisionDocument";
        var ws = Find("WorkspaceRoot");
        d.gameObject.SetActive(true);
        foreach (Transform c in d)
        {
            bool on;
            switch (c.name)
            {
                case "Cover": on = cover; break;
                case "Pending": on = false; break;
                case "Outcome": on = outcome; break;
                case "Details": on = details && !cover; break;
                case "Actions": on = !cover && !outcome; break;
                case "StampZone": on = !cover; break;
                case "HoverFrame": on = false; break;
                case "Card": on = true; break;
                default: on = !cover; break;
            }
            c.gameObject.SetActive(on);
        }
        foreach (var mb in d.GetComponents<MonoBehaviour>()) if (mb != null && mb.GetType().Name == "FaceUser") mb.enabled = false;
        Vector3 eye = ws.TransformPoint(new Vector3(0, 0.30f, 0.62f));
        Vector3 pos = ws.TransformPoint(rootLocal);
        d.SetParent(null, true);
        d.position = pos;
        d.rotation = Quaternion.LookRotation(pos - eye, Vector3.up);
        d.localScale = Vector3.one * scale;
        Refresh();
        var sv = SceneView.lastActiveSceneView;
        sv.orthographic = false; sv.cameraSettings.fieldOfView = 70f;
        float dist = (pos - eye).magnitude;
        sv.LookAtDirect(pos, Quaternion.LookRotation(pos - eye, Vector3.up), dist * Mathf.Sin(35f * Mathf.Deg2Rad));
        sv.Repaint();
        return "paper preview (do not save)";
    }

    public static string PaperExpanded() => Paper(false, false, false, 1f, new Vector3(0, 0.30f, 0.17f));
    public static string PaperDetails() => Paper(false, true, false, 1f, new Vector3(0, 0.36f, 0.17f));
    public static string PaperOutcome() => Paper(false, false, true, 1f, new Vector3(0, 0.30f, 0.17f));
    public static string PaperCarried() => Paper(true, false, false, 0.22f, new Vector3(-0.17f, 0.20f, 0.25f));

    /// <summary>Looks from the seated eye at a root-local point with a given FOV.</summary>
    static string Look(Vector3 rootLocal, float fov)
    {
        var ws = Find("WorkspaceRoot");
        Vector3 eye = ws.TransformPoint(new Vector3(0, 0.30f, 0.62f));
        Vector3 pos = ws.TransformPoint(rootLocal);
        var sv = SceneView.lastActiveSceneView;
        sv.orthographic = false; sv.cameraSettings.fieldOfView = fov;
        float dist = (pos - eye).magnitude;
        sv.LookAtDirect(pos, Quaternion.LookRotation(pos - eye, Vector3.up), dist * Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad));
        Refresh();
        return "ok";
    }
    /// <summary>Looks from the seated eye at a named object (works in Play Mode too).</summary>
    static string LookAt(string name, float fov, Vector3 worldOffset = default)
    {
        Transform t = null;
        foreach (var x in Resources.FindObjectsOfTypeAll<Transform>())
            if (x.name == name && x.gameObject.scene.IsValid()) { t = x; break; }
        if (t == null) return "not found: " + name;
        var ws = Find("WorkspaceRoot");
        if (ws == null) foreach (var x in Resources.FindObjectsOfTypeAll<Transform>()) if (x.name == "WorkspaceRoot" && x.gameObject.scene.IsValid()) { ws = x; break; }
        return Look(ws.InverseTransformPoint(t.position + worldOffset), fov);
    }
    public static string AtPaper() => LookAt("DecisionDocument", 60f);
    public static string AtPaperLow() => LookAt("DecisionDocument", 75f, new Vector3(0, -0.07f, 0));
    public static string AtViewer() => LookAt("ViewerSheetRoot", 60f);
    public static string AtReceipt() => LookAt("CompletionReceipt", 45f);
    public static string AtArchive() => LookAt("ArchiveCard", 45f);
    public static string AtBoard() => LookAt("TaskBoard", 75f);
    public static string AtDock() => LookAt("TaskDock", 45f);
    static Transform Any(string name)
    {
        foreach (var x in Resources.FindObjectsOfTypeAll<Transform>()) if (x.name == name && x.gameObject.scene.IsValid()) return x;
        return null;
    }
    static string Cam(Vector3 from, Vector3 at, float fov)
    {
        var sv = SceneView.lastActiveSceneView;
        sv.orthographic = false; sv.cameraSettings.fieldOfView = fov;
        float dist = (at - from).magnitude;
        sv.LookAtDirect(at, Quaternion.LookRotation(at - from, Vector3.up), dist * Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad));
        Refresh();
        return "ok";
    }
    /// <summary>From the real head (Camera.main), looking at the office wherever it is.</summary>
    public static string HeadAtOffice()
    {
        var ws = Any("WorkspaceRoot"); var cam = Camera.main; if (ws == null || cam == null) return "no root or camera";
        return Cam(cam.transform.position, ws.position + Vector3.up * 0.12f, 50f);
    }
    public static string HeadAtOfficeClose()
    {
        var ws = Any("WorkspaceRoot"); var cam = Camera.main; if (ws == null || cam == null) return "no root or camera";
        Vector3 at = ws.position + Vector3.up * 0.12f; Vector3 from = Vector3.Lerp(cam.transform.position, at, 0.45f);
        return Cam(from, at, 55f);
    }
    /// <summary>From the head, looking where the desk office normally is (the user's own controls).</summary>
    public static string HeadForwardDown()
    {
        var cam = Camera.main; if (cam == null) return "no camera";
        Vector3 f = cam.transform.forward; f.y = 0; f.Normalize();
        return Cam(cam.transform.position, cam.transform.position + f * 0.50f + Vector3.down * 0.28f, 90f);
    }
    /// <summary>Third-person view of the room: behind and above the user, seeing them and the office.</summary>
    public static string RoomView()
    {
        var ws = Any("WorkspaceRoot"); var cam = Camera.main; if (ws == null || cam == null) return "no root or camera";
        Vector3 head = cam.transform.position; Vector3 mid = (head + ws.position) * 0.5f;
        Vector3 f = cam.transform.forward; f.y = 0; f.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, f);
        Vector3 from = head - f * 1.9f + Vector3.up * 0.9f + side * (Vector3.Dot(ws.position - head, side) > 0 ? -0.8f : 0.8f);
        return Cam(from, mid + Vector3.down * 0.2f, 60f);
    }
    public static string FogOff()
    {
        var sv = SceneView.lastActiveSceneView;
        var st = sv.sceneViewState; st.showFog = false; st.showImageEffects = false; st.showSkybox = false;
        sv.sceneViewState = st; sv.Repaint();
        return "scene view fog/skybox/post off";
    }

    public static string Overview() => Look(new Vector3(0, 0.10f, 0.10f), 90f);
    public static string FrontBand() => Look(new Vector3(0, 0.02f, 0.40f), 80f);
    public static string Dock() => Look(new Vector3(0.24f, 0.03f, 0.44f), 45f);
    public static string Desks() => Look(new Vector3(0, 0.15f, 0.05f), 60f);
    public static string BoardView() => Look(new Vector3(0.58f, 0.40f, 0.48f), 70f);

    public static string Reload()
    {
        var s = EditorSceneManager.GetActiveScene();
        EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);
        return "scene reloaded from disk";
    }
}
