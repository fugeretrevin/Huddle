using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>A real backend fact the habitat may illustrate. Built by the runtime from reducer events only.</summary>
    public enum HabitatEventKind { TaskAssigned = 0, TaskStarted = 1, TaskCompleted = 2, TaskFailed = 3, Handoff = 4, ApprovalRequested = 5, Reset = 6 }

    public struct HabitatEvent
    {
        /// <summary>The backend event id (or seq). Duplicates are ignored.</summary>
        public string eventId;
        public HabitatEventKind kind;
        public HabitatRole agent;
        public HabitatRole target;
        public bool hasTarget;
        public string taskId;

        public static HabitatEvent Of(string id, HabitatEventKind k, HabitatRole agent, string taskId = null)
        { return new HabitatEvent { eventId = id, kind = k, agent = agent, taskId = taskId }; }

        public static HabitatEvent HandoffOf(string id, HabitatRole from, HabitatRole to, string taskId = null)
        { return new HabitatEvent { eventId = id, kind = HabitatEventKind.Handoff, agent = from, target = to, hasTarget = true, taskId = taskId }; }
    }

    public enum HabitatActKind { CarryToManager = 0, CarryToAgent = 1, Dispatch = 2, Warning = 3 }

    /// <summary>One short journey that illustrates an event. Cosmetic: dropping it never loses information,
    /// because status lights and cards always show the current authoritative state.</summary>
    public sealed class HabitatAct
    {
        public string eventId;
        public HabitatActKind kind;
        public HabitatRole actor;
        public HabitatRole target;
        public string taskId;
        public string targetNode;
        public float dwell;
        public string prop;            // "blueprint", "document", "warning"
        public float enqueuedAt;
        public float deadline;
        public bool started;
        public bool needsRide;
    }

    [Serializable]
    public sealed class StageSettings
    {
        public int maxPendingPerAgent = 2;
        // An act not started within this is dropped (stale). A full elevator round trip takes about 25 s,
        // so a second rider waiting for the elevator lane must be allowed to wait for one whole trip.
        public float actStartDeadline = 60f;
        public float handoffDwell = 1.6f;
        public float managerDwell = 2.0f;
        public float dispatchDwell = 1.2f;
        public int dedupeMemory = 512;
    }

    /// <summary>
    /// The habitat's visual coordination, pure logic: one reservation table, one elevator, one walker per
    /// role, and the event-to-act queue. Rules:
    ///  - every act comes from a real event (deduplicated by event id); nothing is made up to justify motion;
    ///  - at most <see cref="StageSettings.maxPendingPerAgent"/> acts wait per character (same kind+target
    ///    coalesces; overflow drops the oldest); an act that could not start in time is dropped;
    ///  - only one trip that needs the elevator runs at a time (the travel lock), so two characters never
    ///    meet on a landing; same-floor trips are kept apart by the walkers' corridor leases;
    ///  - Reset (session reset, recenter, mode exit) sends everyone home at once.
    /// The backend never waits for any of this.
    /// </summary>
    public sealed class HabitatStage
    {
        public readonly HabitatLayout layout;
        public readonly HabitatReservations reservations;
        public readonly HabitatElevatorMachine elevator;
        readonly HabitatWalker[] _walkers = new HabitatWalker[4];
        readonly List<HabitatAct>[] _queues = new List<HabitatAct>[4];
        readonly HabitatAct[] _running = new HabitatAct[4];
        readonly HashSet<string> _seen = new HashSet<string>();
        readonly Queue<string> _seenOrder = new Queue<string>();
        int _travelLock = -1;
        float _now;

        public StageSettings settings = new StageSettings();
        public int Dropped { get; private set; }
        public int Duplicates { get; private set; }
        public int Completed { get; private set; }
        public string LastDropReason { get; private set; }
        /// <summary>Raised when an act starts (the runtime shows the prop) and when it ends.</summary>
        public event Action<HabitatAct> ActStarted;
        public event Action<HabitatAct> ActEnded;

        public HabitatStage(HabitatLayout layout)
        {
            this.layout = layout;
            reservations = new HabitatReservations(layout);
            var heights = new float[layout.floors.Count];
            for (int i = 0; i < heights.Length; i++) heights[i] = layout.floors[i].floorY;
            elevator = new HabitatElevatorMachine(heights, 0);
            for (int r = 0; r < 4; r++)
            {
                var role = (HabitatRole)r;
                _walkers[r] = new HabitatWalker(role.ToString().ToLowerInvariant(), role, layout, reservations, elevator);
                _queues[r] = new List<HabitatAct>();
                int idx = r;
                _walkers[r].ReturnedHome += () => OnReturnedHome(idx);
            }
        }

        public HabitatWalker Walker(HabitatRole r) { return _walkers[(int)r]; }
        public HabitatAct Running(HabitatRole r) { return _running[(int)r]; }
        public int Pending(HabitatRole r) { return _queues[(int)r].Count; }
        public bool TravelLocked => _travelLock >= 0;

        /// <summary>Take a real event. False when it was a duplicate or illustrates nothing.</summary>
        public bool Accept(HabitatEvent e, float now)
        {
            _now = now;
            if (e.kind == HabitatEventKind.Reset) { ResetAll(); return true; }
            if (!string.IsNullOrEmpty(e.eventId))
            {
                if (_seen.Contains(e.eventId)) { Duplicates++; return false; }
                _seen.Add(e.eventId);
                _seenOrder.Enqueue(e.eventId);
                while (_seenOrder.Count > settings.dedupeMemory) _seen.Remove(_seenOrder.Dequeue());
            }
            HabitatAct act = Map(e, now);
            if (act == null) return false;
            Enqueue(act);
            return true;
        }

        HabitatAct Map(HabitatEvent e, float now)
        {
            var a = new HabitatAct { eventId = e.eventId, actor = e.agent, taskId = e.taskId, enqueuedAt = now, deadline = now + settings.actStartDeadline };
            switch (e.kind)
            {
                case HabitatEventKind.Handoff:
                    if (!e.hasTarget || e.target == e.agent || e.agent == HabitatRole.Manager) return null;   // manager delegation = printed cards, not a walk
                    a.target = e.target;
                    if (e.target == HabitatRole.Manager) { a.kind = HabitatActKind.CarryToManager; a.targetNode = HabitatLayout.HandoffAtManager; a.dwell = settings.managerDwell; a.prop = "document"; }
                    else { a.kind = HabitatActKind.CarryToAgent; a.targetNode = HabitatLayout.DoorNode(e.target); a.dwell = settings.handoffDwell; a.prop = "blueprint"; }
                    return a;
                case HabitatEventKind.TaskCompleted:
                    if (e.agent != HabitatRole.Researcher) return null;          // others celebrate in place
                    a.kind = HabitatActKind.CarryToManager; a.target = HabitatRole.Manager; a.targetNode = HabitatLayout.HandoffAtManager; a.dwell = settings.managerDwell; a.prop = "blueprint";
                    return a;
                case HabitatEventKind.TaskFailed:
                    if (e.agent == HabitatRole.Manager) return null;
                    a.kind = HabitatActKind.Warning; a.target = HabitatRole.Manager; a.targetNode = HabitatLayout.HandoffAtManager; a.dwell = settings.managerDwell; a.prop = "warning";
                    return a;
                case HabitatEventKind.ApprovalRequested:
                    if (e.agent == HabitatRole.Manager) return null;
                    a.kind = HabitatActKind.Dispatch; a.target = e.agent; a.targetNode = HabitatLayout.DispatchNode; a.dwell = settings.dispatchDwell; a.prop = "document";
                    return a;
                default:
                    return null;   // assigned / started: shown by the status light and the work animation, not a trip
            }
        }

        void Enqueue(HabitatAct act)
        {
            List<HabitatAct> q = _queues[(int)act.actor];
            for (int i = 0; i < q.Count; i++)
                if (q[i].kind == act.kind && q[i].target == act.target) { q[i] = act; return; }    // coalesce
            q.Add(act);
            while (q.Count > settings.maxPendingPerAgent) { Drop(q[0], "too many events: older one skipped"); q.RemoveAt(0); }
        }

        void Drop(HabitatAct a, string why) { Dropped++; LastDropReason = why + " (" + a.kind + " " + a.actor + ")"; }

        public void Tick(float dt, float now)
        {
            _now = now;
            elevator.Tick(dt);
            for (int r = 0; r < 4; r++) _walkers[r].Tick(dt);
            for (int r = 0; r < 4; r++) TryStart(r);
        }

        void TryStart(int r)
        {
            if (_running[r] != null) return;
            HabitatWalker w = _walkers[r];
            if (!w.AtHome) return;
            List<HabitatAct> q = _queues[r];
            while (q.Count > 0 && _now > q[0].deadline) { Drop(q[0], "event too old to show"); q.RemoveAt(0); }
            if (q.Count == 0) return;
            HabitatAct a = q[0];
            bool ride;
            if (!w.CanRoute(a.targetNode, out ride)) { Drop(a, "no route"); q.RemoveAt(0); return; }
            if (ride && _travelLock >= 0 && _travelLock != r) return;           // wait for the elevator lane
            q.RemoveAt(0);
            a.needsRide = ride;
            if (!w.GoTo(a.targetNode, a.dwell, true)) { Drop(a, "no route"); return; }
            if (ride) _travelLock = r;
            a.started = true;
            _running[r] = a;
            var h = ActStarted; if (h != null) h(a);
        }

        void OnReturnedHome(int r)
        {
            HabitatAct a = _running[r];
            if (_travelLock == r) _travelLock = -1;
            if (a == null) return;
            _running[r] = null;
            Completed++;
            var h = ActEnded; if (h != null) h(a);
        }

        /// <summary>Everyone home now, queues cleared, elevator stopped at a landing. Task state is untouched.</summary>
        public void ResetAll()
        {
            elevator.Interrupt();
            for (int r = 0; r < 4; r++)
            {
                _queues[r].Clear();
                HabitatAct a = _running[r];
                _running[r] = null;
                _walkers[r].ForceHome();
                if (a != null) { var h = ActEnded; if (h != null) h(a); }
            }
            _travelLock = -1;
        }

        public static bool TryRole(string agentId, out HabitatRole role)
        {
            role = HabitatRole.Researcher;
            if (string.IsNullOrEmpty(agentId)) return false;
            switch (agentId.Trim().ToLowerInvariant())
            {
                case "researcher": role = HabitatRole.Researcher; return true;
                case "developer": role = HabitatRole.Developer; return true;
                case "tester": case "qa": role = HabitatRole.Tester; return true;
                case "manager": role = HabitatRole.Manager; return true;
                default: return false;
            }
        }
    }
}
