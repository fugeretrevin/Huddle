using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Pass 6: the delivering agent (researcher) physically holds the paper out; wire the paper's
/// agent-handoff so it rides small in the agent's hand until the user takes it. Also tighten the
/// agent push hitbox.
/// </summary>
public static class FixPass6
{
    static GameObject[] _all;
    static GameObject Find(string n) => _all.FirstOrDefault(g => g.name == n);

    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        _all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToArray();
        var sb = new StringBuilder();

        // 1. Hold point on the researcher (the delivering agent), in front at offer height.
        Transform hold = null;
        var researcher = Find("Agent_Researcher");
        if (researcher == null) sb.AppendLine("researcher not found");
        else
        {
            var bob = researcher.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Bob") ?? researcher.transform;
            var existing = researcher.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "PaperHoldPoint");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var hp = new GameObject("PaperHoldPoint");
            hp.transform.SetParent(bob, false);
            hp.transform.localPosition = new Vector3(0f, 0.095f, 0.085f);   // in front of chest, raised
            hp.transform.localRotation = Quaternion.identity;
            hold = hp.transform;
            sb.AppendLine("created PaperHoldPoint on researcher");
        }

        // 2. Wire the paper's HeldPaperController to the hold point.
        var paper = Find("DecisionDocument");
        if (paper == null) sb.AppendLine("paper not found");
        else
        {
            var hpc = paper.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "HeldPaperController");
            if (hpc == null) sb.AppendLine("no HeldPaperController");
            else
            {
                var ty = hpc.GetType();
                SetF(ty, hpc, "agentHoldPoint", hold);
                SetF(ty, hpc, "heldScale", 0.35f);
                SetF(ty, hpc, "heldLocalEuler", new Vector3(0f, 180f, 0f));  // readable (-Z) side toward user
                SetF(ty, hpc, "expandSeconds", 0.4f);
                EditorUtility.SetDirty(hpc);
                sb.AppendLine("wired HeldPaperController.agentHoldPoint + heldScale 0.35 + readable-facing");
            }
        }

        // 3. Tighten agent push hitbox.
        int n = 0;
        foreach (var agent in new[] { "Agent_Researcher", "Agent_Developer", "Agent_Tester" })
        {
            var a = Find(agent); if (a == null) continue;
            var sc = a.GetComponents<SphereCollider>().FirstOrDefault(c => c.isTrigger);
            if (sc != null) { sc.radius = 0.035f; sc.center = new Vector3(0f, 0.06f, 0f); n++; }
        }
        sb.AppendLine($"tightened hitbox on {n} agents (radius 0.035)");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("SAVED.");
        return sb.ToString();
    }

    static void SetF(Type ty, object o, string field, object val)
    {
        var f = ty.GetField(field, BindingFlags.Instance | BindingFlags.Public);
        if (f != null) f.SetValue(o, val);
    }
}
