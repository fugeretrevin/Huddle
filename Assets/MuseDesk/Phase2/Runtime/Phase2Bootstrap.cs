using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using MuseDesk.Demo;
using MuseDesk.Networking;
using MuseDesk.Phase2.Board;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Phase2
{
    /// <summary>
    /// Single wire-up point that turns the Phase 2 task board ON and feeds it. This is the
    /// integrator bootstrap the board classes expect (BoardPresenter.Initialize is deliberately
    /// NOT called by anything else). It is additive and completely inert unless <see cref="enableBoard"/>
    /// is true.
    ///
    /// The board follows the session (MuseDesk.Networking.SessionController):
    ///   • The session adopts the offline demo immediately, so the board comes up at once on the
    ///     offline Phase2DemoGateway (honestly source_mode="demo"), connected to the scripted session
    ///     through a DemoBoardRunBridge: assigning the executable card to the Researcher starts the
    ///     scripted run and the card follows it.
    ///   • If the session later switches to the live backend (SessionController.ModeChanged), the
    ///     board switches too: the live v2 snapshot replaces the demo cards and the v2 socket opens,
    ///     reusing the v1 session id + token.
    ///
    /// <see cref="Gateway"/> is a stable relay object for the life of the scene, and <see cref="Reducer"/>
    /// is one reducer instance, so presenters that bound to them once keep working across the switch.
    ///
    /// Everything runs on the Unity main thread: the demo gateway completes GetSnapshotAsync
    /// synchronously and raises EventReceived inline from awaited main-thread methods; the live
    /// channel marshals through its Update pump.
    /// </summary>
    [DisallowMultipleComponent]
    public class Phase2Bootstrap : MonoBehaviour, IRunBoardLink
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
        [Tooltip("Seconds to wait for the v1 session controller to initialize before bringing the board up " +
                 "without it. The session adopts the demo immediately, so this is normally not waited at all.")]
        [Range(1f, 15f)] public float liveWaitTimeout = 8f;

        [Header("Demo timing")]
        [Tooltip("Seconds between scripted-timeline events (only matters if a fixture supplies a timeline; " +
                 "the built-in default has none).")]
        [Range(0f, 8f)] public float timelineStepSeconds = 1.0f;
        [Tooltip("Play the fixture's scripted timeline after hydrating (safe no-op when empty).")]
        public bool playScriptedTimeline = true;

        public Phase2Reducer Reducer { get; private set; }
        /// <summary>The board's gateway. A stable relay: the same object before and after a
        /// demo → live switch (check <c>Gateway.IsLive</c> / <c>Gateway.SourceMode</c> for the mode).</summary>
        public IPhase2Gateway Gateway { get; private set; }
        public bool Ready { get; private set; }
        /// <summary>The offline demo gateway while the board runs on it; null once live. Additive (Wall Habitat
        /// plan runner): lets the offline Manager plan cards be worked the way the backend plan runner works them.</summary>
        public Phase2DemoGateway DemoGateway => Gateway != null && !Gateway.IsLive ? _demo : null;

        Phase2GatewayRelay _relay;
        Phase2DemoGateway _demo;
        Phase2LiveGateway _live;
        DemoBoardRunBridge _bridge;
        CancellationTokenSource _cts;
        bool _sessionHooked;
        bool _switchingToLive;

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

            // 2) Find the v1 session and wait (bounded) until it has adopted a gateway. It adopts the
            //    offline demo immediately, so this normally returns at once.
            await WaitForSessionAsync(ct);

            _relay = new Phase2GatewayRelay();
            Reducer = new Phase2Reducer();

            // 3) Choose the gateway: the real v2 backend if the v1 session is ALREADY live, else the
            //    offline, honestly-stamped demo. source_mode is stamped by the chosen gateway.
            bool live = false;
            if (SessionIsLive())
                live = await TryAdoptLiveAsync(ct);
            if (!live)
                await AdoptDemoAsync(ct);

            // 4) Route events. Subscribed on the relay BEFORE Gateway is published, so the reducer
            //    always applies an event before any presenter that binds later sees it.
            _relay.EventReceived += OnPhase2Event;
            Gateway = _relay;

            // 5) Build the card views. Initialize re-raises RebuildAll from the already-loaded reducer.
            boardPresenter.Initialize(Gateway, Reducer);
            Ready = true;
            Debug.Log($"[MuseDesk.P2] board up ({Gateway.ModeLabel}); tasks={Reducer.Tasks.Count}");

            // 6) Follow the session: let "Start" go through the board, and switch with it if it goes live.
            if (sessionController != null)
            {
                sessionController.AttachBoard(this);
                sessionController.ModeChanged += OnSessionModeChanged;
                _sessionHooked = true;
                if (!live && SessionIsLive()) OnSessionModeChanged();   // it went live while we were starting
            }

            // Optional: replay any scripted timeline (empty for the built-in default → immediate return).
            var demo = _demo;
            if (playScriptedTimeline && demo != null)
            {
                try { await demo.PlayScriptedTimelineAsync(ct); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Debug.LogWarning($"[MuseDesk.P2] scripted timeline error: {ex.Message}"); }
            }
        }

        async Task WaitForSessionAsync(CancellationToken ct)
        {
            if (sessionController == null)
                sessionController = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            if (sessionController == null) return;

            float waited = 0f;
            while (!sessionController.Initialized && waited < liveWaitTimeout && !ct.IsCancellationRequested)
            {
                await Task.Delay(100, ct);
                waited += 0.1f;
            }
        }

        bool SessionIsLive() =>
            preferLiveWhenAvailable && sessionController != null &&
            sessionController.Initialized && sessionController.IsLive;

        /// <summary>Bring the board up on the offline demo gateway and connect it to the scripted
        /// session, so its executable card starts (and follows) the scripted run.</summary>
        async Task AdoptDemoAsync(CancellationToken ct)
        {
            // LoadDefaultFixture() returns null when no phase2_demo.json is present; the ctor then
            // substitutes the built-in default state (one executable card + planned cards).
            _demo = new Phase2DemoGateway(Phase2DemoGateway.LoadDefaultFixture(), timelineStepSeconds);
            _demo.UserCommandAccepted += OnDemoBoardCommand;

            var scripted = sessionController != null ? sessionController.Gateway as DemoAgentGateway : null;
            if (scripted != null)
                _bridge = new DemoBoardRunBridge(scripted, sessionController.Reducer, _demo);
            else
                Debug.Log("[MuseDesk.P2] no scripted session to connect the demo board to; its executable " +
                          "card will refuse to start (EXECUTOR_UNAVAILABLE).");

            _relay.SetInner(_demo);
            var snapshot = await _demo.GetSnapshotAsync(ct);   // completes synchronously
            Reducer.LoadSnapshot(snapshot);
        }

        /// <summary>
        /// Move the board onto the live v2 backend, reusing the v1 session id + token. Returns false
        /// (and changes nothing) if the credentials are missing or the v2 snapshot cannot be fetched.
        /// </summary>
        async Task<bool> TryAdoptLiveAsync(CancellationToken ct)
        {
            string sid = sessionController.LiveSessionId;
            string token = sessionController.LiveToken;
            if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(token))
            {
                Debug.LogWarning("[MuseDesk.P2] v1 session is live but exposes no id/token; using demo board.");
                return false;
            }

            if (eventChannel == null)
            {
                // Explicit null check: '??' bypasses Unity's destroyed/missing-object null.
                eventChannel = GetComponent<Phase2EventChannel>();
                if (eventChannel == null) eventChannel = gameObject.AddComponent<Phase2EventChannel>();
            }

            var live = new Phase2LiveGateway(sessionController.ApiBaseUrl,
                                             sessionController.WebSocketBaseUrl, eventChannel);
            live.SetSession(sid, token);

            SnapshotV2 snapshot;
            try { snapshot = await live.GetSnapshotAsync(ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MuseDesk.P2] live v2 snapshot failed ({ex.Message}); the board is not live.");
                return false;
            }
            if (this == null) return false;

            // Retire the demo board (if one was up), then swap the relay's inner gateway and reload
            // the SAME reducer from the live snapshot: SnapshotLoaded makes every presenter rebuild.
            DetachDemo();
            _live = live;
            _relay.SetInner(live);
            Reducer.LoadSnapshot(snapshot);

            // Open the v2 event socket resuming at the snapshot's last_seq (after the snapshot so we
            // don't replay the whole v2 log over the WS).
            live.Connect(sid, token, snapshot != null ? snapshot.LastSeq : 0);
            Debug.Log("[MuseDesk.P2] board using LIVE v2 backend (shared v1 session).");
            return true;
        }

        void DetachDemo()
        {
            if (_bridge != null) { _bridge.Dispose(); _bridge = null; }
            if (_demo != null) { _demo.UserCommandAccepted -= OnDemoBoardCommand; _demo = null; }
        }

        /// <summary>The v1 session moved from the offline demo to the live backend: follow it.</summary>
        void OnSessionModeChanged()
        {
            if (_live != null || _switchingToLive || !SessionIsLive() || _cts == null) return;
            // The scripted session is gone; stop the demo card from trying to start it meanwhile.
            if (_bridge != null) { _bridge.Dispose(); _bridge = null; }
            _ = SwitchToLiveAsync(_cts.Token);
        }

        async Task SwitchToLiveAsync(CancellationToken ct)
        {
            _switchingToLive = true;
            try
            {
                bool ok = await TryAdoptLiveAsync(ct);
                if (!ok)
                    Debug.LogWarning("[MuseDesk.P2] the session is live but the board could not follow; it " +
                                     "keeps showing the offline demo cards (labelled SCRIPTED DEMO), and its " +
                                     "executable card will refuse to start.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.LogError($"[MuseDesk.P2] board switch to live failed: {ex.Message}"); }
            finally { _switchingToLive = false; }
        }

        void OnDemoBoardCommand()
        {
            // The user is using the demo board: a late live connection must not replace it.
            if (sessionController != null) sessionController.NotifyDemoInteraction();
        }

        void OnPhase2Event(EventEnvelopeV2 e)
        {
            if (Reducer == null) return;
            Reducer.Apply(e);   // bool result intentionally ignored (dup/gap handled inside the reducer)
        }

        // ── IRunBoardLink: lets SessionController.StartDemo start through the board ──

        /// <summary>Find the queued executable card that has not run yet and send the normal
        /// assign-to-Researcher command for it (exactly what dropping it on the Researcher sends).
        /// Returns false when the board is not ready or there is no such card.</summary>
        public bool TryStartThroughCard()
        {
            if (!Ready || Reducer == null || Gateway == null || boardPresenter == null) return false;

            TaskCardV2 target = null;
            foreach (var kv in Reducer.Tasks)
            {
                var c = kv.Value;
                if (c == null || !c.Executable || c.TaskState != TaskStates.Queued) continue;
                if (!string.IsNullOrEmpty(c.RunTaskId)) continue;   // already launched once
                if (target == null || c.Priority > target.Priority) target = c;
            }
            if (target == null) return false;

            // Go through the presenter when the card has a view, so the card shows the same
            // pending / confirmed / refused feedback as a hand drop.
            foreach (var view in boardPresenter.ActiveViews)
            {
                if (view == null || view.Data == null || view.Data.TaskId != target.TaskId) continue;
                _ = boardPresenter.RequestAssignAsync(view, Phase2AgentIds.Researcher,
                                                      boardPresenter.NextIdemKey("start"));
                return true;
            }
            _ = AssignWithoutViewAsync(target.TaskId, target.Revision);
            return true;
        }

        async Task AssignWithoutViewAsync(string taskId, int revision)
        {
            try
            {
                var result = await Gateway.AssignAsync(taskId, new AssignCommand
                {
                    TargetAgentId = Phase2AgentIds.Researcher,
                    ExpectedRevision = revision,
                    IdempotencyKey = boardPresenter.NextIdemKey("start"),
                }, _cts != null ? _cts.Token : CancellationToken.None);
                if (result != null && !result.Ok)
                    Debug.Log($"[MuseDesk.P2] start through card refused: {result.Error?.Code} — {result.Error?.Message}");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.LogWarning($"[MuseDesk.P2] start through card failed: {ex.Message}"); }
        }

        /// <summary>True when the board has executable cards and all of them have finished
        /// (completed / cancelled / failed) — a replay needs a reset first.</summary>
        public bool ExecutableCardSpent
        {
            get
            {
                if (!Ready || Reducer == null) return false;
                bool any = false;
                foreach (var kv in Reducer.Tasks)
                {
                    var c = kv.Value;
                    if (c == null || !c.Executable) continue;
                    any = true;
                    if (!TaskStateSets.IsTerminal(c.TaskState)) return false;
                }
                return any;
            }
        }

        // ── Poke/voice-friendly entry points (wire a world-space button's OnClick to these) ──

        /// <summary>Create a new backlog task from the headset. The server's task.created event
        /// populates the board card, so nothing is fabricated locally.</summary>
        public void CreateTask(string title)
        {
            if (boardPresenter == null) return;
            _ = boardPresenter.RequestCreateTaskAsync(title, null, 1, boardPresenter.NextIdemKey("create"));
        }

        /// <summary>Create a default-titled task (for a single "＋ New task" poke button). It is a
        /// planned card: not executable, nothing will run it.</summary>
        public void CreateSampleTask() => CreateTask("New task");

        /// <summary>Create a new EXECUTABLE card (task_template "auth_demo"): assigning it to the
        /// Researcher starts the pipeline run (live) or the scripted run (offline demo). The card
        /// appears from the gateway's task.created event; nothing is fabricated locally.</summary>
        public void CreateExecutableTask()
        {
            if (!Ready || Gateway == null || boardPresenter == null) return;
            _ = CreateExecutableTaskAsync();
        }

        async Task CreateExecutableTaskAsync()
        {
            try
            {
                await Gateway.CreateTaskAsync(new TaskCreateV2
                {
                    Title = Phase2DemoFixture.ExecutableCardTitle,
                    Priority = 3,
                    TaskTemplate = TaskTemplates.AuthDemo,
                    IdempotencyKey = boardPresenter.NextIdemKey("create-exec"),
                }, _cts != null ? _cts.Token : CancellationToken.None);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.LogWarning($"[MuseDesk.P2] create executable task failed: {ex.Message}"); }
        }

        void OnDestroy()
        {
            if (_sessionHooked && sessionController != null)
            {
                sessionController.ModeChanged -= OnSessionModeChanged;
                sessionController.AttachBoard(null);
            }
            if (_relay != null) _relay.EventReceived -= OnPhase2Event;
            DetachDemo();
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
        }
    }
}
