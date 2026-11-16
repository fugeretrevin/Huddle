// =============================================================================
// MuseDesk · EditMode tests · Wall Habitat walker reliability (owner rc9: "took the elevator up to the manager, never
// left the elevator, teleported back to the seat"). Drives HabitatStage with the ELEVATOR DISPATCHER (what ships) and
// the scene's serialized walker settings, records every pose every tick, and checks:
//   - nobody is ever faded home (SoftTransfers == 0) in normal operation, whatever the mix of concurrent acts;
//   - every rider steps out of the cabin at the floor it was going to, by walking;
//   - no two characters overlap, no capsule enters static geometry, a rider's pose is the cabin's, nobody is in the
//     doorway unless the doors are fully open with the cabin at that landing and not moving;
//   - acts are dropped only for the documented stale reasons (queue overflow, too old to start).
// Not proven here: the drawn avatars/doors (WallHabitatView), animation clips, the Quest.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class WalkerReliabilityTests
    {
        const float Dt = 1f / 72f;

        /// <summary>The only reasons an act may be dropped in normal operation (HabitatStage documents both).</summary>
        static readonly string[] StaleReasons = { "too many events: older one skipped", "event too old to show" };

        /// <summary>MainMRScene's serialized WallHabitatController.walker values: the stress runs what ships.</summary>
        static WalkerSettings SceneWalker()
        {
            return new WalkerSettings
            {
                walkSpeed = 0.14f, startSeconds = 0.3f, stopSeconds = 0.28f, turnSmoothTime = 0.16f, walkTurnLimit = 50f,
                leaseTimeout = 6f, elevatorWaitTimeout = 20f, softTransferSeconds = 0.6f, queueWaitTimeout = 90f,
                faceDoorTolerance = 8f, tripTimeout = 45f,
            };
        }

        sealed class Rig
        {
            public readonly HabitatLayout L;
            public readonly HabitatStage stage;
            public float now;
            public int ticks, rideExits, accepted, resets, posesChecked;
            public readonly List<string> problems = new List<string>();
            public int problemCount;
            public readonly List<string> softTransfers = new List<string>();
            public readonly List<string> dropped = new List<string>();
            public readonly List<string> arrivals = new List<string>();
            public readonly List<RideResult> failedRides = new List<RideResult>();
            public readonly List<int> rideTickets = new List<int>();
            public readonly Dictionary<HabitatActKind, int> started = new Dictionary<HabitatActKind, int>();
            /// <summary>Ticks a rider spent in the cabin at its destination with the doors open, waiting for the floor to clear.</summary>
            public readonly int[] cabinHoldTicks = new int[4];
            readonly WalkerState[] _prev = new WalkerState[4];
            readonly int[] _soft = new int[4];
            readonly bool[] _arrived = new bool[4];
            readonly float _landingZ, _doorHalf;
            bool _reset;

            public Rig(WallLayoutKind kind)
            {
                L = HabitatLayoutBuilder.Build(kind);
                stage = new HabitatStage(L, true);
                WalkerSettings ws = SceneWalker();                       // one shared object, as the controller assigns it
                for (int r = 0; r < 4; r++) stage.Walker((HabitatRole)r).settings = ws;
                stage.ActStarted += a => { _arrived[(int)a.actor] = false; int c; started.TryGetValue(a.kind, out c); started[a.kind] = c + 1; };
                stage.ActArrived += a => { _arrived[(int)a.actor] = true; arrivals.Add(a.kind + ":" + a.actor); };
                stage.ActDropped += (a, why) => dropped.Add(why);
                stage.dispatch.RideCompleted += r =>
                {
                    if (r.ticketId != 0) rideTickets.Add(r.ticketId);
                    if (r.outcome == RideOutcome.Failed) failedRides.Add(r);
                };
                _landingZ = L.nav.Node(HabitatLayout.LandingNode(0)).position.z;
                _doorHalf = L.doorWidth * 0.5f;
            }

            public HabitatWalker W(HabitatRole r) { return stage.Walker(r); }

            public bool Accept(HabitatEvent e)
            {
                bool ok = stage.Accept(e, now);
                if (ok) accepted++;
                return ok;
            }

            public void Reset()
            {
                stage.ResetAll();
                resets++;
                _reset = true;
                for (int r = 0; r < 4; r++) _arrived[r] = false;
            }

            public void Step()
            {
                stage.Tick(Dt, now);
                now += Dt;
                ticks++;
                Check();
                _reset = false;
            }

            public bool Run(float seconds, Func<bool> done = null)
            {
                for (float t = 0f; t < seconds; t += Dt)
                {
                    Step();
                    if (done != null && done()) return true;
                }
                return done == null;
            }

            void Problem(string what)
            {
                problemCount++;
                if (problems.Count < 12) problems.Add("t=" + now.ToString("F2") + " " + what);
            }

            int FloorAt(float y)
            {
                int best = 0; float bd = float.MaxValue;
                for (int f = 0; f < L.floors.Count; f++) { float d = Mathf.Abs(L.floors[f].floorY - y); if (d < bd) { bd = d; best = f; } }
                return best;
            }

            int TargetFloor(HabitatRole r)
            {
                HabitatAct a = stage.Running(r);
                if (a == null) return -1;
                if (_arrived[(int)r]) return L.nav.Node(HabitatLayout.WorkNode(r)).floor;
                return L.nav.Node(a.targetNode).floor;
            }

            void Check()
            {
                IHabitatElevator lift = stage.lift;
                if (stage.dispatch.Violations != 0) Problem("dispatcher violations " + stage.dispatch.Violations);
                HabitatNavNode cabinNode = L.nav.Node(HabitatLayout.CabinNode(0));
                int riders = 0;
                for (int r = 0; r < 4; r++)
                {
                    var role = (HabitatRole)r;
                    HabitatWalker w = W(role);
                    Vector3 p = w.Position;
                    posesChecked++;
                    if (w.SoftTransfers != _soft[r])
                    {
                        _soft[r] = w.SoftTransfers;
                        softTransfers.Add("t=" + now.ToString("F2") + " " + role + " faded home: '" + w.LastBlock + "' at " + p.ToString("F3")
                                          + " lift floor=" + lift.CabinFloor + " door=" + lift.DoorOpen01.ToString("F2"));
                    }
                    bool inShaftColumn = Mathf.Abs(p.x - L.elevatorX) <= _doorHalf && p.z < _landingZ - 1e-4f;

                    if (w.State == WalkerState.Riding)
                    {
                        riders++;
                        Vector3 want = ElevatorDispatcher.RiderPosition(cabinNode.position, lift.CabinY);
                        if ((want - p).sqrMagnitude > 1e-9f) Problem(role + " rides off the cabin pose " + p.ToString("F4") + " vs " + want.ToString("F4"));
                        if (w.Speed != 0f) Problem(role + " walks while riding");
                        if (!lift.CabinMoving && lift.DoorOpen01 >= 1f && TargetFloor(role) == lift.CabinFloor) cabinHoldTicks[r]++;
                    }
                    else if (w.Visibility >= 1f)
                    {
                        int floor = FloorAt(p.y);
                        if (Mathf.Abs(L.floors[floor].floorY - p.y) > 1e-4f) Problem(role + " feet off a deck at " + p.ToString("F4"));
                        string hit;
                        float gap = HabitatLayoutValidator.CapsuleGap(L, p, p, floor, out hit);
                        if (gap < L.clearance - 1e-3f) Problem(role + " within " + gap.ToString("F4") + " m of " + hit + " at " + p.ToString("F3"));
                        if (inShaftColumn)
                        {
                            if (lift.DoorOpen01 < 1f) Problem(role + " in the doorway with the doors at " + lift.DoorOpen01.ToString("F2"));
                            if (Mathf.Abs(L.floors[floor].floorY - lift.CabinY) > 1e-4f) Problem(role + " in the doorway with the cabin elsewhere");
                            if (lift.CabinMoving) Problem(role + " in the doorway while the cabin moves");
                        }
                    }

                    // A rider leaves the cabin at the floor it was riding to, on foot (never by a fade).
                    if (_prev[r] == WalkerState.Riding && w.State != WalkerState.Riding && !_reset)
                    {
                        rideExits++;
                        int want = TargetFloor(role);
                        int got = FloorAt(p.y);
                        if (w.State != WalkerState.Walking && w.State != WalkerState.Dwelling) Problem(role + " left the cabin as " + w.State);
                        if (want >= 0 && got != want) Problem(role + " left the cabin at floor " + got + ", was riding to " + want);
                    }
                    _prev[r] = w.State;
                }
                if (riders > 1) Problem(riders + " riders in a one-person cabin");
                for (int a = 0; a < 4; a++)
                    for (int b = a + 1; b < 4; b++)
                    {
                        HabitatWalker wa = W((HabitatRole)a), wb = W((HabitatRole)b);
                        if (wa.Visibility < 1f || wb.Visibility < 1f) continue;
                        if (wa.State == WalkerState.Riding || wb.State == WalkerState.Riding) continue;
                        if (Mathf.Abs(wa.Position.y - wb.Position.y) > 0.05f) continue;
                        Vector3 d = wa.Position - wb.Position; d.y = 0f;
                        if (d.magnitude < 2f * L.agentRadius - 1e-3f)
                            Problem((HabitatRole)a + " and " + (HabitatRole)b + " overlap (" + d.magnitude.ToString("F4") + " m) at " + wa.Position.ToString("F3"));
                    }
            }

            public bool AllSettled()
            {
                for (int r = 0; r < 4; r++)
                    if (!W((HabitatRole)r).AtHome || stage.Running((HabitatRole)r) != null || stage.Pending((HabitatRole)r) > 0) return false;
                DispatchElevator d = stage.dispatch;
                return d.dispatcher.OpenRequests == 0 && d.PendingRequests == 0 && d.Ghosts == 0;
            }

            /// <summary>One random real-shaped event: every act kind the stage maps, for every role, including the Manager's
            /// own delivery and the acts that ride up to the Manager floor. A few are re-deliveries of the previous id.</summary>
            public void RandomEvent(System.Random rng, int serial)
            {
                string id = serial > 0 && rng.NextDouble() < 0.05 ? "ev" + (serial - 1) : "ev" + serial;
                var worker = (HabitatRole)rng.Next(0, 3);
                double u = rng.NextDouble();
                HabitatEvent e;
                if (u < 0.18)
                {
                    var other = (HabitatRole)((((int)worker) + 1 + rng.Next(0, 2)) % 3);
                    e = HabitatEvent.HandoffOf(id, worker, other, "t" + serial);                     // CarryToAgent (same floor)
                }
                else if (u < 0.33) e = HabitatEvent.HandoffOf(id, worker, HabitatRole.Manager, "t" + serial);   // CarryToManager (ride up)
                else if (u < 0.41) e = HabitatEvent.Of(id, HabitatEventKind.TaskCompleted, worker, "t" + serial);   // Researcher: ride up
                else if (u < 0.51) e = HabitatEvent.Of(id, HabitatEventKind.TaskFailed, worker, "t" + serial);      // Warning (ride up)
                else if (u < 0.64) e = HabitatEvent.Of(id, HabitatEventKind.ApprovalRequested, worker, "t" + serial); // Dispatch (same floor)
                else if (u < 0.82) e = HabitatEvent.Of(id, HabitatEventKind.CardPrinted, worker, "t" + serial);    // CollectCard (ride up)
                else e = HabitatEvent.Of(id, HabitatEventKind.SynthesisReady, HabitatRole.Manager, "t" + serial);  // DeliverReceipt (Manager rides down)
                Accept(e);
            }

            /// <summary>Random acts with exponential gaps of mean <paramref name="meanGap"/> s for <paramref name="seconds"/>,
            /// optionally a reset (recenter / mode exit) every <paramref name="resetEvery"/> s, then a drain until all are home.</summary>
            public bool Schedule(int seed, float seconds, float meanGap, float resetEvery = 0f)
            {
                var rng = new System.Random(seed);
                float next = 0f, nextReset = resetEvery > 0f ? resetEvery : float.MaxValue;
                int serial = 0;
                float end = now + seconds;
                while (now < end)
                {
                    if (now >= next)
                    {
                        RandomEvent(rng, serial++);
                        next = now + (float)(-Math.Log(1.0 - rng.NextDouble()) * meanGap);
                    }
                    if (now >= nextReset) { Reset(); nextReset += resetEvery; }
                    Step();
                }
                return Run(600f, AllSettled);
            }

            public string Summary()
            {
                var kinds = new List<string>();
                foreach (var kv in started) kinds.Add(kv.Key + "=" + kv.Value);
                int stale = 0; foreach (string why in dropped) if (why == StaleReasons[1]) stale++;
                return "accepted=" + accepted + " completed=" + stage.Completed + " dropped=" + stage.Dropped + " (too old " + stale + ")"
                       + " rideExits=" + rideExits + " started[" + string.Join(" ", kinds.ToArray()) + "] " + stage.dispatch;
            }

            public void AssertClean()
            {
                Assert.AreEqual(0, problemCount, "safety: " + string.Join(" | ", problems.ToArray()));
                Assert.AreEqual(0, softTransfers.Count, "faded home: " + string.Join(" | ", softTransfers.ToArray()));
                for (int r = 0; r < 4; r++) Assert.AreEqual(0, W((HabitatRole)r).SoftTransfers, (HabitatRole)r + ": " + W((HabitatRole)r).LastBlock);
                foreach (string why in dropped) Assert.IsTrue(Array.IndexOf(StaleReasons, why) >= 0, "dropped for an undocumented reason: " + why);
                // A ride fails only when a reset (recenter / mode exit) interrupts it: the ticket is let out at a landing.
                foreach (RideResult r in failedRides)
                    Assert.IsTrue(resets > 0 && r.failure == RideFailure.Interrupted, "a ride failed outside a reset: " + r);
                var seen = new HashSet<int>();
                foreach (int id in rideTickets) Assert.IsTrue(seen.Add(id), "ticket " + id + " completed twice");
            }
        }

        // ───────────────────────── stress ─────────────────────────

        [Test]
        public void Stress_RandomConcurrentActs_ThirtyMinutes_NobodyIsFadedHome_EveryRiderWalksOut(
            [Values(WallLayoutKind.Full, WallLayoutKind.Compact)] WallLayoutKind kind, [Values(11, 23, 37)] int seed)
        {
            var g = new Rig(kind);
            Assert.IsTrue(g.Schedule(seed, 30f * 60f, 6f), "everyone came home after the schedule: " + g.Summary());
            g.AssertClean();
            Assert.Greater(g.accepted, 200, g.Summary());
            Assert.Greater(g.rideExits, 40, "rides happened: " + g.Summary());
            foreach (HabitatActKind k in Enum.GetValues(typeof(HabitatActKind)))
            {
                int c; g.started.TryGetValue(k, out c);
                Assert.Greater(c, 0, "act kind " + k + " was exercised: " + g.Summary());
            }
            Assert.Greater(g.stage.Completed, 80, g.Summary());
        }

        [Test]
        public void Stress_DenseBursts_WithResets_NobodyIsFadedHome([Values(WallLayoutKind.Full, WallLayoutKind.Compact)] WallLayoutKind kind, [Values(5, 77)] int seed)
        {
            var g = new Rig(kind);
            // About twenty events a minute for ten minutes, with a recenter / mode exit every 97 s.
            Assert.IsTrue(g.Schedule(seed, 10f * 60f, 3f, 97f), "everyone came home after the schedule: " + g.Summary());
            g.AssertClean();
            Assert.Greater(g.resets, 4);
            Assert.Greater(g.rideExits, 10, g.Summary());
        }

        // ───────────────────────── focused: the owner's rc9 scenario ─────────────────────────

        [Test]
        public void OwnerScenario_RiderArrivesAtTheManagerFloor_WhileTheManagerHoldsTheCorridor_WaitsInTheOpenCabin_ThenWalksOut(
            [Values(WallLayoutKind.Full, WallLayoutKind.Compact)] WallLayoutKind kind)
        {
            var g = new Rig(kind);
            HabitatWalker researcher = g.W(HabitatRole.Researcher), manager = g.W(HabitatRole.Manager);
            Assert.IsTrue(g.Accept(HabitatEvent.HandoffOf("h1", HabitatRole.Researcher, HabitatRole.Manager, "t1")));
            // The rider is at the wait spot; the Manager steps out onto its own corridor and stands at the hand-off spot
            // (longer than the cabin's 8 s exit timeout), so the rider's whole exit segment is leased by someone else.
            Assert.IsTrue(g.Run(60f, () => researcher.State == WalkerState.WaitingElevator
                                          && g.L.nav.nodes[researcher.CurrentNode].kind == HabitatNodeKind.Queue), "rider at the wait spot");
            Assert.IsTrue(manager.GoTo(HabitatLayout.HandoffAtManager, 14f, true));
            Assert.IsTrue(g.Run(60f, () => researcher.State == WalkerState.Riding && !g.stage.lift.CabinMoving
                                          && g.stage.lift.CabinFloor == g.L.ManagerFloor && g.stage.lift.DoorOpen01 >= 1f), "rider at the Manager floor");
            Assert.AreEqual(WalkerState.Dwelling, manager.State, "the Manager stands on the corridor when the doors open");
            Assert.IsTrue(g.Run(240f, () => g.stage.Completed == 1 && g.AllSettled()), "the trip ended: " + g.Summary());
            g.AssertClean();
            CollectionAssert.AreEqual(new[] { "CarryToManager:Researcher" }, g.arrivals, "the rider reached the Manager on foot");
            Assert.Greater(g.cabinHoldTicks[(int)HabitatRole.Researcher], (int)(8f / Dt), "it waited in the open cabin past the old exit timeout");
            Assert.AreEqual(2, g.rideExits, "stepped out up top and back down");
            Assert.AreEqual(2, g.stage.dispatch.Arrived);
        }

        [Test]
        public void RiderComingBackDown_WhileAWorkerStandsByTheShaft_WaitsInTheOpenCabin_ThenWalksHome(
            [Values(WallLayoutKind.Full, WallLayoutKind.Compact)] WallLayoutKind kind)
        {
            var g = new Rig(kind);
            g.stage.settings.handoffDwell = 14f;            // the Developer stands at the Tester's door (next to the queue spot)
            bool sent = false;
            g.stage.ActArrived += a =>
            {
                if (a.actor != HabitatRole.Researcher || sent) return;
                sent = true;
                g.Accept(HabitatEvent.HandoffOf("h2", HabitatRole.Developer, HabitatRole.Tester, "t2"));
            };
            Assert.IsTrue(g.Accept(HabitatEvent.HandoffOf("h1", HabitatRole.Researcher, HabitatRole.Manager, "t1")));
            Assert.IsTrue(g.Run(300f, () => g.stage.Completed == 2 && g.AllSettled()), "both trips ended: " + g.Summary());
            g.AssertClean();
            Assert.IsTrue(sent);
            Assert.Greater(g.cabinHoldTicks[(int)HabitatRole.Researcher], (int)(8f / Dt), "the rider waited at the lower landing past the old exit timeout");
            Assert.AreEqual(2, g.rideExits);
        }

        [Test]
        public void TwoWorkersSwappingDoors_AtTheSameMoment_BothArrive_NoDeadlock([Values(WallLayoutKind.Full, WallLayoutKind.Compact)] WallLayoutKind kind)
        {
            var g = new Rig(kind);
            Assert.IsTrue(g.Accept(HabitatEvent.HandoffOf("a", HabitatRole.Developer, HabitatRole.Tester, "t1")));
            Assert.IsTrue(g.Accept(HabitatEvent.HandoffOf("b", HabitatRole.Tester, HabitatRole.Developer, "t2")));
            Assert.IsTrue(g.Accept(HabitatEvent.HandoffOf("c", HabitatRole.Researcher, HabitatRole.Tester, "t3")));
            Assert.IsTrue(g.Run(240f, () => g.stage.Completed == 3 && g.AllSettled()), g.Summary());
            g.AssertClean();
            Assert.AreEqual(3, g.arrivals.Count);
        }

        [Test]
        public void ManagerDeliveringDown_WhileWorkersCrowdTheLowerCorridor_StepsOutAndReachesDispatch(
            [Values(WallLayoutKind.Full, WallLayoutKind.Compact)] WallLayoutKind kind)
        {
            var g = new Rig(kind);
            Assert.IsTrue(g.Accept(HabitatEvent.Of("s1", HabitatEventKind.SynthesisReady, HabitatRole.Manager, "a1")));
            // While the Manager rides down, every worker heads for the dispatch spot or a neighbour's door, again and again.
            int n = 0;
            for (int i = 0; i < 6; i++)
            {
                g.Run(4f);
                g.Accept(HabitatEvent.Of("ap" + n++, HabitatEventKind.ApprovalRequested, (HabitatRole)(i % 3), "x" + n));
                g.Accept(HabitatEvent.HandoffOf("hd" + n++, (HabitatRole)((i + 1) % 3), (HabitatRole)((i + 2) % 3), "y" + n));
            }
            Assert.IsTrue(g.Run(400f, g.AllSettled), g.Summary());
            g.AssertClean();
            CollectionAssert.Contains(g.arrivals, "DeliverReceipt:Manager");
        }

        [Test]
        public void ALongButMovingTrip_IsNotCutShort_ByTheTripTimeout()
        {
            var g = new Rig(WallLayoutKind.Full);
            // The Developer blocks the corridor by the Tester's door for 40 s; the Tester's card pick-up must wait for it, then
            // ride up and back: a journey well past the scene's 45 s trip timeout, all of it visible waiting or walking.
            g.stage.settings.handoffDwell = 40f;
            Assert.IsTrue(g.Accept(HabitatEvent.HandoffOf("h", HabitatRole.Developer, HabitatRole.Tester, "t1")));
            g.Run(6f);
            Assert.IsTrue(g.Accept(HabitatEvent.Of("c", HabitatEventKind.CardPrinted, HabitatRole.Tester, "t2")));
            Assert.IsTrue(g.Run(300f, () => g.stage.Completed == 2 && g.AllSettled()), g.Summary());
            g.AssertClean();
            CollectionAssert.Contains(g.arrivals, "CollectCard:Tester");
        }
    }
}
#endif
