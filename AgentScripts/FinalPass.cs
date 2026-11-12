using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Documents;
using MuseDesk.Networking;
using MuseDesk.Phase2.Board;
using MuseDesk.Workspace;

/// <summary>
/// Final XR pass over the scene (2026-11-12). Idempotent; each entry saves the scene.
///   Paper()      decision paper: readable dark text, provenance band, summary first, keys in one row
///                inside the frame, a separate stamp target, hover frame, filing target.
///   Affordances() grab-hover feedback on the paper, stamps and task cards; drop-zone halos on the
///                three agents; a near "assign dock" on the task board so nobody reaches across.
///   Guide()      first-use prompt sign; hides unfinished features from the submission experience.
///   DemoKeys()   Start / Replay / Tips keys (run after SessionController has StartDemo/ResetDemo).
///   unity command script run --file AgentScripts/FinalPass.cs --entry FinalPass.Apply
/// </summary>
public static class FinalPass
{
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI";
    static readonly Color Ink = new Color(0.11f, 0.12f, 0.15f);
    static readonly Color Ink2 = new Color(0.29f, 0.30f, 0.34f);

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    static Transform Find(List<Transform> all, string name) => all.FirstOrDefault(t => t != null && t.name == name);
    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t.name == name);

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
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Mesh PrimitiveMesh(PrimitiveType type)
    {
        var go = GameObject.CreatePrimitive(type);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(go);
        return mesh;
    }

    /// <summary>Find or create a child with a mesh + material and no collider.</summary>
    static Transform Shape(Transform parent, string name, Mesh mesh, Material mat, Vector3 lp, Vector3 ls, Quaternion? rot = null)
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
        t.gameObject.layer = parent.gameObject.layer;
        t.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = t.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        t.localPosition = lp;
        t.localRotation = rot ?? Quaternion.identity;
        t.localScale = ls;
        EditorUtility.SetDirty(t.gameObject);
        return t;
    }

    /// <summary>Find or create a TMP label by cloning a template label (keeps the font asset and material).</summary>
    static TMP_Text Label(Transform parent, string name, TMP_Text template, Vector3 lp, float scale)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            var go = Object.Instantiate(template.gameObject, parent);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.name = name;
            t = go.transform;
        }
        t.gameObject.SetActive(true);
        t.localPosition = lp;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one * scale;
        return t.GetComponent<TMP_Text>();
    }

    static void Style(TMP_Text t, string text, Vector2 box, float min, float max, Color color, FontStyles style,
                      TextAlignmentOptions align, bool wrap)
    {
        if (t == null) return;
        Undo.RecordObject(t, "style");
        if (text != null) t.text = text;
        t.rectTransform.sizeDelta = box;
        t.enableAutoSizing = min < max;
        t.fontSizeMin = min; t.fontSizeMax = max; t.fontSize = max;
        t.color = color;
        t.fontStyle = style;
        t.alignment = align;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        EditorUtility.SetDirty(t);
    }

    static void Pose(Transform t, Vector3 lp, Vector3? ls = null)
    {
        if (t == null) return;
        Undo.RecordObject(t, "pose");
        t.localPosition = lp;
        if (ls.HasValue) t.localScale = ls.Value;
        EditorUtility.SetDirty(t);
    }

    static string Save(StringBuilder sb)
    {
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    public static string Apply()
    {
        var sb = new StringBuilder();
        sb.Append(Paper()).Append(Affordances()).Append(Guide());
        return sb.ToString();
    }

    // ───────────────────────────── decision paper ─────────────────────────────

    public static string Paper()
    {
        var all = All();
        var sb = new StringBuilder();
        var doc = Find(all, "DecisionDocument");
        if (doc == null) return "DecisionDocument not found\n";
        var card = doc.Find("Card");
        var quad = card.GetComponent<MeshFilter>().sharedMesh;
        const float z = -0.004f;      // text sits just in front of the sheet (reader is on -Z)

        // Sheet: 34 x 34 cm when expanded, so the summary, the detail area, a separate stamp target
        // and one row of keys all fit INSIDE the frame.
        Pose(card, new Vector3(0f, 0f, 0.002f), new Vector3(0.34f, 0.34f, 1f));
        foreach (var name in new[] { "DecisionDocument", "FingerProxy" })
        {
            var t = name == "DecisionDocument" ? doc : doc.Find(name);
            var bc = t != null ? t.GetComponent<BoxCollider>() : null;
            if (bc != null) { Undo.RecordObject(bc, "collider"); bc.size = new Vector3(0.38f, 0.37f, 0.12f); bc.center = new Vector3(0f, 0f, 0.01f); EditorUtility.SetDirty(bc); }
        }
        card.GetComponent<MeshRenderer>().sharedMaterial = Mat("Paper_Sheet", new Color(0.97f, 0.95f, 0.89f));
        var accent = card.Find("AccentBar");
        if (accent != null) accent.gameObject.SetActive(false);

        // Provenance band across the top: says SCRIPTED DEMO or LIVE in words; colour reinforces it.
        var band = Shape(doc, "ModeBand", quad, Mat("Paper_ModeBand", new Color(0.86f, 0.55f, 0.16f)),
            new Vector3(0f, 0.150f, -0.001f), new Vector3(0.34f, 0.040f, 1f));

        TMP_Text T(string n) { var x = doc.Find(n); return x != null ? x.GetComponent<TMP_Text>() : null; }
        var reco = T("Reco");

        Pose(doc.Find("Badge"), new Vector3(0f, 0.150f, z - 0.002f));
        Style(T("Badge"), null, new Vector2(32f, 3f), 7f, 9.5f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center, false);
        Pose(doc.Find("Title"), new Vector3(0f, 0.110f, z));
        Style(T("Title"), null, new Vector2(31f, 3.6f), 9f, 13f, Ink, FontStyles.Bold, TextAlignmentOptions.Center, true);
        Pose(doc.Find("Summary"), new Vector3(0f, 0.070f, z));
        Style(T("Summary"), null, new Vector2(31f, 3.8f), 6.5f, 8.5f, Ink, FontStyles.Normal, TextAlignmentOptions.Center, true);
        Pose(doc.Find("Reco"), new Vector3(0f, 0.040f, z));
        Style(reco, null, new Vector2(31f, 1.6f), 7f, 9.5f, new Color(0.10f, 0.42f, 0.24f), FontStyles.Bold, TextAlignmentOptions.Center, false);

        var prov = Label(doc, "Provenance", reco, new Vector3(0f, 0.026f, z), 0.01f);
        Style(prov, "Scripted sample. Nothing is running on a computer.", new Vector2(31f, 1.2f), 5f, 6.5f, Ink2, FontStyles.Italic, TextAlignmentOptions.Center, false);

        // Details (behind the Details key): left column, so the stamp target has its own area.
        var details = doc.Find("Details");
        var body = details != null ? details.Find("Body") : null;
        var answer = details != null ? details.Find("Answer") : null;
        Pose(body, new Vector3(-0.055f, -0.021f, z));
        Style(body != null ? body.GetComponent<TMP_Text>() : null, null, new Vector2(20f, 7.6f), 5.2f, 7f, Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
        Pose(answer, new Vector3(-0.055f, -0.073f, z));
        Style(answer != null ? answer.GetComponent<TMP_Text>() : null, null, new Vector2(20f, 2.4f), 4.6f, 6f, Ink2, FontStyles.Italic, TextAlignmentOptions.TopLeft, true);

        // Status line (Saving / Approved / error) above the keys.
        Pose(doc.Find("Status"), new Vector3(0f, -0.099f, z));
        Style(T("Status"), null, new Vector2(31f, 1.8f), 6f, 8.5f, Ink, FontStyles.Bold, TextAlignmentOptions.Center, false);

        // Stamp target: its own area on the right, clearly marked, no longer under a key.
        var zone = doc.Find("StampZone");
        Pose(zone, new Vector3(0.108f, -0.032f, -0.015f));
        var marker = zone != null ? zone.Find("ZoneMarker") : null;
        if (marker != null)
        {
            Pose(marker, Vector3.zero, new Vector3(0.066f, 0.0015f, 0.066f));
            marker.GetComponent<MeshRenderer>().sharedMaterial = Mat("Paper_StampTarget", new Color(0.84f, 0.80f, 0.66f));
        }
        if (zone != null)
        {
            // A darker ring behind the disc so the target reads as a printed circle, not a blob.
            Shape(zone, "ZoneRing", marker != null ? marker.GetComponent<MeshFilter>().sharedMesh : PrimitiveMesh(PrimitiveType.Cylinder),
                Mat("Paper_StampRing", new Color(0.45f, 0.41f, 0.33f)), new Vector3(0f, 0f, 0.0015f), new Vector3(0.074f, 0.001f, 0.074f),
                Quaternion.Euler(90f, 0f, 0f));
            var here = Label(zone, "StampHere", reco, new Vector3(0f, 0f, -0.004f), 0.01f);
            Style(here, "Stamp\nhere", new Vector2(6f, 4f), 5.0f, 6.5f, Ink2, FontStyles.Bold, TextAlignmentOptions.Center, true);
        }

        // Keys: one row, inside the frame. Voice needs a token and network, so it is not offered.
        var actions = doc.Find("Actions");
        if (actions != null)
        {
            var row = new (string name, float x)[] { ("Btn_Approve", -0.117f), ("Btn_Ask_why", -0.039f), ("Btn_Details", 0.039f), ("Btn_Reject", 0.117f) };
            foreach (var (name, x) in row)
                Pose(actions.Find(name), new Vector3(x, -0.142f, -0.004f), Vector3.one * 0.837f);
            var voice = actions.Find("Btn_Voice");
            if (voice != null && voice.gameObject.activeSelf) { Undo.RecordObject(voice.gameObject, "hide voice"); voice.gameObject.SetActive(false); }
        }

        // Grab affordance: a frame that lights up when a hand is in range of the paper.
        var frame = Shape(doc, "HoverFrame", quad, Mat("Hover_Frame", new Color(0.35f, 0.85f, 0.95f)),
            new Vector3(0f, 0f, 0.016f), new Vector3(0.385f, 0.385f, 1f));
        frame.gameObject.SetActive(false);
        var hover = doc.GetComponent<GrabHoverHighlight>();
        if (hover == null) hover = Undo.AddComponent<GrabHoverHighlight>(doc.gameObject);
        hover.indicator = frame.gameObject;
        hover.brighten = new Renderer[0];
        hover.interactable = doc.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "HandGrabInteractable");
        EditorUtility.SetDirty(hover);

        // Wiring.
        var panel = doc.GetComponentInChildren<DecisionPanel>(true);
        if (panel != null)
        {
            Undo.RecordObject(panel, "panel wiring");
            panel.modeBand = band.GetComponent<Renderer>();
            panel.provenance = prov;
            panel.stampZoneRoot = zone != null ? zone.gameObject : null;
            EditorUtility.SetDirty(panel);
        }
        var held = doc.GetComponent<HeldPaperController>();
        if (held != null)
        {
            Undo.RecordObject(held, "hide while held");
            // "Details" is NOT in this list: the panel owns its visibility (collapsed until the user
            // presses Details). Listing it here would pop the long explanation open on every take.
            var list = (held.hideWhileHeld ?? new GameObject[0]).Where(g => g != null && g.name != "Details").ToList();
            foreach (var t in new[] { band, prov.transform, zone })
                if (t != null && !list.Contains(t.gameObject)) list.Add(t.gameObject);
            held.hideWhileHeld = list.ToArray();
            EditorUtility.SetDirty(held);
        }
        foreach (var st in all.Where(t => t != null).Select(t => t.GetComponent<StampTool>()).Where(s => s != null))
        {
            Undo.RecordObject(st, "stamp wiring");
            st.paper = held;
            st.contactRadius = 0.05f;
            EditorUtility.SetDirty(st);
        }
        var delivery = Object.FindAnyObjectByType<DocumentDeliveryController>(FindObjectsInactive.Include);
        var outbox = Find(all, "Outbox_Stack_Anchor");
        if (delivery != null && outbox != null)
        {
            Undo.RecordObject(delivery, "file target");
            delivery.fileTarget = outbox;
            EditorUtility.SetDirty(delivery);
            sb.AppendLine("decided papers are filed to the out-tray");
        }
        sb.AppendLine("decision paper: 34 x 34 cm, dark text, provenance band, stamp target on the right, four keys in one row, hover frame");
        return Save(sb);
    }

    // ───────────────────────────── affordances ─────────────────────────────

    static readonly (string id, string label, Color color)[] Roles =
    {
        ("researcher", "Researcher", new Color(0.30f, 0.55f, 0.95f)),
        ("developer", "Developer", new Color(0.26f, 0.68f, 0.40f)),
        ("tester", "Tester", new Color(0.93f, 0.62f, 0.22f)),
    };

    public static string Affordances()
    {
        var all = All();
        var sb = new StringBuilder();
        var cyl = PrimitiveMesh(PrimitiveType.Cylinder);
        var quad = PrimitiveMesh(PrimitiveType.Quad);
        var ringMat = Mat("Hover_Ring", new Color(0.35f, 0.85f, 0.95f));
        var haloMat = Mat("DropZone_Halo", new Color(0.30f, 0.80f, 0.45f));

        // Stamps: a ring on the desk under the stamp lights up when a hand is in range.
        foreach (var name in new[] { "StampApprove", "StampReject" })
        {
            var s = Find(all, name);
            if (s == null) continue;
            var ring = Shape(s, "HoverRing", cyl, ringMat, new Vector3(0f, -0.018f, 0f), new Vector3(0.07f, 0.0008f, 0.07f));
            ring.gameObject.SetActive(false);
            var h = s.GetComponent<GrabHoverHighlight>();
            if (h == null) h = Undo.AddComponent<GrabHoverHighlight>(s.gameObject);
            h.indicator = ring.gameObject;
            var head = s.Find("Head");
            h.brighten = head != null ? new[] { head.GetComponent<Renderer>() } : new Renderer[0];
            h.interactable = s.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "HandGrabInteractable");
            EditorUtility.SetDirty(h);
        }
        sb.AppendLine("stamps: hover ring");

        // Task cards: a frame behind the card.
        var card = Find(all, "CardTemplate");
        if (card != null)
        {
            var frame = Shape(card, "HoverFrame", quad, Mat("Hover_Frame", new Color(0.35f, 0.85f, 0.95f)),
                new Vector3(0f, 0f, 0.0065f), new Vector3(0.090f, 0.060f, 1f));
            frame.gameObject.SetActive(false);
            var h = card.GetComponent<GrabHoverHighlight>();
            if (h == null) h = Undo.AddComponent<GrabHoverHighlight>(card.gameObject);
            h.indicator = frame.gameObject;
            h.brighten = new Renderer[0];
            h.interactable = card.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "HandGrabInteractable");
            h.tickVolume = 0.08f;
            EditorUtility.SetDirty(h);
            sb.AppendLine("task cards: hover frame");
        }

        // Agents' own drop zones get a halo disc on the desk (green = will assign, red = refused).
        foreach (var z in all.Where(t => t != null && t.name == "TaskDropZone"))
        {
            var dz = z.GetComponent<StationDropZone>();
            if (dz == null) continue;
            var agent = z.parent;
            Vector3 ps = z.lossyScale;
            var halo = Shape(z, "Halo", cyl, haloMat, z.InverseTransformPoint(agent.position + Vector3.up * 0.003f),
                new Vector3(0.15f / Mathf.Max(1e-5f, ps.x), 0.0006f / Mathf.Max(1e-5f, ps.y), 0.15f / Mathf.Max(1e-5f, ps.z)));
            halo.rotation = Quaternion.identity;
            halo.gameObject.SetActive(false);
            Undo.RecordObject(dz, "halo");
            dz.halo = halo.gameObject;
            EditorUtility.SetDirty(dz);
        }
        sb.AppendLine("agent drop zones: halo discs");

        // Assign dock: three labelled pads under the task board, at the board's own (near) distance,
        // so a card can be assigned without reaching 0.75 m across the desks to the agent itself.
        var board = Find(all, "TaskBoard");
        var newTask = board != null ? Under(board, "NewTaskButton") : null;
        var anyLabel = newTask != null ? newTask.GetComponentInChildren<TMP_Text>(true) : null;
        var headerLabel = board != null ? board.Find("Label_Backlog") : null;
        if (board != null && headerLabel != null)
        {
            var dock = board.Find("AssignDock");
            if (dock == null)
            {
                var go = new GameObject("AssignDock");
                Undo.RegisterCreatedObjectUndo(go, "dock");
                go.transform.SetParent(board, false);
                dock = go.transform;
            }
            dock.localPosition = new Vector3(0.09f, -0.300f, -0.012f);
            dock.localRotation = Quaternion.identity;
            dock.localScale = Vector3.one;

            var tmpl = headerLabel.GetComponent<TMP_Text>();
            var caption = Label(dock, "Caption", tmpl, new Vector3(0f, 0.044f, -0.002f), 0.01f);
            Style(caption, "Drop a card on a name to assign it", new Vector2(40f, 2.2f), 7.5f, 9.5f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center, false);

            for (int i = 0; i < Roles.Length; i++)
            {
                var (id, label, color) = Roles[i];
                string padName = "Dock_" + label;
                var pad = dock.Find(padName);
                if (pad == null)
                {
                    var go = new GameObject(padName);
                    Undo.RegisterCreatedObjectUndo(go, padName);
                    go.transform.SetParent(dock, false);
                    pad = go.transform;
                }
                pad.localPosition = new Vector3((i - 1) * 0.125f, 0f, 0f);
                pad.localRotation = Quaternion.identity;
                pad.localScale = Vector3.one;

                var bc = pad.GetComponent<BoxCollider>();
                if (bc == null) bc = Undo.AddComponent<BoxCollider>(pad.gameObject);
                bc.isTrigger = true;
                bc.size = new Vector3(0.115f, 0.075f, 0.10f);
                bc.center = new Vector3(0f, 0f, -0.02f);

                Shape(pad, "Plate", quad, Mat("Dock_" + label, color), new Vector3(0f, 0f, 0f), new Vector3(0.110f, 0.058f, 1f));
                var halo = Shape(pad, "Halo", quad, haloMat, new Vector3(0f, 0f, 0.003f), new Vector3(0.124f, 0.072f, 1f));
                halo.gameObject.SetActive(false);
                var name = Label(pad, "Name", tmpl, new Vector3(0f, 0f, -0.003f), 0.01f);
                Style(name, label, new Vector2(10.4f, 3.4f), 9f, 13f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center, false);

                var dz = pad.GetComponent<StationDropZone>();
                if (dz == null) dz = Undo.AddComponent<StationDropZone>(pad.gameObject);
                dz.agentId = id;
                dz.halo = halo.gameObject;
                dz.dropAnchor = pad;
                EditorUtility.SetDirty(dz);
            }
            sb.AppendLine("assign dock: three pads under the task board (Researcher, Developer, Tester)");
        }
        return Save(sb);
    }

    // ───────────────────────────── first-use guide, feature hiding ─────────────────────────────

    public static string Guide()
    {
        var all = All();
        var sb = new StringBuilder();
        var ws = Find(all, "WorkspaceRoot");
        var board = Find(all, "TaskBoard");
        var headerLabel = board != null ? board.Find("Label_Backlog") : null;
        if (ws == null || headerLabel == null) return "WorkspaceRoot / board label not found\n";
        var quad = PrimitiveMesh(PrimitiveType.Quad);

        // A low sign on the desk between the stamps, tilted up at the seated eye.
        var sign = ws.Find("GuideSign");
        if (sign == null)
        {
            var go = new GameObject("GuideSign");
            Undo.RegisterCreatedObjectUndo(go, "guide sign");
            go.transform.SetParent(ws, false);
            sign = go.transform;
        }
        var pos = new Vector3(0f, 0.035f, 0.345f);
        var eye = new Vector3(0f, 0.30f, 0.62f);
        sign.localPosition = pos;
        sign.localRotation = Quaternion.LookRotation((pos - eye).normalized, Vector3.up);   // -Z (readable face) to the eye
        sign.localScale = Vector3.one;

        var plate = Shape(sign, "Plate", quad, Mat("Guide_Plate", new Color(0.10f, 0.12f, 0.16f)),
            new Vector3(0f, 0f, 0.001f), new Vector3(0.235f, 0.040f, 1f));
        var text = Label(sign, "Text", headerLabel.GetComponent<TMP_Text>(), new Vector3(0f, 0f, -0.001f), 0.01f);
        Style(text, "Welcome to your agent office.", new Vector2(22.5f, 3.4f), 8f, 11.5f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center, true);

        var guide = sign.GetComponent<FirstUseGuide>();
        if (guide == null) guide = Undo.AddComponent<FirstUseGuide>(sign.gameObject);
        guide.controller = Object.FindAnyObjectByType<SessionController>(FindObjectsInactive.Include);
        var doc = Find(all, "DecisionDocument");
        guide.paper = doc != null ? doc.GetComponent<HeldPaperController>() : null;
        guide.label = text;
        guide.root = plate.gameObject;
        EditorUtility.SetDirty(guide);
        sb.AppendLine("first-use guide sign at " + pos);

        // The toy-prop tray was the largest thing in the user's view; keep the props, make it modest.
        var tray = Find(all, "PhysicsTray");
        if (tray != null) { Undo.RecordObject(tray, "tray"); tray.localScale = Vector3.one * 0.55f; tray.localPosition = new Vector3(0.30f, 0.012f, 0.47f); EditorUtility.SetDirty(tray); sb.AppendLine("toy-prop tray scaled to 55%"); }

        // Unfinished features are hidden from the submission experience (objects stay in the scene).
        var explain = Find(all, "ExplainToggle");
        if (explain != null && explain.gameObject.activeSelf)
        {
            Undo.RecordObject(explain.gameObject, "hide explain");
            explain.gameObject.SetActive(false);
            sb.AppendLine("Explain key hidden (diagram labels are unreadable and nodes cannot be selected)");
        }
        return Save(sb);
    }

    // ───────────────────────────── Start / Replay / Tips keys ─────────────────────────────

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

    static Transform Key(Transform parent, Transform template, string name, string text, Vector3 lp, Color cap, Object target, string method)
    {
        var k = parent.Find(name);
        if (k == null)
        {
            var go = Object.Instantiate(template.gameObject, parent);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.name = name;
            k = go.transform;
        }
        k.gameObject.SetActive(true);
        k.localPosition = lp;
        var eye = new Vector3(0f, 0.30f, 0.62f);
        Vector3 away = lp - eye; away.y = 0f;
        k.localRotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        k.localScale = template.localScale;
        var tmp = k.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) { tmp.text = text; EditorUtility.SetDirty(tmp); }
        var capT = Under(k, "Cap3D");
        if (capT != null)
        {
            var m = Mat("Key_" + name, cap, unlit: false);
            m.SetFloat("_Smoothness", 0.35f);
            capT.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
        Rewire(k, target, method);
        return k;
    }

    /// <summary>The filing tray: stack of filed sheets, a readout above the out-tray and a "Filed" key.</summary>
    public static string Archive()
    {
        var all = All();
        var sb = new StringBuilder();
        var ws = Find(all, "WorkspaceRoot");
        var p6 = Find(all, "Phase6");
        var outbox = Find(all, "OutboxTray");
        var anchor = Find(all, "Outbox_Stack_Anchor");
        var focus = Find(all, "FocusToggle");
        var board = Find(all, "TaskBoard");
        var headerLabel = board != null ? board.Find("Label_Backlog") : null;
        var session = Object.FindAnyObjectByType<SessionController>(FindObjectsInactive.Include);
        if (ws == null || p6 == null || outbox == null || anchor == null || focus == null || headerLabel == null || session == null)
            return "archive: a required object was not found\n";
        var quad = PrimitiveMesh(PrimitiveType.Quad);

        var holder = p6.Find("DecisionArchive");
        if (holder == null)
        {
            var go = new GameObject("DecisionArchive");
            Undo.RegisterCreatedObjectUndo(go, "archive");
            go.transform.SetParent(p6, false);
            holder = go.transform;
        }
        var archive = holder.GetComponent<DecisionArchive>();
        if (archive == null) archive = Undo.AddComponent<DecisionArchive>(holder.gameObject);

        // Template sheet: a small cream page lying flat on the stack.
        var sheet = Shape(anchor, "FiledSheetTemplate", quad, Mat("Paper_Sheet", new Color(0.97f, 0.95f, 0.89f)),
            new Vector3(0f, 0.0016f, 0f), new Vector3(0.058f / Mathf.Max(1e-5f, anchor.lossyScale.x), 0.074f / Mathf.Max(1e-5f, anchor.lossyScale.y), 1f),
            Quaternion.Euler(90f, 0f, 0f));
        sheet.gameObject.SetActive(false);

        // Readout: a dark plate with text above the out-tray, turned to the seated eye.
        var readout = ws.Find("ArchiveReadout");
        if (readout == null)
        {
            var go = new GameObject("ArchiveReadout");
            Undo.RegisterCreatedObjectUndo(go, "archive readout");
            go.transform.SetParent(ws, false);
            readout = go.transform;
        }
        var pos = ws.InverseTransformPoint(outbox.position) + new Vector3(0.02f, 0.20f, 0.02f);
        var eye = new Vector3(0f, 0.30f, 0.62f);
        readout.localPosition = pos;
        readout.localRotation = Quaternion.LookRotation((pos - eye).normalized, Vector3.up);
        readout.localScale = Vector3.one;
        Shape(readout, "Plate", quad, Mat("Guide_Plate", new Color(0.10f, 0.12f, 0.16f)), new Vector3(0f, 0f, 0.001f), new Vector3(0.215f, 0.125f, 1f));
        var text = Label(readout, "Text", headerLabel.GetComponent<TMP_Text>(), new Vector3(0f, 0f, -0.001f), 0.01f);
        Style(text, "", new Vector2(20f, 11.3f), 6f, 9.5f, Color.white, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
        readout.gameObject.SetActive(false);

        Undo.RecordObject(archive, "archive wiring");
        archive.controller = session;
        archive.stackAnchor = anchor;
        archive.sheetTemplate = sheet.gameObject;
        archive.readoutRoot = readout.gameObject;
        archive.readout = text;
        EditorUtility.SetDirty(archive);

        Key(p6, focus, "FiledKey", "Filed", new Vector3(0.405f, 0.05f, 0.30f), new Color(0.80f, 0.79f, 0.75f), archive, "ShowNext");
        sb.AppendLine("decision archive: filed-sheet stack in the out-tray, readout above it, Filed key at (0.41, 0.05, 0.30)");
        return Save(sb);
    }

    public static string DemoKeys()
    {
        var all = All();
        var sb = new StringBuilder();
        var p6 = Find(all, "Phase6");
        var focus = Find(all, "FocusToggle");
        var session = Object.FindAnyObjectByType<SessionController>(FindObjectsInactive.Include);
        var guide = Object.FindAnyObjectByType<FirstUseGuide>(FindObjectsInactive.Include);
        if (p6 == null || focus == null || session == null) return "Phase6 / FocusToggle / SessionController not found\n";
        if (session.GetType().GetMethod("StartDemo") == null || session.GetType().GetMethod("ResetDemo") == null)
            return "SessionController has no StartDemo/ResetDemo yet; keys not built\n";

        var start = Key(p6, focus, "StartKey", "Start", new Vector3(-0.31f, 0.05f, 0.42f), new Color(0.24f, 0.62f, 0.60f), session, "StartDemo");
        Key(p6, focus, "ReplayKey", "Replay", new Vector3(-0.215f, 0.05f, 0.455f), new Color(0.80f, 0.79f, 0.75f), session, "ResetDemo");
        if (guide != null)
        {
            Key(p6, focus, "TipsKey", "Hide tips", new Vector3(-0.50f, 0.05f, 0.30f), new Color(0.80f, 0.79f, 0.75f), guide, "Hide");
            // The Start key pulses while the guide is asking the user to start.
            var ring = Shape(start, "StartHint", PrimitiveMesh(PrimitiveType.Quad), Mat("Hover_Frame", new Color(0.35f, 0.85f, 0.95f)),
                new Vector3(0f, 0f, 0.20f), new Vector3(1.55f, 1.55f, 1f));
            Undo.RecordObject(guide, "start hint");
            guide.startHint = ring.gameObject;
            EditorUtility.SetDirty(guide);
        }
        // "+ New" creates a card an agent can actually run (the supported auth-scaffold task), in the
        // demo and against a live backend. It used to create a blank card that nothing runs.
        var bootstrap = Object.FindAnyObjectByType<MuseDesk.Phase2.Phase2Bootstrap>(FindObjectsInactive.Include);
        var newTask = Find(all, "NewTaskButton");
        if (bootstrap != null && newTask != null && bootstrap.GetType().GetMethod("CreateExecutableTask") != null)
        {
            Rewire(newTask, bootstrap, "CreateExecutableTask");
            sb.AppendLine("+ New now creates a runnable card (Phase2Bootstrap.CreateExecutableTask)");
        }
        sb.AppendLine("Start, Replay and Hide-tips keys built and wired");
        return Save(sb);
    }
}
