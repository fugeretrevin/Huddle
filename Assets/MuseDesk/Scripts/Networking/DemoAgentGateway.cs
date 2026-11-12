using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MuseDesk.Models;

namespace MuseDesk.Networking
{
    /// <summary>
    /// Fully self-contained offline gateway for the judged demo. Plays the SAME event
    /// shapes as the live backend from a bundled fixture, so the reducer and presentation
    /// are identical in both modes. Every event is honestly stamped source_mode="demo".
    /// No PC, no network, no provider keys. Started from the Unity main thread so awaited
    /// delays resume there and EventReceived is raised on the main thread.
    ///
    /// One scripted run at a time. A run is "in progress" from StartTaskAsync until it delivers
    /// its work (approve), ends without building anything (reject / request changes) or is reset.
    /// Every run has a generation number: <see cref="ResetRun"/> bumps it and cancels the pending
    /// delays, so nothing from an abandoned run can be emitted afterwards.
    /// </summary>
    public class DemoAgentGateway : IAgentGateway
    {
        public string ModeLabel => "DEMO · Simulated work";
        public event Action<EventEnvelope> EventReceived;

        /// <summary>Raised whenever <see cref="RunInProgress"/> may have changed (start, end, reset).</summary>
        public event Action RunStateChanged;

        /// <summary>True from the start of a scripted run until it completes, is rejected or is reset.</summary>
        public bool RunInProgress { get; private set; }

        /// <summary>How many scripted runs have been started on this gateway.</summary>
        public int RunsStarted => _cycle;

        /// <summary>The board card the current run was started for, or null (a run with no card).</summary>
        public string BoardTaskId => _boardTaskId;

        /// <summary>True after <see cref="Shutdown"/>: this gateway will never start or emit again.</summary>
        public bool IsClosed => _closed;

        public const string ResearchProvenance = "template";
        public const string ResearchProvenanceLabel = "Template recommendation. Not live AI research.";

        const string SessionId = "ses-demo-001";
        readonly DemoFixture _fx;
        readonly float _step;
        long _seq;
        string _resolvedAction;
        string _decisionId;
        bool _decisionRequested;
        int _cycle;
        int _generation;                 // bumped by every start and every reset
        CancellationTokenSource _runCts; // cancels the current run's pending delays
        string _boardTaskId;
        bool _closed;

        public DemoAgentGateway(DemoFixture fixture, float stepSeconds = 1.2f)
        {
            _fx = fixture;
            _step = stepSeconds;
        }

        public static DemoFixture LoadFixture(string resourceName = "offline_demo_fixture")
        {
            var ta = Resources.Load<TextAsset>(resourceName);
            if (ta == null) { Debug.LogError($"[MuseDesk] demo fixture '{resourceName}' not found"); return null; }
            return Newtonsoft.Json.JsonConvert.DeserializeObject<DemoFixture>(ta.text);
        }

        public Task<SessionSnapshot> StartSessionAsync(CancellationToken ct)
        {
            var snap = new SessionSnapshot { SessionId = SessionId, Mode = "demo", LastSeq = 0 };
            snap.Agents = AgentViews();
            Emit("session.ready", new JObject {
                ["mode"] = "demo", ["source_mode"] = "demo",
                ["agents"] = JArray.FromObject(snap.Agents)
            });
            return Task.FromResult(snap);
        }

        public Task<SessionSnapshot> GetSnapshotAsync(CancellationToken ct)
            => Task.FromResult(new SessionSnapshot { SessionId = SessionId, Mode = "demo", LastSeq = _seq });

        public Task<TaskView> StartTaskAsync(string templateId, string idempotencyKey, CancellationToken ct)
            => StartRunAsync(templateId, idempotencyKey, null, ct);

        /// <summary>
        /// Start the scripted run, optionally on behalf of a task-board card. When
        /// <paramref name="boardTaskId"/> is set every event of the run carries it as the additive
        /// payload field board_task_id (same as the live backend). Refused while a run is in progress.
        /// </summary>
        public async Task<TaskView> StartRunAsync(string templateId, string idempotencyKey,
                                                  string boardTaskId, CancellationToken ct)
        {
            if (_closed) throw new InvalidOperationException("the scripted demo session is closed");
            if (RunInProgress)
                throw new InvalidOperationException("RUN_IN_PROGRESS: a scripted run is already in progress");

            // A new cycle (DemoDirector loop / restart) is a NEW decision: clear the previous answer and
            // give it its own id, otherwise the second run could be neither stamped nor resolved.
            _cycle++;
            int gen = ++_generation;
            _resolvedAction = null;
            _decisionRequested = false;
            _decisionId = _cycle == 1 ? _fx.Decision.DecisionId : $"{_fx.Decision.DecisionId}-c{_cycle}";
            _boardTaskId = string.IsNullOrEmpty(boardTaskId) ? null : boardTaskId;
            CancelRunDelays();
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var runCt = _runCts.Token;
            SetRunInProgress(true);

            var view = new TaskView { TaskId = "task-demo-auth", Status = "running", Template = templateId };
            try
            {
                await Step(runCt);
                if (gen != _generation) return Cancelled(view);
                // Same wording as the backend: the recommendation is a template, not live research.
                EmitAgent("researcher", "researching", "Preparing a template recommendation");
                EmitAgent("developer", "coding", "Scaffolding login UI");
                EmitAgent("tester", "testing", "Running baseline tests");
                await Step(runCt);
                if (gen != _generation) return Cancelled(view);
                EmitAgent("tester", "complete", "Baseline tests passed (3/3)");
                EmitAgent("developer", "waiting", "Login UI stub ready; awaiting auth decision");
                await Step(runCt);
                if (gen != _generation) return Cancelled(view);

                var d = _fx.Decision;
                EmitAgent("researcher", "waiting", "Needs a decision");
                _decisionRequested = true;
                Emit("decision.requested", new JObject {
                    ["title"] = d.Title, ["summary"] = d.Summary, ["rationale"] = d.Rationale,
                    ["recommendation"] = "approve", ["risk_level"] = d.RiskLevel,
                    ["priority"] = d.Priority, ["source_mode"] = "demo",
                    ["research_provenance"] = ResearchProvenance,
                    ["research_provenance_label"] = ResearchProvenanceLabel,
                    ["allowed_actions"] = JArray.FromObject(d.AllowedActions)
                }, agentId: "researcher", decisionId: _decisionId, taskId: "task-demo-research");
            }
            catch (OperationCanceledException)
            {
                // Reset (or teardown) while a delay was pending: the run is over, nothing more is emitted.
                return Cancelled(view);
            }
            return view;
        }

        static TaskView Cancelled(TaskView view)
        {
            view.Status = "cancelled";
            return view;
        }

        public Task<DecisionResolutionDto> RespondToDecisionAsync(string decisionId, string action,
            string idempotencyKey, string comment, CancellationToken ct)
        {
            // A decision from a run that was reset (or one that was never requested) is not answerable:
            // nothing is applied and nothing is emitted.
            if (!_decisionRequested || string.IsNullOrEmpty(_decisionId) || decisionId != _decisionId)
                return Task.FromResult(new DecisionResolutionDto { DecisionId = decisionId,
                    State = "expired", Action = action, Applied = false });

            // Idempotent: a second resolution returns the canonical first, applies nothing new.
            if (_resolvedAction != null)
                return Task.FromResult(new DecisionResolutionDto { DecisionId = decisionId,
                    State = StateFor(_resolvedAction), Action = _resolvedAction, Applied = false });

            _resolvedAction = action;
            var state = StateFor(action);
            int gen = _generation;
            Emit("decision.resolved", new JObject {
                ["state"] = state, ["action"] = action, ["source_mode"] = "demo",
                ["resolved_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            }, decisionId: decisionId);

            _ = PlayBranchAsync(action, gen);
            return Task.FromResult(new DecisionResolutionDto { DecisionId = decisionId, State = state,
                Action = action, Applied = true });
        }

        async Task PlayBranchAsync(string action, int gen)
        {
            try
            {
                if (gen != _generation) return;   // a handler of decision.resolved reset the demo
                var runCt = _runCts != null ? _runCts.Token : CancellationToken.None;
                _fx.Branches.TryGetValue(action, out var branch);
                EmitAgent("researcher", "complete", "Decision delivered");
                await Step(runCt);
                if (gen != _generation) return;
                if (action == "approve")
                {
                    EmitAgent("developer", "coding", "Implementing OAuth scaffold");
                    await Step(runCt);
                    if (gen != _generation) return;
                    int passed = branch?.TestsPassed ?? 5;
                    Emit("artifact.created", new JObject {
                        ["artifact_id"] = "art-demo-1", ["artifact_type"] = "code_change",
                        ["summary"] = branch?.Result ?? "Simulated OAuth scaffold complete",
                        ["source_mode"] = "demo", ["tests_passed"] = passed, ["tests_failed"] = 0,
                        ["files_changed"] = new JArray("app/auth/oauth_login.py", "tests/test_oauth_login.py")
                    }, taskId: "task-demo-oauth", agentId: "developer");
                    if (gen != _generation) return;
                    EmitAgent("developer", "complete", "OAuth scaffold handed to the tester");
                    // The scripted tester verifies the artifact before the task is reported done, in the
                    // same order the live pipeline uses (develop → test → complete).
                    EmitAgent("tester", "testing", $"Running {passed} OAuth tests");
                    await Step(runCt);
                    if (gen != _generation) return;
                    EmitAgent("tester", "complete", $"{passed}/{passed} tests passed");
                    Emit("task.completed", new JObject {
                        ["task_id"] = "task-demo-oauth", ["source_mode"] = "demo",
                        ["artifact_id"] = "art-demo-1",
                        ["result_summary"] = branch?.Result ?? "Simulated OAuth scaffold complete"
                    }, taskId: "task-demo-oauth", agentId: "developer");
                }
                else
                {
                    // Rejected / changes requested: nothing was built, so NO task.completed is emitted —
                    // a completion here would light the shelf, the PASSED lamp and the completion chime
                    // for work that never happened.
                    EmitAgent("developer", "waiting", branch?.Result ?? "No OAuth changes made");
                }
                if (gen != _generation) return;
                _boardTaskId = null;
                SetRunInProgress(false);
            }
            catch (OperationCanceledException) { /* reset or teardown mid-branch */ }
            catch (Exception ex) { Debug.LogError($"[MuseDesk] scripted demo branch error: {ex}"); }
        }

        public Task<QuestionAnswerDto> AskAsync(string agentId, string decisionId, string question,
            CancellationToken ct)
        {
            return Task.FromResult(new QuestionAnswerDto {
                Answer = _fx.QuestionAnswer?.Answer ?? "This is a simulated answer in offline demo mode.",
                SourceMode = "demo"
            });
        }

        /// <summary>
        /// Stop the current scripted run (if any) and return the scripted session to its start state.
        /// Pending delays are cancelled and the run generation is bumped, so no event of the old run
        /// can arrive afterwards; its decision can no longer be answered. Emits session.reset (the
        /// reducer raises SessionReset so views clear), then session.ready and an idle status per
        /// agent so the office visibly returns to rest. Safe to call when nothing is running.
        /// </summary>
        public void ResetRun(string reason = "demo_reset")
        {
            if (_closed) return;
            _generation++;
            CancelRunDelays();
            _resolvedAction = null;
            _decisionId = null;
            _decisionRequested = false;
            _boardTaskId = null;
            RunInProgress = false;

            Emit("session.reset", new JObject {
                ["mode"] = "demo", ["source_mode"] = "demo", ["reason"] = reason
            });
            var agents = AgentViews();
            Emit("session.ready", new JObject {
                ["mode"] = "demo", ["source_mode"] = "demo",
                ["agents"] = JArray.FromObject(agents)
            });
            foreach (var a in agents)
                Emit("agent.status_changed", new JObject {
                    ["state"] = a.State, ["progress_message"] = "Ready", ["source_mode"] = "demo"
                }, agentId: a.AgentId);

            RaiseRunStateChanged();
        }

        /// <summary>Silently stop for good (mode switch to live, scene teardown): cancels the run,
        /// emits nothing, and refuses any later start.</summary>
        public void Shutdown()
        {
            _closed = true;
            _generation++;
            CancelRunDelays();
            _decisionId = null;
            _decisionRequested = false;
            _boardTaskId = null;
            RunInProgress = false;
        }

        // ── helpers ──
        static string StateFor(string action) => action switch
        {
            "approve" => "approved",
            "reject" => "rejected",
            _ => "changes_requested"
        };

        List<AgentView> AgentViews()
        {
            var list = new List<AgentView>();
            if (_fx != null && _fx.Agents != null)
                foreach (var a in _fx.Agents)
                    list.Add(new AgentView { AgentId = a.AgentId, Role = a.Role,
                        DisplayName = a.Role, State = a.InitialState });
            return list;
        }

        void SetRunInProgress(bool value)
        {
            if (RunInProgress == value) return;
            RunInProgress = value;
            RaiseRunStateChanged();
        }

        void RaiseRunStateChanged()
        {
            try { RunStateChanged?.Invoke(); }
            catch (Exception ex) { Debug.LogError($"[MuseDesk] run-state listener error: {ex.Message}"); }
        }

        void CancelRunDelays()
        {
            var cts = _runCts;
            _runCts = null;
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }

        async Task Step(CancellationToken ct)
        {
            if (_step > 0f) await Task.Delay(TimeSpan.FromSeconds(_step), ct);
        }

        void EmitAgent(string agentId, string state, string progress)
            => Emit("agent.status_changed", new JObject {
                ["state"] = state, ["task_state"] = "running",
                ["progress_message"] = progress, ["source_mode"] = "demo"
            }, agentId: agentId);

        void Emit(string type, JObject payload, string agentId = null,
                  string decisionId = null, string taskId = null)
        {
            if (_closed) return;
            // Additive: events of a run started from a board card name that card.
            if (_boardTaskId != null && payload != null) payload["board_task_id"] = _boardTaskId;
            _seq++;
            var env = new EventEnvelope {
                EventId = $"evt-demo-{_seq:D4}", Seq = _seq, SessionId = SessionId,
                OccurredAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Type = type, AgentId = agentId, DecisionId = decisionId, TaskId = taskId,
                Payload = payload
            };
            EventReceived?.Invoke(env);
        }
    }
}
