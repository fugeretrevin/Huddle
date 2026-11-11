using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Report the shader on each visible prop so we can spot a non-URP (magenta) material.</summary>
public static class InspectGrab
{
    public static string Dump()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder();
        foreach (var r in scene.GetRootGameObjects()
                     .SelectMany(ro => ro.GetComponentsInChildren<Renderer>(true)))
        {
            var mat = r.sharedMaterial;
            string shader = mat == null ? "NULL MATERIAL" : (mat.shader == null ? "NULL SHADER" : mat.shader.name);
            // Only show the interesting props to keep it short.
            string n = r.gameObject.name;
            if (n.Contains("Grabbable") || n.Contains("Card") || n.Contains("Back") ||
                n.Contains("Agent") || n.Contains("Body") || n.Contains("Head") || n.Contains("Eye"))
                sb.Append($"{Path(r.transform)}  ->  {shader}\n");
        }
        return sb.ToString();
    }

    static string Path(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}
