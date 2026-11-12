using UnityEngine;
using MuseDesk.Models;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Reusable integration layer between a backend-driven <see cref="AgentView"/> and a rigged
    /// character's Unity <see cref="Animator"/>. It NEVER invents state: it only translates the
    /// state the reducer hands to AgentView (plus AgentMovement.IsMoving) into Animator parameters,
    /// so the same prefab architecture works for researcher/developer/tester.
    ///
    /// Parameters on MuseDesk_Agent.controller (built by AgentScripts/BuildAgentCharacters.cs):
    ///   int  StateId     - 0 idle,1 researching,2 coding,3 testing,4 reporting,5 approaching,
    ///                      6 waiting,7 complete,8 error  (see StateId()).
    ///   bool IsMoving    - true while AgentMovement is translating the body (walk cycle).
    ///   trig TrOffer / TrSuccess / TrFailure / TrError / TrHandoffGive / TrHandoffReceive
    ///                    - one-shot reactions fired by real backend events.
    /// </summary>
    [DisallowMultipleComponent]
    public class AgentAnimatorBridge : MonoBehaviour
    {
        public Animator animator;
        public AgentView view;
        public AgentMovement movement;

        static readonly int P_StateId = Animator.StringToHash("StateId");
        static readonly int P_IsMoving = Animator.StringToHash("IsMoving");
        static readonly int P_TrOffer = Animator.StringToHash("TrOffer");
        static readonly int P_TrSuccess = Animator.StringToHash("TrSuccess");
        static readonly int P_TrFailure = Animator.StringToHash("TrFailure");
        static readonly int P_TrError = Animator.StringToHash("TrError");
        static readonly int P_TrGive = Animator.StringToHash("TrHandoffGive");
        static readonly int P_TrReceive = Animator.StringToHash("TrHandoffReceive");

        bool _moving;

        void Reset()
        {
            animator = GetComponentInChildren<Animator>();
            view = GetComponent<AgentView>();
            movement = GetComponent<AgentMovement>();
        }

        void OnEnable()
        {
            if (view != null)
            {
                view.StateChanged += OnStateChanged;
                OnStateChanged(view.State);
            }
        }

        void OnDisable()
        {
            if (view != null) view.StateChanged -= OnStateChanged;
        }

        void Update()
        {
            if (animator == null || movement == null) return;
            bool m = movement.IsMoving;
            if (m != _moving)
            {
                _moving = m;
                animator.SetBool(P_IsMoving, m);
            }
        }

        void OnStateChanged(string state)
        {
            if (animator == null) return;
            animator.SetInteger(P_StateId, StateId(state));
            // Fire the matching one-shot reaction for terminal states (returns to StateId after).
            switch (state)
            {
                case AgentStates.Complete: animator.SetTrigger(P_TrSuccess); break;
                case AgentStates.Error: animator.SetTrigger(P_TrError); break;
            }
        }

        // Must mirror the state ordering baked into MuseDesk_Agent.controller.
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

        // --- One-shot reactions, called by AgentRegistry on real backend events ---
        public void PlayOffer() { if (animator) animator.SetTrigger(P_TrOffer); }
        public void PlaySuccess() { if (animator) animator.SetTrigger(P_TrSuccess); }
        public void PlayFailure() { if (animator) animator.SetTrigger(P_TrFailure); }
        public void PlayHandoffGive() { if (animator) animator.SetTrigger(P_TrGive); }
        public void PlayHandoffReceive() { if (animator) animator.SetTrigger(P_TrReceive); }
    }
}
