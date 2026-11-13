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
    }
}
