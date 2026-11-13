using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using MuseDesk.Agents;
using MuseDesk.Art;
using MuseDesk.Spatial;

/// <summary>
/// Spatial workspace, art pass: replaces the primitive placeholders of BuildSpatialPlacementV1 and
/// BuildSpatialReachV1 with Antigravity's spatial prefabs (Assets/MuseDesk/Art/Prefabs/Spatial),
/// wired to the EXISTING logic components. Runs after BuildSpatialPlacementV1, BuildSpatialReachV1
/// and IntegrateSpatial. Idempotent; placeholders are deactivated, never deleted; saves once.
///
///   Floor    WorkspaceRoot/SpatialArt_FloorPlatform (FloorOfficePlatform or _Compact, scaled to the
///            measured office, plus FloorCourierStation at the delivery platform); shown by
///            FloorPlatformPresenter only in Floor mode.
///   Ceiling  WorkspaceRoot/SpatialArt_CeilingPod (SuspendedOfficePod under the office) shown by
///            CeilingPodArt only in Ceiling mode; Spatial/CeilingPod/Visuals gets the CeilingMount and
///            a three-part mast fed to CeilingPodController (mast top = the mount's SuspensionBottom,
///            mast bottom = the pod's DockingConnector).
///   Beacon   WorkspaceAttentionBeacon on top of the words plate inside the office cue's sizer.
///   Courier  DocumentCourier as CourierVisual's model, posed by its DocumentAttachSocket.
///   Ghosts   PlacementGhostPreview_Floor / _Ceiling under Spatial/PlacementGhost.
///   Perch    AgentConversationPerch under the summary plate.
///   Mailbox  SpatialMailbox + a caption in words at the near-user area (SpatialMailboxPresenter).
///   Reach    mode strip slots tightened / pulled toward the user; near-user offsets set so the
///            pieces do not overlap; the Floor key label is wired for "Send back".
///
/// NOT used, on purpose: the composite prefabs and the art-side CeilingWorkspacePodController and
/// DocumentCourierController (each moves its own root through the room, which would be a second
/// state machine beside WorkspaceTransitionController / RemoteDocumentDeliveryController), and the
/// standalone CeilingInspectionShelf (the pod model already contains the same shelf, bone-driven).
/// Every collider on the art instances is disabled: the art is visual only.
///   unity command script run --file AgentScripts/BuildSpatialArtV1.cs --entry BuildSpatialArtV1.Apply
/// </summary>
public static class BuildSpatialArtV1
{
    const string PrefabDir = "Assets/MuseDesk/Art/Prefabs/Spatial";
    const string UiMatDir = "Assets/MuseDesk/Art/Materials/UI";
    const string ArtMatDir = "Assets/MuseDesk/Art/Materials";
    const float Hidden = 0.001f;                                   // ScaleEase.HiddenFactor
    const float TextZ = -0.0005f;                                  // 0.5 mm proud of a plate
    static readonly Vector3 Eye = new Vector3(0f, 0.30f, 0.62f);   // seated eye, root-local
    const float ReachLimit = 0.55f, ReachTarget = 0.545f;

    // Tuning knobs for the coordinator (root-local metres). Zero = centred on the measured office.
    const float PlatformShiftZ = 0f;
    const float PodShiftZ = 0f;
    const float FitMargin = 0.01f;          // deck margin beyond the measured office, each side
    const float MaxPlatformScale = 1.6f;
    const float MaxPodScale = 1.7f;

    static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
    static readonly Color Plate = Hex("#1B1F27");
    static readonly Color OnPlate = Hex("#F3F4F6");

    // ───────────────────────────── helpers ─────────────────────────────

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    static Transform Find(List<Transform> all, string name) => all.FirstOrDefault(t => t != null && t.name == name);
    static Transform Under(Transform root, string name) =>
        root == null ? null : root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != null && t.name == name);

    static string P(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
    static string S(Vector3 v) => $"{v.x:F3} x {v.y:F3} x {v.z:F3} m";

    static Mesh PrimitiveMesh(PrimitiveType type)
    {
        var go = GameObject.CreatePrimitive(type);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(go);
        return mesh;
    }

    static Transform Holder(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            t = go.transform;
        }
        t.gameObject.layer = parent.gameObject.layer;
        if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        return t;
    }

    static void Local(Transform t, Vector3 lp, Quaternion lr, Vector3 ls)
    {
        t.localPosition = lp; t.localRotation = lr; t.localScale = ls;
        EditorUtility.SetDirty(t);
    }

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
        t.gameObject.SetActive(true);
        t.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = t.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        Local(t, lp, Quaternion.identity, ls);
        return t;
    }

    /// <summary>Find or create a TextMeshPro label at scale 0.01 (font size N = about N mm, box in cm).</summary>
    static TMP_Text Label(Transform parent, string name, Vector3 lp, string text, Vector2 boxCm, float minMm, float maxMm,
                          Color color, FontStyles style)
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
        tmp.text = text;
        tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        tmp.rectTransform.sizeDelta = boxCm;
        tmp.enableAutoSizing = minMm < maxMm;
        tmp.fontSizeMin = minMm; tmp.fontSizeMax = maxMm; tmp.fontSize = maxMm;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.margin = Vector4.zero;
        tmp.richText = false;
        EditorUtility.SetDirty(tmp);
        return tmp;
    }

    static Material UiMat(string name, Color c)
    {
        string path = $"{UiMatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        if (!AssetDatabase.IsValidFolder(ArtMatDir)) AssetDatabase.CreateFolder("Assets/MuseDesk/Art", "Materials");
        if (!AssetDatabase.IsValidFolder(UiMatDir)) AssetDatabase.CreateFolder(ArtMatDir, "UI");
        m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", c);
        m.SetColor("_Color", c);
        m.enableInstancing = true;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static GameObject LoadPrefab(string prefabName, StringBuilder sb)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{prefabName}.prefab");
        if (asset == null) sb.AppendLine($"  WARNING: prefab {PrefabDir}/{prefabName}.prefab not found; that piece keeps its placeholder");
        return asset;
    }

    /// <summary>Find the named child or instantiate the prefab there (PrefabUtility.InstantiatePrefab).
    /// The instance is visual only: layer of the parent, no shadows, every collider disabled.</summary>
    static GameObject Art(Transform parent, GameObject asset, string instanceName, StringBuilder sb)
    {
        if (asset == null || parent == null) return null;
        GameObject go;
        var existing = parent.Find(instanceName);
        if (existing != null) go = existing.gameObject;
        else
        {
            go = PrefabUtility.InstantiatePrefab(asset, parent) as GameObject;
            if (go == null) { sb.AppendLine($"  WARNING: could not instantiate {asset.name}"); return null; }
            Undo.RegisterCreatedObjectUndo(go, instanceName);
            go.name = instanceName;
        }
        if (!go.activeSelf) go.SetActive(true);
        int colliders = 0;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = parent.gameObject.layer;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) { if (c.enabled) { c.enabled = false; EditorUtility.SetDirty(c); } colliders++; }
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            EditorUtility.SetDirty(r);
        }
        sb.AppendLine($"  {(existing != null ? "found" : "instantiated")} {asset.name} as {parent.name}/{instanceName} ({colliders} collider(s) disabled, shadows off)");
        return go;
    }

    /// <summary>Bounds of an object's meshes in <paramref name="space"/> (static meshes; skinned meshes by
    /// their bind-pose mesh bounds; TextMeshPro labels are skipped). False when nothing was measured.</summary>
    static bool MeshBounds(Transform root, Transform space, bool activeOnly, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        bool any = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>(!activeOnly))
        {
            if (r == null || r.GetComponent<TMP_Text>() != null) continue;
            if (activeOnly && !r.enabled) continue;
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
            else { var f = r.GetComponent<MeshFilter>(); if (f != null) mesh = f.sharedMesh; }
            if (mesh == null) continue;
            var b = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var l = space.InverseTransformPoint(r.transform.TransformPoint(c));
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
            }
            any = true;
        }
        return any;
    }

    /// <summary>Static (non-skinned, non-text) mesh bounds only: used for the office, whose agents are skinned.</summary>
    static bool StaticBounds(Transform root, Transform space, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        bool any = false;
        foreach (var f in root.GetComponentsInChildren<MeshFilter>(false))
        {
            if (f == null || f.sharedMesh == null || f.GetComponent<TMP_Text>() != null) continue;
            var r = f.GetComponent<MeshRenderer>();
            if (r == null || !r.enabled) continue;
            if (f.transform.lossyScale.sqrMagnitude < 1e-4f) continue;      // hidden by scale
            var b = f.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var l = space.InverseTransformPoint(f.transform.TransformPoint(c));
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
            }
            any = true;
        }
        return any;
    }

    /// <summary>An axis-aligned rectangle in root-local XZ.</summary>
    struct Rect2
    {
        public float x0, x1, z0, z1;
        public Rect2(float x0, float x1, float z0, float z1) { this.x0 = x0; this.x1 = x1; this.z0 = z0; this.z1 = z1; }
        public float W => x1 - x0;
        public float D => z1 - z0;
        public float Cx => (x0 + x1) * 0.5f;
        public float Cz => (z0 + z1) * 0.5f;
        public bool Overlaps(Rect2 o) => x0 < o.x1 && o.x0 < x1 && z0 < o.z1 && o.z0 < z1;
        public Rect2 Union(Rect2 o) => new Rect2(Mathf.Min(x0, o.x0), Mathf.Max(x1, o.x1), Mathf.Min(z0, o.z0), Mathf.Max(z1, o.z1));
        public override string ToString() => $"x {x0:F3}..{x1:F3}, z {z0:F3}..{z1:F3} ({W:F3} x {D:F3} m)";
    }

    static bool RectOf(Transform obj, Transform ws, out Rect2 rect)
    {
        rect = new Rect2();
        if (obj == null || !obj.gameObject.activeInHierarchy) return false;
        if (!StaticBounds(obj, ws, out var mn, out var mx)) return false;
        rect = new Rect2(mn.x, mx.x, mn.z, mx.z);
        return true;
    }

    /// <summary>Warn unless a socket has unit scale relative to its prefab instance (the 100x armature gotcha).</summary>
    static Transform Socket(GameObject instance, string name, StringBuilder sb)
    {
        if (instance == null) return null;
        var s = Under(instance.transform, name);
        if (s == null) { sb.AppendLine($"  WARNING: socket {name} not found in {instance.name}"); return null; }
        Vector3 a = s.lossyScale, b = instance.transform.lossyScale;
        float k = Mathf.Abs(b.x) > 1e-6f ? a.x / b.x : 0f;
        bool unit = Mathf.Abs(k - 1f) < 1e-2f && Mathf.Abs(a.y / Mathf.Max(1e-6f, Mathf.Abs(b.y)) - 1f) < 1e-2f;
        bool underBone = s.parent != null && (s.parent.name.StartsWith("Bone_") || s.parent.name.EndsWith("_Armature"));
        if (!unit) sb.AppendLine($"  WARNING: socket {instance.name}/{name} is NOT unit scale relative to its prefab (x{k:F3}); anything parented to it would be mis-scaled");
        if (underBone) sb.AppendLine($"  NOTE: socket {instance.name}/{name} is under animated bone {s.parent.name}; nothing is parented to it here");
        return s;
    }

    static T Ensure<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    static void Off(Transform parent, string path, StringBuilder sb)
    {
        var t = parent != null ? parent.Find(path) : null;
        if (t == null) return;
        if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); EditorUtility.SetDirty(t.gameObject); }
        sb.AppendLine($"  placeholder deactivated (kept): {parent.name}/{path}");
    }

    // ───────────────────────────── entry ─────────────────────────────

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildSpatialArtV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildSpatialArtV1: no open scene.\n";

        var sb = new StringBuilder();
        var all = All();
        var ws = Find(all, "WorkspaceRoot");
        if (ws == null) return "BuildSpatialArtV1: WorkspaceRoot not found; nothing changed.\n";
        Transform spatial = all.FirstOrDefault(t => t != null && t.name == "Spatial" && !t.IsChildOf(ws));
        if (spatial == null) return "BuildSpatialArtV1: scene object Spatial not found. Run BuildSpatialPlacementV1 and BuildSpatialReachV1 first; nothing changed.\n";
        var reach = spatial.Find("SpatialReach");
        var director = spatial.GetComponent<SpatialWorkspaceDirector>();
        var podT = spatial.Find("CeilingPod");
        var pod = podT != null ? podT.GetComponent<CeilingPodController>() : null;
        var ghostT = spatial.Find("PlacementGhost");
        var ghost = ghostT != null ? ghostT.GetComponent<WorkspaceGhostPreview>() : null;
        var keysT = spatial.Find("SpatialModeKeys");
        var strip = keysT != null ? keysT.GetComponent<SpatialModeStrip>() : null;
        var attention = reach != null ? reach.GetComponent<AttentionController>() : null;

        sb.AppendLine("== BuildSpatialArtV1 ==");
        if (director == null || pod == null || ghost == null) sb.AppendLine("  WARNING: placement side incomplete (director / pod / ghost missing): run BuildSpatialPlacementV1 first. Those pieces are skipped.");
        if (reach == null || attention == null) sb.AppendLine("  WARNING: reach side incomplete (SpatialReach / AttentionController missing): run BuildSpatialReachV1 first. Those pieces are skipped.");
        Vector3 wsScale = ws.lossyScale;
        if (Mathf.Abs(wsScale.x - 1f) > 1e-3f) sb.AppendLine($"  WARNING: WorkspaceRoot world scale is {wsScale.x:F3}; the art is laid out for scale 1.");

        var quad = PrimitiveMesh(PrimitiveType.Quad);
        var cylinder = PrimitiveMesh(PrimitiveType.Cylinder);
        var matPlate = UiMat("OfficeV2_Plate", Plate);

        // ── 0. the real office footprint (root-local XZ), measured from static meshes ──
        sb.AppendLine("-- 0. office footprint (root-local, measured) --");
        Rect2 office = new Rect2(-0.45f, 0.45f, -0.14f, 0.36f);        // assumed when the measurement is implausible
        bool measured = false;
        Rect2 acc = new Rect2();
        var parts = new Dictionary<string, Rect2>();
        foreach (var n in new[] { "ResearchStation", "DevStation", "QAStation", "SoftwareFactory", "InboxTray", "OutboxTray" })
        {
            var t = Find(all, n);
            if (t == null || !t.IsChildOf(ws)) { sb.AppendLine($"  not found under WorkspaceRoot: {n}"); continue; }
            if (!RectOf(t, ws, out var r)) { sb.AppendLine($"  no static mesh measured: {n}"); continue; }
            parts[n] = r;
            sb.AppendLine($"  {n}: {r}  (origin {P(ws.InverseTransformPoint(t.position))})");
            acc = measured ? acc.Union(r) : r;
            measured = true;
        }
        if (measured && acc.W >= 0.6f && acc.W <= 1.15f && acc.D >= 0.3f && acc.D <= 0.8f)
        {
            office = acc;
            sb.AppendLine($"  office proper (stations, factory, trays): {office}");
        }
        else sb.AppendLine($"  WARNING: measured footprint {(measured ? acc.ToString() : "(nothing)")} is implausible; using the assumed {office}");

        // Sizes of the things the user sees near them, for the reach report at the end.
        float nearAhead = director != null ? director.nearAhead : 0.45f;
        float nearBelow = director != null ? director.nearBelowEye : 0.25f;

        // Read the courier first: the floor station and the delivery platform depend on its socket.
        var courierAsset = LoadPrefab("DocumentCourier", sb);
        var stationAsset = LoadPrefab("FloorCourierStation", sb);
        float courierSocketY = 0.048f, stationDockY = 0.025f;
        Vector2 stationSize = new Vector2(0.15f, 0.16f);
        if (courierAsset != null) { var s = courierAsset.transform.Find("DocumentAttachSocket"); if (s != null) courierSocketY = s.localPosition.y; }
        if (stationAsset != null)
        {
            var d = stationAsset.transform.Find("CourierDockPoint"); if (d != null) stationDockY = d.localPosition.y;
            var bc = stationAsset.GetComponent<BoxCollider>(); if (bc != null) stationSize = new Vector2(bc.size.x, bc.size.z);
        }

        var remote = reach != null ? reach.GetComponent<RemoteDocumentDeliveryController>() : null;
        Animator stationAnimator = null;

        // ── 1. floor office platform + courier station ──
        sb.AppendLine("-- 1. floor office (WorkspaceRoot/SpatialArt_FloorPlatform) --");
        {
            // Delivery platform: where the courier takes the paper, at the office edge on the user's side.
            Vector2 st = remote != null ? new Vector2(remote.platformLocal.x, remote.platformLocal.z) : new Vector2(-0.20f, 0.33f);
            Rect2 StationRect(Vector2 c) => new Rect2(c.x - stationSize.x * 0.5f, c.x + stationSize.x * 0.5f, c.y - stationSize.y * 0.5f, c.y + stationSize.y * 0.5f);
            bool Blocked(Vector2 c)
            {
                var r = StationRect(c);
                foreach (var n in new[] { "SoftwareFactory", "InboxTray", "OutboxTray" })
                    if (parts.TryGetValue(n, out var o) && r.Overlaps(o)) return true;
                return false;
            }
            int tries = 0;
            while (Blocked(st) && tries < 10) { st.y += 0.01f; tries++; }
            if (Blocked(st)) sb.AppendLine($"  WARNING: the courier station at ({st.x:F3}, {st.y:F3}) still overlaps the factory or a tray; move RemoteDocumentDeliveryController.platformLocal by hand");
            else if (tries > 0) sb.AppendLine($"  courier station moved {tries} cm toward the user to clear the factory / trays");

            Rect2 need = office.Union(StationRect(st));
            sb.AppendLine($"  footprint the platform must carry (office + courier station): {need}");

            var full = LoadPrefab("FloorOfficePlatform", sb);
            var compact = LoadPrefab("FloorOfficePlatform_Compact", sb);
            Vector2 DeckOf(GameObject a) { var bc = a != null ? a.GetComponent<BoxCollider>() : null; return bc != null ? new Vector2(bc.size.x, bc.size.z) : Vector2.zero; }
            Vector2 deckFull = DeckOf(full), deckCompact = DeckOf(compact);
            float needW = need.W + 2f * FitMargin, needD = need.D + 2f * FitMargin;
            bool useCompact = compact != null && deckCompact.x >= needW && deckCompact.y >= needD;
            GameObject asset = useCompact ? compact : (full != null ? full : compact);
            Vector2 deck = asset == compact ? deckCompact : deckFull;
            sb.AppendLine($"  decks: full {deckFull.x:F3} x {deckFull.y:F3} m, compact {deckCompact.x:F3} x {deckCompact.y:F3} m; needed {needW:F3} x {needD:F3} m -> {(asset != null ? asset.name : "none")}");

            if (asset != null && deck.x > 0.01f && deck.y > 0.01f)
            {
                float fit = Mathf.Max(1f, Mathf.Max(needW / deck.x, needD / deck.y));
                float s = Mathf.Min(fit, MaxPlatformScale);
                if (fit > MaxPlatformScale) sb.AppendLine($"  WARNING: the platform would need scale {fit:F2}; capped at {MaxPlatformScale:F2}, so the office overhangs it");
                var socket = asset.transform.Find("WorkspaceRoot");
                float deckY = socket != null ? socket.localPosition.y : 0.022f;

                var holder = Holder(ws, "SpatialArt_FloorPlatform");
                Local(holder, new Vector3(need.Cx, 0f, need.Cz + PlatformShiftZ), Quaternion.identity, Vector3.one);
                // Drop a stale instance of the other size so the choice above is what is shown.
                string keep = asset.name;
                foreach (var other in new[] { "FloorOfficePlatform", "FloorOfficePlatform_Compact" })
                    if (other != keep) { var stale = holder.Find(other); if (stale != null) { stale.gameObject.SetActive(false); sb.AppendLine($"  deactivated the unused {other} instance"); } }
                var inst = Art(holder, asset, keep, sb);
                if (inst != null)
                {
                    // The office stands on the deck: the art's own "WorkspaceRoot" socket (deck height)
                    // is put at the office root. The platform is scaled, never the office.
                    Local(inst.transform, new Vector3(0f, -deckY * s, 0f), Quaternion.identity, Vector3.one * s);
                    // The socket is renamed in this instance so that nothing that looks the office root
                    // up by name can ever find it instead (the art controller keeps its serialized reference).
                    var artRoot = inst.transform.Find("WorkspaceRoot");
                    if (artRoot != null) { artRoot.name = "PlatformOfficeSocket"; EditorUtility.SetDirty(artRoot.gameObject); }
                    Socket(inst, "FloorContact", sb);
                    var ctrl = inst.GetComponent<FloorOfficePlatformController>();
                    MeshBounds(inst.transform, holder, false, out var pmn, out var pmx);
                    sb.AppendLine($"  platform: uniform scale {s:F3} (art controller validates {(ctrl != null ? ctrl.MinUniformScale : 0f):F2}..{(ctrl != null ? ctrl.MaxUniformScale : 0f):F2}{(ctrl != null && s > ctrl.MaxUniformScale + 1e-3f ? ": ABOVE its range, set directly on the transform" : "")}), deck {deck.x * s:F3} x {deck.y * s:F3} m, overall {S(pmx - pmn)}, holder at root-local {P(holder.localPosition)}");
                    sb.AppendLine($"  floor contact: the office root's y in Floor mode IS the floor (FloorCandidate.position is 'on the floor') and the office content stands at root y = 0, so the deck top is at y = 0 and the platform body reaches {deckY * s * 100f:F1} cm BELOW the floor plane (its rim rises {(pmx.y) * 100f:F1} cm above it). Lifting the office instead needs a height offset in the Core placement, which this pass may not edit.");

                    var presenterHost = Holder(spatial, "SpatialArt");
                    Local(presenterHost, Vector3.zero, Quaternion.identity, Vector3.one);
                    var presenter = Ensure<FloorPlatformPresenter>(presenterHost.gameObject);
                    presenter.platform = holder;
                    presenter.platformShownScale = Vector3.one;
                    presenter.art = ctrl;
                    presenter.attention = attention;
                    EditorUtility.SetDirty(presenter);
                    sb.AppendLine($"  Spatial/SpatialArt: FloorPlatformPresenter (platform, art controller {(ctrl != null ? "set" : "MISSING")}, attention {(attention != null ? "set" : "MISSING")})");

                    // Courier station on the deck at the delivery platform (same holder: it appears with the platform).
                    var stationInst = Art(holder, stationAsset, "FloorCourierStation", sb);
                    if (stationInst != null)
                    {
                        Local(stationInst.transform, new Vector3(st.x - holder.localPosition.x, 0f, st.y - holder.localPosition.z), Quaternion.identity, Vector3.one);
                        Socket(stationInst, "CourierDockPoint", sb);
                        stationAnimator = stationInst.GetComponent<Animator>();
                        sb.AppendLine($"  FloorCourierStation at root-local ({st.x:F3}, 0, {st.y:F3}), {stationSize.x:F3} x {stationSize.y:F3} m, scale 1");
                    }
                    holder.localScale = Vector3.one * Hidden;              // revealed by FloorPlatformPresenter
                    holder.gameObject.SetActive(false);
                    EditorUtility.SetDirty(holder.gameObject);
                }
            }
            else sb.AppendLine("  SKIP: no floor platform prefab (or no deck collider to measure)");

            if (remote != null)
            {
                // The courier rests on the station's dock; the paper's follow point is its attach socket.
                remote.trayDrop = 0.005f;
                remote.platformLocal = new Vector3(st.x, stationDockY + courierSocketY + remote.trayDrop, st.y);
                EditorUtility.SetDirty(remote);
                sb.AppendLine($"  RemoteDocumentDeliveryController.platformLocal = {P(remote.platformLocal)} (station dock {stationDockY:F3} + courier socket {courierSocketY:F3} + drop {remote.trayDrop:F3}); trayDrop = {remote.trayDrop:F3}");
            }
        }

        // ── 2. ceiling pod, mount, mast ──
        sb.AppendLine("-- 2. ceiling pod (WorkspaceRoot/SpatialArt_CeilingPod, Spatial/CeilingPod/Visuals) --");
        Vector3 podOuter = new Vector3(0.707f, 0.332f, 0.388f); Vector3 podCentre = Vector3.zero; float podScale = 1f, podDeckY = 0.024f;
        Vector3 podHolderPos = new Vector3(office.Cx, 0f, office.Cz + PodShiftZ);
        if (pod != null)
        {
            var podAsset = LoadPrefab("SuspendedOfficePod", sb);
            var mountAsset = LoadPrefab("CeilingMount", sb);
            if (podAsset != null)
            {
                var bc = podAsset.GetComponent<BoxCollider>();
                Vector2 deck = bc != null ? new Vector2(bc.size.x, bc.size.z) : new Vector2(0.58f, 0.34f);
                var seat = podAsset.transform.Find("DeveloperStationSocket");
                podDeckY = seat != null ? seat.localPosition.y : 0.024f;

                var holder = Holder(ws, "SpatialArt_CeilingPod");
                Local(holder, podHolderPos, Quaternion.identity, Vector3.one);
                var inst = Art(holder, podAsset, "SuspendedOfficePod", sb);
                if (inst != null)
                {
                    Local(inst.transform, Vector3.zero, Quaternion.identity, Vector3.one);
                    if (MeshBounds(inst.transform, inst.transform, false, out var bmn, out var bmx)
                        && (bmx - bmn).x > 0.2f && (bmx - bmn).x < 2f && (bmx - bmn).y > 0.05f && (bmx - bmn).y < 1.5f)
                    { podOuter = bmx - bmn; podCentre = (bmx + bmn) * 0.5f; }
                    else sb.AppendLine("  NOTE: pod mesh bounds not measurable; using the inventory size 0.707 x 0.332 x 0.388 m");

                    float needW = office.W + 2f * FitMargin, needD = office.D + 2f * FitMargin;
                    float fit = Mathf.Max(1f, Mathf.Max(needW / deck.x, needD / deck.y));
                    float s = Mathf.Min(fit, MaxPodScale);
                    // The pod must stay inside the footprint the placement checks validated.
                    if (director != null)
                    {
                        float cap = Mathf.Min(director.footprint.x / podOuter.x, director.footprint.y / podOuter.z);
                        if (s > cap) { sb.AppendLine($"  pod scale {s:F2} would exceed the validated footprint {director.footprint.x:F2} x {director.footprint.y:F2} m; capped at {cap:F2}"); s = Mathf.Max(1f, cap); }
                    }
                    podScale = s;
                    Local(inst.transform, new Vector3(0f, -podDeckY * s, 0f), Quaternion.identity, Vector3.one * s);
                    float overW = office.W - deck.x * s, overD = office.D - deck.y * s;
                    sb.AppendLine($"  pod: uniform scale {s:F3}, deck {deck.x * s:F3} x {deck.y * s:F3} m (office {office.W:F3} x {office.D:F3} m{(overW > 0f || overD > 0f ? $": the office OVERHANGS the deck by {Mathf.Max(0f, overW) * 100f:F1} cm in width and {Mathf.Max(0f, overD) * 100f:F1} cm in depth, onto the wings" : "")}), overall {S(podOuter * s)}, deck top at root y = 0, holder at root-local {P(holder.localPosition)}");

                    var connector = Socket(inst, "DockingConnector", sb);
                    Socket(inst, "InspectionShelf", sb);
                    // The pod's little display says "POD READY / STANDBY" whatever the state: not true, so it is off.
                    var display = Under(inst.transform, "Pod_TaskDisplayTMP");
                    if (display != null && display.gameObject.activeSelf) { display.gameObject.SetActive(false); sb.AppendLine("  Pod_TaskDisplayTMP (static 'POD READY / STANDBY' text) switched off: it would not follow the real state"); }

                    Vector3 connLocal = connector != null ? ws.InverseTransformPoint(connector.position) : new Vector3(0f, 0.33f, 0f);
                    // Does the rear arch stand inside the stations?
                    if (parts.TryGetValue("DevStation", out var dev))
                        sb.AppendLine($"  rear arch / connector at root-local {P(connLocal)}; DevStation meshes span z {dev.z0:F3}..{dev.z1:F3}{(connLocal.z > dev.z0 ? ": WARNING the arch is INSIDE the station's depth range, check the capture and set PodShiftZ" : "")}");

                    var podArt = Ensure<CeilingPodArt>(podT.gameObject);
                    podArt.pod = pod;
                    podArt.podVisual = holder;
                    podArt.podShownScale = Vector3.one;
                    podArt.podAnimator = inst.GetComponent<Animator>();

                    // World-fixed parts under the pod controller's visuals root.
                    var visuals = podT.Find("Visuals");
                    if (visuals != null)
                    {
                        bool wasActive = visuals.gameObject.activeSelf;
                        visuals.gameObject.SetActive(true);
                        Off(visuals, "PodMast", sb);
                        Off(visuals, "PodMount", sb);
                        Off(visuals, "PodShelf", sb);

                        var mountInst = Art(visuals, mountAsset, "CeilingMount", sb);
                        Transform mountBottom = null;
                        float mountDepth = 0.118f;
                        if (mountInst != null)
                        {
                            Local(mountInst.transform, new Vector3(0f, 2.4f, 0f), Quaternion.identity, Vector3.one);
                            mountBottom = Socket(mountInst, "SuspensionBottom", sb);
                            Socket(mountInst, "CeilingAnchor", sb);
                            if (mountBottom != null) mountDepth = Mathf.Abs(mountInst.transform.InverseTransformPoint(mountBottom.position).y);
                            podArt.mountAnimator = mountInst.GetComponent<Animator>();
                            pod.ceilingMount = mountInst.transform;
                        }

                        // Mast: three round sections (thicker toward the ceiling), each a unit-height
                        // holder that CeilingPodController stretches over the real distance.
                        var mastMat = AssetDatabase.LoadAssetAtPath<Material>($"{ArtMatDir}/Mat_BrushedMetal.mat");
                        if (mastMat == null) { mastMat = matPlate; sb.AppendLine("  NOTE: Mat_BrushedMetal not found; the mast uses OfficeV2_Plate"); }
                        var segs = new Transform[3];
                        for (int i = 0; i < segs.Length; i++)
                        {
                            var seg = Holder(visuals, "PodMastSection" + i);
                            Local(seg, new Vector3(0f, 2.0f - 0.2f * i, 0f), Quaternion.identity, new Vector3(0.012f, 0.2f, 0.012f));
                            // Unity's cylinder is 2 units tall and 1 wide: half height makes the holder unit height.
                            Shape(seg, "Rod", cylinder, mastMat, Vector3.zero, new Vector3(1f, 0.5f, 1f));
                            segs[i] = seg;
                        }
                        pod.mast = null;
                        pod.shelf = null;                       // the pod model is the platform under the office
                        pod.mastSegments = segs;
                        pod.mastThickness = 0.010f;
                        pod.mastTop = mountBottom;
                        pod.mastBottom = connector;
                        pod.mountOffset = new Vector2(connLocal.x, connLocal.z);
                        pod.alignMountToDock = true;
                        pod.mastAttachHeight = connLocal.y;
                        sb.AppendLine($"  CeilingPodController: ceilingMount = CeilingMount (world-fixed on the ceiling above the dock, scale 1, reaching {mountDepth:F3} m below the ceiling), mastSegments = 3 round sections ({pod.mastThickness * 1.7f * 1000f:F0} / {pod.mastThickness * 1.35f * 1000f:F0} / {pod.mastThickness * 1000f:F0} mm), mastTop = SuspensionBottom, mastBottom = DockingConnector, mountOffset = ({pod.mountOffset.x:F3}, {pod.mountOffset.y:F3}); mast and shelf placeholders unassigned");

                        // The docked pod must hang below the mount with a little mast showing.
                        if (director != null)
                        {
                            float drop = director.ceilingSettings.dockDrop;
                            float gap = drop - connLocal.y - mountDepth;
                            if (gap < 0.03f)
                            {
                                float wanted = connLocal.y + mountDepth + 0.03f;
                                director.ceilingSettings.dockDrop = wanted;
                                sb.AppendLine($"  ceilingSettings.dockDrop {drop:F3} -> {wanted:F3} m so the docked pod's connector ({connLocal.y:F3} m above the root) clears the mount ({mountDepth:F3} m deep) by 3 cm");
                            }
                            else sb.AppendLine($"  docked: connector {connLocal.y:F3} m above the root, mount {mountDepth:F3} m deep, dockDrop {drop:F3} m -> {gap * 100f:F1} cm of mast showing when docked");
                            EditorUtility.SetDirty(director);
                        }
                        visuals.gameObject.SetActive(false);
                        if (wasActive) sb.AppendLine("  NOTE: CeilingPod/Visuals was active; saved inactive (the pod controller shows it in Ceiling mode)");
                    }
                    else sb.AppendLine("  WARNING: Spatial/CeilingPod/Visuals not found; no mount or mast");

                    EditorUtility.SetDirty(pod);
                    EditorUtility.SetDirty(podArt);
                    sb.AppendLine($"  Spatial/CeilingPod: CeilingPodArt (pod animator {(podArt.podAnimator != null ? "set" : "MISSING")}, mount animator {(podArt.mountAnimator != null ? "set" : "MISSING")}); clips follow CeilingPodMachine.StateChanged");
                    holder.localScale = Vector3.one * Hidden;              // revealed by CeilingPodArt
                    holder.gameObject.SetActive(false);
                    EditorUtility.SetDirty(holder.gameObject);
                }
            }
            sb.AppendLine("  CeilingInspectionShelf prefab NOT placed: the pod model contains the same shelf on Bone_InspectionShelf, opened by Pod_Shelf_Open on Engaged and closed by Pod_Shelf_Close in PreparingToRetract");
        }

        // ── 3. attention beacon ──
        sb.AppendLine("-- 3. attention beacon --");
        var beacon = reach != null ? reach.GetComponentInChildren<AttentionBeacon>(true) : null;
        if (beacon != null && beacon.officeSizer != null)
        {
            var sizer = beacon.officeSizer;
            Vector3 cueScale = beacon.officeCue != null ? beacon.officeCue.localScale : Vector3.one;
            if (beacon.officeCue != null) beacon.officeCue.localScale = Vector3.one;      // full size while laid out
            var modelHolder = Holder(sizer, "BeaconModel");
            // The cue turns its -Z to the head; the art faces +Z, so it is turned round. It stands on the plate.
            float baseY = beacon.plateHeight * 0.5f + 0.012f;
            Local(modelHolder, new Vector3(0f, baseY, 0f), Quaternion.Euler(0f, 180f, 0f), Vector3.one);
            var inst = Art(modelHolder, LoadPrefab("WorkspaceAttentionBeacon", sb), "WorkspaceAttentionBeacon", sb);
            if (inst != null)
            {
                Local(inst.transform, Vector3.zero, Quaternion.identity, Vector3.one);
                Socket(inst, "IconSocket", sb);
                MeshBounds(inst.transform, inst.transform, false, out var mn, out var mx);
                beacon.artBeacon = inst.GetComponent<SpatialBeaconVisualController>();
                inst.SetActive(false);                                   // AttentionBeacon switches it on with the cue
                EditorUtility.SetDirty(inst);
                Off(sizer, "Face_Blocking/BlockingMarkChip", sb);
                Off(sizer, "Face_Blocking/Mark", sb);
                Off(sizer, "Face_Error/ErrorTopBand", sb);
                sb.AppendLine($"  WorkspaceAttentionBeacon stands {baseY * 100f:F1} cm above the plate centre at sizer scale 1 ({S(mx - mn)} bind pose), sized for distance with the plate; the words plates (Needs you / Done / Problem) stay; saved inactive. artBeacon {(beacon.artBeacon != null ? "set" : "MISSING")}");
                sb.AppendLine("  levels -> art states: None Inactive, Passive WorkingNormally (shown only if showWorkingState), Suggestion OptionalSuggestion (unreachable), Completed CompletedWork, Error Error, Blocking BlockingDecision, no session Disconnected");
            }
            if (beacon.officeCue != null) beacon.officeCue.localScale = cueScale;

            // The agents' own "!" is scaled away while the office is remote.
            var registry = Object.FindAnyObjectByType<AgentRegistry>(FindObjectsInactive.Include);
            var alerts = new List<Transform>();
            if (registry != null && registry.agents != null)
                foreach (var a in registry.agents) if (a != null && a.alert != null) alerts.Add(a.alert.transform);
            beacon.agentAlerts = alerts.ToArray();
            beacon.hideAgentAlertsWhenRemote = true;
            // Near-user mirror: clear of the perch (moved up and right below).
            beacon.mirrorOffset = new Vector3(0.06f, 0.15f, 0.03f);
            EditorUtility.SetDirty(beacon);
            sb.AppendLine($"  agentAlerts = {alerts.Count} AgentView.alert object(s): scaled away while remote, restored otherwise (their active state stays with AgentRegistry); mirrorOffset = {P(beacon.mirrorOffset)}");
        }
        else sb.AppendLine("  SKIP: AttentionBeacon or its sizer not found");

        // ── 4. courier ──
        sb.AppendLine("-- 4. courier --");
        var courierT = reach != null ? reach.Find("Courier") : null;
        var courier = courierT != null ? courierT.GetComponent<CourierVisual>() : null;
        if (courier != null)
        {
            Vector3 saved = courierT.localScale;
            courierT.localScale = Vector3.one;
            var modelHolder = Holder(courierT, "CourierModel");
            // CourierVisual is posed with +Z away from the user; the art faces +Z, so it is turned round.
            Local(modelHolder, Vector3.zero, Quaternion.Euler(0f, 180f, 0f), Vector3.one);
            var inst = Art(modelHolder, courierAsset, "DocumentCourier", sb);
            if (inst != null)
            {
                Local(inst.transform, Vector3.zero, Quaternion.identity, Vector3.one);
                var socket = Socket(inst, "DocumentAttachSocket", sb);
                MeshBounds(inst.transform, inst.transform, false, out var mn, out var mx);
                courier.model = modelHolder.gameObject;
                courier.animator = inst.GetComponent<Animator>();
                courier.attachSocket = socket;
                courier.stationAnimator = stationAnimator;
                courier.shownScale = Vector3.one;
                foreach (var n in new[] { "TrayBed", "TrayRail", "PodLeft", "PodRight" }) Off(courierT, n, sb);
                modelHolder.gameObject.SetActive(false);                 // CourierVisual switches it on when shown
                sb.AppendLine($"  DocumentCourier ({S(mx - mn)} bind pose, scale 1) is CourierVisual's model; posed by DocumentAttachSocket at courier-local {(socket != null ? P(courierT.InverseTransformPoint(socket.position)) : "MISSING")}; animator {(courier.animator != null ? "set" : "MISSING")}, station animator {(stationAnimator != null ? "set" : "none (no floor station)")}; the paper is NOT parented to it");
            }
            courierT.localScale = saved.sqrMagnitude > 1e-12f ? saved : Vector3.one * Hidden;
            EditorUtility.SetDirty(courier);
        }
        else sb.AppendLine("  SKIP: Spatial/SpatialReach/Courier (CourierVisual) not found");

        // ── 5. ghost previews ──
        sb.AppendLine("-- 5. placement ghosts --");
        if (ghost != null)
        {
            // Floor: the outline is what will stand on the floor (the scaled platform).
            var floorHolderT = ws.Find("SpatialArt_FloorPlatform");
            var floorAsset = LoadPrefab("PlacementGhostPreview_Floor", sb);
            if (floorAsset != null)
            {
                Vector3 tSize = new Vector3(office.W, 0.05f, office.D), tCentre = new Vector3(office.Cx, 0f, office.Cz);
                if (floorHolderT != null)
                {
                    bool was = floorHolderT.gameObject.activeSelf; Vector3 keepScale = floorHolderT.localScale;
                    floorHolderT.gameObject.SetActive(true); floorHolderT.localScale = Vector3.one;
                    var plat = floorHolderT.Find("FloorOfficePlatform"); if (plat == null || !plat.gameObject.activeSelf) plat = floorHolderT.Find("FloorOfficePlatform_Compact");
                    if (plat != null && MeshBounds(plat, ws, false, out var pmn, out var pmx)) { tSize = pmx - pmn; tCentre = (pmx + pmn) * 0.5f; }
                    floorHolderT.localScale = keepScale; floorHolderT.gameObject.SetActive(was);
                }
                var holder = Holder(ghostT, "FloorArt");
                Local(holder, Vector3.zero, Quaternion.identity, Vector3.one);
                var inst = Art(holder, floorAsset, "PlacementGhostPreview_Floor", sb);
                if (inst != null)
                {
                    Local(inst.transform, Vector3.zero, Quaternion.identity, Vector3.one);
                    FitGhost(inst, floorAsset, holder, tSize, tCentre, 0f, false, sb);
                    ghost.floorArt = holder.gameObject;
                    ghost.floorArtController = inst.GetComponent<PlacementGhostPreviewController>();
                }
                holder.gameObject.SetActive(false);
            }
            // Ceiling: the preview is where the pod comes down (the inspection pose), sized to the pod.
            var ceilAsset = LoadPrefab("PlacementGhostPreview_Ceiling", sb);
            if (ceilAsset != null)
            {
                Vector3 tSize = podOuter * podScale;
                Vector3 tCentre = podHolderPos + new Vector3(podCentre.x * podScale, 0f, podCentre.z * podScale);
                float top = (podCentre.y + podOuter.y * 0.5f - podDeckY) * podScale;      // the pod's top above the root
                var holder = Holder(ghostT, "CeilingArt");
                Local(holder, Vector3.zero, Quaternion.identity, Vector3.one);
                var inst = Art(holder, ceilAsset, "PlacementGhostPreview_Ceiling", sb);
                if (inst != null)
                {
                    Local(inst.transform, Vector3.zero, Quaternion.identity, Vector3.one);
                    FitGhost(inst, ceilAsset, holder, tSize, tCentre, top, true, sb);
                    ghost.ceilingArt = holder.gameObject;
                    ghost.ceilingArtController = inst.GetComponent<PlacementGhostPreviewController>();
                }
                holder.gameObject.SetActive(false);
            }
            if (ghost.visual != null && ghost.visual.activeSelf) ghost.visual.SetActive(false);
            sb.AppendLine("  the seven teal quads (PlacementGhost/Visual) stay in the scene, inactive, as the fallback when no art is assigned");
            sb.AppendLine("  NOTE: the ghosts use Antigravity's transparent material Mat_Spatial_Ghost (the only transparent surface this pass adds)");
            EditorUtility.SetDirty(ghost);
        }
        else sb.AppendLine("  SKIP: WorkspaceGhostPreview not found");

        // ── 6. perch ──
        sb.AppendLine("-- 6. summary perch --");
        var perch = reach != null ? reach.GetComponent<PerchController>() : null;
        if (perch != null && perch.stand != null)
        {
            var stand = perch.stand;
            Vector3 saved = stand.localScale;
            stand.localScale = Vector3.one;
            float plateH = 0.075f;
            var plate = stand.Find("PerchPlate");
            if (plate != null) plateH = Mathf.Abs(plate.localScale.y);
            const float k = 0.6f;                                        // the model under a 13 cm plate
            var modelHolder = Holder(stand, "PerchModel");
            var asset = LoadPrefab("AgentConversationPerch", sb);
            Vector3 standSocket = new Vector3(0f, 0.019f, -0.006f);
            if (asset != null) { var s = asset.transform.Find("AgentStandSocket"); if (s != null) standSocket = s.localPosition; }
            // Turned round (the stand shows its -Z to the head). The plate's lower edge rests where an agent would stand.
            Vector3 pos = new Vector3(0f, -plateH * 0.5f - 0.002f, 0f) - Quaternion.Euler(0f, 180f, 0f) * (standSocket * k);
            Local(modelHolder, pos, Quaternion.Euler(0f, 180f, 0f), Vector3.one * k);
            var inst = Art(modelHolder, asset, "AgentConversationPerch", sb);
            if (inst != null)
            {
                Local(inst.transform, Vector3.zero, Quaternion.identity, Vector3.one);
                Socket(inst, "AgentStandSocket", sb);
                MeshBounds(inst.transform, inst.transform, false, out var mn, out var mx);
                perch.artPerch = inst.GetComponent<AgentConversationPerchController>();
                inst.SetActive(false);                                   // PerchController switches it on when it opens
                Off(stand, "PerchFoot", sb);
                sb.AppendLine($"  AgentConversationPerch ({S((mx - mn) * k)} at scale {k:F2}) under the summary plate at stand-local {P(pos)}; plate and text kept; no agent is mounted or moved; artPerch {(perch.artPerch != null ? "set" : "MISSING")}");
            }
            stand.localScale = saved.sqrMagnitude > 1e-12f ? saved : Vector3.one * Hidden;
            // Up and to the right, clear of the mode strip's keys and status plate.
            perch.standOffset = new Vector3(0.24f, 0.12f, 0.05f);
            EditorUtility.SetDirty(perch);
            sb.AppendLine($"  PerchController.standOffset = {P(perch.standOffset)} (was beside the mode strip's status plate)");
        }
        else sb.AppendLine("  SKIP: PerchController or its stand not found");

        // ── 7. mailbox ──
        sb.AppendLine("-- 7. mailbox (near the user, remote modes) --");
        SpatialMailboxPresenter mailbox = null;
        if (reach != null && attention != null)
        {
            var asset = LoadPrefab("SpatialMailbox", sb);
            if (asset != null)
            {
                var stand = Holder(reach, "MailboxStand");
                Local(stand, Vector3.zero, Quaternion.identity, Vector3.one);
                const float k = 0.9f;
                var modelHolder = Holder(stand, "MailboxModel");
                Local(modelHolder, Vector3.zero, Quaternion.Euler(0f, 180f, 0f), Vector3.one * k);
                var inst = Art(modelHolder, asset, "SpatialMailbox", sb);
                if (inst != null)
                {
                    Local(inst.transform, Vector3.zero, Quaternion.identity, Vector3.one);
                    MeshBounds(inst.transform, stand, false, out var mn, out var mx);
                    // Caption in words under the front edge (the stand shows its -Z to the head).
                    const float capW = 0.10f, capH = 0.016f;
                    Vector3 capPos = new Vector3(0f, -capH * 0.5f - 0.003f, mn.z - 0.002f);
                    Shape(stand, "MailboxCaptionPlate", quad, matPlate, capPos, new Vector3(capW, capH, 1f));
                    var caption = Label(stand, "MailboxCaption", capPos + new Vector3(0f, 0f, TextZ), "Nothing waiting",
                                        new Vector2(capW * 100f - 0.6f, capH * 100f - 0.2f), 5.5f, 6.5f, OnPlate, FontStyles.Bold);
                    mailbox = Ensure<SpatialMailboxPresenter>(reach.gameObject);
                    mailbox.attention = attention;
                    mailbox.mailbox = stand;
                    mailbox.mailboxShownScale = Vector3.one;
                    mailbox.model = modelHolder.gameObject;
                    mailbox.art = inst.GetComponent<SpatialMailboxController>();
                    mailbox.caption = caption;
                    mailbox.offset = new Vector3(-0.20f, 0.07f, 0.04f);
                    modelHolder.gameObject.SetActive(false);
                    stand.localScale = Vector3.one * Hidden;
                    EditorUtility.SetDirty(mailbox);
                    sb.AppendLine($"  SpatialMailbox ({S(mx - mn)} at scale {k:F2}) + caption plate {capW} x {capH} m (5.5 to 6.5 mm words) under Spatial/SpatialReach/MailboxStand; SpatialMailboxPresenter shows AttentionController.CompletedCount and PendingDecisionCount (suggestions always 0); offset {P(mailbox.offset)}; saved hidden");
                }
            }
        }
        else sb.AppendLine("  SKIP: no reach side");

        // ── 8. reach of the mode strip and the near-user layout ──
        sb.AppendLine("-- 8. reach --");
        if (strip != null)
        {
            var focus = Find(all, "FocusToggle");
            if (strip.floorKey != null) strip.floorKeyLabel = strip.floorKey.GetComponentInChildren<TMP_Text>(true);
            sb.AppendLine($"  Floor key label {(strip.floorKeyLabel != null ? "wired" : "NOT FOUND")}: it reads \"Send back\" (and SpatialWorkspaceDirector.RequestFloor sends the office back) while a Floor office is parked at the desk");
            if (focus != null && focus.IsChildOf(ws) && strip.slot1.sqrMagnitude > 1e-8f)
            {
                float keyScale = Mathf.Abs(focus.lossyScale.x);
                float housingW = keyScale * 1.2f;
                var housing = Under(focus, "Housing3D");
                var hf = housing != null ? housing.GetComponent<MeshFilter>() : null;
                if (hf != null && hf.sharedMesh != null)
                {
                    Vector3 hmn = Vector3.positiveInfinity, hmx = Vector3.negativeInfinity;
                    var b = hf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var l = focus.InverseTransformPoint(housing.TransformPoint(c));
                        hmn = Vector3.Min(hmn, l); hmx = Vector3.Max(hmx, l);
                    }
                    housingW = Mathf.Max(keyScale, (hmx.x - hmn.x) * keyScale);
                }
                float oldSpacing = strip.slot1.magnitude;
                float spacing = housingW + 0.003f;                         // was + 0.006
                Quaternion localRot = Quaternion.Euler(strip.deskLocalEuler);
                Vector3 focusLocal = ws.InverseTransformPoint(focus.position);
                Vector3 origin = focusLocal + new Vector3(-spacing, 0f, 0f);
                Vector3 Far(Vector3 o) => o + new Vector3(-spacing * 2f, 0f, 0f);
                float before = (Far(origin) - Eye).magnitude;
                int pulls = 0;
                while ((Far(origin) - Eye).magnitude > ReachTarget && pulls < 25 && Far(origin).z < Eye.z) { origin.z += 0.002f; pulls++; }
                Vector3 oldStep = strip.slot1;
                Vector3 step = Quaternion.Inverse(localRot) * new Vector3(-spacing, 0f, 0f);
                strip.deskLocalPosition = origin;
                strip.slot0 = Vector3.zero; strip.slot1 = step; strip.slot2 = step * 2f;
                Vector3 shift = step - oldStep;
                if (strip.statusPlate != null) { strip.statusPlate.localPosition += shift; EditorUtility.SetDirty(strip.statusPlate); }
                if (strip.statusLabel != null) { strip.statusLabel.transform.localPosition += shift; EditorUtility.SetDirty(strip.statusLabel.transform); }
                var slotOf = new (Transform key, Vector3 slot)[]
                {
                    (strip.floorKey, strip.slot0), (strip.ceilingKey, strip.slot1), (strip.deskKey, strip.slot0), (strip.nextKey, strip.slot0),
                    (strip.confirmKey, strip.slot1), (strip.cancelKey, strip.slot2), (strip.podKey, strip.slot1),
                };
                foreach (var (key, slot) in slotOf) if (key != null) { key.localPosition = slot + strip.hiddenOffset; EditorUtility.SetDirty(key); }
                keysT.SetPositionAndRotation(ws.TransformPoint(origin), ws.rotation * localRot);
                float after = (Far(origin) - Eye).magnitude;
                sb.AppendLine($"  mode strip at the desk: spacing {oldSpacing:F4} -> {spacing:F4} m, pulled {pulls * 2} mm toward the user; slots (root-local) {P(origin)}, {P(origin + new Vector3(-spacing, 0f, 0f))}, {P(Far(origin))}; furthest slot {before:F3} -> {after:F3} m from the eye (limit {ReachLimit:F2})");
                if (after > ReachLimit) sb.AppendLine("  WARNING: the furthest desk slot is still beyond the comfortable reach");
            }
            else sb.AppendLine("  NOTE: FocusToggle not found under WorkspaceRoot (or the strip has no slots); desk slots left as they are");

            // The same strip at the near-user pose (office remote or travelling). Pose-local: -X is the
            // user's right, +Z is toward the user; the eye is nearAhead behind and nearBelow above the pose.
            Quaternion rot = Quaternion.Euler(strip.deskLocalEuler);
            Vector3 off = new Vector3(-0.13f, -0.03f, 0.06f);
            float NearDist(Vector3 poseLocal) => new Vector3(poseLocal.x, poseLocal.y - nearBelow, nearAhead - poseLocal.z).magnitude;
            float Worst(Vector3 o) => Mathf.Max(NearDist(o + rot * strip.slot0), Mathf.Max(NearDist(o + rot * strip.slot1), NearDist(o + rot * strip.slot2)));
            int nudges = 0;
            while (Worst(off) > ReachTarget && nudges < 8) { off.z += 0.01f; nudges++; }
            strip.nearUserOffset = off;
            sb.AppendLine($"  mode strip near the user: nearUserOffset = {P(off)}; slots {NearDist(off + rot * strip.slot0):F3} / {NearDist(off + rot * strip.slot1):F3} / {NearDist(off + rot * strip.slot2):F3} m from the eye (nominal seat: near-user pose {nearAhead:F2} m ahead, {nearBelow:F2} m below the eyes)");
            if (Worst(off) > ReachLimit) sb.AppendLine("  WARNING: a near-user slot is still beyond the comfortable reach");
            EditorUtility.SetDirty(strip);
        }
        else sb.AppendLine("  SKIP: SpatialModeStrip not found");

        // Reach-side items: x = to the user's right, y = up, z = away, from the near-user point.
        float ReachOf(Vector3 o) => new Vector3(o.x, o.y - nearBelow, nearAhead + o.z).magnitude;
        var summon = reach != null ? reach.GetComponent<SummonController>() : null;
        if (summon != null)
        {
            summon.keysOffset = new Vector3(-0.17f, -0.02f, -0.04f);
            EditorUtility.SetDirty(summon);
            float d = Mathf.Max(ReachOf(summon.keysOffset + new Vector3(-0.028f, 0f, 0f)), ReachOf(summon.keysOffset + new Vector3(0.028f, 0f, 0f)));
            sb.AppendLine($"  summon keys: keysOffset = {P(summon.keysOffset)}; the further key is {d:F3} m from the eye{(d > ReachLimit ? " WARNING: beyond the comfortable reach" : "")}");
        }
        sb.AppendLine("  near-user layout (right, up, away from the hovering paper): summon keys (-0.17, -0.02, -0.04); mode strip from 0.13 right, 0.03 down, 0.06 nearer; perch plate (0.24, 0.12, 0.05); mirror cue (0.06, 0.15, 0.03); mailbox (-0.20, 0.07, 0.04)");
        if (perch != null) sb.AppendLine($"  perch plate is {ReachOf(perch.standOffset):F3} m from the eye (read, not touched)");
        if (mailbox != null) sb.AppendLine($"  mailbox is {ReachOf(mailbox.offset):F3} m from the eye (read, not touched)");

        sb.AppendLine("-- summary --");
        sb.AppendLine("  nothing was deleted; both feature flags are untouched; every art holder is saved hidden (scale) and its model inactive, so Desk mode shows and animates none of it.");
        sb.AppendLine("  Play Mode (AgentScripts/SpatialDrive.cs): Enable, Floor, Confirm -> platform + station; start a run -> beacon, courier; Bring -> Floor key reads Send back; Ceiling, Confirm -> pod, mount, mast; PodDown / PodUp -> pod clips and shelf.");

        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    /// <summary>
    /// Size a ghost model to a target footprint (root-local) and put it there. The mesh is scaled in
    /// X and Z separately (and in Y for the ceiling ghost, which shows the pod's height); the label's
    /// width is corrected so its letters keep their shape.
    /// </summary>
    static void FitGhost(GameObject inst, GameObject asset, Transform holder, Vector3 targetSize, Vector3 targetCentre,
                         float topY, bool ceiling, StringBuilder sb)
    {
        if (!MeshBounds(inst.transform, inst.transform, false, out var mn, out var mx)) { sb.AppendLine($"  WARNING: {inst.name} has no mesh to measure; left at scale 1"); return; }
        Vector3 size = mx - mn, centre = (mx + mn) * 0.5f;
        float sx = Mathf.Clamp(targetSize.x / Mathf.Max(0.01f, size.x), 0.5f, 3f);
        float sz = Mathf.Clamp(targetSize.z / Mathf.Max(0.01f, size.z), 0.5f, 3f);
        float sy = ceiling ? Mathf.Clamp(targetSize.y / Mathf.Max(0.01f, size.y), 1f, 4f) : 1f;
        // Floor: the model's own floor contact (y = 0) stays on the surface. Ceiling: its top goes to the pod's top.
        float y = ceiling ? topY - mx.y * sy : 0f;
        inst.transform.localPosition = new Vector3(targetCentre.x - centre.x * sx, y, targetCentre.z - centre.z * sz);
        inst.transform.localRotation = Quaternion.identity;
        inst.transform.localScale = new Vector3(sx, sy, sz);
        EditorUtility.SetDirty(inst.transform);
        var badge = inst.transform.Find("Preview_StatusBadgeTMP");
        var badgeSrc = asset.transform.Find("Preview_StatusBadgeTMP");
        if (badge != null && badgeSrc != null)
        {
            Vector3 b = badgeSrc.localScale;
            badge.localScale = new Vector3(b.x / sx, b.y / (ceiling ? sy : 1f), b.z);
            EditorUtility.SetDirty(badge);
        }
        sb.AppendLine($"  {inst.name}: model {S(size)} -> {S(new Vector3(size.x * sx, size.y * sy, size.z * sz))} (scale {sx:F2}, {sy:F2}, {sz:F2}) centred at root-local ({targetCentre.x:F3}, {targetCentre.z:F3}){(ceiling ? $", top {topY:F3} m above the landing root" : ", on the surface")}; label width corrected");
    }
}
