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

        readonly Dictionary<string, AgentView> _byId = new Dictionary<string, AgentView>();
        SessionReducer _reducer;

        void Awake()
        {
            _byId.Clear();
            foreach (var a in agents)
                if (a != null && !string.IsNullOrEmpty(a.agentId)) _byId[a.agentId] = a;
        }

        void OnDisable()
        {
            if (_reducer != null) _reducer.AgentChanged -= OnAgentChanged;
            _reducer = null;
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
            foreach (var a in _reducer.Snapshot.Agents) OnAgentChanged(a);
        }

        void OnAgentChanged(AgentDto view)
        {
            if (view == null) return;
            if (_byId.TryGetValue(view.AgentId, out var v)) v.SetState(view.State);
        }
    }
}
