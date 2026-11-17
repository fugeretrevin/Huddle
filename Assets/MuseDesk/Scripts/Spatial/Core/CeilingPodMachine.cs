using System;
using UnityEngine;

namespace MuseDesk.Spatial
{
    public enum CeilingPodState
    {
        Docked = 0,
        PreparingToDescend = 1,
        Descending = 2,
        Stabilizing = 3,
        Engaged = 4,
        PreparingToRetract = 5,
        Retracting = 6,
        ReturningToDock = 7,
        ErrorRecovery = 8,
    }

    /// <summary>
    /// State machine of the ceiling pod: the office hangs at a dock under the ceiling and comes down
    /// a straight path to an inspection pose beside the user, then goes back up.
    ///
    ///   Docked -> PreparingToDescend -> Descending -> Stabilizing -> Engaged
    ///   Engaged -> PreparingToRetract -> Retracting -> ReturningToDock -> Docked
    ///   any moving state -> ErrorRecovery -> Docked   (a move could not start or was given up)
    ///
    /// Stop (<see cref="RequestStop"/>), explicit transitions:
    ///   PreparingToDescend -> Docked                      nothing had moved
    ///   Descending -> Retracting -> ReturningToDock -> Docked
    ///                                                     the move is reversed along the poses already
    ///                                                     travelled; while a paper is held it only halts
    ///                                                     and goes back when the paper is put down
    ///   PreparingToRetract -> Engaged                     the wind-up is called off
    ///   Retracting                                        not stopped: it is moving away from the user
    ///   first trip up (see <see cref="BeginArrival"/>)    reversed to where it started; the director
    ///                                                     then leaves Ceiling mode again
    ///
    /// The first trip up to the dock (from the desk, the floor or the inspection pose) follows a
    /// route the director had swept: <see cref="BeginArrival"/>. The pod itself only ever travels
    /// the dock - inspection path and that route, never a line of its own invention.
    ///
    /// Driven by <see cref="Tick"/> and a clock (no coroutines), it moves the root only through the
    /// shared <see cref="WorkspaceTransitionMachine"/>. Going up waits while a transition hold is
    /// set (the user is holding a paper). Presentation only: it knows nothing about tasks.
    /// </summary>
    public sealed class CeilingPodMachine
    {
        readonly ISpatialClock _clock;
        readonly WorkspaceTransitionMachine _mover;
        readonly Func<bool> _isHeld;

        float _stateStart;
        int _recoveries;
        WorkspaceRoute _arrivalRoute;
        bool _arriving;
        bool _haltedWords;

        public float prepareSeconds = 0.4f;
        public float stabilizeSeconds = 0.5f;
        public float settleSeconds = 0.4f;
        public float metresPerSecond = 0.45f;
        public float minTravelSeconds = 1.2f;
        public float maxTravelSeconds = 4.0f;
        [Tooltip("How long a move may fail to start (office busy, hold set) before the pod recovers to the dock.")]
        public float startTimeoutSeconds = 8f;
        public float recoveryPauseSeconds = 0.5f;
        public float maxRecoveryAgentWaitSeconds = 20f;

        public CeilingPodState State { get; private set; }
        /// <summary>True while Ceiling mode is on and the dock and inspection poses are set.</summary>
        public bool Active { get; private set; }
        public RootPose DockPose { get; private set; }
        public RootPose InspectionPose { get; private set; }
        /// <summary>True when going up was asked for but is waiting (hold set, or still settling).</summary>
        public bool RetractPending { get; private set; }
        public string StatusText { get; private set; }
        /// <summary>The office is out of reach in every state except Engaged.</summary>
        public bool IsRemote => State != CeilingPodState.Engaged;
        /// <summary>True during the first trip up to the dock.</summary>
        public bool Arriving => _arriving;
        /// <summary>Set when the first trip up was stopped or could not be made. The office is where
        /// that trip started (or was halted); the director reads this and leaves Ceiling mode again.</summary>
        public bool ArrivalFailed { get; private set; }
        /// <summary>Set when the office was halted somewhere that is neither the dock nor the
        /// inspection pose. The pod will not move it from there: the placement must be chosen again.</summary>
        public bool Stranded { get; private set; }

        public event Action StateChanged;

        public CeilingPodMachine(ISpatialClock clock, WorkspaceTransitionMachine mover, Func<bool> isHeld)
        {
            _clock = clock;
            _mover = mover;
            _isHeld = isHeld;
            StatusText = "";
        }

        bool Held => _isHeld != null && _isHeld();

        /// <summary>Set the dock and inspection poses and switch the pod on. Does not move anything.
        /// Both poses, and the path between them, must have been validated by the caller.</summary>
        public void Configure(RootPose dock, RootPose inspection)
        {
            DockPose = dock;
            InspectionPose = inspection;
            Active = true;
        }

        /// <summary>Put the pod back into a resting state with these poses (a mode change was called
        /// off and the office is back where it rested). Does not move anything.</summary>
        public void Restore(RootPose dock, RootPose inspection, bool engaged)
        {
            DockPose = dock;
            InspectionPose = inspection;
            Active = true;
            RetractPending = false;
            _arriving = false;
            ArrivalFailed = false;
            Stranded = false;
            StatusText = "";
            Enter(engaged ? CeilingPodState.Engaged : CeilingPodState.Docked);
        }

        /// <summary>Switch the pod off (leaving Ceiling mode). The root is left where it is.</summary>
        public void Deactivate()
        {
            Active = false;
            RetractPending = false;
            _arriving = false;
            ArrivalFailed = false;
            Stranded = false;
            StatusText = "";
            Enter(CeilingPodState.Docked);
        }

        /// <summary>Drop a waiting request to go up (the director is about to move the office itself).</summary>
        public void CancelPendingRetract() { RetractPending = false; }

        /// <summary>Reads and clears <see cref="ArrivalFailed"/>.</summary>
        public bool ConsumeArrivalFailed()
        {
            bool failed = ArrivalFailed;
            ArrivalFailed = false;
            return failed;
        }

        /// <summary>
        /// Start the first trip up to the dock along <paramref name="route"/>, which the caller has
        /// swept and which must begin where the office is now and end at the dock. The trip runs
        /// through PreparingToRetract, Retracting and ReturningToDock like any other way up.
        /// Refused while held, while something moves, or from a state that is not at rest.
        /// </summary>
        public bool BeginArrival(WorkspaceRoute route)
        {
            if (!Active) return false;
            if (State != CeilingPodState.Docked && State != CeilingPodState.Engaged) return false;
            if (_mover.IsRunning) { StatusText = "The office is already moving"; RaiseChanged(); return false; }
            if (Held) { StatusText = "Waiting: you are holding something from the office"; RaiseChanged(); return false; }
            _arrivalRoute = route;
            _arriving = true;
            ArrivalFailed = false;
            Stranded = false;
            RetractPending = false;
            StatusText = "Going up";
            Enter(CeilingPodState.PreparingToRetract);
            return true;
        }

        /// <summary>Ask the office to come down beside the user. Refused unless docked and free to move.</summary>
        public bool RequestDescend()
        {
            if (!Active) return false;
            if (State != CeilingPodState.Docked || Stranded) return false;
            if (!At(DockPose)) return false;
            if (Held) { StatusText = "Waiting: you are holding something from the office"; RaiseChanged(); return false; }
            RetractPending = false;
            StatusText = "Coming down";
            Enter(CeilingPodState.PreparingToDescend);
            return true;
        }

        /// <summary>
        /// Ask the office to go back up. While engaged it is accepted and waits for a hold to clear.
        /// While it is still coming down this is a Stop: it goes back up from where it is, at once,
        /// and nothing is queued. While it settles beside the user (half a second) the request is
        /// kept and acted on as soon as it has settled.
        /// </summary>
        public bool RequestRetract()
        {
            if (!Active) return false;
            switch (State)
            {
                case CeilingPodState.Engaged:
                    RetractPending = true;
                    if (Held) { StatusText = "Waiting to go up: put the paper down first"; RaiseChanged(); }
                    return true;
                case CeilingPodState.PreparingToDescend:
                case CeilingPodState.Descending:
                    return RequestStop();
                case CeilingPodState.Stabilizing:
                    RetractPending = true;
                    return true;
                default:
                    // Docked but not at the dock: only a swept route (BeginArrival) may take it there.
                    return false;
            }
        }

        /// <summary>
        /// Stop. See the class summary for what it does in each state. Returns true when it changed
        /// something; false when there is nothing to stop (at rest, settling, or already going up).
        /// Safe to call at any time and any number of times.
        /// </summary>
        public bool RequestStop()
        {
            if (!Active) return false;
            switch (State)
            {
                case CeilingPodState.PreparingToDescend:
                    RetractPending = false;
                    StatusText = "";
                    Enter(CeilingPodState.Docked);
                    return true;

                case CeilingPodState.Descending:
                    if (!_mover.Reverse()) return false;        // the move ended this frame: nothing left to stop
                    RetractPending = false;
                    _haltedWords = _mover.IsHalted;
                    StatusText = _haltedWords ? "Stopped. Put the paper down and it goes back up." : "Stopped: going back up";
                    Enter(CeilingPodState.Retracting);
                    RaiseChanged();
                    return true;

                case CeilingPodState.PreparingToRetract:
                    if (_arriving) { FailArrival("Stopped"); return true; }
                    RetractPending = false;
                    StatusText = "";
                    Enter(At(InspectionPose) ? CeilingPodState.Engaged : CeilingPodState.Docked);
                    return true;

                case CeilingPodState.Retracting:
                    if (!_arriving) return false;               // going up: moving away from the user, let it finish
                    if (!_mover.Reverse()) return false;
                    _haltedWords = _mover.IsHalted;
                    StatusText = _haltedWords ? "Stopped. Put the paper down and it goes back." : "Stopped: going back to where it was";
                    RaiseChanged();
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// End any travel where it is and rest there. The pod will not move the office again until
        /// the placement is chosen anew (<see cref="Stranded"/>). Used when the poses can no longer
        /// be trusted (a recenter that could not be validated).
        /// </summary>
        public void Hold(string status)
        {
            if (!Active) return;
            _mover.Abort(status);
            RetractPending = false;
            _arriving = false;
            Stranded = true;
            StatusText = status ?? "";
            Enter(CeilingPodState.Docked);
            RaiseChanged();
        }

        /// <summary>Advance the machine. Call once per frame; cheap while docked or engaged.</summary>
        public void Tick()
        {
            if (!Active) return;
            float elapsed = _clock.Now - _stateStart;

            switch (State)
            {
                case CeilingPodState.Docked:
                    return;

                case CeilingPodState.PreparingToDescend:
                    if (elapsed < prepareSeconds) return;
                    if (StartMove(InspectionPose)) Enter(CeilingPodState.Descending);
                    else if (elapsed > prepareSeconds + startTimeoutSeconds) Fail("Could not start coming down");
                    return;

                case CeilingPodState.Descending:
                    if (_mover.IsRunning) return;
                    if (At(InspectionPose)) Enter(CeilingPodState.Stabilizing);
                    else Fail("The way down was interrupted");
                    return;

                case CeilingPodState.Stabilizing:
                    if (elapsed < stabilizeSeconds) return;
                    StatusText = "";
                    Enter(CeilingPodState.Engaged);
                    return;

                case CeilingPodState.Engaged:
                    if (!RetractPending) return;
                    if (Held)
                    {
                        if (StatusText.Length == 0) { StatusText = "Waiting to go up: put the paper down first"; RaiseChanged(); }
                        return;
                    }
                    StatusText = "Going up";
                    Enter(CeilingPodState.PreparingToRetract);
                    return;

                case CeilingPodState.PreparingToRetract:
                    if (elapsed < prepareSeconds) return;
                    if (_arriving)
                    {
                        // The first trip up. Nothing has moved yet, so a hold or a refusal simply calls it off.
                        if (Held) { FailArrival("Put the paper down first, then choose the ceiling spot again"); return; }
                        TransitionStartResult r = _mover.TryMoveRoute(_arrivalRoute, TravelSeconds(_arrivalRoute.Length));
                        if (r == TransitionStartResult.Started || r == TransitionStartResult.AlreadyThere) Enter(CeilingPodState.Retracting);
                        else if (r == TransitionStartResult.OffPath) FailArrival("The office was moved; choose the ceiling spot again");
                        else if (elapsed > prepareSeconds + startTimeoutSeconds) FailArrival("Could not start going up; try again");
                        return;
                    }
                    if (Held)
                    {
                        // Picked something up during the wind-up: stay down and wait.
                        RetractPending = true;
                        StatusText = "Waiting to go up: put the paper down first";
                        Enter(At(InspectionPose) ? CeilingPodState.Engaged : CeilingPodState.PreparingToRetract);
                        return;
                    }
                    if (StartMove(DockPose)) { RetractPending = false; Enter(CeilingPodState.Retracting); }
                    else if (elapsed > prepareSeconds + startTimeoutSeconds) Fail("Could not start going up");
                    return;

                case CeilingPodState.Retracting:
                    if (_mover.IsRunning)
                    {
                        if (_haltedWords && !_mover.IsHalted)
                        {
                            _haltedWords = false;
                            StatusText = _arriving ? "Going back to where it was" : "Going back up";
                            RaiseChanged();
                        }
                        return;
                    }
                    _haltedWords = false;
                    if (At(DockPose)) { _arriving = false; Enter(CeilingPodState.ReturningToDock); }
                    else if (_arriving) FailArrival(string.IsNullOrEmpty(_mover.LastRefusal) ? "The way up was interrupted" : _mover.LastRefusal);
                    else Fail("The way up was interrupted");
                    return;

                case CeilingPodState.ReturningToDock:
                    if (elapsed < settleSeconds) return;
                    StatusText = "";
                    Enter(CeilingPodState.Docked);
                    return;

                case CeilingPodState.ErrorRecovery:
                    if (elapsed < recoveryPauseSeconds || _mover.IsRunning) return;
                    if (At(DockPose))
                    {
                        RetractPending = false;
                        StatusText = "";
                        Enter(CeilingPodState.Docked);
                        return;
                    }
                    // The only checked way to the dock is the path from the inspection pose. From
                    // anywhere else the office stays where it is and says so.
                    if (!At(InspectionPose))
                    {
                        Hold("The office stopped off its checked path. Return to Desk or choose the ceiling spot again.");
                        return;
                    }
                    if (Held) return;                       // never pull the office away from a held paper
                    // First try an ordinary eased trip back to the dock, a few times.
                    if (_recoveries < 3)
                    {
                        _recoveries++;
                        if (StartMove(DockPose)) { Enter(CeilingPodState.Retracting); return; }
                    }
                    // Agents walk to world-space targets: wait (bounded) for them before a jump.
                    if (_mover.AgentsBusy && elapsed < recoveryPauseSeconds + maxRecoveryAgentWaitSeconds) return;
                    _mover.Snap(DockPose);                  // the dock is a validated pose; a jump sweeps nothing
                    RetractPending = false;
                    StatusText = "";
                    Enter(CeilingPodState.Docked);
                    return;
            }
        }

        /// <summary>
        /// The tracking origin moved (recenter, headset put back on). The caller recomputed the poses
        /// AND VALIDATED THEM against the current room and user; any travel in progress is ended and
        /// the office is put where its state says it belongs: beside the user if it was coming down
        /// or engaged, at the dock otherwise. When the poses could not be validated the caller uses
        /// <see cref="Hold"/> instead and nothing is moved.
        /// </summary>
        public void OnRecenter(RootPose dock, RootPose inspection)
        {
            if (!Active) return;
            DockPose = dock;
            InspectionPose = inspection;
            bool arriving = _arriving;
            _arriving = false;
            Stranded = false;
            ArrivalFailed = false;
            CeilingPodState s = State;
            if (s == CeilingPodState.Descending || s == CeilingPodState.Stabilizing || s == CeilingPodState.Engaged)
            {
                _mover.Snap(inspection);
                Enter(CeilingPodState.Engaged);
            }
            else if (s == CeilingPodState.PreparingToRetract && !arriving)
            {
                _mover.Snap(inspection);
                RetractPending = true;
                Enter(CeilingPodState.Engaged);
            }
            else
            {
                _mover.Snap(dock);
                RetractPending = false;
                Enter(CeilingPodState.Docked);
            }
            StatusText = "";
            RaiseChanged();
        }

        /// <summary>The app is being paused: end any travel at its destination so nothing is left mid-air.</summary>
        public void OnPause()
        {
            if (!Active) return;
            _mover.FinishNow();
            switch (State)
            {
                case CeilingPodState.PreparingToDescend:
                    Enter(CeilingPodState.Docked);
                    break;
                case CeilingPodState.Descending:
                case CeilingPodState.Stabilizing:
                    if (At(InspectionPose)) Enter(CeilingPodState.Engaged); else Fail("Paused on the way down");
                    break;
                case CeilingPodState.PreparingToRetract:
                    if (_arriving) { FailArrival("Paused before going up"); break; }
                    RetractPending = true;
                    Enter(At(InspectionPose) ? CeilingPodState.Engaged : CeilingPodState.Docked);
                    break;
                case CeilingPodState.Retracting:
                case CeilingPodState.ReturningToDock:
                    if (At(DockPose)) { _arriving = false; Enter(CeilingPodState.Docked); }
                    else if (_arriving) FailArrival("Paused: the office went back to where it was");
                    else Fail("Paused on the way up");
                    break;
            }
        }

        float TravelSeconds(float distance)
        {
            return Mathf.Clamp(distance / Mathf.Max(0.05f, metresPerSecond), minTravelSeconds, maxTravelSeconds);
        }

        bool StartMove(RootPose target)
        {
            float distance = (_mover.CurrentPose.position - target.position).magnitude;
            var r = _mover.TryMove(target, TravelSeconds(distance));
            return r == TransitionStartResult.Started || r == TransitionStartResult.AlreadyThere;
        }

        bool At(RootPose pose) { return _mover.CurrentPose.Approximately(pose, 0.002f, 0.5f); }

        void Fail(string why)
        {
            StatusText = why + "; returning to the ceiling";
            Enter(CeilingPodState.ErrorRecovery);
        }

        void FailArrival(string why)
        {
            _arriving = false;
            _haltedWords = false;
            ArrivalFailed = true;
            RetractPending = false;
            StatusText = why ?? "";
            Enter(CeilingPodState.Docked);
            RaiseChanged();
        }

        void Enter(CeilingPodState s)
        {
            _stateStart = _clock.Now;
            if (s == CeilingPodState.Docked || s == CeilingPodState.Engaged) _recoveries = 0;
            if (State == s) return;
            State = s;
            RaiseChanged();
        }

        void RaiseChanged()
        {
            var h = StateChanged;
            if (h != null) h();
        }
    }
}
