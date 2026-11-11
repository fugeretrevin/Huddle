using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Agents;

/// <summary>Dump one agent's Bob hierarchy to confirm the arm rig.</summary>
public static class InspectGrab
{
    public static string Dump()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var view = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<AgentView>(true))
            .FirstOrDefault(v => v.agentId == "researcher");
        if (view == null) return "no researcher";
        var sb = new StringBuilder();
        sb.Append($"movement={view.movement != null} armL={(view.armL ? view.armL.name : "null")} armR={(view.armR ? view.armR.name : "null")}\n");
        Walk(view.bob, 0, sb);
        return sb.ToString();
    }

    static void Walk(Transform t, int d, StringBuilder sb)
    {
        if (t == null) return;
        sb.Append(new string(' ', d * 2)).Append(t.name)
          .Append($"  lp={t.localPosition} ls={t.localScale}\n");
        foreach (Transform c in t) Walk(c, d + 1, sb);
    }
}
