// =============================================================================
// MuseDesk · EditMode tests · placing the floor office by pointing
// =============================================================================
//
// Pure logic only: FloorAimMath, AimFilter, the aim part of SpatialDirectorCore (BeginAimDrag /
// AimDragTo / EndAimDrag / ReleaseAimDrag / CancelAimDrag / CentrePreviewOn), FloorRoutePlanner and
// FloorAimSession (pinch, drag, let go to place, hold to confirm), against the synthetic rooms of
// SpatialPlacementTests.cs (SpatialRig, SpatialRooms). Moves are checked by recording every pose
// written to the root (SpatialRig.trace): on the swept route, swept clear again, away from the head.
//
// What these do NOT prove: anything about real hands (pointer pose quality, pinch detection at
// arm's length), the drawing, or a real room. See FloorAimController and AgentScripts/FloorAimDrive.cs.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;

namespace MuseDesk.Tests.EditMode
{
    static class AimRig
    {
        public const float Dt = 1f / 72f;
        public static readonly Vector3 HandOrigin = new Vector3(0.25f, 0.85f, 0.25f);

        /// <summary>A right hand pointing at a floor point.</summary>
        public static AimHandSample At(Vector3 floorPoint, bool pinch)
        {
            return AimHandSample.Ray(HandOrigin, floorPoint - HandOrigin, pinch);
        }

        public static void Run(FloorAimSession s, float seconds, AimHandSample right)
        {
            int n = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < n; i++) s.Tick(Dt, AimHandSample.None, right);
        }

        public static Vector3 FootprintCentre(SpatialDirectorCore core)
        {
            RootPose p = core.ShownPose;
            Vector2 off = core.floorSettings.footprintCenterOffset;
            return p.position + p.rotation * new Vector3(off.x, 0f, off.y);
        }

        public static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return (a - b).magnitude;
        }

        /// <summary>A rig with the floor preview open on its first candidate.</summary>
        public static SpatialRig OpenFloor()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Floor));
            Assert.That(rig.core.CandidateCount, Is.GreaterThanOrEqualTo(2), "the test office offers several floor spots");
            Assert.IsTrue(rig.core.CanAim);
            return rig;
        }
    }

    // ───────────────────────── geometry and smoothing ─────────────────────────

    [Category("MuseDesk")]
    public class FloorAimMathTests
    {
        [Test]
        public void ARayPointingDown_MeetsTheFloorWhereExpected()
        {
            Vector3 p;
            Assert.IsTrue(FloorAimMath.RayToFloor(new Vector3(0f, 1f, 0f), new Vector3(0f, -1f, 1f), 0f, out p));
            Assert.That((p - new Vector3(0f, 0f, 1f)).magnitude, Is.LessThan(1e-4f));

            Assert.IsTrue(FloorAimMath.RayToFloor(new Vector3(1f, 1.5f, -2f), new Vector3(2f, -1f, 0f), 0.5f, out p));
            Assert.That((p - new Vector3(3f, 0.5f, -2f)).magnitude, Is.LessThan(1e-4f));
        }

        [Test]
        public void ARayPointingUp_OrStartingUnderTheFloor_AimsAtNothing()
        {
            Vector3 p;
            Assert.IsFalse(FloorAimMath.RayToFloor(new Vector3(0f, 1f, 0f), new Vector3(0f, 0.5f, 1f), 0f, out p));
            Assert.IsFalse(FloorAimMath.RayToFloor(new Vector3(0f, 1f, 0f), Vector3.up, 0f, out p));
            Assert.IsFalse(FloorAimMath.RayToFloor(new Vector3(0f, -0.2f, 0f), new Vector3(0f, -1f, 1f), 0f, out p));
            Assert.IsFalse(FloorAimMath.RayToFloor(new Vector3(0f, 1f, 0f), Vector3.zero, 0f, out p));
        }

        [Test]
        public void ALevelRay_AimsFarAway_AndIsClampedToTheAllowedDistance()
        {
            Vector3 p;
            Assert.IsTrue(FloorAimMath.RayToFloor(new Vector3(0f, 1f, 0f), Vector3.forward, 0f, out p));
            Assert.That(p.z, Is.GreaterThan(10f));
            Assert.AreEqual(0f, p.y, 1e-5f);

            Vector3 head = new Vector3(0f, 1.2f, 0f);
            Vector3 c = FloorAimMath.ClampToRing(p, head, 0.8f, 3.5f);
            Assert.AreEqual(3.5f, AimRig.Flat(c, head), 1e-3f);
            Assert.AreEqual(0f, c.y, 1e-5f, "the height is left alone");

            Vector3 near = FloorAimMath.ClampToRing(new Vector3(0.1f, 0.3f, 0f), head, 0.8f, 3.5f);
            Assert.AreEqual(0.8f, AimRig.Flat(near, head), 1e-3f);
            Assert.AreEqual(0.3f, near.y, 1e-5f);

            Vector3 inside = new Vector3(1.5f, 0f, -0.5f);
            Assert.IsTrue(FloorAimMath.ClampToRing(inside, head, 0.8f, 3.5f) == inside, "a point inside the ring is not touched");
        }

        [Test]
        public void TheFilter_IgnoresJitterInsideTheDeadZone()
        {
            var s = new FloorAimSettings();
            var f = new AimFilter();
            Vector3 centre = new Vector3(2f, 0f, 1f);
            Vector3 first = f.Step(centre + new Vector3(0.008f, 0f, 0f), AimRig.Dt, s);
            for (int i = 0; i < 200; i++)
            {
                Vector3 raw = centre + new Vector3((i % 2 == 0 ? -1f : 1f) * 0.008f, 0f, (i % 3 == 0 ? -1f : 1f) * 0.004f);
                Assert.IsTrue(f.Step(raw, AimRig.Dt, s) == first, "a hand at rest gives a point at rest");
            }
        }

        [Test]
        public void TheFilter_FollowsADeliberateMove_ToWithinTheDeadZone()
        {
            var s = new FloorAimSettings();
            var f = new AimFilter();
            f.Step(Vector3.zero, AimRig.Dt, s);
            Vector3 target = new Vector3(1f, 0f, 0.5f);
            Vector3 v = Vector3.zero;
            float previous = target.magnitude;
            for (int i = 0; i < 144; i++)
            {
                v = f.Step(target, AimRig.Dt, s);
                float d = (target - v).magnitude;
                Assert.That(d, Is.LessThanOrEqualTo(previous + 1e-5f), "never overshoots or backs away");
                previous = d;
            }
            Assert.That((target - v).magnitude, Is.LessThan(s.deadZone + 0.01f));

            // A big move is mostly made within a quarter of a second.
            var g = new AimFilter();
            g.Step(Vector3.zero, AimRig.Dt, s);
            for (int i = 0; i < 18; i++) v = g.Step(target, AimRig.Dt, s);
            Assert.That(v.magnitude, Is.GreaterThan(0.6f * target.magnitude));
        }
    }

    // ───────────────────────── the director core: drag the preview ─────────────────────────

    [Category("MuseDesk")]
    public class FloorAimDirectorTests
    {
        [Test]
        public void DraggingToAClearSpot_MovesOnlyThePreview_AndConfirmThenMovesTheOffice()
        {
            var rig = AimRig.OpenFloor();
            RootPose desk = rig.root;
            Vector3 target = rig.core.GetCandidate(1).position;

            Assert.IsTrue(rig.core.BeginAimDrag());
            Assert.IsTrue(rig.core.AimDragging);
            rig.core.AimDragTo(target);
            Assert.IsTrue(rig.core.AimValid, rig.core.AimProblem);
            Assert.That((rig.core.ShownPose.position - target).magnitude, Is.LessThan(1e-3f));
            Vector3 toUser = rig.user.head - rig.core.ShownPose.position; toUser.y = 0f;
            Assert.That(Vector3.Angle(rig.core.ShownPose.rotation * Vector3.forward, toUser), Is.LessThan(1f), "it faces the user");

            Assert.IsTrue(rig.core.EndAimDrag(), rig.core.StatusText);
            Assert.IsFalse(rig.core.AimDragging);
            Assert.That((rig.core.PreviewPose.position - target).magnitude, Is.LessThan(1e-3f));
            Assert.IsTrue(rig.core.PreviewIsDetected, "a spot that passed the room checks is a checked spot");
            Assert.IsTrue(rig.core.PreviewValid);
            Assert.IsTrue(rig.core.PlacedByAim);
            StringAssert.Contains("pointing", rig.core.StatusText);

            // Nothing has moved, and nothing was asked to.
            Assert.AreEqual(0, rig.writes);
            Assert.IsFalse(rig.mover.IsRunning);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            SpatialRooms.AssertPose(desk, rig.root);

            Assert.IsTrue(rig.core.Confirm(), rig.core.StatusText);
            rig.Run(6f);
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);
            Assert.That((rig.root.position - target).magnitude, Is.LessThan(0.002f));
        }

        [Test]
        public void OverAWall_TheSpotIsRefusedInWords_ThePreviewKeepsTheLastClearSpot_AndConfirmIsRefused()
        {
            var rig = AimRig.OpenFloor();
            Vector3 clear = rig.core.GetCandidate(1).position;
            Assert.IsTrue(rig.core.BeginAimDrag());
            rig.core.AimDragTo(clear);
            Assert.IsTrue(rig.core.AimValid);

            Vector3 wall = new Vector3(2.3f, 0f, 0f);        // the footprint would cross the wall at x = 2.5
            rig.core.AimDragTo(wall);
            Assert.IsFalse(rig.core.AimValid);
            Assert.AreEqual("Too close to the wall", rig.core.AimProblem);
            StringAssert.Contains("Too close to the wall", rig.core.StatusText);
            Assert.IsFalse(rig.core.ShownValid, "the ghost is drawn as not clear");
            Assert.That((rig.core.ShownPose.position - wall).magnitude, Is.LessThan(1e-3f), "the ghost is under the hand");
            Assert.That((rig.core.PreviewPose.position - clear).magnitude, Is.LessThan(1e-3f), "the preview stays on the last clear spot");

            Assert.IsFalse(rig.core.Confirm(), "a spot that is not clear cannot be confirmed");
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            Assert.AreEqual(0, rig.writes);

            Assert.IsFalse(rig.core.EndAimDrag(), "released over a spot that is not clear");
            Assert.That((rig.core.PreviewPose.position - clear).magnitude, Is.LessThan(1e-3f), "snapped back to the last clear spot");
            Assert.IsTrue(rig.core.PreviewValid);
            Assert.IsTrue(rig.core.ShownValid);
            StringAssert.Contains("last clear spot", rig.core.StatusText);
            Assert.AreEqual(0, rig.writes);
        }

        [Test]
        public void Furniture_AndTheUsersChair_AreRefusedWithTheirOwnWords()
        {
            var rig = AimRig.OpenFloor();
            RootPose start = rig.core.PreviewPose;
            string words = rig.core.StatusText;
            Assert.IsTrue(rig.core.BeginAimDrag());

            rig.core.AimDragTo(new Vector3(0f, 0f, 1.5f));               // on the far edge of the user's desk
            Assert.IsFalse(rig.core.AimValid);
            Assert.AreEqual("Something is in the way", rig.core.AimProblem);

            rig.core.AimDragTo(new Vector3(0.64f, 0f, -0.64f));          // 0.9 m away, behind the right shoulder
            Assert.IsFalse(rig.core.AimValid);
            Assert.AreEqual("Too close to you", rig.core.AimProblem);
            StringAssert.Contains("Too close to you", rig.core.StatusText);

            rig.core.AimDragTo(new Vector3(0.05f, 0f, -0.02f));          // almost under the user: kept at the minimum distance
            Assert.AreEqual(rig.core.aimMinDistance, AimRig.Flat(rig.core.ShownPose.position, rig.user.head), 1e-3f);
            Assert.IsFalse(rig.core.AimValid);

            rig.core.AimDragTo(new Vector3(-9f, 0f, 0f));                // far outside the room: kept at the maximum, and off the floor
            Assert.AreEqual(rig.core.aimMaxDistance, AimRig.Flat(rig.core.ShownPose.position, rig.user.head), 1e-3f);
            Assert.IsFalse(rig.core.AimValid);
            Assert.AreEqual("Off the scanned floor", rig.core.AimProblem);

            // It never reached a clear spot: the preview is exactly as it was.
            Assert.IsFalse(rig.core.EndAimDrag());
            SpatialRooms.AssertPose(start, rig.core.PreviewPose);
            Assert.IsTrue(rig.core.PreviewIsDetected);
            Assert.IsFalse(rig.core.PlacedByAim);
            Assert.AreNotEqual(words, rig.core.StatusText, "the words say why it stayed");
            Assert.AreEqual(0, rig.writes);
        }

        [Test]
        public void TheForwardConePreference_DoesNotApplyToASpotTheUserChose()
        {
            var room = SpatialRooms.Office();
            SeatedUser user = SpatialRooms.User();
            var s = new FloorPlacementSettings();
            Vector3 p = new Vector3(0f, 0f, 2.0f);                        // straight ahead, beyond the desk, clear of it
            var pose = new RootPose(p, SpatialGeometry.FaceUserYaw(p, user.head), 1f);

            string reason;
            Assert.IsFalse(FloorPlacementEvaluator.ValidatePose(room, user, pose, SpatialRooms.Footprint, s, true, out reason));
            Assert.AreEqual("In front of your desk", reason, "the automatic search still avoids it");

            FloorPoseProblem problem;
            Assert.IsTrue(FloorPlacementEvaluator.CheckPlacedPose(room, user, pose, SpatialRooms.Footprint, s, out problem), problem.ToString());
            Assert.AreEqual(FloorPoseProblem.None, problem);
        }

        [Test]
        public void CancellingADrag_PutsThePreviewBackExactly()
        {
            var rig = AimRig.OpenFloor();
            RootPose start = rig.core.PreviewPose;
            string words = rig.core.StatusText;
            Assert.IsTrue(rig.core.BeginAimDrag());
            rig.core.AimDragTo(rig.core.GetCandidate(1).position);
            Assert.IsTrue(rig.core.PlacedByAim);
            rig.core.CancelAimDrag();
            Assert.IsFalse(rig.core.AimDragging);
            SpatialRooms.AssertPose(start, rig.core.PreviewPose);
            Assert.AreEqual(words, rig.core.StatusText);
            Assert.IsFalse(rig.core.PlacedByAim);
            Assert.AreEqual(0, rig.writes);
        }

        [Test]
        public void Next_AfterADrag_GoesBackToAScoredCandidate()
        {
            var rig = AimRig.OpenFloor();
            Assert.IsTrue(rig.core.BeginAimDrag());
            rig.core.AimDragTo(rig.core.GetCandidate(1).position);
            rig.core.EndAimDrag();
            Assert.IsTrue(rig.core.CanCycle);

            rig.core.NextCandidate();
            Assert.IsFalse(rig.core.PlacedByAim);
            Assert.IsTrue(rig.core.PreviewIsDetected);
            Assert.IsTrue(rig.core.PreviewValid);
            StringAssert.Contains("Detected floor spot", rig.core.StatusText);
            SpatialRooms.AssertPose(rig.core.GetCandidate(rig.core.CandidateIndex).Pose, rig.core.PreviewPose);

            // A key pressed in the middle of a drag takes over from it.
            Assert.IsTrue(rig.core.BeginAimDrag());
            rig.core.NextCandidate();
            Assert.IsFalse(rig.core.AimDragging);
            SpatialRooms.AssertPose(rig.core.GetCandidate(rig.core.CandidateIndex).Pose, rig.core.PreviewPose);
        }

        [Test]
        public void ARoomEvent_OrLeavingThePreview_EndsADrag()
        {
            var room = SpatialRooms.Office();
            var rig = new SpatialRig(room);
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Floor));
            Assert.IsTrue(rig.core.BeginAimDrag());
            room.RaiseChanged();
            Assert.IsFalse(rig.core.AimDragging, "the preview was rebuilt on the new room data");
            Assert.IsFalse(rig.core.PlacedByAim);

            Assert.IsTrue(rig.core.BeginAimDrag());
            rig.core.Cancel();
            Assert.IsFalse(rig.core.AimDragging);
            Assert.IsFalse(rig.core.CanAim);
            Assert.IsFalse(rig.core.BeginAimDrag(), "no preview, nothing to drag");
            rig.core.AimDragTo(new Vector3(1.5f, 0f, 0f));               // ignored
            Assert.AreEqual(DirectorPhase.Idle, rig.core.Phase);
            Assert.AreEqual(0, rig.writes);
        }

        [Test]
        public void WithNoRoomScan_ThePlaneIsThePreviewHeight_AndOnlyTheUserAndTheDistanceLimitsApply()
        {
            var rig = new SpatialRig(null);
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Floor));
            Assert.IsFalse(rig.core.PreviewIsDetected);
            Assert.IsTrue(rig.core.CanAim);
            float y;
            Assert.IsTrue(rig.core.TryGetAimFloorHeight(out y));
            Assert.AreEqual(rig.core.PreviewPose.position.y, y, 1e-5f);

            Assert.IsTrue(rig.core.BeginAimDrag());
            rig.core.AimDragTo(new Vector3(10f, y, 0.2f));               // far: kept at 3.5 m, and that is allowed
            Assert.AreEqual(rig.core.aimMaxDistance, AimRig.Flat(rig.core.ShownPose.position, rig.user.head), 1e-3f);
            Assert.IsTrue(rig.core.AimValid, rig.core.AimProblem);
            Assert.IsFalse(rig.core.AimRoomChecked);
            Assert.IsFalse(rig.core.PreviewIsDetected, "never presented as detected floor");

            rig.core.AimDragTo(new Vector3(0.64f, y, -0.64f));           // on the chair
            Assert.IsFalse(rig.core.AimValid);
            Assert.AreEqual("Too close to you", rig.core.AimProblem);
            Assert.AreEqual(rig.core.aimMaxDistance, AimRig.Flat(rig.core.PreviewPose.position, rig.user.head), 1e-3f, "the preview kept the last clear spot");

            Vector3 side = new Vector3(2.0f, y, 0.2f);
            rig.core.AimDragTo(side);
            Assert.IsTrue(rig.core.AimValid);
            Assert.AreEqual(y, rig.core.ShownPose.position.y, 1e-5f);
            Assert.IsTrue(rig.core.EndAimDrag(), rig.core.StatusText);
            Assert.That((rig.core.PreviewPose.position - side).magnitude, Is.LessThan(1e-3f));
            StringAssert.Contains("not checked against your room", rig.core.StatusText);
            Assert.AreEqual(0, rig.writes);

            Assert.IsTrue(rig.core.Confirm(), rig.core.StatusText);
            rig.Run(6f);
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);
            StringAssert.Contains("by hand", rig.core.StatusText);
        }

        [Test]
        public void ATrackedFloor_ReplacesTheGuessedHeight_ForHandPlacement()
        {
            var rig = new SpatialRig(null);
            rig.core.hasTrackedFloor = true;
            rig.core.trackedFloorHeight = -0.1f;
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Floor));
            Assert.AreEqual(-0.1f, rig.core.PreviewPose.position.y, 1e-5f);
            float y;
            Assert.IsTrue(rig.core.TryGetAimFloorHeight(out y));
            Assert.AreEqual(-0.1f, y, 1e-5f);
        }
    }

    // ───────────────────────── the session: pinch, drag, hold ─────────────────────────

    [Category("MuseDesk")]
    public class FloorAimSessionTests
    {
        [Test]
        public void WithNoPreview_TheSessionIsOff()
        {
            var rig = new SpatialRig(SpatialRooms.Office());
            var session = new FloorAimSession(rig.core);
            AimRig.Run(session, 0.5f, AimRig.At(new Vector3(1.5f, 0f, 0f), true));
            Assert.AreEqual(FloorAimState.Off, session.State);
            Assert.IsFalse(session.HasCursor(1));
            Assert.AreEqual(DirectorPhase.Idle, rig.core.Phase);
        }

        [Test]
        public void PinchOnTheFootprint_ThenMove_DragsIt_AndLettingGoOnAReadySpotPlacesIt()
        {
            // CHANGED (headset feedback 1): letting go used to only drop the footprint, and the
            // office stayed at the desk although the spot was shown ready. It now places it there,
            // through the same Confirm and swept route as the key.
            var rig = AimRig.OpenFloor();
            var session = new FloorAimSession(rig.core);
            Vector3 centre = AimRig.FootprintCentre(rig.core);
            Vector3 from = rig.core.PreviewPose.position;
            Vector3 to = rig.core.GetCandidate(1).position;

            AimRig.Run(session, 0.5f, AimRig.At(centre, false));
            Assert.AreEqual(FloorAimState.Aiming, session.State);
            Assert.IsTrue(session.HasCursor(1));
            Assert.IsTrue(session.OverFootprint(1));
            Assert.IsFalse(session.HasCursor(0), "the left hand is not tracked");

            AimRig.Run(session, 0.1f, AimRig.At(centre, true));
            Assert.AreEqual(FloorAimState.Pressing, session.State);
            Assert.AreEqual(1, session.ActiveHand);
            Assert.IsFalse(rig.core.AimDragging, "a pinch alone moves nothing");

            AimRig.Run(session, 1.5f, AimRig.At(centre + (to - from), true));
            Assert.AreEqual(FloorAimState.Dragging, session.State);
            Assert.IsTrue(rig.core.AimDragging);
            Assert.That(AimRig.Flat(rig.core.ShownPose.position, to), Is.LessThan(0.05f), "the footprint followed the hand, keeping its grab offset");
            Assert.AreEqual(0f, session.HoldProgress);
            Assert.IsTrue(rig.core.ShownValid, rig.core.AimProblem);
            Assert.AreEqual("Let go to place it here", rig.core.FootprintWords);
            Assert.AreEqual(0, rig.writes, "nothing moves while dragging");
            RootPose released = rig.core.ShownPose;

            rig.trace = new List<RootPose>();
            AimRig.Run(session, 0.6f, AimRig.At(centre + (to - from), false));
            Assert.AreEqual(1, session.Drops);
            Assert.AreEqual(1, session.ReleasePlaceAttempts);
            Assert.AreEqual(1, session.ReleasePlaces);
            Assert.AreEqual(0, session.HoldConfirmAttempts);
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode, "the same Confirm the key calls");
            Assert.AreEqual(DirectorPhase.Idle, rig.core.Phase);
            Assert.AreEqual(FloorAimState.Off, session.State);

            rig.Run(6f);
            SpatialRooms.AssertPose(released, rig.root);
            ReleaseRig.AssertTravelledTheSweptRoute(rig, true, "release on a ready spot");
        }

        [Test]
        public void ADrag_NeverConfirms_HoweverLongItPauses()
        {
            var rig = AimRig.OpenFloor();
            var session = new FloorAimSession(rig.core);
            Vector3 centre = AimRig.FootprintCentre(rig.core);
            AimRig.Run(session, 0.5f, AimRig.At(centre, false));
            AimRig.Run(session, 0.1f, AimRig.At(centre, true));
            Vector3 moved = centre + new Vector3(0.3f, 0f, 0f);
            AimRig.Run(session, 4f, AimRig.At(moved, true));
            Assert.AreEqual(FloorAimState.Dragging, session.State);
            Assert.AreEqual(0, session.HoldConfirmAttempts);
            Assert.AreEqual(0f, session.HoldProgress);
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            Assert.AreEqual(0, rig.writes);
        }

        [Test]
        public void PinchAndHold_OnTheFootprint_FillsTheRing_ThenConfirms()
        {
            var rig = AimRig.OpenFloor();
            var session = new FloorAimSession(rig.core);
            Vector3 centre = AimRig.FootprintCentre(rig.core);
            Vector3 spot = rig.core.PreviewPose.position;

            AimRig.Run(session, 0.5f, AimRig.At(centre, false));
            AimRig.Run(session, 0.2f, AimRig.At(centre, true));
            Assert.AreEqual(0f, session.HoldProgress, "the ring does not start at once");
            AimRig.Run(session, 0.4f, AimRig.At(centre, true));
            Assert.That(session.HoldProgress, Is.InRange(0.2f, 0.8f));
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            Assert.AreEqual(0, rig.writes);

            AimRig.Run(session, 0.7f, AimRig.At(centre, true));
            Assert.AreEqual(1, session.HoldConfirmAttempts);
            Assert.AreEqual(1, session.HoldConfirms);
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode, "the same Confirm the key calls");
            Assert.AreEqual(DirectorPhase.Idle, rig.core.Phase);
            Assert.AreEqual(FloorAimState.Off, session.State);

            // The pinch is still closed: it must not do anything more.
            AimRig.Run(session, 2f, AimRig.At(centre, true));
            Assert.AreEqual(1, session.HoldConfirmAttempts);
            rig.Run(6f);
            Assert.That((rig.root.position - spot).magnitude, Is.LessThan(0.002f));
        }

        [Test]
        public void ATap_APinchThatWasAlreadyClosed_AndAPinchThatBeganElsewhere_DoNothing()
        {
            var rig = AimRig.OpenFloor();
            RootPose start = rig.core.PreviewPose;
            Vector3 centre = AimRig.FootprintCentre(rig.core);

            // Already pinching when the preview opened (the hand that pressed something).
            var session = new FloorAimSession(rig.core);
            AimRig.Run(session, 3f, AimRig.At(centre, true));
            Assert.AreEqual(FloorAimState.Aiming, session.State);
            Assert.AreEqual(0, session.HoldConfirmAttempts);

            // A short tap on the footprint.
            AimRig.Run(session, 0.5f, AimRig.At(centre, false));
            AimRig.Run(session, 0.15f, AimRig.At(centre, true));
            AimRig.Run(session, 0.8f, AimRig.At(centre, false));
            Assert.AreEqual(FloorAimState.Aiming, session.State);
            Assert.AreEqual(0, session.HoldConfirmAttempts);
            Assert.AreEqual(0, session.Drops);

            // A pinch that begins off the footprint and is then swept across it.
            Vector3 off = centre + start.rotation * Vector3.right * 1.2f;
            AimRig.Run(session, 0.5f, AimRig.At(off, false));
            Assert.IsFalse(session.OverFootprint(1));
            AimRig.Run(session, 0.3f, AimRig.At(off, true));
            Assert.AreEqual(FloorAimState.Aiming, session.State);
            AimRig.Run(session, 2.5f, AimRig.At(centre, true));
            Assert.AreEqual(FloorAimState.Aiming, session.State);
            Assert.AreEqual(0, session.HoldConfirmAttempts);

            SpatialRooms.AssertPose(start, rig.core.PreviewPose);
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            Assert.AreEqual(0, rig.writes);
        }

        [Test]
        public void LosingTheHand_DuringADrag_DropsItOnTheLastClearSpot()
        {
            var rig = AimRig.OpenFloor();
            var session = new FloorAimSession(rig.core);
            Vector3 centre = AimRig.FootprintCentre(rig.core);
            Vector3 from = rig.core.PreviewPose.position;
            Vector3 to = rig.core.GetCandidate(1).position;
            AimRig.Run(session, 0.5f, AimRig.At(centre, false));
            AimRig.Run(session, 0.1f, AimRig.At(centre, true));
            AimRig.Run(session, 1.5f, AimRig.At(centre + (to - from), true));
            Assert.AreEqual(FloorAimState.Dragging, session.State);

            AimRig.Run(session, 1f, AimHandSample.None);
            Assert.AreEqual(FloorAimState.Aiming, session.State);
            Assert.IsFalse(rig.core.AimDragging);
            Assert.IsTrue(rig.core.ShownValid);
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            Assert.AreEqual(0, session.ReleasePlaceAttempts, "a lost hand never places");
            Assert.AreEqual(0, rig.writes);
        }

        [Test]
        public void AHandThatIsLostAndComesBackOpen_DoesNotPlace()
        {
            // Tracking drops for 0.25 s (past the release debounce, inside the grace time) and the
            // hand comes back with the pinch open: nobody was seen letting go, so nothing is placed.
            var rig = AimRig.OpenFloor();
            var session = new FloorAimSession(rig.core);
            Vector3 centre = AimRig.FootprintCentre(rig.core);
            Vector3 from = rig.core.PreviewPose.position;
            Vector3 to = rig.core.GetCandidate(1).position;
            AimRig.Run(session, 0.5f, AimRig.At(centre, false));
            AimRig.Run(session, 0.1f, AimRig.At(centre, true));
            AimRig.Run(session, 1.5f, AimRig.At(centre + (to - from), true));
            Assert.AreEqual(FloorAimState.Dragging, session.State);
            Assert.IsTrue(rig.core.ShownValid, rig.core.AimProblem);

            AimRig.Run(session, 0.25f, AimHandSample.None);
            Assert.AreEqual(FloorAimState.Dragging, session.State, "within the grace time");
            AimRig.Run(session, 0.6f, AimRig.At(centre + (to - from), false));
            Assert.AreEqual(FloorAimState.Aiming, session.State);
            Assert.AreEqual(1, session.Drops);
            Assert.AreEqual(0, session.ReleasePlaceAttempts);
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            rig.Run(3f);
            Assert.AreEqual(0, rig.writes);
        }

        [Test]
        public void ATwitch_OrAQuickFlick_NeverPlaces()
        {
            var rig = AimRig.OpenFloor();
            var session = new FloorAimSession(rig.core);
            Vector3 centre = AimRig.FootprintCentre(rig.core);
            RootPose start = rig.core.PreviewPose;
            AimRig.Run(session, 0.5f, AimRig.At(centre, false));

            // A twitch: pinch while the hand shakes by 3 cm, let go after 0.2 s.
            for (int i = 0; i < 14; i++)
            {
                Vector3 shake = new Vector3(0.03f * (i % 2 == 0 ? 1f : -1f), 0f, 0.03f * (i % 3 == 0 ? 1f : -1f));
                session.Tick(AimRig.Dt, AimHandSample.None, AimRig.At(centre + shake, true));
            }
            AimRig.Run(session, 0.8f, AimRig.At(centre, false));
            Assert.AreEqual(FloorAimState.Aiming, session.State);
            Assert.AreEqual(0, session.Drops, "it never became a drag");
            Assert.AreEqual(0, session.ReleasePlaceAttempts);
            Assert.AreEqual(0, session.HoldConfirmAttempts);
            SpatialRooms.AssertPose(start, rig.core.PreviewPose);

            // A flick: a real drag, let go 0.15 s after the pinch began.
            Vector3 flick = centre + rig.user.Right * 0.5f;
            AimRig.Run(session, 0.04f, AimRig.At(centre, true));
            Assert.AreEqual(FloorAimState.Pressing, session.State);
            AimRig.Run(session, 0.1f, AimRig.At(flick, true));
            Assert.AreEqual(FloorAimState.Dragging, session.State);
            Assert.That(session.HeldSeconds, Is.LessThan(new FloorAimSettings().placeMinSeconds));
            AimRig.Run(session, 0.8f, AimRig.At(flick, false));
            Assert.AreEqual(1, session.Drops);
            Assert.AreEqual(0, session.ReleasePlaceAttempts, "too short to be a deliberate placement");
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            rig.Run(3f);
            Assert.AreEqual(0, rig.writes);
        }

        [Test]
        public void LettingGoOverASpotThatIsNotClear_LeavesThePreview_WithTheReason()
        {
            var rig = AimRig.OpenFloor();
            var session = new FloorAimSession(rig.core);
            Vector3 centre = AimRig.FootprintCentre(rig.core);
            Vector3 from = rig.core.PreviewPose.position;
            Vector3 onDesk = new Vector3(0f, 0f, 1.5f);

            AimRig.Run(session, 0.5f, AimRig.At(centre, false));
            AimRig.Run(session, 0.1f, AimRig.At(centre, true));
            AimRig.Run(session, 1.5f, AimRig.At(centre + (onDesk - from), true));
            Assert.AreEqual(FloorAimState.Dragging, session.State);
            Assert.IsFalse(rig.core.ShownValid);
            Assert.AreEqual("Something is in the way", rig.core.FootprintWords);

            AimRig.Run(session, 0.6f, AimRig.At(centre + (onDesk - from), false));
            Assert.AreEqual(1, session.Drops);
            Assert.AreEqual(0, session.ReleasePlaces);
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase, "still previewing");
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            StringAssert.Contains("Something is in the way", rig.core.FootprintWords, "the footprint says why");
            Assert.IsTrue(rig.core.ShownValid, "it shows the last ready spot");
            rig.Run(3f);
            Assert.AreEqual(0, rig.writes);
        }
    }

    // ───────────────────────── letting go places it (headset feedback 1) ─────────────────────────

    /// <summary>Spots and checks shared by the release-to-place tests (the test office of SpatialRooms).</summary>
    static class ReleaseRig
    {
        /// <summary>The desk pose of SpatialRig (its root at the start).</summary>
        public static readonly RootPose Desk = new RootPose(new Vector3(0f, 0.9f, 0.6f), Quaternion.Euler(0f, 180f, 0f), 1f);
        // None of these is on the floor search's grid (50..130 degrees by 20, 1.0..2.2 m by 0.3), so
        // none is the preview's first candidate and a drag to it is a real drag.
        /// <summary>112 degrees round from straight ahead, 1.35 m: beside and a little behind the right shoulder.</summary>
        public static readonly Vector3 BesideShoulder = new Vector3(1.25f, 0f, -0.5f);
        /// <summary>110 degrees, 1.0 m: closer (its near edge 0.53 m from the head's axis).</summary>
        public static readonly Vector3 CloseBeside = new Vector3(0.94f, 0f, -0.34f);
        /// <summary>93 degrees, 1.7 m: to the right.</summary>
        public static readonly Vector3 Side = new Vector3(1.7f, 0f, -0.1f);
        /// <summary>Straight behind the user, 1.3 m: clear floor, but no route gets there without passing over the head.</summary>
        public static readonly Vector3 Behind = new Vector3(0f, 0f, -1.3f);

        public static RootPose Facing(Vector3 p) { return new RootPose(p, SpatialGeometry.FaceUserYaw(p, SpatialRooms.User().head), 1f); }

        public static PathSweepOptions Options(bool fromDesk)
        {
            var o = default(PathSweepOptions);
            if (fromDesk) o.fromDesk = true; else o.toDesk = true;
            o.absoluteHigh = true;
            return o;
        }

        /// <summary>A tall cabinet 0.2 m beyond the far edge of the office standing at <see cref="Side"/>:
        /// far enough for the office to stand there, too close for it to come down there.</summary>
        public static void AddCabinetBesideSide(SyntheticRoomGeometry room)
        {
            // The office at Side reaches x = 1.96 m; the cabinet stands from 2.16 to 2.46 m, 2 m tall.
            room.AddFurniture("STORAGE", new Vector3(2.31f, 1.0f, -0.1f), new Vector3(0.3f, 2.0f, 0.4f));
        }

        /// <summary>The poses written to the root lie on the planned route (1 mm), that route is swept
        /// clear again here with the same options, and no written pose comes closer to the head's
        /// vertical axis than the desk pose does, less the desk allowance (and 3 cm for the travel
        /// between two samples of the sweep).</summary>
        public static void AssertTravelledTheSweptRoute(SpatialRig rig, bool fromDesk, string what)
        {
            WorkspaceRoute route = rig.core.LastRoute;
            Assert.IsTrue(rig.core.LastRouteCheck.clear, what + ": the route was swept clear");
            CeilingPathCheck again;
            Assert.IsTrue(CeilingPlacementEvaluator.SweepRoute(rig.core.Room, rig.user, route, SpatialRooms.Footprint, rig.core.ceilingSettings,
                                                              Options(fromDesk), out again), what + ": swept again: " + again.Describe());
            Assert.IsNotNull(rig.trace, what);
            Assert.That(rig.trace.Count, Is.GreaterThan(5), what + ": the move was recorded");

            Vector2 head = new Vector2(rig.user.head.x, rig.user.head.z);
            Vector2 off = rig.core.floorSettings.footprintCenterOffset;
            float deskGap = SpatialGeometry.Footprint(Desk.position, Desk.rotation, 1f, SpatialRooms.Footprint, off).DistanceToPoint(head);
            float floor = deskGap - rig.core.ceilingSettings.deskApproachAllowance - 0.03f;
            for (int i = 0; i < rig.trace.Count; i++)
            {
                RootPose pose = rig.trace[i];
                Assert.That(DistanceToRoute(route, pose), Is.LessThan(0.002f), what + ", write " + i + " lies on the route");
                OrientedRect rect = SpatialGeometry.Footprint(pose.position, pose.rotation, pose.scale, SpatialRooms.Footprint, off);
                Assert.That(rect.DistanceToPoint(head), Is.GreaterThan(floor), what + ", write " + i + " keeps away from the head");
            }
        }

        static float DistanceToRoute(WorkspaceRoute route, RootPose pose)
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
            return best;
        }

        /// <summary>A rig in the test office with the floor preview open.</summary>
        public static SpatialRig Open(SyntheticRoomGeometry room)
        {
            var rig = new SpatialRig(room);
            Assert.IsTrue(rig.core.RequestMode(PlacementMode.Floor), rig.core.StatusText);
            Assert.IsTrue(rig.core.CanAim);
            return rig;
        }
    }

    [Category("MuseDesk")]
    public class FloorReleaseToPlaceTests
    {
        [Test]
        public void ReleasingOnAReadySpot_MovesTheOffice_AlongTheSweptRoute()
        {
            var rig = ReleaseRig.Open(SpatialRooms.Office());
            Assert.AreEqual("Pinch it, drag it to a spot, let go", rig.core.FootprintWords);
            Assert.IsTrue(rig.core.BeginAimDrag());
            rig.core.AimDragTo(ReleaseRig.Side);
            Assert.IsTrue(rig.core.AimValid, rig.core.AimProblem);
            Assert.AreEqual("Let go to place it here", rig.core.FootprintWords);
            Assert.AreEqual(0, rig.writes);

            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.ReleaseAimDrag(true), rig.core.StatusText);
            Assert.AreEqual(1, rig.core.ReleasePlaceAttempts);
            Assert.AreEqual(1, rig.core.ReleasePlaces);
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);
            Assert.AreEqual(DirectorPhase.Idle, rig.core.Phase);
            rig.Run(6f);
            SpatialRooms.AssertPose(ReleaseRig.Facing(ReleaseRig.Side), rig.root);
            ReleaseRig.AssertTravelledTheSweptRoute(rig, true, "release at the side");

            // Release without asking to place (the hand was lost): never moves.
            var other = ReleaseRig.Open(SpatialRooms.Office());
            Assert.IsTrue(other.core.BeginAimDrag());
            other.core.AimDragTo(ReleaseRig.Side);
            Assert.IsFalse(other.core.ReleaseAimDrag(false));
            Assert.AreEqual(0, other.core.ReleasePlaceAttempts);
            Assert.AreEqual(DirectorPhase.Previewing, other.core.Phase);
            other.Run(3f);
            Assert.AreEqual(0, other.writes);
        }

        [Test]
        public void AWayThatIsBlocked_IsShownBeforeRelease_AndReleasingMovesNothing()
        {
            var room = SpatialRooms.Office();
            ReleaseRig.AddCabinetBesideSide(room);
            var rig = ReleaseRig.Open(room);
            RootPose start = rig.core.PreviewPose;

            // The office may STAND there (the floor rules pass) ...
            FloorPoseProblem problem;
            Assert.IsTrue(FloorPlacementEvaluator.CheckPlacedPose(room, rig.user, Vector3.forward, ReleaseRig.Facing(ReleaseRig.Side),
                                                                  SpatialRooms.Footprint, rig.core.floorSettings, out problem), problem.ToString());
            // ... but cannot be brought down there: not ready, and the footprint says why.
            Assert.IsTrue(rig.core.BeginAimDrag());
            rig.core.AimDragTo(ReleaseRig.Side);
            Assert.IsFalse(rig.core.AimValid);
            Assert.IsFalse(rig.core.ShownValid, "drawn as not clear");
            Assert.AreEqual("The way there is blocked by STORAGE", rig.core.AimProblem);
            Assert.AreEqual("The way there is blocked by STORAGE", rig.core.FootprintWords);

            Assert.IsFalse(rig.core.ReleaseAimDrag(true));
            Assert.AreEqual(0, rig.core.ReleasePlaceAttempts);
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            SpatialRooms.AssertPose(start, rig.core.PreviewPose);
            StringAssert.Contains("blocked by STORAGE", rig.core.FootprintWords);
            rig.Run(3f);
            Assert.AreEqual(0, rig.writes);
        }

        [Test]
        public void AWayRefusedAtRelease_LeavesTheFootprintThere_SaysWhy_AndMovesNothing()
        {
            var room = SpatialRooms.Office();
            var rig = ReleaseRig.Open(room);
            Assert.IsTrue(rig.core.BeginAimDrag());
            rig.core.AimDragTo(ReleaseRig.Side);
            Assert.IsTrue(rig.core.AimValid, rig.core.AimProblem);
            Assert.AreEqual("Let go to place it here", rig.core.FootprintWords);

            // The scan now has a cabinet there (no room event): the route planned again at release refuses.
            ReleaseRig.AddCabinetBesideSide(room);
            Assert.IsFalse(rig.core.ReleaseAimDrag(true));
            Assert.AreEqual(0, rig.core.ReleasePlaces);
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            SpatialRooms.AssertPose(ReleaseRig.Facing(ReleaseRig.Side), rig.core.PreviewPose, 0.002f);
            Assert.IsFalse(rig.core.ShownValid, "it stays where it was let go, drawn as not clear");
            Assert.AreEqual("The way there is blocked by STORAGE", rig.core.FootprintWords);
            StringAssert.Contains("STORAGE", rig.core.StatusText);
            rig.Run(3f);
            Assert.AreEqual(0, rig.writes);

            // Confirm plans again (it does not trust the old answer either way): still blocked.
            Assert.IsFalse(rig.core.Confirm());
            StringAssert.Contains("blocked by STORAGE", rig.core.StatusText);
            rig.Run(3f);
            Assert.AreEqual(0, rig.writes);

            // The way clears: the same spot can now be confirmed, and the move is the swept route.
            room.ClearFurniture();
            rig.trace = new List<RootPose>();
            Assert.IsTrue(rig.core.Confirm(), rig.core.StatusText);
            rig.Run(6f);
            SpatialRooms.AssertPose(ReleaseRig.Facing(ReleaseRig.Side), rig.root);
            ReleaseRig.AssertTravelledTheSweptRoute(rig, true, "after the way cleared");
        }

        [Test]
        public void ConfirmRefusingAfterAReadyRelease_KeepsTheSpot_AndSaysWhy()
        {
            var rig = ReleaseRig.Open(SpatialRooms.Office());
            Assert.IsTrue(rig.core.BeginAimDrag());
            rig.core.AimDragTo(ReleaseRig.Side);
            Assert.IsTrue(rig.core.AimValid, rig.core.AimProblem);

            rig.held = true;                                  // a paper is in the user's hand
            Assert.IsFalse(rig.core.ReleaseAimDrag(true));
            Assert.AreEqual(1, rig.core.ReleasePlaceAttempts);
            Assert.AreEqual(0, rig.core.ReleasePlaces);
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            SpatialRooms.AssertPose(ReleaseRig.Facing(ReleaseRig.Side), rig.core.PreviewPose);
            StringAssert.Contains("paper", rig.core.FootprintWords, "never 'ready' followed by nothing");
            StringAssert.Contains("paper", rig.core.StatusText);
            rig.Run(3f);
            Assert.AreEqual(0, rig.writes);

            rig.held = false;
            Assert.IsTrue(rig.core.Confirm(), rig.core.StatusText);
            rig.Run(6f);
            SpatialRooms.AssertPose(ReleaseRig.Facing(ReleaseRig.Side), rig.root);
        }

        [Test]
        public void ATinyDrag_IsNotAPlacement()
        {
            var rig = AimRig.OpenFloor();
            RootPose start = rig.core.PreviewPose;
            Assert.IsTrue(rig.core.BeginAimDrag());
            rig.core.AimDragTo(start.position + rig.user.Right * 0.03f);
            Assert.IsTrue(rig.core.AimValid, rig.core.AimProblem);
            Assert.That(rig.core.AimDragDisplacement, Is.LessThan(rig.core.aimPlaceMinDrag));

            Assert.IsFalse(rig.core.ReleaseAimDrag(true));
            Assert.AreEqual(0, rig.core.ReleasePlaceAttempts);
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
            StringAssert.StartsWith("Moved only a little", rig.core.FootprintWords);
            rig.Run(3f);
            Assert.AreEqual(0, rig.writes);
        }

        [Test]
        public void ReasonableSpotsBesideTheShoulder_WereRefused_AndAreNowPlaced()
        {
            var room = SpatialRooms.Office();
            SeatedUser user = SpatialRooms.User();
            var cs = new CeilingPlacementSettings();

            // 1. Why they were refused: the usual route turns the office over the user's head and
            //    its near corner passes within 8 cm of the head's axis. A side step does not.
            foreach (Vector3 spot in new[] { ReleaseRig.BesideShoulder, ReleaseRig.CloseBeside })
            {
                WorkspaceRoute r; CeilingPathCheck c;
                Assert.IsFalse(CeilingPlacementEvaluator.PlanRoute(room, user, ReleaseRig.Desk, ReleaseRig.Facing(spot), SpatialRooms.Footprint, cs,
                                                                   ReleaseRig.Options(true), true, out r, out c), "usual shapes, " + spot);
                Assert.AreEqual(CeilingPathBlock.User, c.blockedBy, spot.ToString());
                Assert.IsTrue(FloorRoutePlanner.Plan(room, user, ReleaseRig.Desk, ReleaseRig.Facing(spot), SpatialRooms.Footprint, cs,
                                                     ReleaseRig.Options(true), true, out r, out c), c.Describe());
                Assert.AreEqual(4, r.count);
                Assert.That(Mathf.Abs(r[1].position.x - ReleaseRig.Desk.position.x), Is.GreaterThan(0.3f), "it steps sideways as it rises");
            }

            // 2. The closer one also broke the old 0.6 m circle round the head (the head moves
            //    toward the spot when the user leans to point at it); 0.45 m to the side now.
            var old = new FloorPlacementSettings { placedUserClearance = 0.6f, placedFurnitureClearance = 0.15f, placedWallMargin = 0.10f };
            FloorPoseProblem problem;
            Assert.IsFalse(FloorPlacementEvaluator.CheckPlacedPose(room, user, Vector3.forward, ReleaseRig.Facing(ReleaseRig.CloseBeside),
                                                                   SpatialRooms.Footprint, old, out problem));
            Assert.AreEqual(FloorPoseProblem.NearUser, problem);
            Assert.IsTrue(FloorPlacementEvaluator.CheckPlacedPose(room, user, Vector3.forward, ReleaseRig.Facing(ReleaseRig.CloseBeside),
                                                                  SpatialRooms.Footprint, new FloorPlacementSettings(), out problem), problem.ToString());

            // 3. Pointing at them now shows ready; letting go takes the office there along the swept
            //    side step, and Desk brings it back along the same poses.
            foreach (Vector3 spot in new[] { ReleaseRig.BesideShoulder, ReleaseRig.CloseBeside })
            {
                var rig = ReleaseRig.Open(SpatialRooms.Office());
                Assert.IsTrue(rig.core.BeginAimDrag());
                rig.core.AimDragTo(spot);
                Assert.IsTrue(rig.core.AimValid, spot + ": " + rig.core.AimProblem);
                rig.trace = new List<RootPose>();
                Assert.IsTrue(rig.core.ReleaseAimDrag(true), rig.core.StatusText);
                rig.Run(6f);
                Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);
                SpatialRooms.AssertPose(ReleaseRig.Facing(spot), rig.root);
                ReleaseRig.AssertTravelledTheSweptRoute(rig, true, "to " + spot);

                rig.trace = new List<RootPose>();
                Assert.IsTrue(rig.core.RequestMode(PlacementMode.Desk), rig.core.StatusText);
                rig.Run(6f);
                Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
                SpatialRooms.AssertPose(ReleaseRig.Desk, rig.root);
                ReleaseRig.AssertTravelledTheSweptRoute(rig, false, "back from " + spot);
            }
        }

        [Test]
        public void GenuinelyBadSpots_AreStillRefused_AndReleasingThereMovesNothing()
        {
            var rig = ReleaseRig.Open(SpatialRooms.Office());
            RootPose start = rig.core.PreviewPose;
            var cases = new[]
            {
                new KeyValuePair<Vector3, string>(new Vector3(2.3f, 0f, 0f), "Too close to the wall"),
                new KeyValuePair<Vector3, string>(new Vector3(0f, 0f, 1.5f), "Something is in the way"),
                new KeyValuePair<Vector3, string>(new Vector3(0.64f, 0f, -0.64f), "Too close to you"),
                new KeyValuePair<Vector3, string>(ReleaseRig.Behind, "The way there passes too close to you"),
                new KeyValuePair<Vector3, string>(new Vector3(-9f, 0f, 0f), "Off the scanned floor"),
            };
            foreach (var c in cases)
            {
                Assert.IsTrue(rig.core.BeginAimDrag(), c.Value);
                rig.core.AimDragTo(c.Key);
                Assert.IsFalse(rig.core.AimValid, c.Value);
                Assert.AreEqual(c.Value, rig.core.AimProblem);
                Assert.AreEqual(c.Value, rig.core.FootprintWords);
                Assert.IsFalse(rig.core.ReleaseAimDrag(true), c.Value);
                Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase, c.Value);
                SpatialRooms.AssertPose(start, rig.core.PreviewPose);
                StringAssert.StartsWith(c.Value, rig.core.FootprintWords);
            }
            Assert.AreEqual(0, rig.core.ReleasePlaceAttempts);
            rig.Run(3f);
            Assert.AreEqual(0, rig.writes);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
        }

        [Test]
        public void ThePlacementMargins_StayAboveTheSweptRoutesOwn()
        {
            var f = new FloorPlacementSettings();
            var c = new CeilingPlacementSettings();
            float sample = CeilingPlacementEvaluator.SampleMarginHorizontal(c);
            Assert.That(f.placedWallMargin, Is.GreaterThan(sample), "a spot shown clear of the wall is one the route accepts");
            Assert.That(f.placedFurnitureClearance, Is.GreaterThan(sample));
            Assert.That(f.placedUserClearance, Is.GreaterThan(c.userKeepOutRadius + 0.5f * CeilingPlacementEvaluator.EffectiveSweepStep(c)));
            Assert.That(f.placedUserClearance, Is.LessThanOrEqualTo(f.userClearance));
        }

        [Test]
        public void TheGuideHook_CentresThePreviewWhereTheUserLooks_WithoutMovingTheOffice()
        {
            var rig = AimRig.OpenFloor();
            RootPose start = rig.core.PreviewPose;
            Vector3 look = new Vector3(1.15f, 0f, -0.55f);
            Assert.IsTrue(rig.core.CentrePreviewOn(look), rig.core.StatusText);
            Assert.That(AimRig.Flat(AimRig.FootprintCentre(rig.core), look), Is.LessThan(0.02f));
            Assert.IsTrue(rig.core.PreviewValid);
            Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);

            // On the desk: refused, the preview stays where it was, and it says why.
            RootPose there = rig.core.PreviewPose;
            Assert.IsFalse(rig.core.CentrePreviewOn(new Vector3(0f, 0f, 1.2f)));
            SpatialRooms.AssertPose(there, rig.core.PreviewPose);
            StringAssert.StartsWith("Something is in the way", rig.core.FootprintWords);
            Assert.That((there.position - start.position).magnitude, Is.GreaterThan(0.1f));
            rig.Run(3f);
            Assert.AreEqual(0, rig.writes);
        }
    }
}
#endif
