using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial;

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



/// <summary>Coordinator integration for the room-aware modes: the near-user kit. Idempotent; saves.</summary>
public static class IntegrateSpatial
{
    static Transform Find(string n) => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == n);

    public static string Apply()
    {
        var sb = new StringBuilder();
        var spatial = Find("Spatial"); var ws = Find("WorkspaceRoot");
        if (spatial == null || ws == null) return "Spatial or WorkspaceRoot missing";
        var rigT = spatial.Find("NearUserRig");
        if (rigT == null) { rigT = new GameObject("NearUserRig").transform; rigT.SetParent(spatial, false); }
        var kit = spatial.GetComponent<NearUserKit>();
        if (kit == null) kit = spatial.gameObject.AddComponent<NearUserKit>();
        var names = new[] { "TaskDock", "TaskBoard", "StampApprove", "StampReject", "Doc_StampRestPad", "ModePlaque", "CompletionReceipt", "FocusToggle", "ArtifactPanel" };
        var list = new List<Transform>();
        foreach (var n in names)
        {
            var t = Find(n);
            if (t == null || !t.IsChildOf(ws)) { sb.AppendLine("not under the office root, skipped: " + n); continue; }
            list.Add(t);
            sb.AppendLine($"stays with the user: {n} (parent {t.parent.name}{(t.parent == ws ? "" : ", world pose kept")})");
        }
        kit.items = list.ToArray(); kit.rig = rigT;
        EditorUtility.SetDirty(kit);
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
        object r = IntegrateSpatial.Apply();
        return "GUARD " + g + "\n" + (r == null ? "" : r.ToString());
    }
}
