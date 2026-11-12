using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MuseDesk.Phase2.Contracts
{
    /// <summary>
    /// Fully self-contained offline v2 gateway for the judged demo. It holds an authoritative
    /// LOCAL copy of the v2 state (tasks, focus) and replays a scripted timeline, playing the
    /// SAME EventEnvelopeV2 shapes the live backend emits — so the Phase2Reducer and every board
    /// presenter behave identically in both modes. Everything is honestly stamped source_mode="demo".
    ///
    /// Crucially it reproduces the backend command SEMANTICS so the P2-01 drag state machine and its
    /// acceptance tests run offline with no PC: optimistic revision guard (stale → REVISION_CONFLICT),
    /// terminal / running rejection, and idempotency by key. No network, no provider keys.
    ///
    /// Board ↔ session (mirrors the backend contract): assigning an EXECUTABLE, not-yet-started card
    /// to the Researcher starts the scripted v1 run through an <see cref="IDemoRunLauncher"/>; the
    /// card then follows that run through the NotifyRun* methods, which the launcher's owner calls
    /// from the v1 reducer's real events (never from timers here). Non-executable cards are planned
    /// work: commands change the card only and nothing ever claims to run them.
    ///
    /// Started from the Unity main thread so awaited delays resume there and EventReceived is raised
    /// on the main thread.
    /// </summary>
    public class Phase2DemoGateway : IPhase2Gateway
    {
        public string ModeLabel => "DEMO · scripted (v2)";
        public string SourceMode => SourceModes.Demo;
        public bool IsLive => false;
        public event Action<EventEnvelopeV2> EventReceived;

        /// <summary>Raised when a user command changed the board (create / assign / priority /
        /// pause / resume). Lets the session know the demo is in use. Not raised for focus changes,
        /// refused commands or idempotent replays.</summary>
        public event Action UserCommandAccepted;

        /// <summary>Default fixture path (not a Resources path; lives under StreamingAssets-like
        /// project location). See LoadFixture for lookup order.</summary>
        public const string DefaultFixtureProjectPath = "Assets/MuseDesk/Phase2/Fixtures/phase2_demo.json";
        public const string DefaultFixtureResourceName = "phase2_demo";   // if moved under a Resources/ folder

        // Same refusal messages the backend sends, so the card reads the same in both modes.
        public const string RoleIncompatibleMessage = "This task starts with the Researcher. Assign it to the Researcher to run it.";
        public const string RunInProgressMessage = "A task is already running in this session. Wait for it to finish (or answer its pending decision) first.";
        public const string ExecutorUnavailableMessage = "The scripted session is not available, so this task cannot start right now.";
        /// <summary>run_task_id of a card-launched scripted run (the v1 demo's parent task id).</summary>
        public const string DemoRunTaskId = "task-demo-auth";

        readonly Phase2DemoFixture _fx;
        readonly float _step;
        readonly string _sessionId;

        readonly Dictionary<string, TaskCardV2> _tasks = new Dictionary<string, TaskCardV2>();
        readonly Dictionary<string, ArtifactV2> _artifacts = new Dictionary<string, ArtifactV2>();
        readonly Dictionary<string, DiagramV2> _diagrams = new Dictionary<string, DiagramV2>();
        readonly Dictionary<string, object> _idempotency = new Dictionary<string, object>();
        FocusPolicyV2 _focus = new FocusPolicyV2();
        long _seq;

        // ─── scripted run bound to a card ───
        IDemoRunLauncher _launcher;
        string _activeRunTaskId;                                        // card the current run belongs to
        readonly HashSet<string> _startedRuns = new HashSet<string>();  // cards whose run was started
        readonly List<string> _runArtifactIds = new List<string>();     // artifacts of the current run
        Phase2DemoArtifactSamples _samples;

        public Phase2DemoGateway(Phase2DemoFixture fixture, float stepSeconds = 1.0f)
        {
            _fx = fixture ?? Phase2DemoFixture.BuiltInDefault();
            _step = stepSeconds;
            var snap = _fx.Snapshot ?? new SnapshotV2();
            _sessionId = string.IsNullOrEmpty(snap.SessionId) ? "phase2-scripted-demo-001" : snap.SessionId;
            _seq = snap.LastSeq;
            if (snap.Focus != null)
                _focus = new FocusPolicyV2 { Mode = snap.Focus.Mode ?? FocusModes.Normal,
                    Revision = snap.Focus.Revision, Until = snap.Focus.Until };
            LoadSeed();
        }

        /// <summary>(Re)build tasks / artifacts / diagrams from the fixture. Focus and the seq
        /// counter are deliberately left alone: focus is the user's setting, and seq must stay
        /// monotonic across a reset.</summary>
        void LoadSeed()
        {
            _tasks.Clear(); _artifacts.Clear(); _diagrams.Clear(); _idempotency.Clear();
            _startedRuns.Clear(); _runArtifactIds.Clear();
            _activeRunTaskId = null;

            var snap = _fx.Snapshot ?? new SnapshotV2();
            if (snap.Tasks != null)
                foreach (var t in snap.Tasks)
                {
                    if (t == null || string.IsNullOrEmpty(t.TaskId)) continue;
                    var c = t.Clone();
                    c.SourceMode = SourceModes.Demo;                          // a demo card is never labelled live
                    c.Executable = TaskTemplates.IsExecutable(c.TaskTemplate); // only auth_demo can run
                    c.DecisionId = null;
                    c.RunTaskId = null;
                    _tasks[c.TaskId] = c;
                }
            if (_fx.Artifacts != null) foreach (var a in _fx.Artifacts) if (a != null && a.ArtifactId != null) _artifacts[a.ArtifactId] = a;
            if (_fx.Diagrams != null) foreach (var d in _fx.Diagrams) if (d != null && d.DiagramId != null) _diagrams[d.DiagramId] = d;
        }

        // ─────────────────────────── fixture loading ───────────────────────────

        /// <summary>
        /// Load the demo fixture, tolerating its absence at author time (the backend engineer
        /// produces it). Accepts either a full { snapshot, timeline, artifacts, diagrams } document
        /// OR a bare SnapshotV2 document (the file is then treated as the snapshot). Returns null if
        /// not found — the caller should pass that to the ctor, which substitutes a built-in default.
        /// </summary>
        public static Phase2DemoFixture LoadFixture(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                var root = JObject.Parse(text);
                // Full fixture document?
                if (root["snapshot"] != null || root["timeline"] != null)
                    return root.ToObject<Phase2DemoFixture>();
                // Otherwise treat the whole document as a bare snapshot.
                return new Phase2DemoFixture { Snapshot = root.ToObject<SnapshotV2>() };
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MuseDesk.P2] demo fixture parse failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Best-effort load from the project path or a Resources fallback. Returns null if
        /// nothing is present (demo still runs on a built-in default).</summary>
        public static Phase2DemoFixture LoadDefaultFixture()
        {
            // 1) Resources (works in a player build if the fixture is placed under a Resources/ folder)
            var ta = Resources.Load<TextAsset>(DefaultFixtureResourceName);
            if (ta != null) return LoadFixture(ta.text);

            // 2) Project path on disk (Editor / dev convenience; absent in a shipped APK)
            try
            {
                if (File.Exists(DefaultFixtureProjectPath))
                    return LoadFixture(File.ReadAllText(DefaultFixtureProjectPath));
            }
            catch (Exception ex) { Debug.LogWarning($"[MuseDesk.P2] fixture read failed: {ex.Message}"); }

            Debug.Log("[MuseDesk.P2] no phase2_demo fixture found; using built-in default scripted state.");
            return null;
        }

        // ─────────────────────────── snapshot ───────────────────────────

        public Task<SnapshotV2> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(BuildSnapshot());

        SnapshotV2 BuildSnapshot()
        {
            var snap = new SnapshotV2
            {
                SessionId = _sessionId,
                LastSeq = _seq,
                SourceMode = SourceModes.Demo,
                Focus = new FocusSnapshotV2 { Mode = _focus.Mode, Revision = _focus.Revision, Until = _focus.Until },
            };
            foreach (var t in _tasks.Values) snap.Tasks.Add(t.Clone());
            if (_fx.Snapshot?.Handoffs != null) snap.Handoffs = new List<HandoffV2>(_fx.Snapshot.Handoffs);
            foreach (var a in _artifacts.Values)
                snap.Artifacts.Add(new ArtifactRefV2 { ArtifactId = a.ArtifactId, Kind = a.Kind, SourceMode = SourceModes.Demo });
            if (_fx.Snapshot?.Inbox != null) snap.Inbox = new List<InboxItemV2>(_fx.Snapshot.Inbox);
            return snap;
        }

        /// <summary>Optional: replay the scripted timeline (handoffs, artifacts, milestones) with a
        /// delay between events. Call once after the reducer has loaded the snapshot. Safe to skip.</summary>
        public async Task PlayScriptedTimelineAsync(CancellationToken ct)
        {
            if (_fx.Timeline == null) return;
            foreach (var ev in _fx.Timeline)
            {
                if (ct.IsCancellationRequested) return;
                await Step(ct);
                if (ev == null) continue;
                // Honor the fixture's seq if it is newer, else append after current.
                _seq = Math.Max(_seq + 1, ev.Seq);
                ev.Seq = _seq;
                ev.SessionId = _sessionId;
                if (string.IsNullOrEmpty(ev.OccurredAt)) ev.OccurredAt = UtcNow();
                Raise(ev);
            }
        }

        // ─────────────────────────── scripted run binding ───────────────────────────

        /// <summary>Attach (or detach with null) the scripted session that executable cards start.
        /// Without one, assigning an executable card to the Researcher is refused (EXECUTOR_UNAVAILABLE)
        /// rather than pretending to run.</summary>
        public void AttachRunLauncher(IDemoRunLauncher launcher) => _launcher = launcher;

        /// <summary>The card the scripted run in flight belongs to, or null.</summary>
        public string ActiveRunTaskId => _activeRunTaskId;

        /// <summary>The first queued executable card whose run has not been started, or null.
        /// This is the card "Start demo" starts through.</summary>
        public TaskCardV2 FindUnstartedExecutable()
        {
            foreach (var c in _tasks.Values)
                if (IsUnstartedExecutable(c) && c.TaskState == TaskStates.Queued)
                    return c.Clone();
            return null;
        }

        bool IsUnstartedExecutable(TaskCardV2 c) =>
            c != null && c.Executable && !_startedRuns.Contains(c.TaskId) && !TaskStateSets.IsTerminal(c.TaskState);

        bool TryGetRunCard(out TaskCardV2 card)
        {
            card = null;
            return _activeRunTaskId != null && _tasks.TryGetValue(_activeRunTaskId, out card) && card != null;
        }

        /// <summary>The scripted run asked the human for a decision: the card waits on it
        /// (decision.requested on the v2 stream, task_state awaiting_approval).</summary>
        public void NotifyRunDecisionRequested(string decisionId, string summary = null,
            string researchProvenance = null, string researchProvenanceLabel = null)
        {
            if (!TryGetRunCard(out var card)) return;
            if (card.TaskState != TaskStates.Running) return;
            card.TaskState = TaskStates.AwaitingApproval;
            card.DecisionId = decisionId;
            card.Revision += 1;
            var p = TaskPayload(card);
            p["decision_id"] = Str(decisionId);
            p["summary"] = Str(summary);
            p["research_provenance"] = Str(string.IsNullOrEmpty(researchProvenance) ? "template" : researchProvenance);
            p["research_provenance_label"] = Str(string.IsNullOrEmpty(researchProvenanceLabel)
                ? "Template recommendation. Not live AI research." : researchProvenanceLabel);
            EmitTask(Phase2EventTypes.DecisionRequested, card, p, agentId: card.AgentId);
        }

        /// <summary>The decision was answered. Approved → the card runs on. Anything else ends the
        /// card as cancelled (a completion with task_state "cancelled": nothing was built).</summary>
        public void NotifyRunDecisionResolved(string decisionId, string state)
        {
            if (!TryGetRunCard(out var card)) return;
            if (card.TaskState != TaskStates.AwaitingApproval) return;
            if (!string.IsNullOrEmpty(card.DecisionId) && !string.IsNullOrEmpty(decisionId) && card.DecisionId != decisionId)
                return;   // an answer to some other decision

            string s = (state ?? "").ToLowerInvariant();
            string answered = card.DecisionId ?? decisionId;
            card.DecisionId = null;
            card.Revision += 1;
            if (s.Contains("approv"))
            {
                card.TaskState = TaskStates.Running;
                var p = TaskPayload(card);
                p["decision_id"] = Str(answered);
                p["decision_state"] = Str(state);
                EmitTask(Phase2EventTypes.TaskResumed, card, p, agentId: card.AgentId);
            }
            else
            {
                card.TaskState = TaskStates.Cancelled;
                _activeRunTaskId = null;
                var p = TaskPayload(card);
                p["decision_state"] = Str(state);
                p["result_summary"] = "Scripted demo: the decision was not approved, so nothing was built.";
                EmitTask(Phase2EventTypes.TaskCompleted, card, p, agentId: card.AgentId);
            }
        }

        /// <summary>The scripted developer produced its change: publish the labelled sample diff for
        /// the CARD's task id (artifact.created on the v2 stream).</summary>
        public void NotifyRunArtifactCreated()
        {
            if (!TryGetRunCard(out var card)) return;
            PublishRunArtifact(card, ArtifactKinds.CodeDiff, Phase2AgentIds.Developer);
        }

        /// <summary>The scripted run delivered its work: publish the labelled sample test report and
        /// complete the card.</summary>
        public void NotifyRunWorkDelivered(string resultSummary)
        {
            if (!TryGetRunCard(out var card)) return;
            if (_runArtifactIds.Count == 0) PublishRunArtifact(card, ArtifactKinds.CodeDiff, Phase2AgentIds.Developer);
            PublishRunArtifact(card, ArtifactKinds.TestReport, Phase2AgentIds.Tester);

            card.TaskState = TaskStates.Completed;
            card.DecisionId = null;
            card.Revision += 1;
            _activeRunTaskId = null;
            var p = TaskPayload(card);
            if (_runArtifactIds.Count > 0) p["artifact_id"] = _runArtifactIds[0];
            p["artifact_ids"] = new JArray(_runArtifactIds.ToArray());
            p["result_summary"] = Str(string.IsNullOrEmpty(resultSummary) ? "Scripted demo run complete" : resultSummary);
            EmitTask(Phase2EventTypes.TaskCompleted, card, p, agentId: card.AgentId);
            _runArtifactIds.Clear();
        }

        /// <summary>The scripted run failed: the card ends failed.</summary>
        public void NotifyRunFailed(string reason)
        {
            if (!TryGetRunCard(out var card)) return;
            card.TaskState = TaskStates.Failed;
            card.DecisionId = null;
            card.Revision += 1;
            _activeRunTaskId = null;
            _runArtifactIds.Clear();
            var p = TaskPayload(card);
            p["result_summary"] = Str(string.IsNullOrEmpty(reason) ? "Scripted demo run failed" : reason);
            p["code"] = CommandErrorCodes.RunFailed;
            EmitTask(Phase2EventTypes.TaskFailed, card, p, agentId: card.AgentId);
        }

        /// <summary>
        /// Put the board back to its seeded state (cards, artifacts, idempotency memory, run
        /// binding) and announce it with a v2 session.reset that carries the fresh snapshot, so the
        /// reducer and every presenter rebuild from it. Focus is kept. seq keeps counting up.
        /// </summary>
        public void RestoreSeed(string reason = "demo_reset")
        {
            LoadSeed();
            _seq++;
            var snap = BuildSnapshot();   // LastSeq == the reset event's seq
            Raise(new EventEnvelopeV2
            {
                EventId = $"evt2-demo-{_seq:D4}", Seq = _seq, SessionId = _sessionId, OccurredAt = UtcNow(),
                Type = Phase2EventTypes.SessionReset,
                Payload = new JObject
                {
                    ["reason"] = Str(reason), ["source_mode"] = SourceModes.Demo,
                    ["snapshot"] = JObject.FromObject(snap),
                },
            });
        }

        ArtifactV2 PublishRunArtifact(TaskCardV2 card, string kind, string agentId)
        {
            if (_samples == null) _samples = Phase2DemoArtifactSamples.Load();
            var sample = _samples.Find(kind);
            if (sample == null) return null;

            string id = string.IsNullOrEmpty(sample.ArtifactId) ? "art-demo-" + kind : sample.ArtifactId;
            if (_artifacts.ContainsKey(id)) id = id + "-" + card.TaskId;
            if (_artifacts.ContainsKey(id)) return _artifacts[id];   // already published for this card

            var art = new ArtifactV2
            {
                ArtifactId = id, TaskId = card.TaskId, Kind = sample.Kind, Summary = sample.Summary,
                SourceMode = SourceModes.Demo,
                ContentHash = string.IsNullOrEmpty(sample.ContentHash) ? "scripted-sample" : sample.ContentHash,
                CreatedAt = UtcNow(), Pages = sample.Pages ?? new List<ArtifactPage>(),
            };
            _artifacts[id] = art;
            _runArtifactIds.Add(id);

            _seq++;
            Raise(new EventEnvelopeV2
            {
                EventId = $"evt2-demo-{_seq:D4}", Seq = _seq, SessionId = _sessionId, OccurredAt = UtcNow(),
                Type = Phase2EventTypes.ArtifactCreated, TaskId = card.TaskId, AgentId = agentId, ArtifactId = id,
                Payload = new JObject
                {
                    ["artifact_id"] = id, ["task_id"] = card.TaskId, ["kind"] = Str(art.Kind),
                    ["summary"] = Str(art.Summary), ["total_pages"] = art.Pages.Count,
                    ["source_mode"] = SourceModes.Demo,
                },
            });
            return art;
        }

        // ─────────────────────────── commands ───────────────────────────

        public Task<TaskCardV2> CreateTaskAsync(TaskCreateV2 cmd, CancellationToken ct)
        {
            if (TryReplay(cmd.IdempotencyKey, out TaskCardV2 prior)) return Task.FromResult(prior);
            // The backend answers 422 UNKNOWN_TASK_TEMPLATE (a thrown transport error on the client).
            if (!string.IsNullOrEmpty(cmd.TaskTemplate) && !TaskTemplates.IsExecutable(cmd.TaskTemplate))
                throw new InvalidOperationException(
                    $"{CommandErrorCodes.UnknownTaskTemplate}: unknown task_template '{cmd.TaskTemplate}'");
            var card = new TaskCardV2
            {
                TaskId = $"t2-demo-{Guid.NewGuid():N}".Substring(0, 13),
                Title = cmd.Title,
                TaskState = (cmd.Dependencies != null && cmd.Dependencies.Count > 0) ? TaskStates.Blocked : TaskStates.Queued,
                AgentId = cmd.AgentId,
                Priority = cmd.Priority,
                Revision = 0,
                Dependencies = cmd.Dependencies ?? new List<string>(),
                SourceMode = SourceModes.Demo,
                TaskTemplate = string.IsNullOrEmpty(cmd.TaskTemplate) ? null : cmd.TaskTemplate,
                Executable = TaskTemplates.IsExecutable(cmd.TaskTemplate),
            };
            _tasks[card.TaskId] = card;
            // Same card fields the live backend sends on task.created, so the new card is not blank.
            var payload = TaskPayload(card);
            payload["title"] = Str(card.Title);
            payload["priority"] = card.Priority;
            payload["task_template"] = Str(card.TaskTemplate);
            payload["executable"] = card.Executable;
            EmitTask(Phase2EventTypes.TaskCreated, card, payload, agentId: card.AgentId);
            Remember(cmd.IdempotencyKey, card.Clone());
            Touched();
            return Task.FromResult(card.Clone());
        }

        public Task<Phase2CommandResult<TaskCardV2>> AssignAsync(string taskId, AssignCommand cmd, CancellationToken ct)
        {
            if (TryReplay(cmd.IdempotencyKey, out Phase2CommandResult<TaskCardV2> prior)) return Task.FromResult(prior);
            var result = RunGuarded(taskId, cmd.ExpectedRevision, card =>
            {
                if (TaskStateSets.IsTerminal(card.TaskState))
                    return Fail(CommandErrorCodes.TaskNotAssignable, $"task is {card.TaskState}; cannot reassign", card);
                if (TaskStateSets.IsRunning(card.TaskState))
                    return Fail(CommandErrorCodes.TaskRunning,
                        card.TaskId == _activeRunTaskId ? "task is running; it cannot be moved while its run is in progress"
                                                        : "task is running; pause before reassigning", card);
                if (!TaskStateSets.IsSafeAssignCheckpoint(card.TaskState))
                    return Fail(CommandErrorCodes.UnsafeTransfer, $"task state {card.TaskState} is not a safe reassignment checkpoint", card);

                // An executable card that has not run yet: assigning it IS starting the pipeline.
                if (IsUnstartedExecutable(card))
                {
                    if (cmd.TargetAgentId != Phase2AgentIds.Researcher)
                        return Fail(CommandErrorCodes.RoleIncompatible, RoleIncompatibleMessage, card);
                    if (card.TaskState == TaskStates.Paused)
                        return Fail(CommandErrorCodes.TaskPaused, "This task is paused. Resume it before assigning it.", card);
                    if (card.TaskState == TaskStates.Blocked)
                        return Fail(CommandErrorCodes.DependenciesNotReady, "This task is waiting on other tasks. It cannot start yet.", card);
                    if (_activeRunTaskId != null || (_launcher != null && _launcher.RunInProgress))
                        return Fail(CommandErrorCodes.RunInProgress, RunInProgressMessage, card);
                    if (_launcher == null)
                        return Fail(CommandErrorCodes.ExecutorUnavailable, ExecutorUnavailableMessage, card);
                    return StartRunFor(card);
                }

                card.AgentId = cmd.TargetAgentId;
                card.Revision += 1;
                var payload = TaskPayload(card);
                payload["target_agent_id"] = Str(cmd.TargetAgentId);
                EmitTask(Phase2EventTypes.TaskAssigned, card, payload, agentId: cmd.TargetAgentId);
                return Phase2CommandResult<TaskCardV2>.Success(card.Clone());
            });
            Remember(cmd.IdempotencyKey, result);
            if (result.Ok) Touched();
            return Task.FromResult(result);
        }

        /// <summary>Assign the executable card to the Researcher and start the scripted run for it.
        /// task.assigned then task.started are emitted BEFORE the launcher is called, so the run's
        /// own events (which may arrive inline) always follow the card's start.</summary>
        Phase2CommandResult<TaskCardV2> StartRunFor(TaskCardV2 card)
        {
            card.AgentId = Phase2AgentIds.Researcher;
            card.Revision += 1;
            var assigned = TaskPayload(card);
            assigned["target_agent_id"] = Phase2AgentIds.Researcher;
            EmitTask(Phase2EventTypes.TaskAssigned, card, assigned, agentId: Phase2AgentIds.Researcher);

            card.TaskState = TaskStates.Running;
            card.RunTaskId = DemoRunTaskId;
            card.Revision += 1;
            _startedRuns.Add(card.TaskId);
            _activeRunTaskId = card.TaskId;
            _runArtifactIds.Clear();
            var started = TaskPayload(card);
            started["executable"] = card.Executable;
            EmitTask(Phase2EventTypes.TaskStarted, card, started, agentId: Phase2AgentIds.Researcher);

            bool launched = false;
            try { launched = _launcher.TryStartRun(card.TaskId); }
            catch (Exception ex) { Debug.LogError($"[MuseDesk.P2] scripted run launch error: {ex.Message}"); }

            if (!launched && _activeRunTaskId == card.TaskId)
            {
                // The session refused after all: say so on the card instead of leaving it "running".
                NotifyRunFailed("The scripted session could not start this task.");
                return Fail(CommandErrorCodes.ExecutorUnavailable, ExecutorUnavailableMessage, card);
            }
            // Clone last: with zero step delay the run's first events have already moved the card on.
            return Phase2CommandResult<TaskCardV2>.Success(card.Clone());
        }

        public Task<Phase2CommandResult<TaskCardV2>> SetPriorityAsync(string taskId, PriorityCommand cmd, CancellationToken ct)
        {
            if (TryReplay(cmd.IdempotencyKey, out Phase2CommandResult<TaskCardV2> prior)) return Task.FromResult(prior);
            var result = RunGuarded(taskId, cmd.ExpectedRevision, card =>
            {
                if (TaskStateSets.IsTerminal(card.TaskState))
                    return Fail(CommandErrorCodes.TaskNotAssignable, "task is terminal; cannot reprioritize", card);
                card.Priority = cmd.Priority;
                card.Revision += 1;
                var payload = TaskPayload(card);
                payload["priority"] = cmd.Priority;
                EmitTask(Phase2EventTypes.TaskPriorityChanged, card, payload);
                return Phase2CommandResult<TaskCardV2>.Success(card.Clone());
            });
            Remember(cmd.IdempotencyKey, result);
            if (result.Ok) Touched();
            return Task.FromResult(result);
        }

        public Task<Phase2CommandResult<TaskCardV2>> PauseAsync(string taskId, PauseCommand cmd, CancellationToken ct)
        {
            if (TryReplay(cmd.IdempotencyKey, out Phase2CommandResult<TaskCardV2> prior)) return Task.FromResult(prior);
            var result = RunGuarded(taskId, cmd.ExpectedRevision, card =>
            {
                if (TaskStateSets.IsTerminal(card.TaskState))
                    return Fail(CommandErrorCodes.TaskNotPausable, $"task is {card.TaskState}", card);
                // The scripted run cannot be suspended, so a card whose run is in progress is not
                // pausable — refusing is honest; "pause requested" forever would not be.
                if (card.TaskId == _activeRunTaskId)
                    return Fail(CommandErrorCodes.TaskNotPausable, "This task's run is in progress and cannot be paused.", card);
                if (card.TaskState == TaskStates.Paused)
                    return Phase2CommandResult<TaskCardV2>.Success(card.Clone());   // idempotent no-op
                // Nothing is executing a card outside a run, so there is nothing to wait for:
                // it is paused immediately (never pause_requested).
                card.TaskState = TaskStates.Paused;
                card.Revision += 1;
                EmitTask(Phase2EventTypes.TaskPaused, card, TaskPayload(card));
                return Phase2CommandResult<TaskCardV2>.Success(card.Clone());
            });
            Remember(cmd.IdempotencyKey, result);
            if (result.Ok) Touched();
            return Task.FromResult(result);
        }

        public Task<Phase2CommandResult<TaskCardV2>> ResumeAsync(string taskId, ResumeCommand cmd, CancellationToken ct)
        {
            if (TryReplay(cmd.IdempotencyKey, out Phase2CommandResult<TaskCardV2> prior)) return Task.FromResult(prior);
            var result = RunGuarded(taskId, cmd.ExpectedRevision, card =>
            {
                if (card.TaskState != TaskStates.Paused && card.TaskState != TaskStates.PauseRequested)
                    return Fail(CommandErrorCodes.TaskNotPaused, $"task is {card.TaskState}; cannot resume", card);
                // Resuming never starts work: the card goes back to the queue. Only assigning an
                // executable card to the Researcher runs anything.
                card.TaskState = TaskStates.Queued;
                card.Revision += 1;
                EmitTask(Phase2EventTypes.TaskResumed, card, TaskPayload(card));
                return Phase2CommandResult<TaskCardV2>.Success(card.Clone());
            });
            Remember(cmd.IdempotencyKey, result);
            if (result.Ok) Touched();
            return Task.FromResult(result);
        }

        public Task<FocusPolicyV2> SetFocusAsync(FocusCommand cmd, CancellationToken ct)
        {
            if (TryReplay(cmd.IdempotencyKey, out FocusPolicyV2 prior)) return Task.FromResult(prior);
            _focus = new FocusPolicyV2
            {
                Mode = cmd.Mode, Until = cmd.Until,
                QuietPriorities = cmd.QuietPriorities ?? new List<int>(),
                UpdatedAt = UtcNow(), Revision = _focus.Revision + 1,
            };
            _seq++;
            Raise(new EventEnvelopeV2
            {
                EventId = $"evt2-demo-{_seq:D4}", Seq = _seq, SessionId = _sessionId, OccurredAt = UtcNow(),
                Type = Phase2EventTypes.FocusChanged,
                Payload = new JObject { ["mode"] = cmd.Mode, ["revision"] = _focus.Revision, ["source_mode"] = SourceModes.Demo }
            });
            var outPolicy = CloneFocus(_focus);
            Remember(cmd.IdempotencyKey, CloneFocus(_focus));
            return Task.FromResult(outPolicy);
        }

        /// <summary>Serve one window of an artifact. <paramref name="page"/> is 0-based. A page past
        /// the end (or negative) returns an EMPTY pages list with the real total_pages — never an
        /// invented page.</summary>
        public Task<ArtifactPageResponse> GetArtifactPageAsync(string artifactId, int page, int pageSize, CancellationToken ct)
        {
            if (artifactId != null && _artifacts.TryGetValue(artifactId, out var art))
            {
                int size = Math.Max(1, pageSize);
                int count = art.Pages != null ? art.Pages.Count : 0;
                int total = Math.Max(1, (int)Math.Ceiling(count / (double)size));
                var slice = new List<ArtifactPage>();
                if (page >= 0)
                    for (int i = page * size; i < Math.Min(count, (page + 1) * size); i++)
                        slice.Add(art.Pages[i]);
                return Task.FromResult(new ArtifactPageResponse
                {
                    ArtifactId = art.ArtifactId, TaskId = art.TaskId, Kind = art.Kind, Summary = art.Summary,
                    SourceMode = SourceModes.Demo, ContentHash = art.ContentHash, CreatedAt = art.CreatedAt,
                    Page = page, PageSize = size, TotalPages = total, Pages = slice, Stale = false,
                });
            }
            // Honest stub for an artifact not present in the fixture.
            return Task.FromResult(new ArtifactPageResponse
            {
                ArtifactId = artifactId, TaskId = "", Kind = ArtifactKinds.Completion,
                Summary = "SCRIPTED DEMO: artifact body not in fixture", SourceMode = SourceModes.Demo,
                ContentHash = "scripted-missing", CreatedAt = UtcNow(), Page = 0, PageSize = pageSize, TotalPages = 1,
                Pages = new List<ArtifactPage> { new ArtifactPage { Page = 0, Title = "Scripted demo",
                    Content = "This artifact has no scripted body in the offline fixture." } },
            });
        }

        public Task<DiagramV2> CreateDiagramAsync(DiagramCommand cmd, CancellationToken ct)
        {
            foreach (var d in _diagrams.Values)
                if (d.Template == cmd.Template) return Task.FromResult(d);
            return Task.FromResult(new DiagramV2
            {
                DiagramId = $"dia-demo-{cmd.Template}", Template = cmd.Template, SourceMode = SourceModes.Demo,
                Nodes = new List<DiagramNode>(), Edges = new List<DiagramEdge>(),
            });
        }

        // ─────────────────────────── helpers ───────────────────────────

        delegate Phase2CommandResult<TaskCardV2> CardMutation(TaskCardV2 card);

        Phase2CommandResult<TaskCardV2> RunGuarded(string taskId, int expectedRevision, CardMutation mutate)
        {
            if (taskId == null || !_tasks.TryGetValue(taskId, out var card))
                return Fail("NOT_FOUND", $"unknown task {taskId}", null);
            if (card.Revision != expectedRevision)
                return Fail(CommandErrorCodes.RevisionConflict,
                    $"task revision is {card.Revision}, command expected {expectedRevision}", card);
            return mutate(card);
        }

        static Phase2CommandResult<TaskCardV2> Fail(string code, string message, TaskCardV2 canonical) =>
            Phase2CommandResult<TaskCardV2>.Conflict(new CommandErrorBody
            { Code = code, Message = message, CanonicalTask = canonical?.Clone() });

        bool TryReplay<T>(string key, out T value)
        {
            value = default;
            if (!string.IsNullOrEmpty(key) && _idempotency.TryGetValue(key, out var stored) && stored is T t)
            { value = t; return true; }
            return false;
        }

        void Remember(string key, object value)
        {
            if (!string.IsNullOrEmpty(key)) _idempotency[key] = value;
        }

        void Touched()
        {
            try { UserCommandAccepted?.Invoke(); }
            catch (Exception ex) { Debug.LogError($"[MuseDesk.P2] demo command listener error: {ex.Message}"); }
        }

        /// <summary>The fields EVERY task event carries: the card's state, its revision and the
        /// honest source_mode.</summary>
        static JObject TaskPayload(TaskCardV2 card)
        {
            var p = new JObject
            {
                ["task_state"] = Str(card.TaskState), ["revision"] = card.Revision, ["source_mode"] = SourceModes.Demo,
            };
            // Cards tied to a pipeline template name it (and their run, once launched) on every event.
            if (!string.IsNullOrEmpty(card.TaskTemplate)) p["task_template"] = card.TaskTemplate;
            if (!string.IsNullOrEmpty(card.RunTaskId)) p["run_task_id"] = card.RunTaskId;
            return p;
        }

        /// <summary>A JSON string, or an explicit JSON null (never a String-typed null token).</summary>
        static JToken Str(string s) => s != null ? new JValue(s) : JValue.CreateNull();

        void EmitTask(string type, TaskCardV2 card, JObject payload, string agentId = null)
        {
            _seq++;
            Raise(new EventEnvelopeV2
            {
                EventId = $"evt2-demo-{_seq:D4}", Seq = _seq, SessionId = _sessionId, OccurredAt = UtcNow(),
                Type = type, TaskId = card.TaskId, AgentId = agentId, Payload = payload ?? new JObject(),
            });
        }

        void Raise(EventEnvelopeV2 env)
        {
            try { EventReceived?.Invoke(env); }
            catch (Exception ex) { Debug.LogError($"[MuseDesk.P2] demo event handler error: {ex.Message}"); }
        }

        async Task Step(CancellationToken ct)
        {
            if (_step > 0f) await Task.Delay(TimeSpan.FromSeconds(_step), ct);
        }

        static FocusPolicyV2 CloneFocus(FocusPolicyV2 f) => new FocusPolicyV2
        {
            Mode = f.Mode, Until = f.Until, UpdatedAt = f.UpdatedAt, Revision = f.Revision,
            QuietPriorities = new List<int>(f.QuietPriorities ?? new List<int>()),
        };

        static string UtcNow() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    /// <summary>
    /// Tolerant offline fixture. The backend engineer produces phase2_demo.json; this accepts a
    /// full { snapshot, timeline, artifacts, diagrams } document or (via LoadFixture) a bare
    /// SnapshotV2. Any missing section defaults to empty. Unknown keys are ignored.
    /// </summary>
    public class Phase2DemoFixture
    {
        [JsonProperty("snapshot")] public SnapshotV2 Snapshot;
        [JsonProperty("timeline")] public List<EventEnvelopeV2> Timeline;
        [JsonProperty("artifacts")] public List<ArtifactV2> Artifacts;
        [JsonProperty("diagrams")] public List<DiagramV2> Diagrams;

        // Stable ids / titles of the built-in sample cards (tests and scene wiring rely on them).
        public const string ExecutableCardId = "demo-build-login";
        public const string ExecutableCardTitle = "Build the login flow (OAuth scaffold)";
        public const string PlannedReleaseNotesId = "demo-release-notes";
        public const string PlannedColdStartId = "demo-profile-cold-start";
        public const string PlannedPasswordResetId = "demo-password-reset";

        /// <summary>
        /// Built-in scripted board used when no fixture file is present. ONE executable card (the
        /// scripted auth_demo run starts when it is assigned to the Researcher) and three planned
        /// cards that are honestly not runnable: they can be assigned, prioritised and paused, but
        /// nothing ever works on them and they never claim to be running.
        /// </summary>
        public static Phase2DemoFixture BuiltInDefault()
        {
            return new Phase2DemoFixture
            {
                Snapshot = new SnapshotV2
                {
                    SessionId = "phase2-scripted-demo-001",
                    LastSeq = 0,
                    SourceMode = SourceModes.Demo,
                    Focus = new FocusSnapshotV2 { Mode = FocusModes.Normal, Revision = 0 },
                    Tasks = new List<TaskCardV2>
                    {
                        new TaskCardV2 { TaskId = ExecutableCardId, Title = ExecutableCardTitle,
                            TaskState = TaskStates.Queued, AgentId = null, Priority = 3, Revision = 0,
                            Dependencies = new List<string>(), SourceMode = SourceModes.Demo,
                            TaskTemplate = TaskTemplates.AuthDemo, Executable = true },
                        Planned(PlannedReleaseNotesId, "Write release notes (planned)", 1),
                        Planned(PlannedColdStartId, "Profile the cold start (planned)", 2),
                        Planned(PlannedPasswordResetId, "Add password reset email (planned)", 0),
                    },
                },
                Timeline = new List<EventEnvelopeV2>(),
                Artifacts = new List<ArtifactV2>(),
                Diagrams = new List<DiagramV2>(),
            };
        }

        static TaskCardV2 Planned(string id, string title, int priority) => new TaskCardV2
        {
            TaskId = id, Title = title, TaskState = TaskStates.Queued, AgentId = null, Priority = priority,
            Revision = 0, Dependencies = new List<string>(), SourceMode = SourceModes.Demo,
            TaskTemplate = null, Executable = false,
        };
    }

    /// <summary>
    /// The labelled sample artifacts the offline demo shows when the scripted run delivers its
    /// work: a code diff of the OAuth scaffold and a test report. They are SAMPLES, not output of a
    /// live agent — every one is source_mode "demo" and every page starts with
    /// <see cref="ScriptedLabel"/>. Loaded from Resources/phase2_demo_artifacts.json when present,
    /// with a built-in fallback so a missing or broken file never stops the demo.
    /// </summary>
    public class Phase2DemoArtifactSamples
    {
        public const string ResourceName = "phase2_demo_artifacts";
        public const string ScriptedLabel = "SCRIPTED SAMPLE (offline demo). Not produced by a live agent.";
        const string SummaryPrefix = "Scripted sample: ";

        [JsonProperty("artifacts")] public List<ArtifactV2> Artifacts = new List<ArtifactV2>();

        public ArtifactV2 Find(string kind)
        {
            if (Artifacts == null) return null;
            foreach (var a in Artifacts)
                if (a != null && a.Kind == kind) return a;
            return null;
        }

        public static Phase2DemoArtifactSamples Load()
        {
            Phase2DemoArtifactSamples samples = null;
            try
            {
                var ta = Resources.Load<TextAsset>(ResourceName);
                if (ta != null && !string.IsNullOrWhiteSpace(ta.text))
                    samples = JsonConvert.DeserializeObject<Phase2DemoArtifactSamples>(ta.text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MuseDesk.P2] demo artifact samples unreadable ({ex.Message}); using built-in samples.");
                samples = null;
            }
            if (!IsUsable(samples)) samples = BuiltIn();
            Normalize(samples);
            return samples;
        }

        static bool IsUsable(Phase2DemoArtifactSamples s)
        {
            if (s == null) return false;
            var diff = s.Find(ArtifactKinds.CodeDiff);
            var report = s.Find(ArtifactKinds.TestReport);
            return diff != null && diff.Pages != null && diff.Pages.Count >= 2
                && report != null && report.Pages != null && report.Pages.Count >= 2;
        }

        /// <summary>Enforce the honesty labels whatever the file said: demo source_mode, a
        /// "Scripted sample" summary, 0-based page numbers and the label as each page's first line.</summary>
        static void Normalize(Phase2DemoArtifactSamples s)
        {
            foreach (var a in s.Artifacts)
            {
                if (a == null) continue;
                a.SourceMode = SourceModes.Demo;
                a.Summary = a.Summary ?? "";
                if (!a.Summary.StartsWith(SummaryPrefix, StringComparison.OrdinalIgnoreCase))
                    a.Summary = SummaryPrefix + a.Summary;
                if (a.Pages == null) a.Pages = new List<ArtifactPage>();
                for (int i = 0; i < a.Pages.Count; i++)
                {
                    var p = a.Pages[i];
                    if (p == null) { p = new ArtifactPage(); a.Pages[i] = p; }
                    p.Page = i;
                    p.Content = p.Content ?? "";
                    if (!p.Content.StartsWith(ScriptedLabel, StringComparison.Ordinal))
                        p.Content = ScriptedLabel + "\n\n" + p.Content;
                    if (p.LineRefs == null) p.LineRefs = new List<string>();
                }
            }
        }

        /// <summary>Short built-in samples (two pages each), used only when the JSON is absent.</summary>
        public static Phase2DemoArtifactSamples BuiltIn()
        {
            return new Phase2DemoArtifactSamples
            {
                Artifacts = new List<ArtifactV2>
                {
                    new ArtifactV2
                    {
                        ArtifactId = "art-demo-oauth-diff", Kind = ArtifactKinds.CodeDiff,
                        Summary = "OAuth login scaffold (code diff)", SourceMode = SourceModes.Demo,
                        ContentHash = "scripted-sample-oauth-diff-builtin",
                        Pages = new List<ArtifactPage>
                        {
                            new ArtifactPage { Page = 0, Title = "Sample diff 1/2: app/auth/oauth_login.py",
                                Content =
                                    "--- /dev/null\n" +
                                    "+++ b/app/auth/oauth_login.py\n" +
                                    "+AUTHORIZE_URL = 'https://id.example.test/oauth/authorize'\n" +
                                    "+\n" +
                                    "+def build_authorize_url(client_id, redirect_uri, state):\n" +
                                    "+    query = urlencode({'response_type': 'code', 'client_id': client_id,\n" +
                                    "+                       'redirect_uri': redirect_uri, 'state': state})\n" +
                                    "+    return AUTHORIZE_URL + '?' + query\n" +
                                    "+\n" +
                                    "+def handle_callback(params, expected_state):\n" +
                                    "+    if params.get('state') != expected_state:\n" +
                                    "+        raise OAuthError('state mismatch')\n" +
                                    "+    return params['code']\n" },
                            new ArtifactPage { Page = 1, Title = "Sample diff 2/2: tests/test_oauth_login.py",
                                Content =
                                    "--- /dev/null\n" +
                                    "+++ b/tests/test_oauth_login.py\n" +
                                    "+def test_authorize_url_carries_state():\n" +
                                    "+    url = oauth.build_authorize_url('app', 'https://app.example.test/cb', 's1')\n" +
                                    "+    assert 'state=s1' in url\n" +
                                    "+\n" +
                                    "+def test_callback_rejects_state_mismatch():\n" +
                                    "+    with pytest.raises(oauth.OAuthError):\n" +
                                    "+        oauth.handle_callback({'state': 'other', 'code': 'c'}, 's1')\n" },
                        },
                    },
                    new ArtifactV2
                    {
                        ArtifactId = "art-demo-oauth-tests", Kind = ArtifactKinds.TestReport,
                        Summary = "OAuth login tests (test report)", SourceMode = SourceModes.Demo,
                        ContentHash = "scripted-sample-oauth-tests-builtin",
                        Pages = new List<ArtifactPage>
                        {
                            new ArtifactPage { Page = 0, Title = "Sample test report 1/2: summary",
                                Content =
                                    "pytest tests/test_oauth_login.py\n" +
                                    "collected 5 items\n\n" +
                                    "tests/test_oauth_login.py .....   [100%]\n\n" +
                                    "5 passed, 0 failed\n" },
                            new ArtifactPage { Page = 1, Title = "Sample test report 2/2: what this shows",
                                Content =
                                    "This report is part of the scripted offline demo.\n" +
                                    "No test was executed on this headset and no code was changed.\n" +
                                    "Connect the desktop companion to see a real run.\n" },
                        },
                    },
                },
            };
        }
    }
}
