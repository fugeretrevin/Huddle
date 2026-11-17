// =============================================================================
// MuseDesk · EditMode tests · A9 adapter: PresentationMirror, PortalSpotAdapter, BackendLossDetector. Pure logic.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class WallPresentationBridgeTests
    {
        static SeatedUser User() { return new SeatedUser(new Vector3(0f, 1.2f, 0f), Vector3.forward); }

        sealed class Counter
        {
            public int home, closed; public PresentationCloseReason last;
            public Counter(PresentationLifecycle l) { l.ReturnHome += p => home++; l.Closed += c => { closed++; last = c.reason; }; }
        }

        [Test]
        public void Sync_OpensOnePerAgent_ClosesWhenFlowEnds_ExactlyOnce()
        {
            var m = new PresentationMirror(); var cv = new Counter(m.visit);
            m.Sync(true, "dev", Vector3.zero, false, null, Vector3.zero);
            Assert.AreEqual(PresentationPhase.Presenting, m.visit.Phase);
            m.Sync(true, "dev", Vector3.zero, false, null, Vector3.zero);   // idempotent
            Assert.AreEqual(1, m.visit.Session);
            m.Sync(false, null, Vector3.zero, false, null, Vector3.zero);
            m.Sync(false, null, Vector3.zero, false, null, Vector3.zero);
            Assert.AreEqual(1, cv.home); Assert.AreEqual(1, cv.closed);
            Assert.AreEqual(PresentationCloseReason.Finished, cv.last);
            Assert.AreEqual(0, m.map.Count);
        }

        [Test]
        public void Handover_VisitToPortal_SameAgent_NoRejection()
        {
            var m = new PresentationMirror();
            m.Sync(true, "dev", Vector3.zero, false, null, Vector3.zero);
            m.Sync(false, null, Vector3.zero, true, "dev", Vector3.zero);   // visit ended, portal began in the same frame
            Assert.AreEqual(PresentationPhase.Idle, m.visit.Phase);
            Assert.AreEqual(PresentationPhase.Presenting, m.portal.Phase);
            Assert.AreEqual(0, m.RejectedCount);
            Assert.IsTrue(m.map.IsConsistent());
        }

        [Test]
        public void SimultaneousVisitAndPortalOfOneAgent_SecondIsRejected()
        {
            var m = new PresentationMirror();
            m.Sync(true, "dev", Vector3.zero, true, "dev", Vector3.zero);
            Assert.AreEqual(1, m.RejectedCount);
            Assert.AreEqual(1, (m.visit.Phase != PresentationPhase.Idle ? 1 : 0) + (m.portal.Phase != PresentationPhase.Idle ? 1 : 0));
        }

        [Test]
        public void CancelAll_AtAnyTime_ReturnsHomeOnce_AndIsSafeWhenIdle()
        {
            var m = new PresentationMirror(); var cp = new Counter(m.portal);
            m.CancelAll();                                                  // idle: nothing
            Assert.AreEqual(0, cp.closed);
            m.Sync(false, null, Vector3.zero, true, "tester", Vector3.zero);
            m.CancelAll(); m.CancelAll();
            Assert.AreEqual(1, cp.home); Assert.AreEqual(1, cp.closed);
            Assert.AreEqual(PresentationCloseReason.Cancelled, cp.last);
            Assert.IsFalse(m.AnyOpen);
        }

        [Test]
        public void BackendLost_CleansUpImmediately_ExactlyOnce()
        {
            var m = new PresentationMirror(); var cv = new Counter(m.visit);
            m.Sync(true, "dev", Vector3.zero, false, null, Vector3.zero);
            m.BackendLost(); m.BackendLost();
            Assert.AreEqual(1, cv.home); Assert.AreEqual(1, cv.closed);
            Assert.AreEqual(PresentationCloseReason.BackendLost, cv.last);
            Assert.IsFalse(m.AnyOpen);
            Assert.AreEqual(0, m.map.Count);
        }

        [Test]
        public void LossDetector_OnlyLiveToNotLive()
        {
            var d = new BackendLossDetector();
            Assert.IsFalse(d.Update(false));      // offline demo from the start: never a loss
            Assert.IsFalse(d.Update(true));
            Assert.IsTrue(d.Update(false));
            Assert.IsFalse(d.Update(false));
        }

        [Test]
        public void Build_RectRoom_FlattensFloorCeilingAndFurniture()
        {
            var room = SyntheticRoomGeometry.Rectangle(0f, 0f, 6f, 5f, 0f, 2.6f);
            room.AddFurniture("TABLE", new Vector3(2f, 0.4f, 1f), new Vector3(1f, 0.8f, 0.5f), 90f);
            var b = new PortalRoomBounds();
            Assert.IsTrue(PortalSpotAdapter.Build(room, User(), b));
            Assert.IsTrue(b.hasFloor);
            Assert.AreEqual(2.6f, b.ceilingY, 1e-4f);
            Assert.AreEqual(1, b.obstacles.Count);
            // rotated 90 deg: half size swaps to (0.25, 0.5)
            Assert.AreEqual(0.25f, (b.obstacles[0].max.x - b.obstacles[0].min.x) * 0.5f, 1e-3f);
            // building again does not accumulate
            PortalSpotAdapter.Build(room, User(), b);
            Assert.AreEqual(1, b.obstacles.Count);
        }

        [Test]
        public void Build_NonRectangularFloor_IsPlannedExactly_SoTheKeepOutPlannerCoversEveryRoom()
        {
            // P0 keep-out: an L-shaped floor used to fall back to the legacy evaluator, which ignores screens and the desk
            // front. The outline is now planned exactly (PortalKeepOutTests.LShapedFloor_PlannedExactly_NotchRefused).
            var room = new SyntheticRoomGeometry();
            room.AddFloor(new[] { new Vector2(-3, -3), new Vector2(3, -3), new Vector2(3, 0), new Vector2(0, 0), new Vector2(0, 3), new Vector2(-3, 3) }, 0f);   // L shape
            var b = new PortalRoomBounds();
            Assert.IsTrue(PortalSpotAdapter.Build(room, new SeatedUser(new Vector3(-1.5f, 1.2f, -1.5f), Vector3.forward), b));
            Assert.IsTrue(b.hasFloor);
            Assert.IsNotNull(b.floorPolygon);
            Assert.AreEqual(6, b.floorPolygon.Length);
        }

        [Test]
        public void Build_NoRoom_False()
        {
            var b = new PortalRoomBounds();
            Assert.IsFalse(PortalSpotAdapter.Build(null, User(), b));
        }

        [Test]
        public void ListSpots_AreValid_SideOnly_AndSortedBest()
        {
            var room = SyntheticRoomGeometry.Rectangle(0f, 0f, 6f, 6f, 0f, 2.6f);
            var b = new PortalRoomBounds();
            var u = User();
            PortalSpotAdapter.Build(room, u, b);
            var list = PortalSpotAdapter.ListSpots(u, b, new PortalSpotSettings());
            Assert.Greater(list.Count, 0);
            for (int i = 0; i < list.Count; i++)
            {
                Assert.IsTrue(list[i].valid);
                Vector3 d = list[i].position - u.head; d.y = 0f;
                Assert.GreaterOrEqual(Vector3.Angle(u.forward, d), 45f - 0.01f);   // never in front of the face
                if (i > 0) Assert.LessOrEqual(list[i].score, list[i - 1].score + 1e-6f);
            }
        }

        [Test]
        public void TinyRoom_ListEmpty_FallbackIsFlaggedNotValid_AndCandidateCannotConfirm()
        {
            var room = SyntheticRoomGeometry.Rectangle(0f, 0f, 1.5f, 1.5f, 0f, 2.6f);
            var b = new PortalRoomBounds(); var u = User();
            Assert.IsTrue(PortalSpotAdapter.Build(room, u, b));
            var s = new PortalSpotSettings();
            Assert.AreEqual(0, PortalSpotAdapter.ListSpots(u, b, s).Count);
            PortalSpot fb = PortalSpotPlanner.Plan(u, b, s);
            Assert.IsTrue(fb.isFallback); Assert.IsFalse(fb.valid);
            Assert.IsFalse(PortalSpotAdapter.ToCandidate(fb, true, 1f).valid);
        }

        [Test]
        public void ToCandidate_ValidSpot_IsValid_WithProblemNone()
        {
            var spot = new PortalSpot { position = new Vector3(1, 0, 0), rotation = Quaternion.identity, valid = true, reason = "Clear space beside you", score = 0.8f };
            PortalCandidate c = PortalSpotAdapter.ToCandidate(spot, true, 1.1f);
            Assert.IsTrue(c.valid); Assert.AreEqual(PortalProblem.None, c.problem); Assert.AreEqual(1.1f, c.avatarHeight, 1e-5f);
        }

        [Test]
        public void FitHeight_ClampsUnderCeiling_AndIgnoresUnknownCeiling()
        {
            Assert.AreEqual(1.2f, PortalSpotAdapter.FitHeight(1.2f, 0f, 2.6f, 0.12f, null), 1e-4f);
            Assert.AreEqual(0.88f, PortalSpotAdapter.FitHeight(1.2f, 0f, 1.0f, 0.12f, null), 1e-4f);
            Assert.AreEqual(1.2f, PortalSpotAdapter.FitHeight(1.2f, 0f, float.NegativeInfinity, 0.12f, null), 1e-4f);
        }

        [Test]
        public void Tracker_WithBuiltBounds_StaysStill_WhenUserStill()
        {
            var room = SyntheticRoomGeometry.Rectangle(0f, 0f, 6f, 6f, 0f, 2.6f);
            var b = new PortalRoomBounds(); var u = User();
            PortalSpotAdapter.Build(room, u, b);
            var t = new PortalSpotTracker(new PortalSpotSettings());
            PortalSpot a = t.Update(0f, u, b);
            for (int i = 1; i < 20; i++) t.Update(i * 0.2f, u, b);
            Assert.AreEqual(a.position, t.Current.position);
        }

        [Test]
        public void Tether_FadesWithPhase()
        {
            var open = PresentationMirror.TetherFor(PresentationPhase.Opening, 0.5f, Vector3.zero, new Vector3(0, 0, 1));
            var pres = PresentationMirror.TetherFor(PresentationPhase.Presenting, 1f, Vector3.zero, new Vector3(0, 0, 1));
            var idle = PresentationMirror.TetherFor(PresentationPhase.Idle, 0f, Vector3.zero, new Vector3(0, 0, 1));
            Assert.AreEqual(0.5f, open.alpha, 1e-5f); Assert.IsTrue(pres.active); Assert.IsFalse(idle.active);
        }
    }
}
#endif
