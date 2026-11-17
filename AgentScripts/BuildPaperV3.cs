using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Documents;
using MuseDesk.Workspace;

/// <summary>
/// Decision paper refinement (final refinement pass, Paper agent). Runs after BuildPaperV2 on the
/// same DecisionDocument. Idempotent; saves the scene once.
///
/// Expanded sheet, 30 x 30 cm, 1.6 cm margins, one left-aligned grid, top to bottom:
///   header band     role colour chip + "From the Researcher" / "Needs your decision" on the left,
///                   the mode in words (SCRIPTED DEMO / LIVE) on a mode-coloured chip on the right
///   the question    Display 13, two lines, full width
///   summary         9 mm, three lines, in a 17.8 cm column
///   recommendation  a tinted plate with a thin success-green rule: "Recommended: ..." and its reason
///   stamp target    an outlined 8.0 x 7.8 cm box beside the summary and the recommendation
///   provenance      6 mm, always visible, under a thin rule: the quiet footer of the content area
///   status line, one row of four uniform chips, two 3.8 cm primary keys
/// Keys are scaled uniformly and carry a component-free WideKey_ratio marker under the object
/// that has the PokeInteractable; the Kit builder turns the square key into a wide one.
/// The details sheet slides out from behind the bottom edge (DecisionPanel animates its root).
/// The stamp target and the outcome mark are wired to ApprovalStampController; each stamp's model
/// child is wired as StampTool.pressVisual.
///
/// All positions are authored in the paper's own local metres and converted through the measured
/// transforms, so no parent scale is assumed. Returns the measured layout and its warnings.
///   unity command script run --file AgentScripts/BuildPaperV3.cs --entry BuildPaperV3.Apply
/// </summary>
public static class BuildPaperV3
{
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI";

    // Design tokens (sRGB).
    static readonly Color Paper = new Color32(0xF7, 0xF2, 0xE3, 255);
    static readonly Color Ink = new Color32(0x1F, 0x25, 0x30, 255);
    static readonly Color InkSoft = new Color32(0x4B, 0x55, 0x63, 255);
    static readonly Color OnPlate = new Color32(0xF3, 0xF4, 0xF6, 255);
    static readonly Color Housing = new Color32(0x2A, 0x2F, 0x3A, 255);
    static readonly Color Teal = new Color32(0x2F, 0xA3, 0x9A, 255);
    static readonly Color Lavender = new Color32(0x8C, 0x7B, 0xD6, 255);
    static readonly Color InfoBlue = new Color32(0x4C, 0x85, 0xDB, 255);
    static readonly Color Success = new Color32(0x2E, 0x9B, 0x5F, 255);
    static readonly Color Coral = new Color32(0xD9, 0x53, 0x4A, 255);
    static readonly Color Amber = new Color32(0xE0, 0xA0, 0x30, 255);
    // Paper-family shades that are not tokens (depth and grouping cues only; none is white).
    static readonly Color PaperTint = new Color32(0xEC, 0xE4, 0xCF, 255);      // header band, recommendation plate, stamp target
    static readonly Color PaperDetails = new Color32(0xEE, 0xE7, 0xD3, 255);   // the details under-sheet
    static readonly Color PaperRule = new Color32(0xCF, 0xC6, 0xAE, 255);      // thin rules and the fold line
    static readonly Color TargetNear = new Color32(0xCD, 0xE3, 0xD9, 255);     // stamp target while a stamp is near (paper tint toward teal)
    static readonly Color MarkGreen = new Color(0.10f, 0.42f, 0.24f);

    // Type scale (millimetres at label scale 0.01).
    const float Display = 13f, Heading = 10f, Body = 7.5f, LabelSize = 6.5f, Caption = 5.5f;
    const float SummarySize = 9f, RecoSize = 8.5f, ProvSize = 6f, DetailsSize = 8f, StampHereSize = 8f, MarkSize = 11f;

    // Sheet geometry, paper-local metres (x to the reader's right, y up, the reader is on -Z).
    const float W = 0.30f, H = 0.30f, Margin = 0.016f;
    const float Left = -W * 0.5f + Margin, Right = W * 0.5f - Margin, ContentW = W - 2f * Margin;

    // Header band (full bleed at the top).
    const float BandH = 0.036f, BandY = H * 0.5f - BandH * 0.5f;                // 15.0 .. 11.4 cm
    const float RoleChipW = 0.005f, RoleChipH = 0.016f, HeaderMidY = 0.1265f;
    const float FromY = 0.131f, NeedsY = 0.122f;
    const float ModeChipW = 0.074f, ModeChipH = 0.015f, ModeChipX = Right - ModeChipW * 0.5f;
    const float HeaderTextLeft = Left + 0.011f, HeaderTextRight = ModeChipX - ModeChipW * 0.5f - 0.006f;

    // Content.
    const float TitleY = 0.088f, TitleBoxH = 0.034f;                           // 10.5 .. 7.1 cm
    const float ColW = 0.178f, ColX = Left + ColW * 0.5f;
    const float SummaryY = 0.0455f, SummaryBoxH = 0.035f;                      // 6.3 .. 2.8 cm
    const float RecoPlateTop = 0.021f, RecoPlateBottom = -0.015f;
    const float RecoPlateH = RecoPlateTop - RecoPlateBottom, RecoPlateY = (RecoPlateTop + RecoPlateBottom) * 0.5f;
    const float RecoRuleW = 0.003f, RecoTextLeft = Left + 0.008f, RecoTextRight = Left + ColW - 0.004f;
    const float RecoLineY = 0.014f, RecoReasonY = -0.002f;
    const float StampW = 0.080f, StampTop = 0.063f, StampBottom = -0.015f;
    const float StampH = StampTop - StampBottom, StampX = Right - StampW * 0.5f, StampY = (StampTop + StampBottom) * 0.5f;
    const float ProvRuleY = -0.022f, ProvY = -0.0325f, ProvBoxH = 0.015f;      // -2.5 .. -4.0 cm
    const float StatusY = -0.049f, StatusBoxH = 0.011f;

    // Keys. Height is the housing's height; width is height x ratio once the Kit builder has run.
    const float ChipY = -0.074f, ChipH = 0.028f, ChipRatio = 2.0f, ChipW = ChipH * ChipRatio;
    const float PrimY = -0.115f, PrimH = 0.038f, PrimRatio = 3.4f, PrimW = PrimH * PrimRatio;
    const float PrimX = Right - PrimW * 0.5f;

    // Details sheet: overlaps the main sheet's bottom edge by 1 cm when open.
    const float DetailsOverlap = 0.010f, DetailsH = 0.136f;
    const float DetailsTop = -H * 0.5f + DetailsOverlap, DetailsY = DetailsTop - DetailsH * 0.5f;
    const float DetailsBehind = 0.002f;                                         // behind the main sheet's face
    const float FoldLineY = -H * 0.5f - 0.0035f;
    const float DetailsBodyTop = -0.163f, DetailsBodyH = 0.070f, DetailsAnswerTop = -0.238f, DetailsAnswerH = 0.026f;
    const float DetailsTuck = 0.138f;                                           // slide distance (up, behind the main sheet)

    // Depth layers in front of a surface (metres, at full size).
    const float PlateProud = 0.0003f, TextProud = 0.0005f;

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t.name == name);

    static float Safe(float v) => Mathf.Abs(v) < 1e-6f ? 1e-6f : v;

    /// <summary>Scale of <paramref name="t"/> relative to the paper root (per axis; no rotations in the chain).</summary>
    static Vector3 Rel(Transform t, Transform doc)
    {
        Vector3 a = t.lossyScale, d = doc.lossyScale;
        return new Vector3(a.x / Safe(d.x), a.y / Safe(d.y), a.z / Safe(d.z));
    }

    /// <summary>A paper-local point expressed in <paramref name="parent"/>'s local space.</summary>
    static Vector3 InParent(Transform doc, Transform parent, Vector3 docLocal) =>
        parent.InverseTransformPoint(doc.TransformPoint(docLocal));

    static Material Mat(string name, Color c, bool unlit = true)
    {
        if (!AssetDatabase.IsValidFolder("Assets/MuseDesk/Art/Materials")) AssetDatabase.CreateFolder("Assets/MuseDesk/Art", "Materials");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/MuseDesk/Art/Materials", "UI");
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Simple Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetColor("_Color", c);
        if (!unlit) m.SetFloat("_Smoothness", 0.35f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Transform Group(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            t = go.transform;
        }
        t.gameObject.layer = parent.gameObject.layer;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        EditorUtility.SetDirty(t.gameObject);
        return t;
    }

    /// <summary>Find or create a quad under <paramref name="parent"/>; centre and size are paper-local metres.</summary>
    static Transform Quad(Transform doc, Transform parent, string name, Mesh quad, Material mat, Vector2 centre, float z, Vector2 size)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            t = go.transform;
        }
        t.gameObject.SetActive(true);
        t.gameObject.layer = doc.gameObject.layer;
        var mf = t.GetComponent<MeshFilter>();
        if (mf == null) mf = t.gameObject.AddComponent<MeshFilter>();
        // Always a plain unit quad sized by scale: if this builder is re-run after the Kit builder,
        // the Kit builder (which runs last) rounds the plate again.
        mf.sharedMesh = quad;
        var r = t.GetComponent<MeshRenderer>();
        if (r == null) r = t.gameObject.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        Vector3 rel = Rel(parent, doc);
        t.localPosition = InParent(doc, parent, new Vector3(centre.x, centre.y, z));
        t.localRotation = Quaternion.identity;
        t.localScale = new Vector3(size.x / Safe(rel.x), size.y / Safe(rel.y), 1f);
        EditorUtility.SetDirty(t.gameObject);
        return t;
    }

    /// <summary>
    /// Find or create (by cloning <paramref name="template"/>) a label under <paramref name="parent"/>
    /// and style it. Centre is paper-local metres, the box is centimetres, sizes are millimetres:
    /// the label's scale is set so that it is 0.01 relative to the paper whatever its parent's scale.
    /// </summary>
    static TMP_Text Text(Transform doc, Transform parent, string name, TMP_Text template, Vector2 centre, float z,
        Vector2 boxCm, float sizeMm, FontStyles style, Color color, TextAlignmentOptions align, bool wrap, string text, float minMm = -1f)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            var go = Object.Instantiate(template.gameObject, parent);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.name = name;
            t = go.transform;
        }
        var tmp = t.GetComponent<TMP_Text>();
        if (tmp == null) return null;
        PlaceText(doc, tmp, centre, z);
        StyleText(tmp, boxCm, sizeMm, style, color, align, wrap, text, minMm);
        return tmp;
    }

    static void PlaceText(Transform doc, TMP_Text tmp, Vector2 centre, float z)
    {
        var t = tmp.transform;
        var parent = t.parent;
        t.gameObject.SetActive(true);
        t.gameObject.layer = doc.gameObject.layer;
        tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        Vector3 rel = Rel(parent, doc);
        t.localRotation = Quaternion.identity;
        t.localScale = new Vector3(0.01f / Safe(rel.x), 0.01f / Safe(rel.y), 0.01f / Safe(rel.z));
        t.localPosition = InParent(doc, parent, new Vector3(centre.x, centre.y, z));
        EditorUtility.SetDirty(t);
    }

    /// <summary>Style a label. <paramref name="minMm"/> below zero means "72 percent of the size, never under Caption".</summary>
    static void StyleText(TMP_Text tmp, Vector2 boxCm, float sizeMm, FontStyles style, Color color,
        TextAlignmentOptions align, bool wrap, string text, float minMm = -1f)
    {
        if (text != null) tmp.text = text;
        tmp.rectTransform.sizeDelta = boxCm;
        float min = minMm >= 0f ? Mathf.Min(sizeMm, minMm) : Mathf.Min(sizeMm, Mathf.Max(Caption, sizeMm * 0.72f));
        tmp.enableAutoSizing = min < sizeMm;
        tmp.fontSizeMin = min; tmp.fontSizeMax = sizeMm; tmp.fontSize = sizeMm;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.margin = Vector4.zero;
        tmp.richText = true;
        EditorUtility.SetDirty(tmp);
        EditorUtility.SetDirty(tmp.rectTransform);
    }

    static bool OwnMeshBounds(Transform part, Transform space, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        var f = part != null ? part.GetComponent<MeshFilter>() : null;
        if (f == null || f.sharedMesh == null) return false;
        var b = f.sharedMesh.bounds;
        for (int i = 0; i < 8; i++)
        {
            var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var l = space.InverseTransformPoint(part.TransformPoint(c));
            mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
        }
        return true;
    }

    /// <summary>Bounds, in <paramref name="space"/>, of every mesh under <paramref name="part"/> (a housing is a group of boxes).</summary>
    static bool MeshBounds(Transform part, Transform space, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        bool any = false;
        if (part == null) return false;
        foreach (var f in part.GetComponentsInChildren<MeshFilter>(true))
        {
            if (f == null || f.sharedMesh == null || f.GetComponent<TMP_Text>() != null) continue;
            var b = f.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var l = space.InverseTransformPoint(f.transform.TransformPoint(c));
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
            }
            any = true;
        }
        return any;
    }

    static MonoBehaviour Comp(Transform t, string typeName) =>
        t.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == typeName);

    static string SelectMethod(Transform key, out Object target)
    {
        target = null;
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "InteractableUnityEventWrapper") continue;
            var so = new SerializedObject(mb);
            var calls = so.FindProperty("_whenSelect.m_PersistentCalls.m_Calls");
            if (calls == null || calls.arraySize == 0) return null;
            var c = calls.GetArrayElementAtIndex(0);
            target = c.FindPropertyRelative("m_Target").objectReferenceValue;
            return c.FindPropertyRelative("m_MethodName").stringValue;
        }
        return null;
    }

    static void SetColorState(SerializedObject so, string state, Color c)
    {
        var st = so.FindProperty(state);
        var col = st != null ? st.FindPropertyRelative("Color") : null;
        if (col != null) col.colorValue = c;
    }

    static void Hide(Transform t)
    {
        if (t != null && t.gameObject.activeSelf) { t.gameObject.SetActive(false); EditorUtility.SetDirty(t.gameObject); }
    }

    /// <summary>Give an existing child its new name; if both names exist (an older builder was re-run) the old one is hidden.</summary>
    static void Rename(Transform parent, string oldName, string newName)
    {
        var old = parent.Find(oldName);
        if (old == null) return;
        if (parent.Find(newName) != null) { Hide(old); return; }
        old.name = newName;
        EditorUtility.SetDirty(old.gameObject);
    }

    /// <summary>Move a direct child of <paramref name="source"/> under <paramref name="to"/>; a leftover duplicate is hidden.</summary>
    static void Adopt(Transform source, Transform to, string name)
    {
        var t = source.Find(name);
        if (t == null) return;
        if (to.Find(name) != null) { Hide(t); return; }
        Undo.SetTransformParent(t, to, "regroup " + name);
        EditorUtility.SetDirty(t);
    }

    static string RatioName(float ratio) => "WideKey_" + ratio.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A component-free marker child that tells the Kit builder how wide this key is meant to be.</summary>
    static void WideMarker(Transform wrapper, Transform block, float ratio)
    {
        string want = RatioName(ratio);
        foreach (var holder in new[] { wrapper, block })
        {
            if (holder == null) continue;
            for (int i = holder.childCount - 1; i >= 0; i--)
            {
                var c = holder.GetChild(i);
                if (!c.name.StartsWith("WideKey_")) continue;
                if (holder == block && c.name == want) continue;
                Undo.DestroyObjectImmediate(c.gameObject);         // a marker of an earlier size, made by this builder
            }
        }
        var m = block.Find(want);
        if (m == null)
        {
            var go = new GameObject(want);
            Undo.RegisterCreatedObjectUndo(go, want);
            go.transform.SetParent(block, false);
            m = go.transform;
        }
        m.gameObject.layer = block.gameObject.layer;
        m.localPosition = Vector3.zero; m.localRotation = Quaternion.identity; m.localScale = Vector3.one;
        EditorUtility.SetDirty(m.gameObject);
    }

    struct KeyResult
    {
        public bool ok;
        public TMP_Text label;
        public Rect rect;            // the housing footprint the key will have once it is wide (paper-local metres)
        public Rect face;            // the cap face it will have once it is wide
        public bool wideNow;         // the wide geometry is already in place
    }

    static Rect R(float cx, float cy, float w, float h) => new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);

    /// <summary>
    /// Size and place one poke key with a UNIFORM scale on its wrapper (Btn_*): the scale makes the
    /// measured housing exactly <paramref name="height"/> tall, the footprint is centred on
    /// <paramref name="centre"/> and the back of the housing rests on the sheet. ButtonVisual, Cap3D
    /// and Housing3D are not moved relative to the key. The label is fitted to the face the key will
    /// have once it is <paramref name="ratio"/> times as wide as it is tall.
    /// </summary>
    static KeyResult LayoutKey(Transform doc, Transform actions, Transform key, Vector2 centre, float height, float ratio,
        float sheetZ, string text, float sizeMm, Color capColor, Color textColor, string matName,
        float heldScale, StringBuilder sb, List<string> warn)
    {
        var res = new KeyResult();
        var poke = key.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(m => m != null && m.GetType().Name == "PokeInteractable");
        if (poke == null) { sb.AppendLine($"  SKIP {key.name}: no PokeInteractable"); return res; }
        var block = poke.transform;
        var visual = Under(block, "ButtonVisual");
        var cap = visual != null ? Under(visual, "Cap3D") : null;
        var housing = Under(block, "Housing3D");
        if (visual == null || cap == null || housing == null) { sb.AppendLine($"  SKIP {key.name}: ButtonVisual / Cap3D / Housing3D not found"); return res; }

        WideMarker(key, block, ratio);

        // Unit footprint: the housing's size on the paper with the wrapper at scale one.
        key.localRotation = Quaternion.identity;
        key.localScale = Vector3.one;
        if (!MeshBounds(housing, doc, out var umn, out var umx)) { sb.AppendLine($"  SKIP {key.name}: housing mesh not readable"); return res; }
        float unitW = umx.x - umn.x, unitH = umx.y - umn.y;
        if (unitW < 1e-5f || unitH < 1e-5f) { sb.AppendLine($"  SKIP {key.name}: housing has no footprint"); return res; }
        float s = height / unitH;
        key.localScale = new Vector3(s, s, s);                              // uniform: no stretched rim
        Vector3 br = Rel(block, doc);
        if (Mathf.Abs(br.x - br.y) > 0.02f * Mathf.Max(br.x, br.y) || Mathf.Abs(br.x - br.z) > 0.02f * Mathf.Max(br.x, br.z))
            warn.Add($"{key.name}: the object with the PokeInteractable ('{block.name}') has its own non-uniform scale {block.localScale}; this builder only makes the wrapper uniform");

        key.localPosition = InParent(doc, actions, new Vector3(centre.x, centre.y, sheetZ - 0.006f));
        // Centre the footprint (the housing is not necessarily centred on the pivot) and rest its back on the sheet.
        MeshBounds(housing, doc, out var hmn, out var hmx);
        Vector3 delta = new Vector3(centre.x - (hmn.x + hmx.x) * 0.5f, centre.y - (hmn.y + hmx.y) * 0.5f, (sheetZ - 0.0002f) - hmx.z);
        key.position += doc.TransformVector(delta);
        MeshBounds(housing, doc, out hmn, out hmx);
        EditorUtility.SetDirty(key);

        // The rest guard must hold the pose the visual actually has (it is not moved here).
        var guard = visual.GetComponent<PokeVisualRestGuard>();
        if (guard != null)
        {
            guard.restLocalPosition = visual.localPosition;
            guard.hasAuthoredRest = true;
            EditorUtility.SetDirty(guard);
        }

        // Cap colour: the cap's material, and the same colour in the SDK colour visual's states.
        var capRenderer = cap.GetComponent<MeshRenderer>();
        if (capRenderer != null)
        {
            capRenderer.sharedMaterial = Mat(matName, capColor, unlit: false);
            EditorUtility.SetDirty(capRenderer);
        }
        var icv = Comp(visual, "InteractableColorVisual");
        if (icv != null)
        {
            var so = new SerializedObject(icv);
            SetColorState(so, "_normalColorState", capColor);
            SetColorState(so, "_hoverColorState", Color.Lerp(capColor, Teal, 0.15f));
            SetColorState(so, "_selectColorState", Color.Lerp(capColor, Ink, 0.15f));
            so.ApplyModifiedProperties();
        }

        // Depth relation at full press (measured for the report).
        MeshBounds(cap, block, out var cbmn, out _);
        MeshBounds(housing, block, out var hbmn, out _);
        float travel = -block.InverseTransformPoint(visual.position).z;
        float proudUnits = hbmn.z - (cbmn.z + travel);                     // rim behind the stopped cap face: positive is good
        float toMm = block.lossyScale.z * 1000f;
        float travelMm = travel * toMm;
        if (travelMm < 3f || travelMm > 15f) warn.Add($"{key.name}: press travel {travelMm:F1} mm is outside 3..15 mm");
        if (proudUnits <= 0f) warn.Add($"{key.name}: the cap face is not proud of the housing rim at full press");

        // The face the key has now, and the one it will have when wide (the side rim stays as thick as the top rim).
        MeshBounds(cap, doc, out var cmn, out var cmx);
        float capW = cmx.x - cmn.x, capH = cmx.y - cmn.y;
        float housingW = hmx.x - hmn.x, housingH = hmx.y - hmn.y;
        float wideHousingW = housingH * ratio;
        res.wideNow = housingW > wideHousingW * 0.9f;
        float wideCapW = res.wideNow ? capW : Mathf.Max(capW, wideHousingW - (housingH - capH));
        res.rect = R(centre.x, centre.y, res.wideNow ? housingW : wideHousingW, housingH);
        res.face = R((cmn.x + cmx.x) * 0.5f, (cmn.y + cmx.y) * 0.5f, wideCapW, capH);
        if (Mathf.Min(wideCapW, capH) * 1000f < 22f) warn.Add($"{key.name}: cap face {wideCapW * 1000f:F0} x {capH * 1000f:F0} mm is under 22 mm in its smaller dimension");

        // Label on the cap face, 0.5 mm proud, boxed for the wide face.
        var labels = visual.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text label = labels.Length > 0 ? labels[0] : null;
        if (labels.Length > 1) warn.Add($"{key.name}: {labels.Length} labels under ButtonVisual; only '{labels[0].name}' is laid out");
        if (label != null)
        {
            if (label.transform.parent != visual) label.transform.SetParent(visual, false);
            PlaceText(doc, label, new Vector2((cmn.x + cmx.x) * 0.5f, (cmn.y + cmx.y) * 0.5f), cmn.z - TextProud);
            StyleText(label, new Vector2(wideCapW * 100f * 0.86f, capH * 100f * 0.70f), sizeMm, FontStyles.Bold, textColor,
                TextAlignmentOptions.Center, false, text);
        }
        else warn.Add($"{key.name}: no label under ButtonVisual");
        res.label = label;
        res.ok = true;

        sb.AppendLine($"  {key.name,-12} housing now x {hmn.x:F4}..{hmx.x:F4}, y {hmn.y:F4}..{hmx.y:F4} ({housingW * 100f:F1} x {housingH * 100f:F1} cm{(res.wideNow ? ", wide" : ", square until the Kit builder runs")}); " +
                      $"planned wide rect x {res.rect.xMin:F4}..{res.rect.xMax:F4} ({res.rect.width * 100f:F1} x {res.rect.height * 100f:F1} cm, marker {RatioName(ratio)} under '{block.name}'); " +
                      $"cap face now {capW * 100f:F1} x {capH * 100f:F1} cm, planned {wideCapW * 100f:F1} x {capH * 100f:F1} cm; " +
                      $"wrapper scale ({key.localScale.x:F3}, {key.localScale.y:F3}, {key.localScale.z:F3}); travel {travelMm:F1} mm; " +
                      $"cap face stays {proudUnits * toMm:F1} mm proud of the rim at full press; housing back {(sheetZ - hmx.z) * 1000f:F1} mm in front of the sheet; " +
                      (label != null ? $"label '{label.text}' box {label.rectTransform.sizeDelta.x:F1} x {label.rectTransform.sizeDelta.y:F1} cm, {TextHeights(label, heldScale)}" : "NO LABEL"));
        return res;
    }

    static string TextHeights(TMP_Text t, float heldScale)
    {
        float k = t.transform.lossyScale.y * 100f;      // font size N at scale 0.01 is N mm
        float max = (t.enableAutoSizing ? t.fontSizeMax : t.fontSize) * k, min = (t.enableAutoSizing ? t.fontSizeMin : t.fontSize) * k;
        return $"text {max:F1} mm (min {min:F1}) full, {max * heldScale:F1} mm carried";
    }

    /// <summary>The label's box in paper-local metres (rotation ignored).</summary>
    static Rect BoxOf(Transform doc, TMP_Text t)
    {
        Vector3 p = doc.InverseTransformPoint(t.transform.position);
        Vector3 rel = Rel(t.transform, doc);
        Vector2 box = t.rectTransform.sizeDelta;
        return R(p.x, p.y, box.x * rel.x, box.y * rel.y);
    }

    static void ReportText(StringBuilder sb, Transform doc, TMP_Text t, float heldScale)
    {
        if (t == null) return;
        Vector3 p = doc.InverseTransformPoint(t.transform.position);
        Rect b = BoxOf(doc, t);
        string shown = (t.text ?? "").Replace('\n', ' ');
        sb.AppendLine($"  {t.name,-16} centre ({p.x:F4}, {p.y:F4}, {p.z:F4}) box x {b.xMin:F4}..{b.xMax:F4}, y {b.yMin:F4}..{b.yMax:F4} ({b.width * 100f:F1} x {b.height * 100f:F1} cm); {TextHeights(t, heldScale)}; '{shown}'");
    }

    static Rect QuadRect(Transform doc, Transform q)
    {
        if (q == null || !OwnMeshBounds(q, doc, out var mn, out var mx)) return new Rect();
        return Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
    }

    static void ReportQuad(StringBuilder sb, Transform doc, Transform q)
    {
        if (q == null) return;
        if (!OwnMeshBounds(q, doc, out var mn, out var mx)) return;
        sb.AppendLine($"  {q.name,-16} x {mn.x:F4}..{mx.x:F4}, y {mn.y:F4}..{mx.y:F4}, z {mn.z:F4} ({(mx.x - mn.x) * 100f:F1} x {(mx.y - mn.y) * 100f:F1} cm)");
    }

    /// <summary>
    /// How much of a single-line label's box its longest expected string needs, measured with the
    /// font it has now. Warns under 10 percent slack (a true bold cut is a little wider).
    /// </summary>
    static void Slack(StringBuilder sb, List<string> warn, TMP_Text t, string sample, bool mayShrink)
    {
        if (t == null || string.IsNullOrEmpty(sample)) return;
        float box = t.rectTransform.sizeDelta.x, need;
        try
        {
            float keep = t.fontSize; bool auto = t.enableAutoSizing;
            t.enableAutoSizing = false;
            t.fontSize = auto ? t.fontSizeMax : keep;
            need = t.GetPreferredValues(sample).x;
            t.fontSize = keep;
            t.enableAutoSizing = auto;
        }
        catch (System.Exception e)
        {
            sb.AppendLine($"  {t.name,-16} '{sample}': width not measurable here ({e.GetType().Name})");
            return;
        }
        if (need <= 0f || float.IsNaN(need) || float.IsInfinity(need)) { sb.AppendLine($"  {t.name,-16} '{sample}': width not measurable here"); return; }
        float slack = 1f - need / Mathf.Max(0.01f, box);
        sb.AppendLine($"  {t.name,-16} '{sample}' needs {need:F1} of {box:F1} cm at {(t.enableAutoSizing ? t.fontSizeMax : t.fontSize):F1} mm (slack {slack * 100f:F0} percent)");
        if (slack < 0.10f)
            warn.Add($"{t.name}: '{sample}' leaves {slack * 100f:F0} percent width slack (want 10){(mayShrink ? "; it will auto-shrink" : "; it does NOT auto-shrink and may be cut")}");
    }

    static bool Overlaps(Rect a, Rect b, out float w, out float h)
    {
        w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        return w > 0.0002f && h > 0.0002f;
    }

    static void Inside(List<string> warn, string what, Rect inner, string container, Rect outer, float pad = 0f)
    {
        if (inner.width <= 0f || outer.width <= 0f) return;
        if (inner.xMin < outer.xMin + pad - 1e-4f || inner.xMax > outer.xMax - pad + 1e-4f || inner.yMin < outer.yMin + pad - 1e-4f || inner.yMax > outer.yMax - pad + 1e-4f)
            warn.Add($"{what} (x {inner.xMin:F4}..{inner.xMax:F4}, y {inner.yMin:F4}..{inner.yMax:F4}) is not inside {container} (x {outer.xMin:F4}..{outer.xMax:F4}, y {outer.yMin:F4}..{outer.yMax:F4}){(pad > 0f ? $" with {pad * 1000f:F0} mm padding" : "")}");
    }

    public static string Apply()
    {
        var all = All();
        var sb = new StringBuilder();
        var warn = new List<string>();
        var doc = all.FirstOrDefault(t => t != null && t.name == "DecisionDocument");
        if (doc == null) return "BuildPaperV3: DecisionDocument not found\n";
        var card = doc.Find("Card");
        var actions = doc.Find("Actions");
        var panel = doc.GetComponentInChildren<DecisionPanel>(true);
        var held = doc.GetComponent<HeldPaperController>();
        if (card == null || actions == null || panel == null || held == null)
            return "BuildPaperV3: Card / Actions / DecisionPanel / HeldPaperController not found on DecisionDocument (run BuildPaperV2 first)\n";
        var cardFilter = card.GetComponent<MeshFilter>();
        if (cardFilter == null || cardFilter.sharedMesh == null) return "BuildPaperV3: Card has no mesh\n";
        // A plain unit quad for new plates. The Card's own mesh is one unless the Kit builder has already replaced it.
        var tmpQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var quad = tmpQuad.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(tmpQuad);
        float heldScale = held.heldScale;

        var cover = doc.Find("Cover");
        var pending = doc.Find("Pending");
        var outcome = doc.Find("Outcome");
        var details = doc.Find("Details");
        var zone = doc.Find("StampZone");
        if (cover == null || pending == null || outcome == null || details == null || zone == null)
            return "BuildPaperV3: Cover / Pending / Outcome / Details / StampZone not found (run BuildPaperV2 first)\n";

        var recoOld = Under(doc, "Reco");
        var template = recoOld != null ? recoOld.GetComponent<TMP_Text>() : null;
        if (template == null) template = doc.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(x => x != null && x.transform.parent == doc);
        if (template == null) return "BuildPaperV3: no label on the paper to clone\n";

        // Everything is measured with its group active; the saved state is set at the end.
        foreach (var g in new[] { cover, pending, outcome, details, zone, actions }) g.gameObject.SetActive(true);

        sb.AppendLine($"paper root: local scale {doc.localScale}, lossy {doc.lossyScale}, carried at {heldScale:F2}");

        // ── sheet ──
        card.localPosition = new Vector3(0f, 0f, card.localPosition.z);
        card.localRotation = Quaternion.identity;
        card.localScale = new Vector3(W, H, 1f);
        card.GetComponent<MeshRenderer>().sharedMaterial = Mat("Paper_Sheet", Paper);
        EditorUtility.SetDirty(card);
        OwnMeshBounds(card, doc, out var sheetMin, out var sheetMax);
        float cardZ = sheetMin.z;                                           // the printed face
        float plateZ = cardZ - PlateProud, textZ = cardZ - TextProud, plateTextZ = plateZ - TextProud;
        float chipZ = plateZ - PlateProud, chipTextZ = chipZ - TextProud;   // a small chip lying on a plate
        var sheetRect = Rect.MinMaxRect(sheetMin.x, sheetMin.y, sheetMax.x, sheetMax.y);

        // ── header: one band ──
        var tintMat = Mat("PaperV3_Tint", PaperTint);
        var ruleMat = Mat("PaperV3_Rule", PaperRule);
        var headerBand = Quad(doc, doc, "HeaderBand", quad, tintMat, new Vector2(0f, BandY), plateZ, new Vector2(W, BandH));
        var headerRule = Quad(doc, doc, "HeaderRule", quad, ruleMat, new Vector2(0f, BandY - BandH * 0.5f), chipZ, new Vector2(W, 0.0008f));
        var roleChip = Quad(doc, doc, "RoleChip", quad, Mat("PaperV3_RoleChip", InfoBlue), new Vector2(Left + RoleChipW * 0.5f, HeaderMidY), chipZ, new Vector2(RoleChipW, RoleChipH));
        // The V2 mode band becomes the mode chip at the right of the header (same object, still tinted by the panel).
        var modeChip = Quad(doc, doc, "ModeBand", quad, Mat("Paper_ModeBand", Amber), new Vector2(ModeChipX, HeaderMidY), chipZ, new Vector2(ModeChipW, ModeChipH));
        float headerTextW = HeaderTextRight - HeaderTextLeft, headerTextX = (HeaderTextLeft + HeaderTextRight) * 0.5f;
        var fromLabel = Text(doc, doc, "FromLabel", template, new Vector2(headerTextX, FromY), plateTextZ, new Vector2(headerTextW * 100f, 0.9f),
            LabelSize, FontStyles.Bold, Ink, TextAlignmentOptions.Left, false, "From the Researcher");
        var needs = Text(doc, doc, "NeedsLabel", template, new Vector2(headerTextX, NeedsY), plateTextZ, new Vector2(headerTextW * 100f, 0.9f),
            LabelSize, FontStyles.Normal, Ink, TextAlignmentOptions.Left, false, "Needs your decision");
        var badge = Text(doc, doc, "Badge", template, new Vector2(ModeChipX, HeaderMidY), chipTextZ, new Vector2(ModeChipW * 100f - 0.8f, 1.2f),
            LabelSize, FontStyles.Bold, Ink, TextAlignmentOptions.Center, false, "SCRIPTED DEMO");

        // ── the question, summary, recommendation, provenance ──
        var title = Text(doc, doc, "Title", template, new Vector2(0f, TitleY), textZ, new Vector2(ContentW * 100f, TitleBoxH * 100f),
            Display, FontStyles.Bold, Ink, TextAlignmentOptions.BottomLeft, true, null);
        var summary = Text(doc, doc, "Summary", template, new Vector2(ColX, SummaryY), textZ, new Vector2(ColW * 100f, SummaryBoxH * 100f),
            SummarySize, FontStyles.Normal, Ink, TextAlignmentOptions.TopLeft, true, null, Body);

        var recoBlock = Group(doc, "RecoBlock");
        Adopt(doc, recoBlock, "Reco");
        var recoPlate = Quad(doc, recoBlock, "RecoPlate", quad, tintMat, new Vector2(ColX, RecoPlateY), plateZ, new Vector2(ColW, RecoPlateH));
        var recoRule = Quad(doc, recoBlock, "RecoRule", quad, Mat("PaperV3_RecoRule", Success), new Vector2(Left + RecoRuleW * 0.5f, RecoPlateY), chipZ, new Vector2(RecoRuleW, RecoPlateH));
        float recoTextW = RecoTextRight - RecoTextLeft, recoTextX = (RecoTextLeft + RecoTextRight) * 0.5f;
        var reco = Text(doc, recoBlock, "Reco", template, new Vector2(recoTextX, RecoLineY), plateTextZ, new Vector2(recoTextW * 100f, 1.2f),
            RecoSize, FontStyles.Bold, MarkGreen, TextAlignmentOptions.Left, false, null);
        var recoReason = Text(doc, recoBlock, "RecoReason", template, new Vector2(recoTextX, RecoReasonY), plateTextZ, new Vector2(recoTextW * 100f, 1.7f),
            LabelSize, FontStyles.Normal, InkSoft, TextAlignmentOptions.TopLeft, true, "", LabelSize);   // filled from the event's rationale at run time

        var provRule = Quad(doc, doc, "ProvRule", quad, ruleMat, new Vector2(0f, ProvRuleY), plateZ, new Vector2(ContentW, 0.0006f));
        // Provenance is an honesty requirement: fixed size, never auto-shrunk, may wrap to two lines.
        var prov = Text(doc, doc, "Provenance", template, new Vector2(0f, ProvY), textZ, new Vector2(ContentW * 100f, ProvBoxH * 100f),
            ProvSize, FontStyles.Normal, InkSoft, TextAlignmentOptions.TopLeft, true, null, ProvSize);
        var status = Text(doc, doc, "Status", template, new Vector2(0f, StatusY), textZ, new Vector2(ContentW * 100f, StatusBoxH * 100f),
            LabelSize, FontStyles.Bold, Ink, TextAlignmentOptions.Left, false, "");

        if (headerBand == null || fromLabel == null || needs == null || badge == null || title == null || summary == null || reco == null
            || recoReason == null || prov == null || status == null)
            return sb + "BuildPaperV3: a paper label exists without a TextMeshPro component; nothing saved\n";

        // ── stamp target ──
        zone.localRotation = Quaternion.identity;
        zone.localScale = Vector3.one;
        zone.localPosition = new Vector3(StampX, StampY, cardZ - 0.001f);
        EditorUtility.SetDirty(zone);
        foreach (var old in new[] { "ZoneMarker", "ZoneRing", "Outline_Top", "Outline_Bottom", "Outline_Left", "Outline_Right" })
            Hide(zone.Find(old));
        var stampPlate = Quad(doc, zone, "StampPlate", quad, Mat("PaperV2_StampPlate", PaperTint), new Vector2(StampX, StampY), plateZ, new Vector2(StampW, StampH));
        // The outline stays on the sheet after the decision (the mark is printed inside it); the
        // tinted plate and the words "Stamp here" belong to the zone, which the panel hides.
        var frame = Group(doc, "StampFrame");
        var outlineMat = Mat("PaperV2_Outline", InkSoft);
        const float line = 0.0012f;
        var outline = new[]
        {
            Quad(doc, frame, "Frame_Top", quad, outlineMat, new Vector2(StampX, StampTop - line * 0.5f), chipZ, new Vector2(StampW, line)),
            Quad(doc, frame, "Frame_Bottom", quad, outlineMat, new Vector2(StampX, StampBottom + line * 0.5f), chipZ, new Vector2(StampW, line)),
            Quad(doc, frame, "Frame_Left", quad, outlineMat, new Vector2(StampX - StampW * 0.5f + line * 0.5f, StampY), chipZ, new Vector2(line, StampH)),
            Quad(doc, frame, "Frame_Right", quad, outlineMat, new Vector2(StampX + StampW * 0.5f - line * 0.5f, StampY), chipZ, new Vector2(line, StampH)),
        };
        var stampHere = Text(doc, zone, "StampHere", template, new Vector2(StampX, StampY + 0.010f), plateTextZ, new Vector2(StampW * 100f - 1.0f, 1.4f),
            StampHereSize, FontStyles.Bold, Ink, TextAlignmentOptions.Center, false, "Stamp here");
        var stampHint = Text(doc, zone, "StampHint", template, new Vector2(StampX, StampY - 0.012f), plateTextZ, new Vector2(StampW * 100f - 1.0f, 2.0f),
            Caption, FontStyles.Normal, InkSoft, TextAlignmentOptions.Center, true, "Use a stamp instead of a key", Caption);

        // The provisional and the confirmed mark are printed inside the outline.
        var markT = doc.Find("StampMark");
        var mark = markT != null ? markT.GetComponent<TMP_Text>() : null;
        if (mark != null)
        {
            PlaceText(doc, mark, new Vector2(StampX, StampY), textZ);
            markT.localRotation = Quaternion.Euler(0f, 0f, -12f);
            StyleText(mark, new Vector2(StampW * 100f - 0.6f, 2.4f), MarkSize, FontStyles.Bold, MarkGreen, TextAlignmentOptions.Center, false, "APPROVED");
        }
        else warn.Add("StampMark not found under DecisionDocument: there is nowhere to print the provisional or the confirmed mark");

        // ── keys ──
        actions.localPosition = Vector3.zero;
        actions.localRotation = Quaternion.identity;
        actions.localScale = Vector3.one;
        var approve = actions.Find("Btn_Approve");
        var reject = actions.Find("Btn_Reject");
        if (approve == null || reject == null) return sb + "BuildPaperV3: Btn_Approve / Btn_Reject not found under Actions\n";
        var why = actions.Find("Btn_Ask_why");
        var detailsKey = actions.Find("Btn_Details");
        var changes = actions.Find("Btn_Changes");
        var voiceKey = actions.Find("Btn_Voice");

        sb.AppendLine("keys (paper-local metres):");
        var kApprove = LayoutKey(doc, actions, approve, new Vector2(-PrimX, PrimY), PrimH, PrimRatio, cardZ,
            "Approve", Heading, Success, Ink, "PaperV2_Key_Approve", heldScale, sb, warn);
        var kReject = LayoutKey(doc, actions, reject, new Vector2(PrimX, PrimY), PrimH, PrimRatio, cardZ,
            "Reject", Heading, Coral, Ink, "PaperV2_Key_Reject", heldScale, sb, warn);

        var chipKeys = new[] { why, detailsKey, changes, voiceKey };
        var chipWords = new[] { "Why", "Details", "Changes", "Voice" };
        float pitch4 = (ContentW - ChipW) / 3f;
        var x4 = new float[4];
        var x3 = new[] { Left + ChipW * 0.5f, 0f, Right - ChipW * 0.5f, 0f };
        for (int i = 0; i < 4; i++) x4[i] = Left + ChipW * 0.5f + pitch4 * i;
        x3[3] = x4[3];
        var kChips = new KeyResult[4];
        var pos4 = new Vector3[4];
        var pos3 = new Vector3[4];
        TMP_Text detailsChipLabel = null;
        for (int i = 0; i < 4; i++)
        {
            if (chipKeys[i] == null) { sb.AppendLine($"  chip '{chipWords[i]}': key not found, slot left empty"); continue; }
            kChips[i] = LayoutKey(doc, actions, chipKeys[i], new Vector2(x4[i], ChipY), ChipH, ChipRatio, cardZ,
                chipWords[i], LabelSize, Lavender, Ink, "PaperV2_Key_Chip", heldScale, sb, warn);
            pos4[i] = chipKeys[i].localPosition;
            pos3[i] = pos4[i] + actions.InverseTransformVector(doc.TransformVector(new Vector3(x3[i] - x4[i], 0f, 0f)));
            if (chipKeys[i] == detailsKey) detailsChipLabel = kChips[i].label;
        }

        // Wiring is BuildPaperV2's and is not changed here; it is only checked.
        var delivery = Object.FindAnyObjectByType<DocumentDeliveryController>(FindObjectsInactive.Include);
        var wiring = new (Transform key, string method)[]
        {
            (approve, nameof(DocumentDeliveryController.OnApprove)), (reject, nameof(DocumentDeliveryController.OnReject)),
            (why, nameof(DocumentDeliveryController.OnAskWhy)), (detailsKey, nameof(DocumentDeliveryController.OnToggleDetails)),
            (changes, nameof(DocumentDeliveryController.OnRequestChanges)),
        };
        foreach (var (key, method) in wiring)
        {
            if (key == null) continue;
            string current = SelectMethod(key, out var target);
            if (current != method || target != delivery)
                warn.Add($"{key.name} is wired to {(target != null ? target.GetType().Name : "nothing")}.{current ?? "(none)"}, expected DocumentDeliveryController.{method} (not changed by this builder)");
        }

        // ── pending: flat inert plates where the primary keys are ──
        Rename(pending, "GhostApprove", "GhostApprovePlate");
        Rename(pending, "GhostReject", "GhostRejectPlate");
        var plateMat = Mat("PaperV2_Plate", Housing);
        var ghostA = Quad(doc, pending, "GhostApprovePlate", quad, plateMat, new Vector2(-PrimX, PrimY), plateZ, new Vector2(PrimW, PrimH));
        var ghostR = Quad(doc, pending, "GhostRejectPlate", quad, plateMat, new Vector2(PrimX, PrimY), plateZ, new Vector2(PrimW, PrimH));
        var ghostAText = Text(doc, pending, "GhostApproveLabel", template, new Vector2(-PrimX, PrimY), plateTextZ, new Vector2(PrimW * 100f - 2.0f, 2.2f),
            Heading, FontStyles.Normal, OnPlate, TextAlignmentOptions.Center, false, "Approve");
        var ghostRText = Text(doc, pending, "GhostRejectLabel", template, new Vector2(PrimX, PrimY), plateTextZ, new Vector2(PrimW * 100f - 2.0f, 2.2f),
            Heading, FontStyles.Normal, OnPlate, TextAlignmentOptions.Center, false, "Reject");

        // ── outcome: the confirmed result in words, where the primary keys were ──
        var outcomePlate = Quad(doc, outcome, "OutcomePlate", quad, Mat("PaperV2_Outcome", Success), new Vector2(0f, PrimY), plateZ, new Vector2(ContentW, PrimH));
        var outcomeTitle = Text(doc, outcome, "OutcomeTitle", template, new Vector2(0f, PrimY + 0.008f), plateTextZ, new Vector2(ContentW * 100f - 2.4f, 1.5f),
            Heading, FontStyles.Bold, OnPlate, TextAlignmentOptions.Center, false, "Approved");
        var outcomeDetail = Text(doc, outcome, "OutcomeDetail", template, new Vector2(0f, PrimY - 0.0085f), plateTextZ, new Vector2(ContentW * 100f - 2.4f, 1.2f),
            Body, FontStyles.Normal, OnPlate, TextAlignmentOptions.Center, false, "The developer is continuing.");

        // ── details: an under-sheet that slides out from behind the bottom edge ──
        details.localPosition = Vector3.zero; details.localRotation = Quaternion.identity; details.localScale = Vector3.one;   // authored open
        Rename(details, "DetailsPlate", "DetailsSheet");
        Hide(details.Find("DetailsStrip"));
        float detailsZ = cardZ + DetailsBehind;
        var detailsSheet = Quad(doc, details, "DetailsSheet", quad, Mat("PaperV3_DetailsSheet", PaperDetails), new Vector2(0f, DetailsY), detailsZ, new Vector2(W, DetailsH));
        var foldLine = Quad(doc, details, "FoldLine", quad, ruleMat, new Vector2(0f, FoldLineY), detailsZ - 0.0002f, new Vector2(W, 0.0008f));
        var body = Text(doc, details, "Body", template, new Vector2(0f, DetailsBodyTop - DetailsBodyH * 0.5f), detailsZ - TextProud, new Vector2(ContentW * 100f, DetailsBodyH * 100f),
            DetailsSize, FontStyles.Normal, Ink, TextAlignmentOptions.TopLeft, true, null, Body);
        var answer = Text(doc, details, "Answer", template, new Vector2(0f, DetailsAnswerTop - DetailsAnswerH * 0.5f), detailsZ - TextProud, new Vector2(ContentW * 100f, DetailsAnswerH * 100f),
            DetailsSize, FontStyles.Normal, InkSoft, TextAlignmentOptions.TopLeft, true, null, Body);
        if (body == null || answer == null || ghostAText == null || ghostRText == null || outcomeTitle == null || outcomeDetail == null || stampHere == null || stampHint == null)
            return sb + "BuildPaperV3: a details / pending / outcome / stamp label exists without a TextMeshPro component; nothing saved\n";

        // ── cover (kept): only the big word is brought down so that a true bold cut still fits ──
        TMP_Text CoverText(string n) { var x = cover.Find(n); return x != null ? x.GetComponent<TMP_Text>() : null; }
        var coverMode = CoverText("CoverMode");
        var coverTitle = CoverText("CoverTitle");
        var tabLabel = CoverText("GrabTabLabel");
        float carriedFloor = Caption / Mathf.Max(0.05f, heldScale);                 // 5.5 mm at carried scale, in paper units
        if (coverTitle != null)
        {
            coverTitle.enableAutoSizing = true;
            coverTitle.fontSizeMax = 42f; coverTitle.fontSize = 42f;
            coverTitle.fontSizeMin = Mathf.Max(carriedFloor, 42f * 0.72f);
            EditorUtility.SetDirty(coverTitle);
        }

        // ── wiring ──
        panel.badge = badge; panel.title = title; panel.summary = summary; panel.recommendation = reco;
        panel.provenance = prov; panel.status = status; panel.body = body; panel.answer = answer;
        panel.needsLabel = needs;
        panel.fromLabel = fromLabel;
        panel.roleChip = roleChip.GetComponent<Renderer>();
        panel.recommendationReason = recoReason;
        panel.recoPlate = recoPlate.gameObject;
        panel.recoRule = recoRule.GetComponent<Renderer>();
        panel.outcomeTitle = outcomeTitle; panel.outcomeDetail = outcomeDetail;
        panel.outcomePlate = outcomePlate.GetComponent<Renderer>();
        panel.detailsChipLabel = detailsChipLabel;
        panel.modeBand = modeChip.GetComponent<Renderer>();
        panel.demoBand = Amber; panel.liveBand = Success;
        panel.demoBandText = Ink; panel.liveBandText = Ink;        // design system: Ink on every coloured surface
        panel.actionsRoot = actions.gameObject;
        panel.detailsRoot = details.gameObject;
        panel.stampZoneRoot = zone.gameObject;
        panel.pendingRoot = pending.gameObject;
        panel.outcomeRoot = outcome.gameObject;
        panel.voiceChip = voiceKey != null ? voiceKey.gameObject : null;
        panel.chipKeys = chipKeys;
        panel.chipPositionsWithVoice = pos4;
        panel.chipPositionsWithoutVoice = pos3;
        panel.detailsSlides = true;
        panel.detailsOpenLocalPosition = Vector3.zero;
        panel.detailsTuckedLocalPosition = details.parent.InverseTransformVector(doc.TransformVector(new Vector3(0f, DetailsTuck, 0f)));
        panel.detailsSlideSeconds = 0.3f;
        EditorUtility.SetDirty(panel);

        // Content shown once the paper is taken. The keys, the stamp zone, the details sheet and the
        // pending / outcome plates are NOT listed: the panel owns them (it knows the response phase).
        held.hideWhileHeld = new[] { headerBand, headerRule, roleChip, modeChip, fromLabel.transform, needs.transform, badge.transform,
                title.transform, summary.transform, recoBlock, provRule, prov.transform, status.transform, frame }
            .Where(t => t != null).Select(t => t.gameObject).ToArray();
        held.showWhileHeld = new[] { cover.gameObject };
        held.panel = panel;
        held.sheetHeight = H;
        EditorUtility.SetDirty(held);

        var stampCtl = doc.GetComponent<ApprovalStampController>();
        if (stampCtl != null)
        {
            if (mark != null) stampCtl.mark = mark;
            stampCtl.targetPlate = stampPlate.GetComponent<Renderer>();
            stampCtl.targetOutline = outline.Select(o => o.GetComponent<Renderer>()).ToArray();
            stampCtl.targetIdle = PaperTint; stampCtl.targetNear = TargetNear;
            stampCtl.outlineIdle = InkSoft; stampCtl.outlineNear = Teal;
            EditorUtility.SetDirty(stampCtl);
        }
        else warn.Add("ApprovalStampController not found on DecisionDocument: no stamp target highlight, no provisional mark, no confirmed mark");

        sb.AppendLine("stamps:");
        int stamps = 0;
        foreach (var st in all.Where(t => t != null).Select(t => t.GetComponent<StampTool>()).Where(s => s != null))
        {
            stamps++;
            st.paper = held;
            st.stampZone = zone;
            var visual = st.transform.Find("StampVisual");
            st.pressVisual = visual;
            st.pressDip = 0.006f; st.pressSeconds = 0.16f; st.returnSeconds = 0.4f; st.returnHome = true;
            st.highlightRadius = 0.10f;
            EditorUtility.SetDirty(st);
            bool onPaper = st.transform.IsChildOf(doc);
            if (onPaper) warn.Add($"{st.name} is parented under the paper; the stamps must never be children of the paper");
            if (visual == null) warn.Add($"{st.name} has no 'StampVisual' child: the press dip is skipped for it");
            sb.AppendLine($"  {st.name}: parent '{(st.transform.parent != null ? st.transform.parent.name : "(scene root)")}', rest local {st.transform.localPosition:F3}, " +
                          $"press visual {(visual != null ? "'StampVisual' (dips 6 mm over 160 ms)" : "none (no dip)")}, returns to rest in 400 ms, " +
                          $"contact radius {st.contactRadius * 100f:F0} cm, highlight radius {st.highlightRadius * 100f:F0} cm from the target centre");
        }
        if (stamps == 0) warn.Add("no StampTool found in the scene");

        // ── measurements (everything still active) ──
        sb.AppendLine($"sheet: x {sheetMin.x:F4}..{sheetMax.x:F4}, y {sheetMin.y:F4}..{sheetMax.y:F4}, printed face z {cardZ:F4} ({(sheetMax.x - sheetMin.x) * 100f:F1} x {(sheetMax.y - sheetMin.y) * 100f:F1} cm; {(sheetMax.x - sheetMin.x) * heldScale * 100f:F1} cm carried); margins {Margin * 100f:F1} cm");
        sb.AppendLine("plates and rules (paper-local metres):");
        foreach (var q in new[] { headerBand, headerRule, roleChip, modeChip, recoPlate, recoRule, provRule, stampPlate, outline[0], outline[1], outline[2], outline[3],
                     ghostA, ghostR, outcomePlate, detailsSheet, foldLine, cover.Find("CoverBand"), cover.Find("GrabTab"), cover.Find("UnderSheet1"), cover.Find("UnderSheet2") })
            ReportQuad(sb, doc, q);
        sb.AppendLine($"  details sheet when tucked: y {DetailsY - DetailsH * 0.5f + DetailsTuck:F4}..{DetailsY + DetailsH * 0.5f + DetailsTuck:F4} (inside the main sheet, {DetailsBehind * 1000f:F0} mm behind its face); open: overlaps the bottom edge by {DetailsOverlap * 100f:F1} cm; slide {DetailsTuck * 100f:F1} cm in 300 ms");
        sb.AppendLine("text (paper-local metres; height at full size and at carried scale):");
        foreach (var t in new[] { fromLabel, needs, badge, title, summary, reco, recoReason, prov, status, stampHere, stampHint, mark, ghostAText, ghostRText, outcomeTitle, outcomeDetail, body, answer })
            ReportText(sb, doc, t, heldScale);
        sb.AppendLine("cover text (seen only at carried scale):");
        foreach (var t in new[] { coverMode, coverTitle, tabLabel })
            ReportText(sb, doc, t, heldScale);

        sb.AppendLine("single-line labels: width needed by the longest expected string, with the font each label has now:");
        Slack(sb, warn, fromLabel, "From the Researcher", true);
        Slack(sb, warn, needs, "Revised proposal 2. Needs your decision", true);
        Slack(sb, warn, badge, "SCRIPTED DEMO", true);
        Slack(sb, warn, reco, "Recommended: Request changes", true);
        Slack(sb, warn, status, "Couldn't reach the desktop. Try again.", true);
        Slack(sb, warn, stampHere, "Stamp here", true);
        Slack(sb, warn, mark, "APPROVED", true);
        Slack(sb, warn, mark, "REJECTED", true);
        Slack(sb, warn, ghostAText, "Approve", true);
        Slack(sb, warn, ghostRText, "Reject", true);
        Slack(sb, warn, outcomeTitle, "Changes requested", true);
        Slack(sb, warn, outcomeDetail, "The recommendation goes back for revision.", true);
        Slack(sb, warn, kApprove.label, "Approve", true);
        Slack(sb, warn, kReject.label, "Reject", true);
        for (int i = 0; i < 4; i++) Slack(sb, warn, kChips[i].label, chipWords[i], true);
        Slack(sb, warn, detailsChipLabel, "Close", true);
        Slack(sb, warn, coverMode, "SCRIPTED DEMO", coverMode != null && coverMode.enableAutoSizing);
        Slack(sb, warn, coverTitle, "DECISION", true);
        Slack(sb, warn, tabLabel, "TAKE", tabLabel != null && tabLabel.enableAutoSizing);
        if (coverMode != null && coverMode.fontSize * heldScale < Caption - 0.01f) warn.Add($"CoverMode is {coverMode.fontSize * heldScale:F1} mm at carried scale (under 5.5)");
        if (tabLabel != null && tabLabel.fontSize * heldScale < Caption - 0.01f) warn.Add($"GrabTabLabel is {tabLabel.fontSize * heldScale:F1} mm at carried scale (under 5.5)");

        // ── overlaps, containment, margins, rhythm ──
        var headerRect = QuadRect(doc, headerBand);
        var recoRect = QuadRect(doc, recoPlate);
        var stampRect = R(StampX, StampY, StampW, StampH);
        var blocks = new List<(string name, Rect rect)>
        {
            ("header band", headerRect), ("Title", BoxOf(doc, title)), ("Summary", BoxOf(doc, summary)), ("recommendation plate", recoRect),
            ("stamp target", stampRect), ("Provenance", BoxOf(doc, prov)), ("Status", BoxOf(doc, status)),
        };
        if (kApprove.ok) blocks.Add(("Btn_Approve", kApprove.rect));
        if (kReject.ok) blocks.Add(("Btn_Reject", kReject.rect));
        var withVoice = new List<(string name, Rect rect)>(blocks);
        var withoutVoice = new List<(string name, Rect rect)>(blocks);
        for (int i = 0; i < 4; i++)
        {
            if (!kChips[i].ok) continue;
            withVoice.Add((chipKeys[i].name, kChips[i].rect));
            if (i < 3) withoutVoice.Add((chipKeys[i].name, R(x3[i], ChipY, kChips[i].rect.width, kChips[i].rect.height)));
        }
        foreach (var (label, set) in new[] { ("four chips", withVoice), ("three chips", withoutVoice) })
            for (int i = 0; i < set.Count; i++)
                for (int j = i + 1; j < set.Count; j++)
                    if (Overlaps(set[i].rect, set[j].rect, out float ow, out float oh))
                    {
                        string msg = $"OVERLAP: {set[i].name} and {set[j].name} overlap by {ow * 1000f:F1} x {oh * 1000f:F1} mm";
                        if (!warn.Any(x => x.StartsWith(msg))) warn.Add(msg + $" ({label} layout)");
                    }

        var content = Rect.MinMaxRect(-W * 0.5f + 0.015f, -H * 0.5f + 0.015f, W * 0.5f - 0.015f, H * 0.5f);
        foreach (var b in withVoice.Concat(withoutVoice).Where(e => e.name != "header band"))
            if (b.rect.xMin < content.xMin - 1e-4f || b.rect.xMax > content.xMax + 1e-4f || b.rect.yMin < content.yMin - 1e-4f)
            {
                string msg = $"MARGIN: {b.name} (x {b.rect.xMin:F4}..{b.rect.xMax:F4}, y {b.rect.yMin:F4}..{b.rect.yMax:F4}) is closer than 1.5 cm to the sheet edge";
                if (!warn.Contains(msg)) warn.Add(msg);
            }
        Inside(warn, "header band", headerRect, "the sheet", sheetRect);
        Inside(warn, "FromLabel", BoxOf(doc, fromLabel), "the header band", headerRect);
        Inside(warn, "NeedsLabel", BoxOf(doc, needs), "the header band", headerRect);
        Inside(warn, "mode chip", QuadRect(doc, modeChip), "the header band", headerRect, 0.004f);
        Inside(warn, "role chip", QuadRect(doc, roleChip), "the header band", headerRect, 0.004f);
        Inside(warn, "Badge", BoxOf(doc, badge), "the mode chip", QuadRect(doc, modeChip));
        if (Overlaps(BoxOf(doc, fromLabel), QuadRect(doc, modeChip), out _, out _) || Overlaps(BoxOf(doc, needs), QuadRect(doc, modeChip), out _, out _))
            warn.Add("OVERLAP: a header line reaches under the mode chip");
        if (Overlaps(BoxOf(doc, fromLabel), BoxOf(doc, needs), out _, out float fh) && fh > 0.0015f)
            warn.Add($"OVERLAP: FromLabel and NeedsLabel boxes overlap by {fh * 1000f:F1} mm in height");
        Inside(warn, "Reco", BoxOf(doc, reco), "the recommendation plate", recoRect);
        Inside(warn, "RecoReason", BoxOf(doc, recoReason), "the recommendation plate", recoRect);
        Inside(warn, "StampHere", BoxOf(doc, stampHere), "the stamp target", stampRect, 0.003f);
        Inside(warn, "StampHint", BoxOf(doc, stampHint), "the stamp target", stampRect, 0.003f);
        if (mark != null) Inside(warn, "StampMark", BoxOf(doc, mark), "the stamp target", stampRect);
        Inside(warn, "GhostApproveLabel", BoxOf(doc, ghostAText), "its plate", QuadRect(doc, ghostA));
        Inside(warn, "GhostRejectLabel", BoxOf(doc, ghostRText), "its plate", QuadRect(doc, ghostR));
        Inside(warn, "OutcomeTitle", BoxOf(doc, outcomeTitle), "the outcome plate", QuadRect(doc, outcomePlate));
        Inside(warn, "OutcomeDetail", BoxOf(doc, outcomeDetail), "the outcome plate", QuadRect(doc, outcomePlate));
        if (Overlaps(BoxOf(doc, outcomeTitle), BoxOf(doc, outcomeDetail), out _, out float oth) && oth > 0.0015f)
            warn.Add($"OVERLAP: OutcomeTitle and OutcomeDetail boxes overlap by {oth * 1000f:F1} mm in height");
        var detailsRect = QuadRect(doc, detailsSheet);
        Inside(warn, "Body", BoxOf(doc, body), "the details sheet", detailsRect, 0.010f);
        Inside(warn, "Answer", BoxOf(doc, answer), "the details sheet", detailsRect, 0.010f);
        if (BoxOf(doc, body).yMax > -H * 0.5f - 0.004f) warn.Add("Body on the details sheet starts above the main sheet's bottom edge (it would be covered)");
        if (Overlaps(BoxOf(doc, body), BoxOf(doc, answer), out _, out _)) warn.Add("OVERLAP: Body and Answer on the details sheet overlap");
        if (DetailsY - DetailsH * 0.5f + DetailsTuck < -H * 0.5f) warn.Add("the tucked details sheet shows below the main sheet");
        foreach (var k in new[] { ("Btn_Approve", kApprove), ("Btn_Reject", kReject) }.Concat(Enumerable.Range(0, 4).Select(i => (chipWords[i], kChips[i]))))
            if (k.Item2.ok && k.Item2.label != null)
                Inside(warn, $"label of {k.Item1}", BoxOf(doc, k.Item2.label), "its planned cap face", k.Item2.face);
        if (kApprove.ok && kReject.ok)
        {
            float faceGap = kReject.face.xMin - kApprove.face.xMax;
            sb.AppendLine($"gap between the Approve and Reject faces: {faceGap * 1000f:F1} mm (housings {(kReject.rect.xMin - kApprove.rect.xMax) * 1000f:F1} mm)");
            if (faceGap < 0.008f) warn.Add($"Approve and Reject faces are {faceGap * 1000f:F1} mm apart (want 8 or more)");
        }

        sb.AppendLine("vertical rhythm on the main sheet (gap from the bottom of each block to the top of the next, cm):");
        var column = new List<(string name, Rect rect)>
        {
            ("header band", headerRect), ("Title box", BoxOf(doc, title)), ("Summary box", BoxOf(doc, summary)), ("recommendation plate", recoRect),
            ("Provenance box", BoxOf(doc, prov)), ("Status box", BoxOf(doc, status)),
        };
        if (kChips[0].ok) column.Add(("chip row", kChips[0].rect));
        if (kApprove.ok) column.Add(("primary keys", kApprove.rect));
        for (int i = 0; i + 1 < column.Count; i++)
            sb.AppendLine($"  {column[i].name} -> {column[i + 1].name}: {(column[i].rect.yMin - column[i + 1].rect.yMax) * 100f:F2}");
        if (kApprove.ok) sb.AppendLine($"  primary keys -> sheet bottom edge: {(kApprove.rect.yMin - sheetMin.y) * 100f:F2}");
        sb.AppendLine($"horizontal: text column x {Left:F4}..{Left + ColW:F4} ({ColW * 100f:F1} cm), gap {(StampX - StampW * 0.5f - (Left + ColW)) * 100f:F1} cm, stamp target x {StampX - StampW * 0.5f:F4}..{StampX + StampW * 0.5f:F4} ({StampW * 100f:F1} x {StampH * 100f:F1} cm)");
        sb.AppendLine($"chip centres x: with Voice {string.Join(", ", x4.Select(v => v.ToString("F4", CultureInfo.InvariantCulture)))} (gap {(pitch4 - ChipW) * 100f:F1} cm between housings); without Voice {x3[0]:F4}, {x3[1]:F4}, {x3[2]:F4} (gap {(x3[1] - x3[0] - ChipW) * 100f:F1} cm)");

        // ── saved state: the expanded sheet in its Ready state, four chips, details tucked away ──
        // At run time HeldPaperController and DecisionPanel set all of this when the paper is shown.
        details.localPosition = panel.detailsTuckedLocalPosition;
        EditorUtility.SetDirty(details);
        cover.gameObject.SetActive(false);
        pending.gameObject.SetActive(false);
        outcome.gameObject.SetActive(false);
        details.gameObject.SetActive(false);
        actions.gameObject.SetActive(true);
        zone.gameObject.SetActive(true);
        if (mark != null) mark.gameObject.SetActive(false);          // hidden until a stamp lands or decision.resolved
        foreach (var g in held.hideWhileHeld) g.SetActive(true);

        sb.AppendLine($"hideWhileHeld: {string.Join(", ", held.hideWhileHeld.Select(g => g.name))}");
        sb.AppendLine($"showWhileHeld: {string.Join(", ", held.showWhileHeld.Select(g => g.name))}");
        sb.AppendLine($"panel owns: {actions.name}, {zone.name}, {details.name} (slides), {pending.name}, {outcome.name}; voice chip {(panel.voiceChip != null ? "wired" : "absent")}; chips re-space to three when Voice is hidden");
        sb.AppendLine($"hidden (not deleted): StampZone/Outline_*, Details/DetailsStrip; renamed: DetailsPlate -> DetailsSheet, GhostApprove/GhostReject -> ...Plate; Reco moved under RecoBlock");

        sb.AppendLine(warn.Count == 0 ? "warnings: none" : $"warnings: {warn.Count}");
        foreach (var w in warn) sb.AppendLine("  WARNING: " + w);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }
}
