using System;
using System.Collections.Generic;
using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;
using MuseDesk.Phase2;
using MuseDesk.Phase2.Contracts;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// Read-only lookup of "what is this agent doing" from the existing reducers. Shared by the event bridge and the
    /// offline side answers, so both say the same thing the board says.
    /// </summary>
    public static class WallTaskLookup
    {
        static int Rank(string state)
        {
            switch (state)
            {
                case TaskStates.Running: return 5;
                case TaskStates.AwaitingApproval: return 4;
                case TaskStates.PauseRequested:
                case TaskStates.Paused: return 3;
                case TaskStates.Queued: return 2;
                case TaskStates.Blocked: return 1;
                default: return 0;      // terminal or unknown: never "current"
            }
        }

        /// <summary>
        /// taskId (optional) names a card on the v2 board; else the agent's best non-terminal card (running first,
        /// then awaiting approval, paused, queued/assigned, blocked; higher priority wins ties); else the v1
        /// session's current task for that agent. Nothing is made up: false when there is none.
        /// </summary>
        public static bool TryFind(Phase2Reducer p2, SessionReducer v1, string agentId, string taskId,
                                   out string foundTaskId, out string title, out string state, out int revision)
        {
            foundTaskId = title = state = null; revision = 0;
            if (string.IsNullOrEmpty(agentId) || agentId == "manager") return false;

            if (p2 != null)
            {
                if (!string.IsNullOrEmpty(taskId))
                {
                    TaskCardV2 named = p2.GetTask(taskId);
                    if (named != null && (string.IsNullOrEmpty(named.AgentId) || named.AgentId == agentId))
                    { Fill(named, out foundTaskId, out title, out state, out revision); return true; }
                }
                TaskCardV2 best = null; int bestRank = 0;
                foreach (var kv in p2.Tasks)
                {
                    TaskCardV2 c = kv.Value;
                    if (c == null || c.AgentId != agentId) continue;
                    int r = Rank(c.TaskState);
                    if (r == 0) continue;
                    if (best == null || r > bestRank || (r == bestRank && c.Priority > best.Priority)) { best = c; bestRank = r; }
                }
                if (best != null) { Fill(best, out foundTaskId, out title, out state, out revision); return true; }
            }

            if (v1 != null && v1.Snapshot != null && v1.Snapshot.Tasks != null)
            {
                TaskView bestV = null; int bestRankV = 0;
                var list = v1.Snapshot.Tasks;
                for (int i = 0; i < list.Count; i++)
                {
                    TaskView t = list[i];
                    if (t == null || t.AgentId != agentId) continue;
                    int r = Rank(string.IsNullOrEmpty(t.Status) ? TaskStates.Queued : t.Status);
                    if (r == 0) continue;
                    if (bestV == null || r > bestRankV) { bestV = t; bestRankV = r; }
                }
                if (bestV != null)
                {
                    foundTaskId = bestV.TaskId;
                    title = !string.IsNullOrEmpty(bestV.Template) ? bestV.Template : bestV.TaskId;   // the session carries no title
                    state = string.IsNullOrEmpty(bestV.Status) ? TaskStates.Queued : bestV.Status;
                    revision = 0;
                    return true;
                }
            }
            return false;
        }

        static void Fill(TaskCardV2 c, out string id, out string title, out string state, out int revision)
        {
            id = c.TaskId; title = c.Title; state = c.TaskState; revision = c.Revision;
        }
    }

    /// <summary>
    /// Translates REAL reducer events into HabitatEvents and tells the wall about board task creation and resets.
    /// Sources: v1 SessionReducer.Applied (via SessionController.Reducer) and v2 Phase2Reducer.Applied/SnapshotLoaded
    /// (via Phase2Bootstrap.Reducer, a single instance that survives the demo to live switch). Binds lazily and
    /// unbinds on disable. Nothing is synthesised: no reducer event, no HabitatEvent.
    /// </summary>
    [DisallowMultipleComponent]
    public class WallEventBridge : MonoBehaviour, IWallEventSource, IWallTaskContext
    {
        [Header("Wiring (found once if empty)")]
        public SessionController session;
        public Phase2Bootstrap phase2;

        public event Action<HabitatEvent> HabitatEventRaised;
        public event Action<string> BoardTaskCreated;
        public event Action SessionReset;
        public event Action<PlanCardUpdate> PlanCardReported;
        public event Action<string, string, string> CompletionArtifactCreated;
        public event Action<string, int, List<ManagerPrintJob>> PlanCommittedAnnounced;
        public event Action<string, string> SystemWarning;

        const string PlanCommittedType = "manager.plan_committed";
        System.Threading.CancellationTokenSource _cts;

        const string Tag = "[MuseDesk.Wall] ";
        const int SemanticMemory = 256;

        SessionReducer _v1;
        Phase2Reducer _p2;
        bool _awaitInitialSnapshot;
        float _poll;
        bool _searched;
        int _lastResetFrame = -1;
        readonly HashSet<string> _knownTasks = new HashSet<string>();
        readonly HashSet<string> _semantic = new HashSet<string>();
        readonly Queue<string> _semanticOrder = new Queue<string>();

        public string SourceModeLabel
        {
            get
            {
                if (phase2 != null && phase2.Gateway != null) return phase2.Gateway.IsLive ? "LIVE" : "SCRIPTED DEMO";
                return session != null && session.IsLive ? "LIVE" : "SCRIPTED DEMO";
            }
        }

        public bool TryGetAgentTask(string agentId, out string taskId, out string title, out string state, out int revision)
        {
            return WallTaskLookup.TryFind(phase2 != null ? phase2.Reducer : null, session != null ? session.Reducer : null,
                                          agentId, null, out taskId, out title, out state, out revision);
        }

        // ------------------------------------------------------------------ binding

        void OnEnable() { FindRefsOnce(); TryBind(); }

        void OnDisable() { Unbind(); }

        void OnDestroy()
        {
            try { if (_cts != null) _cts.Cancel(); } catch { }
            if (_cts != null) _cts.Dispose();
        }

        // ------------------------------------------------------------------ artifacts (read-only)

        public void FetchArtifactText(string artifactId, Action<string, string> done)
        {
            if (done == null) return;
            IPhase2Gateway gw = phase2 != null ? phase2.Gateway : null;
            if (gw == null || string.IsNullOrEmpty(artifactId)) { done(null, null); return; }
            _ = FetchArtifactAsync(gw, artifactId, done);
        }

        async System.Threading.Tasks.Task FetchArtifactAsync(IPhase2Gateway gw, string artifactId, Action<string, string> done)
        {
            string title = null, content = null;
            try
            {
                if (_cts == null) _cts = new System.Threading.CancellationTokenSource();
                ArtifactPageResponse page = await gw.GetArtifactPageAsync(artifactId, 0, 1, _cts.Token);
                if (page != null && page.Pages != null && page.Pages.Count > 0 && page.Pages[0] != null)
                {
                    title = page.Pages[0].Title;
                    content = page.Pages[0].Content;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Debug.LogWarning(Tag + "artifact " + artifactId + " could not be read: " + ex.Message); }
            if (this == null) return;
            try { done(title, content); } catch (Exception ex) { Debug.LogError(Tag + "listener error: " + ex.Message); }
        }

        // ------------------------------------------------------------------ plan cards (reasons the events carry)

        void ReportCard(EventEnvelopeV2 e)
        {
            if (PlanCardReported == null || string.IsNullOrEmpty(e.TaskId) || _p2 == null) return;
            TaskCardV2 c = _p2.GetTask(e.TaskId);
            if (c == null) return;
            var u = new PlanCardUpdate { taskId = c.TaskId, state = c.TaskState, revision = c.Revision, agentId = c.AgentId, title = c.Title };
            if (c.Dependencies != null) u.dependsOn = c.Dependencies.ToArray();
            string blocked = e.PayloadString("blocked_reason");
            if (blocked != null)
            {
                u.blockedReason = blocked;
                var by = e.Payload != null ? e.Payload["blocked_by"] as Newtonsoft.Json.Linq.JArray : null;
                var ids = new List<string>();
                if (by != null) foreach (var tok in by) { string s = (string)tok; if (!string.IsNullOrEmpty(s)) ids.Add(s); }
                u.blockedBy = ids.ToArray();
            }
            string summary = e.PayloadString("result_summary");
            if (e.Type == Phase2EventTypes.TaskFailed) u.failureReason = summary ?? "";
            else if (e.Type == Phase2EventTypes.TaskCompleted && summary != null) u.resultSummary = summary;
            Fire(PlanCardReported, u);
        }

        void AnnouncePlanCommitted(EventEnvelopeV2 e)
        {
            if (PlanCommittedAnnounced == null || e.Payload == null) return;
            string planId = e.PayloadString("plan_id");
            var arr = e.Payload["tasks"] as Newtonsoft.Json.Linq.JArray;
            if (string.IsNullOrEmpty(planId) || arr == null) return;
            var jobs = new List<ManagerPrintJob>();
            foreach (var tok in arr)
            {
                var o = tok as Newtonsoft.Json.Linq.JObject;
                if (o == null) continue;
                string taskId = (string)o["task_id"];
                if (string.IsNullOrEmpty(taskId)) continue;                 // never invent an id
                TaskCardV2 c = _p2 != null ? _p2.GetTask(taskId) : null;
                jobs.Add(new ManagerPrintJob { taskId = taskId, clientItemId = (string)o["client_item_id"], agentId = (string)o["agent_id"], title = c != null ? c.Title : null });
            }
            int rev;
            e.TryPayloadInt("revision", out rev);
            if (jobs.Count == 0) return;
            var h = PlanCommittedAnnounced;
            try { h(planId, rev, jobs); } catch (Exception ex) { Debug.LogError(Tag + "listener error: " + ex.Message); }
        }

        void FindRefsOnce()
        {
            if (_searched) return;
            _searched = true;
            if (session == null) session = FindAnyObjectByType<SessionController>();
            if (phase2 == null) phase2 = FindAnyObjectByType<Phase2Bootstrap>();
        }

        void Update()
        {
            if (_v1 != null && _p2 != null) return;
            _poll += Time.unscaledDeltaTime;
            if (_poll < 0.5f) return;
            _poll = 0f;
            TryBind();
        }

        void TryBind()
        {
            if (_v1 == null && session != null && session.Reducer != null)
            {
                _v1 = session.Reducer;
                _v1.Applied += OnV1Applied;
            }
            if (_p2 == null && phase2 != null && phase2.Reducer != null)
            {
                _p2 = phase2.Reducer;
                _awaitInitialSnapshot = _p2.SessionId == null;
                _knownTasks.Clear();
                foreach (var kv in _p2.Tasks) _knownTasks.Add(kv.Key);
                _p2.Applied += OnV2Applied;
                _p2.SnapshotLoaded += OnV2SnapshotLoaded;
            }
        }

        void Unbind()
        {
            if (_v1 != null) { _v1.Applied -= OnV1Applied; _v1 = null; }
            if (_p2 != null) { _p2.Applied -= OnV2Applied; _p2.SnapshotLoaded -= OnV2SnapshotLoaded; _p2 = null; }
        }

        // ------------------------------------------------------------------ v1 (SessionReducer)

        void OnV1Applied(EventEnvelope e)
        {
            if (e == null) return;
            string id = !string.IsNullOrEmpty(e.EventId) ? e.EventId : "v1-" + e.Seq;
            switch (e.Type)
            {
                case EventTypes.SessionReset:
                    RaiseReset();
                    break;
                case EventTypes.TaskStarted:
                    EmitTask(id, HabitatEventKind.TaskStarted, e.AgentId, e.TaskId, null);
                    break;
                case EventTypes.TaskCompleted:
                    if (e.PayloadString("task_state", "completed") == TaskStates.Cancelled) return;   // a stopped branch is not a completion
                    EmitTask(id, HabitatEventKind.TaskCompleted, e.AgentId, e.TaskId, null);
                    break;
                case EventTypes.TaskFailed:
                    EmitTask(id, HabitatEventKind.TaskFailed, e.AgentId, e.TaskId, null);
                    break;
                case EventTypes.DecisionRequested:
                    EmitTask(id, HabitatEventKind.ApprovalRequested, e.AgentId, e.TaskId, e.DecisionId);
                    break;
            }
        }

        // ------------------------------------------------------------------ v2 (Phase2Reducer)

        void OnV2Applied(EventEnvelopeV2 e)
        {
            if (e == null) return;
            string id = !string.IsNullOrEmpty(e.EventId) ? e.EventId : "v2-" + e.Seq;
            if (e.Type != null && e.Type.StartsWith("task.")) ReportCard(e);
            switch (e.Type)
            {
                case Phase2EventTypes.SessionReset:
                    RaiseReset();
                    break;
                case PlanCommittedType:
                    AnnouncePlanCommitted(e);
                    break;
                case Phase2EventTypes.ArtifactCreated:
                {
                    string kind = e.PayloadString("kind", e.PayloadString("artifact_type"));
                    string artifactId = e.ArtifactId ?? e.PayloadString("artifact_id");
                    string taskId = e.TaskId ?? e.PayloadString("task_id");
                    var h = CompletionArtifactCreated;
                    if (h != null && kind == ArtifactKinds.Completion && !string.IsNullOrEmpty(artifactId))
                    {
                        try { h(artifactId, taskId, e.PayloadString("summary", "")); } catch (Exception ex) { Debug.LogError(Tag + "listener error: " + ex.Message); }
                    }
                    break;
                }
                case Phase2EventTypes.SystemWarning:
                {
                    var h = SystemWarning;
                    if (h != null)
                    {
                        try { h(e.PayloadString("code", ""), e.PayloadString("message", "")); } catch (Exception ex) { Debug.LogError(Tag + "listener error: " + ex.Message); }
                    }
                    break;
                }
                case Phase2EventTypes.TaskCreated:
                    if (!string.IsNullOrEmpty(e.TaskId))
                    {
                        _knownTasks.Add(e.TaskId);
                        Fire(BoardTaskCreated, e.TaskId);
                    }
                    // Plan cards arrive with agent_id set at creation and no task.assigned event.
                    if (!string.IsNullOrEmpty(e.AgentId)) EmitTask(id, HabitatEventKind.TaskAssigned, e.AgentId, e.TaskId, null, false);
                    break;
                case Phase2EventTypes.TaskAssigned:
                    EmitTask(id, HabitatEventKind.TaskAssigned, e.AgentId ?? e.PayloadString("target_agent_id"), e.TaskId, null, false);
                    break;
                case Phase2EventTypes.TaskStarted:
                    EmitTask(id, HabitatEventKind.TaskStarted, V2Agent(e), e.TaskId, null);
                    break;
                case Phase2EventTypes.TaskCompleted:
                    if (e.PayloadString("task_state", TaskStates.Completed) == TaskStates.Cancelled) return;
                    EmitTask(id, HabitatEventKind.TaskCompleted, V2Agent(e), e.TaskId, null);
                    break;
                case Phase2EventTypes.TaskFailed:
                    EmitTask(id, HabitatEventKind.TaskFailed, V2Agent(e), e.TaskId, null);
                    break;
                case Phase2EventTypes.DecisionRequested:
                    EmitTask(id, HabitatEventKind.ApprovalRequested, V2Agent(e), e.TaskId, e.PayloadString("decision_id"));
                    break;
                case Phase2EventTypes.HandoffCreated:
                    EmitHandoff(id, e);
                    break;
            }
        }

        string V2Agent(EventEnvelopeV2 e)
        {
            if (!string.IsNullOrEmpty(e.AgentId)) return e.AgentId;
            string a = e.PayloadString("agent_id");
            if (!string.IsNullOrEmpty(a)) return a;
            TaskCardV2 c = _p2 != null && !string.IsNullOrEmpty(e.TaskId) ? _p2.GetTask(e.TaskId) : null;
            return c != null ? c.AgentId : null;
        }

        void OnV2SnapshotLoaded()
        {
            bool initial = _awaitInitialSnapshot;
            _awaitInitialSnapshot = false;
            // New cards that arrived inside a snapshot (reconcile, live switch) were never announced as task.created.
            var current = new HashSet<string>();
            foreach (var kv in _p2.Tasks)
            {
                current.Add(kv.Key);
                if (!initial && _knownTasks.Add(kv.Key)) Fire(BoardTaskCreated, kv.Key);
            }
            _knownTasks.Clear();
            foreach (var t in current) _knownTasks.Add(t);
            if (!initial) RaiseReset();      // the picture the habitat was showing may be stale: everyone goes home
        }

        // ------------------------------------------------------------------ emit

        void EmitTask(string eventId, HabitatEventKind kind, string agentId, string taskId, string decisionId, bool crossSourceDedupe = true)
        {
            HabitatRole role;
            if (!HabitatStage.TryRole(agentId, out role)) return;          // unknown agent ids are skipped
            // The v1 session and the v2 stream both carry some of the same real events under different ids.
            if (crossSourceDedupe)
            {
                string key = (int)kind + "|" + (!string.IsNullOrEmpty(decisionId) ? decisionId : taskId) + "|" + role;
                if (!RememberSemantic(key)) return;
            }
            Fire(HabitatEventRaised, HabitatEvent.Of(eventId, kind, role, taskId));
        }

        void EmitHandoff(string eventId, EventEnvelopeV2 e)
        {
            HabitatRole from, to;
            if (!HabitatStage.TryRole(e.PayloadString("from_agent_id"), out from)) return;
            if (!HabitatStage.TryRole(e.PayloadString("to_agent_id"), out to)) return;
            Fire(HabitatEventRaised, HabitatEvent.HandoffOf(eventId, from, to, e.TaskId));
        }

        bool RememberSemantic(string key)
        {
            if (!_semantic.Add(key)) return false;
            _semanticOrder.Enqueue(key);
            while (_semanticOrder.Count > SemanticMemory) _semantic.Remove(_semanticOrder.Dequeue());
            return true;
        }

        void RaiseReset()
        {
            if (_lastResetFrame == Time.frameCount) return;      // v1 + v2 both report one reset in the same frame
            _lastResetFrame = Time.frameCount;
            _semantic.Clear();
            _semanticOrder.Clear();
            Fire(SessionReset);
            Fire(HabitatEventRaised, new HabitatEvent { kind = HabitatEventKind.Reset, eventId = null });
        }

        static void Fire(Action a)
        {
            if (a == null) return;
            try { a(); } catch (Exception ex) { Debug.LogError(Tag + "listener error: " + ex.Message); }
        }

        static void Fire<T>(Action<T> a, T v)
        {
            if (a == null) return;
            try { a(v); } catch (Exception ex) { Debug.LogError(Tag + "listener error: " + ex.Message); }
        }
    }
}
