using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;
using MuseDesk.Wall;
using MuseDesk.Workspace;

/// <summary>
/// Play Mode driver for the ONE Spaces dock (AgentScripts/UnifiedModeDockV1) and the start-up reach. Evidence level PLAY
/// (Editor, synthetic room), never on-head. It presses the console keys exactly as a poke would (ModeConsole.PressByName)
/// and injects the gaze for the guided Floor / Ceiling step; it never moves the camera.
///
///   Run      start-up distances of the dock, the task dock (Start key, board) from the head, then
///            Floor -> Place -> Desk; Ceiling -> Place -> Lower -> Raise -> Desk; Wall -> Place -> Desk.
///            Desk placement can take 15-40 s to settle in the Editor: the first step waits for it, and every
///            "open the preview" step presses again every few seconds until the preview is open.
///   Status   the report so far (call it repeatedly; the run takes one to three minutes)
///   Stop     abandon the run
///   NowReport    a one-shot report (distances, console keys, reach) without running anything
///   ForceReseat  exercise the start-up re-seat of the near-user rig now
/// Every 'script run' compiles this file afresh, so the running instance writes its report to Library/UnifiedDockDrive.txt.
/// </summary>
public static class UnifiedDockDrive
{
    sealed class Step
    {
        public string name;
        public Func<string> begin;
        public Func<bool> done;
        public float timeout;
        public Func<string> detail;
        public bool softFail;
    }

    const string ReportFile = "Library/UnifiedDockDrive.txt";
    const string StopFile = "Library/UnifiedDockDrive.stop";

    static readonly List<Step> _steps = new List<Step>();
    static readonly List<string> _log = new List<string>();
    static readonly List<string> _errors = new List<string>();
    static readonly List<string> _reached = new List<string>();
    static int _i = -1, _failures, _soft, _errorCount;
    static float _stepT0, _runT0, _lastWrite, _lastPress;
    static bool _running, _begun;

    static T One<T>() where T : UnityEngine.Object => UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static ModeConsole Con => One<ModeConsole>();
    static SpatialWorkspaceDirector Dir => One<SpatialWorkspaceDirector>();
    static WallHabitatController WallC => One<WallHabitatController>();
    static PlacementGuide Guide => One<PlacementGuide>();
    static float Now => Time.realtimeSinceStartup;

    // ───────────────────────── entry points ─────────────────────────

    public static string Run()
    {
        if (!Application.isPlaying) return "not playing: enter Play Mode first";
        if (System.IO.File.Exists(ReportFile) && (DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(ReportFile)).TotalSeconds < 5
            && System.IO.File.ReadAllText(ReportFile).Contains("\nRUNNING"))
            return "a run is already in progress: " + Status();
        if (System.IO.File.Exists(StopFile)) System.IO.File.Delete(StopFile);
        if (Con == null || Dir == null) return "no ModeConsole or SpatialWorkspaceDirector in the scene";
        _steps.Clear(); _log.Clear(); _errors.Clear(); _reached.Clear();
        _i = -1; _failures = _soft = _errorCount = 0;
        Build();
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        UnityEditor.EditorApplication.update -= Tick;
        UnityEditor.EditorApplication.update += Tick;
        _running = true; _begun = false;
        _runT0 = Now;
        return "UnifiedDockDrive started, " + _steps.Count + " steps. Call UnifiedDockDrive.Status for the report.";
    }

    public static string Status()
    {
        if (_steps.Count > 0) return Report();
        return System.IO.File.Exists(ReportFile) ? System.IO.File.ReadAllText(ReportFile) : "no run yet";
    }

    public static string Stop()
    {
        if (_running) { End("stopped by the driver"); return Report(); }
        System.IO.File.WriteAllText(StopFile, "stop");
        return "stop requested; call Status in a moment";
    }

    /// <summary>One-shot: distances, the console's keys and the start-up reach, now.</summary>
    public static string NowReport() { return Snapshot(); }

    /// <summary>Exercise the start-up re-seat now (the head must be usable; in the Editor the camera may sit at the origin).</summary>
    public static string ForceReseat()
    {
        var kit = One<NearUserKit>();
        if (kit == null) return "no NearUserKit";
        bool ok = kit.ForceStartupReseat();
        return (ok ? "re-seated" : "refused (head not usable or a move in flight)") + "\n  " + Distances();
    }

    // ───────────────────────── measurements ─────────────────────────

    static Transform Head() { var c = Camera.main; return c != null ? c.transform : null; }

    static string Dist(string what, Transform t, Transform head)
    {
        if (t == null) return what + "=missing";
        if (head == null) return what + "=no head";
        Vector3 d = t.position - head.position;
        Vector3 f = head.forward; f.y = 0f; f = f.sqrMagnitude < 1e-6f ? Vector3.forward : f.normalized;
        float ahead = Vector3.Dot(d, f);
        string flag = d.magnitude > StartupReachRule.MaxReach ? " OUT-OF-REACH" : d.magnitude > 0.55f ? " (beyond 0.55)" : "";
        return what + "=" + d.magnitude.ToString("F2") + "m (ahead " + ahead.ToString("F2") + ", below eye " + (-d.y).ToString("F2") + ")" + flag;
    }

    static string Distances()
    {
        Transform head = Head();
        var sb = new StringBuilder();
        sb.Append("head ").Append(head != null ? head.position.ToString("F2") : "none");
        var c = Con;
        if (c != null)
        {
            sb.Append(" | ").Append(Dist("dock.Desk", c.deskKey.root, head));
            sb.Append(" | ").Append(Dist("dock.Floor", c.floorKey.root, head));
            sb.Append(" | ").Append(Dist("dock.Ceiling", c.ceilingKey.root, head));
            sb.Append(" | ").Append(Dist("dock.Wall", c.wallKey != null ? c.wallKey.root : null, head));
        }
        var td = One<MuseDesk.Phase2.Board.TaskDock>();
        if (td != null)
        {
            sb.Append(" | ").Append(Dist("taskDock", td.transform, head));
            sb.Append(" | ").Append(Dist("Start", td.startKey != null ? td.startKey.transform : null, head));
            sb.Append(" | ").Append(Dist("board", td.board != null ? td.board.transform : null, head));
        }
        else sb.Append(" | taskDock=missing");
        var kit = One<NearUserKit>();
        if (kit != null) sb.Append("\n    ").Append(kit.DescribeReach());
        return sb.ToString();
    }

    static string Keys()
    {
        var c = Con;
        if (c == null) return "no console";
        var sb = new StringBuilder();
        sb.Append("active=").Append(c.ActiveSegment).Append(" status='").Append(c.StatusShown).Append("' shown:");
        foreach (string line in c.Describe().Split('\n'))
        {
            string l = line.Trim();
            int colon = l.IndexOf(':');
            if (colon < 0 || l.StartsWith("ModeConsole")) continue;
            string rest = l.Substring(colon + 1);
            if (!rest.Contains("shown") || rest.Contains("hidden")) continue;
            int q = rest.IndexOf('\'');
            string words = q >= 0 ? rest.Substring(q).Trim() : "";
            sb.Append(' ').Append(l.Substring(0, colon)).Append(rest.Contains("disabled") ? "(off)" : "").Append(words);
        }
        var h = c.WallHost;
        if (h != null) sb.Append(" | wall enabled=").Append(h.WallEnabled).Append(" active=").Append(h.WallActive).Append(" previewing=").Append(h.WallPreviewing)
                         .Append(" canPlace=").Append(h.WallCanPlace).Append(" externalDock=").Append(h.ExternalDock);
        var wc = WallC;
        if (wc != null && wc.nearUserRig != null) sb.Append(" | WallDock object ").Append(wc.nearUserRig.Find("WallDock") != null ? (wc.nearUserRig.Find("WallDock").gameObject.activeSelf ? "SHOWN (second dock!)" : "exists, hidden") : "none");
        return sb.ToString();
    }

    static string ModeLine()
    {
        var d = Dir;
        if (d == null || d.Core == null) return "no director";
        return "mode=" + d.Mode + " previewing=" + d.IsPreviewing + (d.IsPreviewing ? "(" + d.Core.PendingMode + ")" : "")
             + " transitioning=" + d.Core.IsTransitioning + " pod=" + d.Core.Pod.State + " canPlace=" + d.Core.CanPlaceHere
             + " status='" + d.StatusText + "'";
    }

    static string Snapshot()
    {
        if (!Application.isPlaying) return "not playing";
        return "NOW " + ModeLine() + "\n  " + Keys() + "\n  " + Distances();
    }

    // ───────────────────────── the script ─────────────────────────

    static void Add(string name, Func<string> begin, Func<bool> done, float timeout, Func<string> detail = null, bool softFail = false)
    {
        _steps.Add(new Step { name = name, begin = begin, done = done, timeout = timeout, detail = detail, softFail = softFail });
    }

    static bool Press(string key) { _lastPress = Now; return Con.PressByName(key); }
    static bool Every(float seconds) { return Now - _lastPress > seconds; }
    static bool DeskPlaced()
    {
        var p = One<WorkspacePlacement>();
        return p == null || p.State == WorkspacePlacement.PlacementState.PlacedManual || p.State == WorkspacePlacement.PlacementState.PlacedFromTable;
    }

    static Vector3 Gaze(float pitchDeg)
    {
        Transform h = Head();
        Vector3 f = h != null ? h.forward : Vector3.forward; f.y = 0f;
        f = f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
        float r = pitchDeg * Mathf.Deg2Rad;
        return f * Mathf.Cos(r) + Vector3.up * Mathf.Sin(r);
    }

    static bool AtRest(WorkspaceMode m)
    {
        var d = Dir;
        return d.Mode == m && !d.IsPreviewing && !d.Core.IsTransitioning;
    }

    static void OpenPreview(string key, PlacementMode pm, float pitch)
    {
        string name = "open the " + key + " preview (guided look, presses again until it opens)";
        Add(name, () => { var g = Guide; if (g != null) g.InjectGaze(Gaze(pitch)); Press(key); return null; },
            () =>
            {
                var d = Dir;
                if (d.IsPreviewing && d.Core.PendingMode == pm) return true;
                var g = Guide;
                bool guiding = g != null && g.Active;
                if (!guiding && Every(4f)) { if (g != null) g.InjectGaze(Gaze(pitch)); Press(key); }
                return false;
            }, 90f, () => ModeLine() + " | " + Keys());
    }

    static void PlaceAndArrive(string label, WorkspaceMode m)
    {
        Add("Place (" + label + ") and arrive", () => { Press("Place"); return null; },
            () =>
            {
                var d = Dir;
                if (AtRest(m) && (m != WorkspaceMode.Ceiling || d.Core.Pod.State == CeilingPodState.Docked)) { _reached.Add(m.ToString()); return true; }
                if (d.IsPreviewing && !d.Core.IsTransitioning && Every(3f))
                {
                    // Place is shown only while the spot and its way are clear: otherwise try the next spot.
                    if (Con.Describe().Contains("Confirm: shown") && !Con.Describe().Contains("Confirm: shown disabled")) Press("Place");
                    else Press("Next");
                }
                return false;
            }, 90f, () => ModeLine() + " | " + Keys());
    }

    static void BackToDesk(string from)
    {
        Add("Desk (from " + from + ")", () => { Press("Desk"); return null; },
            () =>
            {
                var d = Dir; var h = Con.WallHost;
                bool wallOff = h == null || (!h.WallActive && !h.WallPreviewing);
                if (AtRest(WorkspaceMode.Desk) && wallOff) { _reached.Add("Desk"); return true; }
                if (!d.Core.IsTransitioning && Every(5f)) Press("Desk");
                return false;
            }, 60f, () => ModeLine() + " | " + Keys());
    }

    static void Build()
    {
        Add("wait for the desk placement and the near-user rig (Editor: 15-40 s)", null,
            () => DeskPlaced() && Con.gameObject.activeInHierarchy && Head() != null, 90f, () => ModeLine());
        Add("start-up reach: distances from the head", null, () => true, 5f, () => Distances());
        Add("start-up reach: every near-user key within 0.8 m", null,
            () => !Distances().Contains("OUT-OF-REACH"), 3f, () => Distances(), softFail: true);
        Add("flags on (driver only), synthetic room, one dock", () =>
            {
                var d = Dir;
                d.SetFlags(true, true);
                d.UseSyntheticTestRoom();
                var w = WallC;
                if (w != null && !w.WallEnabled)
                    w.SetFlags(new WallFeatureFlags { wallHabitatEnabled = true, wallManagerEnabled = true, agentSelectEnabled = true });
                return null;
            },
            () => Con.HasWallKey && Con.WallHost != null && Con.WallHost.ExternalDock && Con.WallHost.WallEnabled, 5f, () => Keys());

        // Floor -> Place -> Desk
        OpenPreview("Floor", PlacementMode.Floor, -60f);
        PlaceAndArrive("floor", WorkspaceMode.Floor);
        BackToDesk("Floor");

        // Ceiling -> Place -> Lower -> Raise -> Desk
        OpenPreview("Ceiling", PlacementMode.Ceiling, 60f);
        PlaceAndArrive("ceiling", WorkspaceMode.Ceiling);
        Add("Lower (pod down beside the user)", () => { Press("Pod"); return null; },
            () =>
            {
                var d = Dir;
                if (d.Core.Pod.State == CeilingPodState.Engaged) { _reached.Add("Ceiling-lowered"); return true; }
                if (d.Core.Pod.State == CeilingPodState.Docked && Every(5f)) Press("Pod");
                return false;
            }, 60f, () => ModeLine() + " | " + Keys());
        Add("Raise (pod back to the dock)", () => { Press("Pod"); return null; },
            () =>
            {
                var d = Dir;
                if (d.Core.Pod.State == CeilingPodState.Docked && !d.Core.IsTransitioning) { _reached.Add("Ceiling-raised"); return true; }
                if (d.Core.Pod.State == CeilingPodState.Engaged && Every(5f)) Press("Pod");
                return false;
            }, 60f, () => ModeLine() + " | " + Keys());
        BackToDesk("Ceiling");

        // Wall -> Place -> Desk (the shared Place / Next spot / Cancel keys act on the wall preview)
        Add("Wall: open the wall preview", () => { var g = Guide; if (g != null) g.InjectGaze(null); Press("Wall"); return null; },
            () =>
            {
                var h = Con.WallHost;
                if (h != null && h.WallPreviewing) return true;
                if (Every(4f)) Press("Wall");
                return false;
            }, 60f, () => ModeLine() + " | " + Keys());
        Add("Place (wall) and arrive", () => { Press("Place"); return null; },
            () =>
            {
                var h = Con.WallHost;
                if (h != null && h.WallActive && !h.WallPreviewing) { _reached.Add("Wall"); return true; }
                if (h != null && h.WallPreviewing && Every(3f)) { if (h.WallCanPlace) Press("Place"); else Press("Next"); }
                return false;
            }, 60f, () => ModeLine() + " | " + Keys());
        Add("Wall at rest: Move / Select / Manager on the same dock", null,
            () => { string k = Keys(); return k.Contains("Move") && k.Contains("Desk"); }, 5f, () => Keys(), softFail: true);
        BackToDesk("Wall");
        Add("final distances", null, () => true, 5f, () => Distances());
    }

    // ───────────────────────── runner ─────────────────────────

    static void OnLog(string condition, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        _errorCount++;
        if (_errors.Count < 8) _errors.Add(type + ": " + (condition.Length > 180 ? condition.Substring(0, 180) : condition));
    }

    static void WriteReport() { try { System.IO.File.WriteAllText(ReportFile, Report()); } catch (Exception) { } }

    static void End(string why)
    {
        if (!_running) return;
        _running = false;
        UnityEditor.EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        var g = Application.isPlaying ? Guide : null;
        if (g != null) g.InjectGaze(null);
        _log.Add("== run ended: " + why + " after " + (Now - _runT0).ToString("F1") + " s");
        WriteReport();
    }

    static void Tick()
    {
        if (!_running) return;
        if (!Application.isPlaying) { End("Play Mode ended"); return; }
        if (System.IO.File.Exists(StopFile)) { System.IO.File.Delete(StopFile); End("stopped by the driver"); return; }
        if (Now - _lastWrite > 1f) { _lastWrite = Now; WriteReport(); }
        if (_i < 0) { _i = 0; _begun = false; }
        if (_i >= _steps.Count) { End(_failures == 0 ? "all steps done" : "finished with failures"); return; }
        Step s = _steps[_i];
        try
        {
            if (!_begun)
            {
                _stepT0 = Now;
                string why = s.begin != null ? s.begin() : null;
                if (why != null) { Fail(s, "could not start: " + why); return; }
                _begun = true;
            }
            if (s.done())
            {
                _log.Add("OK   " + s.name + "  [" + (Now - _stepT0).ToString("F1") + " s]" + (s.detail != null ? "  " + SafeDetail(s) : ""));
                _i++; _begun = false;
                return;
            }
            if (Now - _stepT0 > s.timeout) Fail(s, "timed out after " + s.timeout.ToString("F0") + " s | " + (s.detail != null ? SafeDetail(s) : ""));
        }
        catch (Exception ex) { Fail(s, "exception: " + ex.Message); }
    }

    static string SafeDetail(Step s) { try { return s.detail(); } catch (Exception ex) { return "(detail failed: " + ex.Message + ")"; } }

    static void Fail(Step s, string why)
    {
        if (s.softFail) { _soft++; _log.Add("MISS " + s.name + "  " + why + "  (run continues)"); _i++; _begun = false; return; }
        _failures++;
        _log.Add("FAIL " + s.name + "  " + why);
        End("stopped at a failed step");
    }

    static string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("UNIFIED DOCK DRIVE — evidence level PLAY (Editor, synthetic room), not on-head");
        sb.AppendLine(_running ? "RUNNING: step " + (_i + 1) + " of " + _steps.Count + (_i >= 0 && _i < _steps.Count ? " (" + _steps[_i].name + ", " + (Now - _stepT0).ToString("F1") + " s)" : "")
                               : (_steps.Count == 0 ? "not started" : "FINISHED"));
        foreach (string l in _log) sb.AppendLine(l);
        sb.AppendLine("modes reached: " + (_reached.Count == 0 ? "none yet" : string.Join(" -> ", _reached)));
        sb.AppendLine("steps failed=" + _failures + " missed(optional)=" + _soft + " console errors=" + _errorCount);
        foreach (string e in _errors) sb.AppendLine("  " + e);
        if (Application.isPlaying) sb.AppendLine(Snapshot());
        return sb.ToString();
    }
}
