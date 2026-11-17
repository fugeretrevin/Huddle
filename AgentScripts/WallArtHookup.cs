using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MuseDesk.Wall;
using MuseDesk.Spatial.Wall;

/// <summary>
/// Fills Assets/MuseDesk/Wall/HabitatArtManifest.asset with the two complete Wall Habitat art prefabs
/// (Full -> WallHabitat_Complete_3Tier, Compact -> WallHabitat_Complete_2Tier), placed at WallContact, replacing the
/// proxy structure, doors, stop lights, printer and cabin visuals. Stage geometry stays the layout's. Idempotent.
/// Rollback: Revert() (empties the manifest) or set useHabitatArt = false on the asset. Scene is not touched.
/// </summary>
public static class WallArtHookup
{
    const string Asset = "Assets/MuseDesk/Wall/HabitatArtManifest.asset";
    const string Dir = "Assets/MuseDesk/Art/Prefabs/WallHabitat/";

    public static string Apply()
    {
        var m = AssetDatabase.LoadAssetAtPath<HabitatArtManifest>(Asset);
        if (m == null) return "manifest asset missing";
        m.entries = new List<HabitatArtManifest.Entry>
        {
            Make(WallLayoutKind.Full, "WallHabitat_Complete_3Tier"),
            Make(WallLayoutKind.Compact, "WallHabitat_Complete_2Tier"),
        };
        m.useHabitatArt = true;
        m.artVersion = "wall-habitat-fit-310e070";
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
        return "manifest filled: " + m.entries.Count + " entries, useHabitatArt=true, version " + m.artVersion;
    }

    public static string Revert()
    {
        var m = AssetDatabase.LoadAssetAtPath<HabitatArtManifest>(Asset);
        if (m == null) return "manifest asset missing";
        m.entries = new List<HabitatArtManifest.Entry>();
        m.artVersion = "proxy-0";
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
        return "manifest emptied (proxy-0)";
    }

    static HabitatArtManifest.Entry Make(WallLayoutKind kind, string prefab)
    {
        var layout = HabitatLayoutBuilder.Build(kind);
        var hide = new List<string> { "Structure (proxy art)", "TaskPrinter", "TaskPrinterSlot", "BeaconPost" };
        foreach (var f in layout.floors)
        {
            hide.Add("Door_" + f.id + "_L");
            hide.Add("Door_" + f.id + "_R");
            hide.Add("StopLight_" + f.id);
        }
        return new HabitatArtManifest.Entry
        {
            socketId = "WallContact",
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + prefab + ".prefab"),
            uniformScale = 1f,
            hideProxies = hide.ToArray(),
            hideRenderersOf = new[] { "ElevatorCabin" },
            enabled = true,
            full = kind == WallLayoutKind.Full,
            compact = kind == WallLayoutKind.Compact,
        };
    }
}
