using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>What makes the scene shiny: reflection settings and the smoothness / specular state of every Lit material in use.</summary>
public static class GlossProbe
{
    public static string Run()
    {
        var sb = new StringBuilder();
        sb.Append("reflectionIntensity=").Append(RenderSettings.reflectionIntensity).Append(" defaultReflectionMode=").Append(RenderSettings.defaultReflectionMode)
          .Append(" customReflection=").Append(RenderSettings.customReflectionTexture != null ? RenderSettings.customReflectionTexture.name : "null")
          .Append(" ambientMode=").Append(RenderSettings.ambientMode).Append(" probes=").Append(Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length).Append('\n');
        var mats = new Dictionary<Material, int>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var m in r.sharedMaterials) if (m != null) { mats.TryGetValue(m, out int n); mats[m] = n + 1; }
        var rows = new List<string>();
        int lit = 0, glossy = 0;
        foreach (var kv in mats)
        {
            var m = kv.Key;
            if (m.shader == null || !m.shader.name.Contains("Lit") || m.shader.name.Contains("Unlit")) continue;
            lit++;
            float s = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : -1f;
            float met = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : -1f;
            bool specOff = m.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"), envOff = m.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF");
            if (s > 0.45f) glossy++;
            rows.Add(string.Format("{0,-44} {1,-28} smooth={2:F2} metal={3:F2} specOff={4} envOff={5} uses={6} path={7}", m.name, m.shader.name, s, met, specOff, envOff, kv.Value,
                UnityEditor.AssetDatabase.GetAssetPath(m)));
        }
        sb.Append("lit materials=").Append(lit).Append(" smooth>0.45: ").Append(glossy).Append('\n');
        foreach (var row in rows.OrderByDescending(x => x)) sb.Append(row).Append('\n');
        return sb.ToString();
    }
}
