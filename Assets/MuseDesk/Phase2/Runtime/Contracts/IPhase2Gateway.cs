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

    /// <summary>
    /// What the offline demo board needs from the scripted (v1) session in order to make an
    /// executable card start a run: the demo equivalent of the backend launching the pipeline when
    /// such a card is assigned to the Researcher. The v2 demo gateway only depends on this; the
    /// implementation (MuseDesk.Demo.DemoBoardRunBridge) owns the v1 side.
    /// </summary>
    public interface IDemoRunLauncher
    {
        /// <summary>True while a scripted run is in flight (from start until it completes, is
        /// rejected, fails or is reset).</summary>
        bool RunInProgress { get; }

        /// <summary>Start the scripted run on behalf of the board card <paramref name="boardTaskId"/>.
        /// Returns false when no run could be started (nothing is emitted in that case).</summary>
        bool TryStartRun(string boardTaskId);
    }

    /// <summary>
    /// A stable IPhase2Gateway that forwards to whichever gateway is currently active. Presenters
    /// keep one reference (and one EventReceived subscription) for the life of the scene, so the
    /// board can move from the offline demo to the live backend without any of them re-binding.
    /// Adds nothing and stamps nothing: ModeLabel / SourceMode / IsLive are the inner gateway's.
    /// </summary>
    public class Phase2GatewayRelay : IPhase2Gateway
    {
        IPhase2Gateway _inner;

        public IPhase2Gateway Inner => _inner;
        public event Action<EventEnvelopeV2> EventReceived;

        public string ModeLabel => _inner != null ? _inner.ModeLabel : "STARTING";
        public string SourceMode => _inner != null ? _inner.SourceMode : SourceModes.Demo;
        public bool IsLive => _inner != null && _inner.IsLive;

        /// <summary>Swap the active gateway. Events from the previous one stop being forwarded.</summary>
        public void SetInner(IPhase2Gateway inner)
        {
            if (_inner != null) _inner.EventReceived -= Forward;
            _inner = inner;
            if (_inner != null) _inner.EventReceived += Forward;
        }

        void Forward(EventEnvelopeV2 e) => EventReceived?.Invoke(e);

        IPhase2Gateway Require()
        {
            if (_inner == null) throw new InvalidOperationException("no v2 gateway is attached yet");
            return _inner;
        }

        public Task<SnapshotV2> GetSnapshotAsync(CancellationToken ct) => Require().GetSnapshotAsync(ct);
        public Task<TaskCardV2> CreateTaskAsync(TaskCreateV2 cmd, CancellationToken ct) => Require().CreateTaskAsync(cmd, ct);
        public Task<Phase2CommandResult<TaskCardV2>> AssignAsync(string taskId, AssignCommand cmd, CancellationToken ct)
            => Require().AssignAsync(taskId, cmd, ct);
        public Task<Phase2CommandResult<TaskCardV2>> SetPriorityAsync(string taskId, PriorityCommand cmd, CancellationToken ct)
            => Require().SetPriorityAsync(taskId, cmd, ct);
        public Task<Phase2CommandResult<TaskCardV2>> PauseAsync(string taskId, PauseCommand cmd, CancellationToken ct)
            => Require().PauseAsync(taskId, cmd, ct);
        public Task<Phase2CommandResult<TaskCardV2>> ResumeAsync(string taskId, ResumeCommand cmd, CancellationToken ct)
            => Require().ResumeAsync(taskId, cmd, ct);
        public Task<FocusPolicyV2> SetFocusAsync(FocusCommand cmd, CancellationToken ct) => Require().SetFocusAsync(cmd, ct);
        public Task<ArtifactPageResponse> GetArtifactPageAsync(string artifactId, int page, int pageSize, CancellationToken ct)
            => Require().GetArtifactPageAsync(artifactId, page, pageSize, ct);
        public Task<DiagramV2> CreateDiagramAsync(DiagramCommand cmd, CancellationToken ct) => Require().CreateDiagramAsync(cmd, ct);
    }
}
