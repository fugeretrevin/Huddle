using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Pass 7: while the agent holds the paper it should look like a clean, angled blank sheet — no
/// buttons/text. Wire hideWhileHeld to the content children, tilt the held pose, and make the card
/// read as paper (cream).
/// </summary>
public static class FixPass7
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject).ToList();
        var sb = new StringBuilder();

        var paper = all.FirstOrDefault(g => g.name == "DecisionDocument");
        if (paper == null) return "paper not found";

        // 1. Content children to hide while held (everything but the Card itself).
        var contentNames = new[] { "Badge", "Title", "Summary", "Reco", "Status", "Actions", "Details" };
        var content = new List<GameObject>();
        foreach (Transform child in paper.transform)
            if (contentNames.Contains(child.name)) content.Add(child.gameObject);

        var hpc = paper.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "HeldPaperController");
        if (hpc == null) return "no HeldPaperController";
        var ty = hpc.GetType();
        SetF(ty, hpc, "hideWhileHeld", content.ToArray());
        SetF(ty, hpc, "heldLocalEuler", new Vector3(22f, 180f, 0f));   // tilt: offered at an angle, readable side to user
        EditorUtility.SetDirty(hpc);
        sb.AppendLine($"wired hideWhileHeld ({content.Count}: {string.Join(",", content.Select(c => c.name))}) + tilt 22deg");

        // 2. Make the Card look like paper (cream, lit).
        var card = paper.transform.Find("Card");
        if (card != null)
        {
            var r = card.GetComponent<Renderer>();
            if (r != null)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var m = new Material(lit);
                var cream = new Color(0.96f, 0.94f, 0.88f);
                m.color = cream; m.SetColor("_BaseColor", cream);
                // a touch of emission so it stays legible against bright passthrough
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", cream * 0.25f);
                r.sharedMaterial = m;
                sb.AppendLine("card -> cream paper material");
            }
        }

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
