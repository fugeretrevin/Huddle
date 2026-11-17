namespace MuseDesk.Phase2.Contracts
{
    // String constants mirror the FROZEN v2 contract enum values (contracts/phase2/*.schema.json
    // and backend/app/phase2/models.py). Kept as const strings — not C# enums — so an unknown
    // value arriving on the wire never throws during deserialization; it is logged / ignored.

    public static class Phase2EventTypes
    {
        // v1 lifecycle types mirrored into the v2 projection
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
        // Phase 2 additive types
        public const string TaskAssigned = "task.assigned";
        public const string TaskPriorityChanged = "task.priority_changed";
        public const string TaskPauseRequested = "task.pause_requested";
        public const string TaskPaused = "task.paused";
        public const string TaskResumed = "task.resumed";
        public const string HandoffCreated = "handoff.created";
        public const string HandoffReceived = "handoff.received";
        public const string HandoffFailed = "handoff.failed";
        public const string ArtifactPageReady = "artifact.page_ready";
        public const string FocusChanged = "focus.changed";
        public const string InboxUpdated = "inbox.updated";
        public const string DiagramCreated = "diagram.created";
        public const string StationMilestone = "station.milestone";
    }

    public static class TaskStates
    {
        public const string Queued = "queued";
        public const string Running = "running";
        public const string AwaitingApproval = "awaiting_approval";
        public const string Blocked = "blocked";
        public const string PauseRequested = "pause_requested";
        public const string Paused = "paused";
        public const string Completed = "completed";
        public const string Failed = "failed";
        public const string Cancelled = "cancelled";
    }

    public static class Phase2AgentIds
    {
        public const string Researcher = "researcher";
        public const string Developer = "developer";
        public const string Tester = "tester";
    }

    public static class HandoffKinds
    {
        public const string Blueprint = "blueprint";
        public const string Implementation = "implementation";
        public const string TestRequest = "test_request";
        public const string TestFailure = "test_failure";
        public const string Review = "review";
    }

    public static class HandoffStates
    {
        public const string Created = "created";
        public const string Received = "received";
        public const string Failed = "failed";
    }

    public static class ArtifactKinds
    {
        public const string CodeDiff = "code_diff";
        public const string TestReport = "test_report";
        public const string ResearchReport = "research_report";
        public const string ImplementationPlan = "implementation_plan";
        public const string Completion = "completion";
    }

    public static class FocusModes
    {
        public const string Normal = "normal";
        public const string Focus = "focus";
        public const string DoNotDisturb = "do_not_disturb";
    }

    public static class DiagramTemplates
    {
        public const string AuthFlow = "auth_flow";
        public const string TaskDependencyGraph = "task_dependency_graph";
        public const string ModuleDependencyGraph = "module_dependency_graph";
    }

    public static class SourceModes
    {
        public const string Live = "live";
        public const string Demo = "demo";
    }

    /// <summary>Stable machine codes returned in a 409 command-conflict body. The client
    /// reconciles from canonical_task and shows the human message; it does not branch on the
    /// code beyond logging / an optional reason string.</summary>
    public static class CommandErrorCodes
    {
        public const string RevisionConflict = "REVISION_CONFLICT";
        public const string RoleIncompatible = "ROLE_INCOMPATIBLE";
        public const string TaskRunning = "TASK_RUNNING";
        public const string TaskNotAssignable = "TASK_NOT_ASSIGNABLE";
        public const string UnsafeTransfer = "UNSAFE_TRANSFER";
        public const string IdempotencyKeyReuse = "IDEMPOTENCY_KEY_REUSE";
        // emitted by pause/resume guards (not in the P2-01 assign set, included for completeness)
        public const string TaskNotPausable = "TASK_NOT_PAUSABLE";
        public const string TaskNotPaused = "TASK_NOT_PAUSED";
        /// <summary>An executable card was assigned while a pipeline run is already in progress.</summary>
        public const string RunInProgress = "RUN_IN_PROGRESS";
        /// <summary>Assign of an executable card that is paused (resume it first).</summary>
        public const string TaskPaused = "TASK_PAUSED";
        /// <summary>Assign of an executable card whose dependencies are not completed.</summary>
        public const string DependenciesNotReady = "DEPENDENCIES_NOT_READY";
        /// <summary>Nothing can run the card right now (HTTP 503 live; in the offline demo the
        /// scripted session is not attached).</summary>
        public const string ExecutorUnavailable = "EXECUTOR_UNAVAILABLE";
        /// <summary>Create with a task_template the backend does not know (HTTP 422).</summary>
        public const string UnknownTaskTemplate = "UNKNOWN_TASK_TEMPLATE";
        /// <summary>Create with a dependency id that does not exist (HTTP 422).</summary>
        public const string UnknownDependency = "UNKNOWN_DEPENDENCY";
        // Run outcome codes carried in a task.failed payload ("code").
        public const string RunFailed = "RUN_FAILED";
        public const string RunInterrupted = "RUN_INTERRUPTED";
    }

    /// <summary>Pipeline templates a card can carry (task_template). Only AuthDemo is executable.</summary>
    public static class TaskTemplates
    {
        public const string AuthDemo = "auth_demo";

        public static bool IsExecutable(string template) => template == AuthDemo;
    }

    /// <summary>
    /// Terminal task states cannot be reassigned / mutated; the board blocks a drop onto them
    /// locally (the server also rejects). Mirrors backend TERMINAL_STATES.
    /// </summary>
    public static class TaskStateSets
    {
        public static bool IsTerminal(string state) =>
            state == TaskStates.Completed || state == TaskStates.Failed || state == TaskStates.Cancelled;

        /// <summary>Safe reassignment checkpoints. Mirrors backend SAFE_ASSIGN_STATES plus
        /// blocked (the backend assign() also admits blocked as a safe checkpoint).</summary>
        public static bool IsSafeAssignCheckpoint(string state) =>
            state == TaskStates.Queued || state == TaskStates.Paused || state == TaskStates.Blocked;

        /// <summary>Running work can never be teleported to another agent from a local gesture.</summary>
        public static bool IsRunning(string state) => state == TaskStates.Running;
    }
}
