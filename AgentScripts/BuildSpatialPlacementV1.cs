using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Spatial;
using MuseDesk.Workspace;

/// <summary>
/// Room-aware workspace, scene part (docs/Engineering/SPATIAL_WORKSPACE_ARCHITECTURE.md). Adds, under
/// one new root object "Spatial" (NOT a child of WorkspaceRoot, so it does not travel with the office):
///   1. the director, room service, MRUK room reader (saved DISABLED), transition controller,
///      persistence and debug view (off), all on "Spatial";
///   2. "Spatial/CeilingPod": the pod controller with PLACEHOLDER visuals from primitives (a thin
///      mast, a ceiling mount, a platform under the office) because the pod art has not arrived;
///   3. "Spatial/PlacementGhost": the flat teal footprint outline with a facing arrow;
///   4. "Spatial/SpatialModeKeys": seven poke keys cloned from FocusToggle (Floor, Ceiling, Desk,
///      Next, Confirm, Cancel, Lower/Raise) in three slots beside the Focus key, plus a one-line
///      status plate. Everything is saved HIDDEN BY SCALE: with both flags off nothing is shown.
/// Both feature flags are left FALSE. Nothing existing is moved, re-parented or deactivated.
/// Idempotent; tolerates missing objects; saves the scene once.
///   unity command script run --file AgentScripts/BuildSpatialPlacementV1.cs --entry BuildSpatialPlacementV1.Apply
/// </summary>
public static class BuildSpatialPlacementV1
{
    const string MatDir = "Assets/MuseDesk/Art/Materials/UI";
    const float Hidden = 0.001f;                                   // ScaleEase.HiddenFactor
    static readonly Vector3 Eye = new Vector3(0f, 0.30f, 0.62f);   // seated eye, root-local

    // Office footprint at scale 1 (root-local metres): 1.0 wide, 0.7 deep, centred 0.12 m toward the user.
    const float FootW = 1.0f, FootD = 0.7f, FootCz = 0.12f;
    const float GhostLine = 0.015f;

    static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
    static readonly Color Paper = Hex("#F7F2E3");
    static readonly Color Ink = Hex("#1F2530");
    static readonly Color Plate = Hex("#1B1F27");
    static readonly Color OnPlate = Hex("#F3F4F6");
    static readonly Color Teal = Hex("#2FA39A");

    // ───────────────────────────── helpers (idioms from BuildOfficeV2 / FinalPass) ─────────────────────────────

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    static Transform Find(List<Transform> all, string name) => all.FirstOrDefault(t => t != null && t.name == name);
    static Transform Under(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t.name == name);

    static string P(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";

    /// <summary>An existing UI material; created (same recipe as BuildOfficeV2.Mat) only when it is missing.</summary>
    static Material Mat(string name, Color c, StringBuilder sb)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        if (!AssetDatabase.IsValidFolder("Assets/MuseDesk/Art/Materials")) AssetDatabase.CreateFolder("Assets/MuseDesk/Art", "Materials");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/MuseDesk/Art/Materials", "UI");
        m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", c);
        m.SetColor("_Color", c);
        m.enableInstancing = true;
        AssetDatabase.CreateAsset(m, path);
        sb.AppendLine($"  NOTE: material {name} was missing and was created");
        return m;
    }

    static Mesh PrimitiveMesh(PrimitiveType type)
    {
        var go = GameObject.CreatePrimitive(type);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(go);
        return mesh;
    }

    static Transform Holder(Transform parent, string name, int layer)
    {
        Transform t = parent != null ? parent.Find(name) : null;
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            if (parent != null) go.transform.SetParent(parent, false);
            t = go.transform;
        }
        t.gameObject.layer = layer;
        return t;
    }

    /// <summary>Find or create a child mesh (no collider).</summary>
    static Transform Shape(Transform parent, string name, Mesh mesh, Material mat, Vector3 lp, Quaternion lr, Vector3 ls)
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
        t.localPosition = lp;
        t.localRotation = lr;
        t.localScale = ls;
        EditorUtility.SetDirty(t.gameObject);
        return t;
    }

    /// <summary>Find or create a TextMeshPro label at scale 0.01 (font size N = about N mm, box in cm).</summary>
    static TMP_Text Label(Transform parent, string name, Vector3 lp)
    {
        var t = parent.Find(name);
        TextMeshPro tmp;
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            tmp = go.AddComponent<TextMeshPro>();
            go.transform.SetParent(parent, false);
            t = go.transform;
        }
        else
        {
            tmp = t.GetComponent<TextMeshPro>();
            if (tmp == null) tmp = t.gameObject.AddComponent<TextMeshPro>();
            t = tmp.transform;
        }
        t.gameObject.layer = parent.gameObject.layer;
        t.gameObject.SetActive(true);
        t.localPosition = lp;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one * 0.01f;
        var mr = t.GetComponent<MeshRenderer>();
        if (mr != null) { mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; }
        return tmp;
    }

    static void Style(TMP_Text t, string text, Vector2 boxCm, float minMm, float maxMm, Color color, FontStyles style,
                      TextAlignmentOptions align, bool wrap)
    {
        if (t == null) return;
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
        t.richText = true;
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

    /// <summary>Find or clone a poke key from a template key (keeps the Housing3D / Cap3D structure).</summary>
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

    static void CapMaterial(Transform key, Material m)
    {
        if (m == null) return;
        var capT = Under(key, "Cap3D");
        var mr = capT != null ? capT.GetComponent<MeshRenderer>() : null;
        if (mr != null) { mr.sharedMaterial = m; EditorUtility.SetDirty(mr); }
    }

    /// <summary>Key label sized from the label's world scale (BuildOfficeV2.KeyLabel): glyphs mm tall, never below 5.5 mm.</summary>
    static TMP_Text KeyLabel(Transform key, string text, float mm, Color color)
    {
        var tmp = key.GetComponentInChildren<TMP_Text>(true);
        if (tmp == null) return null;
        float s = Mathf.Abs(tmp.transform.lossyScale.x);
        if (s < 1e-7f) return tmp;
        float capWidth = Mathf.Abs(key.lossyScale.x) * 0.98f;
        var cap = Under(key, "Cap3D");
        if (cap != null && OwnMeshBounds(cap, key, out var cmn, out var cmx))
            capWidth = (cmx.x - cmn.x) * Mathf.Abs(key.lossyScale.x);
        tmp.text = text;
        tmp.rectTransform.sizeDelta = new Vector2(0.90f * capWidth / s, 0.50f * capWidth / s);
        float max = mm * 0.01f / s;
        float min = Mathf.Min(max, 5.5f * 0.01f / s);
        tmp.enableAutoSizing = min < max;
        tmp.fontSizeMax = max; tmp.fontSizeMin = min; tmp.fontSize = max;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        EditorUtility.SetDirty(tmp);
        return tmp;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    // ───────────────────────────── entry ─────────────────────────────

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildSpatialPlacementV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildSpatialPlacementV1: no open scene.\n";

        var sb = new StringBuilder();
        var all = All();
        var ws = Find(all, "WorkspaceRoot");
        if (ws == null) return "BuildSpatialPlacementV1: WorkspaceRoot not found; nothing changed.\n";
        int layer = ws.gameObject.layer;

        sb.AppendLine("== BuildSpatialPlacementV1 ==");
        sb.AppendLine("New objects are under the scene root object \"Spatial\" (not under WorkspaceRoot). Both feature flags stay FALSE.");

        // The root holder: a scene root at the origin, unit scale.
        Transform spatial = scene.GetRootGameObjects().Select(g => g.transform).FirstOrDefault(t => t.name == "Spatial");
        bool createdRoot = spatial == null;
        if (createdRoot) spatial = Holder(null, "Spatial", layer);
        spatial.gameObject.layer = layer;
        spatial.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        spatial.localScale = Vector3.one;
        if (!spatial.gameObject.activeSelf) spatial.gameObject.SetActive(true);
        sb.AppendLine($"  {(createdRoot ? "created" : "found")} Spatial");

        var quad = PrimitiveMesh(PrimitiveType.Quad);
        var cube = PrimitiveMesh(PrimitiveType.Cube);
        var matPlate = Mat("OfficeV2_Plate", Plate, sb);
        var matTeal = Mat("OfficeV2_Strip_Teal", Teal, sb);

        // ── 1. services on "Spatial" ──
        sb.AppendLine("-- 1. director and services --");
        var go = spatial.gameObject;
        var transition = GetOrAdd<WorkspaceTransitionController>(go);
        transition.workspaceRoot = ws;
        var mruk = GetOrAdd<MrukRoomGeometry>(go);
        mruk.enabled = false;                                   // enabled by the service only when a flag is on
        var service = GetOrAdd<RoomPlacementService>(go);
        service.mrukRoom = mruk;
        var persistence = GetOrAdd<WorkspacePlacementPersistence>(go);
        var director = GetOrAdd<SpatialWorkspaceDirector>(go);
        var debug = GetOrAdd<WorkspacePlacementDebugView>(go);
        debug.director = director;
        debug.show = false;

        var placement = Object.FindAnyObjectByType<WorkspacePlacement>(FindObjectsInactive.Include);
        if (placement == null) sb.AppendLine("  WARNING: no WorkspacePlacement in the scene; the director will not wait for the desk placement");
        else if (placement.workspaceRoot != ws) sb.AppendLine($"  WARNING: WorkspacePlacement.workspaceRoot is {(placement.workspaceRoot != null ? placement.workspaceRoot.name : "null")}, not WorkspaceRoot");

        director.flags.floorModeEnabled = false;
        director.flags.ceilingModeEnabled = false;
        director.workspaceRoot = ws;
        director.deskPlacement = placement;
        director.roomService = service;
        director.transition = transition;
        director.persistence = persistence;
        director.footprint = new Vector2(FootW, FootD);
        director.floorSettings.footprintCenterOffset = new Vector2(0f, FootCz);
        director.ceilingSettings.footprintCenterOffset = new Vector2(0f, FootCz);
        director.allowScaleChange = false;
        sb.AppendLine("  Spatial: SpatialWorkspaceDirector (floorModeEnabled=false, ceilingModeEnabled=false, allowScaleChange=false), RoomPlacementService, MrukRoomGeometry (component DISABLED), WorkspaceTransitionController, WorkspacePlacementPersistence, WorkspacePlacementDebugView (show=false)");
        sb.AppendLine($"  wired: workspaceRoot=WorkspaceRoot, deskPlacement={(placement != null ? placement.gameObject.name + "/WorkspacePlacement" : "null")}");

        // ── 2. ceiling pod with placeholder visuals ──
        sb.AppendLine("-- 2. ceiling pod (placeholder geometry: the pod art has not arrived) --");
        var podT = Holder(spatial, "CeilingPod", layer);
        podT.localPosition = Vector3.zero; podT.localRotation = Quaternion.identity; podT.localScale = Vector3.one;
        if (!podT.gameObject.activeSelf) podT.gameObject.SetActive(true);
        var pod = GetOrAdd<CeilingPodController>(podT.gameObject);
        pod.transition = transition;
        var visuals = Holder(podT, "Visuals", layer);
        visuals.localPosition = Vector3.zero; visuals.localRotation = Quaternion.identity; visuals.localScale = Vector3.one;
        visuals.gameObject.SetActive(true);                     // children are laid out, then the holder is switched off
        // Names deliberately do not end in Plate / Sheet / Band / Strip / Chip: the Kit builder must not swap these cube meshes.
        var mast = Shape(visuals, "PodMast", cube, matPlate, new Vector3(0f, 1.6f, 0f), Quaternion.identity, new Vector3(0.02f, 1f, 0.02f));
        var mount = Shape(visuals, "PodMount", cube, matPlate, new Vector3(0f, 2.1f, 0f), Quaternion.identity, new Vector3(0.12f, 0.02f, 0.12f));
        var shelf = Shape(visuals, "PodShelf", cube, matPlate, new Vector3(0f, 1.1f, FootCz), Quaternion.identity, new Vector3(FootW, 0.012f, FootD));
        pod.visualsRoot = visuals.gameObject;
        pod.mast = mast;
        pod.ceilingMount = mount;
        pod.shelf = shelf;
        pod.mastSegments = new Transform[0];
        pod.shelfSize = new Vector3(FootW, 0.012f, FootD);
        pod.shelfCenterOffset = new Vector2(0f, FootCz);
        visuals.gameObject.SetActive(false);                    // no interactables inside: SetActive is safe here
        EditorUtility.SetDirty(pod);
        director.pod = pod;
        sb.AppendLine("  CeilingPod/Visuals (saved inactive): PodMast (2 cm cube stretched by the real distance), PodMount (12 cm), PodShelf (1.0 x 0.7 m), material OfficeV2_Plate. Replace by assigning art to CeilingPodController.mast / mastSegments / ceilingMount / shelf");

        // ── 3. ghost preview ──
        sb.AppendLine("-- 3. placement ghost --");
        var ghostT = Holder(spatial, "PlacementGhost", layer);
        ghostT.localPosition = Vector3.zero; ghostT.localRotation = Quaternion.identity; ghostT.localScale = Vector3.one;
        if (!ghostT.gameObject.activeSelf) ghostT.gameObject.SetActive(true);
        var ghost = GetOrAdd<WorkspaceGhostPreview>(ghostT.gameObject);
        var gv = Holder(ghostT, "Visual", layer);
        gv.localPosition = Vector3.zero; gv.localRotation = Quaternion.identity; gv.localScale = Vector3.one;
        gv.gameObject.SetActive(true);
        // A quad is visible from its -Z side; Euler(90,0,0) turns -Z up, so the outline is seen from above.
        Quaternion flat = Quaternion.Euler(90f, 0f, 0f);
        float hw = FootW * 0.5f, hd = FootD * 0.5f, t2 = GhostLine * 0.5f;
        var parts = new List<Renderer>();
        parts.Add(Shape(gv, "GhostEdgeNear", quad, matTeal, new Vector3(0f, 0f, FootCz + hd - t2), flat, new Vector3(FootW, GhostLine, 1f)).GetComponent<Renderer>());
        parts.Add(Shape(gv, "GhostEdgeFar", quad, matTeal, new Vector3(0f, 0f, FootCz - hd + t2), flat, new Vector3(FootW, GhostLine, 1f)).GetComponent<Renderer>());
        parts.Add(Shape(gv, "GhostEdgeA", quad, matTeal, new Vector3(hw - t2, 0f, FootCz), flat, new Vector3(GhostLine, FootD, 1f)).GetComponent<Renderer>());
        parts.Add(Shape(gv, "GhostEdgeB", quad, matTeal, new Vector3(-hw + t2, 0f, FootCz), flat, new Vector3(GhostLine, FootD, 1f)).GetComponent<Renderer>());
        // Facing arrow: points along +Z, the side the office presents to the user.
        const float shaftLen = 0.26f, headLen = 0.12f, arrowW = 0.03f;
        Vector3 tip = new Vector3(0f, 0.0005f, FootCz + 0.20f);
        parts.Add(Shape(gv, "GhostArrowShaft", quad, matTeal, tip - new Vector3(0f, 0f, shaftLen * 0.5f), flat, new Vector3(arrowW, shaftLen, 1f)).GetComponent<Renderer>());
        for (int i = 0; i < 2; i++)
        {
            float yaw = i == 0 ? 180f - 38f : 180f + 38f;
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 dir = q * Vector3.forward;
            parts.Add(Shape(gv, i == 0 ? "GhostArrowHeadA" : "GhostArrowHeadB", quad, matTeal, tip + dir * (headLen * 0.5f), q * flat, new Vector3(arrowW, headLen, 1f)).GetComponent<Renderer>());
        }
        ghost.visual = gv.gameObject;
        ghost.renderers = parts.ToArray();
        gv.gameObject.SetActive(false);
        EditorUtility.SetDirty(ghost);
        director.ghost = ghost;
        sb.AppendLine($"  PlacementGhost/Visual (saved inactive): {parts.Count} flat teal quads, footprint {FootW} x {FootD} m centred {FootCz} m toward the user, arrow toward +Z");

        // ── 4. mode keys ──
        sb.AppendLine("-- 4. mode keys (hidden by scale; shown only when a flag is on) --");
        var focus = Find(all, "FocusToggle");
        var keysT = Holder(spatial, "SpatialModeKeys", layer);
        if (!keysT.gameObject.activeSelf) keysT.gameObject.SetActive(true);
        keysT.localScale = Vector3.one;
        var strip = GetOrAdd<SpatialModeStrip>(keysT.gameObject);
        strip.workspaceRoot = ws;
        director.strip = strip;

        if (focus == null)
        {
            sb.AppendLine("  SKIP keys: FocusToggle not found (nothing to clone). The modes are still reachable through the director's public methods.");
        }
        else
        {
            float keyScale = Mathf.Abs(focus.lossyScale.x);
            float housingW = keyScale * 1.2f;
            var housing = Under(focus, "Housing3D");
            if (housing != null && OwnMeshBounds(housing, focus, out var hmn, out var hmx)) housingW = Mathf.Max(keyScale, (hmx.x - hmn.x) * keyScale);
            float spacing = housingW + 0.006f;

            // The strip takes the Focus key's orientation; its slots run along the user's right (root -X).
            Quaternion localRot = Quaternion.Inverse(ws.rotation) * focus.rotation;
            Vector3 focusLocal = ws.InverseTransformPoint(focus.position);
            Vector3 originLocal = focusLocal + new Vector3(-spacing, 0f, 0f);
            Vector3 step = Quaternion.Inverse(localRot) * new Vector3(-spacing, 0f, 0f);      // one slot, in strip-local space
            keysT.SetPositionAndRotation(ws.TransformPoint(originLocal), ws.rotation * localRot);

            strip.deskLocalPosition = originLocal;
            strip.deskLocalEuler = localRot.eulerAngles;
            strip.keyScale = keyScale;
            strip.slot0 = Vector3.zero;
            strip.slot1 = step;
            strip.slot2 = step * 2f;
            strip.hiddenOffset = new Vector3(0f, -0.06f, 0.03f);

            var matPaperKey = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Key_FiledPoke.mat");
            var matTealKey = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Key_RunAgain.mat");
            if (matPaperKey == null || matTealKey == null) sb.AppendLine("  NOTE: Key_FiledPoke / Key_RunAgain material not found; keys keep the Focus key's cap material");

            var defs = new (string name, string label, int slot, string method, bool primary)[]
            {
                ("FloorKey",   "Floor",   0, nameof(SpatialWorkspaceDirector.RequestFloor),     false),
                ("CeilingKey", "Ceiling", 1, nameof(SpatialWorkspaceDirector.RequestCeiling),   false),
                ("DeskKey",    "Desk",    0, nameof(SpatialWorkspaceDirector.RequestDesk),      false),
                ("NextKey",    "Next",    0, nameof(SpatialWorkspaceDirector.NextCandidate),    false),
                ("ConfirmKey", "Confirm", 1, nameof(SpatialWorkspaceDirector.ConfirmPlacement), true),
                ("CancelKey",  "Cancel",  2, nameof(SpatialWorkspaceDirector.CancelPlacement),  false),
                ("PodKey",     "Lower",   1, nameof(SpatialWorkspaceDirector.TogglePod),        true),
            };
            var keys = new Transform[defs.Length];
            TMP_Text podLabel = null;
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                Vector3 slot = step * d.slot;
                var key = CloneKey(keysT, focus, d.name, out bool created);
                key.localRotation = Quaternion.identity;
                key.localPosition = slot;
                key.localScale = Vector3.one * keyScale;                // full size while the label is measured
                var label = KeyLabel(key, d.label, 5.5f, Ink);
                if (d.name == "PodKey") podLabel = label;
                CapMaterial(key, d.primary ? matTealKey : matPaperKey);
                Rewire(key, director, d.method);
                key.localPosition = slot + strip.hiddenOffset;          // saved hidden: parked and scaled to almost nothing
                key.localScale = Vector3.one * keyScale * Hidden;
                EditorUtility.SetDirty(key.gameObject);
                keys[i] = key;
                sb.AppendLine($"  {(created ? "created" : "updated")} {d.name} \"{d.label}\" (clone of FocusToggle, uniform scale {keyScale:F4}) slot {d.slot} -> SpatialWorkspaceDirector.{d.method}");
            }
            strip.floorKey = keys[0]; strip.ceilingKey = keys[1]; strip.deskKey = keys[2];
            strip.nextKey = keys[3]; strip.confirmKey = keys[4]; strip.cancelKey = keys[5]; strip.podKey = keys[6];
            strip.podKeyLabel = podLabel;

            // One-line status plate above the middle slot; text 0.5 mm proud of the plate.
            const float plateW = 0.15f, plateH = 0.034f;
            Vector3 platePos = step + new Vector3(0f, housingW * 0.5f + 0.006f + plateH * 0.5f, 0f);
            var plate = Shape(keysT, "StatusPlate", quad, matPlate, platePos, Quaternion.identity, new Vector3(plateW, plateH, 1f));
            var status = Label(keysT, "StatusLabel", platePos + new Vector3(0f, 0f, -0.0005f));
            Style(status, "", new Vector2(plateW * 100f - 1.0f, plateH * 100f - 0.5f), 5.5f, 6.5f, OnPlate, FontStyles.Normal, TextAlignmentOptions.Center, true);
            strip.statusPlate = plate;
            strip.statusPlateScale = new Vector3(plateW, plateH, 1f);
            strip.statusLabel = status;
            plate.localScale = strip.statusPlateScale * Hidden;         // saved hidden

            Vector3 s2 = originLocal + new Vector3(-spacing * 2f, 0f, 0f);
            sb.AppendLine($"  slots (root-local): {P(originLocal)}, {P(originLocal + new Vector3(-spacing, 0f, 0f))}, {P(s2)}; spacing {spacing:F3} m; furthest slot is {(s2 - Eye).magnitude:F3} m from the eye");
            sb.AppendLine($"  StatusPlate {plateW} x {plateH} m above the middle slot (quad, Kit may round it), StatusLabel 5.5 to 6.5 mm, wrapped; both saved hidden");
            if ((s2 - Eye).magnitude > 0.55f) sb.AppendLine("  WARNING: the furthest slot is beyond the 0.55 m comfortable reach");
        }
        EditorUtility.SetDirty(strip);
        EditorUtility.SetDirty(director);
        EditorUtility.SetDirty(transition);
        EditorUtility.SetDirty(service);
        EditorUtility.SetDirty(mruk);
        EditorUtility.SetDirty(debug);
        EditorUtility.SetDirty(persistence);

        sb.AppendLine("-- summary --");
        sb.AppendLine("  flags: floorModeEnabled=false, ceilingModeEnabled=false. With both off the director publishes Desk state and nothing else runs.");
        sb.AppendLine("  to try in Play Mode: director.SetFlags(true, true); director.UseSyntheticTestRoom(); director.RequestFloor(); director.NextCandidate(); director.ConfirmPlacement(); director.RequestDesk();");

        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }
}
