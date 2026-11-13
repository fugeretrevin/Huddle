using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using MuseDesk.Models;
using MuseDesk.Networking;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Demo
{
    /// <summary>
    /// Connects the offline demo BOARD (v2, <see cref="Phase2DemoGateway"/>) to the offline demo
    /// SESSION (v1, <see cref="DemoAgentGateway"/>), the way the live backend connects a card to
    /// the real pipeline:
    ///
    ///   board → session: when an executable card is assigned to the Researcher the board calls
    ///   <see cref="TryStartRun"/>, which starts the scripted run for that card.
    ///
    ///   session → board: the card then follows the run from the v1 reducer's REAL events
    ///   (decision.requested, decision.resolved, artifact.created, a task.completed that delivered
    ///   work, task.failed, session.reset). No timers and nothing invented here: if the session
    ///   does not say it, the card does not move. Only events that name the card (payload
    ///   board_task_id) are applied, so a run started without a card never moves a card.
    ///
    /// Plain class (no MonoBehaviour) so the same wiring runs in the scene and in the smoke tests.
    /// Main thread only.
    /// </summary>
    public class DemoBoardRunBridge : IDemoRunLauncher, IDisposable
    {
        readonly DemoAgentGateway _session;
        readonly SessionReducer _reducer;
        readonly Phase2DemoGateway _board;
        bool _disposed;
        int _keyCounter;

        /// <param name="session">The scripted v1 session gateway that plays the run.</param>
        /// <param name="reducer">The v1 reducer that <paramref name="session"/>'s events are applied
        /// to (SessionController.Reducer in the scene).</param>
        /// <param name="board">The demo board gateway whose executable cards start the run.</param>
        public DemoBoardRunBridge(DemoAgentGateway session, SessionReducer reducer, Phase2DemoGateway board)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
            _board = board ?? throw new ArgumentNullException(nameof(board));

            _reducer.DecisionRequested += OnDecisionRequested;
            _reducer.DecisionResolved += OnDecisionResolved;
            _reducer.ArtifactCreated += OnArtifactCreated;
            _reducer.WorkDelivered += OnWorkDelivered;
            _reducer.TaskFailed += OnTaskFailed;
            _reducer.SessionReset += OnSessionReset;
            _board.AttachRunLauncher(this);
        }

        public bool RunInProgress => !_disposed && _session.RunInProgress;

        public bool TryStartRun(string boardTaskId)
        {
            if (_disposed || _session.IsClosed || _session.RunInProgress) return false;
            _ = StartAsync(boardTaskId);
            return true;
        }

        async Task StartAsync(string boardTaskId)
        {
            try
            {
                await _session.StartRunAsync("auth_demo", $"board-{boardTaskId}-{++_keyCounter:D4}",
                                             boardTaskId, CancellationToken.None);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogError($"[MuseDesk.Demo] scripted run for card {boardTaskId} could not start: {ex.Message}");
                if (!_disposed && _board.ActiveRunTaskId == boardTaskId)
                    _board.NotifyRunFailed("The scripted session could not start this task.");
            }
        }

        // ── v1 reducer events → the card ──

        bool IsForActiveCard(EventEnvelope e)
        {
            string active = _board.ActiveRunTaskId;
            if (_disposed || active == null || e == null) return false;
            return e.PayloadString("board_task_id") == active;
        }

        void OnDecisionRequested(string decisionId, EventEnvelope e)
        {
            if (IsForActiveCard(e))
            {
                int rev = e.PayloadValue<int>("decision_revision", 0);
                string revises = e.PayloadString("revises_decision_id", null);
                _board.NotifyRunDecisionRequested(decisionId, e.PayloadString("summary"),
                    e.PayloadString("research_provenance"), e.PayloadString("research_provenance_label"),
                    rev, revises);
            }
        }

        void OnDecisionResolved(string decisionId, string state)
        {
            // If changes were requested and a proposal revision will follow, resume the card to
            // running so it awaits the revised decision; otherwise let NotifyRunDecisionResolved end it.
            if (!_disposed)
            {
                string s = (state ?? "").ToLowerInvariant();
                if (s == "changes_requested" && _session.WillReviseOnChanges(decisionId))
                    _board.NotifyRunChangesRequested(decisionId, _session.ProposalRevision + 1);
                else
                    _board.NotifyRunDecisionResolved(decisionId, state);
            }
        }

        void OnArtifactCreated(EventEnvelope e)
        {
            if (IsForActiveCard(e)) _board.NotifyRunArtifactCreated();
        }

        void OnWorkDelivered(EventEnvelope e)
        {
            if (IsForActiveCard(e)) _board.NotifyRunWorkDelivered(e.PayloadString("result_summary"));
        }

        void OnTaskFailed(EventEnvelope e)
        {
            if (IsForActiveCard(e)) _board.NotifyRunFailed(e.PayloadString("reason", e.PayloadString("error")));
        }

        void OnSessionReset()
        {
            if (!_disposed) _board.RestoreSeed();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _reducer.DecisionRequested -= OnDecisionRequested;
            _reducer.DecisionResolved -= OnDecisionResolved;
            _reducer.ArtifactCreated -= OnArtifactCreated;
            _reducer.WorkDelivered -= OnWorkDelivered;
            _reducer.TaskFailed -= OnTaskFailed;
            _reducer.SessionReset -= OnSessionReset;
            _board.AttachRunLauncher(null);
        }
    }
}
