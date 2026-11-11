using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Make the decision card read as a real sheet of paper: a soft drop-shadow/border quad behind it
/// and a thin role-neutral accent bar near the top. Additive, parented under the Card so it scales
/// with the handoff. Visual-only, no interaction impact.
/// </summary>
public static class PaperLook
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var paper = all.FirstOrDefault(t => t.name == "DecisionDocument");
        if (paper == null) return "paper not found";
        var card = paper.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Card");
        if (card == null) return "Card not found";
        var sb = new StringBuilder();

        var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material Mat(Color c, float emit)
        {
            var m = new Material(lit); m.color = c; m.SetColor("_BaseColor", c);
            if (emit > 0) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * emit); }
            return m;
        }

        // Backing (border + soft shadow): a slightly larger quad just BEHIND the card (+z local, away
        // from the reader on the -z readable face).
        var oldB = card.Find("Backing"); if (oldB) Object.DestroyImmediate(oldB.gameObject);
        var backing = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backing.name = "Backing";
        backing.transform.SetParent(card, false);
        backing.transform.localPosition = new Vector3(0f, 0f, 0.012f);
        backing.transform.localRotation = Quaternion.identity;
        backing.transform.localScale = new Vector3(1.06f, 1.08f, 1f);
        var bc = backing.GetComponent<Collider>(); if (bc) Object.DestroyImmediate(bc);
        backing.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.20f, 0.19f, 0.17f), 0f);

        // Remove the earlier accent bar experiment (it read as a glitchy blue block).
        var oldA = card.Find("AccentBar"); if (oldA) Object.DestroyImmediate(oldA.gameObject);

        // Thin, subtle divider line just under the title area (muted, not a big block).
        var divider = GameObject.CreatePrimitive(PrimitiveType.Quad);
        divider.name = "AccentBar";
        divider.transform.SetParent(card, false);
        divider.transform.localPosition = new Vector3(0f, 0.28f, -0.004f);   // below title
        divider.transform.localRotation = Quaternion.identity;
        divider.transform.localScale = new Vector3(0.78f, 0.006f, 1f);        // thin rule
        var ac = divider.GetComponent<Collider>(); if (ac) Object.DestroyImmediate(ac);
        divider.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.55f, 0.50f, 0.42f), 0f); // muted ink

        sb.AppendLine("added paper Backing (border/shadow) + thin divider rule under Card");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("SAVED.");
        return sb.ToString();
    }
}
