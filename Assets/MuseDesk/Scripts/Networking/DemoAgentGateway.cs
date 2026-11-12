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
    /// </summary>
    public class DemoAgentGateway : IAgentGateway
    {
        public string ModeLabel => "DEMO · Simulated work";
        public event Action<EventEnvelope> EventReceived;

        const string SessionId = "ses-demo-001";
        readonly DemoFixture _fx;
        readonly float _step;
        long _seq;
        string _resolvedAction;
        string _decisionId;
        int _cycle;

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
            foreach (var a in _fx.Agents)
                snap.Agents.Add(new AgentView { AgentId = a.AgentId, Role = a.Role,
                    DisplayName = a.Role, State = a.InitialState });
            Emit("session.ready", new JObject {
                ["mode"] = "demo", ["source_mode"] = "demo",
                ["agents"] = JArray.FromObject(snap.Agents)
            });
            return Task.FromResult(snap);
        }

        public Task<SessionSnapshot> GetSnapshotAsync(CancellationToken ct)
            => Task.FromResult(new SessionSnapshot { SessionId = SessionId, Mode = "demo", LastSeq = _seq });

        public async Task<TaskView> StartTaskAsync(string templateId, string idempotencyKey,
                                                   CancellationToken ct)
        {
            // A new cycle (DemoDirector loop / restart) is a NEW decision: clear the previous answer and
            // give it its own id, otherwise the second run could be neither stamped nor resolved.
            _cycle++;
            _resolvedAction = null;
            _decisionId = _cycle == 1 ? _fx.Decision.DecisionId : $"{_fx.Decision.DecisionId}-c{_cycle}";

            await Step(ct);
            EmitAgent("researcher", "researching", "Comparing OAuth vs local passwords");
            EmitAgent("developer", "coding", "Scaffolding login UI");
            EmitAgent("tester", "testing", "Running baseline tests");
            await Step(ct);
            EmitAgent("tester", "complete", "Baseline tests passed (3/3)");
            EmitAgent("developer", "waiting", "Login UI stub ready; awaiting auth decision");
            await Step(ct);

            var d = _fx.Decision;
            EmitAgent("researcher", "waiting", "Needs a decision");
            Emit("decision.requested", new JObject {
                ["title"] = d.Title, ["summary"] = d.Summary, ["rationale"] = d.Rationale,
                ["recommendation"] = "approve", ["risk_level"] = d.RiskLevel,
                ["priority"] = d.Priority, ["source_mode"] = "demo",
                ["allowed_actions"] = JArray.FromObject(d.AllowedActions)
            }, agentId: "researcher", decisionId: _decisionId, taskId: "task-demo-research");

            return new TaskView { TaskId = "task-demo-auth", Status = "running", Template = templateId };
        }

        public async Task<DecisionResolutionDto> RespondToDecisionAsync(string decisionId, string action,
            string idempotencyKey, string comment, CancellationToken ct)
        {
            // Idempotent: a second resolution returns the canonical first, applies nothing new.
            if (_resolvedAction != null)
                return new DecisionResolutionDto { DecisionId = decisionId,
                    State = StateFor(_resolvedAction), Action = _resolvedAction, Applied = false };

            _resolvedAction = action;
            var state = StateFor(action);
            Emit("decision.resolved", new JObject {
                ["state"] = state, ["action"] = action, ["source_mode"] = "demo",
                ["resolved_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            }, decisionId: decisionId);

            _ = PlayBranchAsync(action, ct);
            return new DecisionResolutionDto { DecisionId = decisionId, State = state,
                Action = action, Applied = true };
        }

        async Task PlayBranchAsync(string action, CancellationToken ct)
        {
            _fx.Branches.TryGetValue(action, out var branch);
            EmitAgent("researcher", "complete", "Decision delivered");
            await Step(ct);
            if (action == "approve")
            {
                EmitAgent("developer", "coding", "Implementing OAuth scaffold");
                await Step(ct);
                int passed = branch?.TestsPassed ?? 5;
                Emit("artifact.created", new JObject {
                    ["artifact_id"] = "art-demo-1", ["artifact_type"] = "code_change",
                    ["summary"] = branch?.Result ?? "Simulated OAuth scaffold complete",
                    ["source_mode"] = "demo", ["tests_passed"] = passed, ["tests_failed"] = 0,
                    ["files_changed"] = new JArray("app/auth/oauth_login.py", "tests/test_oauth_login.py")
                }, taskId: "task-demo-oauth", agentId: "developer");
                EmitAgent("developer", "complete", "OAuth scaffold handed to the tester");
                // The scripted tester verifies the artifact before the task is reported done, in the
                // same order the live pipeline uses (develop → test → complete).
                EmitAgent("tester", "testing", $"Running {passed} OAuth tests");
                await Step(ct);
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
        }

        public Task<QuestionAnswerDto> AskAsync(string agentId, string decisionId, string question,
            CancellationToken ct)
        {
            return Task.FromResult(new QuestionAnswerDto {
                Answer = _fx.QuestionAnswer?.Answer ?? "This is a simulated answer in offline demo mode.",
                SourceMode = "demo"
            });
        }

        // ── helpers ──
        static string StateFor(string action) => action switch
        {
            "approve" => "approved",
            "reject" => "rejected",
            _ => "changes_requested"
        };

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
