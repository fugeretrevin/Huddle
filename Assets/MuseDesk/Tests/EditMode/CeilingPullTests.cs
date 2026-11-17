// =============================================================================
// MuseDesk · EditMode tests · "reach up and pull it down" (CeilingPullMath, CeilingPullMachine)
// Pure logic only. Not proven here: real hands, the drawing, the director calls.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;

namespace MuseDesk.Tests.EditMode
{
    public class CeilingPullTests
    {
        const float Dt = 1f / 72f;
        static readonly Vector3 Hand0 = new Vector3(0.1f, 1.8f, 0.3f);
        const CeilingPullDirection Down = CeilingPullDirection.Down;

        static CeilingPullState Step(CeilingPullMachine m, bool pinch, Vector3 hand, bool targeted = true, bool allowed = true,
                                     CeilingPullDirection dir = CeilingPullDirection.Down, float dt = Dt)
        {
            return m.Tick(dt, true, pinch, hand, targeted, allowed, dir);
        }

        // Pinch starts (rising edge) at Hand0.
        static CeilingPullMachine Armed(CeilingPullDirection dir = CeilingPullDirection.Down)
        {
            var m = new CeilingPullMachine();
            Step(m, false, Hand0, dir: dir);
            Assert.AreEqual(CeilingPullState.Armed, Step(m, true, Hand0, dir: dir));
            return m;
        }

        [Test]
        public void SegmentDistance_Basics()
        {
            Assert.AreEqual(1f, CeilingPullMath.SegmentSegmentDistance(new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0)), 1e-4f);
            Assert.AreEqual(0f, CeilingPullMath.SegmentSegmentDistance(new Vector3(-1, 0, 0), new Vector3(1, 0, 0), new Vector3(0, -1, 0), new Vector3(0, 1, 0)), 1e-4f);
            Assert.AreEqual(2f, CeilingPullMath.SegmentSegmentDistance(Vector3.zero, Vector3.zero, new Vector3(2, 0, 0), new Vector3(2, 0, 0)), 1e-4f);
            Assert.AreEqual(1f, CeilingPullMath.SegmentSegmentDistance(new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(3, 1, 0), new Vector3(2, 1, 0)) - Mathf.Sqrt(2f) + 1f, 1e-3f);
        }

        [Test]
        public void Ray_AtMast_Targets_AndFarRay_Does_Not()
        {
            Vector3 top = new Vector3(0, 2.5f, -1f), bottom = new Vector3(0, 2.0f, -1f), centre = new Vector3(0, 1.9f, -1f);
            Vector3 hand = new Vector3(0.2f, 1.4f, 0f);
            Vector3 toMast = (new Vector3(0, 2.2f, -1f) - hand).normalized;
            Assert.IsTrue(CeilingPullMath.RayTargetsPod(hand, toMast, top, bottom, centre, 0.25f, 0.3f));
            // 0.2 m to the side of the mast at its distance: still inside the generous radius.
            Vector3 near = (new Vector3(0.2f, 2.2f, -1f) - hand).normalized;
            Assert.IsTrue(CeilingPullMath.RayTargetsPod(hand, near, top, bottom, centre, 0.25f, 0.3f));
            // A metre to the side: not.
            Vector3 far = (new Vector3(1.2f, 2.2f, -1f) - hand).normalized;
            Assert.IsFalse(CeilingPullMath.RayTargetsPod(hand, far, top, bottom, centre, 0.25f, 0.3f));
            // Pointing away (behind the hand) does not count even if the line would hit.
            Assert.IsFalse(CeilingPullMath.RayTargetsPod(hand, -toMast, top, bottom, centre, 0.25f, 0.3f));
        }

        [Test]
        public void Pull_Down_ThenRelease_Fires()
        {
            var m = Armed();
            Vector3 h = Hand0;
            for (int i = 0; i < 30; i++) { h.y = Hand0.y - 0.13f * (i + 1) / 30f; Step(m, true, h); }
            Assert.AreEqual(CeilingPullState.Pulling, m.State);
            Assert.AreEqual(1f, m.Progress, 1e-3f);
            Assert.AreEqual(CeilingPullState.Fired, Step(m, false, h));
            Assert.AreEqual(1, m.Fires);
            Assert.AreEqual(CeilingPullState.Idle, Step(m, false, h));
        }

        [Test]
        public void Progress_Tracks_Travel()
        {
            var m = Armed();
            Step(m, true, Hand0 + Vector3.down * 0.06f);
            Assert.AreEqual(0.5f, m.Progress, 1e-3f);
            Assert.AreEqual(CeilingPullState.Pulling, m.State);
        }

        [Test]
        public void Release_Early_Cancels()
        {
            var m = Armed();
            Step(m, true, Hand0 + Vector3.down * 0.07f);
            Assert.AreEqual(CeilingPullState.Cancelled, Step(m, false, Hand0 + Vector3.down * 0.07f));
            Assert.AreEqual(CeilingPullCancel.Released, m.LastCancel);
            Assert.AreEqual(0, m.Fires);
        }

        [Test]
        public void Sideways_Cancels()
        {
            var m = Armed();
            Assert.AreEqual(CeilingPullState.Cancelled, Step(m, true, Hand0 + new Vector3(0.25f, -0.05f, 0f)));
            Assert.AreEqual(CeilingPullCancel.Sideways, m.LastCancel);
        }

        [Test]
        public void WrongWay_Cancels()
        {
            var m = Armed();
            Assert.AreEqual(CeilingPullState.Cancelled, Step(m, true, Hand0 + Vector3.up * 0.10f));
            Assert.AreEqual(CeilingPullCancel.WrongWay, m.LastCancel);
        }

        [Test]
        public void Too_Slow_TimesOut()
        {
            var m = Armed();
            Vector3 h = Hand0 + Vector3.down * 0.05f;
            CeilingPullState s = CeilingPullState.Pulling;
            for (int i = 0; i < 120 && s != CeilingPullState.Cancelled; i++) s = Step(m, true, h);
            Assert.AreEqual(CeilingPullState.Cancelled, s);
            Assert.AreEqual(CeilingPullCancel.Timeout, m.LastCancel);
        }

        [Test]
        public void Reaching_ThenGoingBack_DoesNotFire()
        {
            var m = Armed();
            Step(m, true, Hand0 + Vector3.down * 0.13f);
            Step(m, true, Hand0 + Vector3.down * 0.03f);
            Assert.AreEqual(CeilingPullState.Cancelled, Step(m, false, Hand0 + Vector3.down * 0.03f));
        }

        [Test]
        public void Pinch_Already_Closed_Cannot_Arm()
        {
            var m = new CeilingPullMachine();
            Step(m, true, Hand0, targeted: false);            // pinch began while not pointing at the pod
            Assert.AreEqual(CeilingPullState.Idle, Step(m, true, Hand0, targeted: true));
            Step(m, false, Hand0);
            Assert.AreEqual(CeilingPullState.Armed, Step(m, true, Hand0, targeted: true));
        }

        [Test]
        public void Blocked_Start_Is_Ignored_And_Block_Mid_Gesture_Cancels()
        {
            var m = new CeilingPullMachine();
            Step(m, false, Hand0);
            Assert.AreEqual(CeilingPullState.Idle, Step(m, true, Hand0, allowed: false));
            var n = Armed();
            Assert.AreEqual(CeilingPullState.Cancelled, Step(n, true, Hand0 + Vector3.down * 0.05f, allowed: false));
            Assert.AreEqual(CeilingPullCancel.Blocked, n.LastCancel);
        }

        [Test]
        public void Lost_Tracking_Cancels()
        {
            var m = Armed();
            Assert.AreEqual(CeilingPullState.Cancelled, m.Tick(Dt, false, false, Hand0, true, true, Down));
            Assert.AreEqual(CeilingPullCancel.Lost, m.LastCancel);
        }

        [Test]
        public void Push_Up_Fires_For_Up_Direction_Only()
        {
            var up = CeilingPullDirection.Up;
            var m = Armed(up);
            Vector3 h = Hand0 + Vector3.up * 0.13f;
            Step(m, true, h, dir: up);
            Assert.AreEqual(CeilingPullState.Fired, Step(m, false, h, dir: up));
            Assert.AreEqual(up, m.Direction);
            // Moving down while pushing up is the wrong way.
            var n = Armed(up);
            Assert.AreEqual(CeilingPullState.Cancelled, Step(n, true, Hand0 + Vector3.down * 0.12f, dir: up));
        }

        [Test]
        public void Cooldown_Blocks_An_Immediate_Second_Pull()
        {
            var m = Armed();
            Step(m, true, Hand0 + Vector3.down * 0.13f);
            Step(m, false, Hand0 + Vector3.down * 0.13f);       // fired
            Step(m, false, Hand0);                              // idle
            Assert.AreEqual(CeilingPullState.Idle, Step(m, true, Hand0));
            for (int i = 0; i < 80; i++) Step(m, false, Hand0);
            Assert.AreEqual(CeilingPullState.Armed, Step(m, true, Hand0));
        }
    }
}
#endif
