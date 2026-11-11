using System;
using System.Collections.Generic;
using MuseDesk.Models;

namespace MuseDesk.Networking
{
    /// <summary>
    /// Applies backend events to a local snapshot, identically in live and demo mode.
    /// Only advances on a strictly newer seq (idempotent replay), so reconnect + replay
    /// and duplicate events converge to exactly one current state. The presentation layer
    /// subscribes to the typed events rather than parsing envelopes itself.
    /// </summary>
    public class SessionReducer
    {
        public SessionSnapshot Snapshot { get; private set; } = new SessionSnapshot();
        public long LastSeq { get; private set; }

        readonly Dictionary<string, AgentView> _agents = new Dictionary<string, AgentView>();
        readonly Dictionary<string, TaskView> _tasks = new Dictionary<string, TaskView>();
        readonly Dictionary<string, DecisionView> _decisions = new Dictionary<string, DecisionView>();

        public event Action<AgentView> AgentChanged;
        public event Action<string, EventEnvelope> DecisionRequested;   // decisionId, envelope
        public event Action<string, string> DecisionResolved;           // decisionId, state
        public event Action<EventEnvelope> ArtifactCreated;
        public event Action<EventEnvelope> TaskCompleted;
        public event Action<EventEnvelope> Applied;                     // any applied event
        public event Action SessionReset;

        public void LoadSnapshot(SessionSnapshot snap)
        {
            if (snap == null) return;
            Snapshot = snap;
            LastSeq = snap.LastSeq;
            _agents.Clear(); _tasks.Clear(); _decisions.Clear();
            foreach (var a in snap.Agents) _agents[a.AgentId] = a;
            foreach (var t in snap.Tasks) _tasks[t.TaskId] = t;
            foreach (var d in snap.Decisions) _decisions[d.DecisionId] = d;
        }

        /// <summary>Apply one event. Returns true if it advanced state (new seq).</summary>
        public bool Apply(EventEnvelope e)
        {
            if (e == null) return false;
            if (e.Seq <= LastSeq) return false;           // already applied / out of order dup
            LastSeq = e.Seq;

            switch (e.Type)
            {
                case EventTypes.SessionReady:
                    ApplySessionReady(e);
                    break;
                case EventTypes.AgentStatusChanged:
                    ApplyAgentStatus(e);
                    break;
                case EventTypes.TaskCreated:
                case EventTypes.TaskStarted:
                    ApplyTaskUpsert(e);
                    break;
                case EventTypes.DecisionRequested:
                    UpsertDecision(e.DecisionId, e.TaskId, e.AgentId, "requested",
                        e.PayloadString("summary"), e.PayloadString("recommendation"));
                    DecisionRequested?.Invoke(e.DecisionId, e);
                    break;
                case EventTypes.DecisionResolved:
                    var state = e.PayloadString("state", "resolved");
                    if (_decisions.TryGetValue(e.DecisionId ?? "", out var dv)) dv.State = state;
                    DecisionResolved?.Invoke(e.DecisionId, state);
                    break;
                case EventTypes.ArtifactCreated:
                    ArtifactCreated?.Invoke(e);
                    break;
                case EventTypes.TaskCompleted:
                    if (e.TaskId != null && _tasks.TryGetValue(e.TaskId, out var tc))
                        tc.Status = "completed";
                    TaskCompleted?.Invoke(e);
                    break;
                case EventTypes.TaskFailed:
                    if (e.TaskId != null && _tasks.TryGetValue(e.TaskId, out var tf))
                        tf.Status = "failed";
                    break;
                case EventTypes.SessionReset:
                    Reset();
                    SessionReset?.Invoke();
                    break;
            }

            Snapshot.LastSeq = LastSeq;
            Applied?.Invoke(e);
            return true;
        }

        void ApplySessionReady(EventEnvelope e)
        {
            var agents = e.PayloadValue<List<AgentView>>("agents");
            if (agents == null) return;
            foreach (var a in agents) _agents[a.AgentId] = a;
            Snapshot.Agents = new List<AgentView>(_agents.Values);
        }

        void ApplyAgentStatus(EventEnvelope e)
        {
            if (string.IsNullOrEmpty(e.AgentId)) return;
            if (!_agents.TryGetValue(e.AgentId, out var a))
            {
                a = new AgentView { AgentId = e.AgentId, Role = e.AgentId, DisplayName = e.AgentId };
                _agents[e.AgentId] = a;
            }
            a.State = e.PayloadString("state", a.State);
            Snapshot.Agents = new List<AgentView>(_agents.Values);
            AgentChanged?.Invoke(a);
        }

        void ApplyTaskUpsert(EventEnvelope e)
        {
            var id = e.TaskId ?? e.PayloadString("task_id");
            if (string.IsNullOrEmpty(id)) return;
            if (!_tasks.TryGetValue(id, out var t))
            {
                t = new TaskView { TaskId = id };
                _tasks[id] = t;
            }
            t.AgentId = e.AgentId ?? t.AgentId;
            t.Status = e.PayloadString("task_state", t.Status ?? "queued");
            Snapshot.Tasks = new List<TaskView>(_tasks.Values);
        }

        void UpsertDecision(string id, string taskId, string agentId, string state,
                            string summary, string recommendation)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!_decisions.TryGetValue(id, out var d))
            {
                d = new DecisionView { DecisionId = id };
                _decisions[id] = d;
            }
            d.TaskId = taskId ?? d.TaskId;
            d.AgentId = agentId ?? d.AgentId;
            d.State = state;
            d.Summary = summary ?? d.Summary;
            d.Recommendation = recommendation ?? d.Recommendation;
            Snapshot.Decisions = new List<DecisionView>(_decisions.Values);
        }

        public void Reset()
        {
            _agents.Clear(); _tasks.Clear(); _decisions.Clear();
            Snapshot = new SessionSnapshot();
            LastSeq = 0;
        }
    }
}
