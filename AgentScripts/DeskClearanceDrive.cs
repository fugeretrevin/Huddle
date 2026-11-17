using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using MuseDesk.Agents;

/// <summary>
/// Desk walk clearance (Polish S9c, audit C6; extended for the desk choreography layout): checks that every authored desk
/// walk keeps the agent's footprint clear of the desks, the props, the factory line, the near-user panels, the other
/// agents and the user zone. It reads the REAL scene (meshes, the agents' waypoints, the registry's delivery anchor, the
/// handoff controller's lanes) and tests every sample (every centimetre, plus every corner) with the pure helper
/// WalkClearance. It moves nothing and changes no state. Works in Edit Mode and in Play Mode.
///
///   scripts\drv.ps1 DeskClearanceDrive Report -Max 6000     one line per route: min clearance, the obstacle, where
///   scripts\drv.ps1 DeskClearanceDrive ReportAt 0.05        the same with another footprint radius
///   scripts\drv.ps1 DeskClearanceDrive Why                  the nearest triangle at each failing route's worst point
///   scripts\drv.ps1 DeskClearanceDrive Obstacles            what was treated as an obstacle (names and root-local bounds)
///   scripts\drv.ps1 DeskClearanceDrive Map | MapOf developer   a plan-view picture of the obstacles, 1 cm per character
///   scripts\drv.ps1 DeskClearanceDrive TraceFor 90 ... TraceReport   Play Mode: record where the agents really walk, then its clearance
///   scripts\drv.ps1 DeskClearanceDrive Try "[agent;]r;x,z;x,z;..."   a proposed lane against the live scene
///
/// Frame: WorkspaceRoot-local (x to the user's left, +z toward the seated user), metres at scale 1.
/// Footprint: the agent root CapsuleCollider radius times its scale (0.08 m in the scene), else WalkClearance.DefaultAgentRadius.
/// Obstacle: the plan-view TRIANGLES of every visible mesh under WorkspaceRoot (static and skinned; not text, not part of an
/// agent, not hidden by scale) and of the near-user panels where they stand, that reach into the agent's body band
/// (feet + 15 mm .. the top of its collider, 0.25 m). Surfaces the agent stands on are below the band and ignored.
/// Other agents count as obstacles at their HOME slot (a square of the footprint radius), except the walker itself.
/// User zone: a square 0.30 m across centred on the seated head (root-local 0, 0.62).
/// Routes are built by AgentMovement.BuildRoute, the same code the walk uses.
/// </summary>
public static class DeskClearanceDrive
{
    const float BandLow = 0.015f;
    /// <summary>The clearance every route must keep (metres, at the collider radius).</summary>
    const float MinClearance = 0.015f;
    const float UserZoneX = 0f, UserZoneZ = 0.62f, UserZoneHalf = 0.15f;

    struct Route { public string name; public Transform walker; public Vector3[] pts; public int n; }

    static bool Ready() { return Root() != null; }
    static Transform Root()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == "WorkspaceRoot") return t;
        return null;
    }

    static float RadiusOf(AgentMovement m, Transform root)
    {
        var cap = m.GetComponent<CapsuleCollider>();
        float scale = Mathf.Max(Mathf.Abs(m.transform.lossyScale.x), Mathf.Abs(m.transform.lossyScale.z));
        float world = cap != null ? cap.radius * scale : WalkClearance.DefaultAgentRadius;
        return world / Mathf.Max(1e-4f, Mathf.Abs(root.lossyScale.x));
    }

    static bool HiddenByScale(Transform t, Transform root)
    {
        for (var a = t; a != null && a != root; a = a.parent)
        {
            var s = a.localScale;
            if (Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))) < 0.005f) return true;
            if (!a.gameObject.activeSelf) return true;
        }
        return false;
    }

    static string Path(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var a = t; a != null && a != root; a = a.parent) parts.Add(a.name);
        parts.Reverse();
        return string.Join("/", parts.Skip(Mathf.Max(0, parts.Count - 3)));
    }

    static float BandHighOf(AgentMovement m)
    {
        var cap = m.GetComponent<CapsuleCollider>();
        float scale = Mathf.Abs(m.transform.lossyScale.y);
        return cap != null ? (cap.center.y + cap.height * 0.5f) * scale : 0.25f;
    }

    static void AddRect(List<ClearMesh> list, string name, float x0, float z0, float x1, float z1)
    {
        var m = new ClearMesh(name, 2);
        m.Add(x0, z0, x1, z0, x1, z1); m.Add(x0, z0, x1, z1, x0, z1);
        list.Add(m);
    }

    // Adds the plan-view triangles (WorkspaceRoot-local) of one mesh that reach into the body band.
    static void AddTriangles(List<ClearMesh> list, string name, Transform source, List<Vector3> verts, Mesh mesh, Matrix4x4 l2w, Transform root, float lo, float hi, ref int rendererCount, ref int triCount)
    {
        var idx = new List<int>();
        var obs = new ClearMesh(name, 64);
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
            mesh.GetIndices(idx, sub);
            for (int i = 0; i + 2 < idx.Count; i += 3)
            {
                Vector3 a = l2w.MultiplyPoint3x4(verts[idx[i]]), b = l2w.MultiplyPoint3x4(verts[idx[i + 1]]), c = l2w.MultiplyPoint3x4(verts[idx[i + 2]]);
                float ymin = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), ymax = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                if (ymax < lo || ymin > hi) continue;                           // below the feet or above the head
                Vector3 la = root.InverseTransformPoint(a), lb = root.InverseTransformPoint(b), lc = root.InverseTransformPoint(c);
                obs.Add(la.x, la.z, lb.x, lb.z, lc.x, lc.z);
            }
        }
        if (obs.triCount > 0) { list.Add(obs); Source[obs] = source; triCount += obs.triCount; rendererCount++; }
    }

    // Every visible mesh under one scope: static meshes (MeshFilter) and skinned ones that are not part of an agent
    // (the factory line is skinned; the S9c version of this driver did not see it).
    static void AddMeshes(List<ClearMesh> list, Transform scope, string prefix, Transform root, float lo, float hi, ref int rendererCount, ref int triCount)
    {
        var verts = new List<Vector3>();
        foreach (var f in scope.GetComponentsInChildren<MeshFilter>(false))
        {
            if (f == null || f.sharedMesh == null) continue;
            var r = f.GetComponent<MeshRenderer>();
            if (r == null || !r.enabled || !f.gameObject.activeInHierarchy) continue;
            if (f.GetComponent<TMP_Text>() != null || f.GetComponent<TMP_SubMesh>() != null) continue;   // text is not furniture
            if (f.GetComponentInParent<AgentMovement>() != null) continue;          // agents (and what they carry) are not furniture
            if (HiddenByScale(f.transform, scope)) continue;
            var wb = r.bounds;
            if (wb.size.magnitude < 0.004f || wb.max.y < lo || wb.min.y > hi) continue;
            f.sharedMesh.GetVertices(verts);
            AddTriangles(list, prefix + Path(f.transform, scope), f.transform, verts, f.sharedMesh, f.transform.localToWorldMatrix, root, lo, hi, ref rendererCount, ref triCount);
        }
        foreach (var s in scope.GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (s == null || s.sharedMesh == null || !s.enabled || !s.gameObject.activeInHierarchy) continue;
            if (s.GetComponentInParent<AgentMovement>() != null) continue;
            if (HiddenByScale(s.transform, scope)) continue;
            var wb = s.bounds;
            if (wb.size.magnitude < 0.004f || wb.max.y < lo || wb.min.y > hi) continue;
            // The pose it is drawn in, skinned here on the CPU (exact for any rig and any scale): each vertex is the
            // weighted sum of bone.localToWorld * bindpose * vertex. A renderer with no bones is placed by its transform.
            var mesh = s.sharedMesh;
            mesh.GetVertices(verts);
            var bones = s.bones; var binds = mesh.bindposes; var weights = mesh.boneWeights;
            if (bones != null && bones.Length > 0 && binds.Length == bones.Length && weights.Length == verts.Count)
            {
                var mats = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++) mats[i] = bones[i] != null ? bones[i].localToWorldMatrix * binds[i] : s.transform.localToWorldMatrix;
                for (int i = 0; i < verts.Count; i++)
                {
                    var bw = weights[i]; Vector3 v = verts[i];
                    Vector3 p = mats[bw.boneIndex0].MultiplyPoint3x4(v) * bw.weight0;
                    if (bw.weight1 > 0f) p += mats[bw.boneIndex1].MultiplyPoint3x4(v) * bw.weight1;
                    if (bw.weight2 > 0f) p += mats[bw.boneIndex2].MultiplyPoint3x4(v) * bw.weight2;
                    if (bw.weight3 > 0f) p += mats[bw.boneIndex3].MultiplyPoint3x4(v) * bw.weight3;
                    verts[i] = p;                                                   // already world space
                }
                AddTriangles(list, prefix + Path(s.transform, scope) + "(skinned)", s.transform, verts, mesh, Matrix4x4.identity, root, lo, hi, ref rendererCount, ref triCount);
            }
            else
                AddTriangles(list, prefix + Path(s.transform, scope) + "(skinned, no bones)", s.transform, verts, mesh, s.transform.localToWorldMatrix, root, lo, hi, ref rendererCount, ref triCount);
        }
    }

    // Which scene object each obstacle came from (to tell a walker's own desk and nameplate from the rest).
    static readonly Dictionary<ClearMesh, Transform> Source = new Dictionary<ClearMesh, Transform>();

    // True for a mesh that belongs to the walker's own station: anything under its station object, and its nameplate
    // (Nameplate_<Role> stands on its desk). At the home slot the agent is at its desk by design, so these are exempt for the
    // first / last 10 cm of a route that starts / ends at home, and only there.
    static bool OwnStation(ClearMesh m, AgentMovement walker)
    {
        if (m == null || walker == null || !Source.TryGetValue(m, out var src) || src == null) return false;
        var station = walker.transform.parent;
        if (station != null && src.IsChildOf(station)) return true;
        string role = walker.name.StartsWith("Agent_") ? walker.name.Substring(6) : walker.name;
        for (var a = src; a != null; a = a.parent) if (a.name == "Nameplate_" + role) return true;
        return false;
    }

    static WalkClearance.Result Eval(Route r, Transform root, float radiusOverride, out ClearMesh[] arr, out float radius)
    {
        var mv = r.walker.GetComponent<AgentMovement>();
        radius = radiusOverride > 0f ? radiusOverride : RadiusOf(mv, root);
        Source.Clear();
        arr = Gather(root, mv, radius, mv.HomeWorld.y, out _, out _).ToArray();
        Vector3 home = root.InverseTransformPoint(mv.HomeWorld);
        bool startsHome = Flat(r.pts[0], home) < 0.002f, endsHome = Flat(r.pts[r.n - 1], home) < 0.002f;
        bool[] ownStart = null, ownEnd = null;
        if (startsHome || endsHome)
        {
            var own = new bool[arr.Length];
            for (int i = 0; i < arr.Length; i++) own[i] = OwnStation(arr[i], mv);
            if (startsHome) ownStart = own;
            if (endsHome) ownEnd = own;
        }
        return WalkClearance.Evaluate(r.pts, r.n, arr, arr.Length, radius, ownStart, ownEnd);
    }

    static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

    static Transform NearUserRig()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.name == "NearUserRig") return t;
        return null;
    }

    // Obstacles for a walker, as the plan-view triangles (WorkspaceRoot-local) of every mesh whose triangles reach into the agent's body band:
    // everything under WorkspaceRoot, plus the near-user panels (mode console, task dock, plaque) where they stand now (they are the
    // user's to move, and stay at the seat in Floor and Ceiling mode). Other agents appear as squares at their home slot, and the user zone as a square.
    static List<ClearMesh> Gather(Transform root, AgentMovement walker, float radius, float feetY, out int rendererCount, out int triCount)
    {
        var list = new List<ClearMesh>();
        var movers = Object.FindObjectsByType<AgentMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        rendererCount = 0; triCount = 0;
        float lo = feetY + BandLow, hi = feetY + BandHighOf(walker);
        AddMeshes(list, root, "", root, lo, hi, ref rendererCount, ref triCount);
        var near = NearUserRig();
        if (near != null && !near.IsChildOf(root)) AddMeshes(list, near, "NearUser:", root, lo, hi, ref rendererCount, ref triCount);
        foreach (var m in movers)
        {
            if (m == null || m == walker || !m.gameObject.activeInHierarchy) continue;
            Vector3 h = root.InverseTransformPoint(m.HomeWorld);
            AddRect(list, "Agent:" + m.name, h.x - radius, h.z - radius, h.x + radius, h.z + radius);
        }
        AddRect(list, "UserZone", UserZoneX - UserZoneHalf, UserZoneZ - UserZoneHalf, UserZoneX + UserZoneHalf, UserZoneZ + UserZoneHalf);
        return list;
    }
    static Vector3[] ToLocal(Transform root, Vector3[] world, int n)
    {
        var a = new Vector3[n];
        for (int i = 0; i < n; i++) a[i] = root.InverseTransformPoint(world[i]);
        return a;
    }

    static Vector3[] Reverse(Vector3[] p, int n)
    {
        var a = new Vector3[n];
        for (int i = 0; i < n; i++) a[i] = p[n - 1 - i];
        return a;
    }

    static MuseDesk.Agents.AgentView ViewOf(string id)
    {
        foreach (var v in Object.FindObjectsByType<MuseDesk.Agents.AgentView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (v.agentId == id) return v;
        return null;
    }

    // The point a courier stops at in front of its receiver when the pair has NO authored lane
    // (AgentHandoffController.HandoffRoutine, copied; start = where the courier is).
    static Vector3 HandoffDest(AgentHandoffController h, Vector3 courierPos, Transform courier, Transform target)
    {
        Vector3 toward = courierPos - target.position; toward.y = 0f;
        if (toward.sqrMagnitude < 1e-4f) toward = Vector3.back;
        Vector3 dest = target.position + toward.normalized * h.stopDistance;
        var frame = courier.parent != null ? courier.parent : courier;
        dest += frame.TransformVector(Vector3.back * h.backOffset);
        return new Vector3(dest.x, courierPos.y, dest.z);
    }

    static AgentHandoffController.Lane LaneOf(AgentHandoffController h, string src, string dst)
    {
        if (h == null || h.lanes == null) return null;
        foreach (var l in h.lanes) if (l != null && l.stand != null && l.sourceId == src && l.targetId == dst) return l;
        return null;
    }

    static void Add(List<Route> routes, Transform root, string name, AgentMovement walker, Vector3[] buf, int n)
    {
        routes.Add(new Route { name = name, walker = walker.transform, pts = ToLocal(root, buf, n), n = n });
    }

    // Every walk the desk agents can be sent on, built with the same code the walk itself uses (AgentMovement.BuildRoute):
    //   R1 / R2  Researcher home <-> delivery spot (exit waypoints)
    //   R3 / R5  Researcher home <-> the Developer (handoff from home, and every walk home after a handoff)
    //   R4       Researcher delivery spot -> the Developer (the handoff after an approval)
    //   R6 / R7  Developer home <-> the Tester
    //   R8       only without lanes: the straight walk home from where R4 ends (with lanes it is R5)
    static List<Route> Routes(Transform root, out string notes)
    {
        var routes = new List<Route>();
        var sb = new StringBuilder();
        var registry = Object.FindFirstObjectByType<AgentRegistry>(FindObjectsInactive.Include);
        var handoff = Object.FindFirstObjectByType<AgentHandoffController>(FindObjectsInactive.Include);
        var res = ViewOf("researcher"); var dev = ViewOf("developer"); var tst = ViewOf("tester");
        var resMove = res != null ? res.GetComponent<AgentMovement>() : null;
        var devMove = dev != null ? dev.GetComponent<AgentMovement>() : null;
        var tstMove = tst != null ? tst.GetComponent<AgentMovement>() : null;
        var buf = new Vector3[AgentWalkPath.MaxPoints];
        bool haveAnchor = resMove != null && registry != null && registry.deliveryAnchor != null;

        if (haveAnchor)
        {
            Vector3 home = resMove.HomeWorld, anchor = registry.deliveryAnchor.position;
            int n = resMove.BuildRoute(home, anchor, resMove.exitWaypoints, false, buf);
            Add(routes, root, "R1 Researcher home -> delivery (exit waypoints x" + (resMove.exitWaypoints != null ? resMove.exitWaypoints.Length : 0) + ")", resMove, buf, n);
            n = resMove.BuildRoute(anchor, home, resMove.exitWaypoints, true, buf);
            Add(routes, root, "R2 Researcher delivery -> home (rejected / no handoff)", resMove, buf, n);
        }
        else sb.Append("[no Researcher delivery route: movement=" + (resMove != null) + " registry=" + (registry != null) + " anchor=" + (registry != null && registry.deliveryAnchor != null) + "] ");

        if (handoff == null) { sb.Append("[no AgentHandoffController] "); notes = sb.ToString(); return routes; }

        if (resMove != null && devMove != null)
        {
            Vector3 home = resMove.HomeWorld;
            var lane = LaneOf(handoff, "researcher", "developer");
            if (lane != null)
            {
                Vector3 stand = new Vector3(lane.stand.position.x, home.y, lane.stand.position.z);
                int n = resMove.BuildRoute(home, stand, lane.fromHome, false, buf);
                Add(routes, root, "R3 Researcher home -> Developer (lane from home)", resMove, buf, n);
                if (haveAnchor)
                {
                    Vector3 anchor = registry.deliveryAnchor.position; anchor.y = home.y;
                    n = resMove.BuildRoute(anchor, stand, lane.fromDelivery, false, buf);
                    Add(routes, root, "R4 Researcher delivery -> Developer (approval handoff, lane from the delivery spot)", resMove, buf, n);
                }
                n = resMove.BuildRoute(stand, home, lane.fromHome, true, buf);
                Add(routes, root, "R5 Researcher Developer stand -> home (every walk home after a handoff)", resMove, buf, n);
            }
            else
            {
                sb.Append("[researcher->developer: no lane, straight walks] ");
                Vector3 dest = HandoffDest(handoff, home, resMove.transform, devMove.transform);
                int n = AgentWalkPath.Build(home, dest, new Vector3[0], 0, false, buf);
                Add(routes, root, "R3 Researcher home -> Developer (handoff from home, straight)", resMove, buf, n);
                Vector3 dest4 = dest;
                if (haveAnchor)
                {
                    Vector3 anchor = registry.deliveryAnchor.position; anchor.y = home.y;
                    dest4 = HandoffDest(handoff, anchor, resMove.transform, devMove.transform);
                    n = AgentWalkPath.Build(anchor, dest4, new Vector3[0], 0, false, buf);
                    Add(routes, root, "R4 Researcher delivery -> Developer (approval handoff, straight)", resMove, buf, n);
                }
                n = AgentWalkPath.Build(dest, home, new Vector3[0], 0, true, buf);
                Add(routes, root, "R5 Researcher Developer-front -> home (handoff return, straight)", resMove, buf, n);
                if (haveAnchor)
                {
                    n = AgentWalkPath.Build(dest4, home, new Vector3[0], 0, true, buf);
                    Add(routes, root, "R8 Researcher approval-handoff stop -> home (straight; not in the S9c list)", resMove, buf, n);
                }
            }
        }
        if (devMove != null && tstMove != null)
        {
            Vector3 home = devMove.HomeWorld;
            var lane = LaneOf(handoff, "developer", "tester");
            if (lane != null)
            {
                Vector3 stand = new Vector3(lane.stand.position.x, home.y, lane.stand.position.z);
                int n = devMove.BuildRoute(home, stand, lane.fromHome, false, buf);
                Add(routes, root, "R6 Developer home -> Tester (lane from home)", devMove, buf, n);
                n = devMove.BuildRoute(stand, home, lane.fromHome, true, buf);
                Add(routes, root, "R7 Developer Tester stand -> home", devMove, buf, n);
            }
            else
            {
                sb.Append("[developer->tester: no lane, straight walks] ");
                Vector3 dest = HandoffDest(handoff, home, devMove.transform, tstMove.transform);
                int n = AgentWalkPath.Build(home, dest, new Vector3[0], 0, false, buf);
                Add(routes, root, "R6 Developer home -> Tester (artifact handoff, straight)", devMove, buf, n);
                n = AgentWalkPath.Build(dest, home, new Vector3[0], 0, true, buf);
                Add(routes, root, "R7 Developer Tester-front -> home (handoff return, straight)", devMove, buf, n);
            }
        }
        notes = sb.ToString();
        return routes;
    }

    /// <summary>What counts as an obstacle for the Researcher's routes (names and root-local bounds).</summary>
    public static string Obstacles()
    {
        if (!Ready()) return "no scene";
        var root = Root();
        if (root == null) return "no WorkspaceRoot";
        var res = ViewOf("researcher"); var m = res != null ? res.GetComponent<AgentMovement>() : null;
        if (m == null) return "no Researcher AgentMovement";
        float radius = RadiusOf(m, root);
        float feetY = m.HomeWorld.y;
        var obs = Gather(root, m, radius, feetY, out int rc, out int tc);
        var sb = new StringBuilder();
        sb.Append("radius=" + radius.ToString("F3") + " feetY(world)=" + feetY.ToString("F3") + " rootScale=" + root.lossyScale.x.ToString("F3")
                  + " bandHigh=" + BandHighOf(m).ToString("F3") + " meshesInBand=" + rc + " triangles=" + tc + " | ");
        foreach (var o in obs)
            sb.Append(o.name + " x[" + o.minX.ToString("F3") + "," + o.maxX.ToString("F3") + "] z[" + o.minZ.ToString("F3") + "," + o.maxZ.ToString("F3") + "]; ");
        return sb.ToString();
    }

    // ───────────── Play Mode trace: where the agents REALLY walked ─────────────
    const string TraceFile = "Temp/DeskWalkTrace.csv";   // a numbered copy per trace: Temp/DeskWalkTrace_<n>.csv

    /// <summary>Play Mode: record every agent's root-local position each frame for the next N seconds (default 60) into
    /// Temp/DeskWalkTrace.csv. Sampling only: nothing is moved. Follow with TraceReport.</summary>
    public static string TraceStart() { return TraceFor("60"); }
    public static string TraceFor(string seconds)
    {
        if (!Application.isPlaying) return "not playing";
        var root = Root();
        if (root == null) return "no WorkspaceRoot";
        float.TryParse(seconds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dur);
        if (dur <= 0f) dur = 60f;
        var movers = Object.FindObjectsByType<AgentMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        // A newer trace ends an older one (the token is kept by the Editor across driver calls).
        int token = UnityEditor.SessionState.GetInt("DeskWalkTraceToken", 0) + 1;
        UnityEditor.SessionState.SetInt("DeskWalkTraceToken", token);
        string file = TraceFile.Replace(".csv", "_" + token + ".csv");
        UnityEditor.SessionState.SetString("DeskWalkTraceFile", file);
        var w = new System.IO.StreamWriter(new System.IO.FileStream(file, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite));
        float until = Time.realtimeSinceStartup + dur;
        UnityEditor.EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            bool done = !Application.isPlaying || Time.realtimeSinceStartup > until || root == null || UnityEditor.SessionState.GetInt("DeskWalkTraceToken", 0) != token;
            if (!done)
            {
                foreach (var m in movers)
                {
                    if (m == null) continue;
                    Vector3 p = root.InverseTransformPoint(m.transform.position);
                    w.Write(Time.time.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ";" + m.name + ";"
                            + p.x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + ";" + p.z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + ";"
                            + (m.IsMoving ? "1" : "0") + ";" + (m.IsTurningInPlace ? "1" : "0") + ";" + m.transform.eulerAngles.y.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "\n");
                }
                w.Flush();
            }
            else { w.Dispose(); UnityEditor.EditorApplication.update -= tick; }
        };
        UnityEditor.EditorApplication.update += tick;
        return "tracing " + movers.Length + " agents for " + dur.ToString("F0") + " s into " + file;
    }

    /// <summary>Clearance of the positions recorded by TraceStart against the scene as it is now: per agent, how far it
    /// travelled, the smallest gap while away from its home slot (own desk and nameplate exempt within 10 cm of home, as in
    /// Report), where that was, and how many times it stopped to turn. Read-only.</summary>
    public static string TraceReport()
    {
        string TraceFile = UnityEditor.SessionState.GetString("DeskWalkTraceFile", "");
        if (TraceFile.Length == 0 || !System.IO.File.Exists(TraceFile)) return "no trace file (run TraceFor in Play Mode first)";
        var root = Root();
        if (root == null) return "no WorkspaceRoot";
        string[] lines;
        using (var fs = new System.IO.FileStream(TraceFile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
        using (var rd = new System.IO.StreamReader(fs)) lines = rd.ReadToEnd().Split('\n');
        var byAgent = new Dictionary<string, List<string[]>>();
        foreach (var l in lines)
        {
            var f = l.Split(';');
            if (f.Length < 7) continue;
            if (!byAgent.TryGetValue(f[1], out var list)) byAgent[f[1]] = list = new List<string[]>();
            list.Add(f);
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        foreach (var kv in byAgent)
        {
            AgentMovement mv = null;
            foreach (var m in Object.FindObjectsByType<AgentMovement>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) if (m.name == kv.Key) mv = m;
            if (mv == null) { sb.Append(kv.Key + ": agent not found | "); continue; }
            float radius = RadiusOf(mv, root);
            Source.Clear();
            var obs = Gather(root, mv, radius, mv.HomeWorld.y, out _, out _).ToArray();
            var own = new bool[obs.Length];
            for (int i = 0; i < obs.Length; i++) own[i] = OwnStation(obs[i], mv);
            Vector3 home = root.InverseTransformPoint(mv.HomeWorld);
            float min = float.PositiveInfinity, minAll = float.PositiveInfinity, dist = 0f, far = 0f; string who = "-", at = ""; int moving = 0, turns = 0; bool wasTurning = false;
            Vector3 prev = Vector3.zero; bool have = false;
            foreach (var f in kv.Value)
            {
                var p = new Vector3(float.Parse(f[2], inv), 0f, float.Parse(f[3], inv));
                if (have) dist += Flat(p, prev);
                prev = p; have = true;
                bool turning = f[5] == "1";
                if (turning && !wasTurning) turns++;
                wasTurning = turning;
                if (f[4] != "1" && Flat(p, home) < 0.002f) continue;                 // standing at home: not a walk sample
                moving++;
                float fromHome = Flat(p, home);
                far = Mathf.Max(far, fromHome);
                for (int o = 0; o < obs.Length; o++)
                {
                    float g = WalkClearance.Gap(p.x, p.z, radius, obs[o]);
                    if (g < minAll) minAll = g;
                    if (own[o] && fromHome <= 0.10f) continue;
                    if (g < min) { min = g; who = obs[o].name; at = "(" + p.x.ToString("F3") + "," + p.z.ToString("F3") + ") t=" + f[0]; }
                }
            }
            sb.Append(kv.Key + ": samples=" + kv.Value.Count + " away-or-walking=" + moving + " travelled=" + dist.ToString("F2") + " m, farthest from home " + far.ToString("F2")
                      + " m, turns-in-place=" + turns + ", min clearance=" + (float.IsInfinity(min) ? "n/a" : min.ToString("F3") + " @ " + who + " " + at)
                      + ", unexempted=" + (float.IsInfinity(minAll) ? "n/a" : minAll.ToString("F3")) + " | ");
        }
        return sb.Length == 0 ? "trace is empty" : sb.ToString();
    }

    /// <summary>Plan-view picture of what the check treats as an obstacle for one agent ("researcher", "developer" or "tester";
    /// default researcher): one character per centimetre, WorkspaceRoot-local, user at the top (+z), the user's right (-x) on the
    /// left. '#' is a mesh in the body band, 'a' another agent's home slot, 'u' the user zone, '.' free. Read-only.</summary>
    public static string Map() { return MapOf("researcher"); }
    public static string MapOf(string agentId)
    {
        var root = Root();
        if (root == null) return "no WorkspaceRoot";
        var v = ViewOf(string.IsNullOrEmpty(agentId) ? "researcher" : agentId); var m = v != null ? v.GetComponent<AgentMovement>() : null;
        if (m == null) return "no AgentMovement for " + agentId;
        float radius = RadiusOf(m, root);
        var obs = Gather(root, m, radius, m.HomeWorld.y, out _, out _);
        var sb = new StringBuilder();
        sb.Append("\n");
        for (int zi = 50; zi >= -26; zi--)
        {
            float z = zi * 0.01f;
            sb.Append((z >= 0 ? "+" : "-") + Mathf.Abs(z).ToString("F2") + " ");
            for (int xi = -64; xi <= 64; xi++)
            {
                float x = xi * 0.01f; char c = '.';
                foreach (var o in obs)
                {
                    if (x < o.minX - 0.006f || x > o.maxX + 0.006f || z < o.minZ - 0.006f || z > o.maxZ + 0.006f) continue;
                    if (WalkClearance.Gap(x, z, 0f, o) > 0.005f) continue;
                    c = o.name.StartsWith("Agent:") ? 'a' : o.name == "UserZone" ? 'u' : '#';
                    if (c == '#') break;
                }
                sb.Append(c);
            }
            sb.Append("\n");
        }
        sb.Append("     x from -0.64 (left) to +0.64, 1 cm per character; agent=" + m.name + " radius=" + radius.ToString("F3"));
        return sb.ToString();
    }

    /// <summary>For the worst point of each route (at the collider radius): the nearest triangle of the offending mesh, its plan
    /// distance, its height above the agent's feet (min..max, mm) and which mesh vertex it sits on. Read-only.</summary>
    public static string Why()
    {
        if (!Ready()) return "no scene";
        var root = Root();
        if (root == null) return "no WorkspaceRoot";
        var routes = Routes(root, out _);
        var sb = new StringBuilder();
        foreach (var r in routes)
        {
            var mv = r.walker.GetComponent<AgentMovement>();
            float feetY = mv.HomeWorld.y;
            var res = Eval(r, root, -1f, out var obs, out float radius);
            if (res.obstacle < 0 || res.minClearance >= MinClearance) continue;
            string name = obs[res.obstacle].name;
            float bestD = float.PositiveInfinity, yMin = 0, yMax = 0; Vector3 at = Vector3.zero;
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(false))
            {
                if (f.sharedMesh == null || Path(f.transform, root) != name) continue;
                var vs = f.sharedMesh.vertices; var ts = f.sharedMesh.triangles; var l2w = f.transform.localToWorldMatrix;
                for (int i = 0; i + 2 < ts.Length; i += 3)
                {
                    Vector3 a = l2w.MultiplyPoint3x4(vs[ts[i]]), b = l2w.MultiplyPoint3x4(vs[ts[i + 1]]), c = l2w.MultiplyPoint3x4(vs[ts[i + 2]]);
                    float y0 = Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - feetY, y1 = Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - feetY;
                    if (y1 < BandLow || y0 > BandHighOf(mv)) continue;
                    Vector3 la = root.InverseTransformPoint(a), lb = root.InverseTransformPoint(b), lc = root.InverseTransformPoint(c);
                    float d = WalkClearance.PointTriangleDistance(res.atX, res.atZ, la.x, la.z, lb.x, lb.z, lc.x, lc.z);
                    if (d < bestD) { bestD = d; yMin = y0; yMax = y1; at = (la + lb + lc) / 3f; }
                }
                break;
            }
            sb.Append(r.name.Substring(0, 2) + " worst at (" + res.atX.ToString("F3") + "," + res.atZ.ToString("F3") + ") vs " + name + ": nearest triangle " + (bestD * 1000f).ToString("F0")
                      + " mm away, " + (yMin * 1000f).ToString("F0") + ".." + (yMax * 1000f).ToString("F0") + " mm above the feet, centroid (" + at.x.ToString("F3") + "," + at.z.ToString("F3") + ") | ");
        }
        return sb.ToString();
    }
    /// <summary>What-if for a proposed lane (nothing is moved). Arg "radius;x,z;x,z;..." in WorkspaceRoot-local metres, evaluated for the
    /// Researcher against the live scene, e.g. "0.05;-0.30,0;-0.50,0;-0.50,0.375;-0.20,0.375".</summary>
    public static string Try(string spec)
    {
        if (!Ready()) return "no scene";
        var root = Root();
        var parts = spec.Split(';');
        // Optional first field: the walker ("researcher", "developer", "tester"); default researcher.
        string who = "researcher"; int first = 0;
        if (parts.Length > 0 && parts[0].Length > 0 && char.IsLetter(parts[0][0])) { who = parts[0].Trim(); first = 1; }
        var res = ViewOf(who); var mv = res != null ? res.GetComponent<AgentMovement>() : null;
        if (root == null || mv == null) return "no root / " + who;
        float radius = float.Parse(parts[first], System.Globalization.CultureInfo.InvariantCulture);
        var pts = new List<Vector3>();
        for (int i = first + 1; i < parts.Length; i++)
        {
            var xz = parts[i].Split(',');
            pts.Add(new Vector3(float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), 0f, float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture)));
        }
        var r = Eval(new Route { name = "try", walker = mv.transform, pts = pts.ToArray(), n = pts.Count }, root, radius, out var obs, out _);
        return "r=" + radius.ToString("F3") + " len=" + r.length.ToString("F2") + " min=" + r.minClearance.ToString("F3") + " @ " + (r.obstacle >= 0 ? obs[r.obstacle].name : "-")
               + " (d=" + r.atDistance.ToString("F2") + " x=" + r.atX.ToString("F3") + " z=" + r.atZ.ToString("F3") + ") unexempted=" + r.minClearanceAll.ToString("F3");
    }
    /// <summary>One line per route: min clearance (strict, with endpoint-adjacent obstacles exempt for the first and last 10 cm),
    /// the obstacle and where, the unexempted minimum, and PASS / FAIL. No state is changed.</summary>
    public static string Report() { return ReportAt(null); }

    /// <summary>Same as Report with the footprint radius replaced (metres, e.g. "0.05"): how much of a failure is the 0.08 collider.</summary>
    public static string ReportAt(string radiusOverride)
    {
        if (!Ready()) return "no scene";
        float over = -1f;
        if (!string.IsNullOrEmpty(radiusOverride)) float.TryParse(radiusOverride, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out over);
        var root = Root();
        if (root == null) return "no WorkspaceRoot";
        var routes = Routes(root, out string notes);
        var sb = new StringBuilder();
        sb.Append(notes);
        int fails = 0;
        foreach (var r in routes)
        {
            var res = Eval(r, root, over, out var arr, out float radius);
            bool pass = res.minClearance >= MinClearance;
            if (!pass) fails++;
            string who = res.obstacle >= 0 ? arr[res.obstacle].name : "-";
            string whoAll = res.obstacleAll >= 0 ? arr[res.obstacleAll].name : "-";
            var pts = new StringBuilder();
            for (int i = 0; i < r.n; i++) pts.Append("(").Append(r.pts[i].x.ToString("F3")).Append(",").Append(r.pts[i].z.ToString("F3")).Append(")");
            sb.Append(r.name + " r=" + radius.ToString("F3") + " pts" + pts + ": len=" + res.length.ToString("F2") + " m, corners=" + Mathf.Max(0, r.n - 2)
                      + ", min=" + (float.IsInfinity(res.minClearance) ? "inf" : res.minClearance.ToString("F3")) + " @ " + who
                      + " (d=" + res.atDistance.ToString("F2") + " x=" + res.atX.ToString("F3") + " z=" + res.atZ.ToString("F3") + ")"
                      + ", unexempted=" + (float.IsInfinity(res.minClearanceAll) ? "inf" : res.minClearanceAll.ToString("F3")) + " @ " + whoAll
                      + ", samples=" + res.samples + " => " + (pass ? "PASS" : (res.minClearance >= 0f ? "TIGHT (under " + MinClearance.ToString("F3") + ")" : "FAIL")) + " | ");
        }
        sb.Append("routes=" + routes.Count + " below " + MinClearance.ToString("F3") + " m: " + fails + " (own desk and nameplate exempt within 10 cm of the home slot; unexempted = no exemption at all)");
        return sb.ToString();
    }
}






