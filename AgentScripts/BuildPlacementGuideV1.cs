using System.Collections.Generic;
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
/// Builds the guided placement step: one scene-root object "PlacementGuide" (the paper card with
/// arrow, fill ring, title and body text, a Cancel key and a "Start here" key cloned from the mode
/// console's Cancel key), one scene-root "FloorScanReveal" (the floor twin of CeilingScanReveal, same
/// material), and wires ModeConsole.guide. Idempotent: finds things by name, reuses them, rewires.
/// Nothing is destroyed; Revert() deactivates both objects and clears ModeConsole.guide.
/// Run after BuildModeConsoleV1 and BuildCeilingScanV1 (it needs the console's Cancel key and the
/// Mat_CeilingScanGrid material). Saves the scene.
///   BuildPlacementGuideV1.Apply
/// </summary>
public static class BuildPlacementGuideV1
{
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI";
    const string ScanMatPath = "Assets/MuseDesk/Art/Materials/Mat_CeilingScanGrid.mat";
    const string FontMedium = "Assets/MuseDesk/Art/Fonts/Inter-Medium SDF.asset";
    const string FontBold = "Assets/MuseDesk/Art/Fonts/Inter-Bold SDF.asset";
    const string GuideName = "LookGuide", FloorScanName = "FloorScanReveal", LegacyName = "PlacementGuide";

    const float CardW = 0.30f, CardH = 0.155f;
    const float PlateZ = 0.006f, ProudZ = 0.0055f;      // content-local: +z is away from the user
    const float ArrowX = -0.105f, ArrowY = 0.008f;
    const float KeyY = -0.050f, CancelX = -0.060f, StartX = 0.060f;   // content-space x (the user's right)

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
    static readonly Color Paper = Hex("#F7F2E3"), Ink = Hex("#1F2530"), Teal = Hex("#2FA39A"), Track = Hex("#D9D2BD");

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

    static Material UnlitMat(string name, Color c)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetColor("_Color", c);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Transform Child(Transform parent, string name, int layer)
    {
        Transform t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            t = go.transform;
        }
        t.gameObject.layer = layer;
        t.gameObject.SetActive(true);
        return t;
    }

    static Transform QuadChild(Transform parent, string name, Material mat, Vector3 lp, Vector2 size, int layer)
    {
        Transform t = parent.Find(name);
        if (t == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            t = go.transform;
        }
        t.gameObject.layer = layer;
        t.localPosition = lp; t.localRotation = Quaternion.identity; t.localScale = new Vector3(size.x, size.y, 1f);
        var mr = t.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return t;
    }

    static MeshFilter MeshChild(Transform parent, string name, Material mat, Vector3 lp, int layer)
    {
        Transform t = Child(parent, name, layer);
        t.localPosition = lp; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
        var mf = t.GetComponent<MeshFilter>();
        if (mf == null) mf = t.gameObject.AddComponent<MeshFilter>();
        var mr = t.GetComponent<MeshRenderer>();
        if (mr == null) mr = t.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mf;
    }

    static TMP_Text Text(Transform parent, string name, TMP_FontAsset font, float fontSize, Vector3 lp, Vector2 sizeMetres,
                         TextAlignmentOptions align, string text, int layer)
    {
        Transform t = Child(parent, name, layer);
        t.localPosition = lp; t.localRotation = Quaternion.identity; t.localScale = Vector3.one * 0.01f;
        var tmp = t.GetComponent<TextMeshPro>();
        if (tmp == null) tmp = t.gameObject.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;                         // scale 0.01: font size is roughly millimetres
        tmp.color = Ink;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMax = fontSize; tmp.fontSizeMin = Mathf.Min(fontSize, 5.5f);
        tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        tmp.rectTransform.sizeDelta = new Vector2(sizeMetres.x / 0.01f, sizeMetres.y / 0.01f);
        EditorUtility.SetDirty(tmp);
        return tmp;
    }

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

    static PlacementGuide.GuideKey MakeKey(Transform card, Transform src, Vector3 shownScale, string name, string words,
                                           float contentX, PlacementGuide target, string method, int layer, StringBuilder sb)
    {
        Transform k = card.Find(name);
        bool created = k == null;
        if (created)
        {
            var go = Object.Instantiate(src.gameObject, card);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.name = name;
            k = go.transform;
            var face = go.GetComponent<FaceUser>();
            if (face != null) Object.DestroyImmediate(face);
        }
        k.gameObject.layer = layer;
        k.gameObject.SetActive(true);
        k.localRotation = Quaternion.identity;
        k.localPosition = new Vector3(-contentX, KeyY, 0f);        // the card faces the user: its x is the user's left
        k.localScale = shownScale * 0.001f;                         // saved hidden; the guide eases it in
        var label = k.GetComponentInChildren<TMP_Text>(true);
        if (label != null) { label.text = words; EditorUtility.SetDirty(label); }
        Rewire(k, target, method);
        EditorUtility.SetDirty(k.gameObject);
        sb.AppendLine($"  {(created ? "created" : "found")} {name} \"{words}\" -> PlacementGuide.{method}");
        return new PlacementGuide.GuideKey
        {
            root = k,
            shownScale = shownScale,
            poke = k.GetComponentInChildren<PokeInteractable>(true),
            visual = k.GetComponent<KeyStateVisual>() != null ? k.GetComponent<KeyStateVisual>() : k.GetComponentInChildren<KeyStateVisual>(true),
        };
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildPlacementGuideV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildPlacementGuideV1: no open scene.\n";
        var sb = new StringBuilder("== BuildPlacementGuideV1 ==\n");

        var console = Object.FindAnyObjectByType<ModeConsole>(FindObjectsInactive.Include);
        if (console == null) return sb.Append("ModeConsole not found (run BuildModeConsoleV1 first); nothing changed.\n").ToString();
        Transform srcKey = console.cancelKey != null ? console.cancelKey.root : null;
        if (srcKey == null) return sb.Append("ModeConsole.cancelKey.root missing; nothing changed.\n").ToString();
        var director = Object.FindAnyObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);
        if (director == null) sb.AppendLine("  WARNING: no SpatialWorkspaceDirector: the guide will fall back to direct requests");
        int layer = srcKey.gameObject.layer;
        Vector3 keyScale = console.cancelKey.shownScale.sqrMagnitude > 1e-6f ? console.cancelKey.shownScale : srcKey.localScale;

        var fontM = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontMedium);
        var fontB = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBold);
        if (fontM == null || fontB == null) sb.AppendLine("  WARNING: Inter font assets not found; TMP default font used");

        var paper = UnlitMat("Mat_Guide_Paper", Paper);
        var ink = UnlitMat("Mat_Guide_Ink", Ink);
        var teal = UnlitMat("Mat_Guide_Teal", Teal);
        var track = UnlitMat("Mat_Guide_Track", Track);
        AssetDatabase.SaveAssets();

        // ── the guide root and card (scene root, world space) ──
        var all = All();
        // A first run used the name "PlacementGuide", which is ALSO the old desk-placement marker that
        // WorkspacePlacement switches off at runtime. Take back what that run added to it (our component
        // and our "Card" child); the old object itself is left as it was.
        foreach (Transform old in all.Where(t => t != null && t.name == LegacyName && t.parent == null).ToList())
        {
            var oldGuide = old.GetComponent<PlacementGuide>();
            if (oldGuide == null) continue;
            Object.DestroyImmediate(oldGuide);
            Transform oldCard = old.Find("Card");
            if (oldCard != null) Object.DestroyImmediate(oldCard.gameObject);
            sb.AppendLine("  removed the guide component and card from the old desk-placement marker 'PlacementGuide'");
        }
        all = All();
        Transform root = all.FirstOrDefault(t => t.name == GuideName && t.parent == null);
        bool madeRoot = root == null;
        if (madeRoot) { root = new GameObject(GuideName).transform; Undo.RegisterCreatedObjectUndo(root.gameObject, GuideName); }
        root.gameObject.layer = layer;
        root.gameObject.SetActive(true);
        if (root.parent != null) root.SetParent(null, true);
        var guide = root.GetComponent<PlacementGuide>();
        if (guide == null) guide = root.gameObject.AddComponent<PlacementGuide>();

        Transform card = Child(root, "Card", layer);
        card.localPosition = Vector3.zero; card.localRotation = Quaternion.identity;
        Transform content = Child(card, "Content", layer);
        content.localPosition = Vector3.zero;
        content.localRotation = Quaternion.Euler(0f, 180f, 0f);     // TMP reads from its local -Z; the user is at the card's +Z
        content.localScale = Vector3.one;

        QuadChild(content, "Border", ink, new Vector3(0f, 0f, PlateZ + 0.0005f), new Vector2(CardW + 0.006f, CardH + 0.006f), layer);
        QuadChild(content, "Plate", paper, new Vector3(0f, 0f, PlateZ), new Vector2(CardW, CardH), layer);

        guide.title = Text(content, "Title", fontB, 9f, new Vector3(0.040f, 0.045f, ProudZ), new Vector2(0.19f, 0.016f),
                           TextAlignmentOptions.Left, "Look up at the ceiling", layer);
        guide.title.fontStyle = FontStyles.Bold;
        guide.body = Text(content, "Body", fontM, 6.5f, new Vector3(0.040f, 0.006f, ProudZ), new Vector2(0.19f, 0.030f),
                          TextAlignmentOptions.TopLeft, "Hold your gaze there for a moment", layer);

        guide.ringTrack = MeshChild(content, "RingTrack", track, new Vector3(ArrowX, ArrowY, ProudZ + 0.0001f), layer);
        guide.ringFill = MeshChild(content, "RingFill", teal, new Vector3(ArrowX, ArrowY, ProudZ), layer);
        guide.arrow = MeshChild(content, "Arrow", teal, new Vector3(ArrowX, ArrowY, ProudZ), layer);

        // ── keys, cloned from the console's Cancel key ──
        guide.cancelKey = MakeKey(card, srcKey, keyScale, "Guide_Cancel", "Cancel", CancelX, guide, nameof(PlacementGuide.PressCancel), layer, sb);
        guide.startKey = MakeKey(card, srcKey, keyScale, "Guide_Start", "Start here", StartX, guide, nameof(PlacementGuide.PressStart), layer, sb);

        guide.director = director;
        guide.card = card;
        guide.content = content;
        card.localScale = Vector3.one * 0.001f;                     // saved hidden
        EditorUtility.SetDirty(guide);

        // ── wiring ──
        console.guide = guide;
        EditorUtility.SetDirty(console);

        // ── the floor scan twin ──
        var scanMat = AssetDatabase.LoadAssetAtPath<Material>(ScanMatPath);
        if (scanMat == null) sb.AppendLine("  WARNING: Mat_CeilingScanGrid not found (run BuildCeilingScanV1): the floor scan has no material");
        Transform fl = all.FirstOrDefault(t => t.name == FloorScanName && t.parent == null);
        bool madeFloor = fl == null;
        if (madeFloor) { fl = new GameObject(FloorScanName).transform; Undo.RegisterCreatedObjectUndo(fl.gameObject, FloorScanName); }
        fl.gameObject.SetActive(true);
        fl.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var floorScan = fl.GetComponent<FloorScanReveal>();
        if (floorScan == null) floorScan = fl.gameObject.AddComponent<FloorScanReveal>();
        floorScan.material = scanMat;
        floorScan.director = director;
        EditorUtility.SetDirty(floorScan);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        sb.AppendLine($"  PlacementGuide {(madeRoot ? "created" : "found")}, FloorScanReveal {(madeFloor ? "created" : "found")}; ModeConsole.guide wired; director {(director != null ? "wired" : "MISSING")}");
        sb.AppendLine($"  keys use scale {keyScale}; card {CardW * 1000f:F0} x {CardH * 1000f:F0} mm; saved hidden (the guide eases it in)");
        sb.AppendLine("  NOTE: the ceiling scan reads the gaze from PlacementGuide; BuildCeilingScanV1 is unchanged (the component was edited in code)");
        return sb.ToString();
    }

    public static string Revert()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = All();
        var console = Object.FindAnyObjectByType<ModeConsole>(FindObjectsInactive.Include);
        if (console != null) { console.guide = null; EditorUtility.SetDirty(console); }
        foreach (string n in new[] { GuideName, FloorScanName })
        {
            var t = all.FirstOrDefault(x => x.name == n && x.parent == null);
            if (t != null) t.gameObject.SetActive(false);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "PlacementGuide and FloorScanReveal deactivated; ModeConsole.guide cleared";
    }
}
