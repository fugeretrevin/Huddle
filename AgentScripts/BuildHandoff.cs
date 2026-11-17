using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Agents;

/// <summary>
/// Wires the AgentHandoffController into MainMRScene: adds it to AppServices, binds the
/// SessionController + AgentRegistry, and (optionally) places the Collab_ArtifactToken prop in
/// the researcher's hand (inactive until a handoff). Idempotent. Run: BuildHandoff.Build.
/// </summary>
public static class BuildHandoff
{
    const string TokenFbx = "Assets/MuseDesk/Art/Models/Documents/Collab_ArtifactToken.fbx";

    public static string Build()
    {
        var scene = EditorSceneManager.OpenScene("Assets/MuseDesk/Scenes/MainMRScene.unity", OpenSceneMode.Single);
        var sb = new System.Text.StringBuilder();

        var reg = Object.FindObjectsByType<AgentRegistry>(FindObjectsInactive.Include).FirstOrDefault();
        if (reg == null) return "NO AgentRegistry in MainMRScene";

        var host = reg.gameObject;
        var handoff = host.GetComponent<AgentHandoffController>();
        if (handoff == null) handoff = host.AddComponent<AgentHandoffController>();
        handoff.controller = reg.controller;
        handoff.registry = reg;
        sb.AppendLine("AgentHandoffController on " + host.name + " (controller=" + (reg.controller != null) + ", registry set)");

        // Optional carried token in the researcher's right hand.
        var researcher = reg.agents != null ? reg.agents.FirstOrDefault(a => a != null && a.agentId == "researcher") : null;
        if (researcher != null)
        {
            var hand = researcher.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RightHand_Attach");
            var fbx = AssetDatabase.LoadMainAssetAtPath(TokenFbx) as GameObject;
            if (hand != null && fbx != null)
            {
                var existing = hand.Find("Collab_ArtifactToken");
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
                var token = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                token.name = "Collab_ArtifactToken";
                token.transform.SetParent(hand, false);
                token.transform.localPosition = Vector3.zero;
                token.transform.localScale = Vector3.one;
                token.SetActive(false);
                handoff.artifactProp = token.transform;
                sb.AppendLine("artifactProp = Collab_ArtifactToken in researcher RightHand_Attach (inactive)");
            }
            else
            {
                sb.AppendLine("artifactProp LEFT NULL (hand=" + (hand != null) + ", fbx=" + (fbx != null) + ")");
            }
        }
        else sb.AppendLine("artifactProp LEFT NULL (no researcher AgentView)");

        EditorUtility.SetDirty(handoff);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return sb.ToString();
    }
}
