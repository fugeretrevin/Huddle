using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;
using System.Text;
using UnityEngine;
using TMPro;

/// <summary>The Editor is shared with another tool that opens its own preview scenes. Call Ensure
/// before any production builder: it opens MainMRScene only if the scene currently open has no
/// unsaved changes, so nobody's work is discarded.</summary>
public static class SceneGuard
{
    const string Main = "Assets/MuseDesk/Scenes/MainMRScene.unity";

    public static string Ensure()
    {
        var s = EditorSceneManager.GetActiveScene();
        if (s.path == Main) return "OK main scene open" + (s.isDirty ? " (dirty)" : "");
        if (EditorApplication.isPlaying) return "BLOCKED play mode is running in " + s.path;
        if (s.isDirty) return "BLOCKED other scene has unsaved changes: " + s.path;
        string was = s.path;
        EditorSceneManager.OpenScene(Main, OpenSceneMode.Single);
        return "OK opened main scene (was " + was + ")";
    }

    public static string Which()
    {
        var s = EditorSceneManager.GetActiveScene();
        return s.path + " dirty=" + s.isDirty + " time=" + System.DateTime.Now.ToString("HH:mm:ss");
    }
}



/// <summary>Coordinator fix-ups after the refinement builders (Paper, Receipt, Board, Life, Kit).
/// Idempotent; saves the scene.</summary>
public static class IntegrateV3
{
    static Transform Find(string n) => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == n);

    public static string Apply()
    {
        var sb = new StringBuilder();
        // Approve / Reject: white bold label on the coloured cap (as the owner saw and liked), not ink.
        var white = new Color32(0xF3, 0xF4, 0xF6, 255);
        foreach (var k in new[] { "Btn_Approve", "Btn_Reject" })
        {
            var t = Find(k); if (t == null) { sb.AppendLine("missing " + k); continue; }
            foreach (var l in t.GetComponentsInChildren<TMP_Text>(true))
            {
                l.color = white; l.fontStyle = FontStyles.Bold; EditorUtility.SetDirty(l);
                var ksv = l.GetComponentInParent<MuseDesk.Workspace.KeyStateVisual>(true);
                sb.AppendLine($"{k}/{l.name} -> white bold");
            }
        }
        // Artifact viewer: BuildReceiptV3 put it at (0, 0.32, 0.26), where the open sheet covers the
        // Developer's face. From the eye (0, 0.30, 0.62) the agents' heads (y 0.19 to 0.31 at z about 0)
        // are cleared only if the sheet's lower edge projects above them, so it goes above the office:
        // centre (0, 0.435, 0.10), 0.54 m from the eye, lower edge and keys at y 0.325 (0.52 m, in reach),
        // top edge 25 degrees above eye level.
        var viewer = Find("ArtifactPanel"); var wsRoot = Find("WorkspaceRoot");
        if (viewer != null && wsRoot != null)
        {
            Vector3 eye = new Vector3(0f, 0.30f, 0.62f), p = new Vector3(0f, 0.435f, 0.10f);
            viewer.position = wsRoot.TransformPoint(p);
            viewer.rotation = wsRoot.rotation * Quaternion.LookRotation(p - eye, Vector3.up);
            sb.AppendLine($"ArtifactPanel -> {p} facing the eye, {Vector3.Distance(p, eye):F2} m away (above the office, clear of the agents' heads)");
        }
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return sb.ToString();
    }
}


public static class GuardedRun
{
    public static string Go()
    {
        string g = SceneGuard.Ensure();
        if (!g.StartsWith("OK")) return "GUARD " + g + " : builder not run";
        object r = IntegrateV3.Apply();
        return "GUARD " + g + "\n" + (r == null ? "" : r.ToString());
    }
}
