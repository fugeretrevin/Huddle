using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DiagHeld
{
    public static string Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
        var paper = all.FirstOrDefault(g => g.name == "DecisionDocument");
        if (paper == null) return "paper not found";
        var sb = new StringBuilder();
        var hpc = paper.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "HeldPaperController");
        if (hpc == null) return "no HeldPaperController";
        sb.AppendLine("HeldPaperController fields:");
        foreach (var f in hpc.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            var v = f.GetValue(hpc);
            string desc = v == null ? "NULL" : (v is Component c ? $"{c.GetType().Name}@{c.gameObject.name}" : v.ToString());
            sb.AppendLine($"  {f.Name} = {desc}");
        }
        // Also the DocumentDeliveryController paperRoot wiring
        var dd = all.SelectMany(g => g.GetComponents<Component>()).FirstOrDefault(c => c != null && c.GetType().Name == "DocumentDeliveryController");
        if (dd != null)
        {
            sb.AppendLine("DocumentDeliveryController fields:");
            foreach (var f in dd.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var v = f.GetValue(dd);
                string desc = v == null ? "NULL" : (v is Component c ? $"{c.GetType().Name}@{c.gameObject.name}" : (v is GameObject go ? $"GO@{go.name}" : v.ToString()));
                sb.AppendLine($"  {f.Name} = {desc}");
            }
        }
        return sb.ToString();
    }
}
