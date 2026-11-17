// =============================================================================
// MuseDesk · EditMode tests · P0 portal comfort keep-out: seated user head/torso/hand zones, default forward work area,
// MRUK-labelled screen / desk-front / door volumes, swept appearance and transition, candidate filtering (view, controls,
// hands busy), the one fallback, re-evaluation and clean closing mid-interaction (PortalComfortGuard). Pure logic.
// Written without the Unity Editor: NOT run when written.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class PortalKeepOutTests
    {
        // ── helpers ──
        // Seated user at the origin facing +Z (user's right = +X in world here), floor at 0.
        static SeatedUser User() { return new SeatedUser(new Vector3(0f, 1.2f, 0f), Vector3.forward); }

        // The controller's settings: footprint 0.30, the large avatar 1.2 m tall.
        static PortalSpotSettings S() { return new PortalSpotSettings { bodyRadius = 0.30f, avatarHeight = 1.2f }; }

        static SyntheticRoomGeometry Room6() { return SyntheticRoomGeometry.Rectangle(0f, 0f, 6f, 6f, 0f, 2.6f); }

        static PortalRoomBounds Bounds(IRoomGeometry room, IWallGeometry walls = null, SeatedUser? user = null)
        {
            var b = new PortalRoomBounds();
            PortalSpotAdapter.Build(room, walls, user ?? User(), b, null);
            return b;
        }

        static float Angle(SeatedUser u, Vector3 p) { Vector3 d = p - u.head; d.y = 0f; return Vector3.Angle(u.forward, d); }

        static Vector2 XZ(Vector3 v) { return new Vector2(v.x, v.z); }

        /// <summary>Every recorded pose of the appearance sweep keeps every keep-out (asserts on the poses, not the centre).</summary>
        static void AssertSweepPosesClear(SeatedUser u, PortalRoomBounds b, PortalSpotSettings s, Vector2 at)
        {
            var poses = new List<Vector4>();
            float clearance; string why;
            Assert.IsTrue(PortalSpotPlanner.SweepClear(u, b, s, at, s.comfort.emergeGrow, at, 1f, out clearance, out why, poses), why);
            Assert.Greater(poses.Count, 2, "the appearance is sampled, not just the end pose");
            float envelope = SeatedUserKeepOut.HeadTorsoEnvelope(s.comfort);
            PortalKeepOutVolume hands = SeatedUserKeepOut.HandZone(u, s.comfort), work = SeatedUserKeepOut.WorkArea(u, s.comfort);
            for (int i = 0; i < poses.Count; i++)
            {
                Vector2 c = new Vector2(poses[i].x, poses[i].y); float r = poses[i].z;
                if (i > 0) Assert.GreaterOrEqual(r, poses[i - 1].z - 1e-5f, "grows monotonically");
                Assert.IsTrue(PortalAvatarScale.ClearsUserEnvelope(new Vector3(c.x, 0f, c.y), r, u, envelope), "pose " + i + " in the head/torso envelope");
                Assert.GreaterOrEqual(hands.footprint.DistanceToPoint(c) - r, hands.margin - 1e-4f, "pose " + i + " in the hand zone");
                Assert.GreaterOrEqual(work.footprint.DistanceToPoint(c) - r, work.margin - 1e-4f, "pose " + i + " in the forward work area");
                for (int k = 0; k < b.volumes.Count; k++)
                    Assert.GreaterOrEqual(b.volumes[k].footprint.DistanceToPoint(c) - r, b.volumes[k].margin - 1e-4f, "pose " + i + " in " + b.volumes[k].kind);
            }
        }

        // ── user keep-out model ──

        [Test]
        public void UserZones_FollowTheSeatedFrame_AndCoverHandsAndForwardWork()
        {
            var c = new SeatedComfortSettings();
            var u = User();
            PortalKeepOutVolume hands = SeatedUserKeepOut.HandZone(u, c);
            Assert.IsTrue(hands.footprint.Contains(new Vector2(0.6f, 0.4f)), "right hand on the desk");
            Assert.IsTrue(hands.footprint.Contains(new Vector2(-0.6f, -0.1f)), "left elbow");
            Assert.IsFalse(hands.footprint.Contains(new Vector2(1.0f, 0f)));
            PortalKeepOutVolume work = SeatedUserKeepOut.WorkArea(u, c);
            Assert.IsTrue(work.footprint.Contains(new Vector2(0f, 1.2f)), "the monitor in front");
            Assert.IsTrue(float.IsInfinity(work.maxY), "unknown monitors: any height");

            var turned = new SeatedUser(u.head, Vector3.right);       // facing +X
            Assert.IsTrue(SeatedUserKeepOut.WorkArea(turned, c).footprint.Contains(new Vector2(1.2f, 0f)));
            Assert.IsFalse(SeatedUserKeepOut.WorkArea(turned, c).footprint.Contains(new Vector2(0f, 1.2f)));
        }

        // ── screen in front / beside ──

        [Test]
        public void ScreenInFront_SideSpotStillFound_AndClearsScreenAndSightline()
        {
            var room = Room6();
            room.AddFurniture("SCREEN", new Vector3(0f, 1.1f, 0.9f), new Vector3(0.6f, 0.4f, 0.1f));
            var b = Bounds(room); var u = User(); var s = S();
            Assert.AreEqual(1, b.screensFromScan);
            Assert.IsTrue(b.volumes.Exists(v => v.kind == PortalKeepOutKind.Screen));
            Assert.IsTrue(b.volumes.Exists(v => v.kind == PortalKeepOutKind.ScreenSightline));

            PortalSpot spot = PortalSpotPlanner.Plan(u, b, s);
            Assert.IsTrue(spot.valid, spot.reason);
            Assert.GreaterOrEqual(Angle(u, spot.position), s.frontConeDeg);
            Assert.LessOrEqual(Angle(u, spot.position), s.comfort.maxViewDeg + 1e-3f);
            AssertSweepPosesClear(u, b, s, XZ(spot.position));
        }

        [Test]
        public void ScreenBesideUser_RightSideRefused_SpotGoesLeft()
        {
            var room = Room6();
            room.AddFurniture("SCREEN", new Vector3(1.3f, 1.1f, 0.2f), new Vector3(0.1f, 0.4f, 0.6f));   // a second monitor on the right
            var b = Bounds(room); var u = User(); var s = S();
            PortalSpot right = PortalSpotPlanner.Evaluate(u, b, s, new Vector2(1.35f, 0f));
            Assert.IsFalse(right.valid);
            StringAssert.Contains("screen", right.reason);
            foreach (PortalSpot p in PortalSpotAdapter.ListSpots(u, b, s)) Assert.Less(p.position.x, 0f, "no valid spot on the screen's side");
            PortalSpot best = PortalSpotPlanner.Plan(u, b, s);
            Assert.IsTrue(best.valid, best.reason);
            Assert.Less(best.position.x, 0f);
        }

        // ── desk edge ──

        [Test]
        public void DeskNearUser_GetsFrontZoneFacingTheUser_AndSpotsClearIt()
        {
            var room = Room6();
            room.AddFurniture("TABLE", new Vector3(0f, 0.375f, 0.75f), new Vector3(1.4f, 0.75f, 0.7f));
            var b = Bounds(room); var u = User(); var s = S();
            Assert.AreEqual(1, b.desksFromScan);
            PortalKeepOutVolume front = b.volumes.Find(v => v.kind == PortalKeepOutKind.DeskFront);
            Assert.AreEqual(-1f, front.footprint.axisY.y, 1e-4f, "the zone faces the user (toward -Z)");
            Assert.IsTrue(front.footprint.Contains(new Vector2(0f, 0.1f)), "between the desk edge and the user");
            Assert.IsTrue(front.footprint.Contains(new Vector2(0.65f, 0.3f)));
            Assert.IsFalse(front.footprint.Contains(new Vector2(0f, 0.8f)), "not the desk top itself (that is an obstacle)");

            PortalSpot spot = PortalSpotPlanner.Plan(u, b, s);
            Assert.IsTrue(spot.valid, spot.reason);
            AssertSweepPosesClear(u, b, s, XZ(spot.position));
            foreach (PortalBox o in b.obstacles) Assert.GreaterOrEqual(o.DistanceTo(XZ(spot.position)) - s.bodyRadius, s.obstacleMargin);
        }

        [Test]
        public void LongDeskEdge_SpotAtTheDeskEnd_Refused_WithDeskReason()
        {
            var room = Room6();
            room.AddFurniture("TABLE", new Vector3(0f, 0.375f, 0.75f), new Vector3(2.4f, 0.75f, 0.7f));
            var b = Bounds(room); var u = User(); var s = S();
            PortalSpot atEnd = PortalSpotPlanner.Evaluate(u, b, s, new Vector2(1.35f, 0f));
            Assert.IsFalse(atEnd.valid);
            StringAssert.Contains("desk", atEnd.reason);
            foreach (PortalSpot p in PortalSpotAdapter.ListSpots(u, b, s)) AssertSweepPosesClear(u, b, s, XZ(p.position));
        }

        [Test]
        public void FarTable_IsAnObstacle_NotYourDesk()
        {
            var room = Room6();
            room.AddFurniture("TABLE", new Vector3(2.2f, 0.4f, 2.2f), new Vector3(0.8f, 0.8f, 0.8f));
            var b = Bounds(room);
            Assert.AreEqual(0, b.desksFromScan);
            Assert.AreEqual(1, b.obstacles.Count);
        }

        // ── no side area / unknown room / the one fallback ──

        [Test]
        public void NoSideArea_NoValidSpot_FallbackIsFlagged_AndCannotConfirm()
        {
            var room = SyntheticRoomGeometry.Rectangle(0f, 0f, 2.0f, 6f, 0f, 2.6f);    // a 2 m wide corridor around the user
            var b = Bounds(room); var u = User(); var s = S();
            Assert.IsTrue(b.hasFloor);
            Assert.AreEqual(0, PortalSpotAdapter.ListSpots(u, b, s).Count);
            PortalSpot f = PortalSpotPlanner.Plan(u, b, s);
            Assert.IsTrue(f.isFallback); Assert.IsFalse(f.valid);
            StringAssert.Contains("No clear space", f.reason);
            Assert.GreaterOrEqual(Angle(u, f.position), s.frontConeDeg);
            Assert.IsFalse(PortalSpotAdapter.ToCandidate(f, true, 1.2f).valid, "the large avatar never opens on the fallback");

            // The fallback position itself sits outside the head and hand zones (it is never a squeeze).
            var c = s.comfort;
            Assert.GreaterOrEqual((XZ(f.position) - XZ(u.head)).magnitude - PortalSpotPlanner.BodyRadius(s), c.handReachSide + c.handMargin - 1e-4f);
        }

        [Test]
        public void UnknownRoom_NoLargeSpot_AndForwardWorkAreaApplied_WhenNoScreenIsKnown()
        {
            var u = User(); var s = S();
            var none = new PortalRoomBounds();
            Assert.IsFalse(PortalSpotAdapter.Build(null, null, u, none, null));
            PortalSpot f = PortalSpotPlanner.Plan(u, none, s);
            Assert.IsTrue(f.isFallback); Assert.IsFalse(f.valid);
            StringAssert.Contains("unchecked", f.reason);
            Assert.IsFalse(PortalSpotPlanner.Evaluate(u, none, s, new Vector2(1.35f, 0f)).valid);

            // A scanned room without any SCREEN label: monitors are NOT assumed absent.
            var b = Bounds(Room6());
            Assert.AreEqual(0, b.screensFromScan);
            PortalSpot nearFront = PortalSpotPlanner.Evaluate(u, b, s, new Vector2(0.92f, 0.77f));   // 50 deg: outside the cone, inside the work/hand zone
            Assert.IsFalse(nearFront.valid);
            StringAssert.Contains("you", nearFront.reason);
        }

        // ── scale changes ──

        [Test]
        public void ScaleChanges_TallerAvatarIsWider_AndIsRecheckedNotAssumed()
        {
            var u = User(); var b = Bounds(Room6());
            var s = S();
            Assert.AreEqual(0.30f, PortalSpotPlanner.BodyRadius(s), 1e-4f);
            Vector2 side = new Vector2(1.35f, 0f);
            Assert.IsTrue(PortalSpotPlanner.Evaluate(u, b, s, side).valid, "1.2 m avatar fits");

            s.avatarHeight = 2.0f;
            Assert.AreEqual(0.50f, PortalSpotPlanner.BodyRadius(s), 1e-4f);
            Assert.IsFalse(PortalSpotPlanner.Evaluate(u, b, s, side).valid, "a 2 m avatar at the same spot reaches the user's zones");

            s.avatarHeight = 1.2f;
            var low = Bounds(SyntheticRoomGeometry.Rectangle(0f, 0f, 6f, 6f, 0f, 1.25f));
            StringAssert.Contains("head room", PortalSpotPlanner.Evaluate(u, low, s, side).reason);
            Assert.AreEqual(0.3f, PortalAvatarScale.ComfortRadius(0.5f, 0.3f, 0.25f), 1e-5f, "never below the footprint");
        }

        // ── swept transition, not just the end points ──

        [Test]
        public void SweptTransition_BothEndsClear_ButPathAcrossTheUser_Refused()
        {
            var u = User(); var b = Bounds(Room6()); var s = S();
            Vector2 right = new Vector2(1.4f, -0.3f), left = new Vector2(-1.4f, -0.3f);
            float cl; string why;
            Assert.IsTrue(PortalSpotPlanner.SweepClear(u, b, s, right, 1f, right, 1f, out cl, out why), why);
            Assert.IsTrue(PortalSpotPlanner.SweepClear(u, b, s, left, 1f, left, 1f, out cl, out why), why);
            var poses = new List<Vector4>();
            Assert.IsFalse(PortalSpotPlanner.SweepClear(u, b, s, right, 1f, left, 1f, out cl, out why, poses));
            Assert.IsNotNull(why);
            Assert.Greater(poses.Count, 2, "intermediate poses were tested");
            Vector4 last = poses[poses.Count - 1];
            Assert.Less(Mathf.Abs(last.x), 1.39f, "it stopped at the first offending pose, between the two clear ends");
        }

        [Test]
        public void SweptAppearance_RecordedPoses_AllClear_ForEveryListedSpot()
        {
            var room = Room6();
            room.AddFurniture("TABLE", new Vector3(0f, 0.375f, 0.75f), new Vector3(1.4f, 0.75f, 0.7f));
            room.AddFurniture("SCREEN", new Vector3(0f, 1.1f, 0.95f), new Vector3(0.6f, 0.4f, 0.1f));
            var u = User(); var b = Bounds(room); var s = S();
            List<PortalSpot> spots = PortalSpotAdapter.ListSpots(u, b, s);
            Assert.Greater(spots.Count, 0);
            foreach (PortalSpot p in spots)
            {
                Assert.IsTrue(p.valid);
                AssertSweepPosesClear(u, b, s, XZ(p.position));
                Assert.IsTrue(PortalAvatarScale.ClearsUserEnvelope(p.position, PortalSpotPlanner.BodyRadius(s), u, SeatedUserKeepOut.HeadTorsoEnvelope(s.comfort)));
            }
        }

        // ── furniture / closed door ──

        [Test]
        public void Furniture_OnTheRight_SpotGoesLeft()
        {
            var room = Room6();
            room.AddFurniture("COUCH", new Vector3(1.6f, 0.4f, 0f), new Vector3(0.8f, 0.8f, 2.0f));
            var u = User(); var b = Bounds(room); var s = S();
            PortalSpot r = PortalSpotPlanner.Evaluate(u, b, s, new Vector2(1.35f, 0f));
            Assert.IsFalse(r.valid);
            StringAssert.Contains("Furniture", r.reason);
            PortalSpot best = PortalSpotPlanner.Plan(u, b, s);
            Assert.IsTrue(best.valid); Assert.Less(best.position.x, 0f);
        }

        [Test]
        public void Door_KeepsItsSwingClear_EvenWhenClosed()
        {
            var walls = SyntheticWallRoom.Rectangle(0f, 0f, 4f, 4f, 0f, 2.6f);
            var u = User(); var s = S();
            Vector2 side = new Vector2(1.35f, 0f);
            Assert.IsTrue(PortalSpotPlanner.Evaluate(u, Bounds(walls.room, walls), s, side).valid, "baseline: clear without the door");

            RoomWall east = default(RoomWall);
            for (int i = 0; i < walls.WallCount; i++) if (walls.GetWall(i).id == "wall-E") east = walls.GetWall(i);
            Assert.AreEqual("wall-E", east.id);
            walls.AddOpening("wall-E", WallOpeningKind.Door, east.UOf(new Vector3(2f, 0f, 0f)), 1.0f, 0.9f, 2.0f);
            var b = Bounds(walls.room, walls);
            Assert.AreEqual(1, b.doorsFromScan);
            PortalSpot blocked = PortalSpotPlanner.Evaluate(u, b, s, side);
            Assert.IsFalse(blocked.valid);
            StringAssert.Contains("door", blocked.reason);
            Assert.AreEqual(PortalProblem.HitsFurniture, PortalSpotAdapter.ToCandidate(blocked, true, 1.2f).problem);
        }

        // ── controls stay visible and reachable ──

        [Test]
        public void Controls_TheAvatarNeverStandsOnOrInFrontOfTheCard()
        {
            var u = User(); var s = S();
            var b = Bounds(Room6());
            Vector2 side = new Vector2(1.35f, 0f);
            Assert.IsTrue(PortalSpotPlanner.Evaluate(u, b, s, side).valid);
            PortalSpotAdapter.AddControls(b, u, new Vector3(0.9f, 1.0f, 0f), s.comfort);
            PortalSpot p = PortalSpotPlanner.Evaluate(u, b, s, side);
            Assert.IsFalse(p.valid);
            StringAssert.Contains("controls", p.reason);
        }

        // ── polygon floors (rooms are rarely rectangles) ──

        [Test]
        public void LShapedFloor_PlannedExactly_NotchRefused()
        {
            var room = new SyntheticRoomGeometry();
            room.AddFloor(new[] { new Vector2(-3, -3), new Vector2(3, -3), new Vector2(3, 0), new Vector2(0, 0), new Vector2(0, 3), new Vector2(-3, 3) }, 0f);
            var u = new SeatedUser(new Vector3(-0.8f, 1.2f, -0.5f), Vector3.right);
            var b = Bounds(room, null, u); var s = S();
            Assert.IsTrue(b.hasFloor);
            Assert.IsNotNull(b.floorPolygon);
            PortalSpot notch = PortalSpotPlanner.Evaluate(u, b, s, new Vector2(0.1f, 0.6f));
            Assert.IsFalse(notch.valid);
            StringAssert.Contains("floor", notch.reason);
            PortalSpot best = PortalSpotPlanner.Plan(u, b, s);
            Assert.IsTrue(best.valid, best.reason);
            Assert.IsTrue(SpatialGeometry.PointInPolygon(b.floorPolygon, XZ(best.position)));
        }

        [Test]
        public void Build_Revision_StableWhenUnchanged_ChangesWhenFurnitureMoves()
        {
            var room = Room6();
            room.AddFurniture("STORAGE", new Vector3(2f, 0.5f, 2f), new Vector3(0.5f, 1f, 0.5f));
            int a = Bounds(room).revision, a2 = Bounds(room).revision;
            Assert.AreEqual(a, a2);
            room.ClearFurniture();
            room.AddFurniture("STORAGE", new Vector3(1.5f, 0.5f, 2f), new Vector3(0.5f, 1f, 0.5f));
            Assert.AreNotEqual(a, Bounds(room).revision);
        }

        // ── tracker: looking at the ring never moves it ──

        [Test]
        public void Tracker_LookingAtTheRing_DoesNotMoveIt_SustainedLookAway_Reseats()
        {
            var s = S();
            var t = new PortalSpotTracker(s);
            var u = User(); var b = Bounds(Room6());
            PortalSpot first = t.Update(0f, u, b);
            Assert.IsTrue(first.valid);
            Vector3 toSpot = first.position - u.head; toSpot.y = 0f;
            var looking = new SeatedUser(u.head, toSpot);
            for (int i = 1; i <= 100; i++) t.Update(i * 0.1f, looking, b);
            Assert.AreEqual(first.position, t.Current.position, "turning to look at the ring is not a reason to move it");
            Assert.AreEqual(1, t.EvaluationCount);

            var away = new SeatedUser(u.head, -toSpot);                  // looking the other way for a long time
            for (int i = 101; i <= 160; i++) t.Update(i * 0.1f, away, b);
            Assert.Less(Vector3.Angle(t.SeatForward, away.forward), 1f, "re-seated after a sustained look away");
        }

        // ── the guard: hands busy, closing mid-interaction, re-evaluation ──

        static PortalCandidate PlannedCandidate(IRoomGeometry room, SeatedUser u)
        {
            var b = Bounds(room, null, u);
            List<PortalSpot> spots = PortalSpotAdapter.ListSpots(u, b, S());
            Assert.Greater(spots.Count, 0);
            return PortalSpotAdapter.ToCandidate(spots[0], true, 1.2f);
        }

        static PortalFlow Flow(out AgentPresentationRegistry reg) { reg = new AgentPresentationRegistry(); return new PortalFlow(reg); }

        static void RunToConversing(PortalFlow flow)
        {
            Assert.IsTrue(flow.Confirm());
            flow.Tick(0.8f); Assert.AreEqual(PortalState.Emerging, flow.State);
            flow.Tick(1.1f); Assert.AreEqual(PortalState.Conversing, flow.State);
        }

        [Test]
        public void Guard_HandsBusy_BlocksConfirm_RestoresWhenFree_ClosesIfOpening()
        {
            var room = Room6(); var u = User();
            AgentPresentationRegistry reg; var flow = Flow(out reg);
            var g = new PortalComfortGuard();
            Assert.IsTrue(flow.BeginPreview("researcher", PlannedCandidate(room, u)));
            Assert.AreEqual(PortalState.AwaitConfirm, flow.State);
            Assert.AreEqual(PortalGuardAction.None, g.Tick(flow, 0f, u, room, false));

            Assert.AreEqual(PortalGuardAction.BlockedConfirm, g.Tick(flow, 0.05f, u, room, true));
            Assert.AreEqual(PortalState.Preview, flow.State);
            Assert.IsFalse(flow.Confirm(), "no Open while holding a paper");
            Assert.AreEqual(PortalGuardAction.Restored, g.Tick(flow, 0.4f, u, room, false));
            Assert.AreEqual(PortalState.AwaitConfirm, flow.State);
            Assert.IsTrue(flow.Spot.valid);

            Assert.IsTrue(flow.Confirm());
            Assert.AreEqual(PortalGuardAction.Closed, g.Tick(flow, 0.5f, u, room, true), "grabbed something during Opening: nothing is shown");
            Assert.AreEqual(PortalState.Closing, flow.State);
            flow.Tick(1f);
            Assert.AreEqual(PortalState.Hidden, flow.State);
            Assert.AreEqual(AgentPresentation.Home, reg.Of("researcher"), "presentation freed");
        }

        [Test]
        public void Guard_HoldingWhileConversing_IsFine()
        {
            var room = Room6(); var u = User();
            AgentPresentationRegistry reg; var flow = Flow(out reg);
            var g = new PortalComfortGuard();
            flow.BeginPreview("developer", PlannedCandidate(room, u));
            g.Tick(flow, 0f, u, room, false);
            RunToConversing(flow);
            for (int i = 1; i <= 20; i++) Assert.AreEqual(PortalGuardAction.None, g.Tick(flow, i * 0.3f, u, room, true));
            Assert.AreEqual(PortalState.Conversing, flow.State);
        }

        [Test]
        public void Guard_FurnitureAppearsMidInteraction_ClosesCleanlyOnce()
        {
            var room = Room6(); var u = User();
            AgentPresentationRegistry reg; var flow = Flow(out reg);
            var g = new PortalComfortGuard();
            PortalCandidate c = PlannedCandidate(room, u);
            flow.BeginPreview("tester", c);
            Assert.AreEqual(PortalGuardAction.None, g.Tick(flow, 0f, u, room, false));
            RunToConversing(flow);
            Assert.AreEqual(PortalGuardAction.None, g.Tick(flow, 0.3f, u, room, false));
            Assert.IsTrue(g.LastCheck.valid);

            room.AddFurniture("STORAGE", c.position + new Vector3(0.1f, 0.5f, 0f), new Vector3(0.4f, 1f, 0.4f));   // the room scan updated
            Assert.AreEqual(PortalGuardAction.None, g.Tick(flow, 1.0f, u, room, false), "debounced: one failed check");
            Assert.AreEqual(PortalGuardAction.Closed, g.Tick(flow, 1.3f, u, room, false));
            StringAssert.Contains("Furniture", g.LastReason);
            Assert.AreEqual(PortalState.Closing, flow.State);
            flow.Tick(1f);
            Assert.AreEqual(PortalState.Hidden, flow.State);
            Assert.AreEqual(AgentPresentation.Home, reg.Of("tester"));
            for (int i = 0; i < 5; i++) Assert.AreEqual(PortalGuardAction.None, g.Tick(flow, 2f + i * 0.3f, u, room, false));
            Assert.AreEqual(1, g.Closures, "closed exactly once");
        }

        [Test]
        public void Guard_LookingAtAvatar_KeepsIt_LeaningIntoIt_ClosesIt()
        {
            var room = Room6(); var u = User();
            AgentPresentationRegistry reg; var flow = Flow(out reg);
            var g = new PortalComfortGuard();
            PortalCandidate c = PlannedCandidate(room, u);
            flow.BeginPreview("researcher", c);
            g.Tick(flow, 0f, u, room, false);
            RunToConversing(flow);
            Vector3 toAvatar = c.position - u.head; toAvatar.y = 0f;
            var looking = new SeatedUser(u.head, toAvatar);
            for (int i = 1; i <= 30; i++) Assert.AreEqual(PortalGuardAction.None, g.Tick(flow, i * 0.3f, looking, room, false), "turning to talk to it is the point");
            Assert.AreEqual(PortalState.Conversing, flow.State);

            Vector3 leaned = u.head + toAvatar.normalized * 0.8f;          // the user leaned / moved toward it
            var close = new SeatedUser(leaned, toAvatar);
            g.Tick(flow, 10f, close, room, false);
            g.Tick(flow, 10.3f, close, room, false);
            Assert.AreEqual(PortalState.Closing, flow.State);
            Assert.AreEqual(1, g.Closures);
        }

        [Test]
        public void Guard_RecenterOrRoomLoss_ReevaluatesAndRefuses()
        {
            var room = Room6(); var u = User();
            AgentPresentationRegistry reg; var flow = Flow(out reg);
            var g = new PortalComfortGuard();
            PortalCandidate c = PlannedCandidate(room, u);
            flow.BeginPreview("researcher", c);
            Assert.AreEqual(PortalGuardAction.None, g.Tick(flow, 0f, u, room, false));

            // A recenter / relocalization: the head is now somewhere the ring would be in front of the face.
            var jumped = new SeatedUser(new Vector3(c.position.x - 0.3f, 1.2f, c.position.z - 1.5f), Vector3.forward);
            Assert.AreEqual(PortalGuardAction.BlockedConfirm, g.Tick(flow, 0.05f, jumped, room, false), "re-checked at once, not after the interval");
            Assert.IsFalse(flow.Confirm());

            // Room data lost while conversing: a large avatar is never kept without room data.
            AgentPresentationRegistry reg2; var flow2 = Flow(out reg2);
            var g2 = new PortalComfortGuard();
            flow2.BeginPreview("developer", c);
            g2.Tick(flow2, 0f, u, room, false);
            RunToConversing(flow2);
            g2.Tick(flow2, 1.0f, u, null, false);
            g2.Tick(flow2, 1.3f, u, null, false);
            Assert.AreEqual(PortalState.Closing, flow2.State);
            StringAssert.Contains("No room", g2.LastReason);
        }

        [Test]
        public void Guard_SmallDeskVisitor_IsTheFallback_NoLargeGeometryCheck()
        {
            var u = User();
            AgentPresentationRegistry reg; var flow = Flow(out reg);
            var g = new PortalComfortGuard();
            PortalCandidate small = PortalPlacementEvaluator.ManualSmall(u, 70f, new PortalSettings());
            Assert.IsTrue(flow.BeginPreview("researcher", small));
            for (int i = 0; i < 5; i++) Assert.AreEqual(PortalGuardAction.None, g.Tick(flow, i * 0.3f, u, null, false));
            Assert.AreEqual(0, g.Checks, "the small visitor is not checked as a large avatar");
            Assert.AreEqual(PortalState.AwaitConfirm, flow.State);
            Assert.AreEqual(PortalGuardAction.BlockedConfirm, g.Tick(flow, 2f, u, null, true), "but not while the hands are busy");
        }

        [Test]
        public void Guard_LegacyCandidate_InFrontOfTheUser_IsHeldToTheSameRules()
        {
            var room = Room6(); var u = User();
            AgentPresentationRegistry reg; var flow = Flow(out reg);
            var g = new PortalComfortGuard();
            // A candidate some other planner marked valid, but it stands in the forward work area.
            var bad = new PortalCandidate { position = new Vector3(0.9f, 0f, 1.0f), rotation = Quaternion.identity, avatarHeight = 1.2f, valid = true, roomChecked = true, reason = "legacy" };
            flow.BeginPreview("researcher", bad);
            Assert.AreEqual(PortalGuardAction.BlockedConfirm, g.Tick(flow, 0f, u, room, false));
            Assert.IsFalse(flow.Spot.valid);
            Assert.IsFalse(flow.Confirm());
        }
    }
}
#endif
