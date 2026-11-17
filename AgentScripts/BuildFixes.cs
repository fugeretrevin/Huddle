using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MuseDesk.Agents;
using MuseDesk.Workspace;

/// <summary>
/// On-head fixes: (1) wire each agent's RubberBandReturn to the tracked hand anchors so the push
/// works deterministically (physics triggers don't fire reliably against the kinematic agent),
/// and enlarge the agent trigger to match the visible body; (2) make the task board face the user
/// (fixes the mirrored text) using the same FaceUser the name tags use. Idempotent.
/// </summary>
public static class BuildFixes
{
    public static string Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();

        // Find the tracked hand anchors on the camera rig.
        Transform left = null, right = null;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t.name == "LeftHandAnchor" && left == null) left = t;
            else if (t.name == "RightHandAnchor" && right == null) right = t;
        }
        var hands = new[] { left, right }.Where(h => h != null).ToArray();
        sb.AppendLine($"hand anchors found: left={left != null} right={right != null}");

        // (1) Agents: wire push to hand anchors + enlarge trigger.
        foreach (var v in Object.FindObjectsByType<AgentView>(FindObjectsInactive.Include))
        {
            var go = v.gameObject;
            var rbr = go.GetComponent<RubberBandReturn>();
            if (rbr != null)
            {
                rbr.handPoints = hands;
                rbr.pushRadius = 0.09f;
                rbr.maxOffset = 0.05f;
                rbr.springback = 7f;
                rbr.pushStrength = 0.9f;
                EditorUtility.SetDirty(rbr);
            }
            var cap = go.GetComponent<CapsuleCollider>();
            if (cap != null)
            {
                cap.center = new Vector3(0f, 0.125f, 0f);
                cap.height = 0.25f;
                cap.radius = 0.08f;
                cap.isTrigger = true;
                EditorUtility.SetDirty(cap);
            }
            sb.AppendLine($"[{v.agentId}] push wired to {hands.Length} hand(s), trigger enlarged.");
        }

        // (2) Task board: face the user (un-mirror).
        var board = GameObject.Find("WorkspaceRoot") != null
            ? GameObject.Find("WorkspaceRoot").transform.Find("TaskBoard") : null;
        if (board != null)
        {
            var fu = board.GetComponent<FaceUser>();
            if (fu == null) fu = board.gameObject.AddComponent<FaceUser>();
            fu.forward = false;   // same convention as the readable name tags
            fu.turnSpeed = 6f;
            EditorUtility.SetDirty(board.gameObject);
            sb.AppendLine("TaskBoard: FaceUser(forward=false) added (un-mirror).");
        }
        else sb.AppendLine("TaskBoard NOT found.");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return sb.ToString();
    }
}
