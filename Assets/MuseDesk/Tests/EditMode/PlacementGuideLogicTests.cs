using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;

namespace MuseDesk.Tests.EditMode
{
    public class PlacementGuideLogicTests
    {
        static Vector3 Dir(float pitchDeg)
        {
            float r = pitchDeg * Mathf.Deg2Rad;
            return new Vector3(0f, Mathf.Sin(r), Mathf.Cos(r));
        }

        [Test]
        public void Pitch_IsSignedDegrees()
        {
            Assert.AreEqual(0f, PlacementGuideLogic.PitchDeg(Vector3.forward), 0.01f);
            Assert.AreEqual(90f, PlacementGuideLogic.PitchDeg(Vector3.up), 0.01f);
            Assert.AreEqual(-45f, PlacementGuideLogic.PitchDeg(new Vector3(0f, -1f, 1f)), 0.01f);
        }

        [Test]
        public void GazeAtTarget_UsesThresholds()
        {
            Assert.IsTrue(PlacementGuideLogic.GazeAtTarget(GuideTarget.Ceiling, Dir(36f), 35f, 30f));
            Assert.IsFalse(PlacementGuideLogic.GazeAtTarget(GuideTarget.Ceiling, Dir(34f), 35f, 30f));
            Assert.IsTrue(PlacementGuideLogic.GazeAtTarget(GuideTarget.Floor, Dir(-31f), 35f, 30f));
            Assert.IsFalse(PlacementGuideLogic.GazeAtTarget(GuideTarget.Floor, Dir(-29f), 35f, 30f));
            Assert.IsFalse(PlacementGuideLogic.GazeAtTarget(GuideTarget.Floor, Dir(40f), 35f, 30f));
            Assert.IsFalse(PlacementGuideLogic.GazeAtTarget(GuideTarget.None, Dir(80f), 35f, 30f));
        }

        [Test]
        public void Dwell_RequestsOnceAfterHold()
        {
            var g = new PlacementGuideLogic();
            g.Begin(GuideTarget.Ceiling);
            Assert.AreEqual(GuideState.Guiding, g.State);
            int requests = 0;
            for (int i = 0; i < 100; i++) if (g.Tick(0.01f, Dir(50f))) requests++;
            Assert.AreEqual(1, requests);
            Assert.AreEqual(GuideState.Requested, g.State);
            Assert.IsFalse(g.RequestedByKey);
            Assert.IsTrue(g.ConsumeRequestEdge());
            Assert.IsFalse(g.ConsumeRequestEdge());
        }

        [Test]
        public void Dwell_NotBeforeHoldTime()
        {
            var g = new PlacementGuideLogic();
            g.Begin(GuideTarget.Floor);
            for (int i = 0; i < 50; i++) Assert.IsFalse(g.Tick(0.01f, Dir(-60f)));   // 0.5 s
            Assert.Less(g.Fill, 1f);
            Assert.AreEqual(GuideState.Guiding, g.State);
        }

        [Test]
        public void Dwell_DrainsWhenLookingAway()
        {
            var g = new PlacementGuideLogic();
            g.Begin(GuideTarget.Ceiling);
            for (int i = 0; i < 30; i++) g.Tick(0.01f, Dir(60f));
            float peak = g.Fill;
            Assert.Greater(peak, 0.4f);
            for (int i = 0; i < 10; i++) g.Tick(0.01f, Dir(0f));
            Assert.Less(g.Fill, peak);
            for (int i = 0; i < 100; i++) g.Tick(0.01f, Dir(0f));
            Assert.AreEqual(0f, g.Fill, 1e-4f);
        }

        [Test]
        public void Timeout_OffersStartKey_AndKeyRequests()
        {
            var g = new PlacementGuideLogic();
            g.Begin(GuideTarget.Floor);
            Assert.IsFalse(g.PressStart());                       // not offered yet
            g.Tick(7.9f, Dir(0f));
            Assert.IsFalse(g.StartOffered);
            g.Tick(0.2f, Dir(0f));
            Assert.IsTrue(g.StartOffered);
            Assert.IsTrue(g.PressStart());
            Assert.AreEqual(GuideState.Requested, g.State);
            Assert.IsTrue(g.RequestedByKey);
        }

        [Test]
        public void Requested_ToPreviewing_ToDone()
        {
            var g = new PlacementGuideLogic();
            g.Begin(GuideTarget.Ceiling);
            g.Tick(1f, Dir(70f));
            g.ReportRequestResult(true);
            Assert.AreEqual(GuideState.Previewing, g.State);
            g.ReportPreviewing(true);
            Assert.AreEqual(GuideState.Previewing, g.State);
            g.ReportPreviewing(false);
            Assert.AreEqual(GuideState.Done, g.State);
            Assert.IsFalse(g.Active);
        }

        [Test]
        public void RefusedRequest_IsCancelled()
        {
            var g = new PlacementGuideLogic();
            g.Begin(GuideTarget.Ceiling);
            g.Tick(1f, Dir(70f));
            g.ReportRequestResult(false);
            Assert.AreEqual(GuideState.Cancelled, g.State);
        }

        [Test]
        public void Cancel_FromGuiding_IsCancelled_AndTickDoesNothing()
        {
            var g = new PlacementGuideLogic();
            g.Begin(GuideTarget.Floor);
            g.Cancel();
            Assert.AreEqual(GuideState.Cancelled, g.State);
            Assert.IsFalse(g.Tick(5f, Dir(-80f)));
            Assert.AreEqual(GuideState.Cancelled, g.State);
        }

        [Test]
        public void Begin_RestartsFromTerminalState()
        {
            var g = new PlacementGuideLogic();
            g.Begin(GuideTarget.Floor);
            g.Cancel();
            g.Begin(GuideTarget.Ceiling);
            Assert.AreEqual(GuideState.Guiding, g.State);
            Assert.AreEqual(GuideTarget.Ceiling, g.Target);
            Assert.AreEqual(0f, g.Fill);
            Assert.AreEqual(0f, g.Elapsed);
        }

        [Test]
        public void RayPlane_HitsCeilingAndFloor()
        {
            Vector3 eye = new Vector3(1f, 1.2f, 2f);
            Assert.IsTrue(PlacementGuideLogic.RayPlane(eye, new Vector3(0f, 1f, 1f).normalized, 2.6f, out var up));
            Assert.AreEqual(2.6f, up.y, 1e-4f);
            Assert.AreEqual(2f + 1.4f, up.z, 1e-3f);
            Assert.IsTrue(PlacementGuideLogic.RayPlane(eye, new Vector3(0f, -1f, 0.5f).normalized, 0f, out var down));
            Assert.AreEqual(0f, down.y, 1e-4f);
            Assert.IsFalse(PlacementGuideLogic.RayPlane(eye, Vector3.forward, 2.6f, out _));
            Assert.IsFalse(PlacementGuideLogic.RayPlane(eye, Vector3.down, 2.6f, out _));   // plane behind
        }

        [Test]
        public void Sweep_StartsAtGaze_EndsAtCandidate()
        {
            Vector3 a = new Vector3(0f, 2.6f, 0f), b = new Vector3(2f, 2.6f, 0f);
            Assert.AreEqual(0f, PlacementGuideLogic.SweepCentre(a, b, 0f, 0.8f).x, 1e-4f);
            Assert.AreEqual(2f, PlacementGuideLogic.SweepCentre(a, b, 0.8f, 0.8f).x, 1e-4f);
            float mid = PlacementGuideLogic.SweepCentre(a, b, 0.4f, 0.8f).x;
            Assert.Greater(mid, 1f);                                // ease-out: past halfway at half time
            Assert.AreEqual(2f, PlacementGuideLogic.SweepCentre(a, b, 5f, 0.8f).x, 1e-4f);
        }

        [Test]
        public void ClampOrigin_LimitsHorizontalDistance()
        {
            Vector3 c = new Vector3(0f, 2.6f, 0f);
            Vector3 o = PlacementGuideLogic.ClampOrigin(new Vector3(10f, 2.6f, 0f), c, 3f);
            Assert.AreEqual(3f, o.x, 1e-4f);
            Vector3 near = new Vector3(1f, 2.6f, 1f);
            Assert.AreEqual(near, PlacementGuideLogic.ClampOrigin(near, c, 3f));
        }

        // ───────────────────────── Wall card (rc9) ─────────────────────────

        [Test]
        public void Wall_StepOne_DwellOnAcceptedWall_AdvancesToStepTwoOnce()
        {
            var g = new PlacementGuideLogic();
            g.Begin(GuideTarget.Wall);
            Assert.AreEqual(GuideState.Guiding, g.State);
            Assert.AreEqual(1, g.WallStep);
            int advances = 0;
            for (int i = 0; i < 100; i++) if (g.TickWall(0.01f, true, false)) advances++;
            Assert.AreEqual(1, advances);
            Assert.AreEqual(GuideState.Previewing, g.State);
            Assert.AreEqual(2, g.WallStep);
            Assert.AreEqual(WallGuideAdvance.Gaze, g.WallAdvancedBy);
            Assert.IsTrue(g.Active);
            Assert.IsFalse(g.ConsumeRequestEdge(), "the wall card never requests a mode: the preview is already open");
        }

        [Test]
        public void Wall_StepOne_NotBeforeDwell_AndDrainsWhenTheGazeLeaves()
        {
            var g = new PlacementGuideLogic();
            g.Begin(GuideTarget.Wall);
            for (int i = 0; i < 50; i++) Assert.IsFalse(g.TickWall(0.01f, true, false));   // 0.5 s
            float peak = g.Fill;
            Assert.Greater(peak, 0.7f);
            Assert.AreEqual(1, g.WallStep);
            for (int i = 0; i < 10; i++) g.TickWall(0.01f, false, false);
            Assert.Less(g.Fill, peak);
            Assert.AreEqual(GuideState.Guiding, g.State);
        }

        [Test]
        public void Wall_UserAimingOrTimeout_AdvancesToStepTwo_NeverOffersAStartKey()
        {
            var g = new PlacementGuideLogic();
            g.Begin(GuideTarget.Wall);
            Assert.IsTrue(g.TickWall(0.01f, false, true));
            Assert.AreEqual(WallGuideAdvance.Aim, g.WallAdvancedBy);
            Assert.AreEqual(2, g.WallStep);

            g.Begin(GuideTarget.Wall);
            Assert.AreEqual(WallGuideAdvance.None, g.WallAdvancedBy);
            Assert.IsFalse(g.TickWall(7.9f, false, false));
            Assert.IsFalse(g.StartOffered, "the wall card has no keys of its own");
            Assert.IsFalse(g.PressStart());
            Assert.IsTrue(g.TickWall(0.2f, false, false));
            Assert.AreEqual(WallGuideAdvance.Timeout, g.WallAdvancedBy);
            Assert.IsFalse(g.StartOffered);
        }

        [Test]
        public void Wall_PreviewClosing_EndsTheCard()
        {
            var g = new PlacementGuideLogic();
            g.Begin(GuideTarget.Wall);
            g.ReportWallOpen(true);
            Assert.AreEqual(GuideState.Guiding, g.State);
            g.ReportWallOpen(false);                       // Cancel / mode change during step 1
            Assert.AreEqual(GuideState.Cancelled, g.State);
            Assert.IsFalse(g.Active);
            Assert.AreEqual(0, g.WallStep);

            g.Begin(GuideTarget.Wall);
            g.TickWall(1f, true, false);
            Assert.AreEqual(2, g.WallStep);
            g.ReportWallOpen(false);                       // Place during step 2
            Assert.AreEqual(GuideState.Done, g.State);
            Assert.IsFalse(g.Active);
            Assert.IsFalse(g.TickWall(1f, true, true), "terminal until the next Begin");
        }

        [Test]
        public void Wall_CancelEndsTheCard_FromEitherStep()
        {
            var g = new PlacementGuideLogic();
            g.Begin(GuideTarget.Wall);
            g.Cancel();
            Assert.AreEqual(GuideState.Cancelled, g.State);
            g.Begin(GuideTarget.Wall);
            g.TickWall(0.01f, false, true);
            g.Cancel();
            Assert.AreEqual(GuideState.Done, g.State);
        }

        [Test]
        public void Wall_FloorAndCeilingPathsIgnoreTheWall_AndViceVersa()
        {
            var g = new PlacementGuideLogic();
            g.Begin(GuideTarget.Wall);
            Assert.IsFalse(g.Tick(1f, Dir(-80f)), "the pitch dwell never runs for the wall");
            Assert.AreEqual(GuideState.Guiding, g.State);
            Assert.IsFalse(PlacementGuideLogic.GazeAtTarget(GuideTarget.Wall, Dir(0f), 35f, 30f));

            var f = new PlacementGuideLogic();
            f.Begin(GuideTarget.Floor);
            Assert.IsFalse(f.TickWall(1f, true, true));
            f.ReportWallOpen(false);
            Assert.AreEqual(GuideState.Guiding, f.State, "the wall's preview closing never ends a floor guide");
            Assert.AreEqual(0, f.WallStep);
        }

        [Test]
        public void Wall_CardWords_UseTheDocksKeys_AndNoneOfItsOwn()
        {
            Assert.AreEqual("Look at a wall", PlacementGuideLogic.WallTitle(1));
            Assert.AreEqual("Point and pinch to move it", PlacementGuideLogic.WallTitle(2));
            string fits = PlacementGuideLogic.WallBody(2, true, true);
            string refused = PlacementGuideLogic.WallBody(2, true, false);
            string none = PlacementGuideLogic.WallBody(2, false, false);
            StringAssert.Contains(SpacesDockLogic.Place, fits);
            StringAssert.Contains(SpacesDockLogic.NextSpot, refused);
            StringAssert.Contains(SpacesDockLogic.NextSpot, none);
            foreach (string s in new[] { fits, refused, none, PlacementGuideLogic.WallBody(1, true, true) })
            {
                StringAssert.DoesNotContain("Place here", s, "the floor card's key, not the dock's");
                StringAssert.DoesNotContain("Start here", s);
                StringAssert.DoesNotContain("Other spot", s);
                Assert.LessOrEqual(s.Length, 52, "fits the card body: " + s);
            }
        }

        [Test]
        public void RayHitsBox_FrontOnlyWithinRange_AndRespectsRotation()
        {
            Vector3 c = new Vector3(0f, 1.5f, 2f), half = new Vector3(0.4f, 0.3f, 0.1f);
            Vector3 eye = new Vector3(0f, 1.5f, 0f);
            Assert.IsTrue(PlacementGuideLogic.RayHitsBox(eye, Vector3.forward, c, Quaternion.identity, half, 8f));
            Assert.IsTrue(PlacementGuideLogic.RayHitsBox(eye, new Vector3(0.35f, 0f, 2f), c, Quaternion.identity, half, 8f), "near the edge");
            Assert.IsFalse(PlacementGuideLogic.RayHitsBox(eye, new Vector3(0.6f, 0f, 2f), c, Quaternion.identity, half, 8f), "past the edge");
            Assert.IsFalse(PlacementGuideLogic.RayHitsBox(eye, Vector3.back, c, Quaternion.identity, half, 8f), "behind");
            Assert.IsFalse(PlacementGuideLogic.RayHitsBox(eye, Vector3.forward, c, Quaternion.identity, half, 1f), "out of range");
            Assert.IsFalse(PlacementGuideLogic.RayHitsBox(eye, Vector3.zero, c, Quaternion.identity, half, 8f));
            // Turned 90 degrees: the long side now runs along Z, so a ray 0.35 m off to the side misses.
            Quaternion r = Quaternion.Euler(0f, 90f, 0f);
            Assert.IsFalse(PlacementGuideLogic.RayHitsBox(eye, new Vector3(0.35f, 0f, 2f), c, r, half, 8f));
            Assert.IsTrue(PlacementGuideLogic.RayHitsBox(eye, Vector3.forward, c, r, half, 8f));
        }

        [Test]
        public void SideOf_IsTheUsersRightOrLeft_OrAhead()
        {
            Vector3 head = new Vector3(0f, 1.2f, 0f);
            Assert.AreEqual(1, PlacementGuideLogic.SideOf(head, Vector3.forward, new Vector3(1.5f, 1.4f, 0.5f)), "facing +Z the user's right is +X");
            Assert.AreEqual(-1, PlacementGuideLogic.SideOf(head, Vector3.forward, new Vector3(-1.5f, 1.4f, 0.5f)));
            Assert.AreEqual(0, PlacementGuideLogic.SideOf(head, Vector3.forward, new Vector3(0.1f, 1.6f, 2f)));
            Assert.AreEqual(0, PlacementGuideLogic.SideOf(head, Vector3.up, new Vector3(1f, 1f, 1f)), "no level forward");
        }

        [Test]
        public void WallSpotMoved_CountsAimingOnly()
        {
            Vector3 a = new Vector3(1f, 1.5f, 2f);
            Assert.IsFalse(PlacementGuideLogic.WallSpotMoved(true, a, false, a), "no spot shown");
            Assert.IsTrue(PlacementGuideLogic.WallSpotMoved(false, Vector3.zero, true, a), "a spot appeared");
            Assert.IsFalse(PlacementGuideLogic.WallSpotMoved(true, a, true, a + new Vector3(0.02f, 0f, 0f)));
            Assert.IsTrue(PlacementGuideLogic.WallSpotMoved(true, a, true, a + new Vector3(0.2f, 0f, 0f)));
        }
    }
}
