using System;
using System.Reflection;
using UnityEngine;
using MuseDesk.Agents;
using MuseDesk.Documents;
using MuseDesk.Models;
using MuseDesk.Networking;
using MuseDesk.Phase2;
using MuseDesk.Phase2.Board;
using MuseDesk.Phase2.Contracts;
using MuseDesk.Spatial;
using MuseDesk.Workspace;
using AgentDto = MuseDesk.Models.AgentView;

namespace MuseDesk.Voice
{
    /// <summary>Whether a spoken decision could be sent right now.</summary>
    public enum VoiceDecisionState
    {
        /// <summary>No decision is awaiting the user.</summary>
        None,
        /// <summary>A decision waits, but the paper is still in the agent's hand (or on its way).</summary>
        NotTaken,
        /// <summary>The paper was taken and is still unfolding.</summary>
        Opening,
        /// <summary>A response is already in flight.</summary>
        Busy,
        Ready,
    }

    /// <summary>
    /// Everything voice reads from, or asks of, the office. It finds the existing components by type
    /// and calls the same public methods the keys call (the ones the Play Mode drivers PlayDrive and
    /// SpatialDrive use), so voice can never do something a key could not, and the backend stays the
    /// authority. It also remembers what the run's own events said (test counts, the last outcome)
    /// so an answer can quote them. Plain class owned by VoiceCommandCenter; every reference is
    /// optional and every call is safe when its target is missing.
    /// </summary>
    public sealed class VoiceOffice
    {
        public SessionController Session;

        public TaskDock Dock { get; private set; }
        public Phase2Bootstrap Board { get; private set; }
        public DocumentDeliveryController Delivery { get; private set; }
        public CompletionReceipt Receipt { get; private set; }
        public ArtifactViewer Viewer { get; private set; }
        public FocusModeController Focus { get; private set; }
        public SpatialWorkspaceDirector Director { get; private set; }

        /// <summary>A new run began or the session was reset (the once-per-run hint is due again).</summary>
        public event Action RunChanged;

        float _nextFind;
        SessionReducer _reducer;
        bool _wasRunning;

        // What this run's events said.
        int _testsPassed = -1, _testsFailed = -1;
        string _lastOutcome, _lastSummary, _artifactSummary;

        // ───────────────────────── lookups ─────────────────────────

        /// <summary>Find whatever is still missing (at most once a second unless forced).</summary>
        public void Find(bool force)
        {
            if (!force && Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 1f;
            if (Session == null) Session = One<SessionController>();
            if (Dock == null) Dock = One<TaskDock>();
            if (Board == null) Board = One<Phase2Bootstrap>();
            if (Delivery == null) Delivery = One<DocumentDeliveryController>();
            if (Receipt == null) Receipt = One<CompletionReceipt>();
            if (Viewer == null) Viewer = One<ArtifactViewer>();
            if (Focus == null) Focus = One<FocusModeController>();
            if (Director == null) Director = One<SpatialWorkspaceDirector>();
            if (_reducer == null && Session != null) Bind();
        }

        static T One<T>() where T : UnityEngine.Object
        {
            return UnityEngine.Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
        }

        DecisionPanel Panel => Delivery != null ? Delivery.panel : null;

        HeldPaperController Held =>
            Delivery != null && Delivery.paperRoot != null ? Delivery.paperRoot.GetComponent<HeldPaperController>() : null;

        // ───────────────────────── the run's own events ─────────────────────────

        void Bind()
        {
            _reducer = Session.Reducer;
            _reducer.ArtifactCreated += OnArtifact;
            _reducer.WorkDelivered += OnDelivered;
            _reducer.TaskFailed += OnFailed;
            _reducer.DecisionResolved += OnResolved;
            _reducer.SessionReset += OnReset;
            _reducer.SnapshotLoaded += OnReset;
            Session.RunStateChanged += OnRunState;
            _wasRunning = Session.RunInProgress;
        }

        public void Unbind()
        {
            if (_reducer != null)
            {
                _reducer.ArtifactCreated -= OnArtifact;
                _reducer.WorkDelivered -= OnDelivered;
                _reducer.TaskFailed -= OnFailed;
                _reducer.DecisionResolved -= OnResolved;
                _reducer.SessionReset -= OnReset;
                _reducer.SnapshotLoaded -= OnReset;
            }
            if (Session != null) Session.RunStateChanged -= OnRunState;
            _reducer = null;
        }

        void OnArtifact(EventEnvelope e)
        {
            if (e == null) return;
            _testsPassed = e.PayloadValue<int>("tests_passed", -1);
            _testsFailed = e.PayloadValue<int>("tests_failed", -1);
            _artifactSummary = e.PayloadString("summary", null);
        }

        void OnDelivered(EventEnvelope e)
        {
            _lastOutcome = "delivered";
            _lastSummary = e != null ? e.PayloadString("result_summary", null) : null;
            if (string.IsNullOrEmpty(_lastSummary)) _lastSummary = _artifactSummary;
        }

        void OnFailed(EventEnvelope e)
        {
            _lastOutcome = "failed";
            _lastSummary = e != null ? e.PayloadString("result_summary", e.PayloadString("reason", null)) : null;
        }

        void OnResolved(string decisionId, string state)
        {
            if (state == "rejected") { _lastOutcome = "rejected"; _lastSummary = null; }
        }

        void OnReset()
        {
            ClearRun();
            RunChanged?.Invoke();
        }

        void OnRunState()
        {
            bool running = Session != null && Session.RunInProgress;
            if (running && !_wasRunning) { ClearRun(); RunChanged?.Invoke(); }
            _wasRunning = running;
        }

        void ClearRun()
        {
            _testsPassed = _testsFailed = -1;
            _lastOutcome = _lastSummary = _artifactSummary = null;
        }

        // ───────────────────────── facts ─────────────────────────

        public bool IsLive => Session != null && Session.IsLive;

        static bool Working(string state)
        {
            return state == AgentStates.Researching || state == AgentStates.Coding
                || state == AgentStates.Testing || state == AgentStates.Reporting;
        }

        AgentDto FindAgent(string agentId)
        {
            var snap = Session != null ? Session.Reducer.Snapshot : null;
            var agents = snap != null ? snap.Agents : null;
            if (agents == null) return null;
            for (int i = 0; i < agents.Count; i++)
                if (agents[i] != null && agents[i].AgentId == agentId) return agents[i];
            return null;
        }

        public VoiceRunFacts Facts()
        {
            var f = new VoiceRunFacts { TestsPassed = _testsPassed, TestsFailed = _testsFailed, LastOutcome = _lastOutcome, LastSummary = _lastSummary };
            if (Session == null) return f;
            f.Live = Session.IsLive;
            f.RunInProgress = Session.RunInProgress;
            f.HasStartedRun = Session.HasStartedRun;
            var pending = Session.Reducer.FirstPendingDecision();
            if (pending != null && !string.IsNullOrEmpty(Session.PendingDecisionId))
            {
                f.DecisionPending = true;
                f.DecisionAgentId = pending.AgentId;
                f.DecisionSummary = pending.Summary;
            }
            var agents = Session.Reducer.Snapshot != null ? Session.Reducer.Snapshot.Agents : null;
            if (agents != null)
                for (int i = 0; i < agents.Count; i++)
                {
                    var a = agents[i];
                    if (a == null || !Working(a.State)) continue;
                    f.ActiveAgentId = a.AgentId;
                    f.ActiveActivity = MuseDesk.Art.WorkstationStatusDisplay.ActivityWords(a.State, false);
                    f.ActiveProgress = a.Progress;
                    break;
                }
            var tasks = Board != null && Board.Reducer != null ? Board.Reducer.Tasks : null;
            if (tasks != null)
                foreach (var kv in tasks)
                    if (kv.Value != null && TaskCardView.IsReadyToRun(kv.Value)) f.TasksReady++;
            return f;
        }

        /// <summary>What one agent would say, from the run state. Uses the characters' own status
        /// sentence (AgentAttention.DescribeStatus) so the bubble, the chip and voice agree; the
        /// Tester adds the test counts the run reported.</summary>
        public string AgentLine(string agentId)
        {
            VoiceRunFacts run = Facts();
            string line = null;
            try
            {
                var attention = AgentAttention.Instance;
                if (attention != null) line = attention.DescribeStatus(agentId);
            }
            catch (Exception e) { Debug.LogWarning("[MuseDesk] voice: agent status unavailable (" + e.GetType().Name + ")."); }
            if (string.IsNullOrEmpty(line))
            {
                var a = FindAgent(agentId);
                bool waiting = run.DecisionPending && run.DecisionAgentId == agentId;
                line = VoiceAnswers.AgentLine(agentId, a != null,
                    a != null ? MuseDesk.Art.WorkstationStatusDisplay.ActivityWords(a.State, waiting) : null,
                    a != null ? a.Progress : null, waiting, null, run);
            }
            else if (agentId == "tester" && run.TestsPassed >= 0 && line.IndexOf("passed", StringComparison.OrdinalIgnoreCase) < 0)
            {
                line = VoiceAnswers.Sentence(line) + " Tests: " + run.TestsPassed + " passed, " + Mathf.Max(0, run.TestsFailed) + " failed.";
            }
            return VoiceAnswers.Ascii(line);
        }

        /// <summary>The character looks at the user and shows <paramref name="text"/> in its bubble.
        /// Both are requests: a character the pipeline is using may not turn.</summary>
        public void AgentSpeak(string agentId, string text, float seconds)
        {
            try
            {
                var attention = AgentAttention.Instance;
                if (attention != null)
                {
                    attention.Address(agentId);
                    if (!string.IsNullOrEmpty(text)) attention.Say(agentId, text, seconds);
                }
            }
            catch (Exception e) { Debug.LogWarning("[MuseDesk] voice: could not address the agent (" + e.GetType().Name + ")."); }
            // When the office is away its characters are too far to read: the near-user perch shows the same agent.
            if (SpatialWorkspaceState.IsRemote) SummonRequests.RaiseAskAgent(agentId);
        }

        /// <summary>The characters tilt their heads to the user while the microphone is open.</summary>
        public void AgentsListen(bool on)
        {
            try
            {
                var attention = AgentAttention.Instance;
                if (attention == null || attention.agents == null) return;
                for (int i = 0; i < attention.agents.Length; i++)
                {
                    var p = attention.agents[i];
                    if (p != null && p.gaze != null && p.gaze.isActiveAndEnabled) p.gaze.SetListening(on);
                }
            }
            catch { }
        }

        // ───────────────────────── context ─────────────────────────

        public VoiceDecisionState DecisionState()
        {
            if (Session == null || string.IsNullOrEmpty(Session.PendingDecisionId) || Delivery == null) return VoiceDecisionState.None;
            var panel = Panel;
            if (Delivery.ResponseInFlight || (panel != null && (panel.IsPending || panel.IsResolved))) return VoiceDecisionState.Busy;
            if (Delivery.paperRoot == null || !Delivery.paperRoot.activeInHierarchy) return VoiceDecisionState.NotTaken;
            var held = Held;
            if (held != null)
            {
                if (!held.IsTaken) return VoiceDecisionState.NotTaken;
                if (!held.ReadyForDecision) return VoiceDecisionState.Opening;
            }
            return VoiceDecisionState.Ready;
        }

        public string PendingDecisionId => Session != null ? Session.PendingDecisionId : null;
        public bool Moving => SpatialWorkspaceState.IsTransitioning;
        public bool Previewing => Director != null && Director.IsPreviewing;
        public bool BoardOpen => Dock != null && Dock.board != null && Dock.board.IsExpanded;
        public bool ViewerOpen => Viewer != null && Viewer.IsOpen;

        public VoiceContext Context(bool asking, bool undo)
        {
            VoiceContext c = VoiceContext.None;
            if (DecisionState() != VoiceDecisionState.None) c |= VoiceContext.DecisionPending;
            if (asking) c |= VoiceContext.ConfirmationPending;
            if (undo) c |= VoiceContext.UndoWindow;
            if (Previewing) c |= VoiceContext.Previewing;
            if (Moving) c |= VoiceContext.Moving;
            if (ViewerOpen) c |= VoiceContext.ViewerOpen;
            if (BoardOpen) c |= VoiceContext.BoardOpen;
            if (IsLive) c |= VoiceContext.Live;
            return c;
        }

        public VoiceHelpState HelpState(VoiceContext context)
        {
            VoiceRunFacts run = Facts();
            var held = Held;
            return new VoiceHelpState
            {
                Context = context,
                Live = run.Live,
                Mode = (int)SpatialWorkspaceState.Mode,
                FloorEnabled = Director != null && Director.flags != null && Director.flags.floorModeEnabled,
                CeilingEnabled = Director != null && Director.flags != null && Director.flags.ceilingModeEnabled,
                OfficeAway = SpatialWorkspaceState.IsRemote,
                CanStart = run.TasksReady > 0 && !run.RunInProgress && !run.DecisionPending,
                CanRunAgain = Receipt != null && Receipt.IsShown && !run.Live,
                HasResult = (Receipt != null && Receipt.CanView) || (Viewer != null && Viewer.HasAvailable),
                FocusOn = FocusModeController.Active,
                PaperTaken = held == null || held.IsTaken,
                CanResetLayout = ResetLayoutMethod() != null,
            };
        }

        // ───────────────────────── actions (each one is a key's own method) ─────────────────────────

        /// <summary>Send the decision exactly as the paper's key or a stamp does.</summary>
        public bool Respond(string action)
        {
            if (Delivery == null || DecisionState() != VoiceDecisionState.Ready) return false;
            string idBefore = PendingDecisionId;
            switch (action)
            {
                case DecisionActions.Approve: Delivery.OnApprove(); break;
                case DecisionActions.Reject: Delivery.OnReject(); break;
                case DecisionActions.RequestChanges: Delivery.OnRequestChanges(); break;
                default: return false;
            }
            // The scripted demo resolves in the same frame, so the decision is already gone rather than in flight.
            return Delivery.ResponseInFlight || PendingDecisionId != idBefore;
        }

        public bool TakePaper()
        {
            var held = Held;
            if (held == null || held.IsTaken || !held.isActiveAndEnabled) return false;
            held.TakeNow();
            return held.IsTaken;
        }

        public bool PaperOpen
        {
            get
            {
                if (Delivery == null || Delivery.paperRoot == null || !Delivery.paperRoot.activeInHierarchy) return false;
                var held = Held;
                return held == null || held.IsTaken;
            }
        }

        public bool DetailsShown => Panel != null && Panel.IsExpanded;

        public bool SetDetails(bool on)
        {
            var panel = Panel;
            if (panel == null || !PaperOpen) return false;
            if (on) panel.Expand(); else panel.Collapse();
            return true;
        }

        /// <summary>Ask the pending decision's agent a question; the answer goes on the paper, as the
        /// Ask why key's does.</summary>
        public bool AskWhy(string question, string agentId, Action<string> onAnswer)
        {
            if (Session == null || string.IsNullOrEmpty(Session.PendingDecisionId)) return false;
            var panel = Panel;
            if (panel != null && PaperOpen) { panel.Expand(); panel.SetAnswer("Asking..."); }
            if (string.IsNullOrEmpty(agentId))
            {
                var pending = Session.Reducer.FirstPendingDecision();
                agentId = pending != null && !string.IsNullOrEmpty(pending.AgentId) ? pending.AgentId : AgentIds.Researcher;
            }
            Session.AskPending(string.IsNullOrEmpty(question) ? "Why do you recommend this?" : question,
                ans => { if (panel != null) panel.SetAnswer(ans); onAnswer?.Invoke(ans); }, agentId);
            return true;
        }

        /// <summary>Write one line on the paper's status line, only while the paper is waiting for an
        /// answer (never over "Sending..." or an outcome). Returns the text written, or null.</summary>
        public string PaperStatus(string line)
        {
            var panel = Panel;
            if (panel == null || !PaperOpen || panel.IsPending || panel.IsResolved) return null;
            panel.SetVoiceStatus(line);
            return panel.status != null ? panel.status.text : null;
        }

        /// <summary>Clear the paper's status line if it still shows what voice wrote there.</summary>
        public void ClearPaperStatus(string written)
        {
            var panel = Panel;
            if (panel == null || string.IsNullOrEmpty(written) || panel.status == null) return;
            if (panel.IsPending || panel.IsResolved || panel.status.text != written) return;
            panel.SetVoiceStatus("");
        }

        public void Start()
        {
            if (Dock != null) Dock.StartReadyTask();
            else if (Session != null) Session.StartDemo();
        }

        public bool ToggleBoard()
        {
            if (Dock == null || Dock.board == null) return false;
            bool before = Dock.board.IsExpanded;
            Dock.ToggleBoard();
            return Dock.board.IsExpanded != before;
        }

        public bool ViewResult()
        {
            if (Receipt != null && Receipt.CanView) { Receipt.View(); return true; }
            return Viewer != null && Viewer.OpenLatest();
        }

        // Reset layout: MovablePanel.ResetAll(), by reflection so this compiles without it.
        static bool _layoutLooked;
        static MethodInfo _layoutReset;

        static MethodInfo ResetLayoutMethod()
        {
            if (_layoutLooked) return _layoutReset;
            _layoutLooked = true;
            try
            {
                Type t = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType("MuseDesk.Spatial.MovablePanel", false) ?? asm.GetType("MuseDesk.Workspace.MovablePanel", false);
                    if (t != null) break;
                }
                if (t != null) _layoutReset = t.GetMethod("ResetAll", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            }
            catch { _layoutReset = null; }
            return _layoutReset;
        }

        public bool ResetLayout()
        {
            MethodInfo m = ResetLayoutMethod();
            if (m == null) return false;
            try { m.Invoke(null, null); return true; }
            catch (Exception e) { Debug.LogWarning("[MuseDesk] voice: reset layout failed (" + e.GetType().Name + ")."); return false; }
        }
    }
}
