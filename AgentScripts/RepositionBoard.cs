using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Moves the procedural TaskBoard UP and BEHIND the agent row so it reads as a wall-mounted kanban
/// board the agents work in front of, instead of a panel floating between the user and the agents
/// (which occluded them). Authored layout: user at origin looking +Z; agents at ~Z0.5 (WorkspaceRoot
/// local Z ~0); board was at local Z -0.22 (user side). New local pose puts it past the agents and
/// higher. FaceUser billboards it at runtime, so rotation is left to that. Idempotent.
/// </summary>
public static class RepositionBoard
{
    // WorkspaceRoot-local target: +Z is away from the user (behind the agents); +Y is up.
    static readonly Vector3 NewLocalPos = new Vector3(0f, 0.65f, 0.35f);

    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var board = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name == "TaskBoard");
        if (board == null) return "ERROR: TaskBoard not found.";

        var sb = new StringBuilder();
        sb.AppendLine($"before: localPos={board.localPosition} worldPos={board.position}");
        board.localPosition = NewLocalPos;
        sb.AppendLine($"after:  localPos={board.localPosition} worldPos={board.position}");

        // Confirm it has FaceUser so it still turns to the user from the new spot.
        bool hasFaceUser = board.GetComponents<Component>().Any(c => c != null && c.GetType().Name == "FaceUser");
        sb.AppendLine("FaceUser present on board: " + hasFaceUser);
        if (!hasFaceUser)
            sb.AppendLine("  NOTE: no FaceUser — board keeps its authored rotation; verify its readable (−Z) face points at the user.");

        EditorUtility.SetDirty(board);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        sb.AppendLine("SAVED scene.");
        return sb.ToString();
    }
}
