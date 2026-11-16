// =============================================================================
// MuseDesk · EditMode tests · rc10 walker reliability building blocks: corridor claims (HabitatReservations), the
// elevator's KeepWaiting (doors held open for a rider that is visibly waiting for its corridor), the in-cabin hold
// (pose recorded every tick: attached to the cabin, doors fully open, cabin still), and the last-resort watchdog.
// Not proven here: the drawn avatars/doors (WallHabitatView), the Quest.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class WalkerReliabilityClaimTests
    {
        const float Dt = 1f / 72f;

        static float[] FloorYs(HabitatLayout L) { var y = new float[L.floors.Count]; for (int i = 0; i < y.Length; i++) y[i] = L.floors[i].floorY; return y; }

        // ── claims ──

        [Test]
        public void Claims_RefuseOnlyLowerPriorityNewLeases_NeverWhatSomeoneAlreadyHolds([Values(WallLayoutKind.Full, WallLayoutKind.Compact)] WallLayoutKind kind)
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(kind);
            var res = new HabitatReservations(L);
            int doorD = L.nav.IndexOf(HabitatLayout.DoorNode(HabitatRole.Developer));
            int doorR = L.nav.IndexOf(HabitatLayout.DoorNode(HabitatRole.Researcher));
            int queue = L.nav.IndexOf(HabitatLayout.QueueNode(0));
            int doorT = L.nav.IndexOf(HabitatLayout.DoorNode(HabitatRole.Tester));
            var route = new List<int> { doorT, queue };
            res.SetClaim("rider", route, 0, 1, HabitatWalker.RiderPriority + 2f);
            Assert.IsTrue(res.HasClaim("rider"));
            Assert.IsFalse(res.CanLease(doorT, "w", 30f), "a lower priority may not start through the claimed zone");
            Assert.IsFalse(res.CanLease(queue, "w", 30f));
            Assert.IsTrue(res.CanReserve(doorT, "w"), "a claim is not a reservation: nobody holds the node");
            Assert.IsTrue(res.CanLease(doorR, "w", 30f), "outside the zone: free");
            Assert.IsTrue(res.CanLease(doorT, "boss", HabitatWalker.RiderPriority + 5f), "a higher priority is not held back");
            Assert.IsTrue(res.CanLease(doorT, "rider", 0f), "own claim never blocks");
            // Someone already holding a claimed node always keeps it (it must be able to finish and leave).
            Assert.IsTrue(res.TryReserve(doorD, "holder"));
            res.SetClaim("rider", new List<int> { doorD }, 0, 0, HabitatWalker.RiderPriority);
            Assert.IsTrue(res.CanLease(doorD, "holder", 0f), "what the holder owns is never refused by a claim");
            // Ties: the ordinal-first name wins (deterministic).
            res.ClearClaim("rider");
            res.SetClaim("a", route, 0, 1, 5f);
            Assert.IsFalse(res.CanLease(doorT, "b", 5f));
            res.SetClaim("b", route, 0, 1, 5f);
            Assert.IsTrue(res.CanLease(doorT, "a", 5f));
            res.ReleaseAll("a");
            Assert.IsFalse(res.HasClaim("a"), "releasing everything drops the claim too");
            res.ClearClaim("b");
            Assert.AreEqual(0, res.ClaimCount);
            Assert.IsTrue(res.CanLease(doorT, "w", 0f));
        }

        // ── KeepWaiting ──

        [Test]
        public void Dispatcher_KeepWaiting_HoldsTheDoorsOpenForAnExitingRider_ThenTheOldTimeoutStillApplies()
        {
            HabitatLayout L = HabitatLayoutBuilder.Build(WallLayoutKind.Full);
            var d = new ElevatorDispatcher(FloorYs(L), ElevatorWaitSpots.ForLayout(L, 0));
            var results = new List<RideResult>();
            d.RideCompleted += r => results.Add(r);
            d.Request("a", 0, 2);
            RidePhase p = RidePhase.Queued;
            for (int i = 0; i < 72 * 60; i++)
            {
                d.Tick(Dt);
                if (!d.TryGetPhase("a", out p)) break;
                if (p == RidePhase.ToWaitSpot) d.ReportAtWaitSpot("a");
                else if (p == RidePhase.Waiting) d.ReportFacingDoor("a");
                else if (p == RidePhase.Boarding) d.ReportEnteredCabin("a");
                else if (p == RidePhase.Exiting) break;
            }
            Assert.AreEqual(RidePhase.Exiting, p);
            for (float t = 0f; t < d.settings.exitTimeout * 4f; t += Dt)
            {
                d.Tick(Dt);
                Assert.IsTrue(d.KeepWaiting("a"));
                Assert.IsTrue(d.DoorFullyOpen, "the doors stay fully open while the rider waits for its floor");
                Assert.IsTrue(d.MustExit("a"));
            }
            Assert.AreEqual(0, results.Count, "not timed out while it was visibly waiting");
            for (float t = 0f; t < d.settings.exitTimeout + 1f; t += Dt) d.Tick(Dt);
            Assert.AreEqual(1, results.Count, "without KeepWaiting the exit timeout still applies");
            Assert.AreEqual(RideFailure.ExitTimeout, results[0].failure);
            Assert.AreEqual(0, d.Violations);
            Assert.IsFalse(d.KeepWaiting("a"), "no ticket any more");
        }

        [Test]
        public void Machine_KeepWaiting_HoldsTheDoorsOpenForTheActiveRiderOnly()
        {
            var e = new HabitatElevatorMachine(new[] { -0.4f, -0.11f, 0.15f });
            string evicted = null;
            e.RiderEvicted += a => evicted = a;
            e.Request("a", 0, 2);
            for (float t = 0; t < 1f; t += Dt) e.Tick(Dt);
            Assert.IsTrue(e.MayBoard("a"));
            for (float t = 0; t < e.settings.boardTimeout * 3f; t += Dt) { e.Tick(Dt); e.KeepWaiting("a"); e.KeepWaiting("b"); }
            Assert.IsNull(evicted);
            Assert.IsTrue(e.MayBoard("a"));
            Assert.AreEqual(1f, e.DoorOpen01, 1e-4f);
            e.ReportBoarded("a");
            for (float t = 0; t < 8f && !e.MustExit("a"); t += Dt) e.Tick(Dt);
            Assert.IsTrue(e.MustExit("a"));
            for (float t = 0; t < e.settings.exitTimeout * 3f; t += Dt) { e.Tick(Dt); e.KeepWaiting("a"); }
            Assert.IsNull(evicted, "held while it waits for its floor");
            Assert.IsTrue(e.MustExit("a"));
            for (float t = 0; t < e.settings.exitTimeout + 1f; t += Dt) e.Tick(Dt);
            Assert.AreEqual("a", evicted, "the timeout is unchanged for a rider that stops reporting");
        }

        // ── the in-cabin hold, pose recorded every tick ──

        sealed class Rec
        {
            public readonly HabitatLayout L;
            public readonly HabitatStage stage;
            public float now;
            public int holdTicks, problems;
            public string firstProblem;

            public Rec(WallLayoutKind kind) { L = HabitatLayoutBuilder.Build(kind); stage = new HabitatStage(L, true); }

            public bool Run(float seconds, System.Func<bool> done)
            {
                Vector3 cabinXZ = L.nav.Node(HabitatLayout.CabinNode(0)).position;
                for (float t = 0f; t < seconds; t += Dt)
                {
                    stage.Tick(Dt, now);
                    now += Dt;
                    for (int r = 0; r < 4; r++)
                    {
                        HabitatWalker w = stage.Walker((HabitatRole)r);
                        if (!w.HoldingInCabin) continue;
                        holdTicks++;
                        IHabitatElevator lift = stage.lift;
                        Vector3 want = ElevatorDispatcher.RiderPosition(cabinXZ, lift.CabinY);
                        if (w.State != WalkerState.Riding) Fail(w.role + " holds in the cabin while " + w.State);
                        if (lift.DoorOpen01 < 1f) Fail(w.role + " holds with the doors at " + lift.DoorOpen01);
                        if (lift.CabinMoving) Fail(w.role + " holds while the cabin moves");
                        if ((w.Position - want).sqrMagnitude > 1e-9f) Fail(w.role + " off the cabin pose " + w.Position.ToString("F4"));
                        if (w.Speed != 0f) Fail(w.role + " walks in the cabin");
                        if (w.Visibility < 1f) Fail(w.role + " fades in the cabin");
                    }
                    if (done()) return true;
                }
                return false;
            }

            void Fail(string what) { problems++; if (firstProblem == null) firstProblem = "t=" + now.ToString("F2") + " " + what; }
        }

        [Test]
        public void HoldInCabin_TheRiderStaysAttached_DoorsFullyOpen_UntilItsFloorClears([Values(WallLayoutKind.Full, WallLayoutKind.Compact)] WallLayoutKind kind)
        {
            var g = new Rec(kind);
            HabitatWalker rider = g.stage.Walker(HabitatRole.Tester);
            int handoff = g.L.nav.IndexOf(HabitatLayout.HandoffAtManager);
            g.stage.Accept(HabitatEvent.HandoffOf("h", HabitatRole.Tester, HabitatRole.Manager, "t"), 0f);
            Assert.IsTrue(g.Run(60f, () => rider.State == WalkerState.Riding), "boarded");
            Assert.IsTrue(g.stage.reservations.TryReserve(handoff, "visitor"), "someone stands at the hand-off spot");
            Assert.IsTrue(g.Run(30f, () => rider.HoldingInCabin), "the rider holds at the Manager floor");
            g.Run(20f, () => false);
            Assert.IsTrue(rider.HoldingInCabin, "still holding after 20 s (the old exit timeout was 8 s), doors open, not faded");
            Assert.AreEqual(0, rider.SoftTransfers);
            Assert.Greater(rider.WaitSeconds, 15f);
            g.stage.reservations.Release(handoff, "visitor");
            Assert.IsTrue(g.Run(120f, () => g.stage.Completed == 1 && rider.AtHome), "the floor cleared: it walked out, delivered and came home");
            Assert.AreEqual(0, g.problems, g.firstProblem);
            Assert.Greater(g.holdTicks, (int)(20f / Dt));
            Assert.AreEqual(0, rider.SoftTransfers, rider.LastBlock);
            Assert.AreEqual(0, g.stage.dispatch.Failed);
            Assert.AreEqual(2, g.stage.dispatch.Arrived);
        }

        [Test]
        public void HoldInCabin_ForeverBlocked_IsTheOnlyCaseThatFadesHome_AfterTheStuckTimeout()
        {
            var g = new Rec(WallLayoutKind.Full);
            HabitatWalker rider = g.stage.Walker(HabitatRole.Developer);
            int handoff = g.L.nav.IndexOf(HabitatLayout.HandoffAtManager);
            g.stage.Accept(HabitatEvent.HandoffOf("h", HabitatRole.Developer, HabitatRole.Manager, "t"), 0f);
            Assert.IsTrue(g.Run(60f, () => rider.State == WalkerState.Riding), "boarded");
            Assert.IsTrue(g.stage.reservations.TryReserve(handoff, "statue"));        // never released: pathological
            Assert.IsTrue(g.Run(30f, () => rider.HoldingInCabin));
            float heldFrom = g.now;
            Assert.IsTrue(g.Run(rider.settings.stuckTimeout + 5f, () => rider.State == WalkerState.SoftTransfer), "the last resort fires");
            Assert.GreaterOrEqual(g.now - heldFrom, rider.settings.stuckTimeout - 0.1f, "not before the stuck timeout");
            Assert.AreEqual(1, rider.SoftTransfers);
            StringAssert.StartsWith("stuck in the cabin", rider.LastBlock);
            Assert.IsTrue(g.Run(60f, () => rider.AtHome && g.stage.dispatch.dispatcher.OpenRequests == 0), "home, and the cabin let the ticket out at a landing");
            Assert.AreEqual(0, g.problems, g.firstProblem);
            Assert.AreEqual(0, g.stage.dispatch.Violations);
            Assert.AreEqual(0, g.stage.dispatch.Ghosts);
            Assert.IsFalse(g.stage.TravelLocked, "the lane is free again");
        }
    }
}
#endif
