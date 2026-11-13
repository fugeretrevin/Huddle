using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.HandGrab;
using MuseDesk.Phase2.Board;
using MuseDesk.Spatial;
using MuseDesk.Workspace;

/// <summary>
/// Movable control panels (owner feedback: the "button cards" intersect the ceiling office when it
/// comes down; let me hold a small bar under them and drag them, like a Horizon OS window).
///
/// DESIGN. Each panel is an empty holder "Panel_&lt;Id&gt;" under Spatial/NearUserRig with identity
/// local pose, and the things that belong together are moved under it keeping their world pose:
///   Panel_Dock ...... TaskDock + TaskBoard (the board opens from the dock, so it follows it)
///   Panel_Plaque .... ModePlaque
///   Panel_Mode ...... ModeConsole (mode keys, Focus key, Bring / Status); without a console: FocusToggle alone (Panel_Focus)
///   Panel_Receipt ... CompletionReceipt (its bar shows only while the receipt does)
/// NearUserKit keeps the rig on the office root while the office is at the desk (pose, scale, active
/// state), so nothing looks different until the user moves a panel; when the office leaves, the rig
/// stays and nothing is re-parented. The stamps, their pad and the artifact viewer stay children of
/// the office root and keep NearUserKit's old re-parenting; the paper is untouched. No bars on those.
///
/// Per panel: a visible pill "GrabBar" (60 x 8 x 6 mm) centred under the near edge, and a
/// "GrabProxy_&lt;Id&gt;" under the rig with the grab stack of the stamps (trigger BoxCollider 90 x 30 x
/// 30 mm, kinematic Rigidbody, Grabbable, HandGrabInteractable, ReRegisterInteractable).
/// One shared "PanelResetChip" key (hidden) offers Reset layout after a 1.5 s still pinch.
///
/// Run AFTER BuildModeConsoleV1. Idempotent; saves the scene once.
///   unity command script run --file AgentScripts/BuildMovablePanelsV1.cs --entry BuildMovablePanelsV1.Apply
///   (BuildMovablePanelsV1.Revert moves everything back under its old parent and restores NearUserKit's list.)
/// </summary>
public static class BuildMovablePanelsV1
{
    const string GenDir = "Assets/MuseDesk/Art/Generated";
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI";
    const float Hidden = 0.001f;
    static readonly Vector3 Eye = new Vector3(0f, 0.30f, 0.62f);

    const float BarW = 0.060f, BarH = 0.008f, BarD = 0.006f;
    const float BarGap = 0.012f;                       // near edge of the panel to the bar
    static readonly Vector3 GrabBox = new Vector3(0.090f, 0.030f, 0.030f);
    const float ChipScale = 0.030f;
    const int CornerSegs = 6, BevelSegs = 3;

    static readonly string[] LegacyKitItems = { "TaskDock", "TaskBoard", "StampApprove", "StampReject", "Doc_StampRestPad", "ModePlaque", "CompletionReceipt", "FocusToggle", "ArtifactPanel" };
    static readonly string[] LooseKitItems = { "StampApprove", "StampReject", "Doc_StampRestPad", "ArtifactPanel" };
    static readonly string[] DecisionOnly = { "StampApprove", "StampReject", "Doc_StampRestPad" };

    // ───────────────────────────── helpers ─────────────────────────────

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    static Transform Find(List<Transform> all, string name) => all.FirstOrDefault(t => t != null && t.name == name);
    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t != root && t.name == name);
    static string P(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
    static float Safe(float v) => Mathf.Abs(v) < 1e-9f ? 1e-9f : v;

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    static Transform Holder(Transform parent, string name, int layer, out bool created)
    {
        Transform t = parent.Find(name);
        created = t == null;
        if (created)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            t = go.transform;
        }
        t.gameObject.layer = layer;
        t.gameObject.SetActive(true);
        return t;
    }

    /// <summary>World x scale of an object when shown: an ancestor hidden by scale counts at full size.</summary>
    static float ShownScaleX(Transform t)
    {
        float s = 1f;
        for (Transform a = t; a != null; a = a.parent)
        {
            Vector3 l = a.localScale;
            float k = Mathf.Abs(l.x);
            if (Mathf.Max(Mathf.Abs(l.x), Mathf.Max(Mathf.Abs(l.y), Mathf.Abs(l.z))) < 0.005f) k *= 1000f;
            s *= k;
        }
        return s;
    }

    /// <summary>
    /// Corner points of every mesh under <paramref name="t"/> in <paramref name="space"/>, as shown:
    /// an ancestor (up to and including t) that is hidden by scale counts at its full size.
    /// </summary>
    static void ShownPoints(Transform t, Transform space, List<Vector3> pts)
    {
        foreach (var f in t.GetComponentsInChildren<MeshFilter>(true))
        {
            if (f.sharedMesh == null || f.GetComponent<TMP_Text>() != null) continue;
            if (f.name == "GrabBar") continue;
            bool inactive = false;
            for (Transform a = f.transform; a != null && a != t.parent; a = a.parent)
                if (!a.gameObject.activeSelf) { inactive = true; break; }
            if (inactive) continue;
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
                pts.Add(space.InverseTransformPoint(w));
            }
        }
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

    // ───────────────────────────── the pill mesh ─────────────────────────────

    class MB
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> tris = new List<int>();
        public int Add(Vector3 p, Vector3 normal, Vector2 t) { v.Add(p); n.Add(normal); uv.Add(t); return v.Count - 1; }
        public void Tri(int a, int b, int c)
        {
            Vector3 g = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (g.sqrMagnitude < 1e-20f) return;
            if (Vector3.Dot(g, n[a] + n[b] + n[c]) < 0f) { int t = b; b = c; c = t; }
            tris.Add(a); tris.Add(b); tris.Add(c);
        }
        public void Band(int[] a, int[] b)
        {
            for (int j = 0; j < a.Length; j++) { int k = (j + 1) % a.Length; Tri(a[j], a[k], b[k]); Tri(a[j], b[k], b[j]); }
        }
        public void Fan(int centre, int[] ring)
        {
            for (int j = 0; j < ring.Length; j++) Tri(centre, ring[j], ring[(j + 1) % ring.Length]);
        }
    }

    static int[] Ring(MB mb, float hw, float hh, float r, float z, float nxy, float nz)
    {
        var idx = new int[4 * (CornerSegs + 1)];
        int k = 0;
        hw = Mathf.Max(hw, 0f); hh = Mathf.Max(hh, 0f);
        float cr = Mathf.Clamp(r, 0f, Mathf.Min(hw, hh));
        for (int c = 0; c < 4; c++)
        {
            float sx = (c == 0 || c == 3) ? 1f : -1f, sy = c < 2 ? 1f : -1f;
            for (int s = 0; s <= CornerSegs; s++)
            {
                float a = (c * 90f + 90f * s / CornerSegs) * Mathf.Deg2Rad;
                float dx = Mathf.Cos(a), dy = Mathf.Sin(a);
                if (s == 0 || s == CornerSegs) { dx = Mathf.Round(dx); dy = Mathf.Round(dy); }
                var p = new Vector3(sx * (hw - cr) + cr * dx, sy * (hh - cr) + cr * dy, z);
                idx[k++] = mb.Add(p, new Vector3(dx * nxy, dy * nxy, nz).normalized, new Vector2(p.x / Safe(hw * 2f) + 0.5f, p.y / Safe(hh * 2f) + 0.5f));
            }
        }
        return idx;
    }

    /// <summary>A pill: rounded in outline (radius = half its height) and bevelled front and back.</summary>
    static Mesh PillMesh()
    {
        var mb = new MB();
        float hw = BarW * 0.5f, hh = BarH * 0.5f, hd = BarD * 0.5f, r = hh, b = Mathf.Min(0.002f, hd * 0.9f);
        for (int side = -1; side <= 1; side += 2)                     // -1: the face toward the reader (-Z); +1: the back
        {
            float zFace = side * hd;
            int centre = mb.Add(new Vector3(0f, 0f, zFace), new Vector3(0f, 0f, side), new Vector2(0.5f, 0.5f));
            mb.Fan(centre, Ring(mb, hw - b, hh - b, r - b, zFace, 0f, side));
            int[] prev = null;
            for (int i = 0; i <= BevelSegs; i++)
            {
                float phi = i * Mathf.PI * 0.5f / BevelSegs;
                float d = b * (1f - Mathf.Sin(phi));
                int[] ring = Ring(mb, hw - d, hh - d, r - d, zFace - side * b * (1f - Mathf.Cos(phi)), Mathf.Sin(phi), side * Mathf.Cos(phi));
                if (prev != null) mb.Band(prev, ring);
                prev = ring;
            }
            if (side < 0)
            {
                // The side wall, once, between the two bevels.
                mb.Band(Ring(mb, hw, hh, r, -hd + b, 1f, 0f), Ring(mb, hw, hh, r, hd - b, 1f, 0f));
            }
        }
        string path = GenDir + "/Panel_GrabBar.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool isNew = mesh == null;
        if (isNew) mesh = new Mesh { name = "Panel_GrabBar" };
        mesh.Clear();
        mesh.SetVertices(mb.v); mesh.SetNormals(mb.n); mesh.SetUVs(0, mb.uv); mesh.SetTriangles(mb.tris, 0);
        mesh.RecalculateBounds();
        if (isNew) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    static Material BarMaterial()
    {
        string path = MatDir + "/Panel_GrabBar.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(m, path);
        }
        Color c = Color.Lerp(OfficeTokens.Housing, OfficeTokens.OnPlate, 0.60f);     // soft grey-white; MovablePanel tints it
        m.SetColor("_BaseColor", c); m.SetColor("_Color", c);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ───────────────────────────── board pose re-basing ─────────────────────────────

    /// <summary>
    /// BoardExpander keeps the open pose in its parent's space. When the board changes parent and the
    /// two parents do not coincide, the stored pose is rewritten for the new parent. Returns a note.
    /// </summary>
    static string RebaseBoard(Transform board, Transform from, Transform to)
    {
        var ex = board.GetComponent<BoardExpander>();
        if (ex == null || !ex.useInspectPose || from == null || to == null) return null;
        bool same = (from.position - to.position).sqrMagnitude < 1e-10f && Quaternion.Angle(from.rotation, to.rotation) < 0.01f
                    && (from.lossyScale - to.lossyScale).sqrMagnitude < 1e-10f;
        if (same) return null;
        Vector3 world = from.TransformPoint(ex.expandedLocalPosition);
        Quaternion worldRot = from.rotation * Quaternion.Euler(ex.expandedLocalEuler);
        float k = Mathf.Abs(from.lossyScale.x) / Safe(Mathf.Abs(to.lossyScale.x));
        ex.expandedLocalPosition = to.InverseTransformPoint(world);
        ex.expandedLocalEuler = (Quaternion.Inverse(to.rotation) * worldRot).eulerAngles;
        ex.expandedScale *= k;
        ex.authoredBase *= k;
        EditorUtility.SetDirty(ex);
        return $"BoardExpander open pose rewritten for the new parent ({from.name} -> {to.name}): {P(ex.expandedLocalPosition)}";
    }

    // ───────────────────────────── one panel ─────────────────────────────

    static MovablePanel Panel(Transform rig, string id, string holderName, Transform[] members, Transform[] footprintOf,
                              Transform[] visibility, Mesh pill, Material barMat, int layer, int grabLayer,
                              HandGrabInteractable grabSource, StringBuilder sb)
    {
        members = members.Where(x => x != null).ToArray();
        footprintOf = footprintOf.Where(x => x != null).ToArray();
        if (members.Length == 0 || footprintOf.Length == 0) { sb.AppendLine($"  SKIP {holderName}: its objects were not found"); return null; }

        var holder = Holder(rig, holderName, layer, out bool created);
        holder.localPosition = Vector3.zero; holder.localRotation = Quaternion.identity; holder.localScale = Vector3.one;
        var mp = GetOrAdd<MovablePanel>(holder.gameObject);
        mp.panelId = id;

        // Move the members under the holder (world pose kept), remembering where they came from.
        var moved = new List<Transform>(mp.movedChildren ?? new Transform[0]);
        var parents = new List<Transform>(mp.originalParents ?? new Transform[0]);
        var siblings = new List<int>(mp.originalSiblings ?? new int[0]);
        while (parents.Count < moved.Count) parents.Add(null);
        while (siblings.Count < moved.Count) siblings.Add(0);
        var notes = new List<string>();
        foreach (var m in members)
        {
            if (m.parent == holder) continue;
            if (members.Any(o => o != m && m.IsChildOf(o))) continue;           // travels with its parent
            int at = moved.IndexOf(m);
            if (at < 0) { moved.Add(m); parents.Add(m.parent); siblings.Add(m.GetSiblingIndex()); }
            else { parents[at] = m.parent; siblings[at] = m.GetSiblingIndex(); }
            string note = RebaseBoard(m, m.parent, holder);
            if (note != null) notes.Add(note);
            string cameFrom = m.parent != null ? m.parent.name : "(scene root)";
            Undo.SetTransformParent(m, holder, "panel member");
            EditorUtility.SetDirty(m.gameObject);
            notes.Add($"{m.name} moved from {cameFrom}");
        }
        mp.movedChildren = moved.ToArray(); mp.originalParents = parents.ToArray(); mp.originalSiblings = siblings.ToArray();

        // Footprint: the tighter of an axis-aligned box and a box turned toward the seated eye.
        var pts = new List<Vector3>();
        foreach (var f in footprintOf) ShownPoints(f, rig, pts);
        if (pts.Count == 0) { sb.AppendLine($"  WARNING {holderName}: nothing to measure; no bar added"); return mp; }
        Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
        foreach (var p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
        Vector3 c = (mn + mx) * 0.5f;
        float eyeYaw = Mathf.Atan2(Eye.x - c.x, Eye.z - c.z) * Mathf.Rad2Deg;
        float yaw = 0f; Vector3 fmn = mn - c, fmx = mx - c;
        {
            Quaternion inv = Quaternion.AngleAxis(-eyeYaw, Vector3.up);
            Vector3 a = Vector3.positiveInfinity, b = Vector3.negativeInfinity;
            foreach (var p in pts) { Vector3 q = inv * (p - c); a = Vector3.Min(a, q); b = Vector3.Max(b, q); }
            float area0 = (mx.x - mn.x) * (mx.z - mn.z), area1 = (b.x - a.x) * (b.z - a.z);
            if (area1 < area0 * 0.85f) { yaw = eyeYaw; fmn = a; fmx = b; }
        }
        Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up);
        float baseY = c.y + fmn.y;
        Vector3 barPos = c + turn * new Vector3((fmn.x + fmx.x) * 0.5f, 0f, fmx.z + BarGap + BarD * 0.5f);
        barPos.y = baseY + BarH * 0.5f;
        Vector3 centre = c + turn * ((fmn + fmx) * 0.5f);

        // The visible bar (a child of the panel).
        var bar = Holder(holder, "GrabBar", layer, out _);
        var mf = GetOrAdd<MeshFilter>(bar.gameObject); mf.sharedMesh = pill;
        var mr = GetOrAdd<MeshRenderer>(bar.gameObject);
        mr.sharedMaterial = barMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        bar.localPosition = barPos; bar.localRotation = turn; bar.localScale = Vector3.one;

        // The grabbed proxy (a child of the rig, not of the panel): the stamps' grab stack.
        var proxy = Holder(rig, "GrabProxy_" + id, grabLayer, out _);
        proxy.localPosition = barPos; proxy.localRotation = turn; proxy.localScale = Vector3.one;
        var rb = GetOrAdd<Rigidbody>(proxy.gameObject);
        rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;
        var box = GetOrAdd<BoxCollider>(proxy.gameObject);
        box.isTrigger = true; box.center = Vector3.zero; box.size = GrabBox;
        var grab = GetOrAdd<Grabbable>(proxy.gameObject);
        {
            var so = new SerializedObject(grab);
            SerializedProperty Prop(string prop)
            {
                var found = so.FindProperty(prop);
                if (found == null) notes.Add("Grabbable has no " + prop + " (SDK changed?)");
                return found;
            }
            SerializedProperty sp;
            if ((sp = Prop("_rigidbody")) != null) sp.objectReferenceValue = rb;
            if ((sp = Prop("_throwWhenUnselected")) != null) sp.boolValue = false;
            if ((sp = Prop("_kinematicWhileSelected")) != null) sp.boolValue = true;
            if ((sp = Prop("_maxGrabPoints")) != null) sp.intValue = 1;
            if ((sp = Prop("_targetTransform")) != null) sp.objectReferenceValue = null;
            if ((sp = Prop("_oneGrabTransformer")) != null) sp.objectReferenceValue = null;     // the SDK creates its free transformer
            if ((sp = Prop("_twoGrabTransformer")) != null) sp.objectReferenceValue = null;
            so.ApplyModifiedProperties();
        }
        var hgi = GetOrAdd<HandGrabInteractable>(proxy.gameObject);
        GrabbingRule pinch = grabSource != null ? grabSource.PinchGrabRules : GrabbingRule.DefaultPinchRule;
        GrabbingRule palm = grabSource != null ? grabSource.PalmGrabRules : GrabbingRule.DefaultPalmRule;
        hgi.InjectAllHandGrabInteractable(GrabTypeFlags.Pinch, rb, pinch, palm);
        hgi.InjectOptionalPointableElement(grab);
        EditorUtility.SetDirty(hgi);
        var rereg = GetOrAdd<ReRegisterInteractable>(proxy.gameObject);
        rereg.interactables = new Behaviour[] { grab, hgi };
        EditorUtility.SetDirty(rereg);
        EditorUtility.SetDirty(proxy.gameObject);

        mp.pivotLocal = barPos;
        mp.centreLocal = centre;
        mp.footprint = new Vector2(fmx.x - fmn.x, fmx.z - fmn.z);
        mp.footprintYaw = yaw;
        mp.baseBelowPivot = barPos.y - baseY;
        mp.height = fmx.y - fmn.y;
        mp.bar = bar; mp.barRenderer = mr; mp.barShownScale = Vector3.one;
        mp.grabProxy = proxy; mp.grabbable = grab; mp.handGrab = hgi;
        visibility = (visibility ?? new Transform[0]).Where(t => t != null).ToArray();
        mp.visibilitySources = visibility;
        mp.visibilityShownScale = visibility.Select(ShownScaleX).ToArray();
        EditorUtility.SetDirty(mp);
        EditorUtility.SetDirty(holder.gameObject);

        float reach = (barPos - Eye).magnitude;
        sb.AppendLine($"  {(created ? "created" : "updated")} {holderName} (id \"{id}\"): footprint {mp.footprint.x * 1000f:F0} x {mp.footprint.y * 1000f:F0} mm, {mp.height * 1000f:F0} mm tall, yaw {yaw:F0}; base at root y {baseY:F3}; bar at {P(barPos)} root-local, {reach:F2} m from the eye{(reach > 0.6f ? "  WARNING: the bar is a long reach" : "")}{(baseY < -0.002f ? "  WARNING: below the desk plane" : "")}");
        foreach (var n in notes) sb.AppendLine("      " + n);
        if (visibility.Length > 0) sb.AppendLine("      bar shows only while shown: " + string.Join(", ", visibility.Select(t => t.name)));
        return mp;
    }

    // ───────────────────────────── entry ─────────────────────────────

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildMovablePanelsV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildMovablePanelsV1: no open scene.\n";
        var sb = new StringBuilder("== BuildMovablePanelsV1 ==\n");
        var all = All();
        var ws = Find(all, "WorkspaceRoot");
        var spatial = scene.GetRootGameObjects().Select(g => g.transform).FirstOrDefault(t => t.name == "Spatial");
        if (ws == null || spatial == null) return "BuildMovablePanelsV1: WorkspaceRoot or Spatial not found; nothing changed.\n";
        int layer = ws.gameObject.layer;

        var rig = spatial.Find("NearUserRig");
        if (rig == null) { rig = new GameObject("NearUserRig").transform; Undo.RegisterCreatedObjectUndo(rig.gameObject, "NearUserRig"); rig.SetParent(spatial, false); }
        rig.gameObject.layer = layer;
        rig.gameObject.SetActive(true);                                  // while building; set to the root's state below
        rig.SetPositionAndRotation(ws.position, ws.rotation);
        rig.localScale = ws.lossyScale;
        if ((ws.lossyScale - Vector3.one).sqrMagnitude > 1e-4f) sb.AppendLine($"  WARNING: WorkspaceRoot scale is {ws.lossyScale}; panels assume 1");

        EnsureFolder("Assets/MuseDesk/Art", "Generated");
        EnsureFolder("Assets/MuseDesk/Art", "Materials");
        EnsureFolder("Assets/MuseDesk/Art/Materials", "UI");
        Mesh pill = PillMesh();
        Material barMat = BarMaterial();

        var stamp = Find(all, "StampApprove");
        var grabSource = stamp != null ? stamp.GetComponent<HandGrabInteractable>() : null;
        int grabLayer = stamp != null ? stamp.gameObject.layer : layer;
        sb.AppendLine(grabSource != null
            ? $"  grab stack modelled on StampApprove (layer {LayerMask.LayerToName(grabLayer)}, its pinch rules; pinch only)"
            : "  NOTE: StampApprove has no HandGrabInteractable to model the grab on: SDK default pinch rules used");

        // ── panels ──
        sb.AppendLine("-- panels --");
        Transform dock = Find(all, "TaskDock"), board = Find(all, "TaskBoard"), plaque = Find(all, "ModePlaque"),
                  receipt = Find(all, "CompletionReceipt"), focus = Find(all, "FocusToggle");
        var consoleC = Object.FindAnyObjectByType<ModeConsole>(FindObjectsInactive.Include);
        Transform console = consoleC != null && consoleC.gameObject.activeSelf ? consoleC.transform : null;

        var panels = new List<MovablePanel>();
        panels.Add(Panel(rig, "dock", "Panel_Dock", new[] { dock, board }, new[] { dock }, null, pill, barMat, layer, grabLayer, grabSource, sb));
        panels.Add(Panel(rig, "plaque", "Panel_Plaque", new[] { plaque }, new[] { plaque }, null, pill, barMat, layer, grabLayer, grabSource, sb));
        if (console != null)
        {
            panels.Add(Panel(rig, "mode", "Panel_Mode", new[] { console }, new[] { console }, null, pill, barMat, layer, grabLayer, grabSource, sb));
            Retire(rig, "Panel_Focus", sb);
        }
        else
        {
            sb.AppendLine("  NOTE: no active ModeConsole (run BuildModeConsoleV1 first): the Focus key gets a panel of its own; the old mode strip is not movable");
            panels.Add(Panel(rig, "focus", "Panel_Focus", new[] { focus }, new[] { focus }, null, pill, barMat, layer, grabLayer, grabSource, sb));
            Retire(rig, "Panel_Mode", sb);
        }
        panels.Add(Panel(rig, "receipt", "Panel_Receipt", new[] { receipt }, new[] { receipt }, new[] { receipt }, pill, barMat, layer, grabLayer, grabSource, sb));
        panels.RemoveAll(p => p == null);

        // Default layout: report panels whose footprints already touch (they are left as they are).
        for (int i = 0; i < panels.Count; i++)
            for (int j = i + 1; j < panels.Count; j++)
            {
                MovablePanel a = panels[i], b = panels[j];
                if (Mathf.Abs(a.centreLocal.y - b.centreLocal.y) > (a.height + b.height) * 0.5f) continue;
                if (PanelLayoutMath.Overlaps(new Vector2(a.centreLocal.x, a.centreLocal.z), PanelLayoutMath.RotatedHalfExtents(a.footprint * 0.5f, a.footprintYaw),
                                             new Vector2(b.centreLocal.x, b.centreLocal.z), PanelLayoutMath.RotatedHalfExtents(b.footprint * 0.5f, b.footprintYaw), 0f))
                    sb.AppendLine($"  NOTE: {a.name} and {b.name} already touch in the default layout (kept; a released panel is nudged clear of the other)");
            }

        // ── the Reset layout chip ──
        sb.AppendLine("-- reset chip --");
        PanelResetChip chip = null;
        if (focus != null)
        {
            Transform chipT = rig.Find("PanelResetChip");
            bool made = chipT == null;
            if (made)
            {
                var go = Object.Instantiate(focus.gameObject, rig);
                Undo.RegisterCreatedObjectUndo(go, "PanelResetChip");
                go.name = "PanelResetChip";
                chipT = go.transform;
                var face = go.GetComponent<FaceUser>();
                if (face != null) Object.DestroyImmediate(face);
            }
            chipT.gameObject.layer = layer;
            chipT.gameObject.SetActive(true);
            chipT.localScale = Vector3.one * ChipScale;                 // full size while the label is measured
            chipT.localPosition = new Vector3(0f, 0.10f, 0.40f); chipT.localRotation = Quaternion.identity;
            var tmp = chipT.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                float ls = Mathf.Abs(tmp.transform.lossyScale.x);
                if (ls > 1e-7f)
                {
                    tmp.text = "Reset\nlayout";
                    tmp.rectTransform.sizeDelta = new Vector2(0.9f * ChipScale / ls, 0.8f * ChipScale / ls);
                    tmp.enableAutoSizing = false;
                    tmp.fontSize = 5.5f * 0.01f / ls;
                    tmp.textWrappingMode = TextWrappingModes.NoWrap;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.fontStyle = FontStyles.Bold;
                    tmp.color = OfficeTokens.Ink;
                    EditorUtility.SetDirty(tmp);
                }
            }
            var idle = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Key_ConsoleIdle.mat") ?? AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Key_FiledPoke.mat");
            var capT = Under(chipT, "Cap3D");
            var capR = capT != null ? capT.GetComponent<MeshRenderer>() : null;
            if (capR != null && idle != null) capR.sharedMaterial = idle;
            chip = GetOrAdd<PanelResetChip>(chipT.gameObject);
            chip.shownScale = Vector3.one * ChipScale;
            Rewire(chipT, chip, nameof(PanelResetChip.Press));
            chipT.localScale = Vector3.one * ChipScale * Hidden;        // saved hidden
            EditorUtility.SetDirty(chip);
            EditorUtility.SetDirty(chipT.gameObject);
            sb.AppendLine($"  {(made ? "created" : "updated")} NearUserRig/PanelResetChip (clone of FocusToggle, scale {ChipScale}, \"Reset layout\") -> PanelResetChip.Press -> MovablePanel.ResetAll; saved hidden");
        }
        else sb.AppendLine("  SKIP: no FocusToggle to clone; MovablePanel.ResetAll() is still callable (voice, driver)");
        foreach (var p in panels) { p.resetChip = chip; EditorUtility.SetDirty(p); }

        // ── NearUserKit ──
        sb.AppendLine("-- NearUserKit --");
        var kit = spatial.GetComponent<NearUserKit>();
        if (kit == null) kit = Undo.AddComponent<NearUserKit>(spatial.gameObject);
        kit.rig = rig;
        kit.workspaceRoot = ws;
        kit.followDesk = true;
        kit.items = LooseKitItems.Select(n => Find(all, n)).Where(t => t != null && t.IsChildOf(ws)).ToArray();
        kit.decisionOnly = DecisionOnly.Select(n => Find(all, n)).Where(t => t != null).ToArray();
        EditorUtility.SetDirty(kit);
        sb.AppendLine($"  followDesk = true (the rig copies the office root's pose, scale and active state in Desk mode); still re-parented while away: {string.Join(", ", kit.items.Select(t => t.name))}; decision-only (unchanged): {string.Join(", ", kit.decisionOnly.Select(t => t.name))}");

        // The rig starts as the office root does (hidden until the desk is placed, if the root is saved hidden).
        rig.gameObject.SetActive(ws.gameObject.activeSelf);
        sb.AppendLine($"  NearUserRig saved {(ws.gameObject.activeSelf ? "active" : "INACTIVE, like WorkspaceRoot (NearUserKit activates it with the root)")}");

        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    /// <summary>A panel holder that no longer applies: its members go home, the holder and proxy are switched off.</summary>
    static void Retire(Transform rig, string holderName, StringBuilder sb)
    {
        var holder = rig.Find(holderName);
        if (holder == null) return;
        var mp = holder.GetComponent<MovablePanel>();
        if (mp != null) SendHome(mp, sb);
        holder.gameObject.SetActive(false);
        if (mp != null && mp.grabProxy != null) mp.grabProxy.gameObject.SetActive(false);
    }

    static void SendHome(MovablePanel mp, StringBuilder sb)
    {
        if (mp.movedChildren == null) return;
        for (int i = 0; i < mp.movedChildren.Length; i++)
        {
            Transform m = mp.movedChildren[i];
            Transform home = mp.originalParents != null && i < mp.originalParents.Length ? mp.originalParents[i] : null;
            if (m == null || home == null || m.parent != mp.transform) continue;
            RebaseBoard(m, mp.transform, home);
            Undo.SetTransformParent(m, home, "panel member home");
            if (mp.originalSiblings != null && i < mp.originalSiblings.Length)
                m.SetSiblingIndex(Mathf.Min(mp.originalSiblings[i], home.childCount - 1));
            EditorUtility.SetDirty(m.gameObject);
            sb.AppendLine($"  {m.name} back under {home.name}");
        }
        mp.movedChildren = new Transform[0]; mp.originalParents = new Transform[0]; mp.originalSiblings = new int[0];
        EditorUtility.SetDirty(mp);
    }

    /// <summary>
    /// Undo the panels: every member goes back under its old parent (world pose kept), holders,
    /// proxies and the chip are deactivated (not destroyed), and NearUserKit gets its old item list
    /// with followDesk off. ModeConsole stays under the rig (BuildModeConsoleV1.Revert removes it).
    /// </summary>
    public static string Revert()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildMovablePanelsV1.Revert: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        var sb = new StringBuilder("== BuildMovablePanelsV1.Revert ==\n");
        var all = All();
        var ws = Find(all, "WorkspaceRoot");
        var spatial = scene.GetRootGameObjects().Select(g => g.transform).FirstOrDefault(t => t.name == "Spatial");
        var rig = spatial != null ? spatial.Find("NearUserRig") : null;
        if (ws == null || rig == null) return "BuildMovablePanelsV1.Revert: WorkspaceRoot or NearUserRig not found.\n";
        rig.gameObject.SetActive(true);
        rig.SetPositionAndRotation(ws.position, ws.rotation);
        rig.localScale = ws.lossyScale;
        foreach (var mp in rig.GetComponentsInChildren<MovablePanel>(true))
        {
            SendHome(mp, sb);
            if (mp.grabProxy != null) mp.grabProxy.gameObject.SetActive(false);
            mp.gameObject.SetActive(false);
        }
        var chip = rig.Find("PanelResetChip");
        if (chip != null) chip.gameObject.SetActive(false);
        var kit = spatial.GetComponent<NearUserKit>();
        if (kit != null)
        {
            all = All();
            kit.followDesk = false;
            kit.items = LegacyKitItems.Select(n => Find(all, n)).Where(t => t != null && t.IsChildOf(ws)).ToArray();
            EditorUtility.SetDirty(kit);
            sb.AppendLine("  NearUserKit: followDesk off; items = " + string.Join(", ", kit.items.Select(t => t.name)));
        }
        if (rig.GetComponentInChildren<ModeConsole>(false) != null)
            sb.AppendLine("  NOTE: ModeConsole is still under the rig, which no longer follows the office root: run BuildModeConsoleV1.Revert too, or Apply again.");
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }
}
