using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Art;

/// <summary>
/// Final refinement, Office-Art part: adopts Antigravity's polished models
/// (Assets/MuseDesk/Art/Prefabs/FinalRefinement) as visual bodies in the open production scene,
/// element by element, WITHOUT moving any text, key, collider or script. For each element the
/// builder measures the current surface and the model (mesh data, in the element's own space),
/// decides adopt / skip from the constants below, and prints a table. An adopted element gets a
/// child "<Element>_ArtBody" (the unpacked prefab with its texts, keys, scripts and colliders
/// removed) and its old renderer is disabled; nothing else on the old object changes. A failed
/// check leaves that element exactly as it was. Revert() restores everything.
///   unity command script run --file AgentScripts/BuildOfficeArtV3.cs --entry BuildOfficeArtV3.Apply
///   unity command script run --file AgentScripts/BuildOfficeArtV3.cs --entry BuildOfficeArtV3.Revert
/// Details and the numbers behind each rule: docs/Design/ART_ADOPTION_NOTES.md
/// </summary>
public static class BuildOfficeArtV3
{
    // ───────────────────────────── switches and thresholds ─────────────────────────────
    const bool EnableFactory = true, EnableTrays = true, EnablePlates = true, EnableDock = true,
               EnableReceipt = true, EnablePaper = false /* lit folio body renders grey against the unlit cream plates: revisit with an unlit paper material */, EnableCard = true;

    const float MaxNonUniform = 0.05f;          // x scale vs y scale of a fitted body
    const float MaxScaleDeviation = 0.35f;      // how far from 1.0 a fit scale may be (plates, trays)
    const float CardFaceTolerance = 0.02f;      // task card: face size must match within 2 percent
    const float MaxOverhangMm = 3f;             // a model rim may grow past the old footprint by this much per side
    const float ProudMinMm = 0.3f, ProudMaxMm = 1.0f;   // text in front of the surface it is printed on
    const float ProudUnchangedTolMm = 0.02f;    // or: exactly as today (carried-scale cover text, the stamp mark)
    const float ProudBehindTolMm = 0.1f;        // a text further than this behind the old sheet is not printed on it
    const float MinOverlayProudMm = 0.05f;      // scene plates and bands must stay this far in front of the body
    const float OverlayMaxDepthMm = 0.5f;       // only flat scene plates count as printed surfaces (not stamps, bases, cubes)
    const float DockPocketTolMm = 2f;           // task dock: model key pockets vs the scene's keys
    const bool DockHidePocketsOnMismatch = false;       // false: a pocket mismatch skips the dock (the assignment's rule)
    const float PaperFeatureTolMm = 3f;         // decision paper: stamp brackets, key pockets
    const float ReceiptPocketTolMm = 2f;
    const int MaxTriangles = 6000;              // per adopted body, except the factory
    const float FactorySizeTolerance = 0.20f;   // polished factory mesh bounds vs the current one, per axis
    const bool FactoryUseOldAvatar = true;      // the new Animator takes the current Animator's avatar (same 14-bone rig)
    const bool FactoryReuseSceneMaterials = true;       // same-named material slots take the scene's current materials
    const float TrayAnchorBuryMaxMm = 1f;       // the stack anchor may be at most this far under the new tray's surface
    const float TrayAnchorFloatMaxMm = 8f;      // and at most this much further above it than today
    const float FiledKeyHalfMm = 24f;           // FiledPoke: centre to lower edge (0.6 x its 40 mm scale)

    const string PrefabDir = "Assets/MuseDesk/Art/Prefabs/FinalRefinement/";
    const string BodySuffix = "_ArtBody";
    const string PartSuffix = "_Art";           // body mesh parts are renamed so no other builder treats them as plates
    const string RestoreName = "ArtRestore";

    // ───────────────────────────── data ─────────────────────────────
    sealed class Feature
    {
        public string label, anchor, scene;     // model anchor (or point) vs a scene object, compared in x / y
        public bool usePoint; public Vector2 point;
        public float tolMm; public string[] hide = new string[0]; public bool mayHide;
    }

    sealed class Spec
    {
        public string element, prefab, mainPart, outerPart;
        public Transform root, frame, scope;
        public List<Renderer> old = new List<Renderer>();
        public string[] alsoHide = new string[0], hideParts = new string[0], decorParts = new string[0], deskParts = new string[0];
        public Feature[] features = new Feature[0];
        public float unit = 1f, maxScaleDev = MaxScaleDeviation;
    }

    sealed class Row
    {
        public string element, oldSize = "-", newSize = "-", scale = "-", offset = "-", decision = "SKIP", reason = "";
        public int tris, trisRemoved, draws;
    }

    struct Ref { public Component comp; public string path; }

    static Dictionary<UnityEngine.Object, List<Ref>> _refs;
    static StringBuilder _log;
    static Transform _current;                  // the body being built; removed again if its element throws

    // ───────────────────────────── small helpers ─────────────────────────────
    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    static Transform Find(List<Transform> all, string name) => all.FirstOrDefault(t => t != null && t.name == name);
    static Transform Under(Transform root, string name) =>
        root == null ? null : root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t.name == name);
    static string Mm(float metres) => (metres * 1000f).ToString("F1");
    static string Size2(float w, float h) => Mm(w) + " x " + Mm(h);
    static string Size3(Vector3 s) => Mm(s.x) + " x " + Mm(s.y) + " x " + Mm(s.z);
    static bool Has(string[] list, string name) { for (int i = 0; i < list.Length; i++) if (list[i] == name) return true; return false; }

    /// <summary>t-local to root-local, built from local transforms only (works when root is hidden by scale).</summary>
    static Matrix4x4 Chain(Transform t, Transform root)
    {
        var m = Matrix4x4.identity;
        for (var x = t; x != null && x != root; x = x.parent)
            m = Matrix4x4.TRS(x.localPosition, x.localRotation, x.localScale) * m;
        return m;
    }

    /// <summary>root-local to the metric space of 'frame': origin and axes of frame, unit scale. Front is -Z.</summary>
    static Matrix4x4 MetricInverse(Transform frame, Transform root)
    {
        var f = Chain(frame, root);
        Vector3 pos = f.GetColumn(3), fwd = f.GetColumn(2), up = f.GetColumn(1);
        var rot = (fwd.sqrMagnitude > 1e-14f && up.sqrMagnitude > 1e-14f) ? Quaternion.LookRotation(fwd, up) : Quaternion.identity;
        return Matrix4x4.TRS(pos, rot, Vector3.one).inverse;
    }

    static Vector3 ColumnScale(Matrix4x4 m)
    {
        Vector3 x = m.GetColumn(0), y = m.GetColumn(1), z = m.GetColumn(2);
        return new Vector3(x.magnitude, y.magnitude, z.magnitude);
    }

    static Mesh MeshOf(Transform t)
    {
        var mf = t.GetComponent<MeshFilter>();
        if (mf != null) return mf.sharedMesh;
        var smr = t.GetComponent<SkinnedMeshRenderer>();
        return smr != null ? smr.sharedMesh : null;
    }

    static int TriCount(Mesh mesh)
    {
        if (mesh == null) return 0;
        long n = 0;
        for (int i = 0; i < mesh.subMeshCount; i++) n += (long)mesh.GetIndexCount(i);
        return (int)(n / 3);
    }

    /// <summary>Appends the mesh's triangles, transformed by m, as vertex triples.</summary>
    static void AddMesh(List<Vector3> soup, Mesh mesh, Matrix4x4 m)
    {
        if (mesh == null) return;
        if (!mesh.isReadable)
        {
            // Not readable: its bounding box stands in (12 triangles).
            Bounds b = mesh.bounds;
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
                c[i] = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
            int[] q = { 0, 1, 3, 0, 3, 2, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 3, 7, 1, 7, 5 };
            for (int i = 0; i < q.Length; i++) soup.Add(c[q[i]]);
            return;
        }
        var vs = mesh.vertices; var ts = mesh.triangles;
        for (int i = 0; i < ts.Length; i++) soup.Add(m.MultiplyPoint3x4(vs[ts[i]]));
    }

    static bool SoupBounds(List<Vector3> soup, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        for (int i = 0; i < soup.Count; i++) { mn = Vector3.Min(mn, soup[i]); mx = Vector3.Max(mx, soup[i]); }
        return soup.Count > 0;
    }

    static bool PartBounds(Transform part, Matrix4x4 toMetric, Transform root, out Vector3 mn, out Vector3 mx)
    {
        var soup = new List<Vector3>();
        AddMesh(soup, MeshOf(part), toMetric * Chain(part, root));
        return SoupBounds(soup, out mn, out mx);
    }

    /// <summary>
    /// Where a line through (u, v) meets the triangles. axis 2: the line runs along z, (u, v) = (x, y).
    /// axis 1: along y, (u, v) = (x, z). Returns the smallest (front-most / lowest) or the largest hit.
    /// </summary>
    static bool Sample(List<Vector3> s, float u, float v, int axis, bool smallest, out float val)
    {
        val = smallest ? float.PositiveInfinity : float.NegativeInfinity;
        bool hit = false;
        for (int i = 0; i + 2 < s.Count; i += 3)
        {
            Vector3 a = s[i], b = s[i + 1], c = s[i + 2];
            float au = a.x, bu = b.x, cu = c.x, av, bv, cv, aw, bw, cw;
            if (axis == 2) { av = a.y; bv = b.y; cv = c.y; aw = a.z; bw = b.z; cw = c.z; }
            else { av = a.z; bv = b.z; cv = c.z; aw = a.y; bw = b.y; cw = c.y; }
            float d = (bv - cv) * (au - cu) + (cu - bu) * (av - cv);
            if (Mathf.Abs(d) < 1e-14f) continue;
            float l1 = ((bv - cv) * (u - cu) + (cu - bu) * (v - cv)) / d;
            float l2 = ((cv - av) * (u - cu) + (au - cu) * (v - cv)) / d;
            float l3 = 1f - l1 - l2;
            if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
            float w = l1 * aw + l2 * bw + l3 * cw;
            if (smallest ? w < val : w > val) val = w;
            hit = true;
        }
        return hit;
    }

    static bool HasKeyAncestor(Transform t, Transform stop)
    {
        for (var x = t; x != null && x != stop; x = x.parent)
            if (x.GetComponent("KitKeyRecord") != null || x.GetComponent("InteractableUnityEventWrapper") != null) return true;
        return false;
    }

    static bool UnderBody(Transform t)
    {
        for (var x = t; x != null; x = x.parent) if (x.name.EndsWith(BodySuffix, System.StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>True when t belongs to root's own model (not an object added under it, not another prefab, not a body).</summary>
    static bool OwnPart(Transform t, Transform root)
    {
        for (var x = t; x != null && x != root; x = x.parent)
        {
            if (x.name.EndsWith(BodySuffix, System.StringComparison.Ordinal)) return false;
            if (PrefabUtility.IsAddedGameObjectOverride(x.gameObject)) return false;
            var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(x.gameObject);
            if (outer != null && outer != root.gameObject) return false;
        }
        return true;
    }

    static Component FindComponentByTypeName(string typeName)
    {
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == typeName) return mb;
        return null;
    }

    // ───────────────────────────── serialized reference index ─────────────────────────────
    static void BuildRefIndex()
    {
        _refs = new Dictionary<UnityEngine.Object, List<Ref>>();
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb is TMP_Text) continue;
                var so = new SerializedObject(mb);
                var it = so.GetIterator();
                bool enter = true;
                while (it.Next(enter))
                {
                    enter = it.propertyType != SerializedPropertyType.String;      // do not walk strings character by character
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var v = it.objectReferenceValue;
                    if (v == null || v is MonoScript) continue;
                    if (!_refs.TryGetValue(v, out var list)) { list = new List<Ref>(); _refs[v] = list; }
                    list.Add(new Ref { comp = mb, path = it.propertyPath });
                }
            }
    }

    static List<Ref> RefsTo(UnityEngine.Object target, Transform ignoreUnder)
    {
        var res = new List<Ref>();
        if (_refs == null || target == null || !_refs.TryGetValue(target, out var list)) return res;
        foreach (var r in list)
            if (r.comp != null && (ignoreUnder == null || !r.comp.transform.IsChildOf(ignoreUnder))) res.Add(r);
        return res;
    }

    static void SetRef(Ref r, UnityEngine.Object value)
    {
        var so = new SerializedObject(r.comp);
        var p = so.FindProperty(r.path);
        if (p == null) return;
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(r.comp);
    }

    static string Describe(UnityEngine.Object o)
    {
        if (o == null) return "null";
        var c = o as Component;
        Transform t = c != null ? c.transform : (o as GameObject != null ? ((GameObject)o).transform : null);
        string where = t != null ? (t.parent != null ? t.parent.name + "/" : "") + t.name : o.name;
        return where + " (" + o.GetType().Name + ")";
    }

    static string StripPart(string name) =>
        name.EndsWith(PartSuffix, System.StringComparison.Ordinal) ? name.Substring(0, name.Length - PartSuffix.Length) : name;

    /// <summary>The object under 'to' that corresponds to 'target' under 'from': same relative path, else same name.</summary>
    static UnityEngine.Object Counterpart(UnityEngine.Object target, Transform fromRoot, Transform to, Transform exclude)
    {
        var comp = target as Component;
        var go = target as GameObject;
        Transform tr = comp != null ? comp.transform : (go != null ? go.transform : null);
        if (tr == null) return null;
        Transform nt = null;
        if (tr == fromRoot) nt = to;
        else
        {
            string path = tr.IsChildOf(fromRoot) ? AnimationUtility.CalculateTransformPath(tr, fromRoot) : null;
            if (!string.IsNullOrEmpty(path)) nt = to.Find(path);
            if (nt == null)
            {
                string leaf = StripPart(tr.name);
                foreach (var c in to.GetComponentsInChildren<Transform>(true))
                {
                    if (c == to || (exclude != null && c.IsChildOf(exclude))) continue;
                    if (StripPart(c.name) == leaf) { nt = c; break; }
                }
            }
        }
        if (nt == null) return null;
        if (go != null) return nt.gameObject;
        if (comp is Transform) return nt;
        return nt.GetComponent(comp.GetType());
    }

    // ───────────────────────────── body creation, restore marker ─────────────────────────────
    /// <summary>Instantiates the prefab under parent, unpacks it and removes texts, keys, scripts and colliders.</summary>
    static Transform Spawn(string prefabName, Transform parent, string bodyName, out string error)
    {
        error = null;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + prefabName + ".prefab");
        if (prefab == null) { error = "prefab not found: " + PrefabDir + prefabName + ".prefab"; return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        if (go == null) { error = "could not instantiate " + prefabName; return null; }
        if (PrefabUtility.IsPartOfPrefabInstance(go))
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = bodyName;

        // Anything carrying a script is a text or a key from the preview prefab: the scene has its own.
        var doomed = new List<GameObject>();
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            if (mb.gameObject == go) UnityEngine.Object.DestroyImmediate(mb);
            else if (!doomed.Contains(mb.gameObject)) doomed.Add(mb.gameObject);
        }
        foreach (var d in doomed) if (d != null) UnityEngine.Object.DestroyImmediate(d);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = parent.gameObject.layer;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        go.transform.SetAsLastSibling();
        _current = go.transform;
        return go.transform;
    }

    static void WriteRestore(Transform body, Transform root, IEnumerable<Renderer> disabled, bool animator)
    {
        int up = 0;
        for (var x = body; x != null && x != root; x = x.parent) up++;
        var marker = new GameObject(RestoreName + ":" + up);
        marker.transform.SetParent(body, false);
        marker.SetActive(false);
        foreach (var r in disabled)
        {
            if (r == null) continue;
            string path = r.transform == root ? "." : AnimationUtility.CalculateTransformPath(r.transform, root);
            var e = new GameObject(path);
            e.transform.SetParent(marker.transform, false);
        }
        if (animator) { var e = new GameObject("@Animator"); e.transform.SetParent(marker.transform, false); }
    }

    /// <summary>Undoes one adoption: old renderers on, serialized references back, body destroyed.</summary>
    static string RevertBody(Transform body)
    {
        var sb = new StringBuilder();
        Transform marker = null;
        foreach (Transform c in body) if (c.name.StartsWith(RestoreName, System.StringComparison.Ordinal)) marker = c;
        Transform root = body.parent;
        if (marker != null)
        {
            int up = 1;
            int colon = marker.name.IndexOf(':');
            if (colon >= 0) int.TryParse(marker.name.Substring(colon + 1), out up);
            root = body;
            for (int i = 0; i < up && root.parent != null; i++) root = root.parent;
            foreach (Transform e in marker)
            {
                if (e.name == "@Animator")
                {
                    var a = root.GetComponent<Animator>();
                    if (a != null) { a.enabled = true; EditorUtility.SetDirty(a); }
                    continue;
                }
                Transform t = e.name == "." ? root : root.Find(e.name);
                var r = t != null ? t.GetComponent<Renderer>() : null;
                if (r != null) { r.enabled = true; EditorUtility.SetDirty(r); sb.Append(" on:" + t.name); }
                else sb.Append(" MISSING:" + e.name);
            }
        }
        else sb.Append(" (no restore marker: only the body is removed)");

        // References that were pointed into the body go back to the old object.
        if (_refs != null)
        {
            var targets = new List<UnityEngine.Object>();
            foreach (var t in body.GetComponentsInChildren<Transform>(true))
            {
                targets.Add(t.gameObject);
                targets.AddRange(t.GetComponents<Component>());
            }
            foreach (var target in targets)
                foreach (var r in RefsTo(target, body))
                {
                    UnityEngine.Object back = target is Animator && ((Animator)target).transform == body
                        ? (UnityEngine.Object)root.GetComponent<Animator>()
                        : Counterpart(target, body, root, body);
                    SetRef(r, back);
                    sb.Append("\n    ref " + r.comp.GetType().Name + "." + r.path + ": " + Describe(target) + " -> " + Describe(back));
                }
        }
        string name = body.name;
        UnityEngine.Object.DestroyImmediate(body.gameObject);
        return "  reverted " + name + ":" + sb;
    }

    static string RevertAll()
    {
        var sb = new StringBuilder();
        var bodies = All().Where(t => t != null && t.name.EndsWith(BodySuffix, System.StringComparison.Ordinal)).ToList();
        if (bodies.Count == 0) return "  nothing to revert\n";
        BuildRefIndex();
        foreach (var b in bodies) if (b != null) sb.AppendLine(RevertBody(b));
        _refs = null;
        return sb.ToString();
    }

    public static string Revert()
    {
        var sb = new StringBuilder("=== BuildOfficeArtV3.Revert ===\n");
        sb.Append(RevertAll());
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        sb.AppendLine("scene saved");
        return sb.ToString();
    }

    // ───────────────────────────── entry ─────────────────────────────
    public static string Apply()
    {
        _log = new StringBuilder("=== BuildOfficeArtV3.Apply ===\n");
        _log.AppendLine("previous run:");
        _log.Append(RevertAll());                 // idempotent: always decide from the un-adopted scene
        var all = All();
        BuildRefIndex();
        var ws = Find(all, "WorkspaceRoot");
        var rows = new List<Row>();

        if (EnableFactory) rows.Add(Guard("SoftwareFactory", () => AdoptFactory()));
        if (EnableTrays)
        {
            rows.Add(Guard("OutboxTray", () => AdoptTray(all, "OutboxTray", "Doc_ArchiveTray_Polished", "Outbox_Stack_Anchor", true, new[] { "ArchiveTray_PaperEdge" })));
            rows.Add(Guard("InboxTray", () => AdoptTray(all, "InboxTray", "Doc_InboxTray_Optimized", "Inbox_Stack_Anchor", false, new string[0])));
        }
        if (EnablePlates)
        {
            foreach (var role in new[] { "Researcher", "Developer", "Tester" })
            {
                var root = Find(all, "Nameplate_" + role);
                var s = new Spec
                {
                    element = "Nameplate_" + role, prefab = "Plate_AgentNameplate_Polished", root = root,
                    frame = root != null ? root.Find("Plate") : null,
                    mainPart = "Nameplate_Plate", outerPart = "Nameplate_PlateRim",
                    hideParts = new[] { "RoleStrip" }, deskParts = new[] { "Nameplate_HousingInner" },
                };
                rows.Add(Guard(s.element, () => AdoptPlate(s, ws)));
            }
            {
                var root = Find(all, "ModePlaque");
                var s = new Spec
                {
                    element = "ModePlaque", prefab = "Plate_ModePlaque_Polished", root = root,
                    frame = root != null ? root.Find("Plate") : null,
                    mainPart = "ModePlaque_Plate", outerPart = "ModePlaque_PlateRim",
                    hideParts = new[] { "ModeStrip" }, deskParts = new[] { "ModePlaque_HousingInner" },
                };
                rows.Add(Guard(s.element, () => AdoptPlate(s, ws)));
            }
        }
        if (EnableDock)
        {
            var root = Find(all, "TaskDock");
            var s = new Spec
            {
                element = "TaskDock", prefab = "TaskDock_Polished", root = root,
                frame = root != null ? root.Find("Plate") : null,
                mainPart = "TaskDock_Plate", outerPart = "TaskDock_Housing",
                alsoHide = new[] { "Frame" }, hideParts = new[] { "ModeChip" },
                features = new[]
                {
                    new Feature { label = "Start key pocket", anchor = "Anchor_StartKey", scene = "DockStartKey", tolMm = DockPocketTolMm,
                                  hide = new[] { "TaskDock_HousingInner" }, mayHide = DockHidePocketsOnMismatch },
                    new Feature { label = "Board key pocket", anchor = "Anchor_BoardKey", scene = "DockBoardKey", tolMm = DockPocketTolMm,
                                  hide = new[] { "TaskDock_HousingInner" }, mayHide = DockHidePocketsOnMismatch },
                },
            };
            rows.Add(Guard(s.element, () => AdoptPlate(s, ws)));
        }
        if (EnableReceipt)
        {
            var root = Find(all, "CompletionReceipt");
            var s = new Spec
            {
                element = "CompletionReceipt", prefab = "Doc_ReceiptCard_Polished", root = root,
                frame = root != null ? root.Find("Sheet") : null,
                mainPart = "Receipt_Paper",
                alsoHide = new[] { "ReceiptCardBody" }, hideParts = new[] { "ModeStrip" },
                decorParts = new[] { "Receipt_Brass", "Receipt_InkSoft" },
                features = new[]
                {
                    new Feature { label = "Run again key well", anchor = "Anchor_RunAgainKey", scene = "RunAgainKey", tolMm = ReceiptPocketTolMm,
                                  hide = new[] { "Receipt_PaperTint" }, mayHide = true },
                },
            };
            rows.Add(Guard(s.element, () => AdoptPlate(s, ws)));
        }
        if (EnablePaper)
        {
            var doc = Find(all, "DecisionDocument");
            var card = doc != null ? doc.Find("Card") : null;
            var s = new Spec
            {
                element = "DecisionPaper Card", prefab = "Doc_PaperV2_Expanded_Polished", root = doc, frame = card,
                mainPart = "PaperExp_Paper",
                hideParts = new[] { "ModeBand" },             // the V3 header (band, chip, labels) is the scene's own
                decorParts = new[] { "PaperExp_Brass" },
                features = new[]
                {
                    new Feature { label = "stamp brackets", anchor = "Anchor_StampZone", scene = "StampZone", tolMm = PaperFeatureTolMm,
                                  hide = new[] { "StampZone_Pad", "PaperExp_InkSoft" }, mayHide = true },
                    new Feature { label = "Approve pocket", anchor = "Anchor_Btn_Approve", scene = "Btn_Approve", tolMm = PaperFeatureTolMm,
                                  hide = new[] { "PaperExp_PaperEdge" }, mayHide = true },
                    new Feature { label = "Reject pocket", anchor = "Anchor_Btn_Reject", scene = "Btn_Reject", tolMm = PaperFeatureTolMm,
                                  hide = new[] { "PaperExp_PaperEdge" }, mayHide = true },
                    // First chip pocket (generator: x = -W/2 + 16 + 30 mm, y = -42 mm), no anchor in the model.
                    new Feature { label = "chip pocket 0", usePoint = true, point = new Vector2(-0.104f, -0.042f), scene = "Btn_Ask_why", tolMm = PaperFeatureTolMm,
                                  hide = new[] { "PaperExp_PaperEdge" }, mayHide = true },
                },
            };
            rows.Add(Guard(s.element, () => AdoptPlate(s, ws)));

            var details = doc != null ? doc.Find("Details") : null;
            var d = new Spec
            {
                element = "DecisionPaper Details", prefab = "Doc_PaperV2_DetailsSheet_Polished", root = doc,
                frame = details != null ? details.Find("DetailsSheet") : null, scope = details,
                mainPart = "PaperDet_Paper", hideParts = new[] { "DetailsStrip" },
            };
            rows.Add(Guard(d.element, () => AdoptPlate(d, ws)));

            rows.Add(Guard("DecisionPaper Cover", () => CoverRow(doc, card)));
        }
        if (EnableCard)
        {
            var board = Find(all, "TaskBoard");
            var template = Under(board, "CardTemplate");
            var art = template != null ? template.Find("TaskCard_Visual") : null;
            var s = new Spec
            {
                element = "CardTemplate", prefab = "TaskCard_V2_Polished", root = template, frame = art,
                mainPart = "Card_Body", hideParts = new[] { "Card_SelectionRim" }, maxScaleDev = CardFaceTolerance,
            };
            if (art != null)
                s.old = art.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => r.enabled && r.GetComponent<TMP_Text>() == null && MeshOf(r.transform) != null).Cast<Renderer>().ToList();
            rows.Add(Guard(s.element, () => AdoptPlate(s, ws)));
            rows.Add(Guard("TaskBoard frame", () => FrameRow(board)));
        }

        // ── table ──
        _log.AppendLine();
        _log.AppendLine("element | old size mm | new size mm | scale applied | surface offset | decision | reason");
        int tris = 0, removed = 0, draws = 0, adopted = 0;
        foreach (var r in rows)
        {
            _log.AppendLine(r.element + " | " + r.oldSize + " | " + r.newSize + " | " + r.scale + " | " + r.offset + " | " + r.decision + " | " + r.reason);
            if (r.decision == "ADOPT") { adopted++; tris += r.tris; removed += r.trisRemoved; draws += r.draws; }
        }
        _log.AppendLine();
        foreach (var r in rows.Where(x => x.decision == "ADOPT"))
            _log.AppendLine("  " + r.element + ": +" + r.tris + " triangles in the body, -" + r.trisRemoved + " in the renderers switched off, " + r.draws + " material slots drawn");
        _log.AppendLine("adopted " + adopted + " of " + rows.Count + ": +" + tris + " triangles added, -" + removed + " switched off (net " + (tris - removed) +
                        "), " + draws + " material slots in the new bodies (before batching)");

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        _log.AppendLine("scene saved. Revert: BuildOfficeArtV3.Revert");
        _refs = null;
        string text = _log.ToString();
        _log = null;
        return text;
    }

    /// <summary>Runs one element; any exception removes that element's body and restores it.</summary>
    static Row Guard(string element, System.Func<Row> run)
    {
        Row row;
        _current = null;
        try { row = run(); }
        catch (System.Exception e)
        {
            row = new Row { element = element, reason = "EXCEPTION, element restored: " + e.GetType().Name + ": " + e.Message };
            if (_current != null)
            {
                BuildRefIndex();                    // references may already have been pointed into the body
                _log.AppendLine(RevertBody(_current));
            }
        }
        _current = null;
        if (row.element == null) row.element = element;
        return row;
    }

    static Row Fail(Row row, Transform body, string reason)
    {
        if (body != null) UnityEngine.Object.DestroyImmediate(body.gameObject);
        row.decision = "SKIP";
        row.reason = reason;
        return row;
    }

    // ───────────────────────────── flat, text-bearing elements ─────────────────────────────
    static Row AdoptPlate(Spec s, Transform ws)
    {
        var row = new Row { element = s.element };
        if (s.root == null || s.frame == null) { row.reason = "scene object not found"; return row; }
        if (s.scope == null) s.scope = s.root;
        if (s.old.Count == 0)
        {
            var own = s.frame.GetComponent<Renderer>();
            if (own != null && own.enabled && MeshOf(s.frame) != null) s.old.Add(own);
        }
        if (s.old.Count == 0) { row.reason = "the current surface has no enabled mesh renderer"; return row; }
        float mm = 0.001f * s.unit;

        // ── the current surface, in the frame's metric space (front is -Z) ──
        var toM = MetricInverse(s.frame, s.root);
        var oldSoup = new List<Vector3>();
        int oldTris = 0;
        foreach (var r in s.old) { AddMesh(oldSoup, MeshOf(r.transform), toM * Chain(r.transform, s.root)); oldTris += TriCount(MeshOf(r.transform)); }
        if (!SoupBounds(oldSoup, out var oMn, out var oMx)) { row.reason = "the current surface has no geometry"; return row; }
        float oldW = oMx.x - oMn.x, oldH = oMx.y - oMn.y;
        Vector2 oldC = new Vector2((oMn.x + oMx.x) * 0.5f, (oMn.y + oMx.y) * 0.5f);
        if (!Sample(oldSoup, oldC.x, oldC.y, 2, true, out float oldFront)) oldFront = oMn.z;
        row.oldSize = Size2(oldW, oldH);
        row.trisRemoved = oldTris;
        if (oldW < 1e-5f || oldH < 1e-5f) { row.reason = "the current surface has no size"; return row; }

        // Scene parts the model replaces (a frame quad, a thickness block): they go off with the old renderer,
        // and they count towards the old outline.
        var hideAlso = new List<Renderer>();
        Vector3 ooMn = oMn, ooMx = oMx;
        foreach (Transform c in s.root)
        {
            var mr = c.GetComponent<MeshRenderer>();
            if (mr == null || !mr.enabled || !Has(s.alsoHide, c.name) || MeshOf(c) == null) continue;
            hideAlso.Add(mr);
            row.trisRemoved += TriCount(MeshOf(c));
            if (PartBounds(c, toM, s.root, out var hMn, out var hMx)) { ooMn = Vector3.Min(ooMn, hMn); ooMx = Vector3.Max(ooMx, hMx); }
        }
        float oldOutW = ooMx.x - ooMn.x, oldOutH = ooMx.y - ooMn.y;
        if (hideAlso.Count > 0) row.oldSize += " (outline " + Size2(oldOutW, oldOutH) + ")";

        // ── the model, at unit scale on the frame's origin ──
        var body = Spawn(s.prefab, s.frame, NameFor(s) + BodySuffix, out string err);
        if (body == null) { row.reason = err; return row; }
        Vector3 fs = ColumnScale(toM * Chain(s.frame, s.root));
        if (fs.x < 1e-6f || fs.y < 1e-6f || fs.z < 1e-6f) return Fail(row, body, "the current surface object has a zero scale");
        body.localPosition = Vector3.zero;
        body.localRotation = Quaternion.identity;
        body.localScale = new Vector3(1f / fs.x, 1f / fs.y, 1f / fs.z);

        var parts = new Dictionary<string, Transform>();
        foreach (var t in body.GetComponentsInChildren<Transform>(true))
            if (t != body && MeshOf(t) != null && !parts.ContainsKey(t.name)) parts[t.name] = t;
        if (!parts.TryGetValue(s.mainPart, out var main))
            return Fail(row, body, "model part '" + s.mainPart + "' not found (parts: " + string.Join(", ", parts.Keys) + ")");
        foreach (var n in s.hideParts) if (parts.TryGetValue(n, out var hp)) hp.gameObject.SetActive(false);

        if (!PartBounds(main, toM, s.root, out var nMn, out var nMx)) return Fail(row, body, "the model's face part has no geometry");
        Vector3 outMn = nMn, outMx = nMx;
        bool hasOuter = false;
        if (s.outerPart != null && parts.TryGetValue(s.outerPart, out var outer) && PartBounds(outer, toM, s.root, out var a, out var b))
        { outMn = Vector3.Min(outMn, a); outMx = Vector3.Max(outMx, b); hasOuter = true; }
        row.newSize = Size2(nMx.x - nMn.x, nMx.y - nMn.y) + (hasOuter ? " (with rim " + Size2(outMx.x - outMn.x, outMx.y - outMn.y) + ")" : "");

        // ── fit: the printable face to the old face; if that is too non-uniform, the rim outline to the old face ──
        float sx = 0f, sy = 0f; Vector2 fitC = Vector2.zero; string fit = null, tried = "";
        for (int pass = 0; pass < (hasOuter ? 2 : 1) && fit == null; pass++)
        {
            Vector3 mn = pass == 0 ? nMn : outMn, mx = pass == 0 ? nMx : outMx;
            float w = mx.x - mn.x, h = mx.y - mn.y;
            if (w < 1e-6f || h < 1e-6f) continue;
            float cx = oldW / w, cy = oldH / h;
            float nonUni = Mathf.Max(cx, cy) / Mathf.Min(cx, cy) - 1f;
            float dev = Mathf.Max(Mathf.Abs(cx - 1f), Mathf.Abs(cy - 1f));
            float over = pass == 0 ? Mathf.Max(((outMx.x - outMn.x) * cx - oldOutW) * 0.5f, ((outMx.y - outMn.y) * cy - oldOutH) * 0.5f) : 0f;
            string what = (pass == 0 ? "face" : "rim") + " fit: scale " + cx.ToString("F3") + " x " + cy.ToString("F3") + ", non-uniform " +
                          (nonUni * 100f).ToString("F1") + "%, overhang " + Mm(over / s.unit) + " mm";
            tried += (tried.Length > 0 ? "; " : "") + what;
            if (nonUni > MaxNonUniform || dev > s.maxScaleDev || over > MaxOverhangMm * mm) continue;
            sx = cx; sy = cy; fit = pass == 0 ? "face" : "rim";
            fitC = new Vector2((mn.x + mx.x) * 0.5f, (mn.y + mx.y) * 0.5f);
        }
        if (fit == null)
            return Fail(row, body, "footprint does not match (limits: non-uniform " + (MaxNonUniform * 100f).ToString("F0") + "%, deviation " +
                                   (s.maxScaleDev * 100f).ToString("F0") + "%, overhang " + MaxOverhangMm.ToString("F0") + " mm): " + tried);
        float sz = Mathf.Sqrt(sx * sy);
        var p = new Vector3(oldC.x - sx * fitC.x, oldC.y - sy * fitC.y, oldFront - sz * nMn.z);
        body.localScale = new Vector3(sx / fs.x, sy / fs.y, sz / fs.z);
        body.localPosition = new Vector3(p.x / fs.x, p.y / fs.y, p.z / fs.z);
        row.scale = sx.ToString("F3") + " x " + sy.ToString("F3") + " x " + sz.ToString("F3") + " (" + fit + " fit)";
        var notes = new List<string>();

        // ── features that must line up with the scene (or be hidden) ──
        foreach (var f in s.features)
        {
            Vector3 mp;
            if (f.usePoint) mp = (toM * Chain(body, s.root)).MultiplyPoint3x4(new Vector3(f.point.x, f.point.y, 0f));
            else
            {
                var an = Under(body, f.anchor);
                if (an == null) return Fail(row, body, f.label + ": model anchor '" + f.anchor + "' not found");
                mp = (toM * Chain(an, s.root)).MultiplyPoint3x4(Vector3.zero);
            }
            var so = Under(s.root, f.scene);
            float d = float.PositiveInfinity;
            if (so != null && !UnderBody(so))
            {
                Vector3 sp = (toM * Chain(so, s.root)).MultiplyPoint3x4(Vector3.zero);
                d = Vector2.Distance(new Vector2(mp.x, mp.y), new Vector2(sp.x, sp.y));
            }
            if (d <= f.tolMm * mm) { notes.Add(f.label + " lines up (" + Mm(d / s.unit) + " mm)"); continue; }
            string off = float.IsInfinity(d) ? "scene object '" + f.scene + "' not found" : Mm(d / s.unit) + " mm from " + f.scene;
            if (!f.mayHide) return Fail(row, body, f.label + " is " + off + " (limit " + f.tolMm.ToString("F0") + " mm)");
            foreach (var n in f.hide) if (parts.TryGetValue(n, out var hp)) hp.gameObject.SetActive(false);
            notes.Add(f.label + " " + off + ": hidden " + string.Join("+", f.hide));
        }

        // ── parts that would dip under the real desk surface ──
        if (ws != null && Mathf.Abs(s.root.lossyScale.x) > 0.5f)
            foreach (var n in s.deskParts)
            {
                if (!parts.TryGetValue(n, out var dp) || !dp.gameObject.activeSelf) continue;
                var soup = new List<Vector3>();
                AddMesh(soup, MeshOf(dp), ws.worldToLocalMatrix * dp.localToWorldMatrix);
                if (SoupBounds(soup, out var dMn, out _) && dMn.y < -0.0005f)
                { dp.gameObject.SetActive(false); notes.Add(n + " reaches " + Mm(-dMn.y) + " mm under the desk: hidden"); }
            }

        // ── cost ──
        var shown = parts.Values.Where(t => t.gameObject.activeSelf).ToList();
        foreach (var t in shown) { row.tris += TriCount(MeshOf(t)); var r = t.GetComponent<Renderer>(); if (r != null) row.draws += r.sharedMaterials.Length; }
        if (row.tris > MaxTriangles) return Fail(row, body, row.tris + " triangles (limit " + MaxTriangles + ")");

        // ── geometry for the depth checks ──
        var bodySoup = new List<Vector3>(); var bodyPlain = new List<Vector3>();
        foreach (var t in shown)
        {
            var m = toM * Chain(t, s.root);
            AddMesh(bodySoup, MeshOf(t), m);
            if (!Has(s.decorParts, t.name)) AddMesh(bodyPlain, MeshOf(t), m);
        }
        var overlays = new List<(string name, List<Vector3> soup)>();
        var overlaySoup = new List<Vector3>();
        foreach (var mr in s.scope.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr.GetComponent<TMP_Text>() != null || mr.GetComponent<TMP_SubMesh>() != null) continue;
            if (s.old.Contains(mr) || hideAlso.Contains(mr) || UnderBody(mr.transform)) continue;
            if (!mr.enabled || !mr.gameObject.activeSelf || MeshOf(mr.transform) == null) continue;
            if (HasKeyAncestor(mr.transform, s.root)) continue;
            var soup = new List<Vector3>();
            AddMesh(soup, MeshOf(mr.transform), toM * Chain(mr.transform, s.root));
            if (!SoupBounds(soup, out var vMn, out var vMx) || vMx.z - vMn.z > OverlayMaxDepthMm * mm) continue;   // flat plates only
            overlays.Add((mr.name, soup));
            overlaySoup.AddRange(soup);
        }

        // Scene plates, bands and rules that are in front of the sheet today must stay in front of the body.
        foreach (var (name, soup) in overlays)
        {
            if (!SoupBounds(soup, out var mn, out var mx)) continue;
            for (int iy = 0; iy < 5; iy++)
                for (int ix = 0; ix < 5; ix++)
                {
                    float x = Mathf.Lerp(mn.x, mx.x, 0.05f + 0.9f * ix / 4f), y = Mathf.Lerp(mn.y, mx.y, 0.05f + 0.9f * iy / 4f);
                    if (!Sample(soup, x, y, 2, true, out float zo) || !Sample(oldSoup, x, y, 2, true, out float zOld)) continue;
                    if (zo >= zOld - 1e-6f) continue;                       // behind the sheet today: not this check's business
                    if (!Sample(bodyPlain, x, y, 2, true, out float zb)) continue;
                    if (zo > zb - MinOverlayProudMm * mm)
                        return Fail(row, body, "scene plate '" + name + "' would be " + Mm((zo - zb) / s.unit) + " mm behind the new surface at (" +
                                               Mm(x) + ", " + Mm(y) + "); it is " + Mm((zOld - zo) / s.unit) + " mm in front of the sheet today");
                }
        }

        // Every text printed on the element: 0.3 to 1.0 mm in front of what is under it (or exactly as today), never behind.
        float proudLo = float.PositiveInfinity, proudHi = float.NegativeInfinity; int texts = 0;
        foreach (var tmp in s.scope.GetComponentsInChildren<TMP_Text>(true))
        {
            if (UnderBody(tmp.transform) || HasKeyAncestor(tmp.transform, s.root)) continue;
            var tm = toM * Chain(tmp.transform, s.root);
            Rect rc = tmp.rectTransform.rect;
            bool counted = false;
            for (int iy = 0; iy < 3; iy++)
                for (int ix = 0; ix < 7; ix++)
                {
                    var lp = new Vector3(Mathf.Lerp(rc.xMin, rc.xMax, 0.06f + 0.88f * ix / 6f), Mathf.Lerp(rc.yMin, rc.yMax, 0.06f + 0.88f * iy / 2f), 0f);
                    Vector3 q = tm.MultiplyPoint3x4(lp);
                    bool h1 = Sample(oldSoup, q.x, q.y, 2, true, out float z1), h2 = Sample(overlaySoup, q.x, q.y, 2, true, out float z2);
                    if (!h1) continue;                                      // not over this element
                    float oldSurf = h2 ? Mathf.Min(z1, z2) : z1;
                    float oldProud = oldSurf - q.z;
                    if (oldProud < -ProudBehindTolMm * mm) continue;        // behind the sheet today: printed on something else
                    bool h3 = Sample(bodySoup, q.x, q.y, 2, true, out float z3);
                    if (!h3 && !h2)
                        return Fail(row, body, "text '" + tmp.name + "' at (" + Mm(q.x) + ", " + Mm(q.y) + ") would have no surface under it");
                    float newSurf = h3 ? (h2 ? Mathf.Min(z3, z2) : z3) : z2;
                    float proud = newSurf - q.z;
                    bool ok = (proud >= ProudMinMm * mm - 1e-6f && proud <= ProudMaxMm * mm + 1e-6f)
                              || Mathf.Abs(proud - oldProud) <= ProudUnchangedTolMm * mm;
                    if (!ok)
                        return Fail(row, body, "text '" + tmp.name + "' would be " + Mm(proud / s.unit) + " mm in front of the new surface at (" +
                                               Mm(q.x) + ", " + Mm(q.y) + ") (today " + Mm(oldProud / s.unit) + " mm; allowed " + ProudMinMm + " to " + ProudMaxMm + ")");
                    proudLo = Mathf.Min(proudLo, proud); proudHi = Mathf.Max(proudHi, proud);
                    counted = true;
                }
            if (counted) texts++;
        }

        // ── scripts that hold the renderers about to be switched off ──
        var retarget = new List<(Ref r, UnityEngine.Object was, UnityEngine.Object to)>();
        foreach (var r in s.old.Concat(hideAlso))
            foreach (var rf in RefsTo(r, body))
            {
                var to = Counterpart(r, s.root, body, null);
                var toR = to as Renderer;
                if (toR == null || !toR.gameObject.activeSelf)
                    return Fail(row, body, rf.comp.GetType().Name + "." + rf.path + " on '" + rf.comp.name + "' holds the old renderer '" + r.name +
                                           "' and the model has no shown part of that name");
                retarget.Add((rf, r, to));
            }

        // ── commit ──
        foreach (var (rf, was, to) in retarget)
        {
            SetRef(rf, to);
            _log.AppendLine("  " + s.element + ": " + rf.comp.GetType().Name + "." + rf.path + " " + Describe(was) + " -> " + Describe(to));
        }
        foreach (var t in parts.Values) t.name = t.name + PartSuffix;
        var disabled = s.old.Concat(hideAlso).ToList();
        foreach (var r in disabled) { r.enabled = false; EditorUtility.SetDirty(r); }
        WriteRestore(body, s.root, disabled, false);
        EditorUtility.SetDirty(body.gameObject);

        row.decision = "ADOPT";
        row.offset = "face on the old surface (0.0 mm); " + texts + " texts " +
                     (texts > 0 ? Mm(proudLo / s.unit) + " to " + Mm(proudHi / s.unit) + " mm proud" : "checked");
        row.reason = "hidden with the old renderer: " + (hideAlso.Count > 0 ? string.Join(", ", hideAlso.Select(r => r.name)) : "none") +
                     "; model parts shown: " + string.Join(", ", shown.Select(t => StripPart(t.name))) +
                     (notes.Count > 0 ? "; " + string.Join("; ", notes) : "");
        return row;
    }

    static string NameFor(Spec s)
    {
        // Paper bodies are named after the group they belong to (Card, Details); the rest after the element root.
        if (s.frame.name == "Card") return "Card";
        if (s.frame.name == "DetailsSheet") return "Details";
        return s.root.name;
    }

    // ───────────────────────────── carried cover (always skipped, with the measurements) ─────────────────────────────
    static Row CoverRow(Transform doc, Transform card)
    {
        var row = new Row { element = "DecisionPaper Cover" };
        var cover = doc != null ? doc.Find("Cover") : null;
        if (cover == null || card == null) { row.reason = "Cover / Card not found"; return row; }
        float held = 0.22f;
        var hc = doc.GetComponent("HeldPaperController");
        if (hc != null) { var p = new SerializedObject(hc).FindProperty("heldScale"); if (p != null) held = p.floatValue; }
        Vector3 cs = card.localScale;
        row.oldSize = Size2(Mathf.Abs(cs.x), Mathf.Abs(cs.y)) + " shared Card sheet (" + Size2(Mathf.Abs(cs.x) * held, Mathf.Abs(cs.y) * held) + " carried)";
        row.newSize = "300.0 x 300.0 cover sheet, 15 mm corners (generator)";
        bool ownSheet = false;
        foreach (var mr in cover.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr.GetComponent<TMP_Text>() != null || UnderBody(mr.transform)) continue;
            Vector3 ls = mr.transform.localScale;
            // A cover sheet of its own would be a full-size plate at or in front of the Card face.
            if (Mathf.Abs(ls.x) >= 0.9f * Mathf.Abs(cs.x) && Mathf.Abs(ls.y) >= 0.9f * Mathf.Abs(cs.y) && mr.transform.localPosition.z <= card.localPosition.z + 0.0001f)
                ownSheet = true;
        }
        row.reason = ownSheet
            ? "the Cover group has a sheet of its own, which this builder does not handle: left as it is"
            : "the Cover group has no sheet of its own: the Card sheet is the paper in both states and is never hidden while held, so the model's " +
              "cover sheet would lie on the same plane as the Card (two faces with different corner radii), and its band and tab stand 4.2 and 1.0 mm " +
              "proud where the scene's own band and tab labels are 0.3 and 0.5 mm (carried) proud. Needs a HeldPaperController hide/show change: see the notes";
        return row;
    }

    // ───────────────────────────── board frame (measured, never adopted here) ─────────────────────────────
    static Row FrameRow(Transform board)
    {
        var row = new Row { element = "TaskBoard frame" };
        var frame = board != null ? board.Find("KanbanFrame_Visual") : null;
        if (frame == null) { row.reason = "TaskBoard/KanbanFrame_Visual not found"; return row; }
        var oldSoup = new List<Vector3>();                          // everything here is in board-local space
        foreach (var mr in frame.GetComponentsInChildren<MeshRenderer>(true))
            if (mr.enabled && mr.GetComponent<TMP_Text>() == null) AddMesh(oldSoup, MeshOf(mr.transform), Chain(mr.transform, board));
        if (!SoupBounds(oldSoup, out var oMn, out var oMx)) { row.reason = "the current frame has no geometry"; return row; }
        row.oldSize = Size3(oMx - oMn) + " (board units)";

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "TaskBoard_KanbanFrame_V2.prefab");
        if (prefab == null) { row.reason = "prefab not found"; return row; }
        var newSoup = new List<Vector3>(); int tris = 0;
        var lanes = new List<float>();
        foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
        {
            if (t.GetComponentInParent<MonoBehaviour>(true) != null) continue;      // the preview prefab's texts and key
            if (MeshOf(t) != null) { AddMesh(newSoup, MeshOf(t), Chain(t, prefab.transform)); tris += TriCount(MeshOf(t)); }
            if (t.name.StartsWith("HeaderLabel_", System.StringComparison.Ordinal)) lanes.Add(Chain(t, prefab.transform).MultiplyPoint3x4(Vector3.zero).x);
        }
        if (!SoupBounds(newSoup, out var nMn, out var nMx)) { row.reason = "the model has no geometry"; return row; }
        row.newSize = Size3(nMx - nMn) + ", " + tris + " triangles";
        float s = (oMx.x - oMn.x) / Mathf.Max(1e-6f, nMx.x - nMn.x);
        float sy = (oMx.y - oMn.y) / Mathf.Max(1e-6f, nMx.y - nMn.y);
        lanes.Sort();
        float newC = (nMn.x + nMx.x) * 0.5f, oldC = (oMn.x + oMx.x) * 0.5f;
        string model = string.Join(", ", lanes.Select(lx => Mm(oldC + (lx - newC) * s)));

        // The scene's lane centres: BoardPresenter's four region transforms (read by name, no type dependency).
        var regions = new List<float>();
        var presenter = board.GetComponent("BoardPresenter");
        if (presenter == null) presenter = FindComponentByTypeName("BoardPresenter");
        if (presenter != null)
        {
            var so = new SerializedObject(presenter);
            foreach (var n in new[] { "backlogRegion", "workingRegion", "needsMeRegion", "completedRegion" })
            {
                var p = so.FindProperty(n);
                var t = p != null ? p.objectReferenceValue as Transform : null;
                if (t != null) regions.Add(Chain(t, board).MultiplyPoint3x4(Vector3.zero).x);
            }
        }
        regions.Sort();
        float worst = 0f;
        if (regions.Count == lanes.Count)
            for (int i = 0; i < lanes.Count; i++) worst = Mathf.Max(worst, Mathf.Abs(regions[i] - (oldC + (lanes[i] - newC) * s)));
        row.scale = "would need " + s.ToString("F3") + " x " + sy.ToString("F3");
        row.reason = "not adopted: BuildBoardV3 measured every lane, slot, label and key position on the current frame mesh. Model lane centres at this " +
                     "scale (board x, mm): " + model + "; scene lane regions: " + (regions.Count > 0 ? string.Join(", ", regions.Select(rx => Mm(rx))) : "not found") +
                     (regions.Count == lanes.Count ? "; worst difference " + Mm(worst) + " mm (limit 2.0)" : "") +
                     "; width/height scale differ by " + (Mathf.Abs(s / Mathf.Max(1e-6f, sy) - 1f) * 100f).ToString("F1") + "%";
        return row;
    }

    // ───────────────────────────── trays ─────────────────────────────
    static float BackSign(List<Vector3> soup, Vector3 mn, Vector3 mx)
    {
        // Where the tall back wall is: mean height of the vertices behind the centre minus those in front of it.
        float cz = (mn.z + mx.z) * 0.5f, hi = 0f, lo = 0f; int nh = 0, nl = 0;
        foreach (var v in soup) { if (v.z > cz) { hi += v.y; nh++; } else { lo += v.y; nl++; } }
        if (nh == 0 || nl == 0) return 0f;
        float d = (hi / nh - lo / nl) / Mathf.Max(1e-6f, mx.y - mn.y);
        return Mathf.Abs(d) < 0.08f ? 0f : Mathf.Sign(d);
    }

    static float TopUnder(List<Vector3> soup, float x, float z, float radius)
    {
        float top = float.NegativeInfinity;
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                if (Sample(soup, x + i * radius, z + j * radius, 1, false, out float y)) top = Mathf.Max(top, y);
        return top;
    }

    static Row AdoptTray(List<Transform> all, string sceneName, string prefabName, string anchorName, bool outbox, string[] optionalParts)
    {
        var row = new Row { element = sceneName };
        var tray = Find(all, sceneName);
        if (tray == null) { row.reason = "scene object not found"; return row; }
        var old = tray.GetComponentsInChildren<MeshRenderer>(true)
            .Where(r => r.enabled && r.gameObject.activeSelf && r.GetComponent<TMP_Text>() == null && MeshOf(r.transform) != null && OwnPart(r.transform, tray))
            .Cast<Renderer>().ToList();
        if (old.Count == 0) { row.reason = "the current tray has no enabled mesh renderer of its own"; return row; }

        Transform space = tray.parent;
        var toM = MetricInverse(tray, space);                       // the tray's own axes, unit scale, y up
        Matrix4x4 worldToM = toM * (space != null ? space.worldToLocalMatrix : Matrix4x4.identity);
        var oldSoup = new List<Vector3>();
        foreach (var r in old) { AddMesh(oldSoup, MeshOf(r.transform), toM * Chain(r.transform, space)); row.trisRemoved += TriCount(MeshOf(r.transform)); }
        SoupBounds(oldSoup, out var oMn, out var oMx);
        Vector3 oSize = oMx - oMn;
        row.oldSize = Size3(oSize);

        var body = Spawn(prefabName, tray, sceneName + BodySuffix, out string err);
        if (body == null) { row.reason = err; return row; }
        Vector3 fs = ColumnScale(toM * Chain(tray, space));
        if (fs.x < 1e-6f || fs.y < 1e-6f || fs.z < 1e-6f) return Fail(row, body, "the tray has a zero scale");
        body.localPosition = Vector3.zero; body.localRotation = Quaternion.identity;
        body.localScale = new Vector3(1f / fs.x, 1f / fs.y, 1f / fs.z);
        var parts = body.GetComponentsInChildren<Transform>(true).Where(t => t != body && MeshOf(t) != null).ToList();
        if (parts.Count == 0) return Fail(row, body, "the model has no mesh parts");

        List<Vector3> BodySoup()
        {
            var soup = new List<Vector3>();
            foreach (var t in parts) if (t.gameObject.activeSelf) AddMesh(soup, MeshOf(t), toM * Chain(t, space));
            return soup;
        }
        var newSoup = BodySoup();
        SoupBounds(newSoup, out var nMn, out var nMx);
        Vector3 nSize = nMx - nMn;
        row.newSize = Size3(nSize);
        float sx = oSize.x / Mathf.Max(1e-6f, nSize.x), szf = oSize.z / Mathf.Max(1e-6f, nSize.z);
        float nonUni = Mathf.Max(sx, szf) / Mathf.Min(sx, szf) - 1f;
        float s = Mathf.Sqrt(sx * szf);
        if (nonUni > MaxNonUniform || Mathf.Abs(s - 1f) > MaxScaleDeviation)
            return Fail(row, body, "footprint does not match: width scale " + sx.ToString("F3") + ", depth scale " + szf.ToString("F3") + " (non-uniform " +
                                   (nonUni * 100f).ToString("F1") + "%, limit " + (MaxNonUniform * 100f).ToString("F0") + "%; deviation limit " +
                                   (MaxScaleDeviation * 100f).ToString("F0") + "%)");

        // Same way round as the current tray (tall wall at the back), judged from both meshes.
        float oldBack = BackSign(oldSoup, oMn, oMx), newBack = BackSign(newSoup, nMn, nMx);
        bool flip = oldBack != 0f && newBack != 0f && oldBack != newBack;
        string facing = oldBack == 0f || newBack == 0f ? "orientation could not be judged from the meshes: kept as delivered (CHECK BY EYE)"
                        : flip ? "turned 180 degrees to match the current tray's back wall" : "same way round as the current tray";
        body.localRotation = flip ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
        body.localScale = new Vector3(s / fs.x, s / fs.y, s / fs.z);
        newSoup = BodySoup();
        SoupBounds(newSoup, out nMn, out nMx);
        Vector3 delta = new Vector3((oMn.x + oMx.x - nMn.x - nMx.x) * 0.5f, oMn.y - nMn.y, (oMn.z + oMx.z - nMn.z - nMx.z) * 0.5f);
        body.localPosition = new Vector3(delta.x / fs.x, delta.y / fs.y, delta.z / fs.z);
        newSoup = BodySoup();
        SoupBounds(newSoup, out nMn, out nMx);
        row.scale = s.ToString("F3") + " uniform (width " + sx.ToString("F3") + ", depth " + szf.ToString("F3") + "); height " + Mm(oSize.y) + " -> " + Mm(nMx.y - nMn.y) + " mm";
        var notes = new List<string> { facing };

        // ── the stack anchor must sit on the new tray's surface ──
        var anchor = Find(all, anchorName);
        string offset = "anchor '" + anchorName + "' not found: not checked";
        if (anchor != null)
        {
            Vector3 a = worldToM.MultiplyPoint3x4(anchor.position);
            float oldTop = TopUnder(oldSoup, a.x, a.z, 0.004f);
            float newTop = TopUnder(newSoup, a.x, a.z, 0.004f);
            if (!float.IsInfinity(newTop) && newTop - a.y > TrayAnchorBuryMaxMm * 0.001f)
                foreach (var n in optionalParts)
                {
                    var op = parts.FirstOrDefault(t => t.name == n);
                    if (op == null) continue;
                    op.gameObject.SetActive(false);
                    notes.Add("model part " + n + " would bury the stack anchor by " + Mm(newTop - a.y) + " mm: hidden");
                    newSoup = BodySoup();
                    newTop = TopUnder(newSoup, a.x, a.z, 0.004f);
                }
            if (float.IsInfinity(newTop)) return Fail(row, body, "the stack anchor is not over the new tray");
            float gapNew = a.y - newTop, gapOld = float.IsInfinity(oldTop) ? 0f : a.y - oldTop;
            offset = "stack anchor " + Mm(gapNew) + " mm above the new surface (today " + (float.IsInfinity(oldTop) ? "not over the tray" : Mm(gapOld) + " mm") + ")";
            if (gapNew < -TrayAnchorBuryMaxMm * 0.001f)
                return Fail(row, body, "the stack anchor would be " + Mm(-gapNew) + " mm under the new tray's surface (limit " + TrayAnchorBuryMaxMm + ")");
            if (gapNew > Mathf.Max(gapOld, 0f) + TrayAnchorFloatMaxMm * 0.001f)
                return Fail(row, body, "the stack would float " + Mm(gapNew) + " mm above the new tray (today " + Mm(gapOld) + ", limit +" + TrayAnchorFloatMaxMm + ")");
        }

        if (outbox)
        {
            // FiledPoke plate: its lower edge must not go deeper into the tray than today.
            var key = Find(all, "FiledPoke");
            if (key != null)
            {
                Vector3 k = worldToM.MultiplyPoint3x4(key.position);
                float lower = k.y - FiledKeyHalfMm * 0.001f;
                float oldTop = TopUnder(oldSoup, k.x, k.z, 0.015f), newTop = TopUnder(newSoup, k.x, k.z, 0.015f);
                float cOld = float.IsInfinity(oldTop) ? 1f : lower - oldTop, cNew = float.IsInfinity(newTop) ? 1f : lower - newTop;
                notes.Add("FiledPoke lower edge " + (float.IsInfinity(newTop) ? "clear of the tray" : Mm(cNew) + " mm above the new tray") +
                          " (today " + (float.IsInfinity(oldTop) ? "clear" : Mm(cOld) + " mm") + ")");
                if (cNew < Mathf.Min(cOld, 0f) - 0.002f)
                    return Fail(row, body, "the FiledPoke plate would sit " + Mm(-cNew) + " mm into the new tray (today " + Mm(cOld) + " mm clear)");
            }
            else notes.Add("FiledPoke not found: not checked");

            // Archive card rise point.
            var archive = FindComponentByTypeName("DecisionArchive");
            if (archive != null)
            {
                var so = new SerializedObject(archive);
                var pRise = so.FindProperty("riseFromLocal"); var pRoot = so.FindProperty("readoutRoot");
                var cardGo = pRoot != null ? pRoot.objectReferenceValue as GameObject : null;
                if (pRise != null && cardGo != null && cardGo.transform.parent != null)
                {
                    Vector3 rise = worldToM.MultiplyPoint3x4(cardGo.transform.parent.TransformPoint(pRise.vector3Value));
                    float newTop = TopUnder(newSoup, rise.x, rise.z, 0.004f);
                    float c = float.IsInfinity(newTop) ? 1f : rise.y - newTop;
                    notes.Add("archive card rises from " + (float.IsInfinity(newTop) ? "beside the tray" : Mm(c) + " mm above the new surface"));
                    if (c < 0f) return Fail(row, body, "the archive card's rise point would be " + Mm(-c) + " mm inside the new tray");
                }
                else notes.Add("archive rise point not readable: not checked");
            }
        }

        foreach (var t in parts) if (t.gameObject.activeSelf) { row.tris += TriCount(MeshOf(t)); var r = t.GetComponent<Renderer>(); if (r != null) row.draws += r.sharedMaterials.Length; }
        if (row.tris > MaxTriangles) return Fail(row, body, row.tris + " triangles (limit " + MaxTriangles + ")");
        foreach (var r in old)
            foreach (var rf in RefsTo(r, body))
                return Fail(row, body, rf.comp.GetType().Name + "." + rf.path + " on '" + rf.comp.name + "' holds the old renderer '" + r.name + "'");

        foreach (var t in parts) t.name = t.name + PartSuffix;
        foreach (var r in old) { r.enabled = false; EditorUtility.SetDirty(r); }
        WriteRestore(body, tray, old, false);
        EditorUtility.SetDirty(body.gameObject);
        row.decision = "ADOPT";
        row.offset = offset;
        row.reason = "footprint centred on the old one, resting at the old bottom; " + string.Join("; ", notes);
        return row;
    }

    // ───────────────────────────── software factory ─────────────────────────────
    static Row AdoptFactory()
    {
        var row = new Row { element = "SoftwareFactory" };
        var fvc = UnityEngine.Object.FindAnyObjectByType<SoftwareFactoryVisualController>(FindObjectsInactive.Include);
        if (fvc == null) { row.reason = "no SoftwareFactoryVisualController in the scene"; return row; }
        Transform root = fvc.transform;
        var oldAnim = root.GetComponent<Animator>();
        if (oldAnim == null || oldAnim.runtimeAnimatorController == null) { row.reason = "the current factory has no Animator with a controller on its root"; return row; }
        var oldSkins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.enabled && r.sharedMesh != null && OwnPart(r.transform, root)).ToList();
        var oldStatic = root.GetComponentsInChildren<MeshRenderer>(true)
            .Where(r => r.enabled && r.GetComponent<TMP_Text>() == null && MeshOf(r.transform) != null && OwnPart(r.transform, root)).ToList();
        if (oldSkins.Count == 0) { row.reason = "the current factory has no enabled skinned mesh of its own"; return row; }
        Bounds ob = oldSkins[0].sharedMesh.bounds;
        foreach (var r in oldSkins) { ob.Encapsulate(r.sharedMesh.bounds); row.trisRemoved += TriCount(r.sharedMesh); }
        foreach (var r in oldStatic) row.trisRemoved += TriCount(MeshOf(r.transform));
        row.oldSize = Size3(ob.size) + " (mesh units)";

        var body = Spawn("SoftwareFactory_Complete_Polished", root, root.name + BodySuffix, out string err);
        if (body == null) { row.reason = err; return row; }
        body.localPosition = Vector3.zero; body.localRotation = Quaternion.identity; body.localScale = Vector3.one;
        var newSkins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null).ToList();
        if (newSkins.Count == 0) return Fail(row, body, "the polished prefab has no skinned mesh");
        Bounds nb = newSkins[0].sharedMesh.bounds;
        foreach (var r in newSkins) { nb.Encapsulate(r.sharedMesh.bounds); row.tris += TriCount(r.sharedMesh); row.draws += r.sharedMaterials.Length; }
        foreach (var r in body.GetComponentsInChildren<MeshRenderer>(true)) { row.tris += TriCount(MeshOf(r.transform)); row.draws += r.sharedMaterials.Length; }
        row.newSize = Size3(nb.size) + " (mesh units), " + row.tris + " triangles";
        Vector3 ratio = new Vector3(nb.size.x / Mathf.Max(1e-6f, ob.size.x), nb.size.y / Mathf.Max(1e-6f, ob.size.y), nb.size.z / Mathf.Max(1e-6f, ob.size.z));
        row.scale = "1.000 (same rig; mesh bounds ratio " + ratio.x.ToString("F3") + " x " + ratio.y.ToString("F3") + " x " + ratio.z.ToString("F3") + ")";
        for (int i = 0; i < 3; i++)
            if (Mathf.Abs(ratio[i] - 1f) > FactorySizeTolerance)
                return Fail(row, body, "mesh bounds differ from the current factory by more than " + (FactorySizeTolerance * 100f).ToString("F0") + "% (ratio " +
                                       ratio.x.ToString("F3") + " x " + ratio.y.ToString("F3") + " x " + ratio.z.ToString("F3") + "): different unit scale?");

        // ── same bones at the same paths ──
        var missing = new List<string>();
        var bones = new HashSet<Transform>();
        foreach (var r in oldSkins) foreach (var bn in r.bones) if (bn != null) bones.Add(bn);
        foreach (var bn in bones)
        {
            string path = AnimationUtility.CalculateTransformPath(bn, root);
            if (body.Find(path) == null) missing.Add(path);
        }
        if (missing.Count > 0) return Fail(row, body, "bones missing in the polished model: " + string.Join(", ", missing));

        // ── every curve of every clip in the current controller resolves in the new hierarchy ──
        var ctrl = oldAnim.runtimeAnimatorController;
        var clips = ctrl.animationClips;
        int curves = 0;
        foreach (var clip in clips)
        {
            if (clip == null) continue;
            foreach (var bd in AnimationUtility.GetCurveBindings(clip))
            {
                curves++;
                if (bd.path.Length > 0 && body.Find(bd.path) == null) missing.Add(clip.name + ": " + bd.path);
            }
        }
        if (missing.Count > 0) return Fail(row, body, "animation paths not found in the polished model: " + string.Join(", ", missing.Take(6)));
        var wanted = new[]
        {
            SoftwareFactoryVisualController.ClipBlueprintPrint, SoftwareFactoryVisualController.ClipBlueprintTransfer,
            SoftwareFactoryVisualController.ClipArtifactAssemble, SoftwareFactoryVisualController.ClipDevelopmentComplete,
            SoftwareFactoryVisualController.ClipArtifactTransport, SoftwareFactoryVisualController.ClipTestingActivate,
            SoftwareFactoryVisualController.ClipTestSuccess, SoftwareFactoryVisualController.ClipTestFailed,
            SoftwareFactoryVisualController.ClipRejectedReturn, SoftwareFactoryVisualController.ClipCompletedDelivery,
        };
        var absent = wanted.Where(w => !clips.Any(c => c != null && c.name == w)).ToList();

        // ── animator: the same controller, on the new rig ──
        var anim = body.GetComponent<Animator>();
        if (anim == null) anim = body.gameObject.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        if (FactoryUseOldAvatar) anim.avatar = oldAnim.avatar;
        anim.applyRootMotion = oldAnim.applyRootMotion;
        anim.cullingMode = oldAnim.cullingMode;
        anim.updateMode = oldAnim.updateMode;
        anim.enabled = oldAnim.enabled;

        // ── materials: a slot with the same source name takes what the scene uses today ──
        int matched = 0, slots = 0;
        if (FactoryReuseSceneMaterials)
        {
            var map = new Dictionary<string, Material>();
            foreach (var o in oldSkins)
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(o);
                var cur = o.sharedMaterials;
                var srcM = src != null ? src.sharedMaterials : cur;
                for (int i = 0; i < cur.Length; i++)
                {
                    if (cur[i] == null) continue;
                    if (i < srcM.Length && srcM[i] != null) map[srcM[i].name] = cur[i];
                    if (!map.ContainsKey(cur[i].name)) map[cur[i].name] = cur[i];
                }
            }
            foreach (var n in newSkins)
            {
                var ms = n.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    slots++;
                    if (ms[i] != null && map.TryGetValue(ms[i].name, out var m)) { ms[i] = m; matched++; }
                }
                n.sharedMaterials = ms;
                n.shadowCastingMode = oldSkins[0].shadowCastingMode;
                n.receiveShadows = oldSkins[0].receiveShadows;
                n.updateWhenOffscreen = oldSkins[0].updateWhenOffscreen;
            }
        }

        // ── every serialized reference into the old model gets its equivalent in the new one ──
        var retarget = new List<(Ref r, UnityEngine.Object was, UnityEngine.Object to)>();
        var targets = new List<UnityEngine.Object> { oldAnim };
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root || t.IsChildOf(body) || !OwnPart(t, root)) continue;
            targets.Add(t.gameObject);
            targets.AddRange(t.GetComponents<Component>());
        }
        foreach (var target in targets)
            foreach (var rf in RefsTo(target, body))
            {
                UnityEngine.Object to = target == oldAnim ? (UnityEngine.Object)anim : Counterpart(target, root, body, null);
                if (to == null)
                    return Fail(row, body, rf.comp.GetType().Name + "." + rf.path + " on '" + rf.comp.name + "' references " + Describe(target) +
                                           ", which has no equivalent in the polished model");
                retarget.Add((rf, target, to));
            }
        foreach (var n in new[] { "Research_Input_Socket", "Dev_Build_Socket", "QA_Chamber_Socket", "Delivery_Output_Socket", "Failed_Return_Socket" })
            if (Under(body, n) == null) return Fail(row, body, "socket '" + n + "' not found in the polished model");

        // ── commit ──
        _log.AppendLine("factory reference mapping (" + retarget.Count + "):");
        foreach (var (rf, was, to) in retarget)
        {
            SetRef(rf, to);
            _log.AppendLine("  " + rf.comp.GetType().Name + "." + rf.path + " on " + rf.comp.name + ": " + Describe(was) + " -> " + Describe(to));
        }
        var disabled = oldSkins.Cast<Renderer>().Concat(oldStatic.Cast<Renderer>()).ToList();
        foreach (var r in disabled) { r.enabled = false; EditorUtility.SetDirty(r); }
        oldAnim.enabled = false;                    // its bones no longer drive anything that is drawn or referenced
        EditorUtility.SetDirty(oldAnim);
        WriteRestore(body, root, disabled, true);
        body.SetAsLastSibling();                    // SoftwareFactoryVisualController.ResolveReferences keeps the last socket of each name it finds
        EditorUtility.SetDirty(body.gameObject);
        EditorUtility.SetDirty(fvc);

        row.decision = "ADOPT";
        row.offset = "same origin, rotation and scale as the current model";
        row.reason = bones.Count + " bones at the same paths; " + clips.Length + " clips, " + curves + " curves all resolve; controller '" + ctrl.name +
                     "' reused, avatar " + (FactoryUseOldAvatar ? (oldAnim.avatar != null ? "'" + oldAnim.avatar.name + "' (current)" : "none (current has none)") : "as delivered") +
                     "; " + retarget.Count + " references re-wired; materials " + matched + " of " + slots + " slots from the scene" +
                     (absent.Count > 0 ? "; NOTE the controller has no clip named " + string.Join(", ", absent) + " (true today as well)" : "") +
                     "; exempt from the " + MaxTriangles + " triangle limit";
        return row;
    }
}
