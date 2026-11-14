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
    }

    /// <summary>Read-only view of what an agent is currently doing, from the reducers (never from the habitat).</summary>
    public interface IWallTaskContext
    {
        /// <summary>The agent's current task on the board/session, if any. state is the canonical state string.</summary>
        bool TryGetAgentTask(string agentId, out string taskId, out string title, out string state, out int revision);
        /// <summary>LIVE or SCRIPTED DEMO, exactly as the rest of the app labels it.</summary>
        string SourceModeLabel { get; }
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
    }
}
