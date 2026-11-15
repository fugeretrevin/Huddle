using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    public enum ManagerFlowState { Idle = 0, Proposing = 1, AwaitingApproval = 2, Committing = 3, Delegating = 4, Monitoring = 5, Rejected = 6, Failed = 7, Revising = 8 }

    /// <summary>One proposed item as the plan card shows it. Mirrors the backend ManagerPlan item.</summary>
    public sealed class ManagerPlanItemView
    {
        public string clientItemId;
        public string title;
        public string kind;
        public string proposedAgent;
        public string[] dependsOn = new string[0];
        public string[] acceptanceCriteria = new string[0];
        public string requiredPermission;
        public bool dangerous;
        public string[] dangerReasons = new string[0];
        /// <summary>Ticked by the user. Proposed items start ticked except dangerous ones.</summary>
        public bool approved;
        /// <summary>The user confirmed the separate "this is dangerous" step.</summary>
        public bool authorized;
    }

    /// <summary>A printed card the habitat may show: only for a canonical task id that the commit returned AND the board has.</summary>
    public struct ManagerPrintJob
    {
        public string taskId;
        public string clientItemId;
        public string agentId;
        public string title;
    }

    /// <summary>
    /// Client side of the Manager: request -> proposed plan card -> the user ticks items (dangerous ones need a
    /// second explicit confirmation) -> commit with revision + idempotency key -> canonical task ids. The
    /// printer shows a card ONLY for a task id that is both in the commit result and seen on the real board
    /// (task.created reached the reducer). A proposed plan never produces a card. The manager never approves.
    /// </summary>
    public sealed class ManagerPlanFlow
    {
        readonly List<ManagerPlanItemView> _items = new List<ManagerPlanItemView>();
        readonly Dictionary<string, ManagerPrintJob> _committed = new Dictionary<string, ManagerPrintJob>();  // taskId -> job
        readonly HashSet<string> _seenOnBoard = new HashSet<string>();
        readonly HashSet<string> _printed = new HashSet<string>();
        readonly Queue<ManagerPrintJob> _print = new Queue<ManagerPrintJob>();
        readonly List<string> _assumptions = new List<string>();
        readonly List<string> _risks = new List<string>();
        int _requestCounter;
        string _commitKey, _reviseKey;

        /// <summary>The plan's own objective, assumptions and risks, exactly as the backend sent them (may be empty).</summary>
        public string PlanObjective { get; private set; }
        public IList<string> Assumptions => _assumptions;
        public IList<string> Risks => _risks;
        /// <summary>The plan id the committed cards belong to (kept after a new request clears <see cref="PlanId"/>).</summary>
        public string CommittedPlanId { get; private set; }
        /// <summary>Raised once per commit with the canonical cards the backend created.</summary>
        public event Action<string, IList<ManagerPrintJob>> Committed;

        public ManagerFlowState State { get; private set; }
        public string RequestId { get; private set; }
        public string Objective { get; private set; }
        public string PlanId { get; private set; }
        public int Revision { get; private set; }
        public string Title { get; private set; }
        public string Provenance { get; private set; }
        public string Message { get; private set; }
        public IList<ManagerPlanItemView> Items => _items;
        public int CommittedCount => _committed.Count;
        public int PrintedCount => _printed.Count;
        public event Action<ManagerFlowState> StateChanged;

        public bool Request(string objective, out string requestId, out string idempotencyKey)
        {
            requestId = idempotencyKey = null;
            if (State == ManagerFlowState.Proposing || State == ManagerFlowState.Committing || State == ManagerFlowState.Revising) { Message = "The manager is busy with the last request"; return false; }
            if (string.IsNullOrEmpty(objective) || objective.Trim().Length < 3) { Message = "Say what you want done"; return false; }
            _requestCounter++;
            Objective = objective.Trim();
            RequestId = "mreq-" + DateTime.UtcNow.Ticks.ToString("x") + "-" + _requestCounter;
            requestId = RequestId;
            idempotencyKey = "mgr-propose-" + RequestId;
            _items.Clear();
            PlanId = null; Revision = 0; Title = null; Provenance = null; _commitKey = null; _reviseKey = null;
            PlanObjective = null; _assumptions.Clear(); _risks.Clear();
            Go(ManagerFlowState.Proposing);
            Message = "The manager is drafting a plan";
            return true;
        }

        /// <summary>The proposed plan arrived. Items start approved except dangerous ones (they need an explicit tick and a confirm).</summary>
        public void OnProposed(string planId, int revision, string title, string provenance, IList<ManagerPlanItemView> items)
        {
            if (State != ManagerFlowState.Proposing && State != ManagerFlowState.AwaitingApproval && State != ManagerFlowState.Revising) return;
            _reviseKey = null;
            PlanId = planId; Revision = revision; Title = title; Provenance = provenance;
            _items.Clear();
            if (items != null)
                foreach (var it in items)
                {
                    it.approved = !it.dangerous;
                    it.authorized = false;
                    _items.Add(it);
                }
            _commitKey = null;
            Go(ManagerFlowState.AwaitingApproval);
            Message = provenance == "TEMPLATE_PLAN" ? "Template plan (no AI model configured). Review before approving."
                    : provenance == "SCRIPTED_DEMO" ? "SCRIPTED DEMO plan. Review before approving."
                    : "Review the plan. Nothing runs until you approve.";
        }

        /// <summary>The plan's descriptive fields (objective, assumptions, risks). Only what the backend sent.</summary>
        public void SetPlanNotes(string planObjective, IList<string> assumptions, IList<string> risks)
        {
            PlanObjective = planObjective;
            _assumptions.Clear(); _risks.Clear();
            if (assumptions != null) foreach (string s in assumptions) if (!string.IsNullOrEmpty(s)) _assumptions.Add(s);
            if (risks != null) foreach (string s in risks) if (!string.IsNullOrEmpty(s)) _risks.Add(s);
        }

        // ── revise: drop the unticked steps and get a new revision to review ──

        /// <summary>
        /// Build a revision that keeps only the ticked steps. False with a reason when there is nothing to drop, nothing
        /// would be left, or a kept step depends on a dropped one. The key is stable for the same selection.
        /// </summary>
        public bool TryBuildRevision(out List<ManagerPlanItemView> kept, out int revision, out string idempotencyKey, out string problem)
        {
            kept = new List<ManagerPlanItemView>(); revision = Revision; idempotencyKey = null; problem = null;
            if (State != ManagerFlowState.AwaitingApproval) { problem = "No plan waiting for review"; return false; }
            foreach (var it in _items) if (it.approved) kept.Add(it);
            if (kept.Count == _items.Count) { problem = "Untick the steps you want removed, then press Revise"; return false; }
            if (kept.Count == 0) { problem = "A plan needs at least one step"; return false; }
            foreach (var it in kept)
                foreach (var d in it.dependsOn)
                {
                    var dep = Find(d);
                    if (dep == null || !dep.approved) { problem = "\"" + it.title + "\" needs \"" + (dep != null ? dep.title : d) + "\": keep both or drop both"; return false; }
                }
            var ids = new List<string>();
            foreach (var it in kept) ids.Add(it.clientItemId);
            ids.Sort(StringComparer.Ordinal);
            if (_reviseKey == null) _reviseKey = "mgr-revise-" + PlanId + "-r" + Revision + "-" + StableHash(string.Join(",", ids));
            idempotencyKey = _reviseKey;
            return true;
        }

        public void BeginRevise()
        {
            if (State != ManagerFlowState.AwaitingApproval) return;
            Go(ManagerFlowState.Revising);
            Message = "Revising the plan";
        }

        /// <summary>The revision was refused or never answered: the same plan stays on the paper. Nothing changed.</summary>
        public void OnReviseFailed(string code, string message)
        {
            if (State != ManagerFlowState.Revising) return;
            if (code != "UNREACHABLE") _reviseKey = null;        // a definitive refusal: the next try is a new command
            Go(ManagerFlowState.AwaitingApproval);
            Message = "Not revised: " + (string.IsNullOrEmpty(message) ? code : message);
        }

        public void OnProposeFailed(string reason)
        {
            if (State != ManagerFlowState.Proposing) return;
            Message = "No plan: " + reason;
            Go(ManagerFlowState.Failed);
        }

        ManagerPlanItemView Find(string id)
        {
            for (int i = 0; i < _items.Count; i++) if (_items[i].clientItemId == id) return _items[i];
            return null;
        }

        public void SetApproved(string clientItemId, bool on)
        {
            if (State != ManagerFlowState.AwaitingApproval) return;
            var it = Find(clientItemId);
            if (it == null) return;
            it.approved = on;
            if (!on) it.authorized = false;
            _commitKey = null; _reviseKey = null;
        }

        /// <summary>The separate, explicit confirmation for a dangerous item.</summary>
        public void Authorize(string clientItemId)
        {
            if (State != ManagerFlowState.AwaitingApproval) return;
            var it = Find(clientItemId);
            if (it == null || !it.dangerous || !it.approved) return;
            it.authorized = true;
            _commitKey = null;
        }

        /// <summary>
        /// Build the commit request for the plan revision the user saw. False with a reason when the selection
        /// cannot be committed (nothing ticked, a ticked item depends on an unticked one, a dangerous item
        /// not confirmed). The idempotency key is stable for the same selection, so a retry is safe.
        /// </summary>
        public bool TryBuildCommit(out List<string> approved, out List<string> authorized, out int revision, out string idempotencyKey, out string problem)
        {
            approved = new List<string>(); authorized = new List<string>(); revision = Revision; idempotencyKey = null; problem = null;
            if (State != ManagerFlowState.AwaitingApproval) { problem = "No plan waiting for approval"; return false; }
            foreach (var it in _items) if (it.approved) approved.Add(it.clientItemId);
            if (approved.Count == 0) { problem = "Tick at least one step"; return false; }
            foreach (var it in _items)
            {
                if (!it.approved) continue;
                foreach (var d in it.dependsOn)
                {
                    var dep = Find(d);
                    if (dep == null || !dep.approved) { problem = "\"" + it.title + "\" needs \"" + (dep != null ? dep.title : d) + "\" too"; return false; }
                }
                if (it.dangerous)
                {
                    if (!it.authorized) { problem = "\"" + it.title + "\" needs your explicit OK (" + string.Join(", ", it.dangerReasons) + ")"; return false; }
                    authorized.Add(it.clientItemId);
                }
            }
            approved.Sort(StringComparer.Ordinal);
            authorized.Sort(StringComparer.Ordinal);
            if (_commitKey == null) _commitKey = "mgr-commit-" + PlanId + "-r" + Revision + "-" + StableHash(string.Join(",", approved) + "|" + string.Join(",", authorized));
            idempotencyKey = _commitKey;
            return true;
        }

        public void BeginCommit()
        {
            if (State != ManagerFlowState.AwaitingApproval) return;
            Go(ManagerFlowState.Committing);
            Message = "Creating the approved tasks";
        }

        /// <summary>The backend created the cards. Only these canonical ids may ever be printed.</summary>
        public void OnCommitted(int revision, IList<ManagerPrintJob> tasks)
        {
            if (State != ManagerFlowState.Committing && State != ManagerFlowState.AwaitingApproval) return;
            Revision = revision;
            CommittedPlanId = PlanId;
            if (tasks != null)
                foreach (var t in tasks)
                {
                    if (string.IsNullOrEmpty(t.taskId)) continue;
                    var job = t;
                    if (string.IsNullOrEmpty(job.title)) { var it = Find(t.clientItemId); if (it != null) job.title = it.title; }
                    _committed[t.taskId] = job;
                    TryQueuePrint(t.taskId);
                }
            Go(ManagerFlowState.Delegating);
            Message = _committed.Count + " task" + (_committed.Count == 1 ? "" : "s") + " added to the board";
            var done = Committed;
            if (done != null && tasks != null) done(CommittedPlanId, tasks);
        }

        /// <summary>
        /// The commit was refused. Revision conflict / plan not open: the card is stale (show it, ask to
        /// re-plan). Authorization or dependency problems go back to the approval step. Nothing was created.
        /// </summary>
        public void OnCommitFailed(string code, string message)
        {
            if (State != ManagerFlowState.Committing) return;
            Message = "Not created: " + (string.IsNullOrEmpty(message) ? code : message);
            if (code == "AUTHORIZATION_REQUIRED" || code == "DEPENDENCY_NOT_APPROVED" || code == "IDEMPOTENCY_KEY_REUSE") { _commitKey = null; Go(ManagerFlowState.AwaitingApproval); }
            else Go(ManagerFlowState.Failed);
        }

        public void Reject()
        {
            if (State != ManagerFlowState.AwaitingApproval) return;
            Message = "Plan rejected. Nothing was created.";
            Go(ManagerFlowState.Rejected);
        }

        /// <summary>The board reducer saw task.created for this id (any source).</summary>
        public void OnBoardTask(string taskId)
        {
            if (string.IsNullOrEmpty(taskId)) return;
            _seenOnBoard.Add(taskId);
            TryQueuePrint(taskId);
        }

        void TryQueuePrint(string taskId)
        {
            ManagerPrintJob job;
            if (!_committed.TryGetValue(taskId, out job)) return;      // not ours: never print
            if (!_seenOnBoard.Contains(taskId)) return;               // not on the board yet
            if (!_printed.Add(taskId)) return;                         // already printed
            _print.Enqueue(job);
        }

        public bool TryDequeuePrint(out ManagerPrintJob job)
        {
            if (_print.Count > 0) { job = _print.Dequeue(); return true; }
            job = default(ManagerPrintJob);
            if (State == ManagerFlowState.Delegating && _printed.Count >= _committed.Count && _committed.Count > 0) Go(ManagerFlowState.Monitoring);
            return false;
        }

        /// <summary>The backend stream announced the commit (manager.plan_committed). Same effect as the commit reply, whichever
        /// arrives first; the other is then ignored. An announcement for another plan is ignored.</summary>
        public bool OnCommittedEvent(string planId, int revision, IList<ManagerPrintJob> tasks)
        {
            if (string.IsNullOrEmpty(planId) || planId != PlanId) return false;
            if (State != ManagerFlowState.Committing && State != ManagerFlowState.AwaitingApproval) return false;
            OnCommitted(revision, tasks);
            return true;
        }

        public void Reset()
        {
            _items.Clear(); _committed.Clear(); _seenOnBoard.Clear(); _printed.Clear(); _print.Clear();
            _assumptions.Clear(); _risks.Clear(); PlanObjective = null; CommittedPlanId = null; _reviseKey = null;
            PlanId = null; Revision = 0; _commitKey = null; Message = "";
            Go(ManagerFlowState.Idle);
        }

        void Go(ManagerFlowState s)
        {
            if (State == s) return;
            State = s;
            var h = StateChanged; if (h != null) h(s);
        }

        static string StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
                return h.ToString("x8");
            }
        }
    }
}
