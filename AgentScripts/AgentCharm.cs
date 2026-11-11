using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Agents;

/// <summary>
/// Gives each agent a persistent role-colored "scarf" ring at the neck so the three coworkers are
/// instantly distinguishable even though their body color is state-driven. Idempotent.
/// researcher=blue, developer=green, tester=amber (matches their active-state hues).
/// </summary>
public static class AgentCharm
{
    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder();
        var urp = Shader.Find("Universal Render Pipeline/Lit");

        Color For(string role) => role switch
        {
            "researcher" => new Color(0.30f, 0.55f, 0.95f),
            "developer"  => new Color(0.30f, 0.80f, 0.45f),
            "tester"     => new Color(0.95f, 0.65f, 0.25f),
            _            => new Color(0.75f, 0.75f, 0.80f),
        };

        foreach (var v in scene.GetRootGameObjects()
                     .SelectMany(r => r.GetComponentsInChildren<AgentView>(true)))
        {
            var bob = v.bob != null ? v.bob : v.transform;
            var scarf = bob.Find("Scarf");
            if (scarf == null)
            {
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "Scarf";
                var col = disc.GetComponent<Collider>(); if (col) Object.DestroyImmediate(col);
                scarf = disc.transform;
                scarf.SetParent(bob, false);
            }
            // Thin disc just under the head (head sits ~y=0.11, body top ~y=0.095).
            scarf.localPosition = new Vector3(0f, 0.088f, 0f);
            scarf.localScale = new Vector3(0.050f, 0.006f, 0.050f);
            var mat = new Material(urp) { name = $"Scarf_{v.agentId}" };
            mat.SetColor("_BaseColor", For(v.agentId));
            scarf.GetComponent<Renderer>().sharedMaterial = mat;
            EditorUtility.SetDirty(v);
            sb.AppendLine($"{v.agentId}: scarf {For(v.agentId)}");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved");
        return sb.ToString();
    }
}
