using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MuseDesk.Phase2.Contracts
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 2 (schema_version:2) client DTOs.
    //
    // Field names are FROZEN against contracts/phase2/*.schema.json and the backend
    // Pydantic models in backend/app/phase2/models.py. Do NOT rename a JSON field or
    // change an enum string without a coordinated migration across contracts, backend,
    // this file, fixtures and tests (AGENTS invariant #4).
    //
    // These live in their own namespace and are entirely separate from the v1 DTOs in
    // MuseDesk.Models. v1 and v2 each have their own monotonic seq space and their own
    // event log; the v2 snapshot last_seq and the WS after_seq are v2 seq only.
    //
    // Serialization lib matches the rest of the project: Newtonsoft.Json with explicit
    // [JsonProperty] names (NOT Unity JsonUtility, which cannot round-trip nullable
    // ints / dictionaries / the JObject payload).
    // ─────────────────────────────────────────────────────────────────────────

    // ─── snapshot entities ───

    /// <summary>A board task card. agent_id is null when unassigned.</summary>
    public class TaskCardV2
    {
        [JsonProperty("task_id")] public string TaskId;
        [JsonProperty("title")] public string Title;
        [JsonProperty("task_state")] public string TaskState;
        [JsonProperty("agent_id")] public string AgentId;          // nullable
        [JsonProperty("priority")] public int Priority;             // 0..3
        [JsonProperty("revision")] public int Revision;
        [JsonProperty("dependencies")] public List<string> Dependencies = new List<string>();
        [JsonProperty("source_mode")] public string SourceMode;    // "live" | "demo"
        /// <summary>Pipeline template this card runs, or null. Only "auth_demo" is executable.</summary>
        [JsonProperty("task_template")] public string TaskTemplate; // nullable
        /// <summary>True when assigning this card to the Researcher starts a real (or, in the demo,
        /// scripted) pipeline run. False = a planned card: commands change the card only.</summary>
        [JsonProperty("executable")] public bool Executable;
        /// <summary>The v1 parent task id of the pipeline run this card launched; null until launched.</summary>
        [JsonProperty("run_task_id")] public string RunTaskId;     // nullable
        /// <summary>The decision this card is waiting on while task_state is awaiting_approval.</summary>
        [JsonProperty("decision_id", NullValueHandling = NullValueHandling.Ignore)] public string DecisionId;

        public TaskCardV2 Clone() => new TaskCardV2
        {
            TaskId = TaskId, Title = Title, TaskState = TaskState, AgentId = AgentId,
            Priority = Priority, Revision = Revision,
            Dependencies = Dependencies != null ? new List<string>(Dependencies) : new List<string>(),
            SourceMode = SourceMode,
            TaskTemplate = TaskTemplate, Executable = Executable, RunTaskId = RunTaskId,
            DecisionId = DecisionId
        };
    }

    /// <summary>A typed agent-to-agent handoff (courier) record.</summary>
    public class HandoffV2
    {
        [JsonProperty("handoff_id")] public string HandoffId;
        [JsonProperty("from_agent_id")] public string FromAgentId;
        [JsonProperty("to_agent_id")] public string ToAgentId;
        [JsonProperty("task_id")] public string TaskId;
        [JsonProperty("artifact_id")] public string ArtifactId;
        [JsonProperty("kind")] public string Kind;     // blueprint|implementation|test_request|test_failure|review
        [JsonProperty("state")] public string State;   // created|received|failed
        [JsonProperty("source_mode")] public string SourceMode;
    }

    /// <summary>Lightweight artifact reference carried in the snapshot. The full body
    /// (summary, pages) is fetched on demand via GetArtifactPageAsync.</summary>
    public class ArtifactRefV2
    {
        [JsonProperty("artifact_id")] public string ArtifactId;
        [JsonProperty("kind")] public string Kind;     // code_diff|test_report|research_report|implementation_plan|completion
        [JsonProperty("source_mode")] public string SourceMode;
    }

    /// <summary>Trimmed focus projection carried in the snapshot.</summary>
    public class FocusSnapshotV2
    {
        [JsonProperty("mode")] public string Mode;      // normal|focus|do_not_disturb
        [JsonProperty("revision")] public int Revision;
        [JsonProperty("until", NullValueHandling = NullValueHandling.Ignore)] public string Until;
    }

    public class InboxItemV2
    {
        [JsonProperty("item_id")] public string ItemId;
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("summary")] public string Summary;
        [JsonProperty("priority")] public int Priority;
        [JsonProperty("critical")] public bool Critical;
        [JsonProperty("created_at")] public string CreatedAt;
        [JsonProperty("source_mode")] public string SourceMode;
    }

    /// <summary>The authoritative v2 snapshot used to (re)hydrate on connect and on a seq gap.</summary>
    public class SnapshotV2
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 2;
        [JsonProperty("session_id")] public string SessionId;
        [JsonProperty("last_seq")] public long LastSeq;           // v2 seq space
        [JsonProperty("source_mode")] public string SourceMode;   // "live" | "demo"
        [JsonProperty("tasks")] public List<TaskCardV2> Tasks = new List<TaskCardV2>();
        [JsonProperty("handoffs")] public List<HandoffV2> Handoffs = new List<HandoffV2>();
        [JsonProperty("artifacts")] public List<ArtifactRefV2> Artifacts = new List<ArtifactRefV2>();
        [JsonProperty("focus")] public FocusSnapshotV2 Focus = new FocusSnapshotV2();
        [JsonProperty("inbox")] public List<InboxItemV2> Inbox = new List<InboxItemV2>();
    }

    // ─── event envelope ───

    /// <summary>
    /// v2 event envelope. The wire format OMITS unset optional id fields (task_id, agent_id,
    /// artifact_id, handoff_id) — the reducer treats absence as "not applicable". Newtonsoft
    /// leaves omitted fields null on deserialize, which is exactly that. Payload stays a
    /// JObject so unknown / additive payload keys survive without throwing.
    /// </summary>
    public class EventEnvelopeV2
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 2;
        [JsonProperty("event_id")] public string EventId;
        [JsonProperty("seq")] public long Seq;
        [JsonProperty("session_id")] public string SessionId;
        [JsonProperty("occurred_at")] public string OccurredAt;
        [JsonProperty("type")] public string Type;
        [JsonProperty("task_id", NullValueHandling = NullValueHandling.Ignore)] public string TaskId;
        [JsonProperty("agent_id", NullValueHandling = NullValueHandling.Ignore)] public string AgentId;
        [JsonProperty("artifact_id", NullValueHandling = NullValueHandling.Ignore)] public string ArtifactId;
        [JsonProperty("handoff_id", NullValueHandling = NullValueHandling.Ignore)] public string HandoffId;
        [JsonProperty("payload")] public JObject Payload = new JObject();

        public string PayloadString(string key, string fallback = null)
        {
            if (Payload != null && Payload.TryGetValue(key, out var token))
                return token.Type == JTokenType.Null ? fallback : token.ToString();
            return fallback;
        }

        public T PayloadValue<T>(string key, T fallback = default)
        {
            if (Payload != null && Payload.TryGetValue(key, out var token) && token.Type != JTokenType.Null)
            {
                try { return token.ToObject<T>(); } catch { return fallback; }
            }
            return fallback;
        }

        public bool TryPayloadInt(string key, out int value)
        {
            value = 0;
            if (Payload != null && Payload.TryGetValue(key, out var token) &&
                (token.Type == JTokenType.Integer || token.Type == JTokenType.Float))
            {
                value = token.Value<int>();
                return true;
            }
            return false;
        }
    }

    // ─── full artifact DTOs (P2-02 uses these; included for completeness) ───

    public class ArtifactPage
    {
        [JsonProperty("page")] public int Page;
        [JsonProperty("title")] public string Title;
        [JsonProperty("content")] public string Content;
        [JsonProperty("line_refs")] public List<string> LineRefs = new List<string>();
    }

    public class ArtifactV2
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 2;
        [JsonProperty("artifact_id")] public string ArtifactId;
        [JsonProperty("task_id")] public string TaskId;
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("summary")] public string Summary;
        [JsonProperty("source_mode")] public string SourceMode;
        [JsonProperty("content_hash")] public string ContentHash;
        [JsonProperty("created_at")] public string CreatedAt;
        [JsonProperty("pages")] public List<ArtifactPage> Pages = new List<ArtifactPage>();
    }

    /// <summary>Paginated response from GET /v2/.../artifacts/{aid}?page=&amp;page_size=.</summary>
    public class ArtifactPageResponse
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 2;
        [JsonProperty("artifact_id")] public string ArtifactId;
        [JsonProperty("task_id")] public string TaskId;
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("summary")] public string Summary;
        [JsonProperty("source_mode")] public string SourceMode;
        [JsonProperty("content_hash")] public string ContentHash;
        [JsonProperty("created_at")] public string CreatedAt;
        [JsonProperty("page")] public int Page;
        [JsonProperty("page_size")] public int PageSize;
        [JsonProperty("total_pages")] public int TotalPages;
        [JsonProperty("pages")] public List<ArtifactPage> Pages = new List<ArtifactPage>();
        [JsonProperty("stale")] public bool Stale;
    }

    // ─── focus policy (full entity) ───

    public class FocusPolicyV2
    {
        [JsonProperty("mode")] public string Mode = "normal";
        [JsonProperty("until", NullValueHandling = NullValueHandling.Ignore)] public string Until;
        [JsonProperty("quiet_priorities")] public List<int> QuietPriorities = new List<int>();
        [JsonProperty("updated_at", NullValueHandling = NullValueHandling.Ignore)] public string UpdatedAt;
        [JsonProperty("revision")] public int Revision;
    }

    // ─── diagram DTOs (P2-04 uses these; included for completeness) ───

    public class DiagramNode
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("label")] public string Label;
        [JsonProperty("icon")] public string Icon;    // file|code|database|user|shield|test|service|queue
        [JsonProperty("evidence_ref", NullValueHandling = NullValueHandling.Ignore)] public string EvidenceRef;
    }

    public class DiagramEdge
    {
        [JsonProperty("source")] public string Source;
        [JsonProperty("target")] public string Target;
        [JsonProperty("kind")] public string Kind;    // data_flow|depends_on|calls|tests
    }

    public class DiagramV2
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 2;
        [JsonProperty("diagram_id")] public string DiagramId;
        [JsonProperty("template")] public string Template; // auth_flow|task_dependency_graph|module_dependency_graph
        [JsonProperty("source_mode")] public string SourceMode;
        [JsonProperty("nodes")] public List<DiagramNode> Nodes = new List<DiagramNode>();
        [JsonProperty("edges")] public List<DiagramEdge> Edges = new List<DiagramEdge>();
    }

    // ─── station layout (P2-05 — local device storage only, never uploaded with room geometry) ───

    public class StationPose
    {
        [JsonProperty("station_id")] public string StationId;
        [JsonProperty("position")] public float[] Position = new float[3];
        [JsonProperty("rotation")] public float[] Rotation = new float[4];
    }

    public class StationLayoutV2
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 2;
        [JsonProperty("local_anchor_ref")] public string LocalAnchorRef;
        [JsonProperty("layout_revision")] public int LayoutRevision;
        [JsonProperty("station_poses")] public List<StationPose> StationPoses = new List<StationPose>();
        [JsonProperty("scale")] public float Scale = 1.0f;
    }

    /// <summary>Non-authoritative, idempotent visual cue that can be skipped safely.</summary>
    public class VisualCueV2
    {
        [JsonProperty("cue_id")] public string CueId;
        [JsonProperty("cue_kind")] public string CueKind;
        [JsonProperty("canonical_ref")] public string CanonicalRef;
        [JsonProperty("status")] public string Status = "pending";
    }

    // ─── command bodies (POSTed to /v2 endpoints) ───

    public class AssignCommand
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 2;
        [JsonProperty("target_agent_id")] public string TargetAgentId;  // researcher|developer|tester
        [JsonProperty("expected_revision")] public int ExpectedRevision;
        [JsonProperty("idempotency_key")] public string IdempotencyKey; // 8..200 chars
    }

    public class PriorityCommand
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 2;
        [JsonProperty("priority")] public int Priority;                 // 0..3
        [JsonProperty("expected_revision")] public int ExpectedRevision;
        [JsonProperty("idempotency_key")] public string IdempotencyKey;
    }

    public class PauseCommand
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 2;
        [JsonProperty("expected_revision")] public int ExpectedRevision;
        [JsonProperty("idempotency_key")] public string IdempotencyKey;
    }

    public class ResumeCommand
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 2;
        [JsonProperty("expected_revision")] public int ExpectedRevision;
        [JsonProperty("idempotency_key")] public string IdempotencyKey;
    }

    public class FocusCommand
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 2;
        [JsonProperty("mode")] public string Mode;    // normal|focus|do_not_disturb
        [JsonProperty("until", NullValueHandling = NullValueHandling.Ignore)] public string Until;
        [JsonProperty("quiet_priorities")] public List<int> QuietPriorities = new List<int>();
        [JsonProperty("idempotency_key")] public string IdempotencyKey;
    }

    public class DiagramCommand
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 2;
        [JsonProperty("template")] public string Template;
        [JsonProperty("task_id", NullValueHandling = NullValueHandling.Ignore)] public string TaskId;
        [JsonProperty("artifact_id", NullValueHandling = NullValueHandling.Ignore)] public string ArtifactId;
        [JsonProperty("idempotency_key")] public string IdempotencyKey;
    }

    /// <summary>Additive foundation endpoint that seeds board task cards (POST /v2/sessions/{sid}/tasks).</summary>
    public class TaskCreateV2
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 2;
        [JsonProperty("title")] public string Title;
        [JsonProperty("agent_id", NullValueHandling = NullValueHandling.Ignore)] public string AgentId;
        [JsonProperty("priority")] public int Priority = 1;
        [JsonProperty("dependencies")] public List<string> Dependencies = new List<string>();
        [JsonProperty("role_lock", NullValueHandling = NullValueHandling.Ignore)] public string RoleLock;
        /// <summary>Optional pipeline template. Only "auth_demo" makes the card executable; omitted
        /// from the wire when null so a plain card create is unchanged.</summary>
        [JsonProperty("task_template", NullValueHandling = NullValueHandling.Ignore)] public string TaskTemplate;
        [JsonProperty("idempotency_key")] public string IdempotencyKey;
    }

    // ─── command error body (HTTP 409) ───

    /// <summary>
    /// Command-conflict error body: { code, message, canonical_task? }. The client clears its
    /// local pending state, reconciles from canonical_task, and keeps the readable message.
    /// </summary>
    public class CommandErrorBody
    {
        [JsonProperty("code")] public string Code;
        [JsonProperty("message")] public string Message;
        [JsonProperty("canonical_task", NullValueHandling = NullValueHandling.Ignore)] public TaskCardV2 CanonicalTask;
    }

    /// <summary>Result wrapper for the task commands: either the canonical card, or a parsed
    /// 409 conflict body. Transport / server errors other than a command conflict still throw.</summary>
    public class Phase2CommandResult<T>
    {
        public bool Ok;
        public T Value;
        public CommandErrorBody Error;    // populated when !Ok

        public static Phase2CommandResult<T> Success(T value) =>
            new Phase2CommandResult<T> { Ok = true, Value = value };

        public static Phase2CommandResult<T> Conflict(CommandErrorBody error) =>
            new Phase2CommandResult<T> { Ok = false, Error = error };
    }
}
