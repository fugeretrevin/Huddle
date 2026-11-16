using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Wall;

/// <summary>
/// rc12: give the Wall controller Antigravity's floor portal (WallHabitat_LargeAgentPortal, and the low-effects variant for
/// reduced motion). Idempotent; Revert() clears both fields (the ring of segments only, as before). Saves the scene.
///   powershell scripts\run_guarded.ps1 -File AgentScripts\PortalArtV1.cs -Entry PortalArtV1.Apply
/// </summary>
public static class PortalArtV1
{
    const string Full = "Assets/MuseDesk/Art/Prefabs/WallHabitat/WallHabitat_LargeAgentPortal.prefab";
    const string Low = "Assets/MuseDesk/Art/Prefabs/WallHabitat/WallHabitat_LargePortal_LowFx.prefab";

    public static string Apply() { return Set(AssetDatabase.LoadAssetAtPath<GameObject>(Full), AssetDatabase.LoadAssetAtPath<GameObject>(Low)); }
    public static string Revert() { return Set(null, null); }

    static string Set(GameObject full, GameObject low)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "PortalArtV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        var c = Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include);
        if (c == null) return "PortalArtV1: no WallHabitatController in the open scene.\n";
        Undo.RecordObject(c, "Portal art");
        c.portalArt = full;
        c.portalArtLowFx = low;
        EditorUtility.SetDirty(c);
        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        return "PortalArtV1: portalArt=" + (full != null ? full.name : "none") + " lowFx=" + (low != null ? low.name : "none") + " scene saved=" + ok + "\n";
    }
}
