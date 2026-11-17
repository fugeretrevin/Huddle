using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    /// <summary>The meaning of one nav edge in a multi-floor trip.</summary>
    public enum FloorEdgeClass
    {
        Walkway = 0,              // corridor to corridor on one level
        WalkwayToElevator = 1,    // corridor node to the queue / landing at the shaft
        ElevatorBoarding = 2,     // landing to cabin (and back: cabin to level on exit)
        ElevatorRide = 3,         // cabin to cabin between levels
        LevelToCubicle = 4,       // door/entry/aisle/work inside a cubicle or office
        CubicleToHandoff = 5,     // an edge ending at a handoff spot
    }

    public enum NoRouteReason { None = 0, UnknownNode = 1, Disconnected = 2, EdgeBlocked = 3, RideUnavailable = 4 }

    /// <summary>What to do instead when no route exists. Intentional, never a straight line.</summary>
    public enum RouteFallback { None = 0, HoldAtStart = 1, ReturnHome = 2 }

    public sealed class FloorRoute
    {
        public bool found;
        public NoRouteReason reason;
        public RouteFallback fallback;
        public readonly List<int> nodes = new List<int>();
        /// <summary>Feet positions along the nav nodes; every consecutive pair is a validated nav edge (or a ride).</summary>
        public readonly List<Vector3> polyline = new List<Vector3>();
        public readonly List<FloorEdgeClass> edgeClasses = new List<FloorEdgeClass>();
        public bool hasRide;
        public float length;
        public string detail;
    }

    /// <summary>
    /// Explicit edge classes over a habitat nav graph and a route query that only ever follows validated edges.
    /// Each walk edge is checked at build time with the same swept-capsule test as the layout validator; an edge that
    /// fails (or that the caller blocks, e.g. a faulted elevator) is never used. The result is a polyline of nav
    /// nodes or a "no route" with a fallback. Pure logic.
    /// </summary>
    public sealed class FloorRouteGraph
    {
        readonly HabitatLayout _L;
        readonly FloorEdgeClass[] _class;
        readonly bool[] _valid;
        readonly bool[] _blocked;
        bool _rideBlocked;

        public int InvalidEdgeCount { get; private set; }

        public FloorRouteGraph(HabitatLayout layout)
        {
            _L = layout;
            int n = layout.nav.edges.Count;
            _class = new FloorEdgeClass[n]; _valid = new bool[n]; _blocked = new bool[n];
            for (int i = 0; i < n; i++)
            {
                HabitatNavEdge e = layout.nav.edges[i];
                _class[i] = Classify(layout.nav.nodes[e.a], layout.nav.nodes[e.b], e.ride);
                _valid[i] = e.ride ? RideValid(layout, e) : WalkValid(layout, e);
                if (!_valid[i]) InvalidEdgeCount++;
            }
        }

        public FloorEdgeClass ClassOf(int edge) { return _class[edge]; }

        static FloorEdgeClass Classify(HabitatNavNode a, HabitatNavNode b, bool ride)
        {
            if (ride) return FloorEdgeClass.ElevatorRide;
            if (a.kind == HabitatNodeKind.Cabin || b.kind == HabitatNodeKind.Cabin) return FloorEdgeClass.ElevatorBoarding;
            if (a.kind == HabitatNodeKind.Handoff || b.kind == HabitatNodeKind.Handoff) return FloorEdgeClass.CubicleToHandoff;
            if (IsShaft(a) || IsShaft(b)) return FloorEdgeClass.WalkwayToElevator;
            if (IsCubicle(a) || IsCubicle(b)) return FloorEdgeClass.LevelToCubicle;
            return FloorEdgeClass.Walkway;
        }

        static bool IsShaft(HabitatNavNode n) { return n.kind == HabitatNodeKind.Queue || n.kind == HabitatNodeKind.Landing; }
        static bool IsCubicle(HabitatNavNode n) { return n.kind == HabitatNodeKind.Work || n.kind == HabitatNodeKind.Aisle || n.kind == HabitatNodeKind.Entry; }

        static bool WalkValid(HabitatLayout L, HabitatNavEdge e)
        {
            HabitatNavNode a = L.nav.nodes[e.a], b = L.nav.nodes[e.b];
            if (a.floor != b.floor || Mathf.Abs(a.position.y - b.position.y) > 1e-4f) return false;
            string hit;
            return HabitatLayoutValidator.CapsuleGap(L, a.position, b.position, a.floor, out hit) >= L.clearance - 1e-4f;
        }

        static bool RideValid(HabitatLayout L, HabitatNavEdge e)
        {
            return L.nav.nodes[e.a].kind == HabitatNodeKind.Cabin && L.nav.nodes[e.b].kind == HabitatNodeKind.Cabin
                && L.nav.nodes[e.a].floor != L.nav.nodes[e.b].floor;
        }

        /// <summary>Close or reopen one edge (either direction), e.g. a blocked corridor.</summary>
        public void SetEdgeBlocked(string nodeA, string nodeB, bool blocked)
        {
            int a = _L.nav.IndexOf(nodeA), b = _L.nav.IndexOf(nodeB);
            for (int i = 0; i < _L.nav.edges.Count; i++)
            {
                HabitatNavEdge e = _L.nav.edges[i];
                if ((e.a == a && e.b == b) || (e.a == b && e.b == a)) _blocked[i] = blocked;
            }
        }

        /// <summary>Elevator out of service: every ride edge is closed.</summary>
        public void SetRidesAvailable(bool available) { _rideBlocked = !available; }

        bool Usable(int edge) { return _valid[edge] && !_blocked[edge] && !(_rideBlocked && _class[edge] == FloorEdgeClass.ElevatorRide); }

        /// <summary>Cheapest validated route. When none exists the result says why and gives an intentional fallback:
        /// ReturnHome when the start can still reach <paramref name="homeNode"/>, else HoldAtStart.</summary>
        public FloorRoute Query(string fromId, string toId, string homeNode = null)
        {
            var r = new FloorRoute();
            int from = _L.nav.IndexOf(fromId), to = _L.nav.IndexOf(toId);
            if (from < 0 || to < 0) { return Fail(r, NoRouteReason.UnknownNode, from, homeNode, "unknown node"); }
            if (!Dijkstra(from, to, r.nodes))
            {
                NoRouteReason why = Reachable(from, to, false) ? (RideNeeded(from, to) ? NoRouteReason.RideUnavailable : NoRouteReason.EdgeBlocked) : NoRouteReason.Disconnected;
                return Fail(r, why, from, homeNode, fromId + " -> " + toId);
            }
            r.found = true;
            r.length = 0f;
            for (int i = 0; i < r.nodes.Count; i++)
            {
                r.polyline.Add(_L.nav.nodes[r.nodes[i]].position);
                if (i == 0) continue;
                int e = EdgeBetween(r.nodes[i - 1], r.nodes[i]);
                r.edgeClasses.Add(_class[e]);
                if (_class[e] == FloorEdgeClass.ElevatorRide) r.hasRide = true;
                else r.length += (r.polyline[i] - r.polyline[i - 1]).magnitude;
            }
            return r;
        }

        FloorRoute Fail(FloorRoute r, NoRouteReason why, int from, string homeNode, string detail)
        {
            r.found = false; r.reason = why; r.detail = detail;
            int home = homeNode != null ? _L.nav.IndexOf(homeNode) : -1;
            var tmp = new List<int>();
            r.fallback = (from >= 0 && home >= 0 && Dijkstra(from, home, tmp)) ? RouteFallback.ReturnHome : RouteFallback.HoldAtStart;
            if (r.fallback == RouteFallback.ReturnHome) { r.nodes.AddRange(tmp); for (int i = 0; i < tmp.Count; i++) r.polyline.Add(_L.nav.nodes[tmp[i]].position); }
            return r;
        }

        int EdgeBetween(int a, int b)
        {
            int best = -1; float bc = float.MaxValue;
            for (int i = 0; i < _L.nav.edges.Count; i++)
            {
                HabitatNavEdge e = _L.nav.edges[i];
                if (!((e.a == a && e.b == b) || (e.a == b && e.b == a))) continue;
                if (Usable(i) && e.cost < bc) { bc = e.cost; best = i; }
            }
            return best;
        }

        bool Dijkstra(int from, int to, List<int> route)
        {
            route.Clear();
            int n = _L.nav.nodes.Count;
            var dist = new float[n]; var prev = new int[n]; var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            dist[from] = 0f;
            for (int it = 0; it < n; it++)
            {
                int u = -1; float best = float.MaxValue;
                for (int i = 0; i < n; i++) if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                if (u < 0 || u == to) break;
                done[u] = true;
                for (int k = 0; k < _L.nav.edges.Count; k++)
                {
                    if (!Usable(k)) continue;
                    HabitatNavEdge e = _L.nav.edges[k];
                    int v;
                    if (e.a == u) v = e.b; else if (e.b == u) v = e.a; else continue;
                    float nd = dist[u] + e.cost;
                    if (nd < dist[v]) { dist[v] = nd; prev[v] = u; }
                }
            }
            if (dist[to] == float.MaxValue) return false;
            for (int v = to; v >= 0; v = prev[v]) route.Add(v);
            route.Reverse();
            return true;
        }

        /// <summary>Plain reachability ignoring closures (to tell "blocked" from "not connected").</summary>
        bool Reachable(int from, int to, bool useClosures)
        {
            var seen = new bool[_L.nav.nodes.Count];
            var stack = new Stack<int>(); stack.Push(from); seen[from] = true;
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                if (u == to) return true;
                for (int k = 0; k < _L.nav.edges.Count; k++)
                {
                    if (!_valid[k]) continue;
                    if (useClosures && !Usable(k)) continue;
                    HabitatNavEdge e = _L.nav.edges[k];
                    int v; if (e.a == u) v = e.b; else if (e.b == u) v = e.a; else continue;
                    if (!seen[v]) { seen[v] = true; stack.Push(v); }
                }
            }
            return false;
        }

        bool RideNeeded(int from, int to) { return _L.nav.nodes[from].floor != _L.nav.nodes[to].floor && _rideBlocked; }
    }
}
