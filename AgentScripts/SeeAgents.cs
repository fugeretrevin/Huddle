using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Agents;

/// <summary>Aim the Scene View at the three agents from the front for a close-up capture.</summary>
public static class SeeAgents
{
    public static string Go()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var views = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<AgentView>(true)).ToList();
        if (views.Count == 0) return "no agents";
        // Centre of the group, at head height.
        Vector3 c = Vector3.zero;
        foreach (var v in views) c += v.transform.position;
        c /= views.Count;
        c.y += 0.10f;

        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return "no sceneview";
        // Agents face +Z (authored); view them from +Z looking back (-Z).
        sv.LookAt(c, Quaternion.LookRotation(Vector3.back, Vector3.up), 0.45f);
        sv.Repaint();
        return $"framed {views.Count} agents at {c}";
    }

    public static string One()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var v = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<AgentView>(true))
            .FirstOrDefault(x => x.agentId == "researcher");
        if (v == null) return "no researcher";
        Vector3 c = v.transform.position; c.y += 0.10f;
        var sv = SceneView.lastActiveSceneView;
        sv.LookAt(c, Quaternion.LookRotation(Vector3.back, Vector3.up), 0.18f);
        sv.Repaint();
        return $"framed researcher at {c}";
    }
}
