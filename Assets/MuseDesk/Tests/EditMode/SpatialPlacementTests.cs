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

        public readonly WorkspaceTransitionMachine mover;
        public readonly CeilingPodMachine pod;
        public readonly SpatialDirectorCore core;

        public SpatialRig(IRoomGeometry room)
        {
            mover = new WorkspaceTransitionMachine(clock, () => root, p => { root = p; writes++; }, () => held, () => agentsBusy);
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

            Vector3 d = p.inspectionPosition - user.head;
            float side = Mathf.Abs(Vector3.Dot(d, user.Right));
            Assert.That(side, Is.InRange(0.5f - 1e-4f, 0.7f + 1e-4f), "beside the user");
            Assert.That(Vector3.Dot(d, user.forward), Is.LessThan(side), "not in front of the face");
            Assert.AreEqual(user.head.y - s.inspectDropBelowEye, p.inspectionPosition.y, 1e-4f);
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
            rig.Run(8f);                             // prepare, then the bounded agent wait runs out
            Assert.AreEqual(CeilingPodState.ErrorRecovery, rig.pod.State);
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
