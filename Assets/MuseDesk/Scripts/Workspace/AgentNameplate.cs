using TMPro;
using UnityEngine;
using MuseDesk.Art;
using MuseDesk.Models;
using MuseDesk.Networking;

namespace MuseDesk.Workspace
{
    /// <summary>
    /// A small nameplate on the front edge of an agent's desk: the role on the first line and what
    /// the agent is doing, in plain words, on the second. It replaces the floating status panel
    /// and the floating name tag.
    ///
    /// The words come only from session reducer state (agent.status_changed, plus whether a
    /// decision from this agent is waiting for the user) through the mapping shared with
    /// WorkstationStatusDisplay. The text is rebuilt only when an event changes it; nothing here
    /// runs per frame except a short scale pulse that marks a change. Presentation only.
    /// </summary>
    public class AgentNameplate : MonoBehaviour
    {
        public SessionController controller;
        [Tooltip("Backend agent id this plate mirrors: researcher / developer / tester.")]
        public string agentId = AgentIds.Researcher;
        [Tooltip("Role name printed on the first line.")]
        public string roleName = "Researcher";

        [Header("Plate")]
        public TMP_Text roleText;
        public TMP_Text activityText;
        [Tooltip("Scale of the plate when shown (used when the builder saved it hidden).")]
        public Vector3 shownScale = Vector3.one;

        readonly ScaleEase _ease = new ScaleEase();
        bool _bound;
        bool _decisionPending;
        string _state = AgentStates.Idle;
        WorkstationStatusDisplay.Activity _activity = WorkstationStatusDisplay.Activity.Idle;
        bool _rendered;

        void Awake()
        {
            _ease.Init(transform, ScaleEase.ResolveFull(transform, shownScale), 0.2f, true);
            if (roleText != null && roleText.text != roleName) roleText.text = roleName;
        }

        void OnEnable() { Render(false); }

        void Update()
        {
            if (!_bound && controller != null) Bind();
            _ease.Tick(Time.deltaTime);
        }

        void Bind()
        {
            var r = controller.Reducer;
            r.AgentChanged += OnAgentChanged;
            r.DecisionRequested += OnDecisionRequested;
            r.DecisionResolved += OnDecisionResolved;
            r.SessionReset += OnSessionReset;
            r.SnapshotLoaded += ProjectSnapshot;
            _bound = true;
            ProjectSnapshot();
        }

        void OnDisable()
        {
            if (!_bound || controller == null) return;
            var r = controller.Reducer;
            r.AgentChanged -= OnAgentChanged;
            r.DecisionRequested -= OnDecisionRequested;
            r.DecisionResolved -= OnDecisionResolved;
            r.SessionReset -= OnSessionReset;
            r.SnapshotLoaded -= ProjectSnapshot;
            _bound = false;
        }

        // State that predates the event stream (start-up, resume, the switch to live).
        void ProjectSnapshot()
        {
            var r = controller.Reducer;
            _state = AgentStates.Idle;
            var agents = r.Snapshot != null ? r.Snapshot.Agents : null;
            if (agents != null)
                for (int i = 0; i < agents.Count; i++)
                    if (agents[i] != null && agents[i].AgentId == agentId)
                    {
                        if (!string.IsNullOrEmpty(agents[i].State)) _state = agents[i].State;
                        break;
                    }
            DecisionView pending = r.FirstPendingDecision();
            _decisionPending = pending != null && pending.AgentId == agentId;
            Render(true);
        }

        void OnAgentChanged(AgentView agent)
        {
            if (agent == null || agent.AgentId != agentId) return;
            _state = string.IsNullOrEmpty(agent.State) ? _state : agent.State;
            Render(true);
        }

        void OnDecisionRequested(string decisionId, EventEnvelope e)
        {
            // The agent that asks is named on the event; the researcher is the one who asks today.
            string asker = e != null && !string.IsNullOrEmpty(e.AgentId) ? e.AgentId : AgentIds.Researcher;
            if (asker != agentId) return;
            _decisionPending = true;
            Render(true);
        }

        void OnDecisionResolved(string decisionId, string state)
        {
            if (!_decisionPending) return;
            _decisionPending = false;
            Render(true);
        }

        void OnSessionReset()
        {
            _state = AgentStates.Idle;
            _decisionPending = false;
            Render(true);
        }

        void Render(bool pulse)
        {
            var activity = WorkstationStatusDisplay.Classify(_state, _decisionPending);
            if (_rendered && activity == _activity) return;
            _rendered = true;
            _activity = activity;
            if (activityText != null)
            {
                activityText.text = WorkstationStatusDisplay.Words(activity);
                // Colour only repeats what the words already say.
                activityText.color =
                    activity == WorkstationStatusDisplay.Activity.WaitingForUser ? OfficeTokens.Amber
                    : activity == WorkstationStatusDisplay.Activity.Error ? OfficeTokens.Coral
                    : OfficeTokens.OnPlate;
            }
            if (pulse) _ease.Pulse(0.05f, 0.28f);
        }
    }
}
