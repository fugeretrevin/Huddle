// =============================================================================
// MuseDesk · EditMode tests · loosened on owner request after the first worn pass (rc6-1)
// =============================================================================
//
// The owner wore rc6-1 and asked for three things to be less strict:
//  1. Floor: "something's here in the way" for a sock a foot away.
//  2. Ceiling: few docks, "screen is in the way".
//  3. Ceiling: "can't come down, you're too close"; stop higher and further away by default.
//
// What these tests pin down: low floor clutter only blocks under or right next to the office, tall
// furniture keeps its margins; a screen keeps the ordinary sweep margin but not the extra clearance of
// the high part of a path, and a screen in the pod's column still blocks; the ceiling search works in
// the SEAT's frame (the gaze turned the poses into the desk and across the face, which is how both
// ceiling messages were produced); the pod stops higher instead of refusing when the normal lowering
// cannot be reached, and still refuses when even the shallowest descent would break the head clearance.
// The user keep-out, the sweep, Stop/reverse and fail-closed behaviour are unchanged; every move here
// is recorded pose by pose.
//
// Not proven here: anything about a real room scan (what MRUK reported for the sock), comfort or reach
// on a headset.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;

namespace MuseDesk.Tests.EditMode
{
    static class WornPass
    {
        public static readonly Vector2 Fp = SpatialRooms.Footprint;
        public static readonly RootPose Desk = new RootPose(new Vector3(0f, 0.9f, 0.6f), Quaternion.Euler(0f, 180f, 0f), 1f);

        public static PathSweepOptions FromDesk() { var o = default(PathSweepOptions); o.fromDesk = true; return o; }

        /// <summary>The settings as they were in rc6-1 (before the worn pass), for before/after comparisons.</summary>
        public static CeilingPlacementSettings Rc6Ceiling()
        {
            return new CeilingPlacementSettings
            {
                forwardOptions = new[] { 0.3f, 0.6f, 0f },
                inspectDropBelowEye = 0.35f,
                inspectWideSideOptions = new[] { 0.85f, 0.9f, 0.8f },
                shallowBaseAboveKeepOut = new float[0],
                minDescent = 0f,
                compactObstacleLabels = new string[0],
                lowObstacleMaxHeight = 0f,
            };
        }

        /// <summary>
        /// A typical home office: the desk against the wall in front of the user, two monitors on it, a
        /// TV on the right-hand wall. User at (0, 1.2, 0) looking along +Z (their right is +X).
        /// </summary>
        public static SyntheticRoomGeometry TypicalRoom(float frontWall = 1.3f, float rightWall = 1.6f)
        {
            var poly = new[] { new Vector2(-1.8f, -2.2f), new Vector2(rightWall, -2.2f), new Vector2(rightWall, frontWall), new Vector2(-1.8f, frontWall) };
            var room = new SyntheticRoomGeometry();
            room.AddFloor(poly, 0f);
            room.AddCeiling(poly, 2.5f);
            room.referencePose = new Pose(Vector3.zero, Quaternion.identity);
            room.AddFurniture("TABLE", new Vector3(0f, 0.37f, frontWall - 0.35f), new Vector3(1.4f, 0.74f, 0.7f));
            room.AddFurniture("SCREEN", new Vector3(0f, 0.98f, frontWall - 0.2f), new Vector3(1.2f, 0.48f, 0.2f));
            room.AddFurniture("SCREEN", new Vector3(rightWall - 0.05f, 1.35f, 0.1f), new Vector3(0.1f, 0.65f, 1.1f));
            return room;
        }

        /// <summary>No margins at all: does the office box, or the mast from its top to <paramref name="mastTop"/>,
        /// really touch the box anywhere on the straight path? Checked about every millimetre.</summary>
        public static bool RealContact(FurnitureBox box, RootPose from, RootPose to, CeilingPlacementSettings s, Vector3? mastTop)
        {
            const int steps = 2000;
            for (int i = 0; i <= steps; i++)
            {
                RootPose p = CeilingPlacementEvaluator.PathPose(from, to, i / (float)steps);
                OrientedRect rect = SpatialGeometry.Footprint(p.position, p.rotation, 1f, Fp, s.footprintCenterOffset);
                float top = p.position.y + s.officeHeight;
                if (!(box.maxY <= p.position.y || box.minY >= top) && rect.Overlaps(box.Footprint)) return true;
                if (mastTop.HasValue && SpatialGeometry.SegmentHitsBox(new Vector3(rect.center.x, top, rect.center.y), mastTop.Value, box, 0f, 0f)) return true;
            }
            return false;
        }

        /// <summary>A box of <paramref name="size"/> (x across, z along) standing on the floor, its near face
        /// <paramref name="gap"/> beyond the far edge of the office footprint at <paramref name="spot"/>
        /// (the edge away from the user).</summary>
        public static void AddBehind(SyntheticRoomGeometry room, string label, RootPose spot, float gap, float height, CeilingPlacementSettings s)
        {
            OrientedRect r = SpatialGeometry.Footprint(spot.position, spot.rotation, spot.scale, Fp, s.footprintCenterOffset);
            const float depth = 0.10f;
            Vector2 c = r.center - r.axisY * (r.half.y + gap + depth * 0.5f);
            room.AddFurniture(label, new Vector3(c.x, spot.position.y + height * 0.5f, c.y), new Vector3(0.25f, height, depth), spot.rotation.eulerAngles.y);
        }

        public static RootPose FloorSpot(float x, float z)
        {
            var p = new Vector3(x, 0f, z);
            return new RootPose(p, SpatialGeometry.FaceUserYaw(p, SpatialRooms.User().head), 1f);
        }

        /// <summary>Docked in Ceiling mode in <paramref name="room"/>, with the user as the rig has them.</summary>
        public static SpatialRig Docked(SyntheticRoomGeometry room, out CeilingPlacement placement)
        {
            var rig = new SpatialRig(room);
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Ceiling), rig.core.StatusText);
            Assert.IsTrue(rig.core.Confirm(), rig.core.StatusText);
            placement = rig.core.LastCeiling;
            rig.Run(7f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
            SpatialRooms.AssertPose(placement.DockPose, rig.root);
            return rig;
        }

        /// <summary>Every recorded pose lies on the straight path a..b (1 mm, and the yaw of that point).</summary>
        public static void AssertOnPath(RootPose a, RootPose b, List<RootPose> trace, string what)
        {
            Assert.That(trace.Count, Is.GreaterThan(5), what + ": the move was recorded");
            Vector3 ab = b.position - a.position;
            for (int i = 0; i < trace.Count; i++)
            {
                float k = Mathf.Clamp01(Vector3.Dot(trace[i].position - a.position, ab) / ab.sqrMagnitude);
                SpatialRooms.AssertPose(CeilingPlacementEvaluator.PathPose(a, b, k), trace[i], 0.001f);
            }
        }
    }

    // ───────────────────────── 1. floor: low clutter ─────────────────────────

    [Category("MuseDesk")]
    public class FloorLowClutterTests
    {
        static readonly Vector2 Fp = WornPass.Fp;

        [Test] // the discriminating distance: 8 cm, inside the old 0.11 m furniture margin
        public void LowObjectNextToTheFootprint_DoesNotBlock_ATallOneAtTheSameDistanceStillDoes()
        {
            var fs = new FloorPlacementSettings();
            var cs = new CeilingPlacementSettings();
            SeatedUser user = SpatialRooms.User();
            RootPose spot = WornPass.FloorSpot(1.5f, -0.2f);
            FloorPoseProblem problem;
            Assert.IsTrue(FloorPlacementEvaluator.CheckPlacedPose(SpatialRooms.Office(), user, spot, Fp, fs, out problem), "the empty spot is fine");

            var sock = SpatialRooms.Office();
            WornPass.AddBehind(sock, "OTHER", spot, 0.08f, 0.04f, cs);
            Assert.IsTrue(FloorPlacementEvaluator.CheckPlacedPose(sock, user, spot, Fp, fs, out problem), "a sock 8 cm away: " + problem);

            var shelf = SpatialRooms.Office();
            WornPass.AddBehind(shelf, "STORAGE", spot, 0.08f, 1.8f, cs);
            Assert.IsFalse(FloorPlacementEvaluator.CheckPlacedPose(shelf, user, spot, Fp, fs, out problem));
            Assert.AreEqual(FloorPoseProblem.Furniture, problem);
            Assert.AreEqual("Something is in the way", FloorPlacementEvaluator.ShortReason(problem));

            // The boundary is the height: 15 cm is clutter, 16 cm is furniture (0.11 m margin, unchanged).
            var low = SpatialRooms.Office();
            WornPass.AddBehind(low, "OTHER", spot, 0.08f, 0.15f - 1e-3f, cs);
            Assert.IsTrue(FloorPlacementEvaluator.CheckPlacedPose(low, user, spot, Fp, fs, out problem));
            var higher = SpatialRooms.Office();
            WornPass.AddBehind(higher, "OTHER", spot, 0.08f, 0.16f, cs);
            Assert.IsFalse(FloorPlacementEvaluator.CheckPlacedPose(higher, user, spot, Fp, fs, out problem));
        }

        [Test] // the owner's distance: 0.3 m from the footprint, spot AND the way there
        public void LowObject0p3mAway_DoesNotBlock_TallObjectAtTheSameDistanceStillBlocksTheWayThere()
        {
            var fs = new FloorPlacementSettings();
            var cs = new CeilingPlacementSettings();
            SeatedUser user = SpatialRooms.User();
            RootPose spot = WornPass.FloorSpot(1.5f, -0.2f);
            FloorPoseProblem problem;
            WorkspaceRoute route; CeilingPathCheck check;

            var sock = SpatialRooms.Office();
            WornPass.AddBehind(sock, "OTHER", spot, 0.30f, 0.04f, cs);
            Assert.IsTrue(FloorPlacementEvaluator.CheckPlacedPose(sock, user, spot, Fp, fs, out problem));
            Assert.IsTrue(FloorRoutePlanner.Plan(sock, user, WornPass.Desk, spot, Fp, cs, WornPass.FromDesk(), true, out route, out check), check.Describe());

            // A 1.8 m shelf there: the spot itself keeps the 0.11 m furniture margin, but the office comes
            // down onto the spot from above head height, where the high-part clearance (0.405 m) applies.
            var shelf = SpatialRooms.Office();
            WornPass.AddBehind(shelf, "STORAGE", spot, 0.30f, 1.8f, cs);
            Assert.IsTrue(FloorPlacementEvaluator.CheckPlacedPose(shelf, user, spot, Fp, fs, out problem), "0.3 m is outside the spot's own margin");
            Assert.IsFalse(FloorRoutePlanner.Plan(shelf, user, WornPass.Desk, spot, Fp, cs, WornPass.FromDesk(), true, out route, out check));
            Assert.AreEqual(CeilingPathBlock.Furniture, check.blockedBy);
            Assert.AreEqual("STORAGE", check.blockingLabel);
            StringAssert.Contains("blocked by STORAGE", check.Words("The way there"));
        }

        [Test]
        public void LowObjectUnderOrRightAgainstTheOffice_StillBlocks()
        {
            var fs = new FloorPlacementSettings();
            var cs = new CeilingPlacementSettings();
            SeatedUser user = SpatialRooms.User();
            RootPose spot = WornPass.FloorSpot(1.5f, -0.2f);
            FloorPoseProblem problem;
            WorkspaceRoute route; CeilingPathCheck check;

            // Under the footprint.
            var under = SpatialRooms.Office();
            OrientedRect r = SpatialGeometry.Footprint(spot.position, spot.rotation, 1f, Fp, fs.footprintCenterOffset);
            under.AddFurniture("OTHER", new Vector3(r.center.x, 0.02f, r.center.y), new Vector3(0.1f, 0.04f, 0.1f));
            Assert.IsFalse(FloorPlacementEvaluator.CheckPlacedPose(under, user, spot, Fp, fs, out problem));
            Assert.AreEqual(FloorPoseProblem.Furniture, problem);
            Assert.IsFalse(FloorRoutePlanner.Plan(under, user, WornPass.Desk, spot, Fp, cs, WornPass.FromDesk(), true, out route, out check), "the route agrees");

            // 3 cm from it: inside the small pad (0.06 m).
            var against = SpatialRooms.Office();
            WornPass.AddBehind(against, "OTHER", spot, 0.03f, 0.04f, cs);
            Assert.IsFalse(FloorPlacementEvaluator.CheckPlacedPose(against, user, spot, Fp, fs, out problem));
            // ...and the sweep's own low margin (0.03 m + half a step) at the boundary.
            float need = cs.lowObstacleMargin + 0.5f * cs.sweepStep;
            foreach (float delta in new[] { 0.004f, -0.004f })
            {
                var room = SpatialRooms.Office();
                WornPass.AddBehind(room, "OTHER", spot, need + delta, 0.04f, cs);
                bool clear = CeilingPlacementEvaluator.SweepPath(room, user, spot, spot, Fp, cs, out check);
                Assert.AreEqual(delta > 0f, clear, check.Describe());
            }
        }

        [Test]
        public void TheSpotRuleAndTheSweep_AgreeOnLowClutter()
        {
            var fs = new FloorPlacementSettings();
            var cs = new CeilingPlacementSettings();
            Assert.AreEqual(fs.lowObstacleMaxHeight, cs.lowObstacleMaxHeight, 1e-6f, "one definition of low clutter");
            Assert.That(fs.lowObstaclePad, Is.GreaterThan(cs.lowObstacleMargin + 0.5f * CeilingPlacementEvaluator.EffectiveSweepStep(cs)),
                        "a spot shown clear keeps more than the sweep tests a sample with");
            // Furniture is unchanged: still above the sweep's ordinary margin at rest.
            Assert.That(fs.placedFurnitureClearance, Is.GreaterThan(CeilingPlacementEvaluator.SampleMarginHorizontal(cs)));
            Assert.AreEqual(0.11f, fs.placedFurnitureClearance, 1e-6f);
            Assert.AreEqual(0.08f, cs.sweepMarginHorizontal, 1e-6f);
            Assert.AreEqual(0.30f, cs.descentClearance, 1e-6f);

            // A spot the rule calls clear with clutter at its pad is reachable by the route.
            SeatedUser user = SpatialRooms.User();
            RootPose spot = WornPass.FloorSpot(1.5f, -0.2f);
            var room = SpatialRooms.Office();
            WornPass.AddBehind(room, "OTHER", spot, fs.lowObstaclePad + 0.002f, 0.04f, cs);
            FloorPoseProblem problem;
            Assert.IsTrue(FloorPlacementEvaluator.CheckPlacedPose(room, user, spot, Fp, fs, out problem));
            WorkspaceRoute route; CeilingPathCheck check;
            Assert.IsTrue(FloorRoutePlanner.Plan(room, user, WornPass.Desk, spot, Fp, cs, WornPass.FromDesk(), true, out route, out check), check.Describe());

            // Clutter not on a detected floor is not "low" (fail closed): it keeps the usual margin.
            var off = SpatialRooms.Office();
            off.AddFurniture("OTHER", new Vector3(4f, 0.02f, 4f), new Vector3(0.1f, 0.04f, 0.1f));
            Assert.AreEqual(CeilingPlacementEvaluator.KindUsual, CeilingPlacementEvaluator.Classify(off, off.GetFurniture(1), cs));
            Assert.AreEqual(CeilingPlacementEvaluator.KindLow, CeilingPlacementEvaluator.Classify(room, room.GetFurniture(1), cs));
        }

        [Test] // Place here with a sock beside the spot: the office moves, along a route swept clear
        public void PlaceHere_WithASockBesideTheSpot_MovesAlongTheSweptRoute()
        {
            var room = SpatialRooms.Office();
            var rig = new SpatialRig(room);
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Floor), rig.core.StatusText);
            RootPose spot = rig.core.PreviewPose;
            WornPass.AddBehind(room, "OTHER", spot, 0.08f, 0.04f, rig.core.ceilingSettings);

            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.Confirm(), rig.core.StatusText);
            WorkspaceRoute route = rig.core.LastRoute;
            Assert.IsTrue(rig.core.LastRouteCheck.clear);
            rig.Run(5f);
            SpatialRooms.AssertPose(spot, rig.root);
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);
            // Every pose written lies on the route that was swept, and the route sweeps clear again.
            for (int i = 0; i < rig.trace.Count; i++)
            {
                float best = float.MaxValue;
                for (int l = 0; l < route.LegCount; l++)
                {
                    RootPose a = route[l], b = route[l + 1];
                    Vector3 ab = b.position - a.position;
                    float k = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(rig.trace[i].position - a.position, ab) / ab.sqrMagnitude) : 0f;
                    best = Mathf.Min(best, (CeilingPlacementEvaluator.PathPose(a, b, k).position - rig.trace[i].position).magnitude);
                }
                Assert.That(best, Is.LessThan(0.002f), "write " + i);
            }
            CeilingPathCheck again;
            var o = WornPass.FromDesk(); o.absoluteHigh = true;
            Assert.IsTrue(CeilingPlacementEvaluator.SweepRoute(room, rig.user, route, WornPass.Fp, rig.core.ceilingSettings, o, out again), again.Describe());

            // The same spot with a shelf there instead: refused, nothing moves.
            var room2 = SpatialRooms.Office();
            var rig2 = new SpatialRig(room2);
            Assert.IsTrue(rig2.core.RequestMode(PlacementMode.Floor));
            WornPass.AddBehind(room2, "STORAGE", rig2.core.PreviewPose, 0.08f, 1.8f, rig2.core.ceilingSettings);
            Assert.IsFalse(rig2.core.Confirm());
            StringAssert.Contains("blocked by STORAGE", rig2.core.StatusText);
            rig2.Run(2f);
            Assert.AreEqual(0, rig2.writes);
            Assert.AreEqual(PlacementMode.Desk, rig2.core.Mode);
        }
    }

    // ───────────────────────── 2. ceiling: screens, and more docks ─────────────────────────

    [Category("MuseDesk")]
    public class CeilingScreenTests
    {
        static readonly Vector2 Fp = WornPass.Fp;

        static SyntheticRoomGeometry Empty() { return SyntheticRoomGeometry.Rectangle(0f, 0f, 5f, 5f, 0f, 2.6f); }

        /// <summary>A wall-mounted screen 1.45 to 2.05 m high, 1 m wide, beside the docked office's far
        /// edge with its face <paramref name="gap"/> from it: next to the high part of the way down.</summary>
        static SyntheticRoomGeometry WithBoxBesideTheDock(CeilingPlacement dock, string label, float gap)
        {
            var s = new CeilingPlacementSettings();
            var room = Empty();
            OrientedRect r = SpatialGeometry.Footprint(dock.dockPosition, dock.dockRotation, 1f, Fp, s.footprintCenterOffset);
            Vector2 c = r.center - r.axisY * (r.half.y + gap + 0.05f);
            room.AddFurniture(label, new Vector3(c.x, 1.75f, c.y), new Vector3(1.0f, 0.6f, 0.1f), dock.dockRotation.eulerAngles.y);
            return room;
        }

        [Test]
        public void ScreenClearOfThePodColumn_DoesNotBlockTheDock_TheSameBoxAsFurnitureStillDoes()
        {
            var s = new CeilingPlacementSettings();
            SeatedUser user = SpatialRooms.User();
            CeilingPlacement dock;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(Empty(), user, Fp, s, out dock), dock.explanation);

            foreach (float gap in new[] { 0.12f, 0.2f, 0.3f })
            {
                CeilingPlacement v;
                var screen = WithBoxBesideTheDock(dock, "SCREEN", gap);
                Assert.IsTrue(CeilingPlacementEvaluator.ValidateDock(screen, user, dock.DockPose, Fp, s, out v), gap + ": " + v.explanation);
                Assert.IsTrue(v.path.clear);
                Assert.AreEqual(gap, v.path.minFurnitureClearance, 0.01f, "it passes the screen at the gap, no closer");
                Assert.IsFalse(WornPass.RealContact(screen.GetFurniture(0), v.DockPose, v.InspectionPose, s, v.mastTop), "nothing is crossed");

                // The same box labelled as furniture keeps the high-part clearance (0.405 m per sample).
                var shelf = WithBoxBesideTheDock(dock, "STORAGE", gap);
                Assert.IsFalse(CeilingPlacementEvaluator.ValidateDock(shelf, user, dock.DockPose, Fp, s, out v));
                StringAssert.Contains("blocked by STORAGE", v.explanation);
            }
            // Beyond the high-part clearance both pass.
            CeilingPlacement far;
            Assert.IsTrue(CeilingPlacementEvaluator.ValidateDock(WithBoxBesideTheDock(dock, "STORAGE", 0.42f), user, dock.DockPose, Fp, s, out far), far.explanation);

            // With the exemption switched off the screen is treated as before.
            var strict = new CeilingPlacementSettings { compactObstacleLabels = new string[0] };
            Assert.IsFalse(CeilingPlacementEvaluator.ValidateDock(WithBoxBesideTheDock(dock, "SCREEN", 0.2f), user, dock.DockPose, Fp, strict, out far));
        }

        [Test]
        public void ScreenCrossingThePodColumn_OrInsideTheSweepMargin_StillBlocks()
        {
            var s = new CeilingPlacementSettings();
            SeatedUser user = SpatialRooms.User();
            CeilingPlacement dock;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(Empty(), user, Fp, s, out dock), dock.explanation);
            CeilingPlacement v;

            // 1. Inside the ordinary sweep margin (0.08 m + half a step): blocked, named.
            foreach (float gap in new[] { 0.05f, 0.09f })
            {
                Assert.IsFalse(CeilingPlacementEvaluator.ValidateDock(WithBoxBesideTheDock(dock, "SCREEN", gap), user, dock.DockPose, Fp, s, out v));
                StringAssert.Contains("blocked by SCREEN", v.explanation);
            }

            // 2. Across the way down: a screen a quarter of the way down, in the office's path (every
            //    way down from this dock passes there).
            RootPose mid = CeilingPlacementEvaluator.PathPose(dock.DockPose, dock.InspectionPose, 0.25f);
            OrientedRect rect = SpatialGeometry.Footprint(mid.position, mid.rotation, 1f, Fp, s.footprintCenterOffset);
            var across = Empty();
            across.AddFurniture("SCREEN", new Vector3(rect.center.x, mid.position.y + 0.2f, rect.center.y), new Vector3(0.6f, 0.35f, 0.05f));
            Assert.IsFalse(CeilingPlacementEvaluator.ValidateDock(across, user, dock.DockPose, Fp, s, out v));
            StringAssert.Contains("SCREEN", v.explanation);

            // 3. In the column above the office, between it and the ceiling (a projector screen rolled up
            //    there): the column is swept up to the ceiling on the high part of the path.
            RootPose lowDock = new RootPose(new Vector3(1.0f, 1.9f, 0.3f), SpatialGeometry.FaceUserYaw(new Vector3(1.0f, 1.9f, 0.3f), user.head), 1f);
            RootPose inspect = dock.InspectionPose;
            Vector3 mastTop = CeilingPlacementEvaluator.MastTop(lowDock, 2.6f, s);
            var column = Empty();
            column.AddFurniture("SCREEN", new Vector3(mastTop.x + 0.3f, 2.5f, mastTop.z), new Vector3(0.2f, 0.15f, 0.2f));
            CeilingPathCheck check;
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(column, user, lowDock, inspect, Fp, s, out check), "the office box alone misses it");
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(column, user, lowDock, inspect, Fp, s, CeilingPlacementEvaluator.PodPathOptions(mastTop), out check));
            Assert.AreEqual("SCREEN", check.blockingLabel);

            // 4. On the mast line.
            OrientedRect rest = SpatialGeometry.Footprint(inspect.position, inspect.rotation, 1f, Fp, s.footprintCenterOffset);
            Vector3 foot = new Vector3(rest.center.x, inspect.position.y + s.officeHeight, rest.center.y);
            var onMast = Empty();
            onMast.AddFurniture("SCREEN", foot + (mastTop - foot) * 0.5f, new Vector3(0.06f, 0.06f, 0.06f));
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(onMast, user, inspect, inspect, Fp, s, CeilingPlacementEvaluator.PodPathOptions(mastTop), out check));
            Assert.IsTrue(check.blockedByMast);
        }

        [Test]
        public void TypicalRoom_OffersMoreDocks_AndNoPathCrossesARealObject()
        {
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            var all = new List<CeilingPlacement>();
            string summary, before;
            var room = WornPass.TypicalRoom();
            int old = CeilingPlacementEvaluator.EvaluateAll(room, user, Fp, WornPass.Rc6Ceiling(), all, out before);
            int now = CeilingPlacementEvaluator.EvaluateAll(room, user, Fp, s, all, out summary);
            StringAssert.Contains("path blocked by furniture (first: SCREEN)", before, "rc6-1 refused docks for the wall screen");
            Assert.That(now, Is.GreaterThan(old), "rc6-1: " + before + " / now: " + summary);
            Assert.That(old, Is.GreaterThan(0));

            foreach (CeilingPlacement p in all)
            {
                Assert.IsTrue(p.valid);
                Assert.IsTrue(p.path.clear);
                Assert.That(p.path.minUserClearance, Is.GreaterThan(s.userKeepOutRadius), "the keep-out is unchanged");
                for (int f = 0; f < room.FurnitureCount; f++)
                    Assert.IsFalse(WornPass.RealContact(room.GetFurniture(f), p.DockPose, p.InspectionPose, s, p.mastTop),
                                   room.GetFurniture(f).label + " is crossed by the way down from " + p.dockPosition);
            }
        }
    }

    // ───────────────────────── 2b. ceiling: the way up to a dock beside the shoulder ─────────────────────────

    [Category("MuseDesk")]
    public class CeilingDockRouteTests
    {
        static readonly Vector2 Fp = WornPass.Fp;

        static PathSweepOptions UpFromDesk(Vector3 mastTop)
        {
            var o = default(PathSweepOptions); o.fromDesk = true; o.mast = true; o.mastTop = mastTop;
            return o;
        }

        [Test] // rc6-1 could not reach any dock here: the turn on the way across passed 7 cm from the head's axis
        public void DeskAgainstTheWall_EveryOfferedDock_CanNowBeReachedAndLeft_WithTheSameSweep()
        {
            var room = WornPass.TypicalRoom(1.1f, 1.9f);
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            var all = new List<CeilingPlacement>();
            string summary;
            Assert.That(CeilingPlacementEvaluator.EvaluateAll(room, user, Fp, s, all, out summary), Is.GreaterThan(4), summary);

            int oldShapes = 0;
            foreach (CeilingPlacement p in all)
            {
                WorkspaceRoute r; CeilingPathCheck c;
                if (CeilingPlacementEvaluator.PlanRoute(room, user, WornPass.Desk, p.DockPose, Fp, s, UpFromDesk(p.mastTop), false, out r, out c)) oldShapes++;
                else Assert.AreEqual(CeilingPathBlock.User, c.blockedBy, "rc6-1's only shape: refused for the user");

                Assert.IsTrue(FloorRoutePlanner.Plan(room, user, WornPass.Desk, p.DockPose, Fp, s, UpFromDesk(p.mastTop), false, out r, out c),
                              p.dockPosition + ": " + c.Describe());
                // The side step: up at the desk (no turn), sideways above head height (no turn), then to the dock.
                Assert.AreEqual(4, r.count);
                Assert.AreEqual(WornPass.Desk.position.x, r[1].position.x, 1e-5f);
                Assert.AreEqual(WornPass.Desk.position.z, r[1].position.z, 1e-5f);
                Assert.AreEqual(r[1].position.y, r[2].position.y, 1e-5f, "the step is level");
                Assert.That(r[1].position.y, Is.GreaterThan(user.head.y + s.userKeepOutAboveHead), "above the keep-out");
                Assert.That(Quaternion.Angle(WornPass.Desk.rotation, r[2].rotation), Is.LessThan(0.01f), "no turn until the step is made");
                Assert.That(Vector3.Dot(r[2].position - r[1].position, p.dockPosition - WornPass.Desk.position), Is.GreaterThan(0f), "toward the dock's side");
                SpatialRooms.AssertPose(p.DockPose, r[3]);

                // The same sweep, again, and nothing real is touched on any leg.
                var o = UpFromDesk(p.mastTop); o.absoluteHigh = true;
                Assert.IsTrue(CeilingPlacementEvaluator.SweepRoute(room, user, r, Fp, s, o, out c), c.Describe());
                for (int l = 0; l < r.LegCount; l++)
                    for (int f = 0; f < room.FurnitureCount; f++)
                    {
                        // The leg that starts at the desk pose starts against the monitors by design (desk rule).
                        if (l == 0) continue;
                        Assert.IsFalse(WornPass.RealContact(room.GetFurniture(f), r[l], r[l + 1], s, null), "leg " + l + " touches " + room.GetFurniture(f).label);
                    }

                // The way back is found too, and is the same set of poses.
                var back = default(PathSweepOptions); back.toDesk = true; back.mast = true; back.mastTop = p.mastTop;
                WorkspaceRoute rb; CeilingPathCheck cb;
                Assert.IsTrue(FloorRoutePlanner.Plan(room, user, p.DockPose, WornPass.Desk, Fp, s, back, false, out rb, out cb), cb.Describe());
                for (int i = 0; i < rb.count; i++) SpatialRooms.AssertPose(r[r.count - 1 - i], rb[i]);
            }
            Assert.AreEqual(0, oldShapes, "with rc6-1's shapes no dock here could be reached");
        }

        [Test] // the director: every pose written on the way up, the way back, and Stop on the new shape
        public void FirstTripUp_ByTheSideStep_IsTravelledExactly_StopsAndComesBack()
        {
            var room = WornPass.TypicalRoom(1.1f, 1.9f);
            var rig = new SpatialRig(room);
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Ceiling), rig.core.StatusText);
            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.Confirm(), rig.core.StatusText);
            WorkspaceRoute route = rig.core.LastRoute;
            CeilingPlacement placement = rig.core.LastCeiling;
            Assert.AreEqual(4, route.count, "the side step");
            Assert.IsTrue(rig.core.LastRouteCheck.clear);
            rig.Run(8f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
            SpatialRooms.AssertPose(placement.DockPose, rig.root);
            float h = route[1].position.y;
            foreach (RootPose p in rig.trace)
            {
                AssertOnRoute(route, p);
                if (p.position.y < h - 1e-3f)
                {
                    // Below the transit height: straight up at the desk, never turned.
                    Assert.AreEqual(WornPass.Desk.position.x, p.position.x, 1e-4f);
                    Assert.AreEqual(WornPass.Desk.position.z, p.position.z, 1e-4f);
                    Assert.That(Quaternion.Angle(WornPass.Desk.rotation, p.rotation), Is.LessThan(0.05f));
                }
            }

            // Back to the desk: the same poses in reverse, ending exactly on the desk pose.
            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Desk), rig.core.StatusText);
            WorkspaceRoute down = rig.core.LastRoute;
            Assert.AreEqual(4, down.count);
            for (int i = 0; i < 4; i++) SpatialRooms.AssertPose(route[3 - i], down[i]);
            rig.Run(8f);
            SpatialRooms.AssertPose(WornPass.Desk, rig.root, 1e-4f);
            foreach (RootPose p in rig.trace) AssertOnRoute(down, p);

            // Stop during the trip up: it goes back along what it travelled and ends on the desk pose.
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Ceiling), rig.core.StatusText);
            Assert.IsTrue(rig.core.Confirm(), rig.core.StatusText);
            rig.Run(2.0f);
            Assert.That((rig.root.position - WornPass.Desk.position).magnitude, Is.GreaterThan(0.2f), "it had left the desk");
            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.StopMove());
            rig.Run(8f);
            SpatialRooms.AssertPose(WornPass.Desk, rig.root, 1e-3f);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            foreach (RootPose p in rig.trace) AssertOnRoute(route, p);
        }

        [Test]
        public void ThePreview_OffersOnlyADockItCanReach_AndSaysSoWhenNoneIsLeft()
        {
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();

            // The evaluator: a dock the caller does not take is passed over, in order, and counted.
            CeilingPlacement p;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(SpatialRooms.Office(), user, Fp, s, c => c.side < 0, out p), p.explanation);
            Assert.AreEqual(-1, p.side);
            Assert.IsFalse(CeilingPlacementEvaluator.Evaluate(SpatialRooms.Office(), user, Fp, s, c => false, out p));
            StringAssert.Contains("32 no clear way up from where the office is", p.explanation);

            // The director: a lamp hanging just over the desk office (1.65 to 1.75 m). Every dock is
            // fine on its own, but every way up rises through the lamp. rc6-1 offered the first dock and
            // refused at Confirm; now the preview itself says so, and nothing moves.
            var room = SpatialRooms.Office();
            room.AddFurniture("LAMP", new Vector3(-0.3f, 1.7f, 0.5f), new Vector3(0.1f, 0.1f, 0.1f));
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(room, user, Fp, s, out p), "the docks themselves are clear");
            var rig = new SpatialRig(room);
            Assert.IsFalse(rig.core.RequestMode(PlacementMode.Ceiling));
            StringAssert.Contains("no clear way up from where the office is", rig.core.StatusText);
            Assert.AreEqual(DirectorPhase.Idle, rig.core.Phase);
            rig.Run(2f);
            Assert.AreEqual(0, rig.writes);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
        }

        static void AssertOnRoute(WorkspaceRoute route, RootPose pose)
        {
            float best = float.MaxValue;
            for (int l = 0; l < route.LegCount; l++)
            {
                RootPose a = route[l], b = route[l + 1];
                Vector3 ab = b.position - a.position;
                float k = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(pose.position - a.position, ab) / ab.sqrMagnitude) : 0f;
                RootPose q = CeilingPlacementEvaluator.PathPose(a, b, k);
                best = Mathf.Min(best, (q.position - pose.position).magnitude + Quaternion.Angle(q.rotation, pose.rotation) * 0.002f);
            }
            Assert.That(best, Is.LessThan(0.002f));
        }
    }

    // ───────────────────────── 3. ceiling: the seat's frame, not the gaze ─────────────────────────

    [Category("MuseDesk")]
    public class CeilingSeatFrameTests
    {
        static readonly Vector2 Fp = WornPass.Fp;

        static SeatedUser Looking(float yawDeg)
        {
            return new SeatedUser(new Vector3(0f, 1.2f, 0f), Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward);
        }

        [Test] // how both owner messages were produced
        public void WithTheGazeAsForward_Rc6SaidScreenInTheWayAndTooCloseToYou()
        {
            var room = WornPass.TypicalRoom(1.1f, 1.9f);
            var rc6 = WornPass.Rc6Ceiling();
            CeilingPlacement found, v;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(room, Looking(0f), Fp, rc6, out found), found.explanation);
            Assert.AreEqual(1, found.side);

            // Looking ahead: fine. Looking a little left: the poses "beside you" turn into the monitors.
            Assert.IsTrue(CeilingPlacementEvaluator.ValidateDock(room, Looking(0f), found.DockPose, Fp, rc6, out v), v.explanation);
            Assert.IsFalse(CeilingPlacementEvaluator.ValidateDock(room, Looking(-30f), found.DockPose, Fp, rc6, out v));
            StringAssert.Contains("SCREEN in the way", v.explanation);
            // Looking round at the pod to bring it down: the way down crosses in front of the face.
            foreach (float yaw in new[] { 75f, 90f })
            {
                Assert.IsFalse(CeilingPlacementEvaluator.ValidateDock(room, Looking(yaw), found.DockPose, Fp, rc6, out v));
                StringAssert.Contains("passes too close to you", v.explanation);
            }
        }

        [Test]
        public void TurningTheHeadTowardThePod_DoesNotChangeWhereItComesDown()
        {
            var room = WornPass.TypicalRoom(1.1f, 1.9f);
            CeilingPlacement placement;
            var rig = WornPass.Docked(room, out placement);
            RootPose expected = placement.InspectionPose;

            foreach (float yaw in new[] { 75f, 90f, -30f, 40f })
            {
                rig.user = Looking(yaw);
                rig.trace = new List<RootPose>();
                Assert.IsTrue(rig.core.RequestPodDescend(), yaw + ": " + rig.core.StatusText);
                SpatialRooms.AssertPose(expected, rig.pod.InspectionPose);
                rig.Run(6f);
                Assert.AreEqual(CeilingPodState.Engaged, rig.pod.State);
                SpatialRooms.AssertPose(expected, rig.root);
                WornPass.AssertOnPath(placement.DockPose, expected, rig.trace, "down, looking " + yaw);
                Assert.IsTrue(rig.core.RequestPodRetract(), rig.core.StatusText);
                rig.Run(6f);
                Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
            }
        }

        [Test]
        public void ThePreview_DoesNotTurnWithTheHeadEither()
        {
            var room = WornPass.TypicalRoom(1.1f, 1.9f);
            var a = new SpatialRig(room);
            Assert.IsTrue(a.core.RequestMode(PlacementMode.Ceiling), a.core.StatusText);
            var b = new SpatialRig(room);
            b.user = Looking(-30f);
            Assert.IsTrue(b.core.RequestMode(PlacementMode.Ceiling), b.core.StatusText);
            SpatialRooms.AssertPose(a.core.LastCeiling.DockPose, b.core.LastCeiling.DockPose);
            SpatialRooms.AssertPose(a.core.LastCeiling.InspectionPose, b.core.LastCeiling.InspectionPose);
        }
    }

    // ───────────────────────── 4. ceiling: how far down ─────────────────────────

    [Category("MuseDesk")]
    public class CeilingShallowDescentTests
    {
        static readonly Vector2 Fp = WornPass.Fp;

        static SyntheticRoomGeometry Office(float ceiling)
        {
            var room = SyntheticRoomGeometry.Rectangle(0f, 0f, 5f, 5f, 0f, ceiling);
            room.AddFurniture("TABLE", new Vector3(0f, 0.375f, 0.8f), new Vector3(1.4f, 0.75f, 0.7f));
            return room;
        }

        /// <summary>Horizontal distance from the head's vertical axis to the office's footprint.</summary>
        static float FromAxis(RootPose p, SeatedUser user, CeilingPlacementSettings s)
        {
            return SpatialGeometry.Footprint(p.position, p.rotation, 1f, Fp, s.footprintCenterOffset).DistanceToPoint(new Vector2(user.head.x, user.head.z));
        }

        [Test]
        public void TheNormalLowering_StopsHigherAndFurtherOut_AndStaysWithinReach()
        {
            var s = new CeilingPlacementSettings();
            var rc6 = WornPass.Rc6Ceiling();
            SeatedUser user = SpatialRooms.User();
            CeilingPlacement now, before;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(SpatialRooms.Office(), user, Fp, s, out now), now.explanation);
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(SpatialRooms.Office(), user, Fp, rc6, out before), before.explanation);

            Assert.AreEqual(user.head.y - 0.25f, now.inspectionPosition.y, 1e-4f, "0.25 m below the eyes (was 0.35)");
            Assert.AreEqual(user.head.y - 0.35f, before.inspectionPosition.y, 1e-4f);
            Assert.That(now.descentDistance, Is.LessThan(before.descentDistance), "a shallower descent");
            float nowOut = FromAxis(now.InspectionPose, user, s), beforeOut = FromAxis(before.InspectionPose, user, s);
            Assert.That(nowOut, Is.GreaterThan(beforeOut + 0.04f), "further from the user");
            Assert.That(nowOut, Is.GreaterThan(s.userKeepOutRadius + 0.05f), "with room to spare outside the keep-out");
            Assert.That(nowOut, Is.LessThan(0.6f), "the near edge is within arm's reach");
            Assert.IsTrue(now.path.clear);
        }

        [Test]
        public void UserCloseToALowDock_GetsAShallowerEngagedHeight_InsteadOfARefusal()
        {
            var room = Office(2.1f);
            var s = new CeilingPlacementSettings();
            CeilingPlacement placement;
            var rig = WornPass.Docked(room, out placement);
            Assert.AreEqual(1.6f, placement.dockPosition.y, 1e-4f, "a 2.1 m ceiling: the dock is only 0.4 m above the eyes");

            // The user rolls back and toward the dock: 0.81 m from it. "Forward" is toward the desk, as the
            // director takes it (the seat's frame, not the gaze).
            Vector3 head = new Vector3(0.45f, 1.2f, -0.30f);
            SeatedUser moved = new SeatedUser(head, WornPass.Desk.position - head);
            CeilingPlacement v;
            var noShallow = new CeilingPlacementSettings { shallowBaseAboveKeepOut = new float[0] };
            Assert.IsFalse(CeilingPlacementEvaluator.ValidateDock(room, moved, placement.DockPose, Fp, noShallow, out v), "the normal lowering cannot keep clear of the head");
            StringAssert.Contains("too close to you", v.explanation);

            Assert.IsTrue(CeilingPlacementEvaluator.ValidateDock(room, moved, placement.DockPose, Fp, s, out v), v.explanation);
            float keepOutTop = moved.head.y + s.userKeepOutAboveHead;
            Assert.AreEqual(keepOutTop + s.shallowBaseAboveKeepOut[0], v.inspectionPosition.y, 1e-4f, "stops just above the keep-out");
            Assert.That(v.inspectionPosition.y, Is.LessThanOrEqualTo(moved.head.y + s.shallowMaxAboveEye + 1e-4f), "not out of reach");
            Assert.That(placement.dockPosition.y - v.inspectionPosition.y, Is.GreaterThanOrEqualTo(s.minDescent - 1e-4f), "still a descent");
            Assert.That(FromAxis(v.InspectionPose, moved, s), Is.GreaterThan(s.userOverheadRadius), "never overhead");
            Assert.That(FromAxis(v.InspectionPose, moved, s), Is.LessThan(0.6f), "within reach");

            // Through the director: the pod comes down to that pose; every pose written is on the
            // straight path and above the keep-out; Stop halfway sends it back up the same way.
            rig.user = moved;
            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.RequestPodDescend(), rig.core.StatusText);
            SpatialRooms.AssertPose(v.InspectionPose, rig.pod.InspectionPose);
            rig.Run(6f);
            Assert.AreEqual(CeilingPodState.Engaged, rig.pod.State);
            Assert.IsFalse(rig.core.IsRemote, "engaged");
            SpatialRooms.AssertPose(v.InspectionPose, rig.root);
            WornPass.AssertOnPath(placement.DockPose, v.InspectionPose, rig.trace, "shallow descent");
            foreach (RootPose p in rig.trace)
            {
                Assert.That(p.position.y, Is.GreaterThan(keepOutTop), "never inside the keep-out's height");
                Assert.That(FromAxis(p, moved, s), Is.GreaterThan(s.userOverheadRadius), "never overhead");
            }

            Assert.IsTrue(rig.core.RequestPodRetract(), rig.core.StatusText);
            rig.Run(6f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
            SpatialRooms.AssertPose(placement.DockPose, rig.root);

            Assert.IsTrue(rig.core.RequestPodDescend(), rig.core.StatusText);
            rig.Run(0.9f);
            Assert.AreEqual(CeilingPodState.Descending, rig.pod.State, "on the way down");
            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.StopMove());
            rig.Run(6f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State, "Stop reversed it");
            SpatialRooms.AssertPose(placement.DockPose, rig.root);
            WornPass.AssertOnPath(placement.DockPose, v.InspectionPose, rig.trace, "back up after Stop");
        }

        [Test]
        public void UserSoCloseThatEvenTheShallowestDescentBreaksTheHeadClearance_IsRefused()
        {
            var s = new CeilingPlacementSettings();
            CeilingPlacement v;

            // 1. Under the corner of the dock (the dock itself is within the "never overhead" radius).
            CeilingPlacement placement;
            var rig = WornPass.Docked(SpatialRooms.Office(), out placement);
            SeatedUser under = new SeatedUser(new Vector3(0.6f, 1.2f, -0.4f), Vector3.forward);
            Assert.IsFalse(CeilingPlacementEvaluator.ValidateDock(rig.core.Room, under, placement.DockPose, Fp, s, out v));
            StringAssert.Contains("too close to you", v.explanation);
            Assert.AreEqual(CeilingPathBlock.User, v.path.blockedBy);
            rig.user = under;
            int writes = rig.writes;
            Assert.IsFalse(rig.core.RequestPodDescend());
            StringAssert.Contains("too close to you", rig.core.StatusText);
            rig.Run(3f);
            Assert.AreEqual(writes, rig.writes, "nothing moved");
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);

            // 2. A 2.0 m ceiling: the dock hangs at 1.5 m, so a pose above the keep-out (1.40 m) would be
            //    only 10 cm below it, less than minDescent. With the user close, there is no descent.
            var low = Office(2.0f);
            CeilingPlacement lowDock;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(low, SpatialRooms.User(), Fp, s, out lowDock), lowDock.explanation);
            Assert.AreEqual(1.5f, lowDock.dockPosition.y, 1e-4f);
            SeatedUser moved = new SeatedUser(new Vector3(0.4f, 1.2f, -0.35f), Vector3.forward);
            Assert.IsFalse(CeilingPlacementEvaluator.ValidateDock(low, moved, lowDock.DockPose, Fp, s, out v));
            StringAssert.Contains("too close to you", v.explanation);
        }

        [Test]
        public void ThePoseAboveTheKeepOut_IsOnlyAFallback_AndItsReasonWordsNameTheRealObstacle()
        {
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();

            // In the test office the normal lowering is always found first.
            var all = new List<CeilingPlacement>();
            string summary;
            Assert.That(CeilingPlacementEvaluator.EvaluateAll(SpatialRooms.Office(), user, Fp, s, all, out summary), Is.GreaterThan(0), summary);
            foreach (CeilingPlacement p in all)
                Assert.AreEqual(user.head.y - s.inspectDropBelowEye, p.inspectionPosition.y, 0.101f, "normal lowering (raised at most 0.10 m)");

            // A side desk beside the user on both sides, 1.1 m high: the normal lowering hits it, the office
            // stops above it (above the keep-out too), and the way down misses it.
            var room = SpatialRooms.Office();
            room.AddFurniture("TABLE", new Vector3(1.0f, 0.55f, 0f), new Vector3(1.2f, 1.1f, 1.6f));
            room.AddFurniture("TABLE", new Vector3(-1.0f, 0.55f, 0f), new Vector3(1.2f, 1.1f, 1.6f));
            CeilingPlacement shallow;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(room, user, Fp, s, out shallow), shallow.explanation);
            Assert.That(shallow.inspectionPosition.y, Is.GreaterThan(user.head.y + s.userKeepOutAboveHead));
            Assert.IsTrue(shallow.path.clear);
            for (int f = 0; f < room.FurnitureCount; f++)
                Assert.IsFalse(WornPass.RealContact(room.GetFurniture(f), shallow.DockPose, shallow.InspectionPose, s, shallow.mastTop));

            // Words: a wall screen beside the user on both sides, head height to 2 m. The closest options
            // (0.5 to 0.7 m) fail the keep-out first, but what stops the ones that could work is the
            // screen, and that is what is named (rc6-1 said "too close to you").
            // (1.0 to 1.5 m to each side: the 0.6 m pose reaches only 0.83 m out, so it is refused for
            // the user alone; the 0.8 to 1.0 m poses reach into the screen.)
            var screens = SpatialRooms.Office();
            screens.AddFurniture("SCREEN", new Vector3(1.25f, 1.35f, 0f), new Vector3(0.5f, 1.3f, 1.6f));
            screens.AddFurniture("SCREEN", new Vector3(-1.25f, 1.35f, 0f), new Vector3(0.5f, 1.3f, 1.6f));
            CeilingPlacement none;
            Assert.IsFalse(CeilingPlacementEvaluator.Evaluate(screens, user, Fp, s, out none));
            StringAssert.Contains("no room beside you (SCREEN in the way)", none.explanation);
        }
    }
}
#endif
