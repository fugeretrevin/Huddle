using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MuseDesk.Phase2.Board;
using MuseDesk.Phase2.Contracts;

/// <summary>
/// Editor-only preview for captures: puts sample task cards on the board and shows the decision
/// paper, without ever saving. Show() then capture, then Hide(). Preview objects are DontSave.
///   unity command script run --file AgentScripts/Preview.cs --entry Preview.Show
/// </summary>
public static class Preview
{
    const string Tag = "__PreviewCard";

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

    public static string Show()
    {
        Hide();
        var all = All();
        var board = all.First(t => t.name == "TaskBoard");
        var template = board.Find("CardTemplate");
        var samples = new (string region, TaskCardV2 data)[]
        {
            ("Region_Backlog", new TaskCardV2 { TaskId = "t2-ab12cd34", Title = "Write the login tests", TaskState = TaskStates.Queued, Priority = 2, Revision = 0, SourceMode = SourceModes.Demo }),
            ("Region_Backlog", new TaskCardV2 { TaskId = "t2-ef56ab78", Title = "Investigate the flaky token refresh on slow networks and document the root cause", TaskState = TaskStates.Blocked, AgentId = "researcher", Priority = 1, Revision = 3, SourceMode = SourceModes.Demo }),
            ("Region_Working", new TaskCardV2 { TaskId = "t2-0911aa22", Title = "Implement OAuth scaffold", TaskState = TaskStates.Running, AgentId = "developer", Priority = 3, Revision = 5, SourceMode = SourceModes.Live }),
            ("Region_NeedsMe", new TaskCardV2 { TaskId = "t2-77c0de11", Title = "Approve auth approach", TaskState = TaskStates.AwaitingApproval, AgentId = "researcher", Priority = 3, Revision = 2, SourceMode = SourceModes.Live }),
            ("Region_Completed", new TaskCardV2 { TaskId = "t2-5150beef", Title = "Baseline test run", TaskState = TaskStates.Completed, AgentId = "tester", Priority = 0, Revision = 4, SourceMode = SourceModes.Demo }),
        };
        var rows = new Dictionary<string, int>();
        foreach (var (regionName, data) in samples)
        {
            var region = board.Find(regionName);
            var go = Object.Instantiate(template.gameObject, region);
            go.name = Tag;
            go.hideFlags = HideFlags.DontSave;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
            rows.TryGetValue(regionName, out int row);
            rows[regionName] = row + 1;
            go.transform.localPosition = new Vector3(0f, -row * 0.10f, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.SetActive(true);
            go.GetComponent<TaskCardView>().SetData(data);
        }
        foreach (var pc in All().Where(t => t != null && t.name == Tag)) foreach (var tm in pc.GetComponentsInChildren<TMPro.TMP_Text>(true)) tm.ForceMeshUpdate(true, true);
        var report = new System.Text.StringBuilder();
        var first = All().FirstOrDefault(t => t != null && t.name == Tag);
        if (first != null)
            foreach (var tmp in first.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (tmp.transform.parent != first) continue;
                tmp.ForceMeshUpdate(true, true);
                var r = tmp.GetComponent<Renderer>();
                var wb = r.bounds;
                var cl = first.InverseTransformPoint(wb.center);
                var m0 = tmp.mesh.vertexCount > 0 ? tmp.mesh.vertices[0] : Vector3.zero;
                report.Append($" | {tmp.name}: boundsCenterInCard=({cl.x * 1000:F1},{cl.y * 1000:F1},{cl.z * 1000:F1})mm worldSize=({wb.size.x * 100:F1},{wb.size.y * 100:F1},{wb.size.z * 100:F1})cm meshBounds={tmp.mesh.bounds.size} v0={m0} mat={r.sharedMaterial.name} shader={r.sharedMaterial.shader.name} layer={tmp.gameObject.layer} fwdDotBoard={Vector3.Dot(tmp.transform.forward, board.forward):F2}");
                report.Append($" | {tmp.name}: '{tmp.text}' chars={tmp.textInfo.characterCount} font={tmp.fontSize:F2} active={tmp.gameObject.activeInHierarchy} rend={(r != null && r.enabled)} mf={(tmp.GetComponent<MeshFilter>() != null)} mesh={(tmp.mesh != null ? tmp.mesh.vertexCount : -1)} lp={tmp.transform.localPosition} ls={tmp.transform.localScale.x} color={tmp.color}");
            }
        var doc = all.FirstOrDefault(t => t.name == "DecisionDocument");
        return report + "\npreview cards: " + samples.Length + (doc != null ? "; paper is " + (doc.gameObject.activeSelf ? "active" : "inactive") : "");
    }

    public static string Hide()
    {
        int n = 0;
        foreach (var t in All().Where(t => t != null && t.name == Tag).ToList())
            if (t != null) { Object.DestroyImmediate(t.gameObject); n++; }
        return "removed " + n;
    }

    /// <summary>Frame the SceneView squarely on a named object from its readable (-Z) side.</summary>
    static string Front(string name, float distance, float fov = 50f)
    {
        var t = All().FirstOrDefault(x => x.name == name);
        if (t == null) return "not found " + name;
        var sv = SceneView.lastActiveSceneView;
        sv.orthographic = false;
        sv.cameraSettings.fieldOfView = fov;
        Vector3 eye = t.position - t.forward * distance;
        var rot = Quaternion.LookRotation(t.position - eye, Vector3.up);
        sv.LookAtDirect(t.position, rot, distance * Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad));
        sv.Repaint();
        return "framed " + name;
    }

    /// <summary>Show the decision paper where the researcher would hand it over (never saved).</summary>
    public static string ShowPaper()
    {
        var all = All();
        var doc = all.FirstOrDefault(t => t.name == "DecisionDocument");
        var ws = all.First(t => t.name == "WorkspaceRoot");
        if (doc == null) return "no DecisionDocument";
        doc.gameObject.SetActive(true);
        foreach (Transform c in doc) if (c.name != "ApprovalStamp") c.gameObject.SetActive(true);
        Vector3 eye = ws.TransformPoint(new Vector3(0f, 0.30f, 0.62f));
        doc.position = ws.TransformPoint(new Vector3(-0.05f, 0.24f, 0.30f));
        doc.rotation = Quaternion.LookRotation(doc.position - eye, Vector3.up);
        foreach (var tm in doc.GetComponentsInChildren<TMPro.TMP_Text>(true)) tm.ForceMeshUpdate(true, true);
        var sv = SceneView.lastActiveSceneView;
        sv.orthographic = false; sv.cameraSettings.fieldOfView = 55f;
        float dist = (doc.position - eye).magnitude;
        sv.LookAtDirect(doc.position, Quaternion.LookRotation(doc.position - eye, Vector3.up), dist * Mathf.Sin(27.5f * Mathf.Deg2Rad));
        sv.Repaint();
        return "paper shown (do not save the scene); call HidePaper";
    }

    public static string HidePaper()
    {
        // Re-open the scene from disk to discard the preview state entirely.
        var scene = EditorSceneManager.GetActiveScene();
        Hide();
        EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
        return "scene reloaded from disk";
    }

    public static string FrameBoard() => Front("TaskBoard", 0.50f);
    public static string FrameBoardButtons() => Front("PauseResumeButton", 0.22f, 45f);
    public static string FrameFocus()
    {
        var t = All().First(x => x.name == "FocusToggle");
        var sv = SceneView.lastActiveSceneView;
        sv.orthographic = false; sv.cameraSettings.fieldOfView = 45f;
        Vector3 eye = t.position - t.forward * 0.20f + Vector3.up * 0.10f + t.right * 0.10f;
        sv.LookAtDirect(t.position, Quaternion.LookRotation(t.position - eye, Vector3.up), 0.25f * Mathf.Sin(22.5f * Mathf.Deg2Rad));
        sv.Repaint();
        return "framed focus toggle (three-quarter view)";
    }
}
