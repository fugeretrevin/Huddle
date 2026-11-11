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

        void Awake()
        {
            _byId.Clear();
            foreach (var a in agents)
                if (a != null && !string.IsNullOrEmpty(a.agentId)) _byId[a.agentId] = a;
            if (_byId.TryGetValue("researcher", out var r))
                _researcherMove = r.GetComponent<AgentMovement>();
        }

        void OnDisable()
        {
            if (_reducer != null)
            {
                _reducer.AgentChanged -= OnAgentChanged;
                _reducer.DecisionRequested -= OnDecisionRequested;
                _reducer.DecisionResolved -= OnDecisionResolved;
            }
            _reducer = null;
        }

        void OnDecisionRequested(string decisionId, MuseDesk.Models.EventEnvelope e)
        {
            // Walk toward the user (reachable), not to a fixed desk anchor that may sit
            // on the far/monitor side after orientation.
            if (_researcherMove != null) _researcherMove.DeliverInFrontOfUser(0.4f);
            if (_byId.TryGetValue("researcher", out var r)) r.SetAlert(true);
        }

        void OnDecisionResolved(string decisionId, string state)
        {
            if (_researcherMove != null) _researcherMove.ReturnHome();
            if (_byId.TryGetValue("researcher", out var r)) r.SetAlert(false);
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
            foreach (var a in _reducer.Snapshot.Agents) OnAgentChanged(a);
        }

        void OnAgentChanged(AgentDto view)
        {
            if (view == null) return;
            if (_byId.TryGetValue(view.AgentId, out var v)) v.SetState(view.State);
        }
    }
}
