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
    ///   2. NEVER PRESENT DEMO AS LIVE. This component does nothing while the session is live
    ///      (<see cref="SessionController.IsLive"/>), including when the session switches to live
    ///      after launch: every timer re-checks the mode before it acts. Every event the judge sees
    ///      still comes from <see cref="DemoAgentGateway"/>, honestly stamped source_mode="demo".
    ///
    /// It does NOT fabricate any events itself: it only calls SessionController's public command
    /// methods (<see cref="SessionController.StartDemo"/>, <see cref="SessionController.ApprovePending"/>).
    ///
    /// Start behavior: nothing starts by itself at launch — the user is guided to start the run
    /// (drop the executable card on the Researcher, or poke a Start key). Only if NO run has been
    /// started after <see cref="idleAutoStartSeconds"/> does the director start the scripted run,
    /// through the same <see cref="SessionController.StartDemo"/> a Start key calls.
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
        [Tooltip("Start the scripted run automatically if the user has not started anything after " +
                 "idleAutoStartSeconds. Offline demo only.")]
        public bool autoStartIfIdle = true;

        [Tooltip("Seconds after the session is ready during which nothing starts by itself (the user is " +
                 "being guided to start it). If no run has been started by then, the scripted run starts.")]
        [Range(0f, 300f)] public float idleAutoStartSeconds = 40f;

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
        [Tooltip("When true, the director replays a fresh cycle a few seconds after each run ends, " +
                 "for an unattended booth (SessionController.StartDemo resets the demo first when the " +
                 "board's executable card has already run). Default false so a single visit is not " +
                 "surprised by a restart.")]
        public bool loop = false;

        [Tooltip("Seconds to pause after a cycle completes before starting the next (loop only).")]
        [Range(1f, 30f)] public float loopPauseSeconds = 6f;

        CancellationTokenSource _cts;          // lifetime of this component
        CancellationTokenSource _idleCts;      // per-decision idle-resolve timer
        SessionReducer _reducer;
        bool _subscribed;
        bool _loopPending;

        /// <summary>True when this director must not act: no session, or the session is live.</summary>
        bool StandingDown => sessionController == null || sessionController.IsLive;

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

            // Wait (bounded) for the session to be adopted and hydrated (immediate for the demo).
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

            Subscribe();
            sessionController.ModeChanged += OnModeChanged;

            // LINE #2: never run against live.
            if (StandingDown)
            {
                Debug.Log("[MuseDesk.Demo] live mode — DemoDirector standing down (no auto-drive).");
                return;
            }

            Debug.Log($"[MuseDesk.Demo] demo mode ({sessionController.Gateway.ModeLabel}); " +
                      $"autoStartIfIdle={autoStartIfIdle} after {idleAutoStartSeconds:0}s, " +
                      $"autoResolveIfIdle={autoResolveIfIdle}, loop={loop}.");

            // Nothing starts by itself while the user is being guided to start it. Only an untouched
            // demo starts on its own, after the idle window.
            if (!autoStartIfIdle) return;
            try { await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, idleAutoStartSeconds)), ct); }
            catch (OperationCanceledException) { return; }
            if (this == null || StandingDown) return;
            if (sessionController.HasStartedRun || sessionController.RunInProgress) return;   // the user started it
            Debug.Log($"[MuseDesk.Demo] nothing started after {idleAutoStartSeconds:0}s — starting the scripted run.");
            sessionController.StartDemo();
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _reducer = sessionController.Reducer;
            if (_reducer == null) return;
            _reducer.DecisionRequested += OnDecisionRequested;
            _reducer.DecisionResolved += OnDecisionResolved;
            _reducer.WorkDelivered += OnWorkDelivered;
            _reducer.SessionReset += OnSessionReset;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed || _reducer == null) return;
            _reducer.DecisionRequested -= OnDecisionRequested;
            _reducer.DecisionResolved -= OnDecisionResolved;
            _reducer.WorkDelivered -= OnWorkDelivered;
            _reducer.SessionReset -= OnSessionReset;
            _subscribed = false;
        }

        // ── session / reducer event handlers (raised on the main thread) ──

        void OnModeChanged()
        {
            // The session went live after launch: drop every unattended timer.
            if (StandingDown)
            {
                CancelIdleTimer();
                Debug.Log("[MuseDesk.Demo] session is now live — DemoDirector standing down.");
            }
        }

        void OnDecisionRequested(string decisionId, EventEnvelope _evt)
        {
            if (StandingDown) return;
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
            // (nothing was built), so the loop restarts from here instead of from the delivery.
            string s = (state ?? "").ToLowerInvariant();
            if (!s.Contains("approv") && loop && !StandingDown) _ = LoopRestartAsync();
        }

        void OnWorkDelivered(EventEnvelope _evt)
        {
            if (loop && !StandingDown) _ = LoopRestartAsync();
        }

        void OnSessionReset()
        {
            // A reset withdraws the pending decision: its idle timer must not outlive it.
            CancelIdleTimer();
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

            // Only act if this exact decision is still the one awaiting a human, and still in the demo.
            if (!token.IsCancellationRequested && !StandingDown &&
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
            if (_loopPending) return;
            _loopPending = true;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(loopPauseSeconds), _cts.Token);
                if (this == null || StandingDown) return;
                if (sessionController.RunInProgress) return;                             // someone already restarted it
                if (!string.IsNullOrEmpty(sessionController.PendingDecisionId)) return;  // still mid-cycle
                sessionController.StartDemo();
            }
            catch (OperationCanceledException) { }
            finally { _loopPending = false; }
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
            if (sessionController != null) sessionController.ModeChanged -= OnModeChanged;
            CancelIdleTimer();
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
        }
    }
}
