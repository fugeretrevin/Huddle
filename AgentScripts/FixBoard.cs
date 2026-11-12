using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fix the task board: it was built static (text mirrored from the user's side) and overlapping the
/// desk clutter. Face it at the user (FaceUser forward=false, same as the paper) and move it up/back
/// above the agents, clear of the tray/cube/paper. Also dump the board's child layout for tuning.
/// </summary>
public static class FixBoard
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var board = all.FirstOrDefault(t => t.name == "TaskBoard");
        if (board == null) return "TaskBoard not found";
        var sb = new StringBuilder();

        // Move up + back, centered, clear of the desk clutter (tray/cube/paper sit ~y1.0-1.1, z0.4-0.5).
        board.position = new Vector3(0f, 1.45f, 0.62f);
        sb.AppendLine($"board -> world {board.position}");

        // Face the user (yaw billboard, readable): add FaceUser forward=false like the paper.
        var faceUserType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
            .FirstOrDefault(t => t.Name == "FaceUser");
        if (faceUserType != null)
        {
            var fu = board.GetComponent(faceUserType);
            if (fu == null) fu = board.gameObject.AddComponent(faceUserType);
            var f = faceUserType.GetField("forward", BindingFlags.Instance | BindingFlags.Public);
            if (f != null) f.SetValue(fu, false);
            var ts = faceUserType.GetField("turnSpeed", BindingFlags.Instance | BindingFlags.Public);
            if (ts != null) ts.SetValue(fu, 12f);
            EditorUtility.SetDirty(fu);
            sb.AppendLine("added FaceUser(forward=false) to board");
        }
        else sb.AppendLine("FaceUser type not found");

        // Dump the board's immediate children + local positions, so I can see any overlap in the layout.
        sb.AppendLine("=== board child layout (local) ===");
        foreach (Transform c in board)
            sb.AppendLine($"  {c.name} lp={c.localPosition} ls={c.localScale}");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("SAVED.");
        return sb.ToString();
    }
}
