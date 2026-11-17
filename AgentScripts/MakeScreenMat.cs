#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>rc20: the PC screen panel's material (URP Unlit) in Resources, so the shader ships in the APK.</summary>
public static class MakeScreenMat
{
    const string Path = "Assets/MuseDesk/Resources/PcScreenMat.mat";

    public static string Run()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(Path);
        if (existing != null) return "exists: " + existing.shader.name;
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) return "URP Unlit shader not found";
        var m = new Material(sh) { name = "PcScreenMat" };
        m.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(m, Path);
        AssetDatabase.SaveAssets();
        return "created " + Path + " (" + sh.name + ")";
    }
}
#endif
