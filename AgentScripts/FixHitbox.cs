using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Agents react to the hand from ~a foot away -> their trigger volumes are far too big in WORLD
/// space (likely a WorkspaceRoot scale multiplying the local radius). Shrink each agent's root push
/// sphere + arm nudge capsules to a small WORLD size regardless of parent scale.
/// </summary>
public static class FixHitbox
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var sb = new StringBuilder();

        const float ROOT_WORLD_R = 0.028f;  // only on genuine near-touch of the tiny body
        const float ARM_WORLD_R  = 0.012f;  // arm nudge: snug to the little arm

        // Gentle it down: smaller push + offset so a graze doesn't fling them, and faster spring back.
        foreach (var go in all.Select(t => t.gameObject).Distinct())
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                var tn = c.GetType().Name;
                if (tn == "RubberBandReturn")
                {
                    SetF(c, "pushStrength", 0.25f); SetF(c, "maxOffset", 0.025f); SetF(c, "springback", 10f);
                }
                else if (tn == "AgentTilt")
                {
                    // reduce whatever tilt-amount + strength fields exist (best-effort by common names)
                    SetF(c, "maxTilt", 7f); SetF(c, "maxTiltDeg", 7f); SetF(c, "tiltDegrees", 7f);
                    SetF(c, "pushStrength", 0.25f); SetF(c, "strength", 0.25f); SetF(c, "springback", 10f);
                }
                else if (tn == "AgentArmNudge")
                {
                    SetF(c, "maxNudge", 10f); SetF(c, "maxNudgeDeg", 10f); SetF(c, "nudgeDegrees", 10f);
                    SetF(c, "strength", 0.3f); SetF(c, "springback", 12f);
                }
            }
        }

        foreach (var agent in new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" })
        {
            var a = all.FirstOrDefault(t => t.name == agent);
            if (a == null) continue;

            // Root push sphere
            var sc = a.GetComponents<SphereCollider>().FirstOrDefault(c => c.isTrigger);
            if (sc != null)
            {
                float ls = Mathf.Max(0.0001f, Mathf.Max(a.lossyScale.x, a.lossyScale.y, a.lossyScale.z));
                float worldBefore = sc.radius * ls;
                sc.radius = ROOT_WORLD_R / ls;
                // keep the sphere centered on the body, scaled too
                sb.AppendLine($"{agent}: root sphere worldR {worldBefore:F3} -> {ROOT_WORLD_R:F3} (local {sc.radius:F3}, lossy {ls:F2})");
            }

            // Arm nudge capsules
            foreach (var arm in a.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Arm_L" || t.name == "Arm_R"))
            {
                var cap = arm.GetComponents<CapsuleCollider>().FirstOrDefault(c => c.isTrigger);
                if (cap != null)
                {
                    float ls = Mathf.Max(0.0001f, Mathf.Max(arm.lossyScale.x, arm.lossyScale.y, arm.lossyScale.z));
                    float worldBefore = cap.radius * ls;
                    cap.radius = ARM_WORLD_R / ls;
                    sb.AppendLine($"  {arm.name}: cap worldR {worldBefore:F3} -> {ARM_WORLD_R:F3} (lossy {ls:F2})");
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("SAVED.");
        return sb.ToString();
    }

    static void SetF(Component c, string field, float val)
    {
        var f = c.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (f != null && (f.FieldType == typeof(float))) f.SetValue(c, val);
    }
}
