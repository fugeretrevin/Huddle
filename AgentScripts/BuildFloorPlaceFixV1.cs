using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Spatial;

/// <summary>
/// Floor placement fix (headset feedback: "ready" spots that did not move on release), scene part.
/// Adds the words plate the footprint now carries, under the existing "Spatial/FloorAim/Visuals"
/// (built by BuildFloorAimV1), and wires it to the FloorAimController:
///   AimWords (placed every frame in front of the footprint's near edge, turned to the eye)
///     AimWordsPlate: the built-in quad with the existing FloorAim_Line material (unlit, opaque,
///                    two-sided), tinted Plate at run time and sized to the text;
///     AimWordsText:  TextMeshPro at scale 0.01, 26 mm, centred, two lines at most, On plate colour.
///                    The font is left to the coordinating pass (design system: builders do not set fonts).
/// No collider, no interactable. Nothing existing is moved, re-parented or rewired; the visuals stay
/// saved inactive; no feature flag and no placement number is changed (the new placement settings
/// take their defaults from code). Idempotent; saves the scene once.
///   unity command script run --file AgentScripts/BuildFloorPlaceFixV1.cs --entry BuildFloorPlaceFixV1.Apply
/// </summary>
public static class BuildFloorPlaceFixV1
{
    static Transform Child(Transform parent, string name, int layer, ref int created)
    {
        Transform t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            t = go.transform;
            created++;
        }
        t.gameObject.layer = layer;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        return t;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildFloorPlaceFixV1: stop Play Mode first.";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildFloorPlaceFixV1: no open scene.";

        var aim = Object.FindAnyObjectByType<FloorAimController>(FindObjectsInactive.Include);
        if (aim == null) return "BuildFloorPlaceFixV1: no FloorAimController (run BuildFloorAimV1 first); nothing changed.";
        if (aim.visuals == null) return "BuildFloorPlaceFixV1: FloorAimController.visuals is not set (run BuildFloorAimV1 first); nothing changed.";
        Material mat = null;
        if (aim.footprintRim != null)
        {
            var r = aim.footprintRim.GetComponent<MeshRenderer>();
            if (r != null) mat = r.sharedMaterial;
        }
        if (mat == null) mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/MuseDesk/Art/Materials/UI/FloorAim_Line.mat");
        if (mat == null) return "BuildFloorPlaceFixV1: FloorAim_Line material not found (run BuildFloorAimV1 first); nothing changed.";
        Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        if (quad == null) return "BuildFloorPlaceFixV1: built-in quad mesh not found; nothing changed.";

        Transform visuals = aim.visuals.transform;
        int layer = visuals.gameObject.layer;
        int created = 0;

        Transform words = Child(visuals, "AimWords", layer, ref created);

        Transform plateT = Child(words, "AimWordsPlate", layer, ref created);
        var filter = GetOrAdd<MeshFilter>(plateT.gameObject);
        filter.sharedMesh = quad;
        var plate = GetOrAdd<MeshRenderer>(plateT.gameObject);
        plate.sharedMaterial = mat;
        plate.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        plate.receiveShadows = false;
        plate.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        plate.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        plateT.localPosition = new Vector3(0f, 0f, 0.0015f);
        plateT.localScale = new Vector3(0.30f, 0.05f, 1f);       // resized to the text at run time
        var plateCollider = plateT.GetComponent<Collider>();
        if (plateCollider != null) Undo.DestroyObjectImmediate(plateCollider);

        Transform textT = Child(words, "AimWordsText", layer, ref created);
        var text = GetOrAdd<TextMeshPro>(textT.gameObject);
        text.transform.localPosition = Vector3.zero;
        text.transform.localRotation = Quaternion.identity;
        text.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        text.text = "Pinch it, drag it to a spot, let go";
        text.fontSize = 26f;
        text.enableAutoSizing = false;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.maxVisibleLines = 2;
        text.color = new Color32(0xF3, 0xF4, 0xF6, 0xFF);
        text.rectTransform.sizeDelta = new Vector2(80f, 8f);    // 80 cm wide: about 30 letters a line at 26 mm
        text.raycastTarget = false;
        var textRenderer = text.GetComponent<MeshRenderer>();
        if (textRenderer != null) textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Undo.RecordObject(aim, "Wire floor aim words");
        aim.wordsRoot = words;
        aim.wordsText = text;
        aim.wordsPlate = plate;
        EditorUtility.SetDirty(aim);
        EditorUtility.SetDirty(words.gameObject);
        EditorUtility.SetDirty(text);

        bool visualsActive = aim.visuals.activeSelf;
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        return "BuildFloorPlaceFixV1: FloorAim/Visuals/AimWords " + (created > 0 ? "built (" + created + " objects created)" : "updated (nothing created)")
               + "; plate material=" + mat.name + "; text 26 mm, 80 cm wide, 2 lines max, font left to the font pass"
               + "; visuals saved " + (visualsActive ? "ACTIVE (expected inactive)" : "inactive")
               + "; release to place=" + aim.settings.releaseToPlace + " (min hold " + aim.settings.placeMinSeconds.ToString("F2") + " s)"
               + "; scene saved=" + saved;
    }
}
