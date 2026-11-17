using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Ui;
using MuseDesk.Spatial.Wall;
using MuseDesk.Wall;

/// <summary>
/// Play Mode driver for the whole Living Office loop (synthetic room, no headset). Evidence level: PLAY (Editor), never
/// on-head. It presses the same public entry points the keys, the pinch and the voice hook use; it never injects a
/// backend event. Every step is timed and the console is watched for errors.
///
///   RunOffline            scripted demo board: ask -> plan paper -> revise -> reject -> ask again -> approve (stamp) ->
///                         cards print -> owners ride up and carry them down -> work -> summary -> receipt on the desk
///   RunOfflineWithFailure the same, and the Developer's card fails once (labelled scripted failure): blocked reason,
///                         failure summary, Retry (idempotent, locked), then the plan finishes and a new receipt arrives
///   RunLive               the same loop against the live backend (the session must already be live). No scripted
///                         failure. With no model configured the cards stay queued and the report says so.
///   Status                the report so far (call it repeatedly; the run takes a few minutes)
///   Stop                  abandon the run
/// The flags are switched on for this Play session only (driver), like WallDrive.Enable.
/// </summary>
public static class LivingOfficeDrive
{
    sealed class Step
    {
        public string name;
        public Func<string> begin;      // null = nothing to do; returns null when started, else why it could not start
        public Func<bool> done;
        public float timeout;
        public Func<string> detail;
        public bool softFail;           // a miss is reported but the run goes on
    }

    static readonly List<Step> _steps = new List<Step>();
    static readonly List<string> _log = new List<string>();
    static readonly List<string> _errors = new List<string>();
    static int _i = -1, _failures, _softFailures, _errorCount;
    static float _t0, _stepT0, _runT0;
    static bool _running, _begun, _live, _withFailure;
    static string _mode = "";

    // observations sampled every frame while the run is active
    static int _ridingFrames, _ridingDetached, _doorwayFrames, _doorwayClosed, _scaleChanges, _frames, _onDeckFrames;
    static readonly Vector3[] _scale0 = new Vector3[4];
    static string _firstBlockedReason = "", _firstFailureReason = "", _retryNote = "";
    static int _receipts;
    static ReceiptStage _lastReceiptStage;

    static T One<T>() where T : UnityEngine.Object => UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    static WallHabitatController C => One<WallHabitatController>();
    static WallBackendGateway G => One<WallBackendGateway>();
    static float Now => Time.realtimeSinceStartup;

    static bool _stamp;
    public static string RunOffline() { _stamp = false; return Begin(false, false); }
    public static string RunOfflineWithFailure() { _stamp = false; return Begin(false, true); }
    public static string RunLive() { _stamp = false; return Begin(true, false); }
    /// <summary>As RunOffline, but the plan is rejected and then approved with the physical desk stamps (the stamp press path, same
    /// guarded commit as the key) instead of the keys. The stamp's pose is not simulated: the press itself is.</summary>
    public static string RunOfflineStamp() { _stamp = true; return Begin(false, false); }

    // Every 'script run' call compiles this file into a fresh assembly, so the statics of the call that started the run are
    // not visible to a later Status call. The running instance therefore writes its report to a file once a second, and
    // Status / Stop talk to it through files.
    const string ReportFile = "Library/LivingOfficeDrive.txt";
    const string StopFile = "Library/LivingOfficeDrive.stop";
    static float _lastWrite;

    static void WriteReport() { try { System.IO.File.WriteAllText(ReportFile, Report()); } catch (Exception) { } }

    public static string Stop()
    {
        if (_running) { End("stopped by the driver"); return Report(); }
        System.IO.File.WriteAllText(StopFile, "stop");
        return "stop requested; call Status in a moment";
    }

    static string Begin(bool live, bool withFailure)
    {
        if (!Application.isPlaying) return "not playing: enter Play Mode first";
        if (System.IO.File.Exists(ReportFile) && (DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(ReportFile)).TotalSeconds < 5
            && System.IO.File.ReadAllText(ReportFile).Contains("\nRUNNING"))
            return "a run is already in progress: " + Status();
        if (System.IO.File.Exists(StopFile)) System.IO.File.Delete(StopFile);
        var c = C;
        if (c == null) return "no WallHabitatController in the scene";
        var g = G;
        if (g == null) return "no WallBackendGateway in the scene";
        if (live && !g.IsLive) return "the session is not live: start the backend (scripts\\start_wall_backend.ps1), go live, then call RunLive";
        if (!live && g.IsLive) return "the session is live: use RunLive, or leave the backend off for the offline run";

        // Back-to-back runs: close the paper/visit and forget the previous run's plan, so step 3 starts from a clean Manager.
        if (!live) c.ResetForDriver();
        _live = live; _withFailure = withFailure;
        _mode = live ? "LIVE BACKEND" : (withFailure ? "OFFLINE (scripted demo) + scripted failure" : "OFFLINE (scripted demo)");
        _steps.Clear(); _log.Clear(); _errors.Clear();
        _i = -1; _failures = _softFailures = _errorCount = 0;
        _ridingFrames = _ridingDetached = _doorwayFrames = _doorwayClosed = _scaleChanges = _frames = _onDeckFrames = _receipts = 0;
        _firstBlockedReason = _firstFailureReason = _retryNote = "";
        _lastReceiptStage = ReceiptStage.None;
        Build();
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        UnityEditor.EditorApplication.update -= Tick;
        UnityEditor.EditorApplication.update += Tick;
        _running = true; _begun = false;
        _runT0 = _t0 = Now;
        return "Living Office run started (" + _mode + "), " + _steps.Count + " steps. Call LivingOfficeDrive.Status for the report.";
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
        _log.Add("== run ended: " + why + " after " + (Now - _runT0).ToString("F1") + " s");
        WriteReport();
    }

    // ───────────────────────── the script ─────────────────────────

    static void Add(string name, Func<string> begin, Func<bool> done, float timeout, Func<string> detail = null, bool softFail = false)
    {
        _steps.Add(new Step { name = name, begin = begin, done = done, timeout = timeout, detail = detail, softFail = softFail });
    }

    static bool AllCards(Func<PlanCardView, bool> test)
    {
        var b = C.Board;
        if (b.Count == 0) return false;
        for (int i = 0; i < b.Count; i++) if (!test(b.Cards[i])) return false;
        return true;
    }

    static PlanCardView CardOf(string agent)
    {
        var b = C.Board;
        for (int i = 0; i < b.Count; i++) if (b.Cards[i].agentId == agent) return b.Cards[i];
        return null;
    }

    static string PaperLine()
    {
        var p = C.Paper;
        if (p == null || !p.Visible) return "paper closed";
        float min = float.MaxValue;
        for (int i = 0; i < p.RowCount; i++) min = Mathf.Min(min, p.RowSizeMm(i));
        return "rows=" + p.RowCount + (p.RowCount > 0 ? " smallest=" + min.ToString("F1") + "mm (floor " + TypeScale.MinMmAt(p.readingDistance).ToString("F1") + "mm at " + p.readingDistance.ToString("F2") + " m)" : "")
             + " stamp='" + p.StampWord + "' first='" + (p.RowText(0) ?? "") + "'";
    }

    static void Build()
    {
        const string goal = "Build the login flow, use the existing repo, and have Tester verify it";

        Add("1 flags on (driver only) + synthetic room", () =>
        {
            C.SetFlags(new WallFeatureFlags { wallHabitatEnabled = true, wallManagerEnabled = true, agentSelectEnabled = true, agentBtwEnabled = true, largeAgentPortalEnabled = true, wallElevatorVfxEnabled = false });
            var d = One<SpatialWorkspaceDirector>();
            if (d == null) return "no SpatialWorkspaceDirector";
            d.UseSyntheticTestRoom();
            if (!_live) G.offlineWorkSeconds = 28f;          // scripted pacing: a card is worked for about as long as one pick-up trip takes
            return null;
        }, () => C.WallEnabled, 5f, () => "dispatcher=" + C.useElevatorDispatcher);

        Add("2 enter Wall mode and place the habitat", () => { if (!C.WallActive) C.PressWall(); return null; },
            () =>
            {
                if (C.WallActive && !C.WallPreviewing) return true;
                if (C.Flow != null && C.Flow.HasShown && C.Flow.Shown.valid) C.PressPlaceHere();
                return false;
            }, 25f, () =>
            {
                for (int r = 0; r < 4; r++) { var a = C.View != null ? C.View.Avatar((HabitatRole)r) : null; _scale0[r] = a != null ? a.lossyScale : Vector3.zero; }
                return "placed on " + (C.Flow.HasPlaced ? C.Flow.Placed.wallId : "?") + ", lift=" + (C.Stage != null && C.Stage.dispatch != null ? "ElevatorDispatcher" : "legacy machine");
            });

        Add("3 ask the Manager by hand (ray pinch on the Manager)", () =>
        {
            Vector3 at;
            if (!C.TryGetAvatarCenter(HabitatRole.Manager, out at)) return "no Manager avatar";
            var cam = Camera.main;
            Vector3 o = (cam != null ? cam.transform.position : new Vector3(0, 1.2f, 0)) + Vector3.down * 0.25f + Vector3.right * 0.15f;
            if (!C.PokeManagerByRay(o, at - o)) return "the ray did not open the Manager paper";
            return null;
        }, () => C.Paper != null && C.Paper.Visible, 5f, PaperLine);

        Add("4 request a plan -> plan-review paper (proposed, nothing created)", () => C.RequestPlan(goal) ? null : "request refused: " + C.Manager.Message,
            () => C.Manager.State == ManagerFlowState.AwaitingApproval, _live ? 90f : 10f,
            () => "provenance=" + C.Manager.Provenance + " -> '" + PlanReviewLayout.ProvenanceLabel(C.Manager.Provenance) + "' items=" + C.Manager.Items.Count
                  + " assumptions=" + C.Manager.Assumptions.Count + " risks=" + C.Manager.Risks.Count + " boardCards=" + C.Board.Count + " | " + PaperLine());

        Add("5 revise: drop the last step -> a new revision to review", () =>
        {
            if (C.Manager.Items.Count < 2) return "plan has fewer than two steps";
            C.ToggleItem(C.Manager.Items.Count - 1);
            return C.RevisePlan() ? null : "revise refused: " + C.StatusLine;
        }, () => C.Manager.State == ManagerFlowState.AwaitingApproval && C.Manager.Revision >= 2, _live ? 60f : 10f,
            () => "rev=" + C.Manager.Revision + " items=" + C.Manager.Items.Count + " boardCards=" + C.Board.Count + " | " + PaperLine(), true);

        Add("6 reject" + (_stamp ? " with the Reject stamp" : "") + " -> REJECTED stamp only after confirmation, nothing created", () =>
            {
                if (_stamp)
                {
                    // The stamps are summoned while the plan awaits the user; the Approve and Reject stamps must be in the scene.
                    if (!C.PressPlanStampForDriver(false)) return "no Reject stamp could be pressed (none in the scene, or the plan does not await approval)";
                    _stampNote = "reject stamp presses=" + C.PlanStampRejects + " kitShown=" + KitShown();
                    return null;
                }
                C.RejectPlan(); return null;
            },
            () => C.Manager.State == ManagerFlowState.Rejected && C.Paper.StampWord == "REJECTED", _live ? 60f : 10f,
            () => "boardCards=" + C.Board.Count + " | " + PaperLine() + (_stamp ? " | " + _stampNote : ""), true);

        Add("7 ask again -> a fresh plan", () => C.RequestPlan(goal) ? null : "request refused: " + C.Manager.Message,
            () => C.Manager.State == ManagerFlowState.AwaitingApproval && C.Paper.StampWord == "", _live ? 90f : 10f,
            () => "plan=" + C.Manager.PlanId + " rev=" + C.Manager.Revision + " items=" + C.Manager.Items.Count);

        Add("8 approve" + (_stamp ? " with the Approve stamp" : "") + " -> the backend creates the cards -> APPROVED stamp", () =>
        {
            if (_withFailure) G.ScriptFailNextOfflineCard("developer");
            if (_stamp)
            {
                if (C.Paper.StampWord != "") return "a mark is already on the fresh paper";
                if (!C.PressPlanStampForDriver(true)) return "no Approve stamp could be pressed (none in the scene, or the plan does not await approval)";
                bool second = C.PressPlanStampForDriver(true);       // a second press while the commit is in flight must be refused
                _stampNote = "approve stamp presses=" + C.PlanStampApproves + " secondPressAccepted=" + second + " kitShown=" + KitShown() + " stampWordRightAfter='" + C.Paper.StampWord + "'";
                return null;
            }
            return C.ApprovePlan() ? null : "approve refused: " + C.StatusLine;
        }, () => (C.Manager.State == ManagerFlowState.Delegating || C.Manager.State == ManagerFlowState.Monitoring) && C.Board.Count > 0 && C.Paper.StampWord == "APPROVED",
            _live ? 60f : 10f, () => "committed=" + C.Manager.CommittedCount + " boardCards=" + C.Board.Count + " | " + PaperLine() + (_stamp ? " | " + _stampNote : ""));

        Add("9 cards print in the Manager's office (committed ids only)", null,
            () => C.Manager.PrintedCount >= C.Manager.CommittedCount && C.Manager.CommittedCount > 0, 20f,
            () => "printed=" + C.Manager.PrintedCount + " of " + C.Manager.CommittedCount);

        Add("10 each owner's card reaches its desk (rc18: all at once by paper tube)", null,
            () => AllCards(c => c.delivery == CardDelivery.OnDesk), 420f,
            () =>
            {
                var sb = new StringBuilder();
                for (int i = 0; i < C.Board.Count; i++) sb.Append(C.Board.Cards[i].agentId + (C.Board.Cards[i].deliveredByChute ? "=chute " : "=carried "));
                return sb + "| rides " + (C.Stage.dispatch != null ? C.Stage.dispatch.ToString() : "legacy");
            });

        if (_withFailure)
        {
            Add("11 the Developer's card fails (scripted, labelled) and the Tester's shows the real blocked reason", null,
                () => { var d = CardOf("developer"); var t = CardOf("tester"); return d != null && d.Failed && t != null && t.Blocked && t.blockedBy.Length > 0; }, 240f,
                () =>
                {
                    _firstFailureReason = CardOf("developer").failureReason;
                    _firstBlockedReason = CardOf("tester").blockedReason;
                    return "failure='" + _firstFailureReason + "' | blocked='" + _firstBlockedReason + "'";
                });

            Add("12 the failure summary arrives as a receipt", null, () => C.Board.Receipt.stage == ReceiptStage.OnDesk, 240f,
                () => "receipt artifact=" + C.Board.Receipt.artifactId + " '" + C.Board.Receipt.summary + "'");

            Add("13 Retry poke (twice at once: the second is refused)", () =>
            {
                string id = CardOf("developer").taskId;
                bool first = C.RetryCard(id);
                bool second = C.RetryCard(id);
                _retryNote = "first=" + first + " second=" + second + " ('" + C.StatusLine + "')";
                return first ? null : "the first retry was refused: " + C.StatusLine;
            }, () => { var d = CardOf("developer"); return d != null && !d.Failed && !d.retryInFlight; }, 30f, () => _retryNote + " -> developer=" + CardOf("developer").state);
        }

        Add((_withFailure ? "14" : "11") + " every card is worked to completion (states from the " + (_live ? "backend" : "demo board") + ")", null,
            () => AllCards(c => c.state == "completed"), _live ? 600f : 300f,
            () =>
            {
                var sb = new StringBuilder();
                for (int i = 0; i < C.Board.Count; i++) sb.Append(C.Board.Cards[i].agentId + "=" + C.Board.Cards[i].state + " ");
                if (!string.IsNullOrEmpty(C.ManagerWarning)) sb.Append("| warning='" + C.ManagerWarning + "'");
                return sb.ToString();
            });

        Add((_withFailure ? "15" : "12") + " the Manager's summary comes down and lands on the desk as a receipt", null,
            () => C.Board.Receipt.stage == ReceiptStage.OnDesk && C.ReceiptCard != null && C.ReceiptCard.Visible
                  && (!_withFailure || _receipts >= 2) && C.ReceiptCard.Body.text.Length > 40, 300f,
            () => "receipts=" + _receipts + " artifact=" + C.Board.Receipt.artifactId + " body='" + Trim(C.ReceiptCard.Body.text, 110) + "'");

        Add((_withFailure ? "16" : "13") + " everyone is home, the lift is idle, nothing was faded or failed", null,
            () =>
            {
                var s = C.Stage;
                for (int r = 0; r < 4; r++) if (!s.Walker((HabitatRole)r).AtHome || s.Running((HabitatRole)r) != null) return false;
                return s.dispatch == null || (s.dispatch.dispatcher.OpenRequests == 0 && s.dispatch.dispatcher.Cabin == CabinPhase.Idle);
            }, 240f, () =>
            {
                var s = C.Stage;
                int soft = 0;
                for (int r = 0; r < 4; r++) soft += s.Walker((HabitatRole)r).SoftTransfers;
                bool scaleOk = true;
                for (int r = 0; r < 4; r++) { var a = C.View.Avatar((HabitatRole)r); if (a != null && (a.lossyScale - _scale0[r]).magnitude > 1e-4f) scaleOk = false; }
                return "softTransfers=" + soft + " acts completed=" + s.Completed + " dropped=" + s.Dropped + (s.dispatch != null ? " | " + s.dispatch : "") + " | avatar scale preserved=" + scaleOk
                     + " | reactions played=" + C.ReactionsPlayed + " held=" + C.ReactionsHeld
                     + " | nods deferred=" + C.NodsDeferred + " released=" + C.NodsReleased + " expired=" + C.NodsExpired;
            });
    }

    static string _stampNote = "";
    static string KitShown() { var k = One<NearUserKit>(); return k != null ? k.DecisionItemsShown.ToString() : "no kit"; }

    /// <summary>Item 4 check, call after a run while the plan is on the board: clears the wall's picture the way a stream resync does and
    /// reads it back from the backend (the offline demo board answers the same status shape). Reports the board before and after.</summary>
    public static string ResyncCheck()
    {
        if (!Application.isPlaying) return "not playing";
        var c = C;
        if (c == null) return "no WallHabitatController";
        string before = "cards=" + c.Board.Count + " committedPlan=" + c.Manager.CommittedPlanId;
        int restores0 = c.PlanRestores;
        c.SimulateSessionReset();
        return "before: " + before + " | after reset (async read in flight): cards=" + c.Board.Count + " restores=" + c.PlanRestores + " (was " + restores0 + "). Call ResyncStatus.";
    }

    public static string ResyncStatus()
    {
        var c = C;
        if (c == null) return "no WallHabitatController";
        return "restores=" + c.PlanRestores + " | " + c.DescribeLivingOffice();
    }

    static string Trim(string s, int n) { if (string.IsNullOrEmpty(s)) return ""; s = s.Replace("\n", " / "); return s.Length <= n ? s : s.Substring(0, n) + "..."; }

    // ───────────────────────── frame ─────────────────────────

    static void Tick()
    {
        if (!_running) return;
        if (!Application.isPlaying) { End("Play Mode ended"); return; }
        if (System.IO.File.Exists(StopFile)) { System.IO.File.Delete(StopFile); End("stopped by the driver"); return; }
        if (Now - _lastWrite > 1f) { _lastWrite = Now; WriteReport(); }
        var c = C;
        if (c == null) { End("the controller is gone"); return; }
        Observe(c);

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
                _log.Add("OK   " + s.name + "  [" + (Now - _stepT0).ToString("F1") + " s]" + (s.detail != null ? "  " + s.detail() : ""));
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
        if (s.softFail)
        {
            _softFailures++;
            _log.Add("MISS " + s.name + "  " + why + "  (run continues)");
            _i++; _begun = false;
            return;
        }
        _failures++;
        _log.Add("FAIL " + s.name + "  " + why + " | " + C.DescribeLivingOffice());
        End("stopped at a failed step");
    }

    /// <summary>What the eye would see, sampled every frame: riders attached to the cabin, doorway only with open doors, scale.</summary>
    static void Observe(WallHabitatController c)
    {
        var stage = c.Stage; var view = c.View;
        if (stage == null || view == null || !c.WallActive) return;
        _frames++;
        IHabitatElevator lift = stage.lift;
        float landingZ = stage.layout.nav.Node(HabitatLayout.LandingNode(0)).position.z;
        for (int r = 0; r < 4; r++)
        {
            var role = (HabitatRole)r;
            HabitatWalker w = stage.Walker(role);
            Transform a = view.Avatar(role);
            if (a == null) continue;
            if (w.State == WalkerState.Riding)
            {
                _ridingFrames++;
                if (!view.IsAttachedToCabin(role)) _ridingDetached++;
            }
            else if (w.Visibility >= 1f && Mathf.Abs(w.Position.x - stage.layout.elevatorX) <= stage.layout.doorWidth * 0.5f && w.Position.z < landingZ - 1e-4f)
            {
                _doorwayFrames++;
                if (lift.DoorOpen01 < 1f) _doorwayClosed++;
            }
            if (_scale0[r] != Vector3.zero && w.Visibility >= 1f && (a.lossyScale - _scale0[r]).magnitude > 1e-4f) _scaleChanges++;
        }
        if (stage.OnDeck >= 0) _onDeckFrames++;
        ReceiptStage rs = c.Board.Receipt.stage;
        if (rs == ReceiptStage.OnDesk && _lastReceiptStage != ReceiptStage.OnDesk) _receipts++;
        _lastReceiptStage = rs;
    }

    public static string Status()
    {
        if (_steps.Count > 0) return Report();
        return System.IO.File.Exists(ReportFile) ? System.IO.File.ReadAllText(ReportFile) : "no run yet";
    }

    static string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("LIVING OFFICE DRIVE — " + _mode + " — evidence level PLAY (Editor), not on-head");
        sb.AppendLine(_running ? "RUNNING: step " + (_i + 1) + " of " + _steps.Count + (_i >= 0 && _i < _steps.Count ? " (" + _steps[_i].name + ", " + (Now - _stepT0).ToString("F1") + " s)" : "")
                               : (_steps.Count == 0 ? "not started" : "FINISHED"));
        foreach (string l in _log) sb.AppendLine(l);
        sb.AppendLine("observed over " + _frames + " frames: riding frames=" + _ridingFrames + " (rider not attached to the cabin: " + _ridingDetached + "), doorway frames=" + _doorwayFrames
                      + " (with doors not fully open: " + _doorwayClosed + "), frames with a changed avatar scale=" + _scaleChanges + ", frames with a rider standing by=" + _onDeckFrames);
        sb.AppendLine("steps failed=" + _failures + " missed(optional)=" + _softFailures + " console errors=" + _errorCount);
        foreach (string e in _errors) sb.AppendLine("  " + e);
        var c = Application.isPlaying ? C : null;
        if (c != null) sb.AppendLine("now: " + c.DescribeLivingOffice());
        return sb.ToString();
    }
}
