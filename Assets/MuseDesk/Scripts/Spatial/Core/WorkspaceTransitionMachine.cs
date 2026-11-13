using System;
using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>Time source of the spatial state machines, so tests can drive them without Unity time.</summary>
    public interface ISpatialClock { float Now { get; } }

    /// <summary>Clock advanced by hand (tests).</summary>
    public sealed class ManualClock : ISpatialClock
    {
        public float Now { get; set; }
        public void Advance(float seconds) { Now += seconds; }
    }

    /// <summary>Unscaled game time: transitions keep their length if the time scale changes.</summary>
    public sealed class UnityUnscaledClock : ISpatialClock
    {
        public float Now => Time.unscaledTime;
    }

    public enum TransitionPhase { Idle = 0, WaitingForAgents = 1, Moving = 2 }

    public enum TransitionStartResult
    {
        Started = 0,
        AlreadyRunning = 1,
        Held = 2,
        AlreadyThere = 3,
        /// <summary>The root is not at the first pose of the route that was asked for. The route was
        /// checked from that pose, so the move is refused rather than started from somewhere else.</summary>
        OffPath = 4,
    }

    /// <summary>
    /// The one state machine that moves the office root: Idle -> (WaitingForAgents) -> Moving -> Idle.
    /// It refuses to start while another move runs or while a transition hold is set, waits (bounded)
    /// for agents to stop walking before it moves, and can be finished at once (app pause).
    ///
    /// A move follows a <see cref="WorkspaceRoute"/>: one to three straight legs. Every pose written
    /// is <see cref="RootPose.Lerp"/> between the two poses of a leg (each leg eased in and out), so
    /// the root is only ever on the route, which is what the swept-volume check tested.
    ///
    /// A running move can be stopped (<see cref="Reverse"/>): it halts where it is and goes back
    /// along the poses it already travelled to where it started. While a transition hold is set
    /// (the user holds a paper) it only halts, and goes back once the hold is released.
    /// <see cref="Abort"/> ends a move where it is without going anywhere.
    ///
    /// It reads and writes the root only through the two delegates it is given, so it runs unchanged
    /// against a real Transform or a test variable. No allocation after construction.
    /// </summary>
    public sealed class WorkspaceTransitionMachine
    {
        readonly ISpatialClock _clock;
        readonly Func<RootPose> _read;
        readonly Action<RootPose> _write;
        readonly Func<bool> _isHeld;
        readonly Func<bool> _agentsBusy;

        readonly RootPose[] _pts = new RootPose[WorkspaceRoute.MaxPoints];
        readonly float[] _cum = new float[WorkspaceRoute.MaxPoints];   // route parameter (0..1) at each pose
        int _count;
        float _t;                   // route parameter of the last pose written (0 = start, 1 = target)
        float _tFrom;               // reversing: the parameter the way back started from
        int _dir;                   // +1 forward, -1 going back, 0 halted (waiting for the hold to clear)
        float _startTime, _waitStart, _seconds, _fullSeconds;

        /// <summary>Longest wait for agents to finish walking before the move is given up.</summary>
        public float maxAgentWaitSeconds = 6f;
        /// <summary>A way back takes at least this long, however short it is.</summary>
        public float minReverseSeconds = 0.4f;

        public TransitionPhase Phase { get; private set; }
        public bool IsRunning => Phase != TransitionPhase.Idle;
        /// <summary>True only while the root is actually being moved (not while waiting for agents).</summary>
        public bool IsMoving => Phase == TransitionPhase.Moving;
        /// <summary>True after <see cref="Reverse"/>, until the root is back where the move started.</summary>
        public bool IsReversing => Phase == TransitionPhase.Moving && _dir <= 0;
        /// <summary>True while a stopped move stands still because a transition hold is set.</summary>
        public bool IsHalted => Phase == TransitionPhase.Moving && _dir == 0;
        public RootPose Target => _count > 0 ? _pts[_count - 1] : default(RootPose);
        /// <summary>Where the current (or last) move started.</summary>
        public RootPose Origin => _pts[0];
        public RootPose CurrentPose => _read();
        /// <summary>True while an agent is walking or a hand-over runs (the root must not jump then).</summary>
        public bool AgentsBusy => _agentsBusy != null && _agentsBusy();
        /// <summary>Why the last TryMove or wait did not lead to a move, in words; null after a success.</summary>
        public string LastRefusal { get; private set; }
        /// <summary>Number of completed moves (tests, diagnostics).</summary>
        public int CompletedMoves { get; private set; }

        /// <summary>Raised when a move ends: true = the root reached its target, false = given up,
        /// stopped and returned to its start, or aborted where it was.</summary>
        public event Action<bool> MoveFinished;
        /// <summary>Raised when the phase changes.</summary>
        public event Action PhaseChanged;

        public WorkspaceTransitionMachine(ISpatialClock clock, Func<RootPose> readRoot, Action<RootPose> writeRoot,
                                          Func<bool> isHeld, Func<bool> agentsBusy)
        {
            _clock = clock;
            _read = readRoot;
            _write = writeRoot;
            _isHeld = isHeld;
            _agentsBusy = agentsBusy;
        }

        bool Held => _isHeld != null && _isHeld();

        /// <summary>Start an eased move of the root straight to <paramref name="target"/>.</summary>
        public TransitionStartResult TryMove(RootPose target, float seconds)
        {
            return TryMoveRoute(WorkspaceRoute.Straight(_read(), target), seconds);
        }

        /// <summary>
        /// Start a move along <paramref name="route"/>, which must begin where the root is now (2 mm,
        /// 0.5 degrees): a route is checked from its first pose, so it is never travelled from
        /// anywhere else. <paramref name="seconds"/> is the time for the whole route.
        /// </summary>
        public TransitionStartResult TryMoveRoute(WorkspaceRoute route, float seconds)
        {
            if (IsRunning) { LastRefusal = "The office is already moving"; return TransitionStartResult.AlreadyRunning; }
            if (Held) { LastRefusal = "Waiting: you are holding something from the office"; return TransitionStartResult.Held; }
            RootPose now = _read();
            if (route.count < 2 || now.Approximately(route.Last)) { LastRefusal = null; return TransitionStartResult.AlreadyThere; }
            if (!now.Approximately(route.First, 0.002f, 0.5f))
            {
                LastRefusal = "The office is not where the checked path starts";
                return TransitionStartResult.OffPath;
            }

            LastRefusal = null;
            _count = route.count;
            _pts[0] = now;
            for (int i = 1; i < _count; i++) _pts[i] = route[i];
            // Each leg gets a share of the time in proportion to how far it moves and turns.
            float total = 0f;
            _cum[0] = 0f;
            for (int i = 1; i < _count; i++)
            {
                float w = (_pts[i].position - _pts[i - 1].position).magnitude
                          + 0.5f * Quaternion.Angle(_pts[i].rotation, _pts[i - 1].rotation) * Mathf.Deg2Rad
                          + Mathf.Abs(_pts[i].scale - _pts[i - 1].scale);
                total += Mathf.Max(1e-4f, w);
                _cum[i] = total;
            }
            for (int i = 1; i < _count; i++) _cum[i] /= total;
            _cum[_count - 1] = 1f;

            _fullSeconds = Mathf.Max(0.05f, seconds);
            _seconds = _fullSeconds;
            _t = 0f;
            _dir = 1;
            if (_agentsBusy != null && _agentsBusy())
            {
                _waitStart = _clock.Now;
                SetPhase(TransitionPhase.WaitingForAgents);
            }
            else
            {
                _startTime = _clock.Now;
                SetPhase(TransitionPhase.Moving);
            }
            return TransitionStartResult.Started;
        }

        /// <summary>Advance the machine. Call once per frame; does nothing while idle.</summary>
        public void Tick()
        {
            if (Phase == TransitionPhase.Idle) return;

            if (Phase == TransitionPhase.WaitingForAgents)
            {
                if (_agentsBusy == null || !_agentsBusy())
                {
                    // The route was checked from where the root stood. If something re-placed the
                    // root while we waited, the check no longer applies: give the move up.
                    if (!_read().Approximately(_pts[0], 0.002f, 0.5f))
                    {
                        LastRefusal = "The office was moved while it waited; try again";
                        End(false);
                        return;
                    }
                    _startTime = _clock.Now;
                    SetPhase(TransitionPhase.Moving);
                }
                else if (_clock.Now - _waitStart > maxAgentWaitSeconds)
                {
                    LastRefusal = "The agents are in the middle of a hand-over; try again in a moment";
                    End(false);
                }
                return;
            }

            if (_dir == 0)
            {
                // Stopped while the user holds a paper: stand still until it is put down, then go back.
                if (Held) return;
                BeginWayBack();
            }

            float k = Mathf.Clamp01((_clock.Now - _startTime) / _seconds);
            if (_dir > 0)
            {
                if (k >= 1f)
                {
                    _t = 1f;
                    _write(_pts[_count - 1]);
                    End(true);
                    return;
                }
                _t = k;
            }
            else
            {
                if (k >= 1f)
                {
                    _t = 0f;
                    _write(_pts[0]);
                    LastRefusal = "Stopped: the office went back to where it was";
                    End(false);
                    return;
                }
                _t = _tFrom * (1f - k * k * (3f - 2f * k));    // eased in time: it halts, then goes back
            }
            _write(PoseAt(_t));
        }

        /// <summary>The pose at route parameter <paramref name="t"/> (0..1): on one leg, eased in and
        /// out along that leg (smoothstep). With a single leg this is the eased straight move.</summary>
        RootPose PoseAt(float t)
        {
            int leg = 0;
            while (leg < _count - 2 && t > _cum[leg + 1]) leg++;
            float span = _cum[leg + 1] - _cum[leg];
            float f = span > 1e-7f ? Mathf.Clamp01((t - _cum[leg]) / span) : 1f;
            return RootPose.Lerp(_pts[leg], _pts[leg + 1], f * f * (3f - 2f * f));
        }

        /// <summary>
        /// Stop the running move and take the root back to where the move started, along the poses
        /// it already travelled (a path that was just shown to be free). While a transition hold is
        /// set the root only halts, and goes back when the hold is released. A move still waiting
        /// for agents is dropped. Returns false when nothing is running. Safe to call repeatedly.
        /// </summary>
        public bool Reverse()
        {
            if (Phase == TransitionPhase.WaitingForAgents) { LastRefusal = "Move cancelled"; End(false); return true; }
            if (Phase != TransitionPhase.Moving) return false;
            if (_dir <= 0) return true;                       // already halted or on the way back
            _t = Mathf.Clamp01((_clock.Now - _startTime) / _seconds);
            if (Held) { _dir = 0; return true; }
            BeginWayBack();
            return true;
        }

        void BeginWayBack()
        {
            _dir = -1;
            _tFrom = _t;
            _startTime = _clock.Now;
            _seconds = Mathf.Clamp(_fullSeconds * _tFrom, Mathf.Min(minReverseSeconds, _fullSeconds), _fullSeconds);
        }

        /// <summary>End a move where it is: the root is not written again. Used when the place the
        /// move was heading for can no longer be trusted (a recenter). Returns false when idle.</summary>
        public bool Abort(string why)
        {
            if (Phase == TransitionPhase.Idle) return false;
            LastRefusal = why ?? "Move cancelled";
            End(false);
            return true;
        }

        /// <summary>Finish a running move at once (app pause): the root is put at its target (or, if
        /// it was on its way back, where it started); a move still waiting for agents is dropped.</summary>
        public void FinishNow()
        {
            if (Phase == TransitionPhase.Moving)
            {
                if (_dir > 0) { _t = 1f; _write(_pts[_count - 1]); End(true); }
                else { _t = 0f; _write(_pts[0]); LastRefusal = "Stopped: the office went back to where it was"; End(false); }
            }
            else if (Phase == TransitionPhase.WaitingForAgents) { LastRefusal = "Move cancelled"; End(false); }
        }

        /// <summary>Put the root at a pose immediately, ending any running move. Used after a recenter,
        /// when the world moved under the office and easing would be meaningless. The CALLER must have
        /// checked the pose: this writes it without any test.</summary>
        public void Snap(RootPose pose)
        {
            bool wasRunning = IsRunning;
            _count = 2;
            _pts[0] = pose;
            _pts[1] = pose;
            _write(pose);
            if (wasRunning) End(true);
        }

        void End(bool reached)
        {
            if (reached) CompletedMoves++;
            _dir = 1;
            SetPhase(TransitionPhase.Idle);
            var h = MoveFinished;
            if (h != null) h(reached);
        }

        void SetPhase(TransitionPhase p)
        {
            if (Phase == p) return;
            Phase = p;
            var h = PhaseChanged;
            if (h != null) h();
        }
    }
}
