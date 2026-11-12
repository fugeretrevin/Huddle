using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using MuseDesk.Config;
using MuseDesk.DebugTools;
using MuseDesk.Models;

namespace MuseDesk.Networking
{
    /// <summary>
    /// Owns the active IAgentGateway, the SessionReducer, and the connection lifecycle.
    ///
    /// Launch is immediate: the offline demo session is adopted at once (no waiting on a backend),
    /// and — unless RuntimeSettings.preferOfflineDemo is on — a live connection is attempted in the
    /// background. If it succeeds before the user has started anything in the demo, the controller
    /// switches to live cleanly (views are cleared through session.reset, the live snapshot is
    /// loaded, the mode label changes, <see cref="ModeChanged"/> fires). If the user has already
    /// started the demo, it stays in the demo for this launch and only logs that a desktop is there.
    ///
    /// Every event goes through one reducer, so the presentation is identical in both modes.
    /// Nothing starts by itself here: a run starts when the user assigns the executable card, pokes
    /// a button wired to <see cref="StartDemo"/>, or (offline only) when DemoDirector's idle timer fires.
    /// </summary>
    public class SessionController : MonoBehaviour
    {
        [Header("Wiring")]
        public RuntimeSettings settings;
        public DiagnosticsOverlay diagnostics;
        public EventDispatcher dispatcher;   // required for live mode

        public SessionReducer Reducer { get; } = new SessionReducer();
        public IAgentGateway Gateway { get; private set; }
        public bool IsLive { get; private set; }
        public string PendingDecisionId { get; private set; }

        /// <summary>True once a session is adopted and hydrated. With the immediate demo launch this
        /// is true as soon as Start has run; <see cref="IsLive"/> may still flip to true shortly
        /// afterwards (listen to <see cref="ModeChanged"/>).</summary>
        public bool Initialized { get; private set; }

        // Live-session credentials, exposed so the additive v2 (Phase 2) surface reuses the SAME
        // session + bearer token instead of creating a second session (master §3). Null in demo mode.
        public string LiveSessionId => (Gateway as LiveAgentGateway)?.SessionId;
        public string LiveToken => (Gateway as LiveAgentGateway)?.Token;
        public string ApiBaseUrl => settings != null ? settings.apiBaseUrl : null;
        public string WebSocketBaseUrl => settings != null ? settings.webSocketBaseUrl : null;

        /// <summary>True while a run is in flight. Demo: exactly the scripted run's lifetime. Live:
        /// tracked from the backend's events (the backend stays the authority; it refuses a second
        /// start with RUN_IN_PROGRESS whatever this says).</summary>
        public bool RunInProgress => IsLive ? _liveRunActive : (_demo != null && _demo.RunInProgress);

        /// <summary>True once any run has been started in this launch (by a card, a button or the
        /// idle timer). UI can show "Start demo" before and "Replay" after.</summary>
        public bool HasStartedRun { get; private set; }

        /// <summary>Raised on the main thread whenever <see cref="RunInProgress"/> or
        /// <see cref="HasStartedRun"/> changes.</summary>
        public event Action RunStateChanged;

        /// <summary>Raised on the main thread after the session moved from the offline demo to the
        /// live backend (<see cref="IsLive"/> is already true and the live snapshot is loaded).</summary>
        public event Action ModeChanged;

        CancellationTokenSource _cts;
        int _idemCounter;
        DemoAgentGateway _demo;          // the scripted session while in demo mode, else null
        IRunBoardLink _board;            // the task board, when one is attached
        bool _reducerHooked;
        bool _demoInteracted;            // the user did something on the demo board
        bool _liveRunActive;
        bool _lastRunState;
        bool _lastHasStarted;

        /// <summary>A decision response could not be delivered (decisionId, human-readable reason).
        /// The paper restores its buttons and the stamps re-arm, so the user can retry.</summary>
        public event Action<string, string> RespondFailed;

        string _endpoint = "(offline)";

        async void Start()
        {
            _cts = new CancellationTokenSource();
            try { await InitializeAsync(_cts.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Without this an exception here is swallowed by async void and every dependent
                // system waits on Initialized forever with nothing in the log.
                Debug.LogError($"[MuseDesk] session initialization failed: {ex}");
                if (diagnostics != null) diagnostics.SetMode("ERROR · Could not start a session", "(none)");
            }
        }

        void Update()
        {
            // The socket raises ConnectionChanged on a background thread; reflect it here.
            int dirty = System.Threading.Interlocked.Exchange(ref _connectionDirtyBacking, 0);
            if (dirty == 0 || !IsLive || Gateway == null || diagnostics == null) return;
            diagnostics.SetMode(dirty == 1 ? Gateway.ModeLabel : "LIVE · Reconnecting to desktop…", _endpoint);
            if (dirty == 2) diagnostics.note = "connection lost; nothing is being simulated";
        }

        int _connectionDirtyBacking;
        void OnConnectionChanged(bool connected) =>
            System.Threading.Interlocked.Exchange(ref _connectionDirtyBacking, connected ? 1 : 2);

        void OnDestroy()
        {
            try { _cts?.Cancel(); } catch { }
            if (Gateway != null) Gateway.EventReceived -= OnEvent;
            if (_demo != null)
            {
                _demo.RunStateChanged -= OnDemoRunStateChanged;
                _demo.Shutdown();
            }
            if (dispatcher != null)
            {
                dispatcher.ConnectionChanged -= OnConnectionChanged;
                dispatcher.Disconnect();
            }
        }

        async Task InitializeAsync(CancellationToken ct)
        {
            // 1) The offline demo is ready immediately: no network, nothing to wait for.
            var fixture = DemoAgentGateway.LoadFixture();
            var demo = new DemoAgentGateway(fixture);
            var demoSnap = await demo.StartSessionAsync(ct);
            _demo = demo;
            demo.RunStateChanged += OnDemoRunStateChanged;
            AdoptGateway(demo, demoSnap, isLive: false, endpoint: "(offline)");

            // 2) Look for the desktop in the background. Never blocks the demo.
            bool wantLive = settings != null && !settings.preferOfflineDemo;
            if (wantLive && dispatcher != null)
                await TryGoLiveAsync(ct);
        }

        /// <summary>Attempt the live handshake. Success switches to live only if the user has not
        /// started anything in the demo; failure just leaves the demo running.</summary>
        async Task TryGoLiveAsync(CancellationToken ct)
        {
            LiveAgentGateway live;
            SessionSnapshot snap;
            try
            {
                live = new LiveAgentGateway(settings.apiBaseUrl, settings.webSocketBaseUrl, dispatcher);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(TimeSpan.FromSeconds(settings.connectTimeoutSeconds));
                snap = await live.StartSessionAsync(linked.Token);
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;   // scene tearing down
                Debug.Log($"[MuseDesk] no desktop backend reachable ({ex.Message}); staying in the offline demo.");
                return;
            }
            if (this == null || ct.IsCancellationRequested) return;

            if (DemoEngaged)
            {
                // The user is already in the scripted demo: do not pull it out from under them.
                Debug.Log("[MuseDesk] a desktop backend is available, but the scripted demo has already " +
                          "been started; staying in the demo for this launch. Relaunch to use live mode.");
                if (dispatcher != null) dispatcher.Disconnect();   // StartSessionAsync opened the socket
                return;
            }

            SwitchToLive(live, snap);
        }

        /// <summary>True once the user has started a run or changed the demo board.</summary>
        bool DemoEngaged => HasStartedRun || _demoInteracted || (_demo != null && _demo.RunInProgress);

        void SwitchToLive(LiveAgentGateway live, SessionSnapshot snap)
        {
            // Clear every view through the normal reset path while the demo is still attached, then
            // retire it for good so nothing scripted can arrive in live mode.
            if (_demo != null)
            {
                _demo.ResetRun("switch_to_live");
                _demo.RunStateChanged -= OnDemoRunStateChanged;
                _demo.Shutdown();
            }
            if (Gateway != null) Gateway.EventReceived -= OnEvent;
            _demo = null;
            PendingDecisionId = null;
            _liveRunActive = false;

            AdoptGateway(live, snap, isLive: true, endpoint: settings.apiBaseUrl);
            Debug.Log("[MuseDesk] switched from the offline demo to the LIVE desktop backend.");
            try { ModeChanged?.Invoke(); }
            catch (Exception ex) { Debug.LogError($"[MuseDesk] mode-change listener error: {ex.Message}"); }
            RefreshRunState();
        }

        void AdoptGateway(IAgentGateway gateway, SessionSnapshot snap, bool isLive, string endpoint)
        {
            Gateway = gateway;
            IsLive = isLive;
            _endpoint = endpoint;
            if (!_reducerHooked)
            {
                _reducerHooked = true;
                Reducer.DecisionResolved += (id, _) => { if (id == PendingDecisionId) PendingDecisionId = null; };
                Reducer.SessionReset += () => { PendingDecisionId = null; _liveRunActive = false; };
            }
            Reducer.LoadSnapshot(snap);
            // Resume: a decision that was already awaiting a human when we (re)connected has no
            // decision.requested event coming, so adopt it from the snapshot.
            PendingDecisionId = Reducer.FirstPendingDecision()?.DecisionId;
            if (isLive && !string.IsNullOrEmpty(PendingDecisionId)) _liveRunActive = true;
            gateway.EventReceived += OnEvent;
            if (isLive && dispatcher != null) dispatcher.ConnectionChanged += OnConnectionChanged;
            if (diagnostics != null)
            {
                diagnostics.SetMode(gateway.ModeLabel, endpoint);
                diagnostics.SetEvent(Reducer.LastSeq, "snapshot");
            }
            Initialized = true;
            Debug.Log($"[MuseDesk] session ready ({gateway.ModeLabel}); agents={snap?.Agents?.Count}");
        }

        /// <summary>Approve the decision currently awaiting a human (e.g. from a hand-poked button).</summary>
        public void ApprovePending() => RespondPending("approve");
        public void RejectPending() => RespondPending("reject");
        public void RequestChangesPending() => RespondPending("request_changes");

        void RespondPending(string action)
        {
            if (!string.IsNullOrEmpty(PendingDecisionId))
                Respond(PendingDecisionId, action, $"{action} via hand UI");
            else
                Debug.Log($"[MuseDesk] {action}: no decision awaiting.");
        }

        /// <summary>Ask a contextual question about the pending decision; answer returned via callback.</summary>
        public async void AskPending(string question, Action<string> onAnswer, string agentId = "researcher")
        {
            if (Gateway == null || string.IsNullOrEmpty(PendingDecisionId))
            {
                onAnswer?.Invoke("(no decision to ask about yet)");
                return;
            }
            try
            {
                var ans = await Gateway.AskAsync(string.IsNullOrEmpty(agentId) ? "researcher" : agentId, PendingDecisionId, question, _cts.Token);
                onAnswer?.Invoke(ans?.Answer ?? "(no answer)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MuseDesk] ask failed: {ex.Message}");
                onAnswer?.Invoke("(couldn't reach the backend for an answer)");
            }
        }

        void OnEvent(EventEnvelope e)
        {
            // Set before Apply so any handler reading PendingDecisionId during decision.requested
            // sees the new decision, whatever order the subscribers were added in.
            if (e != null && e.Type == EventTypes.DecisionRequested && e.Seq > Reducer.LastSeq
                && !string.IsNullOrEmpty(e.DecisionId))
                PendingDecisionId = e.DecisionId;
            if (!Reducer.Apply(e)) return;
            if (IsLive) TrackLiveRun(e);
            RefreshRunState();
            if (diagnostics == null) return;
            diagnostics.SetEvent(e.Seq, e.EventId);
            switch (e.Type)
            {
                case EventTypes.DecisionRequested:
                    diagnostics.note = "Needs you: " + e.PayloadString("summary", "approval needed");
                    break;
                case EventTypes.DecisionResolved:
                    diagnostics.note = "Decision " + e.PayloadString("state", "resolved").Replace('_', ' ');
                    break;
                case EventTypes.ArtifactCreated:
                    diagnostics.note = "Result: " + e.PayloadString("summary", "result ready");
                    break;
                case EventTypes.TaskCompleted:
                    diagnostics.note = (e.PayloadString("task_state", "completed") == "cancelled" ? "Stopped: " : "Done: ")
                                       + e.PayloadString("result_summary", "task completed");
                    break;
                case EventTypes.AgentStatusChanged:
                    diagnostics.note = e.AgentId + ": " + e.PayloadString("progress_message", e.PayloadString("state", ""));
                    break;
                case EventTypes.SessionReset:
                    diagnostics.note = "Session reset. Ready to start.";
                    break;
            }
        }

        /// <summary>Live mode: follow the backend's own events to know whether a run is in flight.
        /// A run begins with task.started / decision.requested and ends when work is delivered, the
        /// branch is cancelled, the decision is not approved, or the task fails.</summary>
        void TrackLiveRun(EventEnvelope e)
        {
            switch (e.Type)
            {
                case EventTypes.TaskStarted:
                case EventTypes.DecisionRequested:
                    _liveRunActive = true;
                    HasStartedRun = true;
                    break;
                case EventTypes.DecisionResolved:
                    if (!e.PayloadString("state", "").ToLowerInvariant().Contains("approv")) _liveRunActive = false;
                    break;
                case EventTypes.TaskCompleted:
                    if (!string.IsNullOrEmpty(e.PayloadString("artifact_id"))
                        || e.PayloadString("task_state", "completed") == "cancelled")
                        _liveRunActive = false;
                    break;
                case EventTypes.TaskFailed:
                case EventTypes.SessionReset:
                    _liveRunActive = false;
                    break;
            }
        }

        void OnDemoRunStateChanged()
        {
            if (_demo != null && _demo.RunInProgress) HasStartedRun = true;
            RefreshRunState();
        }

        void RefreshRunState()
        {
            bool now = RunInProgress;
            bool started = HasStartedRun;
            if (now == _lastRunState && started == _lastHasStarted) return;
            _lastRunState = now;
            _lastHasStarted = started;
            try { RunStateChanged?.Invoke(); }
            catch (Exception ex) { Debug.LogError($"[MuseDesk] run-state listener error: {ex.Message}"); }
        }

        // ── board link ──

        /// <summary>Attach the task board so <see cref="StartDemo"/> can start through its executable
        /// card. Called by Phase2Bootstrap once the board is up; pass null to detach.</summary>
        public void AttachBoard(IRunBoardLink board) => _board = board;

        /// <summary>The user changed something on the offline demo board (created / assigned / paused
        /// a card). From then on a late live connection no longer replaces the demo. No-op in live.</summary>
        public void NotifyDemoInteraction()
        {
            if (!IsLive) _demoInteracted = true;
        }

        // ── commands the presentation layer calls ──

        /// <summary>
        /// Poke-button entry point: start the run if none is in progress.
        /// Offline demo: starts the scripted run — THROUGH the board's executable card when it is
        /// still unstarted (so the card and the session stay consistent), otherwise directly. If the
        /// executable card has already run (a replay), the demo is reset first so the board is
        /// seeded again and the new run goes through the card.
        /// Live: issues the real start (the card's assign-to-Researcher command when the board has
        /// an unstarted executable card, else the real v1 start). Nothing is fabricated.
        /// </summary>
        public void StartDemo()
        {
            if (!Initialized || Gateway == null)
            {
                Debug.Log("[MuseDesk] StartDemo: the session is not ready yet.");
                return;
            }
            if (RunInProgress)
            {
                Debug.Log("[MuseDesk] StartDemo: a run is already in progress.");
                return;
            }
            if (!IsLive && _board != null && _board.ExecutableCardSpent) ResetDemo();
            if (_board != null && _board.TryStartThroughCard()) return;
            StartSampleTask();
        }

        /// <summary>
        /// Poke-button entry point: stop the scripted run and put the offline demo back to its
        /// start. Cancels the run's pending delays (no late events can arrive), emits session.reset
        /// so every view clears through SessionReducer.SessionReset, returns the agents to idle,
        /// restores the demo board to its seeded cards, and leaves the app ready for StartDemo.
        /// Live: there is no safe server-side equivalent, so this logs and changes nothing.
        /// </summary>
        public void ResetDemo()
        {
            if (!Initialized) return;
            if (IsLive)
            {
                Debug.Log("[MuseDesk] ResetDemo ignored: connected to the live desktop backend, " +
                          "which has no reset. Nothing was changed.");
                return;
            }
            if (_demo == null) return;
            _demo.ResetRun();   // session.reset → reducer → SessionReset → views clear, board re-seeds
            PendingDecisionId = null;
            RefreshRunState();
        }

        /// <summary>Start the auth_demo run directly on the active gateway (no board card). Prefer
        /// <see cref="StartDemo"/> from UI: it keeps the task board consistent.</summary>
        public async void StartSampleTask()
        {
            if (Gateway == null) return;
            if (!IsLive && RunInProgress)
            {
                Debug.Log("[MuseDesk] start ignored: the scripted run is already in progress.");
                return;
            }
            try
            {
                await Gateway.StartTaskAsync("auth_demo", NextKey("task"), _cts.Token);
                if (IsLive) { _liveRunActive = true; HasStartedRun = true; RefreshRunState(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.LogError($"[MuseDesk] start task failed: {ex.Message}"); }
        }

        public async void Respond(string decisionId, string action, string comment = null)
        {
            if (Gateway == null || string.IsNullOrEmpty(decisionId)) return;
            try
            {
                var res = await Gateway.RespondToDecisionAsync(decisionId, action,
                    NextKey($"resolve-{decisionId}"), comment, _cts.Token);
                Debug.Log($"[MuseDesk] decision {decisionId} -> {res.State} (applied={res.Applied})");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogError($"[MuseDesk] respond failed: {ex.Message}");
                RespondFailed?.Invoke(decisionId, IsLive
                    ? "Couldn't reach the desktop. Your decision was NOT saved. Try again."
                    : "Couldn't save the decision. Try again.");
            }
        }

        string NextKey(string prefix) => $"{prefix}-{SystemInfo.deviceUniqueIdentifier[..6]}-{++_idemCounter:D4}";
    }
}
