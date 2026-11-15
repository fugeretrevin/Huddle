// =============================================================================
// MuseDesk · EditMode tests · Sprint A9: portal spot planner (+ hysteresis), large-avatar scale, and the
// presentation lifecycle shared by desk visit and portal. Pure logic. NOT compiled or run when written.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class PortalVisitLifecycleTests
    {
        // ── helpers ──
        static SeatedUser User() { return new SeatedUser(new Vector3(0f, 1.2f, 0f), Vector3.forward); }

        static PortalRoomBounds Room()
        {
            var r = new PortalRoomBounds { hasFloor = true, floorY = 0f, ceilingY = 2.6f, floor = new PortalBox(new Vector2(-3f, -2.5f), new Vector2(3f, 3f)) };
            r.keepOut.Add(PortalBox.FromCenter(new Vector2(0f, 0.55f), new Vector2(0.6f, 0.3f)));   // the desk + screen in front
            return r;
        }

        static float AngleFromForward(SeatedUser u, Vector3 p)
        {
            Vector3 d = p - u.head; d.y = 0f;
            return Vector3.Angle(u.forward, d);
        }

        // ── 1. spot planner ──

        [Test]
        public void Plan_PicksSideSpot_NotInFrontCone_NotOverKeepOut()
        {
            var u = User(); var room = Room(); var s = new PortalSpotSettings();
            PortalSpot spot = PortalSpotPlanner.Plan(u, room, s);
            Assert.IsTrue(spot.valid);
            Assert.IsFalse(spot.isFallback);
            Assert.GreaterOrEqual(AngleFromForward(u, spot.position), s.frontConeDeg);
            Assert.LessOrEqual(AngleFromForward(u, spot.position), s.maxSideDeg);
            var xz = new Vector2(spot.position.x, spot.position.z);
            Assert.GreaterOrEqual(room.keepOut[0].DistanceTo(xz) - s.bodyRadius, s.keepOutMargin);
            Assert.AreEqual(room.floorY, spot.position.y, 1e-4f, "on the floor plane");
        }

        [Test]
        public void Evaluate_RejectsInFrontOfUser()
        {
            var room = Room(); room.keepOut.Clear();
            PortalSpot c = PortalSpotPlanner.Evaluate(User(), room, new PortalSpotSettings(), new Vector2(0f, 1.4f));
            Assert.IsFalse(c.valid);
            StringAssert.Contains("front", c.reason);
        }

        [Test]
        public void Evaluate_RejectsKeepOutZone_AndObstacle_AndWall_AndUserEnvelope()
        {
            var u = User(); var s = new PortalSpotSettings();
            var room = Room();
            var side = new Vector2(1.4f, 0f);                      // 90 degrees right
            Assert.IsTrue(PortalSpotPlanner.Evaluate(u, room, s, side).valid, "baseline clear");

            room.keepOut.Add(PortalBox.FromCenter(side, new Vector2(0.2f, 0.2f)));
            PortalSpot k = PortalSpotPlanner.Evaluate(u, room, s, side);
            Assert.IsFalse(k.valid); StringAssert.Contains("screen", k.reason);

            room = Room();
            room.obstacles.Add(PortalBox.FromCenter(side + new Vector2(0.3f, 0f), new Vector2(0.2f, 0.2f)));
            Assert.IsFalse(PortalSpotPlanner.Evaluate(u, room, s, side).valid, "furniture margin is conservative");

            room = Room();
            Assert.IsFalse(PortalSpotPlanner.Evaluate(u, room, s, new Vector2(2.8f, 0f)).valid, "wall margin");
            Assert.IsFalse(PortalSpotPlanner.Evaluate(u, room, s, new Vector2(0.5f, 0f)).valid, "inside the comfort envelope");
        }

        [Test]
        public void Plan_ValidSpot_BodyClearsUserEnvelope()
        {
            var u = User(); var s = new PortalSpotSettings();
            PortalSpot spot = PortalSpotPlanner.Plan(u, Room(), s);
            Assert.IsTrue(PortalAvatarScale.ClearsUserEnvelope(spot.position, s.bodyRadius, u, s.personalRadius));
        }

        [Test]
        public void Plan_NoClearArea_ReturnsStableFlaggedFallback_NotInFront()
        {
            var u = User(); var s = new PortalSpotSettings();
            var tiny = new PortalRoomBounds { hasFloor = true, floorY = 0f, floor = new PortalBox(new Vector2(-0.8f, -0.8f), new Vector2(0.8f, 0.8f)) };
            PortalSpot a = PortalSpotPlanner.Plan(u, tiny, s);
            Assert.IsTrue(a.isFallback); Assert.IsFalse(a.valid);
            StringAssert.Contains("No clear space", a.reason);
            Assert.GreaterOrEqual(AngleFromForward(u, a.position), s.frontConeDeg);

            tiny.obstacles.Add(PortalBox.FromCenter(new Vector2(0.3f, 0.3f), new Vector2(0.1f, 0.1f)));
            tiny.revision++;
            PortalSpot b = PortalSpotPlanner.Plan(u, tiny, s);
            Assert.AreEqual(a.position, b.position, "fallback depends only on the user pose: no jitter");
        }

        [Test]
        public void Plan_NoFloorData_FallbackSaysUnchecked()
        {
            PortalSpot f = PortalSpotPlanner.Plan(User(), new PortalRoomBounds(), new PortalSpotSettings());
            Assert.IsTrue(f.isFallback); Assert.IsFalse(f.valid);
            StringAssert.Contains("unchecked", f.reason);
            f = PortalSpotPlanner.Plan(User(), null, new PortalSpotSettings());
            Assert.IsTrue(f.isFallback);
        }

        [Test]
        public void Tracker_DoesNotReevaluateEveryFrame()
        {
            var t = new PortalSpotTracker(new PortalSpotSettings());
            var u = User(); var room = Room();
            PortalSpot first = t.Update(0f, u, room);
            for (int i = 1; i <= 300; i++) t.Update(i * 0.016f, u, room);   // ~5 s, user still
            Assert.AreEqual(1, t.EvaluationCount);
            Assert.AreEqual(first.position, t.Current.position);
        }

        [Test]
        public void Tracker_SmallJitter_KeepsSpot_BigMove_Reevaluates()
        {
            var s = new PortalSpotSettings();
            var t = new PortalSpotTracker(s);
            var room = Room();
            PortalSpot first = t.Update(0f, User(), room);
            var nudged = new SeatedUser(new Vector3(0.05f, 1.2f, 0.02f), Vector3.forward);
            t.Update(2f, nudged, room);
            Assert.AreEqual(1, t.EvaluationCount, "below the move threshold");
            Assert.AreEqual(first.position, t.Current.position);

            var moved = new SeatedUser(new Vector3(0.2f, 1.2f, -0.8f), Vector3.forward);
            t.Update(4f, moved, room);
            Assert.AreEqual(2, t.EvaluationCount);
            Assert.IsTrue(t.Current.valid);
        }

        [Test]
        public void Tracker_CurrentSpotBecomesBlocked_IsReplaced()
        {
            var t = new PortalSpotTracker(new PortalSpotSettings());
            var u = User(); var room = Room();
            PortalSpot first = t.Update(0f, u, room);
            room.obstacles.Add(PortalBox.FromCenter(new Vector2(first.position.x, first.position.z), new Vector2(0.3f, 0.3f)));
            room.revision++;
            PortalSpot next = t.Update(2f, u, room);
            Assert.IsTrue(next.valid);
            Assert.AreNotEqual(first.position, next.position);
        }

        [Test]
        public void Tracker_FallbackBecomesRealSpot_WhenRoomOpensUp()
        {
            var t = new PortalSpotTracker(new PortalSpotSettings());
            var u = User();
            var tiny = new PortalRoomBounds { hasFloor = true, floorY = 0f, floor = new PortalBox(new Vector2(-0.8f, -0.8f), new Vector2(0.8f, 0.8f)) };
            Assert.IsTrue(t.Update(0f, u, tiny).isFallback);
            var big = Room(); big.revision = 5;
            PortalSpot next = t.Update(2f, u, big);
            Assert.IsTrue(next.valid); Assert.IsFalse(next.isFallback);
        }

        // ── 2. large avatar scale ──

        [Test]
        public void Scale_FeetOnFloor_ForAnyScale()
        {
            var s = new PortalScaleSettings();
            const float footOffset = 0.05f;   // root (hip) above the feet at scale 1
            PortalAvatarFit fit = PortalAvatarScale.Compute(0.1f, footOffset, 0.03f, 1.0f, 0.2f, 0f, s);
            Assert.AreEqual(10f, fit.scale, 1e-4f);
            Assert.AreEqual(0.2f, fit.rootY - footOffset * fit.scale, 1e-4f, "feet exactly on the floor plane");
            Assert.AreEqual(1.0f, fit.height, 1e-4f);
            Assert.AreEqual(0.3f, fit.bodyRadius, 1e-4f);
            Assert.IsFalse(fit.clamped);
        }

        [Test]
        public void Scale_ClampedByCeilingAndRange()
        {
            var s = new PortalScaleSettings();
            PortalAvatarFit low = PortalAvatarScale.Compute(0.1f, 0f, 0.03f, 1.2f, 0f, 1.0f, s);
            Assert.IsTrue(low.clamped);
            Assert.AreEqual((1.0f - s.headRoom) / 0.1f, low.scale, 1e-3f);
            PortalAvatarFit big = PortalAvatarScale.Compute(0.1f, 0f, 0.03f, 9f, 0f, 0f, s);
            Assert.AreEqual(s.maxScale, big.scale, 1e-4f);
            PortalAvatarFit small = PortalAvatarScale.Compute(0.1f, 0f, 0.03f, 0.1f, 0f, 0f, s);
            Assert.AreEqual(s.minScale, small.scale, 1e-4f);
        }

        [Test]
        public void Scale_Step_NoOvershoot_AndFeetStayOnFloorWhileGrowing()
        {
            var s = new PortalScaleSettings();
            float cur = 1f;
            for (int i = 0; i < 200; i++)
            {
                cur = PortalAvatarScale.Step(cur, 10f, 0.016f, s);
                Assert.LessOrEqual(cur, 10f);
                Assert.AreEqual(0f, PortalAvatarScale.RootYFor(cur, 0.05f, 0f) - 0.05f * cur, 1e-4f);
            }
            Assert.AreEqual(10f, cur, 1e-4f);
        }

        // ── 3. lifecycle ──

        class Rec
        {
            public readonly List<string> log = new List<string>();
            public int closed;
            public PresentationClosed last;
            public Rec(PresentationLifecycle l)
            {
                l.Opened += (p, a) => log.Add("opened:" + p);
                l.ReturnHome += p => log.Add("home:" + p);
                l.Closed += c => { closed++; last = c; log.Add("closed:" + c.reason); };
            }
        }

        static PresentationLifecycle Make(PresentationKind k, PresentationIdentityMap map) { return new PresentationLifecycle(k, map); }

        static void Run(PresentationLifecycle l, float seconds) { for (float t = 0; t < seconds; t += 0.02f) l.Tick(0.02f); }

        [Test]
        public void Lifecycle_FullCycle_OrderAndExactlyOnce()
        {
            var l = Make(PresentationKind.Portal, null); var r = new Rec(l);
            Assert.IsTrue(l.IsHome);
            Assert.AreEqual(PresentationOpenResult.Started, l.Open("p1", "developer", Vector3.zero));
            Assert.AreEqual(PresentationPhase.Opening, l.Phase);
            Run(l, 1f);
            Assert.AreEqual(PresentationPhase.Presenting, l.Phase);
            l.RequestClose();
            Assert.AreEqual(PresentationPhase.Closing, l.Phase);
            Run(l, 1f);
            Assert.AreEqual(PresentationPhase.Idle, l.Phase);
            Assert.AreEqual(1, r.closed);
            CollectionAssert.AreEqual(new[] { "opened:p1", "home:p1", "closed:Finished" }, r.log);
            Run(l, 2f);
            Assert.AreEqual(1, r.closed, "Closed never repeats");
        }

        [Test]
        public void Lifecycle_Cancel_AtEveryPoint_ClosesOnce_AndReturnsHome()
        {
            // during Opening
            var l = Make(PresentationKind.DeskVisit, null); var r = new Rec(l);
            l.Open("p", "tester", Vector3.zero);
            l.Cancel();
            Assert.AreEqual(PresentationPhase.Closing, l.Phase);
            l.Cancel();                                          // again while closing: no-op
            Run(l, 1f);
            Assert.AreEqual(1, r.closed); Assert.AreEqual(PresentationCloseReason.Cancelled, r.last.reason); Assert.IsTrue(l.IsHome);

            // during Presenting
            l = Make(PresentationKind.DeskVisit, null); r = new Rec(l);
            l.Open("p", "tester", Vector3.zero); l.MarkReady();
            Assert.AreEqual(PresentationPhase.Presenting, l.Phase);
            l.Cancel(); Run(l, 1f);
            Assert.AreEqual(1, r.closed);

            // while Idle: nothing
            l = Make(PresentationKind.DeskVisit, null); r = new Rec(l);
            l.Cancel(); Run(l, 1f);
            Assert.AreEqual(0, r.closed);
        }

        [Test]
        public void Lifecycle_BackendLost_CleansUpImmediately_ExactlyOnce()
        {
            var map = new PresentationIdentityMap();
            var l = Make(PresentationKind.Portal, map); var r = new Rec(l);
            l.Open("p", "researcher", Vector3.zero); l.MarkReady();
            l.BackendLost();
            Assert.AreEqual(PresentationPhase.Idle, l.Phase);
            Assert.AreEqual(1, r.closed);
            Assert.AreEqual(PresentationCloseReason.BackendLost, r.last.reason);
            Assert.AreEqual(0, map.Count, "identity released");
            l.BackendLost(); l.Tick(5f);
            Assert.AreEqual(1, r.closed);

            // also mid-close
            l.Open("p2", "researcher", Vector3.zero); l.MarkReady(); l.RequestClose();
            l.BackendLost();
            Assert.AreEqual(2, r.closed);
            Run(l, 2f);
            Assert.AreEqual(2, r.closed, "the close timer cannot raise a second Closed");
        }

        [Test]
        public void Lifecycle_OpenWhileClosing_Queues_AndStartsAfterClose()
        {
            var l = Make(PresentationKind.Portal, null); var r = new Rec(l);
            l.Open("p1", "developer", Vector3.zero); l.MarkReady(); l.RequestClose();
            Assert.AreEqual(PresentationOpenResult.QueuedUntilClosed, l.Open("p2", "tester", new Vector3(1, 0, 0)));
            Assert.AreEqual(PresentationOpenResult.QueuedUntilClosed, l.Open("p3", "researcher", new Vector3(2, 0, 0)), "latest wins");
            Run(l, 1f);
            Assert.AreEqual(1, r.closed);
            Assert.AreEqual(PresentationPhase.Opening, l.Phase);
            Assert.AreEqual("p3", l.PresentationId);
            Assert.AreEqual(new Vector3(2, 0, 0), l.HomeAnchor);
            Assert.AreEqual(2, l.Session);
        }

        [Test]
        public void Lifecycle_Cancel_WithdrawsQueuedOpen()
        {
            var l = Make(PresentationKind.Portal, null); var r = new Rec(l);
            l.Open("p1", "developer", Vector3.zero); l.MarkReady(); l.RequestClose();
            l.Open("p2", "tester", Vector3.zero);
            l.Cancel();
            Assert.IsFalse(l.HasPendingOpen);
            Run(l, 1f);
            Assert.IsTrue(l.IsHome);
        }

        [Test]
        public void Lifecycle_ReopenFromClosedHandler_WinsOverQueue()
        {
            var l = Make(PresentationKind.Portal, null);
            int started = 0;
            l.Closed += c => { if (c.presentationId == "a") { if (l.Open("b", "tester", Vector3.zero) == PresentationOpenResult.Started) started++; } };
            l.Open("a", "developer", Vector3.zero); l.MarkReady(); l.RequestClose();
            l.Open("c", "researcher", Vector3.zero);       // queued
            Run(l, 1f);
            Assert.AreEqual(1, started);
            Assert.AreEqual("b", l.PresentationId, "a live presentation is never overwritten by the queue");
        }

        [Test]
        public void Lifecycle_SecondOpen_WhilePresenting_IsRefusedOrIdempotent()
        {
            var l = Make(PresentationKind.Portal, null);
            l.Open("p1", "developer", Vector3.zero); l.MarkReady();
            Assert.AreEqual(PresentationOpenResult.AlreadyOpen, l.Open("p1", "developer", Vector3.zero));
            Assert.AreEqual(PresentationOpenResult.RejectedBusy, l.Open("p2", "tester", Vector3.zero));
            Assert.AreEqual(PresentationOpenResult.RejectedInvalid, l.Open("", "tester", Vector3.zero));
            Assert.AreEqual("p1", l.PresentationId);
        }

        [Test]
        public void Identity_OnePresentationPerLogicalAgent_AcrossVisitAndPortal()
        {
            var map = new PresentationIdentityMap();
            var visit = Make(PresentationKind.DeskVisit, map);
            var portal = Make(PresentationKind.Portal, map);
            Assert.AreEqual(PresentationOpenResult.Started, visit.Open("visit-dev", "developer", Vector3.zero));
            Assert.AreEqual(PresentationOpenResult.RejectedBusy, portal.Open("portal-dev", "developer", Vector3.zero), "no second presentation of the same agent");
            Assert.AreEqual("developer", map.LogicalAgentOf("visit-dev"));
            Assert.AreEqual("visit-dev", map.PresentationOf("developer"));
            Assert.IsNull(map.LogicalAgentOf("portal-dev"));
            Assert.AreEqual(PresentationOpenResult.Started, portal.Open("portal-tester", "tester", Vector3.zero), "another agent is a different logical identity");
            Assert.IsTrue(map.IsConsistent());
            visit.BackendLost();
            Assert.IsNull(map.PresentationOf("developer"), "released after the visit ended");
            Assert.AreEqual("portal-tester", map.PresentationOf("tester"));
            Assert.IsTrue(map.IsConsistent());
        }

        [Test]
        public void IdentityMap_RegisterRules()
        {
            var map = new PresentationIdentityMap(); string why;
            Assert.IsTrue(map.TryRegister("p", "a", out why));
            Assert.IsTrue(map.TryRegister("p", "a", out why), "idempotent");
            Assert.IsFalse(map.TryRegister("p", "b", out why), "id bound to another agent");
            Assert.IsFalse(map.TryRegister("q", "a", out why), "agent already has a presentation");
            Assert.IsFalse(map.TryRegister("", "a", out why));
            map.Release("p");
            Assert.IsTrue(map.TryRegister("q", "a", out why));
            Assert.AreEqual(1, map.Count);
            Assert.IsTrue(map.IsConsistent());
        }

        [Test]
        public void Lifecycle_ComfortMode_ShortensAnimation()
        {
            var l = Make(PresentationKind.Portal, null); l.comfortMode = true;
            l.Open("p", "developer", Vector3.zero);
            Run(l, l.timings.opening * l.timings.comfortScale + 0.1f);
            Assert.AreEqual(PresentationPhase.Presenting, l.Phase);
        }

        // ── 4. tether ──

        [Test]
        public void Tether_ActiveWhilePresenting_FadesInAndOut_InactiveAtHome()
        {
            var home = new Vector3(0f, 0.8f, 0.5f); var away = new Vector3(1.5f, 1.0f, 0f);
            var l = Make(PresentationKind.Portal, null);
            Assert.IsFalse(l.Tether(away).active);
            l.Open("p", "developer", home);
            l.Tick(l.timings.opening * 0.5f);
            PresentationTether t = l.Tether(away);
            Assert.IsTrue(t.active);
            Assert.AreEqual(home, t.from); Assert.AreEqual(away, t.to);
            Assert.Greater(t.alpha, 0.2f); Assert.Less(t.alpha, 0.8f);
            l.MarkReady();
            Assert.AreEqual(1f, l.Tether(away).alpha, 1e-4f);
            l.RequestClose();
            l.Tick(l.timings.closing * 0.5f);
            Assert.Less(l.Tether(away).alpha, 0.8f);
            Run(l, 2f);
            Assert.IsFalse(l.Tether(away).active);
        }

        [Test]
        public void Tether_ZeroLength_IsInactive()
        {
            PresentationTether t = PresentationTether.Compute(PresentationPhase.Presenting, 1f, Vector3.one, Vector3.one);
            Assert.IsFalse(t.active);
        }
    }
}
#endif
