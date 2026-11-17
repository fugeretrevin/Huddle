using System.Collections.Generic;
using Newtonsoft.Json;

namespace MuseDesk.Models
{
    public class AgentView
    {
        [JsonProperty("agent_id")] public string AgentId;
        [JsonProperty("role")] public string Role;
        [JsonProperty("display_name")] public string DisplayName;
        [JsonProperty("state")] public string State;
        /// <summary>The agent's own latest progress line (agent.status_changed.progress_message).</summary>
        [JsonProperty("progress_message")] public string Progress;
    }

    public class TaskView
    {
        [JsonProperty("task_id")] public string TaskId;
        [JsonProperty("agent_id")] public string AgentId;
        [JsonProperty("parent_task_id")] public string ParentTaskId;
        [JsonProperty("status")] public string Status;
        [JsonProperty("template")] public string Template;
        [JsonProperty("dependencies")] public List<string> Dependencies = new List<string>();
    }

    public class DecisionView
    {
        [JsonProperty("decision_id")] public string DecisionId;
        [JsonProperty("task_id")] public string TaskId;
        [JsonProperty("agent_id")] public string AgentId;
        [JsonProperty("state")] public string State;
        [JsonProperty("summary")] public string Summary;
        [JsonProperty("recommendation")] public string Recommendation;
    }

    /// <summary>Server-authoritative snapshot used to reconcile on (re)connect.</summary>
    public class SessionSnapshot
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 1;
        [JsonProperty("session_id")] public string SessionId;
        [JsonProperty("mode")] public string Mode;
        [JsonProperty("last_seq")] public long LastSeq;
        [JsonProperty("agents")] public List<AgentView> Agents = new List<AgentView>();
        [JsonProperty("tasks")] public List<TaskView> Tasks = new List<TaskView>();
        [JsonProperty("decisions")] public List<DecisionView> Decisions = new List<DecisionView>();
    }

    /// <summary>Decision content as carried in a decision.requested payload.</summary>
    public class DecisionContent
    {
        [JsonProperty("title")] public string Title;
        [JsonProperty("summary")] public string Summary;
        [JsonProperty("recommendation")] public string Recommendation;
        [JsonProperty("rationale")] public string Rationale;
        [JsonProperty("risk_level")] public string RiskLevel;
        [JsonProperty("priority")] public string Priority;
        [JsonProperty("impact")] public string Impact;
        [JsonProperty("source_mode")] public string SourceMode;
        /// <summary>Where the recommendation came from, e.g. "template" (a fixed recommendation,
        /// not live AI research). Null when the backend does not say.</summary>
        [JsonProperty("research_provenance")] public string ResearchProvenance;
        /// <summary>Short sentence for the paper, e.g. "Template recommendation. Not live AI research."</summary>
        [JsonProperty("research_provenance_label")] public string ResearchProvenanceLabel;
        [JsonProperty("allowed_actions")] public List<string> AllowedActions = new List<string>();
        [JsonProperty("alternatives")] public List<DecisionAlternative> Alternatives = new List<DecisionAlternative>();
    }

    public class DecisionAlternative
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("label")] public string Label;
        [JsonProperty("tradeoff")] public string Tradeoff;
    }
}
