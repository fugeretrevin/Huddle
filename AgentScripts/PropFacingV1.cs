using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Owner (worn rc17): "the prop objects should face the right way". The desk props were placed with yaws of 140-195 degrees
/// (an older facing convention), so their fronts (model +Z, like the agents) pointed away from the user: the clock showed its
/// back, the lamp shade pointed away. Each PushableProp under WorkspaceRoot is turned (yaw only) to face the seated user's
/// eye at WorkspaceRoot local (0, 0.30, 0.62), as the scene facing convention says.
///   unity command script run --file AgentScripts/PropFacingV1.cs --entry PropFacingV1.Apply --args '[true]'
/// </summary>
public static class PropFacingV1
{
    public static string Apply(bool save)
    {
        var sb = new StringBuilder();
        Transform ws = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "WorkspaceRoot" && t.gameObject.scene.IsValid());
        if (ws == null) return "no WorkspaceRoot";
        Vector3 eye = ws.TransformPoint(new Vector3(0f, 0.30f, 0.62f));
        foreach (var p in ws.GetComponentsInChildren<MuseDesk.Workspace.PushableProp>(true))
        {
            Transform t = p.transform;
            Vector3 to = eye - t.position; to = Vector3.ProjectOnPlane(to, ws.up);
            if (to.sqrMagnitude < 1e-6f) continue;
            Undo.RecordObject(t, "Prop facing");
            float before = t.localEulerAngles.y;
            t.rotation = Quaternion.LookRotation(to.normalized, ws.up);
            sb.AppendLine(t.name + ": yaw " + before.ToString("F0") + " -> " + t.localEulerAngles.y.ToString("F0") + (t.gameObject.activeInHierarchy ? "" : " (inactive)"));
        }
        if (save)
        {
            EditorSceneManager.MarkSceneDirty(ws.gameObject.scene);
            EditorSceneManager.SaveScene(ws.gameObject.scene);
            sb.AppendLine("saved");
        }
        return sb.ToString();
    }
}
