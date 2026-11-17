using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>What drives the look: pipeline asset per quality level (Android), renderer post-processing, HDR, cameras, volumes, lights, ambient.</summary>
public static class LookProbe
{
    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("default RP: " + (GraphicsSettings.defaultRenderPipeline ? GraphicsSettings.defaultRenderPipeline.name : "-"));
        string[] names = QualitySettings.names;
        var perPlatform = typeof(QualitySettings).GetMethod("GetActiveQualityLevelsForPlatform", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        for (int i = 0; i < names.Length; i++)
        {
            var rp = QualitySettings.GetRenderPipelineAssetAt(i);
            sb.AppendLine("quality " + i + " " + names[i] + " rp=" + (rp ? rp.name : "-"));
        }
        sb.AppendLine("current quality " + QualitySettings.GetQualityLevel());
        foreach (var a in Resources.FindObjectsOfTypeAll<UniversalRenderPipelineAsset>())
        {
            sb.AppendLine("URP " + a.name + " hdr=" + a.supportsHDR + " msaa=" + a.msaaSampleCount + " scale=" + a.renderScale + " shadows=" + a.supportsMainLightShadows + " addLights=" + a.additionalLightsRenderingMode);
            var so = new SerializedObject(a);
            var list = so.FindProperty("m_RendererDataList");
            for (int j = 0; list != null && j < list.arraySize; j++)
            {
                var rd = list.GetArrayElementAtIndex(j).objectReferenceValue as UniversalRendererData;
                if (rd == null) continue;
                var rso = new SerializedObject(rd);
                var pp = rso.FindProperty("postProcessData");
                sb.AppendLine("   renderer " + rd.name + " postProcessData=" + (pp != null && pp.objectReferenceValue != null) + " features=" + string.Join(",", rd.rendererFeatures.Select(f => f ? f.name : "null")));
            }
        }
        foreach (var c in Resources.FindObjectsOfTypeAll<Camera>().Where(c => c.gameObject.scene.IsValid()))
        {
            var d = c.GetUniversalAdditionalCameraData();
            sb.AppendLine("camera " + c.name + " enabled=" + c.enabled + " active=" + c.gameObject.activeInHierarchy + " clear=" + c.clearFlags + " bg=" + c.backgroundColor + " post=" + (d != null && d.renderPostProcessing) + " aa=" + (d != null ? d.antialiasing.ToString() : "-"));
        }
        foreach (var v in Resources.FindObjectsOfTypeAll<Volume>().Where(v => v.gameObject.scene.IsValid()))
            sb.AppendLine("volume " + v.name + " global=" + v.isGlobal + " profile=" + (v.sharedProfile ? v.sharedProfile.name + " [" + string.Join(",", v.sharedProfile.components.Select(x => x.GetType().Name + (x.active ? "" : "(off)"))) + "]" : "-"));
        foreach (var l in Resources.FindObjectsOfTypeAll<Light>().Where(l => l.gameObject.scene.IsValid()))
            sb.AppendLine("light " + l.name + " " + l.type + " enabled=" + l.enabled + " active=" + l.gameObject.activeInHierarchy + " int=" + l.intensity + " col=" + l.color + " shadows=" + l.shadows + " rot=" + l.transform.eulerAngles.ToString("F0"));
        sb.AppendLine("ambient mode=" + RenderSettings.ambientMode + " sky=" + RenderSettings.ambientSkyColor + " eq=" + RenderSettings.ambientEquatorColor + " ground=" + RenderSettings.ambientGroundColor + " light=" + RenderSettings.ambientLight + " intensity=" + RenderSettings.ambientIntensity);
        sb.AppendLine("fog=" + RenderSettings.fog + " skybox=" + (RenderSettings.skybox ? RenderSettings.skybox.name : "-"));
        return sb.ToString();
    }
}
