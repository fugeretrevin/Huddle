#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// rc20 (owner, worn rc19: "the agents look a bit softer yes but still plasticy"): the agents' body and clothing
/// materials go matte, like soft vinyl or clay. Smoothness 0.28 and no sky reflections on those materials (the sheen that
/// read as plastic); visors, lenses, glass and glows keep their shine so the bodies read softer by contrast.
/// Run once; the marker file stops a second run from compounding.
/// </summary>
public static class MatteAgentsRc20
{
    const string Marker = "Logs/rc20_matte_applied.txt";
    const float Smooth = 0.28f;
    static readonly string[] Names =
    {
        "Mat_CreamCeramic", "Mat_CheekBlush", "Mat_DarkBoots", "Mat_Developer_Amber", "Mat_Developer_Coral",
        "Mat_Researcher_Lavender", "Mat_Researcher_Teal", "Mat_Tester_Mint", "Mat_Tester_SlateBlue", "Mat_WarmBeige",
    };

    public static string Run()
    {
        if (File.Exists(Marker)) return "already applied: " + File.ReadAllText(Marker).Replace("\n", " | ");
        var sb = new StringBuilder();
        foreach (var n in Names)
        {
            string path = "Assets/MuseDesk/Art/Materials/" + n + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { sb.Append(path).Append(" MISSING\n"); continue; }
            float before = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : -1f;
            if (before > Smooth) m.SetFloat("_Smoothness", Smooth);
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(m);
            sb.Append(path).Append(' ').Append(before.ToString("F2")).Append(" -> ").Append(Mathf.Min(before, Smooth).ToString("F2")).Append(", sky reflections off\n");
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText(Marker, sb.ToString());
        return sb.ToString();
    }
}
#endif
