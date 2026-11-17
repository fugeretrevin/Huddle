using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using MuseDesk.Agents;

/// <summary>
/// Adds and wires the agent liveliness components on the three agents in the OPEN scene:
///   AgentGaze      head / neck look-at, glances, listening and speaking visuals
///   AgentIdleLife  breathing and small weight shifts on the spine / chest
/// Bones are found by name under each agent (Head, Neck, Spine, Chest: the shared Pipkin armature,
/// docs/Art/RIG_SPECIFICATION.md). The decision paper (scene object "DecisionDocument") is handed
/// to each AgentGaze so the agent that holds it can look at it.
///
/// Idempotent: existing components are reused and re-wired, nothing is duplicated or destroyed,
/// tuning values already set on a component are left alone. Only the open scene is touched; it is
/// marked dirty and saved once at the end. Does nothing in Play Mode.
///
///   unity command script run --file AgentScripts/BuildAgentLiveliness.cs --entry BuildAgentLiveliness.Apply
/// </summary>
public static class BuildAgentLiveliness
{
    static readonly string[] AgentIds = { "researcher", "developer", "tester" };

    static Transform Under(Transform root, string name)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].name == name) return all[i];
        return null;
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Not in Play Mode: stop the player first.";

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "No open scene.";

        var sb = new StringBuilder();
        List<Transform> all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

        Transform paper = all.FirstOrDefault(t => t != null && t.name == "DecisionDocument");
        sb.AppendLine(paper != null ? "decision paper: DecisionDocument" : "decision paper: NOT FOUND (agents will not glance at it)");

        int done = 0;
        foreach (string id in AgentIds)
        {
            AgentView view = null;
            foreach (var t in all)
            {
                if (t == null) continue;   // defensive: the list is cached across the whole run
                var v = t.GetComponent<AgentView>();
                if (v != null && v.agentId == id) { view = v; break; }
            }
            if (view == null) { sb.AppendLine($"SKIP (no AgentView with agentId '{id}')"); continue; }

            GameObject go = view.gameObject;
            Transform root = go.transform;
            var movement = go.GetComponent<AgentMovement>();
            var bridge = go.GetComponent<AgentAnimatorBridge>();

            Transform head = Under(root, "Head");
            Transform neck = Under(root, "Neck");
            Transform spine = Under(root, "Spine");
            Transform chest = Under(root, "Chest");

            var gaze = go.GetComponent<AgentGaze>();
            if (gaze == null) gaze = Undo.AddComponent<AgentGaze>(go);
            else Undo.RecordObject(gaze, "wire AgentGaze");
            gaze.head = head;
            gaze.neck = neck;
            gaze.view = view;
            gaze.movement = movement;
            gaze.bridge = bridge;
            gaze.paper = paper;
            EditorUtility.SetDirty(gaze);

            var life = go.GetComponent<AgentIdleLife>();
            if (life == null) life = Undo.AddComponent<AgentIdleLife>(go);
            else Undo.RecordObject(life, "wire AgentIdleLife");
            life.spine = spine;
            life.chest = chest;
            life.view = view;
            life.movement = movement;
            life.bridge = bridge;
            life.gaze = gaze;
            EditorUtility.SetDirty(life);

            // The older lean component tilts AgentView.bob, which the rigged agents do not have; it
            // would fall back to tilting the agent root. Reported, not removed.
            bool oldTilt = go.GetComponent<AgentTilt>() != null;

            var animator = bridge != null ? bridge.animator : null;
            sb.AppendLine($"{go.name} ({id}): Head={Name(head)} Neck={Name(neck)} Spine={Name(spine)} Chest={Name(chest)}"
                + $" | movement={(movement != null)} bridge={(bridge != null)} animator={(animator != null)}"
                + $" faceUser={(go.GetComponent<FaceUserWhileDelivering>() != null)}"
                + (head == null ? " | WARNING: no Head bone, AgentGaze will switch itself off" : "")
                + (spine == null && chest == null ? " | WARNING: no Spine/Chest bone, AgentIdleLife will switch itself off" : "")
                + (animator != null && animator.updateMode != AnimatorUpdateMode.Normal ? " | WARNING: Animator update mode is not Normal; bone layers need Normal" : "")
                + (oldTilt ? " | NOTE: AgentTilt is present on this rigged agent (it tilts the root); consider removing it" : ""));
            done++;
        }

        if (done == 0)
        {
            sb.AppendLine("no agents found; scene not saved");
            return sb.ToString();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        sb.AppendLine($"agents wired: {done}/3, scene saved={saved} ({scene.path})");
        return sb.ToString();
    }

    static string Name(Transform t) => t != null ? t.name : "MISSING";
}
