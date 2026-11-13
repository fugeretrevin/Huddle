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
        /// <summary>True when going up was asked for but is waiting (hold set, or still coming down).</summary>
        public bool RetractPending { get; private set; }
        public string StatusText { get; private set; }
        /// <summary>The office is out of reach in every state except Engaged.</summary>
        public bool IsRemote => State != CeilingPodState.Engaged;

        public event Action StateChanged;

        public CeilingPodMachine(ISpatialClock clock, WorkspaceTransitionMachine mover, Func<bool> isHeld)
        {
            _clock = clock;
            _mover = mover;
            _isHeld = isHeld;
            StatusText = "";
        }

        bool Held => _isHeld != null && _isHeld();

        /// <summary>Set the dock and inspection poses and switch the pod on. Does not move anything.</summary>
        public void Configure(RootPose dock, RootPose inspection)
        {
            DockPose = dock;
            InspectionPose = inspection;
            Active = true;
        }

        /// <summary>Switch the pod off (leaving Ceiling mode). The root is left where it is.</summary>
        public void Deactivate()
        {
            Active = false;
            RetractPending = false;
            StatusText = "";
            Enter(CeilingPodState.Docked);
        }

        /// <summary>Drop a waiting request to go up (the director is about to move the office itself).</summary>
        public void CancelPendingRetract() { RetractPending = false; }

        /// <summary>Ask the office to come down beside the user. Refused unless docked and free to move.</summary>
        public bool RequestDescend()
        {
            if (!Active) return false;
            if (State != CeilingPodState.Docked) return false;
            if (Held) { StatusText = "Waiting: you are holding something from the office"; RaiseChanged(); return false; }
            RetractPending = false;
            StatusText = "Coming down";
            Enter(CeilingPodState.PreparingToDescend);
            return true;
        }

        /// <summary>Ask the office to go back up. Accepted while engaged (it waits for a hold to clear),
        /// while still coming down (it goes up after arriving), or when docked but not yet at the dock.</summary>
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
                case CeilingPodState.Stabilizing:
                    RetractPending = true;
                    return true;
                case CeilingPodState.Docked:
                    if (_mover.CurrentPose.Approximately(DockPose, 0.002f, 0.5f)) return false;
                    if (Held) { StatusText = "Waiting: you are holding something from the office"; RaiseChanged(); return false; }
                    StatusText = "Going up";
                    Enter(CeilingPodState.PreparingToRetract);
                    return true;
                default:
                    return false;
            }
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
                    if (_mover.IsRunning) return;
                    if (At(DockPose)) Enter(CeilingPodState.ReturningToDock);
                    else Fail("The way up was interrupted");
                    return;

                case CeilingPodState.ReturningToDock:
                    if (elapsed < settleSeconds) return;
                    StatusText = "";
                    Enter(CeilingPodState.Docked);
                    return;

                case CeilingPodState.ErrorRecovery:
                    if (elapsed < recoveryPauseSeconds || _mover.IsRunning) return;
                    if (Held) return;                       // never pull the office away from a held paper
                    // First try an ordinary eased trip back to the dock, a few times.
                    if (_recoveries < 3)
                    {
                        _recoveries++;
                        if (StartMove(DockPose)) { Enter(CeilingPodState.Retracting); return; }
                    }
                    // Agents walk to world-space targets: wait (bounded) for them before a jump.
                    if (_mover.AgentsBusy && elapsed < recoveryPauseSeconds + maxRecoveryAgentWaitSeconds) return;
                    _mover.Snap(DockPose);
                    RetractPending = false;
                    StatusText = "";
                    Enter(CeilingPodState.Docked);
                    return;
            }
        }

        /// <summary>
        /// The tracking origin moved (recenter, headset put back on). The poses were recomputed by the
        /// caller; any travel in progress is ended and the office is put where its state says it
        /// belongs: beside the user if it was coming down or engaged, at the dock otherwise.
        /// </summary>
        public void OnRecenter(RootPose dock, RootPose inspection)
        {
            if (!Active) return;
            DockPose = dock;
            InspectionPose = inspection;
            switch (State)
            {
                case CeilingPodState.Descending:
                case CeilingPodState.Stabilizing:
                case CeilingPodState.Engaged:
                    _mover.Snap(inspection);
                    Enter(CeilingPodState.Engaged);
                    break;
                case CeilingPodState.PreparingToRetract:
                    _mover.Snap(inspection);
                    RetractPending = true;
                    Enter(CeilingPodState.Engaged);
                    break;
                default:
                    _mover.Snap(dock);
                    RetractPending = false;
                    Enter(CeilingPodState.Docked);
                    break;
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
                    RetractPending = true;
                    Enter(At(InspectionPose) ? CeilingPodState.Engaged : CeilingPodState.Docked);
                    break;
                case CeilingPodState.Retracting:
                case CeilingPodState.ReturningToDock:
                    if (At(DockPose)) Enter(CeilingPodState.Docked); else Fail("Paused on the way up");
                    break;
            }
        }

        bool StartMove(RootPose target)
        {
            float distance = (_mover.CurrentPose.position - target.position).magnitude;
            float seconds = Mathf.Clamp(distance / Mathf.Max(0.05f, metresPerSecond), minTravelSeconds, maxTravelSeconds);
            var r = _mover.TryMove(target, seconds);
            return r == TransitionStartResult.Started || r == TransitionStartResult.AlreadyThere;
        }

        bool At(RootPose pose) { return _mover.CurrentPose.Approximately(pose, 0.002f, 0.5f); }

        void Fail(string why)
        {
            StatusText = why + "; returning to the ceiling";
            Enter(CeilingPodState.ErrorRecovery);
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
