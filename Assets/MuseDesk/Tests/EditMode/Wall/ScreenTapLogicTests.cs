// =============================================================================
// MuseDesk · EditMode tests · rc25 screen-panel taps on the agents' app (ScreenTapLogic). Pure logic only: a fingertip
// coming onto the picture from in front taps once, at the right (u, v); brushing in sideways, reaching around and
// resting on the surface do not repeat. Not proven here: real hand tracking, the PC window receiving it.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using NUnit.Framework;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class ScreenTapLogicTests
    {
        static bool Step(ScreenTapLogic t, float x, float y, float d, out float u, out float v, int finger = 0)
            => t.Step(finger, true, x, y, d, out u, out v);

        [Test]
        public void PressFromInFrontTapsOnceAtTheRightSpot()
        {
            var t = new ScreenTapLogic();
            float u, v;
            Assert.IsFalse(Step(t, 0.25f, 0.25f, -0.05f, out u, out v));
            Assert.IsFalse(Step(t, 0.25f, 0.25f, -0.01f, out u, out v));
            Assert.IsTrue(Step(t, 0.25f, 0.25f, -0.002f, out u, out v));
            Assert.AreEqual(0.75f, u, 1e-4f);     // right of centre
            Assert.AreEqual(0.25f, v, 1e-4f);     // upper half (top-left origin)
            // Resting on / pushing through the surface does not tap again.
            Assert.IsFalse(Step(t, 0.25f, 0.25f, 0.0f, out u, out v));
            Assert.IsFalse(Step(t, 0.25f, 0.25f, 0.01f, out u, out v));
            // A small pull back is not enough...
            Assert.IsFalse(Step(t, 0.25f, 0.25f, -0.015f, out u, out v));
            Assert.IsFalse(Step(t, 0.25f, 0.25f, -0.001f, out u, out v));
            // ...a real one is.
            Assert.IsFalse(Step(t, 0.25f, 0.25f, -0.03f, out u, out v));
            Assert.IsTrue(Step(t, 0.25f, 0.25f, -0.001f, out u, out v));
        }

        [Test]
        public void SlidingInAlongTheSurfaceIsNotATap()
        {
            var t = new ScreenTapLogic();
            float u, v;
            Assert.IsFalse(Step(t, 0.7f, 0f, -0.002f, out u, out v));   // beside the picture, at surface depth
            Assert.IsFalse(Step(t, 0.45f, 0f, -0.002f, out u, out v));  // slides onto it: never armed
        }

        [Test]
        public void ReachingAroundBehindIsNotATap()
        {
            var t = new ScreenTapLogic();
            float u, v;
            Assert.IsFalse(Step(t, 0f, 0f, -0.05f, out u, out v));
            Assert.IsFalse(Step(t, 0f, 0f, 0.2f, out u, out v));        // far behind: disarmed
            Assert.IsFalse(Step(t, 0f, 0f, -0.002f, out u, out v));
        }

        [Test]
        public void LosingTrackingDisarms()
        {
            var t = new ScreenTapLogic();
            float u, v;
            Assert.IsFalse(Step(t, 0f, 0f, -0.05f, out u, out v));
            Assert.IsFalse(t.Step(0, false, 0f, 0f, -0.05f, out u, out v));
            Assert.IsFalse(Step(t, 0f, 0f, -0.002f, out u, out v));
        }

        [Test]
        public void EachFingerIsSeparate()
        {
            var t = new ScreenTapLogic();
            float u, v;
            Assert.IsFalse(Step(t, -0.4f, -0.4f, -0.05f, out u, out v, 0));
            Assert.IsFalse(Step(t, 0.4f, 0.4f, -0.05f, out u, out v, 1));
            Assert.IsTrue(Step(t, 0.4f, 0.4f, -0.001f, out u, out v, 1));
            Assert.AreEqual(0.9f, u, 1e-4f);
            Assert.AreEqual(0.1f, v, 1e-4f);
            Assert.IsTrue(Step(t, -0.4f, -0.4f, -0.001f, out u, out v, 0));
            Assert.AreEqual(0.1f, u, 1e-4f);
            Assert.AreEqual(0.9f, v, 1e-4f);
            Assert.IsFalse(t.Step(5, true, 0f, 0f, 0f, out u, out v));  // unknown finger: ignored
        }
    }
}
#endif
