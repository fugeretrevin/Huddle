using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// rc19 (owner worn rc18) scene and material values:
///  - look: "ever so slightly less reflections, it looks a little too toy-like". The key light 0.95 -> 0.85; sky reflections
///    1 -> 0.55 (ViewerKeyLight.reflectionIntensity, applied at start, and the scene's own setting); every glossy MuseDesk art
///    material (smoothness above 0.45) loses 18% of its smoothness (visors 0.80 -> 0.66, ceramic 0.68 -> 0.56, brass 0.76 -> 0.62).
///    Run once: a second run would lower them again (the script refuses when the marker file says it already ran).
///  - meet: the visitor platform hovers lower (0.045 + 0.04 h -> 0.02 + 0.015 h); the portal is 1.2x (portalPadGrow).
///   unity command script run --file AgentScripts/SceneTweaksRc19.cs --entry SceneTweaksRc19.Apply --args '[true]'
/// </summary>
public static class SceneTweaksRc19
{
    const float GlossFactor = 0.82f, GlossOver = 0.45f;
    const string Marker = "Logs/rc19_gloss_applied.txt";

    public static string Apply(bool save)
    {
        var sb = new StringBuilder();
        Transform ws = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "WorkspaceRoot" && t.gameObject.scene.IsValid());
        if (ws == null) return "no WorkspaceRoot";
        var scene = ws.gameObject.scene;

        foreach (var k in Resources.FindObjectsOfTypeAll<MuseDesk.Art.ViewerKeyLight>().Where(x => x.gameObject.scene.IsValid()))
        {
            Undo.RecordObject(k, "Key light");
            sb.AppendLine("key light " + k.keyIntensity + " -> 0.85, reflections " + k.reflectionIntensity + " -> 0.55");
            k.keyIntensity = 0.85f; k.reflectionIntensity = 0.55f;
            if (k.keyLight != null) { Undo.RecordObject(k.keyLight, "Key light"); k.keyLight.intensity = 0.85f; EditorUtility.SetDirty(k.keyLight); }
            EditorUtility.SetDirty(k);
        }
        RenderSettings.reflectionIntensity = 0.55f;
        sb.AppendLine("RenderSettings.reflectionIntensity -> 0.55");

        foreach (var c in Resources.FindObjectsOfTypeAll<MuseDesk.Wall.WallHabitatController>().Where(x => x.gameObject.scene.IsValid()))
        {
            Undo.RecordObject(c, "Portal");
            sb.AppendLine("platform hover " + c.platformHoverBase + " + " + c.platformHoverPerHeight + " h -> 0.02 + 0.015 h; pad grow " + c.portalPadGrow + " -> 1.2");
            c.platformHoverBase = 0.02f; c.platformHoverPerHeight = 0.015f; c.portalPadGrow = 1.2f;
            EditorUtility.SetDirty(c);
        }

        if (System.IO.File.Exists(Marker)) sb.AppendLine("materials: already lowered once (" + Marker + "), skipped");
        else
        {
            var log = new StringBuilder();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/MuseDesk/Art/Materials" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/UI/")) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == null || !m.shader.name.Contains("Universal Render Pipeline/Lit") || !m.HasProperty("_Smoothness")) continue;
                float s = m.GetFloat("_Smoothness");
                if (s <= GlossOver) continue;
                float to = Mathf.Round(s * GlossFactor * 100f) / 100f;
                Undo.RecordObject(m, "Gloss");
                m.SetFloat("_Smoothness", to);
                EditorUtility.SetDirty(m);
                log.AppendLine(path + " " + s.ToString("F2") + " -> " + to.ToString("F2"));
            }
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText(Marker, log.ToString());
            sb.Append(log);
        }

        if (save)
        {
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            sb.AppendLine("saved");
        }
        return sb.ToString();
    }
}
