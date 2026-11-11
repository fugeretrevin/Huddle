using System;
using System.Threading;
using System.Threading.Tasks;
using MuseDesk.Models;

namespace MuseDesk.Networking
{
    /// <summary>
    /// The single client-side contract both the live backend and the offline demo
    /// implement. The event reducer is identical regardless of which is active, so
    /// the whole XR experience runs the same offline as online (AGENTS invariant #8).
    /// Implemented by LiveAgentGateway (TASK-07) and DemoAgentGateway (TASK-07);
    /// a minimal stub ships with the foundation to prove scene wiring.
    /// </summary>
    public interface IAgentGateway
    {
        /// <summary>Human-readable mode for the UI badge, e.g. "LIVE" or "DEMO".</summary>
        string ModeLabel { get; }

        Task<SessionSnapshot> StartSessionAsync(CancellationToken ct);
        Task<TaskView> StartTaskAsync(string templateId, string idempotencyKey, CancellationToken ct);
        Task<DecisionResolutionDto> RespondToDecisionAsync(
            string decisionId, string action, string idempotencyKey, string comment, CancellationToken ct);
        Task<QuestionAnswerDto> AskAsync(string agentId, string decisionId, string question, CancellationToken ct);
        Task<SessionSnapshot> GetSnapshotAsync(CancellationToken ct);

        /// <summary>Raised on the Unity main thread, in strict seq order, by the dispatcher.</summary>
        event Action<EventEnvelope> EventReceived;
    }
}
