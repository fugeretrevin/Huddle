using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using MuseDesk.Spatial;
using MuseDesk.Workspace;

/// <summary>
/// Play Mode driver for the ceiling polish pass. Everything here only READS the scene, except
/// ForceNeedsYou / ForceProblem / ForceClear, which put a made-up level into the attention model
/// so the cue can be looked at without a run (driver only; never part of the app).
///
/// Typical sequence (with AgentScripts/SpatialDrive.cs for the moves):
///   SpatialDrive.Enable
///   CeilingPolishDrive.SnapDesk
///   SpatialDrive.Ceiling, SpatialDrive.Confirm      CeilingPolishDrive.ProbeStart just before Confirm
///   (wait for the dock)                             CeilingPolishDrive.ProbeReport, SnapDocked
///   CeilingPolishDrive.ForceNeedsYou, Beacon        (docked: BelowPod, up angle 0)
///   SpatialDrive.PodDown (ProbeStart before it)     ProbeReport, SnapLowered, Beacon
///   CeilingPolishDrive.PropTable                    every prop: desk vs docked vs lowered
///   CeilingPolishDrive.Bodies / Carrier             physics state of every rigidbody
///
/// Snapshots are kept in Temp/CeilingPolishDrive so they survive between driver calls even if each
/// call is compiled on its own.
/// </summary>
public static class CeilingPolishDrive
{
    const string Dir = "Temp/CeilingPolishDrive";
    const string ForcedId = "drive-placement-test";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static T One<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static IEnumerable<Transform> AllTransforms() => Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
        .SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<Transform>(true));
    static Transform Root()
    {
        var ws = SpatialWorkspaceState.WorkspaceRoot;
        return ws != null ? ws : AllTransforms().FirstOrDefault(t => t.name == "WorkspaceRoot");
    }
    static string P(Vector3 v) => "(" + v.x.ToString("F3", Inv) + ", " + v.y.ToString("F3", Inv) + ", " + v.z.ToString("F3", Inv) + ")";
    static string Mm(float m) => (m * 1000f).ToString("F1", Inv) + "mm";
    static float UpAngle(Transform t) => Vector3.Angle(t.up, Vector3.up);

    // ───────────────────────── tracked props ─────────────────────────

    /// <summary>Every direct prop of the office (children of the root and of its grouping objects)
    /// and every rigidbody, by name. Names are how a prop is found again after NearUserKit has
    /// moved it to its rig.</summary>
    static List<Transform> Tracked(Transform ws)
    {
        var list = new List<Transform>();
        var seen = new HashSet<Transform>();
        var groups = new List<Transform> { ws };
        foreach (var n in new[] { "Phase6", "OfficeProps_Collection", "OfficeV2", "V2_DepthFixed", "V2_InboxScaled" })
        {
            var g = AllTransforms().FirstOrDefault(t => t.name == n);
            if (g != null) groups.Add(g);
        }
        var kit = One<NearUserKit>();
        if (kit != null && kit.rig != null) groups.Add(kit.rig);
        foreach (var g in groups)
            foreach (Transform c in g)
                if (c != null && !groups.Contains(c) && seen.Add(c)) list.Add(c);
        foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (rb == null) continue;
            bool office = rb.transform.IsChildOf(ws) || (kit != null && kit.rig != null && rb.transform.IsChildOf(kit.rig));
            if (office && seen.Add(rb.transform)) list.Add(rb.transform);
        }
        return list;
    }

    static string Key(Transform t, Dictionary<string, int> used)
    {
        string k = t.name.Replace('\t', ' ');
        if (used.TryGetValue(k, out int n)) { used[k] = n + 1; return k + "#" + (n + 1); }
        used[k] = 1;
        return k;
    }

    static string Snap(string label)
    {
        if (!Application.isPlaying) return "not playing";
        var ws = Root(); if (ws == null) return "WorkspaceRoot not found";
        Directory.CreateDirectory(Dir);
        var used = new Dictionary<string, int>();
        var sb = new StringBuilder();
        var kit = One<NearUserKit>();
        int n = 0;
        foreach (var t in Tracked(ws).OrderBy(t => t.name))
        {
            Vector3 lp = ws.InverseTransformPoint(t.position);
            Vector3 le = (Quaternion.Inverse(ws.rotation) * t.rotation).eulerAngles;
            string where = t.IsChildOf(ws) ? "office" : (kit != null && kit.rig != null && t.IsChildOf(kit.rig) ? "deskrig" : "elsewhere");
            bool shown = t.gameObject.activeInHierarchy && t.lossyScale.sqrMagnitude > 1e-4f;
            var rb = t.GetComponent<Rigidbody>();
            sb.Append(Key(t, used)).Append('\t').Append(lp.x.ToString("R", Inv)).Append('\t').Append(lp.y.ToString("R", Inv)).Append('\t').Append(lp.z.ToString("R", Inv))
              .Append('\t').Append(le.x.ToString("F1", Inv)).Append('\t').Append(le.y.ToString("F1", Inv)).Append('\t').Append(le.z.ToString("F1", Inv))
              .Append('\t').Append(where).Append('\t').Append(shown ? "shown" : "hidden").Append('\t').Append(rb != null ? (rb.isKinematic ? "kinematic" : "dynamic") : "-").Append('\n');
            n++;
        }
        File.WriteAllText(Path.Combine(Dir, label + ".tsv"), sb.ToString());
        return $"snapshot '{label}': {n} props, root at {P(ws.position)} yaw {ws.eulerAngles.y:F0}, mode={SpatialWorkspaceState.Mode} transitioning={SpatialWorkspaceState.IsTransitioning}";
    }

    public static string SnapDesk() => Snap("desk");
    public static string SnapFloor() => Snap("floor");
    public static string SnapDocked() => Snap("docked");
    public static string SnapLowered() => Snap("lowered");
    public static string SnapReturned() => Snap("returned");
    public static string SnapClear()
    {
        if (Directory.Exists(Dir)) foreach (var f in Directory.GetFiles(Dir, "*.tsv")) File.Delete(f);
        return "snapshots cleared";
    }

    /// <summary>
    /// Every tracked prop with its pose relative to the office in each snapshot taken so far.
    /// The first column present (desk, if taken) is the reference; a prop that is more than 2 mm
    /// or 1 degree from it in a later snapshot is marked. "deskrig" props are NearUserKit items
    /// that stay at the desk on purpose: their large offsets are expected and marked as such.
    /// </summary>
    public static string PropTable()
    {
        var order = new[] { "desk", "floor", "docked", "lowered", "returned" };
        var labels = order.Where(l => File.Exists(Path.Combine(Dir, l + ".tsv"))).ToList();
        if (labels.Count == 0) return "no snapshots yet: call SnapDesk, SnapDocked, SnapLowered first";
        var data = new Dictionary<string, Dictionary<string, string[]>>();
        var names = new List<string>();
        foreach (var l in labels)
        {
            var map = new Dictionary<string, string[]>();
            foreach (var line in File.ReadAllLines(Path.Combine(Dir, l + ".tsv")))
            {
                var c = line.Split('\t');
                if (c.Length < 10) continue;
                map[c[0]] = c;
                if (!names.Contains(c[0])) names.Add(c[0]);
            }
            data[l] = map;
        }
        var sb = new StringBuilder();
        sb.AppendLine("prop | " + string.Join(" | ", labels.Select(l => l + " (root-local pos; where; shown; body)")) + " | verdict");
        int outliers = 0;
        foreach (var name in names.OrderBy(x => x))
        {
            sb.Append(name);
            Vector3? refPos = null; Vector3 refEuler = Vector3.zero; string verdict = "";
            foreach (var l in labels)
            {
                if (!data[l].TryGetValue(name, out var c)) { sb.Append(" | (absent)"); continue; }
                var pos = new Vector3(float.Parse(c[1], Inv), float.Parse(c[2], Inv), float.Parse(c[3], Inv));
                var eul = new Vector3(float.Parse(c[4], Inv), float.Parse(c[5], Inv), float.Parse(c[6], Inv));
                sb.Append(" | ").Append(P(pos)).Append(' ').Append(c[7]).Append(' ').Append(c[8]).Append(' ').Append(c[9]);
                if (refPos == null) { refPos = pos; refEuler = eul; continue; }
                float d = (pos - refPos.Value).magnitude;
                float a = Quaternion.Angle(Quaternion.Euler(eul), Quaternion.Euler(refEuler));
                if (c[7] == "deskrig") { if (verdict == "") verdict = "stays at the desk (NearUserKit)"; }
                else if (d > 0.002f || a > 1f)
                {
                    verdict = (verdict.Length > 0 && !verdict.StartsWith("stays") ? verdict + "; " : "") + $"OUTLIER in {l}: moved {Mm(d)}, turned {a:F1} deg";
                    outliers++;
                }
            }
            sb.Append(" | ").AppendLine(verdict == "" ? "ok" : verdict);
        }
        sb.AppendLine($"{names.Count} props, {outliers} outlier entr{(outliers == 1 ? "y" : "ies")} (a prop that is rigid with the office reads 'ok' in every column).");
        return sb.ToString();
    }

    // ───────────────────────── rigidbodies and the carrier ─────────────────────────

    /// <summary>Every rigidbody of the office (and of the desk rig): state now, and its distance
    /// from the pose it had in the 'desk' snapshot (root-local), if that snapshot exists.</summary>
    public static string Bodies()
    {
        if (!Application.isPlaying) return "not playing";
        var ws = Root(); if (ws == null) return "WorkspaceRoot not found";
        var rest = new Dictionary<string, Vector3>();
        string deskFile = Path.Combine(Dir, "desk.tsv");
        if (File.Exists(deskFile))
            foreach (var line in File.ReadAllLines(deskFile))
            {
                var c = line.Split('\t');
                if (c.Length >= 4) rest[c[0]] = new Vector3(float.Parse(c[1], Inv), float.Parse(c[2], Inv), float.Parse(c[3], Inv));
            }
        var used = new Dictionary<string, int>();
        var sb = new StringBuilder();
        var carrier = WorkspacePropCarrier.Instance;
        sb.AppendLine($"mode={SpatialWorkspaceState.Mode} transitioning={SpatialWorkspaceState.IsTransitioning} carrier={(carrier != null ? "carrying=" + carrier.Carrying + " pinned=" + carrier.PinnedCount : "MISSING")}");
        foreach (var t in Tracked(ws).OrderBy(t => t.name))
        {
            string key = Key(t, used);
            var rb = t.GetComponent<Rigidbody>();
            if (rb == null) continue;
            Vector3 lp = ws.InverseTransformPoint(t.position);
            var pp = t.GetComponent<PushableProp>();
            sb.Append("  ").Append(key).Append(": local ").Append(P(lp));
            if (rest.TryGetValue(key, out var r)) sb.Append(" offsetFromDeskRest=").Append(Mm((lp - r).magnitude));
            sb.Append(" kinematic=").Append(rb.isKinematic).Append(" interp=").Append(rb.interpolation)
              .Append(" speed=").Append(rb.isKinematic ? "0" : rb.linearVelocity.magnitude.ToString("F3", Inv)).Append(" asleep=").Append(rb.IsSleeping())
              .Append(pp != null ? " PushableProp.enabled=" + pp.enabled : "")
              .Append(t.IsChildOf(ws) ? "" : " [not under the office: left alone]")
              .Append(t.gameObject.activeInHierarchy ? "" : " [inactive]")
              .Append(t.lossyScale.sqrMagnitude < 1e-4f ? " [hidden by scale]" : "").AppendLine();
        }
        return sb.ToString();
    }

    public static string Carrier()
    {
        var c = WorkspacePropCarrier.Instance;
        if (c == null) c = One<WorkspacePropCarrier>();
        return c == null ? "no WorkspacePropCarrier in the scene (run BuildCeilingPolishV1.Apply)" : c.Describe();
    }

    // ───────────────────────── beacon ─────────────────────────

    public static string Beacon()
    {
        if (!Application.isPlaying) return "not playing";
        var b = One<AttentionBeacon>(); if (b == null) return "no AttentionBeacon";
        var ws = Root(); var cam = Camera.main;
        var pod = One<CeilingPodController>();
        var sb = new StringBuilder();
        sb.AppendLine($"mode={SpatialWorkspaceState.Mode} remote={SpatialWorkspaceState.IsRemote} transitioning={SpatialWorkspaceState.IsTransitioning} pod={(pod != null ? pod.State.ToString() : "none")} level={(b.attention != null ? b.attention.Level.ToString() : "?")} pending={(b.attention != null ? b.attention.PendingDecisionCount : -1)} withdrawn={(b.attention != null ? b.attention.WithdrawnDecisions : -1)}");
        sb.AppendLine($"office cue: shown={b.OfficeCueShown} shownLevel={b.ShownLevel} placement={b.Placement} sizer={b.SizerScale:F2} art={b.ArtBeaconShown}");
        if (b.officeCue != null && ws != null)
        {
            Transform c = b.officeCue;
            Vector3 local = ws.InverseTransformPoint(c.position);
            sb.AppendLine($"  world {P(c.position)} up {P(c.up)} tilt from world up {UpAngle(c):F2} deg (expect 0.00); scale {c.lossyScale.x:F3}");
            sb.AppendLine($"  relative to the office root: {P(local)}; distance to the root {Vector3.Distance(c.position, ws.position):F3} m; root at {P(ws.position)}");
            Vector3 under = ws.TransformPoint(new Vector3(b.officeFrontLocal.x, b.podUndersideY, b.podFrontZ));
            Vector3 above = ws.TransformPoint(new Vector3(b.officeFrontLocal.x, b.officeTopHeight, b.officeFrontLocal.y));
            sb.AppendLine($"  pod underside at its front edge {P(under)}: cue is {(under.y - c.position.y):F3} m below it, {new Vector2(c.position.x - under.x, c.position.z - under.z).magnitude:F3} m away horizontally");
            sb.AppendLine($"  office top at its front edge {P(above)}: cue is {(c.position.y - above.y):F3} m above it");
            if (cam != null)
            {
                Vector3 toHead = cam.transform.position - c.position; float dist = toHead.magnitude;
                Vector3 flat = toHead; flat.y = 0f;
                float facing = flat.sqrMagnitude > 1e-6f ? Vector3.Dot(c.rotation * Vector3.back, flat.normalized) : 1f;
                float elev = Mathf.Atan2(-toHead.y, Mathf.Max(1e-4f, flat.magnitude)) * Mathf.Rad2Deg;
                sb.AppendLine($"  head {P(cam.transform.position)}: {dist:F2} m away, cue is {elev:F0} deg above eye level, printed side faces the head (1.00 = exactly) {facing:F2}; office root is {(ws.position.y - cam.transform.position.y):F2} m above the eyes");
            }
            if (b.artBeacon != null) sb.AppendLine($"  model: active={b.artBeacon.gameObject.activeInHierarchy} root tilt {UpAngle(b.artBeacon.transform):F2} deg (its own clip may move bones, never the root)");
        }
        sb.AppendLine($"mirror cue: shown={b.MirrorCueShown}");
        if (b.mirrorCue != null)
        {
            Transform m = b.mirrorCue;
            sb.AppendLine($"  world {P(m.position)} tilt {UpAngle(m):F2} deg (expect 0.00)");
            if (RemoteNearUser.TryGetFrame(out Vector3 origin, out Vector3 right, out Vector3 away))
            {
                Vector3 d = m.position - origin;
                sb.AppendLine($"  from the near-user point {P(origin)}: right {Vector3.Dot(d, right):F3}, up {d.y:F3}, away {Vector3.Dot(d, away):F3} (wanted {P(b.mirrorOffset)})");
            }
            if (cam != null)
            {
                Vector3 flat = cam.transform.position - m.position; flat.y = 0f;
                sb.AppendLine($"  {Vector3.Distance(cam.transform.position, m.position):F2} m from the head; printed side faces the head {(flat.sqrMagnitude > 1e-6f ? Vector3.Dot(m.rotation * Vector3.back, flat.normalized) : 1f):F2}");
            }
            // What is near it: every near-user piece and every desk-rig item.
            var near = new List<(string name, Transform t)>();
            var sc = One<SummonController>(); if (sc != null && sc.keysRoot != null) near.Add(("summon keys", sc.keysRoot));
            var strip = One<SpatialModeStrip>(); if (strip != null) near.Add(("mode strip", strip.transform));
            var perch = One<PerchController>(); if (perch != null && perch.stand != null) near.Add(("perch", perch.stand));
            var mail = One<SpatialMailboxPresenter>(); if (mail != null && mail.mailbox != null) near.Add(("mailbox", mail.mailbox));
            var kit = One<NearUserKit>();
            if (kit != null && kit.items != null) foreach (var it in kit.items) if (it != null) near.Add(("kit:" + it.name, it));
            foreach (var (name, t) in near.OrderBy(x => Vector3.Distance(x.t.position, m.position)))
            {
                float dist = Vector3.Distance(t.position, m.position);
                bool visible = t.gameObject.activeInHierarchy && t.lossyScale.sqrMagnitude > 1e-6f;
                sb.AppendLine($"    {name}: {dist:F3} m{(visible ? "" : " (hidden)")}{(visible && dist < 0.07f ? "  TOO CLOSE: the mirror is on top of it" : "")}");
            }
        }
        return sb.ToString();
    }

    /// <summary>Driver only: put a made-up pending decision into the attention model so the cue
    /// shows "Needs you" in a remote mode without a run. Not a real decision; nothing can be
    /// approved. Remove it with ForceClear.</summary>
    public static string ForceNeedsYou()
    {
        var a = AttentionController.Instance; if (a == null) return "no AttentionController";
        a.Model.DecisionRequested(ForcedId, "researcher");
        return "forced (driver only): " + Beacon();
    }

    /// <summary>Driver only: a made-up failure, to see the "Problem" face.</summary>
    public static string ForceProblem()
    {
        var a = AttentionController.Instance; if (a == null) return "no AttentionController";
        a.Model.TaskFailed("developer");
        return "forced (driver only): " + Beacon();
    }

    public static string ForceClear()
    {
        var a = AttentionController.Instance; if (a == null) return "no AttentionController";
        a.Model.DecisionResolved(ForcedId);
        a.Model.Acknowledge();
        return "cleared: " + Beacon();
    }

    // ───────────────────────── lag probe (sampled every frame, just before rendering) ─────────────────────────

    sealed class Probe
    {
        public Transform ws, cue;
        public Transform[] bodies; public Vector3[] start; public float[] maxDev;
        public Vector3 lastRoot; public float travel; public int frames, movingFrames;
        public Vector3 cueMin, cueMax; public bool cueAny; public float cueMaxTilt, cueMaxStep; public Vector3 cueLast; public bool cueHasLast;
        public float startedAt;
    }
    static Probe _probe;
    static string ProbeFile => Path.Combine(Dir, "probe.txt");
    static string StopFile => Path.Combine(Dir, "probe.stop");

    /// <summary>Start sampling, once per rendered frame after every LateUpdate: how far each loose
    /// body strays from its office-relative pose, and how steady the cue is relative to the
    /// office. Call just before a move; read with ProbeReport after it. Stops by itself after 90 s.</summary>
    public static string ProbeStart()
    {
        if (!Application.isPlaying) return "not playing";
        var ws = Root(); if (ws == null) return "WorkspaceRoot not found";
        if (_probe != null) Application.onBeforeRender -= Sample;
        Directory.CreateDirectory(Dir);
        if (File.Exists(StopFile)) File.Delete(StopFile);
        var bodies = ws.GetComponentsInChildren<Rigidbody>(false).Select(r => r.transform).ToArray();
        var beacon = One<AttentionBeacon>();
        _probe = new Probe
        {
            ws = ws, cue = beacon != null ? beacon.officeCue : null, bodies = bodies,
            start = bodies.Select(t => ws.InverseTransformPoint(t.position)).ToArray(), maxDev = new float[bodies.Length],
            lastRoot = ws.position, startedAt = Time.unscaledTime,
        };
        Application.onBeforeRender += Sample;
        return $"probe started: {bodies.Length} active rigidbodies under the office, cue {(beacon != null ? "tracked" : "not found")}";
    }

    static void Sample()
    {
        var p = _probe;
        if (p == null || p.ws == null || !Application.isPlaying) { Application.onBeforeRender -= Sample; _probe = null; return; }
        p.frames++;
        float step = (p.ws.position - p.lastRoot).magnitude;
        p.lastRoot = p.ws.position; p.travel += step;
        if (step > 1e-5f) p.movingFrames++;
        for (int i = 0; i < p.bodies.Length; i++)
        {
            Transform t = p.bodies[i];
            if (t == null || !t.IsChildOf(p.ws)) continue;
            float d = (p.ws.InverseTransformPoint(t.position) - p.start[i]).magnitude;
            if (d > p.maxDev[i]) p.maxDev[i] = d;
        }
        if (p.cue != null && p.cue.lossyScale.x > 0.5f)          // only while the cue is fully shown
        {
            Vector3 l = p.ws.InverseTransformPoint(p.cue.position);
            if (!p.cueAny) { p.cueMin = p.cueMax = l; p.cueAny = true; }
            else { p.cueMin = Vector3.Min(p.cueMin, l); p.cueMax = Vector3.Max(p.cueMax, l); }
            if (p.cueHasLast && step > 1e-5f) p.cueMaxStep = Mathf.Max(p.cueMaxStep, (l - p.cueLast).magnitude);
            p.cueLast = l; p.cueHasLast = true;
            p.cueMaxTilt = Mathf.Max(p.cueMaxTilt, UpAngle(p.cue));
        }
        else p.cueHasLast = false;
        if (p.frames % 20 == 0)
        {
            File.WriteAllText(ProbeFile, Format(p));
            if (File.Exists(StopFile) || Time.unscaledTime - p.startedAt > 90f) { Application.onBeforeRender -= Sample; _probe = null; }
        }
    }

    static string Format(Probe p)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"probe: {p.frames} frames ({p.movingFrames} with the office moving), office travelled {p.travel:F3} m");
        float worst = 0f;
        for (int i = 0; i < p.bodies.Length; i++)
        {
            if (p.bodies[i] == null) continue;
            worst = Mathf.Max(worst, p.maxDev[i]);
            sb.AppendLine($"  {p.bodies[i].name}: max drift from its office-relative pose {Mm(p.maxDev[i])}{(p.maxDev[i] > 0.002f ? "  LAGGED / MOVED" : "")}");
        }
        sb.AppendLine($"  worst body drift {Mm(worst)} (rigid = under 2 mm; before this pass a trailing prop showed centimetres)");
        if (p.cueAny)
        {
            Vector3 range = p.cueMax - p.cueMin;
            sb.AppendLine($"  cue relative to the office while fully shown: range {P(range)} m, largest frame-to-frame change while the office moved {Mm(p.cueMaxStep)}, max tilt {p.cueMaxTilt:F2} deg");
            sb.AppendLine("    (no lag = a frame-to-frame change of well under the office's own step, about 6 mm per frame at pod speed; a slow change in y is the cue re-sizing for the distance; x follows the concerned station)");
        }
        else sb.AppendLine("  cue was never fully shown during the probe (ForceNeedsYou first, in a remote mode)");
        return sb.ToString();
    }

    public static string ProbeReport()
    {
        if (_probe != null) return Format(_probe);
        return File.Exists(ProbeFile) ? File.ReadAllText(ProbeFile) + "(from the last write, up to 20 frames old)" : "no probe data: call ProbeStart before a move";
    }

    public static string ProbeStop()
    {
        string report = ProbeReport();
        if (_probe != null) { Application.onBeforeRender -= Sample; _probe = null; }
        else { Directory.CreateDirectory(Dir); File.WriteAllText(StopFile, "stop"); }
        return "probe stopped. " + report;
    }

    /// <summary>Everything at once.</summary>
    public static string All()
    {
        return "== carrier ==\n" + Carrier() + "\n== bodies ==\n" + Bodies() + "\n== beacon ==\n" + Beacon() + "\n== probe ==\n" + ProbeReport();
    }
}
