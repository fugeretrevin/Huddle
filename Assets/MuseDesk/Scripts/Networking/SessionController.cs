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

        CancellationTokenSource _cts;
        int _idemCounter;

        async void Start()
        {
            _cts = new CancellationTokenSource();
            await InitializeAsync(_cts.Token);
        }

        void OnDestroy()
        {
            try { _cts?.Cancel(); } catch { }
            if (dispatcher != null) dispatcher.Disconnect();
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
            Reducer.LoadSnapshot(snap);
            Reducer.DecisionRequested += (id, _) => PendingDecisionId = id;
            Reducer.DecisionResolved += (id, _) => { if (id == PendingDecisionId) PendingDecisionId = null; };
            gateway.EventReceived += OnEvent;
            if (diagnostics != null)
            {
                diagnostics.SetMode(gateway.ModeLabel, endpoint);
                diagnostics.SetEvent(Reducer.LastSeq, "snapshot");
            }
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
        public void ApprovePending()
        {
            if (!string.IsNullOrEmpty(PendingDecisionId))
                Respond(PendingDecisionId, "approve", "Approved via foundation poke button");
            else
                Debug.Log("[MuseDesk] ApprovePending: no decision awaiting.");
        }

        void OnEvent(EventEnvelope e)
        {
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
            catch (Exception ex) { Debug.LogError($"[MuseDesk] respond failed: {ex.Message}"); }
        }

        string NextKey(string prefix) => $"{prefix}-{SystemInfo.deviceUniqueIdentifier[..6]}-{++_idemCounter:D4}";
    }
}
