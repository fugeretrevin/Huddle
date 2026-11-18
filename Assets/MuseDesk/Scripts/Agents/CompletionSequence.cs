using UnityEngine;
using MuseDesk.Art;
using MuseDesk.Feedback;
using MuseDesk.Models;
using MuseDesk.Networking;
using MuseDesk.Workspace;

namespace MuseDesk.Agents
{
    /// <summary>
    /// The payoff when work is delivered, and the shorter beat when a run fails. A coordinator only:
    /// it schedules calls into components that already exist (testing station lamps, factory stage
    /// clips, agent reactions and gaze, the shelf token, the delivered cue) so they read as one
    /// short sequence instead of all firing on the same frame. It owns no state of the run and
    /// never starts before the reducer has applied the confirming event:
    ///
    ///   SUCCESS, on SessionReducer.WorkDelivered (a task.completed that references an artifact).
    ///   A handoff that has not reached its give / receive beat is retired first
    ///   (<see cref="deliverySupersedesHandoff"/>); if one is being held at that beat the sequence
    ///   waits for it (at most <see cref="handoffWaitMax"/>). Then, in seconds from its start:
    ///     0.0   testing station shows PASSED (only if the backend reported a pass), the factory
    ///           lights its test gate (only with real passing test evidence), the Tester plays its
    ///           success reaction;
    ///     0.4   the Developer and the Researcher look toward the Tester and give the small
    ///           Task_Completed nod (each as soon as it is standing at its desk);
    ///     1.1   the factory delivers the finished artifact;
    ///     1.6   the token grows onto the project shelf and FeedbackCue.Delivered sounds, once;
    ///     1.75  the Tester glances at the user;
    ///     2.5   done.
    ///   In focus mode the agents only look; no reaction clips are played.
    ///
    ///   FAILURE, on SessionReducer.TaskFailed, about 1.6 s: the agent the event names (else the
    ///   one in the error state, else the Tester) plays its failure reaction unless its own error
    ///   reaction is already playing, and the other two look at it. The station's FAILED lamp, the
    ///   factory's reject clip and FeedbackCue.Error are already raised once each by their own
    ///   controllers on the same event and are left to them.
    ///
    ///   A rejection by the user is not a failure and starts nothing here.
    ///
    /// While this component is bound it takes over three things that used to fire on the same frame
    /// as the event: FactoryEventController's "gate, then delivery" (SequencedDelivery),
    /// FeedbackController's delivered cue (DeliveredCueSequenced) and the moment the new shelf token
    /// is seen (ProjectShelfController still creates it on the event; it is held at near-zero scale
    /// until its beat). All three are handed back when this component is disabled, so without it
    /// the office behaves exactly as before. SessionReset cancels everything at once.
    ///
    /// No allocation after binding; Update returns immediately while nothing is playing.
    /// </summary>
    [DisallowMultipleComponent]
    public class CompletionSequence : MonoBehaviour
    {
        [Header("Wiring (assigned by AgentScripts/BuildLifeV3)")]
        public SessionController controller;
        public AgentRegistry registry;
        public TestingStationController testingStation;
        public FactoryEventController factoryEvents;
        public ProjectShelfController shelf;
        [Tooltip("Optional. Lets a delivery retire handoffs that have not reached their exchange yet.")]
        public AgentHandoffController handoffs;

        [Header("Handoffs still in flight when the work is delivered")]
        [Tooltip("On: a handoff that has not reached its give / receive beat is abandoned (queued ones dropped, " +
                 "a courier on its way sent home), so the payoff starts at once and nobody carries work to the " +
                 "Tester after it has passed. Off: the sequence only waits, up to the time below.")]
        public bool deliverySupersedesHandoff = true;
        [Tooltip("Longest wait for a give / receive beat that is being held to finish before the sequence starts.")]
        public float handoffWaitMax = 1.2f;
        [Tooltip("Longest wait for the whole chain of reported handoffs (including queued ones) to finish their " +
                 "give / receive beat before the celebration starts. Past it, unfinished ones are retired. Presentation only.")]
        public float handoffChainWaitMax = 24f;   // was 15: the rc18 handoffs are slower (walk >= 2.2 s, ready beat, 1.8 s hold)

        [Header("Success timing, seconds from the start of the sequence")]
        [Tooltip("When the Developer and Researcher acknowledge the Tester.")]
        public float acknowledgeDelay = 0.4f;
        [Tooltip("When the factory delivers the finished artifact.")]
        public float factoryDeliveryDelay = 1.1f;
        [Tooltip("When the shelf token appears and the delivered cue sounds.")]
        public float tokenDelay = 1.6f;
        [Tooltip("When the Tester glances at the user.")]
        public float userGlanceDelay = 1.75f;
        [Tooltip("Length of the whole success sequence.")]
        public float totalSeconds = 2.5f;
        [Tooltip("Seconds the shelf token takes to grow onto its slot.")]
        public float tokenPopSeconds = 0.3f;

        [Header("Failure timing")]
        [Tooltip("When the responsible agent reacts and the others look at it.")]
        public float failureReactDelay = 0.15f;
        [Tooltip("Length of the whole failure sequence.")]
        public float failureSeconds = 1.6f;

        /// <summary>True while a success or failure sequence is waiting or playing.</summary>
        public bool IsPlaying => _phase != Phase.None;

        enum Phase { None, WaitHandoff, Success, Failure }

        const int Researcher = 0, Developer = 1, Tester = 2, AgentCount = 3;
        static readonly string[] Ids = { AgentIds.Researcher, AgentIds.Developer, AgentIds.Tester };

        const int BeatOpen = 1, BeatAck = 2, BeatTransport = 4, BeatToken = 8, BeatGlance = 16, BeatReact = 32;
        const string TokenPrefix = "CompletedArtifact_";   // the name ProjectShelfController gives a token

        readonly AgentView[] _views = new AgentView[AgentCount];
        readonly AgentAnimatorBridge[] _bridges = new AgentAnimatorBridge[AgentCount];
        readonly AgentGaze[] _gazes = new AgentGaze[AgentCount];
        readonly AgentMovement[] _moves = new AgentMovement[AgentCount];
        bool _agentsResolved;

        SessionReducer _reducer;
        FeedbackController _feedback;
        bool _bound;
        int _enabledFrame;

        Phase _phase = Phase.None;
        float _t, _wait;
        int _fired;
        int _ackPending;          // bit per agent index still owed its acknowledgement
        int _responsible = Tester;
        bool _preview;
        bool _cueOwed;            // this delivery's cue is ours to play (the feedback controller stood down)
        bool _tokenSearched;

        Transform _hiddenToken;   // the new shelf token, held small until its beat
        Transform _popping;       // the token while it grows
        Transform _lastToken;     // the newest token already handled
        Vector3 _tokenFull = Vector3.one;
        float _popT;

        void OnEnable() { _enabledFrame = Time.frameCount; }

        void Update()
        {
            // Bind a few frames after the other views. They bind on their first Update; subscribing
            // after them puts this component's handlers behind theirs, so on WorkDelivered the shelf
            // has already created its token when OnWorkDelivered looks for it.
            if (!_bound)
            {
                if (controller == null || Time.frameCount < _enabledFrame + 3) return;
                Bind();
            }

            float dt = Time.deltaTime;
            if (_popping != null) TickPop(dt);

            if (_phase == Phase.None)
            {
                // The feedback controller appeared after this component bound: take the cue over now,
                // between events (never inside an event, where its handler may not have run yet).
                if (_feedback == null)
                {
                    _feedback = FeedbackController.Instance;
                    if (_feedback != null) _feedback.DeliveredCueSequenced = true;
                }
                return;
            }

            if (_phase == Phase.WaitHandoff)
            {
                // The handler may have run before the shelf's on this event: look once more.
                if (!_tokenSearched)
                {
                    _tokenSearched = true;
                    if (_hiddenToken == null) HideNewToken();
                }
                _wait += dt;
                float waitMax = Mathf.Max(handoffWaitMax, handoffChainWaitMax);
                if (HandoffWaitPolicy.KeepWaiting(AgentHandoffController.ExchangePending, _wait, waitMax)) return;
                // Out of time with a reported handoff still unfinished: retire it rather than show it late.
                if (HandoffWaitPolicy.ShouldAbandon(deliverySupersedesHandoff, AgentHandoffController.ExchangePending, _wait, waitMax)
                    && handoffs != null) handoffs.AbandonPending();
                _phase = Phase.Success;
                _t = 0f;
            }
            else _t += dt;

            if (_phase == Phase.Success) TickSuccess();
            else if (_phase == Phase.Failure) TickFailure();
        }

        // ───────────────────────── binding ─────────────────────────

        void Bind()
        {
            _reducer = controller.Reducer;
            _reducer.WorkDelivered += OnWorkDelivered;
            _reducer.TaskFailed += OnTaskFailed;
            _reducer.SessionReset += OnSessionReset;
            ResolveAgents();
            Claim();
            _bound = true;
        }

        // Take over the pieces that would otherwise fire on the event's own frame.
        void Claim()
        {
            if (factoryEvents != null) factoryEvents.SequencedDelivery = true;
            if (_feedback == null) _feedback = FeedbackController.Instance;
            if (_feedback != null) _feedback.DeliveredCueSequenced = true;
        }

        void OnDisable()
        {
            if (_reducer != null)
            {
                _reducer.WorkDelivered -= OnWorkDelivered;
                _reducer.TaskFailed -= OnTaskFailed;
                _reducer.SessionReset -= OnSessionReset;
            }
            _reducer = null;
            _bound = false;

            // Hand everything back, and never leave a real token invisible.
            if (factoryEvents != null) factoryEvents.SequencedDelivery = false;
            if (_feedback != null) _feedback.DeliveredCueSequenced = false;
            RestoreToken();
            _phase = Phase.None;
            _ackPending = 0;
            _cueOwed = false;
        }

        void ResolveAgents()
        {
            _agentsResolved = true;
            for (int i = 0; i < AgentCount; i++)
            {
                AgentView view = null;
                if (registry != null && registry.agents != null)
                {
                    for (int a = 0; a < registry.agents.Count; a++)
                    {
                        AgentView candidate = registry.agents[a];
                        if (candidate != null && candidate.agentId == Ids[i]) { view = candidate; break; }
                    }
                }
                AgentGaze gaze = view != null ? view.GetComponent<AgentGaze>() : AgentGaze.ForAgent(Ids[i]);
                if (view == null && gaze != null) view = gaze.view;
                _views[i] = view;
                _gazes[i] = gaze;
                _bridges[i] = view != null ? view.GetComponent<AgentAnimatorBridge>() : null;
                _moves[i] = view != null ? view.GetComponent<AgentMovement>() : null;
            }
        }

        // ───────────────────────── reducer events ─────────────────────────

        void OnWorkDelivered(EventEnvelope e)
        {
            // A second delivery while one is still playing: settle the first at once.
            if (_phase == Phase.WaitHandoff || _phase == Phase.Success) SettleDelivery();
            // The cue is ours only if the feedback controller really stood down for this event.
            _cueOwed = _feedback != null && _feedback.DeliveredCueSequenced;
            // Handoffs the event stream reported (Developer -> Tester on artifact.created) are NOT dropped here: at
            // 1x the delivery arrives while that one is still queued behind the Researcher -> Developer walk, and
            // dropping it hid a real event. The sequence waits for the chain (bounded) in Update instead.
            BeginSuccess(false);
        }

        void OnTaskFailed(EventEnvelope e)
        {
            if (_phase == Phase.WaitHandoff || _phase == Phase.Success) SettleDelivery();
            BeginFailure(e != null ? e.AgentId : null, false);
        }

        // Replay: stop at once. The shelf, the station and the factory clear themselves on the same event.
        void OnSessionReset()
        {
            Cancel();
            _lastToken = null;
        }

        /// <summary>Stop whatever is playing. A token that was being held back is shown at full size
        /// (it is real: the delivery happened); no further beat fires.</summary>
        public void Cancel()
        {
            RestoreToken();
            _phase = Phase.None;
            _ackPending = 0;
            _cueOwed = false;
            _fired = 0;
        }

        // ───────────────────────── success ─────────────────────────

        void BeginSuccess(bool preview)
        {
            if (!_agentsResolved) ResolveAgents();
            _preview = preview;
            _phase = Phase.WaitHandoff;
            _t = 0f;
            _wait = 0f;
            _fired = 0;
            _ackPending = 0;
            _tokenSearched = false;
            HideNewToken();
        }

        void TickSuccess()
        {
            bool focus = FocusModeController.Active;

            if (Due(BeatOpen, 0f))
            {
                // PASSED only for a pass the backend reported: the tester's own run went from testing
                // to complete, or passing test evidence came with the artifact.
                bool passed = _preview
                              || (testingStation != null && testingStation.TesterPassed)
                              || (factoryEvents != null && factoryEvents.HasPassingTestEvidence);
                if (passed && testingStation != null)
                    testingStation.ShowResult(WorkstationVisualController.TestingVisualState.Passed, totalSeconds);
                if (factoryEvents != null && factoryEvents.SequencedDelivery) factoryEvents.PlayDeliveryGate(_preview);
                if (!focus && _bridges[Tester] != null && Standing(Tester)) _bridges[Tester].PlaySuccess();
            }

            if (Due(BeatAck, acknowledgeDelay))
            {
                LookAt(Researcher, Tester, 1.7f, 0.7f);
                LookAt(Developer, Tester, 1.7f, 0.7f);
                if (!focus) _ackPending = (1 << Researcher) | (1 << Developer);
            }

            // Each colleague nods as soon as it is standing at its own desk (the Developer is often
            // still walking back from the handoff); whoever is not there in time just keeps looking.
            if (_ackPending != 0)
            {
                if (_t >= totalSeconds - 0.4f) _ackPending = 0;
                else
                {
                    TryAcknowledge(Researcher);
                    TryAcknowledge(Developer);
                }
            }

            if (Due(BeatTransport, factoryDeliveryDelay))
            {
                if (factoryEvents != null && factoryEvents.SequencedDelivery) factoryEvents.PlayDeliveryTransport();
            }

            if (Due(BeatToken, tokenDelay))
            {
                RevealToken();
                if (_cueOwed || _preview) FeedbackController.PlayCue(FeedbackCue.Delivered);
                _cueOwed = false;
            }

            if (Due(BeatGlance, userGlanceDelay))
            {
                AgentGaze g = _gazes[Tester];
                if (g != null && g.isActiveAndEnabled && Standing(Tester)) g.SetAttentionOnUser(1.0f, 0.85f);
            }

            if (_t >= totalSeconds)
            {
                // Every beat has fired by now unless the timings were set past the total length.
                SettleDelivery();
                _phase = Phase.None;
            }
        }

        void TryAcknowledge(int who)
        {
            int bit = 1 << who;
            if ((_ackPending & bit) == 0) return;
            AgentMovement move = _moves[who];
            if (move != null && (move.IsMoving || !move.AtHome)) return;   // not at its desk yet
            _ackPending &= ~bit;
            AgentAnimatorBridge bridge = _bridges[who];
            if (bridge == null) return;
            if (bridge.PlayAcknowledge()) LookAt(who, Tester, 1.4f, 0.7f);
        }

        // Whatever of this delivery is still owed, now: the token at full size and the cue.
        void SettleDelivery()
        {
            RestoreToken();
            if (_cueOwed && (_fired & BeatToken) == 0) FeedbackController.PlayCue(FeedbackCue.Delivered);
            _cueOwed = false;
            _ackPending = 0;
        }

        // ───────────────────────── failure ─────────────────────────

        void BeginFailure(string agentId, bool preview)
        {
            if (!_agentsResolved) ResolveAgents();
            _preview = preview;
            _responsible = -1;
            if (!string.IsNullOrEmpty(agentId))
                for (int i = 0; i < AgentCount; i++)
                    if (Ids[i] == agentId) { _responsible = i; break; }
            if (_responsible < 0)
                for (int i = 0; i < AgentCount; i++)
                    if (_views[i] != null && _views[i].State == AgentStates.Error) { _responsible = i; break; }
            if (_responsible < 0) _responsible = Tester;

            _phase = Phase.Failure;
            _t = 0f;
            _fired = 0;
            _ackPending = 0;
        }

        void TickFailure()
        {
            if (Due(BeatOpen, 0f) && _preview)
            {
                // A real task.failed already raised these through their own controllers, once each.
                if (testingStation != null)
                    testingStation.ShowResult(WorkstationVisualController.TestingVisualState.Failed, failureSeconds + 0.6f);
                if (factoryEvents != null && factoryEvents.factory != null) factoryEvents.factory.PlayTestFailed();
                FeedbackController.PlayCue(FeedbackCue.Error);
            }

            if (Due(BeatReact, failureReactDelay))
            {
                bool focus = FocusModeController.Active;
                AgentAnimatorBridge bridge = _bridges[_responsible];
                // Its own error reaction (played by the bridge when the backend put it in the error
                // state) takes precedence: never two reactions for one failure.
                if (!focus && bridge != null && Standing(_responsible) && !bridge.ReactionPlaying)
                    bridge.PlayFailure();
                for (int i = 0; i < AgentCount; i++)
                    if (i != _responsible) LookAt(i, _responsible, 1.5f, focus ? 0.5f : 0.7f);
            }

            if (_t >= failureSeconds) _phase = Phase.None;
        }

        // ───────────────────────── helpers ─────────────────────────

        bool Due(int beat, float at)
        {
            if ((_fired & beat) != 0 || _t < at) return false;
            _fired |= beat;
            return true;
        }

        bool Standing(int who)
        {
            AgentMovement move = _moves[who];
            return move == null || !move.IsMoving;
        }

        void LookAt(int who, int target, float seconds, float weight)
        {
            AgentGaze gaze = _gazes[who];
            if (gaze == null || !gaze.isActiveAndEnabled) return;
            Transform at = _gazes[target] != null && _gazes[target].head != null ? _gazes[target].head
                         : (_views[target] != null ? _views[target].transform : null);
            if (at != null) gaze.SetAttention(at, seconds, weight);
        }

        // ───────────────────────── the shelf token ─────────────────────────

        // ProjectShelfController creates the token on the event itself. Find the newest one and hold
        // it at near-zero scale until its beat. Runs once or twice per delivery, never per frame.
        void HideNewToken()
        {
            if (shelf == null || shelf.slots == null) return;
            Transform newest = null;
            for (int i = shelf.slots.Count - 1; i >= 0 && newest == null; i--)
            {
                Transform slot = shelf.slots[i];
                if (slot == null) continue;
                for (int c = slot.childCount - 1; c >= 0; c--)
                {
                    Transform child = slot.GetChild(c);
                    if (child.name.StartsWith(TokenPrefix, System.StringComparison.Ordinal)) { newest = child; break; }
                }
            }
            if (newest == null || newest == _lastToken) return;
            // A token from the delivery before this one that is still growing: finish it first.
            if (_popping != null) { _popping.localScale = _tokenFull; _popping = null; }
            _lastToken = newest;
            _hiddenToken = newest;
            _tokenFull = newest.localScale;
            newest.localScale = _tokenFull * 0.001f;
        }

        void RevealToken()
        {
            if (_hiddenToken == null) return;
            _popping = _hiddenToken;
            _hiddenToken = null;
            _popT = 0f;
            if (tokenPopSeconds <= 0.01f) { _popping.localScale = _tokenFull; _popping = null; }
        }

        void TickPop(float dt)
        {
            // Ease-out with a slight overshoot, the same curve the decision paper unfolds with.
            _popT += dt / Mathf.Max(0.05f, tokenPopSeconds);
            float k = Mathf.Clamp01(_popT);
            float inv = 1f - k;
            float s = 1f - inv * inv * inv + Mathf.Sin(k * Mathf.PI) * 0.05f;
            _popping.localScale = _tokenFull * Mathf.Max(0.001f, s);
            if (k >= 1f) { _popping.localScale = _tokenFull; _popping = null; }
        }

        // Put a held-back or growing token at its full size at once.
        void RestoreToken()
        {
            if (_hiddenToken != null) _hiddenToken.localScale = _tokenFull;
            if (_popping != null) _popping.localScale = _tokenFull;
            _hiddenToken = null;
            _popping = null;
        }

        // ───────────────────────── observation (Editor only) ─────────────────────────

        /// <summary>
        /// Editor Play Mode only: play the success sequence without a delivery, to look at the
        /// animation. It shows the PASSED lamp, the factory clips, the reactions and the cue; it
        /// creates no shelf token and changes no session state. Does nothing in a build, so scripted
        /// activity can never be presented as a result there.
        /// </summary>
        public void PreviewSuccess()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
            Debug.Log("[MuseDesk] CompletionSequence.PreviewSuccess: PREVIEW ONLY, no work was delivered.");
            if (IsPlaying) Cancel();
            _cueOwed = false;
            BeginSuccess(true);
#endif
        }

        /// <summary>Editor Play Mode only: play the failure sequence without a failed run, with the
        /// Tester as the responsible agent. Does nothing in a build.</summary>
        public void PreviewFailure()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
            Debug.Log("[MuseDesk] CompletionSequence.PreviewFailure: PREVIEW ONLY, no run failed.");
            if (IsPlaying) Cancel();
            BeginFailure(AgentIds.Tester, true);
#endif
        }
    }
}
