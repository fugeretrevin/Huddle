using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MuseDesk.Workspace;

/// <summary>
/// Foundation fixes from on-head feedback:
///  1. Spawn the workspace IN FRONT of the user (not 0.75 m to the side) and at a comfortable height,
///     so on load the user is facing it. (The facing DIRECTION is fixed separately in
///     WorkspacePlacement.FacingUserYaw, now pointing the content's authored -Z front at the user.)
///  2. Strip the self-billboarding FaceUser off every poke BUTTON — a poke button that re-aims itself
///     every frame looks like it "jumps out" when pressed. Buttons inherit facing from their parent
///     board/panel (which keep their FaceUser), so they still face the user without the jitter.
/// Idempotent; operates on the saved MainMRScene.
/// </summary>
public static class FixFoundation
{
    public static string Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();

        // 1) Place in front, comfortable height.
        var wp = Object.FindAnyObjectByType<WorkspacePlacement>();
        if (wp != null)
        {
            wp.offDeskMode = true;
            wp.offDeskSide = 0f;      // centered in front (was 0.75 to the right)
            wp.offDeskDepth = 0.55f;  // agents ~0.55 m ahead; stamps (local -0.32 z) land ~0.23 m out
            wp.offDeskDrop = 0.30f;   // 0.30 m below eye (was 0.55)
            EditorUtility.SetDirty(wp);
            sb.AppendLine($"WorkspacePlacement: side={wp.offDeskSide} depth={wp.offDeskDepth} drop={wp.offDeskDrop}");
        }
        else sb.AppendLine("WARN: WorkspacePlacement not found.");

        // 2) Strip FaceUser from poke buttons (objects carrying an ISDK poke interactable or the
        //    InteractableUnityEventWrapper we wire).
        var all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToArray();
        int stripped = 0;
        foreach (var go in all)
        {
            bool isButton = go.GetComponents<Component>().Any(c => c != null &&
                (c.GetType().Name == "PokeInteractable" || c.GetType().Name == "InteractableUnityEventWrapper"));
            if (!isButton) continue;
            // Remove FaceUser from this button and any FaceUser on its immediate parent "button" wrapper.
            foreach (var fu in go.GetComponents<FaceUser>())
            {
                Object.DestroyImmediate(fu);
                stripped++;
            }
        }
        sb.AppendLine($"Stripped FaceUser from {stripped} poke button object(s).");

        // 3) The user now stamps physically (StampTool). Drop the agent's redundant auto-stamp prop
        //    but KEEP the authoritative APPROVED/REJECTED mark (ApprovalStampController reveals it on
        //    the backend's decision.resolved even when stamp is null).
        foreach (var asc in Object.FindObjectsByType<MuseDesk.Documents.ApprovalStampController>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (asc.stamp != null)
            {
                asc.stamp.gameObject.SetActive(false);
                sb.AppendLine($"Agent auto-stamp '{asc.stamp.name}' disabled; mark kept.");
                asc.stamp = null;
                EditorUtility.SetDirty(asc);
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        sb.AppendLine("SAVED scene.");
        return sb.ToString();
    }
}
