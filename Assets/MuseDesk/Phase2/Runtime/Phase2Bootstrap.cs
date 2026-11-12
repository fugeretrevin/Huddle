using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using MuseDesk.Networking;
using MuseDesk.Phase2.Board;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Phase2
{
    /// <summary>
    /// Single wire-up point that turns the Phase 2 task board ON in demo mode and feeds it. This is
    /// the integrator bootstrap the board classes expect (BoardPresenter.Initialize is deliberately
    /// NOT called by anything else). It is additive and completely inert unless <see cref="enableBoard"/>
    /// is true, and it never touches Phase 1 systems.
    ///
    /// Lifecycle (mirrors MuseDesk.Networking.SessionController's main-thread async pattern):
    ///   1. flip the feature flags ON (Phase2Flags.Phase2Enabled + Board) BEFORE Initialize, because
    ///      BoardPresenter.Initialize no-ops while Phase2Flags.BoardActive is false;
    ///   2. construct the offline Phase2DemoGateway (honestly source_mode="demo" — stamped by the
    ///      gateway and the built-in fixture, never inferred here);
    ///   3. create a Phase2Reducer, hydrate it from the gateway snapshot;
    ///   4. route gateway.EventReceived -> reducer.Apply (so scripted timeline deltas land);
    ///   5. BoardPresenter.Initialize(gateway, reducer) builds the card views.
    ///
    /// Everything runs on the Unity main thread: the demo gateway completes GetSnapshotAsync
    /// synchronously and raises EventReceived inline from awaited main-thread methods, exactly like
    /// SessionController relies on for v1. No SynchronizationContext juggling is required.
    ///
    /// Demo-only for now: no Wit.ai / backend token needed, so the board is visible in an offline
    /// build. A live gateway can be swapped in later without changing the presenter.
    /// </summary>
    [DisallowMultipleComponent]
    public class Phase2Bootstrap : MonoBehaviour
    {
        [Header("Feature")]
        [Tooltip("Master switch for this bootstrap. When false NOTHING here runs and the Phase 2 flags " +
                 "stay off, so the component is safe to leave in the scene.")]
        public bool enableBoard = true;

        [Header("Wiring (assigned by AgentScripts/BuildBoard)")]
        [Tooltip("The board presenter to initialize. Its cardPrefab + region anchors are wired by the builder.")]
        public BoardPresenter boardPresenter;

        [Tooltip("The station drop zones (informational / keep-alive reference; they self-register via " +
                 "StationDropZone.Active in OnEnable, so this array is not strictly required).")]
        public StationDropZone[] dropZones;

        [Header("Live backend (shares the v1 session + token)")]
        [Tooltip("Use the real v2 backend when the Phase 1 session is live; fall back to the offline " +
                 "demo otherwise. The board commands then hit the real /v2 endpoints.")]
        public bool preferLiveWhenAvailable = true;
        [Tooltip("Phase 1 session controller to borrow the live session id + token from. Auto-found if null.")]
        public SessionController sessionController;
        [Tooltip("The v2 WebSocket pump. Auto-added at runtime if null (live mode only).")]
        public Phase2EventChannel eventChannel;
        [Tooltip("Seconds to wait for the v1 session to decide live/demo before falling back to the demo board.")]
        [Range(1f, 15f)] public float liveWaitTimeout = 8f;

        [Header("Demo timing")]
        [Tooltip("Seconds between scripted-timeline events (only matters if a fixture supplies a timeline; " +
                 "the built-in default has none).")]
        [Range(0f, 8f)] public float timelineStepSeconds = 1.0f;
        [Tooltip("Play the fixture's scripted timeline after hydrating (safe no-op when empty).")]
        public bool playScriptedTimeline = true;

        public Phase2Reducer Reducer { get; private set; }
        public IPhase2Gateway Gateway { get; private set; }
        public bool Ready { get; private set; }

        Phase2DemoGateway _demo;
        CancellationTokenSource _cts;

        async void Start()
        {
            if (!enableBoard)
            {
                // Inert: leave every Phase 2 flag off so Phase 1 is unaffected.
                return;
            }
            if (boardPresenter == null)
            {
                Debug.LogWarning("[MuseDesk.P2] Phase2Bootstrap.enableBoard is true but boardPresenter is " +
                                 "not assigned; run AgentScripts/BuildBoard.Run() to wire it.");
                return;
            }

            _cts = new CancellationTokenSource();
            try { await InitializeAsync(_cts.Token); }
            catch (OperationCanceledException) { /* scene tearing down */ }
            catch (Exception ex) { Debug.LogError($"[MuseDesk.P2] Phase2Bootstrap init failed: {ex.Message}"); }
        }

        async Task InitializeAsync(CancellationToken ct)
        {
            // 1) Turn the board feature ON. Must happen BEFORE BoardPresenter.Initialize, which gates on
            //    Phase2Flags.BoardActive (== Phase2Enabled && Board).
            Phase2Flags.Phase2Enabled = true;
            Phase2Flags.Board = true;

            // 2) Choose the gateway: the real v2 backend when the v1 session is live (reusing its
            //    session + token), else the offline, honestly-stamped demo. source_mode is stamped by
            //    the chosen gateway, never inferred here.
            Phase2LiveGateway liveGateway = await ResolveLiveGatewayAsync(ct);
            if (liveGateway != null)
            {
                Gateway = liveGateway;
            }
            else
            {
                // LoadDefaultFixture() returns null when no phase2_demo.json is present; the ctor then
                // substitutes the built-in default state (~2 backlog cards).
                _demo = new Phase2DemoGateway(Phase2DemoGateway.LoadDefaultFixture(), timelineStepSeconds);
                Gateway = _demo;
            }

            // 3) Reducer + snapshot hydrate. Demo completes synchronously; live does a REST round-trip.
            Reducer = new Phase2Reducer();
            var snapshot = await Gateway.GetSnapshotAsync(ct);
            Reducer.LoadSnapshot(snapshot);

            // 3b) Live only: open the v2 event socket resuming at the snapshot's last_seq (after the
            //     snapshot so we don't replay the whole v2 log over the WS).
            if (liveGateway != null)
                liveGateway.Connect(sessionController.LiveSessionId, sessionController.LiveToken,
                                    snapshot != null ? snapshot.LastSeq : 0);

            // 4) Route events. Phase2Reducer.Apply returns bool, so it cannot be used as an
            //    Action<EventEnvelopeV2> method group directly — wrap it (the bool result is ignored,
            //    matching how SessionController discards Reducer.Apply's return in OnEvent).
            Gateway.EventReceived += OnPhase2Event;

            // 5) Build the card views. Initialize re-raises RebuildAll from the already-loaded reducer.
            boardPresenter.Initialize(Gateway, Reducer);
            Ready = true;

            Debug.Log($"[MuseDesk.P2] board up ({Gateway.ModeLabel}); tasks={snapshot?.Tasks?.Count ?? 0}");

            // Optional: replay any scripted timeline (empty for the built-in default → immediate return).
            if (playScriptedTimeline && _demo != null)
            {
                try { await _demo.PlayScriptedTimelineAsync(ct); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Debug.LogWarning($"[MuseDesk.P2] scripted timeline error: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Returns a connected-credential live v2 gateway when the Phase 1 session is live, else null
        /// (caller then uses the demo). Waits, bounded, for the v1 session to decide live vs demo so
        /// we reuse its session id + token rather than creating a second session.
        /// </summary>
        async Task<Phase2LiveGateway> ResolveLiveGatewayAsync(CancellationToken ct)
        {
            if (!preferLiveWhenAvailable) return null;
            if (sessionController == null)
                sessionController = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            if (sessionController == null) return null;

            float waited = 0f;
            while (!sessionController.Initialized && waited < liveWaitTimeout && !ct.IsCancellationRequested)
            {
                await Task.Delay(100, ct);
                waited += 0.1f;
            }
            if (!sessionController.Initialized || !sessionController.IsLive) return null;

            string sid = sessionController.LiveSessionId;
            string token = sessionController.LiveToken;
            if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(token))
            {
                Debug.LogWarning("[MuseDesk.P2] v1 session is live but exposes no id/token; using demo board.");
                return null;
            }

            if (eventChannel == null)
                eventChannel = GetComponent<Phase2EventChannel>() ?? gameObject.AddComponent<Phase2EventChannel>();

            var live = new Phase2LiveGateway(sessionController.ApiBaseUrl,
                                             sessionController.WebSocketBaseUrl, eventChannel);
            live.SetSession(sid, token);
            Debug.Log("[MuseDesk.P2] board using LIVE v2 backend (shared v1 session).");
            return live;
        }

        void OnPhase2Event(EventEnvelopeV2 e)
        {
            if (Reducer == null) return;
            Reducer.Apply(e);   // bool result intentionally ignored (dup/gap handled inside the reducer)
        }

        void OnDestroy()
        {
            if (Gateway != null) Gateway.EventReceived -= OnPhase2Event;
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
        }
    }
}
