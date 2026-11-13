using System;
using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;
using MuseDesk.Workspace;
using AgentDto = MuseDesk.Models.AgentView;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// The one place that decides how much attention the office is asking for, whatever the
    /// spatial mode. It is driven only by the session reducer's events (and the reducer's
    /// snapshot on load), so a level is reported only after the backend, or the labelled demo
    /// gateway, confirmed what caused it. It shows nothing itself: <see cref="AttentionBeacon"/>
    /// and other surfaces read <see cref="Level"/> and listen to <see cref="Changed"/>.
    ///
    /// Focus mode (FocusModeController.Active): progress and completions accumulate quietly in
    /// <see cref="QuietCount"/>; a pending decision and a failure stay visible. Nothing is ever
    /// approved, rejected or dismissed on the user's behalf.
    /// </summary>
    public class AttentionController : MonoBehaviour
    {
        public SessionController controller;

        /// <summary>The scene's attention controller, or null.</summary>
        public static AttentionController Instance { get; private set; }

        /// <summary>The classification itself (plain class; see <see cref="AttentionModel"/>).</summary>
        public AttentionModel Model { get; } = new AttentionModel();

        /// <summary>The highest attention level that is currently true.</summary>
        public AttentionLevel Level => Model.Level;
        /// <summary>The agent the level concerns (researcher / developer / tester), or null.</summary>
        public string AgentId => Model.AgentId;
        /// <summary>The agent of the most recent update of any kind, or null.</summary>
        public string LatestAgentId => Model.LatestAgentId;
        /// <summary>Updates held back while focus mode was on.</summary>
        public int QuietCount => Model.QuietCount;

        /// <summary>Raised (main thread) when the level, its agent or the quiet count changed.</summary>
        public event Action<AttentionLevel> Changed;

        SessionReducer _reducer;
        bool _bound;
        bool _wasRunning;

        void Awake()
        {
            Instance = this;
            Model.Changed += OnModelChanged;
        }

        void OnDestroy()
        {
            Model.Changed -= OnModelChanged;
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!_bound && controller != null) Bind();
            // A static bool read; SetFocus does nothing unless the value changed.
            Model.SetFocus(FocusModeController.Active);
        }

        void Bind()
        {
            _reducer = controller.Reducer;
            _reducer.AgentChanged += OnAgentChanged;
            _reducer.DecisionRequested += OnDecisionRequested;
            _reducer.DecisionResolved += OnDecisionResolved;
            _reducer.WorkDelivered += OnWorkDelivered;
            _reducer.TaskFailed += OnTaskFailed;
            _reducer.SessionReset += OnSessionReset;
            _reducer.SnapshotLoaded += ProjectSnapshot;
            controller.RunStateChanged += OnRunStateChanged;
            _wasRunning = controller.RunInProgress;
            _bound = true;
            ProjectSnapshot();
        }

        void OnDisable()
        {
            if (!_bound) return;
            if (_reducer != null)
            {
                _reducer.AgentChanged -= OnAgentChanged;
                _reducer.DecisionRequested -= OnDecisionRequested;
                _reducer.DecisionResolved -= OnDecisionResolved;
                _reducer.WorkDelivered -= OnWorkDelivered;
                _reducer.TaskFailed -= OnTaskFailed;
                _reducer.SessionReset -= OnSessionReset;
                _reducer.SnapshotLoaded -= ProjectSnapshot;
            }
            if (controller != null) controller.RunStateChanged -= OnRunStateChanged;
            _reducer = null;
            _bound = false;
        }

        /// <summary>The user looked at the latest update: clear completed / error / quiet updates.
        /// A pending decision is never cleared here.</summary>
        public void Acknowledge() { Model.Acknowledge(); }

        // State that predates the event stream (start-up, resume, the switch to live).
        void ProjectSnapshot()
        {
            Model.Reset();
            if (_reducer == null) return;
            DecisionView pending = _reducer.FirstPendingDecision();
            if (pending != null)
                Model.DecisionRequested(pending.DecisionId,
                    string.IsNullOrEmpty(pending.AgentId) ? AgentIds.Researcher : pending.AgentId);
        }

        void OnAgentChanged(AgentDto agent)
        {
            if (agent == null || string.IsNullOrEmpty(agent.AgentId)) return;
            Model.AgentProgress(agent.AgentId);
        }

        void OnDecisionRequested(string decisionId, EventEnvelope e)
        {
            // The agent that asks is named on the event; the researcher is the one who asks today
            // (the same convention AgentNameplate uses).
            string asker = e != null && !string.IsNullOrEmpty(e.AgentId) ? e.AgentId : AgentIds.Researcher;
            Model.DecisionRequested(decisionId, asker);
        }

        void OnDecisionResolved(string decisionId, string state) { Model.DecisionResolved(decisionId); }

        void OnWorkDelivered(EventEnvelope e) { Model.WorkDelivered(e != null ? e.AgentId : null); }

        void OnTaskFailed(EventEnvelope e) { Model.TaskFailed(e != null ? e.AgentId : null); }

        void OnSessionReset() { Model.Reset(); }

        void OnRunStateChanged()
        {
            bool running = controller != null && controller.RunInProgress;
            if (running && !_wasRunning) Model.RunStarted();
            _wasRunning = running;
        }

        void OnModelChanged()
        {
            var h = Changed;
            if (h == null) return;
            try { h(Model.Level); } catch (Exception ex) { Debug.LogException(ex); }
        }
    }
}
