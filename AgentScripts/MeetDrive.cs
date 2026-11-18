using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;
using MuseDesk.Voice;
using MuseDesk.Wall;

/// <summary>
/// Play Mode driver for Meet (MeetKeyV1 + WallHabitatController.Meet.cs): the large-agent portal in one poke, and by voice.
/// Synthetic room, offline backend, NO hands needed (that is the point: the portal no longer depends on a hand carry).
/// Evidence level: PLAY (Editor). Real pokes on the dock, the real microphone and the headset are NOT shown by this run.
///
///   Run      flags on (driver only) + synthetic room; enter Wall and place; the status line names Meet; the dock shows
///            Console_Meet; press Meet on the dock (ModeConsole.PressByName, the poke's own method); the chooser lists
///            Researcher / Developer / Tester / Manager / Cancel; choose Researcher -> portal preview; Open here on the card;
///            the avatar is out (Conversing) at a spot the keep-out guard re-checked as clear (or the small-visitor fallback,
///            reported as such); Send back -> home, with exactly ONE leave and ONE return in the presentation registry, one
///            mirror close, the habitat's "on visit" marker shown and hidden once; then voice: InjectAmbientFinal("Tester,
///            come here") -> preview for the Tester -> Cancel -> home once; "meet the developer" (hands-free, name not first)
///            -> preview -> Cancel; Meet then the chooser's Cancel leaves nothing away; the canonical task state is unchanged.
///   Status   the report so far (call repeatedly; ~40 s). It is also written to Library/MeetDrive.txt.
///   Stop     abandon the run
/// No state is kept between calls: each script run compiles a fresh assembly. Run() drives itself from EditorApplication.update
/// inside its own assembly and writes the report file every second; Status() and Stop() talk to it through files only.
/// Run with: scripts\drv.ps1 MeetDrive Run   then   scripts\drv.ps1 MeetDrive Status
/// </summary>
public static class MeetDrive
{
    sealed class Step
    {
        public string name;
        public Func<string> begin;
        public Func<bool> done;
        public float timeout;
        public Func<string> detail;
    }

    // Lives only inside the assembly that Run() compiled (its update loop); other calls read the files below.
    static readonly List<Step> _steps = new List<Step>();
    static readonly List<string> _log = new List<string>();
    static readonly List<string> _errors = new List<string>();
    static readonly Dictionary<string, int> _leaves = new Dictionary<string, int>(), _homes = new Dictionary<string, int>();
    static readonly Dictionary<string, int> _mirrorClosed = new Dictionary<string, int>();
    static readonly Dictionary<string, AgentPresentation> _last = new Dictionary<string, AgentPresentation>();
    static readonly int[] _markerOn = new int[4], _markerOff = new int[4];
    static readonly bool[] _markerWas = new bool[4];
    static readonly Transform[] _markers = new Transform[4];
    static int _i = -1, _failures, _errorCount, _frames, _maxAway, _ghostFrames, _overlapFrames, _checksBefore, _closuresBefore;
    static float _stepT0, _runT0, _lastWrite, _conversingAt = -1f;
    static bool _running, _begun, _mirrorHooked, _alreadyPlaced;
    static string _tasksBefore = "";
    static AgentPresentationRegistry _hookedRegistry;
    static PresentationMirror _hookedMirror;

    const string ReportFile = "Library/MeetDrive.txt";
    const string StopFile = "Library/MeetDrive.stop";

    static T One<T>() where T : UnityEngine.Object => UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static WallHabitatController C => One<WallHabitatController>();
    static ModeConsole Dock => One<ModeConsole>();
    static float Now => Time.realtimeSinceStartup;
    static float Since => Now - _stepT0;
    static Vector3 Head() { var cam = Camera.main; return cam != null ? cam.transform.position : new Vector3(0, 1.2f, 0); }

    // ───────────────────────── entry points ─────────────────────────

    public static string Run()
    {
        if (!Application.isPlaying) return "not playing: enter Play Mode first";
        var c = C;
        if (c == null) return "no WallHabitatController in the scene";
        var g = One<WallBackendGateway>();
        if (g != null && g.IsLive) return "the session is live: this driver runs offline only (leave the backend off)";
        if (System.IO.File.Exists(StopFile)) System.IO.File.Delete(StopFile);
        _steps.Clear(); _log.Clear(); _errors.Clear();
        _leaves.Clear(); _homes.Clear(); _mirrorClosed.Clear(); _last.Clear();
        for (int r = 0; r < 4; r++) { _markerOn[r] = _markerOff[r] = 0; _markerWas[r] = false; _markers[r] = null; }
        _i = -1; _failures = _errorCount = _frames = _maxAway = _ghostFrames = _overlapFrames = 0;
        _conversingAt = -1f; _tasksBefore = ""; _mirrorHooked = false; _alreadyPlaced = false;
        Unhook();
        Build();
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        UnityEditor.EditorApplication.update -= Tick;
        UnityEditor.EditorApplication.update += Tick;
        _running = true; _begun = false;
        _runT0 = Now;
        return "MeetDrive started, " + _steps.Count + " steps (PLAY, no hands). Call MeetDrive.Status.";
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

    /// <summary>One line, any time (no run needed).</summary>
    public static string Describe()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C;
        if (c == null) return "no WallHabitatController";
        var d = Dock;
        return c.DescribeMeet() + " | status='" + c.WallStatus + "'" + (d != null ? " | dock meetKey=" + (d.meetKey != null && d.meetKey.root != null ? d.meetKey.root.name : "MISSING") : "");
    }

    // ───────────────────────── bookkeeping ─────────────────────────

    static void OnLog(string condition, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        _errorCount++;
        if (_errors.Count < 6) _errors.Add(type + ": " + (condition.Length > 160 ? condition.Substring(0, 160) : condition));
    }

    static void OnPresentation(string agent, AgentPresentation p)
    {
        if (agent == null) return;
        AgentPresentation was;
        if (!_last.TryGetValue(agent, out was)) was = AgentPresentation.Home;
        if (p == AgentPresentation.Home) Bump(_homes, agent);
        else if (was == AgentPresentation.Home) Bump(_leaves, agent);
        _last[agent] = p;
    }

    static void OnMirrorClosed(PresentationClosed c) { if (c.agentId != null) Bump(_mirrorClosed, c.agentId); }

    static void Bump(Dictionary<string, int> d, string k) { int n; d.TryGetValue(k, out n); d[k] = n + 1; }
    static int Count(Dictionary<string, int> d, string k) { int n; return d.TryGetValue(k, out n) ? n : 0; }

    static void Unhook()
    {
        if (_hookedRegistry != null) _hookedRegistry.Changed -= OnPresentation;
        if (_hookedMirror != null) _hookedMirror.portal.Closed -= OnMirrorClosed;
        _hookedRegistry = null; _hookedMirror = null; _mirrorHooked = false;
    }

    static void End(string why)
    {
        if (!_running) return;
        _running = false;
        UnityEditor.EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        Unhook();
        _log.Add("== run ended: " + why + " after " + (Now - _runT0).ToString("F1") + " s");
        try { System.IO.File.WriteAllText(ReportFile, Report()); } catch (Exception) { }
    }

    static void Add(string name, Func<string> begin, Func<bool> done, float timeout, Func<string> detail = null)
    {
        _steps.Add(new Step { name = name, begin = begin, done = done, timeout = timeout, detail = detail });
    }

    static string Tasks()
    {
        var bridge = One<WallEventBridge>();
        if (bridge == null) return "no WallEventBridge";
        var sb = new StringBuilder();
        foreach (var a in MeetCopy.Agents)
        {
            string id, title, state; int rev;
            sb.Append(a + "=" + (bridge.TryGetAgentTask(a, out id, out title, out state, out rev) ? id + "/" + state + "/r" + rev : "none") + "; ");
        }
        return sb.ToString();
    }

    static int KeyIndex(WallCard card, string label)
    {
        if (card == null) return -1;
        for (int i = 0; i < card.KeyCount; i++) if (card.KeyLabel(i) == label) return i;
        return -1;
    }

    static string KeyList(WallCard card)
    {
        if (card == null) return "[]";
        var l = new List<string>();
        for (int i = 0; i < card.KeyCount; i++) { string s = card.KeyLabel(i); if (s != null) l.Add(s); }
        return "[" + string.Join(",", l) + "]";
    }

    static bool PressCardKey(WallCard card, string label)
    {
        int i = KeyIndex(card, label);
        return i >= 0 && card.Press(i);
    }

    static bool Previewing(string agent)
    {
        var p = C.Portal;
        return (p.State == PortalState.AwaitConfirm || p.State == PortalState.Preview) && p.Agent == agent;
    }

    static bool HomeAgain(string agent)
    {
        var c = C;
        return c.Portal.State == PortalState.Hidden && !c.HasPortalAvatar && c.Presentations.Of(agent) == AgentPresentation.Home
               && c.Presentations.AwayCount == 0 && c.Side == null;
    }

    static string Exactly(string agent)
    {
        return agent + " left=" + Count(_leaves, agent) + " home=" + Count(_homes, agent) + " mirrorClosed=" + Count(_mirrorClosed, agent);
    }

    static bool OnceEach(string agent) { return Count(_leaves, agent) == 1 && Count(_homes, agent) == 1; }

    static string SpotWords()
    {
        var c = C;
        PortalCandidate s = c.Portal.Spot;
        Vector3 head = Head();
        Vector3 d = s.position - head; d.y = 0f;
        Vector3 f = Camera.main != null ? Camera.main.transform.forward : Vector3.forward; f.y = 0f;
        float ang = d.sqrMagnitude > 1e-6f && f.sqrMagnitude > 1e-6f ? Vector3.Angle(f, d) : 0f;
        var g = c.ComfortGuard;
        return "spot " + s.position.ToString("F2") + " h=" + s.avatarHeight.ToString("F2") + " from head " + d.magnitude.ToString("F2") + " m at " + ang.ToString("F0")
               + " deg off forward, checked=" + s.roomChecked + (c.PortalSpotIsFallback ? " SMALL-VISITOR FALLBACK (not enough space for a large one)" : "")
               + " | guard checks=" + g.Checks + " lastCheck valid=" + g.LastCheck.valid + " '" + g.LastCheck.reason + "' closures=" + g.Closures;
    }

    // ───────────────────────── the run ─────────────────────────

    static void Build()
    {
        Add("1 flags on (driver only) + synthetic room, offline; hands not simulated", () =>
        {
            C.SetFlags(new WallFeatureFlags { wallHabitatEnabled = true, wallManagerEnabled = true, agentSelectEnabled = true, agentBtwEnabled = true, largeAgentPortalEnabled = true, wallElevatorVfxEnabled = false });
            var d = One<SpatialWorkspaceDirector>();
            if (d == null) return "no SpatialWorkspaceDirector";
            d.UseSyntheticTestRoom();
            C.StopSimulating();
            _hookedRegistry = C.Presentations;
            if (_hookedRegistry == null) return "no presentation registry";
            _hookedRegistry.Changed += OnPresentation;
            return null;
        }, () => C.WallEnabled && C.WallMeetEnabled, 5f, () => C.DescribeMeet());

        Add("2 enter Wall and place the habitat", () => { _alreadyPlaced = C.WallActive && !C.WallPreviewing; if (!C.WallActive) C.PressWall(); return null; },
            () =>
            {
                if (C.WallActive && !C.WallPreviewing) return true;
                if (C.Flow != null && C.Flow.HasShown && C.Flow.Shown.valid) C.PressPlaceHere();
                return false;
            }, 40f, () => "placed on " + (C.Flow.HasPlaced ? C.Flow.Placed.wallId : "?"));

        Add("3 the status line names Meet once the habitat is placed", null,
            () => _alreadyPlaced || C.WallStatus.Contains("Meet"), 3f,
            () => _alreadyPlaced ? "SKIPPED: the habitat was already on the wall (run from a fresh Play Mode to see the hint)" : "status='" + C.WallStatus + "'");

        Add("4 the Spaces dock shows Console_Meet (shown by scale, poke on)", () =>
        {
            var d = Dock;
            if (d == null) return "no ModeConsole";
            if (d.meetKey == null || d.meetKey.root == null) return "no Console_Meet on the dock: run AgentScripts/MeetKeyV1.cs (MeetKeyV1.Apply) first";
            return null;
        }, () =>
        {
            var k = Dock.meetKey;
            return k.root.localScale.x >= k.shownScale.x * 0.95f && (k.poke == null || k.poke.enabled);
        }, 4f, () => { string s = Dock.Describe(); int at = s.IndexOf("\n  Meet:", StringComparison.Ordinal); return at >= 0 ? s.Substring(at + 3).Trim() : s; });

        Add("5 press Meet on the dock: the chooser lists Researcher / Developer / Tester / Manager / Cancel", () =>
        {
            _tasksBefore = Tasks();
            Dock.PressByName("Meet");
            return null;
        }, () => C.MeetChoosing && KeyList(C.MeetCard) == "[Researcher,Developer,Tester,Manager,Cancel]" && C.Presentations.AwayCount == 0, 3f,
            () => "card='" + (C.MeetCard != null ? C.MeetCard.Title.text : "-") + "' keys=" + KeyList(C.MeetCard) + " | " + C.DescribeMeet());

        Add("6 choose Researcher: straight to the portal preview (no hand carry, no desk visit)", () =>
        {
            int i = KeyIndex(C.MeetCard, "Researcher");
            if (i < 0 || !C.MeetCard.Press(i)) return "the Researcher key could not be pressed: " + KeyList(C.MeetCard);
            return null;
        }, () => Previewing("researcher") && !C.MeetChoosing && C.Visit.State == VisitState.Off
                 && C.Presentations.Of("researcher") == AgentPresentation.LargePortal && C.Presentations.AwayCount == 1
                 && C.VisitCard != null && C.VisitCard.Visible && KeyIndex(C.VisitCard, "Cancel") >= 0, 3f,
            () => "card='" + (C.VisitCard != null ? C.VisitCard.Title.text : "-") + "' keys=" + KeyList(C.VisitCard) + " msg='" + C.Portal.Message + "' | " + SpotWords());

        Add("7 Open here: the avatar comes out and stays out at a spot the keep-out guard re-checks as clear", () =>
        {
            if (C.Portal.State != PortalState.AwaitConfirm) return "no confirmable spot: " + C.Portal.Message + " | " + SpotWords();
            _checksBefore = C.ComfortGuard.Checks;
            _closuresBefore = C.ComfortGuard.Closures;
            _conversingAt = -1f;
            return PressCardKey(C.VisitCard, "Open here") ? null : "no Open here key: " + KeyList(C.VisitCard);
        }, () =>
        {
            var c = C;
            if (c.Portal.State == PortalState.Hidden || c.Portal.State == PortalState.Closing)
                throw new Exception("the portal closed before it was out: guard '" + c.ComfortGuard.LastReason + "' msg='" + c.Portal.Message + "'");
            if (c.Portal.State != PortalState.Conversing || !c.HasPortalAvatar) return false;
            if (_conversingAt < 0f) _conversingAt = Now;
            if (Now - _conversingAt < 0.8f) return false;                      // several guard checks while it stands there
            var g = c.ComfortGuard;
            bool clear = c.PortalSpotIsFallback || c.Portal.Spot.avatarHeight <= 0.5f || g.LastCheck.valid;
            return clear && g.Checks > _checksBefore && g.Closures == _closuresBefore && c.Portal.Agent == "researcher";
        }, 8f, () => "out for " + (_conversingAt >= 0f ? (Now - _conversingAt).ToString("F1") : "0") + " s | " + SpotWords() + " | card keys=" + KeyList(C.VisitCard));

        Add("8 Send back: home again; the registry saw exactly one leave and one return", () =>
            PressCardKey(C.VisitCard, "Send back") ? null : "no Send back key: " + KeyList(C.VisitCard),
            () => HomeAgain("researcher") && !C.VisitCard.Visible && Since > 0.3f, 6f,
            () => Exactly("researcher") + " markers on/off=" + _markerOn[0] + "/" + _markerOff[0] + " | " + C.DescribeMeet());

        Add("9 ...and exactly once (registry, mirror and the habitat's on-visit marker)", null,
            () => OnceEach("researcher") && Count(_mirrorClosed, "researcher") == 1 && _markerOn[0] == 1 && _markerOff[0] == 1, 1.5f,
            () => Exactly("researcher") + " markers on/off=" + _markerOn[0] + "/" + _markerOff[0]);

        // rc22 (owner: "Developer, come down here ... so they actually go there"): a spoken "come here" on a safe, room-checked
        // spot opens the portal by itself (no Open here press). "meet the developer" (step 12) still only previews.
        Add("10 voice, hands-free: \"Tester, come here\" opens the portal and the Tester comes out by itself", () =>
        {
            var vc = One<VoiceCommandCenter>();
            if (vc == null) return "no VoiceCommandCenter in the scene";
            if (VoiceCommandCenter.PortalRequestHandler == null) return "the Wall did not register PortalRequestHandler | " + C.DescribeMeet();
            vc.InjectAmbientFinal("Tester, come here");
            return null;
        }, () => C.Portal.Agent == "tester" && C.Portal.State == PortalState.Conversing && C.VoiceAutoOpens > 0
                 && One<VoiceCommandCenter>().LastIntent == "PortalRequest", 8f,
            () => { var vc = One<VoiceCommandCenter>(); return "intent=" + vc.LastIntent + " said='" + vc.LastMessage + "' portal=" + C.Portal.State + " autoOpens=" + C.VoiceAutoOpens + " | " + SpotWords(); });

        Add("11 Send back on the card: the Tester is home, once", () =>
            PressCardKey(C.VisitCard, "Send back") ? null : "no Send back key: " + KeyList(C.VisitCard),
            () => HomeAgain("tester") && OnceEach("tester") && Since > 0.3f, 6f, () => Exactly("tester"));

        Add("12 voice, hands-free, name not first: \"meet the developer\" opens the Developer's preview", () =>
        {
            One<VoiceCommandCenter>().InjectAmbientFinal("meet the developer");
            return null;
        }, () => Previewing("developer") && One<VoiceCommandCenter>().LastIntent == "PortalRequest", 3f,
            () => { var vc = One<VoiceCommandCenter>(); return "intent=" + vc.LastIntent + " said='" + vc.LastMessage + "'"; });

        Add("13 Cancel: the Developer is home, once", () =>
            PressCardKey(C.VisitCard, "Cancel") ? null : "no Cancel key: " + KeyList(C.VisitCard),
            () => HomeAgain("developer") && OnceEach("developer") && Since > 0.2f, 4f, () => Exactly("developer"));

        Add("14 Meet, then the chooser's Cancel: nothing leaves home", () =>
        {
            Dock.PressByName("Meet");
            return C.MeetChoosing ? null : "the chooser did not open: '" + C.LastMeetRefusal + "'";
        }, () =>
        {
            if (C.MeetChoosing && Since > 0.3f) PressCardKey(C.MeetCard, MeetCopy.Cancel);
            return !C.MeetChoosing && Since > 0.4f && C.Presentations.AwayCount == 0 && C.Portal.State == PortalState.Hidden;
        }, 3f, () => C.DescribeMeet());

        Add("15 the canonical task state is unchanged (the portal never touches a task)", null, () => Since > 0.2f, 2f,
            () => { string after = Tasks(); return (after == _tasksBefore ? "UNCHANGED " : "CHANGED (a scripted offline board may advance on its own; compare by task id) ") + "before: " + _tasksBefore + " after: " + after; });
    }

    // ───────────────────────── frame loop ─────────────────────────

    static void Tick()
    {
        if (!_running) return;
        if (!Application.isPlaying) { End("Play Mode ended"); return; }
        if (System.IO.File.Exists(StopFile)) { System.IO.File.Delete(StopFile); End("stopped by the driver"); return; }
        if (Now - _lastWrite > 1f) { _lastWrite = Now; try { System.IO.File.WriteAllText(ReportFile, Report()); } catch (Exception) { } }
        var c = C;
        if (c == null) { End("the controller is gone"); return; }

        if (!_mirrorHooked && c.Mirror != null) { _hookedMirror = c.Mirror; _hookedMirror.portal.Closed += OnMirrorClosed; _mirrorHooked = true; }
        if (c.WallActive && c.Presentations != null)
        {
            _frames++;
            _maxAway = Mathf.Max(_maxAway, c.Presentations.AwayCount);
            if (c.Presentations.AwayCount > 0 && !c.Visit.Busy && c.Portal.State == PortalState.Hidden) _ghostFrames++;
            if (c.MeetChoosing && c.Portal.State != PortalState.Hidden) _overlapFrames++;
            if (c.View != null && c.View.root != null)
                for (int r = 0; r < 4; r++)
                {
                    if (_markers[r] == null) _markers[r] = FindDeep(c.View.root, "OnVisitMarker_" + (HabitatRole)r);
                    Transform m = _markers[r];
                    bool on = m != null && m.gameObject.activeSelf;
                    if (on && !_markerWas[r]) _markerOn[r]++;
                    if (!on && _markerWas[r]) _markerOff[r]++;
                    _markerWas[r] = on;
                }
        }

        if (_i < 0) { _i = 0; _begun = false; }
        if (_i >= _steps.Count)
        {
            bool clean = _failures == 0 && _maxAway <= 1 && _ghostFrames == 0 && _overlapFrames == 0;
            End(clean ? "all steps done" : "finished with failures");
            return;
        }
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
        catch (Exception ex) { Fail(s, "exception: " + ex.Message + (s.detail != null ? " | " + SafeDetail(s) : "")); }
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
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
        sb.AppendLine("MEET DRIVE — offline, synthetic room, no hands — evidence PLAY (Editor). Real pokes, the microphone and the headset: not shown here.");
        sb.AppendLine(_running ? "RUNNING: step " + (_i + 1) + " of " + _steps.Count : (_steps.Count == 0 ? "not started" : "FINISHED"));
        foreach (string l in _log) sb.AppendLine(l);
        sb.AppendLine("observed over " + _frames + " frames: max agents away=" + _maxAway + " (must be <= 1), frames with an agent away but no visit or portal="
                      + _ghostFrames + " (must be 0), frames with the chooser open while a portal exists=" + _overlapFrames + " (must be 0)");
        sb.AppendLine("registry leaves/returns: " + Exactly("researcher") + "; " + Exactly("tester") + "; " + Exactly("developer"));
        sb.AppendLine("steps failed=" + _failures + " console errors=" + _errorCount);
        foreach (string e in _errors) sb.AppendLine("  " + e);
        var c = Application.isPlaying ? C : null;
        if (c != null) sb.AppendLine("now: " + c.DescribeMeet());
        return sb.ToString();
    }
}
