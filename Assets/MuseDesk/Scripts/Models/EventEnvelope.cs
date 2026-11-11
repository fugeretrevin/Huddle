using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MuseDesk.Models
{
    /// <summary>
    /// Wire event envelope. Field names are FROZEN against contracts/event.schema.json.
    /// Payload is kept as a JObject so unknown/optional fields survive without crashing.
    /// </summary>
    public class EventEnvelope
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 1;
        [JsonProperty("event_id")] public string EventId;
        [JsonProperty("seq")] public long Seq;
        [JsonProperty("session_id")] public string SessionId;
        [JsonProperty("occurred_at")] public string OccurredAt;
        [JsonProperty("type")] public string Type;
        [JsonProperty("task_id")] public string TaskId;
        [JsonProperty("agent_id")] public string AgentId;
        [JsonProperty("decision_id")] public string DecisionId;
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
    }

    public class SessionCreatedDto
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 1;
        [JsonProperty("session_id")] public string SessionId;
        [JsonProperty("token")] public string Token;
        [JsonProperty("mode")] public string Mode;
        [JsonProperty("created_at")] public string CreatedAt;
        [JsonProperty("last_seq")] public long LastSeq;
    }

    public class DecisionResolutionDto
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 1;
        [JsonProperty("decision_id")] public string DecisionId;
        [JsonProperty("state")] public string State;
        [JsonProperty("action")] public string Action;
        [JsonProperty("resolved_at")] public string ResolvedAt;
        [JsonProperty("applied")] public bool Applied;
    }

    public class QuestionAnswerDto
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 1;
        [JsonProperty("answer")] public string Answer;
        [JsonProperty("evidence")] public List<string> Evidence = new List<string>();
        [JsonProperty("source_mode")] public string SourceMode;
    }
}
