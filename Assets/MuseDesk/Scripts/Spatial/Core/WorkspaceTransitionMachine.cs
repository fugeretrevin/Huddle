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

    public enum TransitionStartResult { Started = 0, AlreadyRunning = 1, Held = 2, AlreadyThere = 3 }

    /// <summary>
    /// The one state machine that moves the office root: Idle -> (WaitingForAgents) -> Moving -> Idle.
    /// It refuses to start while another move runs or while a transition hold is set, waits (bounded)
    /// for agents to stop walking before it moves, and can be finished at once (app pause, recenter).
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

        RootPose _from, _to;
        float _startTime, _waitStart, _seconds;

        /// <summary>Longest wait for agents to finish walking before the move is given up.</summary>
        public float maxAgentWaitSeconds = 6f;

        public TransitionPhase Phase { get; private set; }
        public bool IsRunning => Phase != TransitionPhase.Idle;
        /// <summary>True only while the root is actually being moved (not while waiting for agents).</summary>
        public bool IsMoving => Phase == TransitionPhase.Moving;
        public RootPose Target => _to;
        public RootPose CurrentPose => _read();
        /// <summary>True while an agent is walking or a hand-over runs (the root must not jump then).</summary>
        public bool AgentsBusy => _agentsBusy != null && _agentsBusy();
        /// <summary>Why the last TryMove or wait did not lead to a move, in words; null after a success.</summary>
        public string LastRefusal { get; private set; }
        /// <summary>Number of completed moves (tests, diagnostics).</summary>
        public int CompletedMoves { get; private set; }

        /// <summary>Raised when a move ends: true = the root reached its target, false = given up.</summary>
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

        /// <summary>Start an eased move of the root to <paramref name="target"/>.</summary>
        public TransitionStartResult TryMove(RootPose target, float seconds)
        {
            if (IsRunning) { LastRefusal = "The office is already moving"; return TransitionStartResult.AlreadyRunning; }
            if (_isHeld != null && _isHeld()) { LastRefusal = "Waiting: you are holding something from the office"; return TransitionStartResult.Held; }
            RootPose now = _read();
            if (now.Approximately(target)) { LastRefusal = null; return TransitionStartResult.AlreadyThere; }

            LastRefusal = null;
            _from = now;
            _to = target;
            _seconds = Mathf.Max(0.05f, seconds);
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
                    _from = _read();               // the root may have been re-placed while we waited
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

            float k = Mathf.Clamp01((_clock.Now - _startTime) / _seconds);
            if (k >= 1f)
            {
                _write(_to);
                End(true);
                return;
            }
            float eased = k * k * (3f - 2f * k);   // smoothstep: eases in and out
            _write(RootPose.Lerp(_from, _to, eased));
        }

        /// <summary>Finish a running move at once (app pause, recenter): the root is put at its
        /// target, or, if still waiting for agents, the move is dropped and the root stays.</summary>
        public void FinishNow()
        {
            if (Phase == TransitionPhase.Moving) { _write(_to); End(true); }
            else if (Phase == TransitionPhase.WaitingForAgents) { LastRefusal = "Move cancelled"; End(false); }
        }

        /// <summary>Put the root at a pose immediately, ending any running move. Used after a recenter,
        /// when the world moved under the office and easing would be meaningless.</summary>
        public void Snap(RootPose pose)
        {
            bool wasRunning = IsRunning;
            _to = pose;
            _write(pose);
            if (wasRunning) End(true);
        }

        void End(bool reached)
        {
            if (reached) CompletedMoves++;
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
