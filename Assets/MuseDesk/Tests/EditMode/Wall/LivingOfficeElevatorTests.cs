// =============================================================================
// MuseDesk · EditMode tests · Living Office: the A7 ElevatorDispatcher driving the habitat walkers
// (DispatchElevator, HabitatWalker wait-spot handshake, HabitatStage ride lane, card pick-up and receipt acts).
// Every simulation RECORDS the pose each walker writes, every tick, and checks: no capsule enters static geometry,
// no two characters overlap, nobody is past the door line unless the doors are fully open with the cabin there,
// a rider's pose is exactly the cabin's, nobody walks while the cabin travels, every ride request completes once.
// Not proven here: the drawn doors/avatars (view), animation clips, the Quest.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class LivingOfficeElevatorTests
    {
        const float Dt = 1f / 72f;

        sealed class Sim
        {
            public readonly HabitatLayout L;
            public readonly HabitatStage stage;
            public float now;
            public int ticks, posesRecorded, ridingTicks, doorCrossTicks, onDeckTicks, facedChecks;
            public readonly List<string> arrivals = new List<string>();
            public readonly List<string> ended = new List<string>();
            public readonly List<string> dropped = new List<string>();
            public readonly List<RideResult> rides = new List<RideResult>();
            readonly WalkerState[] _prev = new WalkerState[4];
            readonly float _landingZ, _doorHalf;

            public Sim(WallLayoutKind kind)
            {
                L = HabitatLayoutBuilder.Build(kind);
                stage = new HabitatStage(L, true);
                stage.ActArrived += a => arrivals.Add(a.kind + ":" + a.actor);
                stage.ActEnded += a => ended.Add(a.kind + ":" + a.actor);
                stage.ActDropped += (a, why) => dropped.Add(a.kind + ":" + a.actor + ":" + why);
                stage.dispatch.RideCompleted += r => rides.Add(r);
                _landingZ = L.nav.Node(HabitatLayout.LandingNode(0)).position.z;
                _doorHalf = L.doorWidth * 0.5f;
            }

            public HabitatWalker W(HabitatRole r) { return stage.Walker(r); }

            public void Event(HabitatEventKind k, HabitatRole agent, string id)
            {
                stage.Accept(HabitatEvent.Of(id, k, agent, "task-" + id), now);
            }

            /// <summary>Run until <paramref name="done"/> (or the time limit), asserting the safety rules on every tick.</summary>
            public bool Run(float seconds, Func<bool> done = null)
            {
                for (float t = 0f; t < seconds; t += Dt)
                {
                    stage.Tick(Dt, now);
                    now += Dt;
                    ticks++;
                    Check();
                    if (done != null && done()) return true;
                }
                return done == null;
            }

            int FloorAt(float y)
            {
                int best = 0; float bd = float.MaxValue;
                for (int f = 0; f < L.floors.Count; f++) { float d = Mathf.Abs(L.floors[f].floorY - y); if (d < bd) { bd = d; best = f; } }
                return best;
            }

            void Check()
            {
                IHabitatElevator lift = stage.lift;
                Assert.AreEqual(0, stage.dispatch.Violations, "a rider reported entering or leaving against the rules");
                for (int r = 0; r < 4; r++)
                {
                    HabitatWalker w = W((HabitatRole)r);
                    Vector3 p = w.Position;                      // the pose the view writes this frame
                    posesRecorded++;
                    HabitatNavNode cabinNode = L.nav.Node(HabitatLayout.CabinNode(0));
                    bool inShaftColumn = Mathf.Abs(p.x - L.elevatorX) <= _doorHalf && p.z < _landingZ - 1e-4f;

                    if (w.State == WalkerState.Riding)
                    {
                        ridingTicks++;
                        Vector3 want = ElevatorDispatcher.RiderPosition(cabinNode.position, lift.CabinY);
                        Assert.AreEqual(want.x, p.x, 1e-5f, "rider x is the cabin's");
                        Assert.AreEqual(want.z, p.z, 1e-5f, "rider z is the cabin's");
                        Assert.AreEqual(want.y, p.y, 1e-5f, "rider y is the cabin's");
                        Assert.AreEqual(0f, w.Speed, 1e-6f, "a rider does not walk");
                    }
                    else if (w.Visibility >= 1f)
                    {
                        int floor = FloorAt(p.y);
                        Assert.AreEqual(L.floors[floor].floorY, p.y, 1e-4f, "feet are on a deck");
                        string hit;
                        float gap = HabitatLayoutValidator.CapsuleGap(L, p, p, floor, out hit);
                        Assert.GreaterOrEqual(gap, L.clearance - 1e-3f, (HabitatRole)r + " is within " + gap.ToString("F4") + " m of " + hit + " at " + p.ToString("F3"));
                        if (inShaftColumn)
                        {
                            // Between the landing and the cabin floor: the door line is being crossed.
                            doorCrossTicks++;
                            Assert.GreaterOrEqual(lift.DoorOpen01, 1f, (HabitatRole)r + " is past the door line with the doors at " + lift.DoorOpen01.ToString("F2"));
                            Assert.AreEqual(L.floors[floor].floorY, lift.CabinY, 1e-4f, "the cabin is at this landing");
                            Assert.IsFalse(lift.CabinMoving, "nobody is in the doorway while the cabin travels");
                        }
                    }
                    if (lift.CabinMoving && w.State == WalkerState.Walking)
                        Assert.IsFalse(inShaftColumn, "nobody walks inside the shaft while the cabin travels");

                    // The rider turned to face the door before it stepped off the wait spot toward it.
                    HabitatNavNode at = L.nav.nodes[w.CurrentNode];
                    if (_prev[r] == WalkerState.WaitingElevator && w.State == WalkerState.Walking && at.kind == HabitatNodeKind.Queue && w.TripHasRide
                        && stage.dispatch.dispatcher.MayEnterCabin(w.agent))
                    {
                        facedChecks++;
                        Vector3 d = L.nav.Node(HabitatLayout.LandingNode(at.floor)).position - at.position;
                        float heading = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                        Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(w.Yaw, heading)), w.settings.faceDoorTolerance + 0.5f, "faces the door when boarding starts");
                    }
                    _prev[r] = w.State;

                    // Waiting for the lane happens OFF the corridor (desk or aisle), never on it.
                    HabitatAct act = stage.Running((HabitatRole)r);
                    if (act != null && act.needsRide && stage.OnDeck == r)
                    {
                        onDeckTicks++;
                        Assert.IsTrue(at.kind == HabitatNodeKind.Work || at.kind == HabitatNodeKind.Aisle, (HabitatRole)r + " waits for the lane at " + at.id);
                    }
                }
                // No two visible characters overlap (riders are alone in the cabin: capacity 1).
                for (int a = 0; a < 4; a++)
                    for (int b = a + 1; b < 4; b++)
                    {
                        HabitatWalker wa = W((HabitatRole)a), wb = W((HabitatRole)b);
                        if (wa.Visibility < 1f || wb.Visibility < 1f) continue;
                        if (Mathf.Abs(wa.Position.y - wb.Position.y) > 0.05f) continue;
                        Vector3 d = wa.Position - wb.Position; d.y = 0f;
                        Assert.GreaterOrEqual(d.magnitude, 2f * L.agentRadius - 1e-3f, (HabitatRole)a + " and " + (HabitatRole)b + " overlap");
                    }
                int riders = 0;
                for (int r = 0; r < 4; r++) if (W((HabitatRole)r).State == WalkerState.Riding) riders++;
                Assert.LessOrEqual(riders, 1, "one rider in the cabin");
            }

            public bool AllHome()
            {
                for (int r = 0; r < 4; r++) if (!W((HabitatRole)r).AtHome || stage.Running((HabitatRole)r) != null || stage.Pending((HabitatRole)r) > 0) return false;
                return true;
            }

            public void AssertSettled(int expectArrivedRides)
            {
                DispatchElevator d = stage.dispatch;
                Assert.AreEqual(0, d.dispatcher.OpenRequests, "no ticket left open");
                Assert.AreEqual(0, d.PendingRequests, "no request left waiting for the lane");
                Assert.AreEqual(0, d.Ghosts);
                Assert.AreEqual(expectArrivedRides, d.Arrived, "rides arrived: " + string.Join(", ", rides.ConvertAll(x => x.ToString()).ToArray()));
                Assert.AreEqual(0, d.Failed, "no ride failed");
                var seen = new HashSet<int>();
                foreach (RideResult r in rides) if (r.ticketId != 0) Assert.IsTrue(seen.Add(r.ticketId), "ticket " + r.ticketId + " completed twice");
                for (int r = 0; r < 4; r++) Assert.AreEqual(0, W((HabitatRole)r).SoftTransfers, (HabitatRole)r + " was faded home: " + W((HabitatRole)r).LastBlock);
                Assert.IsFalse(stage.TravelLocked);
                Assert.AreEqual(-1, stage.OnDeck);
            }
        }

        [Test]
        public void DefaultStage_StillRunsTheOriginalMachine()
        {
            var stage = new HabitatStage(HabitatLayoutBuilder.Build(WallLayoutKind.Full));
            Assert.IsNotNull(stage.elevator);
            Assert.IsNull(stage.dispatch);
            Assert.AreSame(stage.elevator, stage.lift);
            Assert.IsFalse(stage.lift.UsesWaitSpot);
        }

        [Test]
        public void OneRider_RoundTrip_EveryPoseSafe_EachRideCompletesOnce([Values(WallLayoutKind.Full, WallLayoutKind.Compact)] WallLayoutKind kind)
        {
            var s = new Sim(kind);
            Assert.IsNull(s.stage.elevator, "the two elevator authorities never run together");
            s.Event(HabitatEventKind.Handoff, HabitatRole.Developer, "e1");   // no target: ignored
            Assert.AreEqual(0, s.stage.Pending(HabitatRole.Developer));
            s.stage.Accept(HabitatEvent.HandoffOf("h1", HabitatRole.Developer, HabitatRole.Manager, "t1"), s.now);
            Assert.IsTrue(s.Run(120f, s.AllHome), "the trip ended: " + s.stage.dispatch);
            Assert.AreEqual(1, s.stage.Completed);
            CollectionAssert.AreEqual(new[] { "CarryToManager:Developer" }, s.arrivals);
            s.AssertSettled(2);                      // up and back down
            Assert.Greater(s.ridingTicks, 0);
            Assert.Greater(s.doorCrossTicks, 0, "the door line was crossed (with the doors open)");
            Assert.AreEqual(2, s.facedChecks, "boarded twice, facing the door each time");
            Assert.Greater(s.posesRecorded, 1000);
            // Doors closed and cabin idle at the end.
            s.Run(3f);
            Assert.AreEqual(0f, s.stage.lift.DoorOpen01, 1e-4f);
        }

        [Test]
        public void ThreeRiders_TakeTheLaneFirstComeFirstServed_TheNextWaitsAtItsAisle_NoDeadlock()
        {
            var s = new Sim(WallLayoutKind.Full);
            // Printed in this order: tester, developer, researcher (not role order).
            s.Event(HabitatEventKind.CardPrinted, HabitatRole.Tester, "c1");
            s.Run(0.2f);
            s.Event(HabitatEventKind.CardPrinted, HabitatRole.Developer, "c2");
            s.Run(0.2f);
            s.Event(HabitatEventKind.CardPrinted, HabitatRole.Researcher, "c3");
            Assert.IsTrue(s.Run(400f, () => s.AllHome() && s.stage.Completed == 3), "all three trips ended: " + s.stage.dispatch + " dropped=" + string.Join(";", s.dropped.ToArray()));
            CollectionAssert.AreEqual(new[] { "CollectCard:Tester", "CollectCard:Developer", "CollectCard:Researcher" }, s.arrivals, "served in request order");
            Assert.AreEqual(0, s.dropped.Count);
            s.AssertSettled(6);
            Assert.Greater(s.onDeckTicks, 0, "the next rider stood by while the lane was busy");
        }

        [Test]
        public void SameAgentsCards_CoalesceIntoOneTrip_AndTheManagersOwnCardsStayInTheTray()
        {
            var s = new Sim(WallLayoutKind.Full);
            s.Event(HabitatEventKind.CardPrinted, HabitatRole.Developer, "c1");
            s.Event(HabitatEventKind.CardPrinted, HabitatRole.Developer, "c2");
            s.Event(HabitatEventKind.CardPrinted, HabitatRole.Manager, "c3");
            Assert.AreEqual(1, s.stage.Pending(HabitatRole.Developer));
            Assert.AreEqual(0, s.stage.Pending(HabitatRole.Manager));
            Assert.IsTrue(s.Run(150f, s.AllHome));
            CollectionAssert.AreEqual(new[] { "CollectCard:Developer" }, s.arrivals);
            s.AssertSettled(2);
        }

        [Test]
        public void Synthesis_TheManagerBringsTheReceiptDownToTheDispatchSpot_AndGoesBack()
        {
            var s = new Sim(WallLayoutKind.Full);
            s.Event(HabitatEventKind.SynthesisReady, HabitatRole.Researcher, "x0");     // only the Manager delivers it
            Assert.AreEqual(0, s.stage.Pending(HabitatRole.Researcher));
            s.Event(HabitatEventKind.SynthesisReady, HabitatRole.Manager, "x1");
            bool atDispatch = false;
            s.stage.ActArrived += a =>
            {
                if (a.kind != HabitatActKind.DeliverReceipt) return;
                atDispatch = (s.W(HabitatRole.Manager).Position - s.L.nav.Node(HabitatLayout.DispatchNode).position).magnitude < 1e-4f;
            };
            Assert.IsTrue(s.Run(200f, s.AllHome), "the Manager came back: " + s.stage.dispatch);
            Assert.IsTrue(atDispatch, "arrived exactly on the dispatch node");
            s.AssertSettled(2);
        }

        [Test]
        public void Reset_WhileTheCabinTravelsWithARider_NobodyIsLeftBetweenFloors_AndTheNextRideWorks()
        {
            var s = new Sim(WallLayoutKind.Full);
            s.stage.Accept(HabitatEvent.HandoffOf("h1", HabitatRole.Tester, HabitatRole.Manager, "t1"), s.now);
            Assert.IsTrue(s.Run(60f, () => s.W(HabitatRole.Tester).State == WalkerState.Riding && s.stage.lift.CabinMoving), "riding");
            s.stage.ResetAll();
            Assert.IsTrue(s.W(HabitatRole.Tester).AtHome, "the walker is home at once");
            Assert.AreEqual(1, s.stage.dispatch.Ghosts, "its ticket is still in the cabin");
            Assert.IsTrue(s.Run(30f, () => s.stage.dispatch.dispatcher.OpenRequests == 0), "the ticket was closed at a landing");
            Assert.AreEqual(0, s.stage.dispatch.Ghosts);
            bool atLanding = false;
            for (int f = 0; f < s.L.floors.Count; f++) atLanding |= Mathf.Abs(s.stage.lift.CabinY - s.L.floors[f].floorY) < 1e-4f;
            Assert.IsTrue(atLanding, "the cabin stopped at a landing");
            Assert.AreEqual(1, s.rides.Count, "exactly one completion for the interrupted ride");
            Assert.AreEqual(0, s.W(HabitatRole.Tester).SoftTransfers, "the ghost ticket did not evict the walker again");

            // A new trip after the reset runs normally.
            s.stage.Accept(HabitatEvent.HandoffOf("h2", HabitatRole.Tester, HabitatRole.Manager, "t2"), s.now);
            Assert.IsTrue(s.Run(150f, () => s.AllHome() && s.stage.Completed >= 1), "second trip ended: " + s.stage.dispatch);
            Assert.AreEqual(0, s.stage.dispatch.dispatcher.OpenRequests);
            Assert.AreEqual(2, s.stage.dispatch.Arrived, "up and down after the reset");
            var seen = new HashSet<int>();
            foreach (RideResult r in s.rides) Assert.IsTrue(seen.Add(r.ticketId), "a ticket completed twice");
        }

        [Test]
        public void Reset_WhileWaitingForTheLane_DropsTheWaitingRequest()
        {
            var s = new Sim(WallLayoutKind.Full);
            s.Event(HabitatEventKind.CardPrinted, HabitatRole.Researcher, "c1");
            s.Run(0.1f);
            s.Event(HabitatEventKind.CardPrinted, HabitatRole.Tester, "c2");
            Assert.IsTrue(s.Run(30f, () => s.stage.OnDeck == (int)HabitatRole.Tester && s.stage.dispatch.PendingRequests == 1), "tester stands by with a held request");
            s.stage.ResetAll();
            Assert.AreEqual(0, s.stage.dispatch.PendingRequests);
            Assert.AreEqual(-1, s.stage.OnDeck);
            Assert.IsTrue(s.Run(30f, () => s.stage.dispatch.dispatcher.OpenRequests == 0 && s.AllHome()));
        }

        [Test]
        public void DeliveryActs_OutliveTheOrdinaryStartDeadline_AndADropIsAnnounced()
        {
            var s = new Sim(WallLayoutKind.Full);
            s.stage.settings.deliveryStartDeadline = 5f;
            // The lane is taken by a long trip; a second and third card wait. The third cannot start in 5 s and is dropped.
            s.Event(HabitatEventKind.CardPrinted, HabitatRole.Researcher, "c1");
            s.Run(0.1f);
            s.Event(HabitatEventKind.CardPrinted, HabitatRole.Developer, "c2");
            s.Run(0.1f);
            s.Event(HabitatEventKind.CardPrinted, HabitatRole.Tester, "c3");
            Assert.IsTrue(s.Run(300f, s.AllHome));
            Assert.AreEqual(1, s.dropped.Count, "the card that could not start in time was announced as dropped");
            StringAssert.StartsWith("CollectCard:Tester", s.dropped[0]);
            Assert.AreEqual(2, s.stage.Completed);
        }
    }
}
#endif
