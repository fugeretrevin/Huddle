using UnityEngine;
using UnityEditor;

/// <summary>Aims the SceneView from the AUTHORED user eye (near origin, looking +Z toward the agents)
/// so a capture shows the agent row with the repositioned board behind/above them.</summary>
public static class AimUserView
{
    public static string Run()
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return "no scene view";
        // Frame the agents (Z~0.5) and the raised board (Z0.85, Y1.65). Pivot between them, look +Z.
        sv.pivot = new Vector3(0f, 1.35f, 0.65f);
        sv.rotation = Quaternion.Euler(6f, 0f, 0f); // yaw 0 => camera looks toward +Z (user's forward)
        sv.size = 1.3f;
        sv.Repaint();
        return $"aimed: pivot={sv.pivot} size={sv.size}";
    }
}
