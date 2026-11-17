using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>Do the font assets the scene's texts use (and their fallbacks) contain the characters our strings use?</summary>
public static class GlyphCheck
{
    const string Chars = "·…—–’“”°×✓";

    public static string Run()
    {
        var sb = new StringBuilder();
        var fonts = Resources.FindObjectsOfTypeAll<TMP_Text>().Where(t => t.gameObject.scene.IsValid() && t.font != null)
            .GroupBy(t => t.font).OrderByDescending(g => g.Count());
        foreach (var g in fonts)
        {
            TMP_FontAsset f = g.Key;
            sb.Append(f.name).Append(" (").Append(g.Count()).Append(" texts): ");
            foreach (char ch in Chars)
            {
                bool has = f.HasCharacter(ch, true, true);
                sb.Append(ch).Append(has ? "+" : "-").Append(' ');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
