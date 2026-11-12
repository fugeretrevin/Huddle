using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using MuseDesk.Models;
using MuseDesk.Networking;
using MuseDesk.Phase2.Contracts;
using MuseDesk.Voice;

/// <summary>
/// Real-type regression checks for the MuseDesk runtime (reducers, demo gateways, voice matching).
/// The NUnit suite in Assets/MuseDesk/Tests cannot reference Assembly-CSharp, so these run through
/// the pipeline instead:
///   unity command script run --file AgentScripts/RuntimeSmokeTests.cs --entry RuntimeSmokeTests.Run
/// Returns a report; throws (non-zero exit) if any check fails. Touches no scene or asset.
/// </summary>
public static class RuntimeSmokeTests
{
    static readonly List<string> Failures = new List<string>();
    static int _checks;

    static void Check(bool ok, string what)
    {
        _checks++;
        if (!ok) Failures.Add(what);
    }

    static DemoFixture Fixture()
    {
        var fx = new DemoFixture { Mode = "demo", SourceMode = "demo", Title = "Auth demo" };
        foreach (var id in new[] { "researcher", "developer", "tester" })
            fx.Agents.Add(new DemoAgent { AgentId = id, Role = id, InitialState = "idle" });
        fx.Decision = new DemoDecision
        {
            DecisionId = "dec-1", Title = "Use OAuth?", Summary = "s", Rationale = "r",
            RiskLevel = "low", Priority = "blocking",
            AllowedActions = new List<string> { "approve", "reject", "request_changes" },
        };
        fx.Branches["approve"] = new DemoBranch { DeveloperState = "coding", Result = "OAuth done", TestsPassed = 5 };
        fx.Branches["reject"] = new DemoBranch { DeveloperState = "waiting", Result = "No change", TestsPassed = 0 };
        return fx;
    }

    static List<EventEnvelope> RunDemo(string action, out DemoAgentGateway gw)
    {
        gw = new DemoAgentGateway(Fixture(), stepSeconds: 0f);
        var seen = new List<EventEnvelope>();
        gw.EventReceived += e => seen.Add(e);
        gw.StartSessionAsync(CancellationToken.None).GetAwaiter().GetResult();
        gw.StartTaskAsync("auth", "k-start", CancellationToken.None).GetAwaiter().GetResult();
        var pending = seen.Last(e => e.Type == EventTypes.DecisionRequested).DecisionId;
        gw.RespondToDecisionAsync(pending, action, "k-resp", null, CancellationToken.None).GetAwaiter().GetResult();
        return seen;
    }

    static void DemoGateway()
    {
        // Reject must not report finished work.
        var rejected = RunDemo("reject", out _);
        Check(rejected.All(e => e.Type != EventTypes.TaskCompleted), "demo reject emits no task.completed");
        Check(rejected.All(e => e.Type != EventTypes.ArtifactCreated), "demo reject emits no artifact");
        Check(rejected.Any(e => e.Type == EventTypes.DecisionResolved && e.PayloadString("state") == "rejected"),
            "demo reject resolves to rejected");

        var changes = RunDemo("request_changes", out _);
        Check(changes.All(e => e.Type != EventTypes.TaskCompleted), "demo request_changes emits no task.completed");

        // Approve: develop -> artifact -> tester runs -> completed (with the artifact reference).
        var approved = RunDemo("approve", out var gw);
        int iArtifact = approved.FindIndex(e => e.Type == EventTypes.ArtifactCreated);
        int iDone = approved.FindIndex(e => e.Type == EventTypes.TaskCompleted);
        int iTesting = approved.FindLastIndex(e => e.Type == EventTypes.AgentStatusChanged && e.AgentId == "tester"
                                                   && e.PayloadString("state") == "testing");
        int iTesterDone = approved.FindLastIndex(e => e.Type == EventTypes.AgentStatusChanged && e.AgentId == "tester"
                                                      && e.PayloadString("state") == "complete");
        Check(iArtifact >= 0 && iDone > iArtifact, "demo approve: artifact precedes task.completed");
        Check(iTesting > iArtifact && iTesterDone > iTesting && iDone > iTesterDone,
            "demo approve: tester tests the artifact before completion");
        Check(iDone >= 0 && approved[iDone].PayloadString("artifact_id") == "art-demo-1",
            "demo approve: task.completed references the artifact");
        Check(approved.All(e => e.PayloadString("source_mode") == "demo"), "every demo event is stamped demo");
        for (int i = 1; i < approved.Count; i++)
            if (approved[i].Seq != approved[i - 1].Seq + 1) { Check(false, "demo seq is dense"); break; }

        // A second cycle is a new, resolvable decision.
        var seen = new List<EventEnvelope>();
        gw.EventReceived += e => seen.Add(e);
        gw.StartTaskAsync("auth", "k-start-2", CancellationToken.None).GetAwaiter().GetResult();
        var second = seen.LastOrDefault(e => e.Type == EventTypes.DecisionRequested);
        Check(second != null && second.DecisionId != "dec-1", "second demo cycle raises a new decision id");
        if (second != null)
        {
            var res = gw.RespondToDecisionAsync(second.DecisionId, "reject", "k-resp-2", null, CancellationToken.None)
                        .GetAwaiter().GetResult();
            Check(res.Applied && res.State == "rejected", "second demo cycle can be resolved independently");
        }
    }

    static EventEnvelope Ev(long seq, string type, string agent = null, string task = null,
                            string decision = null, JObject payload = null)
        => new EventEnvelope { EventId = "e" + seq, Seq = seq, Type = type, AgentId = agent, TaskId = task,
                               DecisionId = decision, Payload = payload ?? new JObject() };

    static void Session()
    {
        var r = new SessionReducer();
        int completed = 0, delivered = 0;
        AgentView last = null;
        r.TaskCompleted += _ => completed++;
        r.WorkDelivered += _ => delivered++;
        r.AgentChanged += a => last = a;

        r.Apply(Ev(1, EventTypes.AgentStatusChanged, "tester",
            payload: new JObject { ["state"] = "testing", ["progress_message"] = "Running baseline tests" }));
        Check(last != null && last.State == "testing" && last.Progress == "Running baseline tests",
            "reducer carries the agent's progress message");

        r.Apply(Ev(2, EventTypes.TaskCompleted, "tester", "t-base", payload: new JObject { ["task_id"] = "t-base" }));
        Check(completed == 1 && delivered == 0, "a completion without an artifact is not delivered work");
        r.Apply(Ev(3, EventTypes.TaskCompleted, "developer", "t-oauth",
            payload: new JObject { ["task_id"] = "t-oauth", ["artifact_id"] = "art-1" }));
        Check(completed == 2 && delivered == 1, "a completion with an artifact is delivered work");
        Check(!r.Apply(Ev(3, EventTypes.TaskCompleted, "developer", "t-oauth",
            payload: new JObject { ["artifact_id"] = "art-1" })) && delivered == 1, "a replayed event delivers nothing twice");

        // Snapshot projection for restart / resume.
        var r2 = new SessionReducer();
        int loaded = 0;
        r2.SnapshotLoaded += () => loaded++;
        var snap = new SessionSnapshot { SessionId = "s", LastSeq = 9 };
        snap.Agents.Add(new AgentView { AgentId = "developer", State = "coding" });
        snap.Decisions.Add(new DecisionView { DecisionId = "d-old", State = "approved" });
        snap.Decisions.Add(new DecisionView { DecisionId = "d-wait", State = "requested", Summary = "Use OAuth?" });
        r2.LoadSnapshot(snap);
        Check(loaded == 1, "SnapshotLoaded fires");
        Check(r2.FirstPendingDecision()?.DecisionId == "d-wait", "the pending decision is found in the snapshot");
        r2.Apply(Ev(10, EventTypes.DecisionResolved, decision: "d-wait", payload: new JObject { ["state"] = "approved" }));
        Check(r2.FirstPendingDecision() == null, "a resolved decision is no longer pending");
        Check(!r2.Apply(Ev(9, EventTypes.AgentStatusChanged, "developer")), "events at or below the snapshot seq are ignored");
    }

    static void Board()
    {
        var gw = new Phase2DemoGateway(null, 0f);
        var reducer = new Phase2Reducer();
        reducer.LoadSnapshot(gw.GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult());
        gw.EventReceived += e => reducer.Apply(e);

        var card = gw.CreateTaskAsync(new TaskCreateV2 { Title = "Write the login tests", Priority = 2,
            IdempotencyKey = "k-create-0001" }, CancellationToken.None).GetAwaiter().GetResult();
        var shown = reducer.GetTask(card.TaskId);
        Check(shown != null && shown.Title == "Write the login tests", "a created task's card carries its title");
        Check(shown != null && shown.Priority == 2, "a created task's card carries its priority");
        Check(shown != null && shown.TaskState == TaskStates.Queued, "a created task starts queued");

        int before = reducer.Tasks.Count;
        gw.CreateTaskAsync(new TaskCreateV2 { Title = "Write the login tests", Priority = 2,
            IdempotencyKey = "k-create-0001" }, CancellationToken.None).GetAwaiter().GetResult();
        Check(reducer.Tasks.Count == before, "replaying a create adds no second card");

        var assigned = gw.AssignAsync(card.TaskId, new AssignCommand { TargetAgentId = "tester",
            ExpectedRevision = shown.Revision, IdempotencyKey = "k-assign-0001" }, CancellationToken.None)
            .GetAwaiter().GetResult();
        Check(assigned.Ok && reducer.GetTask(card.TaskId).AgentId == "tester", "assign reaches the card");

        var stale = gw.AssignAsync(card.TaskId, new AssignCommand { TargetAgentId = "developer",
            ExpectedRevision = shown.Revision - 5, IdempotencyKey = "k-assign-0002" }, CancellationToken.None)
            .GetAwaiter().GetResult();
        Check(!stale.Ok && reducer.GetTask(card.TaskId).AgentId == "tester", "a stale assign is rejected and changes nothing");
    }

    static void Voice()
    {
        var approve = new[] { "approve", "yes", "ship it", "go ahead", "sounds good", "yes approve it" };
        foreach (var s in approve)
            Check(WitVoiceSource.Classify(s, "") == WitVoiceSource.VoiceCommand.Approve, $"voice '{s}' approves");

        var reject = new[] { "reject", "no", "deny it", "cancel", "decline" };
        foreach (var s in reject)
            Check(WitVoiceSource.Classify(s, "") == WitVoiceSource.VoiceCommand.Reject, $"voice '{s}' rejects");

        // The dangerous ones: none of these may approve.
        var never = new[] { "don't approve", "do not approve this", "not sure", "i'm not sure about that",
            "no approve it", "maybe approve", "wait don't ship it", "disapprove", "unapproved",
            "yes but first tell me about the other option and what the developer would need to change", "" };
        foreach (var s in never)
            Check(WitVoiceSource.Classify(s, "") != WitVoiceSource.VoiceCommand.Approve, $"voice '{s}' must NOT approve");

        var ask = new[] { "why", "why do you recommend this", "explain the risk", "developer what are you changing?" };
        foreach (var s in ask)
            Check(WitVoiceSource.Classify(s, "") == WitVoiceSource.VoiceCommand.Ask, $"voice '{s}' asks");
        Check(WitVoiceSource.Classify("why should i approve", "") == WitVoiceSource.VoiceCommand.Ask,
            "a question containing 'approve' is a question");

        Check(VoiceDecisionController.AgentFor("Developer, what are you changing?") == "developer", "question targets developer");
        Check(VoiceDecisionController.AgentFor("Tester, why did the tests fail?") == "tester", "question targets tester");
        Check(VoiceDecisionController.AgentFor("why do you recommend this") == "researcher", "unaddressed question goes to researcher");
    }

    public static string Run()
    {
        Failures.Clear(); _checks = 0;
        var sections = new (string name, Action run)[]
        {
            ("demo gateway", DemoGateway), ("session reducer", Session), ("task board", Board), ("voice", Voice),
        };
        foreach (var (name, run) in sections)
        {
            try { run(); }
            catch (Exception ex) { Failures.Add($"[{name}] threw {ex.GetType().Name}: {ex.Message}"); }
        }

        var sb = new StringBuilder();
        sb.Append($"RuntimeSmokeTests: {_checks} checks, {Failures.Count} failed");
        foreach (var f in Failures) sb.Append("\n  FAIL: ").Append(f);
        if (Failures.Count > 0) throw new Exception(sb.ToString());
        return sb.ToString();
    }
}
