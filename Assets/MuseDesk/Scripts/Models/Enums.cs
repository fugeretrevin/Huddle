namespace MuseDesk.Models
{
    // String constants mirror the frozen JSON contract values (contracts/*.schema.json).
    // Kept as const strings rather than C# enums so unknown values from the wire never
    // throw during deserialization — unknown types are logged and ignored per contract.

    public static class EventTypes
    {
        public const string SessionReady = "session.ready";
        public const string AgentStatusChanged = "agent.status_changed";
        public const string TaskCreated = "task.created";
        public const string TaskStarted = "task.started";
        public const string TaskProgress = "task.progress";
        public const string DecisionRequested = "decision.requested";
        public const string DecisionResolved = "decision.resolved";
        public const string ArtifactCreated = "artifact.created";
        public const string TaskCompleted = "task.completed";
        public const string TaskFailed = "task.failed";
        public const string SessionReset = "session.reset";
        public const string SystemWarning = "system.warning";
    }

    public static class AgentIds
    {
        public const string Researcher = "researcher";
        public const string Developer = "developer";
        public const string Tester = "tester";
        public const string System = "system";
    }

    public static class AgentStates
    {
        public const string Idle = "idle";
        public const string Researching = "researching";
        public const string Coding = "coding";
        public const string Testing = "testing";
        public const string Reporting = "reporting";
        public const string Approaching = "approaching";
        public const string Waiting = "waiting";
        public const string Complete = "complete";
        public const string Error = "error";
    }

    public static class DecisionActions
    {
        public const string Approve = "approve";
        public const string Reject = "reject";
        public const string RequestChanges = "request_changes";
    }

    public static class SourceModes
    {
        public const string Live = "live";
        public const string Demo = "demo";
    }
}
