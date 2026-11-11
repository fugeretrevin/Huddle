using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Does the paper's grab collider actually overlap the visible card? Dump world geometry.</summary>
public static class DiagPaperGeom
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
        var sb = new StringBuilder();

        var paper = all.FirstOrDefault(g => g.name == "DecisionDocument");
        if (paper == null) { return "DecisionDocument not found"; }

        // Temporarily activate so bounds/renderers resolve (then restore).
        bool wasActive = paper.activeSelf;
        paper.SetActive(true);

        sb.AppendLine("=== DecisionDocument root ===");
        sb.AppendLine($"  parent chain: {Chain(paper)}");
        sb.AppendLine($"  worldPos={paper.transform.position} lossyScale={paper.transform.lossyScale} localScale={paper.transform.localScale}");

        var bc = paper.GetComponent<BoxCollider>();
        if (bc != null)
        {
            Vector3 worldCenter = paper.transform.TransformPoint(bc.center);
            Vector3 worldSize = Vector3.Scale(bc.size, paper.transform.lossyScale);
            sb.AppendLine($"  BoxCollider: localCenter={bc.center} localSize={bc.size} isTrigger={bc.isTrigger}");
            sb.AppendLine($"    -> WORLD center={worldCenter} size={worldSize}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Child renderers (the visible card + text) world bounds ===");
        foreach (var r in paper.GetComponentsInChildren<Renderer>(true))
        {
            var b = r.bounds; // world
            sb.AppendLine($"  '{r.gameObject.name}' center={b.center} size={b.size} active={r.gameObject.activeInHierarchy}");
        }

        // Compare: is the collider world-center near the visible card center?
        var cardRend = paper.GetComponentsInChildren<Renderer>(true)
            .FirstOrDefault(r => r.gameObject.name.ToLower().Contains("card") || r.GetType().Name == "MeshRenderer");
        if (bc != null && cardRend != null)
        {
            var d = Vector3.Distance(paper.transform.TransformPoint(bc.center), cardRend.bounds.center);
            sb.AppendLine($"\n  collider-center to '{cardRend.gameObject.name}'-center distance = {d:F3} m");
        }

        paper.SetActive(wasActive);
        return sb.ToString();
    }

    static string Chain(GameObject go)
    {
        var p = go.name; var t = go.transform.parent;
        while (t != null) { p = t.name + "/" + p; t = t.parent; }
        return p;
    }
}
