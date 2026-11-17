using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;
using MuseDesk.Wall;

/// <summary>
/// Play Mode driver for the Select Agents force grab (AgentSelectGrabV1). Synthetic room, offline backend, SIMULATED hands.
/// Evidence level: PLAY (Editor). Real-hand behaviour (the ISDK rig's hand rays, pinch strength, the hidden-office mute
/// against the rig's real interactors) is WORN-only and is NOT shown by this run.
///
/// The hands go through WallHabitatController.SimulateHand, which feeds the SAME per-frame path a real hand does
/// (HandOwnershipArbiter -> hover lock -> AgentSelect claim -> carry -> seen-open release); only ReadHand and the
/// HandInteractionProbe reading are replaced. Every frame it checks: at most one desk projection, at most one agent away.
///
///   Run      enter Wall offline, arm Select, point (open hand), pinch with the ray dipping 3 deg off the agent (the lock
///            must keep it), pull it to the perch and let go, ask one /btw question, pick it up off the perch and let go
///            with the arm stretched toward the wall (put back), then a second visit ended with the Return key, then a
///            pinch while "holding a paper" (must be refused out loud), and the canonical task state must be unchanged.
///   Status   the report so far (call repeatedly; ~40 s)
///   Stop     abandon the run
/// Run with: scripts\drv.ps1 AgentSelectGrabDrive Run   then   scripts\drv.ps1 AgentSelectGrabDrive Status
/// </summary>
public static class AgentSelectGrabDrive
{
    sealed class Step
    {
        public string name;
        public Func<string> begin;
        public Func<bool> done;
        public float timeout;
        public Func<string> detail;
    }

    static readonly List<Step> _steps = new List<Step>();
    static readonly List<string> _log = new List<string>();
    static readonly List<string> _errors = new List<string>();
    static int _i = -1, _failures, _errorCount, _frames, _maxProjections, _maxAway, _ghostFrames;
    static float _stepT0, _runT0, _lastWrite;
    static bool _running, _begun;
    static string _tasksBefore = "";
    static int _refusalsBefore;

    const string ReportFile = "Library/AgentSelectGrabDrive.txt";
    const string StopFile = "Library/AgentSelectGrabDrive.stop";

    static T One<T>() where T : UnityEngine.Object => UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static WallHabitatController C => One<WallHabitatController>();
    static float Now => Time.realtimeSinceStartup;
    static Vector3 Head() { var cam = Camera.main; return cam != null ? cam.transform.position : new Vector3(0, 1.2f, 0); }
    static Vector3 HandOrigin() => Head() + Vector3.down * 0.25f + Vector3.right * 0.15f;

    public static string Run()
    {
        if (!Application.isPlaying) return "not playing: enter Play Mode first";
        var c = C;
        if (c == null) return "no WallHabitatController in the scene";
        var g = One<WallBackendGateway>();
        if (g != null && g.IsLive) return "the session is live: this driver runs offline only (leave the backend off)";
        if (System.IO.File.Exists(StopFile)) System.IO.File.Delete(StopFile);
        _steps.Clear(); _log.Clear(); _errors.Clear();
        _i = -1; _failures = _errorCount = _frames = _maxProjections = _maxAway = _ghostFrames = 0;
        _tasksBefore = ""; _refusalsBefore = 0;
        Build();
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        UnityEditor.EditorApplication.update -= Tick;
        UnityEditor.EditorApplication.update += Tick;
        _running = true; _begun = false;
        _runT0 = Now;
        return "AgentSelectGrabDrive started, " + _steps.Count + " steps (PLAY, simulated hands). Call AgentSelectGrabDrive.Status.";
    }

    public static string Stop()
    {
        if (_running) { End("stopped by the driver"); return Report(); }
        System.IO.File.WriteAllText(StopFile, "stop");
        return "stop requested; call Status in a moment";
    }

    public static string Status()
    {
        if (_steps.Count > 0) return Report();
        return System.IO.File.Exists(ReportFile) ? System.IO.File.ReadAllText(ReportFile) : "no run yet";
    }

    static void OnLog(string condition, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        _errorCount++;
        if (_errors.Count < 6) _errors.Add(type + ": " + (condition.Length > 160 ? condition.Substring(0, 160) : condition));
    }

    static void End(string why)
    {
        if (!_running) return;
        _running = false;
        UnityEditor.EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        var c = C;
        if (c != null) c.StopSimulating();
        _log.Add("== run ended: " + why + " after " + (Now - _runT0).ToString("F1") + " s");
        try { System.IO.File.WriteAllText(ReportFile, Report()); } catch (Exception) { }
    }

    static void Add(string name, Func<string> begin, Func<bool> done, float timeout, Func<string> detail = null)
    {
        _steps.Add(new Step { name = name, begin = begin, done = done, timeout = timeout, detail = detail });
    }

    // ── simulated hand helpers (right hand = 1) ──

    static bool Aim(HabitatRole r, bool pinch, float dipDegrees = 0f)
    {
        Vector3 at;
        if (!C.TryGetAvatarCenter(r, out at)) return false;
        Vector3 o = HandOrigin(), d = (at - o).normalized;
        if (dipDegrees != 0f) d = Quaternion.AngleAxis(dipDegrees, Vector3.Cross(d, Vector3.up).normalized) * d;
        C.SimulateHand(1, o, d, pinch);
        return true;
    }

    static void HoldToPerch()
    {
        Vector3 perch = C.PerchWorld + Vector3.up * 0.02f;
        Vector3 o = perch - (perch - Head()).normalized * 0.14f;     // the carry point is 0.14 m along the ray
        C.SimulateHand(1, o, perch - o, true);
    }

    static Vector3 HomeOf(HabitatRole r) { Vector3 at; return C.TryGetAvatarCenter(r, out at) ? at : Head() + Vector3.forward * 2f; }

    static void HoldStretchedToWall(HabitatRole r)
    {
        Vector3 d = (HomeOf(r) - Head()).normalized;
        Vector3 o = Head() + d * 0.62f;                               // arm out; the carry point lands ~0.76 m from the eye
        C.SimulateHand(1, o, d, true);
    }

    static void OnProxy(bool pinch)
    {
        Vector3 p = C.ProxyPosition, h = Head();
        Vector3 o = p + Vector3.up * 0.10f + (h - p).normalized * 0.06f;
        Vector3 center = p + Vector3.up * 0.11f;
        C.SimulateHand(1, o, center - o, pinch);
    }

    static string Tasks()
    {
        var bridge = One<WallEventBridge>();
        if (bridge == null) return "no WallEventBridge";
        var sb = new StringBuilder();
        foreach (var a in new[] { "researcher", "developer", "tester", "manager" })
        {
            string id, title, state; int rev;
            sb.Append(a + "=" + (bridge.TryGetAgentTask(a, out id, out title, out state, out rev) ? id + "/" + state + "/r" + rev : "none") + "; ");
        }
        return sb.ToString();
    }

    // phase clock inside a step
    static float Since => Now - _stepT0;

    static void Build()
    {
        Add("1 flags on (driver only) + synthetic room, offline", () =>
        {
            C.SetFlags(new WallFeatureFlags { wallHabitatEnabled = true, wallManagerEnabled = true, agentSelectEnabled = true, agentBtwEnabled = true, largeAgentPortalEnabled = true, wallElevatorVfxEnabled = false });
            var d = One<SpatialWorkspaceDirector>();
            if (d == null) return "no SpatialWorkspaceDirector";
            d.UseSyntheticTestRoom();
            C.useSelectGrabV2 = true;
            C.StopSimulating();
            return null;
        }, () => C.WallEnabled, 5f, () => "useSelectGrabV2=" + C.useSelectGrabV2 + " | " + C.DescribeHands());

        Add("2 enter Wall and place the habitat", () => { if (!C.WallActive) C.PressWall(); return null; },
            () =>
            {
                if (C.WallActive && !C.WallPreviewing) return true;
                if (C.Flow != null && C.Flow.HasShown && C.Flow.Shown.valid) C.PressPlaceHere();
                return false;
            }, 40f, () => "placed on " + (C.Flow.HasPlaced ? C.Flow.Placed.wallId : "?"));

        Add("3 the hidden desk office's interactables are switched off while Wall is active", null,
            () => C.InteractableMute != null && C.InteractableMute.OfficeActive && Since > 1.2f, 6f,
            () => "office interactables muted=" + C.InteractableMute.OfficeMuted + " (0 means none exist under the office root in this scene)");

        Add("4 arm Select agents; open hand pointing at the Developer highlights it", () =>
        {
            _tasksBefore = Tasks();
            if (C.Visit.IsArmed) return null;
            C.PressSelectAgents();
            return C.Visit.IsArmed ? null : "Select refused: " + C.StatusLine;
        }, () =>
        {
            Aim(HabitatRole.Developer, false);
            return Since > 0.6f && C.HoverRole == (int)HabitatRole.Developer;
        }, 4f, () => "hover=" + (HabitatRole)C.HoverRole + " distance grabs muted=" + C.InteractableMute.DistanceMuted);

        Add("5 pinch on the Developer, the ray dips 6 deg (off the target cone) as the fingers close: the pinch keeps its target, the carry starts", null,
            () =>
            {
                Aim(HabitatRole.Developer, true, Since > 0.04f ? 6f : 0f);
                return C.Visit.State == VisitState.Carrying && C.Visit.Agent == "developer";
            }, 3f, () => C.DescribeSelectGrab());

        Add("6 pull it to the perch and let go: it lands on the pad", null,
            () =>
            {
                if (Since < 1.2f) { HoldToPerch(); return false; }
                if (C.Visit.State == VisitState.Carrying) { C.SimulateRelease(1); return false; }
                return C.Visit.State == VisitState.Perched && C.HasProxy && (C.ProxyPosition - C.PerchWorld).magnitude < 0.01f;
            }, 6f, () => "proxy->perch d=" + (C.ProxyPosition - C.PerchWorld).magnitude.ToString("F3") + " | " + C.DescribeSelectGrab());

        Add("7 ask one /btw question (read-only)", () => C.Ask(0) ? null : "Ask refused (side session " + (C.Side != null) + ")",
            () => C.Side != null && C.Side.History.Count > 0, 15f,
            () => { var x = C.Side.History[C.Side.History.Count - 1]; return "provenance=" + x.provenance + " status=" + x.status + " label='" + x.label + "' reply='" + Trim(x.reply, 80) + "'"; });

        Add("8 pick it up off the perch (pinch on it)", () => { C.StopSimulating(); return null; },
            () =>
            {
                if (Since < 0.5f) { OnProxy(false); return false; }          // seen open first (a fresh pinch)
                OnProxy(true);
                return C.Visit.State == VisitState.Carrying && C.Visit.IsRegrab;
            }, 3f, () => C.DescribeSelectGrab());

        Add("9 stretch the arm toward the wall and let go: it goes back to work on the wall", null,
            () =>
            {
                if (Since < 1.0f) { HoldStretchedToWall(HabitatRole.Developer); return false; }
                if (C.Visit.State == VisitState.Carrying) { C.SimulateRelease(1); return false; }
                return C.Visit.State == VisitState.Off && C.Presentations.AwayCount == 0 && C.ProjectionCount == 0 && !C.HasProxy;
            }, 6f, () => "lastEnd=" + C.Visit.LastEnd + " | " + C.DescribeSelectGrab());

        Add("10 second visit (Tester): grab, land, then the Return key", () =>
        {
            C.StopSimulating();
            if (!C.Visit.IsArmed) C.PressSelectAgents();
            return C.Visit.IsArmed ? null : "Select refused: " + C.StatusLine;
        }, () =>
        {
            if (Since < 0.5f) { Aim(HabitatRole.Tester, false); return false; }
            if (C.Visit.State == VisitState.Armed) { Aim(HabitatRole.Tester, true); return false; }
            if (C.Visit.State == VisitState.Carrying && Since < 2.5f) { HoldToPerch(); return false; }
            if (C.Visit.State == VisitState.Carrying) { C.SimulateRelease(1); return false; }
            if (C.Visit.State == VisitState.Perched) { C.StopSimulating(); C.PressReturn(); return false; }
            return C.Visit.State == VisitState.Off && C.Presentations.AwayCount == 0 && C.ProjectionCount == 0;
        }, 10f, () => "lastEnd=" + C.Visit.LastEnd + " | " + C.DescribeSelectGrab());

        Add("11 a pinch on an agent while holding a paper is refused out loud (nothing is carried)", () =>
        {
            C.StopSimulating();
            if (!C.Visit.IsArmed) C.PressSelectAgents();
            _refusalsBefore = C.SelectRefusals;
            return C.Visit.IsArmed ? null : "Select refused: " + C.StatusLine;
        }, () =>
        {
            Vector3 at; C.TryGetAvatarCenter(HabitatRole.Researcher, out at);
            Vector3 o = HandOrigin();
            if (Since < 0.5f) { C.SimulateHand(1, o, at - o, false, false); return false; }
            C.SimulateHand(1, o, at - o, true, Since > 0.52f);                 // the paper is picked up as the pinch closes
            return Since > 1.2f && C.Visit.State == VisitState.Armed && C.SelectRefusals > _refusalsBefore && C.Presentations.AwayCount == 0;
        }, 4f, () => "refusal='" + C.LastSelectRefusal + "' visit=" + C.Visit.State);

        Add("12 clean up: Select off, hands real; the canonical task state is unchanged", () =>
        {
            C.StopSimulating();
            if (C.Visit.IsArmed) C.PressSelectAgents();
            return null;
        }, () => C.Visit.State == VisitState.Off && Since > 0.3f, 3f,
            () => { string after = Tasks(); return (after == _tasksBefore ? "UNCHANGED " : "CHANGED (a scripted offline board may advance on its own; compare by task id) ") + "before: " + _tasksBefore + " after: " + after; });
    }

    static string Trim(string s, int n) { if (string.IsNullOrEmpty(s)) return ""; s = s.Replace("\n", " / "); return s.Length <= n ? s : s.Substring(0, n) + "..."; }

    static void Tick()
    {
        if (!_running) return;
        if (!Application.isPlaying) { End("Play Mode ended"); return; }
        if (System.IO.File.Exists(StopFile)) { System.IO.File.Delete(StopFile); End("stopped by the driver"); return; }
        if (Now - _lastWrite > 1f) { _lastWrite = Now; try { System.IO.File.WriteAllText(ReportFile, Report()); } catch (Exception) { } }
        var c = C;
        if (c == null) { End("the controller is gone"); return; }
        if (c.WallActive && c.Presentations != null)
        {
            _frames++;
            _maxProjections = Mathf.Max(_maxProjections, c.ProjectionCount);
            _maxAway = Mathf.Max(_maxAway, c.Presentations.AwayCount);
            if (c.Visit.State == VisitState.Off && c.Presentations.AwayCount > 0) _ghostFrames++;
        }

        if (_i < 0) { _i = 0; _begun = false; }
        if (_i >= _steps.Count) { End(_failures == 0 && _maxProjections <= 1 && _maxAway <= 1 ? "all steps done" : "finished with failures"); return; }
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
            if (Now - _stepT0 > s.timeout) Fail(s, "timed out after " + s.timeout.ToString("F0") + " s" + (s.detail != null ? " | " + SafeDetail(s) : ""));
        }
        catch (Exception ex) { Fail(s, "exception: " + ex.Message); }
    }

    static string SafeDetail(Step s) { try { return s.detail(); } catch (Exception ex) { return "(detail failed: " + ex.Message + ")"; } }

    static void Fail(Step s, string why)
    {
        _failures++;
        _log.Add("FAIL " + s.name + "  " + why);
        End("stopped at a failed step");
    }

    static string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("AGENT SELECT GRAB DRIVE — offline, simulated hands — evidence PLAY (Editor). Real hands: WORN-only, not shown here.");
        sb.AppendLine(_running ? "RUNNING: step " + (_i + 1) + " of " + _steps.Count : (_steps.Count == 0 ? "not started" : "FINISHED"));
        foreach (string l in _log) sb.AppendLine(l);
        sb.AppendLine("observed over " + _frames + " frames: max desk projections=" + _maxProjections + " (must be <= 1), max agents away=" + _maxAway
                      + " (must be <= 1), frames with an agent away but no visit=" + _ghostFrames);
        sb.AppendLine("steps failed=" + _failures + " console errors=" + _errorCount);
        foreach (string e in _errors) sb.AppendLine("  " + e);
        var c = Application.isPlaying ? C : null;
        if (c != null) sb.AppendLine("now: " + c.DescribeSelectGrab());
        return sb.ToString();
    }
}
