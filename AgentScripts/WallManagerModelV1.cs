using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Wall;

/// <summary>
/// Owner decision (rc8 round 1): the Wall Manager is the dedicated Agent_Manager model, not the Developer clone with a
/// lavender cap. Wires WallHabitatController.managerSource to the Agent_Manager prefab (same 43-bone rig and the same
/// 77 state names as the desk agents; its own MuseDesk_WallAgent controller with the Manager_* clips).
/// Revert clears the field (the view falls back to the dressed Developer clone).
///   scripts\run_guarded.ps1 -File AgentScripts\WallManagerModelV1.cs -Entry WallManagerModelV1.Apply
/// </summary>
public static class WallManagerModelV1
{
    const string PrefabPath = "Assets/MuseDesk/Art/Prefabs/WallHabitat/Agent_Manager.prefab";

    public static string Apply() { return Set(true); }
    public static string Revert() { return Set(false); }

    static string Set(bool on)
    {
        var sb = new StringBuilder("WallManagerModelV1." + (on ? "Apply" : "Revert") + "\n");
        var wh = Object.FindFirstObjectByType<WallHabitatController>(FindObjectsInactive.Include);
        if (wh == null) return sb.Append("  no WallHabitatController").ToString();
        GameObject prefab = on ? AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) : null;
        if (on && prefab == null) return sb.Append("  missing " + PrefabPath).ToString();
        if (on && prefab.GetComponentInChildren<Animator>(true) == null) return sb.Append("  the prefab has no Animator").ToString();
        if (wh.managerSource == prefab) return sb.AppendLine("  already set; nothing to do").ToString();
        Undo.RecordObject(wh, "Wall Manager model");
        wh.managerSource = prefab;
        EditorUtility.SetDirty(wh);
        sb.AppendLine("  managerSource = " + (prefab != null ? PrefabPath : "(none: Developer clone + cap)"));
        EditorSceneManager.MarkSceneDirty(wh.gameObject.scene);
        sb.AppendLine("  scene saved=" + EditorSceneManager.SaveScene(wh.gameObject.scene));
        return sb.ToString();
    }
}
