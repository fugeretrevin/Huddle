using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The Building-Block grab cube shipped with a Built-in "Standard" material, which renders
/// magenta in this URP project (and the orange tint missed because Standard uses _Color, not
/// _BaseColor). Swap it to URP/Lit with a warm orange so the grab prop reads correctly on device.
/// </summary>
public static class FixGrabMaterial
{
    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var grab = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name.Contains("Grabbable Item"));
        if (grab == null) return "no Grabbable Item";

        var rend = grab.GetComponent<Renderer>();
        if (rend == null) return "no Renderer on Grabbable Item";

        var urp = Shader.Find("Universal Render Pipeline/Lit");
        if (urp == null) return "URP/Lit shader not found";

        var orange = new Color(0.95f, 0.55f, 0.15f);
        var mat = new Material(urp) { name = "GrabCube_URP" };
        mat.SetColor("_BaseColor", orange);
        rend.sharedMaterial = mat;
        // Clear any stale per-renderer override from the earlier (ineffective) tint pass.
        rend.SetPropertyBlock(null);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return $"Grabbable Item material -> URP/Lit orange (was {rend.sharedMaterial.shader.name})";
    }
}
