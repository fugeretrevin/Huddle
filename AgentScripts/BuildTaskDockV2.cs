using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Networking;
using MuseDesk.Phase2.Board;

/// <summary>
/// UX redesign, Board part (2026-11-12). Idempotent; tolerates missing objects; saves the scene once.
///   1. TaskDock      a 16 x 9 cm plate on the desk, front-left of the user, tilted to face the eye:
///                    summary line, most relevant task, state line, LIVE / SCRIPTED DEMO, and two keys
///                    (Start, Board).
///   2. BoardExpander on TaskBoard: the full board starts collapsed and opens from the dock's Board key.
///   3. CardToolbar   Pause/Resume + Priority + caption become a contextual toolbar under the board,
///                    shown only while a card is selected. "+ New" moves up beside the board title.
///   4. AssignDock    pads shown only while a card is held; caption says what a drop will do / why it
///                    was refused.
///   5. Cards         title, one state line, role chip, mode words. Id/revision off the face; priority
///                    only as a word when it is not the default.
///   unity command script run --file AgentScripts/BuildTaskDockV2.cs --entry BuildTaskDockV2.Apply
/// </summary>
public static class BuildTaskDockV2
{
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI";
    static readonly Vector3 Eye = new Vector3(0f, 0.30f, 0.62f);

    // Design tokens (sRGB hex from the redesign brief).
    static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
    static readonly Color Ink = Hex("#1F2530");
    static readonly Color InkSoft = Hex("#4B5563");
    static readonly Color Plate = Hex("#1B1F27");
    static readonly Color OnPlate = Hex("#F3F4F6");
    static readonly Color Housing = Hex("#2A2F3A");
    static readonly Color Teal = Hex("#2FA39A");
    static readonly Color Lavender = Hex("#8C7BD6");
    static readonly Color InfoBlue = Hex("#4C85DB");
    static readonly Color Success = Hex("#2E9B5F");
    static readonly Color Amber = Hex("#E0A030");

    // ───────────────────────────── helpers (copied idioms from FinalPass / FeedbackPass4) ─────────────────────────────

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

    static Transform Node(Transform parent, string name, StringBuilder sb)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            t = go.transform;
            sb.AppendLine("  created " + parent.name + "/" + name);
        }
        return t;
    }

    /// <summary>Find or create a child with a mesh + material and no collider.</summary>
    static Transform Shape(Transform parent, string name, Mesh mesh, Material mat, Vector3 lp, Vector3 ls)
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
        t.localRotation = Quaternion.identity;
        t.localScale = ls;
        EditorUtility.SetDirty(t.gameObject);
        return t;
    }

    /// <summary>Find or create a TMP label by cloning a template label (keeps the font asset and material).</summary>
    static TMP_Text Label(Transform parent, string name, TMP_Text template, Vector3 lp)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            if (template == null) return null;
            var go = Object.Instantiate(template.gameObject, parent);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.name = name;
            t = go.transform;
        }
        t.gameObject.SetActive(true);
        t.gameObject.layer = parent.gameObject.layer;
        t.localPosition = lp;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one * 0.01f;        // 0.01-scale convention: font size N = N mm, box in cm
        return t.GetComponent<TMP_Text>();
    }

    static void Style(TMP_Text t, string text, Vector2 boxCm, float minMm, float maxMm, Color color, FontStyles style,
                      TextAlignmentOptions align, bool wrap)
    {
        if (t == null) return;
        Undo.RecordObject(t, "style");
        Undo.RecordObject(t.rectTransform, "style box");
        if (text != null) t.text = text;
        t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        t.rectTransform.sizeDelta = boxCm;
        t.enableAutoSizing = minMm < maxMm;
        t.fontSizeMin = minMm; t.fontSizeMax = maxMm; t.fontSize = maxMm;
        t.color = color;
        t.fontStyle = style;
        t.alignment = align;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.margin = Vector4.zero;
        var mr = t.GetComponent<MeshRenderer>();
        if (mr != null) { mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; }
        EditorUtility.SetDirty(t);
    }

    static bool OwnMeshBounds(Transform part, Transform space, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        var f = part.GetComponent<MeshFilter>();
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

    /// <summary>Front-most (smallest z, board-local) surface of the frame meshes inside a rectangle.</summary>
    static bool FrontSurface(Transform frame, Transform board, Rect area, out float z)
    {
        z = float.MaxValue; bool any = false;
        foreach (var r in frame.GetComponentsInChildren<MeshRenderer>(true))
        {
            var f = r.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null || !f.sharedMesh.isReadable) continue;
            foreach (var v in f.sharedMesh.vertices)
            {
                var l = board.InverseTransformPoint(r.transform.TransformPoint(v));
                if (area.Contains(new Vector2(l.x, l.y)) && l.z < z) { z = l.z; any = true; }
            }
        }
        return any;
    }

    static bool IsPokeKey(Transform t) =>
        t != null && t.GetComponents<MonoBehaviour>().Any(m => m != null && m.GetType().Name == "PokeInteractable");

    static void Rewire(Transform key, Object target, string method)
    {
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "InteractableUnityEventWrapper") continue;
            var so = new SerializedObject(mb);
            var calls = so.FindProperty("_whenSelect.m_PersistentCalls.m_Calls");
            if (calls == null) continue;
            if (calls.arraySize == 0) calls.InsertArrayElementAtIndex(0);
            while (calls.arraySize > 1) calls.DeleteArrayElementAtIndex(calls.arraySize - 1);
            var c = calls.GetArrayElementAtIndex(0);
            c.FindPropertyRelative("m_Target").objectReferenceValue = target;
            c.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = target.GetType().AssemblyQualifiedName;
            c.FindPropertyRelative("m_MethodName").stringValue = method;
            c.FindPropertyRelative("m_Mode").intValue = 1;
            c.FindPropertyRelative("m_CallState").intValue = 2;
            so.ApplyModifiedProperties();
        }
    }

    /// <summary>Cap colour: the cap's material and the hover / press colours the SDK's colour visual drives.</summary>
    static void CapColor(Transform key, string matName, Color c)
    {
        var capT = Under(key, "Cap3D");
        if (capT != null)
        {
            var mr = capT.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var m = Mat(matName, c, unlit: false);
                m.SetFloat("_Smoothness", 0.35f);
                mr.sharedMaterial = m;
                EditorUtility.SetDirty(mr);
            }
        }
        foreach (var icv in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (icv == null || icv.GetType().Name != "InteractableColorVisual") continue;
            var so = new SerializedObject(icv);
            SetStateColor(so, "_normalColorState", c);
            SetStateColor(so, "_hoverColorState", Color.Lerp(c, Color.white, 0.25f));
            SetStateColor(so, "_selectColorState", Color.Lerp(c, Color.black, 0.15f));
            so.ApplyModifiedProperties();
        }
    }

    static void SetStateColor(SerializedObject so, string state, Color c)
    {
        var st = so.FindProperty(state);
        var col = st != null ? st.FindPropertyRelative("Color") : null;
        if (col != null) col.colorValue = c;
    }

    /// <summary>Key label: one line, sized in millimetres for the key's scale (label units are 1/100 of the key).</summary>
    static TMP_Text KeyLabel(Transform key, string text, float keyScale, float minMm, float maxMm)
    {
        var tmp = key.GetComponentInChildren<TMP_Text>(true);
        if (tmp == null) return null;
        Undo.RecordObject(tmp, "key label");
        if (text != null) tmp.text = text;
        // A label at local scale 0.01 under a key of scale s renders font size F about F * s mm tall.
        float k = 0.001f / Mathf.Max(1e-6f, tmp.transform.lossyScale.y * 0.1f);   // font units per millimetre
        if (float.IsInfinity(k) || k <= 0f) k = 1f / Mathf.Max(1e-4f, keyScale);
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = minMm * k;
        tmp.fontSizeMax = maxMm * k;
        tmp.fontSize = maxMm * k;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.color = Ink;
        EditorUtility.SetDirty(tmp);
        return tmp;
    }

    /// <summary>Find or clone a poke key under a parent. Pose is set by the caller.</summary>
    static Transform Key(Transform parent, Transform template, string name, StringBuilder sb)
    {
        var k = parent.Find(name);
        if (k == null)
        {
            if (template == null) return null;
            var go = Object.Instantiate(template.gameObject, parent);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.name = name;
            k = go.transform;
            // Extras a template may carry (the old Start key's hint ring, a reveal from a previous build).
            foreach (var extra in new[] { "StartHint" })
            {
                var e = k.Find(extra);
                if (e != null) Object.DestroyImmediate(e.gameObject);
            }
            var oldReveal = k.GetComponent<ScaleReveal>();
            if (oldReveal != null) Object.DestroyImmediate(oldReveal);
            sb.AppendLine("  cloned key " + parent.name + "/" + name + " from " + template.name);
        }
        k.gameObject.SetActive(true);
        return k;
    }

    /// <summary>How far the key's housing extends behind its poke surface, in key-local units (0 if unknown).</summary>
    static float HousingBack(Transform key)
    {
        var housing = Under(key, "Housing3D");
        if (housing == null || !OwnMeshBounds(housing, key, out _, out var mx)) return 0f;
        return Mathf.Max(0f, mx.z);
    }

    static T Ensure<T>(GameObject go, StringBuilder sb) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null)
        {
            c = Undo.AddComponent<T>(go);
            sb.AppendLine("  added " + typeof(T).Name + " to " + go.name);
        }
        return c;
    }

    static void Reparent(Transform t, Transform parent)
    {
        if (t != null && parent != null && t.parent != parent) Undo.SetTransformParent(t, parent, "reparent " + t.name);
    }

    static void Pose(Transform t, Vector3 lp, Quaternion lr, Vector3 ls)
    {
        if (t == null) return;
        Undo.RecordObject(t, "pose");
        t.localPosition = lp;
        t.localRotation = lr;
        t.localScale = ls;
        EditorUtility.SetDirty(t);
    }

    // ───────────────────────────── entry ─────────────────────────────

    public static string Apply()
    {
        var sb = new StringBuilder();
        var all = All();
        var ws = Find(all, "WorkspaceRoot");
        var board = Find(all, "TaskBoard");
        if (ws == null) return "BuildTaskDockV2: WorkspaceRoot not found; nothing done\n";

        var quad = PrimitiveMesh(PrimitiveType.Quad);
        var presenter = board != null ? board.GetComponent<BoardPresenter>() : Object.FindAnyObjectByType<BoardPresenter>(FindObjectsInactive.Include);
        var session = Object.FindAnyObjectByType<SessionController>(FindObjectsInactive.Include);

        // A text template (any 3D TMP label that keeps the project's font asset and material).
        TMP_Text textTemplate = null;
        foreach (var n in new[] { "Label_Backlog", "Label_Working", "Caption" })
        {
            var t = board != null ? Under(board, n) : null;
            if (t != null && t.GetComponent<TMP_Text>() != null) { textTemplate = t.GetComponent<TMP_Text>(); break; }
        }
        if (textTemplate == null)
            textTemplate = all.Where(t => t != null && t.GetComponent<TextMeshPro>() != null && !IsUnderKey(t))
                              .Select(t => t.GetComponent<TMP_Text>()).FirstOrDefault();
        if (textTemplate == null) sb.AppendLine("WARNING: no TextMeshPro label found to clone; new labels are skipped");

        // A key template: any loose poke key, else one of the board's own.
        Transform keyTemplate = null;
        foreach (var n in new[] { "FocusToggle", "StartKey", "ReplayKey", "FiledKey", "TipsKey", "NewTaskButton" })
        {
            var t = Find(all, n);
            if (IsPokeKey(t) && Under(t, "ButtonVisual") != null) { keyTemplate = t; break; }
        }
        if (keyTemplate == null) sb.AppendLine("WARNING: no poke key found to clone; the dock keys are skipped if they do not exist yet");

        BoardExpander expander = null;
        if (board != null)
        {
            sb.AppendLine("board:");
            expander = Ensure<BoardExpander>(board.gameObject, sb);
            Undo.RecordObject(expander, "expander");
            expander.startExpanded = false;
            expander.duration = 0.25f;
            expander.collapsedFactor = 0.001f;
            EditorUtility.SetDirty(expander);
            sb.AppendLine($"  BoardExpander on TaskBoard: starts collapsed, opens to its authored scale {board.localScale} in 0.25 s");
            if (presenter == null) sb.AppendLine("  WARNING: TaskBoard has no BoardPresenter");
        }
        else sb.AppendLine("SKIP board: TaskBoard not found (the dock is still built; its Board key will do nothing)");

        var dock = BuildDock(ws, all, quad, textTemplate, keyTemplate, presenter, expander, session, sb);
        if (board != null)
        {
            BuildToolbar(board, quad, textTemplate, presenter, sb);
            BuildAssignDock(board, quad, textTemplate, presenter, sb);
            MoveNewTaskKey(board, sb);
            SimplifyCard(board, sb);
        }
        if (dock != null) ReportOverlaps(ws, dock, sb);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    static bool IsUnderKey(Transform t)
    {
        for (var p = t; p != null; p = p.parent) if (IsPokeKey(p)) return true;
        return false;
    }

    // ───────────────────────────── 1. task dock ─────────────────────────────

    static Transform BuildDock(Transform ws, List<Transform> all, Mesh quad, TMP_Text textTemplate, Transform keyTemplate,
                               BoardPresenter presenter, BoardExpander expander, SessionController session, StringBuilder sb)
    {
        sb.AppendLine("task dock:");
        var dock = Node(ws, "TaskDock", sb);

        // Desk top under the dock (root-local), from the desk collider when there is one.
        float deskTop = 0f;
        var desk = Find(all, "DeskSurface");
        var deskBox = desk != null ? desk.GetComponent<BoxCollider>() : null;
        if (deskBox != null)
        {
            float y = ws.InverseTransformPoint(desk.TransformPoint(deskBox.center + new Vector3(0f, deskBox.size.y * 0.5f, 0f))).y;
            if (Mathf.Abs(y) < 0.06f) deskTop = y;
            else sb.AppendLine($"  desk collider top reads y={y:F3}; ignored, using 0");
        }

        // 16 x 9 cm plate, tilted so its face (local -Z) points at the seated eye, lower edge 6 mm above the desk.
        const float w = 0.16f, h = 0.09f;
        Vector3 centre = new Vector3(0.24f, deskTop + 0.038f, 0.44f);
        Quaternion rot = Quaternion.identity;
        for (int i = 0; i < 3; i++)
        {
            rot = Quaternion.LookRotation((centre - Eye).normalized, Vector3.up);
            centre.y = deskTop + 0.006f + h * 0.5f * (rot * Vector3.up).y;
        }
        Pose(dock, centre, rot, Vector3.one);
        float tilt = Mathf.Acos(Mathf.Clamp((rot * Vector3.up).y, -1f, 1f)) * Mathf.Rad2Deg;
        sb.AppendLine($"  TaskDock at root-local {centre.ToString("F3")}, tilted back {tilt:F0} degrees, {Vector3.Distance(centre, Eye):F2} m from the eye (desk top y={deskTop:F3})");

        Shape(dock, "Frame", quad, Mat("TaskDock_Frame", Housing), new Vector3(0f, 0f, 0.0008f), new Vector3(w + 0.008f, h + 0.008f, 1f));
        Shape(dock, "Plate", quad, Mat("TaskDock_Plate", Plate), Vector3.zero, new Vector3(w, h, 1f));
        var chip = Shape(dock, "ModeChip", quad, Mat("TaskDock_ModeChip", Amber), new Vector3(-0.050f, -0.022f, -0.0003f), new Vector3(0.048f, 0.016f, 1f));

        // Text sits 0.5 mm in front of the surface it is printed on (reader is on local -Z).
        const float tz = -0.0005f;
        var summary = Label(dock, "Summary", textTemplate, new Vector3(0f, 0.0365f, tz));
        Style(summary, "Task board", new Vector2(15.0f, 1.1f), 5.5f, 6.5f, OnPlate, FontStyles.Bold, TextAlignmentOptions.Left, false);
        var title = Label(dock, "Title", textTemplate, new Vector3(0f, 0.0235f, tz));
        Style(title, "Starting", new Vector2(15.0f, 1.5f), 7.2f, 10f, OnPlate, FontStyles.Bold, TextAlignmentOptions.Left, false);
        var state = Label(dock, "State", textTemplate, new Vector3(0f, 0.0105f, tz));
        Style(state, "Connecting to the task board", new Vector2(15.0f, 1.1f), 5.5f, 7.5f, OnPlate, FontStyles.Normal, TextAlignmentOptions.Left, false);
        var mode = Label(dock, "Mode", textTemplate, new Vector3(-0.050f, -0.022f, -0.0003f + tz));
        Style(mode, "SCRIPTED DEMO", new Vector2(4.5f, 1.4f), 5.5f, 6.5f, Ink, FontStyles.Bold, TextAlignmentOptions.Center, false);

        var td = Ensure<TaskDock>(dock.gameObject, sb);

        // Two keys, 4 cm, on the lower half of the plate. Housing backs rest on the plate.
        const float keyScale = 0.04f;
        var startKey = Key(dock, keyTemplate, "DockStartKey", sb);
        var boardKey = Key(dock, keyTemplate, "DockBoardKey", sb);
        ScaleReveal startReveal = null;
        TMP_Text boardKeyLabel = null;
        if (startKey != null)
        {
            float back = HousingBack(startKey);
            float kz = back > 0f && back < 0.6f ? -(back * keyScale) - 0.0003f : -0.008f;
            Pose(startKey, new Vector3(0.000f, -0.020f, kz), Quaternion.identity, Vector3.one * keyScale);
            KeyLabel(startKey, "Start", keyScale, 5.5f, 8.5f);
            CapColor(startKey, "Key_TaskDockStart", Teal);
            Rewire(startKey, td, nameof(TaskDock.StartReadyTask));
            startReveal = Ensure<ScaleReveal>(startKey.gameObject, sb);
            Undo.RecordObject(startReveal, "start reveal");
            startReveal.startShown = false;
            startReveal.duration = 0.18f;
            EditorUtility.SetDirty(startReveal);
            sb.AppendLine($"  Start key at dock-local (0.000, -0.020, {kz:F4}), scale {keyScale}, wired to TaskDock.StartReadyTask, hidden by scale until a card is ready");
        }
        if (boardKey != null)
        {
            float back = HousingBack(boardKey);
            float kz = back > 0f && back < 0.6f ? -(back * keyScale) - 0.0003f : -0.008f;
            Pose(boardKey, new Vector3(0.052f, -0.020f, kz), Quaternion.identity, Vector3.one * keyScale);
            boardKeyLabel = KeyLabel(boardKey, "Board", keyScale, 5.5f, 8.5f);
            CapColor(boardKey, "Key_TaskDockBoard", Lavender);
            Rewire(boardKey, td, nameof(TaskDock.ToggleBoard));
            var stray = boardKey.GetComponent<ScaleReveal>();
            if (stray != null) Undo.DestroyObjectImmediate(stray);
            sb.AppendLine($"  Board key at dock-local (0.052, -0.020, {kz:F4}), scale {keyScale}, wired to TaskDock.ToggleBoard (label Board / Close)");
        }

        Undo.RecordObject(td, "task dock wiring");
        td.presenter = presenter;
        td.board = expander;
        td.session = session;
        td.summaryLabel = summary;
        td.titleLabel = title;
        td.stateLabel = state;
        td.modeLabel = mode;
        td.modeChip = chip.GetComponent<Renderer>();
        td.startKey = startReveal;
        td.boardKeyLabel = boardKeyLabel;
        td.demoChipColor = Amber;
        td.liveChipColor = Teal;
        td.startingChipColor = Housing;
        td.textColor = OnPlate;
        EditorUtility.SetDirty(td);
        sb.AppendLine($"  TaskDock wired: presenter={(presenter != null)}, board={(expander != null)}, session={(session != null)}, labels={(summary != null && title != null && state != null && mode != null)}");
        return dock;
    }

    // ───────────────────────────── 3. contextual card toolbar ─────────────────────────────

    static void BuildToolbar(Transform board, Mesh quad, TMP_Text textTemplate, BoardPresenter presenter, StringBuilder sb)
    {
        sb.AppendLine("card toolbar:");
        var console = board.GetComponent<CardActionConsole>();
        if (console == null) console = Object.FindAnyObjectByType<CardActionConsole>(FindObjectsInactive.Include);
        if (console == null) { sb.AppendLine("  SKIP: no CardActionConsole in the scene"); return; }

        // Below the frame, left half (the assignment pads take the right half).
        var toolbar = Node(board, "CardToolbar", sb);
        Pose(toolbar, new Vector3(-0.190f, -0.262f, -0.012f), Quaternion.identity, Vector3.one);

        const float keyScale = 0.04f;
        var pause = Under(board, "PauseResumeButton");
        var prio = Under(board, "PriorityButton");
        TMP_Text pauseLabel = null;
        ScaleReveal pauseReveal = null, prioReveal = null;
        if (pause != null)
        {
            Reparent(pause, toolbar);
            Pose(pause, new Vector3(-0.030f, 0.006f, 0f), Quaternion.identity, Vector3.one * keyScale);
            pauseLabel = KeyLabel(pause, "Pause", keyScale, 5.5f, 8.5f);
            Rewire(pause, console, nameof(CardActionConsole.PauseResume));
            pauseReveal = Ensure<ScaleReveal>(pause.gameObject, sb);
            pauseReveal.startShown = false;
            EditorUtility.SetDirty(pauseReveal);
        }
        else sb.AppendLine("  PauseResumeButton not found");
        if (prio != null)
        {
            Reparent(prio, toolbar);
            Pose(prio, new Vector3(0.030f, 0.006f, 0f), Quaternion.identity, Vector3.one * keyScale);
            KeyLabel(prio, "Priority", keyScale, 5.5f, 8.5f);
            Rewire(prio, console, nameof(CardActionConsole.CyclePriority));
            prioReveal = Ensure<ScaleReveal>(prio.gameObject, sb);
            prioReveal.startShown = false;
            EditorUtility.SetDirty(prioReveal);
        }
        else sb.AppendLine("  PriorityButton not found");

        // Caption on its own small plate under the keys: the selected card, then the outcome words.
        Shape(toolbar, "CaptionPlate", quad, Mat("TaskDock_Plate", Plate), new Vector3(0f, -0.034f, 0.0005f), new Vector3(0.200f, 0.016f, 1f));
        var captionT = Under(board, "CardConsoleLabel");
        TMP_Text caption = captionT != null ? captionT.GetComponent<TMP_Text>() : null;
        if (caption != null)
        {
            Reparent(captionT, toolbar);
            Pose(captionT, new Vector3(0f, -0.034f, 0f), Quaternion.identity, Vector3.one * 0.01f);
        }
        else
        {
            caption = Label(toolbar, "CardConsoleLabel", textTemplate, new Vector3(0f, -0.034f, 0f));
        }
        Style(caption, "", new Vector2(19.0f, 1.3f), 5.5f, 7.5f, OnPlate, FontStyles.Bold, TextAlignmentOptions.Center, false);

        var reveal = Ensure<ScaleReveal>(toolbar.gameObject, sb);
        reveal.startShown = false;
        EditorUtility.SetDirty(reveal);

        Undo.RecordObject(console, "toolbar wiring");
        if (console.presenter == null) console.presenter = presenter;
        console.selectedLabel = caption;
        console.toolbar = reveal;
        console.pauseResumeKey = pauseReveal;
        console.pauseResumeLabel = pauseLabel;
        console.priorityKey = prioReveal;
        console.idleHideSeconds = 8f;
        EditorUtility.SetDirty(console);
        sb.AppendLine("  CardToolbar at board-local (-0.190, -0.262, -0.012): Pause/Resume and Priority keys (4 cm) and the caption; " +
                      "hidden by scale until a card is selected; each key shown only when its action is valid");
    }

    // ───────────────────────────── 4. assignment pads ─────────────────────────────

    static void BuildAssignDock(Transform board, Mesh quad, TMP_Text textTemplate, BoardPresenter presenter, StringBuilder sb)
    {
        sb.AppendLine("assignment pads:");
        var dock = Under(board, "AssignDock");
        if (dock == null) { sb.AppendLine("  SKIP: AssignDock not found"); return; }

        var captionT = dock.Find("Caption");
        TMP_Text caption = captionT != null ? captionT.GetComponent<TMP_Text>() : Label(dock, "Caption", textTemplate, new Vector3(0f, 0.044f, -0.002f));
        if (caption != null)
        {
            var cp = caption.transform.localPosition;
            // The caption gets a plate (white text on passthrough was unreadable) and sits 0.5 mm proud of it.
            Shape(dock, "CaptionPlate", quad, Mat("TaskDock_Plate", Plate), new Vector3(cp.x, cp.y, cp.z + 0.0005f), new Vector3(0.300f, 0.018f, 1f));
            Style(caption, "Drop the card on a name", new Vector2(29.0f, 1.5f), 6.5f, 8.5f, OnPlate, FontStyles.Bold, TextAlignmentOptions.Center, false);
        }

        // Pad plates in the role colours of the brief; the Tester's amber pad takes dark text.
        var roles = new (string pad, Color color, Color text)[]
        {
            ("Dock_Researcher", InfoBlue, OnPlate), ("Dock_Developer", Success, OnPlate), ("Dock_Tester", Amber, Ink),
        };
        int pads = 0;
        foreach (var (pad, color, text) in roles)
        {
            var p = dock.Find(pad);
            if (p == null) { sb.AppendLine("  pad not found: " + pad); continue; }
            pads++;
            var plate = p.Find("Plate");
            var mr = plate != null ? plate.GetComponent<MeshRenderer>() : null;
            if (mr != null) { mr.sharedMaterial = Mat(pad, color); EditorUtility.SetDirty(mr); }
            var name = p.Find("Name");
            var tmp = name != null ? name.GetComponent<TMP_Text>() : null;
            if (tmp != null) { Undo.RecordObject(tmp, "pad name"); tmp.color = text; EditorUtility.SetDirty(tmp); }
        }

        var reveal = Ensure<ScaleReveal>(dock.gameObject, sb);
        reveal.startShown = false;
        EditorUtility.SetDirty(reveal);
        var fb = Ensure<AssignDockFeedback>(dock.gameObject, sb);
        Undo.RecordObject(fb, "assign feedback wiring");
        fb.presenter = presenter;
        fb.reveal = reveal;
        fb.caption = caption;
        EditorUtility.SetDirty(fb);
        sb.AppendLine($"  AssignDock: {pads} pads, hidden by scale until a card is held; caption on a plate says what a drop does or why it was refused");
    }

    // ───────────────────────────── "+ New" beside the title ─────────────────────────────

    static void MoveNewTaskKey(Transform board, StringBuilder sb)
    {
        var key = Under(board, "NewTaskButton");
        if (key == null) { sb.AppendLine("new-task key: NewTaskButton not found"); return; }
        Reparent(key, board);
        const float keyScale = 0.03f;
        var title = board.Find("Title");
        float y = title != null ? title.localPosition.y : 0.240f;
        float surface = title != null ? title.localPosition.z + 0.0005f : 0f;     // the title is 0.5 mm proud of the rail
        string how = "title depth";
        var frame = board.Find("KanbanFrame_Visual");
        const float x = 0.262f;
        if (frame != null && FrontSurface(frame, board, new Rect(x - 0.015f, y - 0.012f, 0.030f, 0.024f), out float measured))
        {
            surface = measured;
            how = "measured from the frame mesh";
        }
        float back = HousingBack(key);
        float z = surface - (back > 0f && back < 0.6f ? back * keyScale : 0.008f) - 0.0003f;
        Pose(key, new Vector3(x, y, z), Quaternion.identity, Vector3.one * keyScale);
        KeyLabel(key, "+ New", keyScale, 5.5f, 7.5f);
        var stray = key.GetComponent<ScaleReveal>();
        if (stray != null) Undo.DestroyObjectImmediate(stray);
        sb.AppendLine($"new-task key: on the board beside the title at board-local ({x:F3}, {y:F3}, {z:F4}), 3 cm, surface from {how}; it collapses with the board");
    }

    // ───────────────────────────── 5. simplified card ─────────────────────────────

    static void SimplifyCard(Transform board, StringBuilder sb)
    {
        sb.AppendLine("card:");
        var card = Under(board, "CardTemplate");
        if (card == null) { sb.AppendLine("  SKIP: CardTemplate not found"); return; }
        float s = Mathf.Max(0.2f, card.localScale.x);     // label font size F renders F * s mm tall on the board
        float Mm(float mm) => mm / s;

        // x / y in card-local metres; each label keeps its measured depth (FeedbackPass3: on the surface).
        void Place(string name, float x, float y, Vector2 boxCm, float minMm, float maxMm, Color color, FontStyles style, TextAlignmentOptions align, bool wrap)
        {
            var t = card.Find(name);
            var tmp = t != null ? t.GetComponent<TMP_Text>() : null;
            if (tmp == null) { sb.AppendLine("  card label not found: " + name); return; }
            Undo.RecordObject(t, "card label");
            t.localPosition = new Vector3(x, y, t.localPosition.z);
            t.localScale = Vector3.one * 0.01f;
            float lo = Mathf.Max(Mm(5.5f), Mm(minMm)), hi = Mathf.Max(lo, Mm(maxMm));
            Style(tmp, null, boxCm, lo, hi, color, style, align, wrap);
        }

        // Role chip and (only when not default) the priority word, on the header strip.
        Place("SH_Role", -0.0120f, 0.0212f, new Vector2(3.6f, 0.62f), 5.5f, 6.5f, OnPlate, FontStyles.Bold, TextAlignmentOptions.Left, false);
        Place("SH_Priority", 0.0165f, 0.0212f, new Vector2(1.7f, 0.62f), 5.5f, 6.5f, OnPlate, FontStyles.Bold, TextAlignmentOptions.Right, false);
        // Title (Heading), up to two lines.
        Place("Title", 0f, 0.0065f, new Vector2(6.6f, 1.9f), 7.2f, 10f, Ink, FontStyles.Bold, TextAlignmentOptions.Center, true);
        // One state line in plain words.
        Place("SH_State", 0f, -0.0095f, new Vector2(6.6f, 0.85f), 5.5f, 7.5f, Ink, FontStyles.Normal, TextAlignmentOptions.Center, false);
        // LIVE / SCRIPTED DEMO: always on the card.
        Place("SH_Source", 0f, -0.0185f, new Vector2(4.8f, 0.75f), 5.5f, 6.5f, InkSoft, FontStyles.Bold, TextAlignmentOptions.Center, false);

        var idRev = card.Find("SH_IdRev");
        if (idRev != null && idRev.gameObject.activeSelf)
        {
            Undo.RecordObject(idRev.gameObject, "hide id/rev");
            idRev.gameObject.SetActive(false);        // a plain label, not an interactable
        }

        // Role chip colour: tint the card art's own header strip (the old flat quad is disabled).
        var view = card.GetComponent<TaskCardView>();
        var tint = card.GetComponent<CardRoleTint>();
        var strip = card.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name == "Card_RoleHeaderStrip" && r.enabled);
        bool stripShown = strip != null;
        if (stripShown) for (var t = strip.transform; t != null && t != card; t = t.parent) if (!t.gameObject.activeSelf) stripShown = false;
        if (tint != null)
        {
            Undo.RecordObject(tint, "role tint");
            tint.researcher = InfoBlue;
            tint.developer = Success;
            tint.tester = Amber;
            tint.unassigned = InkSoft;
            tint.emission = 0f;
            if (stripShown) tint.headerStrip = strip;
            EditorUtility.SetDirty(tint);
        }
        if (view != null)
        {
            Undo.RecordObject(view, "card view");
            view.tintHeaderText = tint != null && stripShown;
            EditorUtility.SetDirty(view);
        }
        sb.AppendLine($"  card face (scale {s:F2}): role chip, title up to {10f:F0} mm, state line, mode words; id/revision hidden; priority is a word only when not Normal");
        sb.AppendLine(stripShown && tint != null
            ? "  role strip: Card_RoleHeaderStrip is tinted by role (blue / green / amber, grey when unassigned) through a property block"
            : "  role strip: Card_RoleHeaderStrip not found or hidden, so the strip keeps its own colour and the role is carried by the word only");
    }

    // ───────────────────────────── report: what else is where the dock is ─────────────────────────────

    static void ReportOverlaps(Transform ws, Transform dock, StringBuilder sb)
    {
        var plate = dock.Find("Plate");
        var pr = plate != null ? plate.GetComponent<Renderer>() : null;
        if (pr == null) return;
        var b = pr.bounds;
        b.Expand(new Vector3(0.02f, 0.05f, 0.02f));
        var hits = new List<string>();
        foreach (var r in ws.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || !r.enabled || r.transform.IsChildOf(dock)) continue;
            var rb = r.bounds;
            if (rb.size.x > 0.5f || rb.size.z > 0.5f) continue;            // the desk itself, room-sized things
            if (!rb.Intersects(b)) continue;
            // Name the top-level thing under the workspace (or its group), not the mesh part.
            var top = r.transform;
            while (top.parent != null && top.parent != ws && top.parent.name != "Phase6" && top.parent.name != "OfficeProps_Collection") top = top.parent;
            if (!hits.Contains(top.name)) hits.Add(top.name);
        }
        sb.AppendLine(hits.Count == 0
            ? "dock clearance: nothing else occupies the dock's place"
            : "dock clearance: these objects overlap the dock's place and need to move (not this builder's objects): " + string.Join(", ", hits));
    }
}
