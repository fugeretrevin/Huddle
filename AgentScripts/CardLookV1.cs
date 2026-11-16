using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MuseDesk.Wall;

/// <summary>
/// rc12 (owner: the buttons and papers after Manager / Meet should look nicer). Gives the Wall cards the app's unlit UI
/// materials, so they look like the task dock and the desk paper instead of grey lit boxes:
///   cardPlateMaterial  = OfficeV2_Plate (the task dock's dark plate)
///   cardPaperMaterial  = Paper_Sheet    (the decision paper's cream sheet; the Manager plan)
///   cardAccentMaterial = PaperV2_Teal   (the teal accent bar)
/// Also moves the visit / Meet card up so it no longer overlaps the task dock (visitCardLocal). Idempotent; Revert() clears
/// the materials (the old lit look) and puts the card back. Saves the scene.
///   powershell scripts\run_guarded.ps1 -File AgentScripts\CardLookV1.cs -Entry CardLookV1.Apply
/// </summary>
public static class CardLookV1
{
    const string Dir = "Assets/MuseDesk/Art/Materials/UI/";
    static readonly Vector3 OldVisit = new Vector3(0.26f, 0.13f, 0.40f), NewVisit = new Vector3(0.13f, 0.21f, 0.42f);   // left of the perch, above the task dock: never in front of the visitor
    static readonly Vector3 OldPerch = new Vector3(0.22f, 0.004f, 0.40f), NewPerch = new Vector3(0.40f, 0.004f, 0.38f);

    public static string Apply()
    {
        return Set(AssetDatabase.LoadAssetAtPath<Material>(Dir + "OfficeV2_Plate.mat"), AssetDatabase.LoadAssetAtPath<Material>(Dir + "Paper_Sheet.mat"),
                   AssetDatabase.LoadAssetAtPath<Material>(Dir + "PaperV2_Teal.mat"), NewVisit);
    }

    public static string Revert() { return Set(null, null, null, OldVisit); }

    static string Set(Material plate, Material paper, Material accent, Vector3 visit)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "CardLookV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        var c = Object.FindAnyObjectByType<WallHabitatController>(FindObjectsInactive.Include);
        if (c == null) return "CardLookV1: no WallHabitatController.\n";
        Undo.RecordObject(c, "Card look");
        c.cardPlateMaterial = plate; c.cardPaperMaterial = paper; c.cardAccentMaterial = accent;
        c.visitCardLocal = visit;
        c.perchLocal = plate != null ? NewPerch : OldPerch;       // the perch pad sat on the task dock: move it to the dock's right
        EditorUtility.SetDirty(c);
        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene);
        return "CardLookV1: plate=" + (plate != null ? plate.name : "none") + " paper=" + (paper != null ? paper.name : "none")
             + " accent=" + (accent != null ? accent.name : "none") + " visitCardLocal=" + visit + " perchLocal=" + c.perchLocal + " scene saved=" + ok + "\n";
    }
}
