using UnityEngine;
using MuseDesk.Models;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Reusable integration layer between a backend-driven <see cref="AgentView"/> and a rigged
    /// character's Unity <see cref="Animator"/>. It NEVER invents state: it only translates the
    /// state the reducer hands to AgentView (plus AgentMovement.IsMoving) into animation, so the
    /// same prefab architecture works for researcher/developer/tester. Everything here is
    /// presentation; nothing waits on an animation and nothing is reported back.
    ///
    /// It works with either controller that has been built at
    /// Assets/MuseDesk/Art/Controllers/MuseDesk_Agent.controller, detected once at runtime:
    ///
    ///   DIRECT (the controller in the repository, built by Editor/ArtPipeline/MuseDeskArtPipeline):
    ///     one state per clip, named after the clip (Idle_Breathing, Walk_Loop, Typing, ...), and a
    ///     single float parameter MoveSpeed that drives its only two transitions
    ///     (Idle_Breathing to Walk_Loop above 0.02, Walk_Loop to Idle_Breathing below 0.015).
    ///     The bridge cross-fades between the clip states itself, so the blend times are the
    ///     public fields below, and keeps MoveSpeed consistent with what it is playing.
    ///
    ///   PARAMS (the controller AgentScripts/BuildAgentCharacters.BuildController would build):
    ///     int StateId (see StateId()), bool IsMoving, triggers TrOffer / TrSuccess / TrFailure /
    ///     TrError / TrHandoffGive / TrHandoffReceive. The controller owns the transitions.
    ///
    /// Parameters that do not exist on the bound controller are never set.
    /// </summary>
    [DisallowMultipleComponent]
    public class AgentAnimatorBridge : MonoBehaviour
    {
        public Animator animator;
        public AgentView view;
        public AgentMovement movement;

        [Header("Blend times in seconds (used when this bridge cross-fades clip states itself)")]
        [Tooltip("Between two standing states (idle, working, waiting...).")]
        public float baseBlend = 0.25f;
        [Tooltip("Into the walk cycle when the agent starts moving.")]
        public float walkInBlend = 0.2f;
        [Tooltip("Out of the walk cycle when the agent arrives.")]
        public float walkOutBlend = 0.22f;
        [Tooltip("Into a one-shot reaction (success, error, give, receive).")]
        public float reactionBlend = 0.2f;

        [Header("Walk playback follows the real speed (DIRECT controller only)")]
        [Tooltip("Scale the Animator's playback speed while walking so the feet keep up with the ground speed.")]
        public bool matchWalkToSpeed = true;
        [Tooltip("Ground covered by one Walk_Loop cycle (1.00 s) at the agent's size, metres. Not measured " +
                 "from the clip (it is an in-place cycle): an estimate for a 0.31 m character. Tune on the headset.")]
        public float walkCycleMeters = 0.15f;
        [Tooltip("Slowest and fastest playback while walking, as a multiple of the authored speed.")]
        public float walkRateMin = 0.7f;
        public float walkRateMax = 1.4f;
        [Tooltip("Slowest playback during the start and stop ramps of a walk (the body is almost still).")]
        public float walkRateFloor = 0.2f;

        // No cross-fade is ever shorter than this, whatever is serialized on the component: shorter
        // blends read as a pop on the headset.
        const float MinBlend = 0.2f;
        const float WalkClipSeconds = 1.0f;   // docs/Art/ANIMATION_MANIFEST.md

        // ---- PARAMS controller ----
        static readonly int P_StateId = Animator.StringToHash("StateId");
        static readonly int P_IsMoving = Animator.StringToHash("IsMoving");
        static readonly int P_TrOffer = Animator.StringToHash("TrOffer");
        static readonly int P_TrSuccess = Animator.StringToHash("TrSuccess");
        static readonly int P_TrFailure = Animator.StringToHash("TrFailure");
        static readonly int P_TrError = Animator.StringToHash("TrError");
        static readonly int P_TrGive = Animator.StringToHash("TrHandoffGive");
        static readonly int P_TrReceive = Animator.StringToHash("TrHandoffReceive");

        // ---- DIRECT controller: parameter and state names (states are named after their clips) ----
        static readonly int P_MoveSpeed = Animator.StringToHash("MoveSpeed");
        static readonly int S_Idle = Animator.StringToHash("Idle_Breathing");
        static readonly int S_Walk = Animator.StringToHash("Walk_Loop");
        static readonly int S_Reading = Animator.StringToHash("Reading");
        static readonly int S_Typing = Animator.StringToHash("Typing");
        static readonly int S_Inspecting = Animator.StringToHash("Inspecting");
        static readonly int S_Talking = Animator.StringToHash("Talking");
        static readonly int S_Listening = Animator.StringToHash("Listening");
        static readonly int S_Thinking = Animator.StringToHash("Thinking");
        static readonly int S_Waiting = Animator.StringToHash("Waiting_For_Input");
        static readonly int S_Blocked = Animator.StringToHash("Blocked");
        static readonly int S_Worried = Animator.StringToHash("Worried");
        static readonly int S_Offer = Animator.StringToHash("Offer_Document");
        static readonly int S_OfferPresent = Animator.StringToHash("Offer_Document_Present");   // the one-shot raise (20 frames)
        static readonly int S_TaskCompleted = Animator.StringToHash("Task_Completed");
        static readonly int S_Success = Animator.StringToHash("Success");
        static readonly int S_Failure = Animator.StringToHash("Failure");
        static readonly int S_Error = Animator.StringToHash("Error");
        static readonly int S_Give = Animator.StringToHash("Exchange_Package");
        static readonly int S_Receive = Animator.StringToHash("Receive_Document");
        static readonly int S_TurnL = Animator.StringToHash("Turn_Left");
        static readonly int S_TurnR = Animator.StringToHash("Turn_Right");
        static readonly int S_Nodding = Animator.StringToHash("Nodding");

        // Clip lengths in seconds (docs/Art/ANIMATION_MANIFEST.md).
        const float LenTaskCompleted = 1.67f, LenSuccess = 1.5f, LenFailure = 1.67f, LenError = 1.33f,
                    LenGive = 1.5f, LenReceive = 1.17f, LenOffer = 1.67f, LenNodding = 1.17f;

        bool _listening;         // addressed by the user with the mic open (VoiceAddressing), gated by ListeningGate
        MuseDesk.Voice.VoiceAgentTarget _listenTarget;
        float _lastNodAt = -100f;  // Polish S9b: per-agent rate limit for the confirmed-completion nod

        enum Mode { Unresolved, None, Direct, Params }

        Mode _mode = Mode.Unresolved;
        bool _hasMoveSpeed;
        float _moveSpeed;
        bool _moving;
        string _lastState = AgentStates.Idle;
        int _playing;            // DIRECT: the state we last cross-faded to
        bool _oneShot;           // a one-shot reaction is playing
        float _oneShotUntil;
        bool _offerHold;         // DIRECT: holding the paper out until it is taken or the agent moves
        int _offerTarget = S_Offer;   // DIRECT: the state that shows the raised paper (the one-shot raise when the controller has it)
        bool _offerFrozen;       // DIRECT: the animator is paused on the raised pose (Offer_Document loops its raise / lower otherwise)
        float _phase;            // per-agent offset so the three never animate in unison
        float _rate = 1f;        // DIRECT: the Animator playback speed we last set

        /// <summary>True while a one-shot reaction clip (success, failure, error, give, receive) plays.
        /// Procedural layers (gaze, idle life) fade themselves out while this is set.</summary>
        public bool ReactionPlaying => _oneShot;

        /// <summary>True while the acknowledged state is one where the agent is working at its desk.</summary>
        public bool IsWorkingState { get; private set; }

        /// <summary>True while the agent is holding the decision paper out (set by PlayOffer).</summary>
        public bool IsOffering => _offerHold;

        void Reset()
        {
            animator = GetComponentInChildren<Animator>();
            view = GetComponent<AgentView>();
            movement = GetComponent<AgentMovement>();
        }

        void Awake()
        {
            _phase = Random.value;
        }

        void OnEnable()
        {
            // The Animator rebinds and returns to its default state when re-enabled: start over.
            _mode = Mode.Unresolved;
            _oneShot = false;
            _offerHold = false;
            _offerFrozen = false;
            _moving = false;
            _moveSpeed = 0f;
            if (view != null)
            {
                view.StateChanged += OnStateChanged;
                // Adopt the current state WITHOUT a reaction: only a real transition earns one.
                _lastState = view.State;
                IsWorkingState = IsWorking(_lastState);
            }
            if (movement != null) movement.TurnInPlaceChanged += OnTurnInPlaceChanged;

            // Truthful Listening: only from VoiceAddressing (mic open and THIS agent addressed).
            _listenTarget = view != null ? MuseDesk.Voice.VoiceIntentClassifier.FromId(view.agentId) : MuseDesk.Voice.VoiceAgentTarget.None;
            var va = MuseDesk.Voice.VoiceAddressing.Shared;
            va.ListeningStarted += OnListenStart;
            va.ListeningEnded += OnListenEnd;
            _listening = va.MicOpen && _listenTarget != MuseDesk.Voice.VoiceAgentTarget.None && va.AddressedAgent == _listenTarget;
        }

        void OnDisable()
        {
            var va = MuseDesk.Voice.VoiceAddressing.Shared;
            va.ListeningStarted -= OnListenStart;
            va.ListeningEnded -= OnListenEnd;
            _listening = false;
            if (view != null) view.StateChanged -= OnStateChanged;
            if (movement != null) movement.TurnInPlaceChanged -= OnTurnInPlaceChanged;
            // Never leave the Animator running at a walk-matched rate.
            if ((_rate != 1f || _offerFrozen) && animator != null) animator.speed = 1f;
            _rate = 1f;
            _offerFrozen = false;
        }

        void Update()
        {
            if (animator == null) return;
            if (_mode == Mode.Unresolved && !Resolve()) return;
            if (_mode == Mode.None) return;

            SyncMoving();

            if (_oneShot && Time.time >= _oneShotUntil)
            {
                _oneShot = false;
                if (_mode == Mode.Direct) GoBase(baseBlend);
            }

            if (_mode == Mode.Direct && _hasMoveSpeed) UpdateMoveSpeed();
            if (_mode == Mode.Direct) UpdateOfferHold();
            if (_mode == Mode.Direct && !_offerFrozen) UpdateWalkRate();
        }

        // DIRECT: hold the raised paper. The offer clip raises the sheet and (it is authored as a loop) lowers it
        // again; once the cross-fade into the offer state is done and the raised pose is reached, pause the Animator
        // there. It is released the moment the offer ends, the agent walks or a one-shot reaction takes over.
        void UpdateOfferHold()
        {
            if (animator == null) return;
            if (_offerFrozen)
            {
                if (OfferHoldPolicy.ShouldRelease(true, _offerHold, _moving, _oneShot)) ReleaseOfferHold();
                return;
            }
            if (!_offerHold || _moving || _oneShot) return;
            AnimatorStateInfo cur = animator.GetCurrentAnimatorStateInfo(0);
            if (OfferHoldPolicy.ShouldFreeze(_offerHold, _moving, _oneShot, animator.IsInTransition(0),
                    cur.shortNameHash == _offerTarget, cur.normalizedTime,
                    OfferHoldPolicy.HoldAt(_offerTarget == S_OfferPresent)))
            {
                animator.speed = 0f;
                _offerFrozen = true;
            }
        }

        void ReleaseOfferHold()
        {
            if (!_offerFrozen) return;
            _offerFrozen = false;
            _rate = 1f;
            if (animator != null) animator.speed = 1f;
        }

        // DIRECT: the walk clip is an in-place cycle and AgentMovement eases the agent along its path,
        // so at a fixed playback speed the feet slide (too fast at the ends of the walk, too slow in
        // the middle). Follow the measured ground speed with the Animator's own playback speed, within
        // limits, and return to normal speed the moment the agent stops. No controller parameter is
        // involved: the controller only has MoveSpeed.
        void UpdateWalkRate()
        {
            float want = 1f;
            if (_moving && matchWalkToSpeed && movement != null && walkCycleMeters > 1e-4f && !movement.IsTurningInPlace)
            {
                float authored = walkCycleMeters / WalkClipSeconds;
                // The lower bound is walkRateFloor (not walkRateMin): AgentMovement now accelerates from rest and
                // brakes to rest, so the feet must slow with the body or they skate at the start and the stop.
                want = Mathf.Clamp(movement.PlanarSpeed / authored,
                                   Mathf.Min(walkRateFloor, walkRateMax), Mathf.Max(walkRateMin, walkRateMax));
            }
            if (want == _rate) return;
            // Slowing follows the body at once (a full stride under a slow start is the skate); speeding up eases.
            _rate = want < _rate ? want : Mathf.MoveTowards(_rate, want, Time.deltaTime * 4f);
            animator.speed = _rate;
        }

        // Work out which controller is bound. Returns false while the Animator is not ready yet.
        bool Resolve()
        {
            if (!animator.isActiveAndEnabled || !animator.isInitialized) return false;
            if (animator.runtimeAnimatorController == null) return false;

            bool hasStateId = false;
            _hasMoveSpeed = false;
            int n = animator.parameterCount;
            for (int i = 0; i < n; i++)
            {
                AnimatorControllerParameter p = animator.GetParameter(i);
                if (p.name == "StateId" && p.type == AnimatorControllerParameterType.Int) hasStateId = true;
                else if (p.name == "MoveSpeed" && p.type == AnimatorControllerParameterType.Float) _hasMoveSpeed = true;
            }

            if (animator.HasState(0, S_Idle) && animator.HasState(0, S_Walk)) _mode = Mode.Direct;
            else if (hasStateId) _mode = Mode.Params;
            else _mode = Mode.None;

            _moving = movement != null && movement.IsMoving;
            if (_mode == Mode.Direct)
            {
                // Start in the pose for the current state, at a per-agent point in its cycle.
                int target = DesiredBase();
                if (!animator.HasState(0, target)) target = S_Idle;
                animator.Play(target, 0, _phase);
                _playing = target;
            }
            else if (_mode == Mode.Params)
            {
                animator.SetInteger(P_StateId, StateId(_lastState));
                animator.SetBool(P_IsMoving, _moving);
            }
            return true;
        }

        void SyncMoving()
        {
            bool m = movement != null && movement.IsMoving;
            if (m == _moving) return;
            _moving = m;
            if (m) { _offerHold = false; _oneShot = false; ReleaseOfferHold(); }   // walking always wins
            if (_mode == Mode.Params) animator.SetBool(P_IsMoving, m);
            else if (_mode == Mode.Direct) GoBase(m ? walkInBlend : walkOutBlend);
        }

        // DIRECT: keep the controller's own MoveSpeed transitions agreeing with what we play.
        void UpdateMoveSpeed()
        {
            float want;
            if (_moving) want = 1f;
            else if (_moveSpeed > 0.5f && _playing != S_Idle)
            {
                // Blending out of the walk into a state the controller has no transition for. Hold
                // MoveSpeed up until the walk has been left, so the walk's own "back to idle"
                // transition cannot start underneath the cross-fade.
                AnimatorStateInfo cur = animator.GetCurrentAnimatorStateInfo(0);
                want = (cur.shortNameHash == S_Walk || animator.IsInTransition(0)) ? 1f : 0f;
            }
            else want = 0f;
            SetMoveSpeed(want);
        }

        void SetMoveSpeed(float v)
        {
            if (!_hasMoveSpeed || v == _moveSpeed) return;
            _moveSpeed = v;
            animator.SetFloat(P_MoveSpeed, v);
        }

        // Polish S9b: while AgentMovement turns the agent on the spot (before the first step, or at a sharp routed corner)
        // the turn clip plays instead of the walk cycle going on the spot. Mirrors WallHabitatView.SetAnim.
        int TurnState()
        {
            if (movement == null) return 0;
            switch (DeskTurnRule.Select(movement.TurnInPlaceDirection))
            {
                case TurnClip.Left: return animator.HasState(0, S_TurnL) ? S_TurnL : 0;
                case TurnClip.Right: return animator.HasState(0, S_TurnR) ? S_TurnR : 0;
                default: return 0;
            }
        }

        void OnTurnInPlaceChanged(int dir)   // see also TurnState
        {
            if (animator == null || _mode != Mode.Direct) return;
            if (_moving && !_oneShot) GoBase(walkInBlend);
        }

        int DesiredBase()
        {
            if (_moving)
            {
                int turn = TurnState();
                return turn != 0 ? turn : S_Walk;
            }
            if (_offerHold) return _offerTarget;
            if (ListeningGate.Show(_listening, false, false, _oneShot, _lastState == AgentStates.Error)) return S_Listening;
            return BaseFor(_lastState);
        }

        // Called by VoiceAddressing on the main thread. Only the addressed agent reacts.
        void OnListenStart(MuseDesk.Voice.VoiceAgentTarget t)
        {
            if (t != _listenTarget || _listenTarget == MuseDesk.Voice.VoiceAgentTarget.None) return;
            _listening = true;
            RefreshBase();
        }

        void OnListenEnd(MuseDesk.Voice.VoiceAgentTarget t)
        {
            if (t != _listenTarget) return;
            _listening = false;
            RefreshBase();
        }

        void RefreshBase()
        {
            if (animator == null || _mode != Mode.Direct || _oneShot) return;   // Update() re-picks after a one-shot
            GoBase(baseBlend);
        }

        // DIRECT: the looping clip that represents a standing state. States the backend does not
        // emit today (thinking, blocked, listening, talking, failed) are mapped as well, so they
        // read correctly if they ever arrive; nothing here makes them up.
        static int BaseFor(string state) => state switch
        {
            AgentStates.Researching => S_Reading,
            AgentStates.Coding => S_Typing,
            AgentStates.Testing => S_Inspecting,
            AgentStates.Reporting => S_Talking,
            AgentStates.Waiting => S_Waiting,
            AgentStates.Error => S_Worried,
            "thinking" => S_Thinking,
            "blocked" => S_Blocked,
            "listening" => S_Listening,
            "talking" => S_Talking,
            "failed" => S_Worried,
            _ => S_Idle,   // idle, approaching (walking is IsMoving), complete (after its reaction)
        };

        static bool IsWorking(string state)
        {
            return state == AgentStates.Researching || state == AgentStates.Coding
                || state == AgentStates.Testing || state == AgentStates.Reporting;
        }

        // DIRECT: cross-fade to the clip for the current situation (no-op if already there).
        void GoBase(float blend)
        {
            int target = DesiredBase();
            if (!animator.HasState(0, target)) target = S_Idle;
            if (target == S_Idle && !_moving) SetMoveSpeed(0f);
            if (target == _playing) return;
            // A per-agent start offset keeps the three agents' loops out of step.
            // The offer starts at the beginning of its raise (a random offset would drop it mid-cycle).
            float offset = (target == S_Walk || target == S_TurnL || target == S_TurnR || target == S_Offer || target == S_OfferPresent) ? 0f : _phase * 2f;
            animator.CrossFadeInFixedTime(target, Mathf.Max(MinBlend, blend), 0, offset);
            _playing = target;
        }

        // DIRECT: play a non-looping reaction once, then return to the base clip (see Update).
        void PlayOneShot(int state, float clipSeconds)
        {
            if (!animator.HasState(0, state)) { GoBase(baseBlend); return; }
            animator.CrossFadeInFixedTime(state, Mathf.Max(MinBlend, reactionBlend), 0, 0f);
            _playing = state;
            _oneShot = true;
            // Leave a little early so the blend back starts before the clip freezes on its last frame.
            _oneShotUntil = Time.time + Mathf.Max(0.3f, clipSeconds - baseBlend * 0.5f);
        }

        void OnStateChanged(string state)
        {
            string prev = _lastState;
            _lastState = state;
            IsWorkingState = IsWorking(state);
            if (animator == null || _mode == Mode.Unresolved || _mode == Mode.None) return;   // Resolve() applies it

            // A reaction belongs to the TRANSITION into complete / error. A repeated event with the
            // same state (progress updates, a re-projected snapshot) must not replay it.
            bool entered = prev != state;
            bool walking = movement != null && movement.IsMoving;

            if (_mode == Mode.Params)
            {
                animator.SetInteger(P_StateId, StateId(state));
                if (entered && !_oneShot)
                {
                    if (state == AgentStates.Complete) { animator.SetTrigger(P_TrSuccess); MarkOneShot(LenSuccess); }
                    else if (state == AgentStates.Error) { animator.SetTrigger(P_TrError); MarkOneShot(LenError); }
                }
                return;
            }

            // DIRECT
            if (entered && !walking && state == AgentStates.Error)
            {
                PlayOneShot(S_Error, LenError);            // startled, then settles into Worried
            }
            else if (entered && !walking && !_oneShot && state == AgentStates.Complete)
            {
                PlayOneShot(S_TaskCompleted, LenTaskCompleted);   // small satisfied nod, then idle
            }
            else if (!_oneShot)
            {
                GoBase(baseBlend);
            }
            // else: a reaction is playing; Update() goes to the new base clip when it ends.
        }

        void MarkOneShot(float seconds)
        {
            _oneShot = true;
            _oneShotUntil = Time.time + seconds;
        }

        // Must mirror the state ordering baked into the PARAMS controller.
        public static int StateId(string state) => state switch
        {
            AgentStates.Idle => 0,
            AgentStates.Researching => 1,
            AgentStates.Coding => 2,
            AgentStates.Testing => 3,
            AgentStates.Reporting => 4,
            AgentStates.Approaching => 5,
            AgentStates.Waiting => 6,
            AgentStates.Complete => 7,
            AgentStates.Error => 8,
            _ => 0,
        };

        bool Ready()
        {
            if (animator == null) return false;
            if (_mode == Mode.Unresolved && !Resolve()) return false;
            return _mode != Mode.None;
        }

        void Reaction(int trigger, int state, float clipSeconds)
        {
            if (!Ready()) return;
            _offerHold = false;
            ReleaseOfferHold();
            if (_mode == Mode.Params) { animator.SetTrigger(trigger); MarkOneShot(clipSeconds); return; }
            SyncMoving();
            // Walking always wins: a standing reaction played over a walk slid across the desk and
            // left the reaction flag set for the rest of the walk.
            if (_moving) return;
            PlayOneShot(state, clipSeconds);
        }

        // --- Reactions, called by AgentRegistry / AgentHandoffController on real backend events ---

        /// <summary>Hold the decision paper out to the user. Stays until the paper is taken
        /// (<see cref="EndOffer"/>), the agent walks, or another reaction plays.</summary>
        public void PlayOffer()
        {
            if (!Ready()) return;
            if (_mode == Mode.Params) { animator.SetTrigger(P_TrOffer); MarkOneShot(LenOffer); return; }
            SyncMoving();
            // Already holding it out: never restart the raise (that was the give / take-back loop).
            if (_offerHold && !_oneShot) return;
            _oneShot = false;
            _offerHold = true;
            _offerTarget = animator.HasState(0, S_OfferPresent) ? S_OfferPresent : S_Offer;
            GoBase(baseBlend);
        }

        /// <summary>Is the raised paper being held steady right now (the raise finished and is paused)?
        /// For the Play Mode driver and diagnostics.</summary>
        public bool OfferPoseHeld => _offerHold && _offerFrozen;

        /// <summary>Stop holding the paper out (the user took it). Safe to call at any time.</summary>
        public void EndOffer()
        {
            if (!_offerHold) return;
            _offerHold = false;
            ReleaseOfferHold();
            if (animator != null && _mode == Mode.Direct && !_oneShot) GoBase(baseBlend);
        }

        public void PlaySuccess() { Reaction(P_TrSuccess, S_Success, LenSuccess); }
        public void PlayFailure() { Reaction(P_TrFailure, S_Failure, LenFailure); }
        public void PlayHandoffGive() { Reaction(P_TrGive, S_Give, LenGive); }
        public void PlayHandoffReceive() { Reaction(P_TrReceive, S_Receive, LenReceive); }

        /// <summary>A small acknowledgement (the Task_Completed nod), for an agent that is reacting to
        /// a colleague's result and not to its own. Does nothing while the agent walks, holds the
        /// paper out or is already in a reaction, and nothing on a controller without that clip.
        /// Returns true if the nod was started.</summary>
        public bool PlayAcknowledge()
        {
            if (!Ready() || _mode != Mode.Direct) return false;
            SyncMoving();
            if (_moving || _offerHold || _oneShot) return false;
            if (!animator.HasState(0, S_TaskCompleted)) return false;
            PlayOneShot(S_TaskCompleted, LenTaskCompleted);
            return true;
        }

        /// <summary>One Nodding one-shot for a confirmed task.completed on THIS agent (Polish S9b). It never stacks: it
        /// is refused while the agent walks, holds the paper out, or is already in a one-shot (the Complete state's
        /// Task_Completed nod, Success, Error, or a colleague acknowledgement), under Focus, and within
        /// <see cref="NodGate.DefaultGapSeconds"/> of its previous nod. Returns true if the nod started.</summary>
        public bool PlayConfirmedNod()
        {
            if (!Ready() || _mode != Mode.Direct) return false;
            SyncMoving();
            bool busy = _moving || _offerHold || _oneShot;
            if (!NodGate.Allow(Time.time, _lastNodAt, NodGate.DefaultGapSeconds,
                               MuseDesk.Workspace.FocusModeController.Active, busy)) return false;
            if (!animator.HasState(0, S_Nodding)) return false;
            _lastNodAt = Time.time;
            PlayOneShot(S_Nodding, LenNodding);
            return true;
        }

        /// <summary>The clip state last cross-faded to (DIRECT controller), for a driver's evidence.</summary>
        public int PlayingStateHash => _playing;

        /// <summary>True while the agent is translating (the walk clip owns the body).</summary>
        public bool IsWalking => _moving;
    }
}
