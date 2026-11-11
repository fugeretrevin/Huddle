using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Workspace;

/// <summary>
/// TASK-03 polish: adds AgentMovement (researcher delivery walk), sets the delivery anchor,
/// and gives each agent a distinct held prop + a station prop so the three roles read at a
/// glance. Primitive art — cohesive low-poly meshes can replace it later.
/// </summary>
public static class BuildAgentPolish
{
    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects();
        var sb = new StringBuilder();

        var views = all.SelectMany(r => r.GetComponentsInChildren<AgentView>(true)).ToList();
        var anchors = all.SelectMany(r => r.GetComponentsInChildren<WorkstationAnchor>(true))
                         .ToDictionary(a => a.role, a => a);

        // delivery anchor on the registry
        var registry = all.SelectMany(r => r.GetComponentsInChildren<AgentRegistry>(true)).FirstOrDefault();
        if (registry != null && anchors.TryGetValue("delivery", out var dz))
        {
            registry.deliveryAnchor = dz.transform;
            sb.AppendLine("registry.deliveryAnchor set");
        }

        foreach (var v in views)
        {
            var go = v.gameObject;
            if (go.GetComponent<AgentMovement>() == null) go.AddComponent<AgentMovement>();

            var bob = v.bob != null ? v.bob : go.transform;
            // held prop (distinct silhouette), only once
            if (bob.Find("HeldProp") == null)
                AddHeldProp(bob, v.agentId);

            // station prop at the matching station anchor
            if (anchors.TryGetValue(v.agentId, out var anchor) && anchor.transform.Find("StationProp") == null)
                AddStationProp(anchor.transform, v.agentId);
            sb.AppendLine($"{v.agentId}: movement + props");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        sb.AppendLine("saved");
        return sb.ToString();
    }

    static void AddHeldProp(Transform bob, string role)
    {
        switch (role)
        {
            case "researcher":   // open book held up to read
                var book = Prim(PrimitiveType.Cube, bob, "HeldProp",
                    new Vector3(0f, 0.055f, 0.03f), new Vector3(0.03f, 0.022f, 0.004f),
                    Quaternion.Euler(60f, 0f, 0f), new Color(0.80f, 0.68f, 0.45f));
                break;
            case "developer":    // laptop
                var baseP = Prim(PrimitiveType.Cube, bob, "HeldProp",
                    new Vector3(0f, 0.045f, 0.035f), new Vector3(0.038f, 0.004f, 0.026f),
                    Quaternion.identity, new Color(0.25f, 0.27f, 0.30f));
                Prim(PrimitiveType.Cube, baseP.transform, "Screen",
                    new Vector3(0f, 2.2f, -1.0f), new Vector3(1f, 0.08f, 0.9f),
                    Quaternion.Euler(-105f, 0f, 0f), new Color(0.35f, 0.75f, 0.95f));
                break;
            default:             // tester: clipboard
                Prim(PrimitiveType.Cube, bob, "HeldProp",
                    new Vector3(0f, 0.05f, 0.03f), new Vector3(0.026f, 0.034f, 0.003f),
                    Quaternion.Euler(70f, 0f, 0f), new Color(0.92f, 0.92f, 0.90f));
                break;
        }
    }

    static void AddStationProp(Transform anchor, string role)
    {
        switch (role)
        {
            case "researcher":   // book pile
                Prim(PrimitiveType.Cube, anchor, "StationProp", new Vector3(0.05f, 0.012f, 0f),
                    new Vector3(0.05f, 0.016f, 0.07f), Quaternion.identity, new Color(0.55f, 0.30f, 0.25f));
                Prim(PrimitiveType.Cube, anchor, "StationProp2", new Vector3(0.05f, 0.03f, 0.005f),
                    new Vector3(0.046f, 0.014f, 0.066f), Quaternion.Euler(0f, 8f, 0f), new Color(0.30f, 0.45f, 0.60f));
                break;
            case "developer":    // monitor on desk
                var stand = Prim(PrimitiveType.Cube, anchor, "StationProp", new Vector3(-0.05f, 0.02f, 0f),
                    new Vector3(0.012f, 0.04f, 0.012f), Quaternion.identity, new Color(0.2f, 0.2f, 0.22f));
                Prim(PrimitiveType.Cube, stand.transform, "Screen", new Vector3(0f, 1.2f, 0f),
                    new Vector3(5f, 2.4f, 0.3f), Quaternion.identity, new Color(0.30f, 0.70f, 0.95f));
                break;
            default:             // tester console
                Prim(PrimitiveType.Cube, anchor, "StationProp", new Vector3(0.05f, 0.015f, 0f),
                    new Vector3(0.06f, 0.03f, 0.05f), Quaternion.identity, new Color(0.22f, 0.24f, 0.26f));
                break;
        }
    }

    static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 lp,
                           Vector3 ls, Quaternion lr, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp;
        go.transform.localScale = ls;
        go.transform.localRotation = lr;
        var c = go.GetComponent<Collider>(); if (c) Object.DestroyImmediate(c);
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"), color);
            r.SetPropertyBlock(mpb);
        }
        return go;
    }
}
