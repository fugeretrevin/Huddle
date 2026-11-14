// =============================================================================
// MuseDesk · EditMode tests · Wall Habitat layout, local navigation, elevator and choreography
// (HabitatLayoutBuilder/Validator, HabitatReservations, HabitatElevatorMachine, HabitatWalker, HabitatStage).
// Every simulation checks, EVERY tick, that no character capsule enters static geometry and that no two
// characters on the same floor overlap. Not proven here: the art meshes match these boxes (see the
// runtime bounds check), animation clips, the Quest.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class HabitatMotionTests
    {
        const float Dt = 1f / 72f;

        [Test]
        public void BothLayouts_Validate_WithZeroIssues([Values(WallLayoutKind.Full, WallLayoutKind.Compact)] WallLayoutKind k)
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(k);
            var issues = HabitatLayoutValidator.Validate(L);
            Assert.AreEqual(0, issues.Count, string.Join("\n", issues.ConvertAll(i => i.what).ToArray()));
            Assert.AreEqual(k == WallLayoutKind.Full ? 3 : 2, L.floors.Count);
        }

        [Test]
        public void Sockets_AreUnique_AndRequiredOnesExist([Values(WallLayoutKind.Full, WallLayoutKind.Compact)] WallLayoutKind k)
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(k);
            var ids = new HashSet<string>();
            foreach (var s in L.sockets) Assert.IsTrue(ids.Add(s.id), "duplicate " + s.id);
            foreach (var id in HabitatLayoutBuilder.RequiredHabitatSockets) Assert.IsTrue(ids.Contains(id), id);
            foreach (HabitatRole r in new[] { HabitatRole.Researcher, HabitatRole.Developer, HabitatRole.Tester, HabitatRole.Manager })
                foreach (var pose in HabitatLayoutBuilder.RequiredAgentSockets) Assert.IsTrue(ids.Contains(r + "." + pose), r + "." + pose);
            // Queue poses were patched to the floor's queue node (never left at the origin).
            Assert.AreNotEqual(Vector3.zero, L.Socket("Researcher.ElevatorQueuePose").position);
            Assert.AreNotEqual(Vector3.zero, L.Socket("Manager.ElevatorQueuePose").position);
        }

        [Test]
        public void Validator_CatchesADeskInTheCorridor()
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(WallLayoutKind.Full);
            HabitatNavNode door = L.nav.Node(HabitatLayout.DoorNode(HabitatRole.Developer));
            L.boxes.Add(HabitatBox.FromMinMax("Intruder", HabitatBoxKind.Desk, door.position + new Vector3(-0.02f, 0, -0.02f), door.position + new Vector3(0.02f, 0.05f, 0.02f), 0));
            var issues = HabitatLayoutValidator.Validate(L);
            Assert.Greater(issues.Count, 0);
            Assert.That(issues.Exists(i => i.what.Contains("Intruder")));
        }

        [Test]
        public void RouteToManager_UsesTheElevator_AndStaysOnDecks()
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(WallLayoutKind.Full);
            var route = new List<int>();
            Assert.IsTrue(L.nav.FindRoute(L.nav.IndexOf(HabitatLayout.WorkNode(HabitatRole.Researcher)), L.nav.IndexOf(HabitatLayout.HandoffAtManager), route));
            bool ride = false;
            for (int i = 1; i < route.Count; i++) ride |= L.nav.IsRide(route[i - 1], route[i]);
            Assert.IsTrue(ride);
            Assert.AreEqual(HabitatNodeKind.Queue, L.nav.nodes[route[route.IndexOf(L.nav.IndexOf(HabitatLayout.LandingNode(0))) - 1]].kind, "the landing is reached through the queue spot");
        }

        // ── reservations ──

        [Test]
        public void Reservations_BlockNeighbouringNodes_ForOtherAgents()
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(WallLayoutKind.Full);
            var res = new HabitatReservations(L);
            int door = L.nav.IndexOf(HabitatLayout.DoorNode(HabitatRole.Tester));
            int queue = L.nav.IndexOf(HabitatLayout.QueueNode(0));
            Assert.IsTrue(res.TryReserve(door, "a"));
            Assert.IsFalse(res.CanReserve(queue, "b"), "queue is within two capsules of the tester's door");
            Assert.IsTrue(res.CanReserve(queue, "a"));
            res.ReleaseAll("a");
            Assert.IsTrue(res.TryReserve(queue, "b"));
        }

        // ── elevator ──

        static HabitatElevatorMachine Elevator() { return new HabitatElevatorMachine(new[] { -0.4f, -0.11f, 0.15f }); }

        static void Run(HabitatElevatorMachine e, float seconds) { for (float t = 0; t < seconds; t += Dt) e.Tick(Dt); }

        [Test]
        public void Elevator_OneRider_FullCycle_FollowsLandingHeights()
        {
            var e = Elevator();
            e.Request("r", 0, 2);
            Run(e, 1f);
            Assert.IsTrue(e.MayBoard("r"));
            Assert.AreEqual(1f, e.DoorOpen01, 1e-4f);
            e.ReportBoarded("r");
            Run(e, 0.3f);
            Assert.AreEqual(ElevatorState.DoorsClosing, e.State, "doors close before travel");
            Run(e, 5f);
            Assert.IsTrue(e.MustExit("r"));
            Assert.AreEqual(0.15f, e.CabinY, 1e-4f, "stops at the real manager landing height");
            e.ReportExited("r");
            Run(e, 1f);
            Assert.AreEqual(ElevatorState.IdleAtFloor, e.State);
            Assert.AreEqual(0f, e.DoorOpen01, 1e-4f);
        }

        [Test]
        public void Elevator_QueuesFifo_AndCoalescesRepeatRequests()
        {
            var e = Elevator();
            e.Request("a", 0, 2);
            e.Request("b", 0, 1);
            e.Request("b", 0, 2);          // coalesced: still one ticket
            Assert.AreEqual(2, e.QueueLength);
            Run(e, 1f);
            Assert.IsTrue(e.MayBoard("a"));
            Assert.IsFalse(e.MayBoard("b"), "one rider at a time");
            e.Cancel("a");
            Run(e, 2f);
            Assert.IsTrue(e.MayBoard("b"));
            Assert.AreEqual(2, e.DestinationOf("b"));
        }

        [Test]
        public void Elevator_NoShow_IsEvicted_AndCabinRecovers()
        {
            var e = Elevator();
            string evicted = null;
            e.RiderEvicted += a => evicted = a;
            e.Request("a", 0, 2);
            Run(e, e.settings.boardTimeout + 2f);
            Assert.AreEqual("a", evicted);
            Assert.AreEqual(ElevatorState.IdleAtFloor, e.State);
        }

        [Test]
        public void Elevator_Interrupt_MidTravel_StopsAtALanding_AndEvicts()
        {
            var e = Elevator();
            string evicted = null;
            e.RiderEvicted += a => evicted = a;
            e.Request("a", 0, 2);
            Run(e, 1f); e.ReportBoarded("a"); Run(e, 1.2f);
            Assert.AreEqual(ElevatorState.Traveling, e.State);
            e.Interrupt();
            Run(e, 4f);
            Assert.AreEqual("a", evicted);
            bool atLanding = Mathf.Abs(e.CabinY + 0.4f) < 1e-4f || Mathf.Abs(e.CabinY + 0.11f) < 1e-4f || Mathf.Abs(e.CabinY - 0.15f) < 1e-4f;
            Assert.IsTrue(atLanding, "cabin never left between floors: " + e.CabinY);
            Assert.AreEqual(ElevatorState.IdleAtFloor, e.State);
        }

        // ── walking and the stage, with geometry checks every tick ──

        static void AssertSafe(HabitatStage st, string when)
        {
            HabitatLayout L = st.layout;
            var ws = new HabitatWalker[4];
            for (int r = 0; r < 4; r++) ws[r] = st.Walker((HabitatRole)r);
            for (int r = 0; r < 4; r++)
            {
                HabitatWalker w = ws[r];
                if (w.Visibility <= 0.01f) continue;
                int floor = FloorAt(L, w.Position.y);
                if (floor < 0) continue;      // riding between landings: inside the shaft, checked by the cabin size
                string hit;
                float gap = HabitatLayoutValidator.CapsuleGap(L, w.Position, w.Position, floor, out hit);
                Assert.GreaterOrEqual(gap, L.clearance - 2e-3f, when + ": " + w.role + " at " + w.Position.ToString("F3") + " within " + gap.ToString("F4") + " of " + hit);
                for (int s = r + 1; s < 4; s++)
                {
                    HabitatWalker o = ws[s];
                    if (o.Visibility <= 0.01f || Mathf.Abs(o.Position.y - w.Position.y) > 0.05f) continue;
                    Vector2 a = new Vector2(w.Position.x, w.Position.z), b = new Vector2(o.Position.x, o.Position.z);
                    Assert.GreaterOrEqual((a - b).magnitude, 2f * L.agentRadius - 2e-3f, when + ": " + w.role + " overlaps " + o.role);
                }
            }
        }

        static int FloorAt(HabitatLayout L, float y)
        {
            for (int i = 0; i < L.floors.Count; i++) if (Mathf.Abs(L.floors[i].floorY - y) < 1e-3f) return i;
            return -1;
        }

        static float Simulate(HabitatStage st, float seconds, float t0 = 0f)
        {
            float t = t0;
            for (; t < t0 + seconds; t += Dt) { st.Tick(Dt, t); AssertSafe(st, "t=" + t.ToString("F2")); }
            return t;
        }

        static bool AllHome(HabitatStage st)
        {
            for (int r = 0; r < 4; r++) if (!st.Walker((HabitatRole)r).AtHome || st.Running((HabitatRole)r) != null) return false;
            return true;
        }

        [Test]
        public void ResearcherCarriesToManager_RidesUp_AndComesHome([Values(WallLayoutKind.Full, WallLayoutKind.Compact)] WallLayoutKind k)
        {
            var st = new HabitatStage(HabitatLayoutBuilder.Build(k));
            string arrived = null;
            st.Walker(HabitatRole.Researcher).ArrivedAt += n => arrived = n;
            Assert.IsTrue(st.Accept(HabitatEvent.HandoffOf("ev-1", HabitatRole.Researcher, HabitatRole.Manager, "t1"), 0f));
            Simulate(st, 60f);
            Assert.AreEqual(HabitatLayout.HandoffAtManager, arrived);
            Assert.IsTrue(AllHome(st));
            Assert.AreEqual(1, st.Completed);
            Assert.AreEqual(0, st.Walker(HabitatRole.Researcher).SoftTransfers, st.Walker(HabitatRole.Researcher).LastBlock);
            Assert.IsFalse(st.TravelLocked);
        }

        [Test]
        public void TwoElevatorTrips_AreSerialised_NoOverlap_BothComplete()
        {
            var st = new HabitatStage(HabitatLayoutBuilder.Build(WallLayoutKind.Full));
            st.Accept(HabitatEvent.Of("ev-a", HabitatEventKind.TaskCompleted, HabitatRole.Researcher, "t1"), 0f);
            st.Accept(HabitatEvent.Of("ev-b", HabitatEventKind.TaskFailed, HabitatRole.Tester, "t2"), 0f);
            Simulate(st, 120f);
            Assert.IsTrue(AllHome(st));
            Assert.AreEqual(2, st.Completed);
        }

        [Test]
        public void SameFloorCrossing_CorridorLeases_PreventFaceToFace()
        {
            var st = new HabitatStage(HabitatLayoutBuilder.Build(WallLayoutKind.Full));
            st.Accept(HabitatEvent.HandoffOf("x1", HabitatRole.Researcher, HabitatRole.Tester), 0f);
            st.Accept(HabitatEvent.HandoffOf("x2", HabitatRole.Tester, HabitatRole.Researcher), 0f);
            st.Accept(HabitatEvent.HandoffOf("x3", HabitatRole.Developer, HabitatRole.Researcher), 0f);
            Simulate(st, 90f);
            Assert.IsTrue(AllHome(st));
            Assert.AreEqual(3, st.Completed);
        }

        [Test]
        public void DuplicateEvents_AreIgnored_ManagerDelegationIsNotAWalk()
        {
            var st = new HabitatStage(HabitatLayoutBuilder.Build(WallLayoutKind.Full));
            Assert.IsTrue(st.Accept(HabitatEvent.HandoffOf("dup", HabitatRole.Developer, HabitatRole.Tester), 0f));
            Assert.IsFalse(st.Accept(HabitatEvent.HandoffOf("dup", HabitatRole.Developer, HabitatRole.Tester), 0f));
            Assert.AreEqual(1, st.Duplicates);
            Assert.IsFalse(st.Accept(HabitatEvent.HandoffOf("m1", HabitatRole.Manager, HabitatRole.Developer), 0f));
            Assert.IsFalse(st.Accept(HabitatEvent.Of("s1", HabitatEventKind.TaskStarted, HabitatRole.Developer), 0f), "status changes are not trips");
        }

        [Test]
        public void EventBurst_IsCoalesced_NeverMoreThanTheCapPending()
        {
            var st = new HabitatStage(HabitatLayoutBuilder.Build(WallLayoutKind.Full));
            HabitatRole[] targets = { HabitatRole.Developer, HabitatRole.Tester, HabitatRole.Manager, HabitatRole.Developer, HabitatRole.Tester };
            for (int i = 0; i < targets.Length; i++) st.Accept(HabitatEvent.HandoffOf("b" + i, HabitatRole.Researcher, targets[i]), 0f);
            Assert.LessOrEqual(st.Pending(HabitatRole.Researcher), st.settings.maxPendingPerAgent);
            Simulate(st, 150f);
            Assert.IsTrue(AllHome(st), "no perpetual travel");
        }

        [Test]
        public void StaleActs_AreDropped_NotPlayedLate()
        {
            var st = new HabitatStage(HabitatLayoutBuilder.Build(WallLayoutKind.Full));
            st.Accept(HabitatEvent.Of("old", HabitatEventKind.ApprovalRequested, HabitatRole.Developer), -100f);
            st.Tick(Dt, 0f);
            Assert.IsNull(st.Running(HabitatRole.Developer));
            Assert.AreEqual(1, st.Dropped);
        }

        [Test]
        public void Reset_MidRide_EveryoneHome_ElevatorRecovers()
        {
            var st = new HabitatStage(HabitatLayoutBuilder.Build(WallLayoutKind.Full));
            st.Accept(HabitatEvent.HandoffOf("r1", HabitatRole.Tester, HabitatRole.Manager), 0f);
            float t = 0f;
            for (; t < 40f && st.Walker(HabitatRole.Tester).State != WalkerState.Riding; t += Dt) st.Tick(Dt, t);
            Assert.AreEqual(WalkerState.Riding, st.Walker(HabitatRole.Tester).State, "reached the cabin");
            st.Accept(new HabitatEvent { kind = HabitatEventKind.Reset }, t);
            Assert.IsTrue(AllHome(st));
            Simulate(st, 6f, t);
            Assert.AreEqual(ElevatorState.IdleAtFloor, st.elevator.State);
            Assert.IsFalse(st.TravelLocked);
        }

        [Test]
        public void Walker_NoRoute_DoesNotMove()
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(WallLayoutKind.Full);
            var res = new HabitatReservations(L);
            var w = new HabitatWalker("researcher", HabitatRole.Researcher, L, res, new HabitatElevatorMachine(new[] { -0.4f, -0.11f, 0.15f }));
            Vector3 p = w.Position;
            Assert.IsFalse(w.GoTo("NoSuchNode", 1f));
            w.Tick(1f);
            Assert.AreEqual(p, w.Position);
            Assert.IsTrue(w.AtHome);
        }

        [Test]
        public void Walker_BlockedCorridor_SoftTransfersHome_InsteadOfWalkingThrough()
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(WallLayoutKind.Full);
            var res = new HabitatReservations(L);
            var elev = new HabitatElevatorMachine(new[] { -0.4f, -0.11f, 0.15f });
            var w = new HabitatWalker("researcher", HabitatRole.Researcher, L, res, elev);
            res.TryReserve(L.nav.IndexOf(HabitatLayout.DoorNode(HabitatRole.Developer)), "stranger");   // never released
            Assert.IsTrue(w.GoTo(HabitatLayout.DoorNode(HabitatRole.Tester), 1f));
            for (float t = 0; t < 30f; t += Dt) { elev.Tick(Dt); w.Tick(Dt); }
            Assert.IsTrue(w.AtHome);
            Assert.GreaterOrEqual(w.SoftTransfers, 1);
            Assert.AreEqual("stranger", res.OwnerOf(L.nav.IndexOf(HabitatLayout.DoorNode(HabitatRole.Developer))));
        }

        [Test]
        public void TryRole_MapsBackendIds()
        {
            HabitatRole r;
            Assert.IsTrue(HabitatStage.TryRole("developer", out r)); Assert.AreEqual(HabitatRole.Developer, r);
            Assert.IsTrue(HabitatStage.TryRole("Manager", out r)); Assert.AreEqual(HabitatRole.Manager, r);
            Assert.IsFalse(HabitatStage.TryRole("deployer", out r));
        }
    }
}
#endif
