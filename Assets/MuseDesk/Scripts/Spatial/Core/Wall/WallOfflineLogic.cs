using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    // ---- wire DTOs (JsonUtility; field names are the backend's snake_case on purpose) ----

    [Serializable] public sealed class WallSideAmendmentDto { public string summary; public bool requires_approval; }

    // A16: backend A8 /btw adds proposed_action; it is NEVER applied by the client (applied:false), only surfaced as text.
    [Serializable] public sealed class WallSideProposedActionDto { public string kind; public bool negated; public bool requires_confirmation; public bool applied; }

    [Serializable]
    public sealed class WallSideReplyDto
    {
        public string request_id; public string agent_id; public string task_id; public string visit_session_id;
        public string reply; public string provenance; public string status;
        public int context_revision; public bool stale_context;
        public WallSideAmendmentDto proposed_amendment;
        public string label; public string intent;                 // A8: provenance label / routed intent
        public WallSideProposedActionDto proposed_action;
    }

    [Serializable]
    public sealed class WallPlanItemDto
    {
        public string client_item_id; public string title; public string kind; public string proposed_agent;
        public string[] depends_on; public string[] acceptance_criteria; public string required_permission;
        public bool dangerous; public string[] danger_reasons;
    }

    [Serializable]
    public sealed class WallCommittedTaskDto
    {
        public string client_item_id; public string task_id; public string agent_id; public string state; public int revision; public string title;
        public string[] depends_on;
    }

    // ---- plan status (GET .../manager/plans/{id}/status) and retry ----

    [Serializable]
    public sealed class WallStatusCardDto
    {
        public string task_id; public string client_item_id; public string title; public string agent_id; public string state;
        public int revision; public string[] depends_on; public string blocked_reason; public string[] blocked_by;
        public string failure; public string result;
    }

    [Serializable]
    public sealed class WallSynthesisDto { public string artifact_id; public string task_id; public string mode; public int completed; public int total; }

    [Serializable]
    public sealed class WallPlanStatusDto
    {
        public string plan_id; public string title; public string plan_status; public string provenance; public int total;
        public WallStatusCardDto[] cards; public bool settled; public WallSynthesisDto synthesis; public string source_mode;
    }

    [Serializable]
    public sealed class WallRetryDto { public string task_id; public bool retried; public string state; public int revision; }

    public sealed class WallParsedPlanStatus
    {
        public string planId, title, planStatus, provenance, sourceMode;
        public bool settled;
        /// <summary>The summary artifact, or null/empty ids when the plan has none yet.</summary>
        public string synthesisArtifactId, synthesisTaskId, synthesisMode;
        public int synthesisCompleted, synthesisTotal;
        public List<PlanCardUpdate> cards = new List<PlanCardUpdate>();
    }

    [Serializable]
    public sealed class WallPlanDto
    {
        public string plan_id; public string title; public string objective; public string provenance; public string provenance_note;
        public string status; public int revision; public string[] assumptions; public string[] risks;
        public WallPlanItemDto[] items; public WallCommittedTaskDto[] committed_tasks;
    }

    [Serializable]
    public sealed class WallCommitDto
    {
        public string plan_id; public string status; public int revision;
        public WallCommittedTaskDto[] tasks; public WallCommittedTaskDto[] committed_tasks;
    }

    [Serializable] public sealed class WallErrorDetailDto { public string code; public string message; public string item_id; }

    [Serializable]
    public sealed class WallErrorDto
    {
        public string code; public string message; public string error_code; public string user_message;
        public WallPlanDto canonical_plan; public WallErrorDetailDto[] details;
    }

    // ---- parsed results ----

    public sealed class WallParsedPlan
    {
        public string planId, title, provenance, provenanceNote, status, objective;
        public string[] assumptions = new string[0], risks = new string[0];
        public int revision;
        public List<ManagerPlanItemView> items = new List<ManagerPlanItemView>();
    }

    public sealed class WallParsedCommit
    {
        public string planId, status;
        public int revision;
        public List<ManagerPrintJob> tasks = new List<ManagerPrintJob>();
    }

    public struct WallParsedError
    {
        public long httpStatus;
        public string code;
        public string message;
        public int canonicalRevision;     // from canonical_plan on REVISION_CONFLICT, else 0
        public string firstItemId;        // details[0].item_id when present
    }

    /// <summary>A locally made template plan (offline). No model produced it.</summary>
    public sealed class WallTemplatePlan
    {
        public string planId, title, objective, provenance;
        public string[] assumptions = new string[0], risks = new string[0];
        public int revision;
        public List<ManagerPlanItemView> items = new List<ManagerPlanItemView>();
    }

    /// <summary>
    /// Pure helpers behind the Wall backend adapter (Scripts/Wall): reply parsing, error wording, change-request detection,
    /// the offline (honest, labelled) side reply, the deterministic template plan and commit validation.
    /// Nothing here touches the network or a scene.
    /// </summary>
    public static class WallOfflineLogic
    {
        public const string ProvOfflineSnapshot = "OFFLINE_SNAPSHOT";
        public const string ProvScriptedDemo = "SCRIPTED_DEMO";
        public const string FeatureOffMessage = "Wall features are switched off on the backend";
        public const string UnreachableMessage = "Backend unreachable";

        // ---------------------------------------------------------------- change requests

        static readonly string[] ChangeWords =
        {
            "cancel", "stop", "instead", "switch", "change", "redo", "delete", "deploy",
            "remove", "rollback", "revert", "replace", "abort", "rewrite", "undo"
        };

        /// <summary>Heuristic: does this question read like an instruction to change the work? Errs toward yes
        /// (a false yes only adds a "was NOT applied" note; nothing can be changed from a side question).</summary>
        public static bool LooksLikeChangeRequest(string question)
        {
            if (string.IsNullOrEmpty(question)) return false;
            string q = question.ToLowerInvariant();
            int i = 0;
            while (i < q.Length)
            {
                while (i < q.Length && !char.IsLetter(q[i])) i++;
                int start = i;
                while (i < q.Length && char.IsLetter(q[i])) i++;
                int len = i - start;
                if (len < 4) continue;
                for (int w = 0; w < ChangeWords.Length; w++)
                {
                    string cw = ChangeWords[w];
                    if (len >= cw.Length && string.CompareOrdinal(q, start, cw, 0, cw.Length) == 0) return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- offline side reply

        public static string Pretty(string agent)
        {
            if (string.IsNullOrEmpty(agent)) return "The agent";
            return char.ToUpperInvariant(agent[0]) + agent.Substring(1);
        }

        public static string StateWords(string state)
        {
            switch (state)
            {
                case "queued": return "queued";
                case "running": return "running";
                case "awaiting_approval": return "waiting for your decision";
                case "blocked": return "blocked on another task";
                case "pause_requested": return "pausing";
                case "paused": return "paused";
                case "completed": return "completed";
                case "failed": return "failed";
                case "cancelled": return "cancelled";
                default: return string.IsNullOrEmpty(state) ? "state unknown" : state;
            }
        }

        public static string Clip(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        /// <summary>
        /// Offline /btw answer built ONLY from what the local reducers say. Never invents progress.
        /// scriptedDemo: the local state belongs to the scripted demo (provenance SCRIPTED_DEMO), else OFFLINE_SNAPSHOT.
        /// boardCount is only used for the manager (-1 = unknown).
        /// </summary>
        public static SideExchange BuildOfflineSide(string requestId, string question, string agentId, bool haveTask,
                                                    string title, string state, int revision, int askedRevision,
                                                    int boardCount, bool scriptedDemo)
        {
            string prefix = scriptedDemo ? "[SCRIPTED DEMO] " : "[OFFLINE SNAPSHOT] ";
            string basis = scriptedDemo ? " This is scripted demo state, not an AI answer."
                                        : " This is the last state this headset saw, not an AI answer.";
            var ex = new SideExchange
            {
                requestId = requestId,
                question = question,
                provenance = scriptedDemo ? ProvScriptedDemo : ProvOfflineSnapshot,
                status = "answered",
                contextRevision = haveTask ? revision : Math.Max(0, askedRevision),
                staleContext = haveTask && askedRevision >= 0 && askedRevision < revision,
            };
            var sb = new StringBuilder(prefix);
            if (agentId == "manager")
            {
                sb.Append("The Manager has no task of its own.");
                if (boardCount >= 0) sb.Append(" The board shows ").Append(boardCount).Append(boardCount == 1 ? " card." : " cards.");
            }
            else if (haveTask)
            {
                sb.Append(Pretty(agentId)).Append(" has \"").Append(Clip(title, 60)).Append("\": ")
                  .Append(StateWords(state)).Append(", revision ").Append(revision).Append('.');
            }
            else sb.Append("No task right now.");
            sb.Append(basis);
            if (LooksLikeChangeRequest(question))
            {
                ex.status = "amendment_suggested";
                ex.amendment = question == null ? "" : question.Trim();
                sb.Append(" That reads like a change request. It was NOT applied and nothing changed. Use the task board to change work.");
            }
            ex.reply = sb.ToString();
            return ex;
        }

        // ---------------------------------------------------------------- template plan (offline)

        static readonly string[] DangerWords = { "deploy", "delete", "payment", "credential", "secret", "token", "password" };

        public static List<string> DangerReasonsFor(string objective)
        {
            var r = new List<string>();
            if (string.IsNullOrEmpty(objective)) return r;
            string o = objective.ToLowerInvariant();
            for (int i = 0; i < DangerWords.Length; i++)
                if (o.Contains(DangerWords[i])) r.Add("objective mentions " + DangerWords[i]);
            return r;
        }

        /// <summary>Deterministic research, implement, test chain. No model involved: provenance SCRIPTED_DEMO.</summary>
        public static WallTemplatePlan BuildTemplatePlan(string objective, int counter)
        {
            string obj = Clip(objective, 60);
            var danger = DangerReasonsFor(objective);
            var plan = new WallTemplatePlan
            {
                planId = "demo-plan-" + counter,
                revision = 1,
                objective = objective == null ? "" : objective.Trim(),
                title = "Plan: " + Clip(objective, 50),
                provenance = ProvScriptedDemo,
                // The same words the backend's template plan carries (planner.template_plan).
                assumptions = new[]
                {
                    "The existing repository and workspace are used as they are.",
                    "Each step is a plain board card owned by one worker; the user approves before any card exists.",
                },
                risks = new[] { "Template plan: it does not analyse the objective, so scope and dependencies may need revising." },
            };
            plan.items.Add(new ManagerPlanItemView
            {
                clientItemId = "a", title = "Research: " + obj, kind = "research", proposedAgent = "researcher",
                dependsOn = new string[0], acceptanceCriteria = new[] { "Options and risks are written up for review" },
                requiredPermission = "read_repo",
            });
            plan.items.Add(new ManagerPlanItemView
            {
                clientItemId = "b", title = "Implement: " + obj, kind = "implement", proposedAgent = "developer",
                dependsOn = new[] { "a" }, acceptanceCriteria = new[] { "The change is implemented and builds" },
                requiredPermission = "workspace_write", dangerous = danger.Count > 0, dangerReasons = danger.ToArray(),
            });
            plan.items.Add(new ManagerPlanItemView
            {
                clientItemId = "c", title = "Test: " + obj, kind = "test", proposedAgent = "tester",
                dependsOn = new[] { "b" }, acceptanceCriteria = new[] { "Tests pass and the results are reported" },
                requiredPermission = "run_tests",
            });
            return plan;
        }

        // ---------------------------------------------------------------- scripted plan-card work (offline)

        /// <summary>
        /// The labelled deliverable the offline plan runner attaches when a plan card is worked, mirroring the backend
        /// runner's scripted mode. Never claims a test ran or code was applied. kind is a v2 artifact kind string.
        /// </summary>
        public static void ScriptedDeliverable(string agentId, string cardTitle, string objective,
                                               out string kind, out string summary, out string pageTitle, out string body)
        {
            string obj = Clip(objective, 80);
            string what;
            switch (agentId)
            {
                case "researcher":
                    kind = "research_report"; pageTitle = "Research write-up";
                    what = "Findings for \"" + obj + "\": the existing structure is reused and the options are compared on effort and risk. Recommendation: the simplest option that meets the criteria.";
                    break;
                case "tester":
                    kind = "implementation_plan"; pageTitle = "Test plan";
                    what = "Test plan for \"" + obj + "\": one happy-path case, one failure case and one edge case per acceptance criterion. No tests were run.";
                    break;
                default:
                    kind = "implementation_plan"; pageTitle = "Proposed change";
                    what = "Proposed change for \"" + obj + "\": the relevant module gains the new behaviour behind its existing interface. Nothing was applied.";
                    break;
            }
            summary = pageTitle + ": scripted for \"" + Clip(cardTitle, 60) + "\"";
            body = "[SCRIPTED DEMO] No model was used.\n\n" + what;
        }

        // ---------------------------------------------------------------- commit validation (offline)

        static ManagerPlanItemView FindItem(IList<ManagerPlanItemView> items, string id)
        {
            for (int i = 0; i < items.Count; i++) if (items[i].clientItemId == id) return items[i];
            return null;
        }

        /// <summary>Same refusals the backend makes, in the same order. True = commit may proceed.</summary>
        public static bool ValidateCommit(IList<ManagerPlanItemView> items, IList<string> approved, IList<string> authorizedDangerous,
                                          out string code, out string message)
        {
            code = message = null;
            if (approved == null || approved.Count == 0) { code = "NO_ITEMS_APPROVED"; message = "Tick at least one step"; return false; }
            for (int i = 0; i < approved.Count; i++)
            {
                var it = FindItem(items, approved[i]);
                if (it == null) { code = "UNKNOWN_ITEM"; message = "Unknown step: " + approved[i]; return false; }
            }
            for (int i = 0; i < approved.Count; i++)
            {
                var it = FindItem(items, approved[i]);
                for (int d = 0; d < it.dependsOn.Length; d++)
                    if (!approved.Contains(it.dependsOn[d]))
                    { code = "DEPENDENCY_NOT_APPROVED"; message = "\"" + it.title + "\" needs step " + it.dependsOn[d] + " too"; return false; }
            }
            for (int i = 0; i < approved.Count; i++)
            {
                var it = FindItem(items, approved[i]);
                if (it.dangerous && (authorizedDangerous == null || !authorizedDangerous.Contains(it.clientItemId)))
                { code = "AUTHORIZATION_REQUIRED"; message = "\"" + it.title + "\" needs your explicit OK"; return false; }
            }
            return true;
        }

        /// <summary>The approved items in dependency order (Kahn, stable by plan order). Empty on a cycle.</summary>
        public static List<ManagerPlanItemView> OrderForCreate(IList<ManagerPlanItemView> items, IList<string> approved)
        {
            var result = new List<ManagerPlanItemView>();
            var done = new HashSet<string>();
            var pending = new List<ManagerPlanItemView>();
            for (int i = 0; i < items.Count; i++) if (approved.Contains(items[i].clientItemId)) pending.Add(items[i]);
            int guard = pending.Count + 1;
            while (pending.Count > 0 && guard-- > 0)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    bool ready = true;
                    for (int d = 0; d < pending[i].dependsOn.Length; d++)
                        if (!done.Contains(pending[i].dependsOn[d])) { ready = false; break; }
                    if (!ready) continue;
                    result.Add(pending[i]);
                    done.Add(pending[i].clientItemId);
                    pending.RemoveAt(i);
                    i--;
                }
            }
            if (pending.Count > 0) result.Clear();
            return result;
        }

        // ---------------------------------------------------------------- reply parsing

        static string[] S(string[] a) { return a ?? new string[0]; }

        public static ManagerPlanItemView MapItem(WallPlanItemDto d)
        {
            return new ManagerPlanItemView
            {
                clientItemId = d.client_item_id, title = d.title, kind = d.kind, proposedAgent = d.proposed_agent,
                dependsOn = S(d.depends_on), acceptanceCriteria = S(d.acceptance_criteria),
                requiredPermission = d.required_permission, dangerous = d.dangerous, dangerReasons = S(d.danger_reasons),
            };
        }

        static WallParsedPlan MapPlan(WallPlanDto d)
        {
            var p = new WallParsedPlan
            {
                planId = d.plan_id, title = d.title, provenance = d.provenance, provenanceNote = d.provenance_note,
                status = d.status, revision = d.revision, objective = d.objective,
                assumptions = S(d.assumptions), risks = S(d.risks),
            };
            if (d.items != null) for (int i = 0; i < d.items.Length; i++) if (d.items[i] != null) p.items.Add(MapItem(d.items[i]));
            return p;
        }

        public static bool TryParsePlan(string json, out WallParsedPlan plan)
        {
            plan = null;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                var d = JsonUtility.FromJson<WallPlanDto>(json);
                if (d == null || string.IsNullOrEmpty(d.plan_id) || d.items == null || d.items.Length == 0) return false;
                plan = MapPlan(d);
                return true;
            }
            catch (Exception) { return false; }
        }

        static void AddTasks(List<ManagerPrintJob> into, WallCommittedTaskDto[] src, Dictionary<string, string> titles)
        {
            if (src == null) return;
            for (int i = 0; i < src.Length; i++)
            {
                var t = src[i];
                if (t == null || string.IsNullOrEmpty(t.task_id)) continue;     // never invent an id
                string title = t.title;
                if (string.IsNullOrEmpty(title) && titles != null && t.client_item_id != null) titles.TryGetValue(t.client_item_id, out title);
                into.Add(new ManagerPrintJob { taskId = t.task_id, clientItemId = t.client_item_id, agentId = t.agent_id, title = title });
            }
        }

        /// <summary>Commit reply. Reads `tasks` (commit response) and `committed_tasks` (plan object). titles: clientItemId to title.</summary>
        public static bool TryParseCommit(string json, Dictionary<string, string> titles, out WallParsedCommit commit)
        {
            commit = null;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                var d = JsonUtility.FromJson<WallCommitDto>(json);
                if (d == null) return false;
                var c = new WallParsedCommit { planId = d.plan_id, status = d.status, revision = d.revision };
                AddTasks(c.tasks, d.tasks != null && d.tasks.Length > 0 ? d.tasks : d.committed_tasks, titles);
                if (c.tasks.Count == 0) return false;
                commit = c;
                return true;
            }
            catch (Exception) { return false; }
        }

        public static bool TryParseSide(string json, string requestId, out SideExchange ex)
        {
            ex = default(SideExchange);
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                var d = JsonUtility.FromJson<WallSideReplyDto>(json);
                if (d == null || (string.IsNullOrEmpty(d.reply) && string.IsNullOrEmpty(d.status))) return false;
                ex = new SideExchange
                {
                    requestId = string.IsNullOrEmpty(d.request_id) ? requestId : d.request_id,
                    reply = d.reply,
                    provenance = string.IsNullOrEmpty(d.provenance) ? "NONE" : d.provenance,
                    status = string.IsNullOrEmpty(d.status) ? "answered" : d.status,
                    staleContext = d.stale_context,
                    contextRevision = d.context_revision,
                    amendment = d.proposed_amendment != null && !string.IsNullOrEmpty(d.proposed_amendment.summary) ? d.proposed_amendment.summary : null,
                    label = d.label,
                };
                // A negated proposed action ("don't do X") must never surface as a suggested change.
                if (d.proposed_action != null && d.proposed_action.negated) ex.amendment = null;
                return true;
            }
            catch (Exception) { return false; }
        }

        // ---------------------------------------------------------------- plan status, retry, offline reasons and roll-up

        public static bool TryParsePlanStatus(string json, out WallParsedPlanStatus status)
        {
            status = null;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                var d = JsonUtility.FromJson<WallPlanStatusDto>(json);
                if (d == null || string.IsNullOrEmpty(d.plan_id) || d.cards == null) return false;
                var s = new WallParsedPlanStatus
                {
                    planId = d.plan_id, title = d.title, planStatus = d.plan_status, provenance = d.provenance,
                    sourceMode = d.source_mode, settled = d.settled,
                };
                if (d.synthesis != null && !string.IsNullOrEmpty(d.synthesis.artifact_id))
                {
                    s.synthesisArtifactId = d.synthesis.artifact_id; s.synthesisTaskId = d.synthesis.task_id; s.synthesisMode = d.synthesis.mode;
                    s.synthesisCompleted = d.synthesis.completed; s.synthesisTotal = d.synthesis.total;
                }
                for (int i = 0; i < d.cards.Length; i++)
                {
                    var c = d.cards[i];
                    if (c == null || string.IsNullOrEmpty(c.task_id)) continue;
                    s.cards.Add(new PlanCardUpdate
                    {
                        taskId = c.task_id, state = c.state, agentId = c.agent_id, title = c.title, revision = c.revision,
                        dependsOn = S(c.depends_on),
                        // A status read is the full truth for the card: an absent reason means there is none.
                        blockedReason = c.blocked_reason ?? "", blockedBy = S(c.blocked_by), failureReason = c.failure ?? "",
                        resultSummary = string.IsNullOrEmpty(c.result) ? null : c.result,
                    });
                }
                status = s;
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Retry reply: true when it parsed. <paramref name="retried"/> false means the backend treated it as a repeat (no-op).</summary>
        public static bool TryParseRetry(string json, out bool retried, out string state, out int revision)
        {
            retried = false; state = null; revision = -1;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                var d = JsonUtility.FromJson<WallRetryDto>(json);
                if (d == null || string.IsNullOrEmpty(d.task_id)) return false;
                retried = d.retried; state = d.state; revision = d.revision;
                return true;
            }
            catch (Exception) { return false; }
        }

        // The offline demo board says the same words the backend does (plan_ops.blocked_info), so a blocked card reads
        // the same in both modes.
        public static string BlockedByFailureReason(IList<string> failedTitles, string failedState = "failed")
        {
            var sb = new StringBuilder("Blocked: it depends on ");
            for (int i = 0; i < failedTitles.Count; i++) { if (i > 0) sb.Append(", "); sb.Append('\'').Append(failedTitles[i]).Append("' (").Append(failedState).Append(')'); }
            return sb.Append(". Retry the failed step to continue.").ToString();
        }

        public static string BlockedWaitingReason(IList<string> waitingTitles)
        {
            if (waitingTitles == null || waitingTitles.Count == 0) return "Waiting to be released.";
            var sb = new StringBuilder("Waiting for ");
            for (int i = 0; i < waitingTitles.Count; i++) { if (i > 0) sb.Append(", "); sb.Append('\'').Append(waitingTitles[i]).Append('\''); }
            return sb.Append(" to finish.").ToString();
        }

        public const string ScriptedFailureReason = "[SCRIPTED DEMO] Scripted failure for the demonstration. No model was used and nothing was run.";
        public const string ScriptedRollupLabel = "[SCRIPTED DEMO] No model was used; this is a roll-up of the scripted steps.";

        /// <summary>One step of an offline plan for the roll-up (state and the recorded result text).</summary>
        public struct RollupStep { public string title, agentId, state, result; }

        /// <summary>The offline counterpart of the backend's deterministic plan roll-up (plan_ops.rollup): built only from the
        /// recorded step states and results. <paramref name="summary"/> is the artifact's one-line summary.</summary>
        public static void OfflineRollup(string planTitle, string objective, IList<RollupStep> steps, out string summary, out string body)
        {
            int done = 0, failed = 0, blocked = 0, cancelled = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].state == "completed") done++;
                else if (steps[i].state == "failed") failed++;
                else if (steps[i].state == "blocked") blocked++;
                else if (steps[i].state == "cancelled") cancelled++;
            }
            var sb = new StringBuilder();
            sb.Append("Plan: ").Append(planTitle).Append('\n').Append("Objective: ").Append(objective).Append('\n');
            sb.Append("Result: ").Append(done).Append(" of ").Append(steps.Count).Append(" steps completed, ").Append(failed).Append(" failed, ")
              .Append(blocked).Append(" blocked, ").Append(cancelled).Append(" cancelled.\n\nSteps:");
            for (int i = 0; i < steps.Count; i++)
                sb.Append('\n').Append(i + 1).Append(". [").Append(steps[i].state).Append("] ").Append(steps[i].title).Append(" (").Append(steps[i].agentId).Append("): ")
                  .Append(string.IsNullOrEmpty(steps[i].result) ? "No result recorded." : steps[i].result);
            if (failed > 0 || blocked > 0 || cancelled > 0) sb.Append("\n\nOpen issues: not every step finished. Retry failed steps to continue.");
            body = sb.ToString();
            summary = "[SCRIPTED DEMO] Plan summary: " + done + " of " + steps.Count + " steps completed";
        }

        // ---------------------------------------------------------------- errors

        public static WallParsedError ParseError(long httpStatus, string body)
        {
            var e = new WallParsedError { httpStatus = httpStatus };
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var d = JsonUtility.FromJson<WallErrorDto>(body);
                    if (d != null)
                    {
                        e.code = !string.IsNullOrEmpty(d.code) ? d.code : d.error_code;
                        e.message = !string.IsNullOrEmpty(d.user_message) ? d.user_message : d.message;
                        if (d.canonical_plan != null) e.canonicalRevision = d.canonical_plan.revision;
                        if (d.details != null && d.details.Length > 0 && d.details[0] != null) e.firstItemId = d.details[0].item_id;
                    }
                }
                catch (Exception) { /* not our error shape */ }
            }
            if (string.IsNullOrEmpty(e.code))
                e.code = httpStatus == 401 ? "UNAUTHORIZED" : httpStatus == 404 ? "NOT_FOUND" : "HTTP_" + httpStatus;
            return e;
        }

        /// <summary>The words shown to the user for a backend refusal.</summary>
        public static string FriendlyMessage(WallParsedError e)
        {
            if (e.code == "FEATURE_DISABLED") return FeatureOffMessage;
            string m = e.message;
            if (string.IsNullOrEmpty(m)) m = e.httpStatus == 401 ? "The backend refused this session" : "Backend error (HTTP " + e.httpStatus + ")";
            if (e.code == "REVISION_CONFLICT" && e.canonicalRevision > 0) m += " (plan is now revision " + e.canonicalRevision + ")";
            return m;
        }
    }
}
