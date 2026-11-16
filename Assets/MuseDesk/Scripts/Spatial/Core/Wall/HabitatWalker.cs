using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    public enum WalkerState { Home = 0, Leasing = 1, Walking = 2, WaitingElevator = 3, Riding = 4, Dwelling = 5, SoftTransfer = 6, AtRest = 7 }

    /// <summary>What the avatar's Animator should show. The walker owns the root; the Animator owns bones.</summary>
    public enum WalkerAnim { Work = 0, Idle = 1, Walk = 2, Wait = 3, Ride = 4, Present = 5 }

    [Serializable]
    public sealed class WalkerSettings
    {
        public float walkSpeed = 0.14f;            // m/s at habitat scale
        [Obsolete("Turns are critically damped now; see turnSmoothTime.")]
        public float turnSpeed = 420f;             // deg/s (unused, kept for serialized data)
        public float startSeconds = 0.30f;         // rest -> walking speed (polish spec: 0.20-0.40 s)
        public float stopSeconds = 0.28f;          // walking speed -> rest
        public float turnSmoothTime = 0.16f;       // critically damped body turn
        public float walkTurnLimit = 50f;          // sharper heading changes stop and turn in place first
        // rc10: the three limits below are LOWER BOUNDS only. A walker waits visibly (at its aisle, or in the open cabin)
        // while others clear the corridor; it is faded home only after stuckTimeout without progress (a true last resort).
        public float leaseTimeout = 6f;            // corridor wait: soft transfer after max(this, stuckTimeout)
        public float elevatorWaitTimeout = 20f;    // cabin wait: soft transfer after max(this, stuckTimeout) (one-rider machine)
        public float softTransferSeconds = 0.6f;   // fade out, re-home, fade in
        public float queueWaitTimeout = 90f;       // dispatcher only: waiting off the corridor for the ride lane, lower bound of laneWaitTimeout
        public float faceDoorTolerance = 8f;       // dispatcher only: degrees off the door line that still counts as facing it
        // rc9 and before: "a whole journey longer than this -> soft transfer home". That cut legitimate journeys short (a rider
        // that waited for the corridor was faded home the moment it stepped out of the cabin). Ignored since rc10.
        public float tripTimeout = 45f;
        public float stuckTimeout = 60f;           // rc10: no progress (corridor, cabin exit) for this long -> soft transfer (last resort)
        public float laneWaitTimeout = 300f;       // rc10: dispatcher lane / cabin wait (others ride first) -> soft transfer (last resort)
        public float maxTripSeconds = 600f;        // rc10: absolute cap on one journey (livelock guard), lane waits excluded
    }

    /// <summary>
    /// The ONE writer of a habitat avatar's local pose.
    ///
    /// The corridors are single file (a capsule is 0.064 m wide in a 0.08 m corridor), so stepping node by node
    /// could leave two characters face to face. Instead the route is cut into SEGMENTS that run from one
    /// rest stop (desk, aisle, cabin, the elevator wait spot) to the next rest stop or the destination, and a
    /// segment is LEASED whole (every node reserved at once) before the first step. A segment that ends in the cabin
    /// is entered only once the elevator is at this floor with its doors open for this character. Nodes are released as
    /// they are left.
    ///
    /// rc10 (owner rc9: "never left the elevator and teleported back to the seat"):
    ///  - Nobody waits ON the corridor for someone else. A walker waits at its own aisle (inside its cubicle, where it
    ///    blocks nobody), in the open cabin, or at the elevator wait spot (only for the cabin). The cubicle entry is not a
    ///    stop any more: standing there blocked the corridor at the cubicle door, and two such waiters could block each
    ///    other forever.
    ///  - A trip to a spot ON the corridor (a colleague's door, the dispatch spot, the Manager's hand-off spot) keeps the
    ///    way back reserved while it dwells there (it returns the same way), so it never has to wait on the corridor.
    ///  - A rider that arrives at a busy floor stays in the cabin with the doors held open (<see cref="IHabitatElevator.KeepWaiting"/>)
    ///    and claims its exit segment with rider priority; nobody else may start through that zone, so the floor clears
    ///    and the rider walks out. A waiting walker's claim (<see cref="HabitatReservations.SetClaim"/>) gives the corridor
    ///    to the oldest waiter next: no deadlock, no starvation.
    ///  - Soft transfer (fade out, re-home, fade in) is the last resort for a pathological stall only: no progress for
    ///    <see cref="WalkerSettings.stuckTimeout"/>, an elevator fault (eviction), no route. Visual only: it never touches
    ///    task state; the stage sends it home whenever the authoritative state moves on.
    /// </summary>
    public sealed class HabitatWalker
    {
        /// <summary>Claim priority bonus of a trip that rides (the ride lane holder): it is served before anyone else waits.</summary>
        public const float RiderPriority = 1000f;

        readonly HabitatLayout _L;
        readonly HabitatReservations _res;
        readonly IHabitatElevator _elev;
        float _dt;
        bool _spotReported, _faceReported;
        readonly List<int> _route = new List<int>();
        readonly int _home;
        int _step;              // index into _route of the next node to walk to
        int _segEnd;            // index of the last node of the leased segment
        int _at;                // node we stand on (or last left)
        float _waitTimer, _tripTimer, _dwell, _dwellTimer, _fade;
        bool _returnHomeAfter, _exitReported, _holdBehind;
        string _pendingTarget;
        float _pendingDwell;
        bool _pendingReturn;
        float _speed, _yawVel;

        /// <summary>Current ground speed (habitat m/s). The view matches the walk cycle to it (no foot skating).</summary>
        public float Speed => _speed;
        /// <summary>Body turn rate (deg/s, positive = clockwise seen from above). The view plays turn-in-place steps from it.</summary>
        public float TurnRate => _yawVel;
        float Accel => settings.walkSpeed / Mathf.Max(0.02f, settings.startSeconds);
        float Decel => settings.walkSpeed / Mathf.Max(0.02f, settings.stopSeconds);

        public readonly string agent;
        public readonly HabitatRole role;
        public WalkerSettings settings = new WalkerSettings();
        public WalkerState State { get; private set; }
        public WalkerAnim Anim { get; private set; }
        /// <summary>Feet position, habitat-local.</summary>
        public Vector3 Position { get; private set; }
        public float Yaw { get; private set; }
        /// <summary>0 = invisible (mid soft transfer), 1 = fully shown.</summary>
        public float Visibility { get; private set; }
        public int CurrentNode => _at;
        public int HomeNode => _home;
        public bool AtHome => State == WalkerState.Home && _at == _home;
        public bool Idle => State == WalkerState.Home || State == WalkerState.AtRest;
        /// <summary>True when the current trip includes an elevator ride.</summary>
        public bool TripHasRide { get; private set; }
        public string LastBlock { get; private set; }
        public int SoftTransfers { get; private set; }
        /// <summary>True while a rider stands in the open cabin at its floor, waiting for the floor's corridor to clear.</summary>
        public bool HoldingInCabin { get; private set; }
        /// <summary>Seconds the walker has been waiting in its current wait (corridor, lift, or in the cabin). 0 while moving.</summary>
        public float WaitSeconds => State == WalkerState.Leasing || State == WalkerState.WaitingElevator || HoldingInCabin ? _waitTimer : 0f;
        /// <summary>Raised at the destination of an outbound trip (node id).</summary>
        public event Action<string> ArrivedAt;
        /// <summary>Raised when the character is back at its desk (walked or soft-transferred).</summary>
        public event Action ReturnedHome;

        public HabitatWalker(string agent, HabitatRole role, HabitatLayout layout, HabitatReservations res, IHabitatElevator elev)
        {
            this.agent = agent; this.role = role;
            _L = layout; _res = res; _elev = elev;
            _home = layout.nav.IndexOf(HabitatLayout.WorkNode(role));
            if (_home < 0) throw new ArgumentException("no work node for " + role);
            _at = _home;
            Position = layout.nav.nodes[_home].position;
            Visibility = 1f;
            State = WalkerState.Home;
            Anim = WalkerAnim.Work;
            _res.TryReserve(_home, agent);
            if (_elev != null) _elev.RiderEvicted += OnEvicted;
        }

        public void Dispose() { if (_elev != null) _elev.RiderEvicted -= OnEvicted; }

        /// <summary>The ride failed (timed out, fault, interrupt). A walker that is already at its desk (sent home by a reset) has
        /// nothing to re-home: the late eviction of its old ticket is ignored.</summary>
        void OnEvicted(string who)
        {
            if (who != agent || State == WalkerState.Home || State == WalkerState.AtRest || State == WalkerState.SoftTransfer) return;
            BeginSoftTransfer("elevator timed out");
        }

        /// <summary>Segment ends: where a walker may stand still. The cubicle Entry is NOT one (rc10): it sits within two
        /// capsules of the cubicle's corridor door, so a walker waiting there blocked the corridor.</summary>
        static bool IsStop(HabitatNodeKind k)
        {
            return k == HabitatNodeKind.Work || k == HabitatNodeKind.Aisle || k == HabitatNodeKind.Cabin;
        }

        /// <summary>Off the corridor: a walker standing here blocks nobody else's way.</summary>
        static bool IsRest(HabitatNodeKind k) { return k == HabitatNodeKind.Work || k == HabitatNodeKind.Aisle; }

        float BasePriority => TripHasRide ? RiderPriority : 0f;
        float LeasePriority => BasePriority + _waitTimer;

        /// <summary>Plan a trip; true when a route exists. Mid-ride the new trip waits until the ride ends.</summary>
        public bool CanRoute(string nodeId, out bool needsRide)
        {
            needsRide = false;
            int target = _L.nav.IndexOf(nodeId);
            var tmp = new List<int>();
            if (target < 0 || !_L.nav.FindRoute(_at, target, tmp)) return false;
            for (int i = 1; i < tmp.Count; i++) if (_L.nav.IsRide(tmp[i - 1], tmp[i])) { needsRide = true; break; }
            return true;
        }

        /// <summary>Walk to a node, stay <paramref name="dwellSeconds"/>, then walk home (when <paramref name="returnHome"/>).
        /// False (and nothing moves) when there is no route.</summary>
        public bool GoTo(string nodeId, float dwellSeconds, bool returnHome = true)
        {
            int target = _L.nav.IndexOf(nodeId);
            if (target < 0) { LastBlock = "unknown node " + nodeId; return false; }
            if (State == WalkerState.Riding || State == WalkerState.SoftTransfer || State == WalkerState.Walking)
            {
                // Finish the step/ride in progress first; the new trip starts from the next stop.
                _pendingTarget = nodeId; _pendingDwell = dwellSeconds; _pendingReturn = returnHome;
                return true;
            }
            var route = new List<int>();
            if (!_L.nav.FindRoute(_at, target, route)) { LastBlock = "no route to " + nodeId; return false; }
            if (State == WalkerState.WaitingElevator && _elev != null) _elev.Cancel(agent);
            if (_elev != null) _elev.ReportReachedDestination(agent);   // a new trip: the last ride's journey ends where we stand
            _spotReported = _faceReported = false;
            _res.ClearClaim(agent);
            _res.ReleaseAllExcept(agent, route);                     // the way back held during a dwell is on the new route
            HoldingInCabin = false;
            _holdBehind = false;
            _route.Clear(); _route.AddRange(route);
            TripHasRide = false;
            for (int i = 1; i < _route.Count; i++) if (_L.nav.IsRide(_route[i - 1], _route[i])) { TripHasRide = true; break; }
            _dwell = dwellSeconds;
            _returnHomeAfter = returnHome;
            _tripTimer = 0f;
            _step = 1;
            _segEnd = 0;
            if (_route.Count <= 1) { Arrive(); return true; }
            State = WalkerState.Leasing;
            _waitTimer = 0f;
            return true;
        }

        public bool GoHome() { return GoTo(HabitatLayout.WorkNode(role), 0f, false); }

        /// <summary>Recenter, pause, mode exit: drop everything and stand at the desk now.</summary>
        public void ForceHome()
        {
            if (_elev != null) _elev.Cancel(agent);
            _spotReported = _faceReported = false;
            _res.ReleaseAll(agent);
            _route.Clear();
            _pendingTarget = null;
            _at = _home;
            _res.TryReserve(_home, agent);
            Position = _L.nav.nodes[_home].position;
            Yaw = 0f;
            _speed = 0f; _yawVel = 0f;
            Visibility = 1f;
            TripHasRide = false;
            HoldingInCabin = false;
            _holdBehind = false;
            _waitTimer = 0f;
            State = WalkerState.Home;
            Anim = WalkerAnim.Work;
        }

        void BeginSoftTransfer(string why)
        {
            LastBlock = why;
            SoftTransfers++;
            if (_elev != null && !_elev.IsRiding(agent)) _elev.Cancel(agent);
            _res.ClearClaim(agent);
            _spotReported = _faceReported = false;
            HoldingInCabin = false;
            _fade = 0f;
            _speed = 0f;
            State = WalkerState.SoftTransfer;
            Anim = WalkerAnim.Idle;
        }

        void FaceDesk(float dt) { Yaw = MuseDesk.Motion.Damped.FollowAngle(Yaw, 0f, ref _yawVel, settings.turnSmoothTime, dt); }

        public void Tick(float dt)
        {
            if (dt < 0f) dt = 0f;
            _dt = dt;
            // Waiting off the corridor for the ride lane is not travel time (dispatcher only; it has its own limit).
            bool laneWait = State == WalkerState.WaitingElevator && _elev != null && _elev.UsesWaitSpot;
            bool travelling = State != WalkerState.Home && State != WalkerState.AtRest && State != WalkerState.SoftTransfer;
            if (travelling && !laneWait) _tripTimer += dt;
            if (travelling && State != WalkerState.Riding && _tripTimer > Mathf.Max(1f, settings.maxTripSeconds))
                BeginSoftTransfer("trip took too long");

            switch (State)
            {
                case WalkerState.Home:
                    Anim = WalkerAnim.Work;
                    FaceDesk(dt);
                    return;

                case WalkerState.AtRest:
                    Anim = WalkerAnim.Idle;
                    return;

                case WalkerState.Leasing:
                case WalkerState.WaitingElevator:
                {
                    _waitTimer += dt;
                    Anim = WalkerAnim.Wait;
                    if (TryLease()) { State = WalkerState.Walking; _waitTimer = 0f; StepWalk(dt); return; }
                    float limit;
                    if (State == WalkerState.WaitingElevator)
                        limit = _elev != null && _elev.UsesWaitSpot
                            ? Mathf.Max(settings.elevatorWaitTimeout, Mathf.Max(settings.queueWaitTimeout, settings.laneWaitTimeout))
                            : Mathf.Max(settings.elevatorWaitTimeout, settings.stuckTimeout);
                    else limit = Mathf.Max(settings.leaseTimeout, settings.stuckTimeout);
                    if (_waitTimer > limit) BeginSoftTransfer(State == WalkerState.WaitingElevator ? "elevator busy" : "corridor busy (" + LastBlock + ")");
                    return;
                }

                case WalkerState.Walking:
                    StepWalk(dt);
                    return;

                case WalkerState.Riding:
                {
                    Anim = WalkerAnim.Ride;
                    _speed = 0f;
                    Vector3 p = Position; p.y = _elev.CabinY; Position = p;
                    FaceDesk(dt);   // face out of the cabin door, toward the user
                    if (!_elev.MustExit(agent)) { HoldingInCabin = false; _waitTimer = 0f; return; }
                    // Skip the cabin nodes of the ride: we are now at the destination floor's cabin node.
                    while (_step < _route.Count && _L.nav.nodes[_route[_step]].kind == HabitatNodeKind.Cabin) { _at = _route[_step]; _step++; }
                    Position = _L.nav.nodes[_at].position;
                    _exitReported = false;
                    if (_step >= _route.Count) { HoldingInCabin = false; _elev.ReportExited(agent); Arrive(); return; }
                    if (TryLease()) { HoldingInCabin = false; _waitTimer = 0f; State = WalkerState.Walking; StepWalk(dt); return; }
                    // The floor is busy: stay in the cabin with the doors held open (TryLease asked the elevator to keep
                    // waiting) and our exit claimed with rider priority; whoever is in the way finishes and the floor clears.
                    HoldingInCabin = true;
                    _waitTimer += dt;
                    if (_waitTimer > Mathf.Max(1f, settings.stuckTimeout)) BeginSoftTransfer("stuck in the cabin (" + LastBlock + ")");
                    return;
                }

                case WalkerState.Dwelling:
                    Anim = WalkerAnim.Present;
                    _dwellTimer += dt;
                    if (_dwellTimer < _dwell) return;
                    if (_pendingTarget != null) { StartPending(); return; }
                    if (_returnHomeAfter) { _returnHomeAfter = false; if (!GoHome()) BeginSoftTransfer("no route home"); }
                    else State = WalkerState.AtRest;
                    return;

                case WalkerState.SoftTransfer:
                {
                    _fade += dt;
                    float half = Mathf.Max(0.01f, settings.softTransferSeconds * 0.5f);
                    if (_fade < half) { Visibility = 1f - _fade / half; return; }
                    if (_at != _home || (Position - _L.nav.nodes[_home].position).sqrMagnitude > 1e-8f)
                    {
                        if (_elev != null) _elev.Cancel(agent);
                        _res.ReleaseAll(agent);
                        _at = _home;
                        _res.TryReserve(_home, agent);
                        Position = _L.nav.nodes[_home].position;
                        Yaw = 0f; _yawVel = 0f; _speed = 0f;
                    }
                    Visibility = Mathf.Clamp01((_fade - half) / half);
                    if (_fade < settings.softTransferSeconds) return;
                    Visibility = 1f;
                    _route.Clear();
                    TripHasRide = false;
                    _holdBehind = false;
                    _tripTimer = 0f;
                    State = WalkerState.Home;
                    Anim = WalkerAnim.Work;
                    var h = ReturnedHome; if (h != null) h();
                    if (_pendingTarget != null) StartPending();
                    return;
                }
            }
        }

        int SegmentEnd(int start)
        {
            for (int k = start; k < _route.Count; k++)
            {
                if (k == _route.Count - 1) return k;
                if (IsStop(_L.nav.nodes[_route[k]].kind)) return k;
                if (IsWaitStop(k)) return k;
            }
            return _route.Count - 1;
        }

        /// <summary>A segment that ends at the trip's destination ON the corridor, with the walk home to follow, keeps every node
        /// it passes until the walker is back: the way home is the same way, so it never waits on the corridor after its dwell.</summary>
        bool HoldsWayBack(int end)
        {
            if (end != _route.Count - 1 || !_returnHomeAfter || _pendingTarget != null) return false;
            HabitatNodeKind k = _L.nav.nodes[_route[end]].kind;
            return !IsRest(k) && k != HabitatNodeKind.Cabin;
        }

        /// <summary>Reserve the whole next segment, or nothing. A segment into the cabin needs the cabin open for us.
        /// When the nodes are taken, the walker claims them (so the floor clears for it) and tells the elevator it is still
        /// coming; the caller decides where it waits (its aisle, the wait spot, or the cabin).</summary>
        bool TryLease()
        {
            if (_step >= _route.Count) return true;
            int end = SegmentEnd(_step);
            HabitatNavNode endNode = _L.nav.nodes[_route[end]];
            if (_elev != null && _elev.UsesWaitSpot)
            {
                if (!WaitSpotHandshake(end, endNode)) { _res.ClearClaim(agent); return false; }
            }
            else if (endNode.kind == HabitatNodeKind.Cabin && _L.nav.nodes[_at].kind != HabitatNodeKind.Cabin)
            {
                int dest = RideDestinationFloor(end);
                if (dest >= 0 && dest != endNode.floor)
                {
                    if (!_elev.IsQueued(agent)) _elev.Request(agent, endNode.floor, dest);
                    if (!_elev.MayBoard(agent)) { _res.ClearClaim(agent); WaitForLift(); return false; }
                }
            }
            float priority = LeasePriority;
            for (int k = _step; k <= end; k++)
                if (!_res.CanLease(_route[k], agent, priority))
                {
                    LastBlock = "waiting for " + _L.nav.nodes[_route[k]].id;
                    _res.SetClaim(agent, _route, _step, end, priority);
                    if (_elev != null) _elev.KeepWaiting(agent);
                    if (State == WalkerState.WaitingElevator) { State = WalkerState.Leasing; _waitTimer = 0f; }   // the lift is ours: now it is the corridor
                    return false;
                }
            for (int k = _step; k <= end; k++) _res.TryReserve(_route[k], agent);
            _res.ClearClaim(agent);
            _segEnd = end;
            _holdBehind = HoldsWayBack(end);
            return true;
        }

        // ── dispatcher handshake (IHabitatElevator.UsesWaitSpot) ──

        /// <summary>Route index k is the floor's queue node and the route goes on through the landing into the cabin for a ride:
        /// the rider must stop here, report, and turn to face the door.</summary>
        bool IsWaitStop(int k)
        {
            if (_elev == null || !_elev.UsesWaitSpot || k < 0 || k + 3 >= _route.Count) return false;
            if (_L.nav.nodes[_route[k]].kind != HabitatNodeKind.Queue) return false;
            if (_L.nav.nodes[_route[k + 1]].kind != HabitatNodeKind.Landing) return false;
            if (_L.nav.nodes[_route[k + 2]].kind != HabitatNodeKind.Cabin) return false;
            return _L.nav.nodes[_route[k + 3]].kind == HabitatNodeKind.Cabin;
        }

        /// <summary>Index of the wait stop ahead of <paramref name="from"/> on this floor (before any cabin), or -1.</summary>
        int BoardingAhead(int from)
        {
            for (int k = Mathf.Max(0, from); k < _route.Count; k++)
            {
                if (_L.nav.nodes[_route[k]].kind == HabitatNodeKind.Cabin) return -1;
                if (IsWaitStop(k)) return k;
            }
            return -1;
        }

        bool WaitForLift()
        {
            if (State != WalkerState.WaitingElevator && State != WalkerState.Riding) { State = WalkerState.WaitingElevator; _waitTimer = 0f; }
            return false;
        }

        /// <summary>
        /// The dispatcher's explicit steps for the segment that ends at route index <paramref name="end"/>. True: the segment
        /// may be leased now. False: wait where we stand (never on the way to doors that are not open for us).
        ///  - Standing up to the aisle beside the desk is always allowed (that is where a rider queues, off the corridor).
        ///  - Any further step toward the wait spot needs a ticket and the dispatcher's go-ahead (<c>MayApproach</c>).
        ///  - From the wait spot into the cabin: report at the spot, turn to face the door, report, then wait for <c>MayBoard</c>.
        /// </summary>
        bool WaitSpotHandshake(int end, HabitatNavNode endNode)
        {
            HabitatNavNode here = _L.nav.nodes[_at];
            if (endNode.kind == HabitatNodeKind.Cabin && here.kind != HabitatNodeKind.Cabin)
            {
                int dest = RideDestinationFloor(end);
                if (dest < 0 || dest == endNode.floor) return true;
                if (!_elev.IsQueued(agent)) { _elev.Request(agent, endNode.floor, dest); _spotReported = _faceReported = false; }
                if (!_spotReported)
                {
                    if (!_elev.MayApproach(agent)) return WaitForLift();
                    _elev.ReportAtWaitSpot(agent);
                    _spotReported = true;
                }
                if (!_faceReported)
                {
                    Vector3 d = _L.nav.nodes[_route[_step]].position - Position; d.y = 0f;
                    if (d.sqrMagnitude > 1e-10f)
                    {
                        float heading = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                        Yaw = MuseDesk.Motion.Damped.FollowAngle(Yaw, heading, ref _yawVel, settings.turnSmoothTime, _dt);
                        if (Mathf.Abs(Mathf.DeltaAngle(Yaw, heading)) > settings.faceDoorTolerance) return WaitForLift();
                    }
                    _elev.ReportFacingDoor(agent);
                    _faceReported = true;
                }
                if (!_elev.MayBoard(agent)) return WaitForLift();
                return true;
            }
            int q = BoardingAhead(_step);
            if (q < 0 || here.kind == HabitatNodeKind.Cabin) return true;
            if (here.kind == HabitatNodeKind.Work) return true;                 // stand up to the aisle: off the corridor
            int rideTo = RideDestinationFloor(q + 2);
            if (rideTo < 0) return true;
            if (!_elev.IsQueued(agent)) { _elev.Request(agent, _L.nav.nodes[_route[q]].floor, rideTo); _spotReported = _faceReported = false; }
            if (!_elev.MayApproach(agent)) return WaitForLift();
            return true;
        }

        int RideDestinationFloor(int cabinIndex)
        {
            int c = cabinIndex;
            while (c + 1 < _route.Count && _L.nav.nodes[_route[c + 1]].kind == HabitatNodeKind.Cabin) c++;
            return c == cabinIndex ? -1 : _L.nav.nodes[_route[c]].floor;
        }

        void StepWalk(float dt)
        {
            if (_step >= _route.Count) { Arrive(); return; }
            int next = _route[_step];
            HabitatNavNode nn = _L.nav.nodes[next];
            // Boarding: the doors must still be open for us on every step into the cabin.
            if (nn.kind == HabitatNodeKind.Cabin && !_elev.MayBoard(agent) && !_elev.IsRiding(agent))
            { BeginSoftTransfer("cabin left without us"); return; }
            Anim = WalkerAnim.Walk;
            TryExtendLease();
            if (!MoveToward(nn.position, dt)) return;

            int left = _at;
            _at = next;
            _step++;
            if (left != _at && !_holdBehind) _res.Release(left, agent);
            if (!_exitReported && _L.nav.nodes[left].kind == HabitatNodeKind.Cabin && nn.kind != HabitatNodeKind.Cabin && _elev != null)
            {
                _elev.ReportExited(agent);       // out of the door: the cabin is free
                _exitReported = true;
            }

            if (nn.kind == HabitatNodeKind.Cabin && _step < _route.Count && _L.nav.nodes[_route[_step]].kind == HabitatNodeKind.Cabin)
            {
                _elev.ReportBoarded(agent);
                _spotReported = _faceReported = false;
                _speed = 0f;   // A16: a rider is stationary from the first riding tick (no walk cycle in the cabin)
                _waitTimer = 0f;
                HoldingInCabin = false;
                State = WalkerState.Riding;
                return;
            }
            if (_step >= _route.Count) { Arrive(); return; }
            if (_step > _segEnd)
            {
                if (_pendingTarget != null && IsStop(nn.kind) && nn.kind != HabitatNodeKind.Cabin) { StartPending(); return; }
                State = WalkerState.Leasing;
                _waitTimer = 0f;
                if (TryLease()) State = WalkerState.Walking;
            }
        }

        /// <summary>
        /// Move along the CURRENT validated segment toward <paramref name="target"/> with a velocity profile: accelerate from
        /// rest, cruise, and decelerate so the character is stopped at the next stop point (segment end or a corner sharper
        /// than <see cref="WalkerSettings.walkTurnLimit"/>). Heading is critically damped, and the speed allowed scales with
        /// how well the body faces the direction of travel, so a sharp turn happens in place before walking. The path is
        /// unchanged (same straight segments the validator proves clear), only the timing along it.
        /// </summary>
        bool MoveToward(Vector3 target, float dt)
        {
            Vector3 d = target - Position;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist <= 1e-5f) { Position = target; return true; }
            float heading = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            Yaw = MuseDesk.Motion.Damped.FollowAngle(Yaw, heading, ref _yawVel, settings.turnSmoothTime, dt);
            float err = Mathf.Abs(Mathf.DeltaAngle(Yaw, heading));
            float align = Mathf.Clamp01(1f - err / Mathf.Max(1f, settings.walkTurnLimit));
            float stop = DistanceToStop();
            float want = Mathf.Min(settings.walkSpeed * align, Mathf.Sqrt(2f * Decel * stop));
            _speed = Mathf.MoveTowards(_speed, want, (want > _speed ? Accel : Decel) * dt);
            float stepLen = _speed * dt;
            if (stepLen >= dist) { Position = target; return true; }
            Position += d / dist * stepLen;
            return false;
        }

        /// <summary>Path distance from here to the next point where the character must be standing still.</summary>
        float DistanceToStop()
        {
            Vector3 p = Position;
            float sum = 0f;
            for (int k = _step; k < _route.Count; k++)
            {
                Vector3 q = _L.nav.nodes[_route[k]].position;
                Vector3 seg = q - p; seg.y = 0f;
                sum += seg.magnitude;
                if (k >= _segEnd || k == _route.Count - 1) return sum;
                Vector3 next = _L.nav.nodes[_route[k + 1]].position - q; next.y = 0f;
                if (seg.sqrMagnitude > 1e-10f && next.sqrMagnitude > 1e-10f && Vector3.Angle(seg, next) > settings.walkTurnLimit) return sum;
                p = q;
            }
            return sum;
        }

        /// <summary>
        /// While walking, lease the NEXT segment early when it is free, so a multi-segment trip flows through doorways
        /// instead of stopping at each one. Never into the cabin (boarding needs a stop and the elevator's handshake),
        /// never while a new trip is pending, never through a zone someone with a higher claim is waiting for, and never by
        /// waiting: if any node is taken, nothing extra is held.
        /// </summary>
        void TryExtendLease()
        {
            if (_pendingTarget != null) return;
            int start = _segEnd + 1;
            if (start >= _route.Count || start <= _step) return;
            if (_L.nav.nodes[_route[_segEnd]].kind == HabitatNodeKind.Cabin) return;
            // Never flow onto the corridor toward the wait spot without the dispatcher's go-ahead.
            if (_elev != null && _elev.UsesWaitSpot && BoardingAhead(start) >= 0 && !_elev.MayApproach(agent)) return;
            int end = SegmentEnd(start);
            float priority = BasePriority;
            for (int k = start; k <= end; k++)
            {
                if (_L.nav.nodes[_route[k]].kind == HabitatNodeKind.Cabin) return;
                if (!_res.CanLease(_route[k], agent, priority)) return;
            }
            for (int k = start; k <= end; k++) _res.TryReserve(_route[k], agent);
            _segEnd = end;
            _holdBehind = HoldsWayBack(end);
        }

        void Arrive()
        {
            _speed = 0f;
            HoldingInCabin = false;
            _res.ClearClaim(agent);
            if (_elev != null) _elev.ReportReachedDestination(agent);
            if (_at == _home)
            {
                State = WalkerState.Home;
                Anim = WalkerAnim.Work;
                _route.Clear();
                TripHasRide = false;
                _holdBehind = false;
                _tripTimer = 0f;
                _res.ReleaseAllExcept(agent, new[] { _home });
                var h = ReturnedHome; if (h != null) h();
                if (_pendingTarget != null) StartPending();
                return;
            }
            State = WalkerState.Dwelling;
            _dwellTimer = 0f;
            Anim = WalkerAnim.Present;
            var a = ArrivedAt;
            if (a != null) a(_L.nav.nodes[_at].id);
        }

        void StartPending()
        {
            string t = _pendingTarget; _pendingTarget = null;
            State = WalkerState.AtRest;
            if (!GoTo(t, _pendingDwell, _pendingReturn)) { if (!GoHome()) BeginSoftTransfer("no route"); }
        }
    }
}
