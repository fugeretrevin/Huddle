using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using MuseDesk.Documents;
using MuseDesk.Networking;
using MuseDesk.Workspace;

/// <summary>
/// A15 integration: adds the first-use hint presenter to the open scene.
///
///   unity command script run --file AgentScripts/BuildOnboardingHintsV1.cs --entry BuildOnboardingHintsV1.Apply
///   (Revert deactivates and renames it "OnboardingHints_old"; Apply brings it back. Idempotent; saves the scene.)
///
/// One scene-root object "OnboardingHints" (scale 1, so the card it makes in world space is never scaled by a parent)
/// carries <see cref="OnboardingHintPresenter"/>, wired to the SessionController, the Inter Medium font and two shared
/// materials (Mat_Guide_Paper for the plate, Mat_Guide_Teal for the dismiss dot). The presenter is inert while the
/// ReleaseConfig flag onboardingHints is off (the committed value), so running this changes nothing a user can see until
/// WallValidationConfig.On() (or ReleaseConfigDrive.Set("hints=1") in Play Mode) turns the flag on.
/// Nothing else in the scene is touched.
/// </summary>
public static class BuildOnboardingHintsV1
{
    const string ObjName = "OnboardingHints", OldName = "OnboardingHints_old";
    const string FontPath = "Assets/MuseDesk/Art/Fonts/Inter-Medium SDF.asset";
    const string PlateMat = "Assets/MuseDesk/Art/Materials/UI/Mat_Guide_Paper.mat";
    const string DotMat = "Assets/MuseDesk/Art/Materials/UI/Mat_Guide_Teal.mat";

    static GameObject FindRoot(string name) =>
        EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g != null && g.name == name);

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildOnboardingHintsV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildOnboardingHintsV1: no open scene.\n";
        var sb = new StringBuilder();
        sb.AppendLine("== BuildOnboardingHintsV1 ==");

        var go = FindRoot(ObjName);
        bool created = false;
        if (go == null)
        {
            var old = FindRoot(OldName);
            if (old != null) { go = old; go.name = ObjName; sb.AppendLine("  revived " + OldName); }
        }
        if (go == null)
        {
            go = new GameObject(ObjName);
            Undo.RegisterCreatedObjectUndo(go, ObjName);
            created = true;
        }
        go.SetActive(true);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;

        var pres = go.GetComponent<OnboardingHintPresenter>();
        if (pres == null) pres = Undo.AddComponent<OnboardingHintPresenter>(go);

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        var plate = AssetDatabase.LoadAssetAtPath<Material>(PlateMat);
        var dot = AssetDatabase.LoadAssetAtPath<Material>(DotMat);
        var ctrl = Object.FindFirstObjectByType<SessionController>(FindObjectsInactive.Include);
        pres.controller = ctrl;
        pres.font = font;
        pres.plateMaterial = plate;
        pres.dotMaterial = dot;
        EditorUtility.SetDirty(pres);

        sb.AppendLine($"  {(created ? "created" : "updated")} {ObjName} at the scene root; controller={(ctrl != null)} font={(font != null)} plate={(plate != null)} dot={(dot != null)}");
        if (ctrl == null || font == null || plate == null || dot == null)
            sb.AppendLine("  WARNING: a reference is missing; the presenter will warn and stay off. Check the paths at the top of this builder.");
        sb.AppendLine("  the presenter is inert until ReleaseConfig.onboardingHints is on (WallValidationConfig.On()).");

        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("  scene saved=" + EditorSceneManager.SaveOpenScenes());
        return sb.ToString();
    }

    public static string Revert()
    {
        var go = FindRoot(ObjName);
        if (go == null) return "BuildOnboardingHintsV1.Revert: no " + ObjName;
        go.name = OldName;
        go.SetActive(false);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        return "BuildOnboardingHintsV1.Revert: " + ObjName + " deactivated and renamed " + OldName;
    }
}
