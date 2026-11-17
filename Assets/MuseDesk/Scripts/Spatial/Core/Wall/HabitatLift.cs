using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>
    /// What a <see cref="HabitatWalker"/> and the view need from "the elevator", so the same walker can be driven by the
    /// original one-rider machine (<see cref="HabitatElevatorMachine"/>) or by the sprint A7 <see cref="ElevatorDispatcher"/>
    /// (through <see cref="DispatchElevator"/>). Exactly one implementation runs a cabin; never both.
    /// </summary>
    public interface IHabitatElevator
    {
        // ── what the view draws ──
        float CabinY { get; }
        int CabinFloor { get; }
        float DoorOpen01 { get; }
        float DoorOpenEased { get; }
        bool DoorsMoving { get; }
        bool CabinMoving { get; }

        /// <summary>True when a rider must stop on the floor's wait spot and face the door before the doors open for it
        /// (the dispatcher's explicit phases). False keeps the original "doors wait while the rider walks over" handshake.</summary>
        bool UsesWaitSpot { get; }

        int Request(string agent, int from, int to);
        bool IsQueued(string agent);
        /// <summary>The rider gives up whatever it had (not aboard: withdrawn; aboard: the cabin finishes the trip safely).</summary>
        void Cancel(string agent);
        /// <summary>The rider may leave its off-corridor stop for the wait spot (always true without wait spots).</summary>
        bool MayApproach(string agent);
        void ReportAtWaitSpot(string agent);
        void ReportFacingDoor(string agent);
        /// <summary>Doors fully open at the rider's floor, for this rider.</summary>
        bool MayBoard(string agent);
        void ReportBoarded(string agent);
        bool IsRiding(string agent);
        bool MustExit(string agent);
        void ReportExited(string agent);
        /// <summary>The rider's journey after the ride ended (it stands at its stop): navigation ownership is released.</summary>
        void ReportReachedDestination(string agent);
        /// <summary>
        /// The rider is alive and visibly waiting for its corridor to clear (at its aisle with a ticket, on the way to the
        /// doors, or standing in the open cabin at its destination). Its ticket must not time out meanwhile: the queue/board/
        /// exit timer restarts and doors that are open for it stay open. Bounded by the walker's own no-progress watchdog.
        /// </summary>
        void KeepWaiting(string agent);

        void Tick(float dt);
        /// <summary>Recenter, pause, mode change: the cabin stops at the nearest landing; nobody is left between floors.</summary>
        void Interrupt();
        /// <summary>The ride failed for this rider (timed out, interrupted, out of service): the walker re-homes it.</summary>
        event Action<string> RiderEvicted;
    }

    /// <summary>
    /// The A7 <see cref="ElevatorDispatcher"/> behind the walker's elevator interface.
    ///
    ///  - Lane: the single-file corridor means a rider that leaves the cabin must pass the wait spot, so only the rider the
    ///    stage has given the lane (<see cref="approachGate"/>) is handed to the dispatcher. Everyone else's request is kept
    ///    here, in order, and the walker waits OFF the corridor. Nothing walks toward doors that will not open for it.
    ///  - Exactly once: every dispatcher ticket ends in one RideCompleted; the counters here are what the driver reports.
    ///  - A rider whose walker was sent home while it was inside (recenter, reset) becomes a "ghost": the cabin still
    ///    carries the ticket to a landing and opens, and this adapter closes the ticket there. The walker is not evicted
    ///    twice and cannot ask for a new ride until the old ticket is closed.
    ///  - HoldInCabin (doors cannot open): the rider is not evicted until the dispatcher releases it.
    /// </summary>
    public sealed class DispatchElevator : IHabitatElevator
    {
        struct Pending { public string agent; public int from, to; }

        public readonly ElevatorDispatcher dispatcher;
        readonly List<Pending> _pending = new List<Pending>();
        readonly HashSet<string> _ghosts = new HashSet<string>();
        readonly HashSet<string> _held = new HashSet<string>();
        readonly List<string> _scratch = new List<string>();
        readonly List<RideResult> _recent = new List<RideResult>();

        /// <summary>Who holds the ride lane (set by the stage). Null: everyone may approach.</summary>
        public Func<string, bool> approachGate;

        public int Arrived { get; private set; }
        public int Failed { get; private set; }
        public int Cancelled { get; private set; }
        public int Completions => Arrived + Failed + Cancelled;
        public int Violations => dispatcher.Violations;
        public int PendingRequests => _pending.Count;
        public int Ghosts => _ghosts.Count;
        /// <summary>The last few results, oldest first (for drivers and logs).</summary>
        public IList<RideResult> Recent => _recent;

        public event Action<string> RiderEvicted;
        public event Action<RideResult> RideCompleted;

        public DispatchElevator(HabitatLayout layout, int startFloor = 0)
        {
            var heights = new float[layout.floors.Count];
            for (int i = 0; i < heights.Length; i++) heights[i] = layout.floors[i].floorY;
            // One wait spot per floor: the queue node. Extra corridor spots would stand in the way of a rider leaving.
            dispatcher = new ElevatorDispatcher(heights, ElevatorWaitSpots.ForLayout(layout, 0), startFloor);
            dispatcher.settings.capacity = 1;
            dispatcher.RideCompleted += OnRideCompleted;
            dispatcher.StrandedRiderReleased += OnStrandedReleased;
        }

        // ── view ──
        public float CabinY => dispatcher.CabinY;
        public int CabinFloor => dispatcher.CabinFloor;
        public float DoorOpen01 => dispatcher.DoorOpen01;
        public float DoorOpenEased => MuseDesk.Motion.Ease.Evaluate(MuseDesk.Motion.EaseKind.InOutCubic, dispatcher.DoorOpen01);
        public bool DoorsMoving => (dispatcher.Cabin == CabinPhase.DoorsOpening || dispatcher.Cabin == CabinPhase.DoorsClosing) && !dispatcher.DoorsJammed;
        public bool CabinMoving => dispatcher.Cabin == CabinPhase.Moving;
        public bool UsesWaitSpot => true;

        // ── rider ──
        int FindPending(string agent)
        {
            for (int i = 0; i < _pending.Count; i++) if (_pending[i].agent == agent) return i;
            return -1;
        }

        bool Gate(string agent) { return approachGate == null || approachGate(agent); }

        public int Request(string agent, int from, int to)
        {
            if (string.IsNullOrEmpty(agent) || _ghosts.Contains(agent) || _held.Contains(agent)) return 0;
            if (!Gate(agent))
            {
                int i = FindPending(agent);
                var p = new Pending { agent = agent, from = from, to = to };
                if (i >= 0) _pending[i] = p; else _pending.Add(p);
                return -1;
            }
            int at = FindPending(agent);
            if (at >= 0) _pending.RemoveAt(at);
            return dispatcher.Request(agent, from, to);
        }

        public bool IsQueued(string agent)
        {
            if (_ghosts.Contains(agent)) return false;
            return FindPending(agent) >= 0 || dispatcher.IsQueued(agent);
        }

        public void Cancel(string agent)
        {
            int i = FindPending(agent);
            if (i >= 0) _pending.RemoveAt(i);
            RidePhase p;
            if (!dispatcher.TryGetPhase(agent, out p)) return;
            if (p == RidePhase.ToDestination) dispatcher.ReportReachedDestination(agent);
            else if (p >= RidePhase.Riding) _ghosts.Add(agent);         // inside: the cabin lets the ticket out at a landing
            else dispatcher.Cancel(agent);
        }

        public bool MayApproach(string agent)
        {
            if (_ghosts.Contains(agent) || _held.Contains(agent) || !Gate(agent)) return false;
            int i = FindPending(agent);
            if (i >= 0)
            {
                Pending p = _pending[i];
                _pending.RemoveAt(i);
                if (dispatcher.Request(p.agent, p.from, p.to) == 0) return false;
            }
            WaitSpot s;
            return dispatcher.TryGetWaitSpot(agent, out s);
        }

        public void ReportAtWaitSpot(string agent) { dispatcher.ReportAtWaitSpot(agent); }
        public void ReportFacingDoor(string agent) { dispatcher.ReportFacingDoor(agent); }
        public bool MayBoard(string agent) { return !_ghosts.Contains(agent) && dispatcher.MayEnterCabin(agent); }
        public void ReportBoarded(string agent) { if (MayBoard(agent)) dispatcher.ReportEnteredCabin(agent); }
        public bool IsRiding(string agent) { return !_ghosts.Contains(agent) && (dispatcher.IsRiding(agent) || _held.Contains(agent)); }
        public bool MustExit(string agent) { return !_ghosts.Contains(agent) && dispatcher.MustExit(agent); }

        public void ReportExited(string agent)
        {
            RidePhase p;
            if (dispatcher.TryGetPhase(agent, out p) && p == RidePhase.Exiting) dispatcher.ReportExitedCabin(agent);
        }

        public void ReportReachedDestination(string agent)
        {
            RidePhase p;
            if (dispatcher.TryGetPhase(agent, out p) && p == RidePhase.ToDestination) dispatcher.ReportReachedDestination(agent);
        }

        public void KeepWaiting(string agent) { if (!_ghosts.Contains(agent)) dispatcher.KeepWaiting(agent); }

        public void Tick(float dt)
        {
            dispatcher.Tick(dt);
            if (_ghosts.Count == 0) return;
            _scratch.Clear();
            foreach (string g in _ghosts) _scratch.Add(g);
            for (int i = 0; i < _scratch.Count; i++)
            {
                string g = _scratch[i];
                RidePhase p;
                if (!dispatcher.TryGetPhase(g, out p)) { _ghosts.Remove(g); continue; }
                if (p == RidePhase.Exiting && dispatcher.MustExit(g)) dispatcher.ReportExitedCabin(g);
                if (dispatcher.TryGetPhase(g, out p) && p == RidePhase.ToDestination) dispatcher.ReportReachedDestination(g);
            }
        }

        public void Interrupt()
        {
            _pending.Clear();
            dispatcher.Interrupt();
        }

        void OnRideCompleted(RideResult r)
        {
            if (r.outcome == RideOutcome.Arrived) Arrived++;
            else if (r.outcome == RideOutcome.Failed) Failed++;
            else Cancelled++;
            _recent.Add(r);
            while (_recent.Count > 16) _recent.RemoveAt(0);
            var h = RideCompleted; if (h != null) h(r);
            if (r.agent == null) return;
            if (_ghosts.Remove(r.agent)) return;                        // its walker is already home
            if (r.outcome != RideOutcome.Failed) return;
            if (r.fallback == RideFallback.HoldInCabin) { _held.Add(r.agent); return; }
            Evict(r.agent);
        }

        void OnStrandedReleased(string agent, int floor)
        {
            if (_held.Remove(agent)) Evict(agent);
        }

        void Evict(string agent)
        {
            var h = RiderEvicted; if (h != null) h(agent);
        }

        public override string ToString()
        {
            return "cabin=" + dispatcher.Cabin + " floor=" + dispatcher.CabinFloor + " y=" + dispatcher.CabinY.ToString("F3") + " door=" + dispatcher.DoorOpen01.ToString("F2")
                 + " open=" + dispatcher.OpenRequests + " pending=" + _pending.Count + " arrived=" + Arrived + " failed=" + Failed + " cancelled=" + Cancelled
                 + " violations=" + dispatcher.Violations;
        }
    }
}
