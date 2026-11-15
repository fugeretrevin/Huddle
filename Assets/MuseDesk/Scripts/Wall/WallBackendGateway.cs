using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using MuseDesk.Networking;
using MuseDesk.Phase2;
using MuseDesk.Phase2.Contracts;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>
    /// The Wall Habitat's only door to the backend. Live: the additive /v2 Phase 3 routes with the v1 session id and
    /// bearer token (UnityWebRequest, callbacks complete on the Unity main thread). Offline: honest local fallbacks,
    /// always labelled (OFFLINE_SNAPSHOT / SCRIPTED_DEMO / SCRIPTED_DEMO template plan), never presented as live AI.
    /// Offline plan commits go through the same board gateway as BoardPresenter.RequestCreateTaskAsync.
    /// </summary>
    [DisallowMultipleComponent]
    public class WallBackendGateway : MonoBehaviour, IWallBackend
    {
        [Header("Wiring (found once if empty)")]
        public SessionController session;
        public Phase2Bootstrap phase2;

        [Header("Timeouts (s)")]
        [Tooltip("Must exceed the backend's MUSEDESK_P3_SIDE_TIMEOUT_SECONDS (30) so a slow model answer is not cut off.")]
        [Range(5, 90)] public int sideTimeoutSeconds = 40;
        [Tooltip("Must exceed the backend's MUSEDESK_P3_PLAN_TIMEOUT_SECONDS (60).")]
        [Range(5, 120)] public int planTimeoutSeconds = 75;

        [Header("Offline plan runner (scripted demo)")]
        [Tooltip("Seconds each offline Manager plan card is 'worked' before its labelled scripted deliverable lands.")]
        [Range(1f, 30f)] public float offlineWorkSeconds = 8f;

        const string Tag = "[MuseDesk.Wall] ";
        // Older scenes serialized shorter timeouts; never wait less than the backend does.
        const int MinSideTimeout = 35, MinPlanTimeout = 70;

        sealed class OfflineCard { public string taskId, agentId, title, objective, planId, result; }
        string _failNextAgent;

        /// <summary>SCRIPTED DEMO ONLY: the next offline plan card worked by <paramref name="agentId"/> ends FAILED with a labelled
        /// scripted reason instead of a deliverable, so the blocked/Retry path can be shown without a backend. One shot.
        /// Does nothing in a live session (the backend decides what fails there).</summary>
        public void ScriptFailNextOfflineCard(string agentId) { _failNextAgent = IsLive ? null : agentId; }
        readonly List<OfflineCard> _offlineCards = new List<OfflineCard>();
        OfflineCard _working;
        float _workingUntil;

        sealed class OfflinePlan
        {
            public WallTemplatePlan plan;
            public string status = "proposed";     // proposed | committed | rejected
            public bool committing;
            public string commitKey;
            public ManagerCommitResult lastResult;
            public bool synthesized;
            public int summaryCount;
            public string summaryArtifactId, summaryAnchor;
        }

        readonly Dictionary<string, OfflinePlan> _offline = new Dictionary<string, OfflinePlan>();
        readonly Dictionary<string, Dictionary<string, string>> _liveTitles = new Dictionary<string, Dictionary<string, string>>();
        CancellationTokenSource _cts;
        int _planCounter;
        bool _searched;

        void Awake()
        {
            _cts = new CancellationTokenSource();
            sideTimeoutSeconds = Math.Max(sideTimeoutSeconds, MinSideTimeout);
            planTimeoutSeconds = Math.Max(planTimeoutSeconds, MinPlanTimeout);
            FindRefsOnce();
        }

        // ------------------------------------------------------------------ offline plan runner

        /// <summary>
        /// Offline counterpart of the backend plan runner: works the cards a committed OFFLINE plan created, one at a
        /// time, in dependency order, with a labelled scripted deliverable. Live sessions are worked by the backend,
        /// so this does nothing once live. A card the user pauses while it is worked keeps its paused state.
        /// </summary>
        void Update()
        {
            if (_offlineCards.Count == 0) return;
            if (IsLive) { _offlineCards.Clear(); _working = null; return; }
            Phase2DemoGateway demo = phase2 != null ? phase2.DemoGateway : null;
            if (demo == null) return;

            if (_working != null)
            {
                if (demo.PlanCardState(_working.taskId) != TaskStates.Running) { _working = null; return; }   // paused or reset
                if (Time.unscaledTime < _workingUntil) return;
                if (_failNextAgent != null && _failNextAgent == _working.agentId)
                {
                    _failNextAgent = null;
                    if (demo.TryFailPlanCard(_working.taskId, WallOfflineLogic.ScriptedFailureReason,
                                             failedTitle => WallOfflineLogic.BlockedByFailureReason(new List<string> { failedTitle })))
                    {
                        _working.result = WallOfflineLogic.ScriptedFailureReason;
                        Debug.Log(Tag + "offline plan card " + _working.taskId + " failed (SCRIPTED DEMO, scripted failure)");
                    }
                    OfflineSynthesize(demo, _working.planId);
                    _working = null;
                    return;
                }
                string kind, summary, pageTitle, body;
                WallOfflineLogic.ScriptedDeliverable(_working.agentId, _working.title, _working.objective,
                                                     out kind, out summary, out pageTitle, out body);
                if (demo.TryCompletePlanCard(_working.taskId, kind, summary, pageTitle, body))
                {
                    _working.result = summary;
                    Debug.Log(Tag + "offline plan card " + _working.taskId + " completed (SCRIPTED DEMO)");
                }
                OfflineSynthesize(demo, _working.planId);
                _working = null;
                return;
            }

            for (int i = 0; i < _offlineCards.Count; i++)
            {
                var c = _offlineCards[i];
                string st = demo.PlanCardState(c.taskId);
                if (st == null) { _offlineCards.RemoveAt(i); i--; continue; }      // the board was reset
                if (st != TaskStates.Queued || !demo.PlanCardDependenciesDone(c.taskId)) continue;
                if (!demo.TryStartPlanCard(c.taskId)) continue;
                _working = c;
                _workingUntil = Time.unscaledTime + offlineWorkSeconds;
                Debug.Log(Tag + "offline plan card " + c.taskId + " started by " + c.agentId);
                return;
            }
        }

        /// <summary>
        /// Offline counterpart of the backend's plan synthesis: once every card of an offline plan has finished (or is
        /// blocked behind a failure), publish ONE labelled roll-up of the recorded step results as a completion artifact.
        /// A retry makes the plan unfinished again, so a later settle publishes a new one.
        /// </summary>
        void OfflineSynthesize(Phase2DemoGateway demo, string planId)
        {
            OfflinePlan op;
            if (demo == null || string.IsNullOrEmpty(planId) || !_offline.TryGetValue(planId, out op) || op.synthesized) return;
            var steps = new List<WallOfflineLogic.RollupStep>();
            string anchor = null, firstCard = null;
            for (int i = 0; i < _offlineCards.Count; i++)
            {
                OfflineCard c = _offlineCards[i];
                if (c.planId != planId) continue;
                TaskCardV2 card = demo.PlanCardSnapshot(c.taskId);
                if (card == null) return;                                   // the board was reset
                string st = card.TaskState;
                bool terminal = st == TaskStates.Completed || st == TaskStates.Failed || st == TaskStates.Cancelled;
                if (!terminal && !(st == TaskStates.Blocked && OfflineRootFailures(demo, card).Count > 0)) return;   // not settled
                if (firstCard == null) firstCard = c.taskId;
                if (st == TaskStates.Completed) anchor = c.taskId;
                steps.Add(new WallOfflineLogic.RollupStep { title = c.title, agentId = c.agentId, state = st,
                    result = st == TaskStates.Blocked ? OfflineBlockedReason(demo, card, out _) : c.result });
            }
            if (steps.Count == 0) return;
            string summary, body;
            WallOfflineLogic.OfflineRollup(op.plan.title, op.plan.objective, steps, out summary, out body);
            op.summaryCount++;
            string id = "art-demo-plansum-" + planId + "-" + op.summaryCount;
            if (demo.TryPublishPlanSummary(anchor ?? firstCard, id, summary, "Plan summary (" + WallOfflineLogic.ScriptedRollupLabel + ")",
                                           WallOfflineLogic.ScriptedRollupLabel + "\n\n" + body))
            {
                op.synthesized = true;
                op.summaryArtifactId = id;
                op.summaryAnchor = anchor ?? firstCard;
                Debug.Log(Tag + "offline plan " + planId + " summary published (SCRIPTED DEMO roll-up)");
            }
        }

        /// <summary>Failed or cancelled cards that keep <paramref name="card"/> from ever running (transitive), from the demo board.</summary>
        static List<TaskCardV2> OfflineRootFailures(Phase2DemoGateway demo, TaskCardV2 card, HashSet<string> seen = null)
        {
            var roots = new List<TaskCardV2>();
            if (seen == null) seen = new HashSet<string>();
            foreach (string d in card.Dependencies ?? new List<string>())
            {
                if (!seen.Add(d)) continue;
                TaskCardV2 dep = demo.PlanCardSnapshot(d);
                if (dep == null) continue;
                if (dep.TaskState == TaskStates.Failed || dep.TaskState == TaskStates.Cancelled) roots.Add(dep);
                else if (dep.TaskState != TaskStates.Completed) roots.AddRange(OfflineRootFailures(demo, dep, seen));
            }
            return roots;
        }

        /// <summary>The same words the backend uses for a blocked card (plan_ops.blocked_info), from the demo board's own state.</summary>
        static string OfflineBlockedReason(Phase2DemoGateway demo, TaskCardV2 card, out string[] blockedBy)
        {
            var roots = OfflineRootFailures(demo, card);
            if (roots.Count > 0)
            {
                var titles = new List<string>(); var ids = new List<string>();
                for (int i = 0; i < roots.Count; i++) { titles.Add(roots[i].Title); ids.Add(roots[i].TaskId); }
                blockedBy = ids.ToArray();
                return WallOfflineLogic.BlockedByFailureReason(titles, roots[0].TaskState);
            }
            blockedBy = new string[0];
            var waiting = new List<string>();
            foreach (string d in card.Dependencies ?? new List<string>())
            {
                TaskCardV2 dep = demo.PlanCardSnapshot(d);
                if (dep == null || dep.TaskState != TaskStates.Completed) waiting.Add(dep != null ? dep.Title : d);
            }
            return WallOfflineLogic.BlockedWaitingReason(waiting);
        }

        void OnDestroy()
        {
            try { if (_cts != null) _cts.Cancel(); } catch { }
            if (_cts != null) _cts.Dispose();
        }

        void FindRefsOnce()
        {
            if (_searched) return;
            _searched = true;
            if (session == null) session = FindAnyObjectByType<SessionController>();
            if (phase2 == null) phase2 = FindAnyObjectByType<Phase2Bootstrap>();
        }

        // ------------------------------------------------------------------ mode

        /// <summary>The v1 session is live: the same rule Phase2Bootstrap uses to choose the live gateway.</summary>
        public bool IsLive
        {
            get
            {
                FindRefsOnce();
                if (session == null || !session.Initialized || !session.IsLive) return false;
                if (phase2 != null && !phase2.preferLiveWhenAvailable) return false;
                return !string.IsNullOrEmpty(session.LiveSessionId) && !string.IsNullOrEmpty(session.LiveToken)
                       && !string.IsNullOrEmpty(session.ApiBaseUrl);
            }
        }

        bool ScriptedDemo { get { return session != null && session.Gateway is DemoAgentGateway; } }

        // ------------------------------------------------------------------ HTTP

        // done(httpStatus, body, networkError). networkError is null when a response arrived (any status).
        void Send(string verb, string path, string json, int timeoutSeconds, Action<long, string, string> done)
        {
            var req = new UnityWebRequest(session.ApiBaseUrl.TrimEnd('/') + path, verb);
            if (json != null) req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + session.LiveToken);
            req.timeout = timeoutSeconds;
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                long status = req.responseCode;
                string body = null, net = null;
                if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError)
                    net = string.IsNullOrEmpty(req.error) ? "network error" : req.error;
                else if (req.downloadHandler != null) body = req.downloadHandler.text;
                req.Dispose();
                if (this == null) return;
                try { done(status, body, net); }
                catch (Exception ex) { Debug.LogError(Tag + "callback error: " + ex.Message); }
            };
        }

        static bool IsTimeout(string net) { return net != null && net.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0; }

        string Sid { get { return Uri.EscapeDataString(session.LiveSessionId); } }

        static string Key(string given, string fallbackPrefix, string id)
        {
            return !string.IsNullOrEmpty(given) && given.Length >= 8 ? given : fallbackPrefix + id;
        }

        // ------------------------------------------------------------------ side question

        public void AskSide(string agentId, string taskId, string visitSessionId, string question, int contextRevision,
                            string requestId, string idempotencyKey, Action<SideExchange> done)
        {
            if (done == null) return;
            if (IsLive) AskSideLive(agentId, taskId, visitSessionId, question, contextRevision, requestId, idempotencyKey, done);
            else done(AskSideOffline(agentId, taskId, question, contextRevision, requestId));
        }

        SideExchange AskSideOffline(string agentId, string taskId, string question, int contextRevision, string requestId)
        {
            FindRefsOnce();
            string tid, title, state; int rev;
            Phase2Reducer p2 = phase2 != null ? phase2.Reducer : null;
            SessionReducer v1 = session != null ? session.Reducer : null;
            bool have = WallTaskLookup.TryFind(p2, v1, agentId, taskId, out tid, out title, out state, out rev);
            int count = p2 != null ? p2.Tasks.Count : -1;
            var ex = WallOfflineLogic.BuildOfflineSide(requestId, question, agentId, have, title, state, rev, contextRevision, count, ScriptedDemo);
            Debug.Log(Tag + "side question answered offline (" + ex.provenance + ", " + ex.status + ")");
            return ex;
        }

        static SideExchange SideFail(string requestId, string status, string reply)
        {
            return new SideExchange { requestId = requestId, reply = reply, provenance = "NONE", status = status };
        }

        void AskSideLive(string agentId, string taskId, string visitSessionId, string question, int contextRevision,
                         string requestId, string idempotencyKey, Action<SideExchange> done)
        {
            var body = new JObject
            {
                ["request_id"] = requestId,
                ["visit_session_id"] = visitSessionId,
                ["question"] = question,
                ["idempotency_key"] = Key(idempotencyKey, "btw-", requestId),
            };
            if (!string.IsNullOrEmpty(taskId)) body["task_id"] = taskId;
            if (contextRevision >= 0) body["context_revision"] = contextRevision;
            string path = "/v2/sessions/" + Sid + "/agents/" + Uri.EscapeDataString(agentId) + "/side-question";
            Send(UnityWebRequest.kHttpVerbPOST, path, body.ToString(Newtonsoft.Json.Formatting.None), sideTimeoutSeconds, (status, resp, net) =>
            {
                if (net != null)
                {
                    done(IsTimeout(net) ? SideFail(requestId, "timeout", "No answer in time. The agent's work is not affected.")
                                        : SideFail(requestId, "failed", WallOfflineLogic.UnreachableMessage + ". The agent's work is not affected."));
                    return;
                }
                if (status >= 200 && status < 300)
                {
                    SideExchange ex;
                    if (WallOfflineLogic.TryParseSide(resp, requestId, out ex)) { done(ex); return; }
                    done(SideFail(requestId, "failed", "The backend sent an answer this app could not read."));
                    return;
                }
                var err = WallOfflineLogic.ParseError(status, resp);
                done(SideFail(requestId, err.code == "FEATURE_DISABLED" ? "unavailable" : "failed", WallOfflineLogic.FriendlyMessage(err)));
            });
        }

        public void ClearVisit(string agentId, string visitSessionId)
        {
            if (!IsLive || string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(visitSessionId)) return;
            string path = "/v2/sessions/" + Sid + "/agents/" + Uri.EscapeDataString(agentId) + "/visits/" + Uri.EscapeDataString(visitSessionId);
            Send(UnityWebRequest.kHttpVerbDELETE, path, null, sideTimeoutSeconds, (status, resp, net) =>
            {
                if (net != null || status < 200 || status >= 300)
                    Debug.Log(Tag + "visit memory not cleared (" + (net != null ? "unreachable" : "HTTP " + status) + "); it expires on the backend");
            });
        }

        // ------------------------------------------------------------------ manager: propose

        public void ProposePlan(string requestId, string objective, string idempotencyKey, Action<ManagerPlanResult> done)
        {
            if (done == null) return;
            if (!IsLive) { done(ProposeOffline(objective)); return; }

            var body = new JObject
            {
                ["request_id"] = requestId,
                ["objective"] = WallOfflineLogic.Clip(objective, 500),
                ["idempotency_key"] = Key(idempotencyKey, "mgr-propose-", requestId),
            };
            Send(UnityWebRequest.kHttpVerbPOST, "/v2/sessions/" + Sid + "/manager/plans", body.ToString(Newtonsoft.Json.Formatting.None), planTimeoutSeconds,
                (status, resp, net) =>
                {
                    if (net != null) { done(PlanFail("UNREACHABLE", IsTimeout(net) ? "The manager did not answer in time" : WallOfflineLogic.UnreachableMessage)); return; }
                    if (status >= 200 && status < 300)
                    {
                        WallParsedPlan p;
                        if (!WallOfflineLogic.TryParsePlan(resp, out p)) { done(PlanFail("BAD_REPLY", "The backend sent a plan this app could not read")); return; }
                        var titles = new Dictionary<string, string>();
                        for (int i = 0; i < p.items.Count; i++) titles[p.items[i].clientItemId] = p.items[i].title;
                        _liveTitles[p.planId] = titles;
                        done(PlanOk(p));
                        return;
                    }
                    var err = WallOfflineLogic.ParseError(status, resp);
                    done(PlanFail(err.code, WallOfflineLogic.FriendlyMessage(err)));
                });
        }

        static ManagerPlanResult PlanOk(WallParsedPlan p)
        {
            var r = new ManagerPlanResult { ok = true, planId = p.planId, revision = p.revision, title = p.title, provenance = p.provenance,
                                            objective = p.objective, assumptions = p.assumptions, risks = p.risks };
            r.items = p.items;
            return r;
        }

        static ManagerPlanResult PlanFail(string code, string message)
        {
            return new ManagerPlanResult { ok = false, errorCode = code, error = message };
        }

        ManagerPlanResult ProposeOffline(string objective)
        {
            _planCounter++;
            var plan = WallOfflineLogic.BuildTemplatePlan(objective, _planCounter);
            _offline[plan.planId] = new OfflinePlan { plan = plan };
            Debug.Log(Tag + "offline template plan " + plan.planId + " (SCRIPTED_DEMO, " + plan.items.Count + " items)");
            return OfflineResult(plan);
        }

        static ManagerPlanResult OfflineResult(WallTemplatePlan plan)
        {
            var r = new ManagerPlanResult { ok = true, planId = plan.planId, revision = plan.revision, title = plan.title, provenance = plan.provenance,
                                            objective = plan.objective, assumptions = plan.assumptions, risks = plan.risks };
            for (int i = 0; i < plan.items.Count; i++) r.items.Add(plan.items[i]);
            return r;
        }

        // ------------------------------------------------------------------ manager: revise (an open plan; creates nothing)

        public void RevisePlan(string planId, int revision, List<ManagerPlanItemView> keptItems, string idempotencyKey, Action<ManagerPlanResult> done)
        {
            if (done == null) return;
            if (keptItems == null || keptItems.Count == 0) { done(PlanFail("PLAN_INVALID", "A plan needs at least one step")); return; }
            OfflinePlan op;
            if (!string.IsNullOrEmpty(planId) && _offline.TryGetValue(planId, out op))
            {
                if (op.status != "proposed") { done(PlanFail(op.status == "committed" ? "PLAN_ALREADY_COMMITTED" : "PLAN_NOT_OPEN", "That plan is no longer open")); return; }
                if (revision != op.plan.revision) { done(PlanFail("REVISION_CONFLICT", "The plan changed (it is now revision " + op.plan.revision + "). Ask again.")); return; }
                var ids = new List<string>();
                for (int i = 0; i < keptItems.Count; i++) ids.Add(keptItems[i].clientItemId);
                for (int i = 0; i < keptItems.Count; i++)
                    for (int d = 0; d < keptItems[i].dependsOn.Length; d++)
                        if (!ids.Contains(keptItems[i].dependsOn[d])) { done(PlanFail("PLAN_INVALID", "\"" + keptItems[i].title + "\" depends on a step that was removed")); return; }
                op.plan.items.Clear();
                op.plan.items.AddRange(keptItems);
                op.plan.revision++;
                Debug.Log(Tag + "offline plan " + planId + " revised to r" + op.plan.revision + " (" + keptItems.Count + " items)");
                done(OfflineResult(op.plan));
                return;
            }
            if (!IsLive) { done(PlanFail("PLAN_NOT_OPEN", "That plan is no longer available")); return; }

            var items = new JArray();
            for (int i = 0; i < keptItems.Count; i++)
            {
                ManagerPlanItemView it = keptItems[i];
                var o = new JObject
                {
                    ["client_item_id"] = it.clientItemId, ["title"] = it.title, ["kind"] = it.kind, ["proposed_agent"] = it.proposedAgent,
                    ["depends_on"] = JArray.FromObject(it.dependsOn ?? new string[0]),
                    ["acceptance_criteria"] = JArray.FromObject(it.acceptanceCriteria ?? new string[0]),
                };
                if (!string.IsNullOrEmpty(it.requiredPermission)) o["required_permission"] = it.requiredPermission;
                items.Add(o);
            }
            var body = new JObject { ["revision"] = revision, ["items"] = items, ["idempotency_key"] = Key(idempotencyKey, "mgr-revise-", planId + "-r" + revision) };
            Send(UnityWebRequest.kHttpVerbPOST, "/v2/sessions/" + Sid + "/manager/plans/" + Uri.EscapeDataString(planId) + "/revise",
                body.ToString(Newtonsoft.Json.Formatting.None), planTimeoutSeconds, (status, resp, net) =>
                {
                    if (net != null) { done(PlanFail("UNREACHABLE", IsTimeout(net) ? "No answer in time. The plan is unchanged" : WallOfflineLogic.UnreachableMessage)); return; }
                    if (status >= 200 && status < 300)
                    {
                        WallParsedPlan p;
                        if (!WallOfflineLogic.TryParsePlan(resp, out p)) { done(PlanFail("BAD_REPLY", "The backend sent a plan this app could not read")); return; }
                        var titles = new Dictionary<string, string>();
                        for (int i = 0; i < p.items.Count; i++) titles[p.items[i].clientItemId] = p.items[i].title;
                        _liveTitles[p.planId] = titles;
                        done(PlanOk(p));
                        return;
                    }
                    var err = WallOfflineLogic.ParseError(status, resp);
                    done(PlanFail(err.code, WallOfflineLogic.FriendlyMessage(err)));
                });
        }

        // ------------------------------------------------------------------ manager: retry a failed card

        string OfflinePlanOf(string taskId)
        {
            for (int i = 0; i < _offlineCards.Count; i++) if (_offlineCards[i].taskId == taskId) return _offlineCards[i].planId;
            return null;
        }

        public void RetryPlanCard(string planId, string taskId, string idempotencyKey, Action<PlanRetryResult> done)
        {
            if (done == null) return;
            FindRefsOnce();
            string offlinePlan = OfflinePlanOf(taskId);
            if (offlinePlan != null && !IsLive)
            {
                Phase2DemoGateway demo = phase2 != null ? phase2.DemoGateway : null;
                string state; int rev;
                if (demo == null) { done(new PlanRetryResult { ok = false, definitive = true, code = "BOARD_NOT_READY", message = "The task board is not ready" }); return; }
                bool retried = demo.TryRetryPlanCard(taskId, out state, out rev);
                if (!retried && state != TaskStates.Queued && state != TaskStates.Running && state != TaskStates.Completed)
                {
                    done(new PlanRetryResult { ok = false, definitive = true, code = state == null ? "NOT_FOUND" : "NOT_RETRYABLE", state = state, revision = rev,
                                               message = state == null ? "That card is gone" : "Only a failed card can be retried (it is " + state + ")" });
                    return;
                }
                OfflinePlan op;
                if (retried && _offline.TryGetValue(offlinePlan, out op)) op.synthesized = false;     // unfinished again: the old summary is stale
                Debug.Log(Tag + "offline plan card " + taskId + " retry: retried=" + retried + " state=" + state);
                done(new PlanRetryResult { ok = true, definitive = true, retried = retried, state = state, revision = rev });
                return;
            }
            if (!IsLive) { done(new PlanRetryResult { ok = false, definitive = true, code = "NOT_FOUND", message = "That plan is no longer available" }); return; }

            var body = new JObject { ["idempotency_key"] = Key(idempotencyKey, "mgr-retry-", taskId) };
            string path = "/v2/sessions/" + Sid + "/manager/plans/" + Uri.EscapeDataString(planId ?? "") + "/tasks/" + Uri.EscapeDataString(taskId ?? "") + "/retry";
            Send(UnityWebRequest.kHttpVerbPOST, path, body.ToString(Newtonsoft.Json.Formatting.None), planTimeoutSeconds, (status, resp, net) =>
            {
                if (net != null) { done(new PlanRetryResult { ok = false, definitive = false, code = "UNREACHABLE", message = IsTimeout(net) ? "No answer in time. Press Retry again" : WallOfflineLogic.UnreachableMessage }); return; }
                if (status >= 200 && status < 300)
                {
                    bool retried; string state; int rev;
                    if (!WallOfflineLogic.TryParseRetry(resp, out retried, out state, out rev))
                    { done(new PlanRetryResult { ok = false, definitive = true, code = "BAD_REPLY", message = "The backend sent a reply this app could not read" }); return; }
                    done(new PlanRetryResult { ok = true, definitive = true, retried = retried, state = state, revision = rev });
                    return;
                }
                var err = WallOfflineLogic.ParseError(status, resp);
                done(new PlanRetryResult { ok = false, definitive = true, code = err.code, message = WallOfflineLogic.FriendlyMessage(err) });
            });
        }

        // ------------------------------------------------------------------ manager: plan status (read-only)

        public void GetPlanStatus(string planId, Action<WallParsedPlanStatus> done)
        {
            if (done == null) return;
            FindRefsOnce();
            OfflinePlan op;
            if (!string.IsNullOrEmpty(planId) && _offline.TryGetValue(planId, out op))
            {
                done(IsLive ? null : OfflineStatus(planId, op));
                return;
            }
            if (!IsLive || string.IsNullOrEmpty(planId)) { done(null); return; }
            Send(UnityWebRequest.kHttpVerbGET, "/v2/sessions/" + Sid + "/manager/plans/" + Uri.EscapeDataString(planId) + "/status", null, sideTimeoutSeconds,
                (status, resp, net) =>
                {
                    WallParsedPlanStatus s;
                    if (net == null && status >= 200 && status < 300 && WallOfflineLogic.TryParsePlanStatus(resp, out s)) done(s);
                    else done(null);
                });
        }

        /// <summary>The offline demo board's own picture of an offline plan, in the backend's status shape and words.</summary>
        WallParsedPlanStatus OfflineStatus(string planId, OfflinePlan op)
        {
            Phase2DemoGateway demo = phase2 != null ? phase2.DemoGateway : null;
            if (demo == null) return null;
            var s = new WallParsedPlanStatus { planId = planId, title = op.plan.title, planStatus = op.status, provenance = op.plan.provenance, sourceMode = "demo", settled = op.synthesized };
            if (op.synthesized) { s.synthesisArtifactId = op.summaryArtifactId; s.synthesisTaskId = op.summaryAnchor; s.synthesisMode = "scripted_demo"; }
            for (int i = 0; i < _offlineCards.Count; i++)
            {
                OfflineCard c = _offlineCards[i];
                if (c.planId != planId) continue;
                TaskCardV2 card = demo.PlanCardSnapshot(c.taskId);
                if (card == null) continue;
                var u = new PlanCardUpdate { taskId = card.TaskId, state = card.TaskState, revision = card.Revision, agentId = card.AgentId, title = card.Title,
                                             dependsOn = (card.Dependencies ?? new List<string>()).ToArray(), blockedReason = "", blockedBy = new string[0], failureReason = "" };
                if (card.TaskState == TaskStates.Blocked) u.blockedReason = OfflineBlockedReason(demo, card, out u.blockedBy);
                if (card.TaskState == TaskStates.Failed) u.failureReason = c.result ?? "";
                if (card.TaskState == TaskStates.Completed && !string.IsNullOrEmpty(c.result)) u.resultSummary = c.result;
                s.cards.Add(u);
            }
            return s;
        }

        // ------------------------------------------------------------------ manager: commit

        public void CommitPlan(string planId, int revision, List<string> approvedItemIds, List<string> authorizedDangerousItemIds,
                               string idempotencyKey, Action<ManagerCommitResult> done)
        {
            if (done == null) return;
            OfflinePlan op;
            if (!string.IsNullOrEmpty(planId) && _offline.TryGetValue(planId, out op)) { _ = CommitOfflineAsync(op, revision, approvedItemIds, authorizedDangerousItemIds, idempotencyKey, done); return; }
            if (!IsLive) { done(CommitFail("PLAN_NOT_OPEN", "That plan is no longer available")); return; }

            var body = new JObject
            {
                ["revision"] = revision,
                ["approved_item_ids"] = JArray.FromObject(approvedItemIds ?? new List<string>()),
                ["authorized_dangerous_item_ids"] = JArray.FromObject(authorizedDangerousItemIds ?? new List<string>()),
                ["idempotency_key"] = Key(idempotencyKey, "mgr-commit-", planId),
            };
            Dictionary<string, string> titles;
            _liveTitles.TryGetValue(planId, out titles);
            // One attempt only. A caller that retries must reuse the SAME idempotency key (the plan flow does).
            Send(UnityWebRequest.kHttpVerbPOST, "/v2/sessions/" + Sid + "/manager/plans/" + Uri.EscapeDataString(planId) + "/commit",
                body.ToString(Newtonsoft.Json.Formatting.None), planTimeoutSeconds, (status, resp, net) =>
                {
                    if (net != null) { done(CommitFail("UNREACHABLE", IsTimeout(net) ? "No answer in time. Nothing is confirmed; try again" : WallOfflineLogic.UnreachableMessage)); return; }
                    if (status >= 200 && status < 300)
                    {
                        WallParsedCommit c;
                        if (!WallOfflineLogic.TryParseCommit(resp, titles, out c)) { done(CommitFail("BAD_REPLY", "The backend confirmed a commit this app could not read")); return; }
                        done(new ManagerCommitResult { ok = true, revision = c.revision, tasks = c.tasks });
                        return;
                    }
                    var err = WallOfflineLogic.ParseError(status, resp);
                    done(CommitFail(err.code, WallOfflineLogic.FriendlyMessage(err)));
                });
        }

        static ManagerCommitResult CommitFail(string code, string message)
        {
            return new ManagerCommitResult { ok = false, code = code, message = message };
        }

        async Task CommitOfflineAsync(OfflinePlan op, int revision, List<string> approved, List<string> authorized, string key, Action<ManagerCommitResult> done)
        {
            ManagerCommitResult result;
            try { result = await RunOfflineCommit(op, revision, approved, authorized, key); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Debug.LogWarning(Tag + "offline commit failed: " + ex.Message); result = CommitFail("COMMIT_FAILED", "Could not create the tasks on the board"); }
            if (this == null) return;
            try { done(result); }
            catch (Exception ex) { Debug.LogError(Tag + "callback error: " + ex.Message); }
        }

        async Task<ManagerCommitResult> RunOfflineCommit(OfflinePlan op, int revision, List<string> approved, List<string> authorized, string key)
        {
            var plan = op.plan;
            if (key != null && key == op.commitKey && op.lastResult != null && op.status == "committed") return op.lastResult;   // idempotent replay
            if (revision != plan.revision) return CommitFail("REVISION_CONFLICT", "The plan changed (it is now revision " + plan.revision + "). Ask again.");
            if (op.status == "committed") return CommitFail("PLAN_ALREADY_COMMITTED", "That plan was already committed");
            if (op.status != "proposed") return CommitFail("PLAN_NOT_OPEN", "That plan was rejected");
            if (op.committing) return CommitFail("COMMIT_IN_PROGRESS", "Still creating the tasks");
            string code, msg;
            if (!WallOfflineLogic.ValidateCommit(plan.items, approved, authorized, out code, out msg)) return CommitFail(code, msg);
            var ordered = WallOfflineLogic.OrderForCreate(plan.items, approved);
            if (ordered.Count == 0) return CommitFail("PLAN_INVALID", "The plan has a dependency loop");

            FindRefsOnce();
            IPhase2Gateway board = phase2 != null ? phase2.Gateway : null;
            if (board == null || phase2 == null || !phase2.Ready) return CommitFail("BOARD_NOT_READY", "The task board is not ready, so nothing was created");

            op.committing = true;
            string baseKey = Key(key, "wall-commit-", plan.planId + "-r" + plan.revision);
            var ids = new Dictionary<string, string>();
            var result = new ManagerCommitResult { ok = false, revision = plan.revision };
            try
            {
                for (int i = 0; i < ordered.Count; i++)
                {
                    var it = ordered[i];
                    var deps = new List<string>();
                    for (int d = 0; d < it.dependsOn.Length; d++) deps.Add(ids[it.dependsOn[d]]);   // validated: all approved + created earlier
                    string failure = null;
                    TaskCardV2 card = null;
                    try
                    {
                        card = await board.CreateTaskAsync(new TaskCreateV2
                        {
                            Title = it.title,
                            AgentId = it.proposedAgent,
                            Priority = 1,
                            Dependencies = deps,
                            IdempotencyKey = baseKey + "-" + it.clientItemId,
                        }, _cts.Token);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { failure = ex.Message; }
                    if (failure != null || card == null || string.IsNullOrEmpty(card.TaskId))
                    {
                        result.code = "COMMIT_FAILED";
                        result.message = "Created " + result.tasks.Count + " of " + ordered.Count + " tasks"
                                       + (result.tasks.Count > 0 ? " (" + CreatedList(result.tasks) + ")" : "")
                                       + ". Could not create \"" + WallOfflineLogic.Clip(it.title, 40) + "\"" + (failure != null ? ": " + WallOfflineLogic.Clip(failure, 60) : "");
                        return result;
                    }
                    ids[it.clientItemId] = card.TaskId;
                    result.tasks.Add(new ManagerPrintJob { taskId = card.TaskId, clientItemId = it.clientItemId, agentId = it.proposedAgent, title = it.title });
                }
            }
            finally { op.committing = false; }

            result.ok = true;
            op.status = "committed";
            op.commitKey = key;
            op.lastResult = result;
            for (int i = 0; i < result.tasks.Count; i++)
                _offlineCards.Add(new OfflineCard { taskId = result.tasks[i].taskId, agentId = result.tasks[i].agentId,
                                                    title = result.tasks[i].title, objective = plan.objective, planId = plan.planId });
            Debug.Log(Tag + "offline plan " + plan.planId + " committed: " + result.tasks.Count + " task(s) on the demo board");
            return result;
        }

        static string CreatedList(List<ManagerPrintJob> tasks)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < tasks.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(tasks[i].clientItemId); }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ manager: reject

        public void RejectPlan(string planId, int revision, string reason, string idempotencyKey, Action<bool, string> done)
        {
            if (done == null) return;
            OfflinePlan op;
            if (!string.IsNullOrEmpty(planId) && _offline.TryGetValue(planId, out op))
            {
                if (op.status == "proposed") op.status = "rejected";
                done(true, null);
                return;
            }
            if (!IsLive) { done(true, null); return; }

            var body = new JObject
            {
                ["revision"] = revision,
                ["reason"] = string.IsNullOrEmpty(reason) ? "Rejected by the user" : WallOfflineLogic.Clip(reason, 200),
                ["idempotency_key"] = Key(idempotencyKey, "mgr-reject-", planId),
            };
            Send(UnityWebRequest.kHttpVerbPOST, "/v2/sessions/" + Sid + "/manager/plans/" + Uri.EscapeDataString(planId) + "/reject",
                body.ToString(Newtonsoft.Json.Formatting.None), planTimeoutSeconds, (status, resp, net) =>
                {
                    if (net != null) { done(false, WallOfflineLogic.UnreachableMessage); return; }
                    if (status >= 200 && status < 300) { done(true, null); return; }
                    done(false, WallOfflineLogic.FriendlyMessage(WallOfflineLogic.ParseError(status, resp)));
                });
        }
    }
}
