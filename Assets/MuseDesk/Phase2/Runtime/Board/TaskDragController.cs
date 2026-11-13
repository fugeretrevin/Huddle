using System;
using System.Threading.Tasks;
using UnityEngine;
using MuseDesk.Phase2.Contracts;

namespace MuseDesk.Phase2.Board
{
    /// <summary>
    /// The per-card drag state machine (master §4.4 / §11.1):
    ///     Idle → Held → Candidate → Pending → Confirmed / Error → Idle
    ///
    /// Hands-first. The card is physically moved by the project's Oculus.Interaction hand-grab
    /// (HandGrabInteractable + Grabbable on the prefab); this controller never invents an ISDK API —
    /// it only READS grab state via TaskCardView.IsHeld (reflection over Grabbable.SelectingPointsCount,
    /// the same signal the existing GrabbableRigidbody uses) and reacts to it.
    ///
    /// Safety rules enforced here:
    ///  - A network command is issued ONLY on a confirmed release over a valid zone. A drop over an
    ///    invalid target or empty space returns the card home and issues NO server call.
    ///  - On tracking loss / release-outside / app background while still held (pre-command), the card
    ///    returns home and nothing is sent.
    ///  - On 409 the card animates back and the presenter reconciles from canonical_task; success is
    ///    never synthesized locally.
    ///  - Once Pending (command already sent) we do NOT cancel — the command is idempotent; on
    ///    uncertainty the presenter resyncs from the canonical snapshot.
    /// </summary>
    public class TaskDragController : MonoBehaviour
    {
        public enum DragState { Idle, Held, Candidate, Pending, Confirmed, Error }

        [Tooltip("Max distance (m) from a zone's bounds within which a hovered card snaps to it.")]
        public float snapRadius = 0.15f;
        [Tooltip("Seconds the Confirmed/Error visual holds before returning to Idle.")]
        public float resolveHoldSeconds = 0.6f;

        public DragState State { get; private set; } = DragState.Idle;

        BoardPresenter _presenter;
        TaskCardView _card;
        StationDropZone _candidate;          // currently highlighted valid target
        StationDropZone _invalidHover;       // nearest zone shown red when not a valid target
        bool _resolving;                     // an async assign is in flight / settling

        /// <summary>Called by BoardPresenter when this card view is (re)used.</summary>
        public void Bind(BoardPresenter presenter, TaskCardView card)
        {
            _presenter = presenter;
            _card = card;
            ResetToIdle(returnHome: false);
        }

        void Update()
        {
            if (_presenter == null || _card?.Data == null || _resolving) return;

            bool held = _card.IsHeld;

            switch (State)
            {
                case DragState.Idle:
                    if (held) State = DragState.Held;
                    break;

                case DragState.Held:
                case DragState.Candidate:
                    if (!held) { OnReleased(); break; }
                    UpdateHover();
                    break;

                // Pending/Confirmed/Error are advanced by the async flow, not by hold changes.
            }
        }

        void UpdateHover()
        {
            Vector3 p = _card.transform.position;
            var valid = StationDropZone.ResolveNearestValid(p, _card.Data, snapRadius);

            if (valid != _candidate)
            {
                if (_candidate != null) _candidate.SetHighlight(false, true);
                _candidate = valid;
                if (_candidate != null) _candidate.SetHighlight(true, valid: true);
            }

            // If not over a valid zone, show a red halo on the nearest zone we ARE hovering (feedback
            // that the drop would be rejected), e.g. a running task over a station.
            if (_candidate == null)
            {
                var any = StationDropZone.ResolveNearestAny(p, snapRadius);
                if (any != _invalidHover)
                {
                    if (_invalidHover != null && _invalidHover != _candidate) _invalidHover.SetHighlight(false, true);
                    _invalidHover = any;
                    if (_invalidHover != null) _invalidHover.SetHighlight(true, valid: false);
                }
                State = DragState.Held;
            }
            else
            {
                if (_invalidHover != null) { _invalidHover.SetHighlight(false, true); _invalidHover = null; }
                State = DragState.Candidate;
            }
        }

        void OnReleased()
        {
            // Read both targets BEFORE clearing the halos: ClearHighlights nulls _candidate, and the
            // check below used to run after it, so a drop on a valid zone never sent the assign
            // (the card just went home).
            var invalidTarget = _invalidHover;
            var target = _candidate;
            ClearHighlights();

            // Valid target under the card at release?
            if (target != null && target.IsCompatible(_card.Data))
            {
                if (!target.WouldReassign(_card.Data))
                {
                    // Already assigned to this agent: no command. The card goes back to its place on
                    // the board (it used to stay floating at the pad, which is now hidden after a drop).
                    _card.ShowResult(true, "Already with the " + TaskCardView.RoleWord(target.agentId));
                    _card.ReturnHome();
                    ResetToIdle(returnHome: false);
                    return;
                }
                _ = BeginAssignAsync(target);
                return;
            }

            // Invalid drop or empty space: explain why if dropped on a red zone, return home, NO server call.
            if (invalidTarget != null && _card?.Data != null)
                _card.ShowResult(false, LocalRefusalWords(_card.Data, invalidTarget));
            _card.ReturnHome();
            ResetToIdle(returnHome: false);
        }

        /// <summary>
        /// Why a drop of <paramref name="card"/> on <paramref name="zone"/> is refused locally (no
        /// server call is made for these). Same wording the gateway's refusal codes map to, so the
        /// card, the assignment pads and the dock all say one thing. Returns null when the drop
        /// would be sent to the gateway.
        /// </summary>
        public static string LocalRefusalWords(TaskCardV2 card, StationDropZone zone)
        {
            if (card == null || zone == null) return null;
            if (zone.IsCompatible(card)) return null;
            if (TaskStateSets.IsTerminal(card.TaskState)) return "Finished tasks can't move";
            if (TaskStateSets.IsRunning(card.TaskState)) return "Already running";
            if (!TaskStateSets.IsSafeAssignCheckpoint(card.TaskState)) return "Can't move it right now";
            if (card.Executable && zone.agentId != Phase2AgentIds.Researcher) return "Starts with the Researcher";
            return "Can't assign here";
        }

        async Task BeginAssignAsync(StationDropZone zone)
        {
            _resolving = true;
            State = DragState.Pending;
            // Optional: nudge the card toward the station to read as "Sending…".
            SnapTo(zone.RestAnchor);

            // One idempotency key per drop attempt (reused only if WE retried the same key).
            string key = _presenter.NextIdemKey("assign");
            Phase2CommandResult<TaskCardV2> result;
            try
            {
                result = await _presenter.RequestAssignAsync(_card, zone.agentId, key);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MuseDesk.P2] drag assign error: {ex.Message}");
                result = Phase2CommandResult<TaskCardV2>.Conflict(new CommandErrorBody
                { Code = "TRANSPORT_ERROR", Message = ex.Message });
            }

            if (result != null && result.Ok)
            {
                State = DragState.Confirmed;              // snap already at station; presenter re-laid out
            }
            else
            {
                State = DragState.Error;                  // animate back; presenter reconciled canonical state
                _card.ReturnHome();
            }

            await HoldThenIdle();
        }

        async Task HoldThenIdle()
        {
            if (resolveHoldSeconds > 0f)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(resolveHoldSeconds)); }
                catch { /* ignore */ }
            }
            _resolving = false;
            ResetToIdle(returnHome: false);
        }

        void SnapTo(Transform anchor)
        {
            if (anchor == null) return;
            // Eases to the pad / station (about 200 ms) instead of jumping; from there the card
            // eases to its lane slot when the answer arrives, or back home when it is refused.
            _card.GlideTo(anchor);
        }

        void ClearHighlights()
        {
            if (_candidate != null) { _candidate.SetHighlight(false, true); _candidate = null; }
            if (_invalidHover != null) { _invalidHover.SetHighlight(false, true); _invalidHover = null; }
        }

        void ResetToIdle(bool returnHome)
        {
            ClearHighlights();
            if (returnHome && _card != null) _card.ReturnHome();
            if (!_resolving) State = DragState.Idle;
        }

        // ─── cancel paths: tracking loss / app background while still held (pre-command) ───
        // These NEVER cancel a command that was already sent (Pending+): that command is idempotent and
        // the presenter resyncs from canonical state on uncertainty.

        void OnDisable() => CancelHeldGrab();
        void OnApplicationPause(bool paused) { if (paused) CancelHeldGrab(); }
        void OnApplicationFocus(bool focused) { if (!focused) CancelHeldGrab(); }

        /// <summary>Abandon a hold that has not sent anything yet: halos off, card back to its slot,
        /// no command. Same path as tracking loss. Does nothing once a command is in flight.</summary>
        public void CancelHold() => CancelHeldGrab();

        void CancelHeldGrab()
        {
            if (_resolving) return;                        // command already in flight — leave it
            if (State == DragState.Held || State == DragState.Candidate)
            {
                ClearHighlights();
                if (_card != null) _card.ReturnHome();
                State = DragState.Idle;
            }
        }
    }
}
