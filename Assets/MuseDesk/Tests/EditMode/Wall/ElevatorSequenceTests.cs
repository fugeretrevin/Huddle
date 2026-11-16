// Sprint A7: the 13-step ride sequence, occupancy/queuing, and the floor route graph.
// Pure logic. Not proven here: art meshes match the boxes, animation, the Quest.

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class ElevatorSequenceTests
    {
        const float Dt = 1f / 72f;

        // ── existing walker + machine: which steps are already enforced ──

        static float[] FloorYs(HabitatLayout L) { var y = new float[L.floors.Count]; for (int i = 0; i < y.Length; i++) y[i] = L.floors[i].floorY; return y; }

        static bool InsideCabinVolume(HabitatLayout L, Vector3 p, float cabinY)
        {
            // The cabin interior in plan; y band follows the cabin.
            return Mathf.Abs(p.x - L.elevatorX) < L.cabinHalf.x - 1e-4f && Mathf.Abs(p.z - L.elevatorZ) < L.cabinHalf.z - 1e-4f
                && p.y > cabinY - 0.01f && p.y < cabinY + L.agentHeight;
        }

        [Test]
        public void Walker_NeverInsideCabin_WithDoorsNotFullyOpen_UnlessBoarded_AndRidesCabinRelative()
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(WallLayoutKind.Full);
            var res = new HabitatReservations(L);
            var elev = new HabitatElevatorMachine(FloorYs(L));
            var w = new HabitatWalker("a", HabitatRole.Researcher, L, res, elev);
            Assert.IsTrue(w.GoTo(HabitatLayout.HandoffAtManager, 0.5f, false));
            bool rode = false; int checkedRiding = 0;
            Vector3 cabinXZ = L.nav.Node(HabitatLayout.CabinNode(0)).position;
            float minArrivalErr = float.MaxValue;
            for (int i = 0; i < 72 * 90 && !(w.State == WalkerState.Dwelling || w.State == WalkerState.AtRest); i++)
            {
                elev.Tick(Dt); w.Tick(Dt);
                bool inside = InsideCabinVolume(L, w.Position, elev.CabinY);
                if (inside && !elev.IsRiding("a")) Assert.AreEqual(1f, elev.DoorOpen01, 1e-4f, "stepped into the cabin with the doors not fully open at tick " + i);
                if (w.State == WalkerState.Riding)
                {
                    rode = true; checkedRiding++;
                    Assert.AreEqual(cabinXZ.x, w.Position.x, 1e-4f, "x is the cabin's while riding");
                    Assert.AreEqual(cabinXZ.z, w.Position.z, 1e-4f, "z is the cabin's while riding");
                    Assert.AreEqual(elev.CabinY, w.Position.y, 1e-4f, "y follows the cabin: no independent world motion");
                    Assert.AreEqual(0f, w.Speed, 1e-6f);
                }
                if (w.State == WalkerState.Walking && inside && !elev.IsRiding("a"))
                    minArrivalErr = Mathf.Min(minArrivalErr, Mathf.Abs(Mathf.DeltaAngle(w.Yaw, 180f)));
            }
            Assert.IsTrue(rode);
            Assert.Greater(checkedRiding, 10);
            Assert.AreEqual(WalkerState.Dwelling, w.State, "reached the destination");
            if (minArrivalErr != float.MaxValue) Assert.Less(minArrivalErr, 50f, "faced the door (within the walk turn limit) when stepping in");
        }

        [Test]
        public void Walker_DoesNotMoveFromTheDoorWhileDoorsClosedOrCabinMoving()
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(WallLayoutKind.Full);
            var res = new HabitatReservations(L);
            var elev = new HabitatElevatorMachine(FloorYs(L));
            var w = new HabitatWalker("a", HabitatRole.Developer, L, res, elev);
            w.GoTo(HabitatLayout.HandoffAtManager, 0.2f, false);
            for (int i = 0; i < 72 * 90 && w.State != WalkerState.Dwelling; i++)
            {
                elev.Tick(Dt); w.Tick(Dt);
                if (elev.State == ElevatorState.Traveling || elev.State == ElevatorState.DoorsClosing && elev.DoorOpen01 < 0.99f && w.State == WalkerState.Riding)
                    Assert.AreEqual(0f, w.Speed, 1e-6f, "no walking while the cabin travels or the doors close");
            }
            Assert.AreEqual(WalkerState.Dwelling, w.State);
        }

        // ── dispatcher: scripted riders ──

        sealed class Script
        {
            public ElevatorDispatcher d;
            public readonly Dictionary<string, RidePhase> last = new Dictionary<string, RidePhase>();
            public readonly Dictionary<string, float> timer = new Dictionary<string, float>();
            public readonly HashSet<string> frozenAt = new HashSet<string>();   // agents that never act (stuck)
            public readonly List<RideResult> results = new List<RideResult>();
            public readonly List<string> released = new List<string>();
            public int maxAboard;

            public Script(ElevatorDispatcher d) { this.d = d; d.RideCompleted += r => results.Add(r); d.NavigationReleased += a => released.Add(a); }

            public void Step(IEnumerable<string> agents)
            {
                d.Tick(Dt);
                foreach (string a in agents)
                {
                    RidePhase p;
                    if (!d.TryGetPhase(a, out p)) { last.Remove(a); continue; }
                    RidePhase prev;
                    if (!last.TryGetValue(a, out prev) || prev != p) { last[a] = p; timer[a] = 0f; }
                    timer[a] += Dt;
                    if (frozenAt.Contains(a)) continue;
                    float t = timer[a];
                    if (p == RidePhase.ToWaitSpot && t > 1.0f) d.ReportAtWaitSpot(a);
                    else if (p == RidePhase.Waiting && t > 0.3f) d.ReportFacingDoor(a);
                    else if (p == RidePhase.Boarding && t > 0.8f) d.ReportEnteredCabin(a);
                    else if (p == RidePhase.Exiting && t > 0.8f) d.ReportExitedCabin(a);
                    else if (p == RidePhase.ToDestination && t > 1.5f) d.ReportReachedDestination(a);
                }
                maxAboard = Mathf.Max(maxAboard, d.AboardCount);
                Invariants();
            }

            void Invariants()
            {
                if (d.Cabin == CabinPhase.Moving) Assert.AreEqual(0f, d.DoorOpen01, 1e-6f, "the cabin never moves with the doors open");
                Assert.LessOrEqual(d.AboardCount, d.Capacity, "capacity");
                Assert.AreEqual(0, d.Violations, "an entry/exit was reported out of order or with the doors not fully open");
            }

            public void Run(IEnumerable<string> agents, float seconds) { for (float t = 0; t < seconds; t += Dt) Step(agents); }
        }

        static HabitatLayout Layout() { return HabitatLayoutBuilder.Build(WallLayoutKind.Full); }

        static ElevatorDispatcher Disp(HabitatLayout L, int capacity = 1, int startFloor = 0)
        {
            var d = new ElevatorDispatcher(FloorYs(L), ElevatorWaitSpots.ForLayout(L), startFloor);
            d.settings.capacity = capacity;
            return d;
        }

        [Test]
        public void ThirteenSteps_AreExplicitPhases_InOrder_AndEndInOneArrivedEvent()
        {
            HabitatLayout L = Layout();
            var d = Disp(L);
            var s = new Script(d);
            var seenPhases = new List<RidePhase>();
            var seenCabin = new List<CabinPhase>();
            var agents = new[] { "r" };
            d.Request("r", 0, 2);
            for (int i = 0; i < 72 * 40 && s.results.Count == 0; i++)
            {
                s.Step(agents);
                RidePhase p; if (d.TryGetPhase("r", out p) && (seenPhases.Count == 0 || seenPhases[seenPhases.Count - 1] != p)) seenPhases.Add(p);
                if (seenCabin.Count == 0 || seenCabin[seenCabin.Count - 1] != d.Cabin) seenCabin.Add(d.Cabin);
            }
            CollectionAssert.AreEqual(new[] { RidePhase.ToWaitSpot, RidePhase.Waiting, RidePhase.Facing, RidePhase.Boarding, RidePhase.Riding,
                                              RidePhase.Exiting, RidePhase.ToDestination }, seenPhases);
            // Doors open, close, travel, settle, open again.
            int firstOpen = seenCabin.IndexOf(CabinPhase.Open), closing = seenCabin.IndexOf(CabinPhase.DoorsClosing), moving = seenCabin.IndexOf(CabinPhase.Moving), settling = seenCabin.LastIndexOf(CabinPhase.Settling), lastOpen = seenCabin.LastIndexOf(CabinPhase.Open);
            Assert.Less(firstOpen, closing); Assert.Less(closing, moving); Assert.Less(moving, settling); Assert.Less(settling, lastOpen);
            Assert.AreEqual(1, s.results.Count);
            Assert.AreEqual(RideOutcome.Arrived, s.results[0].outcome);
            CollectionAssert.AreEqual(new[] { "r" }, s.released, "navigation ownership released once");
            Assert.AreEqual(0, d.OpenRequests);
            Assert.AreEqual(FloorYs(L)[2], d.CabinY, 1e-4f);
        }

        [Test]
        public void EnteringWithDoorsNotFullyOpen_IsRefused_AndCounted()
        {
            var d = Disp(Layout());
            d.Request("r", 0, 2);
            d.Tick(Dt);
            Assert.IsFalse(d.ReportEnteredCabin("r"), "not even authorised yet");
            Assert.AreEqual(1, d.Violations);
            Assert.IsFalse(d.MayEnterCabin("r"));
        }

        [Test]
        public void WaitSpots_AreValidated_NotInTheDoorway_AndNeverShared()
        {
            HabitatLayout L = Layout();
            var spots = ElevatorWaitSpots.ForLayout(L);
            Assert.GreaterOrEqual(spots.Count, L.floors.Count * 2);
            for (int i = 0; i < spots.Count; i++)
            {
                HabitatNavNode land = L.nav.Node(HabitatLayout.LandingNode(spots[i].floor));
                Assert.GreaterOrEqual(Mathf.Abs(spots[i].position.x - land.position.x), L.doorWidth * 0.5f + L.agentRadius, "doorway kept clear");
                string hit;
                Assert.GreaterOrEqual(HabitatLayoutValidator.CapsuleGap(L, spots[i].position, spots[i].position, spots[i].floor, out hit), L.clearance - 1e-4f, hit);
                for (int j = i + 1; j < spots.Count; j++)
                    if (spots[i].floor == spots[j].floor) Assert.GreaterOrEqual((spots[i].position - spots[j].position).magnitude, 2f * L.agentRadius, "spots do not overlap");
            }
            var d = Disp(L);
            string[] names = { "a", "b", "c" };
            var used = new HashSet<int>();
            foreach (string n in names) d.Request(n, 0, 2);
            foreach (string n in names)
            {
                WaitSpot sp;
                if (d.TryGetWaitSpot(n, out sp)) Assert.IsTrue(used.Add(Mathf.RoundToInt(sp.position.x * 100000)), "two agents on one spot");
            }
        }

        [Test]
        public void Capacity_IsRespected_AndFifoOrderHolds()
        {
            HabitatLayout L = Layout();
            var d = Disp(L, 2);
            var s = new Script(d);
            string[] agents = { "a", "b", "c" };
            d.Request("a", 0, 2); d.Request("b", 0, 1); d.Request("c", 0, 2);
            s.Run(agents, 120f);
            Assert.AreEqual(3, s.results.Count);
            foreach (RideResult r in s.results) Assert.AreEqual(RideOutcome.Arrived, r.outcome, r.ToString());
            Assert.LessOrEqual(s.maxAboard, 2);
            Assert.AreEqual(2, s.maxAboard, "two rode together");
            Assert.AreEqual(3, s.released.Count);
            // FIFO: a finishes no later than c; c never boarded ahead of a.
            Assert.Less(s.results.FindIndex(r => r.agent == "a"), s.results.FindIndex(r => r.agent == "c"));
        }

        [Test]
        public void CallsFromMultipleFloors_AreAllServed()
        {
            HabitatLayout L = Layout();
            var d = Disp(L);
            var s = new Script(d);
            string[] agents = { "up", "down", "mid" };
            d.Request("up", 0, 2); d.Request("down", 2, 0); d.Request("mid", 1, 2);
            s.Run(agents, 240f);
            Assert.AreEqual(3, s.results.Count);
            foreach (RideResult r in s.results) Assert.AreEqual(RideOutcome.Arrived, r.outcome, r.ToString());
            Assert.AreEqual(0, d.OpenRequests);
        }

        [Test]
        public void ExitsFirst_NobodyBoardsWhileSomeoneLeaves()
        {
            HabitatLayout L = Layout();
            var d = Disp(L);
            var s = new Script(d);
            string[] agents = { "a", "b" };
            d.Request("a", 0, 2); d.Request("b", 2, 0);
            for (int i = 0; i < 72 * 120 && s.results.Count < 2; i++)
            {
                s.Step(agents);
                RidePhase pa, pb;
                bool ha = d.TryGetPhase("a", out pa), hb = d.TryGetPhase("b", out pb);
                if (ha && hb)
                {
                    Assert.IsFalse(pa == RidePhase.Exiting && (pb == RidePhase.Boarding || pb == RidePhase.Riding), "cabin overlap at capacity 1");
                    Assert.IsFalse(pb == RidePhase.Exiting && (pa == RidePhase.Boarding || pa == RidePhase.Riding), "cabin overlap at capacity 1");
                }
            }
            Assert.AreEqual(2, s.results.Count);
        }

        [Test]
        public void Cancel_RemovesAQueuedRequest_ExactlyOnce_AndFreesTheSpot()
        {
            HabitatLayout L = Layout();
            var d = Disp(L);
            var s = new Script(d);
            d.Request("a", 0, 2); d.Request("b", 0, 1);
            Assert.IsTrue(d.Cancel("b"));
            Assert.IsFalse(d.Cancel("b"), "already gone");
            Assert.AreEqual(1, s.results.FindAll(r => r.agent == "b").Count);
            Assert.AreEqual(RideOutcome.Cancelled, s.results[0].outcome);
            Assert.AreEqual(1, s.released.Count);
            s.Run(new[] { "a" }, 60f);
            Assert.AreEqual(1, s.results.FindAll(r => r.agent == "b").Count, "still exactly once");
            Assert.AreEqual(RideOutcome.Arrived, s.results.Find(r => r.agent == "a").outcome);
        }

        [Test]
        public void Cancel_IsRefusedOnceInsideTheCabin()
        {
            var d = Disp(Layout());
            var s = new Script(d);
            d.Request("a", 0, 2);
            for (int i = 0; i < 72 * 30; i++) { s.Step(new[] { "a" }); RidePhase p; if (d.TryGetPhase("a", out p) && p == RidePhase.Riding) break; }
            Assert.IsFalse(d.Cancel("a"));
            s.Run(new[] { "a" }, 60f);
            Assert.AreEqual(RideOutcome.Arrived, s.results[0].outcome);
        }

        [Test]
        public void NoShow_TimesOut_FailsOnce_WithWalkwayFallback_AndCabinRecovers()
        {
            var d = Disp(Layout());
            var s = new Script(d);
            s.frozenAt.Add("ghost");
            d.Request("ghost", 0, 2);
            s.Run(new[] { "ghost" }, d.settings.queueTimeout + 5f);
            Assert.AreEqual(1, s.results.Count);
            Assert.AreEqual(RideOutcome.Failed, s.results[0].outcome);
            Assert.AreEqual(RideFallback.ReturnHomeViaWalkway, s.results[0].fallback);
            // The cabin serves the next rider.
            d.Request("next", 0, 1);
            s.Run(new[] { "ghost", "next" }, 60f);
            Assert.AreEqual(RideOutcome.Arrived, s.results.Find(r => r.agent == "next").outcome);
        }

        [Test]
        public void RiderThatNeverEnters_TimesOutAtTheDoor_NotThroughIt()
        {
            var d = Disp(Layout());
            var s = new Script(d);
            d.Request("a", 0, 2);
            // Acts until facing, then freezes at the door.
            for (int i = 0; i < 72 * 60 && s.results.Count == 0; i++)
            {
                RidePhase p; if (d.TryGetPhase("a", out p) && p == RidePhase.Boarding) s.frozenAt.Add("a");
                s.Step(new[] { "a" });
            }
            Assert.AreEqual(1, s.results.Count);
            Assert.AreEqual(RideFailure.BoardTimeout, s.results[0].failure);
            Assert.AreEqual(RideFallback.ReturnHomeViaWalkway, s.results[0].fallback);
            s.Run(new[] { "a" }, 5f);
            Assert.AreEqual(CabinPhase.Idle, d.Cabin);
            Assert.AreEqual(0f, d.DoorOpen01, 1e-4f, "doors closed again");
        }

        [Test]
        public void StuckDoors_TakeTheCabinOutOfService_WaitersWalkHome_RiderHolds_ThenIsReleased()
        {
            var d = Disp(Layout(), 2);
            var s = new Script(d);
            var released = new List<string>();
            d.StrandedRiderReleased += (a, f) => released.Add(a);
            string[] agents = { "in", "out" };
            d.Request("in", 0, 2);
            // 'out' must be requested after the cabin has left floor 0 with 'in' aboard (otherwise 'out' would board
            // before the jam and legitimately hold in the cabin), then the doors jam before the cabin arrives at floor 2.
            float floor0 = FloorYs(Layout())[0];
            for (int i = 0; i < 72 * 30; i++) { s.Step(agents); if (d.Cabin == CabinPhase.Moving && d.CabinY > floor0 + 0.05f) break; }
            Assert.AreEqual(CabinPhase.Moving, d.Cabin, "the cabin is under way with 'in' aboard");
            d.Request("out", 0, 1);
            d.DoorsJammed = true;
            s.Run(agents, d.settings.doorStuckSeconds + 20f);
            Assert.AreEqual(CabinPhase.OutOfService, d.Cabin);
            RideResult rin = s.results.Find(r => r.agent == "in"), rout = s.results.Find(r => r.agent == "out");
            Assert.AreEqual(RideFailure.DoorStuck, rin.failure);
            Assert.AreEqual(RideFallback.HoldInCabin, rin.fallback, "never through closed doors");
            Assert.AreEqual(RideFallback.ReturnHomeViaWalkway, rout.fallback);
            Assert.AreEqual(1, s.results.FindAll(r => r.agent == "in").Count);
            Assert.AreEqual(1, s.results.FindAll(r => r.agent == "out").Count);
            // New requests are refused up front, once.
            int before = s.results.Count;
            Assert.AreEqual(0, d.Request("late", 0, 1));
            Assert.AreEqual(before + 1, s.results.Count);
            Assert.AreEqual(RideFailure.OutOfService, s.results[s.results.Count - 1].failure);
            d.ClearFault();
            s.Run(agents, 10f);
            CollectionAssert.AreEqual(new[] { "in" }, released);
            Assert.AreEqual(CabinPhase.Idle, d.Cabin);
        }

        [Test]
        public void Interrupt_MidTravel_LandsAtAFloor_LetsTheRiderOut_FailsOnce()
        {
            HabitatLayout L = Layout();
            var d = Disp(L);
            var s = new Script(d);
            s.Run(new string[0], 0f);
            d.Request("a", 0, 2);
            for (int i = 0; i < 72 * 30; i++) { s.Step(new[] { "a" }); if (d.Cabin == CabinPhase.Moving && d.CabinY > FloorYs(L)[0] + 0.05f) break; }
            d.Interrupt();
            s.Run(new[] { "a" }, 20f);
            Assert.AreEqual(1, s.results.Count);
            Assert.AreEqual(RideFailure.Interrupted, s.results[0].failure);
            Assert.AreEqual(RideFallback.ReturnHomeViaWalkway, s.results[0].fallback);
            bool atLanding = false; foreach (float y in FloorYs(L)) atLanding |= Mathf.Abs(d.CabinY - y) < 1e-4f;
            Assert.IsTrue(atLanding, "never left between floors");
            Assert.AreEqual(CabinPhase.Idle, d.Cabin);
        }

        [Test]
        public void InvalidRequests_AreRejectedOnce_WithoutATicket()
        {
            var d = Disp(Layout());
            var results = new List<RideResult>();
            d.RideCompleted += r => results.Add(r);
            Assert.AreEqual(0, d.Request("a", 1, 1));
            Assert.AreEqual(0, d.Request("a", 0, 9));
            Assert.AreEqual(0, d.Request("", 0, 1));
            Assert.AreEqual(3, results.Count);
            foreach (var r in results) Assert.AreEqual(RideFailure.InvalidRequest, r.failure);
            Assert.AreEqual(0, d.OpenRequests);
        }

        [Test]
        public void Completion_IsExactlyOnePerRequest_AcrossAMixedWorkload()
        {
            HabitatLayout L = Layout();
            var d = Disp(L, 2);
            var s = new Script(d);
            string[] agents = { "a", "b", "c", "d", "e" };
            d.Request("a", 0, 2); d.Request("b", 2, 0); d.Request("c", 1, 0); d.Request("d", 0, 1); d.Request("e", 0, 2);
            d.Cancel("d");
            s.frozenAt.Add("e");
            s.Run(agents, d.settings.queueTimeout + 60f);
            var counts = new Dictionary<string, int>();
            foreach (var r in s.results) { int c; counts.TryGetValue(r.agent, out c); counts[r.agent] = c + 1; }
            foreach (string a in agents) Assert.AreEqual(1, counts[a], a + " completed exactly once");
            Assert.AreEqual(5, s.released.Count);
            Assert.AreEqual(0, d.OpenRequests);
        }

        // ── floor route graph ──

        [Test]
        public void RouteGraph_AllEdges_AreValid_AndClassified([Values(WallLayoutKind.Full, WallLayoutKind.Compact)] WallLayoutKind k)
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(k);
            var g = new FloorRouteGraph(L);
            Assert.AreEqual(0, g.InvalidEdgeCount);
            var seen = new HashSet<FloorEdgeClass>();
            for (int i = 0; i < L.nav.edges.Count; i++) seen.Add(g.ClassOf(i));
            foreach (FloorEdgeClass c in System.Enum.GetValues(typeof(FloorEdgeClass))) Assert.IsTrue(seen.Contains(c), "no edge of class " + c);
        }

        [Test]
        public void RouteGraph_ResearcherToManagerHandoff_IsAValidatedPolyline_ThroughWalkwayElevatorLevelCubicleOrder()
        {
            HabitatLayout L = Layout();
            var g = new FloorRouteGraph(L);
            FloorRoute r = g.Query(HabitatLayout.WorkNode(HabitatRole.Researcher), HabitatLayout.HandoffAtManager);
            Assert.IsTrue(r.found);
            Assert.IsTrue(r.hasRide);
            Assert.AreEqual(r.nodes.Count, r.polyline.Count);
            Assert.AreEqual(r.nodes.Count - 1, r.edgeClasses.Count);
            Assert.AreEqual(FloorEdgeClass.LevelToCubicle, r.edgeClasses[0]);
            Assert.AreEqual(FloorEdgeClass.CubicleToHandoff, r.edgeClasses[r.edgeClasses.Count - 1]);
            Assert.Less(r.edgeClasses.IndexOf(FloorEdgeClass.WalkwayToElevator), r.edgeClasses.IndexOf(FloorEdgeClass.ElevatorBoarding));
            Assert.Less(r.edgeClasses.IndexOf(FloorEdgeClass.ElevatorBoarding), r.edgeClasses.IndexOf(FloorEdgeClass.ElevatorRide));
            // Every polyline step is a real nav edge, and every walk step keeps the clearance along its whole length.
            for (int i = 1; i < r.nodes.Count; i++)
            {
                Assert.IsTrue(L.nav.Connected(r.nodes[i - 1], r.nodes[i]), "polyline jumps between unconnected nodes");
                if (r.edgeClasses[i - 1] == FloorEdgeClass.ElevatorRide) continue;
                HabitatNavNode a = L.nav.nodes[r.nodes[i - 1]], b = L.nav.nodes[r.nodes[i]];
                for (int s = 0; s <= 20; s++)
                {
                    Vector3 p = Vector3.Lerp(a.position, b.position, s / 20f);
                    string hit;
                    Assert.GreaterOrEqual(HabitatLayoutValidator.CapsuleGap(L, p, p, a.floor, out hit), L.clearance - 1e-4f, "clearance on " + a.id + "-" + b.id + " near " + hit);
                }
            }
        }

        [Test]
        public void RouteGraph_BlockedElevator_GivesNoRoute_WithReturnHomeFallback_NotAStraightLine()
        {
            HabitatLayout L = Layout();
            var g = new FloorRouteGraph(L);
            g.SetRidesAvailable(false);
            FloorRoute r = g.Query(HabitatLayout.WorkNode(HabitatRole.Tester), HabitatLayout.HandoffAtManager, HabitatLayout.WorkNode(HabitatRole.Tester));
            Assert.IsFalse(r.found);
            Assert.AreEqual(NoRouteReason.RideUnavailable, r.reason);
            Assert.AreEqual(RouteFallback.ReturnHome, r.fallback);
            for (int i = 1; i < r.nodes.Count; i++) Assert.IsTrue(L.nav.Connected(r.nodes[i - 1], r.nodes[i]));
            // Same floor still routes with the elevator closed.
            Assert.IsTrue(g.Query(HabitatLayout.WorkNode(HabitatRole.Tester), HabitatLayout.WorkNode(HabitatRole.Researcher)).found);
            g.SetRidesAvailable(true);
            Assert.IsTrue(g.Query(HabitatLayout.WorkNode(HabitatRole.Tester), HabitatLayout.HandoffAtManager).found);
        }

        [Test]
        public void RouteGraph_BlockedCorridor_AndUnknownNode_AreIntentionalFailures()
        {
            HabitatLayout L = Layout();
            var g = new FloorRouteGraph(L);
            FloorRoute u = g.Query("Work_Researcher", "Nowhere", "Work_Researcher");
            Assert.IsFalse(u.found);
            Assert.AreEqual(NoRouteReason.UnknownNode, u.reason);
            Assert.AreNotEqual(RouteFallback.None, u.fallback, "an unknown destination still has an intentional fallback");
            g.SetEdgeBlocked("Entry_Developer", "Door_Developer", true);
            FloorRoute b = g.Query("Work_Developer", HabitatLayout.HandoffAtManager, "Work_Developer");
            Assert.IsFalse(b.found);
            Assert.AreEqual(NoRouteReason.EdgeBlocked, b.reason);
            Assert.AreEqual(RouteFallback.ReturnHome, b.fallback, "the agent can still reach its own desk");
            g.SetEdgeBlocked("Entry_Developer", "Door_Developer", false);
            Assert.IsTrue(g.Query("Work_Developer", HabitatLayout.HandoffAtManager).found);
        }

        [Test]
        public void RouteGraph_InvalidatedEdge_IsNeverUsed()
        {
            HabitatLayout L = Layout();
            HabitatNavNode door = L.nav.Node(HabitatLayout.DoorNode(HabitatRole.Developer));
            L.boxes.Add(HabitatBox.FromMinMax("Intruder", HabitatBoxKind.Desk, door.position + new Vector3(-0.02f, 0, -0.02f), door.position + new Vector3(0.02f, 0.05f, 0.02f), 0));
            var g = new FloorRouteGraph(L);
            Assert.Greater(g.InvalidEdgeCount, 0);
            FloorRoute r = g.Query("Work_Developer", HabitatLayout.HandoffAtManager, "Work_Developer");
            Assert.IsFalse(r.found);
        }
    }
}
#endif
