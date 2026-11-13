using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.TextCore.LowLevel;

/// <summary>The Editor is shared with another tool that opens its own preview scenes. Call Ensure
/// before any production builder: it opens MainMRScene only if the scene currently open has no
/// unsaved changes, so nobody's work is discarded.</summary>
public static class SceneGuard
{
    const string Main = "Assets/MuseDesk/Scenes/MainMRScene.unity";

    public static string Ensure()
    {
        var s = EditorSceneManager.GetActiveScene();
        if (s.path == Main) return "OK main scene open" + (s.isDirty ? " (dirty)" : "");
        if (EditorApplication.isPlaying) return "BLOCKED play mode is running in " + s.path;
        if (s.isDirty) return "BLOCKED other scene has unsaved changes: " + s.path;
        string was = s.path;
        EditorSceneManager.OpenScene(Main, OpenSceneMode.Single);
        return "OK opened main scene (was " + was + ")";
    }

    public static string Which()
    {
        var s = EditorSceneManager.GetActiveScene();
        return s.path + " dirty=" + s.isDirty + " time=" + System.DateTime.Now.ToString("HH:mm:ss");
    }
}



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

    /// <summary>Real Inter Bold: a second font asset registered as the regular asset's 700 weight, so
    /// FontStyles.Bold renders true bold glyphs instead of TextMeshPro's synthetic bold.</summary>
    public static string CreateBold()
    {
        const string ttf = "Assets/MuseDesk/Art/Fonts/Inter-Bold.ttf";
        const string path = "Assets/MuseDesk/Art/Fonts/Inter-Bold SDF.asset";
        var regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
        if (regular == null) return "no regular asset";
        var bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (bold == null)
        {
            AssetDatabase.ImportAsset(ttf, ImportAssetOptions.ForceUpdate);
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
            if (font == null) return "bold ttf not imported";
            bold = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            bold.name = "Inter-Bold SDF";
            AssetDatabase.CreateAsset(bold, path);
            bold.material.name = "Inter-Bold SDF Material";
            bold.atlasTexture.name = "Inter-Bold SDF Atlas";
            AssetDatabase.AddObjectToAsset(bold.material, bold);
            AssetDatabase.AddObjectToAsset(bold.atlasTexture, bold);
            string ascii = "";
            for (int c = 32; c < 127; c++) ascii += (char)c;
            bold.TryAddCharacters(ascii, out _);
            bold.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { regular };
            EditorUtility.SetDirty(bold);
        }
        var table = regular.fontWeightTable;
        table[7].regularTypeface = bold;   // index 7 = weight 700
        EditorUtility.SetDirty(regular);
        AssetDatabase.SaveAssets();
        return $"bold weight registered: {path}; regular.fontWeightTable[7]={(regular.fontWeightTable[7].regularTypeface != null ? regular.fontWeightTable[7].regularTypeface.name : "null")}";
    }

    const string MediumPath = "Assets/MuseDesk/Art/Fonts/Inter-Medium SDF.asset";

    /// <summary>Inter Medium as the base face: Regular reads thin and pale at 5 to 9 mm in the
    /// headset. Bold (700) is the real Inter Bold asset.</summary>
    public static string CreateMedium()
    {
        const string ttf = "Assets/MuseDesk/Art/Fonts/Inter-Medium.ttf";
        var bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/MuseDesk/Art/Fonts/Inter-Bold SDF.asset");
        var liberation = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        var med = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MediumPath);
        if (med == null)
        {
            AssetDatabase.ImportAsset(ttf, ImportAssetOptions.ForceUpdate);
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
            if (font == null) return "medium ttf not imported";
            med = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            med.name = "Inter-Medium SDF";
            AssetDatabase.CreateAsset(med, MediumPath);
            med.material.name = "Inter-Medium SDF Material";
            med.atlasTexture.name = "Inter-Medium SDF Atlas";
            AssetDatabase.AddObjectToAsset(med.material, med);
            AssetDatabase.AddObjectToAsset(med.atlasTexture, med);
            string ascii = "";
            for (int c = 32; c < 127; c++) ascii += (char)c;
            med.TryAddCharacters(ascii, out _);
            if (liberation != null) med.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { liberation };
        }
        if (bold != null) med.fontWeightTable[7].regularTypeface = bold;
        EditorUtility.SetDirty(med);
        AssetDatabase.SaveAssets();
        return $"medium base: {MediumPath}; bold weight={(med.fontWeightTable[7].regularTypeface != null ? med.fontWeightTable[7].regularTypeface.name : "null")}";
    }

    public static string Apply()
    {
        var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MediumPath);
        if (fa == null) fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
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


public static class GuardedRun
{
    public static string Go()
    {
        string g = SceneGuard.Ensure();
        if (!g.StartsWith("OK")) return "GUARD " + g + " : builder not run";
        object r = SetupInterFont.Apply();
        return "GUARD " + g + "\n" + (r == null ? "" : r.ToString());
    }
}
