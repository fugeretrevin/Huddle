using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MuseDesk.Spatial;

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
        kit.decisionOnly = new[] { "StampApprove", "StampReject", "Doc_StampRestPad" }.Select(Find).Where(t => t != null).ToArray();
        sb.AppendLine($"shown at the desk only while a decision waits (office away): {string.Join(", ", kit.decisionOnly.Select(t => t.name))}");
        // The single source of truth is Assets/MuseDesk/Resources/ReleaseConfig.asset (MuseDeskReleaseConfig);
        // ReleaseConfigApplier overrides these at run time. Here the serialized values just mirror it (default: on).
        var director = spatial.GetComponent<SpatialWorkspaceDirector>();
        if (director != null)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<MuseDesk.Config.MuseDeskReleaseConfig>("Assets/MuseDesk/Resources/ReleaseConfig.asset");
            bool floor = cfg == null || cfg.floorMode, ceiling = cfg == null || cfg.ceilingMode;
            director.flags.floorModeEnabled = floor; director.flags.ceilingModeEnabled = ceiling;
            EditorUtility.SetDirty(director);
            sb.AppendLine($"feature flags mirrored from ReleaseConfig: Floor {(floor ? "ON" : "OFF")}, Ceiling {(ceiling ? "ON" : "OFF")} (see FINAL_FEATURE_FLAG_DECISION.md)");
        }
        EditorUtility.SetDirty(kit);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return sb.ToString();
    }
}
