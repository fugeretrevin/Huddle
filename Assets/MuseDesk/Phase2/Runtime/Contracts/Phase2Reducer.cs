using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Phase2.Contracts
{
    /// <summary>
    /// Applies v2 events to a local projection of the authoritative v2 snapshot, identically in live
    /// and demo mode. Discipline (master §3.3):
    ///   1. LoadSnapshot(last_seq) hydrates state and sets lastAppliedSeq = last_seq.
    ///   2. The caller then subscribes with after_seq = LastAppliedSeq.
    ///   3. Apply() only advances on a strictly newer seq; duplicates (by event_id OR by seq) are
    ///      dropped — so replay + reconnect converge to exactly one current state and a duplicated
    ///      handoff.created never produces two courier cues.
    ///   4. On a seq GAP (an event arrives more than one ahead of lastApplied) NO state is applied;
    ///      ResyncNeeded fires so the caller re-fetches the snapshot and re-subscribes. No speculative
    ///      state is ever kept.
    ///
    /// Presenters subscribe to the typed C# events rather than parsing envelopes themselves. The
    /// reducer mutates only plain data (no GameObjects); presenters do the scene work on the main
    /// thread. v2 is a SEPARATE seq space from v1 — this reducer never sees v1 seq and vice-versa.
    /// </summary>
    public class Phase2Reducer
    {
        // ─── authoritative projection ───
        public string SessionId { get; private set; }
        public string SourceMode { get; private set; }
        public long LastAppliedSeq { get; private set; }

        readonly Dictionary<string, TaskCardV2> _tasks = new Dictionary<string, TaskCardV2>();
        readonly Dictionary<string, HandoffV2> _handoffs = new Dictionary<string, HandoffV2>();
        readonly Dictionary<string, ArtifactRefV2> _artifacts = new Dictionary<string, ArtifactRefV2>();
        readonly Dictionary<string, InboxItemV2> _inbox = new Dictionary<string, InboxItemV2>();
        public FocusSnapshotV2 Focus { get; private set; } = new FocusSnapshotV2 { Mode = FocusModes.Normal };

        // Bounded event_id dedup memory (most recent ids). Seq ordering already catches the common
        // case; this additionally guards an id re-sent at a new seq slot (it won't be, per the bus,
        // but the contract asks for id-dedup explicitly).
        readonly HashSet<string> _seenEventIds = new HashSet<string>();
        readonly Queue<string> _seenOrder = new Queue<string>();
        const int MaxSeenIds = 4096;

        public IReadOnlyDictionary<string, TaskCardV2> Tasks => _tasks;
        public IReadOnlyDictionary<string, HandoffV2> Handoffs => _handoffs;
        public IReadOnlyDictionary<string, ArtifactRefV2> Artifacts => _artifacts;
        public IReadOnlyDictionary<string, InboxItemV2> Inbox => _inbox;

        // ─── typed events for presenters ───
        public event Action SnapshotLoaded;                 // full reset: rebuild all views
        public event Action<TaskCardV2> TaskChanged;        // a task card upserted/mutated
        public event Action<string> TaskRemoved;            // rc22: a card was removed from the board (task id)
        public event Action<HandoffV2> HandoffCreated;      // deduped: fires once per handoff_id
        public event Action<HandoffV2> HandoffChanged;      // received/failed state change
        public event Action<ArtifactRefV2> ArtifactAdded;   // a new artifact ref known to the snapshot
        public event Action<FocusSnapshotV2> FocusChanged;
        public event Action InboxChanged;
        public event Action<EventEnvelopeV2> DiagramCreated;
        public event Action<EventEnvelopeV2> StationMilestone;
        public event Action<EventEnvelopeV2> Applied;       // any applied event (diagnostics)

        /// <summary>Raised when a seq gap is detected. The caller must re-fetch the snapshot and
        /// re-subscribe with after_seq = LastAppliedSeq. Args: (expectedSeq, gotSeq).</summary>
        public event Action<long, long> ResyncNeeded;

        public TaskCardV2 GetTask(string taskId)
            => taskId != null && _tasks.TryGetValue(taskId, out var t) ? t : null;

        public void LoadSnapshot(SnapshotV2 snap)
        {
            if (snap == null) return;
            SessionId = snap.SessionId;
            SourceMode = snap.SourceMode;
            LastAppliedSeq = snap.LastSeq;

            _tasks.Clear(); _handoffs.Clear(); _artifacts.Clear(); _inbox.Clear();
            _seenEventIds.Clear(); _seenOrder.Clear();

            foreach (var t in snap.Tasks ?? new List<TaskCardV2>()) if (t?.TaskId != null) _tasks[t.TaskId] = t;
            foreach (var h in snap.Handoffs ?? new List<HandoffV2>()) if (h?.HandoffId != null) _handoffs[h.HandoffId] = h;
            foreach (var a in snap.Artifacts ?? new List<ArtifactRefV2>()) if (a?.ArtifactId != null) _artifacts[a.ArtifactId] = a;
            foreach (var i in snap.Inbox ?? new List<InboxItemV2>()) if (i?.ItemId != null) _inbox[i.ItemId] = i;
            Focus = snap.Focus ?? new FocusSnapshotV2 { Mode = FocusModes.Normal };

            SnapshotLoaded?.Invoke();
        }

        /// <summary>
        /// Apply one event. Returns true only if it advanced state. Drops duplicates (by seq or
        /// event_id). On a gap, applies nothing and raises ResyncNeeded.
        /// </summary>
        public bool Apply(EventEnvelopeV2 e)
        {
            if (e == null) return false;

            // Duplicate / already-applied (seq space).
            if (e.Seq <= LastAppliedSeq) return false;

            // Explicit event_id dedup (contract §3.3). Would only matter if an id were re-sent at a
            // newer seq; still honored to be safe. No state applied for a known id.
            if (!string.IsNullOrEmpty(e.EventId) && _seenEventIds.Contains(e.EventId)) return false;

            // Gap: the next expected seq is LastAppliedSeq+1. Anything beyond means we missed events;
            // do NOT apply (no speculative state) — resync from the snapshot instead.
            long expected = LastAppliedSeq + 1;
            if (e.Seq > expected)
            {
                ResyncNeeded?.Invoke(expected, e.Seq);
                return false;
            }

            // Commit the seq advance and remember the id.
            LastAppliedSeq = e.Seq;
            RememberId(e.EventId);

            switch (e.Type)
            {
                case Phase2EventTypes.TaskCreated:
                case Phase2EventTypes.TaskStarted:
                case Phase2EventTypes.TaskProgress:
                case Phase2EventTypes.DecisionRequested:   // card-launched run: awaiting_approval
                case Phase2EventTypes.TaskAssigned:
                case Phase2EventTypes.TaskPriorityChanged:
                case Phase2EventTypes.TaskPauseRequested:
                case Phase2EventTypes.TaskPaused:
                case Phase2EventTypes.TaskResumed:
                case Phase2EventTypes.TaskCompleted:
                case Phase2EventTypes.TaskFailed:
                    ApplyTaskDelta(e);
                    break;

                case Phase2EventTypes.HandoffCreated:
                    ApplyHandoffCreated(e);
                    break;
                case Phase2EventTypes.HandoffReceived:
                    ApplyHandoffState(e, HandoffStates.Received);
                    break;
                case Phase2EventTypes.HandoffFailed:
                    ApplyHandoffState(e, HandoffStates.Failed);
                    break;

                case Phase2EventTypes.ArtifactCreated:
                    ApplyArtifactCreated(e);
                    break;

                case Phase2EventTypes.FocusChanged:
                    ApplyFocusChanged(e);
                    break;

                case Phase2EventTypes.InboxUpdated:
                    ApplyInboxUpdated(e);
                    break;

                case Phase2EventTypes.DiagramCreated:
                    DiagramCreated?.Invoke(e);
                    break;
                case Phase2EventTypes.StationMilestone:
                    StationMilestone?.Invoke(e);
                    break;

                case Phase2EventTypes.SessionReset:
                    ApplySessionReset(e);
                    break;

                // session.ready / agent.status_changed / decision.* / system.warning are mirrored
                // v1 lifecycle types; the board does not derive card truth from them.
                default:
                    break;
            }

            Applied?.Invoke(e);
            return true;
        }

        // ─────────────────────────── appliers ───────────────────────────

        void ApplyTaskDelta(EventEnvelopeV2 e)
        {
            var id = e.TaskId ?? e.PayloadString("task_id");
            if (string.IsNullOrEmpty(id)) return;
            // rc22: a removed card (payload removed:true, sent as task.completed / cancelled) leaves the board model.
            // It is never seeded as a new card, and every view drops it through TaskRemoved.
            if (e.PayloadValue<bool?>("removed") == true)
            {
                if (_tasks.Remove(id)) TaskRemoved?.Invoke(id);
                return;
            }
            if (!_tasks.TryGetValue(id, out var t))
            {
                // task.progress / decision.requested are also mirrored v1 lifecycle types (a v1
                // task id with no card); they only ever update a card that already exists.
                if (e.Type == Phase2EventTypes.TaskProgress || e.Type == Phase2EventTypes.DecisionRequested) return;
                // A task we have not seen (e.g. task.created before snapshot caught it). Seed a card.
                t = new TaskCardV2 { TaskId = id, Dependencies = new List<string>(),
                    SourceMode = SourceMode ?? SourceModes.Live, TaskState = TaskStates.Queued };
                _tasks[id] = t;
            }

            // Apply only the deltas the backend actually emits (task_graph.py). Absent fields are
            // left unchanged; the authoritative values still live in the snapshot after a resync.
            var title = e.PayloadString("title");
            if (!string.IsNullOrEmpty(title)) t.Title = title;

            // Task events carry the canonical task_state; adopt it when present, and fall back to
            // what the event type implies when an older producer omits it.
            var wireState = e.PayloadString("task_state");
            bool hasState = !string.IsNullOrEmpty(wireState);

            switch (e.Type)
            {
                case Phase2EventTypes.TaskAssigned:
                    t.AgentId = e.AgentId ?? e.PayloadString("target_agent_id", t.AgentId);
                    if (hasState) t.TaskState = wireState;
                    break;
                case Phase2EventTypes.TaskPriorityChanged:
                    if (e.TryPayloadInt("priority", out var p)) t.Priority = p;
                    break;
                case Phase2EventTypes.TaskPauseRequested:
                    t.TaskState = TaskStates.PauseRequested;
                    break;
                case Phase2EventTypes.TaskPaused:
                    t.TaskState = TaskStates.Paused;
                    break;
                case Phase2EventTypes.TaskResumed:
                    // Nothing runs a resumed card by itself, so without an explicit state it is
                    // queued — never claimed to be running.
                    t.TaskState = hasState ? wireState : TaskStates.Queued;
                    break;
                case Phase2EventTypes.TaskCompleted:
                    // A rejected / changes-requested run ends as task.completed with
                    // task_state "cancelled"; keep that distinction.
                    t.TaskState = hasState ? wireState : TaskStates.Completed;
                    break;
                case Phase2EventTypes.TaskFailed:
                    t.TaskState = TaskStates.Failed;
                    break;
                case Phase2EventTypes.TaskProgress:
                case Phase2EventTypes.DecisionRequested:
                    // decision.requested on the v2 stream moves a card whose pipeline run asked the
                    // human to awaiting_approval (task.resumed moves it back to running).
                    if (hasState) t.TaskState = wireState;
                    break;
                case Phase2EventTypes.TaskCreated:
                case Phase2EventTypes.TaskStarted:
                    if (hasState) t.TaskState = wireState;
                    if (e.AgentId != null) t.AgentId = e.AgentId;
                    if (e.TryPayloadInt("priority", out var cp)) t.Priority = cp;
                    break;
            }

            // Additive card fields (task.created and later task events may carry them).
            var template = e.PayloadString("task_template");
            if (!string.IsNullOrEmpty(template)) t.TaskTemplate = template;
            bool? executable = e.PayloadValue<bool?>("executable");
            if (executable.HasValue) t.Executable = executable.Value;
            var runTaskId = e.PayloadString("run_task_id");
            if (!string.IsNullOrEmpty(runTaskId)) t.RunTaskId = runTaskId;

            // The decision a card waits on is only meaningful while it is awaiting approval
            // (task.resumed after an approval still names the decision it answered).
            var decisionId = e.PayloadString("decision_id");
            if (t.TaskState != TaskStates.AwaitingApproval) t.DecisionId = null;
            else if (!string.IsNullOrEmpty(decisionId)) t.DecisionId = decisionId;

            // revision is monotonic from the server; adopt it if present so the client's
            // expected_revision for the next command matches the canonical task.
            if (e.TryPayloadInt("revision", out var rev)) t.Revision = rev;

            TaskChanged?.Invoke(t);
        }

        /// <summary>
        /// session.reset on the v2 stream. When the producer includes the post-reset snapshot
        /// (the offline demo does), load it so every presenter rebuilds from it; otherwise ask the
        /// caller to re-fetch the authoritative snapshot. Either way no stale card survives.
        /// </summary>
        void ApplySessionReset(EventEnvelopeV2 e)
        {
            var snap = e.PayloadValue<SnapshotV2>("snapshot");
            if (snap != null)
            {
                snap.LastSeq = e.Seq;      // the reset itself is the newest applied event
                LoadSnapshot(snap);        // raises SnapshotLoaded
                RememberId(e.EventId);     // LoadSnapshot cleared the dedup memory
            }
            else
            {
                ResyncNeeded?.Invoke(e.Seq, e.Seq);
            }
        }

        void ApplyHandoffCreated(EventEnvelopeV2 e)
        {
            var id = e.HandoffId ?? e.PayloadString("handoff_id");
            if (string.IsNullOrEmpty(id)) return;
            if (_handoffs.ContainsKey(id)) return;   // deduped: never two couriers for one handoff
            var h = new HandoffV2
            {
                HandoffId = id,
                FromAgentId = e.PayloadString("from_agent_id"),
                ToAgentId = e.PayloadString("to_agent_id"),
                TaskId = e.TaskId ?? e.PayloadString("task_id"),
                ArtifactId = e.ArtifactId ?? e.PayloadString("artifact_id"),
                Kind = e.PayloadString("kind"),
                State = HandoffStates.Created,
                SourceMode = e.PayloadString("source_mode", SourceMode),
            };
            _handoffs[id] = h;
            HandoffCreated?.Invoke(h);
        }

        void ApplyHandoffState(EventEnvelopeV2 e, string state)
        {
            var id = e.HandoffId ?? e.PayloadString("handoff_id");
            if (string.IsNullOrEmpty(id)) return;
            if (!_handoffs.TryGetValue(id, out var h))
            {
                h = new HandoffV2 { HandoffId = id, TaskId = e.TaskId, ArtifactId = e.ArtifactId,
                    SourceMode = e.PayloadString("source_mode", SourceMode) };
                _handoffs[id] = h;
            }
            h.State = state;
            HandoffChanged?.Invoke(h);
        }

        void ApplyArtifactCreated(EventEnvelopeV2 e)
        {
            var id = e.ArtifactId ?? e.PayloadString("artifact_id");
            if (string.IsNullOrEmpty(id)) return;
            if (_artifacts.ContainsKey(id)) return;
            var a = new ArtifactRefV2
            {
                ArtifactId = id,
                Kind = e.PayloadString("kind", e.PayloadString("artifact_type")),
                SourceMode = e.PayloadString("source_mode", SourceMode),
            };
            _artifacts[id] = a;
            ArtifactAdded?.Invoke(a);
        }

        void ApplyFocusChanged(EventEnvelopeV2 e)
        {
            Focus = new FocusSnapshotV2
            {
                Mode = e.PayloadString("mode", Focus?.Mode ?? FocusModes.Normal),
                Revision = e.TryPayloadInt("revision", out var r) ? r : (Focus?.Revision ?? 0),
                Until = e.PayloadString("until", Focus?.Until),
            };
            FocusChanged?.Invoke(Focus);
        }

        void ApplyInboxUpdated(EventEnvelopeV2 e)
        {
            var item = e.PayloadValue<InboxItemV2>("item");
            if (item?.ItemId != null) _inbox[item.ItemId] = item;
            InboxChanged?.Invoke();
        }

        void RememberId(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_seenEventIds.Add(id))
            {
                _seenOrder.Enqueue(id);
                if (_seenOrder.Count > MaxSeenIds) _seenEventIds.Remove(_seenOrder.Dequeue());
            }
        }
    }
}
