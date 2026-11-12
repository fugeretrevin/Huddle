using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Demo
{
    /// <summary>
    /// Unattended "it demos itself" director for the PUBLIC OFFLINE build (§14). It makes the
    /// judged APK tell its own story with no PC, no network, and no human required — while never
    /// crossing the two lines the product is built on:
    ///
    ///   1. HUMAN-IN-THE-LOOP IS THE THESIS. By default this director NEVER answers a decision for
    ///      the human. <see cref="autoResolveIfIdle"/> is FALSE, so a pending decision waits forever
    ///      for a real approval — exactly what a judge should experience when they put the headset on.
    ///      The unattended auto-resolve is an OPT-IN kiosk convenience: it only triggers after a long
    ///      idle timeout, it only fires if the decision is STILL pending (a human who already answered
    ///      clears it and this no-ops), and it resolves through the gateway's normal
    ///      <see cref="SessionController.ApprovePending"/> path — the identical code a hand-poke runs,
    ///      so nothing is faked or short-circuited.
    ///
    ///   2. NEVER PRESENT DEMO AS LIVE. This component stands down completely in live mode
    ///      (<see cref="SessionController.IsLive"/> == true): it disables itself and does nothing, so
    ///      it can never auto-drive or auto-answer real backend work. Every event the judge sees still
    ///      comes from <see cref="DemoAgentGateway"/>, honestly stamped source_mode="demo".
    ///
    /// It also does NOT fabricate any events itself: it only calls SessionController's public command
    /// methods (<see cref="SessionController.StartSampleTask"/>, <see cref="SessionController.ApprovePending"/>),
    /// so the demo presentation path is byte-for-byte the same one live mode uses.
    ///
    /// Start behavior: <see cref="SessionController"/> already auto-starts the sample task when its own
    /// <c>autoStartSampleTask</c> is on, so this director detects that and does NOT double-start. It only
    /// issues a start itself when the controller's auto-start is OFF (or to begin a new loop cycle).
    ///
    /// Everything runs on the Unity main thread, mirroring SessionController's async pattern (async void
    /// Start, a CancellationTokenSource cancelled in OnDestroy). It is safe to leave in the scene: with
    /// <see cref="enableAutoDemo"/> off, or in live mode, it is completely inert.
    /// </summary>
    [DisallowMultipleComponent]
    public class DemoDirector : MonoBehaviour
    {
        [Header("Feature")]
        [Tooltip("Master switch. When false this component does nothing at all.")]
        public bool enableAutoDemo = true;

        [Tooltip("Session controller to observe + drive. Auto-found if null.")]
        public SessionController sessionController;

        [Header("Start")]
        [Tooltip("Seconds to wait before this director starts the sample task ITSELF. Only used when the " +
                 "SessionController's own autoStartSampleTask is OFF; otherwise the controller owns the start " +
                 "and this director never double-starts.")]
        [Range(0f, 15f)] public float startDelaySeconds = 4f;

        [Header("Unattended kiosk (opt-in, OFF by default)")]
        [Tooltip("DANGER TO THE THESIS IF MISUSED. When false (default) a pending decision waits forever " +
                 "for a real human — the correct behavior for a judged demo. When true, a decision left " +
                 "untouched for idleResolveSeconds is approved automatically so an unattended booth loop can " +
                 "complete. It still routes through the normal gateway path and stays source_mode=demo, and " +
                 "it no-ops if a human already answered.")]
        public bool autoResolveIfIdle = false;

        [Tooltip("How long a decision may sit unanswered before the opt-in kiosk auto-resolve fires. " +
                 "Generous on purpose so a human reading the paper is never cut off.")]
        [Range(8f, 120f)] public float idleResolveSeconds = 30f;

        [Tooltip("Action the unattended auto-resolve takes: approve / reject / request_changes.")]
        public string autoResolveAction = "approve";

        [Header("Loop (opt-in, OFF by default)")]
        [Tooltip("When true, the director restarts a fresh cycle a few seconds after each task completes, " +
                 "for an unattended booth. Default false so a single visit is not surprised by a restart.")]
        public bool loop = false;

        [Tooltip("Seconds to pause after a cycle completes before starting the next (loop only).")]
        [Range(1f, 30f)] public float loopPauseSeconds = 6f;

        CancellationTokenSource _cts;          // lifetime of this component
        CancellationTokenSource _idleCts;      // per-decision idle-resolve timer
        SessionReducer _reducer;
        bool _subscribed;
        bool _standingDown;
        bool _cycleInFlight;

        async void Start()
        {
            if (!enableAutoDemo) return;
            _cts = new CancellationTokenSource();
            try { await RunAsync(_cts.Token); }
            catch (OperationCanceledException) { /* scene tearing down */ }
            catch (Exception ex) { Debug.LogWarning($"[MuseDesk.Demo] DemoDirector error: {ex.Message}"); }
        }

        async Task RunAsync(CancellationToken ct)
        {
            if (sessionController == null)
                sessionController = UnityEngine.Object.FindAnyObjectByType<SessionController>();
            if (sessionController == null)
            {
                Debug.LogWarning("[MuseDesk.Demo] No SessionController found; DemoDirector inert.");
                return;
            }

            // Wait (bounded) for the live/demo choice to be made and the session hydrated.
            float waited = 0f;
            while (!sessionController.Initialized && waited < 20f && !ct.IsCancellationRequested)
            {
                await Task.Delay(100, ct);
                waited += 0.1f;
            }
            if (!sessionController.Initialized || sessionController.Gateway == null)
            {
                Debug.Log("[MuseDesk.Demo] session did not initialize in time; DemoDirector inert.");
                return;
            }

            // LINE #2: never run against live. Stand down completely.
            if (sessionController.IsLive)
            {
                _standingDown = true;
                Debug.Log("[MuseDesk.Demo] live mode — DemoDirector standing down (no auto-drive).");
                enabled = false;
                return;
            }

            Debug.Log($"[MuseDesk.Demo] demo mode ({sessionController.Gateway.ModeLabel}); " +
                      $"autoResolveIfIdle={autoResolveIfIdle}, loop={loop}.");

            Subscribe();

            // Start: the controller auto-starts the sample task itself when autoStartSampleTask is on, so
            // only start here when it is OFF — detect-and-no-op to avoid a double task.
            if (!sessionController.autoStartSampleTask)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(startDelaySeconds), ct); }
                catch (OperationCanceledException) { return; }
                if (string.IsNullOrEmpty(sessionController.PendingDecisionId))
                {
                    _cycleInFlight = true;
                    sessionController.StartSampleTask();
                }
            }
            else
            {
                Debug.Log("[MuseDesk.Demo] SessionController owns the start (autoStartSampleTask on); not double-starting.");
                _cycleInFlight = true; // the controller's auto-started cycle is now in flight
            }
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _reducer = sessionController.Reducer;
            if (_reducer == null) return;
            _reducer.DecisionRequested += OnDecisionRequested;
            _reducer.DecisionResolved += OnDecisionResolved;
            _reducer.TaskCompleted += OnTaskCompleted;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed || _reducer == null) return;
            _reducer.DecisionRequested -= OnDecisionRequested;
            _reducer.DecisionResolved -= OnDecisionResolved;
            _reducer.TaskCompleted -= OnTaskCompleted;
            _subscribed = false;
        }

        // ── reducer event handlers (raised on the main thread) ──

        void OnDecisionRequested(string decisionId, EventEnvelope _evt)
        {
            if (_standingDown) return;
            // Arm the OPT-IN idle auto-resolve. With autoResolveIfIdle false (default), the decision simply
            // waits for a real human — which is the whole point of the demo.
            if (autoResolveIfIdle && !string.IsNullOrEmpty(decisionId))
                _ = ArmIdleResolveAsync(decisionId);
        }

        void OnDecisionResolved(string decisionId, string state)
        {
            // A human (or the kiosk timer) answered — cancel any pending idle timer immediately.
            CancelIdleTimer();
            // A rejected / changes-requested decision ends the scripted cycle without a completion
            // (nothing was built), so the loop restarts from here instead of from task.completed.
            string s = (state ?? "").ToLowerInvariant();
            if (!s.Contains("approv"))
            {
                _cycleInFlight = false;
                if (loop && !_standingDown) _ = LoopRestartAsync();
            }
        }

        void OnTaskCompleted(EventEnvelope _evt)
        {
            _cycleInFlight = false;
            if (loop && !_standingDown)
                _ = LoopRestartAsync();
        }

        // ── opt-in unattended behaviors ──

        /// <summary>
        /// Opt-in kiosk safety valve: after a generous idle window, if the decision is STILL pending
        /// (no human answered), resolve it through the normal gateway path. No-ops the instant a human
        /// has already resolved it, so it can never override or misrepresent a real approval.
        /// </summary>
        async Task ArmIdleResolveAsync(string decisionId)
        {
            CancelIdleTimer();
            _idleCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            var token = _idleCts.Token;
            try { await Task.Delay(TimeSpan.FromSeconds(idleResolveSeconds), token); }
            catch (OperationCanceledException) { return; }   // human answered, or we were cancelled

            // Only act if this exact decision is still the one awaiting a human.
            if (!token.IsCancellationRequested &&
                sessionController != null &&
                sessionController.PendingDecisionId == decisionId)
            {
                Debug.Log($"[MuseDesk.Demo] kiosk idle timeout — auto-resolving {decisionId} " +
                          $"({autoResolveAction}) through the normal gateway (still source_mode=demo).");
                ResolvePending(autoResolveAction);
            }
        }

        void ResolvePending(string action)
        {
            if (sessionController == null) return;
            switch ((action ?? "approve").ToLowerInvariant())
            {
                case "reject": sessionController.RejectPending(); break;
                case "request_changes": sessionController.RequestChangesPending(); break;
                default: sessionController.ApprovePending(); break;
            }
        }

        async Task LoopRestartAsync()
        {
            if (_cycleInFlight) return;
            try { await Task.Delay(TimeSpan.FromSeconds(loopPauseSeconds), _cts.Token); }
            catch (OperationCanceledException) { return; }
            if (_standingDown || sessionController == null) return;
            if (!string.IsNullOrEmpty(sessionController.PendingDecisionId)) return; // still mid-cycle
            _cycleInFlight = true;
            sessionController.StartSampleTask();
        }

        /// <summary>
        /// Optional integration hook: an interaction script (e.g. the grab/poke layer) may call this to
        /// signal a human is present, which cancels any pending unattended auto-resolve for the current
        /// decision. Not required — wiring it is a belt-and-suspenders nicety on top of the "still pending"
        /// guard. Safe to call from the main thread at any time.
        /// </summary>
        public void NotifyHumanInteraction() => CancelIdleTimer();

        void CancelIdleTimer()
        {
            try { _idleCts?.Cancel(); } catch { }
            _idleCts?.Dispose();
            _idleCts = null;
        }

        void OnDestroy()
        {
            Unsubscribe();
            CancelIdleTimer();
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
        }
    }
}
