using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;

/// <summary>
/// Gives the Quest quality level a project-owned URP asset (it pointed at one inside
/// Library/PackageCache, which cannot be tuned or versioned) and turns off pipeline features the
/// scene does not use: it has no lights, so shadows, additional lights and light cookies only cost
/// shader variants and setup time. MSAA 4x, HDR off and render scale 1 are kept.
/// Report() changes nothing.  Apply() is idempotent.
///   unity command script run --file AgentScripts/QuestRenderSetup.cs --entry QuestRenderSetup.Apply
/// </summary>
public static class QuestRenderSetup
{
    const string QuestAsset = "Assets/Settings/URP_Quest.asset";

    static string Describe(UniversalRenderPipelineAsset a)
    {
        if (a == null) return "null";
        var so = new SerializedObject(a);
        string P(string n) { var p = so.FindProperty(n); return p == null ? "?" : p.propertyType == SerializedPropertyType.Boolean ? p.boolValue.ToString() : p.propertyType == SerializedPropertyType.Float ? p.floatValue.ToString("F2") : p.intValue.ToString(); }
        return $"{AssetDatabase.GetAssetPath(a)} | msaa={a.msaaSampleCount} hdr={a.supportsHDR} scale={a.renderScale:F2} mainLightShadows={P("m_MainLightShadowsSupported")} " +
               $"addLightsMode={P("m_AdditionalLightsRenderingMode")} addLightShadows={P("m_AdditionalLightShadowsSupported")} cookies={P("m_SupportsLightCookies")} " +
               $"shadowDist={a.shadowDistance:F0} depthTex={a.supportsCameraDepthTexture} opaqueTex={a.supportsCameraOpaqueTexture} srpBatcher={a.useSRPBatcher} lodCrossFade={P("m_EnableLODCrossFade")}";
    }

    public static string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("default pipeline: " + Describe(GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset));
        var names = QualitySettings.names;
        for (int i = 0; i < names.Length; i++)
            sb.AppendLine($"quality[{i}] {names[i]}: " + Describe(QualitySettings.GetRenderPipelineAssetAt(i) as UniversalRenderPipelineAsset));
        sb.AppendLine("active quality (editor): " + QualitySettings.GetQualityLevel());
        return sb.ToString();
    }

    public static string Apply()
    {
        var sb = new StringBuilder();
        var names = QualitySettings.names;
        int quest = System.Array.FindIndex(names, n => n.ToLowerInvariant().Contains("quest"));
        if (quest < 0) return "No quality level with 'quest' in its name; nothing changed.\n" + Report();

        var current = QualitySettings.GetRenderPipelineAssetAt(quest) as UniversalRenderPipelineAsset;
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(QuestAsset);
        if (asset == null)
        {
            // Start from the project's own Medium asset (same renderer the Android default used).
            var source = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/UniversalRenderPipelineAsset.asset");
            if (source == null) source = current;
            if (source == null) return "No URP asset to copy from.\n" + Report();
            string src = AssetDatabase.GetAssetPath(source);
            if (!src.StartsWith("Assets/")) return "Source URP asset is not under Assets/: " + src;
            if (!AssetDatabase.CopyAsset(src, QuestAsset)) return "Copy failed.";
            asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(QuestAsset);
            sb.AppendLine("created " + QuestAsset + " from " + src);
        }

        var so = new SerializedObject(asset);
        void Set(string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; else sb.AppendLine("  (no property " + name + ")"); }
        void SetInt(string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; else sb.AppendLine("  (no property " + name + ")"); }
        Set("m_MainLightShadowsSupported", false);
        Set("m_AdditionalLightShadowsSupported", false);
        SetInt("m_AdditionalLightsRenderingMode", 0);          // Disabled: the scene has no lights
        Set("m_SupportsLightCookies", false);
        Set("m_EnableLODCrossFade", false);
        Set("m_SupportsHDR", false);
        SetInt("m_MSAA", 4);
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(asset);

        // Assign it to the Quest quality level.
        var qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        var levels = qs.FindProperty("m_QualitySettings");
        var level = levels.GetArrayElementAtIndex(quest);
        level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = asset;
        qs.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        sb.AppendLine($"quality[{quest}] '{names[quest]}' now uses {QuestAsset}");
        sb.Append(Report());
        return sb.ToString();
    }
}
