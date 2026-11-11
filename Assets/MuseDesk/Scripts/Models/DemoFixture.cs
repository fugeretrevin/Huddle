using System.Collections.Generic;
using Newtonsoft.Json;

namespace MuseDesk.Models
{
    /// <summary>Mirror of contracts/offline_demo_fixture.json for the standalone demo gateway.</summary>
    public class DemoFixture
    {
        [JsonProperty("schema_version")] public int SchemaVersion = 1;
        [JsonProperty("mode")] public string Mode = "demo";
        [JsonProperty("source_mode")] public string SourceMode = "demo";
        [JsonProperty("title")] public string Title;
        [JsonProperty("agents")] public List<DemoAgent> Agents = new List<DemoAgent>();
        [JsonProperty("decision")] public DemoDecision Decision;
        [JsonProperty("question_answer")] public DemoQuestionAnswer QuestionAnswer;
        [JsonProperty("branches")] public Dictionary<string, DemoBranch> Branches = new Dictionary<string, DemoBranch>();
    }

    public class DemoAgent
    {
        [JsonProperty("agent_id")] public string AgentId;
        [JsonProperty("role")] public string Role;
        [JsonProperty("station")] public string Station;
        [JsonProperty("initial_state")] public string InitialState = "idle";
    }

    public class DemoDecision
    {
        [JsonProperty("decision_id")] public string DecisionId;
        [JsonProperty("title")] public string Title;
        [JsonProperty("summary")] public string Summary;
        [JsonProperty("rationale")] public string Rationale;
        [JsonProperty("risk_level")] public string RiskLevel;
        [JsonProperty("priority")] public string Priority;
        [JsonProperty("allowed_actions")] public List<string> AllowedActions = new List<string>();
    }

    public class DemoQuestionAnswer
    {
        [JsonProperty("question")] public string Question;
        [JsonProperty("answer")] public string Answer;
    }

    public class DemoBranch
    {
        [JsonProperty("developer_state")] public string DeveloperState;
        [JsonProperty("result")] public string Result;
        [JsonProperty("tests_passed")] public int TestsPassed;
    }
}
