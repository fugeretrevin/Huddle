using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using MuseDesk.Agents;

/// <summary>
/// Walk-through-desk measurement. Edit or Play Mode (works in both; it samples the walk polylines
/// the way AgentMovement builds them, it does not move anything).
///   WalkClipDrive.Measure()      straight line (the old behaviour) vs the current routed walks
///   WalkClipDrive.MeasureRoot()  same, plus the workspace root scale/position (Floor/Ceiling sanity)
///   WalkClipDrive.Delivery()     Play Mode: really walks the Researcher out and back, logging real positions
/// Obstacles: every non-particle renderer under each station's "Workstation", Phase6 children and
/// SoftwareFactory whose bottom is below 0.15 m (root-local). Footprint radius 12 mm.
/// </summary>
public static class WalkClipDrive
{
    const float Radius = 0.012f;
    const int Samples = 160;

    public struct Box { public string name; public Vector3 mn, mx; }

    static Transform Root()
    {
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        return all.FirstOrDefault(t => t.name == "WorkspaceRoot");
    }

    static List<Box> Obstacles(Transform root)
    {
        var list = new List<Box>();
        void Add(Transform t, string label)
        {
            foreach (var r in t.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var b = r.localBounds;   // object-space box: exact even when the root is rotated by an odd yaw
                Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var l = root.InverseTransformPoint(r.transform.TransformPoint(c)); mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
                }
                if (mn.y > 0.15f) continue;
                list.Add(new Box { name = label + "/" + r.name, mn = mn, mx = mx });
            }
        }
        foreach (var s in new[] { "ResearchStation", "DevStation", "QAStation" })
        {
            var st = root.Find(s); var ws = st != null ? st.Find("Workstation") : null;
            if (ws != null) Add(ws, s);
        }
        var p6 = root.Find("Phase6");
        if (p6 != null) foreach (Transform c in p6) if (c.gameObject.activeSelf) Add(c, "Phase6/" + c.name);
        var f = root.Find("SoftwareFactory"); if (f != null) Add(f, "SoftwareFactory");
        return list;
    }

    // Returns depth (m) the point sits inside an inflated box, or 0.
    static float Depth(Vector3 p, Box b)
    {
        float x0 = b.mn.x - Radius, x1 = b.mx.x + Radius, z0 = b.mn.z - Radius, z1 = b.mx.z + Radius;
        if (p.x < x0 || p.x > x1 || p.z < z0 || p.z > z1) return 0f;
        return Mathf.Min(Mathf.Min(p.x - x0, x1 - p.x), Mathf.Min(p.z - z0, z1 - p.z));
    }

    static string Walk(Transform root, List<Box> obs, AgentMovement mv, string label, Vector3 start, Vector3 target, bool returning, bool viaExit)
    {
        var buf = new Vector3[AgentWalkPath.MaxPoints];
        int n = mv.BuildPath(start, target, returning, viaExit, buf);
        float len = AgentWalkPath.Length(buf, n);
        int inside = 0; float maxDepth = 0f; var hit = new HashSet<string>();
        for (int i = 0; i <= Samples; i++)
        {
            Vector3 w = AgentWalkPath.Sample(buf, n, i / (float)Samples, out _, out _, out _);
            Vector3 l = root.InverseTransformPoint(w);
            bool any = false;
            foreach (var b in obs)
            {
                float d = Depth(l, b);
                if (d > 0f) { any = true; if (d > maxDepth) maxDepth = d; hit.Add(b.name); }
            }
            if (any) inside++;
        }
        var pts = string.Join(">", Enumerable.Range(0, n).Select(i => { var l = root.InverseTransformPoint(buf[i]); return $"({l.x:F2},{l.z:F2})"; }));
        string hits = hit.Count == 0 ? "-" : string.Join(",", hit.Take(3).Select(h => h.Substring(h.LastIndexOf('/') + 1)));
        return $"{label,-22} pts={n} len={len * 100:F0}cm clip={100f * inside / (Samples + 1):F0}% maxDepth={maxDepth * 1000:F0}mm hits={hits}  {pts}";
    }

    static string Run(bool withRoot)
    {
        var root = Root(); if (root == null) return "no WorkspaceRoot";
        var obs = Obstacles(root);
        var sb = new StringBuilder();
        sb.AppendLine($"obstacles={obs.Count} rootScale={root.lossyScale.x:F3} rootPos={root.position}");
        var byName = new Dictionary<string, AgentMovement>();
        foreach (var m in root.GetComponentsInChildren<AgentMovement>(true)) byName[m.name.Replace("Agent_", "")] = m;
        var ho = Object.FindFirstObjectByType<AgentHandoffController>();
        float stop = ho != null ? ho.stopDistance : 0.18f;

        // delivery
        var reg = Object.FindFirstObjectByType<AgentRegistry>();
        if (byName.TryGetValue("Researcher", out var rm) && reg != null && reg.deliveryAnchor != null)
        {
            Vector3 home = rm.HomeWorld, dz = reg.deliveryAnchor.position;
            bool hasWp = rm.exitWaypoints != null && rm.exitWaypoints.Length > 0;
            sb.AppendLine($"Researcher waypoints={(hasWp ? rm.exitWaypoints.Length : 0)} maxRoutedSpeed={rm.maxRoutedSpeed:F2} travel={rm.travelSeconds:F1}s");
            sb.AppendLine(Walk(root, obs, rm, "deliver STRAIGHT(old)", home, dz, false, false));
            sb.AppendLine(Walk(root, obs, rm, "deliver ROUTED", home, dz, false, true));
            sb.AppendLine(Walk(root, obs, rm, "return STRAIGHT(old)", dz, home, true, false));
            sb.AppendLine(Walk(root, obs, rm, "return ROUTED", dz, home, true, true));
            var buf = new Vector3[AgentWalkPath.MaxPoints];
            int n = rm.BuildPath(home, dz, false, true, buf);
            float len = AgentWalkPath.Length(buf, n);
            sb.AppendLine($"duration routed={AgentWalkPath.Duration(rm.travelSeconds, len, rm.maxRoutedSpeed):F2}s (old {rm.travelSeconds:F2}s)");
        }
        else sb.AppendLine("Researcher/registry/deliveryAnchor missing");

        // handoffs (source walks to a point stopDistance in front of target, on the source side; then home)
        string[] names = { "Researcher", "Developer", "Tester" };
        foreach (var a in names) foreach (var b in names)
        {
            if (a == b || !byName.TryGetValue(a, out var ma) || !byName.TryGetValue(b, out var mb)) continue;
            Vector3 srcHome = ma.HomeWorld;
            Vector3 toward = srcHome - mb.HomeWorld; toward.y = 0f;
            Vector3 dest = mb.HomeWorld + toward.normalized * stop;
            var fr = ma.transform.parent != null ? ma.transform.parent : ma.transform;
            dest += fr.TransformVector(Vector3.back * (ho != null ? ho.backOffset : 0f));
            dest.y = srcHome.y;
            sb.AppendLine(Walk(root, obs, ma, $"handoff {a[0]}>{b[0]} out", srcHome, dest, false, false));
            sb.AppendLine(Walk(root, obs, ma, $"handoff {a[0]}>{b[0]} home", dest, srcHome, true, false));
        }
        return sb.ToString();
    }

    public static string Measure() => Run(false);
    public static string MeasureRoot() => Run(true);

    static Transform _root; static List<Box> _obs; static AgentMovement _m; static int _phase;
    static float _t0, _outSecs, _backSecs, _maxDepth; static int _frames, _insideOut, _insideBack;
    static readonly HashSet<string> _hit = new HashSet<string>();
    static float _maxStep;

    /// <summary>Play Mode: really send the Researcher to the delivery anchor and home; samples every Editor tick.
    /// Read the result with DeliveryResult().</summary>
    public static string Delivery()
    {
        if (!Application.isPlaying) return "Play Mode only";
        _root = Root(); var reg = Object.FindFirstObjectByType<AgentRegistry>();
        _m = _root.GetComponentsInChildren<AgentMovement>(true).FirstOrDefault(m => m.name.Contains("Researcher"));
        if (_m == null || reg == null || reg.deliveryAnchor == null) return "missing";
        _obs = Obstacles(_root); _hit.Clear(); _frames = _insideOut = _insideBack = 0; _maxDepth = 0f; _maxStep = 0f;
        _phase = 1; _t0 = Time.time;
        _m.DeliverTo(reg.deliveryAnchor, null, true);
        UnityEditor.EditorApplication.update -= Tick; UnityEditor.EditorApplication.update += Tick;
        return "sampling started";
    }

    static Vector3 _last;
    static void Tick()
    {
        if (_phase == 0 || _phase == 3 || _m == null) return;
        if (_m.IsMoving)
        {
            Vector3 l = _root.InverseTransformPoint(_m.transform.position);
            if (_frames > 0) _maxStep = Mathf.Max(_maxStep, (l - _last).magnitude);
            _last = l; _frames++;
            bool any = false;
            foreach (var b in _obs) { float d = Depth(l, b); if (d > 0f) { any = true; if (d > _maxDepth) _maxDepth = d; _hit.Add(b.name); } }
            if (any) { if (_phase == 1) _insideOut++; else _insideBack++; }
        }
        else if (Time.time - _t0 > 0.3f)
        {
            if (_phase == 1) { _outSecs = Time.time - _t0; _phase = 2; _t0 = Time.time; _m.ReturnHome(); }
            else { _backSecs = Time.time - _t0; _phase = 3; UnityEditor.EditorApplication.update -= Tick; Save(); }
        }
    }

    static void Save() => UnityEditor.SessionState.SetString("WalkClipResult", Fmt());
    public static string DeliveryResult() => UnityEditor.SessionState.GetString("WalkClipResult", "no result yet");
    static string Fmt() =>
        $"phase={_phase} out={_outSecs:F2}s back={_backSecs:F2}s ticks={_frames} insideTicks out={_insideOut} back={_insideBack} maxDepth={_maxDepth * 1000:F0}mm hits={string.Join(",", _hit.Select(h => h.Substring(h.LastIndexOf('/') + 1)).Take(4))} atHome={(_m != null && _m.AtHome)} maxStep={_maxStep * 1000:F0}mm";
}


