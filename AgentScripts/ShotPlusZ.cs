using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Frames the SceneView from the +Z side looking -Z (the ORIGINAL runtime user side, where
/// WorkspaceRoot.forward pointed at the head), at seated-eye height relative to the workspace.</summary>
public static class ShotPlusZ
{
    public static string Run()
    {
        var ws = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "WorkspaceRoot");
        if (ws == null) return "WorkspaceRoot not found";
        var sv = SceneView.lastActiveSceneView;
        sv.orthographic = false;
        sv.pivot = ws.position + new Vector3(0f, 0.12f, 0.05f);
        sv.rotation = Quaternion.Euler(20f, 180f, 0f);   // camera on +Z, looking toward -Z and slightly down
        sv.size = 0.5f;
        sv.Repaint();
        return "framed from +Z looking -Z";
    }

    static Transform Find(string name) => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == name);

    static string FrontOf(Transform t, float size, float pitch = 0f)
    {
        var sv = SceneView.lastActiveSceneView;
        sv.orthographic = false;
        sv.pivot = t.position;
        // Look along the object's own -front: stand on the side its readable face points to.
        // Readable/visible face is local -Z, so the camera must look along local +Z.
        sv.rotation = Quaternion.LookRotation(t.forward, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f);
        sv.size = size;
        sv.Repaint();
        return $"front of {t.name} pos={t.position} fwd={t.forward}";
    }

    public static string Paper()
    {
        var doc = Find("DecisionDocument");
        if (doc == null) return "DecisionDocument not found";
        doc.gameObject.SetActive(true);
        foreach (Transform c in doc) if (c.name != "ApprovalStamp") c.gameObject.SetActive(true);
        return FrontOf(doc, 0.22f);
    }

    public static string Board()
    {
        var b = Find("TaskBoard");
        if (b == null) return "TaskBoard not found";
        return FrontOf(b, 0.42f * b.lossyScale.x);
    }

    public static string Wide()
    {
        var ws = EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "WorkspaceRoot");
        var sv = SceneView.lastActiveSceneView;
        sv.pivot = ws.position + new Vector3(0f, 0.35f, 0.1f);
        sv.rotation = Quaternion.Euler(12f, 180f, 0f);
        sv.size = 1.1f;
        sv.Repaint();
        return "framed wide from +Z";
    }
}
