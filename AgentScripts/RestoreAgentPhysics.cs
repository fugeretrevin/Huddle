using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Documents;

/// <summary>
/// Restores the "squishy pushable" agent physics on the rigged agents that was present on the old
/// procedural agents: a kinematic Rigidbody + trigger collider + RubberBandReturn so the user's hand
/// physics-capsules nudge the agent and it springs back. (AgentTilt lean is intentionally NOT added:
/// it needs a visual child to tilt, and tilting the rigged root would fight facing / break Animator
/// paths — deferred.) Also reports paper + prop-physics health. Idempotent.
/// </summary>
public static class RestoreAgentPhysics
{
    public static string Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new StringBuilder();

        foreach (var v in Object.FindObjectsByType<AgentView>(FindObjectsInactive.Include))
        {
            var go = v.gameObject;

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

            var cap = go.GetComponent<CapsuleCollider>();
            if (cap == null)
            {
                cap = go.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(0f, 0.105f, 0f);
                cap.height = 0.21f;
                cap.radius = 0.055f;
            }
            cap.isTrigger = true;   // soft push via OnTriggerStay, not a solid wall

            var mv = go.GetComponent<AgentMovement>();
            var rbr = go.GetComponent<RubberBandReturn>();
            if (rbr == null) rbr = go.AddComponent<RubberBandReturn>();
            rbr.movement = mv;
            rbr.maxOffset = 0.04f;
            rbr.springback = 7f;
            rbr.pushStrength = 0.6f;

            EditorUtility.SetDirty(go);
            sb.AppendLine($"[{v.agentId}] Rigidbody(kinematic)+triggerCapsule+RubberBandReturn restored");
        }

        // Health report (not modified): paper hold point + prop physics tray.
        var hpc = Object.FindObjectsByType<HeldPaperController>(FindObjectsInactive.Include).FirstOrDefault();
        if (hpc != null)
            sb.AppendLine("PAPER agentHoldPoint = " + (hpc.agentHoldPoint != null ? FullPath(hpc.agentHoldPoint) : "NULL") +
                          "  grabbable=" + (hpc.grabbable != null ? hpc.grabbable.GetType().Name : "NULL"));

        var tray = GameObject.Find("WorkspaceRoot/PhysicsTray");
        sb.AppendLine("PhysicsTray present: " + (tray != null));
        var caps = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Count(t => t.name.Contains("HandPhysicsCapsule") || t.name.Contains("PhysicsCapsule"));
        sb.AppendLine("hand physics capsule objects found: " + caps);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return sb.ToString();
    }

    static string FullPath(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}
