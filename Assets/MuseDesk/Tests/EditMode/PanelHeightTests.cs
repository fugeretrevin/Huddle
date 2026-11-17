// MuseDesk · EditMode tests · movable panel height (pure logic in PanelLayoutMath)
#if MUSEDESK_SPATIAL_TESTS
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;

namespace MuseDesk.Tests.EditMode
{
    [Category("MuseDesk")]
    public class PanelHeightTests
    {
        const float Desk = 0.72f;

        [Test]
        public void PivotFollowsTheHandOneToOneIncludingHeight()
        {
            Vector3 p = PanelLayoutMath.FollowHand(new Vector3(0.1f, 0.80f, 0.3f), new Vector3(1f, 1f, 1f), new Vector3(1.05f, 0.93f, 0.9f));
            Assert.AreEqual(0.73f, p.y, 1e-5f);
            Assert.AreEqual(0.15f, p.x, 1e-5f);
            Assert.AreEqual(0.20f, p.z, 1e-5f);
        }

        [Test]
        public void BandIsMinusFiveToPlusFortyFiveCmAroundTheDesk()
        {
            float lo, hi;
            PanelLayoutMath.HeightBand(Desk, Desk + 0.1f, 0.05f, 0.45f, out lo, out hi);
            Assert.AreEqual(Desk - 0.05f, lo, 1e-6f);
            Assert.AreEqual(Desk + 0.45f, hi, 1e-6f);
            Assert.AreEqual(hi, PanelLayoutMath.ClampHeight(Desk + 2f, lo, hi), 1e-6f);
            Assert.AreEqual(lo, PanelLayoutMath.ClampHeight(Desk - 1f, lo, hi), 1e-6f);
            Assert.AreEqual(Desk + 0.2f, PanelLayoutMath.ClampHeight(Desk + 0.2f, lo, hi), 1e-6f);
        }

        [Test]
        public void BandAlwaysContainsTheDefaultHeight()
        {
            float lo, hi;
            PanelLayoutMath.HeightBand(Desk, Desk + 0.60f, 0.05f, 0.45f, out lo, out hi);
            Assert.AreEqual(Desk + 0.60f, hi, 1e-6f);
            PanelLayoutMath.HeightBand(Desk, Desk - 0.30f, 0.05f, 0.45f, out lo, out hi);
            Assert.AreEqual(Desk - 0.30f, lo, 1e-6f);
        }

        [Test]
        public void BandNeverGoesBelowTheFloor()
        {
            float lo, hi;
            PanelLayoutMath.HeightBand(0.02f, 0.10f, 0.05f, 0.45f, out lo, out hi, 0f);
            Assert.AreEqual(0f, lo, 1e-6f);
        }

        [Test]
        public void DragRotationOnlyTurnsAboutUp()
        {
            Quaternion tilted = Quaternion.Euler(-20f, 30f, 0f);
            Quaternion r = PanelLayoutMath.UprightTurn(tilted, 40f);
            Assert.AreEqual(Vector3.Dot(tilted * Vector3.up, Vector3.up), Vector3.Dot(r * Vector3.up, Vector3.up), 1e-4f, "tilt kept");
            Quaternion level = PanelLayoutMath.UprightTurn(Quaternion.identity, 75f);
            Assert.That(Vector3.Angle(level * Vector3.up, Vector3.up), Is.LessThan(1e-3f), "stays upright");
        }

        [Test]
        public void ReachClampKeepsTheChosenHeight()
        {
            Vector3 head = new Vector3(0f, 1.2f, 0f);
            Vector3 p = new Vector3(0.3f, 0.80f, 2.0f);
            Vector3 r = PanelLayoutMath.ClampToReachKeepHeight(p, head, Vector3.forward);
            Assert.AreEqual(0.80f, r.y, 1e-6f);
            Assert.AreEqual(PanelLayoutMath.MaxReach, (r - head).magnitude, 1e-3f);
            Vector3 near = PanelLayoutMath.ClampToReachKeepHeight(new Vector3(0f, 0.9f, 0.4f), head, Vector3.forward);
            Assert.That((near - new Vector3(0f, 0.9f, 0.4f)).magnitude, Is.LessThan(1e-5f), "in reach: untouched");
        }

        [Test]
        public void ReachClampHandlesAPointDirectlyAboveTheHead()
        {
            Vector3 head = new Vector3(0f, 1.2f, 0f);
            Vector3 r = PanelLayoutMath.ClampToReachKeepHeight(new Vector3(0f, 0.9f, 0f), head, Vector3.forward);
            Assert.IsFalse(float.IsNaN(r.x) || float.IsNaN(r.z));
            Assert.That(Vector3.Dot(r - head, Vector3.forward), Is.GreaterThan(0f));
        }

        [Test]
        public void SavedPoseWithHeightRoundTripsAndResetIsTheDefault()
        {
            Vector3 saved = new Vector3(0.05f, 0.27f, 0.10f);
            Vector3 p; Quaternion q;
            Assert.IsTrue(PanelLayoutMath.TryDecodePose(PanelLayoutMath.EncodePose(saved, Quaternion.Euler(0f, 20f, 0f)), out p, out q));
            Assert.AreEqual(0.27f, p.y, 1e-6f);
            // Reset forgets the saved text: nothing decodes, so the default pose (including its height) stays.
            Assert.IsFalse(PanelLayoutMath.TryDecodePose("", out p, out q));
        }
    }
}
#endif
