using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>
    /// Who stands on (or is heading to) which nav node. A node is refused when another character holds any
    /// node of the same floor closer than <see cref="zoneRadius"/> (two capsules plus clearance), so two
    /// characters can never overlap even on neighbouring nodes. Cabin nodes are owned by the elevator.
    /// </summary>
    public sealed class HabitatReservations
    {
        readonly HabitatLayout _layout;
        readonly string[] _owner;
        public float zoneRadius;

        public HabitatReservations(HabitatLayout layout)
        {
            _layout = layout;
            _owner = new string[layout.nav.nodes.Count];
            zoneRadius = 2f * layout.agentRadius + layout.clearance;
        }

        public string OwnerOf(int node) { return node >= 0 && node < _owner.Length ? _owner[node] : null; }

        public bool CanReserve(int node, string agent)
        {
            if (node < 0 || node >= _owner.Length) return false;
            HabitatNavNode n = _layout.nav.nodes[node];
            if (n.kind == HabitatNodeKind.Cabin) return true;
            for (int i = 0; i < _owner.Length; i++)
            {
                string o = _owner[i];
                if (o == null || o == agent) continue;
                HabitatNavNode m = _layout.nav.nodes[i];
                if (m.kind == HabitatNodeKind.Cabin) continue;
                if (m.floor != n.floor) continue;
                if (i == node || (m.position - n.position).magnitude < zoneRadius) return false;
            }
            return true;
        }

        public bool TryReserve(int node, string agent)
        {
            if (!CanReserve(node, agent)) return false;
            if (_layout.nav.nodes[node].kind != HabitatNodeKind.Cabin) _owner[node] = agent;
            return true;
        }

        public void Release(int node, string agent)
        {
            if (node >= 0 && node < _owner.Length && _owner[node] == agent) _owner[node] = null;
        }

        public void ReleaseAll(string agent)
        {
            for (int i = 0; i < _owner.Length; i++) if (_owner[i] == agent) _owner[i] = null;
        }

        public int CountHeldBy(string agent)
        {
            int c = 0;
            for (int i = 0; i < _owner.Length; i++) if (_owner[i] == agent) c++;
            return c;
        }
    }

    public enum ElevatorState
    {
        IdleAtFloor = 0, Reserved = 1, DoorsOpening = 2, Boarding = 3, DoorsClosing = 4, Traveling = 5,
        Arrived = 6, Exiting = 7, Releasing = 8, Interrupted = 9, Recovering = 10,
    }

    [Serializable]
    public sealed class ElevatorSettings
    {
        public float doorSeconds = 0.45f;           // polish spec: 0.30-0.55 s
        public float speed = 0.22f;                 // cruise m/s, habitat scale
        public float accelSeconds = 0.6f;           // rest -> cruise
        public float brakeSeconds = 0.7f;           // cruise -> rest at the landing
        [Tooltip("The rider walks from its cubicle to the cabin while the doors wait; longer than that walk.")]
        public float boardTimeout = 15f;
        public float exitTimeout = 8f;
        public float arriveDwell = 0.15f;
    }

    /// <summary>
    /// One cabin, one rider (v1), a fair FIFO queue. The cabin's height follows the real landing heights
    /// (never a baked animation distance). Door/travel clips are cosmetic: this machine advances on time,
    /// so a missing clip cannot stall it. A rider that never boards or never leaves is timed out and the
    /// walker re-homes it; nothing here touches task state.
    /// </summary>
    public sealed class HabitatElevatorMachine
    {
        sealed class Ticket { public int id; public string agent; public int from; public int to; public bool aboard; }

        readonly float[] _floorY;
        readonly List<Ticket> _queue = new List<Ticket>();
        Ticket _active;
        int _nextId = 1;
        float _timer;

        public ElevatorSettings settings = new ElevatorSettings();
        public ElevatorState State { get; private set; }
        public float CabinY { get; private set; }
        public float DoorOpen01 { get; private set; }
        public int CabinFloor { get; private set; }
        /// <summary>The floor the cabin is heading to (or at).</summary>
        public int TargetFloor { get; private set; }
        public string Rider => _active != null ? _active.agent : null;
        public bool RiderAboard => _active != null && _active.aboard;
        public int QueueLength => _queue.Count;
        public event Action<ElevatorState> StateChanged;
        /// <summary>A rider was timed out (never boarded, never left, or the trip was interrupted): re-home it.</summary>
        public event Action<string> RiderEvicted;

        public HabitatElevatorMachine(float[] floorHeights, int startFloor = 0)
        {
            _floorY = floorHeights;
            CabinFloor = TargetFloor = Mathf.Clamp(startFloor, 0, floorHeights.Length - 1);
            CabinY = _floorY[CabinFloor];
            State = ElevatorState.IdleAtFloor;
        }

        public float FloorHeight(int f) { return _floorY[Mathf.Clamp(f, 0, _floorY.Length - 1)]; }
        public int FloorCount => _floorY.Length;

        /// <summary>Ask for a ride. A second request from the same agent replaces its destination (coalesced).</summary>
        public int Request(string agent, int from, int to)
        {
            if (string.IsNullOrEmpty(agent) || from == to) return 0;
            from = Mathf.Clamp(from, 0, _floorY.Length - 1);
            to = Mathf.Clamp(to, 0, _floorY.Length - 1);
            if (_active != null && _active.agent == agent)
            {
                if (!_active.aboard) { _active.from = from; _active.to = to; }
                return _active.id;
            }
            for (int i = 0; i < _queue.Count; i++)
                if (_queue[i].agent == agent) { _queue[i].from = from; _queue[i].to = to; return _queue[i].id; }
            var t = new Ticket { id = _nextId++, agent = agent, from = from, to = to };
            _queue.Add(t);
            return t.id;
        }

        public bool IsQueued(string agent)
        {
            if (_active != null && _active.agent == agent) return true;
            for (int i = 0; i < _queue.Count; i++) if (_queue[i].agent == agent) return true;
            return false;
        }

        /// <summary>Withdraw a request. Queued or not yet aboard: removed. Aboard: the trip finishes (safe) and
        /// the rider leaves at the destination as usual.</summary>
        public void Cancel(string agent)
        {
            for (int i = _queue.Count - 1; i >= 0; i--) if (_queue[i].agent == agent) _queue.RemoveAt(i);
            if (_active != null && _active.agent == agent && !_active.aboard)
            {
                _active = null;
                Go(DoorOpen01 > 0f ? ElevatorState.DoorsClosing : ElevatorState.IdleAtFloor);
            }
        }

        /// <summary>True while the cabin waits with open doors at the rider's floor for this agent to step in.</summary>
        public bool MayBoard(string agent) { return State == ElevatorState.Boarding && _active != null && _active.agent == agent && !_active.aboard; }
        public bool MustExit(string agent) { return State == ElevatorState.Exiting && _active != null && _active.agent == agent && _active.aboard; }
        public bool IsRiding(string agent) { return _active != null && _active.agent == agent && _active.aboard; }
        public int DestinationOf(string agent) { return _active != null && _active.agent == agent ? _active.to : -1; }

        public void ReportBoarded(string agent)
        {
            if (!MayBoard(agent)) return;
            _active.aboard = true;
            Go(ElevatorState.DoorsClosing);
        }

        public void ReportExited(string agent)
        {
            if (_active == null || _active.agent != agent || !_active.aboard) return;
            _active = null;
            Go(ElevatorState.Releasing);
        }

        /// <summary>Recenter, pause, mode change: stop at the nearest landing, open, evict the rider (the walker
        /// re-homes it), then idle. Never leaves the cabin between floors.</summary>
        public void Interrupt()
        {
            _queue.Clear();
            Go(ElevatorState.Interrupted);
        }

        void Go(ElevatorState s)
        {
            _timer = 0f;
            if (State == s) return;
            State = s;
            var h = StateChanged;
            if (h != null) h(s);
        }

        public void Tick(float dt)
        {
            if (dt < 0f) dt = 0f;
            _timer += dt;
            switch (State)
            {
                case ElevatorState.IdleAtFloor:
                    if (_queue.Count == 0) return;
                    _active = _queue[0];
                    _queue.RemoveAt(0);
                    TargetFloor = _active.from;
                    Go(CabinFloor == _active.from ? ElevatorState.DoorsOpening : ElevatorState.Reserved);
                    return;

                case ElevatorState.Reserved:          // travelling empty to pick the rider up
                    if (_active == null) { Go(ElevatorState.IdleAtFloor); return; }
                    TargetFloor = _active.from;
                    if (Move(dt)) Go(ElevatorState.DoorsOpening);
                    return;

                case ElevatorState.DoorsOpening:
                    DoorOpen01 = Mathf.Min(1f, DoorOpen01 + dt / Mathf.Max(0.01f, settings.doorSeconds));
                    if (DoorOpen01 < 1f) return;
                    if (_active == null) { Go(ElevatorState.DoorsClosing); return; }
                    Go(_active.aboard ? ElevatorState.Exiting : ElevatorState.Boarding);
                    return;

                case ElevatorState.Boarding:
                    if (_active == null) { Go(ElevatorState.DoorsClosing); return; }
                    if (_timer > settings.boardTimeout)
                    {
                        string who = _active.agent;
                        _active = null;
                        Go(ElevatorState.DoorsClosing);
                        Evict(who);
                    }
                    return;

                case ElevatorState.DoorsClosing:
                    DoorOpen01 = Mathf.Max(0f, DoorOpen01 - dt / Mathf.Max(0.01f, settings.doorSeconds));
                    if (DoorOpen01 > 0f) return;
                    if (_active != null && _active.aboard) { TargetFloor = _active.to; Go(ElevatorState.Traveling); }
                    else Go(ElevatorState.IdleAtFloor);
                    return;

                case ElevatorState.Traveling:
                    if (_active == null) { Go(ElevatorState.Arrived); return; }
                    TargetFloor = _active.to;
                    if (Move(dt)) Go(ElevatorState.Arrived);
                    return;

                case ElevatorState.Arrived:
                    if (_timer >= settings.arriveDwell) Go(ElevatorState.DoorsOpening);
                    return;

                case ElevatorState.Exiting:
                    if (_active == null) { Go(ElevatorState.Releasing); return; }
                    if (_timer > settings.exitTimeout)
                    {
                        string who = _active.agent;
                        _active = null;
                        Go(ElevatorState.Releasing);
                        Evict(who);
                    }
                    return;

                case ElevatorState.Releasing:
                    Go(ElevatorState.DoorsClosing);
                    return;

                case ElevatorState.Interrupted:
                {
                    // Nearest landing, then doors open, then evict.
                    int nearest = NearestFloor(CabinY);
                    TargetFloor = nearest;
                    if (!Move(dt)) return;
                    Go(ElevatorState.Recovering);
                    return;
                }

                case ElevatorState.Recovering:
                    DoorOpen01 = Mathf.Min(1f, DoorOpen01 + dt / Mathf.Max(0.01f, settings.doorSeconds));
                    if (DoorOpen01 < 1f) return;
                    if (_active != null) { string who = _active.agent; _active = null; Evict(who); }
                    Go(ElevatorState.DoorsClosing);
                    return;
            }
        }

        void Evict(string who)
        {
            var h = RiderEvicted;
            if (h != null && who != null) h(who);
        }

        /// <summary>Move the cabin toward TargetFloor. True when it is there.</summary>
        bool Move(float dt)
        {
            float target = _floorY[TargetFloor];
            // Accelerate, cruise, brake and land exactly on the landing height (polish spec 5.4: align before doors open).
            var m = new MuseDesk.Motion.ProfiledMover { position = CabinY, velocity = _cabinVel };
            bool there = m.Step(target, settings.speed,
                                settings.speed / Mathf.Max(0.05f, settings.accelSeconds),
                                settings.speed / Mathf.Max(0.05f, settings.brakeSeconds), dt);
            CabinY = m.position; _cabinVel = m.velocity;
            if (!there) return false;
            CabinY = target; _cabinVel = 0f;
            CabinFloor = TargetFloor;
            return true;
        }

        float _cabinVel;
        /// <summary>Cabin vertical speed (habitat m/s, signed).</summary>
        public float CabinVelocity => _cabinVel;
        /// <summary>Door opening with ease-in-out applied: what the view should draw (the machine logic uses the linear value).</summary>
        public float DoorOpenEased => MuseDesk.Motion.Ease.Evaluate(MuseDesk.Motion.EaseKind.InOutCubic, DoorOpen01);

        int NearestFloor(float y)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < _floorY.Length; i++) { float d = Mathf.Abs(_floorY[i] - y); if (d < bd) { bd = d; best = i; } }
            return best;
        }
    }
}
