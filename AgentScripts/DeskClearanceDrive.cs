using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using MuseDesk.Agents;

/// <summary>
/// Polish S9c (audit C6): Play Mode check that every authored desk walk route keeps the agent's footprint clear of the
/// desk props, the workstations, the other agents and the user zone. It reads the REAL scene (renderers, the agents'
/// AgentMovement waypoints, the registry's delivery anchor, the handoff controller's stop distance) and asserts every sample
/// (every centimetre, plus every corner) with the pure helper WalkClearance. It moves nothing and changes no state.
///
///   scripts\drv.ps1 DeskClearanceDrive Report -Max 4000     one line per route: min clearance, the obstacle, where
///   scripts\drv.ps1 DeskClearanceDrive Obstacles -Max 4000  what was treated as an obstacle (names and root-local bounds)
///
/// Frame: WorkspaceRoot-local (x to the user's left, +z toward the seated user), metres at scale 1.
/// Footprint: the agent root CapsuleCollider radius times its scale (0.05 m in the prefabs), else WalkClearance.DefaultAgentRadius.
/// Obstacle: the plan-view TRIANGLES of every MeshFilter under WorkspaceRoot (not text, not part of an agent, not hidden by scale) that reach
/// into the agent's body band (feet + 15 mm .. the top of its collider, 0.25 m). Surfaces the agent stands on are below the band and ignored.
/// Other agents count as obstacles at their HOME slot (a disc of the footprint radius), except the walker itself.
/// User zone: a square 0.30 m across centred on the seated head (root-local 0, 0.62), documented in the S9c doc.
/// </summary>
public static class DeskClearanceDrive
{
    const float BandLow = 0.015f;
    const float UserZoneX = 0f, UserZoneZ = 0.62f, UserZoneHalf = 0.15f;

    struct Route { public string name; public Transform walker; public Vector3[] pts; public int n; }

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

    // Obstacles for a walker, as the plan-view triangles (WorkspaceRoot-local) of every mesh whose triangles reach into the agent's body band;
    // other agents appear as discs-as-squares at their home slot, and the user zone as a square.
    static List<ClearMesh> Gather(Transform root, AgentMovement walker, float radius, float feetY, out int rendererCount, out int triCount)
    {
        var list = new List<ClearMesh>();
        var movers = Object.FindObjectsByType<AgentMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        rendererCount = 0; triCount = 0;
        float lo = feetY + BandLow, hi = feetY + BandHighOf(walker);
        var verts = new List<Vector3>(); var idx = new List<int>();
        foreach (var f in root.GetComponentsInChildren<MeshFilter>(false))
        {
            if (f == null || f.sharedMesh == null) continue;
            var r = f.GetComponent<MeshRenderer>();
            if (r == null || !r.enabled || !f.gameObject.activeInHierarchy) continue;
            if (f.GetComponent<TMP_Text>() != null) continue;
            if (f.GetComponentInParent<AgentMovement>() != null) continue;          // agents (and what they carry) are not furniture
            if (HiddenByScale(f.transform, root)) continue;
            var wb = r.bounds;
            if (wb.size.magnitude < 0.004f || wb.max.y < lo || wb.min.y > hi) continue;
            rendererCount++;

            var mesh = f.sharedMesh;
            mesh.GetVertices(verts); mesh.GetIndices(idx, 0);
            var obs = new ClearMesh(Path(f.transform, root), idx.Count / 3);
            var l2w = f.transform.localToWorldMatrix;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (sub > 0) mesh.GetIndices(idx, sub);
                for (int i = 0; i + 2 < idx.Count; i += 3)
                {
                    Vector3 a = l2w.MultiplyPoint3x4(verts[idx[i]]), b = l2w.MultiplyPoint3x4(verts[idx[i + 1]]), c = l2w.MultiplyPoint3x4(verts[idx[i + 2]]);
                    float ymin = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), ymax = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                    if (ymax < lo || ymin > hi) continue;                           // below the feet or above the head
                    Vector3 la = root.InverseTransformPoint(a), lb = root.InverseTransformPoint(b), lc = root.InverseTransformPoint(c);
                    obs.Add(la.x, la.z, lb.x, lb.z, lc.x, lc.z);
                }
            }
            if (obs.triCount > 0) { list.Add(obs); triCount += obs.triCount; }
        }
        foreach (var m in movers)
        {
            if (m == null || m == walker) continue;
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

    // The point a courier stops at in front of its receiver (AgentHandoffController.HandoffRoutine, copied; start = where the courier is).
    static Vector3 HandoffDest(AgentHandoffController h, Vector3 courierPos, Transform courier, Transform target)
    {
        Vector3 toward = courierPos - target.position; toward.y = 0f;
        if (toward.sqrMagnitude < 1e-4f) toward = Vector3.back;
        Vector3 dest = target.position + toward.normalized * h.stopDistance;
        var frame = courier.parent != null ? courier.parent : courier;
        dest += frame.TransformVector(Vector3.back * h.backOffset);
        return new Vector3(dest.x, courierPos.y, dest.z);
    }

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

        // R1/R2: the Researcher's delivery walk (exit waypoints, deliveryAnchor) and its walk home (the same points reversed).
        if (resMove != null && registry != null && registry.deliveryAnchor != null)
        {
            Vector3 home = resMove.HomeWorld, anchor = registry.deliveryAnchor.position;
            int n = resMove.BuildPath(home, anchor, false, true, buf);
            routes.Add(new Route { name = "R1 Researcher home -> delivery (exit waypoints x" + ((resMove.exitWaypoints != null ? resMove.exitWaypoints.Length : 0)) + ")", walker = resMove.transform, pts = ToLocal(root, buf, n), n = n });
            n = resMove.BuildPath(anchor, home, true, true, buf);
            routes.Add(new Route { name = "R2 Researcher delivery -> home (rejected / no handoff)", walker = resMove.transform, pts = ToLocal(root, buf, n), n = n });
            if (handoff != null && devMove != null)
            {
                // R4: after an approval the handoff starts from where the Researcher stands (the delivery spot), straight to the Developer.
                Vector3 dest = HandoffDest(handoff, new Vector3(anchor.x, home.y, anchor.z), resMove.transform, devMove.transform);
                n = AgentWalkPath.Build(new Vector3(anchor.x, home.y, anchor.z), dest, new Vector3[0], 0, false, buf);
                routes.Add(new Route { name = "R4 Researcher delivery -> Developer (approval handoff, straight)", walker = resMove.transform, pts = ToLocal(root, buf, n), n = n });
            }
        }
        else sb.Append("[no Researcher route: movement=" + (resMove != null) + " registry=" + (registry != null) + " anchor=" + (registry != null && registry.deliveryAnchor != null) + "] ");

        if (handoff != null)
        {
            if (resMove != null && devMove != null)
            {
                Vector3 home = resMove.HomeWorld;
                Vector3 dest = HandoffDest(handoff, home, resMove.transform, devMove.transform);
                int n = AgentWalkPath.Build(home, dest, new Vector3[0], 0, false, buf);
                routes.Add(new Route { name = "R3 Researcher home -> Developer (handoff from home)", walker = resMove.transform, pts = ToLocal(root, buf, n), n = n });
                n = AgentWalkPath.Build(dest, home, new Vector3[0], 0, true, buf);
                routes.Add(new Route { name = "R5 Researcher Developer-front -> home (handoff return, straight)", walker = resMove.transform, pts = ToLocal(root, buf, n), n = n });
            }
            if (devMove != null && tstMove != null)
            {
                Vector3 home = devMove.HomeWorld;
                Vector3 dest = HandoffDest(handoff, home, devMove.transform, tstMove.transform);
                int n = AgentWalkPath.Build(home, dest, new Vector3[0], 0, false, buf);
                routes.Add(new Route { name = "R6 Developer home -> Tester (artifact handoff)", walker = devMove.transform, pts = ToLocal(root, buf, n), n = n });
                n = AgentWalkPath.Build(dest, home, new Vector3[0], 0, true, buf);
                routes.Add(new Route { name = "R7 Developer Tester-front -> home (handoff return, straight)", walker = devMove.transform, pts = ToLocal(root, buf, n), n = n });
            }
        }
        else sb.Append("[no AgentHandoffController] ");
        notes = sb.ToString();
        return routes;
    }

    /// <summary>What counts as an obstacle for the Researcher's routes (names and root-local bounds).</summary>
    public static string Obstacles()
    {
        if (!Application.isPlaying) return "not playing";
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

    /// <summary>For the worst point of each route (at the collider radius): the nearest triangle of the offending mesh, its plan
    /// distance, its height above the agent's feet (min..max, mm) and which mesh vertex it sits on. Read-only.</summary>
    public static string Why()
    {
        if (!Application.isPlaying) return "not playing";
        var root = Root();
        if (root == null) return "no WorkspaceRoot";
        var routes = Routes(root, out _);
        var sb = new StringBuilder();
        foreach (var r in routes)
        {
            var mv = r.walker.GetComponent<AgentMovement>();
            float radius = RadiusOf(mv, root), feetY = mv.HomeWorld.y;
            var obs = Gather(root, mv, radius, feetY, out _, out _).ToArray();
            var res = WalkClearance.Evaluate(r.pts, r.n, obs, obs.Length, radius);
            if (res.obstacle < 0 || res.minClearance >= 0f) continue;
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
        if (!Application.isPlaying) return "not playing";
        var root = Root();
        var res = ViewOf("researcher"); var mv = res != null ? res.GetComponent<AgentMovement>() : null;
        if (root == null || mv == null) return "no root / Researcher";
        var parts = spec.Split(';');
        float radius = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        var pts = new List<Vector3>();
        for (int i = 1; i < parts.Length; i++)
        {
            var xz = parts[i].Split(',');
            pts.Add(new Vector3(float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), 0f, float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture)));
        }
        var obs = Gather(root, mv, radius, mv.HomeWorld.y, out _, out _).ToArray();
        var r = WalkClearance.Evaluate(pts.ToArray(), pts.Count, obs, obs.Length, radius);
        return "r=" + radius.ToString("F3") + " len=" + r.length.ToString("F2") + " min=" + r.minClearance.ToString("F3") + " @ " + (r.obstacle >= 0 ? obs[r.obstacle].name : "-")
               + " (d=" + r.atDistance.ToString("F2") + " x=" + r.atX.ToString("F3") + " z=" + r.atZ.ToString("F3") + ") unexempted=" + r.minClearanceAll.ToString("F3");
    }
    /// <summary>One line per route: min clearance (strict, with endpoint-adjacent obstacles exempt for the first and last 10 cm),
    /// the obstacle and where, the unexempted minimum, and PASS / FAIL. No state is changed.</summary>
    public static string Report() { return ReportAt(null); }

    /// <summary>Same as Report with the footprint radius replaced (metres, e.g. "0.05"): how much of a failure is the 0.08 collider.</summary>
    public static string ReportAt(string radiusOverride)
    {
        if (!Application.isPlaying) return "not playing";
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
            var mv = r.walker.GetComponent<AgentMovement>();
            float radius = over > 0f ? over : RadiusOf(mv, root);
            float feetY = mv.HomeWorld.y;
            var obs = Gather(root, mv, radius, feetY, out _, out _);
            var arr = obs.ToArray();
            var res = WalkClearance.Evaluate(r.pts, r.n, arr, arr.Length, radius);
            bool pass = res.minClearance >= 0f;
            if (!pass) fails++;
            string who = res.obstacle >= 0 ? arr[res.obstacle].name : "-";
            string whoAll = res.obstacleAll >= 0 ? arr[res.obstacleAll].name : "-";
            var pts = new StringBuilder();
            for (int i = 0; i < r.n; i++) pts.Append("(").Append(r.pts[i].x.ToString("F3")).Append(",").Append(r.pts[i].z.ToString("F3")).Append(")");
            sb.Append(r.name + " r=" + radius.ToString("F3") + " pts" + pts + ": len=" + res.length.ToString("F2") + " m, corners=" + Mathf.Max(0, r.n - 2)
                      + ", min=" + (float.IsInfinity(res.minClearance) ? "inf" : res.minClearance.ToString("F3")) + " @ " + who
                      + " (d=" + res.atDistance.ToString("F2") + " x=" + res.atX.ToString("F3") + " z=" + res.atZ.ToString("F3") + ")"
                      + ", unexempted=" + (float.IsInfinity(res.minClearanceAll) ? "inf" : res.minClearanceAll.ToString("F3")) + " @ " + whoAll
                      + ", samples=" + res.samples + " => " + (pass ? (res.minClearance < 0.01f ? "PASS(tight)" : "PASS") : "FAIL") + " | ");
        }
        sb.Append("routes=" + routes.Count + " fails=" + fails);
        return sb.ToString();
    }
}






