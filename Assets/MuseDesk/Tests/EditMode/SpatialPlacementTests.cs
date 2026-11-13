// =============================================================================
// MuseDesk · EditMode tests · room-aware workspace (Floor / Ceiling modes)
// =============================================================================
//
// These tests exercise the REAL spatial logic (evaluators, transition machine, ceiling pod,
// director core, persistence codec) against SYNTHETIC rooms. That logic lives in its own
// assembly, MuseDesk.Spatial.Core (Assets/MuseDesk/Scripts/Spatial/Core), precisely so a test
// assembly can reference it (an .asmdef cannot reference Assembly-CSharp).
//
// ─── ACTIVATION (two edits in MuseDesk.Tests.EditMode.asmdef, a file this agent may not edit) ───
// 1. Add  "MuseDesk.Spatial.Core"  to "references".
// 2. Add this entry to "versionDefines":
//      { "name": "com.meta.xr.mrutilitykit", "expression": "", "define": "MUSEDESK_SPATIAL_TESTS" }
//    (or add MUSEDESK_SPATIAL_TESTS to the Editor Scripting Define Symbols).
// Until then this file compiles to nothing, so the existing test assembly is unaffected.
//
// What these tests do NOT prove is listed in docs/Engineering/SPATIAL_PLACEMENT_TESTS.md.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;

namespace MuseDesk.Tests.EditMode
{
    /// <summary>Everything the director core needs, with a variable standing in for WorkspaceRoot.</summary>
    sealed class SpatialRig
    {
        public readonly ManualClock clock = new ManualClock();
        public readonly SpatialFeatureFlags flags = new SpatialFeatureFlags { floorModeEnabled = true, ceilingModeEnabled = true };
        public RootPose root = new RootPose(new Vector3(0f, 0.9f, 0.6f), Quaternion.Euler(0f, 180f, 0f), 1f);
        public SeatedUser user = new SeatedUser(new Vector3(0f, 1.2f, 0f), Vector3.forward);
        public bool held;
        public bool agentsBusy;
        public bool deskReady = true;
        public int writes;
        public int changes;
        /// <summary>When set, every pose written to the root is recorded (the path actually travelled).</summary>
        public List<RootPose> trace;

        public readonly WorkspaceTransitionMachine mover;
        public readonly CeilingPodMachine pod;
        public readonly SpatialDirectorCore core;

        public SpatialRig(IRoomGeometry room)
        {
            mover = new WorkspaceTransitionMachine(clock, () => root, p => { root = p; writes++; if (trace != null) trace.Add(p); }, () => held, () => agentsBusy);
            pod = new CeilingPodMachine(clock, mover, () => held);
            core = new SpatialDirectorCore(flags, mover, pod, () => user, () => held, () => deskReady);
            core.Changed += () => changes++;
            core.Room = room;
            changes = 0;
        }

        public void Run(float seconds, float step = 0.05f)
        {
            int n = Mathf.CeilToInt(seconds / step);
            for (int i = 0; i < n; i++) { clock.Advance(step); core.Tick(); }
        }
    }

    static class SpatialRooms
    {
        public static readonly Vector2 Footprint = new Vector2(1.0f, 0.7f);

        /// <summary>5 x 5 m room, floor at 0, ceiling at 2.6 m, the user's desk in front of them.</summary>
        public static SyntheticRoomGeometry Office(bool ceiling = true)
        {
            var room = SyntheticRoomGeometry.Rectangle(0f, 0f, 5f, 5f, 0f, ceiling ? 2.6f : 0f);
            room.AddFurniture("TABLE", new Vector3(0f, 0.375f, 0.8f), new Vector3(1.4f, 0.75f, 0.7f));
            return room;
        }

        public static SeatedUser User() { return new SeatedUser(new Vector3(0f, 1.2f, 0f), Vector3.forward); }

        public static void AssertPose(RootPose expected, RootPose actual, float tol = 0.002f)
        {
            Assert.That((expected.position - actual.position).magnitude, Is.LessThan(tol), "position");
            Assert.That(Quaternion.Angle(expected.rotation, actual.rotation), Is.LessThan(0.5f), "rotation");
            Assert.That(Mathf.Abs(expected.scale - actual.scale), Is.LessThan(0.001f), "scale");
        }
    }

    // ───────────────────────── evaluators ─────────────────────────

    [Category("MuseDesk")]
    public class SpatialFloorEvaluatorTests
    {
        static List<FloorCandidate> Evaluate(IRoomGeometry room, FloorEvaluationReport report, FloorPlacementSettings s = null)
        {
            var list = new List<FloorCandidate>();
            FloorPlacementEvaluator.Evaluate(room, SpatialRooms.User(), SpatialRooms.Footprint, s ?? new FloorPlacementSettings(), list, report);
            return list;
        }

        [Test] // fixture 1: valid floor and ceiling
        public void ValidRoom_GivesRankedCandidatesBesideTheUser()
        {
            var room = SpatialRooms.Office();
            var s = new FloorPlacementSettings();
            var report = new FloorEvaluationReport();
            var list = Evaluate(room, report, s);
            SeatedUser user = SpatialRooms.User();

            Assert.That(list.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(list.Count, Is.LessThanOrEqualTo(s.maxCandidates));
            Assert.AreEqual(RoomAvailability.Available, report.availability);
            Assert.AreEqual(1, list[0].side, "the preferred (right) side ranks first in a symmetric room");

            for (int i = 0; i < list.Count; i++)
            {
                FloorCandidate c = list[i];
                if (i > 0) Assert.That(c.score, Is.LessThanOrEqualTo(list[i - 1].score), "ranked best first");
                Assert.That(c.score, Is.InRange(0f, 1f));
                Assert.AreEqual(0f, c.position.y, 1e-4f, "on the detected floor height");
                Assert.IsFalse(string.IsNullOrEmpty(c.explanation));

                Vector3 flat = c.position - user.head; flat.y = 0f;
                Assert.That(flat.magnitude, Is.InRange(s.minDistance - 0.01f, s.maxDistance + 0.01f));
                Assert.That(Vector3.Angle(user.forward, flat), Is.GreaterThanOrEqualTo(s.forwardConeHalfAngleDeg), "not toward the desk");

                // +Z of the root points at the user (project facing convention).
                Vector3 toUser = user.head - c.position; toUser.y = 0f;
                Assert.That(Vector3.Angle(c.rotation * Vector3.forward, toUser), Is.LessThan(1f));

                string reason;
                Assert.IsTrue(FloorPlacementEvaluator.ValidatePose(room, user, c.Pose, SpatialRooms.Footprint, s, true, out reason), reason);

                OrientedRect rect = SpatialGeometry.Footprint(c.position, c.rotation, c.scale, SpatialRooms.Footprint, s.footprintCenterOffset);
                Assert.That(rect.DistanceToPoint(new Vector2(user.head.x, user.head.z)), Is.GreaterThanOrEqualTo(s.userClearance - 1e-4f));
                Assert.That(rect.DistanceTo(room.GetFurniture(0).Footprint), Is.GreaterThanOrEqualTo(s.furnitureClearance - 1e-4f));
            }
        }

        [Test]
        public void Evaluation_IsDeterministic()
        {
            var room = SpatialRooms.Office();
            var a = Evaluate(room, new FloorEvaluationReport());
            var b = Evaluate(room, new FloorEvaluationReport());
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.IsTrue(a[i].position.Equals(b[i].position));
                Assert.AreEqual(a[i].score, b[i].score);
                Assert.AreEqual(a[i].explanation, b[i].explanation);
            }
        }

        [Test] // fixture 3: small room with insufficient floor
        public void SmallRoom_GivesNoCandidates_AndSaysWhy()
        {
            var room = SyntheticRoomGeometry.Rectangle(0f, 0f, 1.6f, 1.6f, 0f, 2.4f);
            var report = new FloorEvaluationReport();
            var list = Evaluate(room, report);
            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(RoomAvailability.Available, report.availability, "the room itself is fine; it is just too small");
            Assert.That(report.generated, Is.GreaterThan(0));
            Assert.AreEqual(report.generated, report.offFloor + report.footprintOffFloor + report.furniture + report.nearUser + report.forwardCone + report.hidden);
            StringAssert.Contains("No clear floor spot", report.Summary());
        }

        [Test] // fixture 4: furniture near candidates
        public void FurnitureOnTheBestSpot_MovesTheCandidatesAway()
        {
            var room = SpatialRooms.Office();
            var s = new FloorPlacementSettings();
            FloorCandidate best = Evaluate(room, new FloorEvaluationReport(), s)[0];

            room.AddFurniture("COUCH", new Vector3(best.position.x, 0.25f, best.position.z), new Vector3(1.0f, 0.5f, 1.0f));
            var report = new FloorEvaluationReport();
            var list = Evaluate(room, report, s);
            Assert.That(report.furniture, Is.GreaterThan(0));
            FurnitureBox couch = room.GetFurniture(1);
            foreach (FloorCandidate c in list)
            {
                OrientedRect rect = SpatialGeometry.Footprint(c.position, c.rotation, c.scale, SpatialRooms.Footprint, s.footprintCenterOffset);
                Assert.That(rect.DistanceTo(couch.Footprint), Is.GreaterThanOrEqualTo(s.furnitureClearance - 1e-4f));
                Assert.That((c.position - best.position).magnitude, Is.GreaterThan(0.2f));
            }
        }

        [Test] // fixture 4: furniture blocking the line of sight
        public void TallFurnitureBetweenUserAndSpot_HidesThatSide()
        {
            var room = SpatialRooms.Office();
            // A 1.8 m tall partition along the user's right.
            room.AddFurniture("STORAGE", new Vector3(0.75f, 0.9f, 0f), new Vector3(0.3f, 1.8f, 3.0f));
            var report = new FloorEvaluationReport();
            var list = Evaluate(room, report);
            Assert.That(list.Count, Is.GreaterThan(0), "the left side is still free");
            Assert.That(report.hidden + report.furniture, Is.GreaterThan(0));
            foreach (FloorCandidate c in list) Assert.AreEqual(-1, c.side, "nothing is offered behind the partition");
        }

        [Test] // fixtures 5 and 6: permission denied, missing room data
        public void NoRoomData_NeverYieldsADetectedFloor()
        {
            foreach (RoomAvailability a in new[] { RoomAvailability.PermissionDenied, RoomAvailability.NotLoadedYet,
                                                   RoomAvailability.NoRoomSetup, RoomAvailability.InvalidFloor })
            {
                var room = SpatialRooms.Office();
                room.forcedAvailability = a;
                var report = new FloorEvaluationReport();
                var list = Evaluate(room, report);
                Assert.AreEqual(0, list.Count, a.ToString());
                Assert.AreEqual(a, report.availability);
                Assert.AreEqual(0, report.generated, "no position is even tried without room data");
            }
            var none = new FloorEvaluationReport();
            Assert.AreEqual(0, Evaluate(null, none).Count);
            Assert.AreEqual(RoomAvailability.NotLoadedYet, none.availability);
        }

        [Test]
        public void ScaleOptions_AreLimitedToTheSafeRange()
        {
            var room = SpatialRooms.Office();
            var s = new FloorPlacementSettings { scaleOptions = new[] { 3f, 1.25f, 1f, 0.2f } };
            foreach (FloorCandidate c in Evaluate(room, new FloorEvaluationReport(), s))
                Assert.That(c.scale, Is.InRange(s.minScale, s.maxScale));
        }
    }

    [Category("MuseDesk")]
    public class SpatialCeilingEvaluatorTests
    {
        [Test] // fixture 1: valid floor and ceiling
        public void ValidCeiling_GivesDockInspectionPoseAndDescent()
        {
            var room = SpatialRooms.Office();
            var s = new CeilingPlacementSettings();
            SeatedUser user = SpatialRooms.User();
            CeilingPlacement p;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(room, user, SpatialRooms.Footprint, s, out p), p.explanation);
            Assert.IsTrue(p.valid);

            Vector3 flat = p.dockPosition - user.head; flat.y = 0f;
            Assert.That(flat.magnitude, Is.GreaterThanOrEqualTo(s.minHorizontalFromUser), "never directly overhead");
            Assert.AreEqual(2.6f - s.dockDrop, p.dockPosition.y, 1e-4f, "hangs from the REAL ceiling height");
            Assert.AreEqual(2.6f, p.ceilingHeight, 1e-4f);

            // CHANGED with the swept-volume rule: this used to demand 0.5 to 0.7 m to the side. At that
            // distance the office's near edge is 0.03 to 0.25 m from the head's vertical axis, inside
            // the user keep-out (0.35 m), so the search now goes on to inspectWideSideOptions (up to 0.9 m).
            Vector3 d = p.inspectionPosition - user.head;
            float side = Mathf.Abs(Vector3.Dot(d, user.Right));
            Assert.That(side, Is.InRange(0.5f - 1e-4f, 0.9f + 1e-4f), "beside the user");
            Assert.That(Vector3.Dot(d, user.forward), Is.LessThan(side), "not in front of the face");
            Assert.AreEqual(user.head.y - s.inspectDropBelowEye, p.inspectionPosition.y, 1e-4f);
            OrientedRect atRest = SpatialGeometry.Footprint(p.inspectionPosition, p.inspectionRotation, 1f, SpatialRooms.Footprint, s.footprintCenterOffset);
            Assert.That(atRest.DistanceToPoint(new Vector2(user.head.x, user.head.z)), Is.GreaterThan(s.userKeepOutRadius), "outside the user keep-out");
            Assert.IsTrue(p.path.clear, "the descent was swept and found clear");
            Assert.AreEqual((p.dockPosition - p.inspectionPosition).magnitude, p.descentDistance, 1e-4f);
            Assert.That(p.descentDistance, Is.GreaterThan(0.5f));
        }

        [Test] // fixture 2: floor but no ceiling
        public void NoCeilingData_FailsWithTheReason_AndNeverGuessesAHeight()
        {
            var room = SpatialRooms.Office(ceiling: false);
            Assert.AreEqual(RoomAvailability.Available, room.Availability);
            Assert.AreEqual(RoomAvailability.NoCeiling, room.CeilingAvailability);
            CeilingPlacement p;
            Assert.IsFalse(CeilingPlacementEvaluator.Evaluate(room, SpatialRooms.User(), SpatialRooms.Footprint, new CeilingPlacementSettings(), out p));
            Assert.IsFalse(p.valid);
            StringAssert.Contains("no ceiling", p.explanation);
        }

        [Test]
        public void NarrowCeiling_RejectsDocksNearWalls()
        {
            // 1.8 m wide: every dock at least 0.8 m to the side is within the wall margin.
            var room = SyntheticRoomGeometry.Rectangle(0f, 0f, 1.8f, 5f, 0f, 2.6f);
            CeilingPlacement p;
            Assert.IsFalse(CeilingPlacementEvaluator.Evaluate(room, SpatialRooms.User(), SpatialRooms.Footprint, new CeilingPlacementSettings(), out p));
            StringAssert.Contains("No clear ceiling dock", p.explanation);
        }

        [Test]
        public void BlockedDescent_UsesTheOtherSide()
        {
            var room = SpatialRooms.Office();
            // A tall shelf beside the user's right at inspection height.
            room.AddFurniture("STORAGE", new Vector3(0.8f, 1.0f, 0.2f), new Vector3(0.8f, 2.0f, 0.8f));
            CeilingPlacement p;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(room, SpatialRooms.User(), SpatialRooms.Footprint, new CeilingPlacementSettings(), out p), p.explanation);
            Assert.AreEqual(-1, p.side);
        }

        [Test]
        public void LowCeiling_IsRefused()
        {
            var room = SyntheticRoomGeometry.Rectangle(0f, 0f, 5f, 5f, 0f, 1.6f);
            CeilingPlacement p;
            Assert.IsFalse(CeilingPlacementEvaluator.Evaluate(room, SpatialRooms.User(), SpatialRooms.Footprint, new CeilingPlacementSettings(), out p));
        }
    }

    // ───────────────────────── ceiling descent: swept volume ─────────────────────────

    /// <summary>
    /// The descent check is a swept-volume test that fails closed. These tests pin down: what the
    /// replaced centre-line rule let through, the margins at their boundaries, the sampling density,
    /// the user keep-out, and that the path swept is the path the transition machine travels.
    /// Geometry used throughout: the user sits at (0, 1.2, 0) looking along +Z, so the user's right
    /// is +X. An unrotated office (yaw 0) at root (x, y, z) covers x - 0.5 .. x + 0.5 and
    /// z - 0.23 .. z + 0.47 (footprint 1.0 x 0.7, centre 0.12 toward +Z), from y to y + 0.45.
    /// </summary>
    [Category("MuseDesk")]
    public class SpatialCeilingSweptPathTests
    {
        static readonly Vector2 Fp = SpatialRooms.Footprint;

        static SyntheticRoomGeometry EmptyRoom() { return SyntheticRoomGeometry.Rectangle(0f, 0f, 5f, 5f, 0f, 2.6f); }

        /// <summary>A root pose at <paramref name="p"/> turned to face the test user (as the evaluator builds its poses).</summary>
        static RootPose Facing(Vector3 p) { return new RootPose(p, SpatialGeometry.FaceUserYaw(p, SpatialRooms.User().head), 1f); }

        static RootPose Unrotated(float x, float y, float z) { return new RootPose(new Vector3(x, y, z), Quaternion.identity, 1f); }

        // The rule that was replaced, reproduced here ONLY to show what it accepted: one line from
        // dock to inspection position against furniture grown by 0.30 m (0.05 m vertically), and,
        // as relaxed on 2026-11-12, with the last 0.45 m of the line not tested at all.
        static bool OldCentreLineBlocked(IRoomGeometry room, Vector3 from, Vector3 to, bool blindLastStretch)
        {
            if (blindLastStretch) to = Vector3.MoveTowards(to, from, 0.45f);
            for (int i = 0; i < room.FurnitureCount; i++)
                if (SpatialGeometry.SegmentHitsBox(from, to, room.GetFurniture(i), 0.30f, 0.05f)) return true;
            return false;
        }

        // The old (and still present) test of the office standing at the inspection pose.
        static bool FootprintAtRestBlocked(IRoomGeometry room, RootPose pose, CeilingPlacementSettings s)
        {
            OrientedRect rect = SpatialGeometry.Footprint(pose.position, pose.rotation, 1f, Fp, s.footprintCenterOffset);
            for (int i = 0; i < room.FurnitureCount; i++)
            {
                FurnitureBox box = room.GetFurniture(i);
                if (box.maxY <= pose.position.y - 0.03f || box.minY >= pose.position.y + s.officeHeight) continue;
                if (rect.Overlaps(box.Footprint)) return true;
            }
            return false;
        }

        /// <summary>Brute force, no margins: does the office box really pass through the furniture box
        /// somewhere on the path? Checked every millimetre or so. Returns the first fraction, or -1.</summary>
        static float FirstRealContact(FurnitureBox box, RootPose from, RootPose to, CeilingPlacementSettings s)
        {
            const int steps = 2000;
            for (int i = 0; i <= steps; i++)
            {
                float k = i / (float)steps;
                RootPose p = CeilingPlacementEvaluator.PathPose(from, to, k);
                if (box.maxY <= p.position.y || box.minY >= p.position.y + s.officeHeight) continue;
                if (SpatialGeometry.Footprint(p.position, p.rotation, 1f, Fp, s.footprintCenterOffset).Overlaps(box.Footprint)) return k;
            }
            return -1f;
        }

        static FurnitureBox Last(SyntheticRoomGeometry room) { return room.GetFurniture(room.FurnitureCount - 1); }

        [Test] // the case the blind final stretch let through
        public void BoxOnTheFinalApproach_PassedTheRelaxedRule_AndIsRefusedNow()
        {
            var room = SpatialRooms.Office();
            SeatedUser user = SpatialRooms.User();
            // The path yesterday's code produced in this room: first dock option, first inspection option.
            RootPose dock = Facing(new Vector3(1.0f, 2.1f, 0.3f));
            RootPose inspect = Facing(new Vector3(0.6f, 0.85f, 0.15f));
            // That inspection pose is inside today's user keep-out; switch the keep-out off here so
            // that what this test shows is about furniture only.
            var s = new CeilingPlacementSettings { userKeepOutRadius = 0f };
            CeilingPathCheck check;
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(room, user, dock, inspect, Fp, s, out check), check.Describe());

            // A 0.30 x 0.10 m lamp, 0.95 to 1.15 m high, standing 3 cm beyond the edge of the office
            // that is farthest from the user at the inspection pose. The office comes down from
            // further out (the dock is 0.4 m further to the side), so on its last 0.3 m of travel
            // that edge passes through the lamp before the office settles 3 cm clear of it.
            OrientedRect atRest = SpatialGeometry.Footprint(inspect.position, inspect.rotation, 1f, Fp, s.footprintCenterOffset);
            Vector2 c = atRest.center - atRest.axisY * (atRest.half.y + 0.03f + 0.05f);
            room.AddFurniture("LAMP", new Vector3(c.x, 1.05f, c.y), new Vector3(0.30f, 0.20f, 0.10f), inspect.rotation.eulerAngles.y);
            FurnitureBox lamp = Last(room);

            // Yesterday: accepted. The lamp does not touch the office at rest, and the line test never
            // looked at the last 0.45 m. (The unrelaxed line test would have refused it.)
            Assert.IsFalse(FootprintAtRestBlocked(room, inspect, s), "the office at rest does not touch the lamp");
            Assert.IsFalse(OldCentreLineBlocked(room, dock.position, inspect.position, true), "the relaxed rule accepted this path");
            Assert.IsTrue(OldCentreLineBlocked(room, dock.position, inspect.position, false), "only the blind stretch hid it");

            // Reality: the office passes through the lamp inside that untested stretch.
            float length = (dock.position - inspect.position).magnitude;
            float blindFrom = 1f - 0.45f / length;
            float contact = FirstRealContact(lamp, dock, inspect, s);
            Assert.That(contact, Is.GreaterThan(blindFrom), "a real collision, inside the stretch that was not tested");

            // Today: refused, and the reason names the lamp.
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(room, user, dock, inspect, Fp, s, out check));
            Assert.IsFalse(check.clear);
            Assert.AreEqual(CeilingPathBlock.Furniture, check.blockedBy);
            Assert.AreEqual("LAMP", check.blockingLabel);
            Assert.That(check.blockedAtFraction, Is.GreaterThan(blindFrom), "caught on the final approach");
            Assert.That(check.blockedAtFraction, Is.LessThanOrEqualTo(contact), "and no later than the real contact");
            StringAssert.Contains("LAMP", check.Describe());

            // And whatever the evaluator offers in this room has a path that was swept and is clear.
            CeilingPlacement p;
            if (CeilingPlacementEvaluator.Evaluate(room, user, Fp, new CeilingPlacementSettings(), out p))
            {
                Assert.IsTrue(p.path.clear);
                Assert.AreEqual(-1f, FirstRealContact(lamp, p.DockPose, p.InspectionPose, s), 1e-6f);
            }
        }

        [Test] // a box the centre line misses but the office's edge hits
        public void BoxOnlyTheOfficeEdgeHits_IsRefused()
        {
            var room = SpatialRooms.Office();
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            CeilingPlacement clean;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(room, user, Fp, s, out clean), clean.explanation);

            // Half way down, a 6 cm box 0.45 m along the office's width from its centre line: inside
            // the office box (half width 0.5 m), 0.45 m or more from the line the old rule tested.
            RootPose mid = CeilingPlacementEvaluator.PathPose(clean.DockPose, clean.InspectionPose, 0.5f);
            OrientedRect rect = SpatialGeometry.Footprint(mid.position, mid.rotation, 1f, Fp, s.footprintCenterOffset);
            Vector2 c = rect.center + rect.axisX * 0.45f;
            room.AddFurniture("SHELF", new Vector3(c.x, mid.position.y + 0.15f, c.y), new Vector3(0.06f, 0.10f, 0.06f));
            FurnitureBox shelf = Last(room);

            Assert.IsFalse(OldCentreLineBlocked(room, clean.dockPosition, clean.inspectionPosition, true), "relaxed line rule: accepted");
            Assert.IsFalse(OldCentreLineBlocked(room, clean.dockPosition, clean.inspectionPosition, false), "full line rule with 0.30 m: accepted too");
            Assert.IsFalse(FootprintAtRestBlocked(room, clean.InspectionPose, s));
            Assert.That(FirstRealContact(shelf, clean.DockPose, clean.InspectionPose, s), Is.GreaterThanOrEqualTo(0f), "yet the office passes through it");

            CeilingPathCheck check;
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(room, user, clean.DockPose, clean.InspectionPose, Fp, s, out check));
            Assert.AreEqual(CeilingPathBlock.Furniture, check.blockedBy);
            Assert.AreEqual("SHELF", check.blockingLabel);

            // The evaluator moves on to a dock whose path really is clear.
            CeilingPlacement p;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(room, user, Fp, s, out p), p.explanation);
            Assert.IsTrue(p.path.clear);
            Assert.That((p.dockPosition - clean.dockPosition).magnitude + (p.inspectionPosition - clean.inspectionPosition).magnitude,
                        Is.GreaterThan(0.05f), "not the blocked path again");
            Assert.AreEqual(-1f, FirstRealContact(shelf, p.DockPose, p.InspectionPose, s), 1e-6f);
        }

        [Test]
        public void FurnishedRooms_StillYieldCandidates_OnBothSides()
        {
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            var all = new List<CeilingPlacement>();
            string summary;

            Assert.That(CeilingPlacementEvaluator.EvaluateAll(SpatialRooms.Office(), user, Fp, s, all, out summary), Is.GreaterThan(0), summary);
            bool right = false, left = false;
            foreach (CeilingPlacement p in all)
            {
                Assert.IsTrue(p.valid);
                Assert.IsTrue(p.path.clear);
                if (p.side > 0) right = true; else left = true;
            }
            Assert.IsTrue(right && left, "a desk in front leaves both sides free");

            // The room RoomPlacementService.UseSyntheticTestRoom builds: 4 x 4 m, ceiling 2.5 m above
            // the floor, the desk centred 0.75 m ahead, here with the floor 1.14 m below the eyes so
            // the desk top is 0.39 m below them.
            // CHANGED (worn pass 1): with the old 0.35 m drop the desk top was 4 cm under the pose and
            // the search had to raise it. The default drop is now 0.25 m: the office stops 14 cm over
            // the desk top without a raise. The vertical margin is unchanged; the raise is still
            // there for a higher desk (next block).
            float floorY = user.head.y - 1.14f;
            var play = SyntheticRoomGeometry.Rectangle(0f, 0f, 4f, 4f, floorY, floorY + 2.5f);
            play.AddFurniture("TABLE", new Vector3(0f, floorY + 0.375f, 0.75f), new Vector3(1.4f, 0.75f, 0.7f));
            CeilingPlacement q;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(play, user, Fp, s, out q), q.explanation);
            Assert.IsTrue(q.path.clear);
            Assert.AreEqual(user.head.y - s.inspectDropBelowEye, q.inspectionPosition.y, 1e-4f, "the normal lowering, no raise needed");
            Assert.That(q.inspectionPosition.y - (floorY + 0.75f), Is.GreaterThan(s.sweepMarginVertical + 0.05f), "14 cm over the desk");

            // A desk 4 cm under the default pose (top 0.29 m below the eyes): the margin does not give,
            // the pose is raised.
            var high = SyntheticRoomGeometry.Rectangle(0f, 0f, 4f, 4f, floorY, floorY + 2.5f);
            float top = user.head.y - s.inspectDropBelowEye - 0.04f;
            high.AddFurniture("TABLE", new Vector3(0f, (floorY + top) * 0.5f, 0.75f), new Vector3(1.4f, top - floorY, 0.7f));
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(high, user, Fp, s, out q), q.explanation);
            Assert.IsTrue(q.path.clear);
            Assert.That(q.inspectionPosition.y, Is.GreaterThan(user.head.y - s.inspectDropBelowEye + 0.04f), "raised clear of the desk top");
            Assert.That(q.inspectionPosition.y - top, Is.GreaterThan(s.sweepMarginVertical + 0.02f), "4 cm + a 5 cm raise = 9 cm over the desk");
        }

        [Test]
        public void MonitorOnTheDesk_IsKeptClearOf_AllTheWayDown()
        {
            var room = SpatialRooms.Office();
            // A monitor-sized box standing on the desk, rising to head height (0.75 to 1.2 m).
            room.AddFurniture("SCREEN", new Vector3(0f, 0.975f, 0.95f), new Vector3(0.6f, 0.45f, 0.2f));
            FurnitureBox screen = Last(room);
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            CeilingPlacement p;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(room, user, Fp, s, out p), p.explanation);
            Assert.IsTrue(p.path.clear);
            Assert.AreEqual("SCREEN", p.path.nearestFurnitureLabel, "the monitor is the nearest thing at the office's height");
            Assert.That(p.path.minFurnitureClearance, Is.GreaterThan(CeilingPlacementEvaluator.SampleMarginHorizontal(s)));
            Assert.AreEqual(-1f, FirstRealContact(screen, p.DockPose, p.InspectionPose, s), 1e-6f);
        }

        [Test] // widening the search, not the tolerance
        public void ClutterAtTheDeskEdge_MovesTheInspectionPoseBack_NotTheMargin()
        {
            var room = SpatialRooms.Office();
            // Two monitors side by side, 1.3 m wide, nearer the user (0.65 to 0.85 m ahead).
            room.AddFurniture("SCREEN", new Vector3(0f, 0.975f, 0.75f), new Vector3(1.3f, 0.45f, 0.2f));
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();

            // With only the first forward option there is no room beside the user on either side...
            // (the shallow descent is switched off here: this test is about the forward offsets; the
            // office stopping above the screens is covered in CeilingWornPassTests)
            var narrow = new CeilingPlacementSettings { inspectForwardOffsets = new[] { 0f }, shallowBaseAboveKeepOut = new float[0] };
            CeilingPlacement none;
            Assert.IsFalse(CeilingPlacementEvaluator.Evaluate(room, user, Fp, narrow, out none));
            StringAssert.Contains("no room beside you", none.explanation);
            StringAssert.Contains("SCREEN", none.explanation);

            // ...and with the wider search the pose moves back, with the same margins.
            CeilingPlacement p;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(room, user, Fp, s, out p), p.explanation);
            Assert.That(Vector3.Dot(p.inspectionPosition - user.head, user.forward), Is.LessThan(s.inspectForward - 0.1f), "further back than the first option");
            Assert.IsTrue(p.path.clear);
            Assert.That(p.path.minFurnitureClearance, Is.GreaterThan(CeilingPlacementEvaluator.SampleMarginHorizontal(s)));
        }

        [Test] // a pose that is fine to stand at is not accepted when the way to it is not
        public void BlockedWayToTheFirstInspectionPose_TriesTheNextPose_NeverTheBlockedOne()
        {
            var room = SpatialRooms.Office();
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            // A shelf board on each side, 6 to 10 cm above the top of the resting office (1.36 to
            // 1.40 m with the old 0.35 m drop, 1.46 to 1.50 m with today's 0.25 m), over the desk 1.0
            // to 1.1 m ahead. It is beyond the office's far end, so the first inspection pose is
            // usable on its own; but the office comes down through that height while still on the
            // high part of its path, where it needs 0.405 m, and it is too close.
            float shelfY = user.head.y - s.inspectDropBelowEye + s.officeHeight + 0.08f;
            room.AddFurniture("WALL_SHELF", new Vector3(0.75f, shelfY, 1.05f), new Vector3(0.2f, 0.04f, 0.1f));
            room.AddFurniture("WALL_SHELF", new Vector3(-0.75f, shelfY, 1.05f), new Vector3(0.2f, 0.04f, 0.1f));

            CeilingPlacement p;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(room, user, Fp, s, out p), p.explanation);
            Assert.IsTrue(p.path.clear);
            Assert.AreEqual(-1f, FirstRealContact(room.GetFurniture(1), p.DockPose, p.InspectionPose, s), 1e-6f);
            Assert.AreEqual(-1f, FirstRealContact(room.GetFurniture(2), p.DockPose, p.InspectionPose, s), 1e-6f);

            // The most preferred pose on that side (the first wide option, 0.15 m forward) was usable at
            // rest, but the way to it from the chosen dock is blocked, so a later pose was taken.
            RootPose first = Facing(user.head + user.Right * (p.side * s.inspectWideSideOptions[0]) + user.forward * s.inspectForward
                                    + Vector3.down * s.inspectDropBelowEye);
            CeilingPathCheck check;
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(room, user, first, first, Fp, s, out check), "usable at rest: " + check.Describe());
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(room, user, p.DockPose, first, Fp, s, out check));
            Assert.AreEqual("WALL_SHELF", check.blockingLabel);
            Assert.That((p.inspectionPosition - first.position).magnitude, Is.GreaterThan(0.04f));
        }

        [Test]
        public void TallShelfBesideTheUser_LeavesOnlyTheOtherSide_AndSaysWhy()
        {
            var room = SpatialRooms.Office();
            room.AddFurniture("STORAGE", new Vector3(0.8f, 1.0f, 0.2f), new Vector3(0.8f, 2.0f, 0.8f));
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            var all = new List<CeilingPlacement>();
            string summary;
            Assert.That(CeilingPlacementEvaluator.EvaluateAll(room, user, Fp, s, all, out summary), Is.GreaterThan(0), summary);
            foreach (CeilingPlacement p in all) Assert.AreEqual(-1, p.side, "nothing is offered on the shelf's side");
            StringAssert.Contains("no room beside you (STORAGE in the way)", summary);

            // A shelf on each side: nothing at all, and the words say which kind of failure it was.
            room.AddFurniture("STORAGE", new Vector3(-0.8f, 1.0f, 0.2f), new Vector3(0.8f, 2.0f, 0.8f));
            CeilingPlacement none;
            Assert.IsFalse(CeilingPlacementEvaluator.Evaluate(room, user, Fp, s, out none));
            Assert.IsFalse(none.valid);
            StringAssert.Contains("No clear ceiling dock", none.explanation);
            int docks = 2 * s.lateralOptions.Length * s.forwardOptions.Length;      // 32 since -0.3 forward was added
            StringAssert.Contains(docks + " no room beside you (STORAGE in the way)", none.explanation);
            StringAssert.Contains(" 0 path blocked by furniture", none.explanation);
        }

        [Test]
        public void LampOnTheWayDown_IsNamed_AndTheOtherSideIsUsed()
        {
            var room = SpatialRooms.Office();
            // A floor lamp, 0.3 m square and 2 m tall, at the right end of the desk.
            room.AddFurniture("LAMP", new Vector3(1.05f, 1.0f, 0.75f), new Vector3(0.3f, 2.0f, 0.3f));
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            var all = new List<CeilingPlacement>();
            string summary;
            Assert.That(CeilingPlacementEvaluator.EvaluateAll(room, user, Fp, s, all, out summary), Is.GreaterThan(0), summary);
            foreach (CeilingPlacement p in all) Assert.AreEqual(-1, p.side);
            StringAssert.Contains("path blocked by furniture (first: LAMP)", summary);

            // The same lamp on both sides: refused, with the path and the label for the caller to show.
            room.AddFurniture("LAMP", new Vector3(-1.05f, 1.0f, 0.75f), new Vector3(0.3f, 2.0f, 0.3f));
            CeilingPlacement none;
            Assert.IsFalse(CeilingPlacementEvaluator.Evaluate(room, user, Fp, s, out none));
            StringAssert.Contains("path blocked by furniture (first: LAMP)", none.explanation);
            Assert.IsFalse(none.path.clear);
            Assert.AreEqual(CeilingPathBlock.Furniture, none.path.blockedBy);
            Assert.AreEqual("LAMP", none.path.blockingLabel);
        }

        [Test]
        public void UserKeepOut_IsNeverEntered()
        {
            var room = SpatialRooms.Office();
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();

            // 1. Every candidate offered stays outside the cylinder for the whole descent.
            var all = new List<CeilingPlacement>();
            string summary;
            Assert.That(CeilingPlacementEvaluator.EvaluateAll(room, user, Fp, s, all, out summary), Is.GreaterThan(0), summary);
            foreach (CeilingPlacement p in all)
                Assert.That(p.path.minUserClearance, Is.GreaterThan(s.userKeepOutRadius), "never closer to the head's axis than the radius");

            // 2. Yesterday's inspection pose (0.6 m to the side) puts the office's near edge about
            //    0.15 m from the head's axis at shoulder height: refused, for the user.
            CeilingPathCheck check;
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(room, user, Facing(new Vector3(1.0f, 2.1f, 0.3f)),
                                                               Facing(new Vector3(0.6f, 0.85f, 0.15f)), Fp, s, out check));
            Assert.AreEqual(CeilingPathBlock.User, check.blockedBy);
            Assert.That(check.minUserClearance, Is.InRange(0.10f, 0.35f));

            // 3. With only the close options to try there is no candidate, and the words say why.
            //    (No shallow descent here either: above the keep-out the close options would pass.)
            var close = new CeilingPlacementSettings { inspectWideSideOptions = new float[0], shallowBaseAboveKeepOut = new float[0] };
            CeilingPlacement none;
            Assert.IsFalse(CeilingPlacementEvaluator.Evaluate(room, user, Fp, close, out none));
            StringAssert.Contains("no room beside you (too close to you)", none.explanation);

            // 4. A path straight across the user at head height is refused.
            var empty = EmptyRoom();
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(empty, user, Unrotated(0.9f, 1.30f, 0f), Unrotated(-0.9f, 1.30f, 0f), Fp, s, out check));
            Assert.AreEqual(CeilingPathBlock.User, check.blockedBy);
            Assert.That(check.blockedAtFraction, Is.InRange(0.001f, 0.5f));
            // CHANGED (safety pass 2): this used to assert that the same path with the office base above
            // the top of the cylinder (1.40 m) was ACCEPTED, that is, that the office may pass straight
            // over the user's head. It may not: above the cylinder the "never overhead" radius applies.
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(empty, user, Unrotated(0.9f, 1.40f, 0f), Unrotated(-0.9f, 1.40f, 0f), Fp, s, out check));
            Assert.AreEqual(CeilingPathBlock.User, check.blockedBy);
            // The same crossing 0.75 m in front of the user (nearest edge 0.52 m from the head's axis):
            // refused inside the cylinder's height, accepted above it.
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(empty, user, Unrotated(0.9f, 1.40f, 0.75f), Unrotated(-0.9f, 1.40f, 0.75f), Fp, s, out check), check.Describe());
            // And the overhead radius is honoured at its boundary: nearest edge 0.23 m, then 0.22 m (need 0.20 + 0.025).
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(empty, user, Unrotated(0.9f, 1.40f, 0.46f), Unrotated(-0.9f, 1.40f, 0.46f), Fp, s, out check), check.Describe());
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(empty, user, Unrotated(0.9f, 1.40f, 0.45f), Unrotated(-0.9f, 1.40f, 0.45f), Fp, s, out check));
        }

        [Test]
        public void HorizontalMargin_IsHonouredAtItsBoundary()
        {
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            // What a sample is tested with low down: the margin plus half a step (0.08 + 0.025).
            float need = CeilingPlacementEvaluator.SampleMarginHorizontal(s);
            Assert.AreEqual(s.sweepMarginHorizontal + 0.5f * s.sweepStep, need, 1e-6f);

            // The office comes straight down 0.3 m without turning: x 1.0 .. 2.0, z 0.77 .. 1.47.
            RootPose top = Unrotated(1.5f, 1.3f, 1.0f), bottom = Unrotated(1.5f, 1.0f, 1.0f);
            CeilingPathCheck check;
            foreach (float delta in new[] { 0.004f, -0.004f })
            {
                // A box at the office's height whose face is (need + delta) from the office's x = 1.0 face.
                float gap = need + delta;
                var room = EmptyRoom();
                room.AddFurniture("BOX", new Vector3(1.0f - gap - 0.1f, 1.1f, 1.1f), new Vector3(0.2f, 0.2f, 0.2f));
                bool clear = CeilingPlacementEvaluator.SweepPath(room, user, top, bottom, Fp, s, out check);
                Assert.AreEqual(delta > 0f, clear, "gap " + gap + ": " + check.Describe());
                Assert.AreEqual(gap, check.minFurnitureClearance, 1e-3f, "the clearance reported is the real gap");
            }
        }

        [Test]
        public void HighPartOfThePath_NeedsTheLargerClearance()
        {
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            float low = CeilingPlacementEvaluator.SampleMarginHorizontal(s);     // 0.105
            float high = low + s.descentClearance;                               // 0.405
            // Straight down 1.0 m: the base is "high" while it is more than 0.5 m above the end (y > 1.5).
            RootPose top = Unrotated(1.5f, 2.0f, 1.0f), bottom = Unrotated(1.5f, 1.0f, 1.0f);
            CeilingPathCheck check;

            // A box up at 1.8 to 1.9 m, beside the high part of the path.
            foreach (float delta in new[] { 0.005f, -0.005f })
            {
                var room = EmptyRoom();
                room.AddFurniture("HIGH", new Vector3(1.0f - (high + delta) - 0.1f, 1.85f, 1.1f), new Vector3(0.2f, 0.1f, 0.2f));
                bool clear = CeilingPlacementEvaluator.SweepPath(room, user, top, bottom, Fp, s, out check);
                Assert.AreEqual(delta > 0f, clear, check.Describe());
                if (!clear) Assert.That(check.blockedAtFraction, Is.LessThan(0.5f), "refused on the high part");
            }

            // The same gap that failed up there is fine beside the low part (1.0 to 1.2 m), where
            // only the sweep margin applies and the user can see the office.
            var lowRoom = EmptyRoom();
            lowRoom.AddFurniture("LOW", new Vector3(1.0f - (high - 0.005f) - 0.1f, 1.1f, 1.1f), new Vector3(0.2f, 0.2f, 0.2f));
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(lowRoom, user, top, bottom, Fp, s, out check), check.Describe());
        }

        [Test]
        public void VerticalMargin_IsHonouredAtItsBoundary()
        {
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            RootPose top = Unrotated(1.5f, 1.3f, 1.0f), bottom = Unrotated(1.5f, 1.0f, 1.0f);
            CeilingPathCheck check;
            foreach (float delta in new[] { 0.004f, -0.004f })
            {
                // A box directly under the office whose top is (margin + delta) below where the base stops.
                float boxTop = bottom.position.y - s.sweepMarginVertical - delta;
                var room = EmptyRoom();
                room.AddFurniture("UNDER", new Vector3(1.5f, (0.5f + boxTop) * 0.5f, 1.1f), new Vector3(0.4f, boxTop - 0.5f, 0.2f));
                bool clear = CeilingPlacementEvaluator.SweepPath(room, user, top, bottom, Fp, s, out check);
                Assert.AreEqual(delta > 0f, clear, "top at " + boxTop + ": " + check.Describe());
                if (!clear) Assert.That(check.blockedAtFraction, Is.GreaterThan(0.7f), "only the last samples reach down to it");
            }
        }

        [Test]
        public void ThinObstacle_CannotSlipBetweenSamples()
        {
            SeatedUser user = SpatialRooms.User();
            // Margins at zero, so only the sampling itself is under test.
            var s = new CeilingPlacementSettings { sweepMarginHorizontal = 0f, sweepMarginVertical = 0f };
            // A level, diagonal move of 1.0 m without turning, direction d = (0.6, 0.8) in XZ.
            RootPose a = Unrotated(1.0f, 1.0f, -1.0f), b = Unrotated(1.6f, 1.0f, -0.2f);
            Vector2 d = new Vector2(0.6f, 0.8f), n = new Vector2(0.8f, -0.6f);       // n: out of the swept region
            // The office corner at (+0.5, -0.35) from the footprint centre trails along one side of
            // the swept region. It starts at (1.5, -1.23).
            Vector2 corner = SpatialGeometry.Footprint(a.position, a.rotation, 1f, Fp, s.footprintCenterOffset).Corner(1);
            Assert.AreEqual(1.5f, corner.x, 1e-4f);
            Assert.AreEqual(-1.23f, corner.y, 1e-4f);
            float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;      // the plate's local Z along d, local X along n

            CeilingPathCheck check;
            int bareMisses = 0;
            for (int j = 0; j < 10; j++)
            {
                // A plate 3 cm thick along the travel direction, reaching just 3 mm into the swept
                // region, so the corner is inside it for under 4 cm of travel: less than one step.
                // Ten positions half a centimetre apart cover every phase against the 5 cm sampling.
                Vector2 onTrail = corner + d * (0.40f + 0.005f * j);
                Vector2 c = onTrail + n * (0.10f - 0.003f);
                var room = EmptyRoom();
                room.AddFurniture("PLATE", new Vector3(c.x, 1.15f, c.y), new Vector3(0.20f, 0.30f, 0.03f), yaw);
                FurnitureBox plate = Last(room);
                Assert.That(FirstRealContact(plate, a, b, s), Is.GreaterThanOrEqualTo(0f), "plate " + j + " really is in the way");
                Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(room, user, a, b, Fp, s, out check), "plate " + j + " slipped through: " + check.Describe());
                Assert.AreEqual("PLATE", check.blockingLabel);

                // What sampling WITHOUT the half-step padding would do: test the bare box at each sample.
                bool bareHit = false;
                int steps = Mathf.Max(1, Mathf.CeilToInt(check.travel / CeilingPlacementEvaluator.EffectiveSweepStep(s)));
                for (int i = 0; i <= steps && !bareHit; i++)
                {
                    RootPose p = CeilingPlacementEvaluator.PathPose(a, b, i / (float)steps);
                    bareHit = SpatialGeometry.Footprint(p.position, p.rotation, 1f, Fp, s.footprintCenterOffset).Overlaps(plate.Footprint);
                }
                if (!bareHit) bareMisses++;
            }
            Assert.That(bareMisses, Is.GreaterThan(0), "bare sampling does miss some of these plates; the padding is what catches them");

            // Control: the same plate 4 cm outside the swept region is not an obstacle and is not reported.
            {
                Vector2 c = corner + d * 0.42f + n * (0.10f + 0.04f);
                var room = EmptyRoom();
                room.AddFurniture("PLATE", new Vector3(c.x, 1.15f, c.y), new Vector3(0.20f, 0.30f, 0.03f), yaw);
                Assert.AreEqual(-1f, FirstRealContact(Last(room), a, b, s), 1e-6f);
                Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(room, user, a, b, Fp, s, out check), check.Describe());
                Assert.AreEqual(0.04f, check.minFurnitureClearance, 0.005f);
            }

            // The step can be set no coarser than MaxSweepStep, and a sample every step is guaranteed.
            var coarse = new CeilingPlacementSettings { sweepStep = 5f };
            Assert.AreEqual(CeilingPlacementEvaluator.MaxSweepStep, CeilingPlacementEvaluator.EffectiveSweepStep(coarse), 1e-6f);
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(EmptyRoom(), user, a, b, Fp, new CeilingPlacementSettings(), out check));
            Assert.That(check.sampleCount, Is.GreaterThanOrEqualTo(21), "1.0 m at 0.05 m: both ends and at least 19 between");
        }

        [Test]
        public void Walls_AreKeptClearOf()
        {
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            var room = EmptyRoom();                      // walls at x = +-2.5
            CeilingPathCheck check;
            // Office face at x = 2.45: 5 cm from the wall, inside the margin.
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(room, user, Unrotated(1.95f, 1.3f, 1.0f), Unrotated(1.95f, 1.0f, 1.0f), Fp, s, out check));
            Assert.AreEqual(CeilingPathBlock.Wall, check.blockedBy);
            // Office face at x = 2.35: 15 cm from the wall.
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(room, user, Unrotated(1.85f, 1.3f, 1.0f), Unrotated(1.85f, 1.0f, 1.0f), Fp, s, out check), check.Describe());
            Assert.AreEqual(0.15f, check.minWallClearance, 1e-3f);
            // No room data: nothing can be shown clear, so nothing is.
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(null, user, Unrotated(1.85f, 1.3f, 1.0f), Unrotated(1.85f, 1.0f, 1.0f), Fp, s, out check));
            Assert.AreEqual(CeilingPathBlock.Unverifiable, check.blockedBy);
        }

        [Test]
        public void GoingUp_IsTheSamePathReversed()
        {
            var room = SpatialRooms.Office();
            room.AddFurniture("SCREEN", new Vector3(0f, 0.975f, 0.95f), new Vector3(0.6f, 0.45f, 0.2f));
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            CeilingPlacement p;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(room, user, Fp, s, out p), p.explanation);

            // Same poses in reverse order...
            for (int i = 0; i <= 10; i++)
            {
                float k = i / 10f;
                SpatialRooms.AssertPose(CeilingPlacementEvaluator.PathPose(p.DockPose, p.InspectionPose, k),
                                        CeilingPlacementEvaluator.PathPose(p.InspectionPose, p.DockPose, 1f - k), 1e-4f);
            }
            // ...so the same verdict and the same numbers.
            CeilingPathCheck down, up;
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(room, user, p.DockPose, p.InspectionPose, Fp, s, out down));
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(room, user, p.InspectionPose, p.DockPose, Fp, s, out up));
            Assert.AreEqual(down.sampleCount, up.sampleCount);
            Assert.AreEqual(down.travel, up.travel, 1e-4f);
            Assert.AreEqual(down.minFurnitureClearance, up.minFurnitureClearance, 1e-3f);
            Assert.AreEqual(down.minUserClearance, up.minUserClearance, 1e-3f);
            Assert.AreEqual(down.minWallClearance, up.minWallClearance, 1e-3f);

            // A blocked path is blocked in both directions.
            room.AddFurniture("LAMP", new Vector3(1.05f, 1.0f, 0.75f), new Vector3(0.3f, 2.0f, 0.3f));
            RootPose dock = Facing(new Vector3(1.0f, 2.1f, 0.3f)), inspect = Facing(new Vector3(0.85f, 0.85f, 0.15f));
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(room, user, dock, inspect, Fp, s, out down));
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(room, user, inspect, dock, Fp, s, out up));
            Assert.AreEqual("LAMP", down.blockingLabel);
            Assert.AreEqual("LAMP", up.blockingLabel);
        }

        [Test] // what is swept is what is travelled
        public void ThePodTravels_ExactlyTheValidatedPath_DownAndUp()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Ceiling), rig.core.StatusText);
            CeilingPlacement placement = rig.core.LastCeiling;
            Assert.IsTrue(placement.path.clear);
            Assert.IsTrue(rig.core.Confirm());
            rig.Run(7f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);

            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.RequestPodDescend());
            rig.Run(6f);
            Assert.AreEqual(CeilingPodState.Engaged, rig.pod.State);
            int down = rig.trace.Count;
            Assert.That(down, Is.GreaterThan(10), "the descent was recorded");
            Assert.IsTrue(rig.core.RequestPodRetract());
            rig.Run(6f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
            Assert.That(rig.trace.Count - down, Is.GreaterThan(10), "the retraction was recorded");

            // Every pose the root was given, going down and going up, lies on the swept path.
            Vector3 a = placement.dockPosition, ab = placement.inspectionPosition - placement.dockPosition;
            float lowestK = 1f, highestK = 0f;
            foreach (RootPose pose in rig.trace)
            {
                float k = Vector3.Dot(pose.position - a, ab) / ab.sqrMagnitude;
                Assert.That(k, Is.InRange(-1e-3f, 1f + 1e-3f));
                SpatialRooms.AssertPose(CeilingPlacementEvaluator.PathPose(placement.DockPose, placement.InspectionPose, k), pose, 0.001f);
                if (k < lowestK) lowestK = k;
                if (k > highestK) highestK = k;
            }
            Assert.That(lowestK, Is.LessThan(0.01f), "the trace reaches the dock");
            Assert.That(highestK, Is.GreaterThan(0.99f), "and the inspection pose");
            SpatialRooms.AssertPose(placement.InspectionPose, rig.trace[down - 1]);
            SpatialRooms.AssertPose(placement.DockPose, rig.trace[rig.trace.Count - 1]);
        }

        [Test]
        public void ThePathThatWasValidated_IsDescribed()
        {
            var s = new CeilingPlacementSettings();
            CeilingPlacement p;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(SpatialRooms.Office(), SpatialRooms.User(), Fp, s, out p), p.explanation);
            Assert.IsTrue(p.path.clear);
            Assert.AreEqual(CeilingPathBlock.None, p.path.blockedBy);
            Assert.That((p.path.start - p.dockPosition).magnitude, Is.LessThan(1e-5f));
            Assert.That((p.path.end - p.inspectionPosition).magnitude, Is.LessThan(1e-5f));
            Assert.That(p.path.travel, Is.GreaterThanOrEqualTo(p.descentDistance - 1e-4f));
            Assert.That(p.path.sampleCount, Is.GreaterThanOrEqualTo(Mathf.CeilToInt(p.descentDistance / s.sweepStep) + 1), "no step longer than sweepStep, both ends included");
            Assert.AreEqual(0.5f * s.sweepStep, p.path.samplePadding, 1e-6f);
            Assert.That(p.path.minUserClearance, Is.GreaterThan(s.userKeepOutRadius));
            StringAssert.Contains("samples", p.path.Describe());
            StringAssert.Contains("recognised furniture", p.path.Describe());
        }

        [Test]
        public void LowerCeiling_StillGivesASweptClearPath()
        {
            // 2.0 m: 0.8 m above the eyes. The dock hangs at 1.5 m, its top at 1.95 m.
            var room = SyntheticRoomGeometry.Rectangle(0f, 0f, 5f, 5f, 0f, 2.0f);
            room.AddFurniture("TABLE", new Vector3(0f, 0.375f, 0.8f), new Vector3(1.4f, 0.75f, 0.7f));
            CeilingPlacement p;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(room, SpatialRooms.User(), Fp, new CeilingPlacementSettings(), out p), p.explanation);
            Assert.AreEqual(1.5f, p.dockPosition.y, 1e-4f);
            Assert.IsTrue(p.path.clear);
        }

        [Test] // restoring a saved dock uses the same swept test
        public void ValidateDock_SweepsThePath_LikeEvaluate()
        {
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            CeilingPlacement found;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(SpatialRooms.Office(), user, Fp, s, out found), found.explanation);
            Assert.AreEqual(1, found.side);

            // Unchanged room: restored, with the same inspection pose and a described, clear path.
            CeilingPlacement restored;
            Assert.IsTrue(CeilingPlacementEvaluator.ValidateDock(SpatialRooms.Office(), user, found.DockPose, Fp, s, out restored), restored.explanation);
            SpatialRooms.AssertPose(found.InspectionPose, restored.InspectionPose);
            Assert.IsTrue(restored.path.clear);
            Assert.AreEqual(found.path.sampleCount, restored.path.sampleCount);

            // A lamp now stands on the way down: refused, naming it. (Every dock on this side fails in this room.)
            var lamp = SpatialRooms.Office();
            lamp.AddFurniture("LAMP", new Vector3(1.05f, 1.0f, 0.75f), new Vector3(0.3f, 2.0f, 0.3f));
            Assert.IsFalse(CeilingPlacementEvaluator.ValidateDock(lamp, user, found.DockPose, Fp, s, out restored));
            Assert.IsFalse(restored.valid);
            StringAssert.Contains("blocked by LAMP", restored.explanation);
            Assert.AreEqual(CeilingPathBlock.Furniture, restored.path.blockedBy);

            // The user now sits almost under the dock: refused (the "not overhead" rule still applies).
            SeatedUser moved = new SeatedUser(new Vector3(0.7f, 1.2f, 0.1f), Vector3.forward);
            Assert.IsFalse(CeilingPlacementEvaluator.ValidateDock(SpatialRooms.Office(), moved, found.DockPose, Fp, s, out restored));
        }
    }

    // ───────────────────────── transition machine ─────────────────────────

    [Category("MuseDesk")]
    public class SpatialTransitionMachineTests
    {
        static readonly RootPose Target = new RootPose(new Vector3(1.6f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), 1.25f);

        [Test]
        public void Move_EasesToTheExactTarget_AndRefusesASecondMove()
        {
            var rig = new SpatialRig(null);
            RootPose start = rig.root;
            Assert.AreEqual(TransitionStartResult.Started, rig.mover.TryMove(Target, 1f));
            Assert.AreEqual(TransitionStartResult.AlreadyRunning, rig.mover.TryMove(start, 1f));

            rig.clock.Advance(0.5f); rig.mover.Tick();
            Assert.That((rig.root.position - start.position).magnitude, Is.GreaterThan(0.1f));
            Assert.That((rig.root.position - Target.position).magnitude, Is.GreaterThan(0.1f));
            Assert.That(rig.root.scale, Is.InRange(1f, 1.25f));

            rig.clock.Advance(0.6f); rig.mover.Tick();
            Assert.IsFalse(rig.mover.IsRunning);
            Assert.IsTrue(rig.root.position.Equals(Target.position), "ends exactly on the target");
            Assert.AreEqual(Target.scale, rig.root.scale);
            Assert.AreEqual(1, rig.mover.CompletedMoves);
        }

        [Test] // fixture 8 (transition level): TransitionHeld refuses a start
        public void Held_RefusesToStart_AndLeavesTheRootAlone()
        {
            var rig = new SpatialRig(null) { held = true };
            RootPose start = rig.root;
            Assert.AreEqual(TransitionStartResult.Held, rig.mover.TryMove(Target, 1f));
            rig.clock.Advance(2f); rig.mover.Tick();
            Assert.AreEqual(0, rig.writes);
            Assert.IsTrue(rig.root.position.Equals(start.position));
            Assert.IsFalse(string.IsNullOrEmpty(rig.mover.LastRefusal));
        }

        [Test]
        public void BusyAgents_DelayTheMove_ThenItRuns()
        {
            var rig = new SpatialRig(null) { agentsBusy = true };
            Assert.AreEqual(TransitionStartResult.Started, rig.mover.TryMove(Target, 1f));
            Assert.AreEqual(TransitionPhase.WaitingForAgents, rig.mover.Phase);
            for (int i = 0; i < 20; i++) { rig.clock.Advance(0.1f); rig.mover.Tick(); }
            Assert.AreEqual(0, rig.writes, "the root does not move under a walking agent");
            rig.agentsBusy = false;
            for (int i = 0; i < 20; i++) { rig.clock.Advance(0.1f); rig.mover.Tick(); }
            Assert.IsFalse(rig.mover.IsRunning);
            SpatialRooms.AssertPose(Target, rig.root);
        }

        [Test]
        public void BusyAgents_BeyondTheBound_GiveTheMoveUp()
        {
            var rig = new SpatialRig(null) { agentsBusy = true };
            bool? reached = null;
            rig.mover.MoveFinished += r => reached = r;
            rig.mover.TryMove(Target, 1f);
            for (int i = 0; i < 80; i++) { rig.clock.Advance(0.1f); rig.mover.Tick(); }
            Assert.AreEqual(false, reached);
            Assert.AreEqual(0, rig.writes);
            Assert.IsFalse(rig.mover.IsRunning);
        }

        [Test]
        public void FinishNow_PutsTheRootOnItsTarget()
        {
            var rig = new SpatialRig(null);
            rig.mover.TryMove(Target, 2f);
            rig.clock.Advance(0.4f); rig.mover.Tick();
            rig.mover.FinishNow();          // app pause
            Assert.IsFalse(rig.mover.IsRunning);
            SpatialRooms.AssertPose(Target, rig.root);
        }
    }

    // ───────────────────────── director: flags, preview, modes ─────────────────────────

    [Category("MuseDesk")]
    public class SpatialDirectorTests
    {
        [Test]
        public void FlagsOff_NothingHappens()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            rig.flags.floorModeEnabled = false;
            rig.flags.ceilingModeEnabled = false;
            RootPose start = rig.root;

            Assert.IsFalse(rig.core.RequestMode(PlacementMode.Floor));
            Assert.IsFalse(rig.core.RequestMode(PlacementMode.Ceiling));
            Assert.IsFalse(rig.core.RequestMode(PlacementMode.Desk));
            Assert.IsFalse(rig.core.Confirm());
            rig.core.NextCandidate(); rig.core.Nudge(1f, 1f); rig.core.Rotate(30f); rig.core.Cancel();
            Assert.IsFalse(rig.core.RequestPodDescend());
            rig.Run(2f);

            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            Assert.AreEqual(DirectorPhase.Idle, rig.core.Phase);
            Assert.IsFalse(rig.core.IsRemote);
            Assert.IsFalse(rig.core.IsTransitioning);
            Assert.AreEqual(0, rig.writes);
            Assert.AreEqual(0, rig.changes);
            Assert.IsTrue(rig.root.position.Equals(start.position));
        }

        [Test]
        public void OnlyTheEnabledModeCanBeRequested()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            rig.flags.ceilingModeEnabled = false;
            Assert.IsFalse(rig.core.RequestMode(PlacementMode.Ceiling));
            Assert.AreEqual(DirectorPhase.Idle, rig.core.Phase);
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Floor));
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
        }

        [Test]
        public void Floor_PreviewDoesNotMove_ConfirmDoes_DeskRestoresExactly()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            RootPose desk = rig.root;

            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Floor));
            Assert.IsTrue(rig.core.PreviewIsDetected);
            Assert.That(rig.core.CandidateCount, Is.GreaterThanOrEqualTo(2));
            StringAssert.Contains("Detected floor spot 1 of", rig.core.StatusText);
            RootPose first = rig.core.PreviewPose;
            rig.core.NextCandidate();
            Assert.AreEqual(1, rig.core.CandidateIndex);
            Assert.That((rig.core.PreviewPose.position - first.position).magnitude, Is.GreaterThan(0.3f));
            rig.Run(2f);
            Assert.AreEqual(0, rig.writes, "cycling and waiting in the preview never moves the office");
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);

            RootPose chosen = rig.core.PreviewPose;
            Assert.IsTrue(rig.core.Confirm());
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);
            Assert.IsTrue(rig.core.IsRemote);
            Assert.IsTrue(rig.core.IsTransitioning);
            Assert.IsFalse(rig.core.RequestMode(PlacementMode.Desk), "no second move while one runs");
            rig.Run(4f);
            Assert.IsFalse(rig.core.IsTransitioning);
            SpatialRooms.AssertPose(chosen, rig.root);

            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Desk));
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            rig.Run(4f);
            Assert.IsFalse(rig.core.IsTransitioning);
            Assert.IsFalse(rig.core.IsRemote);
            Assert.IsTrue(rig.root.position.Equals(desk.position), "exactly the pose Desk was left at");
            Assert.IsTrue(rig.root.rotation.Equals(desk.rotation));
            Assert.AreEqual(desk.scale, rig.root.scale);
        }

        [Test]
        public void Cancel_LeavesEverythingAsItWas()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            rig.core.RequestMode(PlacementMode.Floor);
            rig.core.Cancel();
            rig.Run(1f);
            Assert.AreEqual(DirectorPhase.Idle, rig.core.Phase);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            Assert.AreEqual(0, rig.writes);
            Assert.IsFalse(rig.core.Confirm());
        }

        [Test]
        public void NudgedOffTheFloor_IsNotCalledDetected_AndCannotBeConfirmed()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            rig.core.RequestMode(PlacementMode.Floor);
            rig.core.Nudge(6f, 0f);                 // far outside the room
            Assert.IsFalse(rig.core.PreviewValid);
            Assert.IsFalse(rig.core.Confirm());
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            Assert.AreEqual(0, rig.writes);
            rig.core.ResetPlacement();
            Assert.IsTrue(rig.core.PreviewValid);
            rig.core.Rotate(20f);
            Assert.IsTrue(rig.core.PreviewValid, "a small turn on clear floor stays valid");
        }

        [Test] // fixtures 5 and 6: permission denied / missing room data -> manual placement, in words
        public void NoRoomData_OffersManualPlacement_InWords()
        {
            foreach (RoomAvailability a in new[] { RoomAvailability.PermissionDenied, RoomAvailability.NotLoadedYet, RoomAvailability.NoRoomSetup })
            {
                var room = SpatialRooms.Office();
                room.forcedAvailability = a;
                var rig = new SpatialRig(room);
                Assert.IsTrue(rig.core.RequestMode(PlacementMode.Floor));
                Assert.IsFalse(rig.core.PreviewIsDetected, "a guessed position is never presented as detected floor");
                StringAssert.Contains("place it by hand", rig.core.StatusText);
                StringAssert.DoesNotContain("Detected", rig.core.StatusText);
                Assert.AreEqual(0, rig.core.CandidateCount);

                rig.core.Nudge(0.2f, 0f, -0.1f);    // manual placement may also be raised and lowered
                Assert.IsTrue(rig.core.Confirm());
                rig.Run(4f);
                Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);
                StringAssert.Contains("by hand", rig.core.StatusText);
                Assert.IsNull(rig.core.BuildSave("b"), "a hand-placed spot has no room reference and is not saved");

                Assert.IsFalse(rig.core.RequestMode(PlacementMode.Ceiling), "Ceiling mode needs real ceiling geometry");
            }
            var noRoom = new SpatialRig(null);
            Assert.IsTrue(noRoom.core.RequestMode(PlacementMode.Floor));
            Assert.IsFalse(noRoom.core.PreviewIsDetected);
            StringAssert.Contains("place it by hand", noRoom.core.StatusText);
        }

        [Test] // fixture 3 through the director
        public void SmallRoom_OffersManualPlacement_AndSaysItIsUnchecked()
        {
            var rig = new SpatialRig(SyntheticRoomGeometry.Rectangle(0f, 0f, 1.6f, 1.6f, 0f, 2.4f));
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Floor));
            Assert.IsFalse(rig.core.PreviewIsDetected);
            StringAssert.Contains("not checked against your room", rig.core.StatusText);
        }

        [Test] // fixture 2 through the director
        public void NoCeiling_CeilingModeIsRefused_FloorStillWorks()
        {
            var rig = new SpatialRig(SpatialRooms.Office(ceiling: false));
            Assert.IsFalse(rig.core.RequestMode(PlacementMode.Ceiling));
            Assert.AreEqual(DirectorPhase.Idle, rig.core.Phase);
            StringAssert.Contains("Ceiling mode is not available", rig.core.StatusText);
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Floor));
            Assert.IsTrue(rig.core.PreviewIsDetected);
        }

        [Test]
        public void Held_BlocksConfirm_AndTheReturnToDesk()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            rig.core.RequestMode(PlacementMode.Floor);
            rig.held = true;
            Assert.IsFalse(rig.core.Confirm());
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            rig.held = false;
            Assert.IsTrue(rig.core.Confirm());
            rig.Run(4f);
            rig.held = true;
            Assert.IsFalse(rig.core.RequestMode(PlacementMode.Desk));
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);
            rig.Run(2f);
            Assert.IsFalse(rig.core.IsTransitioning);
        }

        [Test]
        public void BusyAgentsBeyondTheBound_KeepTheOldMode()
        {
            var rig = new SpatialRig(SpatialRooms.Office()) { agentsBusy = true };
            RootPose desk = rig.root;
            rig.core.RequestMode(PlacementMode.Floor);
            Assert.IsTrue(rig.core.Confirm());
            rig.Run(8f);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode, "the move was given up before the root moved");
            Assert.AreEqual(0, rig.writes);
            Assert.IsTrue(rig.root.position.Equals(desk.position));
            Assert.IsFalse(string.IsNullOrEmpty(rig.core.StatusText));
        }

        [Test]
        public void DeskNotPlacedYet_RefusesToLeaveDesk()
        {
            var rig = new SpatialRig(SpatialRooms.Office()) { deskReady = false };
            Assert.IsFalse(rig.core.RequestMode(PlacementMode.Floor));
            Assert.AreEqual(DirectorPhase.Idle, rig.core.Phase);
        }

        [Test]
        public void RoomChangeDuringPreview_RebuildsThePreview()
        {
            var room = SpatialRooms.Office();
            var rig = new SpatialRig(room);
            rig.core.RequestMode(PlacementMode.Floor);
            RootPose shown = rig.core.PreviewPose;
            room.AddFurniture("COUCH", new Vector3(shown.position.x, 0.25f, shown.position.z), new Vector3(1.0f, 0.5f, 1.0f));
            room.RaiseChanged();
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            Assert.That((rig.core.PreviewPose.position - shown.position).magnitude, Is.GreaterThan(0.2f), "the stale spot is not kept");
            Assert.AreEqual(0, rig.writes);
        }

        [Test] // fixture 7: recenter, Floor mode
        public void Recenter_InFloorMode_FollowsTheRoomReference()
        {
            var room = SpatialRooms.Office();
            var rig = new SpatialRig(room);
            rig.core.RequestMode(PlacementMode.Floor);
            rig.core.Confirm();
            rig.Run(4f);
            RootPose placed = rig.root;

            Pose oldRef = room.referencePose;
            Pose newRef = new Pose(oldRef.position + new Vector3(0.5f, 0f, 0.2f), Quaternion.Euler(0f, 30f, 0f));
            room.referencePose = newRef;
            rig.core.OnRecenter();

            RootPose expected = WorkspacePlacementCodec.ToWorld(newRef,
                WorkspacePlacementCodec.ToLocal(oldRef, placed.position), WorkspacePlacementCodec.ToLocal(oldRef, placed.rotation), placed.scale);
            SpatialRooms.AssertPose(expected, rig.root);
            Assert.IsFalse(rig.core.IsTransitioning);
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);
        }

        [Test]
        public void Recenter_DuringAMove_EndsTheMoveSafely()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            rig.core.RequestMode(PlacementMode.Floor);
            RootPose chosen = rig.core.PreviewPose;
            rig.core.Confirm();
            rig.Run(0.3f);
            Assert.IsTrue(rig.mover.IsMoving);
            rig.core.OnRecenter();          // reference unchanged: the office lands on its spot at once
            Assert.IsFalse(rig.mover.IsRunning);
            SpatialRooms.AssertPose(chosen, rig.root);
        }

        [Test]
        public void Pause_DuringAMove_LeavesNothingMidAir()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            rig.core.RequestMode(PlacementMode.Floor);
            RootPose chosen = rig.core.PreviewPose;
            rig.core.Confirm();
            rig.Run(0.3f);
            rig.core.OnPause();
            Assert.IsFalse(rig.core.IsTransitioning);
            SpatialRooms.AssertPose(chosen, rig.root);
        }
    }

    // ───────────────────────── ceiling pod ─────────────────────────

    [Category("MuseDesk")]
    public class SpatialCeilingPodTests
    {
        static SpatialRig Docked(out CeilingPlacement placement)
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Ceiling), rig.core.StatusText);
            placement = rig.core.LastCeiling;
            Assert.AreEqual(0, rig.writes, "the ceiling preview does not move the office");
            Assert.IsTrue(rig.core.Confirm());
            Assert.AreEqual(PlacementMode.Ceiling, rig.core.Mode);
            rig.Run(7f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
            SpatialRooms.AssertPose(placement.DockPose, rig.root);
            Assert.IsTrue(rig.core.IsRemote);
            Assert.IsFalse(rig.core.IsTransitioning);
            return rig;
        }

        static void Engage(SpatialRig rig, CeilingPlacement placement)
        {
            Assert.IsTrue(rig.core.RequestPodDescend());
            rig.Run(6f);
            Assert.AreEqual(CeilingPodState.Engaged, rig.pod.State);
            SpatialRooms.AssertPose(placement.InspectionPose, rig.root);
            Assert.IsFalse(rig.core.IsRemote, "engaged = within reach");
        }

        [Test]
        public void FullCycle_VisitsEveryStateInOrder()
        {
            CeilingPlacement placement;
            var rig = Docked(out placement);
            var seen = new List<CeilingPodState>();
            rig.pod.StateChanged += () => { if (seen.Count == 0 || seen[seen.Count - 1] != rig.pod.State) seen.Add(rig.pod.State); };

            Engage(rig, placement);
            Assert.IsTrue(rig.core.RequestPodRetract());
            rig.Run(6f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
            SpatialRooms.AssertPose(placement.DockPose, rig.root);

            CollectionAssert.AreEqual(new[]
            {
                CeilingPodState.PreparingToDescend, CeilingPodState.Descending, CeilingPodState.Stabilizing, CeilingPodState.Engaged,
                CeilingPodState.PreparingToRetract, CeilingPodState.Retracting, CeilingPodState.ReturningToDock, CeilingPodState.Docked,
            }, seen);
        }

        [Test] // fixture 8: retraction with an active document (TransitionHeld)
        public void Retract_WaitsWhileHeld_ThenGoesUp()
        {
            CeilingPlacement placement;
            var rig = Docked(out placement);
            Engage(rig, placement);

            rig.held = true;                         // the user is holding the decision paper
            int writes = rig.writes;
            Assert.IsTrue(rig.core.RequestPodRetract(), "the request is accepted and waits");
            rig.Run(5f);
            Assert.AreEqual(CeilingPodState.Engaged, rig.pod.State);
            Assert.IsTrue(rig.pod.RetractPending);
            Assert.AreEqual(writes, rig.writes, "the office is not pulled away from a held paper");
            StringAssert.Contains("paper", rig.pod.StatusText);

            rig.held = false;
            rig.Run(6f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
            SpatialRooms.AssertPose(placement.DockPose, rig.root);
        }

        [Test]
        public void Descend_IsRefusedWhileHeld()
        {
            CeilingPlacement placement;
            var rig = Docked(out placement);
            rig.held = true;
            Assert.IsFalse(rig.core.RequestPodDescend());
            rig.Run(2f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
        }

        [Test] // fixture 7: recenter during deployment
        public void Recenter_DuringDescent_EndsEngagedAtTheNewPose()
        {
            CeilingPlacement placement;
            var rig = Docked(out placement);
            var room = (SyntheticRoomGeometry)rig.core.Room;
            Assert.IsTrue(rig.core.RequestPodDescend());
            rig.Run(1.0f);
            Assert.AreEqual(CeilingPodState.Descending, rig.pod.State);
            Assert.IsTrue(rig.mover.IsMoving);

            Pose oldRef = room.referencePose;
            room.referencePose = new Pose(oldRef.position + new Vector3(0.3f, 0f, 0f), oldRef.rotation);
            rig.core.OnRecenter();

            Assert.AreEqual(CeilingPodState.Engaged, rig.pod.State);
            Assert.IsFalse(rig.mover.IsRunning);
            Assert.IsFalse(rig.core.IsTransitioning);
            SpatialRooms.AssertPose(rig.pod.InspectionPose, rig.root);
            Assert.AreEqual(placement.dockPosition.x + 0.3f, rig.pod.DockPose.position.x, 1e-3f, "the dock moved with the room");

            // And it can still go back up to the moved dock.
            Assert.IsTrue(rig.core.RequestPodRetract());
            rig.Run(6f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
            SpatialRooms.AssertPose(rig.pod.DockPose, rig.root);
        }

        [Test]
        public void Recenter_DuringRetraction_EndsDocked()
        {
            CeilingPlacement placement;
            var rig = Docked(out placement);
            Engage(rig, placement);
            rig.core.RequestPodRetract();
            rig.Run(1.0f);
            Assert.AreEqual(CeilingPodState.Retracting, rig.pod.State);
            rig.core.OnRecenter();
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
            SpatialRooms.AssertPose(rig.pod.DockPose, rig.root);
        }

        [Test]
        public void Pause_DuringDescent_EndsEngaged()
        {
            CeilingPlacement placement;
            var rig = Docked(out placement);
            rig.core.RequestPodDescend();
            rig.Run(1.0f);
            rig.core.OnPause();
            Assert.AreEqual(CeilingPodState.Engaged, rig.pod.State);
            SpatialRooms.AssertPose(placement.InspectionPose, rig.root);
        }

        [Test]
        public void BusyAgentsDuringDescent_RecoverToTheDock()
        {
            CeilingPlacement placement;
            var rig = Docked(out placement);
            rig.agentsBusy = true;
            Assert.IsTrue(rig.core.RequestPodDescend());
            int writes = rig.writes;
            rig.Run(6.7f);                           // prepare (0.4 s), then the bounded agent wait (6 s) runs out
            Assert.AreEqual(CeilingPodState.ErrorRecovery, rig.pod.State);
            Assert.AreEqual(writes, rig.writes, "the office never moved under the walking agents");
            rig.agentsBusy = false;
            rig.Run(8f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
            SpatialRooms.AssertPose(placement.DockPose, rig.root);
        }

        [Test]
        public void LeavingCeilingMode_ReturnsToTheExactDeskPose()
        {
            var rig0 = new SpatialRig(SpatialRooms.Office());
            RootPose desk = rig0.root;
            CeilingPlacement placement;
            var rig = Docked(out placement);
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Desk));
            rig.Run(5f);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            Assert.IsFalse(rig.pod.Active);
            Assert.IsTrue(rig.root.position.Equals(desk.position));
            Assert.IsTrue(rig.root.rotation.Equals(desk.rotation));
        }
    }

    // ───────────────────────── persistence ─────────────────────────

    [Category("MuseDesk")]
    public class SpatialPersistenceTests
    {
        const string Key = "test.spatial.placement";
        const string Build = "build-1";

        static RootPose PlaceOnFloor(SpatialRig rig)
        {
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Floor));
            Assert.IsTrue(rig.core.Confirm());
            rig.Run(4f);
            return rig.root;
        }

        [Test] // fixture 10: restart with a saved placement
        public void Restart_RestoresTheFloorPlacement()
        {
            var store = new MemoryPlacementStore();
            var first = new SpatialRig(SpatialRooms.Office());
            RootPose placed = PlaceOnFloor(first);
            SavedPlacement saved = first.core.BuildSave(Build);
            Assert.IsNotNull(saved);
            Assert.AreEqual((int)PlacementMode.Floor, saved.mode);
            WorkspacePlacementCodec.Save(store, Key, saved);

            // "Restart": a fresh director, the same room, the office back at the desk.
            var second = new SpatialRig(SpatialRooms.Office());
            RootPose desk = second.root;
            SavedPlacement loaded; string reason;
            Assert.IsTrue(WorkspacePlacementCodec.TryLoad(store, Key, Build, out loaded, out reason), reason);
            Assert.IsTrue(second.core.TryRestore(loaded, out reason), reason);
            second.Run(4f);
            Assert.AreEqual(PlacementMode.Floor, second.core.Mode);
            SpatialRooms.AssertPose(placed, second.root);

            // Desk still restores exactly after a restored placement.
            Assert.IsTrue(second.core.RequestMode(PlacementMode.Desk));
            second.Run(4f);
            Assert.IsTrue(second.root.position.Equals(desk.position));
        }

        [Test] // fixture 10: the saved pose follows the room reference across sessions
        public void Restart_WithAShiftedTrackingOrigin_StillLandsOnTheSameRoomSpot()
        {
            var first = new SpatialRig(SpatialRooms.Office());
            RootPose placed = PlaceOnFloor(first);
            SavedPlacement saved = first.core.BuildSave(Build);

            // Next session the same room sits 1 m further along X in world space.
            var room = SyntheticRoomGeometry.Rectangle(1f, 0f, 5f, 5f, 0f, 2.6f);
            var second = new SpatialRig(room);
            second.user = new SeatedUser(new Vector3(1f, 1.2f, 0f), Vector3.forward);
            // CHANGED (safety pass 2): the desk office moves with the user, as it does in reality. Before,
            // it was left 1 m to the user's side and the unswept move passed through the user.
            second.root = new RootPose(new Vector3(1f, 0.9f, 0.6f), Quaternion.Euler(0f, 180f, 0f), 1f);
            string reason;
            Assert.IsTrue(second.core.TryRestore(saved, out reason), reason);
            second.Run(4f);
            Assert.AreEqual(placed.position.x + 1f, second.root.position.x, 2e-3f);
            Assert.AreEqual(placed.position.z, second.root.position.z, 2e-3f);
        }

        [Test] // fixture 10: invalid saved placement -> Desk
        public void InvalidSavedPlacement_FallsBackToDesk()
        {
            var first = new SpatialRig(SpatialRooms.Office());
            RootPose placed = PlaceOnFloor(first);
            SavedPlacement saved = first.core.BuildSave(Build);
            string reason;

            // 1. Furniture now stands on the saved spot.
            var blocked = SpatialRooms.Office();
            blocked.AddFurniture("COUCH", new Vector3(placed.position.x, 0.3f, placed.position.z), new Vector3(1.2f, 0.6f, 1.2f));
            var rig = new SpatialRig(blocked);
            Assert.IsFalse(rig.core.TryRestore(saved, out reason));
            StringAssert.Contains("furniture", reason);
            AssertStillAtDesk(rig);

            // 2. A different room.
            var other = SpatialRooms.Office();
            other.roomId = "another-room";
            rig = new SpatialRig(other);
            Assert.IsFalse(rig.core.TryRestore(saved, out reason));
            StringAssert.Contains("different room", reason);
            AssertStillAtDesk(rig);

            // 3. The room is smaller now: the spot is outside the floor.
            rig = new SpatialRig(SyntheticRoomGeometry.Rectangle(0f, 0f, 1.6f, 1.6f, 0f, 2.6f));
            Assert.IsFalse(rig.core.TryRestore(saved, out reason));
            AssertStillAtDesk(rig);

            // 4. No room data at all.
            var denied = SpatialRooms.Office();
            denied.forcedAvailability = RoomAvailability.PermissionDenied;
            rig = new SpatialRig(denied);
            Assert.IsFalse(rig.core.TryRestore(saved, out reason));
            AssertStillAtDesk(rig);

            // 5. The mode's flag is off.
            rig = new SpatialRig(SpatialRooms.Office());
            rig.flags.floorModeEnabled = false;
            Assert.IsFalse(rig.core.TryRestore(saved, out reason));
            AssertStillAtDesk(rig);

            // 6. The user is holding a paper.
            rig = new SpatialRig(SpatialRooms.Office()) { held = true };
            Assert.IsFalse(rig.core.TryRestore(saved, out reason));
            AssertStillAtDesk(rig);
        }

        static void AssertStillAtDesk(SpatialRig rig)
        {
            rig.Run(1f);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            Assert.AreEqual(0, rig.writes);
            Assert.IsFalse(rig.core.IsTransitioning);
        }

        [Test] // fixture 10: unreadable / foreign records
        public void CorruptOrForeignRecords_AreRefused()
        {
            var store = new MemoryPlacementStore();
            SavedPlacement loaded; string reason;
            Assert.IsFalse(WorkspacePlacementCodec.TryLoad(store, Key, Build, out loaded, out reason));

            store.Write(Key, "{ this is not json");
            Assert.IsFalse(WorkspacePlacementCodec.TryLoad(store, Key, Build, out loaded, out reason));
            Assert.IsNull(loaded);

            var first = new SpatialRig(SpatialRooms.Office());
            PlaceOnFloor(first);
            SavedPlacement saved = first.core.BuildSave(Build);
            WorkspacePlacementCodec.Save(store, Key, saved);
            Assert.IsFalse(WorkspacePlacementCodec.TryLoad(store, Key, "build-2", out loaded, out reason), "another build's record is ignored");
            Assert.IsTrue(WorkspacePlacementCodec.TryLoad(store, Key, Build, out loaded, out reason));

            saved.version = 99;
            WorkspacePlacementCodec.Save(store, Key, saved);
            Assert.IsFalse(WorkspacePlacementCodec.TryLoad(store, Key, Build, out loaded, out reason));

            WorkspacePlacementCodec.Save(store, Key, null);
            Assert.IsNull(store.Read(Key), "saving 'nothing' deletes the record (Desk mode)");
        }

        [Test] // fixture 10: ceiling dock
        public void Restart_RestoresTheCeilingDock()
        {
            var first = new SpatialRig(SpatialRooms.Office());
            Assert.IsTrue(first.core.RequestMode(PlacementMode.Ceiling));
            CeilingPlacement placement = first.core.LastCeiling;
            first.core.Confirm();
            first.Run(7f);
            SavedPlacement saved = first.core.BuildSave(Build);
            Assert.IsNotNull(saved);
            Assert.IsTrue(saved.hasDock);

            var second = new SpatialRig(SpatialRooms.Office());
            string reason;
            Assert.IsTrue(second.core.TryRestore(saved, out reason), reason);
            second.Run(7f);
            Assert.AreEqual(PlacementMode.Ceiling, second.core.Mode);
            Assert.AreEqual(CeilingPodState.Docked, second.pod.State);
            SpatialRooms.AssertPose(placement.DockPose, second.root);

            // The ceiling is gone in the next session: Desk.
            var third = new SpatialRig(SpatialRooms.Office(ceiling: false));
            Assert.IsFalse(third.core.TryRestore(saved, out reason));
            Assert.AreEqual(PlacementMode.Desk, third.core.Mode);
            Assert.AreEqual(0, third.writes);
        }
    }

    // ───────────────────────── every move is swept; every move can be stopped ─────────────────────────

    /// <summary>
    /// Safety pass 2. The rig's desk pose is root (0, 0.9, 0.6) turned to face the user at
    /// (0, 1.2, 0): the office covers x -0.5 .. 0.5, z 0.13 .. 0.83, y 0.9 .. 1.35. Its near edge is
    /// 0.13 m from the head's vertical axis, inside the 0.35 m keep-out, as the real desk pose is.
    /// </summary>
    [Category("MuseDesk")]
    public class SpatialSweptMovesTests
    {
        static readonly Vector2 Fp = SpatialRooms.Footprint;
        static readonly RootPose Desk = new RootPose(new Vector3(0f, 0.9f, 0.6f), Quaternion.Euler(0f, 180f, 0f), 1f);

        static SyntheticRoomGeometry EmptyRoom() { return SyntheticRoomGeometry.Rectangle(0f, 0f, 5f, 5f, 0f, 2.6f); }
        static RootPose At(float x, float y, float z) { return new RootPose(new Vector3(x, y, z), Desk.rotation, 1f); }
        static PathSweepOptions FromDesk() { var o = default(PathSweepOptions); o.fromDesk = true; return o; }

        /// <summary>An 8 x 8 m empty room, and a floor preview nudged 1 m further out, so that there is
        /// free floor between the desk office and the spot to stand an obstacle on (well clear of both).</summary>
        static SyntheticRoomGeometry BigRoom() { return SyntheticRoomGeometry.Rectangle(0f, 0f, 8f, 8f, 0f, 2.6f); }

        static RootPose PreviewFarSpot(SpatialRig rig)
        {
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Floor));
            Assert.AreEqual(1, rig.core.GetCandidate(0).side, "on the user's right");
            rig.core.Nudge(1.0f, 0f);
            Assert.IsTrue(rig.core.PreviewValid, rig.core.StatusText);
            return rig.core.PreviewPose;
        }

        /// <summary>The pose lies on one of the route's legs (1 mm, and the yaw of that point of the leg).</summary>
        static void AssertOnRoute(WorkspaceRoute route, RootPose pose, string what)
        {
            float best = float.MaxValue;
            for (int l = 0; l < route.LegCount; l++)
            {
                RootPose a = route[l], b = route[l + 1];
                Vector3 ab = b.position - a.position;
                float k = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(pose.position - a.position, ab) / ab.sqrMagnitude) : 0f;
                RootPose q = CeilingPlacementEvaluator.PathPose(a, b, k);
                float err = (q.position - pose.position).magnitude + Quaternion.Angle(q.rotation, pose.rotation) * 0.002f;
                if (err < best) best = err;
            }
            Assert.That(best, Is.LessThan(0.002f), what);
        }

        static void AssertOnRoute(WorkspaceRoute route, List<RootPose> trace, string what)
        {
            Assert.That(trace.Count, Is.GreaterThan(5), what + ": the move was recorded");
            for (int i = 0; i < trace.Count; i++) AssertOnRoute(route, trace[i], what + ", write " + i);
        }

        static SpatialRig Docked(SyntheticRoomGeometry room, out CeilingPlacement placement)
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

        // ── item 1: the first trip up to the dock ──

        [Test]
        public void FirstTripToTheDock_IsSwept_RisesBeforeItTravels_AndTheMoverTravelsExactlyThat()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Ceiling), rig.core.StatusText);
            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.Confirm(), rig.core.StatusText);
            WorkspaceRoute route = rig.core.LastRoute;
            CeilingPlacement placement = rig.core.LastCeiling;
            Assert.IsTrue(rig.core.LastRouteCheck.clear, "the way up was swept and found clear");

            // Two legs: straight up at the desk, without turning, until the base is above the
            // keep-out cylinder (eyes + 0.25 m); only then across and up to the dock.
            Assert.AreEqual(3, route.count);
            SpatialRooms.AssertPose(Desk, route[0]);
            Assert.AreEqual(Desk.position.x, route[1].position.x, 1e-5f);
            Assert.AreEqual(Desk.position.z, route[1].position.z, 1e-5f);
            Assert.AreEqual(1.2f + 0.25f, route[1].position.y, 1e-4f);
            Assert.That(Quaternion.Angle(Desk.rotation, route[1].rotation), Is.LessThan(0.01f), "no turn on the way up");
            SpatialRooms.AssertPose(placement.DockPose, route[2]);

            rig.Run(7f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
            SpatialRooms.AssertPose(placement.DockPose, rig.root);
            AssertOnRoute(route, rig.trace, "first trip");

            // Below the top of the keep-out the office never turned and never moved sideways.
            foreach (RootPose p in rig.trace)
            {
                if (p.position.y >= 1.2f + 0.25f - 1e-3f) continue;
                Assert.AreEqual(Desk.position.x, p.position.x, 1e-4f);
                Assert.AreEqual(Desk.position.z, p.position.z, 1e-4f);
                Assert.That(Quaternion.Angle(Desk.rotation, p.rotation), Is.LessThan(0.05f));
            }
        }

        [Test]
        public void FirstTripToTheDock_Blocked_IsRefusedWithWords_AndNothingMoves()
        {
            var room = SpatialRooms.Office();
            var rig = new SpatialRig(room);
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Ceiling), rig.core.StatusText);
            // A lamp hanging over the left half of the desk office, 1.65 to 1.75 m: clear of every
            // descent on the right, but the office would rise into it.
            room.AddFurniture("LAMP", new Vector3(-0.3f, 1.7f, 0.5f), new Vector3(0.1f, 0.1f, 0.1f));
            Assert.IsFalse(rig.core.Confirm());
            StringAssert.Contains("blocked by LAMP", rig.core.StatusText);
            StringAssert.Contains("Next", rig.core.StatusText);
            Assert.AreEqual(CeilingPathBlock.Furniture, rig.core.LastRouteCheck.blockedBy);
            rig.Run(3f);
            Assert.AreEqual(0, rig.writes);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            Assert.IsFalse(rig.pod.Active);
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase, "the preview stays open: choose again or cancel");
        }

        [Test]
        public void DeskStartRule_Boundaries()
        {
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            var empty = EmptyRoom();
            CeilingPathCheck check;
            RootPose up = At(0f, 1.45f, 0.6f);

            // The desk pose is inside the keep-out: as an ordinary path the rise is refused at once...
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(empty, user, Desk, up, Fp, s, out check));
            Assert.AreEqual(CeilingPathBlock.User, check.blockedBy);
            Assert.AreEqual(0f, check.blockedAtFraction, 1e-6f);
            // ...and as a path that starts at the desk it is excused, because it never comes closer.
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(empty, user, Desk, up, Fp, s, FromDesk(), out check), check.Describe());
            // The same path travelled downward (ending at the desk) is the same sweep.
            var toDesk = default(PathSweepOptions); toDesk.toDesk = true;
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(empty, user, up, Desk, Fp, s, toDesk, out check), check.Describe());

            // It may come at most deskApproachAllowance (0.05 m) closer than its desk distance (0.13 m).
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(empty, user, Desk, At(0f, 0.9f, 0.56f), Fp, s, FromDesk(), out check), check.Describe());
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(empty, user, Desk, At(0f, 0.9f, 0.54f), Fp, s, FromDesk(), out check));
            Assert.AreEqual(CeilingPathBlock.User, check.blockedBy);

            // The excuse ends after deskStartRegion of travel, whether the keep-out was left or not.
            var shortRegion = new CeilingPlacementSettings { deskStartRegion = 0.30f };
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(empty, user, Desk, up, Fp, shortRegion, FromDesk(), out check));
            Assert.AreEqual(CeilingPathBlock.User, check.blockedBy);
            Assert.That(check.blockedAtFraction, Is.InRange(0.5f, 0.7f), "0.30 m into a 0.55 m rise");

            // Once the keep-out has been left it applies: the office may not come back into it.
            WorkspaceRoute outAndBack = new WorkspaceRoute();
            outAndBack.Add(Desk); outAndBack.Add(At(1.5f, 0.9f, 0.6f)); outAndBack.Add(At(0.6f, 0.9f, 0.6f));
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(empty, user, Desk, At(1.5f, 0.9f, 0.6f), Fp, s, FromDesk(), out check), check.Describe());
            Assert.IsFalse(CeilingPlacementEvaluator.SweepRoute(empty, user, outAndBack, Fp, s, FromDesk(), out check));
            Assert.AreEqual(CeilingPathBlock.User, check.blockedBy);
            Assert.That(check.blockedAtFraction, Is.GreaterThan(0.6f), "on the way back");

            // Furniture the office already stands against at the desk (a screen 2 cm behind it) is
            // excused while the office leaves it, and only then.
            var screen = EmptyRoom();
            screen.AddFurniture("SCREEN", new Vector3(0f, 1.0f, 0.95f), new Vector3(0.6f, 0.45f, 0.2f));
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(screen, user, Desk, up, Fp, s, out check));
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(screen, user, Desk, up, Fp, s, FromDesk(), out check), check.Describe());
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(screen, user, Desk, At(0f, 0.9f, 0.66f), Fp, s, FromDesk(), out check), "moving INTO the screen");
            Assert.AreEqual("SCREEN", check.blockingLabel);
            // A desk top the office stands just above (2 cm, inside the vertical margin): it may rise, not sink.
            var desk = EmptyRoom();
            desk.AddFurniture("TABLE", new Vector3(0f, 0.44f, 0.8f), new Vector3(1.4f, 0.88f, 0.7f));
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(desk, user, Desk, up, Fp, s, FromDesk(), out check), check.Describe());
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(desk, user, Desk, At(0f, 0.8f, 0.6f), Fp, s, FromDesk(), out check), "sinking into the desk");
            Assert.AreEqual("TABLE", check.blockingLabel);
        }

        // ── item 2: floor moves and the way back ──

        [Test]
        public void FloorMove_StraightWhenClear_UpAndOverWhenTheDeskIsInTheWay()
        {
            // No furniture: the straight line is clear and is used.
            var open = new SpatialRig(EmptyRoom());
            Assert.IsTrue(open.core.RequestMode(PlacementMode.Floor));
            RootPose spot = open.core.PreviewPose;
            open.trace = new List<RootPose>();
            Assert.IsTrue(open.core.Confirm(), open.core.StatusText);
            Assert.AreEqual(2, open.core.LastRoute.count, "straight");
            open.Run(4f);
            SpatialRooms.AssertPose(spot, open.root);
            AssertOnRoute(open.core.LastRoute, open.trace, "straight floor move");

            // The user's desk under the office: a straight line would sink through its edge, so the
            // office rises, crosses above head height and comes down on the spot.
            var rig = new SpatialRig(SpatialRooms.Office());
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Floor));
            spot = rig.core.PreviewPose;
            CeilingPathCheck straight;
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(rig.core.Room, rig.user, Desk, spot, Fp, rig.core.ceilingSettings, FromDesk(), out straight));
            Assert.AreEqual("TABLE", straight.blockingLabel);
            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.Confirm(), rig.core.StatusText);
            WorkspaceRoute route = rig.core.LastRoute;
            Assert.AreEqual(4, route.count, "up, across, down");
            Assert.AreEqual(1.45f, route[1].position.y, 1e-4f);
            Assert.AreEqual(1.45f, route[2].position.y, 1e-4f);
            Assert.IsTrue(rig.core.LastRouteCheck.clear);
            rig.Run(4f);
            SpatialRooms.AssertPose(spot, rig.root);
            AssertOnRoute(route, rig.trace, "up-and-over floor move");

            // The way back is swept too, and is the same shape reversed.
            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Desk), rig.core.StatusText);
            Assert.AreEqual(4, rig.core.LastRoute.count);
            rig.Run(4f);
            Assert.IsTrue(rig.root.position.Equals(Desk.position));
            AssertOnRoute(rig.core.LastRoute, rig.trace, "way back");
        }

        [Test]
        public void FloorMove_BlockedBothWays_IsRefusedWithWords_LowObstacleIsCrossedOver()
        {
            // A waist-high bin between the desk and the spot, in a room with no desk volume: the
            // straight line is blocked, up and over is clear and is taken.
            var room = BigRoom();
            var rig = new SpatialRig(room);
            RootPose spot = PreviewFarSpot(rig);
            Vector3 mid = (Desk.position + spot.position) * 0.5f;
            room.AddFurniture("BIN", new Vector3(mid.x, 0.35f, mid.z), new Vector3(0.3f, 0.7f, 0.3f));
            CeilingPathCheck straight;
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(room, rig.user, Desk, spot, Fp, rig.core.ceilingSettings, FromDesk(), out straight));
            Assert.AreEqual("BIN", straight.blockingLabel);
            Assert.IsTrue(rig.core.Confirm(), rig.core.StatusText);
            Assert.AreEqual(4, rig.core.LastRoute.count);
            rig.Run(4f);
            SpatialRooms.AssertPose(spot, rig.root);

            // A cabinet 2.2 m tall in the same place: no way round, no way over. Refused, nothing moves.
            var room2 = BigRoom();
            var rig2 = new SpatialRig(room2);
            PreviewFarSpot(rig2);
            room2.AddFurniture("STORAGE", new Vector3(mid.x, 1.1f, mid.z), new Vector3(0.3f, 2.2f, 0.3f));
            Assert.IsFalse(rig2.core.Confirm());
            StringAssert.Contains("blocked by STORAGE", rig2.core.StatusText);
            StringAssert.Contains("Next", rig2.core.StatusText);
            rig2.Run(3f);
            Assert.AreEqual(0, rig2.writes);
            Assert.AreEqual(PlacementMode.Desk, rig2.core.Mode);
            Assert.IsFalse(rig2.core.IsTransitioning);

            // When the room reports it, the preview is rebuilt and no longer offers that spot.
            room2.RaiseChanged();
            Assert.AreEqual(DirectorPhase.Previewing, rig2.core.Phase);
            for (int i = 0; i < rig2.core.CandidateCount; i++)
            {
                WorkspaceRoute r; CeilingPathCheck c;
                // The planner the director moves with (the usual shapes, then the side steps).
                Assert.IsTrue(FloorRoutePlanner.Plan(room2, rig2.user, Desk, rig2.core.GetCandidate(i).Pose, Fp,
                                                                 rig2.core.ceilingSettings, FromDesk(), true, out r, out c), "every offered spot can be reached");
            }
        }

        [Test]
        public void WayBackToTheDesk_Blocked_IsRefused_AndTheOfficeStays()
        {
            var room = BigRoom();
            var rig = new SpatialRig(room);
            PreviewFarSpot(rig);
            Assert.IsTrue(rig.core.Confirm(), rig.core.StatusText);
            rig.Run(4f);
            RootPose spot = rig.root;
            Vector3 mid = (Desk.position + spot.position) * 0.5f;
            room.AddFurniture("STORAGE", new Vector3(mid.x, 1.1f, mid.z), new Vector3(0.3f, 2.2f, 0.3f));
            int writes = rig.writes;
            Assert.IsFalse(rig.core.RequestMode(PlacementMode.Desk));
            StringAssert.Contains("blocked by STORAGE", rig.core.StatusText);
            StringAssert.Contains("press Desk again", rig.core.StatusText);
            rig.Run(2f);
            Assert.AreEqual(writes, rig.writes);
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);
            SpatialRooms.AssertPose(spot, rig.root);
        }

        // ── item 3: the way down is checked again when it is asked for ──

        [Test]
        public void Descent_IsRevalidated_ForWhereTheUserSitsNow_AndRefusedWhenBlocked()
        {
            CeilingPlacement placement;
            var room = SpatialRooms.Office();
            var rig = Docked(room, out placement);

            // The user rolled the chair 0.3 m back: the office comes down beside where they sit NOW.
            rig.user = new SeatedUser(new Vector3(0f, 1.2f, -0.3f), Vector3.forward);
            Assert.IsTrue(rig.core.RequestPodDescend(), rig.core.StatusText);
            Assert.AreEqual(placement.inspectionPosition.z - 0.3f, rig.pod.InspectionPose.position.z, 1e-3f);
            SpatialRooms.AssertPose(placement.DockPose, rig.pod.DockPose);
            rig.trace = new List<RootPose>();
            rig.Run(6f);
            Assert.AreEqual(CeilingPodState.Engaged, rig.pod.State);
            SpatialRooms.AssertPose(rig.pod.InspectionPose, rig.root);
            AssertOnRoute(WorkspaceRoute.Straight(rig.pod.DockPose, rig.pod.InspectionPose), rig.trace, "the fresh path");
            Assert.IsTrue(rig.core.RequestPodRetract());
            rig.Run(6f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);

            // A lamp now stands in the way down (the scan has it, no room event was raised).
            room.AddFurniture("LAMP", new Vector3(1.05f, 1.0f, 0.75f), new Vector3(0.3f, 2.0f, 0.3f));
            int writes = rig.writes;
            Assert.IsFalse(rig.core.RequestPodDescend());
            StringAssert.Contains("LAMP", rig.core.StatusText);
            StringAssert.Contains("Desk", rig.core.StatusText);
            rig.Run(3f);
            Assert.AreEqual(writes, rig.writes);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);

            // A room event while docked says so without being asked; removing the lamp clears it.
            room.RaiseChanged();
            StringAssert.Contains("The room changed", rig.core.StatusText);
            room.ClearFurniture();
            room.RaiseChanged();
            Assert.AreEqual("", rig.core.StatusText);
            Assert.IsTrue(rig.core.RequestPodDescend(), rig.core.StatusText);
        }

        // ── item 4: recenter fails closed ──

        [Test]
        public void Recenter_WithADockThatNoLongerValidates_NeverMovesTheOffice()
        {
            CeilingPlacement placement;
            var room = SpatialRooms.Office();
            var rig = Docked(room, out placement);
            Assert.IsTrue(rig.core.RequestPodDescend());
            rig.Run(1.5f);
            Assert.AreEqual(CeilingPodState.Descending, rig.pod.State);
            RootPose where = rig.root;

            // The room reference moved 1 m: the dock would now be above the user's head.
            room.referencePose = new Pose(room.referencePose.position + new Vector3(-1f, 0f, 0f), room.referencePose.rotation);
            rig.trace = new List<RootPose>();
            rig.core.OnRecenter();
            rig.Run(3f);

            Assert.AreEqual(0, rig.trace.Count, "not one pose was written after the recenter");
            Assert.IsTrue(rig.root.position.Equals(where.position));
            Assert.IsFalse(rig.mover.IsRunning);
            Assert.IsFalse(rig.core.IsTransitioning);
            Assert.IsTrue(rig.core.PlacementNeedsConfirmation);
            StringAssert.Contains("recentered", rig.core.StatusText);
            StringAssert.Contains("Return to Desk", rig.core.StatusText);
            Assert.IsNull(rig.core.BuildSave("b"), "an unconfirmed placement is not saved");

            // The pod will not move it from there, whoever asks.
            Assert.IsFalse(rig.core.RequestPodDescend());
            Assert.IsFalse(rig.core.RequestPodRetract());
            Assert.IsFalse(rig.pod.RequestDescend());
            Assert.IsFalse(rig.pod.RequestRetract());
            rig.Run(3f);
            Assert.AreEqual(0, rig.trace.Count);

            // Desk is the way out, and it is a swept route like any other (or a refusal in words).
            if (rig.core.RequestMode(PlacementMode.Desk))
            {
                WorkspaceRoute route = rig.core.LastRoute;
                Assert.IsTrue(rig.core.LastRouteCheck.clear);
                rig.Run(5f);
                AssertOnRoute(route, rig.trace, "way out");
                Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            }
            else Assert.IsFalse(string.IsNullOrEmpty(rig.core.StatusText));
        }

        [Test]
        public void Recenter_InFloorMode_WithASpotThatNoLongerValidates_HoldsPosition()
        {
            var room = SpatialRooms.Office();
            var rig = new SpatialRig(room);
            rig.core.RequestMode(PlacementMode.Floor);
            Assert.IsTrue(rig.core.Confirm(), rig.core.StatusText);
            rig.Run(4f);
            RootPose placed = rig.root;

            // After the recenter the spot would lie outside the room.
            room.referencePose = new Pose(room.referencePose.position + new Vector3(3f, 0f, 0f), room.referencePose.rotation);
            rig.trace = new List<RootPose>();
            rig.core.OnRecenter();
            rig.Run(2f);
            Assert.AreEqual(0, rig.trace.Count);
            Assert.IsTrue(rig.root.position.Equals(placed.position));
            Assert.IsTrue(rig.core.PlacementNeedsConfirmation);
            StringAssert.Contains("recentered", rig.core.StatusText);

            // The same during the move out: it stops where it is, and the mode is Desk again.
            var room2 = SpatialRooms.Office();
            var rig2 = new SpatialRig(room2);
            rig2.core.RequestMode(PlacementMode.Floor);
            Assert.IsTrue(rig2.core.Confirm());
            rig2.Run(0.6f);
            Assert.IsTrue(rig2.mover.IsMoving);
            RootPose midAir = rig2.root;
            room2.referencePose = new Pose(room2.referencePose.position + new Vector3(3f, 0f, 0f), room2.referencePose.rotation);
            rig2.trace = new List<RootPose>();
            rig2.core.OnRecenter();
            rig2.Run(2f);
            Assert.AreEqual(0, rig2.trace.Count);
            Assert.IsTrue(rig2.root.position.Equals(midAir.position));
            Assert.IsFalse(rig2.core.IsTransitioning);
            Assert.IsTrue(rig2.core.PlacementNeedsConfirmation);
        }

        [Test]
        public void Restore_WithABlockedWay_StaysAtTheDesk()
        {
            var first = new SpatialRig(BigRoom());
            PreviewFarSpot(first);
            Assert.IsTrue(first.core.Confirm());
            first.Run(4f);
            SavedPlacement saved = first.core.BuildSave("b");
            Assert.IsNotNull(saved);
            Vector3 mid = (Desk.position + first.root.position) * 0.5f;

            // The spot itself is still clear floor, but a cabinet now stands on the way to it.
            var room = BigRoom();
            room.AddFurniture("STORAGE", new Vector3(mid.x, 1.1f, mid.z), new Vector3(0.3f, 2.2f, 0.3f));
            var rig = new SpatialRig(room);
            string reason;
            Assert.IsFalse(rig.core.TryRestore(saved, out reason));
            StringAssert.Contains("blocked by STORAGE", reason);
            rig.Run(2f);
            Assert.AreEqual(0, rig.writes);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
        }

        // ── item 5: stop ──

        [Test]
        public void Stop_DuringDescent_GoesBackUpAlongTheTravelledPath_AndEndsDocked()
        {
            CeilingPlacement placement;
            var rig = Docked(SpatialRooms.Office(), out placement);
            var seen = new List<CeilingPodState>();
            rig.pod.StateChanged += () => { if (seen.Count == 0 || seen[seen.Count - 1] != rig.pod.State) seen.Add(rig.pod.State); };
            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.RequestPodDescend());
            rig.Run(2.0f);
            Assert.AreEqual(CeilingPodState.Descending, rig.pod.State);
            int atStop = rig.trace.Count;
            RootPose lowest = rig.root;
            Assert.That((lowest.position - placement.dockPosition).magnitude, Is.GreaterThan(0.2f), "it had come some way down");
            Assert.That((lowest.position - placement.inspectionPosition).magnitude, Is.GreaterThan(0.2f), "and had not arrived");

            Assert.IsTrue(rig.core.StopMove());
            Assert.AreEqual(CeilingPodState.Retracting, rig.pod.State);
            Assert.IsFalse(rig.pod.RetractPending, "nothing is queued");
            Assert.IsFalse(rig.core.StopMove(), "a second Stop changes nothing");
            rig.Run(6f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
            SpatialRooms.AssertPose(placement.DockPose, rig.root);
            Assert.IsFalse(rig.core.IsTransitioning);

            // Every pose is on the validated path, and after the stop the office only went back up.
            WorkspaceRoute path = WorkspaceRoute.Straight(placement.DockPose, placement.InspectionPose);
            AssertOnRoute(path, rig.trace, "stopped descent");
            float last = lowest.position.y;
            for (int i = atStop; i < rig.trace.Count; i++)
            {
                Assert.That(rig.trace[i].position.y, Is.GreaterThanOrEqualTo(last - 1e-5f), "never lower than where it was stopped");
                last = rig.trace[i].position.y;
            }
            CollectionAssert.AreEqual(new[]
            {
                CeilingPodState.PreparingToDescend, CeilingPodState.Descending, CeilingPodState.Retracting,
                CeilingPodState.ReturningToDock, CeilingPodState.Docked,
            }, seen);

            // "Send back" while it comes down is the same Stop: at once, nothing queued.
            Assert.IsTrue(rig.core.RequestPodDescend());
            rig.Run(1.5f);
            Assert.IsTrue(rig.core.RequestPodRetract());
            Assert.AreEqual(CeilingPodState.Retracting, rig.pod.State);
            Assert.IsFalse(rig.pod.RetractPending);
            rig.Run(6f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
            // And "Bring" while it goes up is refused rather than queued.
            Assert.IsTrue(rig.core.RequestPodDescend());
            rig.Run(6f);
            Assert.IsTrue(rig.core.RequestPodRetract());
            rig.Run(1.2f);
            Assert.AreEqual(CeilingPodState.Retracting, rig.pod.State);
            Assert.IsFalse(rig.core.RequestPodDescend());
            Assert.IsFalse(rig.core.StopMove(), "going up is moving away from the user: it is left to finish");
            rig.Run(6f);
            Assert.AreEqual(CeilingPodState.Docked, rig.pod.State);
        }

        [Test]
        public void Stop_DuringADeskToFloorMove_ReturnsToTheDeskPose_AndTheModeStaysDesk()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            rig.core.RequestMode(PlacementMode.Floor);
            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.Confirm());
            WorkspaceRoute route = rig.core.LastRoute;
            rig.Run(1.2f);
            Assert.IsTrue(rig.mover.IsMoving);
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);
            RootPose where = rig.root;

            Assert.IsTrue(rig.core.StopMove());
            Assert.IsTrue(rig.core.IsTransitioning, "still moving: now on its way back");
            StringAssert.Contains("Stopped", rig.core.StatusText);
            rig.core.Tick();                                    // no time has passed: it has halted, not jumped
            SpatialRooms.AssertPose(where, rig.root, 0.001f);
            Assert.IsFalse(rig.core.StopMove());
            Assert.IsFalse(rig.core.RequestMode(PlacementMode.Floor), "no new move while it goes back");
            rig.Run(4f);

            Assert.IsFalse(rig.core.IsTransitioning);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            Assert.IsFalse(rig.core.IsRemote);
            Assert.IsTrue(rig.root.position.Equals(Desk.position), "exactly the desk pose");
            Assert.IsTrue(rig.root.rotation.Equals(Desk.rotation));
            Assert.IsNull(rig.core.BuildSave("b"));
            AssertOnRoute(route, rig.trace, "stopped floor move");

            // The way back from the floor can be stopped too: the office returns to the floor spot, mode Floor.
            rig.core.RequestMode(PlacementMode.Floor);
            Assert.IsTrue(rig.core.Confirm());
            rig.Run(4f);
            RootPose spot = rig.root;
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Desk));
            rig.Run(1.0f);
            Assert.IsTrue(rig.core.StopMove());
            rig.Run(4f);
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);
            SpatialRooms.AssertPose(spot, rig.root, 1e-4f);
            Assert.IsNotNull(rig.core.BuildSave("b"), "the floor placement is whole again");
        }

        [Test]
        public void Stop_DuringTheFirstTripUp_ReturnsToTheDesk_AndLeavesCeilingMode()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            rig.core.RequestMode(PlacementMode.Ceiling);
            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.Confirm());
            WorkspaceRoute route = rig.core.LastRoute;
            rig.Run(2.5f);
            Assert.AreEqual(CeilingPodState.Retracting, rig.pod.State);
            Assert.That(rig.root.position.y, Is.GreaterThan(1.0f));
            Assert.IsTrue(rig.core.StopMove());
            rig.Run(6f);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            Assert.IsFalse(rig.pod.Active);
            Assert.IsFalse(rig.core.IsTransitioning);
            Assert.IsTrue(rig.root.position.Equals(Desk.position));
            AssertOnRoute(route, rig.trace, "stopped first trip");
            StringAssert.Contains("Stopped", rig.core.StatusText);

            // Stopped during the wind-up, before anything moved.
            rig.core.RequestMode(PlacementMode.Ceiling);
            int writes = rig.writes;
            Assert.IsTrue(rig.core.Confirm(), rig.core.StatusText);
            rig.Run(0.2f);
            Assert.IsTrue(rig.core.StopMove());
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            rig.Run(2f);
            Assert.AreEqual(writes, rig.writes);
        }

        [Test]
        public void Stop_IsSafeAndIdempotent_InEveryPhase()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            // Desk, at rest.
            Assert.IsFalse(rig.core.StopMove());
            rig.core.Cancel();
            Assert.AreEqual(0, rig.changes);
            // Previewing: Stop does nothing, Cancel leaves the preview.
            rig.core.RequestMode(PlacementMode.Floor);
            Assert.IsFalse(rig.core.StopMove());
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            rig.core.Cancel();
            Assert.AreEqual(DirectorPhase.Idle, rig.core.Phase);
            // Waiting for agents (nothing has moved): the move is dropped.
            rig.agentsBusy = true;
            rig.core.RequestMode(PlacementMode.Floor);
            Assert.IsTrue(rig.core.Confirm());
            rig.Run(0.5f);
            Assert.IsTrue(rig.core.StopMove());
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            Assert.IsFalse(rig.core.IsTransitioning);
            Assert.AreEqual(0, rig.writes);
            rig.agentsBusy = false;
            // Floor, at rest.
            rig.core.RequestMode(PlacementMode.Floor);
            Assert.IsTrue(rig.core.Confirm());
            rig.Run(4f);
            int writes = rig.writes;
            Assert.IsFalse(rig.core.StopMove());
            rig.core.Cancel(); rig.core.Cancel();
            rig.Run(1f);
            Assert.AreEqual(writes, rig.writes);
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);

            // Ceiling: docked, the wind-up, engaged, settling.
            CeilingPlacement placement;
            var pod = Docked(SpatialRooms.Office(), out placement);
            Assert.IsFalse(pod.core.StopMove());
            Assert.IsTrue(pod.core.RequestPodDescend());
            pod.Run(0.2f);
            Assert.AreEqual(CeilingPodState.PreparingToDescend, pod.pod.State);
            Assert.IsTrue(pod.core.StopMove());
            Assert.AreEqual(CeilingPodState.Docked, pod.pod.State);
            pod.Run(2f);
            SpatialRooms.AssertPose(placement.DockPose, pod.root);
            Assert.IsTrue(pod.core.RequestPodDescend());
            pod.Run(6f);
            Assert.AreEqual(CeilingPodState.Engaged, pod.pod.State);
            Assert.IsFalse(pod.core.StopMove());
            Assert.IsTrue(pod.core.RequestPodRetract());
            pod.Run(0.2f);
            Assert.AreEqual(CeilingPodState.PreparingToRetract, pod.pod.State);
            Assert.IsTrue(pod.core.StopMove(), "the wind-up is called off");
            Assert.AreEqual(CeilingPodState.Engaged, pod.pod.State);
            pod.Run(3f);
            Assert.AreEqual(CeilingPodState.Engaged, pod.pod.State);
            SpatialRooms.AssertPose(placement.InspectionPose, pod.root);

            // The bare machine: Reverse and Abort when idle do nothing.
            Assert.IsFalse(pod.mover.Reverse());
            Assert.IsFalse(pod.mover.Abort("x"));
        }

        [Test]
        public void Stop_WhileAPaperIsHeld_HaltsInPlace_AndGoesBackOnlyWhenItIsPutDown()
        {
            // A mode change in flight.
            var rig = new SpatialRig(SpatialRooms.Office());
            rig.core.RequestMode(PlacementMode.Floor);
            Assert.IsTrue(rig.core.Confirm());
            rig.Run(1.0f);
            rig.held = true;                        // the user took a paper while the office was moving
            int writes = rig.writes;
            RootPose where = rig.root;
            Assert.IsTrue(rig.core.StopMove(), "Stop always stops");
            Assert.IsTrue(rig.mover.IsHalted);
            StringAssert.Contains("Put the paper down", rig.core.StatusText);
            rig.Run(3f);
            Assert.AreEqual(writes, rig.writes, "halted: the office is not taken away from the held paper");
            Assert.IsTrue(rig.root.position.Equals(where.position));
            Assert.IsTrue(rig.core.IsTransitioning, "Stop stays on the strip");
            Assert.IsFalse(rig.core.StopMove());
            rig.held = false;
            rig.Run(4f);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            Assert.IsTrue(rig.root.position.Equals(Desk.position));

            // The pod coming down.
            CeilingPlacement placement;
            var pod = Docked(SpatialRooms.Office(), out placement);
            Assert.IsTrue(pod.core.RequestPodDescend());
            pod.Run(2f);
            pod.held = true;
            writes = pod.writes;
            Assert.IsTrue(pod.core.StopMove());
            Assert.AreEqual(CeilingPodState.Retracting, pod.pod.State);
            StringAssert.Contains("Put the paper down", pod.pod.StatusText);
            pod.Run(3f);
            Assert.AreEqual(writes, pod.writes);
            pod.held = false;
            pod.Run(6f);
            Assert.AreEqual(CeilingPodState.Docked, pod.pod.State);
            SpatialRooms.AssertPose(placement.DockPose, pod.root);
        }

        [Test]
        public void TransitionMachine_ReversesAMultiLegRoute_Deterministically()
        {
            var rig = new SpatialRig(null);
            RootPose a = rig.root;
            WorkspaceRoute route = WorkspaceRoute.UpAndOver(a, new RootPose(new Vector3(1.5f, 0f, 0.5f), Quaternion.Euler(0f, -90f, 0f), 1f), 1.45f);
            Assert.AreEqual(4, route.count);
            rig.trace = new List<RootPose>();
            bool? reached = null;
            rig.mover.MoveFinished += r => reached = r;

            // A route that does not start where the root is, is refused.
            Assert.AreEqual(TransitionStartResult.OffPath, rig.mover.TryMoveRoute(WorkspaceRoute.Straight(route[1], route[3]), 2f));
            Assert.AreEqual(0, rig.writes);

            Assert.AreEqual(TransitionStartResult.Started, rig.mover.TryMoveRoute(route, 2f));
            for (int i = 0; i < 12; i++) { rig.clock.Advance(0.1f); rig.mover.Tick(); }      // 1.2 s of 2 s: on the second leg
            Assert.That(rig.root.position.y, Is.InRange(1.449f, 1.451f));
            int forward = rig.trace.Count;
            Assert.IsTrue(rig.mover.Reverse());
            Assert.IsTrue(rig.mover.IsReversing);
            Assert.IsTrue(rig.mover.Reverse(), "idempotent");
            for (int i = 0; i < 40 && rig.mover.IsRunning; i++) { rig.clock.Advance(0.1f); rig.mover.Tick(); }
            Assert.IsFalse(rig.mover.IsRunning);
            Assert.AreEqual(false, reached, "a stopped move did not reach its target");
            Assert.IsTrue(rig.root.position.Equals(a.position), "ends exactly where it started");
            Assert.IsTrue(rig.root.rotation.Equals(a.rotation));
            Assert.AreEqual(0, rig.mover.CompletedMoves);
            AssertOnRoute(route, rig.trace, "there and back");
            Assert.That(rig.trace.Count - forward, Is.GreaterThan(5), "the way back was eased, not a jump");

            // The same inputs give the same poses.
            var again = new SpatialRig(null);
            again.trace = new List<RootPose>();
            again.mover.TryMoveRoute(route, 2f);
            for (int i = 0; i < 12; i++) { again.clock.Advance(0.1f); again.mover.Tick(); }
            again.mover.Reverse();
            for (int i = 0; i < 40 && again.mover.IsRunning; i++) { again.clock.Advance(0.1f); again.mover.Tick(); }
            Assert.AreEqual(rig.trace.Count, again.trace.Count);
            for (int i = 0; i < rig.trace.Count; i++) Assert.IsTrue(rig.trace[i].position.Equals(again.trace[i].position));

            // A completed route ends exactly on its last pose.
            var whole = new SpatialRig(null);
            whole.mover.TryMoveRoute(route, 2f);
            for (int i = 0; i < 25; i++) { whole.clock.Advance(0.1f); whole.mover.Tick(); }
            Assert.IsTrue(whole.root.position.Equals(route[3].position));
            Assert.AreEqual(1, whole.mover.CompletedMoves);
        }

        // ── item 6: the mast and the mount ──

        [Test]
        public void Mast_AndTheSpaceUnderTheCeiling_AreSwept()
        {
            SeatedUser user = SpatialRooms.User();
            var s = new CeilingPlacementSettings();
            RootPose inspect = new RootPose(new Vector3(0.85f, 0.85f, 0.15f), SpatialGeometry.FaceUserYaw(new Vector3(0.85f, 0.85f, 0.15f), user.head), 1f);
            // A dock hanging 0.7 m below the ceiling, so there is a gap above the office (top at 2.35 m).
            RootPose dock = new RootPose(new Vector3(1.0f, 1.9f, 0.3f), SpatialGeometry.FaceUserYaw(new Vector3(1.0f, 1.9f, 0.3f), user.head), 1f);
            Vector3 mastTop = CeilingPlacementEvaluator.MastTop(dock, 2.6f, s);
            Assert.AreEqual(2.6f, mastTop.y, 1e-6f);
            PathSweepOptions withMast = CeilingPlacementEvaluator.PodPathOptions(mastTop);
            CeilingPathCheck check;

            var room = EmptyRoom();
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(room, user, dock, inspect, Fp, s, withMast, out check), check.Describe());

            // 1. A pendant lamp hanging in the gap between the docked office and the ceiling
            //    (2.45 to 2.6 m): the office box alone misses it; the volume up to the ceiling does not.
            room.AddFurniture("PENDANT", new Vector3(mastTop.x + 0.3f, 2.525f, mastTop.z), new Vector3(0.2f, 0.15f, 0.2f));
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(room, user, dock, inspect, Fp, s, out check), "without the mast rule it passed");
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(room, user, dock, inspect, Fp, s, withMast, out check));
            Assert.AreEqual("PENDANT", check.blockingLabel);
            Assert.That(check.blockedAtFraction, Is.LessThan(0.1f), "at the dock end");
            string label;
            Assert.IsTrue(CeilingPlacementEvaluator.MountBlocked(room, dock, mastTop, s, out label), "and it is inside the mount's clearance");
            Assert.AreEqual("PENDANT", label);

            // 2. The mast itself: the office standing at the inspection pose, a small box in mid-air
            //    on the line from its top to the mount. Only the mast touches it.
            var room2 = EmptyRoom();
            OrientedRect rest = SpatialGeometry.Footprint(inspect.position, inspect.rotation, 1f, Fp, s.footprintCenterOffset);
            Vector3 foot = new Vector3(rest.center.x, inspect.position.y + s.officeHeight, rest.center.y);
            Vector3 on = foot + (mastTop - foot) * 0.5f;
            room2.AddFurniture("MOBILE", on, new Vector3(0.06f, 0.06f, 0.06f));
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(room2, user, inspect, inspect, Fp, s, out check), check.Describe());
            Assert.IsFalse(CeilingPlacementEvaluator.SweepPath(room2, user, inspect, inspect, Fp, s, withMast, out check));
            Assert.IsTrue(check.blockedByMast);
            Assert.AreEqual("MOBILE", check.blockingLabel);
            // The same box 0.5 m to the side of the mast is not in its way.
            var room3 = EmptyRoom();
            room3.AddFurniture("MOBILE", on + new Vector3(0f, 0f, 0.5f), new Vector3(0.06f, 0.06f, 0.06f));
            Assert.IsTrue(CeilingPlacementEvaluator.SweepPath(room3, user, inspect, inspect, Fp, s, withMast, out check), check.Describe());

            // 3. Through the evaluator: a box hanging from the ceiling where the first dock's mount
            //    would be. That dock is not offered; what is offered has a clear mount and mast.
            CeilingPlacement clean;
            Assert.IsTrue(CeilingPlacementEvaluator.Evaluate(SpatialRooms.Office(), user, Fp, s, out clean), clean.explanation);
            Assert.That((clean.mastTop - CeilingPlacementEvaluator.MastTop(clean.DockPose, 2.6f, s)).magnitude, Is.LessThan(1e-5f));
            var hung = SpatialRooms.Office();
            hung.AddFurniture("FAN", new Vector3(clean.mastTop.x, 2.5f, clean.mastTop.z), new Vector3(0.3f, 0.2f, 0.3f));
            var all = new List<CeilingPlacement>();
            string summary;
            CeilingPlacementEvaluator.EvaluateAll(hung, user, Fp, s, all, out summary);
            foreach (CeilingPlacement p in all)
            {
                Assert.That((p.dockPosition - clean.dockPosition).magnitude, Is.GreaterThan(0.05f), "the dock under the fan is not offered");
                Assert.IsFalse(CeilingPlacementEvaluator.MountBlocked(hung, p.DockPose, p.mastTop, s, out label));
                Assert.IsTrue(p.path.clear);
            }
            StringAssert.Contains("dock blocked by furniture", summary);
            CeilingPlacement restored;
            Assert.IsFalse(CeilingPlacementEvaluator.ValidateDock(hung, user, clean.DockPose, Fp, s, out restored));
            StringAssert.Contains("FAN", restored.explanation);
        }
    }

    // ───────────────────────── isolation from the backend ─────────────────────────

    [Category("MuseDesk")]
    public class SpatialIsolationTests
    {
        static readonly string[] ForbiddenNamespaces =
        {
            "MuseDesk.Networking", "MuseDesk.Phase2", "MuseDesk.Models", "MuseDesk.Documents", "MuseDesk.Orchestration",
        };

        static readonly string[] ForbiddenTokens =
        {
            "SessionController", "SessionReducer", "Phase2Bootstrap", "Phase2Reducer", "IAgentGateway", "Gateway",
            "Reducer", "MuseDesk.Networking", "MuseDesk.Phase2", "MuseDesk.Models",
        };

        [Test] // fixture 9: backend update during movement
        public void CoreAssembly_HoldsNoReferenceToGatewaysOrReducers()
        {
            Assembly asm = typeof(SpatialDirectorCore).Assembly;
            foreach (AssemblyName r in asm.GetReferencedAssemblies())
                Assert.AreNotEqual("Assembly-CSharp", r.Name, "the spatial core cannot even see the app's session code");

            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (Type t in asm.GetTypes())
            {
                foreach (FieldInfo f in t.GetFields(all)) AssertAllowed(f.FieldType, t.Name + "." + f.Name);
                foreach (MethodInfo m in t.GetMethods(all))
                {
                    AssertAllowed(m.ReturnType, t.Name + "." + m.Name);
                    foreach (ParameterInfo p in m.GetParameters()) AssertAllowed(p.ParameterType, t.Name + "." + m.Name);
                }
            }
        }

        static void AssertAllowed(Type type, string where)
        {
            string ns = type.Namespace ?? "";
            foreach (string bad in ForbiddenNamespaces)
                Assert.IsFalse(ns.StartsWith(bad, StringComparison.Ordinal), where + " uses " + type.FullName);
        }

        [Test] // fixture 9, the scene-side scripts: checked in source, since a test assembly cannot load Assembly-CSharp types by reference
        public void SceneSideScripts_NameNoGatewayReducerOrSessionType()
        {
            string dir = Path.Combine(Application.dataPath, "MuseDesk/Scripts/Spatial");
            Assert.IsTrue(Directory.Exists(dir), dir);
            int scanned = 0;
            foreach (string path in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(path);
                // Files owned by the reach side (attention cues, bringing a paper to the user) and the contract.
                if (name.StartsWith("Attention") || name.StartsWith("Remote") || name.StartsWith("Courier")
                    || name.StartsWith("Perch") || name.StartsWith("Summon") || name == "SpatialWorkspaceState.cs") continue;
                string text = File.ReadAllText(path);
                foreach (string token in ForbiddenTokens)
                    Assert.IsFalse(text.Contains(token), name + " mentions " + token);
                scanned++;
            }
            Assert.That(scanned, Is.GreaterThanOrEqualTo(15), "the placement scripts were found and scanned");
        }

        [Test] // fixture 9, behaviour: a move needs and takes nothing from outside
        public void AMove_RunsToCompletionOnItsOwnInputsOnly()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            rig.core.RequestMode(PlacementMode.Floor);
            RootPose chosen = rig.core.PreviewPose;
            rig.core.Confirm();
            int before = rig.changes;
            // Whatever the backend does meanwhile, the only inputs are: the clock, the hold flag,
            // the agents-busy flag, the user's head, and the room. None changes here.
            rig.Run(4f);
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);
            SpatialRooms.AssertPose(chosen, rig.root);
            Assert.That(rig.changes - before, Is.LessThanOrEqualTo(2), "one change when the move ends, no chatter");
        }
    }
}
#endif
