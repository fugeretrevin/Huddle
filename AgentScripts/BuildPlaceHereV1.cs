using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Oculus.Interaction;
using MuseDesk.Spatial;
using MuseDesk.Workspace;

/// <summary>
/// rc6 deliberate floor placement, scene part: two keys on the guide card (scene-root "LookGuide", built
/// by BuildPlacementGuideV1), shown by PlacementGuide during a floor preview:
///   Guide_Place    "Place here"  cloned from the mode console's Confirm key (teal cap), at the Start key's
///                                place (content x +60 mm; the two are never shown together) -> PlacementGuide.PressPlace
///   Guide_Another  "Other spot"  cloned from the console's Next key (paper cap), card centre (content x 0)
///                                -> PlacementGuide.PressAnother
/// The existing Guide_Cancel (content x -60 mm) is the third key. Keys are saved hidden (scale x 0.001);
/// PlacementGuide eases them in and enables their pokes. No other object is moved, re-parented or rewired;
/// no collider is added beyond what the cloned key already carries. Idempotent (finds by name, reuses).
/// Revert() deactivates the two keys and clears PlacementGuide.placeKey / anotherKey. Saves the scene.
/// Run after BuildPlacementGuideV1:
///   powershell scripts\run_guarded.ps1 -File AgentScripts\BuildPlaceHereV1.cs -Entry BuildPlaceHereV1.Apply
/// </summary>
public static class BuildPlaceHereV1
{
    const string GuideName = "LookGuide";
    const string PlaceName = "Guide_Place", AnotherName = "Guide_Another";
    const float KeyY = -0.050f;                     // as BuildPlacementGuideV1
    const float PlaceX = 0.060f, AnotherX = 0f;     // content-space x (the user's right)

    static void Rewire(Transform key, Object target, string method)
    {
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "InteractableUnityEventWrapper") continue;
            var so = new SerializedObject(mb);
            var calls = so.FindProperty("_whenSelect.m_PersistentCalls.m_Calls");
            if (calls == null) continue;
            if (calls.arraySize == 0) calls.InsertArrayElementAtIndex(0);
            for (int i = 0; i < calls.arraySize; i++)
            {
                var c = calls.GetArrayElementAtIndex(i);
                c.FindPropertyRelative("m_Target").objectReferenceValue = target;
                c.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = target.GetType().AssemblyQualifiedName;
                c.FindPropertyRelative("m_MethodName").stringValue = method;
                c.FindPropertyRelative("m_Mode").intValue = 1;
                c.FindPropertyRelative("m_CallState").intValue = 2;
            }
            so.ApplyModifiedProperties();
        }
    }

    static PlacementGuide.GuideKey MakeKey(Transform card, ModeConsole.ConsoleKey src, string name, string words, float contentX,
                                           PlacementGuide target, string method, int layer, StringBuilder sb)
    {
        Transform k = card.Find(name);
        bool created = k == null;
        Vector3 shown = src.shownScale.sqrMagnitude > 1e-8f ? src.shownScale : src.root.localScale;
        if (created)
        {
            var go = Object.Instantiate(src.root.gameObject, card);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.name = name;
            k = go.transform;
            var face = go.GetComponent<FaceUser>();
            if (face != null) Object.DestroyImmediate(face);
        }
        k.gameObject.layer = layer;
        if (!k.gameObject.activeSelf) k.gameObject.SetActive(true);
        k.localRotation = Quaternion.identity;
        k.localPosition = new Vector3(-contentX, KeyY, 0f);         // the card faces the user: its x is the user's left
        k.localScale = shown * 0.001f;                               // saved hidden; the guide eases it in
        var label = k.GetComponentInChildren<TMP_Text>(true);
        if (label != null) { label.text = words; EditorUtility.SetDirty(label); }
        Rewire(k, target, method);
        EditorUtility.SetDirty(k.gameObject);
        var poke = k.GetComponentInChildren<PokeInteractable>(true);
        var visual = k.GetComponent<KeyStateVisual>() != null ? k.GetComponent<KeyStateVisual>() : k.GetComponentInChildren<KeyStateVisual>(true);
        sb.AppendLine($"  {(created ? "created" : "found")} {name} \"{words}\" from {src.root.name} at content x {contentX * 1000f:F0} mm -> PlacementGuide.{method}"
                      + (poke == null ? "  WARNING: no PokeInteractable" : "") + (visual == null ? "  WARNING: no KeyStateVisual" : ""));
        return new PlacementGuide.GuideKey { root = k, shownScale = shown, poke = poke, visual = visual };
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildPlaceHereV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildPlaceHereV1: no open scene.\n";
        var sb = new StringBuilder("== BuildPlaceHereV1 ==\n");

        var guide = Object.FindObjectsByType<PlacementGuide>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .FirstOrDefault(g => g != null && g.transform.parent == null && g.name == GuideName);
        if (guide == null) return sb.Append("LookGuide/PlacementGuide not found (run BuildPlacementGuideV1 first); nothing changed.\n").ToString();
        if (guide.card == null) return sb.Append("PlacementGuide.card is not set (run BuildPlacementGuideV1 first); nothing changed.\n").ToString();
        var console = Object.FindAnyObjectByType<ModeConsole>(FindObjectsInactive.Include);
        if (console == null) return sb.Append("ModeConsole not found (run BuildModeConsoleV1 first); nothing changed.\n").ToString();
        if (console.confirmKey == null || console.confirmKey.root == null || console.nextKey == null || console.nextKey.root == null)
            return sb.Append("ModeConsole confirm/next keys missing; nothing changed.\n").ToString();

        Transform card = guide.card;
        int layer = guide.cancelKey != null && guide.cancelKey.root != null ? guide.cancelKey.root.gameObject.layer : card.gameObject.layer;

        Undo.RecordObject(guide, "Wire Place here keys");
        guide.placeKey = MakeKey(card, console.confirmKey, PlaceName, "Place here", PlaceX, guide, nameof(PlacementGuide.PressPlace), layer, sb);
        guide.anotherKey = MakeKey(card, console.nextKey, AnotherName, "Other spot", AnotherX, guide, nameof(PlacementGuide.PressAnother), layer, sb);
        // The card's floor-preview words were saved by BuildPlacementGuideV1 as "... let go to place": no longer true.
        guide.floorPreviewBody = "Press Place here, or pinch and drag the outline";
        guide.floorDraggingBody = "Let go on a clear spot, then press Place here";
        guide.floorReadyBody = "Press Place here to move it, or drag it again";
        sb.AppendLine("  card words: floor preview '" + guide.floorPreviewBody + "'");
        EditorUtility.SetDirty(guide);

        // Report the layout: the three floor-preview keys must not overlap on the card.
        Transform cancel = guide.cancelKey != null ? guide.cancelKey.root : null;
        if (cancel != null)
            sb.AppendLine($"  card-local x: Cancel {cancel.localPosition.x * 1000f:F0} mm, Other spot {guide.anotherKey.root.localPosition.x * 1000f:F0} mm, Place here {guide.placeKey.root.localPosition.x * 1000f:F0} mm (60 mm apart; console keys are about 42 mm wide)");
        sb.AppendLine($"  floor preview card distance {guide.previewCardDistance:F2} m, pitch {guide.previewCardPitch:F0} degrees (code defaults; not changed here)");

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        sb.AppendLine("  scene saved=" + saved);
        return sb.ToString();
    }

    public static string Revert()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var guide = Object.FindObjectsByType<PlacementGuide>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .FirstOrDefault(g => g != null && g.transform.parent == null && g.name == GuideName);
        if (guide == null) return "BuildPlaceHereV1.Revert: no LookGuide; nothing changed";
        if (guide.card != null)
            foreach (string n in new[] { PlaceName, AnotherName })
            {
                Transform t = guide.card.Find(n);
                if (t != null) t.gameObject.SetActive(false);
            }
        Undo.RecordObject(guide, "Clear Place here keys");
        guide.placeKey = new PlacementGuide.GuideKey();
        guide.anotherKey = new PlacementGuide.GuideKey();
        EditorUtility.SetDirty(guide);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "BuildPlaceHereV1.Revert: Guide_Place / Guide_Another deactivated; PlacementGuide.placeKey / anotherKey cleared";
    }
}
