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

        /// <summary>Decisions waiting for the user right now (the reducer's pending decisions).</summary>
        public int PendingDecisionCount => Model.PendingDecisions;
        /// <summary>Deliveries (WorkDelivered events) since the last run start, failure, acknowledgement
        /// or reset. A true count of reducer events; counted in focus mode too (it is a quiet number).</summary>
        public int CompletedCount => _completedCount;
        /// <summary>Time.unscaledTime at which the reducer last reported a decision request
        /// (FeedbackController sounds its Attention cue for that same event). Far in the past if never.</summary>
        public float LastDecisionRequestedAt { get; private set; } = -1000f;
        /// <summary>Time.unscaledTime at which the reducer last reported a failure
        /// (FeedbackController sounds its Error cue for that same event). Far in the past if never.</summary>
        public float LastTaskFailedAt { get; private set; } = -1000f;

        /// <summary>Raised (main thread) when the level, its agent, the quiet count or a count above changed.</summary>
        public event Action<AttentionLevel> Changed;

        /// <summary>Decisions withdrawn because the session no longer reported them as waiting
        /// (see <see cref="AttentionPendingLedger"/>). Zero is the expected value.</summary>
        public int WithdrawnDecisions { get; private set; }

        readonly AttentionPendingLedger _ledger = new AttentionPendingLedger();
        Func<string, bool> _stillPending;
        SessionReducer _reducer;
        bool _bound;
        bool _wasRunning;
        int _completedCount;

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
            _reducer.Applied += OnApplied;
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
                _reducer.Applied -= OnApplied;
            }
            if (controller != null) controller.RunStateChanged -= OnRunStateChanged;
            _reducer = null;
            _bound = false;
        }

        /// <summary>The user looked at the latest update: clear completed / error / quiet updates.
        /// A pending decision is never cleared here.</summary>
        public void Acknowledge()
        {
            int version = Model.Version;
            bool had = ClearCompleted();
            Model.Acknowledge();
            RaiseIfOnlyCountChanged(had, version);
        }

        /// <summary>Forget the delivery count. True when it was not already zero.</summary>
        bool ClearCompleted()
        {
            bool had = _completedCount != 0;
            _completedCount = 0;
            return had;
        }

        /// <summary>The model raises Changed only for its own values; when only the delivery count
        /// moved, listeners are told here (once).</summary>
        void RaiseIfOnlyCountChanged(bool countChanged, int versionBefore)
        {
            if (countChanged && Model.Version == versionBefore) OnModelChanged();
        }

        // State that predates the event stream (start-up, resume, the switch to live).
        void ProjectSnapshot()
        {
            int version = Model.Version;
            bool had = ClearCompleted();
            Model.Reset();
            _ledger.Clear();
            RaiseIfOnlyCountChanged(had, version);
            if (_reducer == null) return;
            DecisionView pending = _reducer.FirstPendingDecision();
            if (pending != null)
            {
                _ledger.Add(pending.DecisionId);
                Model.DecisionRequested(pending.DecisionId,
                    string.IsNullOrEmpty(pending.AgentId) ? AgentIds.Researcher : pending.AgentId);
            }
        }

        /// <summary>True while the session's own record of this decision is still "requested".</summary>
        bool StillPending(string decisionId)
        {
            if (_reducer == null || _reducer.Snapshot == null) return false;
            var decisions = _reducer.Snapshot.Decisions;
            if (decisions == null) return false;
            for (int i = 0; i < decisions.Count; i++)
            {
                DecisionView d = decisions[i];
                if (d != null && d.DecisionId == decisionId) return d.State == "requested";
            }
            return false;
        }

        // After every applied event: "Needs you" may only stand while the session still has that
        // decision waiting. Normally a no-op (the resolved event already cleared it).
        void OnApplied(EventEnvelope e)
        {
            if (_ledger.Count == 0) return;
            if (_stillPending == null) _stillPending = StillPending;
            int dropped = _ledger.Reconcile(Model, _stillPending);
            if (dropped > 0)
            {
                WithdrawnDecisions += dropped;
                Debug.LogWarning("[MuseDesk] Attention: withdrew " + dropped + " decision cue(s) the session no longer reports as waiting"
                                 + (e != null ? " (after " + e.Type + " seq " + e.Seq + ")." : "."));
            }
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
            LastDecisionRequestedAt = Time.unscaledTime;     // before the model raises Changed
            _ledger.Add(decisionId);
            Model.DecisionRequested(decisionId, asker);
        }

        void OnDecisionResolved(string decisionId, string state)
        {
            _ledger.Remove(decisionId);
            Model.DecisionResolved(decisionId);
        }

        void OnWorkDelivered(EventEnvelope e)
        {
            int version = Model.Version;
            _completedCount++;
            Model.WorkDelivered(e != null ? e.AgentId : null);
            RaiseIfOnlyCountChanged(true, version);
        }

        void OnTaskFailed(EventEnvelope e)
        {
            int version = Model.Version;
            LastTaskFailedAt = Time.unscaledTime;            // before the model raises Changed
            bool had = ClearCompleted();                     // the model drops its completion here too
            Model.TaskFailed(e != null ? e.AgentId : null);
            RaiseIfOnlyCountChanged(had, version);
        }

        void OnSessionReset()
        {
            int version = Model.Version;
            bool had = ClearCompleted();
            _ledger.Clear();
            Model.Reset();
            RaiseIfOnlyCountChanged(had, version);
        }

        void OnRunStateChanged()
        {
            bool running = controller != null && controller.RunInProgress;
            if (running && !_wasRunning)
            {
                int version = Model.Version;
                bool had = ClearCompleted();
                Model.RunStarted();
                RaiseIfOnlyCountChanged(had, version);
            }
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
