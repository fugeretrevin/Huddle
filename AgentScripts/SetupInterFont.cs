using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using UnityEngine.TextCore.LowLevel;

/// <summary>Creates the Inter TMP font asset (dynamic, ASCII pre-baked, LiberationSans fallback) and
/// applies it to every TMP label in the open scene. Idempotent.</summary>
public static class SetupInterFont
{
    const string TtfPath = "Assets/MuseDesk/Art/Fonts/Inter-Regular.ttf";
    const string AssetPath = "Assets/MuseDesk/Art/Fonts/Inter SDF.asset";

    public static string Create()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
        if (existing != null) return "exists: " + AssetPath;
        AssetDatabase.ImportAsset(TtfPath, ImportAssetOptions.ForceUpdate);
        var font = AssetDatabase.LoadAssetAtPath<Font>(TtfPath);
        if (font == null) return "font not imported: " + TtfPath;
        var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        fa.name = "Inter SDF";
        AssetDatabase.CreateAsset(fa, AssetPath);
        fa.material.name = "Inter SDF Material";
        fa.atlasTexture.name = "Inter SDF Atlas";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa);
        string ascii = "";
        for (int c = 32; c < 127; c++) ascii += (char)c;
        bool ok = fa.TryAddCharacters(ascii, out string missing);
        var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        if (fallback != null) fa.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallback };
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        return $"created {AssetPath}; ascii ok={ok} missing='{missing}' fallback={(fallback != null)} shader={fa.material.shader.name}";
    }

    public static string Apply()
    {
        var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
        if (fa == null) return "no font asset; run Create first";
        int n = 0;
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var t in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TMP_Text>(true)))
        {
            if (t.font == fa) continue;
            Undo.RecordObject(t, "font");
            t.font = fa;
            t.fontSharedMaterial = fa.material;
            EditorUtility.SetDirty(t);
            n++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"applied Inter to {n} labels";
    }
}
