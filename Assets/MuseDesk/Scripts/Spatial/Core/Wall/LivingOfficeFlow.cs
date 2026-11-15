using System;
using System.Collections.Generic;
using MuseDesk.Spatial.Ui;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>Where a committed plan card physically is in the habitat. Presentation only.</summary>
    public enum CardDelivery { InTray = 0, Carried = 1, OnDesk = 2 }

    /// <summary>The Manager's summary of a finished plan, as it travels to the user.</summary>
    public enum ReceiptStage { None = 0, Ready = 1, ComingDown = 2, OnDesk = 3 }

    /// <summary>One committed plan card as the wall shows it. Every field is copied from the backend (or the offline demo
    /// board); nothing here is inferred except <see cref="delivery"/>, which only says where the paper card is drawn.</summary>
    public sealed class PlanCardView
    {
        public string planId, taskId, clientItemId, agentId, title;
        public string state = "queued";
        public int revision = -1;
        public string[] dependsOn = new string[0];
        /// <summary>The backend's words for why the card is blocked ("" when it gave none).</summary>
        public string blockedReason = "";
        public string[] blockedBy = new string[0];
        /// <summary>The backend's words for why the card failed.</summary>
        public string failureReason = "";
        public string resultSummary = "";
        public CardDelivery delivery;
        public bool deliveredByChute;
        public bool retryInFlight;
        /// <summary>Last answer to a Retry poke (refusal or transport problem), "" otherwise.</summary>
        public string retryMessage = "";

        public bool Failed => state == "failed";
        public bool Blocked => state == "blocked";
        public bool Terminal => state == "completed" || state == "failed" || state == "cancelled";
        public bool CanRetry => Failed && !retryInFlight;
    }

    /// <summary>A change to a card reported by the backend stream or a status read. Null/-1 fields mean "not in this report".</summary>
    public struct PlanCardUpdate
    {
        public string taskId, state, agentId, title;
        public int revision;                 // -1: not given
        public string blockedReason;         // null: not given
        public string[] blockedBy;
        public string failureReason;
        public string resultSummary;
        public string[] dependsOn;

        public static PlanCardUpdate Of(string taskId, string state, int revision = -1)
        { return new PlanCardUpdate { taskId = taskId, state = state, revision = revision }; }
    }

    public struct PlanReceipt
    {
        public string planId, artifactId, anchorTaskId, summary;
        public ReceiptStage stage;
    }

    /// <summary>
    /// The wall's picture of committed Manager plans: which cards exist, their REAL state and reasons, the Retry
    /// bookkeeping (one in flight per card, one stable idempotency key per failed revision, like the board's
    /// <see cref="CommandLedger"/>) and the summary receipt. Pure logic.
    ///
    /// Rules:
    ///  - a card exists here only after the commit returned its canonical task id (never from a proposed plan);
    ///  - an update for an unknown task id is ignored; an update older than the card's revision is ignored;
    ///  - a blocked reason is shown only while the card is blocked, a failure reason only while it is failed;
    ///  - Retry is offered only for a failed card and is locked while one is in flight; a transport failure keeps the
    ///    same key for the next press, a definitive answer forgets it;
    ///  - the receipt appears once per summary artifact, and only for an artifact anchored on one of these cards.
    /// </summary>
    public sealed class PlanBoard
    {
        readonly List<PlanCardView> _cards = new List<PlanCardView>();
        readonly Dictionary<string, PlanCardView> _byId = new Dictionary<string, PlanCardView>();
        readonly CommandLedger _ledger = new CommandLedger();
        readonly Dictionary<string, string> _retryKeys = new Dictionary<string, string>();   // taskId -> key in flight
        readonly HashSet<string> _receiptsSeen = new HashSet<string>();
        PlanReceipt _receipt;

        public IList<PlanCardView> Cards => _cards;
        public PlanReceipt Receipt => _receipt;
        public int Count => _cards.Count;
        /// <summary>Bumped on every change, so a view can redraw only when something changed.</summary>
        public int Version { get; private set; }

        public PlanCardView Find(string taskId)
        {
            PlanCardView c;
            return taskId != null && _byId.TryGetValue(taskId, out c) ? c : null;
        }

        /// <summary>Register the cards a commit created. Safe to call again with the same ids (commit reply + stream event).</summary>
        public int AddCommitted(string planId, IList<ManagerPrintJob> tasks)
        {
            int added = 0;
            if (tasks == null) return 0;
            for (int i = 0; i < tasks.Count; i++)
            {
                ManagerPrintJob t = tasks[i];
                if (string.IsNullOrEmpty(t.taskId)) continue;
                PlanCardView c;
                if (_byId.TryGetValue(t.taskId, out c))
                {
                    if (string.IsNullOrEmpty(c.title)) c.title = t.title;
                    if (string.IsNullOrEmpty(c.planId)) c.planId = planId;
                    continue;
                }
                c = new PlanCardView { planId = planId, taskId = t.taskId, clientItemId = t.clientItemId, agentId = t.agentId, title = t.title };
                _cards.Add(c);
                _byId[t.taskId] = c;
                added++;
            }
            if (added > 0) Version++;
            return added;
        }

        /// <summary>Apply a real report. False when the task is not one of these cards or the report is stale.</summary>
        public bool Apply(PlanCardUpdate u)
        {
            PlanCardView c = Find(u.taskId);
            if (c == null) return false;
            if (u.revision >= 0 && c.revision >= 0 && u.revision < c.revision) return false;
            if (u.revision >= 0) c.revision = u.revision;
            if (!string.IsNullOrEmpty(u.state)) c.state = u.state;
            if (!string.IsNullOrEmpty(u.agentId)) c.agentId = u.agentId;
            if (!string.IsNullOrEmpty(u.title)) c.title = u.title;
            if (u.dependsOn != null) c.dependsOn = u.dependsOn;
            if (u.blockedReason != null) { c.blockedReason = u.blockedReason; c.blockedBy = u.blockedBy ?? new string[0]; }
            if (u.failureReason != null) c.failureReason = u.failureReason;
            if (u.resultSummary != null) c.resultSummary = u.resultSummary;
            if (!c.Blocked) { c.blockedReason = ""; c.blockedBy = new string[0]; }
            if (!c.Failed) { c.failureReason = ""; if (!c.retryInFlight) c.retryMessage = ""; }
            Version++;
            return true;
        }

        /// <summary>The words for a card's line on the status page: state plus the backend's reason when there is one.</summary>
        public static string StatusWords(PlanCardView c)
        {
            if (c == null) return "";
            if (c.Failed) return "FAILED: " + (string.IsNullOrEmpty(c.failureReason) ? "no reason was reported" : c.failureReason);
            if (c.Blocked) return string.IsNullOrEmpty(c.blockedReason) ? "Blocked (no reason was reported)" : c.blockedReason;
            if (c.state == "completed") return string.IsNullOrEmpty(c.resultSummary) ? "Completed" : "Completed: " + c.resultSummary;
            return WallOfflineLogic.StateWords(c.state);
        }

        // ── retry ──

        /// <summary>Start a Retry for a failed card. False (with a reason) when it is not failed or one is already in flight.</summary>
        public bool TryBeginRetry(string taskId, out string planId, out string idempotencyKey, out string problem)
        {
            planId = idempotencyKey = problem = null;
            PlanCardView c = Find(taskId);
            if (c == null) { problem = "That card is not part of a committed plan"; return false; }
            if (!c.Failed) { problem = "Only a failed card can be retried"; return false; }
            string lockId = "retry|" + taskId;
            if (!_ledger.TryLock(lockId)) { problem = "Retry already sent"; return false; }
            string action = lockId + "|r" + c.revision;
            idempotencyKey = _ledger.KeyFor(action, () => "mgr-retry-" + taskId + "-r" + c.revision + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            _retryKeys[taskId] = idempotencyKey;
            planId = c.planId;
            c.retryInFlight = true;
            c.retryMessage = "";
            Version++;
            return true;
        }

        /// <summary>The Retry came back. <paramref name="definitive"/>: the backend answered (accepted or refused);
        /// false = no answer (the next press reuses the same key).</summary>
        public void EndRetry(string taskId, bool definitive, bool accepted, string message)
        {
            PlanCardView c = Find(taskId);
            string key;
            _retryKeys.TryGetValue(taskId ?? "", out key);
            _ledger.Unlock("retry|" + taskId, key, definitive);
            if (taskId != null) _retryKeys.Remove(taskId);
            if (c == null) return;
            c.retryInFlight = false;
            c.retryMessage = accepted ? "" : (string.IsNullOrEmpty(message) ? "Retry was not accepted" : message);
            Version++;
        }

        // ── delivery (drawing only) ──

        public int TrayCount(string agentId)
        {
            int n = 0;
            for (int i = 0; i < _cards.Count; i++) if (_cards[i].agentId == agentId && _cards[i].delivery == CardDelivery.InTray) n++;
            return n;
        }

        public int MoveCards(string agentId, CardDelivery from, CardDelivery to, bool byChute = false)
        {
            int n = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                PlanCardView c = _cards[i];
                if (c.agentId != agentId || c.delivery != from) continue;
                c.delivery = to;
                if (to == CardDelivery.OnDesk) c.deliveredByChute = byChute;
                n++;
            }
            if (n > 0) Version++;
            return n;
        }

        // ── settle + receipt ──

        /// <summary>Every card finished, or is blocked behind a failure (the backend's definition of settled).</summary>
        public bool Settled(string planId)
        {
            int n = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                PlanCardView c = _cards[i];
                if (c.planId != planId) continue;
                n++;
                if (c.Terminal) continue;
                if (c.Blocked && c.blockedBy.Length > 0) continue;
                return false;
            }
            return n > 0;
        }

        public int CountState(string planId, string state)
        {
            int n = 0;
            for (int i = 0; i < _cards.Count; i++) if ((planId == null || _cards[i].planId == planId) && _cards[i].state == state) n++;
            return n;
        }

        /// <summary>The backend created a summary artifact. True once per artifact, and only when it is anchored on one of
        /// these cards (so no other completion artifact is ever shown as the plan's summary).</summary>
        public bool OnSynthesis(string artifactId, string anchorTaskId, string summary)
        {
            if (string.IsNullOrEmpty(artifactId)) return false;
            PlanCardView c = Find(anchorTaskId);
            if (c == null) return false;
            if (!_receiptsSeen.Add(artifactId)) return false;
            _receipt = new PlanReceipt { planId = c.planId, artifactId = artifactId, anchorTaskId = anchorTaskId, summary = summary ?? "", stage = ReceiptStage.Ready };
            Version++;
            return true;
        }

        public void SetReceiptStage(ReceiptStage s)
        {
            if (_receipt.stage == ReceiptStage.None || _receipt.stage == s) return;
            _receipt.stage = s;
            Version++;
        }

        public void DismissReceipt()
        {
            if (_receipt.stage == ReceiptStage.None) return;
            _receipt = default(PlanReceipt);
            Version++;
        }

        public void Reset()
        {
            _cards.Clear(); _byId.Clear(); _ledger.Clear(); _retryKeys.Clear(); _receiptsSeen.Clear();
            _receipt = default(PlanReceipt);
            Version++;
        }
    }

    /// <summary>Maps the Manager flow's REAL plan fields to the A6 plan-review model. Nothing is added.</summary>
    public static class PlanPaperMapper
    {
        public static PlanModel ToModel(ManagerPlanFlow flow)
        {
            var m = new PlanModel
            {
                Title = flow.Title,
                UserRequest = flow.Objective,
                Objective = flow.PlanObjective,
                Source = flow.Provenance,
                State = flow.State == ManagerFlowState.AwaitingApproval || flow.State == ManagerFlowState.Revising
                        || flow.State == ManagerFlowState.Committing ? PlanState.Proposed : PlanState.Committed,
                Items = new List<PlanItemModel>(),
                Assumptions = new List<string>(flow.Assumptions),
                Risks = new List<string>(flow.Risks),
            };
            for (int i = 0; i < flow.Items.Count; i++)
            {
                ManagerPlanItemView it = flow.Items[i];
                m.Items.Add(new PlanItemModel
                {
                    Id = it.clientItemId, Title = it.title, Kind = it.kind, ProposedAgent = it.proposedAgent,
                    DependsOn = new List<string>(it.dependsOn ?? new string[0]),
                    ExpectedOutputs = new List<string>(it.acceptanceCriteria ?? new string[0]),
                });
                // A risky item says so in the Risks section with the backend's own reasons.
                if (it.dangerous)
                    m.Risks.Add("Step " + (i + 1) + " needs your explicit OK" + (it.dangerReasons != null && it.dangerReasons.Length > 0 ? ": " + string.Join(", ", it.dangerReasons) : ""));
            }
            return m;
        }
    }
}
