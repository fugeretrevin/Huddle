using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>Does the Inter source font have a tick / cross glyph (checked without adding it to the atlas asset)?</summary>
public static class GlyphProbe
{
    public static string Run()
    {
        var sb = new StringBuilder();
        foreach (var path in new[] { "Assets/MuseDesk/Art/Fonts/Inter-Medium SDF.asset", "Assets/MuseDesk/Art/Fonts/Inter-Bold SDF.asset" })
        {
            var f = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (f == null) { sb.Append(path).Append(" missing\n"); continue; }
            sb.Append(f.name).Append(" mode=").Append(f.atlasPopulationMode).Append(" src=").Append(f.sourceFontFile != null ? f.sourceFontFile.name : "null").Append(" fallbacks=").Append(f.fallbackFontAssetTable != null ? f.fallbackFontAssetTable.Count : 0).Append('\n');
            if (f.sourceFontFile == null) continue;
            UnityEngine.TextCore.LowLevel.FontEngine.InitializeFontEngine();
            UnityEngine.TextCore.LowLevel.FontEngine.LoadFontFace(f.sourceFontFile);
            foreach (uint u in new uint[] { 0x2713, 0x2714, 0x2715, 0x2717, 0x00D7, 0x2022, 0x25A1 })
            {
                bool has = UnityEngine.TextCore.LowLevel.FontEngine.TryGetGlyphWithUnicodeValue(u, UnityEngine.TextCore.LowLevel.GlyphLoadFlags.LOAD_NO_BITMAP, out var g);
                sb.Append("  U+").Append(u.ToString("X4")).Append(" glyph=").Append(has ? g.index : 0).Append(" inAtlas=").Append(f.characterLookupTable != null && f.characterLookupTable.ContainsKey(u)).Append('\n');
            }
        }
        return sb.ToString();
    }
}
