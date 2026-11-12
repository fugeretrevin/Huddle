using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using MuseDesk.Demo;
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

    // ───────────── demo board connected to the scripted session ─────────────

    /// <summary>The scene's offline wiring without MonoBehaviours: scripted v1 session + its reducer,
    /// demo v2 board + its reducer, joined by the same DemoBoardRunBridge Phase2Bootstrap creates.
    /// Step delays are 0 so every run plays inline.</summary>
    class BoardRig
    {
        public DemoAgentGateway V1;
        public SessionReducer R1;
        public Phase2DemoGateway Board;
        public Phase2Reducer R2;
        public DemoBoardRunBridge Bridge;
        public readonly List<EventEnvelope> V1Events = new List<EventEnvelope>();
        public readonly List<EventEnvelopeV2> V2Events = new List<EventEnvelopeV2>();
        public int Delivered;
        public int Resets;

        public string CardId => Phase2DemoFixture.ExecutableCardId;
        public TaskCardV2 Card => R2.GetTask(CardId);
        public string PendingDecision
        {
            get
            {
                var e = V1Events.LastOrDefault(x => x.Type == EventTypes.DecisionRequested);
                return e != null ? e.DecisionId : null;
            }
        }

        public Phase2CommandResult<TaskCardV2> Assign(string taskId, string agent, string key)
        {
            var shown = R2.GetTask(taskId);
            return Board.AssignAsync(taskId, new AssignCommand { TargetAgentId = agent,
                ExpectedRevision = shown != null ? shown.Revision : 0, IdempotencyKey = key },
                CancellationToken.None).GetAwaiter().GetResult();
        }

        public DecisionResolutionDto Respond(string decisionId, string action, string key)
            => V1.RespondToDecisionAsync(decisionId, action, key, null, CancellationToken.None).GetAwaiter().GetResult();

        /// <summary>task_state of every task event for one card, in order (artifact events excluded).</summary>
        public string States(string taskId)
            => string.Join(">", V2Events.Where(e => e.TaskId == taskId && e.Type != Phase2EventTypes.ArtifactCreated)
                                        .Select(e => e.PayloadString("task_state", "?")));
    }

    static BoardRig Rig()
    {
        var rig = new BoardRig();
        rig.V1 = new DemoAgentGateway(Fixture(), stepSeconds: 0f);
        rig.R1 = new SessionReducer();
        rig.R1.WorkDelivered += _ => rig.Delivered++;
        rig.R1.SessionReset += () => rig.Resets++;
        rig.V1.EventReceived += e => { rig.V1Events.Add(e); rig.R1.Apply(e); };
        rig.V1.StartSessionAsync(CancellationToken.None).GetAwaiter().GetResult();

        rig.Board = new Phase2DemoGateway(null, 0f);
        rig.R2 = new Phase2Reducer();
        rig.R2.LoadSnapshot(rig.Board.GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult());
        rig.Board.EventReceived += e => { rig.V2Events.Add(e); rig.R2.Apply(e); };
        rig.Bridge = new DemoBoardRunBridge(rig.V1, rig.R1, rig.Board);
        return rig;
    }

    static void BoardRun()
    {
        var rig = Rig();

        // Seeded board: one executable card, the rest honestly not runnable.
        var seed = rig.Card;
        Check(seed != null && seed.Executable && seed.TaskTemplate == "auth_demo", "the seeded login card is executable (auth_demo)");
        Check(seed != null && seed.TaskState == TaskStates.Queued && string.IsNullOrEmpty(seed.AgentId) && seed.Priority == 3,
            "the executable card starts queued, unassigned, priority 3");
        Check(rig.R2.Tasks.Values.Count(t => !t.Executable && t.TaskTemplate == null) >= 2, "the seed has planned, non-executable cards");
        Check(rig.R2.Tasks.Values.All(t => t.SourceMode == "demo"), "every seeded card is labelled demo");

        // Wrong agent: refused, nothing starts.
        var wrong = rig.Assign(rig.CardId, "developer", "k-run-wrong-0001");
        Check(!wrong.Ok && wrong.Error != null && wrong.Error.Code == CommandErrorCodes.RoleIncompatible,
            "assigning the executable card to the developer is ROLE_INCOMPATIBLE");
        Check(rig.V1.RunsStarted == 0 && !rig.V1.RunInProgress && rig.Card.TaskState == TaskStates.Queued,
            "a refused assign starts no run and leaves the card queued");

        // Researcher: exactly one run, and the card follows it to the decision.
        var started = rig.Assign(rig.CardId, "researcher", "k-run-start-0001");
        Check(started.Ok && rig.V1.RunsStarted == 1 && rig.V1.RunInProgress, "assigning the executable card to the researcher starts one run");
        Check(rig.Card.TaskState == TaskStates.AwaitingApproval && rig.Card.AgentId == "researcher",
            "the card waits for approval once the scripted run asks for a decision");
        Check(!string.IsNullOrEmpty(rig.PendingDecision) && rig.Card.DecisionId == rig.PendingDecision,
            "the waiting card names the decision it waits on");
        Check(!string.IsNullOrEmpty(rig.Card.RunTaskId), "a launched card carries run_task_id");

        var requested = rig.V1Events.Last(e => e.Type == EventTypes.DecisionRequested);
        var content = requested.Payload.ToObject<DecisionContent>();
        Check(content != null && content.ResearchProvenance == "template" && !string.IsNullOrEmpty(content.ResearchProvenanceLabel),
            "decision content carries research_provenance and its label");
        Check(requested.PayloadString("board_task_id") == rig.CardId, "v1 events of a card-launched run carry board_task_id");

        // Replay of the same command: no second run, no new events.
        int v1Before = rig.V1Events.Count, v2Before = rig.V2Events.Count;
        var replay = rig.Assign(rig.CardId, "researcher", "k-run-start-0001");
        Check(replay.Ok && rig.V1.RunsStarted == 1 && rig.V1Events.Count == v1Before && rig.V2Events.Count == v2Before,
            "a replayed assign starts nothing and emits nothing");

        // A second start while the run is in progress is refused, by the board and by the session.
        var other = rig.Board.CreateTaskAsync(new TaskCreateV2 { Title = "Second login flow", Priority = 1,
            TaskTemplate = "auth_demo", IdempotencyKey = "k-run-create-0001" }, CancellationToken.None).GetAwaiter().GetResult();
        Check(other.Executable && rig.R2.GetTask(other.TaskId) != null && rig.R2.GetTask(other.TaskId).Executable,
            "a card created with task_template auth_demo is executable");
        var second = rig.Assign(other.TaskId, "researcher", "k-run-second-0001");
        Check(!second.Ok && second.Error.Code == CommandErrorCodes.RunInProgress && rig.V1.RunsStarted == 1,
            "a second executable card is refused with RUN_IN_PROGRESS while a run is in progress");
        bool sessionRefused = false;
        try { rig.V1.StartTaskAsync("auth_demo", "k-run-direct-0001", CancellationToken.None).GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { sessionRefused = true; }
        Check(sessionRefused && rig.V1.RunsStarted == 1, "the scripted session refuses a second start while running");

        var pauseRun = rig.Board.PauseAsync(rig.CardId, new PauseCommand { ExpectedRevision = rig.Card.Revision,
            IdempotencyKey = "k-run-pause-0001" }, CancellationToken.None).GetAwaiter().GetResult();
        Check(!pauseRun.Ok && pauseRun.Error.Code == CommandErrorCodes.TaskNotPausable, "a card whose run is in progress is not pausable");

        // Approve: running again, artifacts, completed.
        var res = rig.Respond(rig.PendingDecision, "approve", "k-run-approve-0001");
        Check(res.Applied && res.State == "approved", "the scripted decision is approved through the session");
        Check(rig.States(rig.CardId) == "queued>running>awaiting_approval>running>completed",
            "the card goes running -> awaiting_approval -> running -> completed (got " + rig.States(rig.CardId) + ")");
        Check(rig.Card.TaskState == TaskStates.Completed && !rig.V1.RunInProgress && rig.Delivered == 1,
            "an approved run completes the card and delivers work once");
        Check(rig.V2Events.All(e => e.PayloadString("source_mode") == "demo"), "every demo board event is stamped demo");
        Check(rig.V1Events.All(e => e.PayloadString("source_mode") == "demo"), "every scripted session event is stamped demo");
        bool dense = true;
        for (int i = 1; i < rig.V2Events.Count; i++) if (rig.V2Events[i].Seq != rig.V2Events[i - 1].Seq + 1) dense = false;
        Check(dense, "demo board seq is dense");
        var cardEvents = rig.V2Events.Where(e => e.TaskId == rig.CardId && e.Type != Phase2EventTypes.ArtifactCreated).ToList();
        bool revs = true;
        for (int i = 0; i < cardEvents.Count; i++)
            if (!cardEvents[i].TryPayloadInt("revision", out var rv) || rv != i + 1) revs = false;
        Check(revs, "every card event carries the next revision");

        // Labelled sample artifacts for the CARD, pageable offline.
        var arts = rig.V2Events.Where(e => e.Type == Phase2EventTypes.ArtifactCreated).ToList();
        Check(arts.Count == 2 && arts.All(e => e.TaskId == rig.CardId), "approve raises two artifacts for the card's task id");
        Check(arts.Any(e => e.PayloadString("kind") == ArtifactKinds.CodeDiff) && arts.Any(e => e.PayloadString("kind") == ArtifactKinds.TestReport),
            "the demo artifacts are a code diff and a test report");
        Check(arts.All(e => rig.R2.Artifacts.ContainsKey(e.ArtifactId)), "the reducer knows the demo artifacts");
        foreach (var a in arts)
        {
            string kind = a.PayloadString("kind");
            var first = rig.Board.GetArtifactPageAsync(a.ArtifactId, 0, 1, CancellationToken.None).GetAwaiter().GetResult();
            Check(first.TotalPages >= 2 && first.Pages.Count == 1 && first.Pages[0].Page == 0, kind + ": page 0 is served and there are several pages");
            Check(first.SourceMode == "demo" && first.TaskId == rig.CardId, kind + ": labelled demo and tied to the card");
            Check(first.Pages.Count == 1 && (first.Pages[0].Content ?? "").StartsWith(Phase2DemoArtifactSamples.ScriptedLabel),
                kind + ": the first line says it is a scripted sample");
            var last = rig.Board.GetArtifactPageAsync(a.ArtifactId, first.TotalPages - 1, 1, CancellationToken.None).GetAwaiter().GetResult();
            Check(last.Pages.Count == 1 && last.Pages[0].Page == first.TotalPages - 1 && last.Page == first.TotalPages - 1,
                kind + ": the last page is served");
            var beyond = rig.Board.GetArtifactPageAsync(a.ArtifactId, first.TotalPages, 1, CancellationToken.None).GetAwaiter().GetResult();
            Check(beyond.Pages.Count == 0 && beyond.TotalPages == first.TotalPages, kind + ": an out-of-range page is empty, not invented");
        }

        // Reject: cancelled, nothing built, nothing delivered.
        var rej = Rig();
        rej.Assign(rej.CardId, "researcher", "k-rej-start-0001");
        rej.Respond(rej.PendingDecision, "reject", "k-rej-resp-0001");
        Check(rej.Card.TaskState == TaskStates.Cancelled && rej.States(rej.CardId) == "queued>running>awaiting_approval>cancelled",
            "reject ends the card cancelled (got " + rej.States(rej.CardId) + ")");
        Check(rej.V2Events.All(e => e.Type != Phase2EventTypes.ArtifactCreated) && rej.R2.Artifacts.Count == 0, "reject produces no artifact");
        Check(rej.Delivered == 0 && rej.V1Events.All(e => e.Type != EventTypes.TaskCompleted), "reject gives no completed-work signal");
        Check(!rej.V1.RunInProgress && rej.Board.ActiveRunTaskId == null, "a rejected run is over");

        // Reset mid-run: no late events, seeded board back, ready to start again.
        var rst = Rig();
        int seeded = rst.R2.Tasks.Count;
        rst.Assign(rst.CardId, "researcher", "k-rst-start-0001");
        rst.Board.CreateTaskAsync(new TaskCreateV2 { Title = "Scratch card", IdempotencyKey = "k-rst-create-0001" },
            CancellationToken.None).GetAwaiter().GetResult();
        string oldDecision = rst.PendingDecision;
        rst.V1.ResetRun();
        Check(rst.Resets == 1 && !rst.V1.RunInProgress && rst.R1.FirstPendingDecision() == null, "reset clears the session and ends the run");
        Check(rst.R2.Tasks.Count == seeded && rst.Card != null && rst.Card.TaskState == TaskStates.Queued
              && string.IsNullOrEmpty(rst.Card.AgentId) && rst.Card.Revision == 0 && rst.Board.ActiveRunTaskId == null,
            "reset restores the seeded board");
        int n1 = rst.V1Events.Count, n2 = rst.V2Events.Count;
        var late = rst.Respond(oldDecision, "approve", "k-rst-late-0001");
        Check(!late.Applied && rst.V1Events.Count == n1 && rst.V2Events.Count == n2 && rst.Delivered == 0,
            "answering the old run's decision after a reset produces no late events");
        var again = rst.Assign(rst.CardId, "researcher", "k-rst-start-0002");
        Check(again.Ok && rst.V1.RunInProgress && rst.Card.TaskState == TaskStates.AwaitingApproval
              && rst.PendingDecision != oldDecision, "after a reset the card starts a fresh run with a new decision");

        // Planned (non-executable) cards: commands change the card only, and never claim to run.
        var plain = Rig();
        string pid = Phase2DemoFixture.PlannedReleaseNotesId;
        var toDev = plain.Assign(pid, "developer", "k-plain-assign-0001");
        Check(toDev.Ok && plain.R2.GetTask(pid).AgentId == "developer" && plain.R2.GetTask(pid).TaskState == TaskStates.Queued
              && plain.V1.RunsStarted == 0, "assigning a planned card changes only the card");
        var paused = plain.Board.PauseAsync(pid, new PauseCommand { ExpectedRevision = plain.R2.GetTask(pid).Revision,
            IdempotencyKey = "k-plain-pause-0001" }, CancellationToken.None).GetAwaiter().GetResult();
        Check(paused.Ok && plain.R2.GetTask(pid).TaskState == TaskStates.Paused
              && plain.V2Events.All(e => e.Type != Phase2EventTypes.TaskPauseRequested), "pause goes straight to paused, never pause_requested");
        var resumed = plain.Board.ResumeAsync(pid, new ResumeCommand { ExpectedRevision = plain.R2.GetTask(pid).Revision,
            IdempotencyKey = "k-plain-resume-0001" }, CancellationToken.None).GetAwaiter().GetResult();
        Check(resumed.Ok && plain.R2.GetTask(pid).TaskState == TaskStates.Queued, "resuming an assigned planned card returns it to queued, not running");
        Check(plain.V2Events.All(e => e.PayloadString("task_state") != TaskStates.Running), "a planned card is never reported running");
    }

    public static string Run()
    {
        Failures.Clear(); _checks = 0;
        var sections = new (string name, Action run)[]
        {
            ("demo gateway", DemoGateway), ("session reducer", Session), ("task board", Board), ("voice", Voice),
            ("board run", BoardRun),
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
