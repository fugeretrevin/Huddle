using System;
using System.Threading;
using System.Threading.Tasks;

namespace MuseDesk.Phase2.Contracts
{
    /// <summary>
    /// The single client-side contract both the live backend (Phase2LiveGateway) and the
    /// offline scripted demo (Phase2DemoGateway) implement for the Phase 2 (v2) surface.
    /// The Phase2Reducer and every presenter run identically regardless of which is active,
    /// so the whole board / handoff / artifact experience is the same offline as online
    /// (AGENTS invariant #8).
    ///
    /// Boundaries (master §2):
    ///  - This is ADDITIVE and sits alongside the Phase 1 IAgentGateway; it never replaces it.
    ///  - Every method is called from the Unity main thread. EventReceived is raised on the
    ///    main thread (the live gateway marshals WS frames through Phase2EventChannel.Update).
    ///  - The commands are idempotent by (session, route, idempotency_key, body-hash); the
    ///    caller supplies an idempotency_key with each command and reuses the SAME key on retry.
    ///  - A command conflict (HTTP 409) is surfaced as a Phase2CommandResult with Ok=false and a
    ///    parsed CommandErrorBody (never thrown); transport / other server errors still throw.
    ///  - source_mode is stamped/propagated on everything (SourceMode below).
    /// </summary>
    public interface IPhase2Gateway
    {
        /// <summary>Human-readable mode for the UI badge, e.g. "LIVE · v2" or "DEMO · scripted".</summary>
        string ModeLabel { get; }

        /// <summary>"live" or "demo" — the canonical source_mode this gateway stamps. Never
        /// inferred from connection alone (master §3).</summary>
        string SourceMode { get; }

        bool IsLive { get; }

        /// <summary>Fetch the authoritative v2 snapshot (used to hydrate and to re-sync on a seq gap).</summary>
        Task<SnapshotV2> GetSnapshotAsync(CancellationToken ct);

        // ─── commands ───
        // Each returns the CANONICAL task card on success. The caller reconciles to it and
        // only shows a confirmed state after the server (or the demo's authoritative local
        // state) acknowledges. A 409 returns Ok=false with the conflict body.

        Task<TaskCardV2> CreateTaskAsync(TaskCreateV2 cmd, CancellationToken ct);
        Task<Phase2CommandResult<TaskCardV2>> AssignAsync(string taskId, AssignCommand cmd, CancellationToken ct);
        Task<Phase2CommandResult<TaskCardV2>> SetPriorityAsync(string taskId, PriorityCommand cmd, CancellationToken ct);
        Task<Phase2CommandResult<TaskCardV2>> PauseAsync(string taskId, PauseCommand cmd, CancellationToken ct);
        Task<Phase2CommandResult<TaskCardV2>> ResumeAsync(string taskId, ResumeCommand cmd, CancellationToken ct);

        Task<FocusPolicyV2> SetFocusAsync(FocusCommand cmd, CancellationToken ct);
        Task<ArtifactPageResponse> GetArtifactPageAsync(string artifactId, int page, int pageSize, CancellationToken ct);
        Task<DiagramV2> CreateDiagramAsync(DiagramCommand cmd, CancellationToken ct);

        /// <summary>Raised on the Unity main thread, in v2 seq order, for each v2 event.</summary>
        event Action<EventEnvelopeV2> EventReceived;
    }
}
