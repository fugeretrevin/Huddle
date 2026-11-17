using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Workspace;

/// <summary>
/// Second polish pass (2026-11-12, from on-head feedback):
///   Text()     task-card text gets a real layout and can no longer spill off the card; every other
///              panel's text is clipped or auto-sized to its box.
///   Buttons()  every poke button becomes a 3D key: a housing fixed to the surface and a raised cap
///              that travels into it when pressed. The label rides on the cap.
///   Physics()  small desk props become pushable rigid bodies; desks and the factory get colliders
///              so pushed props stop against them.
/// All three are idempotent and save the scene.  Apply() runs them in order.
///   unity command script run --file AgentScripts/PolishPass2.cs --entry PolishPass2.Apply
/// </summary>
public static class PolishPass2
{
    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    public static string Apply()
    {
        var sb = new StringBuilder();
        sb.Append(Text(false)).Append(Buttons(false)).Append(Physics(false));
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // ───────────────────────────── text ─────────────────────────────

    static void Fit(TMP_Text t, Vector2 box, float min, float max, bool wrap, TextAlignmentOptions align,
                    TextOverflowModes overflow = TextOverflowModes.Ellipsis)
    {
        if (t == null) return;
        Undo.RecordObject(t, "fit text");
        Undo.RecordObject(t.rectTransform, "fit text box");
        // TextMeshPro's auto-sizing cannot work with font sizes like 0.05 (labels authored at unit
        // scale): the text collapses. Re-express such a label at 1/100 scale with 100x the font size
        // and box. Same world size, units the auto-sizer handles.
        if (max < 1f)
        {
            t.transform.localScale = Vector3.one * 0.01f;
            box *= 100f; min *= 100f; max *= 100f;
        }
        t.rectTransform.sizeDelta = box;
        t.enableAutoSizing = min < max;
        t.fontSizeMin = min;
        t.fontSizeMax = max;
        t.fontSize = max;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode = overflow;
        t.alignment = align;
        EditorUtility.SetDirty(t);
    }

    static void Move(Transform t, Vector3 lp)
    {
        Undo.RecordObject(t, "move");
        t.localPosition = lp;
        EditorUtility.SetDirty(t);
    }

    public static string Text() => Text(true);
    static string Text(bool save)
    {
        var all = All();
        var sb = new StringBuilder();

        // Task card. The face is 8.0 x 5.0 cm before the template scale. Previously the title was a
        // 2.4 cm glyph in a 7.5 cm box with wrapping and no clipping, and five more 1.0-1.3 cm lines
        // shared the remaining space, so any real title ran off the card.
        var card = all.FirstOrDefault(t => t.name == "CardTemplate");
        if (card != null)
        {
            Undo.RecordObject(card, "card scale");
            card.localScale = Vector3.one * 1.5f;     // 12 x 7.5 cm: readable at the board's 0.5 m
            EditorUtility.SetDirty(card);

            TMP_Text L(string n) { var x = card.Find(n); return x != null ? x.GetComponent<TMP_Text>() : null; }
            const float z = -0.0052f;                 // just in front of the card face and art
            // header strip: owner on the left, priority left of the corner gem
            Move(card.Find("SH_Role").transform, new Vector3(-0.012f, 0.0178f, z));
            Fit(L("SH_Role"), new Vector2(0.046f, 0.009f), 0.036f, 0.052f, false, TextAlignmentOptions.Left);
            Move(card.Find("SH_Priority").transform, new Vector3(0.0155f, 0.0178f, z));
            Fit(L("SH_Priority"), new Vector2(0.016f, 0.009f), 0.040f, 0.052f, false, TextAlignmentOptions.Right);
            // title: up to two lines, shrinks before it truncates
            Move(card.Find("Title").transform, new Vector3(0f, 0.0032f, z));
            Fit(L("Title"), new Vector2(0.072f, 0.0165f), 0.046f, 0.078f, true, TextAlignmentOptions.Center);
            // state / command feedback, between the two corner icons
            Move(card.Find("SH_State").transform, new Vector3(0f, -0.0105f, z));
            Fit(L("SH_State"), new Vector2(0.047f, 0.009f), 0.032f, 0.058f, false, TextAlignmentOptions.Center);
            // LIVE / SCRIPTED DEMO, always visible
            Move(card.Find("SH_Source").transform, new Vector3(0f, -0.0190f, z));
            Fit(L("SH_Source"), new Vector2(0.047f, 0.0065f), 0.028f, 0.042f, false, TextAlignmentOptions.Center);
            // id and revision: reference detail, small, on the top edge
            Move(card.Find("SH_IdRev").transform, new Vector3(0.004f, 0.0232f, z));
            Fit(L("SH_IdRev"), new Vector2(0.060f, 0.004f), 0.022f, 0.026f, false, TextAlignmentOptions.Center);
            sb.AppendLine("task card: scale 1.5, six labels laid out and clipped to the face");
        }

        // Artifact panel: the body was a 2.6 cm glyph (about 17 characters per line).
        var panel = all.FirstOrDefault(t => t.name == "ArtifactPanel");
        var proot = panel != null ? panel.Find("Root") : null;
        if (proot != null)
        {
            TMP_Text P(string n) { var x = proot.Find(n); return x != null ? x.GetComponent<TMP_Text>() : null; }
            Fit(P("Title"), new Vector2(0.44f, 0.05f), 0.12f, 0.22f, false, TextAlignmentOptions.Center);
            Fit(P("Body"), new Vector2(0.44f, 0.44f), 0.075f, 0.12f, true, TextAlignmentOptions.TopLeft, TextOverflowModes.Truncate);
            Fit(P("Page"), new Vector2(0.12f, 0.03f), 0.14f, 0.14f, false, TextAlignmentOptions.Center);
            sb.AppendLine("artifact panel: title/body/page sized to the panel, body truncates instead of spilling");
        }

        // Decision paper: keep the authored sizes as the maximum, shrink then ellipsize.
        var doc = all.FirstOrDefault(t => t.name == "DecisionDocument");
        if (doc != null)
            foreach (var t in doc.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.name == "Label" || t.name == "ButtonLabel" || t.name == "StampMark") continue;
                bool oneLine = t.name == "Badge" || t.name == "Reco" || t.name == "Status";
                float mx = t.enableAutoSizing ? t.fontSizeMax : t.fontSize;   // stable across re-runs
                Fit(t, t.rectTransform.sizeDelta, mx * 0.72f, mx, !oneLine, t.alignment);
            }
        if (doc != null) sb.AppendLine("decision paper: every field shrinks to fit, then ellipsizes");

        // Monitors, inbox, status sign: clip to their boxes.
        foreach (var t in all.Where(x => x.name == "Monitor" || x.name == "InboxReadout" || x.name == "DiagnosticsText"))
            foreach (var tmp in t.GetComponentsInChildren<TMP_Text>(true))
            {
                bool monitor = t.name == "Monitor";
                float mx = tmp.enableAutoSizing ? tmp.fontSizeMax : tmp.fontSize;
                Fit(tmp, tmp.rectTransform.sizeDelta, mx * 0.7f, mx, !monitor,
                    t.name == "DiagnosticsText" ? TextAlignmentOptions.Top : tmp.alignment);
            }

        // The line under the board buttons that names the selected card.
        var consoleLabel = all.FirstOrDefault(x => x.name == "CardConsoleLabel");
        if (consoleLabel != null)
        {
            var ctmp = consoleLabel.GetComponent<TMP_Text>();
            Undo.RecordObject(consoleLabel, "console label");
            consoleLabel.localPosition = new Vector3(-0.115f, -0.238f, consoleLabel.localPosition.z);
            if (ctmp != null) { Undo.RecordObject(ctmp, "console label"); ctmp.enableAutoSizing = false; ctmp.fontSize = 2.2f; EditorUtility.SetDirty(ctmp); }
        }
        sb.AppendLine("monitors, inbox, status sign: clipped to their boxes");

        if (save) Save(sb);
        return sb.ToString();
    }

    // ───────────────────────────── 3D buttons ─────────────────────────────

    const string MatDir = "Assets/MuseDesk/Art/Materials/Buttons";

    static Material Mat(string name, Color c, float smooth = 0.35f)
    {
        if (!AssetDatabase.IsValidFolder(MatDir))
        {
            if (!AssetDatabase.IsValidFolder("Assets/MuseDesk/Art/Materials"))
                AssetDatabase.CreateFolder("Assets/MuseDesk/Art", "Materials");
            AssetDatabase.CreateFolder("Assets/MuseDesk/Art/Materials", "Buttons");
        }
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetColor("_Color", c);
        m.SetFloat("_Smoothness", smooth);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject Box(Transform parent, string name, Vector3 lp, Vector3 ls, Material mat, Mesh cube)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "button part");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = ls;
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.layer = parent.gameObject.layer;
        return go;
    }

    static Color CapColorFor(string buttonPath)
    {
        // Decide from the button's own name: the full path matched "ask" inside "TaskBoard".
        string p = buttonPath.ToLowerInvariant().Replace("taskboard/", "").Replace("root/", "").Replace("phase6/", "");
        if (p.Contains("approve")) return new Color(0.26f, 0.66f, 0.40f);
        if (p.Contains("reject")) return new Color(0.82f, 0.30f, 0.27f);
        if (p.Contains("ask") || p.Contains("explain")) return new Color(0.30f, 0.52f, 0.86f);
        if (p.Contains("focus")) return new Color(0.55f, 0.42f, 0.80f);
        if (p.Contains("voice")) return new Color(0.93f, 0.62f, 0.22f);
        if (p.Contains("newtask")) return new Color(0.24f, 0.62f, 0.60f);
        if (p.Contains("close")) return new Color(0.70f, 0.36f, 0.34f);
        return new Color(0.80f, 0.79f, 0.75f);   // neutral key
    }

    public static string Buttons() => Buttons(true);
    static string Buttons(bool save)
    {
        var all = All();
        var sb = new StringBuilder();
        var tmpGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var cube = tmpGo.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(tmpGo);
        var housingMat = Mat("Button_Housing", new Color(0.20f, 0.22f, 0.27f), 0.2f);

        int n = 0;
        foreach (var btn in all)
        {
            if (btn == null) continue;   // a previous build's part, destroyed earlier in this loop
            if (!btn.GetComponents<MonoBehaviour>().Any(m => m != null && m.GetType().Name == "PokeInteractable")) continue;
            var visual = btn.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == "ButtonVisual");
            if (visual == null) continue;

            // Clear a previous build of the 3D parts.
            foreach (var old in btn.GetComponentsInChildren<Transform>(true)
                         .Where(c => c != null && (c.name == "Housing3D" || c.name == "Cap3D")).ToList())
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            // One travel distance for every key (PrevPoke was authored at a third of the others).
            const float travel = 0.30f;
            if (Mathf.Abs(visual.localPosition.z + travel) > 1e-4f)
            {
                Undo.RecordObject(visual, "key travel");
                visual.localPosition = new Vector3(0f, 0f, -travel);
                EditorUtility.SetDirty(visual);
            }
            string path = (btn.parent != null ? btn.parent.name + "/" : "") + btn.name;
            Color cc = CapColorFor(path);
            var capMat = Mat("Button_Cap_" + ColorUtility.ToHtmlStringRGB(cc), cc);

            // Housing: fixed to the surface. A back plate behind the surface and four walls around
            // the cap, so the cap visibly sinks INTO something when pressed.
            float rim = travel * 0.62f;                                  // walls stop short of the cap face
            float back = 0.16f;                                          // housing depth behind the surface
            var housing = new GameObject("Housing3D");
            Undo.RegisterCreatedObjectUndo(housing, "housing");
            housing.transform.SetParent(btn, false);
            housing.layer = btn.gameObject.layer;
            float wallDepth = rim + back, wallZ = (back - rim) * 0.5f;
            const float outer = 1.20f, wall = 0.085f, inner = outer - 2f * wall;
            Box(housing.transform, "Back", new Vector3(0f, 0f, back - 0.02f), new Vector3(outer, outer, 0.04f), housingMat, cube);
            Box(housing.transform, "Top", new Vector3(0f, (outer - wall) * 0.5f, wallZ), new Vector3(outer, wall, wallDepth), housingMat, cube);
            Box(housing.transform, "Bottom", new Vector3(0f, -(outer - wall) * 0.5f, wallZ), new Vector3(outer, wall, wallDepth), housingMat, cube);
            Box(housing.transform, "Left", new Vector3(-(outer - wall) * 0.5f, 0f, wallZ), new Vector3(wall, inner, wallDepth), housingMat, cube);
            Box(housing.transform, "Right", new Vector3((outer - wall) * 0.5f, 0f, wallZ), new Vector3(wall, inner, wallDepth), housingMat, cube);

            // Cap: rides on the moving press visual. Its body runs from just behind the visual's
            // face back toward the surface, so at rest it stands proud of the housing rim and when
            // pressed it is flush inside it.
            float capDepth = travel * 0.92f;
            Box(visual, "Cap3D", new Vector3(0f, 0f, 0.004f + capDepth * 0.5f),
                new Vector3(inner - 0.03f, inner - 0.03f, capDepth), capMat, cube);

            // Labels ride on the cap face (a label left on the surface plane is hidden by the cap).
            var labels = new List<Transform>();
            labels.AddRange(btn.GetComponentsInChildren<TMP_Text>(true).Select(t => t.transform));
            if (btn.parent != null && btn.parent.name.StartsWith("Btn_"))
                labels.AddRange(btn.parent.GetComponentsInChildren<TMP_Text>(true).Select(t => t.transform));
            foreach (var lab in labels.Distinct())
            {
                if (lab.parent != visual) Undo.SetTransformParent(lab, visual, "label on cap");
                var lp = lab.localPosition;
                Undo.RecordObject(lab, "label z");
                var tmp = lab.GetComponent<TMP_Text>();
                bool single = labels.Distinct().Count() == 1;
                // A single label is centred on the key and sized to its face (the key is 1 unit wide).
                lab.localPosition = single ? new Vector3(0f, 0f, -0.035f) : new Vector3(lp.x, lp.y, -0.035f);
                if (tmp != null && single)
                {
                    Undo.RecordObject(tmp, "label fit");
                    Vector3 ps = visual.lossyScale, ls = lab.lossyScale;
                    // A box covering 86% x 46% of the key face, expressed in the label's own units.
                    var boxSize = new Vector2(0.86f * ps.x / Mathf.Max(1e-6f, ls.x), 0.46f * ps.y / Mathf.Max(1e-6f, ls.y));
                    if (boxSize.y < 0.5f)
                    {
                        // Same units problem as the card labels: move to 1/100 scale.
                        Undo.RecordObject(lab, "label scale");
                        lab.localScale = lab.localScale * 0.01f;
                        boxSize *= 100f;
                    }
                    tmp.rectTransform.sizeDelta = boxSize;
                    float full = boxSize.y * 10f * 0.60f;      // glyphs about 60% of the box height
                    tmp.enableAutoSizing = true;
                    tmp.fontSizeMax = full;
                    tmp.fontSizeMin = full * 0.45f;
                    tmp.fontSize = full;
                    tmp.textWrappingMode = TextWrappingModes.NoWrap;
                    tmp.overflowMode = TextOverflowModes.Ellipsis;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.fontStyle = FontStyles.Bold;
                    tmp.color = new Color(0.10f, 0.11f, 0.15f);
                    EditorUtility.SetDirty(tmp);
                }
                EditorUtility.SetDirty(lab);
            }
            n++;
            sb.AppendLine($"3D button: {path} (travel {travel:F2}, {labels.Distinct().Count()} label(s) on cap)");
        }
        sb.AppendLine($"buttons rebuilt: {n}");
        if (save) Save(sb);
        return sb.ToString();
    }

    // ───────────────────────────── hand physics ─────────────────────────────

    static Bounds LocalBounds(Transform root)
    {
        bool any = false; var b = new Bounds();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r.GetComponent<TMP_Text>() != null) continue;
            var wb = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var l = root.InverseTransformPoint(c);
                if (!any) { b = new Bounds(l, Vector3.zero); any = true; } else b.Encapsulate(l);
            }
        }
        return b;
    }

    public static string Physics() => Physics(true);
    static string Physics(bool save)
    {
        var all = All();
        var sb = new StringBuilder();
        var ws = all.FirstOrDefault(t => t.name == "WorkspaceRoot");
        if (ws == null) return "no WorkspaceRoot\n";

        var desk = all.FirstOrDefault(t => t.name == "DeskSurface");
        var deskCol = desk != null ? desk.GetComponent<Collider>() : null;
        if (deskCol != null)
            sb.AppendLine($"desk collider top at root-local y={ws.InverseTransformPoint(new Vector3(deskCol.bounds.center.x, deskCol.bounds.max.y, deskCol.bounds.center.z)).y:F3}");
        else sb.AppendLine("WARNING: DeskSurface has no collider; pushable props would fall");

        // Keep pushable props clear of the furniture colliders and of each other, so nothing starts
        // interpenetrating (which would shove it out of place the moment physics turns on).
        var clear = new (string name, Vector3 pos)[]
        {
            ("Decor_MiniPlant", new Vector3(-0.485f, 0f, 0.08f)), ("Decor_DeskLamp", new Vector3(0.465f, 0f, 0.07f)),
            ("Decor_Toolbox", new Vector3(0.47f, 0f, 0.31f)), ("Doc_SignaturePen", new Vector3(0.30f, 0f, 0.335f)),
            ("Decor_BookStack", new Vector3(-0.49f, 0f, -0.05f)),
            ("Doc_RolledBlueprint", new Vector3(-0.43f, 0f, 0.215f)),
            // The tray with the two toy props sat behind the desks, 0.75 m away and inside the QA desk.
            ("PhysicsTray", new Vector3(0.27f, 0.02f, 0.46f)),
        };
        foreach (var (name, pos) in clear)
        {
            var t = all.FirstOrDefault(x => x != null && x.name == name);
            if (t == null) continue;
            Undo.RecordObject(t, "clear furniture");
            t.localPosition = pos;
            EditorUtility.SetDirty(t);
        }

        // Things a hand can knock about. Flat papers and wall items are left alone.
        var pushable = new (string name, float mass)[]
        {
            ("Decor_CoffeeMug", 0.12f), ("Decor_MiniPlant", 0.15f), ("Decor_BookStack", 0.22f),
            ("Decor_MiniClock", 0.10f), ("Decor_Toolbox", 0.25f), ("Collab_PackageBox", 0.14f),
            ("Doc_ApprovalStamp", 0.08f), ("Doc_SignaturePen", 0.03f), ("Doc_RolledBlueprint", 0.05f),
            ("Decor_DeskLamp", 0.18f), ("Decor_ProgressAward", 0.12f),
        };
        foreach (var (name, mass) in pushable)
        {
            var t = all.FirstOrDefault(x => x != null && x.name == name);
            if (t == null) { sb.AppendLine("SKIP (not found): " + name); continue; }
            var b = LocalBounds(t);
            if (b.size.sqrMagnitude < 1e-8f) { sb.AppendLine("SKIP (no renderers): " + name); continue; }

            var box = t.GetComponent<BoxCollider>();
            if (box == null) box = Undo.AddComponent<BoxCollider>(t.gameObject);
            box.center = b.center;
            box.size = Vector3.Max(b.size, Vector3.one * 0.004f / Mathf.Max(1e-4f, t.lossyScale.x));
            box.isTrigger = false;

            var rb = t.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(t.gameObject);
            rb.mass = mass;
            rb.linearDamping = 0.6f;
            rb.angularDamping = 1.2f;
            rb.useGravity = true;
            rb.isKinematic = true;        // PushableProp turns physics on once the workspace has settled

            if (t.GetComponent<PushableProp>() == null) Undo.AddComponent<PushableProp>(t.gameObject);
            EditorUtility.SetDirty(t.gameObject);
            var ws_size = Vector3.Scale(box.size, t.lossyScale);
            sb.AppendLine($"pushable: {name} ({ws_size.x * 100:F1} x {ws_size.y * 100:F1} x {ws_size.z * 100:F1} cm, {mass * 1000:F0} g)");
        }

        // The two original test props get the same tidy-up behaviour.
        foreach (var name in new[] { "Prop_Block", "Prop_Mug" })
        {
            var t = all.FirstOrDefault(x => x != null && x.name == name);
            if (t == null) continue;
            var dp = t.GetComponent<DelayedPhysics>();
            if (dp != null) Undo.DestroyObjectImmediate(dp);       // PushableProp owns the settle now
            if (t.GetComponent<PushableProp>() == null) Undo.AddComponent<PushableProp>(t.gameObject);
            sb.AppendLine("pushable (existing): " + name);
        }

        // Solid furniture: pushed props stop against the desks and the factory instead of sliding
        // through them. Static colliders only (no rigidbody), sized to the visible furniture.
        foreach (var name in new[] { "Workstation", "SoftwareFactory" })
            foreach (var t in all.Where(x => x != null && x.name == name))
            {
                var b = LocalBounds(t);
                if (b.size.sqrMagnitude < 1e-8f) continue;
                var holder = t.Find("FurnitureCollider");
                if (holder == null)
                {
                    var go = new GameObject("FurnitureCollider");
                    Undo.RegisterCreatedObjectUndo(go, "furniture collider");
                    go.transform.SetParent(t, false);
                    holder = go.transform;
                }
                var box = holder.GetComponent<BoxCollider>();
                if (box == null) box = Undo.AddComponent<BoxCollider>(holder.gameObject);
                box.center = b.center;
                box.size = b.size;
                var s = Vector3.Scale(b.size, t.lossyScale);
                sb.AppendLine($"solid: {t.parent.name}/{t.name} ({s.x * 100:F0} x {s.y * 100:F0} x {s.z * 100:F0} cm)");
            }

        // Report anything that still starts inside something else.
        UnityEngine.Physics.SyncTransforms();
        var solids = all.Where(x => x != null && (x.GetComponent<PushableProp>() != null || x.name == "FurnitureCollider"))
            .Select(x => x.GetComponent<BoxCollider>()).Where(c => c != null).ToList();
        int overlaps = 0;
        for (int i = 0; i < solids.Count; i++)
            for (int j = i + 1; j < solids.Count; j++)
            {
                if (solids[i].GetComponent<PushableProp>() == null && solids[j].GetComponent<PushableProp>() == null) continue;
                if (UnityEngine.Physics.ComputePenetration(solids[i], solids[i].transform.position, solids[i].transform.rotation,
                        solids[j], solids[j].transform.position, solids[j].transform.rotation, out _, out float d) && d > 0.002f)
                {
                    overlaps++;
                    string a = solids[i].name == "FurnitureCollider" ? solids[i].transform.parent.parent.name + " desk" : solids[i].name;
                    string b2 = solids[j].name == "FurnitureCollider" ? solids[j].transform.parent.parent.name + " desk" : solids[j].name;
                    sb.AppendLine($"OVERLAP {d * 100:F1} cm: {a} / {b2}");
                }
            }
        sb.AppendLine("starting overlaps: " + overlaps);

        if (save) Save(sb);
        return sb.ToString();
    }

    static void Save(StringBuilder sb)
    {
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
    }
}
