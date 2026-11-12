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
        public float walkInBlend = 0.18f;
        [Tooltip("Out of the walk cycle when the agent arrives.")]
        public float walkOutBlend = 0.22f;
        [Tooltip("Into a one-shot reaction (success, error, give, receive).")]
        public float reactionBlend = 0.12f;

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
        static readonly int S_TaskCompleted = Animator.StringToHash("Task_Completed");
        static readonly int S_Success = Animator.StringToHash("Success");
        static readonly int S_Failure = Animator.StringToHash("Failure");
        static readonly int S_Error = Animator.StringToHash("Error");
        static readonly int S_Give = Animator.StringToHash("Exchange_Package");
        static readonly int S_Receive = Animator.StringToHash("Receive_Document");

        // Clip lengths in seconds (docs/Art/ANIMATION_MANIFEST.md).
        const float LenTaskCompleted = 1.67f, LenSuccess = 1.5f, LenFailure = 1.67f, LenError = 1.33f,
                    LenGive = 1.5f, LenReceive = 1.17f, LenOffer = 1.67f;

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
        float _phase;            // per-agent offset so the three never animate in unison

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
            _moving = false;
            _moveSpeed = 0f;
            if (view != null)
            {
                view.StateChanged += OnStateChanged;
                // Adopt the current state WITHOUT a reaction: only a real transition earns one.
                _lastState = view.State;
                IsWorkingState = IsWorking(_lastState);
            }
        }

        void OnDisable()
        {
            if (view != null) view.StateChanged -= OnStateChanged;
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
            if (m) { _offerHold = false; _oneShot = false; }   // walking always wins
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

        int DesiredBase()
        {
            if (_moving) return S_Walk;
            if (_offerHold) return S_Offer;
            return BaseFor(_lastState);
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
            float offset = target == S_Walk ? 0f : _phase * 2f;
            animator.CrossFadeInFixedTime(target, Mathf.Max(0.01f, blend), 0, offset);
            _playing = target;
        }

        // DIRECT: play a non-looping reaction once, then return to the base clip (see Update).
        void PlayOneShot(int state, float clipSeconds)
        {
            if (!animator.HasState(0, state)) { GoBase(baseBlend); return; }
            animator.CrossFadeInFixedTime(state, Mathf.Max(0.01f, reactionBlend), 0, 0f);
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
            if (_mode == Mode.Params) { animator.SetTrigger(trigger); MarkOneShot(clipSeconds); return; }
            SyncMoving();
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
            _oneShot = false;
            _offerHold = true;
            GoBase(baseBlend);
        }

        /// <summary>Stop holding the paper out (the user took it). Safe to call at any time.</summary>
        public void EndOffer()
        {
            if (!_offerHold) return;
            _offerHold = false;
            if (animator != null && _mode == Mode.Direct && !_oneShot) GoBase(baseBlend);
        }

        public void PlaySuccess() { Reaction(P_TrSuccess, S_Success, LenSuccess); }
        public void PlayFailure() { Reaction(P_TrFailure, S_Failure, LenFailure); }
        public void PlayHandoffGive() { Reaction(P_TrGive, S_Give, LenGive); }
        public void PlayHandoffReceive() { Reaction(P_TrReceive, S_Receive, LenReceive); }
    }
}
