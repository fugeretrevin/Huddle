using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using Oculus.Interaction;
using MuseDesk.Spatial;
using MuseDesk.Workspace;

/// <summary>
/// The mode console (owner feedback: the Floor / Ceiling keys looked cheap, stacked behind each other
/// at angles). Builds ONE object "ModeConsole" under Spatial/NearUserRig:
///
///   a low wedge (12 degrees toward the user, Ink body, Paper inlay, fine teal edge line), generated
///   here and saved under Assets/MuseDesk/Art/Generated;
///   the Focus key (the existing FocusToggle, re-seated on the console, same size);
///   a segmented selector Desk | Floor | Ceiling: three equal wide keys in one dark well, an icon
///   above a bold word on each, a teal bar under the current place;
///   a context row under it: Next / Confirm / Cancel, one wide amber Stop, Lower / Raise, Bring,
///   Status, each saved hidden by scale and shown by ModeConsole only when it applies;
///   a raised lip at the far edge with the one-line status readout;
///   an empty child "MicSlot" (24 x 24 mm) at the right end for the voice agent's indicator.
///
/// Keys are clones of FocusToggle (same Cap3D / Housing3D / ButtonVisual / PokeVisualRestGuard /
/// KeyStateVisual stack) at one uniform scale, with wide cap and housing meshes generated with the
/// Kit builder's recipe (the square key's bevel, wall and depth, widened in x only).
///
/// The old loose keys under Spatial/SpatialModeKeys and Spatial/SpatialReach/SummonKeys are
/// deactivated and renamed with an "_old" suffix (nothing is destroyed). SpatialModeStrip stays the
/// director's strip and forwards to the console; SummonController keeps taking the Bring / Status
/// presses. Both feature flags are left as they are. Idempotent; saves the scene once.
///
///   unity command script run --file AgentScripts/BuildModeConsoleV1.cs --entry BuildModeConsoleV1.Apply
///   (BuildModeConsoleV1.Revert puts the old keys and the Focus key back and deactivates the console.)
/// Run BEFORE AgentScripts/BuildMovablePanelsV1.cs, which gives the console its grab bar.
/// </summary>
public static class BuildModeConsoleV1
{
    const string GenDir = "Assets/MuseDesk/Art/Generated";
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI";
    const float Hidden = 0.001f;                                    // ScaleEase.HiddenFactor
    static readonly Vector3 Eye = new Vector3(0f, 0.30f, 0.62f);    // seated eye, root-local

    // Design numbers (metres).
    const float TiltDeg = 12f;
    const float CapHeight = 0.023f;          // key cap height (>= 22 mm)
    const float SegCapWidth = 0.036f;        // place key cap width (>= 34 mm)
    const float Gap = 0.006f;                // between two housings in a row
    const float Margin = 0.008f;             // console edge to the first well
    const float WellPad = 0.003f;            // well around a row of housings
    const float IndicatorDrop = 0.005f;      // extra well below the place keys, for the indicator bars
    const float RowGap = 0.004f;             // between the two wells
    const float LipHeight = 0.013f, LipDepth = 0.004f, LipTiltDeg = 35f;
    const float MicSize = 0.024f;
    const float GroupGap = 0.008f;           // Focus | selector | mic
    const float NearThickness = 0.008f;      // body thickness at the near edge
    const float RimProud = 0.0016f;          // housing rim above the body top
    const float CornerRadius = 0.009f, Bevel = 0.0015f;
    const float ReachTarget = 0.50f;
    const int CornerSegs = 6, BevelSegs = 4;

    static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
    static readonly Color Paper = Hex("#F7F2E3"), Ink = Hex("#1F2530"), Plate = Hex("#1B1F27"),
                          OnPlate = Hex("#F3F4F6"), Teal = Hex("#2FA39A"), Amber = Hex("#E0A030");

    // ───────────────────────────── scene helpers ─────────────────────────────

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    static Transform Find(List<Transform> all, string name) => all.FirstOrDefault(t => t != null && t.name == name);
    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t != root && t.name == name);
    static string P(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
    static string Mm(float m) => (m * 1000f).ToString("F1");
    static float Safe(float v) => Mathf.Abs(v) < 1e-9f ? 1e-9f : v;

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    static Material Mat(string name, Color c, bool lit, Material copyFrom = null)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = copyFrom != null ? new Material(copyFrom)
                : new Material(Shader.Find(lit ? "Universal Render Pipeline/Simple Lit" : "Universal Render Pipeline/Unlit"));
            m.name = name;
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        m.SetColor("_Color", c);
        if (lit && copyFrom == null && m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.3f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Existing(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

    static Transform Holder(Transform parent, string name, int layer)
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
        return t;
    }

    static Transform Shape(Transform parent, string name, Mesh mesh, Material mat, Vector3 lp, Quaternion lr)
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
        t.gameObject.SetActive(true);
        t.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = t.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        t.localPosition = lp; t.localRotation = lr; t.localScale = Vector3.one;
        EditorUtility.SetDirty(t.gameObject);
        return t;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    /// <summary>Bounds of a part's own mesh in another transform's local space.</summary>
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

    /// <summary>
    /// Bounds of everything under <paramref name="t"/> in <paramref name="space"/>, as the user sees
    /// it when shown: an ancestor that is hidden by scale (0.001) counts at its full size.
    /// </summary>
    static bool ShownBounds(Transform t, Transform space, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        bool any = false;
        foreach (var f in t.GetComponentsInChildren<MeshFilter>(true))
        {
            if (f.sharedMesh == null || f.GetComponent<TMP_Text>() != null) continue;
            var b = f.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 w = f.transform.TransformPoint(c);
                for (Transform a = f.transform; a != null; a = a.parent)
                {
                    Vector3 s = a.localScale;
                    if (Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))) < 0.005f)
                        w = a.position + (w - a.position) * 1000f;
                    if (a == t) break;
                }
                var l = space.InverseTransformPoint(w);
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
                any = true;
            }
        }
        return any;
    }

    /// <summary>True when t and every parent below <paramref name="root"/> is switched on (the root itself may be saved hidden).</summary>
    static bool ActiveBelow(Transform t, Transform root)
    {
        for (Transform a = t; a != null && a != root; a = a.parent)
            if (!a.gameObject.activeSelf) return false;
        return true;
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

    static Transform CloneKey(Transform parent, Transform template, string name, out bool created)
    {
        created = false;
        var k = parent.Find(name);
        if (k == null)
        {
            var go = Object.Instantiate(template.gameObject, parent);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.name = name;
            k = go.transform;
            created = true;
            var face = go.GetComponent<FaceUser>();
            if (face != null) Object.DestroyImmediate(face);
        }
        k.gameObject.layer = parent.gameObject.layer;
        k.gameObject.SetActive(true);
        return k;
    }

    static Transform SurfaceBase(Transform key)
    {
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "PokeInteractableVisual") continue;
            var p = new SerializedObject(mb).FindProperty("_buttonBaseTransform");
            if (p != null && p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue is Transform)
                return (Transform)p.objectReferenceValue;
        }
        return key;
    }

    // ───────────────────────────── mesh building (the Kit builder's ring recipe) ─────────────────────────────

    class MB
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> tris = new List<int>();

        public int Add(Vector3 p, Vector3 normal, Vector2 t) { v.Add(p); n.Add(normal); uv.Add(t); return v.Count - 1; }

        /// <summary>Adds a triangle wound so its face normal agrees with its vertex normals; drops zero-area ones.</summary>
        public void Tri(int a, int b, int c)
        {
            Vector3 g = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (g.sqrMagnitude < 1e-20f) return;
            if (Vector3.Dot(g, n[a] + n[b] + n[c]) < 0f) { int t = b; b = c; c = t; }
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        public void Band(int[] a, int[] b)
        {
            int count = a.Length;
            for (int j = 0; j < count; j++)
            {
                int k = (j + 1) % count;
                Tri(a[j], a[k], b[k]);
                Tri(a[j], b[k], b[j]);
            }
        }

        public void Fan(int centre, int[] ring)
        {
            for (int j = 0; j < ring.Length; j++) Tri(centre, ring[j], ring[(j + 1) % ring.Length]);
        }

        public void Fill(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = 1;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }
    }

    /// <summary>
    /// One closed loop around a rounded rectangle centred on (cx, cy), half sizes (hw, hh), corner
    /// radius r, at depth z. Normal = (dir.x * nxy, dir.y * nxy, nz) normalised: nxy 1 / nz 0 is an
    /// outer wall, nxy -1 an inner wall, nxy 0 / nz -1 a face toward the reader (-Z).
    /// </summary>
    static int[] Ring(MB mb, float cx, float cy, float hw, float hh, float r, float z, float nxy, float nz, float uvW, float uvH)
    {
        var idx = new int[4 * (CornerSegs + 1)];
        int k = 0;
        hw = Mathf.Max(hw, 0f); hh = Mathf.Max(hh, 0f);
        float cr = Mathf.Clamp(r, 0f, Mathf.Min(hw, hh));
        for (int c = 0; c < 4; c++)
        {
            float sx = (c == 0 || c == 3) ? 1f : -1f;
            float sy = c < 2 ? 1f : -1f;
            for (int s = 0; s <= CornerSegs; s++)
            {
                float a = (c * 90f + 90f * s / CornerSegs) * Mathf.Deg2Rad;
                float dx = Mathf.Cos(a), dy = Mathf.Sin(a);
                if (s == 0 || s == CornerSegs) { dx = Mathf.Round(dx); dy = Mathf.Round(dy); }
                var p = new Vector3(cx + sx * (hw - cr) + cr * dx, cy + sy * (hh - cr) + cr * dy, z);
                var normal = new Vector3(dx * nxy, dy * nxy, nz).normalized;
                var t = new Vector2((p.x - cx) / Safe(uvW) + 0.5f, (p.y - cy) / Safe(uvH) + 0.5f);
                idx[k++] = mb.Add(p, normal, t);
            }
        }
        return idx;
    }

    static Mesh Store(string name, MB mb)
    {
        string path = GenDir + "/" + name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool isNew = mesh == null;
        if (isNew) mesh = new Mesh { name = name };
        mb.Fill(mesh);
        if (isNew) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>A flat rounded rectangle facing -Z.</summary>
    static void FlatPlate(MB mb, float cx, float cy, float hw, float hh, float r, float z)
    {
        int centre = mb.Add(new Vector3(cx, cy, z), Vector3.back, new Vector2(0.5f, 0.5f));
        mb.Fan(centre, Ring(mb, cx, cy, hw, hh, r, z, 0f, -1f, hw * 2f, hh * 2f));
    }

    /// <summary>A flat rounded outline of width t facing -Z.</summary>
    static void FlatOutline(MB mb, float hw, float hh, float r, float t, float z)
    {
        mb.Band(Ring(mb, 0f, 0f, hw, hh, r, z, 0f, -1f, hw * 2f, hh * 2f),
                Ring(mb, 0f, 0f, hw - t, hh - t, Mathf.Max(0f, r - t), z, 0f, -1f, hw * 2f, hh * 2f));
    }

    /// <summary>
    /// A rounded slab: flat front face at zFront (toward the reader), a quarter-round bevel, side
    /// walls back to a plane whose depth may vary with y (zBack0 at y = cy - hh, rising by slope per
    /// metre of y), and a closed back.
    /// </summary>
    static void Slab(MB mb, float cx, float cy, float hw, float hh, float r, float b, float zFront, float zBack0, float slope)
    {
        float uw = hw * 2f, uh = hh * 2f;
        int centre = mb.Add(new Vector3(cx, cy, zFront), Vector3.back, new Vector2(0.5f, 0.5f));
        mb.Fan(centre, Ring(mb, cx, cy, hw - b, hh - b, r - b, zFront, 0f, -1f, uw, uh));
        int[] prev = null;
        for (int i = 0; i <= BevelSegs; i++)
        {
            float phi = i * Mathf.PI * 0.5f / BevelSegs;
            float d = b * (1f - Mathf.Sin(phi));
            float nxy = Mathf.Sin(phi), nz = -Mathf.Cos(phi);
            if (i == BevelSegs) { d = 0f; nxy = 1f; nz = 0f; }
            int[] ring = Ring(mb, cx, cy, hw - d, hh - d, r - d, zFront + b * (1f - Mathf.Cos(phi)), nxy, nz, uw, uh);
            if (prev != null) mb.Band(prev, ring);
            prev = ring;
        }
        int[] wall = Ring(mb, cx, cy, hw, hh, r, 0f, 1f, 0f, uw, uh);
        int[] back = Ring(mb, cx, cy, hw, hh, r, 0f, 0f, 1f, uw, uh);
        foreach (int i in wall) { Vector3 p = mb.v[i]; p.z = zBack0 + (p.y - (cy - hh)) * slope; mb.v[i] = p; }
        foreach (int i in back) { Vector3 p = mb.v[i]; p.z = zBack0 + (p.y - (cy - hh)) * slope; mb.v[i] = p; }
        mb.Band(prev, wall);
        int bc = mb.Add(new Vector3(cx, cy, zBack0 + hh * slope), Vector3.forward, new Vector2(0.5f, 0.5f));
        mb.Fan(bc, back);
    }

    /// <summary>Flat rectangles (cx, cy, w, h in a unit square centred on the origin) facing -Z: an icon.</summary>
    static Mesh Icon(string name, params float[] rects)
    {
        var mb = new MB();
        for (int i = 0; i + 3 < rects.Length; i += 4)
        {
            float cx = rects[i], cy = rects[i + 1], hw = rects[i + 2] * 0.5f, hh = rects[i + 3] * 0.5f;
            int a = mb.Add(new Vector3(cx - hw, cy - hh, 0f), Vector3.back, new Vector2(0f, 0f));
            int b = mb.Add(new Vector3(cx + hw, cy - hh, 0f), Vector3.back, new Vector2(1f, 0f));
            int c = mb.Add(new Vector3(cx + hw, cy + hh, 0f), Vector3.back, new Vector2(1f, 1f));
            int d = mb.Add(new Vector3(cx - hw, cy + hh, 0f), Vector3.back, new Vector2(0f, 1f));
            mb.Tri(a, b, c); mb.Tri(a, c, d);
        }
        return Store(name, mb);
    }

    // ───────────────────────────── the square key, measured (as BuildKitV3.MeasureSquare) ─────────────────────────────

    class Square
    {
        public bool ok; public string why = "";
        public Vector3 capMin, capMax, houMin, houMax;
        public float bevel, wall, chamfer, backPlate;
    }

    static Square Measure(Mesh cap, Mesh hou)
    {
        var sq = new Square();
        if (cap == null || hou == null) { sq.why = "the template key has no cap or housing mesh"; return sq; }
        sq.capMin = cap.bounds.min; sq.capMax = cap.bounds.max;
        sq.houMin = hou.bounds.min; sq.houMax = hou.bounds.max;
        Vector3 cs = cap.bounds.size, hs = hou.bounds.size;
        if (!(cs.z < cs.x && cs.z < cs.y) || !(hs.z < hs.x && hs.z < hs.y))
        {
            sq.why = "the template key meshes do not have z as their depth axis";
            return sq;
        }
        float k = hs.y / 1.20f;
        sq.bevel = 0.065f * k;
        try
        {
            float flat = 0f; Vector3 c = cap.bounds.center;
            foreach (var p in cap.vertices)
                if (p.z < sq.capMin.z + 1e-4f * cs.z) flat = Mathf.Max(flat, Mathf.Abs(p.y - c.y));
            float b = cs.y * 0.5f - flat;
            if (flat > 0f && b > 0.01f * cs.y && b < 0.3f * cs.y) sq.bevel = b;
        }
        catch (System.Exception) { }
        sq.wall = 0.095f * k;
        try
        {
            // The front half of the housing is only walls; the top and bottom walls' inner faces are
            // the vertices nearest the centre line in y (measured in y so a wide template also works).
            float inner = float.MaxValue; Vector3 c = hou.bounds.center;
            float zMid = sq.houMin.z + 0.5f * hs.z;
            foreach (var p in hou.vertices)
                if (p.z < zMid && Mathf.Abs(p.x - c.x) < 0.25f * hs.x) inner = Mathf.Min(inner, Mathf.Abs(p.y - c.y));
            float w = hs.y * 0.5f - inner;
            if (inner < float.MaxValue && w > 0.03f * hs.y && w < 0.25f * hs.y) sq.wall = w;
        }
        catch (System.Exception) { }
        sq.chamfer = Mathf.Min(0.018f * k, sq.wall / 3f);
        sq.backPlate = 0.045f * k;
        sq.ok = true;
        return sq;
    }

    /// <summary>The template cap widened by extra in x only (BuildKitV3.BuildWideCap).</summary>
    static void WideCap(MB mb, Square sq, float extra)
    {
        Vector3 c = (sq.capMin + sq.capMax) * 0.5f;
        float hw = (sq.capMax.x - sq.capMin.x) * 0.5f + extra * 0.5f, hh = (sq.capMax.y - sq.capMin.y) * 0.5f;
        float zF = sq.capMin.z, zB = sq.capMax.z;
        float b = Mathf.Min(sq.bevel, Mathf.Min(hh * 0.9f, (zB - zF) * 0.45f));
        Slab(mb, c.x, c.y, hw, hh, b, b, zF, zB, 0f);
    }

    /// <summary>The template housing frame widened by extra in x only (BuildKitV3.BuildWideHousing).</summary>
    static void WideHousing(MB mb, Square sq, float extra)
    {
        Vector3 c = (sq.houMin + sq.houMax) * 0.5f;
        float HW = (sq.houMax.x - sq.houMin.x) * 0.5f + extra * 0.5f, HH = (sq.houMax.y - sq.houMin.y) * 0.5f;
        float zF = sq.houMin.z, zB = sq.houMax.z;
        float t = sq.wall, ch = sq.chamfer;
        float iw = HW - t, ih = HH - t;
        float zFloor = Mathf.Max(zF + 2f * ch, zB - sq.backPlate);
        float uw = HW * 2f, uh = HH * 2f;
        const float k = 0.70710678f;
        mb.Band(Ring(mb, c.x, c.y, HW, HH, ch, zB, 1f, 0f, uw, uh), Ring(mb, c.x, c.y, HW, HH, ch, zF + ch, 1f, 0f, uw, uh));
        mb.Band(Ring(mb, c.x, c.y, HW, HH, ch, zF + ch, k, -k, uw, uh), Ring(mb, c.x, c.y, HW - ch, HH - ch, 0f, zF, k, -k, uw, uh));
        mb.Band(Ring(mb, c.x, c.y, HW - ch, HH - ch, 0f, zF, 0f, -1f, uw, uh), Ring(mb, c.x, c.y, iw + ch, ih + ch, 0f, zF, 0f, -1f, uw, uh));
        mb.Band(Ring(mb, c.x, c.y, iw + ch, ih + ch, 0f, zF, -k, -k, uw, uh), Ring(mb, c.x, c.y, iw, ih, 0f, zF + ch, -k, -k, uw, uh));
        mb.Band(Ring(mb, c.x, c.y, iw, ih, 0f, zF + ch, -1f, 0f, uw, uh), Ring(mb, c.x, c.y, iw, ih, 0f, zFloor, -1f, 0f, uw, uh));
        int floor = mb.Add(new Vector3(c.x, c.y, zFloor), Vector3.back, new Vector2(0.5f, 0.5f));
        mb.Fan(floor, Ring(mb, c.x, c.y, iw, ih, 0f, zFloor, 0f, -1f, uw, uh));
        int back = mb.Add(new Vector3(c.x, c.y, zB), Vector3.forward, new Vector2(0.5f, 0.5f));
        mb.Fan(back, Ring(mb, c.x, c.y, HW, HH, ch, zB, 0f, 1f, uw, uh));
    }

    // ───────────────────────────── keys ─────────────────────────────

    class Geo
    {
        public Square sq;
        public float capWU, capHU, houWU, houHU, houFrontZU;    // key units (the key root's local space)
        public float capK = 1f, houK = 1f;                       // key units per mesh unit, along x
        public Mesh segCap, segHousing, stopCap, stopHousing;
        public TMP_Text templateLabel;
    }

    static void SetWideMeshes(Transform key, Mesh cap, Mesh housing, List<string> notes)
    {
        Transform capT = Under(key, "Cap3D"), houT = Under(key, "Housing3D");
        var cf = capT != null ? capT.GetComponent<MeshFilter>() : null;
        var hf = houT != null ? houT.GetComponent<MeshFilter>() : null;
        if (cf != null && cap != null && cf.sharedMesh != cap) { cf.sharedMesh = cap; EditorUtility.SetDirty(cf); }
        if (hf != null && housing != null && hf.sharedMesh != housing) { hf.sharedMesh = housing; EditorUtility.SetDirty(hf); }
        var rim = capT != null ? capT.Find("Cap_StateRim") : null;
        if (rim != null && rim.gameObject.activeSelf) rim.gameObject.SetActive(false);   // a collar of another key shape

        // Poke surface: the clipper keeps its height and the margin it had around the cap (as the Kit builder).
        if (capT == null || cf == null || cf.sharedMesh == null) return;
        Vector3 capSize = cf.sharedMesh.bounds.size;
        float capW = capSize.x * Mathf.Abs(capT.lossyScale.x / Safe(key.lossyScale.x));
        float capH = capSize.y * Mathf.Abs(capT.lossyScale.y / Safe(key.lossyScale.y));
        foreach (var clip in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (clip == null || clip.GetType().Name != "BoundsClipper") continue;
            var so = new SerializedObject(clip);
            var p = so.FindProperty("_size");
            if (p == null) { notes.Add("BoundsClipper has no _size (SDK changed?): poke surface not widened"); continue; }
            Vector3 size = p.vector3Value;
            float kx = Mathf.Abs(clip.transform.lossyScale.x / Safe(key.lossyScale.x));
            float ky = Mathf.Abs(clip.transform.lossyScale.y / Safe(key.lossyScale.y));
            float want = (size.y * ky + (capW - capH)) / Safe(kx);
            if (Mathf.Abs(size.x - want) > 1e-4f)
            {
                p.vector3Value = new Vector3(want, size.y, size.z);
                so.ApplyModifiedProperties();
            }
        }
    }

    /// <summary>
    /// Bold key label sized from the label's world scale: glyphs mm tall, never below 5.5 mm,
    /// centred on the cap face and moved by dy (metres, along the key's up).
    /// </summary>
    static TMP_Text KeyLabel(Transform key, string text, float mm, float boxHeight, float dy)
    {
        var tmp = key.GetComponentInChildren<TMP_Text>(true);
        if (tmp == null) return null;
        float s = Mathf.Abs(tmp.transform.lossyScale.x);
        if (s < 1e-7f) return tmp;
        var cap = Under(key, "Cap3D");
        float capWidth = Mathf.Abs(key.lossyScale.x);
        Vector3 centre = key.position;
        if (cap != null && OwnMeshBounds(cap, key, out var cmn, out var cmx))
        {
            capWidth = (cmx.x - cmn.x) * Mathf.Abs(key.lossyScale.x);
            centre = key.TransformPoint((cmn + cmx) * 0.5f);
        }
        // Centre on the cap face (keeping the label's own depth), then offset.
        Vector3 d = tmp.transform.position - centre;
        tmp.transform.position -= key.right * Vector3.Dot(d, key.right) + key.up * Vector3.Dot(d, key.up);
        tmp.transform.position += key.up * dy;
        tmp.text = text;
        tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        tmp.rectTransform.sizeDelta = new Vector2(0.90f * capWidth / s, boxHeight / s);
        float max = mm * 0.01f / s;
        float min = Mathf.Min(max, 5.5f * 0.01f / s);
        tmp.enableAutoSizing = min < max;
        tmp.fontSizeMax = max; tmp.fontSizeMin = min; tmp.fontSize = max;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Ink;
        EditorUtility.SetDirty(tmp);
        EditorUtility.SetDirty(tmp.transform);
        return tmp;
    }

    static ModeConsole.ConsoleKey MakeKey(Transform console, Transform template, Geo geo, string name, string words, bool stop,
                                          Vector3 localPos, float scale, Material capMat, string method, Mesh icon, Material iconMat,
                                          bool savedHidden, ModeConsole target, StringBuilder sb)
    {
        var notes = new List<string>();
        var key = CloneKey(console, template, name, out bool created);
        key.localRotation = Quaternion.identity;
        key.localPosition = localPos;
        key.localScale = Vector3.one * scale;                          // full size while it is measured
        if (geo.sq.ok) SetWideMeshes(key, stop ? geo.stopCap : geo.segCap, stop ? geo.stopHousing : geo.segHousing, notes);

        Transform capT = Under(key, "Cap3D");
        var capR = capT != null ? capT.GetComponent<MeshRenderer>() : null;
        if (capR != null && capMat != null) { capR.sharedMaterial = capMat; EditorUtility.SetDirty(capR); }

        float capH = CapHeight;
        if (capT != null && OwnMeshBounds(capT, key, out var cmn, out var cmx)) capH = (cmx.y - cmn.y) * scale;
        bool hasIcon = icon != null;
        var label = KeyLabel(key, words, hasIcon ? 6f : 6.5f, hasIcon ? 0.0085f : capH * 0.8f, hasIcon ? -capH * 0.24f : 0f);

        // Icon: a flat Ink mesh on the cap face, above the word, moving with the cap.
        if (label != null)
        {
            Transform holder = label.transform.parent;
            Transform it = holder.Find("Icon");
            if (hasIcon)
            {
                if (it == null)
                {
                    var go = new GameObject("Icon");
                    Undo.RegisterCreatedObjectUndo(go, "Icon");
                    go.transform.SetParent(holder, false);
                    go.AddComponent<MeshFilter>();
                    go.AddComponent<MeshRenderer>();
                    it = go.transform;
                }
                it.gameObject.layer = key.gameObject.layer;
                it.gameObject.SetActive(true);
                it.GetComponent<MeshFilter>().sharedMesh = icon;
                var ir = it.GetComponent<MeshRenderer>();
                ir.sharedMaterial = iconMat;
                ir.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ir.receiveShadows = false;
                it.rotation = key.rotation;
                it.position = label.transform.position + key.up * (capH * 0.24f + capH * 0.20f);
                float ps = Mathf.Abs(holder.lossyScale.x);
                it.localScale = Vector3.one * (0.008f / Safe(ps));
                EditorUtility.SetDirty(it.gameObject);
            }
            else if (it != null) it.gameObject.SetActive(false);
        }

        // One writer for the cap colour (as the Kit builder leaves every key).
        foreach (var mb in key.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if ((tn == "InteractableColorVisual" || (tn == "MaterialPropertyBlockEditor" && mb.transform == capT)) && mb.enabled)
            { mb.enabled = false; EditorUtility.SetDirty(mb); }
        }
        var ksv = GetOrAdd<KeyStateVisual>(key.gameObject);
        ksv.capRenderer = capR;
        ksv.rimRenderer = null;
        ksv.label = label;
        ksv.disabledBlend = 0.60f;                                     // 40 percent of its own colour left
        EditorUtility.SetDirty(ksv);

        Rewire(key, target, method);

        // Report: sizes at the shown scale, and the press travel.
        string geoText = "";
        Transform houT = Under(key, "Housing3D"), visual = Under(key, "ButtonVisual");
        if (capT != null && OwnMeshBounds(capT, key, out var a, out var b))
            geoText = $"cap {Mm((b.x - a.x) * scale)} x {Mm((b.y - a.y) * scale)} mm";
        if (houT != null && OwnMeshBounds(houT, key, out var ha, out var hb))
            geoText += $", housing {Mm((hb.x - ha.x) * scale)} x {Mm((hb.y - ha.y) * scale)} mm";
        if (visual != null)
        {
            Transform baseTf = SurfaceBase(key);
            float travel = Vector3.Dot(visual.position - baseTf.position, -baseTf.forward);
            geoText += $", press travel {Mm(travel)} mm";
            if (Mathf.Abs(travel - 0.002f) > 0.0007f) notes.Add("travel is not the 2 mm asked for (it is the template key's rest offset times the key scale; not changed here)");
        }

        var ck = new ModeConsole.ConsoleKey
        {
            root = key,
            shownScale = Vector3.one * scale,
            label = label,
            visual = ksv,
            poke = key.GetComponentInChildren<PokeInteractable>(true),
            cap = capR,
        };
        if (ck.poke == null) notes.Add("WARNING: no PokeInteractable under the key");
        if (savedHidden) key.localScale = Vector3.one * scale * Hidden;
        EditorUtility.SetDirty(key.gameObject);
        sb.AppendLine($"  {(created ? "created" : "updated")} {name} \"{words.Replace("\n", " / ")}\" -> ModeConsole.{method}; {geoText}{(savedHidden ? "; saved hidden" : "")}{(notes.Count > 0 ? "; " + string.Join("; ", notes) : "")}");
        return ck;
    }

    // ───────────────────────────── entry ─────────────────────────────

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildModeConsoleV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildModeConsoleV1: no open scene.\n";
        var sb = new StringBuilder();
        sb.AppendLine("== BuildModeConsoleV1 ==");
        var all = All();
        var ws = Find(all, "WorkspaceRoot");
        var spatial = scene.GetRootGameObjects().Select(g => g.transform).FirstOrDefault(t => t.name == "Spatial");
        if (ws == null || spatial == null) return "BuildModeConsoleV1: WorkspaceRoot or Spatial not found; nothing changed.\n";
        var focus = Find(all, "FocusToggle");
        if (focus == null) return "BuildModeConsoleV1: FocusToggle not found (no key to clone); nothing changed.\n";
        int layer = ws.gameObject.layer;
        if (Mathf.Abs(ws.lossyScale.x - 1f) > 0.01f) sb.AppendLine($"  WARNING: WorkspaceRoot scale is {ws.lossyScale.x:F3}; the console is built for scale 1");

        var strip = Object.FindAnyObjectByType<SpatialModeStrip>(FindObjectsInactive.Include);
        var director = Object.FindAnyObjectByType<SpatialWorkspaceDirector>(FindObjectsInactive.Include);
        var summon = Object.FindAnyObjectByType<SummonController>(FindObjectsInactive.Include);
        if (director == null) sb.AppendLine("  WARNING: no SpatialWorkspaceDirector in the scene: the keys will flash 'refused'");

        // The rig the console lives under, standing at the office's desk pose.
        var rig = spatial.Find("NearUserRig");
        if (rig == null) { rig = new GameObject("NearUserRig").transform; Undo.RegisterCreatedObjectUndo(rig.gameObject, "NearUserRig"); rig.SetParent(spatial, false); }
        rig.gameObject.layer = layer;
        rig.SetPositionAndRotation(ws.position, ws.rotation);
        rig.localScale = ws.lossyScale;

        EnsureFolder("Assets/MuseDesk/Art", "Generated");
        EnsureFolder("Assets/MuseDesk/Art", "Materials");
        EnsureFolder("Assets/MuseDesk/Art/Materials", "UI");

        // ── console object ──
        Transform consoleT = Under(rig, "ModeConsole");
        bool createdConsole = consoleT == null;
        if (createdConsole) consoleT = Holder(rig, "ModeConsole", layer);
        consoleT.gameObject.layer = layer;
        consoleT.gameObject.SetActive(true);
        var console = GetOrAdd<ModeConsole>(consoleT.gameObject);

        // ── measure the template key (FocusToggle) ──
        Transform tCap = Under(focus, "Cap3D"), tHou = Under(focus, "Housing3D");
        if (tCap == null || tHou == null || !OwnMeshBounds(tCap, focus, out var cmn, out var cmx) || !OwnMeshBounds(tHou, focus, out var hmn, out var hmx))
            return sb.Append("BuildModeConsoleV1: FocusToggle has no Cap3D / Housing3D mesh; nothing changed.\n").ToString();
        var geo = new Geo
        {
            sq = Measure(tCap.GetComponent<MeshFilter>().sharedMesh, tHou.GetComponent<MeshFilter>().sharedMesh),
            capWU = cmx.x - cmn.x, capHU = cmx.y - cmn.y, houWU = hmx.x - hmn.x, houHU = hmx.y - hmn.y, houFrontZU = hmn.z,
            capK = Mathf.Abs(tCap.lossyScale.x / Safe(focus.lossyScale.x)), houK = Mathf.Abs(tHou.lossyScale.x / Safe(focus.lossyScale.x)),
            templateLabel = focus.GetComponentInChildren<TMP_Text>(true),
        };
        float fs = Mathf.Abs(focus.lossyScale.x);                       // the Focus key keeps its size
        float s = CapHeight / Safe(geo.capHU);                          // uniform scale of every console key
        float rimU = (geo.houWU - geo.capWU) * 0.5f;
        float segExtraU = geo.sq.ok ? Mathf.Max(0f, SegCapWidth / s - geo.capWU) : 0f;
        float segHouW = (geo.houWU + segExtraU) * s, rowH = geo.houHU * s;
        float rowW = 3f * segHouW + 2f * Gap;
        float stopExtraU = geo.sq.ok ? Mathf.Max(0f, rowW / s - geo.houWU) : 0f;
        sb.AppendLine($"  template FocusToggle: cap {geo.capWU:F3} x {geo.capHU:F3}, housing {geo.houWU:F3} x {geo.houHU:F3} key units, rim {rimU:F3}, housing front z {geo.houFrontZU:F3}; world scale {fs:F4}");
        if (!geo.sq.ok) sb.AppendLine($"  WARNING: {geo.sq.why}: the console keys stay square");
        else
        {
            var mb = new MB(); WideCap(mb, geo.sq, segExtraU / Safe(geo.capK)); geo.segCap = Store("ModeConsole_KeyCap_Segment", mb);
            mb = new MB(); WideHousing(mb, geo.sq, segExtraU / Safe(geo.houK)); geo.segHousing = Store("ModeConsole_KeyHousing_Segment", mb);
            mb = new MB(); WideCap(mb, geo.sq, stopExtraU / Safe(geo.capK)); geo.stopCap = Store("ModeConsole_KeyCap_Stop", mb);
            mb = new MB(); WideHousing(mb, geo.sq, stopExtraU / Safe(geo.houK)); geo.stopHousing = Store("ModeConsole_KeyHousing_Stop", mb);
        }
        sb.AppendLine($"  console keys: uniform scale {s:F4}; place key housing {Mm(segHouW)} x {Mm(rowH)} mm, cap {Mm((geo.capWU + segExtraU) * s)} x {Mm(geo.capHU * s)} mm; cap-to-cap gap {Mm(Gap + 2f * rimU * s)} mm; Stop housing {Mm((geo.houWU + stopExtraU) * s)} mm wide");

        // ── layout (console-local: +x the user's right, +y up the slope away from the user, -z out of the face) ──
        float focusW = geo.houWU * fs, focusH = geo.houHU * fs;
        float wellW = rowW + 2f * WellPad;
        float W = Margin + (focusW + 2f * WellPad) + GroupGap + wellW + GroupGap + MicSize + Margin;
        float segWellH = rowH + 2f * WellPad + IndicatorDrop, ctxWellH = rowH + 2f * WellPad;
        float rowsH = ctxWellH + RowGap + segWellH;
        float contentH = Mathf.Max(rowsH, focusH + 2f * WellPad);
        float H = Margin + contentH + 0.004f + LipHeight + 0.002f;
        float x0 = -W * 0.5f, yBottom = -H * 0.5f + Margin + (contentH - rowsH) * 0.5f;
        float focusX = x0 + Margin + WellPad + focusW * 0.5f;
        float rowLeft = x0 + Margin + focusW + 2f * WellPad + GroupGap + WellPad;
        float[] colX = { rowLeft + segHouW * 0.5f, rowLeft + segHouW * 1.5f + Gap, rowLeft + segHouW * 2.5f + 2f * Gap };
        float rowCx = colX[1];
        float ctxY = yBottom + WellPad + rowH * 0.5f;
        float segY = yBottom + ctxWellH + RowGap + IndicatorDrop + WellPad + rowH * 0.5f;
        float midY = yBottom + rowsH * 0.5f;
        float micX = W * 0.5f - Margin - MicSize * 0.5f;
        float zTop = geo.houFrontZU * s + RimProud;                     // body top; the place keys' origins are on z = 0
        float focusZ = zTop - RimProud - geo.houFrontZU * fs;
        float tan = Mathf.Tan(TiltDeg * Mathf.Deg2Rad), sin = Mathf.Sin(TiltDeg * Mathf.Deg2Rad), cos = Mathf.Cos(TiltDeg * Mathf.Deg2Rad);
        sb.AppendLine($"  console face {Mm(W)} x {Mm(H)} mm, tilt {TiltDeg} degrees, {Mm(NearThickness)} mm thick at the near edge, {Mm(NearThickness + H * tan)} mm at the far edge");

        // ── materials ──
        var keyMatSource = Existing("Key_FiledPoke") ?? Existing("Key_RunAgain");
        var matBody = Mat("ModeConsole_Body", Ink, true);
        var matInlay = Existing("OfficeV2_Paper") ?? Mat("ModeConsole_Inlay", Paper, false);
        var matTeal = Existing("OfficeV2_Strip_Teal") ?? Mat("ModeConsole_Teal", Teal, false);
        var matWell = Existing("OfficeV2_Plate") ?? Mat("ModeConsole_Well", Plate, false);
        var matIcon = Mat("ModeConsole_Icon", Ink, false);
        var matIdle = Mat("Key_ConsoleIdle", Paper, true, keyMatSource);
        var matActive = Mat("Key_ConsoleActive", Teal, true, keyMatSource);
        var matStop = Mat("Key_ConsoleStop", Amber, true, keyMatSource);

        // ── static geometry ──
        {
            var mb = new MB();
            Slab(mb, 0f, 0f, W * 0.5f, H * 0.5f, CornerRadius, Bevel, zTop, zTop + NearThickness, tan);
            Shape(consoleT, "Body", Store("ModeConsole_Body", mb), matBody, Vector3.zero, Quaternion.identity);

            const float inset = 0.0025f;
            mb = new MB(); FlatPlate(mb, 0f, 0f, W * 0.5f - inset, H * 0.5f - inset, CornerRadius - inset, 0f);
            Shape(consoleT, "Inlay", Store("ModeConsole_Inlay", mb), matInlay, new Vector3(0f, 0f, zTop - 0.0004f), Quaternion.identity);
            mb = new MB(); FlatOutline(mb, W * 0.5f - inset, H * 0.5f - inset, CornerRadius - inset, 0.0009f, 0f);
            Shape(consoleT, "EdgeLine", Store("ModeConsole_EdgeLine", mb), matTeal, new Vector3(0f, 0f, zTop - 0.0006f), Quaternion.identity);

            mb = new MB(); FlatPlate(mb, 0f, 0f, wellW * 0.5f, segWellH * 0.5f, 0.004f, 0f);
            Shape(consoleT, "SegmentWell", Store("ModeConsole_SegmentWell", mb), matWell,
                  new Vector3(rowCx, segY - IndicatorDrop * 0.5f, zTop - 0.0007f), Quaternion.identity);
            mb = new MB(); FlatPlate(mb, 0f, 0f, wellW * 0.5f, ctxWellH * 0.5f, 0.004f, 0f);
            Shape(consoleT, "ContextWell", Store("ModeConsole_ContextWell", mb), matWell,
                  new Vector3(rowCx, ctxY, zTop - 0.0007f), Quaternion.identity);
            mb = new MB(); FlatPlate(mb, 0f, 0f, focusW * 0.5f + WellPad, focusH * 0.5f + WellPad, 0.004f, 0f);
            Shape(consoleT, "FocusWell", Store("ModeConsole_FocusWell", mb), matWell,
                  new Vector3(focusX, midY, zTop - 0.0007f), Quaternion.identity);

            // Raised lip at the far edge, tilted further toward the user, carrying the status line.
            float lipW = W - 2f * Margin;
            mb = new MB(); Slab(mb, 0f, LipHeight * 0.5f, lipW * 0.5f, LipHeight * 0.5f, 0.003f, 0.0008f, 0f, LipDepth, 0f);
            var lip = Shape(consoleT, "StatusLip", Store("ModeConsole_Lip", mb), matWell,
                            new Vector3(0f, H * 0.5f - LipHeight - 0.002f, zTop + 0.0005f), Quaternion.Euler(-LipTiltDeg, 0f, 0f));
            var lt = lip.Find("StatusLabel");
            TextMeshPro status = lt != null ? lt.GetComponent<TextMeshPro>() : null;
            if (status == null)
            {
                var go = lt != null ? lt.gameObject : new GameObject("StatusLabel");
                if (lt == null) { Undo.RegisterCreatedObjectUndo(go, "StatusLabel"); go.transform.SetParent(lip, false); }
                status = go.AddComponent<TextMeshPro>();
            }
            status.gameObject.layer = layer;
            status.transform.localPosition = new Vector3(0f, LipHeight * 0.5f, -0.0005f);      // 0.5 mm proud of the lip
            status.transform.localRotation = Quaternion.identity;
            status.transform.localScale = Vector3.one * 0.01f;
            if (geo.templateLabel != null) { status.font = geo.templateLabel.font; }
            status.text = console.deskWords;
            status.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            status.rectTransform.sizeDelta = new Vector2((lipW - 0.008f) * 100f, (LipHeight - 0.002f) * 100f);
            status.enableAutoSizing = true;
            status.fontSizeMin = 5.5f; status.fontSizeMax = 7f; status.fontSize = 7f;       // about 5 mm capitals
            status.color = OnPlate;
            status.fontStyle = FontStyles.Normal;
            status.alignment = TextAlignmentOptions.Center;
            status.textWrappingMode = TextWrappingModes.NoWrap;
            status.overflowMode = TextOverflowModes.Ellipsis;
            status.margin = Vector4.zero;
            var smr = status.GetComponent<MeshRenderer>();
            if (smr != null) { smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; smr.receiveShadows = false; }
            EditorUtility.SetDirty(status);
            console.statusLabel = status;

            // Indicator bars under the three place keys (ModeConsole shows the current one).
            float barW = (geo.capWU + segExtraU) * s * 0.5f, barH = 0.0016f;
            mb = new MB(); FlatPlate(mb, 0f, 0f, barW * 0.5f, barH * 0.5f, barH * 0.5f, 0f);    // real size: a pill
            var barMesh = Store("ModeConsole_Indicator", mb);
            var bars = new Transform[3];
            string[] barNames = { "Indicator_Desk", "Indicator_Floor", "Indicator_Ceiling" };
            for (int i = 0; i < 3; i++)
            {
                bars[i] = Shape(consoleT, barNames[i], barMesh, matTeal,
                                new Vector3(colX[i], segY - rowH * 0.5f - 0.0030f, zTop - 0.0011f), Quaternion.identity);
                bars[i].localScale = Vector3.one * (i == 0 ? 1f : Hidden);
            }
            console.indicators = bars;
            console.indicatorScale = Vector3.one;

            var mic = Holder(consoleT, "MicSlot", layer);
            mic.localPosition = new Vector3(micX, midY, zTop - 0.0012f);
            mic.localRotation = Quaternion.identity;
            mic.localScale = Vector3.one;
            sb.AppendLine($"  MicSlot (empty, for the voice agent): console-local {P(mic.localPosition)}, a clear {Mm(MicSize)} x {Mm(MicSize)} mm of inlay at the right end; -Z is out of the face, the origin is 0.8 mm proud of the inlay");
        }

        // ── icons ──
        Mesh iconDesk = Icon("ModeConsole_Icon_Desk", 0f, 0.26f, 1.0f, 0.16f, -0.36f, -0.12f, 0.12f, 0.60f, 0.36f, -0.12f, 0.12f, 0.60f);
        Mesh iconFloor = Icon("ModeConsole_Icon_Floor",
            0f, -0.42f, 0.93f, 0.09f, 0f, -0.14f, 0.93f, 0.09f, 0f, 0.14f, 0.93f, 0.09f, 0f, 0.42f, 0.93f, 0.09f,
            -0.42f, 0f, 0.09f, 0.93f, -0.14f, 0f, 0.09f, 0.93f, 0.14f, 0f, 0.09f, 0.93f, 0.42f, 0f, 0.09f, 0.93f);
        Mesh iconCeiling = Icon("ModeConsole_Icon_Ceiling", 0f, 0.44f, 1.0f, 0.12f, 0f, 0.20f, 0.10f, 0.36f, 0f, -0.20f, 0.72f, 0.44f);

        // ── keys ──
        sb.AppendLine("-- keys --");
        console.director = director;
        console.summon = summon;
        console.segmentIdle = matIdle;
        console.segmentActive = matActive;
        console.deskKey = MakeKey(consoleT, focus, geo, "Console_Desk", "Desk", false, new Vector3(colX[0], segY, 0f), s, matActive, nameof(ModeConsole.PressDesk), iconDesk, matIcon, false, console, sb);
        console.floorKey = MakeKey(consoleT, focus, geo, "Console_Floor", console.floorWords, false, new Vector3(colX[1], segY, 0f), s, matIdle, nameof(ModeConsole.PressFloor), iconFloor, matIcon, false, console, sb);
        console.ceilingKey = MakeKey(consoleT, focus, geo, "Console_Ceiling", "Ceiling", false, new Vector3(colX[2], segY, 0f), s, matIdle, nameof(ModeConsole.PressCeiling), iconCeiling, matIcon, false, console, sb);
        console.nextKey = MakeKey(consoleT, focus, geo, "Console_Next", "Next", false, new Vector3(colX[0], ctxY, 0f), s, matIdle, nameof(ModeConsole.PressNext), null, null, true, console, sb);
        console.confirmKey = MakeKey(consoleT, focus, geo, "Console_Confirm", "Confirm", false, new Vector3(colX[1], ctxY, 0f), s, matActive, nameof(ModeConsole.PressConfirm), null, null, true, console, sb);
        console.cancelKey = MakeKey(consoleT, focus, geo, "Console_Cancel", "Cancel", false, new Vector3(colX[2], ctxY, 0f), s, matIdle, nameof(ModeConsole.PressCancel), null, null, true, console, sb);
        console.stopKey = MakeKey(consoleT, focus, geo, "Console_Stop", "Stop", true, new Vector3(rowCx, ctxY, 0f), s, matStop, nameof(ModeConsole.PressStop), null, null, true, console, sb);
        console.podKey = MakeKey(consoleT, focus, geo, "Console_Pod", ModeConsoleLogic.Lower, false, new Vector3(colX[0], ctxY, 0f), s, matActive, nameof(ModeConsole.PressPod), null, null, true, console, sb);
        console.bringKey = MakeKey(consoleT, focus, geo, "Console_Bring", "Bring here", false, new Vector3(colX[0], ctxY, 0f), s, matActive, nameof(ModeConsole.PressBring), null, null, true, console, sb);
        console.statusKey = MakeKey(consoleT, focus, geo, "Console_Status", "Status", false, new Vector3(colX[1], ctxY, 0f), s, matIdle, nameof(ModeConsole.PressStatus), null, null, true, console, sb);

        // ── where the old keys were, and the default pose ──
        Transform focusParent0 = console.focusKey == focus && console.focusOriginalParent != null ? console.focusOriginalParent : focus.parent;
        Vector3 focusLocal0 = console.focusKey == focus && console.focusOriginalParent != null
            ? ws.InverseTransformPoint(console.focusOriginalParent.TransformPoint(console.focusOriginalLocalPosition))
            : ws.InverseTransformPoint(focus.position);
        Vector3 anchor = focusLocal0 + new Vector3(-0.10f, 0f, 0f);
        if (strip != null && strip.slot2.sqrMagnitude > 1e-8f)
            anchor = (focusLocal0 + strip.deskLocalPosition + Quaternion.Euler(strip.deskLocalEuler) * strip.slot2) * 0.5f;
        sb.AppendLine($"-- placement -- old row: Focus key at {P(focusLocal0)} (root-local), centre of Focus + mode keys {P(anchor)}");

        // Old loose keys go first, so they are not measured as neighbours.
        int retired = 0;
        if (strip != null)
        {
            foreach (Transform child in strip.transform.Cast<Transform>().ToList())
            {
                if (child.name.EndsWith("_old")) { if (child.gameObject.activeSelf) child.gameObject.SetActive(false); continue; }
                child.gameObject.SetActive(false);
                child.name += "_old";
                EditorUtility.SetDirty(child.gameObject);
                retired++;
            }
            strip.console = console;
            EditorUtility.SetDirty(strip);
        }
        else sb.AppendLine("  WARNING: no SpatialModeStrip in the scene: nothing forwards the director's state to the console (set director.strip to a SpatialModeStrip whose console is ModeConsole)");
        if (summon != null)
        {
            Transform oldKeys = summon.keysRoot != null ? summon.keysRoot : summon.transform.Find("SummonKeys");
            if (oldKeys == null) oldKeys = summon.transform.Find("SummonKeys_old");
            if (oldKeys != null)
            {
                if (!oldKeys.name.EndsWith("_old")) { oldKeys.name += "_old"; retired++; }
                oldKeys.gameObject.SetActive(false);
                EditorUtility.SetDirty(oldKeys.gameObject);
            }
            summon.keysRoot = null;
            summon.console = console;
            summon.bringLabel = console.bringKey.label;
            summon.bringWords = "Bring here";
            summon.sendWords = "Send back";
            EditorUtility.SetDirty(summon);
        }
        sb.AppendLine($"  retired {retired} old object(s): children of {(strip != null ? strip.name : "(no strip)")} and SummonKeys, deactivated and renamed *_old");

        // Neighbours the console must stay clear of (root-local boxes).
        var neighbours = new List<(string name, Vector3 mn, Vector3 mx)>();
        foreach (var n in new[] { "CompletionReceipt", "StampApprove", "StampReject", "Doc_StampRestPad", "TaskDock", "ModePlaque", "FiledPoke" })
        {
            var t = Find(all, n);
            if (t != null && ActiveBelow(t, ws) && ShownBounds(t, ws, out var nmn, out var nmx)) neighbours.Add((n, nmn, nmx));
        }
        Quaternion tilt = Quaternion.LookRotation(new Vector3(0f, -cos, -sin), new Vector3(0f, sin, -cos));
        Vector2 half0 = new Vector2(W * 0.5f, H * 0.5f * cos + NearThickness);
        Vector3[] keyPoints = { new Vector3(focusX, midY, 0f), new Vector3(colX[0], segY, 0f), new Vector3(colX[2], segY, 0f), new Vector3(colX[0], ctxY, 0f), new Vector3(colX[2], ctxY, 0f) };

        float Yaw(Vector3 c) => Mathf.Atan2(Eye.x - c.x, Eye.z - c.z) * Mathf.Rad2Deg;
        float Reach(Vector3 c)
        {
            Quaternion r = Quaternion.AngleAxis(Yaw(c), Vector3.up) * tilt;
            float worst = 0f;
            foreach (var kp in keyPoints) worst = Mathf.Max(worst, (c + r * kp - Eye).magnitude);
            return worst;
        }
        string Blocked(Vector3 c)
        {
            Vector2 h = PanelLayoutMath.RotatedHalfExtents(half0, Yaw(c));
            foreach (var nb in neighbours)
            {
                if (nb.mn.y > c.y + 0.04f) continue;
                Vector2 nc = new Vector2((nb.mn.x + nb.mx.x) * 0.5f, (nb.mn.z + nb.mx.z) * 0.5f);
                Vector2 nh = new Vector2((nb.mx.x - nb.mn.x) * 0.5f, (nb.mx.z - nb.mn.z) * 0.5f);
                if (PanelLayoutMath.Overlaps(new Vector2(c.x, c.z), h, nc, nh, 0.006f)) return nb.name;
            }
            return null;
        }

        // The surface the console stands on: the desk plane, or a wide flat part of the office under it.
        float surfaceY = 0f; string surfaceName = "the desk plane (root y = 0)";
        foreach (var f in ws.GetComponentsInChildren<MeshFilter>(true))
        {
            if (f.sharedMesh == null || f.transform.IsChildOf(consoleT) || f.transform.IsChildOf(focus) || !ActiveBelow(f.transform, ws)) continue;
            var r = f.GetComponent<MeshRenderer>();
            if (r == null || !r.enabled || !OwnMeshBounds(f.transform, ws, out var a, out var b)) continue;
            if (b.x - a.x < 0.3f || b.z - a.z < 0.3f || b.y > 0.08f || b.y <= surfaceY) continue;
            if (anchor.x < a.x || anchor.x > b.x || anchor.z < a.z || anchor.z > b.z) continue;
            surfaceY = b.y; surfaceName = f.name + " (top at root y = " + b.y.ToString("F3") + ")";
        }
        float originY = surfaceY + 0.001f + cos * (zTop + NearThickness) + H * 0.5f * sin;
        Vector3 centre = new Vector3(anchor.x, originY, anchor.z);
        string startBlock = Blocked(centre);
        if (startBlock != null)
        {
            // Out to the user's right (root -X) until clear.
            for (int i = 0; i < 25 && Blocked(centre) != null; i++) centre.x -= 0.01f;
            sb.AppendLine($"  the old spot overlapped {startBlock}: moved to the user's right until clear{(Blocked(centre) != null ? " (STILL overlapping " + Blocked(centre) + ")" : "")}");
        }
        int pulls = 0;
        while (Reach(centre) > ReachTarget && pulls < 30)
        {
            Vector3 dir = Eye - centre; dir.y = 0f;
            if (dir.magnitude < 0.30f) break;                              // never closer than 30 cm (level) to the eye
            Vector3 next = centre + dir.normalized * 0.01f;
            if (Blocked(next) != null) break;
            centre = next; pulls++;
        }
        float yaw = Yaw(centre);
        Quaternion localRot = Quaternion.AngleAxis(yaw, Vector3.up) * tilt;
        consoleT.SetPositionAndRotation(ws.TransformPoint(centre), ws.rotation * localRot);
        consoleT.localScale = Vector3.one;
        float reach = Reach(centre);
        sb.AppendLine($"  stands on {surfaceName}; console origin {P(centre)} root-local, yaw {yaw:F0} degrees toward the seated eye, pulled {pulls} cm toward the user; furthest key {reach:F3} m from the eye{(reach > ReachTarget + 0.005f ? "  WARNING: beyond 0.50 m (blocked by " + (Blocked(centre + (Eye - centre).normalized * 0.01f) ?? "the 30 cm limit") + ")" : "")}");
        string nowBlock = Blocked(centre);
        if (nowBlock != null) sb.AppendLine($"  WARNING: the console's footprint overlaps {nowBlock}");

        // ── the Focus key, re-seated on the console (same size) ──
        if (console.focusKey != focus || focus.parent != consoleT)
        {
            if (console.focusKey != focus)
            {
                console.focusKey = focus;
                console.focusOriginalParent = focus.parent;
                console.focusOriginalLocalPosition = focus.localPosition;
                console.focusOriginalLocalRotation = focus.localRotation;
                console.focusOriginalLocalScale = focus.localScale;
                console.focusOriginalSibling = focus.GetSiblingIndex();
                var face = focus.GetComponent<FaceUser>();
                console.focusBillboardDisabled = face != null && face.enabled ? face : null;
                if (face != null && face.enabled) { face.enabled = false; EditorUtility.SetDirty(face); }
            }
            Undo.SetTransformParent(focus, consoleT, "seat Focus key");
        }
        focus.localRotation = Quaternion.identity;
        focus.localScale = Vector3.one * fs;
        focus.localPosition = new Vector3(focusX, midY, focusZ);
        EditorUtility.SetDirty(focus.gameObject);
        sb.AppendLine($"  FocusToggle seated on the console at {P(focus.localPosition)} (console-local), scale {fs:F4} unchanged; it was under {(focusParent0 != null ? focusParent0.name : "(scene root)")}");

        EditorUtility.SetDirty(console);
        sb.AppendLine("-- summary --");
        sb.AppendLine("  SpatialModeStrip.console and SummonController.console point at ModeConsole; SummonController.keysRoot is empty; feature flags untouched.");
        sb.AppendLine("  ModeConsole is a child of Spatial/NearUserRig. NearUserKit must keep that rig on the office root in Desk mode: run BuildMovablePanelsV1.Apply next (it also adds the grab bar).");
        sb.AppendLine("  Play Mode: AgentScripts/PanelsDrive.cs  Console, PressAllConsoleKeys, and SpatialDrive as before.");
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    /// <summary>
    /// Undo the console: the old loose keys and the summon keys are renamed back and reactivated, the
    /// Focus key goes back to its old parent and pose, the strip and the summon controller work as
    /// before, and ModeConsole is deactivated (not destroyed). Run BuildMovablePanelsV1.Apply again
    /// afterwards so the Focus key gets a panel of its own.
    /// </summary>
    public static string Revert()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildModeConsoleV1.Revert: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder("== BuildModeConsoleV1.Revert ==\n");
        var console = Object.FindAnyObjectByType<ModeConsole>(FindObjectsInactive.Include);
        var strip = Object.FindAnyObjectByType<SpatialModeStrip>(FindObjectsInactive.Include);
        var summon = Object.FindAnyObjectByType<SummonController>(FindObjectsInactive.Include);

        if (console != null && console.focusKey != null && console.focusOriginalParent != null)
        {
            Transform f = console.focusKey;
            Undo.SetTransformParent(f, console.focusOriginalParent, "restore Focus key");
            f.localPosition = console.focusOriginalLocalPosition;
            f.localRotation = console.focusOriginalLocalRotation;
            f.localScale = console.focusOriginalLocalScale;
            f.SetSiblingIndex(Mathf.Min(console.focusOriginalSibling, f.parent.childCount - 1));
            if (console.focusBillboardDisabled != null) { console.focusBillboardDisabled.enabled = true; EditorUtility.SetDirty(console.focusBillboardDisabled); }
            console.focusKey = null; console.focusBillboardDisabled = null;
            EditorUtility.SetDirty(f.gameObject);
            sb.AppendLine("  FocusToggle back under " + f.parent.name);
        }
        if (strip != null)
        {
            int n = 0;
            foreach (Transform child in strip.transform)
            {
                if (!child.name.EndsWith("_old")) continue;
                child.name = child.name.Substring(0, child.name.Length - 4);
                child.gameObject.SetActive(true);
                EditorUtility.SetDirty(child.gameObject);
                n++;
            }
            strip.console = null;
            EditorUtility.SetDirty(strip);
            sb.AppendLine($"  {n} old mode key object(s) reactivated; SpatialModeStrip.console cleared");
        }
        if (summon != null)
        {
            Transform old = summon.transform.Find("SummonKeys_old");
            if (old != null)
            {
                old.name = "SummonKeys";
                old.gameObject.SetActive(true);
                summon.keysRoot = old;
                var bring = old.Find("BringKey");
                summon.bringLabel = bring != null ? bring.GetComponentInChildren<TMP_Text>(true) : null;
                EditorUtility.SetDirty(old.gameObject);
            }
            summon.console = null;
            summon.bringWords = "Bring it\nhere";
            summon.sendWords = "Send it\nback";
            EditorUtility.SetDirty(summon);
            sb.AppendLine("  SummonKeys " + (old != null ? "reactivated" : "not found (SummonController.keysRoot left empty)"));
        }
        if (console != null)
        {
            console.gameObject.SetActive(false);
            EditorUtility.SetDirty(console);
            sb.AppendLine("  ModeConsole deactivated (kept)");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }
}
