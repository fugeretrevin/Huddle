using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Documents;
using MuseDesk.Workspace;

/// <summary>
/// Decision paper redesign (UX redesign, Paper agent). Idempotent; saves the scene once.
///
/// Expanded sheet, 30 x 30 cm, 1.6 cm margins, everything left-aligned on one grid:
///   mode band (SCRIPTED DEMO / LIVE in words + "Needs your decision"), the question (Display 13),
///   a 17 cm text column (summary, recommendation, provenance) beside a separate outlined
///   "Stamp here" area, a status line, one row of small lavender chips (Why, Details, Changes,
///   Voice) and two large primary keys (Approve, Reject) along the bottom. While a response is in
///   flight the keys are replaced by flat inert plates; the confirmed outcome appears on a coloured
///   plate in the same place. Details / Why open on a second sheet below, so nothing moves.
/// Small carried paper (22 percent): a cover with a mode band, the word DECISION, a teal TAKE tab
///   and two offset sheets behind. The cover hides when the paper is taken.
///
/// All positions are authored in the paper's own local metres and converted through the measured
/// transforms, so no parent scale is assumed. Returns the measured layout.
///   unity command script run --file AgentScripts/BuildPaperV2.cs --entry BuildPaperV2.Apply
/// </summary>
public static class BuildPaperV2
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
    static readonly Color Success = new Color32(0x2E, 0x9B, 0x5F, 255);
    static readonly Color Coral = new Color32(0xD9, 0x53, 0x4A, 255);
    static readonly Color Amber = new Color32(0xE0, 0xA0, 0x30, 255);
    // Two paper shades that are not tokens: a tint for the stamp area and a shade for the sheets
    // behind the carried paper (depth cue only).
    static readonly Color PaperTint = new Color32(0xEC, 0xE4, 0xCF, 255);
    static readonly Color PaperUnder = new Color32(0xDD, 0xD5, 0xBF, 255);

    // Type scale (millimetres at label scale 0.01).
    const float Display = 13f, Heading = 10f, Body = 7.5f, LabelSize = 6.5f, Caption = 5.5f;

    // Sheet geometry, paper-local metres (x to the reader's right, y up, the reader is on -Z).
    const float W = 0.30f, H = 0.30f, Margin = 0.016f;
    const float Left = -W * 0.5f + Margin, ContentW = W - 2f * Margin;
    const float ColW = 0.170f;                              // text column: about 45 characters of Body
    const float ColX = Left + ColW * 0.5f;
    const float StampW = 0.086f, StampH = 0.067f, StampX = W * 0.5f - Margin - StampW * 0.5f, StampY = 0.0335f;
    const float BandH = 0.026f, BandY = H * 0.5f - BandH * 0.5f;
    const float TitleY = 0.094f, SummaryY = 0.052f, RecoY = 0.026f, ProvY = 0.008f, StatusY = -0.016f;
    const float ChipY = -0.042f, ChipW = 0.060f, ChipH = 0.028f;
    const float PrimY = -0.109f, PrimW = 0.128f, PrimH = 0.050f, PrimX = 0.070f;
    const float DetailsH = 0.134f, DetailsTop = -H * 0.5f - 0.006f, DetailsY = DetailsTop - DetailsH * 0.5f;

    // Depth layers in front of the sheet (metres, at full size): plates 0.3 mm, text 0.5 mm in
    // front of whatever it is printed on.
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
        t.gameObject.layer = doc.gameObject.layer;
        var mf = t.GetComponent<MeshFilter>();
        if (mf == null) mf = t.gameObject.AddComponent<MeshFilter>();
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
        Vector2 boxCm, float sizeMm, FontStyles style, Color color, TextAlignmentOptions align, bool wrap, string text)
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
        StyleText(tmp, boxCm, sizeMm, style, color, align, wrap, text);
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

    static void StyleText(TMP_Text tmp, Vector2 boxCm, float sizeMm, FontStyles style, Color color,
        TextAlignmentOptions align, bool wrap, string text)
    {
        if (text != null) tmp.text = text;
        tmp.rectTransform.sizeDelta = boxCm;
        // Shrink to 72 percent before truncating, but never below Caption (5.5 mm).
        float min = Mathf.Min(sizeMm, Mathf.Max(Caption, sizeMm * 0.72f));
        tmp.enableAutoSizing = min < sizeMm;
        tmp.fontSizeMin = min; tmp.fontSizeMax = sizeMm; tmp.fontSize = sizeMm;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.margin = Vector4.zero;
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

    static void SetColorState(SerializedObject so, string state, Color c)
    {
        var st = so.FindProperty(state);
        var col = st != null ? st.FindPropertyRelative("Color") : null;
        if (col != null) col.colorValue = c;
    }

    /// <summary>
    /// Size and place one poke key. The key wrapper (Btn_*) is scaled, non-uniformly, so that the
    /// measured Housing3D footprint is exactly <paramref name="housingSize"/> centred on
    /// <paramref name="centre"/>; ButtonVisual, Cap3D and Housing3D are not moved relative to the
    /// key, so the press travel, the rest pose and the cap/housing depth relation are unchanged.
    /// The label is re-fitted to the cap face at paper-relative scale 0.01, 0.5 mm proud of it.
    /// </summary>
    static TMP_Text LayoutKey(Transform doc, Transform actions, Transform key, Vector2 centre, Vector2 housingSize,
        float depthScale, float surfaceZ, string text, float sizeMm, Color capColor, Color textColor, string matName,
        float heldScale, StringBuilder sb)
    {
        var poke = key.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(m => m != null && m.GetType().Name == "PokeInteractable");
        if (poke == null) { sb.AppendLine($"  SKIP {key.name}: no PokeInteractable"); return null; }
        var block = poke.transform;
        var visual = Under(block, "ButtonVisual");
        var cap = visual != null ? Under(visual, "Cap3D") : null;
        var housing = Under(block, "Housing3D");
        if (visual == null || cap == null || housing == null) { sb.AppendLine($"  SKIP {key.name}: ButtonVisual / Cap3D / Housing3D not found"); return null; }

        // Unit footprint: the housing's size on the paper with the wrapper at scale one.
        key.localRotation = Quaternion.identity;
        key.localScale = Vector3.one;
        if (!OwnMeshBounds(housing, doc, out var umn, out var umx)) { sb.AppendLine($"  SKIP {key.name}: housing mesh not readable"); return null; }
        float unitW = umx.x - umn.x, unitH = umx.y - umn.y;
        if (unitW < 1e-5f || unitH < 1e-5f) { sb.AppendLine($"  SKIP {key.name}: housing has no footprint"); return null; }
        key.localScale = new Vector3(housingSize.x / unitW, housingSize.y / unitH, depthScale);
        key.localPosition = InParent(doc, actions, new Vector3(centre.x, centre.y, surfaceZ));
        // The housing is not necessarily centred on the key's pivot: centre the footprint, not the pivot.
        OwnMeshBounds(housing, doc, out var hmn, out var hmx);
        Vector3 delta = new Vector3(centre.x - (hmn.x + hmx.x) * 0.5f, centre.y - (hmn.y + hmx.y) * 0.5f, 0f);
        key.position += doc.TransformVector(delta);
        OwnMeshBounds(housing, doc, out hmn, out hmx);
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
            SetColorState(so, "_hoverColorState", Color.Lerp(capColor, Color.white, 0.25f));
            SetColorState(so, "_selectColorState", Color.Lerp(capColor, Color.black, 0.15f));
            so.ApplyModifiedProperties();
        }

        // Depth relation at full press (unchanged by this builder; measured for the report).
        OwnMeshBounds(cap, block, out var cbmn, out _);
        OwnMeshBounds(housing, block, out var hbmn, out var hbmx);
        float travel = -block.InverseTransformPoint(visual.position).z;
        float proudUnits = hbmn.z - (cbmn.z + travel);                 // rim behind the stopped cap face: positive is good
        float toMm = block.lossyScale.z * 1000f;

        // Label on the cap face.
        var labels = visual.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text label = labels.Length > 0 ? labels[0] : null;
        for (int i = 1; i < labels.Length; i++)
            if (labels[i] != null) Undo.DestroyObjectImmediate(labels[i].gameObject);    // stale duplicate labels
        OwnMeshBounds(cap, doc, out var cmn, out var cmx);
        float capW = cmx.x - cmn.x, capH = cmx.y - cmn.y;
        if (label != null)
        {
            if (label.transform.parent != visual) label.transform.SetParent(visual, false);
            PlaceText(doc, label, new Vector2((cmn.x + cmx.x) * 0.5f, (cmn.y + cmx.y) * 0.5f), cmn.z - TextProud);
            StyleText(label, new Vector2(capW * 100f * 0.88f, capH * 100f * 0.70f), sizeMm, FontStyles.Bold, textColor,
                TextAlignmentOptions.Center, false, text);
        }

        sb.AppendLine($"  {key.name,-12} housing x {hmn.x:F4}..{hmx.x:F4}, y {hmn.y:F4}..{hmx.y:F4} ({(hmx.x - hmn.x) * 100f:F1} x {(hmx.y - hmn.y) * 100f:F1} cm); " +
                      $"cap face {capW * 100f:F1} x {capH * 100f:F1} cm; wrapper scale ({key.localScale.x:F3}, {key.localScale.y:F3}, {key.localScale.z:F3}); " +
                      $"travel {travel * toMm:F1} mm; cap face stays {proudUnits * toMm:F1} mm proud of the rim at full press{(proudUnits <= 0f ? "  WARNING: not proud" : "")}; " +
                      $"housing reaches {hbmx.z * toMm:F1} mm behind the poke surface; " +
                      (label != null ? $"label '{label.text}' {TextHeights(label, heldScale)}" : "NO LABEL"));
        return label;
    }

    static string TextHeights(TMP_Text t, float heldScale)
    {
        float k = t.transform.lossyScale.y * 100f;      // font size N at scale 0.01 is N mm
        float max = (t.enableAutoSizing ? t.fontSizeMax : t.fontSize) * k, min = (t.enableAutoSizing ? t.fontSizeMin : t.fontSize) * k;
        return $"text {max:F1} mm (min {min:F1}) full, {max * heldScale:F1} mm carried";
    }

    static void ReportText(StringBuilder sb, Transform doc, TMP_Text t, float heldScale)
    {
        if (t == null) return;
        Vector3 p = doc.InverseTransformPoint(t.transform.position);
        Vector3 rel = Rel(t.transform, doc);
        Vector2 box = t.rectTransform.sizeDelta;
        float w = box.x * rel.x, h = box.y * rel.y;
        string shown = (t.text ?? "").Replace('\n', ' ');
        sb.AppendLine($"  {t.name,-14} centre ({p.x:F4}, {p.y:F4}, {p.z:F4}) box {w * 100f:F1} x {h * 100f:F1} cm; {TextHeights(t, heldScale)}; '{shown}'");
    }

    static void ReportQuad(StringBuilder sb, Transform doc, Transform q)
    {
        if (q == null) return;
        if (!OwnMeshBounds(q, doc, out var mn, out var mx)) return;
        sb.AppendLine($"  {q.name,-14} x {mn.x:F4}..{mx.x:F4}, y {mn.y:F4}..{mx.y:F4}, z {mn.z:F4} ({(mx.x - mn.x) * 100f:F1} x {(mx.y - mn.y) * 100f:F1} cm)");
    }

    public static string Apply()
    {
        var all = All();
        var sb = new StringBuilder();
        var doc = all.FirstOrDefault(t => t != null && t.name == "DecisionDocument");
        if (doc == null) return "BuildPaperV2: DecisionDocument not found\n";
        var card = doc.Find("Card");
        var actions = doc.Find("Actions");
        var panel = doc.GetComponentInChildren<DecisionPanel>(true);
        var held = doc.GetComponent<HeldPaperController>();
        if (card == null || actions == null || panel == null || held == null)
            return "BuildPaperV2: Card / Actions / DecisionPanel / HeldPaperController not found on DecisionDocument\n";
        var cardFilter = card.GetComponent<MeshFilter>();
        if (cardFilter == null || cardFilter.sharedMesh == null) return "BuildPaperV2: Card has no mesh\n";
        var quad = cardFilter.sharedMesh;
        var delivery = Object.FindAnyObjectByType<DocumentDeliveryController>(FindObjectsInactive.Include);
        float heldScale = held.heldScale;

        TMP_Text T(Transform parent, string n) { var x = parent != null ? parent.Find(n) : null; return x != null ? x.GetComponent<TMP_Text>() : null; }
        var template = T(doc, "Reco");
        if (template == null) template = doc.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(x => x != null && x.transform.parent == doc);
        if (template == null) return "BuildPaperV2: no label on the paper to clone\n";

        sb.AppendLine($"paper root: local scale {doc.localScale}, lossy {doc.lossyScale}, carried at {heldScale:F2}");

        // ── sheet ──
        card.localPosition = new Vector3(0f, 0f, card.localPosition.z);
        card.localRotation = Quaternion.identity;
        card.localScale = new Vector3(W, H, 1f);                            // a direct child of the paper root
        card.GetComponent<MeshRenderer>().sharedMaterial = Mat("Paper_Sheet", Paper);
        EditorUtility.SetDirty(card);
        OwnMeshBounds(card, doc, out var sheetMin, out var sheetMax);
        float cardZ = sheetMin.z;                                           // the printed face (a quad: one plane)
        float plateZ = cardZ - PlateProud, textZ = cardZ - TextProud, plateTextZ = plateZ - TextProud;
        var accent = card.Find("AccentBar");
        if (accent != null) accent.gameObject.SetActive(false);

        foreach (var t in new[] { doc, doc.Find("FingerProxy") })
        {
            var bc = t != null ? t.GetComponent<BoxCollider>() : null;
            if (bc == null) continue;
            bc.size = new Vector3(W + 0.04f, H + 0.03f, 0.12f);
            bc.center = new Vector3(0f, 0f, 0.01f);
            EditorUtility.SetDirty(bc);
        }

        // ── mode band ──
        var band = Quad(doc, doc, "ModeBand", quad, Mat("Paper_ModeBand", Amber), new Vector2(0f, BandY), plateZ, new Vector2(W, BandH));
        var badge = Text(doc, doc, "Badge", template, new Vector2(Left + 0.065f, BandY), plateTextZ, new Vector2(13.0f, 1.6f),
            LabelSize, FontStyles.Bold, Ink, TextAlignmentOptions.Left, false, "SCRIPTED DEMO");
        var needs = Text(doc, doc, "NeedsLabel", template, new Vector2(Left + ContentW - 0.0675f, BandY), plateTextZ, new Vector2(13.5f, 1.6f),
            Caption, FontStyles.Normal, Ink, TextAlignmentOptions.Right, false, "Needs your decision");

        // ── the question, summary, recommendation, provenance ──
        var title = Text(doc, doc, "Title", template, new Vector2(0f, TitleY), textZ, new Vector2(ContentW * 100f, 3.6f),
            Display, FontStyles.Bold, Ink, TextAlignmentOptions.TopLeft, true, null);
        var summary = Text(doc, doc, "Summary", template, new Vector2(ColX, SummaryY), textZ, new Vector2(ColW * 100f, 3.0f),
            Body, FontStyles.Normal, Ink, TextAlignmentOptions.TopLeft, true, null);
        var reco = Text(doc, doc, "Reco", template, new Vector2(ColX, RecoY), textZ, new Vector2(ColW * 100f, 1.2f),
            Body, FontStyles.Bold, new Color(0.10f, 0.42f, 0.24f), TextAlignmentOptions.Left, false, null);
        // Provenance is an honesty requirement: fixed at Caption (5.5 mm), never auto-shrunk, may wrap to two lines.
        var prov = Text(doc, doc, "Provenance", template, new Vector2(ColX, ProvY), textZ, new Vector2(ColW * 100f, 1.6f),
            Caption, FontStyles.Normal, InkSoft, TextAlignmentOptions.TopLeft, true, null);
        var status = Text(doc, doc, "Status", template, new Vector2(0f, StatusY), textZ, new Vector2(ContentW * 100f, 1.2f),
            LabelSize, FontStyles.Bold, Ink, TextAlignmentOptions.Left, false, "");

        // ── stamp area: its own outlined, labelled region beside the text, away from every key ──
        var zone = doc.Find("StampZone");
        if (zone == null) zone = Group(doc, "StampZone");
        zone.localRotation = Quaternion.identity;
        zone.localScale = Vector3.one;
        zone.localPosition = new Vector3(StampX, StampY, cardZ - 0.001f);
        EditorUtility.SetDirty(zone);
        foreach (var old in new[] { "ZoneMarker", "ZoneRing" })
        {
            var o = zone.Find(old);
            if (o != null && o.gameObject.activeSelf) { o.gameObject.SetActive(false); EditorUtility.SetDirty(o.gameObject); }
        }
        var stampPlate = Quad(doc, zone, "StampPlate", quad, Mat("PaperV2_StampPlate", PaperTint), new Vector2(StampX, StampY), plateZ, new Vector2(StampW, StampH));
        var outlineMat = Mat("PaperV2_Outline", InkSoft);
        const float line = 0.0012f;
        float outlineZ = plateZ - 0.0001f;
        Quad(doc, zone, "Outline_Top", quad, outlineMat, new Vector2(StampX, StampY + StampH * 0.5f - line * 0.5f), outlineZ, new Vector2(StampW, line));
        Quad(doc, zone, "Outline_Bottom", quad, outlineMat, new Vector2(StampX, StampY - StampH * 0.5f + line * 0.5f), outlineZ, new Vector2(StampW, line));
        Quad(doc, zone, "Outline_Left", quad, outlineMat, new Vector2(StampX - StampW * 0.5f + line * 0.5f, StampY), outlineZ, new Vector2(line, StampH));
        Quad(doc, zone, "Outline_Right", quad, outlineMat, new Vector2(StampX + StampW * 0.5f - line * 0.5f, StampY), outlineZ, new Vector2(line, StampH));
        var stampHere = Text(doc, zone, "StampHere", template, new Vector2(StampX, StampY + 0.010f), plateTextZ, new Vector2(7.6f, 1.4f),
            LabelSize, FontStyles.Bold, Ink, TextAlignmentOptions.Center, false, "Stamp here");
        var stampHint = Text(doc, zone, "StampHint", template, new Vector2(StampX, StampY - 0.012f), plateTextZ, new Vector2(7.6f, 2.0f),
            Caption, FontStyles.Normal, InkSoft, TextAlignmentOptions.Center, true, "Use a stamp instead of a key");

        // The confirmed APPROVED / REJECTED mark is printed where the stamp area was.
        var markT = doc.Find("StampMark");
        var mark = markT != null ? markT.GetComponent<TMP_Text>() : null;
        if (mark != null)
        {
            PlaceText(doc, mark, new Vector2(StampX, StampY), textZ);
            markT.localRotation = Quaternion.Euler(0f, 0f, -12f);
            StyleText(mark, new Vector2(9.5f, 2.4f), Heading, FontStyles.Bold, new Color(0.10f, 0.42f, 0.24f), TextAlignmentOptions.Center, false, null);
            markT.gameObject.SetActive(false);                  // hidden until decision.resolved (ApprovalStampController)
        }
        if (band == null || badge == null || needs == null || title == null || summary == null || reco == null || prov == null
            || status == null || stampHere == null || stampHint == null)
            return sb + "BuildPaperV2: a paper label exists without a TextMeshPro component; nothing saved\n";

        // ── keys ──
        actions.localPosition = Vector3.zero;
        actions.localRotation = Quaternion.identity;
        actions.localScale = Vector3.one;
        float surfaceZ = cardZ - 0.006f;      // poke surface 6 mm in front of the sheet, as before
        var approve = actions.Find("Btn_Approve");
        var reject = actions.Find("Btn_Reject");
        if (approve == null || reject == null) return sb + "BuildPaperV2: Btn_Approve / Btn_Reject not found under Actions\n";
        var why = actions.Find("Btn_Ask_why");
        var detailsKey = actions.Find("Btn_Details");
        var changes = actions.Find("Btn_Changes");
        var voiceKey = actions.Find("Btn_Voice");
        if (changes == null && detailsKey != null)
        {
            var go = Object.Instantiate(detailsKey.gameObject, actions);
            Undo.RegisterCreatedObjectUndo(go, "Btn_Changes");
            go.name = "Btn_Changes";
            changes = go.transform;
        }

        sb.AppendLine("keys (paper-local metres):");
        LayoutKey(doc, actions, approve, new Vector2(-PrimX, PrimY), new Vector2(PrimW, PrimH), 0.68f, surfaceZ,
            "Approve", Heading, Success, OnPlate, "PaperV2_Key_Approve", heldScale, sb);
        LayoutKey(doc, actions, reject, new Vector2(PrimX, PrimY), new Vector2(PrimW, PrimH), 0.68f, surfaceZ,
            "Reject", Heading, Coral, OnPlate, "PaperV2_Key_Reject", heldScale, sb);
        float chipPitch = (ContentW - ChipW) / 3f;
        var chips = new (Transform key, string text)[] { (why, "Why"), (detailsKey, "Details"), (changes, "Changes"), (voiceKey, "Voice") };
        TMP_Text detailsChipLabel = null;
        for (int i = 0; i < chips.Length; i++)
        {
            if (chips[i].key == null) { sb.AppendLine($"  chip '{chips[i].text}': key not found, slot left empty"); continue; }
            var label = LayoutKey(doc, actions, chips[i].key, new Vector2(Left + ChipW * 0.5f + chipPitch * i, ChipY), new Vector2(ChipW, ChipH), 0.60f, surfaceZ,
                chips[i].text, LabelSize, Lavender, Ink, "PaperV2_Key_Chip", heldScale, sb);
            if (chips[i].key == detailsKey) detailsChipLabel = label;
        }

        if (delivery != null)
        {
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
                if (current == method && target == delivery) continue;
                Rewire(key, delivery, method);
                sb.AppendLine($"  rewired {key.name} -> DocumentDeliveryController.{method} (was {(current ?? "nothing")})");
            }
        }
        else sb.AppendLine("  WARNING: DocumentDeliveryController not found; key wiring left as it was");

        // ── pending: flat inert plates where the primary keys are ──
        var pending = Group(doc, "Pending");
        var plateMat = Mat("PaperV2_Plate", Housing);
        var ghostA = Quad(doc, pending, "GhostApprove", quad, plateMat, new Vector2(-PrimX, PrimY), plateZ, new Vector2(PrimW, PrimH));
        var ghostR = Quad(doc, pending, "GhostReject", quad, plateMat, new Vector2(PrimX, PrimY), plateZ, new Vector2(PrimW, PrimH));
        var ghostAText = Text(doc, pending, "GhostApproveLabel", template, new Vector2(-PrimX, PrimY), plateTextZ, new Vector2(11.0f, 2.2f),
            Heading, FontStyles.Normal, OnPlate, TextAlignmentOptions.Center, false, "Approve");
        var ghostRText = Text(doc, pending, "GhostRejectLabel", template, new Vector2(PrimX, PrimY), plateTextZ, new Vector2(11.0f, 2.2f),
            Heading, FontStyles.Normal, OnPlate, TextAlignmentOptions.Center, false, "Reject");

        // ── outcome: the confirmed result in words, where the primary keys were ──
        var outcome = Group(doc, "Outcome");
        var outcomePlate = Quad(doc, outcome, "OutcomePlate", quad, Mat("PaperV2_Outcome", Success), new Vector2(0f, PrimY), plateZ, new Vector2(ContentW, PrimH));
        var outcomeTitle = Text(doc, outcome, "OutcomeTitle", template, new Vector2(0f, PrimY + 0.009f), plateTextZ, new Vector2(24.0f, 2.0f),
            Heading, FontStyles.Bold, OnPlate, TextAlignmentOptions.Center, false, "Approved");
        var outcomeDetail = Text(doc, outcome, "OutcomeDetail", template, new Vector2(0f, PrimY - 0.012f), plateTextZ, new Vector2(24.0f, 1.4f),
            Body, FontStyles.Normal, OnPlate, TextAlignmentOptions.Center, false, "The developer is continuing.");

        // ── details: a second sheet below; opening it moves nothing on the main sheet ──
        var details = doc.Find("Details");
        if (details == null) details = Group(doc, "Details");
        details.localPosition = Vector3.zero; details.localRotation = Quaternion.identity; details.localScale = Vector3.one;
        var detailsPlate = Quad(doc, details, "DetailsPlate", quad, Mat("Paper_Sheet", Paper), new Vector2(0f, DetailsY), cardZ, new Vector2(W, DetailsH));
        var detailsStrip = Quad(doc, details, "DetailsStrip", quad, Mat("PaperV2_Lavender", Lavender), new Vector2(0f, DetailsTop - 0.003f), plateZ, new Vector2(W, 0.006f));
        var body = Text(doc, details, "Body", template, new Vector2(0f, DetailsTop - 0.016f - 0.036f), textZ, new Vector2(ContentW * 100f, 7.2f),
            Body, FontStyles.Normal, Ink, TextAlignmentOptions.TopLeft, true, null);
        var answer = Text(doc, details, "Answer", template, new Vector2(0f, DetailsTop - 0.094f - 0.012f), textZ, new Vector2(ContentW * 100f, 2.4f),
            Body, FontStyles.Italic, InkSoft, TextAlignmentOptions.TopLeft, true, null);

        // ── cover: what the small carried paper shows (sizes are 1 / heldScale of what the eye gets) ──
        var cover = Group(doc, "Cover");
        float coverText = Mathf.Max(26f, Caption / Mathf.Max(0.05f, heldScale) + 1f);      // at least 5.5 mm when carried
        float coverProud = TextProud / Mathf.Max(0.05f, heldScale);                         // 0.5 mm when carried
        float coverPlateZ = cardZ - PlateProud / Mathf.Max(0.05f, heldScale);
        var under2 = Quad(doc, cover, "UnderSheet2", quad, Mat("PaperV2_SheetUnder", PaperUnder), new Vector2(0.020f, -0.020f), cardZ + 0.008f, new Vector2(W, H));
        var under1 = Quad(doc, cover, "UnderSheet1", quad, Mat("PaperV2_SheetUnder", PaperUnder), new Vector2(0.010f, -0.010f), cardZ + 0.004f, new Vector2(W, H));
        const float coverBandH = 0.085f;
        var coverBand = Quad(doc, cover, "CoverBand", quad, Mat("PaperV2_CoverBand", Amber), new Vector2(0f, H * 0.5f - coverBandH * 0.5f), coverPlateZ, new Vector2(W, coverBandH));
        var coverMode = Text(doc, cover, "CoverMode", template, new Vector2(0f, H * 0.5f - coverBandH * 0.5f), coverPlateZ - coverProud, new Vector2(W * 100f - 1.0f, 6.0f),
            coverText, FontStyles.Bold, Ink, TextAlignmentOptions.Center, false, "SCRIPTED DEMO");
        coverMode.enableAutoSizing = false;                 // never below 5.5 mm at carried scale
        var coverTitle = Text(doc, cover, "CoverTitle", template, new Vector2(0f, -0.020f), cardZ - coverProud, new Vector2(ContentW * 100f, 9.0f),
            46f, FontStyles.Bold, Ink, TextAlignmentOptions.Center, false, "DECISION");
        // The grab cue: a teal tab standing up from the free (top) edge, behind the sheet so only the tab shows.
        const float tabW = 0.130f, tabUp = 0.040f;
        var tab = Quad(doc, cover, "GrabTab", quad, Mat("PaperV2_Teal", Teal), new Vector2(0f, H * 0.5f + tabUp * 0.5f - 0.003f), cardZ + 0.0005f, new Vector2(tabW, tabUp + 0.006f));
        var tabLabel = Text(doc, cover, "GrabTabLabel", template, new Vector2(0f, H * 0.5f + tabUp * 0.5f), cardZ + 0.0005f - coverProud, new Vector2(12.0f, 3.6f),
            coverText, FontStyles.Bold, OnPlate, TextAlignmentOptions.Center, false, "TAKE");
        tabLabel.enableAutoSizing = false;

        // ── hover frame (grab affordance) ──
        var frame = doc.Find("HoverFrame");
        if (frame != null)
        {
            frame.localPosition = new Vector3(0f, 0f, frame.localPosition.z);
            frame.localScale = new Vector3(W + 0.045f, H + 0.045f, 1f);
            EditorUtility.SetDirty(frame);
        }

        // ── wiring ──
        panel.badge = badge; panel.title = title; panel.summary = summary; panel.recommendation = reco;
        panel.provenance = prov; panel.status = status; panel.body = body; panel.answer = answer;
        panel.needsLabel = needs;
        panel.outcomeTitle = outcomeTitle; panel.outcomeDetail = outcomeDetail;
        panel.detailsChipLabel = detailsChipLabel;
        panel.coverMode = coverMode;
        panel.modeBand = band.GetComponent<Renderer>();
        panel.coverBand = coverBand.GetComponent<Renderer>();
        panel.demoBand = Amber; panel.liveBand = Success;
        panel.demoBandText = Ink; panel.liveBandText = OnPlate;
        panel.actionsRoot = actions.gameObject;
        panel.detailsRoot = details.gameObject;
        panel.stampZoneRoot = zone.gameObject;
        panel.pendingRoot = pending.gameObject;
        panel.outcomeRoot = outcome.gameObject;
        panel.outcomePlate = outcomePlate.GetComponent<Renderer>();
        panel.voiceChip = voiceKey != null ? voiceKey.gameObject : null;
        panel.voice = Object.FindAnyObjectByType<MuseDesk.Voice.VoiceDecisionController>(FindObjectsInactive.Include);
        EditorUtility.SetDirty(panel);

        // Content shown once the paper is taken. The keys, the stamp area, the details sheet and the
        // pending / outcome plates are NOT listed: the panel owns them (it knows the response phase).
        held.hideWhileHeld = new[] { band, badge.transform, needs.transform, title.transform, summary.transform, reco.transform, prov.transform, status.transform }
            .Where(t => t != null).Select(t => t.gameObject).ToArray();
        held.showWhileHeld = new[] { cover.gameObject };
        held.panel = panel;
        held.sheetHeight = H;
        EditorUtility.SetDirty(held);

        foreach (var st in all.Where(t => t != null).Select(t => t.GetComponent<StampTool>()).Where(s => s != null))
        {
            st.paper = held;
            st.stampZone = zone;
            EditorUtility.SetDirty(st);
        }
        var stampCtl = doc.GetComponent<ApprovalStampController>();
        if (stampCtl != null && mark != null && stampCtl.mark == null) { stampCtl.mark = mark; EditorUtility.SetDirty(stampCtl); }

        // Saved state: the expanded sheet in its Ready state (what an Editor preview shows). At
        // run time HeldPaperController and DecisionPanel set all of this when the paper is shown.
        cover.gameObject.SetActive(false);
        pending.gameObject.SetActive(false);
        outcome.gameObject.SetActive(false);
        details.gameObject.SetActive(false);
        actions.gameObject.SetActive(true);
        zone.gameObject.SetActive(true);
        foreach (var g in held.hideWhileHeld) g.SetActive(true);

        // ── report ──
        sb.AppendLine($"sheet: x {sheetMin.x:F4}..{sheetMax.x:F4}, y {sheetMin.y:F4}..{sheetMax.y:F4}, printed face z {cardZ:F4} ({(sheetMax.x - sheetMin.x) * 100f:F1} x {(sheetMax.y - sheetMin.y) * 100f:F1} cm; {(sheetMax.x - sheetMin.x) * heldScale * 100f:F1} cm carried); margins {Margin * 100f:F1} cm");
        sb.AppendLine("plates (paper-local metres):");
        foreach (var q in new[] { band, stampPlate, ghostA, ghostR, outcomePlate, detailsPlate, detailsStrip, coverBand, tab, under1, under2, frame })
            ReportQuad(sb, doc, q);
        sb.AppendLine("text (paper-local metres; height at full size and at carried scale):");
        foreach (var t in new[] { badge, needs, title, summary, reco, prov, status, stampHere, stampHint, mark, ghostAText, ghostRText, outcomeTitle, outcomeDetail, body, answer })
            ReportText(sb, doc, t, heldScale);
        sb.AppendLine("cover text (seen only at carried scale):");
        foreach (var t in new[] { coverMode, coverTitle, tabLabel })
            ReportText(sb, doc, t, heldScale);
        sb.AppendLine($"hideWhileHeld: {string.Join(", ", held.hideWhileHeld.Select(g => g.name))}");
        sb.AppendLine($"showWhileHeld: {string.Join(", ", held.showWhileHeld.Select(g => g.name))}");
        sb.AppendLine($"panel owns: {actions.name}, {zone.name}, {details.name}, {pending.name}, {outcome.name}; voice chip {(panel.voiceChip != null ? "wired" : "absent")}, voice layer {(panel.voice != null ? "found" : "NOT found (chip stays hidden)")}");
        sb.AppendLine($"pokeable when ready: 2 primary (Approve, Reject) + {chips.Count(c => c.key != null)} chips (Voice only while the voice layer is usable)");

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }
}
