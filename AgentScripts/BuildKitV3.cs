using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Workspace;

/// <summary>
/// Kit builder of the final refinement pass. Runs LAST, after the Paper, Receipt, Board and Life
/// builders, so everything is discovered by pattern in the open scene and anything missing is
/// tolerated. Idempotent; saves the scene once.
///
///   1. KEY STATE COLOURS. Every poke key (an object with a PokeInteractable and a Cap3D below it)
///      gets a KeyStateVisual wired to its cap renderer. The SDK's InteractableColorVisual and the
///      MaterialPropertyBlockEditor on the cap are disabled: the first writes "_Color", which URP
///      Simple Lit ignores, and the second re-applies its block every frame and would erase the tint.
///   2. WIDE KEYS. A key is wide when it has a child marker "WideKey_&lt;ratio&gt;", or when its root or
///      a wrapper above it (Btn_*) is scaled non-uniformly in x/y. Non-uniform scales are made
///      uniform (the y scale), the measured footprint ratio is written as a marker, and the key gets
///      cap and housing geometry of that ratio: an Antigravity model (UI_PokeKey_Wide / HeroWide /
///      Chip, baked into key space and measured) when its ratio is within 15 percent, otherwise a
///      procedural rounded box whose rim width, bevel and depth are those of the square key.
///      "ratio" always means housing width / housing height.
///   3. ROUNDED PLATES. Unity quads named *Plate, *Sheet, *Band, *Strip, *Chip, *Card, *Backing, or
///      carrying a UI material (OfficeV2_, PaperV2_, Paper_, TaskDock_, ReceiptV3_, BoardV3_, Dock_)
///      under WorkspaceRoot / DecisionDocument get a rounded-rectangle mesh of the same footprint.
///
///   unity command script run --file AgentScripts/BuildKitV3.cs --entry BuildKitV3.Apply
///   (BuildKitV3.Rebuild regenerates every cached mesh in place; BuildKitV3.Revert undoes the builder.)
/// </summary>
public static class BuildKitV3
{
    const string MeshDir = "Assets/MuseDesk/Art/Meshes/UI";
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI";
    const string ModelDir = "Assets/MuseDesk/Art/Models/UI";

    const string CapName = "Cap3D", HousingName = "Housing3D", VisualName = "ButtonVisual";
    const string RimName = "Cap_StateRim", MarkerPrefix = "WideKey_";
    const string KeyMeshPrefix = "UI_Key_", PlateMeshPrefix = "UI_Plate_";

    const float MinWideRatio = 1.05f, MaxWideRatio = 8f;
    const float FbxTolerance = 0.15f;          // use an Antigravity model when its ratio is this close
    const float ProudUnits = 0.016f;           // cap face in front of the rim at full press (key units), as FeedbackPass4
    const int CornerSegs = 5;                  // segments per rounded corner
    const int BevelSegs = 4;                   // segments across the cap's front bevel

    // Square key dimensions from ArtSource/Scripts/generate_visual_polish_assets.py, used only when
    // they cannot be measured from the imported meshes (all in key units for a 1.20 housing).
    const float DefCapBevel = 0.065f, DefWall = 0.095f, DefChamfer = 0.018f, DefBackPlate = 0.045f;

    static readonly string[] PlateSuffixes = { "Plate", "Sheet", "Band", "Strip", "Chip", "Card", "Backing" };
    static readonly string[] PlateMaterialPrefixes = { "OfficeV2_", "PaperV2_", "Paper_", "TaskDock_", "ReceiptV3_", "BoardV3_", "Dock_" };
    static readonly string[] FbxVariants = { "Wide", "HeroWide", "Chip" };

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static bool _force;

    // ───────────────────────────── small helpers ─────────────────────────────

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

    static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name == name) return t;
        return null;
    }

    static MonoBehaviour Comp(Transform t, string typeName)
    {
        foreach (var mb in t.GetComponents<MonoBehaviour>())
            if (mb != null && mb.GetType().Name == typeName) return mb;
        return null;
    }

    static List<MonoBehaviour> CompsBelow(Transform root, string typeName)
    {
        var list = new List<MonoBehaviour>();
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.GetType().Name == typeName) list.Add(mb);
        return list;
    }

    static string PathOf(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    static float Safe(float v) => Mathf.Abs(v) < 1e-9f ? 1e-9f : v;
    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    static string F(float v, string fmt) => v.ToString(fmt, Inv);
    static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    /// <summary>
    /// The scale an object has when it is SHOWN. Signs and receipts are hidden by scaling a parent
    /// to 0.001 (ScaleEase.HiddenFactor); such a parent counts at its full size here, so a hidden
    /// plate or key is measured as the user will see it.
    /// </summary>
    static Vector3 ShownScale(Transform t)
    {
        Vector3 s = Abs(t.localScale);
        for (var p = t.parent; p != null; p = p.parent)
        {
            Vector3 l = Abs(p.localScale);
            if (Mathf.Max(l.x, Mathf.Max(l.y, l.z)) < 0.005f) l *= 1000f;
            s = Vector3.Scale(s, l);
        }
        return s;
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    static void EnsureFolders()
    {
        EnsureFolder("Assets/MuseDesk/Art", "Meshes");
        EnsureFolder("Assets/MuseDesk/Art/Meshes", "UI");
        EnsureFolder("Assets/MuseDesk/Art", "Materials");
        EnsureFolder("Assets/MuseDesk/Art/Materials", "UI");
    }

    static Mesh BuiltinQuad()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(go);
        return mesh;
    }

    // ───────────────────────────── mesh building ─────────────────────────────

    /// <summary>Vertex and triangle lists for one mesh.</summary>
    class MB
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> tris = new List<int>();

        public int Add(Vector3 p, Vector3 normal, Vector2 t)
        {
            v.Add(p); n.Add(normal); uv.Add(t);
            return v.Count - 1;
        }

        /// <summary>
        /// Adds a triangle, wound so that its face normal agrees with its vertex normals. In Unity
        /// the front face of (a, b, c) has the normal Cross(b - a, c - a): the built-in Quad's
        /// triangle (0, 3, 1) gives (0, 0, -1), its vertex normal. Zero-area triangles (collapsed
        /// corners) are dropped.
        /// </summary>
        public void Tri(int a, int b, int c)
        {
            Vector3 g = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (g.sqrMagnitude < 1e-16f) return;
            if (Vector3.Dot(g, n[a] + n[b] + n[c]) < 0f) { int t = b; b = c; c = t; }
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        /// <summary>Joins two rings of the same length with a band of quads.</summary>
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

        /// <summary>Closes a ring with a fan around a centre vertex.</summary>
        public void Fan(int centre, int[] ring)
        {
            for (int j = 0; j < ring.Length; j++) Tri(centre, ring[j], ring[(j + 1) % ring.Length]);
        }

        public void Bounds(out Vector3 mn, out Vector3 mx)
        {
            mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
            for (int i = 0; i < v.Count; i++) { mn = Vector3.Min(mn, v[i]); mx = Vector3.Max(mx, v[i]); }
        }

        public void Shift(Vector3 d)
        {
            for (int i = 0; i < v.Count; i++) v[i] += d;
        }

        public void Fill(Mesh mesh)
        {
            mesh.Clear();
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
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
    /// One closed loop of 4 x (seg + 1) vertices around a rounded rectangle centred on (cx, cy) with
    /// half sizes (hw, hh) at depth z. Corner c covers the angles c x 90 .. c x 90 + 90 degrees:
    /// 0 = (+x, +y), 1 = (-x, +y), 2 = (-x, -y), 3 = (+x, -y); rx[c], ry[c] are its radii (0 = a
    /// sharp corner: its vertices coincide). Each vertex normal is (dir.x * nxy, dir.y * nxy, nz)
    /// normalised, where dir is the outward direction of the outline at that vertex: nxy = 1, nz = 0
    /// is an outer wall; nxy = -1 an inner wall; nxy = 0, nz = -1 a face toward the reader.
    /// UVs are a planar projection over (uvW, uvH), like Unity's Quad.
    /// </summary>
    static int[] Ring(MB mb, float cx, float cy, float hw, float hh, float[] rx, float[] ry, float z,
        float nxy, float nz, float uvW, float uvH, int seg)
    {
        var idx = new int[4 * (seg + 1)];
        int k = 0;
        for (int c = 0; c < 4; c++)
        {
            float sx = (c == 0 || c == 3) ? 1f : -1f;
            float sy = c < 2 ? 1f : -1f;
            float crx = Mathf.Clamp(rx[c], 0f, hw), cry = Mathf.Clamp(ry[c], 0f, hh);
            for (int s = 0; s <= seg; s++)
            {
                float a = (c * 90f + 90f * s / seg) * Mathf.Deg2Rad;
                float dx = Mathf.Cos(a), dy = Mathf.Sin(a);
                if (s == 0 || s == seg) { dx = Mathf.Round(dx); dy = Mathf.Round(dy); }   // exact axes: flat walls stay flat
                var p = new Vector3(cx + sx * (hw - crx) + crx * dx, cy + sy * (hh - cry) + cry * dy, z);
                var normal = new Vector3(dx * nxy, dy * nxy, nz).normalized;
                var t = new Vector2((p.x - cx) / uvW + 0.5f, (p.y - cy) / uvH + 0.5f);
                idx[k++] = mb.Add(p, normal, t);
            }
        }
        return idx;
    }

    static float[] Four(float r) => new[] { r, r, r, r };

    /// <summary>Same radius on every corner.</summary>
    static int[] Ring(MB mb, float cx, float cy, float hw, float hh, float r, float z, float nxy, float nz, float uvW, float uvH)
    {
        var rr = Four(Mathf.Max(0f, r));
        return Ring(mb, cx, cy, hw, hh, rr, rr, z, nxy, nz, uvW, uvH, CornerSegs);
    }

    static Mesh Cached(string name) => _force ? null : AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/" + name + ".asset");

    /// <summary>Creates the mesh asset, or refills the existing one in place (references stay valid).</summary>
    static Mesh Store(string name, MB mb)
    {
        string path = MeshDir + "/" + name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool isNew = mesh == null;
        if (isNew) mesh = new Mesh { name = name };
        mb.Fill(mesh);
        if (isNew) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // ───────────────────────────── the square key, measured ─────────────────────────────

    class Square
    {
        public bool ok; public string why = "";
        public Vector3 capMin, capMax, houMin, houMax;
        public float bevel, wall, chamfer, backPlate;
        public string measured = "";
    }

    static Mesh FirstMesh(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault();

    /// <summary>
    /// Measures the square cap and housing (UI_PokeKey_Cap.fbx / UI_PokeKey_Housing.fbx, the meshes
    /// every key in the scene uses, in their own mesh space, which is key space: Cap3D and Housing3D
    /// have identity rotation and unit scale). The depth axis must be z.
    /// </summary>
    static Square MeasureSquare()
    {
        var sq = new Square();
        Mesh cap = FirstMesh(ModelDir + "/UI_PokeKey_Cap.fbx"), hou = FirstMesh(ModelDir + "/UI_PokeKey_Housing.fbx");
        if (cap == null || hou == null) { sq.why = "UI_PokeKey_Cap.fbx / UI_PokeKey_Housing.fbx not found"; return sq; }
        sq.capMin = cap.bounds.min; sq.capMax = cap.bounds.max;
        sq.houMin = hou.bounds.min; sq.houMax = hou.bounds.max;
        Vector3 cs = cap.bounds.size, hs = hou.bounds.size;
        if (!(cs.z < cs.x && cs.z < cs.y) || !(hs.z < hs.x && hs.z < hs.y))
        {
            sq.why = "the square key meshes do not have z as their depth axis (cap " + cs.ToString("F3") + ", housing " + hs.ToString("F3") + ")";
            return sq;
        }
        float k = hs.y / 1.20f;                      // the generator's units to this mesh's units

        // Cap bevel: the flat front face (the vertices at the smallest z) stops one bevel short of the side.
        sq.bevel = DefCapBevel * k;
        string bevelFrom = "default";
        try
        {
            float flat = 0f; Vector3 c = cap.bounds.center;
            foreach (var p in cap.vertices)
                if (p.z < sq.capMin.z + 1e-4f * cs.z) flat = Mathf.Max(flat, Mathf.Abs(p.x - c.x));
            float b = cs.x * 0.5f - flat;
            if (flat > 0f && b > 0.01f * cs.x && b < 0.3f * cs.x) { sq.bevel = b; bevelFrom = "measured"; }
        }
        catch (System.Exception) { }

        // Housing wall: the front half of the mesh is only walls; the vertex nearest the centre line
        // (largest of |x|, |y|) lies on the inner face of a wall.
        sq.wall = DefWall * k;
        string wallFrom = "default";
        try
        {
            float inner = float.MaxValue; Vector3 c = hou.bounds.center;
            float zMid = sq.houMin.z + 0.5f * hs.z;
            foreach (var p in hou.vertices)
                if (p.z < zMid) inner = Mathf.Min(inner, Mathf.Max(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)));
            float w = hs.x * 0.5f - inner;
            if (inner < float.MaxValue && w > 0.03f * hs.x && w < 0.25f * hs.x) { sq.wall = w; wallFrom = "measured"; }
        }
        catch (System.Exception) { }

        sq.chamfer = Mathf.Min(DefChamfer * k, sq.wall / 3f);
        sq.backPlate = DefBackPlate * k;
        sq.ok = true;
        sq.measured = "square cap " + cs.ToString("F3") + " bevel " + F(sq.bevel, "F3") + " (" + bevelFrom + "); square housing " + hs.ToString("F3")
                      + " wall " + F(sq.wall, "F3") + " (" + wallFrom + "), front z " + F(sq.houMin.z, "F3") + "; rim (housing - cap) / 2 = " + F((hs.x - cs.x) * 0.5f, "F3");
        return sq;
    }

    // ───────────────────────────── procedural wide key ─────────────────────────────

    /// <summary>
    /// Wide cap: the square cap's rounded box, widened by <paramref name="extra"/> in x only. Same
    /// centre, same z range (front = smallest z), same bevel; the plan corners have the bevel's
    /// radius, like the square model (a Blender bevel on every edge). Built from rings:
    ///   front face (flat) -> quarter-round bevel -> side wall -> flat back.
    /// A bevel ring at angle phi (0 at the face, 90 degrees at the wall) is the outline inset by
    /// b (1 - sin phi) at depth zFront + b (1 - cos phi), with normal (dir sin phi, -cos phi).
    /// </summary>
    static void BuildWideCap(MB mb, Square sq, float extra)
    {
        Vector3 c = (sq.capMin + sq.capMax) * 0.5f;
        float hw = (sq.capMax.x - sq.capMin.x) * 0.5f + extra * 0.5f, hh = (sq.capMax.y - sq.capMin.y) * 0.5f;
        float zF = sq.capMin.z, zB = sq.capMax.z;
        float b = Mathf.Min(sq.bevel, Mathf.Min(hh * 0.9f, (zB - zF) * 0.45f));
        float uw = hw * 2f, uh = hh * 2f;

        // Front face: the outline inset by the bevel (its corners are sharp: radius b - b = 0).
        int centre = mb.Add(new Vector3(c.x, c.y, zF), Vector3.back, new Vector2(0.5f, 0.5f));
        mb.Fan(centre, Ring(mb, c.x, c.y, hw - b, hh - b, 0f, zF, 0f, -1f, uw, uh));

        // Bevel.
        int[] prev = null;
        for (int i = 0; i <= BevelSegs; i++)
        {
            float phi = i * Mathf.PI * 0.5f / BevelSegs;
            float d = b * (1f - Mathf.Sin(phi));
            float nxy = Mathf.Sin(phi), nz = -Mathf.Cos(phi);
            if (i == BevelSegs) { d = 0f; nxy = 1f; nz = 0f; }
            int[] ring = Ring(mb, c.x, c.y, hw - d, hh - d, b - d, zF + b * (1f - Mathf.Cos(phi)), nxy, nz, uw, uh);
            if (prev != null) mb.Band(prev, ring);
            prev = ring;
        }

        // Side wall down to the back, and the back face.
        mb.Band(prev, Ring(mb, c.x, c.y, hw, hh, b, zB, 1f, 0f, uw, uh));
        int back = mb.Add(new Vector3(c.x, c.y, zB), Vector3.forward, new Vector2(0.5f, 0.5f));
        mb.Fan(back, Ring(mb, c.x, c.y, hw, hh, b, zB, 0f, 1f, uw, uh));
    }

    /// <summary>
    /// Wide housing: the square housing's frame, widened by <paramref name="extra"/> in x only, so
    /// the wall, and therefore the rim around the cap, keeps its absolute width. Rings, from the
    /// outside in: outer wall, front chamfer, flat rim, inner chamfer, inner wall, the floor of the
    /// well, and a flat back. Same z range as the square housing (front rim = smallest z).
    /// </summary>
    static void BuildWideHousing(MB mb, Square sq, float extra)
    {
        Vector3 c = (sq.houMin + sq.houMax) * 0.5f;
        float HW = (sq.houMax.x - sq.houMin.x) * 0.5f + extra * 0.5f, HH = (sq.houMax.y - sq.houMin.y) * 0.5f;
        float zF = sq.houMin.z, zB = sq.houMax.z;
        float t = sq.wall, ch = sq.chamfer;
        float iw = HW - t, ih = HH - t;
        float zFloor = Mathf.Max(zF + 2f * ch, zB - sq.backPlate);
        float uw = HW * 2f, uh = HH * 2f;
        const float k = 0.70710678f;

        // Outer wall (its plan corners carry the chamfer's radius, as the model's bevel does).
        mb.Band(Ring(mb, c.x, c.y, HW, HH, ch, zB, 1f, 0f, uw, uh), Ring(mb, c.x, c.y, HW, HH, ch, zF + ch, 1f, 0f, uw, uh));
        // Outer front chamfer.
        mb.Band(Ring(mb, c.x, c.y, HW, HH, ch, zF + ch, k, -k, uw, uh), Ring(mb, c.x, c.y, HW - ch, HH - ch, 0f, zF, k, -k, uw, uh));
        // Flat rim.
        mb.Band(Ring(mb, c.x, c.y, HW - ch, HH - ch, 0f, zF, 0f, -1f, uw, uh), Ring(mb, c.x, c.y, iw + ch, ih + ch, 0f, zF, 0f, -1f, uw, uh));
        // Inner chamfer into the well.
        mb.Band(Ring(mb, c.x, c.y, iw + ch, ih + ch, 0f, zF, -k, -k, uw, uh), Ring(mb, c.x, c.y, iw, ih, 0f, zF + ch, -k, -k, uw, uh));
        // Inner wall.
        mb.Band(Ring(mb, c.x, c.y, iw, ih, 0f, zF + ch, -1f, 0f, uw, uh), Ring(mb, c.x, c.y, iw, ih, 0f, zFloor, -1f, 0f, uw, uh));
        // Floor of the well (faces the reader).
        int floor = mb.Add(new Vector3(c.x, c.y, zFloor), Vector3.back, new Vector2(0.5f, 0.5f));
        mb.Fan(floor, Ring(mb, c.x, c.y, iw, ih, 0f, zFloor, 0f, -1f, uw, uh));
        // Back.
        int back = mb.Add(new Vector3(c.x, c.y, zB), Vector3.forward, new Vector2(0.5f, 0.5f));
        mb.Fan(back, Ring(mb, c.x, c.y, HW, HH, ch, zB, 0f, 1f, uw, uh));
    }

    // ───────────────────────────── Antigravity wide key models, baked ─────────────────────────────

    class Variant
    {
        public string name; public bool ok; public string why = "";
        public Mesh cap, rim, housing;
        public float ratio;                    // housing width / height
        public Vector3 capSize, houSize;
    }

    static int MapVertex(Dictionary<int, int> map, int i, MB dst, Matrix4x4 m, Vector3[] vs, Vector3[] ns, Vector2[] us)
    {
        int j;
        if (map.TryGetValue(i, out j)) return j;
        Vector3 normal = ns != null && ns.Length == vs.Length ? m.MultiplyVector(ns[i]).normalized : Vector3.back;
        Vector2 t = us != null && us.Length == vs.Length ? us[i] : Vector2.zero;
        j = dst.Add(m.MultiplyPoint3x4(vs[i]), normal, t);
        map[i] = j;
        return j;
    }

    /// <summary>
    /// Bakes every mesh under an imported model into the model's root space (so the FBX axis
    /// conversion, unit scale and child offsets are all applied, whatever the import settings).
    /// Parts whose object or material name contains "StateRim" go to <paramref name="rim"/> when
    /// one is given; everything else goes to <paramref name="body"/>.
    /// </summary>
    static void Bake(GameObject model, MB body, MB rim)
    {
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) continue;
            var rend = mf.GetComponent<MeshRenderer>();
            Material[] mats = rend != null ? rend.sharedMaterials : new Material[0];
            Matrix4x4 m = mf.transform.localToWorldMatrix;
            bool flip = m.determinant < 0f;
            Vector3[] vs = mesh.vertices; Vector3[] ns = mesh.normals; Vector2[] us = mesh.uv;
            if (vs == null || vs.Length == 0) continue;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                string matName = sub < mats.Length && mats[sub] != null ? mats[sub].name : "";
                bool isRim = rim != null && (mf.name.IndexOf("StateRim", System.StringComparison.OrdinalIgnoreCase) >= 0
                                             || matName.IndexOf("StateRim", System.StringComparison.OrdinalIgnoreCase) >= 0);
                MB dst = isRim ? rim : body;
                var map = new Dictionary<int, int>();
                int[] tri = mesh.GetTriangles(sub);
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    int a = MapVertex(map, tri[i], dst, m, vs, ns, us);
                    int b = MapVertex(map, tri[i + 1], dst, m, vs, ns, us);
                    int c = MapVertex(map, tri[i + 2], dst, m, vs, ns, us);
                    if (flip) { int t = b; b = c; c = t; }
                    dst.tris.Add(a); dst.tris.Add(b); dst.tris.Add(c);      // the model's own winding
                }
            }
        }
    }

    /// <summary>
    /// Loads UI_PokeKey_&lt;name&gt;_Cap.fbx / _Housing.fbx, bakes them into key space, checks that they
    /// measure like a key (z is the depth axis; cap depth and housing depth within 20 percent of
    /// the square key's; height within 25 percent), then aligns them to the square key: same cap
    /// front z, same housing rim z, same centres. That keeps the press travel and the cap-proud-of-
    /// rim relation of the square key, which the builder measures again per key anyway.
    /// </summary>
    static Variant BakeVariant(string name, Square sq)
    {
        var va = new Variant { name = name };
        // Already baked by an earlier run: reuse (Rebuild bakes again).
        Mesh cachedCap = Cached(KeyMeshPrefix + "Cap_fbx" + name), cachedHou = Cached(KeyMeshPrefix + "Housing_fbx" + name);
        if (cachedCap != null && cachedHou != null && cachedHou.bounds.size.y > 1e-6f)
        {
            va.cap = cachedCap; va.housing = cachedHou;
            va.rim = Cached(KeyMeshPrefix + "CapRim_fbx" + name);
            va.capSize = cachedCap.bounds.size; va.houSize = cachedHou.bounds.size;
            va.ratio = va.houSize.x / va.houSize.y;
            va.ok = true;
            va.why = "cached";
            return va;
        }
        var capModel =AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + "/UI_PokeKey_" + name + "_Cap.fbx");
        var houModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + "/UI_PokeKey_" + name + "_Housing.fbx");
        if (capModel == null || houModel == null) { va.why = "model not found or not imported"; return va; }

        MB cap = new MB(), rim = new MB(), hou = new MB();
        try { Bake(capModel, cap, rim); Bake(houModel, hou, null); }
        catch (System.Exception ex) { va.why = "could not read the model meshes: " + ex.Message; return va; }
        if (cap.tris.Count == 0 || hou.tris.Count == 0) { va.why = "no readable geometry"; return va; }

        Vector3 cmn, cmx, hmn, hmx;
        cap.Bounds(out cmn, out cmx); hou.Bounds(out hmn, out hmx);
        Vector3 cs = cmx - cmn, hs = hmx - hmn;
        Vector3 sqCap = sq.capMax - sq.capMin, sqHou = sq.houMax - sq.houMin;
        string sizes = "cap " + cs.ToString("F3") + ", housing " + hs.ToString("F3");
        if (!(cs.z < cs.x && cs.z < cs.y) || !(hs.z < hs.x && hs.z < hs.y)) { va.why = "z is not the depth axis after import (" + sizes + ")"; return va; }
        if (Mathf.Abs(cs.z - sqCap.z) > 0.2f * sqCap.z || Mathf.Abs(hs.z - sqHou.z) > 0.2f * sqHou.z) { va.why = "depth differs from the square key by more than 20 percent (" + sizes + ")"; return va; }
        if (Mathf.Abs(cs.y - sqCap.y) > 0.25f * sqCap.y || Mathf.Abs(hs.y - sqHou.y) > 0.25f * sqHou.y) { va.why = "height differs from the square key by more than 25 percent (" + sizes + "): wrong unit scale?"; return va; }
        if (hs.x <= cs.x || hs.y <= cs.y) { va.why = "the housing is not larger than the cap (" + sizes + ")"; return va; }

        // Align to the square key.
        Vector3 sqCapC = (sq.capMin + sq.capMax) * 0.5f, sqHouC = (sq.houMin + sq.houMax) * 0.5f;
        var capShift = new Vector3(sqCapC.x - (cmn.x + cmx.x) * 0.5f, sqCapC.y - (cmn.y + cmx.y) * 0.5f, sq.capMin.z - cmn.z);
        cap.Shift(capShift); rim.Shift(capShift);
        hou.Shift(new Vector3(sqHouC.x - (hmn.x + hmx.x) * 0.5f, sqHouC.y - (hmn.y + hmx.y) * 0.5f, sq.houMin.z - hmn.z));

        va.cap = Store(KeyMeshPrefix + "Cap_fbx" + name, cap);
        va.housing = Store(KeyMeshPrefix + "Housing_fbx" + name, hou);
        va.rim = rim.tris.Count > 0 ? Store(KeyMeshPrefix + "CapRim_fbx" + name, rim) : null;
        va.capSize = cs; va.houSize = hs;
        va.ratio = hs.x / hs.y;
        va.ok = true;
        return va;
    }

    // ───────────────────────────── choosing meshes for a ratio ─────────────────────────────

    class KeyMeshes { public Mesh cap, rim, housing; public string source; public float ratio; }

    static Square _square;
    static List<Variant> _variants;
    static Dictionary<string, KeyMeshes> _meshCache;

    static string RatioText(float ratio) => ratio.ToString("0.##", Inv);

    static KeyMeshes MeshesFor(float ratio, StringBuilder sb)
    {
        string key = RatioText(ratio);
        KeyMeshes km;
        if (_meshCache.TryGetValue(key, out km)) return km;
        km = null;
        if (_square.ok)
        {
            // An Antigravity model, when one is close enough.
            Variant best = null;
            foreach (var va in _variants)
                if (va.ok && (best == null || Mathf.Abs(va.ratio - ratio) < Mathf.Abs(best.ratio - ratio))) best = va;
            if (best != null && Mathf.Abs(best.ratio - ratio) <= FbxTolerance * ratio)
                km = new KeyMeshes { cap = best.cap, rim = best.rim, housing = best.housing, ratio = best.ratio,
                                     source = "Antigravity UI_PokeKey_" + best.name + " (ratio " + F(best.ratio, "F2") + ")" };
            else
            {
                float extra = (ratio - 1f) * (_square.houMax.y - _square.houMin.y);
                string capName = KeyMeshPrefix + "Cap_r" + key, houName = KeyMeshPrefix + "Housing_r" + key;
                Mesh cap = Cached(capName), hou = Cached(houName);
                if (cap == null) { var mb = new MB(); BuildWideCap(mb, _square, extra); cap = Store(capName, mb); sb.AppendLine("  generated " + capName + " (" + mb.v.Count + " vertices, " + mb.tris.Count / 3 + " triangles)"); }
                if (hou == null) { var mb = new MB(); BuildWideHousing(mb, _square, extra); hou = Store(houName, mb); sb.AppendLine("  generated " + houName + " (" + mb.v.Count + " vertices, " + mb.tris.Count / 3 + " triangles)"); }
                km = new KeyMeshes { cap = cap, rim = null, housing = hou, ratio = ratio, source = "procedural r" + key };
            }
        }
        _meshCache[key] = km;
        return km;
    }

    static Material RimMaterial()
    {
        string path = MatDir + "/Key_StateRim.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            Color c = Color.Lerp(OfficeTokens.Housing, OfficeTokens.OnPlate, 0.35f);   // quiet at rest; lights up on hover
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            m.SetFloat("_Smoothness", 0.35f);
            m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }

    // ───────────────────────────── keys ─────────────────────────────

    /// <summary>
    /// Extents of a part's own mesh in the poke surface frame, in world metres:
    /// x along the surface's right, y along its up, z TOWARD THE USER (minus its forward), all
    /// measured from the surface origin. This is the frame XrVisualQa check 6 uses.
    /// </summary>
    static bool Ext(Transform part, Transform baseTf, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        var mf = part != null ? part.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null) return false;
        Bounds b = mf.sharedMesh.bounds;
        Vector3 o = baseTf.position, r = baseTf.right, u = baseTf.up, f = -baseTf.forward;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 w = part.TransformPoint(corner) - o;
            var l = new Vector3(Vector3.Dot(w, r), Vector3.Dot(w, u), Vector3.Dot(w, f));
            mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
        }
        return true;
    }

    static bool NonUniformXY(Transform t)
    {
        float x = Mathf.Abs(t.localScale.x), y = Mathf.Abs(t.localScale.y);
        return Mathf.Abs(x - y) > 0.01f * Mathf.Max(x, y);
    }

    /// <summary>
    /// A wrapper is an object that exists only to hold one key (Btn_Approve around its building
    /// block): no components of its own, exactly one poke key below it, and either a Btn_ name or
    /// a single child.
    /// </summary>
    static bool IsWrapper(Transform p)
    {
        if (p == null || p.name == "WorkspaceRoot" || p.name == "DecisionDocument") return false;
        if (p.GetComponents<Component>().Length != 1) return false;
        if (!p.name.StartsWith("Btn_") && p.childCount != 1) return false;
        return CompsBelow(p, "PokeInteractable").Count == 1;
    }

    static bool MarkerRatio(List<Transform> chain, Transform key, out float ratio, out Transform marker)
    {
        ratio = 1f; marker = null;
        var candidates = new List<Transform>(key.GetComponentsInChildren<Transform>(true));
        for (int i = 1; i < chain.Count; i++)
            foreach (Transform c in chain[i]) candidates.Add(c);
        foreach (var t in candidates)
        {
            if (t == null || !t.name.StartsWith(MarkerPrefix)) continue;
            float r;
            if (float.TryParse(t.name.Substring(MarkerPrefix.Length), NumberStyles.Float, Inv, out r) && r > 0f)
            {
                ratio = r; marker = t;
                return true;
            }
        }
        return false;
    }

    static Transform SurfaceBase(Transform key)
    {
        foreach (var mb in CompsBelow(key, "PokeInteractableVisual"))
        {
            var p = new SerializedObject(mb).FindProperty("_buttonBaseTransform");
            if (p != null && p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue is Transform)
                return (Transform)p.objectReferenceValue;
        }
        return key;       // the project's keys put the poke surface at key-local z = 0
    }

    static void Capture(KitKeyRecord rec, Transform key, List<Transform> chain, Transform cap, Transform housing, TMP_Text label)
    {
        if (rec.captured) return;
        var capMf = cap.GetComponent<MeshFilter>();
        rec.capMesh = capMf != null ? capMf.sharedMesh : null;
        var houMf = housing != null ? housing.GetComponent<MeshFilter>() : null;
        rec.housingMesh = houMf != null ? houMf.sharedMesh : null;
        rec.hasHousing = housing != null;
        rec.housingLocalPosition = housing != null ? housing.localPosition : Vector3.zero;
        rec.scaledTransforms = chain.ToArray();
        rec.scalesBefore = chain.Select(t => t.localScale).ToArray();
        var clippers = CompsBelow(key, "BoundsClipper");
        rec.clippers = clippers.Select(c => (Component)c).ToArray();
        rec.clipperSizes = clippers.Select(c =>
        {
            var p = new SerializedObject(c).FindProperty("_size");
            return p != null ? p.vector3Value : Vector3.one;
        }).ToArray();
        rec.colliders = key.GetComponentsInChildren<BoxCollider>(true);
        rec.colliderSizes = rec.colliders.Select(c => c.size).ToArray();
        rec.label = label;
        if (label != null)
        {
            rec.labelSize = label.rectTransform.sizeDelta;
            rec.labelScale = label.transform.localScale;
            rec.labelPosition = label.transform.localPosition;
        }
        rec.disabledByKit = new Behaviour[0];
        rec.captured = true;
        EditorUtility.SetDirty(rec);
    }

    static void DisableAndRecord(KitKeyRecord rec, Behaviour b, string what, List<string> notes)
    {
        if (b == null || !b.enabled) return;
        Undo.RecordObject(b, "kit: disable " + what);
        b.enabled = false;
        EditorUtility.SetDirty(b);
        var list = new List<Behaviour>(rec.disabledByKit != null ? rec.disabledByKit : new Behaviour[0]);
        if (!list.Contains(b)) list.Add(b);
        rec.disabledByKit = list.ToArray();
        EditorUtility.SetDirty(rec);
        notes.Add(what + " disabled");
    }

    static void Keys(List<Transform> all, StringBuilder sb)
    {
        sb.AppendLine("== KEYS ==");
        _square = MeasureSquare();
        sb.AppendLine(_square.ok ? "  " + _square.measured : "  WARNING: " + _square.why + ": wide keys are left square");
        _variants = new List<Variant>();
        _meshCache = new Dictionary<string, KeyMeshes>();
        if (_square.ok)
            foreach (var name in FbxVariants)
            {
                var va = BakeVariant(name, _square);
                _variants.Add(va);
                sb.AppendLine(va.ok
                    ? "  Antigravity UI_PokeKey_" + name + ": cap " + va.capSize.ToString("F3") + ", housing " + va.houSize.ToString("F3") + ", ratio " + F(va.ratio, "F2") + (va.rim != null ? ", with state rim" : ", no state rim found") + (va.why == "cached" ? "; baked earlier (cached)" : "; baked and aligned to the square key")
                    : "  Antigravity UI_PokeKey_" + name + " not used: " + va.why);
            }

        int count = 0, wide = 0;
        foreach (var key in all)
        {
            if (key == null || Comp(key, "PokeInteractable") == null) continue;
            Transform cap = FindDeep(key, CapName);
            if (cap == null) { sb.AppendLine("  " + PathOf(key) + ": no " + CapName + " (not a 3D key), skipped"); continue; }
            Transform visual = FindDeep(key, VisualName), housing = FindDeep(key, HousingName);
            var capRenderer = cap.GetComponent<MeshRenderer>();
            var capFilter = cap.GetComponent<MeshFilter>();
            if (capRenderer == null || capFilter == null) { sb.AppendLine("  " + PathOf(key) + ": " + CapName + " has no mesh renderer, skipped"); continue; }
            count++;
            var notes = new List<string>();

            TMP_Text label = visual != null ? visual.GetComponentInChildren<TMP_Text>(true) : null;
            if (label == null) label = key.GetComponentInChildren<TMP_Text>(true);

            // Key root plus the wrappers above it.
            var chain = new List<Transform> { key };
            for (var p = key.parent; p != null && IsWrapper(p); p = p.parent) chain.Add(p);

            var rec = key.GetComponent<KitKeyRecord>();
            if (rec == null) rec = Undo.AddComponent<KitKeyRecord>(key.gameObject);
            Capture(rec, key, chain, cap, housing, label);

            Transform baseTf = SurfaceBase(key);

            // ── is it wide? ──
            float ratio; Transform marker;
            bool hasMarker = MarkerRatio(chain, key, out ratio, out marker);
            var nonUniform = chain.Where(NonUniformXY).ToList();
            if (nonUniform.Count > 0)
            {
                // The footprint the other builder asked for, before the scale is made uniform.
                Vector3 fmn, fmx;
                float measured = 1f;
                if (Ext(housing != null ? housing : cap, baseTf, out fmn, out fmx) && fmx.y - fmn.y > 1e-9f)
                    measured = (fmx.x - fmn.x) / (fmx.y - fmn.y);
                var labels = key.GetComponentsInChildren<TMP_Text>(true);
                var labelWorld = labels.Select(l => l.transform.lossyScale).ToArray();
                foreach (var t in nonUniform)
                {
                    Vector3 s = t.localScale;
                    float y = Mathf.Abs(s.y);
                    Undo.RecordObject(t, "kit: uniform key scale");
                    t.localScale = new Vector3(s.x < 0f ? -y : y, s.y, s.z < 0f ? -y : y);
                    EditorUtility.SetDirty(t);
                    notes.Add(t.name + " scale " + s.ToString("F3") + " -> " + t.localScale.ToString("F3"));
                }
                // Labels compensated for the old scale: keep each one's world scale.
                for (int i = 0; i < labels.Length; i++)
                {
                    Transform lt = labels[i].transform;
                    Vector3 ps = lt.parent != null ? lt.parent.lossyScale : Vector3.one;
                    Undo.RecordObject(lt, "kit: label scale");
                    lt.localScale = new Vector3(labelWorld[i].x / Safe(ps.x), labelWorld[i].y / Safe(ps.y), labelWorld[i].z / Safe(ps.z));
                    EditorUtility.SetDirty(lt);
                }
                if (!hasMarker)
                {
                    ratio = Mathf.Round(Mathf.Clamp(measured, 1f, MaxWideRatio) * 100f) / 100f;
                    if (ratio >= MinWideRatio)
                    {
                        var go = new GameObject(MarkerPrefix + RatioText(ratio));
                        Undo.RegisterCreatedObjectUndo(go, "kit: wide marker");
                        go.transform.SetParent(key, false);
                        go.layer = key.gameObject.layer;
                        marker = go.transform; hasMarker = true;
                        rec.markerAddedByKit = true;
                        notes.Add("marker " + go.name + " added from the measured footprint");
                    }
                }
            }
            else if (!hasMarker)
            {
                // No marker and uniform: wide only if it already carries this builder's wide meshes.
                string mn = capFilter.sharedMesh != null ? capFilter.sharedMesh.name : "";
                if (mn.StartsWith(KeyMeshPrefix) && rec.wideRatio >= MinWideRatio) { ratio = rec.wideRatio; hasMarker = true; notes.Add("wide meshes without a marker: kept"); }
            }
            ratio = Mathf.Clamp(ratio, 1f, MaxWideRatio);
            bool isWide = hasMarker && ratio >= MinWideRatio;

            // ── wide geometry ──
            string source = "square (unchanged)";
            Renderer rimRenderer = null;
            if (isWide)
            {
                KeyMeshes km = MeshesFor(ratio, sb);
                if (km == null) { isWide = false; source = "square (wide r" + RatioText(ratio) + " requested, but the square key could not be measured)"; }
                else
                {
                    wide++;
                    source = km.source;
                    if (capFilter.sharedMesh != km.cap) { Undo.RecordObject(capFilter, "kit: wide cap"); capFilter.sharedMesh = km.cap; EditorUtility.SetDirty(capFilter); }
                    var houFilter = housing != null ? housing.GetComponent<MeshFilter>() : null;
                    if (houFilter != null && houFilter.sharedMesh != km.housing) { Undo.RecordObject(houFilter, "kit: wide housing"); houFilter.sharedMesh = km.housing; EditorUtility.SetDirty(houFilter); }

                    // Optional state collar of the Antigravity models: its own renderer under the cap.
                    Transform rim = cap.Find(RimName);
                    if (km.rim != null)
                    {
                        if (rim == null)
                        {
                            var go = new GameObject(RimName);
                            Undo.RegisterCreatedObjectUndo(go, "kit: state rim");
                            go.transform.SetParent(cap, false);
                            go.AddComponent<MeshFilter>();
                            go.AddComponent<MeshRenderer>();
                            rim = go.transform;
                        }
                        rim.gameObject.layer = cap.gameObject.layer;
                        rim.gameObject.SetActive(true);
                        rim.localPosition = Vector3.zero; rim.localRotation = Quaternion.identity; rim.localScale = Vector3.one;
                        rim.GetComponent<MeshFilter>().sharedMesh = km.rim;
                        var rr = rim.GetComponent<MeshRenderer>();
                        if (rr.sharedMaterial == null) rr.sharedMaterial = RimMaterial();
                        rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        rr.receiveShadows = false;
                        EditorUtility.SetDirty(rim.gameObject);
                        rimRenderer = rr;
                    }
                    else if (rim != null && rim.gameObject.activeSelf)
                    {
                        Undo.RecordObject(rim.gameObject, "kit: hide state rim");
                        rim.gameObject.SetActive(false);
                    }

                    // Cap footprint in key units (the cap may sit under a scaled parent in principle).
                    Vector3 capSize = km.cap.bounds.size;
                    float capW = capSize.x * Mathf.Abs(cap.lossyScale.x / Safe(key.lossyScale.x));
                    float capH = capSize.y * Mathf.Abs(cap.lossyScale.y / Safe(key.lossyScale.y));

                    // Poke surface: the clipper keeps its height and the margin it had around the cap.
                    foreach (var clip in CompsBelow(key, "BoundsClipper"))
                    {
                        var so = new SerializedObject(clip);
                        var p = so.FindProperty("_size");
                        if (p == null) { notes.Add("BoundsClipper has no _size (SDK changed?)"); continue; }
                        Vector3 size = p.vector3Value;
                        float kx = Mathf.Abs(clip.transform.lossyScale.x / Safe(key.lossyScale.x));
                        float ky = Mathf.Abs(clip.transform.lossyScale.y / Safe(key.lossyScale.y));
                        float want = (size.y * ky + (capW - capH)) / Safe(kx);
                        if (Mathf.Abs(size.x - want) > 1e-4f)
                        {
                            p.vector3Value = new Vector3(want, size.y, size.z);
                            so.ApplyModifiedProperties();
                            notes.Add("poke surface " + F(size.x * kx, "F2") + " -> " + F(want * kx, "F2") + " key units wide");
                        }
                    }
                    // Box colliders on the key (none on the standard key): widened the same way, but
                    // only when square or already at the wide size, so a deliberate shape is not lost.
                    foreach (var box in key.GetComponentsInChildren<BoxCollider>(true))
                    {
                        if (box.GetComponent<TMP_Text>() != null) continue;
                        float kx = Mathf.Abs(box.transform.lossyScale.x / Safe(key.lossyScale.x));
                        float ky = Mathf.Abs(box.transform.lossyScale.y / Safe(key.lossyScale.y));
                        float w = box.size.x * kx, h = box.size.y * ky, want = h + (capW - capH);
                        if (Mathf.Abs(w - want) < 1e-3f) continue;
                        if (Mathf.Abs(w - h) > 0.03f * Mathf.Max(w, h)) { notes.Add("collider on " + box.name + " is not square: left alone"); continue; }
                        Undo.RecordObject(box, "kit: wide collider");
                        box.size = new Vector3(want / Safe(kx), box.size.y, box.size.z);
                        EditorUtility.SetDirty(box);
                        notes.Add("collider on " + box.name + " widened");
                    }

                    // Cap proud of the rim at full press, measured as XrVisualQa does; the housing is
                    // moved back only if the new geometry breaks it.
                    Vector3 cmn, cmx, hmn, hmx;
                    if (visual != null && housing != null && Ext(cap, baseTf, out cmn, out cmx) && Ext(housing, baseTf, out hmn, out hmx))
                    {
                        float travel = Vector3.Dot(visual.position - baseTf.position, -baseTf.forward);
                        float margin = cmx.z - travel - hmx.z;
                        float target = ProudUnits * Mathf.Abs(key.lossyScale.z);
                        if (margin < target - 1e-6f * Mathf.Abs(key.lossyScale.z))
                        {
                            Undo.RecordObject(housing, "kit: housing depth");
                            housing.position += baseTf.forward * (target - margin);
                            EditorUtility.SetDirty(housing);
                            notes.Add("housing moved back so the cap stays proud at full press");
                        }
                    }

                    // Label: widened to the wide cap if it is narrower (never shrunk: the other builders
                    // already box their labels for the wide face), 0.5 mm in front of the cap face.
                    if (label != null)
                    {
                        float lx = Mathf.Abs(label.transform.lossyScale.x);
                        if (lx > 1e-12f)
                        {
                            float width = 0.90f * capW * Mathf.Abs(key.lossyScale.x) / lx;
                            Vector2 sd = label.rectTransform.sizeDelta;
                            if (sd.x > width * 1.02f) notes.Add("label box is " + F(sd.x / width * 90f, "F0") + " percent of the cap width (left as authored)");
                            if (sd.x < width * 0.98f)
                            {
                                Undo.RecordObject(label.rectTransform, "kit: wide label");
                                label.rectTransform.sizeDelta = new Vector2(width, sd.y);
                                EditorUtility.SetDirty(label.rectTransform);
                                notes.Add("label box " + F(sd.x, "F1") + " -> " + F(width, "F1"));
                            }
                        }
                        Vector3 fmn, fmx;
                        if (Ext(cap, baseTf, out fmn, out fmx))
                        {
                            // Hidden keys are scaled by 0.001: express the 0.5 mm at the key's shown size.
                            float shown = ShownScale(key).z / Safe(Mathf.Abs(key.lossyScale.z));
                            float want = fmx.z + 0.0005f / Safe(shown);
                            float now = Vector3.Dot(label.transform.position - baseTf.position, -baseTf.forward);
                            float off = (want - now) * shown;
                            if (Mathf.Abs(off) > 0.0002f && Mathf.Abs(off) < 0.004f)
                            {
                                Undo.RecordObject(label.transform, "kit: label depth");
                                label.transform.position += -baseTf.forward * (want - now);
                                EditorUtility.SetDirty(label.transform);
                                notes.Add("label set 0.5 mm proud of the cap face");
                            }
                        }
                    }
                }
            }
            rec.wideRatio = isWide ? ratio : 1f;
            rec.meshSource = source;
            EditorUtility.SetDirty(rec);

            // ── rest pose ──
            if (visual != null)
            {
                var guard = visual.GetComponent<PokeVisualRestGuard>();
                if (guard == null)
                {
                    guard = Undo.AddComponent<PokeVisualRestGuard>(visual.gameObject);
                    rec.guardAddedByKit = true;
                    notes.Add("PokeVisualRestGuard added");
                }
                if (!guard.hasAuthoredRest || (guard.restLocalPosition - visual.localPosition).sqrMagnitude > 1e-12f)
                {
                    Undo.RecordObject(guard, "kit: rest pose");
                    guard.restLocalPosition = visual.localPosition;
                    guard.hasAuthoredRest = true;
                    EditorUtility.SetDirty(guard);
                }
            }

            // ── state colours: one writer ──
            foreach (var icv in CompsBelow(key, "InteractableColorVisual")) DisableAndRecord(rec, icv, "InteractableColorVisual", notes);
            foreach (var other in CompsBelow(key, "PokeKeyStateVisual")) DisableAndRecord(rec, other, "PokeKeyStateVisual", notes);
            var capEditor = Comp(cap, "MaterialPropertyBlockEditor");
            if (capEditor != null)
            {
                var so = new SerializedObject(capEditor);
                var p = so.FindProperty("_updateEveryFrame");
                if (p != null && p.boolValue) { p.boolValue = false; so.ApplyModifiedProperties(); }
                DisableAndRecord(rec, capEditor, "cap MaterialPropertyBlockEditor", notes);
            }
            var ksv = key.GetComponent<KeyStateVisual>();
            if (ksv == null) { ksv = Undo.AddComponent<KeyStateVisual>(key.gameObject); notes.Add("KeyStateVisual added"); }
            if (ksv.capRenderer != capRenderer || ksv.rimRenderer != rimRenderer || ksv.label != label)
            {
                Undo.RecordObject(ksv, "kit: key state visual");
                ksv.capRenderer = capRenderer;
                ksv.rimRenderer = rimRenderer;
                ksv.label = label;
                EditorUtility.SetDirty(ksv);
            }

            // ── report ──
            Material mat = capRenderer.sharedMaterial;
            Color baseColor = Color.white;
            string matNote = "no material";
            if (mat != null)
            {
                if (mat.HasProperty("_BaseColor")) baseColor = mat.GetColor("_BaseColor");
                else if (mat.HasProperty("_Color")) baseColor = mat.GetColor("_Color");
                matNote = mat.name + (mat.enableInstancing ? "" : " (GPU instancing off)");
            }
            float shownK = ShownScale(key).x / Safe(Mathf.Abs(key.lossyScale.x));
            string geo = "not measured";
            Vector3 rcmn, rcmx, rhmn, rhmx;
            if (Ext(cap, baseTf, out rcmn, out rcmx))
            {
                geo = "cap " + F((rcmx.x - rcmn.x) * shownK * 1000f, "F1") + " x " + F((rcmx.y - rcmn.y) * shownK * 1000f, "F1") + " mm";
                if (visual != null)
                {
                    float travel = Vector3.Dot(visual.position - baseTf.position, -baseTf.forward);
                    geo += ", travel " + F(travel * shownK * 1000f, "F1") + " mm";
                    if (housing != null && Ext(housing, baseTf, out rhmn, out rhmx))
                    {
                        float margin = (rcmx.z - travel - rhmx.z) * shownK * 1000f;
                        geo += ", cap " + F(margin, "F1") + " mm proud of the rim at full press" + (margin < 0f ? "  WARNING: NOT PROUD" : "");
                    }
                }
            }
            sb.AppendLine("  " + PathOf(key));
            sb.AppendLine("      " + (isWide ? "WIDE r" + RatioText(ratio) + " from " + source : source) + (shownK > 1.5f ? " [hidden by scale: sizes are at the shown size]" : "") + "; " + geo);
            sb.AppendLine("      colours (" + matNote + "): normal " + Hex(baseColor) + ", hover " + Hex(KeyStateVisual.HoverColor(baseColor))
                          + ", pressed " + Hex(KeyStateVisual.PressedColor(baseColor)) + ", disabled " + Hex(KeyStateVisual.DisabledColor(baseColor)));
            if (notes.Count > 0) sb.AppendLine("      notes: " + string.Join("; ", notes));
        }
        sb.AppendLine("  " + count + " keys, " + wide + " wide");
    }

    // ───────────────────────────── plates ─────────────────────────────

    class Plate
    {
        public Transform t; public MeshFilter mf; public string path;
        public float w, h, depthScale;          // shown size in metres; shown z scale
        public float[] r = new float[4];        // corner radii in metres: 0 (+x,+y)  1 (-x,+y)  2 (-x,-y)  3 (+x,-y)
        public bool thin, mine;
        public string note = "";
    }

    static Vector3 PlateCorner(int c) => new Vector3((c == 0 || c == 3) ? 0.5f : -0.5f, c < 2 ? 0.5f : -0.5f, 0f);
    static int CornerIndex(bool right, bool top) => top ? (right ? 0 : 1) : (right ? 3 : 2);

    static bool IsPlateCandidate(MeshRenderer mr)
    {
        if (mr.GetComponent<TMP_Text>() != null) return false;
        if (Comp(mr.transform, "RoundedBoxProperties") != null) return false;       // the SDK's own rounded quad
        string n = mr.name;
        foreach (var s in PlateSuffixes) if (n.EndsWith(s, System.StringComparison.Ordinal)) return true;
        var m = mr.sharedMaterial;
        if (m != null)
            foreach (var p in PlateMaterialPrefixes) if (m.name.StartsWith(p, System.StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// If plate q lies on plate s (same plane within 3 mm, same facing, inside it, axis-aligned)
    /// and touches s's outline, gives q's corner radii: a corner that sits on a corner of s takes
    /// that corner's radius (so a full-bleed band follows the sheet's rounded corner exactly); a
    /// corner that sits on an edge of s stays square (it meets the edge flush); a free corner gets
    /// q's own radius. Compared in s's local space, where s is the unit square, so a parent that is
    /// hidden by scale does not disturb the 0.5 mm tolerance.
    /// </summary>
    static bool BandRadii(Plate q, Plate s, float own, float[] radii)
    {
        if (Vector3.Dot(q.t.forward, s.t.forward) < 0.99f) return false;
        float tx = 0.0005f / s.w, ty = 0.0005f / s.h;
        var p = new Vector3[4];
        for (int c = 0; c < 4; c++)
        {
            p[c] = s.t.InverseTransformPoint(q.t.TransformPoint(PlateCorner(c)));
            if (Mathf.Abs(p[c].z) * s.depthScale > 0.003f) return false;
            if (Mathf.Abs(p[c].x) > 0.5f + tx || Mathf.Abs(p[c].y) > 0.5f + ty) return false;
        }
        bool aligned = (Mathf.Abs(p[0].y - p[1].y) < ty && Mathf.Abs(p[2].y - p[3].y) < ty && Mathf.Abs(p[0].x - p[3].x) < tx && Mathf.Abs(p[1].x - p[2].x) < tx)
                    || (Mathf.Abs(p[0].x - p[1].x) < tx && Mathf.Abs(p[2].x - p[3].x) < tx && Mathf.Abs(p[0].y - p[3].y) < ty && Mathf.Abs(p[1].y - p[2].y) < ty);
        if (!aligned) return false;
        bool touches = false;
        for (int c = 0; c < 4; c++)
        {
            bool onX = Mathf.Abs(Mathf.Abs(p[c].x) - 0.5f) <= tx, onY = Mathf.Abs(Mathf.Abs(p[c].y) - 0.5f) <= ty;
            if (onX && onY) { radii[c] = s.r[CornerIndex(p[c].x > 0f, p[c].y > 0f)]; touches = true; }
            else if (onX || onY) { radii[c] = 0f; touches = true; }
            else radii[c] = own;
        }
        return touches;
    }

    /// <summary>Two corners on one edge cannot use more than the edge: scale all radii down together.</summary>
    static void FitRadii(float[] r, float w, float h)
    {
        float f = 1f;
        if (r[0] + r[1] > w) f = Mathf.Min(f, w / (r[0] + r[1]));       // top edge
        if (r[2] + r[3] > w) f = Mathf.Min(f, w / (r[2] + r[3]));       // bottom edge
        if (r[1] + r[2] > h) f = Mathf.Min(f, h / (r[1] + r[2]));       // left edge (-x)
        if (r[0] + r[3] > h) f = Mathf.Min(f, h / (r[0] + r[3]));       // right edge (+x)
        if (f < 1f) for (int i = 0; i < 4; i++) r[i] *= f;
    }

    static string HalfMm(float metres) => (Mathf.Round(metres * 2000f) / 2f).ToString("0.#", Inv);

    /// <summary>
    /// A rounded rectangle in the unit space of Unity's Quad: x and y in -0.5 .. 0.5, z = 0, normal
    /// (0, 0, -1) (visible from -Z, like the Quad), UV = position + 0.5. The object is scaled to
    /// (w, h, 1), so a corner of world radius r is an ellipse arc with radii r / w and r / h here.
    /// A centre vertex and one outline loop, triangulated as a fan (the outline is convex).
    /// </summary>
    static void BuildPlate(MB mb, float w, float h, float[] r)
    {
        var rx = new float[4]; var ry = new float[4];
        for (int c = 0; c < 4; c++) { rx[c] = r[c] / w; ry[c] = r[c] / h; }
        int centre = mb.Add(Vector3.zero, Vector3.back, new Vector2(0.5f, 0.5f));
        mb.Fan(centre, Ring(mb, 0f, 0f, 0.5f, 0.5f, rx, ry, 0f, 0f, -1f, 1f, 1f, CornerSegs));
    }

    static void Plates(List<Transform> all, StringBuilder sb)
    {
        sb.AppendLine("== PLATES ==");
        Mesh quad = BuiltinQuad();
        var roots = all.Where(t => t != null && (t.name == "WorkspaceRoot" || t.name == "DecisionDocument")).ToList();
        var seen = new HashSet<Transform>();
        var plates = new List<Plate>();
        foreach (var root in roots)
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || !seen.Add(mr.transform)) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                bool mine = mf.sharedMesh.name.StartsWith(PlateMeshPrefix);
                if (mf.sharedMesh != quad && !mine) continue;
                if (!IsPlateCandidate(mr)) continue;
                Vector3 s = ShownScale(mr.transform);
                plates.Add(new Plate { t = mr.transform, mf = mf, path = PathOf(mr.transform), w = s.x, h = s.y, depthScale = s.z, mine = mine });
            }
        plates = plates.OrderByDescending(p => p.w * p.h).ThenBy(p => p.path, System.StringComparer.Ordinal).ToList();

        int rounded = 0, kept = 0, unchanged = 0, generated = 0;
        var done = new List<Plate>();
        foreach (var q in plates)
        {
            string size = F(q.w * 1000f, "F1") + " x " + F(q.h * 1000f, "F1") + " mm";
            Mesh want = quad;
            string action;
            if (q.w < 0.004f || q.h < 0.004f)
            {
                q.thin = true;
                action = "kept square: thinner than 4 mm";
                // A thin band in a rounded sheet's corner will stand a little outside that corner.
                foreach (var s in done)
                {
                    var tmp = new float[4];
                    if (s.thin || !BandRadii(q, s, 0f, tmp)) continue;
                    float worst = Mathf.Max(Mathf.Max(tmp[0], tmp[1]), Mathf.Max(tmp[2], tmp[3]));
                    if (worst > 0f) action += " (sits in a corner of " + s.t.name + " rounded " + F(worst * 1000f, "F1") + " mm: its square corner shows about " + F(worst * 0.29f * 1000f, "F1") + " mm outside)";
                    break;
                }
            }
            else
            {
                float own = Mathf.Clamp(0.12f * Mathf.Min(q.w, q.h), 0.0015f, 0.006f);
                for (int c = 0; c < 4; c++) q.r[c] = own;
                // The smallest larger plate it lies on decides its corners.
                Plate sheet = null;
                var radii = new float[4];
                for (int i = done.Count - 1; i >= 0; i--)
                {
                    if (done[i].thin) continue;
                    var tmp = new float[4];
                    if (BandRadii(q, done[i], own, tmp)) { sheet = done[i]; radii = tmp; break; }
                }
                if (sheet != null) { q.r = radii; q.note = "band of " + sheet.t.name; }
                FitRadii(q.r, q.w, q.h);

                bool any = q.r[0] > 1e-5f || q.r[1] > 1e-5f || q.r[2] > 1e-5f || q.r[3] > 1e-5f;
                if (!any) action = "kept square: " + q.note + ", every corner meets the sheet's edge";
                else
                {
                    // Cache key: size to the millimetre, radii to half a millimetre.
                    int wmm = Mathf.Max(1, Mathf.RoundToInt(q.w * 1000f)), hmm = Mathf.Max(1, Mathf.RoundToInt(q.h * 1000f));
                    string[] rs = { HalfMm(q.r[0]), HalfMm(q.r[1]), HalfMm(q.r[2]), HalfMm(q.r[3]) };
                    bool same = rs[0] == rs[1] && rs[1] == rs[2] && rs[2] == rs[3];
                    string name = PlateMeshPrefix + wmm + "x" + hmm + "_r" + (same ? rs[0] : string.Join("-", rs));
                    want = Cached(name);
                    if (want == null)
                    {
                        var rr = new float[4];
                        for (int c = 0; c < 4; c++) rr[c] = float.Parse(rs[c], Inv) * 0.001f;
                        FitRadii(rr, wmm * 0.001f, hmm * 0.001f);
                        var mb = new MB();
                        BuildPlate(mb, wmm * 0.001f, hmm * 0.001f, rr);
                        want = Store(name, mb);
                        generated++;
                    }
                    action = (same ? "rounded " + rs[0] + " mm" : "rounded (+x+y " + rs[0] + ", -x+y " + rs[1] + ", -x-y " + rs[2] + ", +x-y " + rs[3] + ") mm")
                             + (q.note.Length > 0 ? ", " + q.note : "") + " -> " + name;
                }
            }
            if (q.mf.sharedMesh == want) { action += " [already]"; unchanged++; }
            else
            {
                Undo.RecordObject(q.mf, "kit: plate mesh");
                q.mf.sharedMesh = want;
                EditorUtility.SetDirty(q.mf);
            }
            if (want == quad) kept++; else rounded++;
            sb.AppendLine("  " + q.path + " | " + size + " | " + action);
            done.Add(q);
        }
        sb.AppendLine("  " + plates.Count + " plates: " + rounded + " rounded, " + kept + " kept square, " + unchanged + " already as wanted, " + generated + " meshes generated");
    }

    // ───────────────────────────── entry points ─────────────────────────────

    public static string Apply() => Run(false);

    /// <summary>As Apply, but regenerates every cached mesh asset in place (after a fix to the mesh code).</summary>
    public static string Rebuild() => Run(true);

    static string Run(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildKitV3: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildKitV3: no open scene.\n";
        _force = force;
        var sb = new StringBuilder();
        sb.AppendLine("BuildKitV3" + (force ? " (rebuilding cached meshes)" : ""));
        try
        {
            EnsureFolders();
            try { Keys(All(), sb); }
            catch (System.Exception ex) { sb.AppendLine("ERROR in the key pass: " + ex); }
            try { Plates(All(), sb); }
            catch (System.Exception ex) { sb.AppendLine("ERROR in the plate pass: " + ex); }
            AssetDatabase.SaveAssets();
        }
        finally { _force = false; }
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }

    /// <summary>
    /// Undoes this builder in the open scene: plates get Unity's Quad back; every key gets back the
    /// meshes, scales, poke surface, colliders, label box and components recorded in its
    /// KitKeyRecord; KeyStateVisual, the record, the state rim and markers this builder added are
    /// removed. Mesh assets are left on disk.
    /// </summary>
    public static string Revert()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildKitV3.Revert: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder("BuildKitV3.Revert\n");
        Mesh quad = BuiltinQuad();
        int plates = 0, keys = 0;
        foreach (var t in All())
        {
            if (t == null) continue;
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name.StartsWith(PlateMeshPrefix))
            {
                Undo.RecordObject(mf, "kit revert: plate");
                mf.sharedMesh = quad;
                EditorUtility.SetDirty(mf);
                plates++;
            }
        }
        foreach (var t in All())
        {
            if (t == null) continue;
            var rec = t.GetComponent<KitKeyRecord>();
            if (rec == null) continue;
            keys++;
            Transform cap = FindDeep(t, CapName), housing = FindDeep(t, HousingName), visual = FindDeep(t, VisualName);
            if (rec.captured)
            {
                var capMf = cap != null ? cap.GetComponent<MeshFilter>() : null;
                if (capMf != null && rec.capMesh != null) { capMf.sharedMesh = rec.capMesh; EditorUtility.SetDirty(capMf); }
                var houMf = housing != null ? housing.GetComponent<MeshFilter>() : null;
                if (houMf != null && rec.housingMesh != null) { houMf.sharedMesh = rec.housingMesh; EditorUtility.SetDirty(houMf); }
                if (housing != null && rec.hasHousing) { housing.localPosition = rec.housingLocalPosition; EditorUtility.SetDirty(housing); }
                if (rec.scaledTransforms != null && rec.scalesBefore != null)
                    for (int i = 0; i < rec.scaledTransforms.Length && i < rec.scalesBefore.Length; i++)
                        if (rec.scaledTransforms[i] != null) { rec.scaledTransforms[i].localScale = rec.scalesBefore[i]; EditorUtility.SetDirty(rec.scaledTransforms[i]); }
                if (rec.clippers != null && rec.clipperSizes != null)
                    for (int i = 0; i < rec.clippers.Length && i < rec.clipperSizes.Length; i++)
                    {
                        if (rec.clippers[i] == null) continue;
                        var so = new SerializedObject(rec.clippers[i]);
                        var p = so.FindProperty("_size");
                        if (p != null) { p.vector3Value = rec.clipperSizes[i]; so.ApplyModifiedProperties(); }
                    }
                if (rec.colliders != null && rec.colliderSizes != null)
                    for (int i = 0; i < rec.colliders.Length && i < rec.colliderSizes.Length; i++)
                        if (rec.colliders[i] != null) { rec.colliders[i].size = rec.colliderSizes[i]; EditorUtility.SetDirty(rec.colliders[i]); }
                if (rec.label != null)
                {
                    rec.label.rectTransform.sizeDelta = rec.labelSize;
                    rec.label.transform.localScale = rec.labelScale;
                    rec.label.transform.localPosition = rec.labelPosition;
                    EditorUtility.SetDirty(rec.label.transform);
                }
            }
            if (rec.disabledByKit != null)
                foreach (var b in rec.disabledByKit)
                {
                    if (b == null) continue;
                    b.enabled = true;
                    if (b.GetType().Name == "MaterialPropertyBlockEditor")
                    {
                        var so = new SerializedObject(b);
                        var p = so.FindProperty("_updateEveryFrame");
                        if (p != null) { p.boolValue = true; so.ApplyModifiedProperties(); }
                    }
                    EditorUtility.SetDirty(b);
                }
            Transform rim = cap != null ? cap.Find(RimName) : null;
            if (rim != null) Undo.DestroyObjectImmediate(rim.gameObject);
            if (rec.markerAddedByKit)
                foreach (var m in t.GetComponentsInChildren<Transform>(true).Where(x => x != null && x != t && x.name.StartsWith(MarkerPrefix)).ToList())
                    Undo.DestroyObjectImmediate(m.gameObject);
            if (rec.guardAddedByKit && visual != null)
            {
                var guard = visual.GetComponent<PokeVisualRestGuard>();
                if (guard != null) Undo.DestroyObjectImmediate(guard);
            }
            var ksv = t.GetComponent<KeyStateVisual>();
            if (ksv != null) Undo.DestroyObjectImmediate(ksv);
            sb.AppendLine("  restored " + PathOf(t));
            Undo.DestroyObjectImmediate(rec);
        }
        sb.AppendLine("  " + plates + " plates back to the Quad, " + keys + " keys restored");
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }
}
