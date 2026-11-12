// =============================================================================
// MuseDesk · §13 EditMode tests · DORMANT real-type suite
// =============================================================================
//
// This file exercises the REAL runtime types (Phase2Reducer, DemoAgentGateway,
// SessionReducer) instead of mirrors — it is the suite with actual regression
// teeth. It is fenced off by default because an .asmdef test assembly cannot
// reference the predefined `Assembly-CSharp` where the MuseDesk runtime currently
// lives (see Phase2ContractSpecTests.cs header for the full explanation).
//
// ─── 3-STEP ACTIVATION ───
// 1. Wrap the runtime in an assembly definition, e.g. create
//    Assets/MuseDesk/MuseDesk.Runtime.asmdef  (name: "MuseDesk.Runtime") so that
//    MuseDesk.Phase2.*, MuseDesk.Networking and MuseDesk.Models compile into it.
//    (Everything under Assets/MuseDesk/ EXCEPT this Tests folder and any Editor
//    folders. Verify the project still compiles — Editor builder scripts in
//    AgentScripts/ are run via the pipeline and are unaffected.)
// 2. In MuseDesk.Tests.EditMode.asmdef add  "MuseDesk.Runtime"  to "references",
//    and add  "Newtonsoft.Json.dll"  to "precompiledReferences"
//    (the runtime DTOs use Newtonsoft JObject payloads).
// 3. Add  MUSEDESK_TESTS_RUNTIME_REF  to this test asmdef's "versionDefines" or to
//    Scripting Define Symbols (Editor platform). The suite below then compiles.
//
// Once active, the mirrors in Phase2ContractSpecTests.cs become redundant for the
// members they duplicate and can be deleted or kept as a plain contract record.
// =============================================================================

#if MUSEDESK_TESTS_RUNTIME_REF
using System.Threading;
using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MuseDesk.Phase2.Contracts;
using MuseDesk.Networking;
using MuseDesk.Models;

namespace MuseDesk.Tests.EditMode
{
    [Category("MuseDesk")]
    public class Phase2ReducerRuntimeTests
    {
        static EventEnvelopeV2 Evt(long seq, string type, string taskId = null,
            string agentId = null, string eventId = null, JObject payload = null)
            => new EventEnvelopeV2
            {
                EventId = eventId ?? $"e{seq}",
                Seq = seq,
                Type = type,
                TaskId = taskId,
                AgentId = agentId,
                Payload = payload ?? new JObject(),
            };

        static SnapshotV2 SnapshotWith(long lastSeq, params TaskCardV2[] tasks)
        {
            var s = new SnapshotV2 { SessionId = "ses-1", LastSeq = lastSeq, SourceMode = SourceModes.Live };
            s.Tasks = new List<TaskCardV2>(tasks);
            return s;
        }

        [Test]
        public void LoadSnapshot_hydrates_tasks_and_sets_last_seq()
        {
            var r = new Phase2Reducer();
            bool rebuilt = false;
            r.SnapshotLoaded += () => rebuilt = true;

            r.LoadSnapshot(SnapshotWith(7,
                new TaskCardV2 { TaskId = "t1", TaskState = TaskStates.Queued, Revision = 1 },
                new TaskCardV2 { TaskId = "t2", TaskState = TaskStates.Running, Revision = 2 }));

            Assert.AreEqual(7, r.LastAppliedSeq, "last_seq must come from the snapshot");
            Assert.AreEqual(2, r.Tasks.Count, "both snapshot tasks hydrate");
            Assert.AreEqual(SourceModes.Live, r.SourceMode);
            Assert.IsTrue(rebuilt, "SnapshotLoaded must fire so presenters rebuild");
        }

        [Test]
        public void Apply_in_order_assign_updates_task_and_raises_TaskChanged()
        {
            var r = new Phase2Reducer();
            r.LoadSnapshot(SnapshotWith(0, new TaskCardV2 { TaskId = "t1", TaskState = TaskStates.Queued }));
            TaskCardV2 changed = null;
            r.TaskChanged += c => changed = c;

            bool advanced = r.Apply(Evt(1, Phase2EventTypes.TaskAssigned, taskId: "t1", agentId: "developer"));

            Assert.IsTrue(advanced, "a strictly-newer seq advances state");
            Assert.AreEqual(1, r.LastAppliedSeq);
            Assert.AreEqual("developer", r.GetTask("t1").AgentId, "assign sets the agent");
            Assert.IsNotNull(changed, "TaskChanged must fire for the upserted card");
            Assert.AreEqual("t1", changed.TaskId);
        }

        [Test]
        public void Apply_duplicate_seq_is_dropped()
        {
            var r = new Phase2Reducer();
            r.LoadSnapshot(SnapshotWith(0, new TaskCardV2 { TaskId = "t1" }));
            Assert.IsTrue(r.Apply(Evt(1, Phase2EventTypes.TaskStarted, taskId: "t1")));

            bool advanced = r.Apply(Evt(1, Phase2EventTypes.TaskStarted, taskId: "t1", eventId: "dup"));

            Assert.IsFalse(advanced, "seq <= lastApplied is a duplicate and must not advance");
            Assert.AreEqual(1, r.LastAppliedSeq);
        }

        [Test]
        public void Apply_duplicate_event_id_at_new_seq_is_dropped()
        {
            var r = new Phase2Reducer();
            r.LoadSnapshot(SnapshotWith(0, new TaskCardV2 { TaskId = "t1" }));
            Assert.IsTrue(r.Apply(Evt(1, Phase2EventTypes.TaskStarted, taskId: "t1", eventId: "same")));

            bool advanced = r.Apply(Evt(2, Phase2EventTypes.TaskCompleted, taskId: "t1", eventId: "same"));

            Assert.IsFalse(advanced, "a re-sent event_id must be deduped even at a newer seq");
            Assert.AreEqual(1, r.LastAppliedSeq, "no state applied for a known event_id");
        }

        [Test]
        public void Apply_seq_gap_raises_ResyncNeeded_and_applies_nothing()
        {
            var r = new Phase2Reducer();
            r.LoadSnapshot(SnapshotWith(0, new TaskCardV2 { TaskId = "t1", TaskState = TaskStates.Queued }));
            Assert.IsTrue(r.Apply(Evt(1, Phase2EventTypes.TaskStarted, taskId: "t1")));

            long expected = -1, got = -1;
            r.ResyncNeeded += (e, g) => { expected = e; got = g; };

            bool advanced = r.Apply(Evt(3, Phase2EventTypes.TaskCompleted, taskId: "t1"));

            Assert.IsFalse(advanced, "a gapped event must not be applied speculatively");
            Assert.AreEqual(2, expected, "expected seq is lastApplied+1");
            Assert.AreEqual(3, got, "got seq is the gapped event's seq");
            Assert.AreEqual(1, r.LastAppliedSeq, "state stays at the last contiguous seq");
            Assert.AreNotEqual(TaskStates.Completed, r.GetTask("t1").TaskState);
        }

        [Test]
        public void Apply_focus_changed_updates_focus_and_raises_event()
        {
            var r = new Phase2Reducer();
            r.LoadSnapshot(SnapshotWith(0));
            FocusSnapshotV2 fired = null;
            r.FocusChanged += f => fired = f;

            r.Apply(Evt(1, Phase2EventTypes.FocusChanged, payload: new JObject { ["mode"] = FocusModes.Focus }));

            Assert.AreEqual(FocusModes.Focus, r.Focus.Mode, "focus mode must update from the event");
            Assert.IsNotNull(fired, "FocusChanged must fire");
            Assert.AreEqual(FocusModes.Focus, fired.Mode);
        }

        [Test]
        public void Apply_priority_changed_adopts_payload_priority()
        {
            var r = new Phase2Reducer();
            r.LoadSnapshot(SnapshotWith(0, new TaskCardV2 { TaskId = "t1", Priority = 1 }));

            r.Apply(Evt(1, Phase2EventTypes.TaskPriorityChanged, taskId: "t1",
                payload: new JObject { ["priority"] = 3 }));

            Assert.AreEqual(3, r.GetTask("t1").Priority, "priority delta must be adopted from payload");
        }
    }

    [Category("MuseDesk")]
    public class DemoAgentGatewayRuntimeTests
    {
        static DemoFixture MakeFixture()
        {
            var fx = new DemoFixture { Mode = "demo", SourceMode = "demo", Title = "Auth demo" };
            fx.Agents.Add(new DemoAgent { AgentId = "researcher", Role = "researcher", InitialState = "idle" });
            fx.Agents.Add(new DemoAgent { AgentId = "developer", Role = "developer", InitialState = "idle" });
            fx.Agents.Add(new DemoAgent { AgentId = "tester", Role = "tester", InitialState = "idle" });
            fx.Decision = new DemoDecision
            {
                DecisionId = "dec-1", Title = "Use OAuth?", Summary = "s", Rationale = "r",
                RiskLevel = "low", Priority = "normal",
                AllowedActions = new List<string> { "approve", "reject" },
            };
            fx.Branches["approve"] = new DemoBranch { DeveloperState = "coding", Result = "OAuth done", TestsPassed = 5 };
            fx.Branches["reject"] = new DemoBranch { DeveloperState = "waiting", Result = "No change", TestsPassed = 0 };
            return fx;
        }

        [Test]
        public void Respond_is_idempotent_second_call_applies_nothing()
        {
            var gw = new DemoAgentGateway(MakeFixture(), stepSeconds: 0f);
            // Drive to a pending decision (all steps are no-ops at stepSeconds:0, so this completes inline).
            gw.StartTaskAsync("auth", "idem-start", CancellationToken.None).GetAwaiter().GetResult();

            var first = gw.RespondToDecisionAsync("dec-1", "approve", "idem-1", null, CancellationToken.None)
                          .GetAwaiter().GetResult();
            var second = gw.RespondToDecisionAsync("dec-1", "approve", "idem-1", null, CancellationToken.None)
                           .GetAwaiter().GetResult();

            Assert.IsTrue(first.Applied, "first resolution applies");
            Assert.AreEqual("approved", first.State);
            Assert.IsFalse(second.Applied, "a repeated resolution must return canonical and apply nothing");
            Assert.AreEqual("approve", second.Action);
            Assert.AreEqual("approved", second.State);
        }

        [Test]
        public void Every_emitted_event_is_stamped_source_mode_demo()
        {
            var gw = new DemoAgentGateway(MakeFixture(), stepSeconds: 0f);
            var seen = new List<EventEnvelope>();
            gw.EventReceived += e => seen.Add(e);

            gw.StartSessionAsync(CancellationToken.None).GetAwaiter().GetResult();
            gw.StartTaskAsync("auth", "idem-start", CancellationToken.None).GetAwaiter().GetResult();
            gw.RespondToDecisionAsync("dec-1", "approve", "idem-1", null, CancellationToken.None)
              .GetAwaiter().GetResult();

            Assert.IsNotEmpty(seen, "the demo gateway must emit events");
            foreach (var e in seen)
                Assert.AreEqual("demo", e.PayloadString("source_mode"),
                    $"invariant #1: event '{e.Type}' (seq {e.Seq}) must be stamped source_mode=demo");
        }

        [Test]
        public void Reject_branch_resolves_to_rejected()
        {
            var gw = new DemoAgentGateway(MakeFixture(), stepSeconds: 0f);
            gw.StartTaskAsync("auth", "idem-start", CancellationToken.None).GetAwaiter().GetResult();

            var res = gw.RespondToDecisionAsync("dec-1", "reject", "idem-r", null, CancellationToken.None)
                        .GetAwaiter().GetResult();

            Assert.AreEqual("rejected", res.State);
            Assert.IsTrue(res.Applied);
        }
    }

    [Category("MuseDesk")]
    public class SessionReducerRuntimeTests
    {
        static EventEnvelope Evt(long seq, string type, string agentId = null)
            => new EventEnvelope { EventId = $"e{seq}", Seq = seq, Type = type, AgentId = agentId };

        [Test]
        public void Apply_advances_only_on_strictly_newer_seq()
        {
            var r = new SessionReducer();
            int applied = 0;
            r.Applied += _ => applied++;

            Assert.IsTrue(r.Apply(Evt(1, "agent.status_changed", "researcher")), "seq 1 advances from 0");
            Assert.IsFalse(r.Apply(Evt(1, "agent.status_changed", "researcher")), "seq 1 again is a dup");
            Assert.IsTrue(r.Apply(Evt(2, "agent.status_changed", "developer")), "seq 2 advances");

            Assert.AreEqual(2, r.LastSeq, "LastSeq tracks the highest contiguous applied seq");
            Assert.AreEqual(2, applied, "Applied fires once per genuinely-new event");
        }
    }
}
#endif
