using System;
using System.Collections.Generic;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// How much of the user's attention the office is asking for. The numeric order is the
    /// priority order: when several things are true at once the highest one is the level.
    /// Blocking outranks Error because it is the one the user can act on right now.
    ///
    /// <see cref="Suggestion"/> is defined for completeness and is UNREACHABLE: the backend has no
    /// event for an optional agent suggestion, and this app never invents one. Nothing in the
    /// model can produce it.
    /// </summary>
    public enum AttentionLevel
    {
        None = 0,
        /// <summary>An agent reported progress. Never shown as a cue; available to quiet surfaces.</summary>
        Passive = 1,
        /// <summary>Unreachable today (no backend event). See the type summary.</summary>
        Suggestion = 2,
        /// <summary>Work was delivered (a completion that references an artifact).</summary>
        Completed = 3,
        /// <summary>A task failed.</summary>
        Error = 4,
        /// <summary>A decision is waiting for the user.</summary>
        Blocking = 5,
    }

    /// <summary>
    /// Mode-independent attention classification. A plain class with no Unity dependency so it can
    /// be tested without a scene: <see cref="AttentionController"/> feeds it the session reducer's
    /// events and nothing else, so every level it reports was confirmed by the backend or by the
    /// labelled demo gateway.
    ///
    /// Focus mode: passive updates and completions are counted quietly (<see cref="QuietCount"/>)
    /// instead of raising the level; a held-back completion surfaces when focus is left. Blocking
    /// and Error are never held back. Nothing here answers a decision: Blocking ends only when
    /// the reducer reports the decision resolved, or on a reset.
    /// </summary>
    public sealed class AttentionModel
    {
        /// <summary>The highest level that is currently true.</summary>
        public AttentionLevel Level { get; private set; }
        /// <summary>The agent the current level concerns, or null when unknown or at None.</summary>
        public string AgentId { get; private set; }
        /// <summary>Updates held back while focus mode was on and not yet acknowledged.</summary>
        public int QuietCount { get; private set; }
        /// <summary>True while focus mode is on (mirrors FocusModeController.Active).</summary>
        public bool Focus { get; private set; }
        /// <summary>Decisions currently waiting for the user.</summary>
        public int PendingDecisions => _pendingIds.Count;
        /// <summary>The agent of the most recent update of any kind, or null.</summary>
        public string LatestAgentId { get; private set; }
        /// <summary>Incremented every time <see cref="Changed"/> is raised.</summary>
        public int Version { get; private set; }

        /// <summary>Raised when the level, its agent or the quiet count changed.</summary>
        public event Action Changed;

        readonly List<string> _pendingIds = new List<string>(2);
        readonly List<string> _pendingAgents = new List<string>(2);
        bool _passive, _completed, _heldCompleted, _error;
        string _passiveAgent, _completedAgent, _heldCompletedAgent, _errorAgent;

        /// <summary>An agent.status_changed was applied.</summary>
        public void AgentProgress(string agentId)
        {
            if (!string.IsNullOrEmpty(agentId)) LatestAgentId = agentId;
            if (Focus) QuietCount++;
            else { _passive = true; _passiveAgent = agentId; }
            Recompute(Focus);
        }

        /// <summary>A decision.requested was applied (or a pending decision was found in a snapshot).</summary>
        public void DecisionRequested(string decisionId, string agentId)
        {
            if (string.IsNullOrEmpty(decisionId)) return;
            int at = _pendingIds.IndexOf(decisionId);
            if (at >= 0) { _pendingAgents[at] = agentId; }
            else { _pendingIds.Add(decisionId); _pendingAgents.Add(agentId); }
            if (!string.IsNullOrEmpty(agentId)) LatestAgentId = agentId;
            Recompute(false);
        }

        /// <summary>A decision.resolved was applied. Unknown ids are ignored.</summary>
        public void DecisionResolved(string decisionId)
        {
            int at = string.IsNullOrEmpty(decisionId) ? -1 : _pendingIds.IndexOf(decisionId);
            if (at < 0) return;
            _pendingIds.RemoveAt(at);
            _pendingAgents.RemoveAt(at);
            Recompute(false);
        }

        /// <summary>Work was delivered (task.completed with an artifact).</summary>
        public void WorkDelivered(string agentId)
        {
            if (!string.IsNullOrEmpty(agentId)) LatestAgentId = agentId;
            _error = false; _errorAgent = null;           // the latest outcome is the one that stands
            if (Focus)
            {
                QuietCount++;
                _heldCompleted = true; _heldCompletedAgent = agentId;
            }
            else { _completed = true; _completedAgent = agentId; }
            Recompute(Focus);
        }

        /// <summary>A task.failed was applied. Never held back by focus mode.</summary>
        public void TaskFailed(string agentId)
        {
            if (!string.IsNullOrEmpty(agentId)) LatestAgentId = agentId;
            _completed = false; _completedAgent = null;
            _heldCompleted = false; _heldCompletedAgent = null;
            _error = true; _errorAgent = agentId;
            Recompute(false);
        }

        /// <summary>A new run started: the previous run's outcome no longer needs attention.</summary>
        public void RunStarted()
        {
            _passive = _completed = _heldCompleted = _error = false;
            _passiveAgent = _completedAgent = _heldCompletedAgent = _errorAgent = null;
            Recompute(false);
        }

        /// <summary>The user looked at the latest update. Clears everything except pending
        /// decisions: a decision is only ever cleared by the backend resolving it.</summary>
        public void Acknowledge()
        {
            bool quiet = QuietCount != 0;
            QuietCount = 0;
            _passive = _completed = _heldCompleted = _error = false;
            _passiveAgent = _completedAgent = _heldCompletedAgent = _errorAgent = null;
            Recompute(quiet);
        }

        /// <summary>Follow focus mode. Entering starts a fresh quiet count; leaving keeps the count
        /// and surfaces a completion that was held back.</summary>
        public void SetFocus(bool on)
        {
            if (on == Focus) return;
            Focus = on;
            bool quiet = false;
            if (on)
            {
                quiet = QuietCount != 0;
                QuietCount = 0;
                _heldCompleted = false; _heldCompletedAgent = null;
            }
            else if (_heldCompleted)
            {
                _completed = true; _completedAgent = _heldCompletedAgent;
                _heldCompleted = false; _heldCompletedAgent = null;
            }
            Recompute(quiet);
        }

        /// <summary>Session reset or a fresh snapshot: forget everything except the focus setting.</summary>
        public void Reset()
        {
            bool quiet = QuietCount != 0;
            QuietCount = 0;
            _pendingIds.Clear(); _pendingAgents.Clear();
            _passive = _completed = _heldCompleted = _error = false;
            _passiveAgent = _completedAgent = _heldCompletedAgent = _errorAgent = null;
            LatestAgentId = null;
            Recompute(quiet);
        }

        void Recompute(bool quietChanged)
        {
            AttentionLevel level; string agent;
            if (_pendingIds.Count > 0) { level = AttentionLevel.Blocking; agent = _pendingAgents[0]; }
            else if (_error) { level = AttentionLevel.Error; agent = _errorAgent; }
            else if (_completed && !Focus) { level = AttentionLevel.Completed; agent = _completedAgent; }
            else if (_passive && !Focus) { level = AttentionLevel.Passive; agent = _passiveAgent; }
            else { level = AttentionLevel.None; agent = null; }

            bool changed = quietChanged || level != Level || agent != AgentId;
            Level = level; AgentId = agent;
            if (!changed) return;
            Version++;
            var h = Changed;
            if (h != null) h();
        }
    }
}
