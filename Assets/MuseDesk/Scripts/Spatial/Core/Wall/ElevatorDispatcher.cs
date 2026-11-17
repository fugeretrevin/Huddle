using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>One ride request, seen from the rider. Strictly increasing: a ticket never goes back.</summary>
    public enum RidePhase
    {
        Queued = 0,          // requested, no wait spot yet
        ToWaitSpot = 1,      // spot assigned, rider walking there
        Waiting = 2,         // standing on the wait spot
        Facing = 3,          // turned to face the door: ready to be called in
        Boarding = 4,        // doors fully open, rider may step in
        Riding = 5,          // inside the cabin; position is cabin-relative
        Exiting = 6,         // arrival doors fully open, rider steps out
        ToDestination = 7,   // out of the cabin, walking to the final stop
        Finished = 8,
    }

    public enum RideOutcome { Arrived = 0, Failed = 1, Cancelled = 2 }

    public enum RideFailure
    {
        None = 0, InvalidRequest = 1, QueueTimeout = 2, BoardTimeout = 3, ExitTimeout = 4,
        DoorStuck = 5, Interrupted = 6, OutOfService = 7, NoRoute = 8,
    }

    /// <summary>What the view must do for a failed ride. Never "walk through the doors".</summary>
    public enum RideFallback
    {
        None = 0,
        /// <summary>Walk back to the home node along the walkway (the rider is outside the cabin, or standing at an open door).</summary>
        ReturnHomeViaWalkway = 1,
        /// <summary>The rider is inside a cabin whose doors cannot open: stay put until <see cref="ElevatorDispatcher.StrandedRiderReleased"/>.</summary>
        HoldInCabin = 2,
    }

    public enum CabinPhase { Idle = 0, Moving = 1, Settling = 2, DoorsOpening = 3, Open = 4, DoorsClosing = 5, OutOfService = 6 }

    /// <summary>The single completion record of a ride. Raised exactly once per request.</summary>
    public struct RideResult
    {
        public int ticketId;       // 0 = the request was rejected before a ticket existed
        public string agent;
        public int from, to;
        public RideOutcome outcome;
        public RideFailure failure;
        public RideFallback fallback;
        public override string ToString() { return agent + " " + from + "->" + to + " " + outcome + (failure != RideFailure.None ? " (" + failure + ")" : ""); }
    }

    /// <summary>A validated place for a rider to wait for the cabin: never the doorway, never inside geometry.</summary>
    public struct WaitSpot
    {
        public int floor;
        public Vector3 position;     // habitat-local feet position
        public string nodeId;        // nav node it coincides with, or "" for an extra corridor spot
    }

    [Serializable]
    public sealed class ElevatorDispatchSettings
    {
        public int capacity = 1;                    // riders in the cabin at once (>= 1)
        public float doorSeconds = 0.45f;
        public float speed = 0.22f;
        public float accelSeconds = 0.6f;
        public float brakeSeconds = 0.7f;
        public float arriveDwell = 0.15f;           // settle after the cabin stops, before the doors open
        public float minOpenSeconds = 0.5f;         // doors stay open at least this long
        public float readyHoldSeconds = 3f;         // extra hold for a rider that is still walking to its wait spot
        public float boardTimeout = 15f;            // from "doors open, step in" to the step
        public float exitTimeout = 8f;
        public float queueTimeout = 60f;            // request until the doors open for it
        public float doorStuckSeconds = 2f;         // doors jammed this long: the cabin goes out of service
    }

    /// <summary>
    /// Authority for who may enter, ride and leave the elevator. It owns the ride tickets, the wait spots and the
    /// cabin; the walker/view walks the rider and REPORTS (at the spot, facing, entered, exited, reached). A rider
    /// can only enter while the doors are fully open for it, rides cabin-relative, and every request ends in exactly
    /// one <see cref="RideCompleted"/> (Arrived, Failed with a fallback, or Cancelled). Pure logic; no Unity objects.
    ///
    /// The thirteen steps are explicit phases: request, path validated (by the caller via FloorRouteGraph; a refusal
    /// is a Failed/NoRoute result), walk to the wait spot (ToWaitSpot/Waiting), turn to face the door (Facing),
    /// door opens (cabin Open), enter (Boarding->Riding), door closes, move, settle, door opens, exit
    /// (Exiting->ToDestination), walk to the destination, release navigation ownership (NavigationReleased).
    /// </summary>
    public sealed class ElevatorDispatcher
    {
        sealed class Ticket
        {
            public int id; public string agent; public int from, to;
            public RidePhase phase; public int spot = -1;
            public float age, phaseTimer; public int boardOrder; public bool interrupted;
        }

        readonly float[] _floorY;
        readonly WaitSpot[] _spots;
        readonly string[] _spotOwner;
        readonly List<Ticket> _tickets = new List<Ticket>();
        readonly List<string> _stranded = new List<string>();
        readonly List<Ticket> _scratch = new List<Ticket>();
        int _nextId = 1, _boardCounter = 1, _target;
        float _settleTimer, _openTimer, _stuckTimer, _vel;
        bool _interrupted;

        public ElevatorDispatchSettings settings = new ElevatorDispatchSettings();
        public CabinPhase Cabin { get; private set; }
        public float CabinY { get; private set; }
        public float DoorOpen01 { get; private set; }
        public int CabinFloor { get; private set; }
        public int TargetFloor => _target;
        /// <summary>Set by the view (or a test) when the door mechanism cannot move. A jam long enough takes the cabin out of service.</summary>
        public bool DoorsJammed;
        /// <summary>Rules broken by reports (entering with doors not fully open, reporting out of order). Must stay 0.</summary>
        public int Violations { get; private set; }
        public int Capacity => Mathf.Max(1, settings.capacity);
        public int FloorCount => _floorY.Length;
        public int OpenRequests => _tickets.Count;

        public event Action<RideResult> RideCompleted;
        /// <summary>Navigation ownership of the agent is released (any outcome). Raised just before RideCompleted.</summary>
        public event Action<string> NavigationReleased;
        /// <summary>The doors finally opened for a rider that was stranded in a faulted cabin (agent, floor).</summary>
        public event Action<string, int> StrandedRiderReleased;

        public ElevatorDispatcher(float[] floorHeights, IList<WaitSpot> waitSpots, int startFloor = 0)
        {
            if (floorHeights == null || floorHeights.Length < 2) throw new ArgumentException("an elevator needs two floors");
            _floorY = floorHeights;
            _spots = new WaitSpot[waitSpots != null ? waitSpots.Count : 0];
            for (int i = 0; i < _spots.Length; i++) _spots[i] = waitSpots[i];
            _spotOwner = new string[_spots.Length];
            CabinFloor = _target = Mathf.Clamp(startFloor, 0, _floorY.Length - 1);
            CabinY = _floorY[CabinFloor];
            Cabin = CabinPhase.Idle;
        }

        public bool DoorFullyOpen => DoorOpen01 >= 1f && Cabin == CabinPhase.Open;
        public int AboardCount { get { int c = 0; for (int i = 0; i < _tickets.Count; i++) if (_tickets[i].phase == RidePhase.Riding || _tickets[i].phase == RidePhase.Exiting) c++; return c; } }
        public int StrandedCount => _stranded.Count;

        // ── queries ──

        Ticket Find(string agent)
        {
            for (int i = 0; i < _tickets.Count; i++) if (_tickets[i].agent == agent) return _tickets[i];
            return null;
        }

        public bool TryGetPhase(string agent, out RidePhase phase)
        {
            Ticket t = Find(agent);
            phase = t != null ? t.phase : RidePhase.Finished;
            return t != null;
        }

        public bool IsQueued(string agent) { return Find(agent) != null; }

        /// <summary>The rider's assigned wait spot (habitat-local), or false while it has none yet.</summary>
        public bool TryGetWaitSpot(string agent, out WaitSpot spot)
        {
            Ticket t = Find(agent);
            if (t != null && t.spot >= 0) { spot = _spots[t.spot]; return true; }
            spot = default(WaitSpot);
            return false;
        }

        /// <summary>Doors fully open and the cabin is at the rider's floor: it may step in.</summary>
        public bool MayEnterCabin(string agent) { Ticket t = Find(agent); return t != null && t.phase == RidePhase.Boarding && DoorFullyOpen && CabinFloor == t.from; }
        public bool MustExit(string agent) { Ticket t = Find(agent); return t != null && t.phase == RidePhase.Exiting && DoorFullyOpen; }
        public bool IsRiding(string agent) { Ticket t = Find(agent); return t != null && (t.phase == RidePhase.Riding || t.phase == RidePhase.Exiting); }

        /// <summary>A riding agent has no independent world position: it is the cabin origin plus a fixed local offset.</summary>
        public static Vector3 RiderPosition(Vector3 cabinNodePosition, float cabinY) { return new Vector3(cabinNodePosition.x, cabinY, cabinNodePosition.z); }

        // ── requests ──

        /// <summary>Ask for a ride. A repeat request from the same agent that has not boarded yet replaces its floors (coalesced).
        /// Returns the ticket id, or 0 when rejected (a Failed result has been raised).</summary>
        public int Request(string agent, int from, int to)
        {
            if (string.IsNullOrEmpty(agent) || from == to || from < 0 || to < 0 || from >= _floorY.Length || to >= _floorY.Length)
            { Reject(agent, from, to, RideFailure.InvalidRequest); return 0; }
            if (Cabin == CabinPhase.OutOfService) { Reject(agent, from, to, RideFailure.OutOfService); return 0; }
            Ticket t = Find(agent);
            if (t != null)
            {
                if (t.phase < RidePhase.Boarding && (t.from != from || t.to != to))
                {
                    if (t.from != from) { ReleaseSpot(t); t.phase = RidePhase.Queued; t.phaseTimer = 0f; }
                    t.from = from; t.to = to;
                }
                return t.id;
            }
            t = new Ticket { id = _nextId++, agent = agent, from = from, to = to, phase = RidePhase.Queued };
            _tickets.Add(t);
            AssignSpots();
            return t.id;
        }

        void Reject(string agent, int from, int to, RideFailure why)
        {
            Raise(new RideResult { ticketId = 0, agent = agent, from = from, to = to, outcome = RideOutcome.Failed, failure = why, fallback = RideFallback.ReturnHomeViaWalkway }, agent);
        }

        /// <summary>Withdraw a request that has not boarded. False once the rider is inside (the trip finishes safely).</summary>
        public bool Cancel(string agent)
        {
            Ticket t = Find(agent);
            if (t == null || t.phase >= RidePhase.Riding) return false;
            Finish(t, RideOutcome.Cancelled, RideFailure.None, RideFallback.None);
            return true;
        }

        // ── rider reports ──

        public bool ReportAtWaitSpot(string agent)
        {
            Ticket t = Find(agent);
            if (t == null || t.phase != RidePhase.ToWaitSpot) return false;
            t.phase = RidePhase.Waiting; t.phaseTimer = 0f;
            return true;
        }

        public bool ReportFacingDoor(string agent)
        {
            Ticket t = Find(agent);
            if (t == null || t.phase != RidePhase.Waiting) return false;
            t.phase = RidePhase.Facing; t.phaseTimer = 0f;
            return true;
        }

        /// <summary>The rider's body crossed into the cabin volume. Legal only while the doors are fully open for it.</summary>
        public bool ReportEnteredCabin(string agent)
        {
            Ticket t = Find(agent);
            if (t == null || !MayEnterCabin(agent)) { Violations++; return false; }
            ReleaseSpot(t);
            t.phase = RidePhase.Riding; t.phaseTimer = 0f; t.boardOrder = _boardCounter++;
            return true;
        }

        public bool ReportExitedCabin(string agent)
        {
            Ticket t = Find(agent);
            if (t == null || t.phase != RidePhase.Exiting) { Violations++; return false; }
            if (t.interrupted) { Finish(t, RideOutcome.Failed, RideFailure.Interrupted, RideFallback.ReturnHomeViaWalkway); return true; }
            t.phase = RidePhase.ToDestination; t.phaseTimer = 0f;
            return true;
        }

        /// <summary>The rider stands at its destination: the ride is Arrived and navigation ownership is released.</summary>
        public bool ReportReachedDestination(string agent)
        {
            Ticket t = Find(agent);
            if (t == null || t.phase != RidePhase.ToDestination) return false;
            Finish(t, RideOutcome.Arrived, RideFailure.None, RideFallback.None);
            return true;
        }

        /// <summary>
        /// The rider is alive and visibly waiting for its walkway (not for the cabin): before boarding its queue age restarts;
        /// while the doors are open for it (Boarding/Exiting) its phase timer restarts, so the doors stay open. A rider that
        /// never reports this is timed out exactly as before. False when the agent has no ticket.
        /// </summary>
        public bool KeepWaiting(string agent)
        {
            Ticket t = Find(agent);
            if (t == null) return false;
            if (t.phase < RidePhase.Boarding) t.age = 0f;
            else if (t.phase == RidePhase.Boarding || t.phase == RidePhase.Exiting) t.phaseTimer = 0f;
            return true;
        }

        /// <summary>Recenter, pause, mode change. Riders outside fail with a walkway fallback; riders inside are carried to the
        /// nearest landing, let out there (Failed/Interrupted) and walk home from it. Never leaves the cabin between floors.</summary>
        public void Interrupt()
        {
            _scratch.Clear(); _scratch.AddRange(_tickets);
            for (int i = 0; i < _scratch.Count; i++)
                if (_scratch[i].phase < RidePhase.Riding) Finish(_scratch[i], RideOutcome.Failed, RideFailure.Interrupted, RideFallback.ReturnHomeViaWalkway);
            _interrupted = true;
            if (Cabin == CabinPhase.Moving) _target = NearestFloor(CabinY);
            else if (Cabin == CabinPhase.Open) ConvertAboardForInterrupt();
        }

        /// <summary>Doors recovered: stranded riders (if any) are let out, then the cabin returns to service.</summary>
        public void ClearFault()
        {
            DoorsJammed = false;
            if (Cabin != CabinPhase.OutOfService) return;
            _stuckTimer = 0f;
            Cabin = _stranded.Count > 0 ? CabinPhase.DoorsOpening : (DoorOpen01 > 0f ? CabinPhase.DoorsClosing : CabinPhase.Idle);
        }

        // ── simulation ──

        public void Tick(float dt)
        {
            if (dt < 0f) dt = 0f;
            for (int i = 0; i < _tickets.Count; i++) { _tickets[i].age += dt; _tickets[i].phaseTimer += dt; }
            AssignSpots();
            Expire();

            switch (Cabin)
            {
                case CabinPhase.Idle: TickIdle(); break;
                case CabinPhase.Moving:
                    if (Move(dt)) { Cabin = CabinPhase.Settling; _settleTimer = 0f; }
                    break;
                case CabinPhase.Settling:
                    _settleTimer += dt;
                    if (_settleTimer >= settings.arriveDwell) { Cabin = CabinPhase.DoorsOpening; _stuckTimer = 0f; }
                    break;
                case CabinPhase.DoorsOpening:
                    if (DoorsJammed) { if (Stuck(dt)) return; break; }
                    _stuckTimer = 0f;
                    DoorOpen01 = Mathf.Min(1f, DoorOpen01 + dt / Mathf.Max(0.01f, settings.doorSeconds));
                    if (DoorOpen01 >= 1f) { Cabin = CabinPhase.Open; OnOpened(); }
                    break;
                case CabinPhase.Open:
                    _openTimer += dt;
                    if (!_interrupted) AuthorizeBoarding();
                    if (CanClose()) { Cabin = CabinPhase.DoorsClosing; _stuckTimer = 0f; }
                    break;
                case CabinPhase.DoorsClosing:
                    if (DoorsJammed) { if (Stuck(dt)) return; break; }
                    _stuckTimer = 0f;
                    DoorOpen01 = Mathf.Max(0f, DoorOpen01 - dt / Mathf.Max(0.01f, settings.doorSeconds));
                    if (DoorOpen01 <= 0f) Cabin = CabinPhase.Idle;
                    break;
                case CabinPhase.OutOfService: break;
            }
        }

        void TickIdle()
        {
            if (_interrupted)
            {
                if (HasAboard()) { _target = NearestFloor(CabinY); Cabin = CabinPhase.Moving; }
                else _interrupted = false;
                return;
            }
            int target;
            if (!PickTarget(out target)) return;
            _target = target;
            if (target == CabinFloor) { Cabin = CabinPhase.DoorsOpening; _stuckTimer = 0f; }
            else Cabin = CabinPhase.Moving;
        }

        bool HasAboard()
        {
            for (int i = 0; i < _tickets.Count; i++) if (_tickets[i].phase == RidePhase.Riding) return true;
            return false;
        }

        /// <summary>Riders aboard first (earliest boarded), then the oldest request. A request at the cabin's own floor only
        /// opens the doors once the rider is ready (Facing), so the doors never cycle for a rider still walking.</summary>
        bool PickTarget(out int floor)
        {
            floor = CabinFloor;
            Ticket aboard = null;
            for (int i = 0; i < _tickets.Count; i++)
                if (_tickets[i].phase == RidePhase.Riding && (aboard == null || _tickets[i].boardOrder < aboard.boardOrder)) aboard = _tickets[i];
            if (aboard != null) { floor = aboard.to; return true; }
            for (int i = 0; i < _tickets.Count; i++)
            {
                Ticket t = _tickets[i];
                if (t.phase > RidePhase.Facing) continue;
                if (t.from == CabinFloor && t.phase != RidePhase.Facing) continue;
                floor = t.from; return true;
            }
            return false;
        }

        void OnOpened()
        {
            _openTimer = 0f; _stuckTimer = 0f;
            if (_stranded.Count > 0)
            {
                for (int i = 0; i < _stranded.Count; i++) { var h = StrandedRiderReleased; if (h != null) h(_stranded[i], CabinFloor); }
                _stranded.Clear();
            }
            for (int i = 0; i < _tickets.Count; i++)
            {
                Ticket t = _tickets[i];
                if (t.phase != RidePhase.Riding) continue;
                if (t.to == CabinFloor) { t.phase = RidePhase.Exiting; t.phaseTimer = 0f; }
                else if (_interrupted) { t.phase = RidePhase.Exiting; t.phaseTimer = 0f; t.interrupted = true; }
            }
        }

        void ConvertAboardForInterrupt()
        {
            for (int i = 0; i < _tickets.Count; i++)
            {
                Ticket t = _tickets[i];
                if (t.phase == RidePhase.Riding) { t.phase = RidePhase.Exiting; t.phaseTimer = 0f; t.interrupted = t.to != CabinFloor; }
            }
        }

        /// <summary>Exits first, then boarding in FIFO order up to capacity. Nobody boards while someone is leaving.</summary>
        void AuthorizeBoarding()
        {
            int aboard = 0, exiting = 0, boarding = 0;
            for (int i = 0; i < _tickets.Count; i++)
            {
                RidePhase p = _tickets[i].phase;
                if (p == RidePhase.Riding) aboard++; else if (p == RidePhase.Exiting) exiting++; else if (p == RidePhase.Boarding) boarding++;
            }
            if (exiting > 0) return;
            int free = Capacity - aboard - boarding;
            for (int i = 0; i < _tickets.Count && free > 0; i++)
            {
                Ticket t = _tickets[i];
                if (t.phase == RidePhase.Facing && t.from == CabinFloor) { t.phase = RidePhase.Boarding; t.phaseTimer = 0f; free--; }
            }
        }

        bool CanClose()
        {
            if (_openTimer < settings.minOpenSeconds) return false;
            bool walkingHere = false;
            for (int i = 0; i < _tickets.Count; i++)
            {
                Ticket t = _tickets[i];
                if (t.phase == RidePhase.Exiting || t.phase == RidePhase.Boarding) return false;
                if (!_interrupted && t.from == CabinFloor && (t.phase == RidePhase.ToWaitSpot || t.phase == RidePhase.Waiting)) walkingHere = true;
            }
            return !(walkingHere && _openTimer < settings.minOpenSeconds + settings.readyHoldSeconds);
        }

        bool Stuck(float dt)
        {
            _stuckTimer += dt;
            if (_stuckTimer < settings.doorStuckSeconds) return false;
            EnterFault();
            return true;
        }

        void EnterFault()
        {
            Cabin = CabinPhase.OutOfService;
            _scratch.Clear(); _scratch.AddRange(_tickets);
            for (int i = 0; i < _scratch.Count; i++)
            {
                Ticket t = _scratch[i];
                bool inside = t.phase == RidePhase.Riding || t.phase == RidePhase.Exiting;
                if (inside) _stranded.Add(t.agent);
                Finish(t, RideOutcome.Failed, RideFailure.DoorStuck, inside ? RideFallback.HoldInCabin : RideFallback.ReturnHomeViaWalkway);
            }
        }

        void Expire()
        {
            for (int i = _tickets.Count - 1; i >= 0; i--)
            {
                if (i >= _tickets.Count) continue;
                Ticket t = _tickets[i];
                if (t.phase < RidePhase.Boarding && t.age > settings.queueTimeout)
                    Finish(t, RideOutcome.Failed, RideFailure.QueueTimeout, RideFallback.ReturnHomeViaWalkway);
                else if (t.phase == RidePhase.Boarding && t.phaseTimer > settings.boardTimeout)
                    Finish(t, RideOutcome.Failed, RideFailure.BoardTimeout, RideFallback.ReturnHomeViaWalkway);
                else if (t.phase == RidePhase.Exiting && t.phaseTimer > settings.exitTimeout)
                    Finish(t, RideOutcome.Failed, t.interrupted ? RideFailure.Interrupted : RideFailure.ExitTimeout, RideFallback.ReturnHomeViaWalkway);
            }
        }

        void AssignSpots()
        {
            for (int i = 0; i < _tickets.Count; i++)
            {
                Ticket t = _tickets[i];
                if (t.phase != RidePhase.Queued) continue;
                for (int s = 0; s < _spots.Length; s++)
                {
                    if (_spotOwner[s] != null || _spots[s].floor != t.from) continue;
                    _spotOwner[s] = t.agent; t.spot = s; t.phase = RidePhase.ToWaitSpot; t.phaseTimer = 0f;
                    break;
                }
            }
        }

        void ReleaseSpot(Ticket t)
        {
            if (t.spot >= 0 && t.spot < _spotOwner.Length && _spotOwner[t.spot] == t.agent) _spotOwner[t.spot] = null;
            t.spot = -1;
        }

        void Finish(Ticket t, RideOutcome outcome, RideFailure failure, RideFallback fallback)
        {
            if (t.phase == RidePhase.Finished) return;     // exactly once
            t.phase = RidePhase.Finished;
            ReleaseSpot(t);
            _tickets.Remove(t);
            Raise(new RideResult { ticketId = t.id, agent = t.agent, from = t.from, to = t.to, outcome = outcome, failure = failure, fallback = fallback }, t.agent);
        }

        void Raise(RideResult r, string agent)
        {
            var n = NavigationReleased; if (n != null && agent != null) n(agent);
            var h = RideCompleted; if (h != null) h(r);
        }

        bool Move(float dt)
        {
            float target = _floorY[_target];
            var m = new MuseDesk.Motion.ProfiledMover { position = CabinY, velocity = _vel };
            bool there = m.Step(target, settings.speed,
                                settings.speed / Mathf.Max(0.05f, settings.accelSeconds),
                                settings.speed / Mathf.Max(0.05f, settings.brakeSeconds), dt);
            CabinY = m.position; _vel = m.velocity;
            if (!there) return false;
            CabinY = target; _vel = 0f; CabinFloor = _target;
            return true;
        }

        int NearestFloor(float y)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < _floorY.Length; i++) { float d = Mathf.Abs(_floorY[i] - y); if (d < bd) { bd = d; best = i; } }
            return best;
        }
    }
}
