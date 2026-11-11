using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using MuseDesk.Agents;
using MuseDesk.Networking;
using MuseDesk.Workspace;

/// <summary>
/// Creates three distinct primitive agents on the existing station anchors and wires an
/// AgentRegistry to the SessionController, so they react to reducer agent-state events.
/// Primitive art is a placeholder (TASK-03 polish swaps in low-poly meshes + Animator).
/// </summary>
public static class BuildAgents
{
    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects();
        var services = all.FirstOrDefault(r => r.name == "AppServices");
        var controller = services != null ? services.GetComponent<SessionController>() : null;
        var sb = new StringBuilder();

        var anchors = all.SelectMany(r => r.GetComponentsInChildren<WorkstationAnchor>(true))
                         .ToDictionary(a => a.role, a => a);

        var views = new System.Collections.Generic.List<AgentView>();
        foreach (var (role, name, display) in new[] {
            ("researcher", "Researcher", "Researcher"),
            ("developer", "Developer", "Developer"),
            ("tester", "Tester", "Tester") })
        {
            if (!anchors.TryGetValue(role, out var anchor)) { sb.AppendLine($"no anchor for {role}"); continue; }
            // Don't duplicate on re-run.
            var existing = anchor.GetComponentInChildren<AgentView>(true);
            if (existing != null) { views.Add(existing); sb.AppendLine($"{role}: exists"); continue; }

            var agent = new GameObject($"Agent_{display}");
            agent.transform.SetParent(anchor.transform, false);

            var bob = new GameObject("Bob").transform;
            bob.SetParent(agent.transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(bob, false);
            body.transform.localScale = new Vector3(0.045f, 0.05f, 0.045f);
            body.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            StripCollider(body);

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(bob, false);
            head.transform.localScale = Vector3.one * 0.04f;
            head.transform.localPosition = new Vector3(0f, 0.11f, 0f);
            StripCollider(head);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(agent.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.17f, 0f);
            labelGo.transform.localScale = Vector3.one * 0.01f;
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = display;
            tmp.fontSize = 6f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(12f, 6f);

            var view = agent.AddComponent<AgentView>();
            view.agentId = role;
            view.displayName = display;
            view.bob = bob;
            view.bodyRenderer = body.GetComponent<Renderer>();
            view.label = tmp;
            views.Add(view);
            sb.AppendLine($"{role}: created");
        }

        if (services != null)
        {
            var reg = services.GetComponent<AgentRegistry>() ?? services.AddComponent<AgentRegistry>();
            reg.controller = controller;
            reg.agents = views;
            sb.AppendLine($"AgentRegistry wired (controller={controller != null}, agents={views.Count})");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("scene saved");
        return sb.ToString();
    }

    static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c != null) Object.DestroyImmediate(c);
    }
}
