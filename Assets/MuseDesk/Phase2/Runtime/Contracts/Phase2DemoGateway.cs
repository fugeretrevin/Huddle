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
    /// Started from the Unity main thread so awaited delays resume there and EventReceived is raised
    /// on the main thread.
    /// </summary>
    public class Phase2DemoGateway : IPhase2Gateway
    {
        public string ModeLabel => "DEMO · scripted (v2)";
        public string SourceMode => SourceModes.Demo;
        public bool IsLive => false;
        public event Action<EventEnvelopeV2> EventReceived;

        /// <summary>Default fixture path (not a Resources path; lives under StreamingAssets-like
        /// project location). See LoadFixture for lookup order.</summary>
        public const string DefaultFixtureProjectPath = "Assets/MuseDesk/Phase2/Fixtures/phase2_demo.json";
        public const string DefaultFixtureResourceName = "phase2_demo";   // if moved under a Resources/ folder

        readonly Phase2DemoFixture _fx;
        readonly float _step;
        readonly string _sessionId;

        readonly Dictionary<string, TaskCardV2> _tasks = new Dictionary<string, TaskCardV2>();
        readonly Dictionary<string, ArtifactV2> _artifacts = new Dictionary<string, ArtifactV2>();
        readonly Dictionary<string, DiagramV2> _diagrams = new Dictionary<string, DiagramV2>();
        readonly Dictionary<string, object> _idempotency = new Dictionary<string, object>();
        FocusPolicyV2 _focus = new FocusPolicyV2();
        long _seq;

        public Phase2DemoGateway(Phase2DemoFixture fixture, float stepSeconds = 1.0f)
        {
            _fx = fixture ?? Phase2DemoFixture.BuiltInDefault();
            _step = stepSeconds;
            var snap = _fx.Snapshot ?? new SnapshotV2();
            _sessionId = string.IsNullOrEmpty(snap.SessionId) ? "phase2-scripted-demo-001" : snap.SessionId;
            _seq = snap.LastSeq;
            foreach (var t in snap.Tasks) _tasks[t.TaskId] = t.Clone();
            if (snap.Focus != null)
                _focus = new FocusPolicyV2 { Mode = snap.Focus.Mode ?? FocusModes.Normal,
                    Revision = snap.Focus.Revision, Until = snap.Focus.Until };
            if (_fx.Artifacts != null) foreach (var a in _fx.Artifacts) if (a != null) _artifacts[a.ArtifactId] = a;
            if (_fx.Diagrams != null) foreach (var d in _fx.Diagrams) if (d != null) _diagrams[d.DiagramId] = d;
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

        public Task<SnapshotV2> GetSnapshotAsync(CancellationToken ct)
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
            return Task.FromResult(snap);
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

        // ─────────────────────────── commands ───────────────────────────

        public Task<TaskCardV2> CreateTaskAsync(TaskCreateV2 cmd, CancellationToken ct)
        {
            if (TryReplay(cmd.IdempotencyKey, out TaskCardV2 prior)) return Task.FromResult(prior);
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
            };
            _tasks[card.TaskId] = card;
            // Same card fields the live backend sends on task.created, so the new card is not blank.
            EmitTask(Phase2EventTypes.TaskCreated, card, new JObject {
                ["title"] = card.Title, ["task_state"] = card.TaskState, ["priority"] = card.Priority,
                ["revision"] = card.Revision, ["source_mode"] = SourceModes.Demo
            }, agentId: card.AgentId);
            Remember(cmd.IdempotencyKey, card.Clone());
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
                    return Fail(CommandErrorCodes.TaskRunning, "task is running; pause before reassigning", card);
                if (!TaskStateSets.IsSafeAssignCheckpoint(card.TaskState))
                    return Fail(CommandErrorCodes.UnsafeTransfer, $"task state {card.TaskState} is not a safe reassignment checkpoint", card);
                card.AgentId = cmd.TargetAgentId;
                card.Revision += 1;
                EmitTask(Phase2EventTypes.TaskAssigned, card, new JObject {
                    ["target_agent_id"] = cmd.TargetAgentId, ["revision"] = card.Revision, ["source_mode"] = SourceModes.Demo
                }, agentId: cmd.TargetAgentId);
                return Phase2CommandResult<TaskCardV2>.Success(card.Clone());
            });
            Remember(cmd.IdempotencyKey, result);
            return Task.FromResult(result);
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
                EmitTask(Phase2EventTypes.TaskPriorityChanged, card, new JObject {
                    ["priority"] = cmd.Priority, ["revision"] = card.Revision, ["source_mode"] = SourceModes.Demo
                });
                return Phase2CommandResult<TaskCardV2>.Success(card.Clone());
            });
            Remember(cmd.IdempotencyKey, result);
            return Task.FromResult(result);
        }

        public Task<Phase2CommandResult<TaskCardV2>> PauseAsync(string taskId, PauseCommand cmd, CancellationToken ct)
        {
            if (TryReplay(cmd.IdempotencyKey, out Phase2CommandResult<TaskCardV2> prior)) return Task.FromResult(prior);
            var result = RunGuarded(taskId, cmd.ExpectedRevision, card =>
            {
                if (TaskStateSets.IsTerminal(card.TaskState))
                    return Fail(CommandErrorCodes.TaskNotPausable, $"task is {card.TaskState}", card);
                if (card.TaskState == TaskStates.Paused || card.TaskState == TaskStates.PauseRequested)
                    return Phase2CommandResult<TaskCardV2>.Success(card.Clone());   // idempotent no-op
                if (card.TaskState == TaskStates.Running)
                {
                    card.TaskState = TaskStates.PauseRequested;   // cooperative request only
                    card.Revision += 1;
                    EmitTask(Phase2EventTypes.TaskPauseRequested, card, new JObject {
                        ["revision"] = card.Revision, ["source_mode"] = SourceModes.Demo });
                }
                else
                {
                    card.TaskState = TaskStates.Paused;           // queued/blocked: pause immediately
                    card.Revision += 1;
                    EmitTask(Phase2EventTypes.TaskPaused, card, new JObject {
                        ["revision"] = card.Revision, ["source_mode"] = SourceModes.Demo });
                }
                return Phase2CommandResult<TaskCardV2>.Success(card.Clone());
            });
            Remember(cmd.IdempotencyKey, result);
            return Task.FromResult(result);
        }

        public Task<Phase2CommandResult<TaskCardV2>> ResumeAsync(string taskId, ResumeCommand cmd, CancellationToken ct)
        {
            if (TryReplay(cmd.IdempotencyKey, out Phase2CommandResult<TaskCardV2> prior)) return Task.FromResult(prior);
            var result = RunGuarded(taskId, cmd.ExpectedRevision, card =>
            {
                if (card.TaskState != TaskStates.Paused && card.TaskState != TaskStates.PauseRequested)
                    return Fail(CommandErrorCodes.TaskNotPaused, $"task is {card.TaskState}; cannot resume", card);
                card.TaskState = !string.IsNullOrEmpty(card.AgentId) ? TaskStates.Running : TaskStates.Queued;
                card.Revision += 1;
                EmitTask(Phase2EventTypes.TaskResumed, card, new JObject {
                    ["revision"] = card.Revision, ["task_state"] = card.TaskState, ["source_mode"] = SourceModes.Demo });
                return Phase2CommandResult<TaskCardV2>.Success(card.Clone());
            });
            Remember(cmd.IdempotencyKey, result);
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

        public Task<ArtifactPageResponse> GetArtifactPageAsync(string artifactId, int page, int pageSize, CancellationToken ct)
        {
            if (_artifacts.TryGetValue(artifactId, out var art))
            {
                int total = Math.Max(1, (int)Math.Ceiling(art.Pages.Count / (double)Math.Max(1, pageSize)));
                var slice = new List<ArtifactPage>();
                for (int i = page * pageSize; i < Math.Min(art.Pages.Count, (page + 1) * pageSize); i++)
                    slice.Add(art.Pages[i]);
                return Task.FromResult(new ArtifactPageResponse
                {
                    ArtifactId = art.ArtifactId, TaskId = art.TaskId, Kind = art.Kind, Summary = art.Summary,
                    SourceMode = SourceModes.Demo, ContentHash = art.ContentHash, CreatedAt = art.CreatedAt,
                    Page = page, PageSize = pageSize, TotalPages = total, Pages = slice, Stale = false,
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
            if (!_tasks.TryGetValue(taskId, out var card))
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

        /// <summary>Minimal built-in scripted state so the board + drag-assign demo runs even when
        /// no fixture file is present. Two backlog cards a user can grab and drop onto a station.</summary>
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
                        new TaskCardV2 { TaskId = "demo-research-auth", Title = "Compare auth options",
                            TaskState = TaskStates.Queued, AgentId = null, Priority = 2, Revision = 0,
                            Dependencies = new List<string>(), SourceMode = SourceModes.Demo },
                        new TaskCardV2 { TaskId = "demo-build-login", Title = "Build login UI",
                            TaskState = TaskStates.Queued, AgentId = null, Priority = 1, Revision = 0,
                            Dependencies = new List<string>(), SourceMode = SourceModes.Demo },
                    },
                },
                Timeline = new List<EventEnvelopeV2>(),
                Artifacts = new List<ArtifactV2>(),
                Diagrams = new List<DiagramV2>(),
            };
        }
    }
}
