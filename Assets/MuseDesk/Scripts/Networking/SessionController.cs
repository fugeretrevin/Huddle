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
    /// Picks live (backend reachable within the timeout) or the offline demo, then feeds
    /// every event through one reducer so the presentation is identical in both modes.
    /// </summary>
    public class SessionController : MonoBehaviour
    {
        [Header("Wiring")]
        public RuntimeSettings settings;
        public DiagnosticsOverlay diagnostics;
        public EventDispatcher dispatcher;   // required for live mode

        [Header("Demo driving (foundation build)")]
        [Tooltip("Auto-start the sample task shortly after the session is ready.")]
        public bool autoStartSampleTask = true;
        [Range(0f, 8f)] public float autoStartDelay = 3f;

        public SessionReducer Reducer { get; } = new SessionReducer();
        public IAgentGateway Gateway { get; private set; }
        public bool IsLive { get; private set; }
        public string PendingDecisionId { get; private set; }

        /// <summary>True once the live/demo gateway choice has been made and the session hydrated.
        /// The additive Phase 2 bootstrap waits on this before deciding live vs demo.</summary>
        public bool Initialized { get; private set; }

        // Live-session credentials, exposed so the additive v2 (Phase 2) surface reuses the SAME
        // session + bearer token instead of creating a second session (master §3). Null in demo mode.
        public string LiveSessionId => (Gateway as LiveAgentGateway)?.SessionId;
        public string LiveToken => (Gateway as LiveAgentGateway)?.Token;
        public string ApiBaseUrl => settings != null ? settings.apiBaseUrl : null;
        public string WebSocketBaseUrl => settings != null ? settings.webSocketBaseUrl : null;

        CancellationTokenSource _cts;
        int _idemCounter;

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
            if (dispatcher != null)
            {
                dispatcher.ConnectionChanged -= OnConnectionChanged;
                dispatcher.Disconnect();
            }
        }

        async Task InitializeAsync(CancellationToken ct)
        {
            bool wantLive = settings != null && !settings.preferOfflineDemo;
            if (wantLive && dispatcher != null)
            {
                try
                {
                    var live = new LiveAgentGateway(settings.apiBaseUrl, settings.webSocketBaseUrl, dispatcher);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    linked.CancelAfter(TimeSpan.FromSeconds(settings.connectTimeoutSeconds));
                    var snap = await live.StartSessionAsync(linked.Token);
                    AdoptGateway(live, snap, isLive: true, endpoint: settings.apiBaseUrl);
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MuseDesk] live connect failed ({ex.Message}); using offline demo.");
                }
            }

            var fixture = DemoAgentGateway.LoadFixture();
            var demo = new DemoAgentGateway(fixture);
            var demoSnap = await demo.StartSessionAsync(ct);
            AdoptGateway(demo, demoSnap, isLive: false, endpoint: "(offline)");
        }

        void AdoptGateway(IAgentGateway gateway, SessionSnapshot snap, bool isLive, string endpoint)
        {
            Gateway = gateway;
            IsLive = isLive;
            _endpoint = endpoint;
            Reducer.DecisionResolved += (id, _) => { if (id == PendingDecisionId) PendingDecisionId = null; };
            Reducer.LoadSnapshot(snap);
            // Resume: a decision that was already awaiting a human when we (re)connected has no
            // decision.requested event coming, so adopt it from the snapshot.
            PendingDecisionId = Reducer.FirstPendingDecision()?.DecisionId;
            gateway.EventReceived += OnEvent;
            if (isLive && dispatcher != null) dispatcher.ConnectionChanged += OnConnectionChanged;
            if (diagnostics != null)
            {
                diagnostics.SetMode(gateway.ModeLabel, endpoint);
                diagnostics.SetEvent(Reducer.LastSeq, "snapshot");
            }
            Initialized = true;
            Debug.Log($"[MuseDesk] session ready ({gateway.ModeLabel}); agents={snap?.Agents?.Count}");
            if (autoStartSampleTask) _ = AutoStartAsync(_cts.Token);
        }

        async Task AutoStartAsync(CancellationToken ct)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(autoStartDelay), ct); }
            catch (OperationCanceledException) { return; }
            StartSampleTask();
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
            if (diagnostics == null) return;
            diagnostics.SetEvent(e.Seq, e.EventId);
            switch (e.Type)
            {
                case EventTypes.DecisionRequested:
                    diagnostics.note = "DECISION: " + e.PayloadString("summary", "approval needed") +
                                       "  → poke button to approve";
                    break;
                case EventTypes.DecisionResolved:
                    diagnostics.note = "decision " + e.PayloadString("action", "") + "d";
                    break;
                case EventTypes.ArtifactCreated:
                    diagnostics.note = "ARTIFACT: " + e.PayloadString("summary", "result ready");
                    break;
                case EventTypes.TaskCompleted:
                    diagnostics.note = "DONE: " + e.PayloadString("result_summary", "task completed");
                    break;
                case EventTypes.AgentStatusChanged:
                    diagnostics.note = e.AgentId + " → " + e.PayloadString("state", "");
                    break;
            }
        }

        // ── commands the presentation layer calls ──
        public async void StartSampleTask()
        {
            if (Gateway == null) return;
            try { await Gateway.StartTaskAsync("auth_demo", NextKey("task"), _cts.Token); }
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
