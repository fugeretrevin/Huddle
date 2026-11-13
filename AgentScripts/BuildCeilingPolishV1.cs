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
/// Ceiling polish, pass 1: three defects seen on the headset in Ceiling mode.
///
///   1. Loose physics props trailed the moving office   -> adds ONE WorkspacePropCarrier
///      (Spatial/PropCarrier) wired to WorkspaceRoot and the transition controller.
///   2. Props out of place on the pod                   -> re-fits the pod to the MEASURED office
///      (depth stretch of at most 5 percent, a small shift so the pod's own station sockets sit
///      under the real stations, both kept inside the footprint the placement checks validate),
///      re-reads the mast / mount sockets, and lists the loose props that have no deck under
///      them; those are given to CeilingPodArt.offDeckProps (scaled away while the mode is Ceiling).
///   3. Attention cue tilted and in the middle          -> gives AttentionBeacon the measured
///      office top, front edge, pod underside and cue height, and moves the near-user mirror
///      beside the near-user controls.
///
/// Runs AFTER BuildSpatialArtV1 and BuildSpatialReachV1 and overrides their results; it never
/// edits or re-runs them. Every value is recomputed from measurements and prefab constants, so a
/// second run changes nothing. Nothing is deleted. Saves the scene once. Refuses in Play Mode and
/// in any scene other than MainMRScene.
///   BuildCeilingPolishV1.Apply
/// </summary>
public static class BuildCeilingPolishV1
{
    // ── tuning knobs for the coordinator ──
    const bool FitDepth = true;                 // stretch the pod in depth so its deck reaches the office's front and back
    const float MaxDepthStretch = 1.05f;        // ART_ADOPTION_NOTES: non-uniform scale of a fitted body at most 5 percent
    const bool AlignToStationSockets = true;    // shift the pod so its station sockets sit under the real stations
    const float MaxSocketShift = 0.03f;         // metres
    const bool HideOffDeckProps = true;         // scale away loose props that have no deck under them, Ceiling mode only
    const float DeckTolerance = 0.005f;         // a prop centre this far outside the deck still counts as on it
    const float FitMargin = 0.01f;              // same margin BuildSpatialArtV1 uses
    const float AgentHeight = 0.31f;            // the agents are skinned; their height is taken from the design brief
    static readonly Vector3 MirrorOffset = new Vector3(-0.29f, 0.02f, 0.02f);   // right, up, away from the near-user point
    const float MirrorKeepClear = 0.07f;        // warn when the mirror is closer than this to another near-user piece

    // ───────────────────────────── helpers ─────────────────────────────

    static List<Transform> All() => EditorSceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
    static Transform Find(List<Transform> all, string name) => all.FirstOrDefault(t => t != null && t.name == name);
    static string P(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";
    static string Cm(float m) => (m * 100f).ToString("F1") + " cm";

    static string PathOf(Transform t, Transform stopAt)
    {
        if (t == null) return "MISSING";
        var sb = new StringBuilder(t.name);
        for (Transform p = t.parent; p != null && p != stopAt; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
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

    static T Ensure<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    static readonly Vector3[] Corners =
    {
        new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(-1, 1, -1), new Vector3(1, 1, -1),
        new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(-1, 1, 1), new Vector3(1, 1, 1),
    };

    /// <summary>Bounds of an object's meshes in <paramref name="space"/> (static meshes, and skinned
    /// meshes by their bind-pose bounds, the same way BuildSpatialArtV1 measures; text is skipped).
    /// <paramref name="staticOnly"/> leaves skinned meshes out (the agents).</summary>
    static bool MeasureIn(Transform root, Transform space, bool includeInactive, bool staticOnly, out Vector3 mn, out Vector3 mx)
    {
        mn = Vector3.positiveInfinity; mx = Vector3.negativeInfinity;
        bool any = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>(includeInactive))
        {
            if (r == null || r.GetComponent<TMP_Text>() != null) continue;
            if (!includeInactive && !r.enabled) continue;
            if (r.transform.lossyScale.sqrMagnitude < 1e-4f) continue;          // hidden by scale
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer smr) { if (!staticOnly) mesh = smr.sharedMesh; }
            else { var f = r.GetComponent<MeshFilter>(); if (f != null) mesh = f.sharedMesh; }
            if (mesh == null) continue;
            var b = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var l = space.InverseTransformPoint(r.transform.TransformPoint(b.center + Vector3.Scale(b.extents, Corners[i])));
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
            }
            any = true;
        }
        return any;
    }

    struct Rect2
    {
        public float x0, x1, z0, z1;
        public Rect2(float x0, float x1, float z0, float z1) { this.x0 = x0; this.x1 = x1; this.z0 = z0; this.z1 = z1; }
        public float W => x1 - x0;
        public float D => z1 - z0;
        public float Cx => (x0 + x1) * 0.5f;
        public float Cz => (z0 + z1) * 0.5f;
        public Rect2 Union(Rect2 o) => new Rect2(Mathf.Min(x0, o.x0), Mathf.Max(x1, o.x1), Mathf.Min(z0, o.z0), Mathf.Max(z1, o.z1));
        public bool Contains(float x, float z, float tol) => x >= x0 - tol && x <= x1 + tol && z >= z0 - tol && z <= z1 + tol;
        /// <summary>How far a point is outside the rectangle (0 inside).</summary>
        public float Outside(float x, float z) => Mathf.Max(Mathf.Max(x0 - x, x - x1), Mathf.Max(z0 - z, z - z1), 0f);
        public override string ToString() => $"x {x0:F3}..{x1:F3}, z {z0:F3}..{z1:F3} ({W:F3} x {D:F3} m)";
    }

    // ───────────────────────────── entry ─────────────────────────────

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BuildCeilingPolishV1: stop Play Mode first.\n";
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return "BuildCeilingPolishV1: no open scene.\n";
        if (!scene.path.Replace('\\', '/').EndsWith("Assets/MuseDesk/Scenes/MainMRScene.unity"))
            return "BuildCeilingPolishV1: the open scene is '" + scene.path + "', not MainMRScene; nothing changed.\n";

        var sb = new StringBuilder();
        var all = All();
        var ws = Find(all, "WorkspaceRoot");
        if (ws == null) return "BuildCeilingPolishV1: WorkspaceRoot not found; nothing changed.\n";
        Transform spatial = all.FirstOrDefault(t => t != null && t.name == "Spatial" && !t.IsChildOf(ws));
        if (spatial == null) return "BuildCeilingPolishV1: scene object Spatial not found (run the spatial builders first); nothing changed.\n";

        var director = spatial.GetComponent<SpatialWorkspaceDirector>();
        var transition = spatial.GetComponentInChildren<WorkspaceTransitionController>(true);
        var podT = spatial.Find("CeilingPod");
        var pod = podT != null ? podT.GetComponent<CeilingPodController>() : null;
        var podArt = podT != null ? podT.GetComponent<CeilingPodArt>() : null;
        var reach = spatial.Find("SpatialReach");
        var beacon = reach != null ? reach.GetComponentInChildren<AttentionBeacon>(true) : null;
        var kit = Object.FindAnyObjectByType<NearUserKit>(FindObjectsInactive.Include);

        sb.AppendLine("== BuildCeilingPolishV1 ==");
        if (Mathf.Abs(ws.lossyScale.x - 1f) > 1e-3f) sb.AppendLine($"  WARNING: WorkspaceRoot world scale is {ws.lossyScale.x:F3}; everything here is measured for scale 1.");

        bool InKit(Transform t)
        {
            if (kit == null || kit.items == null || t == null) return false;
            foreach (var item in kit.items) if (item != null && (t == item || t.IsChildOf(item))) return true;
            return false;
        }

        // ── 1. prop carrier ──
        sb.AppendLine("-- 1. prop carrier (Spatial/PropCarrier) --");
        {
            var host = Holder(spatial, "PropCarrier");
            host.localPosition = Vector3.zero; host.localRotation = Quaternion.identity; host.localScale = Vector3.one;
            var carrier = Ensure<WorkspacePropCarrier>(host.gameObject);
            carrier.workspaceRoot = ws;
            carrier.transition = transition;
            EditorUtility.SetDirty(carrier);
            int count = Object.FindObjectsByType<WorkspacePropCarrier>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            sb.AppendLine($"  WorkspacePropCarrier on Spatial/PropCarrier: workspaceRoot = WorkspaceRoot, transition {(transition != null ? "set" : "MISSING (it still follows IsTransitioning and the root's own movement)")}; settle {carrier.settleFrames} frames and {carrier.settleSeconds:F2} s; tidyBeforeMove={carrier.tidyBeforeMove}");
            if (count > 1) sb.AppendLine($"  WARNING: {count} WorkspacePropCarrier components in the scene; remove the extras by hand (only one may run).");

            sb.AppendLine("  rigidbodies under WorkspaceRoot (as saved in the scene; the carrier collects them again at every move):");
            foreach (var rb in ws.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb == null) continue;
                bool pushable = rb.GetComponent<PushableProp>() != null, delayed = rb.GetComponent<DelayedPhysics>() != null;
                string role = (!rb.isKinematic || pushable || delayed) ? "LOOSE: pinned during a move" : (rb.interpolation != RigidbodyInterpolation.None ? "kinematic: interpolation off during a move" : "kinematic: untouched");
                sb.AppendLine($"    {PathOf(rb.transform, ws)}: active={rb.gameObject.activeInHierarchy} kinematic={rb.isKinematic} gravity={rb.useGravity} interpolation={rb.interpolation}{(pushable ? " PushableProp (sets Interpolate at Awake)" : "")}{(delayed ? " DelayedPhysics" : "")}{(InKit(rb.transform) ? " [NearUserKit item: stays at the desk in remote modes, not carried]" : "")} -> {role}");
            }
        }

        // ── 2. the office, measured ──
        sb.AppendLine("-- 2. office (root-local, measured from static meshes) --");
        Rect2 office = new Rect2(-0.45f, 0.45f, -0.14f, 0.36f);
        float officeTop = AgentHeight;
        {
            bool measured = false; Rect2 acc = new Rect2(); float top = 0f;
            foreach (var n in new[] { "ResearchStation", "DevStation", "QAStation", "SoftwareFactory", "InboxTray", "OutboxTray" })
            {
                var t = Find(all, n);
                if (t == null || !t.IsChildOf(ws) || !t.gameObject.activeInHierarchy) { sb.AppendLine($"  not found (or inactive) under WorkspaceRoot: {n}"); continue; }
                if (!MeasureIn(t, ws, false, true, out var mn, out var mx)) { sb.AppendLine($"  no static mesh measured: {n}"); continue; }
                var r = new Rect2(mn.x, mx.x, mn.z, mx.z);
                sb.AppendLine($"  {n}: {r}, top {mx.y:F3} m (origin {P(ws.InverseTransformPoint(t.position))})");
                acc = measured ? acc.Union(r) : r; measured = true;
                top = Mathf.Max(top, mx.y);
            }
            if (measured && acc.W >= 0.6f && acc.W <= 1.15f && acc.D >= 0.3f && acc.D <= 0.8f) office = acc;
            else sb.AppendLine($"  WARNING: measured footprint {(measured ? acc.ToString() : "(nothing)")} is implausible; using the assumed {office}");
            if (top > 0.05f && top < 0.8f) officeTop = Mathf.Max(officeTop, top);
            sb.AppendLine($"  office proper (stations, factory, trays): {office}; tallest fixed thing {officeTop:F3} m (agents taken as {AgentHeight:F2} m)");
        }

        // ── 3. the pod, fitted to the office ──
        sb.AppendLine("-- 3. ceiling pod fit (WorkspaceRoot/SpatialArt_CeilingPod) --");
        bool havePod = false;
        Rect2 deckRect = new Rect2();
        float podUnderside = -0.06f, podFront = office.z1;
        var podHolder = ws.Find("SpatialArt_CeilingPod");
        var podInst = podHolder != null ? podHolder.Find("SuspendedOfficePod") : null;
        if (podHolder == null || podInst == null)
            sb.AppendLine("  SKIP: SpatialArt_CeilingPod/SuspendedOfficePod not found (run BuildSpatialArtV1 first)");
        else
        {
            bool holderWasActive = podHolder.gameObject.activeSelf; Vector3 holderScale = podHolder.localScale;
            bool instWasActive = podInst.gameObject.activeSelf;
            podHolder.gameObject.SetActive(true); podHolder.localScale = Vector3.one; podInst.gameObject.SetActive(true);

            var bc = podInst.GetComponent<BoxCollider>();
            Vector2 deck = bc != null ? new Vector2(bc.size.x, bc.size.z) : new Vector2(0.58f, 0.34f);
            Vector2 deckCentre = bc != null ? new Vector2(bc.center.x, bc.center.z) : Vector2.zero;
            float s = Mathf.Abs(podInst.localScale.x);          // the uniform fit BuildSpatialArtV1 chose (x is never changed here)
            if (s < 0.5f || s > 2.5f) { sb.AppendLine($"  WARNING: pod scale x = {s:F3} is implausible; treated as 1"); s = 1f; }
            float oldSz = Mathf.Abs(podInst.localScale.z); Vector3 oldHolderPos = podHolder.localPosition;

            // Pod outline in its own space (independent of the instance's scale).
            Vector3 bmn, bmx;
            if (!MeasureIn(podInst, podInst, true, false, out bmn, out bmx) || (bmx - bmn).x < 0.2f || (bmx - bmn).x > 2f)
            { bmn = new Vector3(-0.3535f, -0.05f, -0.194f); bmx = new Vector3(0.3535f, 0.28f, 0.194f); sb.AppendLine("  NOTE: pod mesh bounds not measurable; using the inventory size 0.707 x 0.332 x 0.388 m"); }

            // The footprint the placement and sweep checks validate (never changed here).
            Vector2 fp = director != null ? director.footprint : new Vector2(1.0f, 0.7f);
            Vector2 fpc = director != null ? director.ceilingSettings.footprintCenterOffset : new Vector2(0f, 0.12f);
            Rect2 valid = new Rect2(fpc.x - fp.x * 0.5f, fpc.x + fp.x * 0.5f, fpc.y - fp.y * 0.5f, fpc.y + fp.y * 0.5f);

            // Depth: stretch (at most 5 percent) until the deck reaches the office's front and back.
            float needD = office.D + 2f * FitMargin;
            float sz = s;
            if (FitDepth && deck.y * s < needD) sz = Mathf.Min(needD / deck.y, s * MaxDepthStretch);

            // Shift: the pod's own station sockets under the real stations (least squares in z),
            // but never so far that the deck stops covering the office's front or back.
            float shift = 0f; string shiftWhy = "centred on the measured office";
            if (AlignToStationSockets)
            {
                var pairs = new (string station, string socket)[] { ("ResearchStation", "ResearchStationSocket"), ("DevStation", "DeveloperStationSocket"), ("QAStation", "TesterStationSocket") };
                float sum = 0f; int n = 0;
                foreach (var (station, socket) in pairs)
                {
                    var st = Find(all, station); var so = podInst.Find(socket);
                    if (st == null || so == null || !st.IsChildOf(ws)) continue;
                    float want = ws.InverseTransformPoint(st.position).z;
                    float have = office.Cz + so.localPosition.z * sz;
                    sum += want - have; n++;
                }
                if (n > 0)
                {
                    float raw = sum / n;
                    shift = Mathf.Clamp(raw, -MaxSocketShift, MaxSocketShift);
                    float half = deck.y * sz * 0.5f, baseC = office.Cz + deckCentre.y * sz;
                    float lo = office.z1 - half - baseC, hi = office.z0 + half - baseC;     // deck still covers front / back
                    if (lo <= hi) shift = Mathf.Clamp(shift, lo, hi); else shift = 0f;
                    shiftWhy = $"station sockets ask for {raw * 100f:F1} cm ({n} stations){(Mathf.Abs(shift - raw) > 1e-4f ? ", limited so the deck keeps covering the office and the shift stays within " + Cm(MaxSocketShift) : "")}";
                }
            }

            // Stay inside the validated footprint in depth (width is BuildSpatialArtV1's fit, untouched).
            Vector3 holderPos = new Vector3(office.Cx, 0f, office.Cz + shift);
            float outZ0 = holderPos.z + bmn.z * sz, outZ1 = holderPos.z + bmx.z * sz;
            if (outZ0 < valid.z0 - 5e-4f || outZ1 > valid.z1 + 5e-4f)
            {
                sb.AppendLine($"  the fitted pod would span z {outZ0:F3}..{outZ1:F3}, outside the validated footprint z {valid.z0:F3}..{valid.z1:F3}: depth stretch and shift NOT applied");
                sz = s; shift = 0f; shiftWhy = "centred on the measured office (fit refused)";
                holderPos = new Vector3(office.Cx, 0f, office.Cz);
                outZ0 = holderPos.z + bmn.z * sz; outZ1 = holderPos.z + bmx.z * sz;
            }
            float outX0 = holderPos.x + bmn.x * s, outX1 = holderPos.x + bmx.x * s;
            if (outX0 < valid.x0 - 2e-3f || outX1 > valid.x1 + 2e-3f)
                sb.AppendLine($"  NOTE: the pod (BuildSpatialArtV1's width fit) spans x {outX0:F3}..{outX1:F3}; the validated footprint is x {valid.x0:F3}..{valid.x1:F3}. Not changed here.");

            podHolder.localPosition = holderPos; podHolder.localRotation = Quaternion.identity;
            podInst.localScale = new Vector3(s, s, sz);
            EditorUtility.SetDirty(podHolder); EditorUtility.SetDirty(podInst);
            PrefabUtility.RecordPrefabInstancePropertyModifications(podInst);

            deckRect = new Rect2(holderPos.x + (deckCentre.x - deck.x * 0.5f) * s, holderPos.x + (deckCentre.x + deck.x * 0.5f) * s,
                                 holderPos.z + (deckCentre.y - deck.y * 0.5f) * sz, holderPos.z + (deckCentre.y + deck.y * 0.5f) * sz);
            havePod = true;
            podUnderside = podInst.localPosition.y + bmn.y * s;
            podFront = outZ1;

            sb.AppendLine($"  pod scale ({s:F3}, {s:F3}, {sz:F3}) (depth was {oldSz:F3}; stretch {(sz / s - 1f) * 100f:F1} percent, limit {(MaxDepthStretch - 1f) * 100f:F0}); holder {P(oldHolderPos)} -> {P(holderPos)}: {shiftWhy}");
            sb.AppendLine($"  deck {deckRect}; office {office}");
            sb.AppendLine($"  office beyond the deck: user's right (-x) {Cm(Mathf.Max(0f, deckRect.x0 - office.x0))}, left (+x) {Cm(Mathf.Max(0f, office.x1 - deckRect.x1))}, front (+z) {Cm(Mathf.Max(0f, office.z1 - deckRect.z1))}, back (-z) {Cm(Mathf.Max(0f, deckRect.z0 - office.z0))}");
            float widthShort = office.W - deckRect.W;
            if (widthShort > 0.005f)
                sb.AppendLine($"  NOT FIXABLE HERE: the deck is {Cm(widthShort)} narrower than the office. The pod's outline is already {(outX1 - outX0):F3} m wide and the validated footprint is {fp.x:F2} m; a wider pod would leave the volume the movement checks sweep. The outer stations stand partly over the wings.");
            sb.AppendLine($"  pod outline (root-local): x {outX0:F3}..{outX1:F3}, z {outZ0:F3}..{outZ1:F3}, underside y {podUnderside:F3}; validated footprint {valid}");

            // Sockets follow: the mast's lower end is the connector itself; the mount sits above it.
            var connector = podInst.Find("DockingConnector");
            if (connector != null)
            {
                Vector3 conn = ws.InverseTransformPoint(connector.position);
                if (pod != null)
                {
                    Vector2 was = pod.mountOffset;
                    pod.mountOffset = new Vector2(conn.x, conn.z);
                    pod.mastAttachHeight = conn.y;
                    if (pod.mastBottom == null) pod.mastBottom = connector;
                    EditorUtility.SetDirty(pod);
                    sb.AppendLine($"  CeilingPodController.mountOffset ({was.x:F3}, {was.y:F3}) -> ({conn.x:F3}, {conn.z:F3}), mastAttachHeight = {conn.y:F3}: the mount stays straight above the connector");
                }
                foreach (var n in new[] { "ResearchStation", "DevStation", "QAStation" })
                {
                    var st = Find(all, n);
                    if (st == null) continue;
                    float gap = ws.InverseTransformPoint(st.position).z - conn.z;
                    sb.AppendLine($"  rear arch / connector at z {conn.z:F3}: {n}'s agent stands {Cm(gap)} in front of it{(gap < 0.04f ? "  WARNING: the arch is at or in front of the agent; look at a capture" : "")}");
                }
            }
            else sb.AppendLine("  WARNING: DockingConnector not found in the pod; mount offset left as it was");

            foreach (var n in new[] { "DockingConnector", "InspectionShelf", "AttentionBeaconSocket" })
            {
                var so = podInst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
                if (so == null) continue;
                Vector3 ls = so.lossyScale;
                bool unit = Mathf.Abs(ls.x / s - 1f) < 0.02f && Mathf.Abs(ls.y / s - 1f) < 0.02f;
                sb.AppendLine($"  socket {n}: root-local {P(ws.InverseTransformPoint(so.position))}, lossy scale {P(ls)}{(unit || so.parent != podInst ? "" : "  WARNING: not the pod's scale")} (nothing is parented to it)");
            }
            sb.AppendLine("  NOTE: the ceiling placement ghost (Spatial/PlacementGhost/CeilingArt) was sized by BuildSpatialArtV1 for the uniform pod; it is not re-fitted here (at most " + Cm((sz - s) * (bmx.z - bmn.z)) + " shallower than the pod).");

            podInst.gameObject.SetActive(instWasActive);
            podHolder.localScale = holderScale; podHolder.gameObject.SetActive(holderWasActive);
            EditorUtility.SetDirty(podHolder.gameObject);
        }

        // ── 4. what stands where: every office prop against the pod's deck ──
        sb.AppendLine("-- 4. office props against the pod deck (root-local) --");
        var offDeck = new List<Transform>();
        {
            // Direct children of the office and of its grouping objects.
            var groups = new List<Transform> { ws };
            foreach (var n in new[] { "Phase6", "OfficeProps_Collection", "OfficeV2", "V2_DepthFixed", "V2_InboxScaled" })
            { var g = Find(all, n); if (g != null && g.IsChildOf(ws)) groups.Add(g); }
            var seen = new HashSet<Transform>();
            foreach (var g in groups)
                foreach (Transform c in g)
                {
                    if (c == null || groups.Contains(c) || !seen.Add(c)) continue;
                    if (c.name.StartsWith("SpatialArt_")) continue;
                    if (!c.gameObject.activeInHierarchy) continue;
                    if (c.lossyScale.sqrMagnitude < 1e-4f) { sb.AppendLine($"    {PathOf(c, ws)}: hidden by scale (shown only when needed)"); continue; }
                    bool loose = c.GetComponent<Rigidbody>() != null && (c.GetComponent<PushableProp>() != null || !c.GetComponent<Rigidbody>().isKinematic);
                    bool inKit = InKit(c);
                    Vector3 origin = ws.InverseTransformPoint(c.position);
                    string where;
                    if (!MeasureIn(c, ws, false, false, out var mn, out var mx)) where = "no mesh";
                    else
                    {
                        Vector3 centre = (mn + mx) * 0.5f;
                        where = $"centre ({centre.x:F3}, {centre.z:F3}), size {(mx.x - mn.x):F3} x {(mx.y - mn.y):F3} x {(mx.z - mn.z):F3} m, base y {mn.y:F3}";
                        if (inKit) where += "  [NearUserKit item: stays at the desk in Floor / Ceiling]";
                        else if (havePod)
                        {
                            bool on = deckRect.Contains(centre.x, centre.z, DeckTolerance);
                            float edge = Mathf.Max(Mathf.Max(deckRect.x0 - mn.x, mx.x - deckRect.x1), Mathf.Max(deckRect.z0 - mn.z, mx.z - deckRect.z1));
                            if (on && edge <= 0.005f) where += "  [on the deck]";
                            else if (on) where += $"  [on the deck, overhangs its edge by {Cm(edge)}]";
                            else
                            {
                                where += $"  [OFF THE DECK: centre {Cm(deckRect.Outside(centre.x, centre.z))} outside]";
                                if (loose) { offDeck.Add(c); where += HideOffDeckProps ? " -> scaled away in Ceiling mode" : " -> listed only (HideOffDeckProps is off)"; }
                                else where += "  SUSPECT: fixed prop with no deck under it in Ceiling mode; not changed";
                            }
                        }
                    }
                    sb.AppendLine($"    {PathOf(c, ws)}{(loose ? " (loose)" : "")}: origin {P(origin)}; {where}");
                }
            if (podArt != null)
            {
                podArt.offDeckProps = HideOffDeckProps ? offDeck.ToArray() : new Transform[0];
                EditorUtility.SetDirty(podArt);
                sb.AppendLine($"  CeilingPodArt.offDeckProps = {podArt.offDeckProps.Length} loose prop(s) with no deck under them: {(offDeck.Count > 0 ? string.Join(", ", offDeck.Select(t => t.name)) : "none")}. They are scaled away while the mode is Ceiling and put back when it is not; nothing else about them changes.");
            }
            else sb.AppendLine("  WARNING: CeilingPodArt not found on Spatial/CeilingPod; off-deck props are not hidden");

            // Things that are NOT under the office at all and take their place from it at run time.
            sb.AppendLine("  scene-root objects that follow the office by script, not by parenting (for reference):");
            foreach (var n in new[] { "HandoffDest", "DecisionDocument" })
            {
                var t = Find(all, n);
                if (t != null && !t.IsChildOf(ws)) sb.AppendLine($"    {n}: a scene root at world {P(t.position)} (AgentHandoffController / the paper place it at run time; a move waits for agents to stop first)");
            }
            if (kit != null && kit.items != null)
                sb.AppendLine("  NearUserKit items (stay at the desk pose in Floor / Ceiling by design, so they are NOT on the pod): " + string.Join(", ", kit.items.Where(t => t != null).Select(t => t.name)));
        }

        // ── 5. attention cue ──
        sb.AppendLine("-- 5. attention cue --");
        if (beacon != null)
        {
            // Tallest thing the cue must clear when it stands above the office: fixed things and loose props.
            float top = officeTop;
            foreach (var pp in ws.GetComponentsInChildren<PushableProp>(false))
                if (pp != null && !InKit(pp.transform) && MeasureIn(pp.transform, ws, false, false, out _, out var pmx) && pmx.y < 0.8f) top = Mathf.Max(top, pmx.y);
            beacon.anchorToOffice = true;
            beacon.officeTopHeight = top;
            beacon.officeFrontLocal = new Vector2(office.Cx, office.z1);
            if (havePod) { beacon.podUndersideY = podUnderside; beacon.podFrontZ = podFront; }

            // Height of the whole cue (plate and model) above its origin, at sizer scale 1.
            if (beacon.officeCue != null && beacon.officeSizer != null)
            {
                Vector3 cueScale = beacon.officeCue.localScale, sizerScale = beacon.officeSizer.localScale;
                beacon.officeCue.localScale = Vector3.one; beacon.officeSizer.localScale = Vector3.one;
                if (MeasureIn(beacon.officeSizer, beacon.officeCue, true, false, out var cmn, out var cmx) && cmx.y > 0.02f && cmx.y < 0.6f)
                {
                    beacon.cueTopAtUnitScale = cmx.y;
                    sb.AppendLine($"  cue at sizer scale 1: {cmn.y:F3}..{cmx.y:F3} m about its origin (plate and model, bind pose) -> cueTopAtUnitScale = {cmx.y:F3}");
                }
                else sb.AppendLine($"  NOTE: cue height not measurable; cueTopAtUnitScale stays {beacon.cueTopAtUnitScale:F3}");
                beacon.officeSizer.localScale = sizerScale; beacon.officeCue.localScale = cueScale;
                if (beacon.officeCue.IsChildOf(ws)) sb.AppendLine("  WARNING: the office cue is under WorkspaceRoot; it must not be (it is placed in world space every frame)");
            }
            beacon.mirrorOffset = MirrorOffset;
            EditorUtility.SetDirty(beacon);

            sb.AppendLine($"  Floor / lowered pod: plate centre at root-local (station x or {office.Cx:F3}, {top + beacon.aboveClearance:F3}, {office.z1:F3}) = {beacon.aboveClearance:F2} m over the tallest thing ({top:F3} m), above the front edge");
            sb.AppendLine($"  Ceiling, pod above the eyes: hangs under the pod's front edge at root-local ({office.Cx:F3}, {beacon.podUndersideY:F3}, {beacon.podFrontZ - beacon.hangInset:F3}), its top {Cm(beacon.hangGap)} below the underside (at sizer scale 1.5 the plate centre is {(beacon.hangGap + beacon.cueTopAtUnitScale * 1.5f):F3} m below the underside)");
            sb.AppendLine($"  it switches to hanging when the office root is {Cm(beacon.hangWhenAboveEyes)} above the eyes and back when {Cm(beacon.standWhenBelowEyes)} below; a switch while shown is a pop out and a pop in. Desk mode: above the station as before (showInDeskMode={beacon.showInDeskMode}).");
            sb.AppendLine("  both cues are upright in code (AttentionBeacon.UprightFacing: a turn about the vertical only) and are placed in LateUpdate at execution order 9000, after the office's mover.");

            // Mirror against the other near-user pieces (all offsets as right, up, away from the near-user point).
            var others = new List<(string name, Vector3 off)>();
            var summon = reach != null ? reach.GetComponent<SummonController>() : null;
            if (summon != null) others.Add(("summon keys", summon.keysOffset));
            var perch = reach != null ? reach.GetComponent<PerchController>() : null;
            if (perch != null) others.Add(("perch plate", perch.standOffset));
            var mailbox = reach != null ? reach.GetComponent<SpatialMailboxPresenter>() : null;
            if (mailbox != null) others.Add(("mailbox", mailbox.offset));
            var strip = spatial.GetComponentInChildren<SpatialModeStrip>(true);
            if (strip != null)
            {
                // The strip's offset is in the near-user pose's space: -X is the user's right, +Z is toward the user.
                Quaternion rot = Quaternion.Euler(strip.deskLocalEuler);
                foreach (var (slot, label) in new[] { (strip.slot0, "mode strip slot 0"), (strip.slot1, "mode strip slot 1"), (strip.slot2, "mode strip slot 2") })
                {
                    Vector3 p = strip.nearUserOffset + rot * slot;
                    others.Add((label, new Vector3(-p.x, p.y, -p.z)));
                }
            }
            others.Add(("hovering paper", Vector3.zero));
            sb.AppendLine($"  mirror cue: mirrorOffset = {P(MirrorOffset)} (to the user's LEFT of the near-user point, at its height): beside the controls, not above them. Distances to the other near-user pieces (centre to centre):");
            foreach (var (name, off) in others)
            {
                float d = (off - MirrorOffset).magnitude;
                sb.AppendLine($"    {name} at {P(off)}: {Cm(d)}{(d < MirrorKeepClear ? "  WARNING: closer than " + Cm(MirrorKeepClear) + "; move MirrorOffset in BuildCeilingPolishV1" : "")}");
            }
            if (kit != null) sb.AppendLine("  NOTE: the NearUserKit items sit at the office's DESK pose, not at the near-user point; CeilingPolishDrive.Beacon prints the mirror's real distance to each of them in Play Mode.");
        }
        else sb.AppendLine("  SKIP: AttentionBeacon not found (run BuildSpatialReachV1 first)");

        sb.AppendLine("-- summary --");
        sb.AppendLine("  nothing deleted; flags untouched; the pod holder is saved hidden as before. Play Mode checks: AgentScripts/CeilingPolishDrive.cs (SnapDesk, then Ceiling + Confirm, SnapDocked, PodDown, SnapLowered, PropTable; ProbeStart before a move and ProbeReport after it; ForceNeedsYou + Beacon).");

        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("scene saved=" + EditorSceneManager.SaveScene(scene));
        return sb.ToString();
    }
}
