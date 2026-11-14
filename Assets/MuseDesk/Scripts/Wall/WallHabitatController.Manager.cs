using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// The Manager: ask for a plan, review it on a near-user plan card (proposed items are NOT tasks), tick the
    /// steps, confirm risky ones on a separate step, approve. Only then the backend creates real board cards
    /// (atomic, idempotent, revision-checked) and the printer on the Manager deck prints a card for each
    /// canonical task id that has reached the board. The Manager never approves anything itself.
    /// </summary>
    public partial class WallHabitatController
    {
        ManagerPlanFlow _manager;
        WallCard _planCard;
        bool _planCardOpen;
        bool _riskStep;

        public ManagerPlanFlow Manager => _manager;
        public WallCard PlanCard => _planCard;

        static readonly string[] Objectives =
        {
            "Build the login flow, use the existing repo, and have Tester verify it",
            "Find and fix the failing tests",
            "Write setup docs for the project",
        };
        static readonly string[] ObjectiveKeys = { "Login flow", "Fix tests", "Write docs" };

        public void PressManager()
        {
            if (!flags.wallManagerEnabled || !WallActive) return;
            EnsurePlanCard();
            _planCardOpen = !_planCardOpen;
            if (_planCardOpen) PlaceCards();
            _planCard.Show(_planCardOpen);
        }

        void EnsurePlanCard()
        {
            if (_planCard != null || nearUserRig == null) return;
            _planCard = new WallCard(nearUserRig, "WallPlanCard", keyTemplate, font, ArtHelper(), new Vector2(0.17f, 0.15f), keyScale);
        }

        /// <summary>Ask the Manager for a plan (a preset objective, voice, or a driver).</summary>
        public bool RequestPlan(string objective)
        {
            if (!flags.wallManagerEnabled || _backend == null) return false;
            string rid, key;
            if (!_manager.Request(objective, out rid, out key)) { Note(_manager.Message); return false; }
            _riskStep = false;
            _backend.ProposePlan(rid, objective, key, r =>
            {
                if (_manager.RequestId != rid) return;            // a newer request replaced it
                if (r == null || !r.ok) { _manager.OnProposeFailed(r == null ? "no reply" : (r.error ?? r.errorCode)); return; }
                _manager.OnProposed(r.planId, r.revision, r.title, r.provenance, r.items);
            });
            return true;
        }

        public void ToggleItem(int index)
        {
            if (index < 0 || index >= _manager.Items.Count) return;
            var it = _manager.Items[index];
            _manager.SetApproved(it.clientItemId, !it.approved);
        }

        /// <summary>The separate confirmation for every ticked risky item (shown only when one is ticked).</summary>
        public void ConfirmRisky()
        {
            foreach (var it in _manager.Items) if (it.dangerous && it.approved) _manager.Authorize(it.clientItemId);
            _riskStep = false;
        }

        public bool ApprovePlan()
        {
            List<string> approved, authorized; int rev; string key, problem;
            if (!_manager.TryBuildCommit(out approved, out authorized, out rev, out key, out problem))
            {
                Note(problem);
                foreach (var it in _manager.Items) if (it.dangerous && it.approved && !it.authorized) _riskStep = true;
                return false;
            }
            string planId = _manager.PlanId;
            _manager.BeginCommit();
            _backend.CommitPlan(planId, rev, approved, authorized, key, r =>
            {
                if (_manager.PlanId != planId) return;
                if (r != null && r.ok) _manager.OnCommitted(r.revision, r.tasks);
                else _manager.OnCommitFailed(r != null ? r.code : "NO_REPLY", r != null ? r.message : "No reply");
            });
            return true;
        }

        public void RejectPlan()
        {
            if (_manager.State != ManagerFlowState.AwaitingApproval) return;
            string planId = _manager.PlanId; int rev = _manager.Revision;
            _manager.Reject();
            if (_backend != null) _backend.RejectPlan(planId, rev, "Rejected on the plan card", "mgr-reject-" + planId + "-r" + rev, (ok, msg) => { });
        }

        void TickManager(float dt)
        {
            // Printer: one card per canonical task id that is both committed and on the board.
            ManagerPrintJob job;
            while (_view != null && WallActive && _manager.TryDequeuePrint(out job))
            {
                HabitatRole owner;
                if (!HabitatStage.TryRole(job.agentId, out owner) || owner == HabitatRole.Manager) owner = HabitatRole.Developer;
                _view.PrintCard(owner);
                Debug.Log("[MuseDesk.Wall] Printed card for task " + job.taskId + " -> " + job.agentId);
            }
            if (_planCard == null || !_planCard.Visible) return;
            RefreshPlanCard();
        }

        void OnBoardTask(string taskId) { _manager.OnBoardTask(taskId); }

        void RefreshPlanCard()
        {
            var keys = new List<KeyValuePair<string, Action>>();
            var sb = new StringBuilder();
            string title = "Manager";
            string foot = _tasks != null ? _tasks.SourceModeLabel : "";
            switch (_manager.State)
            {
                case ManagerFlowState.Idle:
                case ManagerFlowState.Rejected:
                case ManagerFlowState.Failed:
                case ManagerFlowState.Monitoring:
                    title = "Manager: what should the team do?";
                    if (!string.IsNullOrEmpty(_manager.Message)) sb.AppendLine(_manager.Message);
                    sb.Append("Pick a goal. You will see the plan before anything is created.");
                    for (int i = 0; i < ObjectiveKeys.Length; i++) { int oi = i; keys.Add(new KeyValuePair<string, Action>(ObjectiveKeys[i], () => RequestPlan(Objectives[oi]))); }
                    keys.Add(new KeyValuePair<string, Action>("Close", PressManager));
                    break;
                case ManagerFlowState.Proposing:
                    title = "Manager is drafting a plan";
                    sb.Append(_manager.Objective);
                    keys.Add(new KeyValuePair<string, Action>("Close", PressManager));
                    break;
                case ManagerFlowState.AwaitingApproval:
                    title = "Plan: " + _manager.Title + " (rev " + _manager.Revision + ")";
                    for (int i = 0; i < _manager.Items.Count && i < 4; i++)
                    {
                        var it = _manager.Items[i];
                        sb.Append(it.approved ? "[x] " : "[ ] ").Append(i + 1).Append(". ").Append(it.title).Append(" — ").Append(AgentPresentationRegistry.Pretty(it.proposedAgent));
                        if (it.dependsOn.Length > 0) sb.Append(" (after ").Append(string.Join(",", it.dependsOn)).Append(")");
                        if (it.dangerous) sb.Append(it.authorized ? "  RISK OK'd" : "  RISK: " + string.Join(", ", it.dangerReasons));
                        sb.AppendLine();
                    }
                    sb.Append(_manager.Message);
                    foot = ProvenanceWords(_manager.Provenance) + " · proposed steps are not tasks yet";
                    for (int i = 0; i < _manager.Items.Count && i < 4; i++) { int ii = i; keys.Add(new KeyValuePair<string, Action>("Step " + (i + 1), () => ToggleItem(ii))); }
                    while (keys.Count < 4) keys.Add(new KeyValuePair<string, Action>(null, null));
                    keys.Add(new KeyValuePair<string, Action>(_riskStep ? "Confirm risk" : null, ConfirmRisky));
                    keys.Add(new KeyValuePair<string, Action>("Approve", () => ApprovePlan()));
                    keys.Add(new KeyValuePair<string, Action>("Reject", RejectPlan));
                    keys.Add(new KeyValuePair<string, Action>("Close", PressManager));
                    break;
                case ManagerFlowState.Committing:
                    title = "Creating the approved tasks";
                    sb.Append("Waiting for the board to confirm.");
                    break;
                case ManagerFlowState.Delegating:
                    title = "Delegating";
                    sb.Append(_manager.Message).Append("\nPrinted ").Append(_manager.PrintedCount).Append(" of ").Append(_manager.CommittedCount).Append(" cards.");
                    sb.Append("\nThey are real board cards; they run only when the scheduler allows.");
                    keys.Add(new KeyValuePair<string, Action>("Close", PressManager));
                    break;
            }
            if (_manager.State == ManagerFlowState.Monitoring && _manager.CommittedCount > 0)
                sb.Insert(0, _manager.CommittedCount + " task(s) on the board from the last plan.\n");
            _planCard.SetText(title, sb.ToString(), foot);
            _planCard.SetKeys(keys);
        }
    }
}
