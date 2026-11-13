using System;

namespace MuseDesk.Spatial
{
    /// <summary>Where a remotely delivered decision paper is on its way to the user.</summary>
    public enum RemoteDeliveryState
    {
        /// <summary>Nothing to deliver, or the office is within reach (Desk behaviour).</summary>
        Idle = 0,
        /// <summary>The agent is walking up / offering the paper at the far office.</summary>
        AtOffice = 1,
        /// <summary>The paper is moving from the agent's hand to the delivery platform.</summary>
        ToPlatform = 2,
        /// <summary>The courier is carrying the paper to the near-user pose.</summary>
        Flying = 3,
        /// <summary>The paper hovers at the near-user pose, waiting to be taken.</summary>
        Hovering = 4,
        /// <summary>The user took the paper; the existing paper flow owns it from here.</summary>
        Taken = 5,
    }

    /// <summary>
    /// The timing of a remote delivery, with the clock passed in (no Unity dependency, so it is
    /// tested without a scene). It decides WHEN each phase starts and ends;
    /// <see cref="RemoteDocumentDeliveryController"/> turns the phase and
    /// <see cref="Progress"/> into positions.
    ///
    /// Safety: from the moment the courier starts, the paper must be hovering within
    /// <see cref="courierTimeoutSeconds"/>; if it is not (a stalled frame loop, a bad pose), the
    /// machine jumps to <see cref="RemoteDeliveryState.Hovering"/> and sets
    /// <see cref="SnappedByTimeout"/>, and the controller places the paper at the near-user pose.
    /// The wait for the agent is bounded too. Nothing here can delay the backend: it only reads
    /// what the paper is doing.
    /// </summary>
    public sealed class RemoteDeliveryMachine
    {
        /// <summary>Seconds the agent's offer is left to read before the courier takes the paper.</summary>
        public float offerDwellSeconds = 0.7f;
        /// <summary>Longest wait for the agent to settle before the courier goes anyway.</summary>
        public float agentWaitTimeoutSeconds = 3f;
        /// <summary>Seconds from the agent's hand to the platform.</summary>
        public float platformSeconds = 0.45f;
        /// <summary>Seconds of flight from the platform to the user (the controller sets 0.9 to 1.4).</summary>
        public float flightSeconds = 1.1f;
        /// <summary>The courier path (platform + flight) may take at most this long.</summary>
        public float courierTimeoutSeconds = 3f;

        public RemoteDeliveryState State { get; private set; }
        /// <summary>0..1 through the current timed phase (ToPlatform, Flying); 0 otherwise.</summary>
        public float Progress { get; private set; }
        /// <summary>True when the current delivery reached Hovering by the timeout or a forced snap.</summary>
        public bool SnappedByTimeout { get; private set; }
        /// <summary>Deliveries that reached Hovering since construction (normal or snapped).</summary>
        public int Arrivals { get; private set; }

        /// <summary>Raised on every state change: (previous, next).</summary>
        public event Action<RemoteDeliveryState, RemoteDeliveryState> StateChanged;

        float _enteredAt;
        float _settledAt = -1f;
        float _courierStart;

        /// <summary>
        /// Advance the machine.
        /// </summary>
        /// <param name="now">Current time in seconds (any monotonic clock).</param>
        /// <param name="canDeliver">The office is remote and a near-user target exists.</param>
        /// <param name="paperOffered">The decision paper is showing and has not been taken.</param>
        /// <param name="paperTaken">The decision paper is showing and the user has taken it.</param>
        /// <param name="agentSettled">The delivering agent has stopped walking.</param>
        public void Tick(float now, bool canDeliver, bool paperOffered, bool paperTaken, bool agentSettled)
        {
            if (paperTaken)
            {
                if (State != RemoteDeliveryState.Taken) Go(RemoteDeliveryState.Taken, now);
                return;
            }
            if (!paperOffered || !canDeliver)
            {
                if (State != RemoteDeliveryState.Idle) Go(RemoteDeliveryState.Idle, now);
                return;
            }
            // From here: a paper is on offer, not taken, and it should be brought to the user.
            // A paper that was taken and is on offer again is a new decision on the same sheet.
            if (State == RemoteDeliveryState.Taken) Go(RemoteDeliveryState.Idle, now);

            switch (State)
            {
                case RemoteDeliveryState.Idle:
                    _settledAt = -1f;
                    SnappedByTimeout = false;
                    Go(RemoteDeliveryState.AtOffice, now);
                    break;

                case RemoteDeliveryState.AtOffice:
                    if (agentSettled && _settledAt < 0f) _settledAt = now;
                    bool dwellDone = _settledAt >= 0f && now - _settledAt >= offerDwellSeconds;
                    bool waitedOut = now - _enteredAt >= agentWaitTimeoutSeconds;
                    if (dwellDone || waitedOut)
                    {
                        _courierStart = now;
                        Go(RemoteDeliveryState.ToPlatform, now);
                    }
                    break;

                case RemoteDeliveryState.ToPlatform:
                    if (TimedOut(now)) break;
                    Progress = Fraction(now, platformSeconds);
                    if (Progress >= 1f) Go(RemoteDeliveryState.Flying, now);
                    break;

                case RemoteDeliveryState.Flying:
                    if (TimedOut(now)) break;
                    Progress = Fraction(now, flightSeconds);
                    if (Progress >= 1f) Arrive(now, false);
                    break;

                case RemoteDeliveryState.Hovering:
                    break;
            }
        }

        /// <summary>Put the paper at the near-user pose now (the fallback, also used by drivers).
        /// Only acts while a delivery is under way and has not arrived.</summary>
        public void SnapNow(float now)
        {
            if (State == RemoteDeliveryState.AtOffice || State == RemoteDeliveryState.ToPlatform
                || State == RemoteDeliveryState.Flying)
                Arrive(now, true);
        }

        /// <summary>Back to Idle at once (session reset, component disabled).</summary>
        public void Reset()
        {
            SnappedByTimeout = false;
            _settledAt = -1f;
            if (State != RemoteDeliveryState.Idle) Go(RemoteDeliveryState.Idle, _enteredAt);
        }

        bool TimedOut(float now)
        {
            if (now - _courierStart < courierTimeoutSeconds) return false;
            Arrive(now, true);
            return true;
        }

        void Arrive(float now, bool snapped)
        {
            SnappedByTimeout = snapped;
            Arrivals++;
            Go(RemoteDeliveryState.Hovering, now);
        }

        float Fraction(float now, float seconds)
        {
            if (seconds <= 1e-4f) return 1f;
            float k = (now - _enteredAt) / seconds;
            return k < 0f ? 0f : (k > 1f ? 1f : k);
        }

        void Go(RemoteDeliveryState next, float now)
        {
            RemoteDeliveryState prev = State;
            State = next;
            _enteredAt = now;
            Progress = 0f;
            var h = StateChanged;
            if (h != null) h(prev, next);
        }
    }
}
