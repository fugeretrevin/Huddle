using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Frames the SceneView as the seated user would see the workspace, from either side of
/// WorkspaceRoot, using the scene's own WorkspacePlacement numbers (depth 0.55 m, drop 0.30 m).
/// Read-only: moves only the SceneView camera.</summary>
public static class ViewFrom
{
    static Transform Root() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "WorkspaceRoot");

    static string Frame(float sign)
    {
        var ws = Root();
        if (ws == null) return "WorkspaceRoot not found";
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return "no scene view";
        Vector3 eye = ws.position + ws.forward * (0.62f * sign) + Vector3.up * 0.30f;
        Vector3 look = ws.position + Vector3.up * 0.12f;
        sv.orthographic = false;
        sv.cameraSettings.fieldOfView = 90f;
        var rot = Quaternion.LookRotation(look - eye, Vector3.up);
        // SceneView camera sits at pivot - forward * distance; choose size so the camera is at the eye.
        float dist = (look - eye).magnitude;
        sv.LookAtDirect(look, rot, dist * Mathf.Sin(sv.cameraSettings.fieldOfView * 0.5f * Mathf.Deg2Rad));
        sv.Repaint();
        return $"eye={eye} look={look} camPos={sv.camera.transform.position}";
    }

    public static string PlusZ() => Frame(+1f);
    public static string MinusZ() => Frame(-1f);

    public static string Board() => At("TaskBoard");
    public static string Right() => At("FocusToggle");
    public static string Stamps() => At("StampApprove");

    /// <summary>Seated eye, looking at a named object (to check a side panel / board).</summary>
    public static string At(string name)
    {
        var ws = Root();
        var t = ws.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
        if (t == null) return "not found: " + name;
        var sv = SceneView.lastActiveSceneView;
        Vector3 eye = ws.position + ws.forward * 0.62f + Vector3.up * 0.30f;
        sv.orthographic = false;
        sv.cameraSettings.fieldOfView = 70f;
        var rot = Quaternion.LookRotation(t.position - eye, Vector3.up);
        float dist = (t.position - eye).magnitude;
        sv.LookAtDirect(t.position, rot, dist * Mathf.Sin(35f * Mathf.Deg2Rad));
        sv.Repaint();
        return $"eye={eye} target={t.position}";
    }
}
