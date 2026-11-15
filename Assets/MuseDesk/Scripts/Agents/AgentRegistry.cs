using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Networking;
using AgentDto = MuseDesk.Models.AgentView;

namespace MuseDesk.Agents
{
    /// <summary>
    /// Routes reducer agent-state changes to the matching AgentView. One AgentView per
    /// logical agent id; no view invents state independent of the server/reducer.
    /// </summary>
    public class AgentRegistry : MonoBehaviour
    {
        public SessionController controller;
        public List<AgentView> agents = new List<AgentView>();
        [Tooltip("Where the researcher carries a decision paper when one is pending.")]
        public Transform deliveryAnchor;

        readonly Dictionary<string, AgentView> _byId = new Dictionary<string, AgentView>();
        SessionReducer _reducer;
        AgentMovement _researcherMove;
        AgentAnimatorBridge _researcherBridge;
        string _deliveredDecisionId;

        void Awake()
        {
            _byId.Clear();
            foreach (var a in agents)
                if (a != null && !string.IsNullOrEmpty(a.agentId)) _byId[a.agentId] = a;
            if (_byId.TryGetValue("researcher", out var r))
            {
                _researcherMove = r.GetComponent<AgentMovement>();
                _researcherBridge = r.GetComponent<AgentAnimatorBridge>();
            }
        }

        void OnDisable()
        {
            if (_reducer != null)
            {
                _reducer.AgentChanged -= OnAgentChanged;
                _reducer.DecisionRequested -= OnDecisionRequested;
                _reducer.DecisionResolved -= OnDecisionResolved;
                _reducer.SnapshotLoaded -= ProjectSnapshot;
                _reducer.SessionReset -= OnSessionReset;
                _reducer.TaskCompleted -= OnTaskCompleted;
            }
            _reducer = null;
        }

        void OnDecisionRequested(string decisionId, MuseDesk.Models.EventEnvelope e)
        {
            if (decisionId == _deliveredDecisionId) return;   // already walked up for this one
            _deliveredDecisionId = decisionId;
            // Walk to the hand-over spot: a fixed point on the USER's side of the desks, clear of the
            // factory line (the workspace always faces the user, so this is reachable). Without an
            // anchor, fall back to a point straight in front of the head.
            if (_researcherMove != null)
            {
                if (deliveryAnchor != null) _researcherMove.DeliverTo(deliveryAnchor, null, true);
                else _researcherMove.DeliverInFrontOfUser(0.4f);
            }
            if (_byId.TryGetValue("researcher", out var r)) r.SetAlert(true);
            // Play the Offer_Document reaction once the agent has actually walked up (not mid-stride).
            if (_researcherBridge != null) StartCoroutine(OfferWhenArrived());
        }

        IEnumerator OfferWhenArrived()
        {
            // Wait for the delivery walk to finish, then present the paper.
            float guard = 0f;
            while (_researcherMove != null && _researcherMove.IsMoving && guard < 5f)
            { guard += Time.deltaTime; yield return null; }
            if (_researcherBridge != null)
            {
                _researcherBridge.PlayOffer();
                // Polish S9b: the paper has really arrived (the agent stands at the user holding it out) and the
                // decision is still waiting on them. Quiet; muted under Focus; rate-limited in FeedbackController.
                if (_researcherBridge.IsOffering && _reducer != null && _reducer.FirstPendingDecision() != null)
                    MuseDesk.Feedback.FeedbackController.PlayCue(MuseDesk.Feedback.FeedbackCue.PaperArrived);
            }
        }

        void OnDecisionResolved(string decisionId, string state)
        {
            if (_byId.TryGetValue("researcher", out var r)) r.SetAlert(false);
            // Agent visibly reacts to the REAL backend outcome, then walks home.
            StartCoroutine(ReactAndReturn(state));
        }

        IEnumerator ReactAndReturn(string state)
        {
            string s = (state ?? "").ToLowerInvariant();
            bool approved = s.Contains("approv");
            if (_researcherBridge != null)
            {
                if (approved) _researcherBridge.PlaySuccess();
                // A rejection or a request for changes is the user's decision, not a failure: no
                // failure clip. The agent lowers the paper and walks back calmly.
                else _researcherBridge.EndOffer();
            }
            // Let the reaction read before walking back; after a rejection only a short beat.
            yield return new WaitForSeconds(approved ? 1.2f : 0.5f);
            // An approved decision starts a researcher→developer handoff, which owns the walk and
            // returns the courier home itself; sending it home here would cut that walk short.
            if (AgentHandoffController.AnyBusy) yield break;
            if (_researcherMove != null) _researcherMove.ReturnHome();
        }

        void Update()
        {
            // SessionController builds its gateway asynchronously; bind once the reducer is live.
            if (_reducer == null && controller != null) TryBind();
        }

        void TryBind()
        {
            _reducer = controller.Reducer;
            _reducer.AgentChanged += OnAgentChanged;
            _reducer.DecisionRequested += OnDecisionRequested;
            _reducer.DecisionResolved += OnDecisionResolved;
            _reducer.SnapshotLoaded += ProjectSnapshot;
            _reducer.SessionReset += OnSessionReset;
            _reducer.TaskCompleted += OnTaskCompleted;
            ProjectSnapshot();
        }

        // Polish S9b: a confirmed task.completed (reducer event, never a local button) earns the agent one Nodding
        // one-shot. A completion that DELIVERED work (artifact_id) is celebrated by CompletionSequence (the Tester's
        // Success, the colleagues' Task_Completed nod), so it is left to that sequence and gets no second nod here;
        // a cancelled branch is not a completion. Rate limit, Focus and body-busy gating live in the bridge.
        void OnTaskCompleted(MuseDesk.Models.EventEnvelope e)
        {
            if (e == null || string.IsNullOrEmpty(e.AgentId)) return;
            if (!string.IsNullOrEmpty(e.PayloadString("artifact_id"))) return;
            if (e.PayloadString("task_state", "completed") != "completed") return;
            if (!_byId.TryGetValue(e.AgentId, out var v) || v == null) return;
            var bridge = v.GetComponent<AgentAnimatorBridge>();
            if (bridge != null && bridge.PlayConfirmedNod()) NodsPlayed++;
        }

        /// <summary>Confirmed-completion nods that actually started (evidence for a Play Mode driver).</summary>
        public int NodsPlayed { get; private set; }

        // Project the authoritative snapshot onto the views. Runs at bind time and again whenever a
        // snapshot is (re)loaded, so a restart or resume shows what the agents are really doing and
        // re-delivers a decision that was already waiting for the user.
        void ProjectSnapshot()
        {
            foreach (var a in _reducer.Snapshot.Agents) OnAgentChanged(a);
            var pending = _reducer.FirstPendingDecision();
            if (pending != null && pending.DecisionId != _deliveredDecisionId)
                OnDecisionRequested(pending.DecisionId, null);
        }

        // Replay: no alert, nobody mid-delivery, and the next decision is delivered afresh.
        void OnSessionReset()
        {
            StopAllCoroutines();
            _deliveredDecisionId = null;
            foreach (var a in agents) if (a != null) a.SetAlert(false);
            if (_researcherMove != null && !_researcherMove.AtHome) _researcherMove.ReturnHome();
        }

        void OnAgentChanged(AgentDto view)
        {
            if (view == null || string.IsNullOrEmpty(view.AgentId)) return;
            if (_byId.TryGetValue(view.AgentId, out var v)) v.SetState(view.State, view.Progress);
        }
    }
}
