using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using MuseDesk.Spatial;

/// <summary>
/// "Reach up and pull it down", scene part. Adds ONE object, "Spatial/CeilingPull", with the
/// CeilingPullGesture and its cue (under "CeilingPull/Visuals", saved inactive, no colliders, no
/// interactables): CeilingPullCue (follows the pod) with CueTrack + CueFill (ring), CueArrow, and
/// CueLabel (TMP, scale 0.01). Meshes are generated at run time. One opaque unlit material
/// (CeilingPull_Line) is created when missing.
/// It is wired to the existing SpatialWorkspaceDirector and the rig's hand visuals (by name, else by
/// handedness at run time). Run it after BuildCeilingVfxV1 and BuildCeilingAudioV1; it does not touch
/// anything those made. Idempotent; saves the scene.
///   unity command script run --file AgentScripts/BuildCeilingPullV1.cs --entry BuildCeilingPullV1.Apply
/// Revert() switches the object off (nothing is destroyed); Apply() switches it back on.
/// </summary>
public static class BuildCeilingPullV1
{
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI";
    static readonly Color Teal = new Color32(0x2F, 0xA3, 0x9A, 0xFF);

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

    static Transform Holder(Transform parent, string name, int layer, ref int created)
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
        return t;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    static MeshFilter Surface(Transform parent, string name, Material mat, int layer, ref int created)
    {
        Transform t = Holder(parent, name, layer, ref created);
        t.gameObject.SetActive(true);
        var filter = GetOrAdd<MeshFilter>(t.gameObject);
        var r = GetOrAdd<MeshRenderer>(t.gameObject);
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        EditorUtility.SetDirty(t.gameObject);
        return filter;
    }

    static Material Mat(ref int createdMats)
    {
        string path = MatDir + "/CeilingPull_Line.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) return null;
        if (!AssetDatabase.IsValidFolder("Assets/MuseDesk/Art/Materials")) AssetDatabase.CreateFolder("Assets/MuseDesk/Art", "Materials");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/MuseDesk/Art/Materials", "UI");
        m = new Material(shader);
        m.SetColor("_BaseColor", Teal);
        m.SetColor("_Color", Teal);
        m.SetFloat("_Cull", 0f);                         // two-sided: the winding and the viewing side cannot hide it
        m.enableInstancing = true;
        AssetDatabase.CreateAsset(m, path);
        createdMats++;
        return m;
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildCeilingPullV1: stop Play Mode first.";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildCeilingPullV1: no open scene.";

        Transform spatial = scene.GetRootGameObjects().Select(g => g.transform).FirstOrDefault(t => t.name == "Spatial");
        if (spatial == null) return "BuildCeilingPullV1: scene root object \"Spatial\" not found; nothing changed.";
        var director = spatial.GetComponentInChildren<SpatialWorkspaceDirector>(true);
        if (director == null) director = Object.FindAnyObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);
        if (director == null) return "BuildCeilingPullV1: no SpatialWorkspaceDirector in the scene; nothing changed.";

        int layer = spatial.gameObject.layer;
        int created = 0, createdMats = 0;
        var mat = Mat(ref createdMats);
        if (mat == null) return "BuildCeilingPullV1: shader \"Universal Render Pipeline/Unlit\" not found; nothing changed.";

        Transform holder = Holder(spatial, "CeilingPull", layer, ref created);
        holder.gameObject.SetActive(true);
        var gesture = GetOrAdd<CeilingPullGesture>(holder.gameObject);

        Transform visuals = Holder(holder, "Visuals", layer, ref created);
        visuals.gameObject.SetActive(true);
        Transform cue = Holder(visuals, "CeilingPullCue", layer, ref created);
        cue.gameObject.SetActive(true);

        gesture.director = director;
        gesture.visuals = visuals.gameObject;
        gesture.cueRoot = cue;
        gesture.ringTrack = Surface(cue, "CueTrack", mat, layer, ref created);
        gesture.ringFill = Surface(cue, "CueFill", mat, layer, ref created);
        gesture.arrow = Surface(cue, "CueArrow", mat, layer, ref created);

        // The label: a TMP at scale 0.01 (font size is roughly millimetres), under the ring.
        Transform lt = Holder(cue, "CueLabel", layer, ref created);
        lt.gameObject.SetActive(true);
        lt.localScale = Vector3.one * 0.01f;
        lt.localRotation = Quaternion.identity;
        var tmp = GetOrAdd<TextMeshPro>(lt.gameObject);
        var console = Object.FindAnyObjectByType<ModeConsole>(FindObjectsInactive.Include);
        if (console != null && console.statusLabel != null)
        {
            if (console.statusLabel.font != null) tmp.font = console.statusLabel.font;
            if (console.statusLabel.fontSharedMaterial != null) tmp.fontSharedMaterial = console.statusLabel.fontSharedMaterial;
        }
        tmp.text = "Pinch and pull down";
        tmp.fontSize = 22f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.color = new Color32(0xF3, 0xF4, 0xF6, 0xFF);
        tmp.raycastTarget = false;
        tmp.rectTransform.sizeDelta = new Vector2(90f, 10f);
        gesture.label = tmp;

        // The rig's own hands (no second rig, no new interactor).
        HandVisual left = null, right = null;
        foreach (var t in All())
        {
            if (t == null) continue;
            if (t.name == "OVRHandVisualLeft" && left == null) left = t.GetComponent<HandVisual>();
            else if (t.name == "OVRHandVisualRight" && right == null) right = t.GetComponent<HandVisual>();
        }
        gesture.leftHandVisual = left;
        gesture.rightHandVisual = right;

        visuals.gameObject.SetActive(false);             // no interactables inside: SetActive is safe
        EditorUtility.SetDirty(gesture);
        EditorUtility.SetDirty(holder.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return "BuildCeilingPullV1: Spatial/CeilingPull " + (created > 0 ? "built (" + created + " objects created)" : "updated (nothing created)")
               + "; material created=" + createdMats + "; director=" + director.gameObject.name
               + "; pod=" + (director.pod != null ? director.pod.name : "MISSING")
               + "; mastTop=" + (director.pod != null && director.pod.mastTop != null) + " mastBottom=" + (director.pod != null && director.pod.mastBottom != null)
               + "; hands: left=" + (left != null) + " right=" + (right != null) + " (else found at run time)"
               + "; label font from ModeConsole=" + (console != null && console.statusLabel != null)
               + "; visuals saved inactive; scene saved=" + saved;
    }

    /// <summary>Switch the gesture off (deactivated, not destroyed). Apply() switches it back on.</summary>
    public static string Revert()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var t = All().FirstOrDefault(x => x != null && x.name == "CeilingPull" && x.GetComponent<CeilingPullGesture>() != null);
        if (t == null) return "BuildCeilingPullV1.Revert: nothing to revert.";
        t.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        return "BuildCeilingPullV1.Revert: Spatial/CeilingPull deactivated; scene saved=" + EditorSceneManager.SaveScene(scene);
    }
}
