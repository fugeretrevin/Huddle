using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Phase2.Board;
using MuseDesk.Workspace;

/// <summary>
/// Final refinement pass, Board part (2026-11-13). Idempotent; tolerates missing objects; saves the
/// scene once. Runs after BuildOfficeV2, BuildTaskDockV2, BuildPaperV2, IntegrateV2.
///   1. Inspect pose   the open board is smaller (0.72 to 0.80 of its authored scale), nearer and
///                     turned to the seated eye; the pose is searched from the constraints (keys and
///                     pads within 0.55 m, agents not covered, clear of what stands under it) and
///                     stored on BoardExpander, which now travels between the task dock and that pose.
///                     The FaceUser on the board is switched off (a board with poke keys is oriented
///                     once, not every frame).
///   2. Bottom rail    New, Pause/Resume and Priority (one key size, 4 cm in the world) and the three
///                     assignment pads sit in one row under the frame, captions in a strip above them.
///   3. Cards          a darker edge plate behind the face (state border through a property block),
///                     text sized for the world after the board scale: title, state line, owner chip,
///                     mode words, priority only when not Normal.
///   4. Columns        headings at least 8 mm, wired to the presenter for counts ("Backlog 3") and for
///                     dimming an empty lane's header block.
///   5. Drops          pads grow and light when they would accept the held card; a drop needs the card
///                     within 7 cm of a pad; cards ease to their slot (code in TaskCardView).
///   6. Dock polish    the mode chip is wide enough for SCRIPTED DEMO in true bold with 10 percent slack.
///   unity command script run --file AgentScripts/BuildBoardV3.cs --entry BuildBoardV3.Apply
/// </summary>
public static class BuildBoardV3
{
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI";
    static readonly Vector3 Eye = new Vector3(0f, 0.30f, 0.62f);
    const float Reach = 0.55f;                 // frequent keys and pads: metres from the eye
    const float KeyWorld = 0.040f;             // every board key is 4 cm in the world
    const float CaptionWorldH = 0.016f;        // caption strip height in the world
    const float MinBottom = 0.05f;             // the open board's lowest edge above the desk plane

    static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
    static readonly Color Ink = Hex("#1F2530");
    static readonly Color InkSoft = Hex("#4B5563");
    static readonly Color OnPlate = Hex("#F3F4F6");
    static readonly Color Housing = Hex("#2A2F3A");
    static readonly Color Teal = Hex("#2FA39A");
    static readonly Color Coral = Hex("#D9534A");
    static readonly Color Amber = Hex("#E0A030");

    // ───────────────────────────── helpers (idioms of FinalPass / FeedbackPass4 / BuildTaskDockV2) ─────────────────────────────

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    static Transform Find(List<Transform> all, string name) => all.FirstOrDefault(t => t != null && t.name == name);
    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t.name == name);

    static Material Mat(string name, Color c)
    {
        if (!AssetDatabase.IsValidFolder("Assets/MuseDesk/Art/Materials")) AssetDatabase.CreateFolder("Assets/MuseDesk/Art", "Materials");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/MuseDesk/Art/Materials", "UI");
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
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
        // Always the plain quad at the scale given here: after a re-run the Kit builder rounds it again.
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

    static void Pose(Transform t, Vector3 lp, Vector3 ls)
    {
        if (t == null) return;
        Undo.RecordObject(t, "pose");
        t.localPosition = lp;
        t.localRotation = Quaternion.identity;
        t.localScale = ls;
        EditorUtility.SetDirty(t);
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

    static bool ShownUnder(Transform t, Transform root)
    {
        for (var p = t; p != null && p != root; p = p.parent) if (!p.gameObject.activeSelf) return false;
        return true;
    }

    /// <summary>How far the key's housing extends behind its poke surface, in key-local units (0 if unknown).</summary>
    static float HousingBack(Transform key)
    {
        var housing = Under(key, "Housing3D");
        if (housing == null || !OwnMeshBounds(housing, key, out _, out var mx)) return 0f;
        return Mathf.Max(0f, mx.z);
    }

    /// <summary>
    /// Style a label in WORLD units, whatever scale its parents have: the box is given in metres and
    /// the font sizes in millimetres of glyph height as seen in the world. (At label scale 0.01 under
    /// unscaled parents this is the 0.01 convention exactly; under a scaled board or card it enlarges
    /// the font and box numbers so the world result still meets the size.) Returns font units per mm.
    /// </summary>
    static float StyleWorld(TMP_Text t, string text, float boxWMetres, float boxHMetres, float minMm, float maxMm,
                            Color color, FontStyles style, TextAlignmentOptions align, bool wrap)
    {
        if (t == null) return 0f;
        float lossy = Mathf.Abs(t.transform.lossyScale.y);
        if (lossy < 1e-7f) return 0f;
        float perMm = 1f / (lossy * 100f);
        Undo.RecordObject(t, "style");
        Undo.RecordObject(t.rectTransform, "style box");
        if (text != null) t.text = text;
        t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        t.rectTransform.sizeDelta = new Vector2(boxWMetres / lossy, boxHMetres / lossy);
        t.enableAutoSizing = minMm < maxMm;
        t.fontSizeMin = minMm * perMm; t.fontSizeMax = maxMm * perMm; t.fontSize = maxMm * perMm;
        t.color = color;
        t.fontStyle = style;
        t.alignment = align;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.margin = Vector4.zero;
        var mr = t.GetComponent<MeshRenderer>();
        if (mr != null) { mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; }
        EditorUtility.SetDirty(t);
        return perMm;
    }

    /// <summary>Key label: one line, sized in world millimetres from the label's own lossy scale.</summary>
    static TMP_Text KeyLabel(Transform key, string text, float minMm, float maxMm)
    {
        var tmp = key.GetComponentInChildren<TMP_Text>(true);
        if (tmp == null) return null;
        float lossy = Mathf.Abs(tmp.transform.lossyScale.y);
        if (lossy < 1e-7f) return tmp;
        float perMm = 1f / (lossy * 100f);
        Undo.RecordObject(tmp, "key label");
        if (text != null) tmp.text = text;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = minMm * perMm;
        tmp.fontSizeMax = maxMm * perMm;
        tmp.fontSize = maxMm * perMm;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.color = Ink;
        EditorUtility.SetDirty(tmp);
        return tmp;
    }

    /// <summary>Width a string needs at a font size and style, in the label's own rect units.</summary>
    static float PreferredWidth(TMP_Text t, string text, float fontSize, FontStyles style)
    {
        bool auto = t.enableAutoSizing; float fs = t.fontSize; var st = t.fontStyle; var wrap = t.textWrappingMode;
        t.enableAutoSizing = false; t.fontSize = fontSize; t.fontStyle = style; t.textWrappingMode = TextWrappingModes.NoWrap;
        float w = t.GetPreferredValues(text).x;
        t.enableAutoSizing = auto; t.fontSize = fs; t.fontStyle = st; t.textWrappingMode = wrap;
        t.ForceMeshUpdate(true, true);
        return w;
    }

    static string F3(Vector3 v) => "(" + v.x.ToString("F3") + ", " + v.y.ToString("F3") + ", " + v.z.ToString("F3") + ")";
    static string Mm(float metres) => (metres * 1000f).ToString("F1");

    // ───────────────────────────── bottom rail layout (board-local metres) ─────────────────────────────

    struct Rail
    {
        public float kL;                 // key local scale (4 cm in the world)
        public float capH;               // caption strip height
        public float yCap, yKey, yBottom;
        public float xNew, xPause, xPrio;
        public float toolCapCx, toolCapW;
        public float padsCx, padPitch, padW, padH, padCapW;
    }

    /// <summary>One row under the frame: caption strip, then keys (left) and pads (right). Local
    /// sizes are divided by the board scale so the world sizes do not shrink with the board.</summary>
    static Rail LayoutRail(float s, Vector3 fMn, Vector3 fMx)
    {
        Rail r = new Rail();
        r.kL = KeyWorld / s;
        r.capH = CaptionWorldH / s;
        float gap = 0.010f / s;                                   // faces are 98 percent of a key: over 8 mm between faces
        r.yCap = fMn.y - 0.004f - r.capH * 0.5f;
        r.yKey = r.yCap - r.capH * 0.5f - 0.004f - r.kL * 0.5f;
        r.yBottom = r.yKey - r.kL * 0.5f;
        float left = fMn.x + 0.004f, right = fMx.x - 0.004f;
        r.xNew = left + r.kL * 0.5f;
        r.xPause = r.xNew + r.kL + gap + 0.006f;
        r.xPrio = r.xPause + r.kL + gap;
        float toolRight = r.xPrio + r.kL * 0.5f;
        r.toolCapCx = (left + toolRight) * 0.5f;
        r.toolCapW = toolRight - left;
        float padsLeft = toolRight + 0.016f;
        r.padPitch = (right - padsLeft) / 3f;
        r.padW = r.padPitch - 0.012f;
        r.padH = r.kL;
        r.padsCx = (padsLeft + right) * 0.5f;
        r.padCapW = right - padsLeft - 0.012f;
        return r;
    }

    // ───────────────────────────── inspect pose search (root-local) ─────────────────────────────

    struct PoseEval
    {
        public Vector3 origin; public Quaternion rot;
        public float s, thetaDeg, d, bottom, top, maxReach, occluded, cost;
        public string under;
    }

    /// <summary>Axis-aligned box of a group's visible renderers in root-local space.</summary>
    static bool RootBox(Transform ws, Transform group, out Bounds box)
    {
        box = new Bounds(); bool any = false;
        if (group == null) return false;
        foreach (var r in group.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || !r.enabled || r.GetComponent<TMP_Text>() != null) continue;
            var wb = r.bounds;
            if (wb.size.x > 0.6f || wb.size.y > 0.6f || wb.size.z > 0.6f) continue;      // floors, room-sized helpers
            if (wb.size.sqrMagnitude < 1e-8f) continue;                                   // hidden by scale
            for (int i = 0; i < 8; i++)
            {
                var c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var l = ws.InverseTransformPoint(c);
                if (!any) { box = new Bounds(l, Vector3.zero); any = true; }
                else box.Encapsulate(l);
            }
        }
        return any;
    }

    static PoseEval Evaluate(float s, float thetaDeg, float d, Vector3 fMn, Vector3 fMx, Rail rail,
                             List<Bounds> agents, List<KeyValuePair<string, Bounds>> obstacles)
    {
        var e = new PoseEval { s = s, thetaDeg = thetaDeg, d = d };
        float th = thetaDeg * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Sin(th), 0f, -Mathf.Cos(th));          // from the eye, toward the user's left (+X)
        e.rot = Quaternion.LookRotation(dir, Vector3.up);                      // board +Z away from the eye: -Z (text, keys) faces it
        Vector3 right = e.rot * Vector3.right, up = Vector3.up, fwd = e.rot * Vector3.forward;

        float xL = fMn.x, xR = fMx.x, yB = rail.yBottom, yT = fMx.y;
        float cx = (fMn.x + fMx.x) * 0.5f, cy = (fMn.y + fMx.y) * 0.5f;        // the frame's centre

        // The board stands just above whatever is under it; a few passes settle height and position.
        e.bottom = 0.08f; e.under = "nothing";
        for (int pass = 0; pass < 3; pass++)
        {
            float originY = e.bottom - yB * s;
            float centreY = originY + cy * s;
            float dy = centreY - Eye.y;
            float h = Mathf.Sqrt(Mathf.Max(1e-4f, d * d - dy * dy));
            Vector3 centre = new Vector3(Eye.x + dir.x * h, centreY, Eye.z + dir.z * h);
            e.origin = centre - right * (cx * s) - up * (cy * s);
            if (pass == 2) break;                                              // placed for the settled height

            float tallest = 0f; string what = "nothing";
            for (int i = 0; i <= 12; i++)
            {
                Vector3 p = e.origin + right * (Mathf.Lerp(xL, xR, i / 12f) * s);
                foreach (var kv in obstacles)
                {
                    Bounds b = kv.Value;
                    if (p.x < b.min.x - 0.02f || p.x > b.max.x + 0.02f || p.z < b.min.z - 0.02f || p.z > b.max.z + 0.02f) continue;
                    if (b.max.y > tallest) { tallest = b.max.y; what = kv.Key; }
                }
            }
            e.bottom = Mathf.Max(MinBottom, tallest + 0.012f);
            e.under = what;
        }
        e.top = e.origin.y + yT * s;

        // Reach: the three keys and the three pads (key faces are about a centimetre nearer than this).
        e.maxReach = 0f;
        for (int i = 0; i < 6; i++)
        {
            float lx = i == 0 ? rail.xNew : i == 1 ? rail.xPause : i == 2 ? rail.xPrio : rail.padsCx + (i - 4) * rail.padPitch;
            Vector3 p = e.origin + right * (lx * s) + up * (rail.yKey * s);
            e.maxReach = Mathf.Max(e.maxReach, Vector3.Distance(p, Eye));
        }

        // Occlusion: share of sample points in the agents' boxes whose sight line crosses the board.
        int total = 0, hit = 0;
        foreach (var a in agents)
            for (int ix = 0; ix < 3; ix++)
                for (int iy = 0; iy < 4; iy++)
                    for (int iz = 0; iz < 3; iz++)
                    {
                        Vector3 pt = a.min + Vector3.Scale(a.size, new Vector3(ix / 2f, iy / 3f, iz / 2f));
                        total++;
                        float denom = Vector3.Dot(pt - Eye, fwd);
                        if (Mathf.Abs(denom) < 1e-6f) continue;
                        float t = Vector3.Dot(e.origin - Eye, fwd) / denom;
                        if (t <= 0f || t >= 1f) continue;
                        Vector3 rel = Eye + (pt - Eye) * t - e.origin;
                        float lx = Vector3.Dot(rel, right) / s, ly = Vector3.Dot(rel, up) / s;
                        if (lx >= xL && lx <= xR && ly >= yB && ly <= yT) hit++;
                    }
        e.occluded = total > 0 ? (float)hit / total : 0f;

        // Smaller head turn, weighed against a larger board (card text is the point: 0.01 of scale
        // is worth 1.5 degrees); low; hard limits as steep penalties.
        e.cost = thetaDeg
               + 150f * (0.80f - s)
               + 100f * Mathf.Max(0f, e.bottom - MinBottom)
               + 50f * Mathf.Max(0f, e.top - 0.50f)
               + 30f * Mathf.Abs(d - 0.47f)
               + (e.maxReach > Reach ? 50f + 1000f * (e.maxReach - Reach) : 0f)
               + 400f * e.occluded
               + (e.bottom > 0.20f ? 200f : 0f);                               // standing on top of a station: not a pose
        return e;
    }

    // ───────────────────────────── entry ─────────────────────────────

    public static string Apply()
    {
        var sb = new StringBuilder();
        var warnings = new List<string>();
        var all = All();
        var ws = Find(all, "WorkspaceRoot");
        var board = Find(all, "TaskBoard");
        var dock = Find(all, "TaskDock");
        if (ws == null || board == null) return "BuildBoardV3: WorkspaceRoot or TaskBoard not found; nothing done\n";

        var quad = PrimitiveMesh(PrimitiveType.Quad);
        var presenter = board.GetComponent<BoardPresenter>();
        var expander = Ensure<BoardExpander>(board.gameObject, sb);
        var frame = board.Find("KanbanFrame_Visual");

        // The board's scale before this pass ("today"), recorded once so a second run starts from the same number.
        Undo.RecordObject(expander, "expander");
        Undo.RecordObject(board, "board pose");
        if (expander.authoredBase.sqrMagnitude < 1e-6f)
            expander.authoredBase = board.localScale.sqrMagnitude > 1e-4f ? board.localScale : Vector3.one;
        Vector3 baseScale = expander.authoredBase;
        if (board.localScale.sqrMagnitude < 1e-4f) board.localScale = baseScale;      // measurable while we work

        // ── frame extents (board-local) ──
        Vector3 fMn = new Vector3(-0.310f, -0.215f, 0f), fMx = new Vector3(0.310f, 0.262f, 0f);
        bool frameMeasured = false;
        if (frame != null)
        {
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            foreach (var r in frame.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.enabled || !ShownUnder(r.transform, frame)) continue;
                if (!OwnMeshBounds(r.transform, board, out var a, out var b)) continue;
                mn = Vector3.Min(mn, a); mx = Vector3.Max(mx, b); frameMeasured = true;
            }
            if (frameMeasured && mx.x - mn.x > 0.2f && mx.y - mn.y > 0.2f) { fMn = mn; fMx = mx; }
            else frameMeasured = false;
        }
        sb.AppendLine("frame (board-local, " + (frameMeasured ? "measured" : "ASSUMED: frame meshes not found") + "): x " +
                      fMn.x.ToString("F3") + " .. " + fMx.x.ToString("F3") + ", y " + fMn.y.ToString("F3") + " .. " + fMx.y.ToString("F3"));
        if (!frameMeasured) warnings.Add("KanbanFrame_Visual could not be measured; the rail and the pose use assumed frame extents");

        // ── what must not be covered, and what the board must clear ──
        var agents = new List<Bounds>();
        foreach (var (name, x) in new[] { ("Agent_Researcher", -0.30f), ("Agent_Developer", 0f), ("Agent_Tester", 0.30f) })
        {
            Bounds b;
            bool ok = RootBox(ws, Find(all, name), out b) && b.size.y > 0.1f && b.size.x < 0.4f && b.size.z < 0.4f;
            if (!ok) b = new Bounds(new Vector3(x, 0.155f, 0f), new Vector3(0.14f, 0.31f, 0.14f));
            agents.Add(b);
            sb.AppendLine("  " + name + " box (root-local, " + (ok ? "measured" : "nominal") + "): centre " + F3(b.center) + " size " + F3(b.size));
        }
        var obstacles = new List<KeyValuePair<string, Bounds>>();
        foreach (var name in new[] { "ResearchStation", "DevStation", "QAStation", "OutboxTray", "InboxTray", "TaskDock" })
        {
            Bounds b;
            if (RootBox(ws, Find(all, name), out b)) obstacles.Add(new KeyValuePair<string, Bounds>(name, b));
        }

        // ── 1. inspect pose: search ──
        PoseEval best = new PoseEval { cost = float.MaxValue };
        Rail bestRail = new Rail();
        foreach (float s in new[] { 0.80f, 0.78f, 0.76f, 0.74f, 0.72f })
        {
            Rail rail = LayoutRail(s, fMn, fMx);
            foreach (float d in new[] { 0.47f, 0.48f, 0.46f, 0.49f, 0.45f, 0.50f })
                for (float th = 35f; th <= 80f; th += 1f)
                {
                    var e = Evaluate(s, th, d, fMn, fMx, rail, agents, obstacles);
                    if (e.cost < best.cost - 1e-4f) { best = e; bestRail = rail; }
                }
        }
        float sBoard = best.s;
        Rail R = bestRail;

        // Apply the pose (root-local -> world -> the board's own parent space).
        board.localScale = baseScale * sBoard;
        board.SetPositionAndRotation(ws.TransformPoint(best.origin), ws.rotation * best.rot);
        EditorUtility.SetDirty(board);
        float S = Mathf.Abs(board.lossyScale.x);                                // world metres per board-local metre

        var billboard = board.GetComponent<FaceUser>();
        if (billboard != null && billboard.enabled)
        {
            Undo.RecordObject(billboard, "board billboard off");
            billboard.enabled = false;
            EditorUtility.SetDirty(billboard);
        }
        expander.useInspectPose = true;
        expander.expandedLocalPosition = board.localPosition;
        expander.expandedLocalEuler = board.localEulerAngles;
        expander.expandedScale = board.localScale;
        expander.collapsedAnchor = dock;
        expander.collapsedAnchorOffset = new Vector3(0f, 0f, 0.012f);
        expander.billboard = billboard;
        expander.aimAtHeadOnOpen = false;
        expander.maxAimDegrees = 25f;
        expander.duration = 0.35f;
        expander.collapsedFactor = 0.001f;
        expander.startExpanded = false;
        EditorUtility.SetDirty(expander);

        Vector3 centreLocal = new Vector3((fMn.x + fMx.x) * 0.5f, (fMn.y + fMx.y) * 0.5f, 0f);
        Vector3 centreRoot = ws.InverseTransformPoint(board.TransformPoint(centreLocal));
        sb.AppendLine("inspect pose:");
        sb.AppendLine("  board scale " + sBoard.ToString("F2") + " of its authored scale " + F3(baseScale) + "; origin root-local " + F3(best.origin) +
                      ", yaw " + best.rot.eulerAngles.y.ToString("F1") + " degrees (upright), " + best.thetaDeg.ToString("F0") + " degrees to the user's left of straight ahead");
        sb.AppendLine("  board world size: frame " + Mm((fMx.x - fMn.x) * S) + " x " + Mm((fMx.y - fMn.y) * S) + " mm; with the bottom rail " +
                      Mm((fMx.x - fMn.x) * S) + " x " + Mm((fMx.y - R.yBottom) * S) + " mm (was " + Mm((fMx.x - fMn.x) * baseScale.x) + " mm wide)");
        sb.AppendLine("  eye to board centre " + Vector3.Distance(centreRoot, Eye).ToString("F3") + " m (centre root-local " + F3(centreRoot) + "); lowest edge y=" +
                      best.bottom.ToString("F3") + " (over: " + best.under + "), top edge y=" + best.top.ToString("F3") + " (eye y=0.300)");
        sb.AppendLine("  agents covered from the eye: " + (best.occluded * 100f).ToString("F0") + " percent of sample points");
        sb.AppendLine("  FaceUser on the board: " + (billboard != null ? "switched off (it would turn the board, and its poke keys, every frame); BoardExpander holds the pose" : "none found"));
        sb.AppendLine("  BoardExpander: travels between the task dock (" + (dock != null ? "found" : "MISSING") + ", collapsed, scale 0.001) and the inspect pose in 0.35 s");
        float dCentre = Vector3.Distance(centreRoot, Eye);
        if (dCentre < 0.445f || dCentre > 0.505f) warnings.Add("eye to board centre is " + dCentre.ToString("F3") + " m, outside 0.45 to 0.50 m");
        if (best.occluded > 0f) warnings.Add("the open board covers " + (best.occluded * 100f).ToString("F0") + " percent of the agents' sample points from the eye");
        if (best.maxReach > Reach) warnings.Add("no pose keeps every key and pad within " + Reach.ToString("F2") + " m; the best has " + best.maxReach.ToString("F3") + " m");
        if (best.bottom > 0.20f) warnings.Add("the open board stands over " + best.under + " (lowest edge y=" + best.bottom.ToString("F3") + ")");
        if (best.top > 0.56f) warnings.Add("the open board's top edge is at y=" + best.top.ToString("F3") + ", more than 0.26 m above eye level");
        if (dock == null) warnings.Add("TaskDock not found: the board opens in place at the inspect pose instead of growing out of the dock");

        // ── 2. bottom rail ──
        BuildRail(board, frame, presenter, R, S, fMn, fMx, sb, warnings);

        // ── 3. cards ──
        BuildCards(board, frame, presenter, quad, sb, warnings);

        // ── 4. columns ──
        BuildColumns(board, frame, presenter, S, sb, warnings);

        // ── 5. drop targets ──
        int zones = 0;
        foreach (var dz in Object.FindObjectsByType<StationDropZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(dz, "drop zone colours");
            bool pad = dz.name.StartsWith("Dock_");
            dz.validColor = new Color(Teal.r, Teal.g, Teal.b, pad ? 0.90f : 0.60f);
            dz.invalidColor = new Color(Coral.r, Coral.g, Coral.b, pad ? 0.90f : 0.60f);
            EditorUtility.SetDirty(dz);
            zones++;
        }
        sb.AppendLine("drop targets: " + zones + " zones: teal halo when a drop would be accepted, coral when it would be refused (the caption says why)");

        // ── 6. dock polish ──
        if (dock != null) PolishDock(dock, sb, warnings);

        // ── report: distances ──
        sb.AppendLine("reach from the eye (limit " + Reach.ToString("F2") + " m):");
        foreach (var name in new[] { "NewTaskButton", "PauseResumeButton", "PriorityButton" })
        {
            var k = Under(board, name);
            if (k == null) { sb.AppendLine("  " + name + ": not found"); continue; }
            var face = Under(k, "ButtonVisual");
            Vector3 p = ws.InverseTransformPoint(face != null ? face.position : k.position);
            float dist = Vector3.Distance(p, Eye);
            sb.AppendLine("  key " + name + ": " + dist.ToString("F3") + " m, root-local " + F3(p) + ", " + Mm(Mathf.Abs(k.lossyScale.x)) + " mm key");
            if (dist > Reach) warnings.Add(name + " is " + dist.ToString("F3") + " m from the eye (limit " + Reach.ToString("F2") + ")");
        }
        foreach (var name in new[] { "Dock_Researcher", "Dock_Developer", "Dock_Tester" })
        {
            var p = Under(board, name);
            if (p == null) { sb.AppendLine("  " + name + ": not found"); continue; }
            Vector3 c = ws.InverseTransformPoint(p.position);
            float dist = Vector3.Distance(c, Eye);
            sb.AppendLine("  pad " + name + ": " + dist.ToString("F3") + " m, root-local " + F3(c));
            if (dist > Reach) warnings.Add(name + " is " + dist.ToString("F3") + " m from the eye (limit " + Reach.ToString("F2") + ")");
        }
        var firstLane = presenter != null ? presenter.backlogRegion : null;
        if (firstLane != null)
            sb.AppendLine("  top card slot of the first lane: " + Vector3.Distance(ws.InverseTransformPoint(firstLane.position), Eye).ToString("F3") + " m (cards are grabbed, with a distance grab as well)");

        foreach (var w in warnings) sb.AppendLine("WARNING: " + w);
        if (warnings.Count == 0) sb.AppendLine("no constraint violated");

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // ───────────────────────────── 2. bottom rail: keys, captions, pads ─────────────────────────────

    static void BuildRail(Transform board, Transform frame, BoardPresenter presenter, Rail R, float S,
                          Vector3 fMn, Vector3 fMx, StringBuilder sb, List<string> warnings)
    {
        sb.AppendLine("bottom rail:");
        // Depth of the rail: flush with the front of the frame's bottom edge when it can be measured.
        float zr = 0.004f; string zHow = "assumed";
        if (frame != null && FrontSurface(frame, board, new Rect(fMn.x + 0.05f, fMn.y, fMx.x - fMn.x - 0.10f, 0.018f), out float zf))
        { zr = zf; zHow = "measured from the frame's bottom edge"; }

        var newKey = Under(board, "NewTaskButton");
        var toolbar = Under(board, "CardToolbar");
        var pause = Under(board, "PauseResumeButton");
        var prio = Under(board, "PriorityButton");
        var assign = Under(board, "AssignDock");

        // Keys: one size. Their housings' backs rest on the rail plane.
        float back = 0f;
        foreach (var k in new[] { newKey, pause, prio }) if (k != null) { back = HousingBack(k); if (back > 0f) break; }
        if (back <= 0f || back > 0.6f) back = 0.37f;
        float zKey = zr - back * R.kL - 0.0003f;
        Vector3 keyScale = Vector3.one * R.kL;

        if (newKey != null)
        {
            if (newKey.parent != board) Undo.SetTransformParent(newKey, board, "new key on the rail");
            Pose(newKey, new Vector3(R.xNew, R.yKey, zKey), keyScale);
            KeyLabel(newKey, "+ New", 5.5f, 8.5f);
        }
        else warnings.Add("NewTaskButton not found");

        if (toolbar != null)
        {
            float xMid = (R.xPause + R.xPrio) * 0.5f, half = (R.xPrio - R.xPause) * 0.5f;
            Pose(toolbar, new Vector3(xMid, R.yKey, zKey), Vector3.one);
            if (pause != null) { Pose(pause, new Vector3(-half, 0f, 0f), keyScale); KeyLabel(pause, null, 5.5f, 8.5f); }
            if (prio != null) { Pose(prio, new Vector3(half, 0f, 0f), keyScale); KeyLabel(prio, "Priority", 5.5f, 8.5f); }
            // Caption strip above the keys (the hand comes from below and does not cover it).
            var plate = toolbar.Find("CaptionPlate");
            Vector3 capLocal = new Vector3(R.toolCapCx - xMid, R.yCap - R.yKey, zr - zKey);
            if (plate != null) Pose(plate, capLocal, new Vector3(R.toolCapW, R.capH, 1f));
            var capT = toolbar.Find("CardConsoleLabel");
            var cap = capT != null ? capT.GetComponent<TMP_Text>() : null;
            if (cap != null)
            {
                Pose(capT, capLocal + new Vector3(0f, 0f, -0.0005f), Vector3.one * 0.01f);
                StyleWorld(cap, null, (R.toolCapW - 0.008f) * S, (R.capH - 0.003f) * S, 5.5f, 7.5f, OnPlate, FontStyles.Bold, TextAlignmentOptions.Center, false);
            }
            else warnings.Add("CardConsoleLabel not found under CardToolbar");
            sb.AppendLine("  keys New / Pause / Priority: " + Mm(R.kL * S) + " mm each, centres x " + R.xNew.ToString("F3") + " / " + R.xPause.ToString("F3") + " / " + R.xPrio.ToString("F3") +
                          ", y " + R.yKey.ToString("F3") + " (board-local), " + Mm((R.xPrio - R.xPause - R.kL * 0.98f) * S) + " mm between faces; rail depth z=" + zr.ToString("F4") + " (" + zHow + ")");
            sb.AppendLine("  toolbar caption: strip " + Mm(R.toolCapW * S) + " x " + Mm(R.capH * S) + " mm above the keys, text 5.5 to 7.5 mm");
        }
        else warnings.Add("CardToolbar not found");

        if (assign != null)
        {
            float zPads = zr - 0.0015f;
            Pose(assign, new Vector3(R.padsCx, R.yKey, zPads), Vector3.one);
            var capPlate = assign.Find("CaptionPlate");
            Vector3 capLocal = new Vector3(0f, R.yCap - R.yKey, zr - zPads);
            if (capPlate != null) Pose(capPlate, capLocal, new Vector3(R.padCapW, R.capH, 1f));
            var capT = assign.Find("Caption");
            var cap = capT != null ? capT.GetComponent<TMP_Text>() : null;
            if (cap != null)
            {
                Pose(capT, capLocal + new Vector3(0f, 0f, -0.0005f), Vector3.one * 0.01f);
                StyleWorld(cap, null, (R.padCapW - 0.008f) * S, (R.capH - 0.003f) * S, 6.5f, 8.5f, OnPlate, FontStyles.Bold, TextAlignmentOptions.Center, false);
            }
            var pads = new[] { "Dock_Researcher", "Dock_Developer", "Dock_Tester" };
            int found = 0;
            for (int i = 0; i < pads.Length; i++)
            {
                var p = assign.Find(pads[i]);
                if (p == null) { warnings.Add("assignment pad not found: " + pads[i]); continue; }
                found++;
                Pose(p, new Vector3((i - 1) * R.padPitch, 0f, 0f), Vector3.one);
                var plate = p.Find("Plate");
                if (plate != null) Pose(plate, Vector3.zero, new Vector3(R.padW, R.padH, 1f));
                var halo = p.Find("Halo");
                if (halo != null) Pose(halo, new Vector3(0f, 0f, 0.0007f), new Vector3(R.padW + 0.012f, R.padH + 0.012f, 1f));
                var nameT = p.Find("Name");
                var name = nameT != null ? nameT.GetComponent<TMP_Text>() : null;
                if (name != null)
                {
                    Pose(nameT, new Vector3(0f, 0f, -0.0005f), Vector3.one * 0.01f);
                    StyleWorld(name, null, (R.padW - 0.008f) * S, (R.padH - 0.008f) * S, 8f, 10.5f, name.color, FontStyles.Bold, TextAlignmentOptions.Center, false);
                }
                var bc = p.GetComponent<BoxCollider>();
                if (bc != null)
                {
                    Undo.RecordObject(bc, "pad trigger");
                    bc.isTrigger = true;
                    bc.size = new Vector3(R.padW + 0.006f, R.padH + 0.010f, 0.10f);
                    bc.center = new Vector3(0f, 0f, -0.02f);
                    EditorUtility.SetDirty(bc);
                }
                // A dropped card rests in front of the pad's plate, not inside it.
                var anchor = Node(p, "DropAnchor", sb);
                Pose(anchor, new Vector3(0f, 0f, -0.014f), Vector3.one);
                var dz = p.GetComponent<StationDropZone>();
                if (dz != null)
                {
                    Undo.RecordObject(dz, "pad highlight");
                    dz.dropAnchor = anchor;
                    dz.highlightPlate = plate;
                    dz.validPlateScale = 1.08f;
                    EditorUtility.SetDirty(dz);
                }
            }
            sb.AppendLine("  assignment pads: " + found + " pads " + Mm(R.padW * S) + " x " + Mm(R.padH * S) + " mm, pitch " + Mm(R.padPitch * S) + " mm, names 8 to 10.5 mm; " +
                          "the accepting pad grows 8 percent and lights teal, a refusing pad lights coral; caption strip " + Mm(R.padCapW * S) + " mm, text 6.5 to 8.5 mm");
        }
        else warnings.Add("AssignDock not found");

        // A drop must be deliberate: with the smaller board the lowest card row is close to the pads.
        var card = Under(board, "CardTemplate");
        var drag = card != null ? card.GetComponent<TaskDragController>() : null;
        if (drag != null)
        {
            Undo.RecordObject(drag, "snap radius");
            drag.snapRadius = 0.07f;
            EditorUtility.SetDirty(drag);
            sb.AppendLine("  drop: a card must be inside a pad or a station zone, or within 7 cm of its centre (was 15 cm), so picking a card up near the rail does not assign it");
        }
        if (R.padW * 1.08f > R.padPitch) warnings.Add("a grown pad would touch its neighbour");
        if (R.kL * S * 0.98f < 0.022f) warnings.Add("board keys are under 22 mm");
    }

    // ───────────────────────────── 3. cards ─────────────────────────────

    static void BuildCards(Transform board, Transform frame, BoardPresenter presenter, Mesh quad, StringBuilder sb, List<string> warnings)
    {
        sb.AppendLine("cards:");
        var card = Under(board, "CardTemplate");
        if (card == null) { warnings.Add("CardTemplate not found; cards unchanged"); return; }
        var view = card.GetComponent<TaskCardView>();
        float cs = Mathf.Abs(card.localScale.x);                 // card units -> board units
        float k = Mathf.Abs(card.lossyScale.x);                  // card units -> world metres
        if (k < 1e-6f) { warnings.Add("CardTemplate has no measurable scale; cards unchanged"); return; }

        // The card art's footprint in card units (not centred on the pivot), and its role strip.
        Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
        MeshRenderer strip = null; Vector3 sMn = Vector3.zero, sMx = Vector3.zero;
        foreach (var r in card.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!r.enabled || r.GetComponent<TMP_Text>() != null || r.GetComponent<TMP_SubMesh>() != null) continue;
            if (r.name == "HoverFrame" || r.name == "CardPlate" || r.name == "OwnerChip" || !ShownUnder(r.transform, card)) continue;
            if (!OwnMeshBounds(r.transform, card, out var a, out var b)) continue;
            mn = Vector3.Min(mn, a); mx = Vector3.Max(mx, b);
            if (r.name == "Card_RoleHeaderStrip") { strip = r; sMn = a; sMx = b; }
        }
        if (mx.x <= mn.x) { warnings.Add("the card template has no visible mesh; cards unchanged"); return; }
        float W = mx.x - mn.x, H = mx.y - mn.y, cx = (mn.x + mx.x) * 0.5f, cy = (mn.y + mx.y) * 0.5f;

        // ── physical edge: a darker plate behind the face, a little larger than it ──
        const float bThin = 0.0012f, bHover = 0.0020f, bBold = 0.0030f;      // border widths in card units
        float zPlate = mx.z + 0.0003f;                                       // behind the back of the card art
        var plate = Shape(card, "CardPlate", quad, Mat("Card_EdgePlate", Housing), new Vector3(cx, cy, zPlate), new Vector3(W + 2f * bThin, H + 2f * bThin, 1f));
        var hover = card.GetComponent<GrabHoverHighlight>();
        if (hover != null && hover.indicator != null)
        {
            // The edge plate now shows hover and held; the old translucent frame stays in the scene, unused and inactive.
            Undo.RecordObject(hover, "hover indicator");
            if (hover.indicator.activeSelf) hover.indicator.SetActive(false);
            hover.indicator = null;
            EditorUtility.SetDirty(hover);
        }
        if (view != null)
        {
            Undo.RecordObject(view, "card edge");
            view.edgePlate = plate.GetComponent<Renderer>();
            view.hover = hover;
            // Ratios of the plate's resting size, so they hold whatever mesh the Kit builder gives the plate.
            view.edgeHoverGrow = new Vector2((W + 2f * bHover) / (W + 2f * bThin), (H + 2f * bHover) / (H + 2f * bThin));
            view.edgeBoldGrow = new Vector2((W + 2f * bBold) / (W + 2f * bThin), (H + 2f * bBold) / (H + 2f * bThin));
            view.edgeIdleColor = Housing;
            view.edgeActiveColor = Teal;
            view.edgePendingColor = Amber;
            view.edgeRefusedColor = Coral;
            view.selectedLift = 0.004f;
            view.selectedScale = 1.04f;
            view.settleSeconds = 0.2f;
            view.refusedSeconds = 1.2f;
            EditorUtility.SetDirty(view);
        }
        else warnings.Add("CardTemplate has no TaskCardView");
        sb.AppendLine("  edge plate 'CardPlate': " + Mm(bThin * k) + " mm dark border at rest, " + Mm(bHover * k) + " mm teal on hover, " + Mm(bBold * k) +
                      " mm teal when selected or held (selected also lifts 4 mm and grows to 1.04), amber while pending, coral for 1.2 s when refused");

        // The plate must be in front of the lane surface behind the cards.
        var region = presenter != null ? presenter.backlogRegion : null;
        if (frame != null && region != null)
        {
            Vector3 slot = region.localPosition;
            float plateBoardZ = slot.z + zPlate * cs;
            if (FrontSurface(frame, board, new Rect(slot.x - 0.03f, slot.y - 0.03f, 0.06f, 0.06f), out float zLane))
            {
                sb.AppendLine("  edge plate is " + Mm(zLane - plateBoardZ) + " mm (board units) in front of the lane surface");
                if (plateBoardZ > zLane - 0.0004f) warnings.Add("the card edge plate is at or behind the lane surface (" + Mm(zLane - plateBoardZ) + " mm): it will not show");
            }
            else sb.AppendLine("  lane surface depth could not be read from the frame mesh: check on the headset that the card edge shows");
        }

        // ── lanes ──
        float plateW = (W + 2f * bThin) * cs, plateH = (H + 2f * bThin) * cs;     // board units
        float S = k / Mathf.Max(1e-6f, cs);
        if (presenter != null)
        {
            float pitch = presenter.cardSpacing.y;
            sb.AppendLine("  card footprint with its edge: " + Mm(plateW * S) + " x " + Mm(plateH * S) + " mm in the world (" + Mm(plateW) + " x " + Mm(plateH) +
                          " board units); row pitch " + Mm(pitch * S) + " mm (" + Mm(pitch) + "), gap " + Mm((pitch - plateH) * S) + " mm; columns " + presenter.columns);
            if (pitch - plateH < 0.003f) warnings.Add("card rows are " + Mm(pitch - plateH) + " mm apart (board units): under 3 mm");
            if ((H + 2f * bBold) * cs * 1.04f > pitch + 0.004f) warnings.Add("a selected card (thick edge, 1.04) is taller than the row pitch by more than its lift hides");
        }
        if (frame != null)
        {
            float laneW = 0f;
            foreach (var r in frame.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name.StartsWith("Header_") && OwnMeshBounds(r.transform, board, out var a, out var b)) { laneW = b.x - a.x; break; }
            if (laneW > 0f)
            {
                sb.AppendLine("  lane width " + Mm(laneW * S) + " mm (" + Mm(laneW) + " board units); card with edge needs " + Mm(plateW * S) + " mm");
                if (plateW > laneW) warnings.Add("cards with their edge are wider than the lane by " + Mm(plateW - laneW) + " mm (board units)");
            }
        }

        // ── face: owner chip on the role strip, title, state line, mode words ──
        const float pad = 0.0035f;
        float innerW = W - 2f * pad;
        bool hasStrip = strip != null && sMx.y - sMn.y > 0.002f;
        var tint = card.GetComponent<CardRoleTint>();
        Transform titleT = card.Find("Title");
        float faceLabelZ = titleT != null ? titleT.localPosition.z : 0.0004f;

        float chipY, chipH, bodyTop;
        if (hasStrip)
        {
            chipY = (sMn.y + sMx.y) * 0.5f; chipH = sMx.y - sMn.y;
            bodyTop = sMn.y - 0.0010f;
        }
        else
        {
            // No role strip on this card art: a chip quad on the face carries the role colour.
            chipH = 0.0075f; chipY = mx.y - 0.0020f - chipH * 0.5f;
            bodyTop = chipY - chipH * 0.5f - 0.0010f;
            var chip = Shape(card, "OwnerChip", quad, Mat("Card_OwnerChip", InkSoft), new Vector3(cx, chipY, faceLabelZ + 0.0002f), new Vector3(innerW, chipH, 1f));
            if (tint != null) { Undo.RecordObject(tint, "role chip"); tint.headerStrip = chip.GetComponent<Renderer>(); tint.emission = 0f; EditorUtility.SetDirty(tint); }
            if (view != null) { view.tintHeaderText = tint != null; EditorUtility.SetDirty(view); }
            sb.AppendLine("  no role strip on the card art: created 'OwnerChip' on the face");
        }
        float bodyBottom = mn.y + 0.0020f;

        // Heights in card units for a world font size (line box about 1.25 em).
        float Line(float mm) => mm * 1.25f / 1000f / k;
        const float modeMm = 5.5f, stateMaxMm = 7.5f, stateMinMm = 6.0f;
        float modeH = Line(modeMm), stateH = Line(stateMaxMm);
        float modeY = bodyBottom + modeH * 0.5f;
        float stateY = bodyBottom + modeH + 0.0006f + stateH * 0.5f;
        float titleBottom = bodyBottom + modeH + 0.0006f + stateH + 0.0008f;
        float titleH = Mathf.Max(0.004f, bodyTop - titleBottom);
        float titleMaxMm = Mathf.Min(12f, titleH * k * 1000f / 2.44f);         // two lines of about 1.22 em
        float titleMinMm = Mathf.Min(titleMaxMm, Mathf.Max(8f, titleMaxMm * 0.70f));
        float chipMm = Mathf.Clamp(chipH * k * 1000f * 0.80f, 5.5f, 6.5f);

        void Place(string name, float x, float y, float boxW, float boxH, float minMm, float maxMm, Color color, FontStyles style, TextAlignmentOptions align, bool wrap, bool onChip)
        {
            var t = card.Find(name);
            var tmp = t != null ? t.GetComponent<TMP_Text>() : null;
            if (tmp == null) { warnings.Add("card label not found: " + name); return; }
            Undo.RecordObject(t, "card label");
            float z = t.localPosition.z;                                        // each label keeps its measured depth
            if (onChip && !hasStrip) z = faceLabelZ - 0.0003f;                  // 0.5 mm proud of the OwnerChip
            t.localPosition = new Vector3(x, y, z);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * 0.01f;
            StyleWorld(tmp, null, boxW * k, boxH * k, minMm, maxMm, color, style, align, wrap);
        }

        float roleW = innerW * 0.60f, prioW = innerW * 0.36f;
        Place("SH_Role", mn.x + pad + roleW * 0.5f, chipY, roleW, chipH, 5.5f, chipMm, OnPlate, FontStyles.Bold, TextAlignmentOptions.Left, false, true);
        Place("SH_Priority", mx.x - pad - prioW * 0.5f, chipY, prioW, chipH, 5.5f, chipMm, OnPlate, FontStyles.Bold, TextAlignmentOptions.Right, false, true);
        Place("Title", cx, titleBottom + titleH * 0.5f, innerW, titleH, titleMinMm, titleMaxMm, Ink, FontStyles.Bold, TextAlignmentOptions.TopLeft, true, false);
        Place("SH_State", cx, stateY, innerW, stateH, stateMinMm, stateMaxMm, Ink, FontStyles.Normal, TextAlignmentOptions.Left, false, false);
        Place("SH_Source", cx, modeY, innerW, modeH, modeMm, modeMm, InkSoft, FontStyles.Bold, TextAlignmentOptions.Left, false, false);

        sb.AppendLine("  card face " + Mm(W * k) + " x " + Mm(H * k) + " mm in the world (card scale " + cs.ToString("F2") + ", world scale " + k.ToString("F3") + "); role " +
                      (hasStrip ? "strip " + Mm(chipH * k) + " mm tall" : "chip " + Mm(chipH * k) + " mm tall"));
        sb.AppendLine("  world text heights: title " + titleMinMm.ToString("F1") + " to " + titleMaxMm.ToString("F1") + " mm (two lines in " + Mm(innerW * k) + " x " + Mm(titleH * k) +
                      " mm); state " + stateMinMm.ToString("F1") + " to " + stateMaxMm.ToString("F1") + " mm; owner and priority words 5.5 to " + chipMm.ToString("F1") +
                      " mm; mode words " + modeMm.ToString("F1") + " mm");
        if (titleMaxMm < 11f) warnings.Add("card title tops out at " + titleMaxMm.ToString("F1") + " mm in the world (target 11 to 12): the card face is " + Mm(H * k) + " mm tall");
        if (chipH * k * 1000f < 6.9f) warnings.Add("the role strip is " + Mm(chipH * k) + " mm tall in the world: the 5.5 mm owner word fills it with almost no margin");
    }

    // ───────────────────────────── 4. columns ─────────────────────────────

    static void BuildColumns(Transform board, Transform frame, BoardPresenter presenter, float S, StringBuilder sb, List<string> warnings)
    {
        sb.AppendLine("columns:");
        var names = new[] { "Label_Backlog", "Label_Working", "Label_NeedsMe", "Label_Completed" };
        var headings = new TMP_Text[4];
        var headers = new Renderer[4];
        var blocks = new List<KeyValuePair<float, Renderer>>();
        if (frame != null)
            foreach (var r in frame.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name.StartsWith("Header_") && OwnMeshBounds(r.transform, board, out var a, out var b))
                    blocks.Add(new KeyValuePair<float, Renderer>((a.x + b.x) * 0.5f, r));

        float minWorld = float.MaxValue, maxWorld = 0f;
        for (int i = 0; i < names.Length; i++)
        {
            var t = board.Find(names[i]);
            var tmp = t != null ? t.GetComponent<TMP_Text>() : null;
            if (tmp == null) { warnings.Add("column heading not found: " + names[i]); continue; }
            headings[i] = tmp;
            float lossy = Mathf.Abs(t.lossyScale.y);
            float perMm = lossy > 1e-7f ? 1f / (lossy * 100f) : 1f;
            Undo.RecordObject(tmp, "heading size");
            // Never under 8.5 mm in the world, also with a count after the name; up to 15 mm.
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 8.5f * perMm;
            tmp.fontSizeMax = 15f * perMm;
            tmp.fontSize = tmp.fontSizeMax;
            tmp.fontStyle = FontStyles.Bold;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            EditorUtility.SetDirty(tmp);
            minWorld = Mathf.Min(minWorld, 8.5f); maxWorld = Mathf.Max(maxWorld, 15f);

            // The header block this label is printed on: the nearest one along x.
            float lx = t.localPosition.x, bestD = float.MaxValue;
            foreach (var kv in blocks)
            {
                float d = Mathf.Abs(kv.Key - lx);
                if (d < bestD) { bestD = d; headers[i] = kv.Value; }
            }
            if (headers[i] == null) warnings.Add("no header block found for " + names[i] + ": that lane is not dimmed when empty");
        }
        if (presenter != null)
        {
            Undo.RecordObject(presenter, "lane headings");
            presenter.laneHeadings = headings;
            presenter.laneHeaders = headers;
            presenter.emptyLaneBrightness = 0.62f;
            EditorUtility.SetDirty(presenter);
        }
        else warnings.Add("TaskBoard has no BoardPresenter: headings are not wired");
        if (maxWorld > 0f)
            sb.AppendLine("  headings: " + minWorld.ToString("F1") + " to " + maxWorld.ToString("F1") + " mm in the world, bold; the presenter prints the count after the name (\"Backlog 3\") " +
                          "and dims an empty lane's header block to 62 percent through a property block");
        var title = board.Find("Title");
        var titleTmp = title != null ? title.GetComponent<TMP_Text>() : null;
        if (titleTmp != null)
            sb.AppendLine("  board title: " + (titleTmp.fontSizeMax * Mathf.Abs(title.lossyScale.y) * 100f).ToString("F1") + " mm in the world (unchanged font numbers, scaled with the board)");
        sb.AppendLine("  empty space: the four-column frame mesh is kept; the board is smaller instead (scale " + (S).ToString("F2") + " in the world)");
    }

    // ───────────────────────────── 6. dock polish ─────────────────────────────

    static void PolishDock(Transform dock, StringBuilder sb, List<string> warnings)
    {
        sb.AppendLine("task dock:");
        const float slack = 1.10f;                       // room for a true bold cut, which is wider than synthetic bold
        var chip = dock.Find("ModeChip");
        var modeT = dock.Find("Mode");
        var mode = modeT != null ? modeT.GetComponent<TMP_Text>() : null;
        var start = dock.Find("DockStartKey");
        var boardKey = dock.Find("DockBoardKey");

        // The chip was 48 mm for a 56 mm label. It is now 60 mm; the two keys move 8 and 4 mm right to make room
        // (still 8.8 mm between their faces, 4 mm from the plate edge).
        const float chipW = 0.060f, chipH = 0.016f, chipCx = -0.046f, rowY = -0.022f;
        if (chip != null) Pose(chip, new Vector3(chipCx, rowY, chip.localPosition.z), new Vector3(chipW, chipH, 1f));
        if (start != null) { Undo.RecordObject(start, "dock key"); start.localPosition = new Vector3(0.008f, start.localPosition.y, start.localPosition.z); EditorUtility.SetDirty(start); }
        if (boardKey != null) { Undo.RecordObject(boardKey, "dock key"); boardKey.localPosition = new Vector3(0.056f, boardKey.localPosition.y, boardKey.localPosition.z); EditorUtility.SetDirty(boardKey); }
        if (mode != null)
        {
            Undo.RecordObject(modeT, "mode label");
            modeT.localPosition = new Vector3(chipCx, rowY, modeT.localPosition.z);
            float lossy = Mathf.Abs(modeT.lossyScale.y);
            float rectW = (chipW - 0.002f) / Mathf.Max(1e-7f, lossy);           // 1 mm inside the chip on each side
            float perMm = 1f / (Mathf.Max(1e-7f, lossy) * 100f);
            float need = PreferredWidth(mode, "SCRIPTED DEMO", 6.5f * perMm, FontStyles.Bold) * slack;
            float fitMm = need > 1e-4f ? Mathf.Min(6.5f, 6.5f * rectW / need) : 6.5f;
            StyleWorld(mode, null, chipW - 0.002f, 0.014f, 5.5f, Mathf.Max(5.5f, fitMm), mode.color, FontStyles.Bold, TextAlignmentOptions.Center, false);
            sb.AppendLine("  mode chip 60 x 16 mm; \"SCRIPTED DEMO\" in bold at 6.5 mm needs " + (need * lossy * 1000f).ToString("F1") + " mm with 10 percent slack, the label box is " +
                          Mm(chipW - 0.002f) + " mm: font " + Mathf.Max(5.5f, fitMm).ToString("F1") + " mm (floor 5.5)");
            if (fitMm < 5.5f) warnings.Add("\"SCRIPTED DEMO\" does not fit the dock's mode chip at 5.5 mm bold with 10 percent slack: it will be cut with an ellipsis");
        }
        else warnings.Add("TaskDock/Mode label not found");

        // The three text lines: do the longest strings the dock can show fit at the smallest size with the same slack?
        var checks = new (string label, string text)[]
        {
            ("Summary", "12 tasks   1 ready   1 running   1 needs you"),
            ("Title", "Build the login flow (OAuth scaffold)"),
            ("State", "Not started: Couldn't reach the desktop"),
            ("State", "In progress: Researcher is on it"),
        };
        foreach (var (label, text) in checks)
        {
            var t = dock.Find(label);
            var tmp = t != null ? t.GetComponent<TMP_Text>() : null;
            if (tmp == null) { warnings.Add("TaskDock/" + label + " not found"); continue; }
            float need = PreferredWidth(tmp, text, tmp.fontSizeMin, FontStyles.Bold) * slack;
            float have = tmp.rectTransform.sizeDelta.x;
            sb.AppendLine("  " + label + " \"" + text + "\": needs " + need.ToString("F1") + " of " + have.ToString("F1") + " units at its smallest size in bold with slack" +
                          (need <= have ? "" : " -> ends with an ellipsis"));
        }
        sb.AppendLine("  while a card is running the state line names the working agent (\"In progress: Developer is on it\") from the session reducer's agent states");
    }
}
