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
