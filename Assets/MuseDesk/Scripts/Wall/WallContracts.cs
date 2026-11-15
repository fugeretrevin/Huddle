using System;
using System.Collections.Generic;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Wall
{
    /// <summary>Result of proposing a Manager plan (POST /v2/sessions/{sid}/manager/plans, or the offline demo).</summary>
    public sealed class ManagerPlanResult
    {
        public bool ok;
        public string errorCode;
        public string error;
        public string planId;
        public int revision;
        public string title;
        /// <summary>LIVE_MODEL, TEMPLATE_PLAN, SCRIPTED_DEMO.</summary>
        public string provenance;
        public List<ManagerPlanItemView> items = new List<ManagerPlanItemView>();
        /// <summary>The plan's own descriptive fields, exactly as sent (empty when the source gave none).</summary>
        public string objective;
        public string[] assumptions = new string[0];
        public string[] risks = new string[0];
    }

    /// <summary>Answer to a Retry poke on a failed plan card.</summary>
    public sealed class PlanRetryResult
    {
        /// <summary>The backend accepted the retry (or said it had already been retried).</summary>
        public bool ok;
        /// <summary>False when no answer arrived (transport): the same idempotency key must be reused.</summary>
        public bool definitive;
        public bool retried;
        public string state;
        public int revision = -1;
        public string code;
        public string message;
    }

    /// <summary>Result of committing a plan. tasks carry CANONICAL task ids from the board service.</summary>
    public sealed class ManagerCommitResult
    {
        public bool ok;
        public string code;
        public string message;
        public int revision;
        public List<ManagerPrintJob> tasks = new List<ManagerPrintJob>();
    }

    /// <summary>
    /// Everything the Wall Habitat asks the backend. Live: the additive /v2 Phase 3 routes
    /// (docs/Engineering/WALL_HABITAT_API_CONTRACTS.md) with the v1 session's id and bearer token.
    /// Offline: honest local fallbacks, always labelled (OFFLINE_SNAPSHOT / SCRIPTED_DEMO), never "live".
    /// Callbacks run on the Unity main thread. No method here can change a task except CommitPlan,
    /// which the user triggers explicitly from the plan card.
    /// </summary>
    public interface IWallBackend
    {
        /// <summary>True when the live backend session is in use (not the offline demo).</summary>
        bool IsLive { get; }
        void AskSide(string agentId, string taskId, string visitSessionId, string question, int contextRevision,
                     string requestId, string idempotencyKey, Action<SideExchange> done);
        void ClearVisit(string agentId, string visitSessionId);
        void ProposePlan(string requestId, string objective, string idempotencyKey, Action<ManagerPlanResult> done);
        void CommitPlan(string planId, int revision, List<string> approvedItemIds, List<string> authorizedDangerousItemIds,
                        string idempotencyKey, Action<ManagerCommitResult> done);
        void RejectPlan(string planId, int revision, string reason, string idempotencyKey, Action<bool, string> done);
        /// <summary>Replace an OPEN plan's steps with <paramref name="keptItems"/> (a new revision to review). Creates nothing.</summary>
        void RevisePlan(string planId, int revision, List<ManagerPlanItemView> keptItems, string idempotencyKey, Action<ManagerPlanResult> done);
        /// <summary>Ask for a FAILED card of a committed plan to be queued again. The only other command besides CommitPlan
        /// that changes a task, and only on the user's explicit Retry poke.</summary>
        void RetryPlanCard(string planId, string taskId, string idempotencyKey, Action<PlanRetryResult> done);
        /// <summary>Read a committed plan's cards (state, blocked reason, failure) and its summary. done(null) when unavailable.</summary>
        void GetPlanStatus(string planId, Action<WallParsedPlanStatus> done);
    }

    /// <summary>Read-only view of what an agent is currently doing, from the reducers (never from the habitat).</summary>
    public interface IWallTaskContext
    {
        /// <summary>The agent's current task on the board/session, if any. state is the canonical state string.</summary>
        bool TryGetAgentTask(string agentId, out string taskId, out string title, out string state, out int revision);
        /// <summary>LIVE or SCRIPTED DEMO, exactly as the rest of the app labels it.</summary>
        string SourceModeLabel { get; }
        /// <summary>Fetch the first page of an artifact (title, content) from the active v2 gateway. done(null, null) when it
        /// cannot be read. Nothing is made up.</summary>
        void FetchArtifactText(string artifactId, Action<string, string> done);
    }

    /// <summary>
    /// Real backend facts translated for the habitat. Raised on the main thread from the existing reducers
    /// only (v1 SessionReducer, v2 Phase2Reducer); never synthesised to justify an animation.
    /// </summary>
    public interface IWallEventSource
    {
        event Action<HabitatEvent> HabitatEventRaised;
        /// <summary>A task.created reached the board reducer (any source). The manager printer needs it.</summary>
        event Action<string> BoardTaskCreated;
        /// <summary>The session was reset or replayed: everything visual goes home.</summary>
        event Action SessionReset;
        /// <summary>A task event was applied by the board reducer: the card's state after it, plus the reasons the event carried.</summary>
        event Action<PlanCardUpdate> PlanCardReported;
        /// <summary>artifact.created with kind completion (artifactId, anchor taskId, summary).</summary>
        event Action<string, string, string> CompletionArtifactCreated;
        /// <summary>manager.plan_committed on the stream (planId, revision, canonical cards).</summary>
        event Action<string, int, List<ManagerPrintJob>> PlanCommittedAnnounced;
        /// <summary>system.warning on the stream (code, message), e.g. NO_MODEL_CONFIGURED.</summary>
        event Action<string, string> SystemWarning;
    }
}
