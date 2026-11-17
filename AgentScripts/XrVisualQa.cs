using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// Read-only XR visual QA for the open scene (MainMRScene). It measures the classes of defect that an
/// Editor capture cannot show and that have reached the headset: text floating in front of its surface,
/// objects parented inside the 100x rig armature, key caps that travel behind their housing, cards
/// wider than their lanes, overlapping groups, controls out of reach, text too small to read.
///
///   unity command script run --file AgentScripts/XrVisualQa.cs --entry XrVisualQa.Run
///   unity command script run --file AgentScripts/XrVisualQa.cs --entry XrVisualQa.RunExpanded
///
/// Run() never changes a transform, never marks the scene dirty and never saves. The only side effect is
/// TMP_Text.ForceMeshUpdate() on visible labels (regenerates the text mesh; does not dirty the scene).
/// RunExpanded() additionally sets the localScale of groups that are collapsed to near zero to the
/// authored scale found on a component, IN MEMORY ONLY, runs the same checks and restores the scale in
/// a finally block. It does not use Undo, SetDirty or SaveScene.
///
/// Every line is "PASS|WARN|FAIL|INFO [check] path: numbers". The report ends with counts and the FAILs.
/// </summary>
public static class XrVisualQa
{
    // ───────────────────────────── names (edit here) ─────────────────────────────

    const string WorkspaceRootName = "WorkspaceRoot";
    const string ExpectedSceneName = "MainMRScene";
    static readonly Vector3 EyeLocal = new Vector3(0f, 0.30f, 0.62f);   // seated eye, WorkspaceRoot-local

    const string PokeTypeName = "PokeInteractable";
    const string PokeVisualTypeName = "PokeInteractableVisual";
    const string RestGuardTypeName = "PokeVisualRestGuard";
    const string ReRegisterTypeName = "ReRegisterInteractable";
    const string BillboardTypeName = "FaceUser";
    const string GrabbableTypeName = "Grabbable";
    const string ButtonVisualName = "ButtonVisual";
    const string CapName = "Cap3D";
    const string HousingName = "Housing3D";
    const string BoardName = "TaskBoard";
    const string BoardPresenterTypeName = "BoardPresenter";
    const string CardTemplateName = "CardTemplate";
    const string BoardFrameName = "KanbanFrame_Visual";

    static readonly string[] GrabTypeNames =
        { "Grabbable", "HandGrabInteractable", "DistanceHandGrabInteractable", "GrabInteractable", "DistanceGrabInteractable" };

    /// <summary>Grabbables that the main flow needs. Others (desk decor) only WARN when out of reach.</summary>
    static readonly string[] CoreGrabNames = { "DecisionDocument", "CardTemplate", "TaskCard", "StampApprove", "StampReject" };

    /// <summary>Major groups for the overlap and occlusion checks: exact names.</summary>
    static readonly string[] GroupExactNames =
    {
        "ResearchStation", "DevStation", "QAStation", "TaskBoard", "TaskDock", "DecisionDocument",
        "InboxTray", "OutboxTray", "StampApprove", "StampReject", "GuideSign", "ArtifactPanel",
        "ArchiveReadout", "AssignDock", "Monitor", "DecisionPanel",
    };
    /// <summary>Major groups: name starts with.</summary>
    static readonly string[] GroupPrefixNames = { "Agent_" };
    /// <summary>Major groups: name contains (case-insensitive). The top-most match is the group.</summary>
    static readonly string[] GroupContainsNames = { "Nameplate", "NameTag", "Plaque", "Receipt", "Factory" };

    /// <summary>Group tokens that can hide things from the eye (check 9).</summary>
    static readonly string[] OccluderTokens = { "Agent_", "ResearchStation", "DevStation", "QAStation", "Factory", "TaskBoard" };

    /// <summary>Pairs that are allowed to intersect (matched against the group root name by "starts
    /// with", or against the group token). Add a pair here only with a reason.</summary>
    static readonly string[][] OverlapAllowList =
    {
        new[] { "DecisionDocument", "Agent_Researcher" },   // the paper is carried in the Researcher's hand
        new[] { "NameTag", "Agent_" },                      // a tag sits on / above its own agent
        new[] { "Nameplate_", "ResearchStation" },          // a nameplate stands on its own desk
        new[] { "Nameplate_", "DevStation" },
        new[] { "Nameplate_", "QAStation" },
        // The open board stands above the Tester's side of the office; BuildBoardV3 places its lowest
        // edge 12 mm clear of what is under it, but the board's box in this test is inflated by its yaw.
        new[] { "TaskBoard", "QAStation" },
    };

    /// <summary>Renderers that are not printable surfaces and not solid bodies (ignored in 3, 8, 9, 10).</summary>
    static readonly string[] NonSurfaceNameParts = { "HoverFrame", "Halo", "Glow", "Shadow", "Outline", "Highlight" };

    static readonly string[] ModelExtensions = { ".fbx", ".blend", ".obj", ".glb", ".gltf", ".dae" };

    // ───────────────────────────── thresholds (edit here) ─────────────────────────────

    const float MinLossyScale = 0.01f;         // transforms below this are treated as hidden
    const float HiddenLocalScale = 0.005f;     // a non-text transform scaled below this is "collapsed"
    const float MinRendererWorldSize = 0.001f; // a renderer smaller than 1 mm is treated as hidden
    const float HiddenTextEmMm = 1.0f;         // glyphs under 1 mm: collapsed or mis-scaled, not "small"

    const float TextFailMm = 4.5f;             // check 1
    const float TextWarnMm = 5.5f;
    const float TextHugeMm = 40f;
    const float CapHeightRatio = 0.72f;        // capital height / em for Inter and LiberationSans
    const float AngularFromDistance = 0.60f;   // metres from the eye
    const float AngularWarnDeg = 0.60f;        // degrees per capital height

    const float OverflowWarnMm = 0.3f;         // check 2: rendered bounds past the rect
    const float OverflowFailMm = 1.0f;

    const float DepthPassMaxMm = 1.0f;         // check 3
    const float DepthWarnMaxMm = 2.5f;
    const float DepthNegTolMm = 0.05f;
    const float DepthFrontWindow = 0.006f;     // metres searched in front of the text
    const float DepthBehindWindow = 0.030f;    // metres searched behind the text
    const float DepthSpreadWarnMm = 1.0f;      // tilt: gap difference across the label
    const int MaxMeshVertsForTriangles = 120000;

    const float FacingFailDeg = 75f;           // check 4

    const float BigScale = 20f;                // check 5
    const float SmallScale = 0.02f;
    const float TextNonUniformTol = 0.05f;
    const float ColliderSaneWorldSize = 0.25f; // a collider on a rig bone larger than this is suspicious
    const float ColliderFailWorldSize = 0.80f;
    const float TextUnderRigFailMm = 60f;
    const float RendererUnderRigFailSize = 0.5f; // metres: bigger than any agent, so not a rig part

    const float TravelMinMm = 3f;              // check 6
    const float TravelMaxMm = 15f;
    const float TravelFailMm = 1f;
    const float RimProudWarnMm = 0.2f;
    const float KeyFaceFailMm = 18f;
    const float KeyFaceWarnMm = 22f;
    const float KeyFacingMaxDeg = 60f;
    const float RestGuardTolMm = 0.1f;

    const float ReachWarn = 0.55f;             // check 7 (metres from the eye)
    const float ReachFail = 0.65f;

    const float OverlapWarnMm = 1f;            // check 8
    const float OverlapFailMm = 8f;
    const float KeySpacingWarnMm = 8f;
    const float KeySpacingFailMm = 4f;

    const float OccluderShrink = 0.9f;         // check 9: occluder boxes are shrunk to cut false alarms

    const float CardGapWarnMm = 3f;            // check 10 (board units, metres at board scale 1)

    const int MaxIdleKeys = 6;                 // check 11

    const bool ListPassLines = true;           // false = only WARN / FAIL / INFO lines
    const int PathDepth = 6;

    // ───────────────────────────── state for one run ─────────────────────────────

    static StringBuilder _sb;
    static List<string> _fails;
    static int _nPass, _nWarn, _nFail, _nInfo;
    static List<Transform> _all;
    static Transform _ws;
    static Vector3 _eye;
    static Dictionary<Mesh, MeshData> _meshCache;
    static List<SolidInfo> _solids;
    static List<TextInfo> _texts;
    static List<KeyInfo> _keys;
    static List<Group> _groups;
    static Dictionary<Transform, Group> _groupOf;
    static HashSet<Transform> _bones;
    static List<string> _hidden;
    static int _hiddenRenderers;

    class MeshData { public Vector3[] v; public int[] t; }

    class SolidInfo
    {
        public Renderer r;
        public MeshRenderer mr;      // null for skinned
        public bool nonSurface;      // hover frame, halo, ...
    }

    class TextInfo
    {
        public TMP_Text tmp;
        public Transform tf;
        public string path;
        public string hidden;
        public bool empty;
        public bool meshOk;
        public bool hasChars;
        public float emMm;
        public float floorMm;
        public Vector3 centre;
        public float eyeDist;
        public bool billboard;
        public Transform grabRoot;
    }

    class KeyInfo
    {
        public Transform key;
        public MonoBehaviour poke;
        public string path;
        public string hidden;
        public Transform visual, cap, housing, baseTf;
        public bool baseFromSdk;
        public Vector3 u, right, up;     // u = toward the finger / reader
        public float travel;             // metres
        public bool capOk, housingOk;
        public float capFront, capBack, housingFront;
        public Vector3 faceCentre;
        public float halfW, halfH;
        public bool faceApprox;
        public Transform grabRoot;
        public bool billboard;
    }

    class Group
    {
        public Transform root;
        public string label;
        public string token;
        public bool isKey;
        public bool any;
        public Vector3 mn = Vector3.positiveInfinity;
        public Vector3 mx = Vector3.negativeInfinity;
        public int renderers;
        public bool occluder;
    }

    class Obb
    {
        public Vector3 c;
        public Vector3[] ax = new Vector3[3];
        public Vector3 h;
    }

    class GrabInfo
    {
        public Transform root;
        public bool distance;
        public string types = "";
    }

    // ───────────────────────────── entry points ─────────────────────────────

    public static string Run() { return RunInternal(false); }

    /// <summary>Same checks with collapsed groups reported at their authored scale where a component
    /// stores it. Temporarily changes localScale in memory and restores it; see the class summary.</summary>
    public static string RunExpanded() { return RunInternal(true); }

    static string RunInternal(bool expanded)
    {
        _sb = new StringBuilder();
        _fails = new List<string>();
        _nPass = 0; _nWarn = 0; _nFail = 0; _nInfo = 0;
        _meshCache = new Dictionary<Mesh, MeshData>();
        _hidden = new List<string>();
        _hiddenRenderers = 0;

        var scene = EditorSceneManager.GetActiveScene();
        bool dirtyBefore = scene.isDirty;
        var restore = new List<KeyValuePair<Transform, Vector3>>();

        _sb.AppendLine("XR VISUAL QA  (" + (expanded ? "RunExpanded: collapsed groups at authored scale" : "Run: scene as it is at idle") + ")");
        _sb.AppendLine("scene=" + scene.path + " dirtyBefore=" + dirtyBefore);
        if (scene.name != ExpectedSceneName)
            Warn("setup", scene.name, "expected the open scene to be " + ExpectedSceneName);

        try
        {
            _all = new List<Transform>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    _all.Add(t);

            _ws = FindByName(WorkspaceRootName);
            if (_ws != null) _eye = _ws.TransformPoint(EyeLocal);
            else { _eye = EyeLocal; Warn("setup", WorkspaceRootName, "not found: eye point taken as world " + V(EyeLocal)); }
            _sb.AppendLine("gameObjects=" + _all.Count + " eye(world)=" + V(_eye));

            if (expanded)
            {
                try { ExpandCollapsed(restore); }
                catch (System.Exception ex) { Fail("script", "expand", "exception: " + ex.GetType().Name + ": " + ex.Message); }
            }

            try { BuildShared(); }
            catch (System.Exception ex) { Fail("script", "collect", "exception: " + ex.GetType().Name + ": " + ex.Message); }

            Section("1. TEXT LEGIBILITY (em height in mm; angular size beyond " + AngularFromDistance.ToString("F2") + " m)");
            try { CheckLegibility(); } catch (System.Exception ex) { ScriptError("1 legibility", ex); }
            Section("2. TEXT OVERFLOW");
            try { CheckOverflow(); } catch (System.Exception ex) { ScriptError("2 overflow", ex); }
            Section("3. TEXT DEPTH OFFSET (gap from the text plane to the surface behind it)");
            try { CheckDepth(); } catch (System.Exception ex) { ScriptError("3 depth", ex); }
            Section("4. TEXT FACING");
            try { CheckFacing(); } catch (System.Exception ex) { ScriptError("4 facing", ex); }
            Section("5. PARENT SCALE HAZARDS");
            try { CheckScaleHazards(); } catch (System.Exception ex) { ScriptError("5 scale", ex); }
            Section("6. POKE KEYS");
            try { CheckKeys(); } catch (System.Exception ex) { ScriptError("6 key", ex); }
            Section("7. REACH (metres from the eye; warn " + ReachWarn.ToString("F2") + ", fail " + ReachFail.ToString("F2") + ")");
            try { CheckReach(); } catch (System.Exception ex) { ScriptError("7 reach", ex); }
            Section("8. OVERLAPS (oriented boxes of the major groups; key spacing)");
            try { CheckOverlaps(); } catch (System.Exception ex) { ScriptError("8 overlap", ex); }
            Section("9. OCCLUSION FROM THE EYE");
            try { CheckOcclusion(); } catch (System.Exception ex) { ScriptError("9 occlusion", ex); }
            Section("10. CARD LANES");
            try { CheckCardLanes(); } catch (System.Exception ex) { ScriptError("10 lanes", ex); }
            Section("11. COUNTS");
            try { CheckCounts(); } catch (System.Exception ex) { ScriptError("11 counts", ex); }
            Section("SKIPPED AS HIDDEN (inactive, collapsed by scale, disabled or invisible)");
            try { ReportHidden(expanded); } catch (System.Exception ex) { ScriptError("hidden", ex); }
        }
        finally
        {
            for (int i = restore.Count - 1; i >= 0; i--)
                if (restore[i].Key != null) restore[i].Key.localScale = restore[i].Value;
        }

        Section("SUMMARY");
        _sb.AppendLine("PASS=" + _nPass + " WARN=" + _nWarn + " FAIL=" + _nFail + " INFO=" + _nInfo);
        _sb.AppendLine("scene dirtyBefore=" + dirtyBefore + " dirtyAfter=" + EditorSceneManager.GetActiveScene().isDirty + " (this script never marks the scene dirty and never saves)");
        if (expanded) _sb.AppendLine("temporary scales restored: " + restore.Count);
        if (_fails.Count == 0) _sb.AppendLine("No FAIL lines.");
        else
        {
            _sb.AppendLine("FAILS (" + _fails.Count + "):");
            foreach (var f in _fails) _sb.AppendLine("  " + f);
        }
        return _sb.ToString();
    }

    // ───────────────────────────── output helpers ─────────────────────────────

    static void Section(string title) { _sb.AppendLine(); _sb.AppendLine("=== " + title + " ==="); }

    static void Pass(string check, string path, string msg)
    {
        _nPass++;
        if (ListPassLines) _sb.AppendLine("PASS [" + check + "] " + path + ": " + msg);
    }

    static void Warn(string check, string path, string msg)
    {
        _nWarn++;
        _sb.AppendLine("WARN [" + check + "] " + path + ": " + msg);
    }

    static void Fail(string check, string path, string msg)
    {
        _nFail++;
        string line = "FAIL [" + check + "] " + path + ": " + msg;
        _sb.AppendLine(line);
        _fails.Add(line);
    }

    static void Info(string check, string path, string msg)
    {
        _nInfo++;
        _sb.AppendLine("INFO [" + check + "] " + path + ": " + msg);
    }

    static void ScriptError(string check, System.Exception ex)
    {
        Fail("script", check, "the check threw " + ex.GetType().Name + ": " + ex.Message + " (results for this check are incomplete)");
    }

    static string V(Vector3 v) { return "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + ")"; }
    static string Mm(float metres) { return (metres * 1000f).ToString("F1"); }

    static string PathOf(Transform t)
    {
        if (t == null) return "(null)";
        var parts = new List<string>();
        Transform p = t;
        int n = 0;
        while (p != null && n < PathDepth) { parts.Add(p.name); p = p.parent; n++; }
        parts.Reverse();
        return (p != null ? ".../" : "") + string.Join("/", parts.ToArray());
    }

    static string Short(string s, int max)
    {
        if (s == null) return "";
        s = s.Replace("\r", "").Replace("\n", "|");
        return s.Length > max ? s.Substring(0, max) + "~" : s;
    }

    // ───────────────────────────── small maths ─────────────────────────────

    static float MaxAbs(Vector3 v) { return Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z))); }
    static float MinAbs(Vector3 v) { return Mathf.Min(Mathf.Abs(v.x), Mathf.Min(Mathf.Abs(v.y), Mathf.Abs(v.z))); }
    static float MaxComp(Vector3 v) { return Mathf.Max(v.x, Mathf.Max(v.y, v.z)); }
    static Vector3 Abs(Vector3 v) { return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z)); }

    static bool Finite(Vector3 v)
    {
        return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                 float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }

    static Vector3 Corner(Bounds b, int i)
    {
        return b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
    }

    /// <summary>Ray or segment against an axis-aligned box. Returns the entry parameter (0 when the
    /// origin is inside) or -1 when there is no hit within [0, maxT].</summary>
    static float RayBox(Vector3 mn, Vector3 mx, Vector3 o, Vector3 d, float maxT)
    {
        float t0 = 0f, t1 = maxT;
        for (int i = 0; i < 3; i++)
        {
            float oi = o[i], di = d[i], a = mn[i], b = mx[i];
            if (Mathf.Abs(di) < 1e-12f)
            {
                if (oi < a || oi > b) return -1f;
            }
            else
            {
                float ta = (a - oi) / di, tb = (b - oi) / di;
                if (ta > tb) { float s = ta; ta = tb; tb = s; }
                if (ta > t0) t0 = ta;
                if (tb < t1) t1 = tb;
                if (t0 > t1) return -1f;
            }
        }
        return t0;
    }

    /// <summary>Nearest ray/triangle hit (both faces), or -1. All inputs in one space.</summary>
    static float RayTriangles(Vector3[] v, int[] tris, Vector3 o, Vector3 d, float maxT)
    {
        float best = -1f;
        double dLen2 = d.sqrMagnitude;
        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            double lim = 1e-10 * (double)e1.sqrMagnitude * (double)e2.sqrMagnitude * dLen2;
            if ((double)det * (double)det <= lim) continue;      // parallel or degenerate
            float inv = 1f / det;
            Vector3 s = o - a;
            float uu = Vector3.Dot(s, p) * inv;
            if (uu < 0f || uu > 1f) continue;
            Vector3 q = Vector3.Cross(s, e1);
            float vv = Vector3.Dot(d, q) * inv;
            if (vv < 0f || uu + vv > 1f) continue;
            float t = Vector3.Dot(e2, q) * inv;
            if (t < 0f || t > maxT) continue;
            if (best < 0f || t < best) best = t;
        }
        return best;
    }

    static MeshData GetMeshData(Mesh m)
    {
        MeshData d;
        if (_meshCache.TryGetValue(m, out d)) return d;
        d = null;
        try
        {
            // In the Editor vertex data can be read even when Read/Write is off on the import; if it
            // cannot, the arrays come back empty and the caller falls back to the bounds box.
            if (m.vertexCount > 0 && m.vertexCount <= MaxMeshVertsForTriangles)
            {
                d = new MeshData();
                d.v = m.vertices;
                d.t = m.triangles;
                if (d.v == null || d.t == null || d.v.Length == 0 || d.t.Length < 3) d = null;
            }
        }
        catch (System.Exception) { d = null; }
        _meshCache[m] = d;
        return d;
    }

    /// <summary>World-space ray against one mesh renderer. Returns the distance in metres to the first
    /// surface or -1. Uses triangles when the mesh is readable, the mesh bounds box otherwise.</summary>
    static float RayRenderer(MeshRenderer r, Vector3 o, Vector3 d, float maxT, out bool boundsOnly)
    {
        boundsOnly = false;
        var mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return -1f;
        Mesh mesh = mf.sharedMesh;
        Transform tr = r.transform;
        MeshData md = GetMeshData(mesh);

        if (md != null && md.v.Length <= 64)
        {
            // Small meshes (quads, boxes): test in world space, which also works for a zero scale axis.
            var wv = new Vector3[md.v.Length];
            for (int i = 0; i < wv.Length; i++) wv[i] = tr.TransformPoint(md.v[i]);
            return RayTriangles(wv, md.t, o, d, maxT);
        }

        if (MinAbs(tr.lossyScale) < 1e-7f) return -1f;
        Vector3 lo = tr.InverseTransformPoint(o);
        Vector3 ld = tr.InverseTransformVector(d);      // linear, so the parameter stays in world metres
        if (!Finite(lo) || !Finite(ld)) return -1f;
        if (md != null) return RayTriangles(md.v, md.t, lo, ld, maxT);

        boundsOnly = true;
        Bounds b = mesh.bounds;
        Vector3 pad = Vector3.one * (1e-5f * (b.size.magnitude + 1e-3f));
        return RayBox(b.min - pad, b.max + pad, lo, ld, maxT);
    }

    // ───────────────────────────── scene helpers ─────────────────────────────

    static Transform FindByName(string name)
    {
        Transform inactive = null;
        foreach (var t in _all)
        {
            if (t == null || t.name != name) continue;
            if (t.gameObject.activeInHierarchy) return t;
            if (inactive == null) inactive = t;
        }
        return inactive;
    }

    static MonoBehaviour GetBehaviour(Transform t, string typeName)
    {
        foreach (var mb in t.GetComponents<MonoBehaviour>())
            if (mb != null && mb.GetType().Name == typeName) return mb;
        return null;
    }

    static bool HasBehaviourInParents(Transform t, string typeName)
    {
        for (Transform p = t; p != null; p = p.parent)
            if (GetBehaviour(p, typeName) != null) return true;
        return false;
    }

    static Transform AncestorWith(Transform t, string typeName)
    {
        for (Transform p = t; p != null; p = p.parent)
            if (GetBehaviour(p, typeName) != null) return p;
        return null;
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name == name) return t;
        return null;
    }

    static bool NameHasPart(string name, string[] parts)
    {
        foreach (var p in parts)
            if (name.IndexOf(p, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>Top-most ancestor (or the transform itself unless skipSelf) that is scaled to near
    /// zero: the project's way of hiding Interaction SDK objects without deactivating them.</summary>
    static Transform CollapseRoot(Transform t, bool skipSelf)
    {
        Transform found = null;
        for (Transform p = skipSelf ? t.parent : t; p != null; p = p.parent)
        {
            if (MaxAbs(p.localScale) < HiddenLocalScale && MaxAbs(p.lossyScale) < HiddenLocalScale && p.GetComponent<TMP_Text>() == null)
                found = p;
        }
        return found;
    }

    static string HiddenReason(Transform t, bool skipSelfScale, bool lossyRule)
    {
        if (!t.gameObject.activeInHierarchy) return "inactive";
        Transform c = CollapseRoot(t, skipSelfScale);
        if (c != null) return "collapsed by scale at '" + c.name + "' (localScale " + MaxAbs(c.localScale).ToString("F4") + ")";
        if (lossyRule && MaxAbs(t.lossyScale) < MinLossyScale) return "lossy scale " + MaxAbs(t.lossyScale).ToString("F4") + " is below " + MinLossyScale.ToString("F2");
        return null;
    }

    static bool IsModelPart(Object o)
    {
        if (o == null) return false;
        try
        {
            Object src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(o);
            if (src == null) return false;
            string p = AssetDatabase.GetAssetPath(src);
            if (string.IsNullOrEmpty(p)) return false;
            p = p.ToLowerInvariant();
            foreach (var e in ModelExtensions) if (p.EndsWith(e)) return true;
            return false;
        }
        catch (System.Exception) { return false; }
    }

    // ───────────────────────────── RunExpanded support ─────────────────────────────

    static void ExpandCollapsed(List<KeyValuePair<Transform, Vector3>> restore)
    {
        Section("EXPANDED GROUPS (temporary, in memory, restored at the end)");
        var failedRoots = new List<Transform>();
        var museDesk = new List<MonoBehaviour>();
        foreach (var t in _all)
            foreach (var mb in t.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                string ns = mb.GetType().Namespace;
                if (ns != null && ns.StartsWith("MuseDesk")) museDesk.Add(mb);
            }

        int found = 0;
        foreach (var t in _all)
        {
            if (t == null || t.GetComponent<TMP_Text>() != null) continue;
            if (!(MaxAbs(t.localScale) < HiddenLocalScale && MaxAbs(t.lossyScale) < HiddenLocalScale)) continue;
            bool underFailed = false;
            foreach (var f in failedRoots) if (t != f && t.IsChildOf(f)) { underFailed = true; break; }
            if (underFailed) continue;
            found++;

            Vector3 scale; string source;
            if (FindAuthoredScale(t, museDesk, out scale, out source))
            {
                restore.Add(new KeyValuePair<Transform, Vector3>(t, t.localScale));
                t.localScale = scale;
                Info("expand", PathOf(t), "evaluated at authored scale " + V(scale) + " from " + source + (t.gameObject.activeInHierarchy ? "" : " (but it is inactive, so it is still skipped)"));
            }
            else
            {
                failedRoots.Add(t);
                Info("expand", PathOf(t), "collapsed (localScale " + MaxAbs(t.localScale).ToString("F5") + ") and no component stores an authored scale: could not evaluate; it stays in the hidden list");
            }
        }
        if (found == 0) _sb.AppendLine("no transform is collapsed by scale");
        _sb.AppendLine("note: inactive objects are not activated by this script; they stay in the hidden list");
    }

    static bool FindAuthoredScale(Transform root, List<MonoBehaviour> museDesk, out Vector3 scale, out string source)
    {
        scale = Vector3.one; source = null;
        var comps = new List<MonoBehaviour>();
        foreach (var mb in root.GetComponents<MonoBehaviour>()) if (mb != null) comps.Add(mb);
        if (root.parent != null)
            foreach (var mb in root.parent.GetComponents<MonoBehaviour>()) if (mb != null && !comps.Contains(mb)) comps.Add(mb);
        foreach (var mb in museDesk)
            if (!comps.Contains(mb) && ReferencesTransform(mb, root)) comps.Add(mb);

        foreach (var mb in comps)
        {
            string field;
            if (TryScaleField(mb, out scale, out field))
            {
                source = mb.GetType().Name + "." + field + " on '" + mb.name + "'";
                return true;
            }
        }
        return false;
    }

    static bool ReferencesTransform(MonoBehaviour mb, Transform target)
    {
        try
        {
            var so = new SerializedObject(mb);
            var it = so.GetIterator();
            bool enter = true;
            while (it.Next(enter))
            {
                enter = false;
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                Object v = it.objectReferenceValue;
                if (v == null) continue;
                if (v == target || v == target.gameObject) return true;
                var c = v as Component;
                if (c != null && c.transform == target) return true;
            }
        }
        catch (System.Exception) { }
        return false;
    }

    static readonly string[] ScaleYesWords = { "expand", "open", "full", "author", "shown", "show", "normal", "target", "rest", "visible", "large" };
    static readonly string[] ScaleNoWords = { "collaps", "hidden", "hide", "closed", "mini", "small", "held", "speed", "time", "duration", "seconds" };

    static bool TryScaleField(MonoBehaviour mb, out Vector3 scale, out string field)
    {
        scale = Vector3.one; field = null;
        try
        {
            var so = new SerializedObject(mb);
            var it = so.GetIterator();
            bool enter = true;
            while (it.Next(enter))
            {
                enter = false;
                string n = it.name.ToLowerInvariant();
                if (n.IndexOf("scale") < 0) continue;
                bool yes = false, no = false;
                foreach (var w in ScaleYesWords) if (n.IndexOf(w) >= 0) yes = true;
                foreach (var w in ScaleNoWords) if (n.IndexOf(w) >= 0) no = true;
                if (!yes || no) continue;
                Vector3 v;
                if (it.propertyType == SerializedPropertyType.Vector3) v = it.vector3Value;
                else if (it.propertyType == SerializedPropertyType.Float) v = Vector3.one * it.floatValue;
                else continue;
                float m = MaxAbs(v);
                if (m < 0.05f || m > BigScale) continue;
                scale = v; field = it.name;
                return true;
            }
        }
        catch (System.Exception) { }
        return false;
    }

    // ───────────────────────────── shared collection ─────────────────────────────

    static void BuildShared()
    {
        _solids = new List<SolidInfo>();
        _texts = new List<TextInfo>();
        _keys = new List<KeyInfo>();
        _groups = new List<Group>();
        _groupOf = new Dictionary<Transform, Group>();
        _bones = new HashSet<Transform>();

        // Rig bones.
        foreach (var t in _all)
        {
            var smr = t.GetComponent<SkinnedMeshRenderer>();
            if (smr == null) continue;
            if (smr.rootBone != null) _bones.Add(smr.rootBone);
            var bones = smr.bones;
            if (bones != null) foreach (var b in bones) if (b != null) _bones.Add(b);
        }

        // Solid renderers (visible, not text).
        foreach (var t in _all)
        {
            var r = t.GetComponent<Renderer>();
            if (r == null) continue;
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (t.GetComponent<TMP_Text>() != null || t.GetComponent<TMP_SubMesh>() != null) continue;
            if (HiddenReason(t, false, false) != null || !r.enabled) { _hiddenRenderers++; continue; }
            if (MaxComp(r.bounds.size) < MinRendererWorldSize) { _hiddenRenderers++; continue; }
            var si = new SolidInfo();
            si.r = r;
            si.mr = r as MeshRenderer;
            si.nonSurface = NameHasPart(t.name, NonSurfaceNameParts);
            _solids.Add(si);
        }

        // Poke keys.
        foreach (var t in _all)
        {
            var poke = GetBehaviour(t, PokeTypeName);
            if (poke == null) continue;
            try { _keys.Add(BuildKey(t, poke)); }
            catch (System.Exception ex) { Fail("script", PathOf(t), "could not measure this key: " + ex.GetType().Name + ": " + ex.Message); }
        }

        // Texts.
        foreach (var t in _all)
        {
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp == null) continue;
            try { _texts.Add(BuildText(tmp)); }
            catch (System.Exception ex) { Fail("script", PathOf(t), "could not measure this text: " + ex.GetType().Name + ": " + ex.Message); }
        }

        // Major groups (parents come before children in _all).
        foreach (var t in _all)
        {
            bool isKey = GetBehaviour(t, PokeTypeName) != null;
            string token = isKey ? "key" : GroupToken(t);
            if (token == null) continue;
            if (!isKey)
            {
                bool nested = false;
                for (Transform p = t.parent; p != null; p = p.parent)
                {
                    Group pg;
                    if (_groupOf.TryGetValue(p, out pg) && pg.token == token) { nested = true; break; }
                }
                if (nested) continue;
            }
            var g = new Group();
            g.root = t; g.token = token; g.isKey = isKey;
            g.label = isKey ? PathOf(t) : t.name + (t.parent != null && t.parent != _ws ? " (under " + t.parent.name + ")" : "");
            foreach (var o in OccluderTokens) if (o == token) g.occluder = true;
            _groups.Add(g);
            _groupOf[t] = g;
        }
        foreach (var si in _solids)
        {
            if (si.nonSurface) continue;
            Group g = null;
            for (Transform p = si.r.transform; p != null; p = p.parent)
                if (_groupOf.TryGetValue(p, out g)) break;
            if (g == null) continue;
            if (AccumulateInSpace(si.r, g.root, ref g.mn, ref g.mx)) { g.any = true; g.renderers++; }
        }
    }

    static string GroupToken(Transform t)
    {
        string n = t.name;
        foreach (var e in GroupExactNames) if (n == e) return e;
        foreach (var p in GroupPrefixNames) if (n.StartsWith(p)) return p;
        foreach (var c in GroupContainsNames) if (n.IndexOf(c, System.StringComparison.OrdinalIgnoreCase) >= 0) return c;
        return null;
    }

    /// <summary>Grow (mn, mx) by the renderer's box expressed in "space" local coordinates.</summary>
    static bool AccumulateInSpace(Renderer r, Transform space, ref Vector3 mn, ref Vector3 mx)
    {
        bool any = false;
        if (MinAbs(space.lossyScale) < 1e-7f) return false;
        var mf = r.GetComponent<MeshFilter>();
        if (r is MeshRenderer && mf != null && mf.sharedMesh != null)
        {
            Bounds b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 l = space.InverseTransformPoint(r.transform.TransformPoint(Corner(b, i)));
                if (!Finite(l)) continue;
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l); any = true;
            }
        }
        else
        {
            Bounds wb = r.bounds;     // skinned meshes: world box
            for (int i = 0; i < 8; i++)
            {
                Vector3 l = space.InverseTransformPoint(Corner(wb, i));
                if (!Finite(l)) continue;
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l); any = true;
            }
        }
        return any;
    }

    static Obb ToObb(Group g, float shrink)
    {
        var o = new Obb();
        Transform t = g.root;
        o.c = t.TransformPoint((g.mn + g.mx) * 0.5f);
        o.ax[0] = t.right; o.ax[1] = t.up; o.ax[2] = t.forward;
        o.h = Vector3.Scale((g.mx - g.mn) * 0.5f, Abs(t.lossyScale)) * shrink;
        return o;
    }

    /// <summary>Separating-axis test. Returns the smallest overlap (metres) or a value at most 0 when
    /// the boxes are separated.</summary>
    static float ObbPenetration(Obb a, Obb b)
    {
        Vector3 d = b.c - a.c;
        float min = float.MaxValue;
        for (int i = 0; i < 15; i++)
        {
            Vector3 l;
            if (i < 3) l = a.ax[i];
            else if (i < 6) l = b.ax[i - 3];
            else
            {
                int k = i - 6;
                l = Vector3.Cross(a.ax[k / 3], b.ax[k % 3]);
                if (l.sqrMagnitude < 1e-6f) continue;
                l.Normalize();
            }
            float ra = a.h.x * Mathf.Abs(Vector3.Dot(a.ax[0], l)) + a.h.y * Mathf.Abs(Vector3.Dot(a.ax[1], l)) + a.h.z * Mathf.Abs(Vector3.Dot(a.ax[2], l));
            float rb = b.h.x * Mathf.Abs(Vector3.Dot(b.ax[0], l)) + b.h.y * Mathf.Abs(Vector3.Dot(b.ax[1], l)) + b.h.z * Mathf.Abs(Vector3.Dot(b.ax[2], l));
            float o = ra + rb - Mathf.Abs(Vector3.Dot(d, l));
            if (o <= 0f) return o;
            if (o < min) min = o;
        }
        return min;
    }

    // ───────────────────────────── text ─────────────────────────────

    static TextInfo BuildText(TMP_Text tmp)
    {
        var ti = new TextInfo();
        ti.tmp = tmp; ti.tf = tmp.transform; ti.path = PathOf(tmp.transform);
        ti.hidden = HiddenReason(ti.tf, true, false);
        if (ti.hidden == null && !tmp.enabled) ti.hidden = "text component disabled";
        if (ti.hidden == null)
        {
            var r = ti.tf.GetComponent<Renderer>();
            if (r != null && !r.enabled) ti.hidden = "renderer disabled";
        }
        if (ti.hidden == null && tmp.color.a < 0.02f) ti.hidden = "alpha " + tmp.color.a.ToString("F2");
        ti.empty = string.IsNullOrEmpty(tmp.text) || tmp.text.Trim().Length == 0;

        if (ti.hidden == null)
        {
            try { tmp.ForceMeshUpdate(); ti.meshOk = true; }
            catch (System.Exception) { ti.meshOk = false; }
        }

        // 3D TextMeshPro: font size 10 = 1 unit. Canvas text (TextMeshProUGUI): font size 1 = 1 unit.
        float unit = (tmp is TextMeshProUGUI) ? 1f : 0.1f;
        float sy = Mathf.Abs(ti.tf.lossyScale.y);
        ti.emMm = tmp.fontSize * unit * sy * 1000f;
        ti.floorMm = tmp.enableAutoSizing ? tmp.fontSizeMin * unit * sy * 1000f : ti.emMm;
        if (ti.hidden == null && ti.emMm < HiddenTextEmMm)
            ti.hidden = "glyph height " + ti.emMm.ToString("F2") + " mm (collapsed or mis-scaled: lossy scale " + sy.ToString("F5") + ", font size " + tmp.fontSize.ToString("F1") + ")";

        Rect rc = tmp.rectTransform.rect;
        Vector3 lc = new Vector3(rc.center.x, rc.center.y, 0f);
        if (ti.meshOk && !ti.empty && tmp.textInfo != null && tmp.textInfo.characterCount > 0)
        {
            Bounds tb = tmp.textBounds;
            if (Finite(tb.center) && Finite(tb.size) && MaxAbs(tb.center) < 1e4f && MaxAbs(tb.size) < 1e4f && tb.size.x > 0f && tb.size.y > 0f)
            {
                lc = new Vector3(tb.center.x, tb.center.y, 0f);
                ti.hasChars = true;
            }
        }
        ti.centre = ti.tf.TransformPoint(lc);
        ti.eyeDist = Vector3.Distance(_eye, ti.centre);
        ti.billboard = HasBehaviourInParents(ti.tf, BillboardTypeName);
        ti.grabRoot = AncestorWith(ti.tf, GrabbableTypeName);
        return ti;
    }

    static string TextTag(TextInfo ti)
    {
        return "\"" + Short(ti.tmp.text, 26) + "\"";
    }

    static void CheckLegibility()
    {
        int n = 0;
        foreach (var ti in _texts)
        {
            if (ti.hidden != null) continue;
            n++;
            var tmp = ti.tmp;
            string nums = "em " + ti.emMm.ToString("F1") + " mm (font " + tmp.fontSize.ToString("F2") + ", lossy " + Mathf.Abs(ti.tf.lossyScale.y).ToString("F4") + ")"
                + (tmp.enableAutoSizing ? " auto-size floor " + ti.floorMm.ToString("F1") + " mm [" + tmp.fontSizeMin.ToString("F1") + ".." + tmp.fontSizeMax.ToString("F1") + "]" : "")
                + " dist " + ti.eyeDist.ToString("F2") + " m " + TextTag(ti);
            if (ti.empty) nums += " (empty in the Editor: size only)";

            if (ti.emMm < TextFailMm) Fail("1 legibility", ti.path, nums + " < " + TextFailMm.ToString("F1") + " mm");
            else if (ti.emMm < TextWarnMm) Warn("1 legibility", ti.path, nums + " < " + TextWarnMm.ToString("F1") + " mm");
            else if (ti.floorMm < TextFailMm) Warn("1 legibility", ti.path, nums + ": fine now, but auto-size may shrink it below " + TextFailMm.ToString("F1") + " mm with longer runtime text");
            else if (ti.floorMm < TextWarnMm) Warn("1 legibility", ti.path, nums + ": fine now, but auto-size may shrink it below " + TextWarnMm.ToString("F1") + " mm with longer runtime text");
            else if (ti.emMm > TextHugeMm) Warn("1 legibility", ti.path, nums + ": larger than " + TextHugeMm.ToString("F0") + " mm (wrong scale?)");
            else Pass("1 legibility", ti.path, nums);

            if (ti.eyeDist > AngularFromDistance)
            {
                float capM = ti.emMm * 0.001f * CapHeightRatio;
                float deg = 2f * Mathf.Atan2(capM * 0.5f, ti.eyeDist) * Mathf.Rad2Deg;
                string a = "capital height " + (capM * 1000f).ToString("F1") + " mm at " + ti.eyeDist.ToString("F2") + " m = " + deg.ToString("F2") + " deg";
                if (deg < AngularWarnDeg) Warn("1 angular", ti.path, a + " < " + AngularWarnDeg.ToString("F2") + " deg");
                else Pass("1 angular", ti.path, a);
            }
        }
        if (n == 0) _sb.AppendLine("no visible TextMeshPro text");
    }

    static void CheckOverflow()
    {
        foreach (var ti in _texts)
        {
            if (ti.hidden != null) continue;
            var tmp = ti.tmp;
            if (!ti.meshOk) { Info("2 overflow", ti.path, "ForceMeshUpdate failed: not measured"); continue; }
            if (ti.empty) { Info("2 overflow", ti.path, "empty in the Editor: not measured (runtime text may be longer than any placeholder)"); continue; }

            Rect rc = tmp.rectTransform.rect;
            Vector4 m = tmp.margin;      // left, top, right, bottom
            float xMin = rc.xMin + m.x, xMax = rc.xMax - m.z, yMin = rc.yMin + m.w, yMax = rc.yMax - m.y;
            Vector3 ls = Abs(ti.tf.lossyScale);
            float exX = 0f, exY = 0f;
            if (ti.hasChars)
            {
                Bounds tb = tmp.textBounds;
                exX = Mathf.Max(0f, Mathf.Max(xMin - tb.min.x, tb.max.x - xMax)) * ls.x * 1000f;
                exY = Mathf.Max(0f, Mathf.Max(yMin - tb.min.y, tb.max.y - yMax)) * ls.y * 1000f;
            }
            string nums = "rect " + (rc.width * ls.x * 1000f).ToString("F1") + " x " + (rc.height * ls.y * 1000f).ToString("F1") + " mm, overflowMode " + tmp.overflowMode
                + ", wrap " + tmp.textWrappingMode + ", lines " + tmp.textInfo.lineCount
                + ", past rect x " + exX.ToString("F1") + " mm y " + exY.ToString("F1") + " mm " + TextTag(ti);

            if (tmp.isTextOverflowing && !tmp.isTextTruncated) Fail("2 overflow", ti.path, "isTextOverflowing; " + nums);
            else if (Mathf.Max(exX, exY) > OverflowFailMm) Fail("2 overflow", ti.path, "rendered text extends past its rect; " + nums);
            else if (tmp.isTextTruncated) Warn("2 overflow", ti.path, "text is truncated (ellipsis or cut); " + nums);
            else if (tmp.isTextOverflowing) Warn("2 overflow", ti.path, "isTextOverflowing; " + nums);
            else if (Mathf.Max(exX, exY) > OverflowWarnMm) Warn("2 overflow", ti.path, "rendered text slightly past its rect; " + nums);
            else Pass("2 overflow", ti.path, nums);
        }
    }

    static bool Related(Transform text, Transform r)
    {
        if (text.IsChildOf(r)) return true;
        Transform p1 = text.parent;
        if (p1 == null) return false;
        if (r.IsChildOf(p1)) return true;
        Transform p2 = p1.parent;
        if (p2 != null && r.IsChildOf(p2)) return true;
        return false;
    }

    static void CheckDepth()
    {
        float maxT = DepthFrontWindow + DepthBehindWindow;
        foreach (var ti in _texts)
        {
            if (ti.hidden != null) continue;
            var tmp = ti.tmp;
            Vector3 n = -ti.tf.forward;                    // toward the reader
            Vector3 d = -n;                                // into the surface
            Vector3 o = ti.centre + n * DepthFrontWindow;

            MeshRenderer best = null; float bestGap = 0f; bool bestBounds = false;
            foreach (var si in _solids)
            {
                if (si.mr == null || si.nonSurface) continue;
                if (si.mr.transform.IsChildOf(ti.tf)) continue;
                Bounds wb = si.r.bounds;
                wb.Expand(0.004f);
                float bd;
                if (!wb.IntersectRay(new Ray(o, d), out bd)) continue;
                if (bd > maxT) continue;
                bool bo;
                float t = RayRenderer(si.mr, o, d, maxT, out bo);
                if (t < 0f) continue;
                float gap = t - DepthFrontWindow;
                if (best == null || Mathf.Abs(gap) < Mathf.Abs(bestGap)) { best = si.mr; bestGap = gap; bestBounds = bo; }
            }

            if (best == null)
            {
                Info("3 depth", ti.path, "no surface found within " + Mm(DepthFrontWindow) + " mm in front / " + Mm(DepthBehindWindow) + " mm behind the text centre (free-floating text) " + TextTag(ti));
                continue;
            }

            // Tilt: the same surface sampled at four more points of the label.
            float gMin = bestGap, gMax = bestGap; int hits = 1;
            Vector3 ext = Vector3.zero;
            if (ti.hasChars) ext = tmp.textBounds.extents;
            else { Rect rc = tmp.rectTransform.rect; ext = new Vector3(rc.width * 0.5f, rc.height * 0.5f, 0f); }
            for (int i = 0; i < 4; i++)
            {
                Vector3 off = new Vector3(((i & 1) == 0 ? -0.3f : 0.3f) * ext.x, ((i & 2) == 0 ? -0.3f : 0.3f) * ext.y, 0f);
                Vector3 p = ti.centre + ti.tf.TransformVector(off) + n * DepthFrontWindow;
                bool bo;
                float t = RayRenderer(best, p, d, maxT, out bo);
                if (t < 0f) continue;
                float g = t - DepthFrontWindow;
                hits++;
                if (g < gMin) gMin = g;
                if (g > gMax) gMax = g;
            }

            float gapMm = bestGap * 1000f;
            bool related = Related(ti.tf, best.transform);
            string nums = "gap " + gapMm.ToString("F2") + " mm to '" + best.name + "'" + (related ? "" : " (not in the text's own group: " + PathOf(best.transform) + ")")
                + (bestBounds ? " [mesh not readable: bounds box used]" : "")
                + ", across the label " + (gMin * 1000f).ToString("F2") + ".." + (gMax * 1000f).ToString("F2") + " mm (" + hits + " samples) " + TextTag(ti);
            float spreadMm = (gMax - gMin) * 1000f;

            if (gapMm < -DepthNegTolMm)
            {
                if (related) Fail("3 depth", ti.path, "text is BEHIND the surface; " + nums);
                else Warn("3 depth", ti.path, "an unrelated surface is in front of the text; " + nums);
            }
            else if (gapMm > DepthWarnMaxMm)
            {
                if (related) Fail("3 depth", ti.path, "text floats in front of its surface (limit " + DepthWarnMaxMm.ToString("F1") + " mm); " + nums);
                else Warn("3 depth", ti.path, "text floats; the nearest surface is an unrelated object; " + nums);
            }
            else if (gapMm > DepthPassMaxMm) Warn("3 depth", ti.path, "more than " + DepthPassMaxMm.ToString("F1") + " mm proud; " + nums);
            else if (spreadMm > DepthSpreadWarnMm) Warn("3 depth", ti.path, "text is not parallel to its surface or the surface is stepped (spread " + spreadMm.ToString("F2") + " mm); " + nums);
            else if (gapMm < 0.15f) Pass("3 depth", ti.path, nums + " (under 0.15 mm: watch for z-fighting on the headset)");
            else Pass("3 depth", ti.path, nums);
        }
    }

    static void CheckFacing()
    {
        foreach (var ti in _texts)
        {
            if (ti.hidden != null) continue;
            float ang = Vector3.Angle(-ti.tf.forward, _eye - ti.centre);
            string nums = "readable side is " + ang.ToString("F0") + " deg off the line to the eye " + TextTag(ti);
            if (ang > FacingFailDeg)
            {
                if (ti.billboard) Warn("4 facing", ti.path, nums + " (has a " + BillboardTypeName + " billboard: re-oriented at runtime, check on the headset)");
                else if (ti.grabRoot != null) Warn("4 facing", ti.path, nums + " (rides on grabbable '" + ti.grabRoot.name + "': orientation is set when held)");
                else Fail("4 facing", ti.path, nums + " > " + FacingFailDeg.ToString("F0") + " deg");
            }
            else Pass("4 facing", ti.path, nums);
        }
    }

    // ───────────────────────────── scale hazards ─────────────────────────────

    static bool ColliderWorldSize(Collider c, out Vector3 size)
    {
        Vector3 s = Abs(c.transform.lossyScale);
        var box = c as BoxCollider;
        if (box != null) { size = Vector3.Scale(box.size, s); return true; }
        var sph = c as SphereCollider;
        if (sph != null) { float dia = sph.radius * 2f * MaxComp(s); size = new Vector3(dia, dia, dia); return true; }
        var cap = c as CapsuleCollider;
        if (cap != null)
        {
            float dia = cap.radius * 2f * MaxComp(s);
            float h = Mathf.Max(cap.height * MaxComp(s), dia);
            size = new Vector3(dia, h, dia);
            return true;
        }
        size = c.bounds.size;
        return MaxComp(size) > 0f;
    }

    static void CheckScaleHazards()
    {
        int examined = 0, rigParts = 0, flagged = 0;
        foreach (var t in _all)
        {
            var r = t.GetComponent<Renderer>();
            var tmp = t.GetComponent<TMP_Text>();
            var cols = t.GetComponents<Collider>();
            if (r == null && tmp == null && (cols == null || cols.Length == 0)) continue;
            if (CollapseRoot(t, tmp != null) != null) continue;      // reported in the hidden section
            examined++;

            // Non-uniform scale on visible text.
            if (tmp != null && t.gameObject.activeInHierarchy)
            {
                Vector3 ls = Abs(t.lossyScale);
                float mxy = Mathf.Max(ls.x, ls.y);
                if (mxy > 1e-7f && Mathf.Abs(ls.x - ls.y) / mxy > TextNonUniformTol)
                {
                    flagged++;
                    Warn("5 scale", PathOf(t), "text has non-uniform lossy scale x " + ls.x.ToString("F5") + " y " + ls.y.ToString("F5") + " (glyphs are stretched)");
                }
            }

            float big = 0f, small = float.MaxValue;
            Transform bigT = null, smallT = null;
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                float s = MaxAbs(p.lossyScale);
                if (s > big) { big = s; bigT = p; }
                if (s < small) { small = s; smallT = p; }
            }
            bool hazardBig = big > BigScale;
            bool hazardSmall = small < SmallScale;
            if (!hazardBig && !hazardSmall) continue;

            bool goIsModel = IsModelPart(t.gameObject) || _bones.Contains(t);
            string state = t.gameObject.activeInHierarchy ? "" : " [inactive]";
            float own = MaxAbs(t.lossyScale);

            if (hazardBig)
            {
                string anc = "ancestor '" + bigT.name + "' has lossy scale " + big.ToString("F1") + (big > 50f && big < 200f ? " (rig armature scale)" : "");
                if (r != null && tmp == null)
                {
                    if (r is SkinnedMeshRenderer || goIsModel) rigParts++;
                    else
                    {
                        flagged++;
                        string nums = anc + "; own lossy scale " + own.ToString("F3") + ", world size " + V(r.bounds.size) + state;
                        if (own > BigScale && MaxComp(r.bounds.size) > RendererUnderRigFailSize) Fail("5 scale", PathOf(t), "renderer inside a scaled-up parent and not compensated: " + nums);
                        else Warn("5 scale", PathOf(t), "renderer inside a scaled-up parent (compensated, fragile: parent it to the agent root instead): " + nums);
                    }
                }
                if (tmp != null)
                {
                    flagged++;
                    float unit = (tmp is TextMeshProUGUI) ? 1f : 0.1f;
                    float em = tmp.fontSize * unit * Mathf.Abs(t.lossyScale.y) * 1000f;
                    string nums = anc + "; glyph height " + em.ToString("F1") + " mm" + state;
                    if (em > TextUnderRigFailMm) Fail("5 scale", PathOf(t), "text inside a scaled-up parent and not compensated: " + nums);
                    else Warn("5 scale", PathOf(t), "text inside a scaled-up parent (compensated, fragile): " + nums);
                }
                if (cols != null)
                    foreach (var c in cols)
                    {
                        if (c == null) continue;
                        if (IsModelPart(c)) { rigParts++; continue; }
                        Vector3 size;
                        bool ok = ColliderWorldSize(c, out size);
                        float m = ok ? MaxComp(size) : 0f;
                        string nums = c.GetType().Name + " " + anc + "; world size " + V(size) + " m, trigger=" + c.isTrigger + state;
                        flagged++;
                        if (m > ColliderFailWorldSize) Fail("5 scale", PathOf(t), "collider inside a scaled-up parent is " + m.ToString("F2") + " m across: " + nums);
                        else if (m > ColliderSaneWorldSize) Warn("5 scale", PathOf(t), "collider inside a scaled-up parent is large: " + nums);
                        else Info("5 scale", PathOf(t), "collider inside a scaled-up parent, world size plausible: " + nums);
                    }
            }
            else
            {
                if ((r is SkinnedMeshRenderer) || goIsModel) { rigParts++; continue; }
                flagged++;
                Warn("5 scale", PathOf(t), "ancestor '" + smallT.name + "' has lossy scale " + small.ToString("F4") + " (< " + SmallScale.ToString("F2") + "): sizes and font sizes under it are easy to get wrong; own lossy scale " + own.ToString("F4") + state);
            }
        }
        _sb.AppendLine("examined " + examined + " objects with a renderer, collider or text; " + rigParts + " rig/model parts under a scaled ancestor ignored; " + flagged + " reported");
        if (flagged == 0) Pass("5 scale", "(scene)", "nothing outside the rigs is parented under a transform scaled above " + BigScale.ToString("F0") + " or below " + SmallScale.ToString("F2"));
    }

    // ───────────────────────────── poke keys ─────────────────────────────

    /// <summary>Extent of all enabled meshes under "part" along (u, right, up) from "origin", metres.</summary>
    static bool PartExtents(Transform part, Vector3 origin, Vector3 u, Vector3 right, Vector3 up, float[] e)
    {
        // e: 0 front (max u), 1 back (min u), 2 rMin, 3 rMax, 4 upMin, 5 upMax
        e[0] = float.MinValue; e[1] = float.MaxValue; e[2] = float.MaxValue; e[3] = float.MinValue; e[4] = float.MaxValue; e[5] = float.MinValue;
        bool any = false;
        foreach (var mr in part.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == null || !mr.enabled || mr.GetComponent<TMP_Text>() != null || mr.GetComponent<TMP_SubMesh>() != null) continue;
            if (NameHasPart(mr.name, NonSurfaceNameParts)) continue;
            bool shown = true;
            for (Transform p = mr.transform; p != null && p != part.parent; p = p.parent)
                if (!p.gameObject.activeSelf) { shown = false; break; }
            if (!shown) continue;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Bounds b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 w = mr.transform.TransformPoint(Corner(b, i)) - origin;
                float du = Vector3.Dot(w, u), dr = Vector3.Dot(w, right), dup = Vector3.Dot(w, up);
                if (du > e[0]) e[0] = du;
                if (du < e[1]) e[1] = du;
                if (dr < e[2]) e[2] = dr;
                if (dr > e[3]) e[3] = dr;
                if (dup < e[4]) e[4] = dup;
                if (dup > e[5]) e[5] = dup;
                any = true;
            }
        }
        return any;
    }

    static KeyInfo BuildKey(Transform k, MonoBehaviour poke)
    {
        var ki = new KeyInfo();
        ki.key = k; ki.poke = poke; ki.path = PathOf(k);
        ki.hidden = HiddenReason(k, false, true);
        if (ki.hidden == null && !poke.enabled) ki.hidden = "PokeInteractable disabled";
        ki.visual = FindDeep(k, ButtonVisualName);
        ki.cap = FindDeep(k, CapName);
        ki.housing = FindDeep(k, HousingName);

        Transform baseTf = null;
        foreach (var mb in k.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != PokeVisualTypeName) continue;
            var so = new SerializedObject(mb);
            var p = so.FindProperty("_buttonBaseTransform");
            if (p != null && p.propertyType == SerializedPropertyType.ObjectReference) baseTf = p.objectReferenceValue as Transform;
            if (ki.visual == null) ki.visual = mb.transform;
            break;
        }
        ki.baseFromSdk = baseTf != null;
        if (baseTf == null) baseTf = k;       // the project's keys put the poke surface at key-local z = 0
        ki.baseTf = baseTf;
        ki.u = -baseTf.forward; ki.right = baseTf.right; ki.up = baseTf.up;

        // PokeInteractableVisual.Start: _maxOffsetAlongNormal = Dot(visual.position - base.position, -base.forward).
        // The visual then moves between that offset (rest) and 0 (finger at the surface plane).
        ki.travel = ki.visual != null ? Vector3.Dot(ki.visual.position - baseTf.position, ki.u) : 0f;

        var e = new float[6];
        if (ki.cap != null && PartExtents(ki.cap, baseTf.position, ki.u, ki.right, ki.up, e))
        {
            ki.capOk = true;
            ki.capFront = e[0]; ki.capBack = e[1];
            ki.halfW = (e[3] - e[2]) * 0.5f; ki.halfH = (e[5] - e[4]) * 0.5f;
            ki.faceCentre = baseTf.position + ki.right * ((e[2] + e[3]) * 0.5f) + ki.up * ((e[4] + e[5]) * 0.5f) + ki.u * e[0];
        }
        else
        {
            Transform f = ki.visual != null ? ki.visual : k;
            ki.faceApprox = true;
            ki.faceCentre = f.position;
            ki.halfW = Mathf.Abs(f.lossyScale.x) * 0.5f;      // assumes a unit quad face
            ki.halfH = Mathf.Abs(f.lossyScale.y) * 0.5f;
        }
        if (ki.housing != null && PartExtents(ki.housing, baseTf.position, ki.u, ki.right, ki.up, e))
        {
            ki.housingOk = true;
            ki.housingFront = e[0];
        }
        Transform gr = AncestorWith(k, GrabbableTypeName);
        ki.grabRoot = gr;
        ki.billboard = HasBehaviourInParents(k, BillboardTypeName);
        return ki;
    }

    static void CheckKeys()
    {
        int visible = 0;
        foreach (var ki in _keys)
        {
            if (ki.hidden != null) continue;
            visible++;
            string p = ki.path;

            // Parts.
            if (ki.visual == null) { Fail("6 key parts", p, "no '" + ButtonVisualName + "' / " + PokeVisualTypeName + " under the key: nothing moves when pressed"); }
            if (ki.cap == null || ki.housing == null)
                Warn("6 key parts", p, "missing " + (ki.cap == null ? CapName + " " : "") + (ki.housing == null ? HousingName : "") + ": flat key, rim test skipped");
            if (!ki.baseFromSdk) Info("6 key parts", p, "no " + PokeVisualTypeName + "._buttonBaseTransform found: the key's own transform is used as the surface plane");

            // Surface.
            try
            {
                var so = new SerializedObject(ki.poke);
                var sp = so.FindProperty("_surfacePatch");
                if (sp == null) Info("6 key surface", p, "PokeInteractable has no _surfacePatch property (SDK changed?)");
                else if (sp.objectReferenceValue == null) Fail("6 key surface", p, "PokeInteractable._surfacePatch is not assigned");
            }
            catch (System.Exception) { }

            // Travel.
            if (ki.visual != null)
            {
                float mm = ki.travel * 1000f;
                string nums = "travel " + mm.ToString("F1") + " mm (ButtonVisual rest local " + V(ki.visual.localPosition) + ", key lossy scale " + MaxAbs(ki.key.lossyScale).ToString("F3") + ")";
                if (mm < TravelFailMm) Fail("6 key travel", p, nums + ": under " + TravelFailMm.ToString("F0") + " mm or negative, the press is not visible");
                else if (mm < TravelMinMm || mm > TravelMaxMm) Warn("6 key travel", p, nums + ": outside " + TravelMinMm.ToString("F0") + ".." + TravelMaxMm.ToString("F0") + " mm");
                else Pass("6 key travel", p, nums);
            }

            // Cap against the housing rim at full press.
            if (ki.capOk && ki.housingOk && ki.visual != null)
            {
                bool rides = ki.cap.IsChildOf(ki.visual);
                float pressed = rides ? ki.capFront - ki.travel : ki.capFront;
                float margin = pressed - ki.housingFront;
                string nums = "cap face at rest " + Mm(ki.capFront) + " mm, at full press " + Mm(pressed) + " mm, housing rim " + Mm(ki.housingFront) + " mm (all in front of the poke surface plane), cap proud of rim by " + Mm(margin) + " mm when pressed";
                if (!rides) Warn("6 key rim", p, CapName + " is not under " + ButtonVisualName + ": the cap does not move with the press; " + nums);
                else if (margin < 0f) Fail("6 key rim", p, "the cap face goes BEHIND the housing rim at full press (the face vanishes); " + nums);
                else if (margin * 1000f < RimProudWarnMm) Warn("6 key rim", p, "the cap face is nearly flush with the rim at full press; " + nums);
                else Pass("6 key rim", p, nums);
                if (rides && ki.capBack > ki.housingFront + 0.0005f)
                    Warn("6 key rim", p, "at rest the back of the cap is " + Mm(ki.capBack - ki.housingFront) + " mm in front of the housing rim: the cap floats clear of its housing when seen from the side");
            }

            // Rest guard.
            if (ki.visual != null)
            {
                var guard = GetBehaviour(ki.visual, RestGuardTypeName);
                if (guard == null) Warn("6 key guard", p, "no " + RestGuardTypeName + " on " + ButtonVisualName + " (a cap can be left pressed in after an unusual release)");
                else
                {
                    var so = new SerializedObject(guard);
                    var pr = so.FindProperty("restLocalPosition");
                    var ph = so.FindProperty("hasAuthoredRest");
                    if (pr == null || ph == null) Info("6 key guard", p, RestGuardTypeName + " fields not found (script changed?)");
                    else if (!ph.boolValue) Pass("6 key guard", p, "hasAuthoredRest=false: the guard captures the rest pose at Awake");
                    else
                    {
                        Vector3 rest = pr.vector3Value;
                        float ps = ki.visual.parent != null ? MaxAbs(ki.visual.parent.lossyScale) : 1f;
                        float diffMm = (rest - ki.visual.localPosition).magnitude * ps * 1000f;
                        string nums = "restLocalPosition " + V(rest) + " vs authored " + V(ki.visual.localPosition) + " (" + diffMm.ToString("F2") + " mm apart)";
                        if (diffMm > RestGuardTolMm) Fail("6 key guard", p, "the guard will snap the cap to a different rest pose every frame; " + nums);
                        else Pass("6 key guard", p, nums);
                    }
                }
            }

            // Face size.
            {
                float w = ki.halfW * 2000f, h = ki.halfH * 2000f, small = Mathf.Min(w, h);
                string nums = "face " + w.ToString("F1") + " x " + h.ToString("F1") + " mm" + (ki.faceApprox ? " (no cap mesh: taken from the " + ButtonVisualName + " scale, approximate)" : "");
                if (small < KeyFaceFailMm) Fail("6 key size", p, nums + ": smaller side under " + KeyFaceFailMm.ToString("F0") + " mm");
                else if (small < KeyFaceWarnMm) Warn("6 key size", p, nums + ": smaller side under " + KeyFaceWarnMm.ToString("F0") + " mm");
                else Pass("6 key size", p, nums);
            }

            // Label.
            {
                TMP_Text label = null; int count = 0;
                foreach (var tmp in ki.key.GetComponentsInChildren<TMP_Text>(true))
                {
                    count++;
                    bool nonEmpty = !string.IsNullOrEmpty(tmp.text) && tmp.text.Trim().Length > 0;
                    if (label == null && tmp.gameObject.activeInHierarchy && nonEmpty) label = tmp;
                }
                if (count == 0 && ki.key.parent != null && ki.key.parent.name.StartsWith("Btn_"))
                    foreach (var tmp in ki.key.parent.GetComponentsInChildren<TMP_Text>(true))
                    {
                        count++;
                        bool nonEmpty = !string.IsNullOrEmpty(tmp.text) && tmp.text.Trim().Length > 0;
                        if (label == null && tmp.gameObject.activeInHierarchy && nonEmpty) label = tmp;
                    }
                if (count == 0) Fail("6 key label", p, "no TextMeshPro label on the key");
                else if (label == null) Fail("6 key label", p, count + " label object(s) but none is active with non-empty text");
                else
                {
                    string nums = "\"" + Short(label.text, 24) + "\" on " + (label.transform.parent != null ? label.transform.parent.name : "(root)");
                    if (ki.visual != null && !label.transform.IsChildOf(ki.visual))
                        Warn("6 key label", p, nums + ": the label is not under " + ButtonVisualName + ", so it does not ride the cap (the cap can cover it or leave it behind)");
                    else Pass("6 key label", p, nums);
                }
            }

            // Facing.
            {
                float ang = Vector3.Angle(ki.u, _eye - ki.faceCentre);
                string nums = "key normal is " + ang.ToString("F0") + " deg off the line to the eye";
                if (ang > KeyFacingMaxDeg)
                {
                    if (ki.billboard) Warn("6 key facing", p, nums + " (under a " + BillboardTypeName + " billboard: re-oriented at runtime)");
                    else if (ki.grabRoot != null) Warn("6 key facing", p, nums + " (rides on grabbable '" + ki.grabRoot.name + "': orientation is set when held)");
                    else Fail("6 key facing", p, nums + " > " + KeyFacingMaxDeg.ToString("F0") + " deg");
                }
                else Pass("6 key facing", p, nums);
            }
        }
        _sb.AppendLine("poke keys: " + _keys.Count + " in the scene, " + visible + " visible and measured");
    }

    // ───────────────────────────── reach ─────────────────────────────

    static void CheckReach()
    {
        foreach (var ki in _keys)
        {
            if (ki.hidden != null) continue;
            float dist = Vector3.Distance(_eye, ki.faceCentre);
            string nums = "key face " + dist.ToString("F3") + " m from the eye, root-local " + (_ws != null ? V(_ws.InverseTransformPoint(ki.faceCentre)) : V(ki.faceCentre));
            if (ki.grabRoot != null) { Info("7 reach", ki.path, nums + " (rides on grabbable '" + ki.grabRoot.name + "': judged with that object, it comes to the hand)"); continue; }
            if (dist > ReachFail) Fail("7 reach", ki.path, nums + " > " + ReachFail.ToString("F2") + " m: a seated user cannot press it");
            else if (dist > ReachWarn) Warn("7 reach", ki.path, nums + " > " + ReachWarn.ToString("F2") + " m: a stretch for something pressed often");
            else Pass("7 reach", ki.path, nums);
        }

        var grabs = new Dictionary<Transform, GrabInfo>();
        var order = new List<GrabInfo>();
        foreach (var t in _all)
        {
            foreach (var mb in t.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                bool match = false;
                foreach (var g in GrabTypeNames) if (g == n) { match = true; break; }
                if (!match) continue;
                Transform root = AncestorWith(t, GrabbableTypeName);
                if (root == null) root = t;
                GrabInfo gi;
                if (!grabs.TryGetValue(root, out gi)) { gi = new GrabInfo(); gi.root = root; grabs[root] = gi; order.Add(gi); }
                if (gi.types.IndexOf(n) < 0) gi.types += (gi.types.Length > 0 ? "+" : "") + n;
                if (n.StartsWith("Distance") && mb.enabled && mb.gameObject.activeInHierarchy) gi.distance = true;
            }
        }

        foreach (var gi in order)
        {
            string path = PathOf(gi.root);
            string hidden = HiddenReason(gi.root, false, false);
            if (hidden != null) { _hidden.Add("grabbable " + path + " [" + gi.types + "]: " + hidden); continue; }

            bool any = false; Bounds wb = new Bounds(gi.root.position, Vector3.zero);
            foreach (var si in _solids)
            {
                if (si.nonSurface || !si.r.transform.IsChildOf(gi.root)) continue;
                if (!any) { wb = si.r.bounds; any = true; } else wb.Encapsulate(si.r.bounds);
            }
            Vector3 centre = any ? wb.center : gi.root.position;
            float dist = Vector3.Distance(_eye, centre);
            float near = any ? Vector3.Distance(_eye, wb.ClosestPoint(_eye)) : dist;
            bool core = false;
            foreach (var c in CoreGrabNames) if (gi.root.name.StartsWith(c)) core = true;
            string nums = "centre " + dist.ToString("F3") + " m from the eye (nearest point " + near.ToString("F3") + " m) [" + gi.types + "]" + (core ? " core item" : " decor");

            // A hand grab needs a trigger collider on the grabbable.
            bool hasCollider = false, hasTrigger = false;
            foreach (var c in gi.root.GetComponentsInChildren<Collider>(false)) { hasCollider = true; if (c.isTrigger) hasTrigger = true; }
            if (!hasCollider) Fail("7 grab collider", path, "grabbable has no active collider: it cannot be grabbed");
            else if (!hasTrigger && core) Warn("7 grab collider", path, "core grabbable has no trigger collider (hand grab has needed one in this project)");

            if (dist > ReachFail)
            {
                if (gi.distance) Pass("7 reach", path, nums + ": beyond reach but has a distance grab");
                else if (core) Fail("7 reach", path, nums + " > " + ReachFail.ToString("F2") + " m and no distance grab");
                else Warn("7 reach", path, nums + " > " + ReachFail.ToString("F2") + " m and no distance grab (acceptable only because the flow never needs it)");
            }
            else if (dist > ReachWarn)
            {
                if (gi.distance) Pass("7 reach", path, nums + ": a stretch, but has a distance grab");
                else Warn("7 reach", path, nums + " > " + ReachWarn.ToString("F2") + " m");
            }
            else Pass("7 reach", path, nums);

            if (gi.distance && !core)
                Warn("7 distance grab", path, "distance grab on a non-core object (rule: only the paper, cards and stamps)");
        }
    }

    // ───────────────────────────── overlaps ─────────────────────────────

    static bool NameMatch(Group g, string s)
    {
        return g.root.name.StartsWith(s) || g.token == s;
    }

    static bool Allowed(Group a, Group b)
    {
        foreach (var pair in OverlapAllowList)
        {
            if (pair == null || pair.Length < 2) continue;
            if ((NameMatch(a, pair[0]) && NameMatch(b, pair[1])) || (NameMatch(a, pair[1]) && NameMatch(b, pair[0]))) return true;
        }
        return false;
    }

    static void CheckOverlaps()
    {
        var live = new List<Group>();
        var obbs = new List<Obb>();
        foreach (var g in _groups)
        {
            if (!g.any)
            {
                if (!g.isKey) _hidden.Add("group " + g.label + ": no visible solid renderer (" + (HiddenReason(g.root, false, false) ?? "renderers hidden or none") + ")");
                continue;
            }
            live.Add(g);
            obbs.Add(ToObb(g, 1f));
            Vector3 size = obbs[obbs.Count - 1].h * 2f;
            _sb.AppendLine("group " + g.label + ": centre(root-local) " + (_ws != null ? V(_ws.InverseTransformPoint(obbs[obbs.Count - 1].c)) : V(obbs[obbs.Count - 1].c)) + " size " + V(size) + " m, " + g.renderers + " renderers");
        }

        int pairs = 0, hits = 0;
        for (int i = 0; i < live.Count; i++)
            for (int j = i + 1; j < live.Count; j++)
            {
                Group a = live[i], b = live[j];
                if (a.isKey && b.isKey) continue;                                   // key pairs: spacing check below
                if (a.root.IsChildOf(b.root) || b.root.IsChildOf(a.root)) continue;  // parent-child
                pairs++;
                float pen = ObbPenetration(obbs[i], obbs[j]);
                if (pen <= 0f) continue;
                float mm = pen * 1000f;
                string nums = a.label + "  x  " + b.label + ": boxes intersect, penetration " + mm.ToString("F1") + " mm";
                if (Allowed(a, b)) { Info("8 overlap", "(pair)", nums + " (on the allow-list)"); continue; }
                hits++;
                if (mm > OverlapFailMm) Fail("8 overlap", "(pair)", nums);
                else if (mm > OverlapWarnMm) Warn("8 overlap", "(pair)", nums);
                else Info("8 overlap", "(pair)", nums + " (touching)");
            }
        if (hits == 0) Pass("8 overlap", "(scene)", pairs + " pairs of major groups tested, none intersect");

        // Spacing between the faces of distinct keys on the same panel.
        int close = 0, tested = 0;
        for (int i = 0; i < _keys.Count; i++)
            for (int j = i + 1; j < _keys.Count; j++)
            {
                KeyInfo a = _keys[i], b = _keys[j];
                if (a.hidden != null || b.hidden != null) continue;
                if (a.key.IsChildOf(b.key) || b.key.IsChildOf(a.key)) continue;
                Vector3 d = b.faceCentre - a.faceCentre;
                if (d.magnitude > 0.20f) continue;
                if (Vector3.Dot(a.u, b.u) < 0.9f) continue;                 // not on the same facing
                if (Mathf.Abs(Vector3.Dot(d, a.u)) > 0.03f) continue;       // not on the same plane
                tested++;
                float bw = Mathf.Abs(Vector3.Dot(b.right, a.right)) * b.halfW + Mathf.Abs(Vector3.Dot(b.up, a.right)) * b.halfH;
                float bh = Mathf.Abs(Vector3.Dot(b.right, a.up)) * b.halfW + Mathf.Abs(Vector3.Dot(b.up, a.up)) * b.halfH;
                float dx = Mathf.Abs(Vector3.Dot(d, a.right)) - (a.halfW + bw);
                float dy = Mathf.Abs(Vector3.Dot(d, a.up)) - (a.halfH + bh);
                float gap;
                if (dx > 0f && dy > 0f) gap = Mathf.Sqrt(dx * dx + dy * dy);
                else gap = Mathf.Max(dx, dy);
                float mm = gap * 1000f;
                string nums = a.path + "  |  " + b.path + ": gap between faces " + mm.ToString("F1") + " mm";
                if (mm < KeySpacingFailMm) { close++; Fail("8 key spacing", "(pair)", nums + (mm < 0f ? " (faces overlap)" : " < " + KeySpacingFailMm.ToString("F0") + " mm")); }
                else if (mm < KeySpacingWarnMm) { close++; Warn("8 key spacing", "(pair)", nums + " < " + KeySpacingWarnMm.ToString("F0") + " mm"); }
            }
        if (close == 0) Pass("8 key spacing", "(scene)", tested + " neighbouring key pairs tested, all at least " + KeySpacingWarnMm.ToString("F0") + " mm apart");
    }

    // ───────────────────────────── occlusion ─────────────────────────────

    static void OccludersFor(Transform target, Vector3 point, List<Group> occluders, List<string> hitNames)
    {
        hitNames.Clear();
        foreach (var g in occluders)
        {
            if (target.IsChildOf(g.root) || g.root.IsChildOf(target)) continue;
            if (MinAbs(g.root.lossyScale) < 1e-7f) continue;
            Vector3 p0 = g.root.InverseTransformPoint(_eye);
            Vector3 p1 = g.root.InverseTransformPoint(point);
            if (!Finite(p0) || !Finite(p1)) continue;
            Vector3 c = (g.mn + g.mx) * 0.5f, h = (g.mx - g.mn) * 0.5f * OccluderShrink;
            float t = RayBox(c - h, c + h, p0, p1 - p0, 1f);
            if (t >= 0f && t < 0.97f) hitNames.Add(g.label);
        }
    }

    static void CheckOcclusion()
    {
        var occluders = new List<Group>();
        foreach (var g in _groups) if (g.occluder && g.any) occluders.Add(g);
        if (occluders.Count == 0) { Info("9 occlusion", "(scene)", "no occluder groups found (agents, stations, factory, board)"); return; }
        var names = new List<string>();
        int warned = 0, tested = 0;
        foreach (var ki in _keys)
        {
            if (ki.hidden != null) continue;
            tested++;
            OccludersFor(ki.key, ki.faceCentre, occluders, names);
            if (names.Count > 0) { warned++; Warn("9 occlusion", ki.path, "the line from the eye to this key passes through the box of: " + string.Join(", ", names.ToArray())); }
        }
        foreach (var ti in _texts)
        {
            if (ti.hidden != null) continue;
            tested++;
            OccludersFor(ti.tf, ti.centre, occluders, names);
            if (names.Count > 0) { warned++; Warn("9 occlusion", ti.path, "the line from the eye to this text " + TextTag(ti) + " passes through the box of: " + string.Join(", ", names.ToArray())); }
        }
        if (warned == 0) Pass("9 occlusion", "(scene)", tested + " keys and texts tested against " + occluders.Count + " occluder boxes, none blocked");
        else _sb.AppendLine(tested + " keys and texts tested against " + occluders.Count + " occluder boxes (boxes, not exact shapes: confirm each warning in a capture from the eye point)");
    }

    // ───────────────────────────── card lanes ─────────────────────────────

    static void CheckCardLanes()
    {
        Transform board = FindByName(BoardName);
        if (board == null) { Info("10 lanes", BoardName, "not found: skipped"); return; }
        MonoBehaviour presenter = null;
        foreach (var mb in board.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.GetType().Name == BoardPresenterTypeName) { presenter = mb; break; }
        if (presenter == null)
            foreach (var t in _all)
            {
                presenter = GetBehaviour(t, BoardPresenterTypeName);
                if (presenter != null) break;
            }
        if (presenter == null) { Info("10 lanes", BoardName, BoardPresenterTypeName + " not found: skipped"); return; }

        var so = new SerializedObject(presenter);
        var pSpacing = so.FindProperty("cardSpacing");
        var pCols = so.FindProperty("columns");
        var pPrefab = so.FindProperty("cardPrefab");
        if (pSpacing == null || pCols == null) { Info("10 lanes", PathOf(presenter.transform), "cardSpacing / columns not found on " + BoardPresenterTypeName + " (script changed?): skipped"); return; }
        Vector2 spacing = pSpacing.vector2Value;
        int cols = Mathf.Max(1, pCols.intValue);

        Transform card = null;
        if (pPrefab != null && pPrefab.objectReferenceValue != null)
        {
            var c = pPrefab.objectReferenceValue as Component;
            if (c != null) card = c.transform;
            var go = pPrefab.objectReferenceValue as GameObject;
            if (go != null) card = go.transform;
        }
        if (card == null) card = FindByName(CardTemplateName);
        if (card == null) { Info("10 lanes", PathOf(board), "no card template (cardPrefab unassigned and no '" + CardTemplateName + "'): skipped"); return; }

        // Card footprint in the card's own units (the mesh is not centred on the pivot).
        Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity; bool any = false;
        foreach (var mr in card.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == null || !mr.enabled || mr.GetComponent<TMP_Text>() != null || mr.GetComponent<TMP_SubMesh>() != null) continue;
            if (NameHasPart(mr.name, NonSurfaceNameParts)) continue;
            bool shown = true;
            for (Transform p = mr.transform; p != null && p != card; p = p.parent)
                if (!p.gameObject.activeSelf) { shown = false; break; }
            if (!shown) continue;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Bounds b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 l = card.InverseTransformPoint(mr.transform.TransformPoint(Corner(b, i)));
                if (!Finite(l)) continue;
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l); any = true;
            }
        }
        if (!any) { Info("10 lanes", PathOf(card), "card template has no measurable mesh (collapsed or no renderers): skipped"); return; }

        // Regions.
        var regionNames = new[] { "backlogRegion", "workingRegion", "needsMeRegion", "completedRegion" };
        var regions = new List<Transform>();
        foreach (var rn in regionNames)
        {
            var pr = so.FindProperty(rn);
            Transform rt = pr != null ? pr.objectReferenceValue as Transform : null;
            if (rt == null) Warn("10 lanes", PathOf(presenter.transform), BoardPresenterTypeName + "." + rn + " is not assigned");
            else regions.Add(rt);
        }
        Transform region0 = regions.Count > 0 ? regions[0] : null;
        Vector3 rs = region0 != null ? region0.localScale : Vector3.one;      // region scale relative to its parent
        Transform regionParent = region0 != null && region0.parent != null ? region0.parent : board;

        // Cards are re-parented to a region with SetParent(parent, false): their localScale is kept.
        float cardW = (mx.x - mn.x) * Mathf.Abs(card.localScale.x) * Mathf.Abs(rs.x);
        float cardH = (mx.y - mn.y) * Mathf.Abs(card.localScale.y) * Mathf.Abs(rs.y);
        float pitchX = spacing.x * Mathf.Abs(rs.x), pitchY = spacing.y * Mathf.Abs(rs.y);
        float world = MaxAbs(regionParent.lossyScale);
        string unitNote = world < HiddenLocalScale
            ? " (board is collapsed: figures are in the regions' parent units, metres at scale 1)"
            : " (regions' parent world scale " + world.ToString("F3") + ")";
        _sb.AppendLine("card template '" + card.name + "' localScale " + card.localScale.x.ToString("F3") + ": footprint " + Mm(cardW) + " x " + Mm(cardH) + " mm; cardSpacing " + Mm(pitchX) + " x " + Mm(pitchY) + " mm; columns " + cols + unitNote);

        float rowGap = (pitchY - cardH) * 1000f;
        if (rowGap < 0f) Fail("10 lanes", PathOf(board), "cards are taller than the row pitch: rows overlap by " + (-rowGap).ToString("F1") + " mm (card " + Mm(cardH) + ", pitch " + Mm(pitchY) + ")");
        else if (rowGap < CardGapWarnMm) Warn("10 lanes", PathOf(board), "only " + rowGap.ToString("F1") + " mm between card rows (card " + Mm(cardH) + ", pitch " + Mm(pitchY) + ")");
        else Pass("10 lanes", PathOf(board), "row gap " + rowGap.ToString("F1") + " mm (card " + Mm(cardH) + ", pitch " + Mm(pitchY) + ")");

        if (cols > 1)
        {
            float colGap = (pitchX - cardW) * 1000f;
            if (colGap < 0f) Fail("10 lanes", PathOf(board), "cards are wider than the column pitch inside a region: overlap " + (-colGap).ToString("F1") + " mm");
            else if (colGap < CardGapWarnMm) Warn("10 lanes", PathOf(board), "only " + colGap.ToString("F1") + " mm between cards side by side in a region");
            else Pass("10 lanes", PathOf(board), "in-region column gap " + colGap.ToString("F1") + " mm");
        }

        // Lane to lane: region origins along the parent's x.
        var xs = new List<float>();
        var names = new List<string>();
        foreach (var rt in regions)
        {
            if (rt.parent != regionParent) { Info("10 lanes", PathOf(rt), "region has a different parent from the others: lane spacing not compared"); continue; }
            int at = 0;
            while (at < xs.Count && xs[at] < rt.localPosition.x) at++;
            xs.Insert(at, rt.localPosition.x);
            names.Insert(at, rt.name);
        }
        float laneContent = (cols - 1) * pitchX + cardW;
        for (int i = 0; i + 1 < xs.Count; i++)
        {
            float dx = xs[i + 1] - xs[i];
            float gap = (dx - laneContent) * 1000f;
            string nums = names[i] + " -> " + names[i + 1] + ": lane pitch " + Mm(dx) + " mm, cards need " + Mm(laneContent) + " mm, gap " + gap.ToString("F1") + " mm";
            if (gap < 0f) Fail("10 lanes", PathOf(board), "cards are wider than their lane and overlap the next lane; " + nums);
            else if (gap < CardGapWarnMm) Warn("10 lanes", PathOf(board), "cards nearly touch the next lane; " + nums);
            else Pass("10 lanes", PathOf(board), nums);
        }

        // Header blocks of the frame, when present: the lane the user sees.
        Transform frame = FindDeep(board, BoardFrameName);
        if (frame != null && MaxAbs(board.lossyScale) > 1e-6f)
        {
            foreach (var mr in frame.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!mr.name.StartsWith("Header_")) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 l = regionParent.InverseTransformPoint(mr.transform.TransformPoint(Corner(mf.sharedMesh.bounds, i)));
                    if (!Finite(l)) continue;
                    if (l.x < lo) lo = l.x;
                    if (l.x > hi) hi = l.x;
                }
                if (hi <= lo) continue;
                float over = (laneContent - (hi - lo)) * 1000f;
                string nums = mr.name + " is " + Mm(hi - lo) + " mm wide, cards need " + Mm(laneContent) + " mm";
                if (over > 2f) Warn("10 lanes", PathOf(mr.transform), "cards are wider than the column header by " + over.ToString("F1") + " mm; " + nums);
                else Pass("10 lanes", PathOf(mr.transform), nums);
            }
        }
    }

    // ───────────────────────────── counts ─────────────────────────────

    static void CheckCounts()
    {
        var idle = new List<string>();
        foreach (var ki in _keys) if (ki.hidden == null) idle.Add(ki.key.name);
        string list = string.Join(", ", idle.ToArray());
        if (idle.Count > MaxIdleKeys) Warn("11 counts", "(scene)", idle.Count + " poke keys visible at idle (target at most " + MaxIdleKeys + "): " + list);
        else Pass("11 counts", "(scene)", idle.Count + " poke keys visible at idle (target at most " + MaxIdleKeys + "): " + list);

        int renderers = 0, skinned = 0, texts = 0, missing = 0;
        var mats = new HashSet<Material>();
        foreach (var t in _all)
        {
            if (!t.gameObject.activeInHierarchy) continue;
            int m = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            if (m > 0) { missing += m; Fail("11 counts", PathOf(t), m + " missing script(s) on an active object"); }
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null && tmp.enabled) texts++;
            var r = t.GetComponent<Renderer>();
            if (r == null || !r.enabled) continue;
            renderers++;
            if (r is SkinnedMeshRenderer) skinned++;
            foreach (var mat in r.sharedMaterials) if (mat != null) mats.Add(mat);
        }
        int transparent = 0, textMats = 0;
        var transparentNames = new List<string>();
        foreach (var mat in mats)
        {
            bool isText = mat.shader != null && mat.shader.name.IndexOf("TextMeshPro") >= 0;
            if (isText) { textMats++; continue; }
            bool tr = mat.renderQueue > 2500 || (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f);
            if (tr) { transparent++; transparentNames.Add(mat.name); }
        }
        _sb.AppendLine("active renderers=" + renderers + " (skinned " + skinned + ", solid measured " + _solids.Count + ", hidden or tiny skipped " + _hiddenRenderers + ")");
        _sb.AppendLine("materials=" + mats.Count + " (text materials " + textMats + "); transparent non-text materials=" + transparent + (transparent > 0 ? ": " + string.Join(", ", transparentNames.ToArray()) : ""));
        _sb.AppendLine("active TextMeshPro texts=" + texts + "; missing scripts on active objects=" + missing);
        if (missing == 0) Pass("11 counts", "(scene)", "no missing scripts on active objects");
    }

    // ───────────────────────────── hidden ─────────────────────────────

    static void ReportHidden(bool expanded)
    {
        // Interaction SDK interactables that are inactive at start-up do not register unless a
        // ReRegisterInteractable lists them.
        var listed = new HashSet<Object>();
        foreach (var t in _all)
        {
            var rr = GetBehaviour(t, ReRegisterTypeName);
            if (rr == null) continue;
            var so = new SerializedObject(rr);
            var arr = so.FindProperty("interactables");
            if (arr == null || !arr.isArray) continue;
            for (int i = 0; i < arr.arraySize; i++)
            {
                Object o = arr.GetArrayElementAtIndex(i).objectReferenceValue;
                if (o != null) listed.Add(o);
            }
        }

        int nKeys = 0;
        foreach (var ki in _keys)
        {
            if (ki.hidden == null) continue;
            nKeys++;
            _sb.AppendLine("key " + ki.path + ": " + ki.hidden);
            if (!ki.key.gameObject.activeInHierarchy && !listed.Contains(ki.poke))
                Warn("hidden", ki.path, "PokeInteractable is inactive in the scene and not listed in any " + ReRegisterTypeName + ": it may not register when shown (hide by scale instead, or list it)");
        }
        foreach (var t in _all)
        {
            if (t.gameObject.activeInHierarchy) continue;
            foreach (var mb in t.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (n == PokeTypeName || !n.EndsWith("Interactable")) continue;
                if (!listed.Contains(mb))
                    Warn("hidden", PathOf(t), n + " is inactive in the scene and not listed in any " + ReRegisterTypeName + ": it may not register when shown");
            }
        }

        int nTexts = 0;
        var byReason = new Dictionary<string, int>();
        foreach (var ti in _texts)
        {
            if (ti.hidden == null) continue;
            nTexts++;
            _sb.AppendLine("text " + ti.path + " " + TextTag(ti) + ": " + ti.hidden);
            string key = ti.hidden.StartsWith("collapsed") ? "collapsed by scale" : (ti.hidden.StartsWith("glyph") ? "glyphs under 1 mm" : ti.hidden);
            int c; byReason.TryGetValue(key, out c); byReason[key] = c + 1;
        }
        foreach (var h in _hidden) _sb.AppendLine(h);

        var sum = new StringBuilder();
        foreach (var kv in byReason) sum.Append(kv.Key + "=" + kv.Value + " ");
        _sb.AppendLine("hidden totals: keys " + nKeys + ", texts " + nTexts + " (" + sum.ToString().Trim() + "), renderers " + _hiddenRenderers + ", other " + _hidden.Count);
        if (!expanded) _sb.AppendLine("run XrVisualQa.RunExpanded to measure groups that are collapsed by scale at their authored size");
    }
}
