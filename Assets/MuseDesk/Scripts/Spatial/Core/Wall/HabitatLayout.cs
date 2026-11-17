using System;
using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Spatial.Wall
{
    public enum HabitatRole { Researcher = 0, Developer = 1, Tester = 2, Manager = 3 }

    public enum HabitatBoxKind { Backplate = 0, Deck = 1, Roof = 2, Desk = 3, Partition = 4, ShaftWall = 5, ShaftFront = 6, Railing = 7, Furniture = 8, EndWall = 9 }

    public enum HabitatNodeKind { Work = 0, Aisle = 1, Entry = 2, Door = 3, Corridor = 4, Queue = 5, Landing = 6, Cabin = 7, Meeting = 8, Dispatch = 9, Handoff = 10 }

    /// <summary>A named pose in habitat-local space (metres; +Z out of the wall; yaw about +Y, 0 = facing +Z).</summary>
    public struct HabitatSocket
    {
        public string id;
        public Vector3 position;
        public float yawDeg;
        public HabitatSocket(string id, Vector3 p, float yaw = 0f) { this.id = id; position = p; yawDeg = yaw; }
        public Quaternion Rotation => Quaternion.Euler(0f, yawDeg, 0f);
    }

    /// <summary>An axis-aligned box in habitat-local space. Static geometry the characters must never enter.</summary>
    public struct HabitatBox
    {
        public string id;
        public HabitatBoxKind kind;
        public Vector3 center;
        public Vector3 half;
        public int floor;
        public bool blocksAgents;
        public Vector3 Min => center - half;
        public Vector3 Max => center + half;
        public static HabitatBox FromMinMax(string id, HabitatBoxKind kind, Vector3 min, Vector3 max, int floor, bool blocks = true)
        {
            return new HabitatBox { id = id, kind = kind, center = (min + max) * 0.5f, half = (max - min) * 0.5f, floor = floor, blocksAgents = blocks };
        }
    }

    public sealed class HabitatFloor
    {
        public int index;
        public string id;                 // "Lower", "Mid", "Manager"
        public float floorY;              // top of the slab: where feet stand
        public float ceilingY;            // underside of the slab or roof above
        public float xMin, xMax;          // deck extent
        public string stopSocket;         // ElevatorStop_<id>
        public float Clearance => ceilingY - floorY;
    }

    public sealed class HabitatNavNode
    {
        public int index;
        public string id;
        public HabitatNodeKind kind;
        public int floor;
        public Vector3 position;          // feet position, habitat-local
        /// <summary>Only one character may stand here or be heading here (corridors, doors, the cabin).</summary>
        public bool reservable;
    }

    public struct HabitatNavEdge
    {
        public int a, b;
        /// <summary>True for a ride between two cabin nodes (vertical travel, needs the elevator).</summary>
        public bool ride;
        public float cost;
    }

    /// <summary>Local walk/ride graph of one habitat. Never spans the room: every node is inside the habitat.</summary>
    public sealed class HabitatNavGraph
    {
        public readonly List<HabitatNavNode> nodes = new List<HabitatNavNode>();
        public readonly List<HabitatNavEdge> edges = new List<HabitatNavEdge>();
        readonly Dictionary<string, int> _byId = new Dictionary<string, int>();
        List<int>[] _adj;

        public int Add(string id, HabitatNodeKind kind, int floor, Vector3 p, bool reservable)
        {
            if (_byId.ContainsKey(id)) throw new ArgumentException("duplicate nav node " + id);
            var n = new HabitatNavNode { index = nodes.Count, id = id, kind = kind, floor = floor, position = p, reservable = reservable };
            nodes.Add(n);
            _byId[id] = n.index;
            _adj = null;
            return n.index;
        }

        public void Link(string a, string b, bool ride = false, float extraCost = 0f)
        {
            int ia = IndexOf(a), ib = IndexOf(b);
            if (ia < 0 || ib < 0) throw new ArgumentException("unknown nav node in link " + a + "-" + b);
            float cost = (nodes[ia].position - nodes[ib].position).magnitude + extraCost;
            edges.Add(new HabitatNavEdge { a = ia, b = ib, ride = ride, cost = cost });
            _adj = null;
        }

        public int IndexOf(string id) { int i; return id != null && _byId.TryGetValue(id, out i) ? i : -1; }
        public HabitatNavNode Node(string id) { int i = IndexOf(id); return i < 0 ? null : nodes[i]; }

        void BuildAdjacency()
        {
            _adj = new List<int>[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) _adj[i] = new List<int>(4);
            for (int e = 0; e < edges.Count; e++) { _adj[edges[e].a].Add(e); _adj[edges[e].b].Add(e); }
        }

        /// <summary>Cheapest route (node indices, start and end included). False when unreachable.</summary>
        public bool FindRoute(int from, int to, List<int> route)
        {
            route.Clear();
            if (from < 0 || to < 0 || from >= nodes.Count || to >= nodes.Count) return false;
            if (_adj == null) BuildAdjacency();
            int n = nodes.Count;
            var dist = new float[n];
            var prev = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            dist[from] = 0f;
            for (int iter = 0; iter < n; iter++)
            {
                int u = -1; float best = float.MaxValue;
                for (int i = 0; i < n; i++) if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                if (u < 0) break;
                if (u == to) break;
                done[u] = true;
                var adj = _adj[u];
                for (int k = 0; k < adj.Count; k++)
                {
                    HabitatNavEdge e = edges[adj[k]];
                    int v = e.a == u ? e.b : e.a;
                    float nd = dist[u] + e.cost;
                    if (nd < dist[v]) { dist[v] = nd; prev[v] = u; }
                }
            }
            if (dist[to] == float.MaxValue) return false;
            for (int v = to; v >= 0; v = prev[v]) route.Add(v);
            route.Reverse();
            return true;
        }

        public bool IsRide(int a, int b)
        {
            for (int e = 0; e < edges.Count; e++)
                if ((edges[e].a == a && edges[e].b == b) || (edges[e].a == b && edges[e].b == a)) return edges[e].ride;
            return false;
        }

        public bool Connected(int a, int b)
        {
            for (int e = 0; e < edges.Count; e++)
                if ((edges[e].a == a && edges[e].b == b) || (edges[e].a == b && edges[e].b == a)) return true;
            return false;
        }
    }

    /// <summary>One habitat layout, pure data. Built by <see cref="HabitatLayoutBuilder"/>; validated by <see cref="HabitatLayoutValidator"/>.</summary>
    public sealed class HabitatLayout
    {
        public WallLayoutKind kind;
        public Vector3 size;
        public float agentRadius;
        public float agentHeight;
        /// <summary>Uniform clearance kept between a character's capsule and any static box (m).</summary>
        public float clearance;
        public readonly List<HabitatFloor> floors = new List<HabitatFloor>();
        public readonly List<HabitatSocket> sockets = new List<HabitatSocket>();
        public readonly List<HabitatBox> boxes = new List<HabitatBox>();
        public readonly HabitatNavGraph nav = new HabitatNavGraph();
        /// <summary>Elevator cabin interior (habitat-local, at the LOWER stop) and travel extents.</summary>
        public Vector3 cabinHalf;
        public float elevatorX, elevatorZ;
        /// <summary>Door opening width (m) of each landing.</summary>
        public float doorWidth;

        public int ManagerFloor => floors.Count - 1;

        public bool TryGetSocket(string id, out HabitatSocket s)
        {
            for (int i = 0; i < sockets.Count; i++) if (sockets[i].id == id) { s = sockets[i]; return true; }
            s = default(HabitatSocket);
            return false;
        }

        public HabitatSocket Socket(string id)
        {
            HabitatSocket s;
            if (!TryGetSocket(id, out s)) throw new KeyNotFoundException("habitat socket " + id);
            return s;
        }

        public HabitatFloor FloorOf(HabitatRole role) { return role == HabitatRole.Manager ? floors[ManagerFloor] : floors[0]; }

        public static string RoleName(HabitatRole r) { return r.ToString(); }
        public static string AgentSocket(HabitatRole r, string pose) { return r + "." + pose; }
        public static string WorkNode(HabitatRole r) { return "Work_" + r; }
        public static string DoorNode(HabitatRole r) { return "Door_" + r; }
        public static string CabinNode(int floor) { return "Cabin_" + floor; }
        public static string LandingNode(int floor) { return "Landing_" + floor; }
        public static string QueueNode(int floor) { return "Queue_" + floor; }
        public const string HandoffAtManager = "Handoff_Manager";
        public const string DispatchNode = "Dispatch";
        public const string MeetingNode = "Meeting";
    }

    /// <summary>
    /// Builds the Full (3 levels: work deck, meeting deck, manager deck) and Compact (2 levels) layouts
    /// from one parameterised code path. Every number a character walks through is derived here so the
    /// validator can prove the capsule never enters a desk, partition, shaft wall, railing or the wall.
    /// </summary>
    public static class HabitatLayoutBuilder
    {
        // Required socket ids (03_INTEGRATION_CONTRACT.json "required_habitat_sockets").
        public static readonly string[] RequiredHabitatSockets =
        {
            "WallContact", "ManagerOfficeSocket", "ResearchCubicleSocket", "DeveloperCubicleSocket", "TesterCubicleSocket",
            "ElevatorCarSocket", "ElevatorStop_Lower", "ElevatorStop_Manager", "TaskPrinterSocket",
            "AttentionBeaconSocket", "PortalDepartureSocket", "CourierDispatchSocket",
        };
        // Required per-agent sockets ("required_agent_sockets").
        public static readonly string[] RequiredAgentSockets =
        {
            "AgentHomePose", "WorkPose", "SeatPose", "EntryPose", "HandoffPose", "ElevatorQueuePose", "DocumentAttach", "HeadLookTarget",
        };

        sealed class P
        {
            public float W, H, D, slab, baseFrame, back, elevW, elevMargin, agentR, agentH, clear;
            public float corridorDepth, railing, deskDepth, deskWFrac, partition;
            public float[] storyClear;   // clearance of each floor
            public float[] deckXMinFromLeft; // deck left edge offsets (x = -W/2 + value)
        }

        static P Params(WallLayoutKind k)
        {
            if (k == WallLayoutKind.Full)
                return new P
                {
                    W = 1.05f, H = 0.90f, D = 0.26f, slab = 0.02f, baseFrame = 0.05f, back = 0.015f,
                    elevW = 0.13f, elevMargin = 0.02f, agentR = 0.032f, agentH = 0.17f, clear = 0.005f,
                    corridorDepth = 0.08f, railing = 0.01f, deskDepth = 0.045f, deskWFrac = 0.5f, partition = 0.012f,
                    storyClear = new[] { 0.27f, 0.24f, 0.24f },
                    deckXMinFromLeft = new[] { 0.02f, 0.475f, 0.225f },
                };
            return new P
            {
                W = 0.74f, H = 0.62f, D = 0.24f, slab = 0.02f, baseFrame = 0.05f, back = 0.015f,
                elevW = 0.11f, elevMargin = 0.02f, agentR = 0.028f, agentH = 0.15f, clear = 0.005f,
                corridorDepth = 0.08f, railing = 0.01f, deskDepth = 0.04f, deskWFrac = 0.42f, partition = 0.012f,
                storyClear = new[] { 0.24f, 0.22f },
                deckXMinFromLeft = new[] { 0.02f, 0.17f },
            };
        }

        public static HabitatLayout Build(WallLayoutKind kind)
        {
            P p = Params(kind);
            var L = new HabitatLayout
            {
                kind = kind,
                size = new Vector3(p.W, p.H, p.D),
                agentRadius = p.agentR,
                agentHeight = p.agentH,
                clearance = p.clear,
            };
            float hw = p.W * 0.5f, hh = p.H * 0.5f;
            int floorCount = p.storyClear.Length;
            string[] floorIds = floorCount == 3 ? new[] { "Lower", "Mid", "Manager" } : new[] { "Lower", "Manager" };

            // Elevator shaft at the +X end.
            float shaftX1 = hw - p.elevMargin, shaftX0 = shaftX1 - p.elevW;
            float elevX = (shaftX0 + shaftX1) * 0.5f;
            float zC0 = p.D - p.railing - p.corridorDepth;       // corridor back edge
            float zC1 = p.D - p.railing;                         // corridor front edge (railing behind it)
            float zc = (zC0 + zC1) * 0.5f;                       // corridor centre line
            float cabinZ = (p.back + zC0) * 0.5f;
            L.elevatorX = elevX; L.elevatorZ = cabinZ;
            L.cabinHalf = new Vector3(p.elevW * 0.5f - 0.01f, 0f, (zC0 - p.back) * 0.5f - 0.01f);
            L.doorWidth = p.elevW - 0.03f;

            // Floors.
            float y = -hh + p.baseFrame;
            for (int f = 0; f < floorCount; f++)
            {
                var fl = new HabitatFloor
                {
                    index = f,
                    id = floorIds[f],
                    floorY = y,
                    ceilingY = y + p.storyClear[f],
                    xMin = -hw + p.deckXMinFromLeft[f],
                    xMax = hw - 0.005f,
                    stopSocket = "ElevatorStop_" + floorIds[f],
                };
                L.floors.Add(fl);
                y = fl.ceilingY + p.slab;
            }
            float roofTop = y;                                    // top of the roof slab
            if (roofTop > hh + 1e-4f) throw new InvalidOperationException("habitat floors exceed the height: " + roofTop + " > " + hh);
            L.cabinHalf.y = (L.floors[0].Clearance - 0.02f) * 0.5f;

            // ── static boxes ──
            L.boxes.Add(HabitatBox.FromMinMax("Backplate", HabitatBoxKind.Backplate, new Vector3(-hw, -hh, 0f), new Vector3(hw, hh, p.back), -1));
            for (int f = 0; f < floorCount; f++)
            {
                HabitatFloor fl = L.floors[f];
                L.boxes.Add(HabitatBox.FromMinMax("Deck_" + fl.id, HabitatBoxKind.Deck, new Vector3(fl.xMin, fl.floorY - p.slab, p.back), new Vector3(shaftX0, fl.floorY, p.D), f));
                // Front railing along the deck (characters stay behind it).
                L.boxes.Add(HabitatBox.FromMinMax("Railing_" + fl.id, HabitatBoxKind.Railing, new Vector3(fl.xMin, fl.floorY, zC1), new Vector3(shaftX0, fl.floorY + 0.045f, p.D), f));
                // Deck end wall at the open (left) end.
                L.boxes.Add(HabitatBox.FromMinMax("EndWall_" + fl.id, HabitatBoxKind.EndWall, new Vector3(fl.xMin, fl.floorY, p.back), new Vector3(fl.xMin + 0.01f, fl.floorY + 0.06f, p.D), f));
            }
            L.boxes.Add(HabitatBox.FromMinMax("Roof", HabitatBoxKind.Roof, new Vector3(-hw + p.deckXMinFromLeft[floorCount - 1], roofTop - p.slab, p.back), new Vector3(hw, roofTop, p.D), floorCount - 1));
            // Shaft: left and right walls, back is the backplate; front wall segments either side of each door.
            float shaftBottom = L.floors[0].floorY - p.slab, shaftTop = roofTop;
            L.boxes.Add(HabitatBox.FromMinMax("ShaftWall_L", HabitatBoxKind.ShaftWall, new Vector3(shaftX0, shaftBottom, p.back), new Vector3(shaftX0 + 0.008f, shaftTop, zC0), -1));
            L.boxes.Add(HabitatBox.FromMinMax("ShaftWall_R", HabitatBoxKind.ShaftWall, new Vector3(shaftX1 - 0.008f, shaftBottom, p.back), new Vector3(shaftX1, shaftTop, zC0), -1));
            float doorX0 = elevX - L.doorWidth * 0.5f, doorX1 = elevX + L.doorWidth * 0.5f;
            L.boxes.Add(HabitatBox.FromMinMax("ShaftFront_L", HabitatBoxKind.ShaftFront, new Vector3(shaftX0, shaftBottom, zC0 - 0.008f), new Vector3(doorX0, shaftTop, zC0), -1));
            L.boxes.Add(HabitatBox.FromMinMax("ShaftFront_R", HabitatBoxKind.ShaftFront, new Vector3(doorX1, shaftBottom, zC0 - 0.008f), new Vector3(shaftX1, shaftTop, zC0), -1));
            // Landing slabs in front of the shaft (the deck stops at the shaft; the landing continues the corridor).
            for (int f = 0; f < floorCount; f++)
            {
                HabitatFloor fl = L.floors[f];
                L.boxes.Add(HabitatBox.FromMinMax("Landing_" + fl.id, HabitatBoxKind.Deck, new Vector3(shaftX0, fl.floorY - p.slab, zC0), new Vector3(shaftX1, fl.floorY, p.D), f));
                L.boxes.Add(HabitatBox.FromMinMax("LandingRail_" + fl.id, HabitatBoxKind.Railing, new Vector3(shaftX0, fl.floorY, zC1), new Vector3(shaftX1, fl.floorY + 0.045f, p.D), f));
                L.boxes.Add(HabitatBox.FromMinMax("LandingEnd_" + fl.id, HabitatBoxKind.EndWall, new Vector3(shaftX1 - 0.008f, fl.floorY, zC0), new Vector3(shaftX1, fl.floorY + 0.06f, p.D), f));
            }

            // ── sockets: habitat level ──
            HabitatFloor lower = L.floors[0], mgr = L.floors[floorCount - 1];
            L.sockets.Add(new HabitatSocket("WallContact", Vector3.zero));
            L.sockets.Add(new HabitatSocket("ElevatorCarSocket", new Vector3(elevX, lower.floorY, cabinZ)));
            for (int f = 0; f < floorCount; f++)
                L.sockets.Add(new HabitatSocket(L.floors[f].stopSocket, new Vector3(elevX, L.floors[f].floorY, zc)));
            L.sockets.Add(new HabitatSocket("AttentionBeaconSocket", new Vector3(-hw + p.deckXMinFromLeft[floorCount - 1] + 0.05f, roofTop, p.D * 0.5f)));
            L.sockets.Add(new HabitatSocket("CourierDispatchSocket", new Vector3(lower.xMin + 0.05f, lower.floorY, zc)));
            L.sockets.Add(new HabitatSocket("PortalDepartureSocket", new Vector3(0f, lower.floorY, p.D + 0.02f)));

            // ── lower deck: three cubicles ──
            float xL0 = lower.xMin + 0.01f, xL1 = shaftX0 - 0.01f;
            float cw = (xL1 - xL0) / 3f;
            HabitatRole[] roles = { HabitatRole.Researcher, HabitatRole.Developer, HabitatRole.Tester };
            string[] cubicleSockets = { "ResearchCubicleSocket", "DeveloperCubicleSocket", "TesterCubicleSocket" };
            var corridorLower = new List<KeyValuePair<float, string>>();
            for (int i = 0; i < 3; i++)
            {
                float cx = xL0 + cw * (i + 0.5f);
                L.sockets.Add(new HabitatSocket(cubicleSockets[i], new Vector3(cx, lower.floorY, (p.back + zC0) * 0.5f)));
                // Partition on the left of each cubicle (the first one is against the end wall) and after the last.
                float px = cx - cw * 0.5f;
                L.boxes.Add(HabitatBox.FromMinMax("Partition_" + i, HabitatBoxKind.Partition,
                    new Vector3(px - p.partition * 0.5f, lower.floorY, p.back), new Vector3(px + p.partition * 0.5f, lower.floorY + 0.11f, zC0 - 0.005f), 0));
                if (i == 2)
                    L.boxes.Add(HabitatBox.FromMinMax("Partition_3", HabitatBoxKind.Partition,
                        new Vector3(cx + cw * 0.5f - p.partition * 0.5f, lower.floorY, p.back), new Vector3(cx + cw * 0.5f + p.partition * 0.5f, lower.floorY + 0.11f, zC0 - 0.005f), 0));
                AddWorkstation(L, p, roles[i], 0, cx, cw, zC0, zc, corridorLower);
            }
            // Lower corridor: dispatch at the open end, the three doors, queue and landing at the shaft.
            L.nav.Add(HabitatLayout.DispatchNode, HabitatNodeKind.Dispatch, 0, new Vector3(lower.xMin + 0.05f, lower.floorY, zc), true);
            corridorLower.Add(new KeyValuePair<float, string>(lower.xMin + 0.05f, HabitatLayout.DispatchNode));
            AddElevatorStop(L, p, 0, elevX, shaftX0, zc, cabinZ, corridorLower);
            LinkCorridor(L, corridorLower);

            // ── mid deck (Full): meeting nook ──
            if (floorCount == 3)
            {
                HabitatFloor mid = L.floors[1];
                var corridorMid = new List<KeyValuePair<float, string>>();
                float mx = (mid.xMin + shaftX0) * 0.5f;
                L.sockets.Add(new HabitatSocket("SharedMeetingSocket", new Vector3(mx, mid.floorY, (p.back + zC0) * 0.5f)));
                L.nav.Add(HabitatLayout.MeetingNode, HabitatNodeKind.Meeting, 1, new Vector3(mx, mid.floorY, zc), true);
                corridorMid.Add(new KeyValuePair<float, string>(mx, HabitatLayout.MeetingNode));
                L.boxes.Add(HabitatBox.FromMinMax("MeetingTable", HabitatBoxKind.Furniture,
                    new Vector3(mx - 0.06f, mid.floorY, p.back + 0.02f), new Vector3(mx + 0.06f, mid.floorY + 0.06f, zC0 - 0.01f), 1));
                AddElevatorStop(L, p, 1, elevX, shaftX0, zc, cabinZ, corridorMid);
                LinkCorridor(L, corridorMid);
            }

            // ── manager deck ──
            {
                int mf = floorCount - 1;
                var corridorMgr = new List<KeyValuePair<float, string>>();
                float officeX0 = mgr.xMin + 0.01f, officeX1 = shaftX0 - 0.01f;
                float printerW = 0.10f;
                float mcw = Mathf.Min(0.30f, officeX1 - officeX0 - printerW - 0.02f);
                float mcx = officeX1 - mcw * 0.5f;
                L.sockets.Add(new HabitatSocket("ManagerOfficeSocket", new Vector3(mcx, mgr.floorY, (p.back + zC0) * 0.5f)));
                L.sockets.Add(new HabitatSocket("TaskPrinterSocket", new Vector3(officeX0 + printerW * 0.5f + 0.01f, mgr.floorY + 0.05f, (p.back + zC0) * 0.5f)));
                L.boxes.Add(HabitatBox.FromMinMax("PrinterTable", HabitatBoxKind.Furniture,
                    new Vector3(officeX0 + 0.01f, mgr.floorY, p.back + 0.01f), new Vector3(officeX0 + 0.01f + printerW, mgr.floorY + 0.05f, zC0 - 0.01f), mf));
                L.boxes.Add(HabitatBox.FromMinMax("ManagerPartition", HabitatBoxKind.Partition,
                    new Vector3(mcx - mcw * 0.5f - p.partition * 0.5f, mgr.floorY, p.back), new Vector3(mcx - mcw * 0.5f + p.partition * 0.5f, mgr.floorY + 0.11f, zC0 - 0.005f), mf));
                AddWorkstation(L, p, HabitatRole.Manager, mf, mcx, mcw, zC0, zc, corridorMgr);
                // Where a visiting worker stands to hand something to the Manager: the corridor in front of the desk.
                L.nav.Add(HabitatLayout.HandoffAtManager, HabitatNodeKind.Handoff, mf, new Vector3(mcx - mcw * 0.25f, mgr.floorY, zc), true);
                corridorMgr.Add(new KeyValuePair<float, string>(mcx - mcw * 0.25f, HabitatLayout.HandoffAtManager));
                AddElevatorStop(L, p, mf, elevX, shaftX0, zc, cabinZ, corridorMgr);
                LinkCorridor(L, corridorMgr);
            }

            // Ride edges between every pair of cabin nodes.
            for (int a = 0; a < floorCount; a++)
                for (int b = a + 1; b < floorCount; b++)
                    L.nav.Link(HabitatLayout.CabinNode(a), HabitatLayout.CabinNode(b), true, 0.6f);
            return L;
        }

        /// <summary>Desk, work/aisle/entry/door nodes and the role's agent sockets for one cubicle or office.</summary>
        static void AddWorkstation(HabitatLayout L, P p, HabitatRole role, int floor, float cx, float cw, float zC0, float zc,
                                   List<KeyValuePair<float, string>> corridor)
        {
            HabitatFloor fl = L.floors[floor];
            float y0 = fl.floorY;
            float innerL = cx - cw * 0.5f + p.partition * 0.5f;
            float innerR = cx + cw * 0.5f - p.partition * 0.5f;
            float deskW = cw * p.deskWFrac;
            float deskX0 = cx - cw * 0.5f + 0.02f, deskX1 = deskX0 + deskW;
            float zD1 = zC0 - 0.005f, zD0 = zD1 - p.deskDepth;
            L.boxes.Add(HabitatBox.FromMinMax("Desk_" + role, HabitatBoxKind.Desk, new Vector3(deskX0, y0, zD0), new Vector3(deskX1, y0 + 0.065f, zD1), floor));

            float workX = (deskX0 + deskX1) * 0.5f;
            float workZ = (p.back + p.agentR + p.clear + zD0 - p.agentR - p.clear) * 0.5f;
            float gapX = (deskX1 + innerR) * 0.5f;
            float entryZ = (zD0 + zD1) * 0.5f;

            string work = HabitatLayout.WorkNode(role), aisle = "Aisle_" + role, entry = "Entry_" + role, door = HabitatLayout.DoorNode(role);
            L.nav.Add(work, HabitatNodeKind.Work, floor, new Vector3(workX, y0, workZ), false);
            L.nav.Add(aisle, HabitatNodeKind.Aisle, floor, new Vector3(gapX, y0, workZ), true);
            L.nav.Add(entry, HabitatNodeKind.Entry, floor, new Vector3(gapX, y0, entryZ), true);
            L.nav.Add(door, HabitatNodeKind.Door, floor, new Vector3(gapX, y0, zc), true);
            L.nav.Link(work, aisle); L.nav.Link(aisle, entry); L.nav.Link(entry, door);
            corridor.Add(new KeyValuePair<float, string>(gapX, door));

            float h = p.agentH;
            string r = role.ToString();
            var workPos = new Vector3(workX, y0, workZ);
            L.sockets.Add(new HabitatSocket(r + ".AgentHomePose", workPos));
            L.sockets.Add(new HabitatSocket(r + ".WorkPose", workPos));
            L.sockets.Add(new HabitatSocket(r + ".SeatPose", workPos));
            L.sockets.Add(new HabitatSocket(r + ".EntryPose", new Vector3(gapX, y0, entryZ)));
            L.sockets.Add(new HabitatSocket(r + ".HandoffPose", new Vector3(gapX, y0, zc)));
            L.sockets.Add(new HabitatSocket(r + ".ElevatorQueuePose", Vector3.zero));   // set by AddElevatorStop for this floor
            L.sockets.Add(new HabitatSocket(r + ".DocumentAttach", workPos + new Vector3(0f, h * 0.55f, 0.03f)));
            L.sockets.Add(new HabitatSocket(r + ".HeadLookTarget", workPos + new Vector3(0f, h * 0.9f, 0f)));
            // Elevator queue pose is the queue node of this role's floor (known once the stop is added): patch later.
        }

        /// <summary>Queue, landing and cabin nodes of one stop; the queue/landing join the corridor.</summary>
        static void AddElevatorStop(HabitatLayout L, P p, int floor, float elevX, float shaftX0, float zc, float cabinZ,
                                    List<KeyValuePair<float, string>> corridor)
        {
            HabitatFloor fl = L.floors[floor];
            string q = HabitatLayout.QueueNode(floor), land = HabitatLayout.LandingNode(floor), cab = HabitatLayout.CabinNode(floor);
            float qx = shaftX0 - p.agentR - 0.02f;
            L.nav.Add(q, HabitatNodeKind.Queue, floor, new Vector3(qx, fl.floorY, zc), true);
            L.nav.Add(land, HabitatNodeKind.Landing, floor, new Vector3(elevX, fl.floorY, zc), true);
            L.nav.Add(cab, HabitatNodeKind.Cabin, floor, new Vector3(elevX, fl.floorY, cabinZ), true);
            L.nav.Link(land, cab, false, 0.05f);
            corridor.Add(new KeyValuePair<float, string>(qx, q));
            corridor.Add(new KeyValuePair<float, string>(elevX, land));

            // Patch every role on this floor: its ElevatorQueuePose is this queue node.
            for (int i = 0; i < L.sockets.Count; i++)
            {
                HabitatSocket s = L.sockets[i];
                if (!s.id.EndsWith(".ElevatorQueuePose")) continue;
                string roleName = s.id.Substring(0, s.id.IndexOf('.'));
                HabitatRole role = (HabitatRole)Enum.Parse(typeof(HabitatRole), roleName);
                int roleFloor = role == HabitatRole.Manager ? L.floors.Count - 1 : 0;
                if (roleFloor != floor) continue;
                s.position = new Vector3(qx, fl.floorY, zc);
                L.sockets[i] = s;
            }
        }

        static void LinkCorridor(HabitatLayout L, List<KeyValuePair<float, string>> corridor)
        {
            corridor.Sort((a, b) => a.Key.CompareTo(b.Key));
            for (int i = 1; i < corridor.Count; i++) L.nav.Link(corridor[i - 1].Value, corridor[i].Value);
        }
    }

    /// <summary>One problem found by the validator.</summary>
    public struct HabitatIssue
    {
        public string what;
        public override string ToString() { return what; }
    }

    /// <summary>
    /// Proves a layout is safe to animate: unique sockets, required sockets present, everything inside the
    /// habitat volume, every nav node's standing capsule and every walk edge's swept capsule clear of all
    /// blocking boxes on that floor (by at least the layout clearance), head room on every floor, the
    /// elevator cabin and door wide enough for a rider, and every work node reachable from every other.
    /// </summary>
    public static class HabitatLayoutValidator
    {
        public static List<HabitatIssue> Validate(HabitatLayout L)
        {
            var issues = new List<HabitatIssue>();
            var seen = new HashSet<string>();
            for (int i = 0; i < L.sockets.Count; i++)
            {
                if (!seen.Add(L.sockets[i].id)) Add(issues, "duplicate socket " + L.sockets[i].id);
                if (!Inside(L, L.sockets[i].position, 0.03f)) Add(issues, "socket outside habitat " + L.sockets[i].id + " " + L.sockets[i].position.ToString("F3"));
            }
            foreach (string id in HabitatLayoutBuilder.RequiredHabitatSockets)
                if (!seen.Contains(id)) Add(issues, "missing habitat socket " + id);
            for (int r = 0; r < 4; r++)
                foreach (string pose in HabitatLayoutBuilder.RequiredAgentSockets)
                    if (!seen.Contains(((HabitatRole)r) + "." + pose)) Add(issues, "missing agent socket " + ((HabitatRole)r) + "." + pose);

            for (int f = 0; f < L.floors.Count; f++)
                if (L.floors[f].Clearance < L.agentHeight + 0.03f) Add(issues, "floor " + L.floors[f].id + " head room " + L.floors[f].Clearance.ToString("F3"));

            float need = L.agentRadius + L.clearance;
            if (L.cabinHalf.x < need) Add(issues, "cabin too narrow " + (L.cabinHalf.x * 2f).ToString("F3"));
            if (L.cabinHalf.z < need) Add(issues, "cabin too shallow " + (L.cabinHalf.z * 2f).ToString("F3"));
            if (L.doorWidth * 0.5f < need) Add(issues, "elevator door too narrow " + L.doorWidth.ToString("F3"));

            // Standing capsules.
            foreach (HabitatNavNode n in L.nav.nodes)
            {
                if (!Inside(L, n.position, 0.0f)) Add(issues, "nav node outside habitat " + n.id);
                string hit;
                float gap = CapsuleGap(L, n.position, n.position, n.floor, out hit);
                if (gap < L.clearance - 1e-4f) Add(issues, "node " + n.id + " capsule within " + gap.ToString("F4") + " m of " + hit);
            }
            // Walk edges (rides are vertical inside the shaft: checked against the cabin size above).
            foreach (HabitatNavEdge e in L.nav.edges)
            {
                if (e.ride) continue;
                HabitatNavNode a = L.nav.nodes[e.a], b = L.nav.nodes[e.b];
                if (a.floor != b.floor) { Add(issues, "walk edge between floors " + a.id + "-" + b.id); continue; }
                if (Mathf.Abs(a.position.y - b.position.y) > 1e-4f) Add(issues, "walk edge not level " + a.id + "-" + b.id);
                string hit;
                float gap = CapsuleGap(L, a.position, b.position, a.floor, out hit);
                if (gap < L.clearance - 1e-4f) Add(issues, "edge " + a.id + "-" + b.id + " sweeps within " + gap.ToString("F4") + " m of " + hit);
            }
            // Connectivity: every work node reaches every other (through the elevator where needed).
            var route = new List<int>();
            for (int r = 0; r < 4; r++)
                for (int s = 0; s < 4; s++)
                {
                    if (r == s) continue;
                    int a = L.nav.IndexOf(HabitatLayout.WorkNode((HabitatRole)r)), b = L.nav.IndexOf(HabitatLayout.WorkNode((HabitatRole)s));
                    if (!L.nav.FindRoute(a, b, route)) Add(issues, "no route " + (HabitatRole)r + " -> " + (HabitatRole)s);
                }
            return issues;
        }

        static void Add(List<HabitatIssue> l, string what) { l.Add(new HabitatIssue { what = what }); }

        static bool Inside(HabitatLayout L, Vector3 p, float tol)
        {
            Vector3 h = L.size * 0.5f;
            return p.x >= -h.x - tol && p.x <= h.x + tol && p.y >= -h.y - tol && p.y <= h.y + tol && p.z >= -tol && p.z <= L.size.z + tol;
        }

        /// <summary>
        /// Smallest gap between a character capsule standing on the floor and moving straight from a to b
        /// (feet positions) and any blocking box of that floor or of every floor (-1). Exact for axis-aligned
        /// boxes: horizontal segment-to-rectangle distance combined with the vertical gap, minus the radius.
        /// The deck the character stands on is not an obstacle; the capsule's lowest point is 1 mm above it.
        /// </summary>
        public static float CapsuleGap(HabitatLayout L, Vector3 a, Vector3 b, int floor, out string closest)
        {
            closest = "";
            float r = L.agentRadius;
            float yLo = a.y + 0.001f, yHi = a.y + L.agentHeight;
            float best = float.MaxValue;
            for (int i = 0; i < L.boxes.Count; i++)
            {
                HabitatBox box = L.boxes[i];
                if (!box.blocksAgents) continue;
                if (box.floor >= 0 && box.floor != floor && box.kind != HabitatBoxKind.Deck && box.kind != HabitatBoxKind.Roof) continue;
                Vector3 mn = box.Min, mx = box.Max;
                // The deck/landing the character stands on: its top is exactly at the feet.
                if ((box.kind == HabitatBoxKind.Deck) && Mathf.Abs(mx.y - a.y) < 1e-4f) continue;
                float dy = Mathf.Max(0f, Mathf.Max(mn.y - yHi, yLo - mx.y));
                float dh = SegmentRectDistance(new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(mn.x, mn.z), new Vector2(mx.x, mx.z));
                // The capsule body is a vertical segment swept horizontally; radius applies all round.
                float gap = Mathf.Sqrt(dh * dh + dy * dy) - r;
                if (gap < best) { best = gap; closest = box.id; }
            }
            return best;
        }

        /// <summary>Distance between a 2D segment and an axis-aligned rectangle (0 when they touch).</summary>
        public static float SegmentRectDistance(Vector2 a, Vector2 b, Vector2 mn, Vector2 mx)
        {
            if (PointInRect(a, mn, mx) || PointInRect(b, mn, mx)) return 0f;
            Vector2 c0 = mn, c1 = new Vector2(mx.x, mn.y), c2 = mx, c3 = new Vector2(mn.x, mx.y);
            if (WallMath.SegmentsIntersect(a, b, c0, c1) || WallMath.SegmentsIntersect(a, b, c1, c2)
                || WallMath.SegmentsIntersect(a, b, c2, c3) || WallMath.SegmentsIntersect(a, b, c3, c0)) return 0f;
            float d = Mathf.Min(PointRect(a, mn, mx), PointRect(b, mn, mx));
            d = Mathf.Min(d, SpatialGeometry.DistanceToSegment(c0, a, b));
            d = Mathf.Min(d, SpatialGeometry.DistanceToSegment(c1, a, b));
            d = Mathf.Min(d, SpatialGeometry.DistanceToSegment(c2, a, b));
            d = Mathf.Min(d, SpatialGeometry.DistanceToSegment(c3, a, b));
            return d;
        }

        static bool PointInRect(Vector2 p, Vector2 mn, Vector2 mx) { return p.x >= mn.x && p.x <= mx.x && p.y >= mn.y && p.y <= mx.y; }

        static float PointRect(Vector2 p, Vector2 mn, Vector2 mx)
        {
            float dx = Mathf.Max(0f, Mathf.Max(mn.x - p.x, p.x - mx.x));
            float dy = Mathf.Max(0f, Mathf.Max(mn.y - p.y, p.y - mx.y));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
    }
}
