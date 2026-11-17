// =============================================================================
// MuseDesk · EditMode tests · movable panels and the mode console (pure logic)
// =============================================================================
//
// Exercises the REAL rules in MuseDesk.Spatial.Core (PanelLayoutMath, ModeConsoleLogic): where a
// released panel may rest, how panels are nudged apart, the saved-pose codec, and the console's
// key table. No scene, no MonoBehaviour. Compiled under the same define as the other spatial
// tests (see SpatialPlacementTests.cs).
//
// NOT covered: anything with a Transform or the Interaction SDK (the grab itself, the rig
// following the office root, poke keys being switched off during a drag, PlayerPrefs).
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;

namespace MuseDesk.Tests.EditMode
{
    [Category("MuseDesk")]
    public class PanelReachTests
    {
        static readonly Vector3 Head = new Vector3(0f, 1.2f, 0f);
        static readonly Vector3 Fwd = Vector3.forward;

        [Test]
        public void PointWithinReachIsLeftAlone()
        {
            Vector3 p = new Vector3(0.1f, 0.9f, 0.4f);
            Vector3 r = PanelLayoutMath.ClampToReach(p, Head, Fwd);
            Assert.That((r - p).magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void FarPointIsPulledToMaxReachAlongTheSameDirection()
        {
            Vector3 p = new Vector3(0.5f, 0.9f, 2.0f);
            Vector3 r = PanelLayoutMath.ClampToReach(p, Head, Fwd);
            Assert.AreEqual(PanelLayoutMath.MaxReach, (r - Head).magnitude, 1e-4f);
            Assert.That(Vector3.Angle(r - Head, p - Head), Is.LessThan(0.01f));
        }

        [Test]
        public void TooClosePointIsPushedToMinReach()
        {
            Vector3 p = Head + new Vector3(0f, -0.05f, 0.08f);
            Vector3 r = PanelLayoutMath.ClampToReach(p, Head, Fwd);
            Assert.AreEqual(PanelLayoutMath.MinReach, (r - Head).magnitude, 1e-4f);
        }

        [Test]
        public void PointBehindTheHeadEndsInFrontAndWithinReach()
        {
            Vector3 p = new Vector3(0.2f, 1.0f, -0.6f);
            Vector3 r = PanelLayoutMath.ClampToReach(p, Head, Fwd);
            Assert.That(Vector3.Dot(r - Head, Fwd), Is.GreaterThan(0f));
            float d = (r - Head).magnitude;
            Assert.That(d, Is.InRange(PanelLayoutMath.MinReach - 1e-4f, PanelLayoutMath.MaxReach + 1e-4f));
        }

        [Test]
        public void PointAtTheHeadGetsAFinitePositionInFront()
        {
            Vector3 r = PanelLayoutMath.ClampToReach(Head, Head, Fwd);
            Assert.IsFalse(float.IsNaN(r.x) || float.IsNaN(r.y) || float.IsNaN(r.z));
            Assert.That(Vector3.Dot(r - Head, Fwd), Is.GreaterThan(0f));
            Assert.That((r - Head).magnitude, Is.InRange(PanelLayoutMath.MinReach - 1e-4f, PanelLayoutMath.MaxReach + 1e-4f));
        }

        [Test]
        public void HeadLookingStraightDownStillGivesAUsableForward()
        {
            Vector3 p = new Vector3(0f, 0.9f, 3f);
            Vector3 r = PanelLayoutMath.ClampToReach(p, Head, Vector3.down);
            Assert.AreEqual(PanelLayoutMath.MaxReach, (r - Head).magnitude, 1e-4f);
            Assert.That(r.z, Is.GreaterThan(0f));
        }

        [Test]
        public void BaseBelowTheDeskIsLiftedOnlyNearTheDesk()
        {
            Assert.AreEqual(0.72f, PanelLayoutMath.LiftAboveDesk(0.60f, 0.72f, 0.3f, 0.9f), 1e-6f);
            Assert.AreEqual(0.80f, PanelLayoutMath.LiftAboveDesk(0.80f, 0.72f, 0.3f, 0.9f), 1e-6f, "above the desk: untouched");
            Assert.AreEqual(0.60f, PanelLayoutMath.LiftAboveDesk(0.60f, 0.72f, 1.5f, 0.9f), 1e-6f, "beside the desk: may hang lower");
        }

        [Test]
        public void YawTowardPointsPlusZAtTheTarget()
        {
            Assert.AreEqual(0f, PanelLayoutMath.YawToward(Vector3.zero, new Vector3(0f, 5f, 1f), 99f), 1e-4f);
            Assert.AreEqual(90f, PanelLayoutMath.YawToward(Vector3.zero, new Vector3(1f, 0f, 0f), 99f), 1e-4f);
            Assert.AreEqual(99f, PanelLayoutMath.YawToward(Vector3.zero, new Vector3(0f, 2f, 0f), 99f), 1e-4f, "directly above: the fallback");
            Vector3 dir = Quaternion.AngleAxis(PanelLayoutMath.YawToward(Vector3.zero, new Vector3(-0.3f, 0f, 0.4f), 0f), Vector3.up) * Vector3.forward;
            Assert.That(Vector3.Angle(dir, new Vector3(-0.3f, 0f, 0.4f)), Is.LessThan(0.01f));
        }

        [Test]
        public void SmoothingCoversEverythingWithoutATimeConstantAndNothingInNoTime()
        {
            Assert.AreEqual(1f, PanelLayoutMath.Smooth(0.01f, 0f), 1e-6f);
            Assert.AreEqual(0f, PanelLayoutMath.Smooth(0f, 0.05f), 1e-6f);
            Assert.That(PanelLayoutMath.Smooth(0.011f, 0.05f), Is.InRange(0.15f, 0.25f));
        }
    }

    [Category("MuseDesk")]
    public class PanelOverlapTests
    {
        [Test]
        public void RotatedHalfExtentsSwapAtNinetyDegrees()
        {
            Vector2 h = PanelLayoutMath.RotatedHalfExtents(new Vector2(0.10f, 0.03f), 90f);
            Assert.AreEqual(0.03f, h.x, 1e-5f);
            Assert.AreEqual(0.10f, h.y, 1e-5f);
            Vector2 same = PanelLayoutMath.RotatedHalfExtents(new Vector2(0.10f, 0.03f), 180f);
            Assert.AreEqual(0.10f, same.x, 1e-5f);
        }

        [Test]
        public void ClearRectangleIsNotMoved()
        {
            var c = PanelLayoutMath.ResolveOverlap(new Vector2(0.5f, 0f), new Vector2(0.05f, 0.05f),
                new[] { Vector2.zero }, new[] { new Vector2(0.1f, 0.1f) }, 1, 0.01f, Vector2.right);
            Assert.AreEqual(0.5f, c.x, 1e-6f);
            Assert.AreEqual(0f, c.y, 1e-6f);
        }

        [Test]
        public void OverlapIsResolvedAlongTheShallowAxisAwayFromTheOther()
        {
            Vector2 half = new Vector2(0.05f, 0.05f), oh = new Vector2(0.10f, 0.10f);
            var c = PanelLayoutMath.ResolveOverlap(new Vector2(0.12f, 0.02f), half, new[] { Vector2.zero }, new[] { oh }, 1, 0.01f, Vector2.right);
            Assert.AreEqual(0.16f, c.x, 1e-5f, "pushed out in +x to half + half + gap");
            Assert.AreEqual(0.02f, c.y, 1e-6f);
            Assert.IsFalse(PanelLayoutMath.Overlaps(c, half, Vector2.zero, oh, 0.0099f));
        }

        [Test]
        public void CoincidentCentresUseThePreferredSide()
        {
            Vector2 half = new Vector2(0.05f, 0.02f), oh = new Vector2(0.05f, 0.02f);
            var c = PanelLayoutMath.ResolveOverlap(Vector2.zero, half, new[] { Vector2.zero }, new[] { oh }, 1, 0f, new Vector2(1f, -1f));
            Assert.AreEqual(0f, c.x, 1e-6f);
            Assert.AreEqual(-0.04f, c.y, 1e-5f, "the shallow axis is z; preferred z is negative");
        }

        [Test]
        public void TwoNeighboursAreBothCleared()
        {
            Vector2 half = new Vector2(0.05f, 0.05f);
            Vector2[] oc = { new Vector2(0f, 0f), new Vector2(0.17f, 0f) };
            Vector2[] oh = { new Vector2(0.05f, 0.05f), new Vector2(0.05f, 0.05f) };
            var c = PanelLayoutMath.ResolveOverlap(new Vector2(0.06f, 0.01f), half, oc, oh, 2, 0.005f, Vector2.up);
            for (int i = 0; i < 2; i++)
                Assert.IsFalse(PanelLayoutMath.Overlaps(c, half, oc[i], oh[i], 0.0049f), "still overlapping neighbour " + i);
        }

        [Test]
        public void NullOrEmptyNeighboursAreHarmless()
        {
            var c = PanelLayoutMath.ResolveOverlap(new Vector2(1f, 2f), Vector2.one, null, null, 3, 0.01f, Vector2.right);
            Assert.AreEqual(new Vector2(1f, 2f), c);
            c = PanelLayoutMath.ResolveOverlap(new Vector2(1f, 2f), Vector2.one, new Vector2[0], new Vector2[0], 5, 0.01f, Vector2.right);
            Assert.AreEqual(new Vector2(1f, 2f), c);
        }
    }

    [Category("MuseDesk")]
    public class PanelCodecTests
    {
        [Test]
        public void PoseRoundTrips()
        {
            Vector3 p = new Vector3(-0.123456f, 0.05f, 0.4f);
            Quaternion q = Quaternion.Euler(0f, 37.5f, 0f);
            Vector3 p2; Quaternion q2;
            Assert.IsTrue(PanelLayoutMath.TryDecodePose(PanelLayoutMath.EncodePose(p, q), out p2, out q2));
            Assert.That((p - p2).magnitude, Is.LessThan(1e-6f));
            Assert.That(Quaternion.Angle(q, q2), Is.LessThan(0.01f));
        }

        [Test]
        public void EncodingDoesNotDependOnTheCulture()
        {
            var before = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                string text = PanelLayoutMath.EncodePose(new Vector3(0.5f, 0.25f, -0.125f), Quaternion.identity);
                Assert.IsFalse(text.Contains(","), text);
                Vector3 p; Quaternion q;
                Assert.IsTrue(PanelLayoutMath.TryDecodePose(text, out p, out q));
                Assert.AreEqual(0.25f, p.y, 1e-6f);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = before; }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("v1|1|2|3")]
        [TestCase("v2|0|0|0|0|0|0|1")]
        [TestCase("v1|0|0|x|0|0|0|1")]
        [TestCase("v1|0|0|NaN|0|0|0|1")]
        [TestCase("v1|0|0|Infinity|0|0|0|1")]
        [TestCase("v1|0|9|0|0|0|0|1")]
        [TestCase("v1|0|0|0|0|0|0|0")]
        public void BadTextIsRefusedAndGivesIdentity(string text)
        {
            Vector3 p; Quaternion q;
            Assert.IsFalse(PanelLayoutMath.TryDecodePose(text, out p, out q));
            Assert.AreEqual(Vector3.zero, p);
            Assert.AreEqual(Quaternion.identity, q);
        }

        [Test]
        public void SlightlyDenormalisedRotationIsNormalised()
        {
            Vector3 p; Quaternion q;
            Assert.IsTrue(PanelLayoutMath.TryDecodePose("v1|0|0|0|0|0|0|1.2", out p, out q));
            Assert.AreEqual(1f, q.w, 1e-5f);
        }

        [Test]
        public void KeysAreDistinctPerPanel()
        {
            Assert.AreNotEqual(PanelLayoutMath.PrefsKey("dock"), PanelLayoutMath.PrefsKey("mode"));
            Assert.AreEqual("MuseDesk.Panel.dock", PanelLayoutMath.PrefsKey("dock"));
        }
    }

    [Category("MuseDesk")]
    public class ModeConsoleLogicTests
    {
        static ModeConsoleKeys K(bool floor, bool ceiling, PlacementMode mode, bool previewing = false, bool canCycle = false,
                                 bool transitioning = false, CeilingPodState pod = CeilingPodState.Docked, bool waiting = false)
        {
            return ModeConsoleLogic.Compute(floor, ceiling, mode, previewing, canCycle, transitioning, pod, waiting);
        }

        static void AssertNoContext(ModeConsoleKeys k)
        {
            Assert.IsFalse(k.next || k.confirm || k.cancel || k.stop || k.pod || k.bring || k.status);
        }

        [Test]
        public void FlagsOffShowsDeskOnlyWithTheOtherPlacesDisabled()
        {
            var k = K(false, false, PlacementMode.Desk);
            Assert.AreEqual(0, k.active);
            Assert.IsFalse(k.deskDisabled);
            Assert.IsTrue(k.floorDisabled);
            Assert.IsTrue(k.ceilingDisabled);
            AssertNoContext(k);
        }

        [Test]
        public void DeskAtRestOffersBothPlacesAndNoContextKeys()
        {
            var k = K(true, true, PlacementMode.Desk);
            Assert.IsFalse(k.floorDisabled || k.ceilingDisabled || k.deskDisabled);
            AssertNoContext(k);
        }

        [Test]
        public void OneFlagOffDisablesOnlyThatPlace()
        {
            var k = K(true, false, PlacementMode.Desk);
            Assert.IsFalse(k.floorDisabled);
            Assert.IsTrue(k.ceilingDisabled);
        }

        [Test]
        public void PreviewShowsConfirmAndCancelAndNextOnlyWhenThereIsAChoice()
        {
            var k = K(true, true, PlacementMode.Desk, previewing: true, canCycle: false);
            Assert.IsTrue(k.confirm && k.cancel);
            Assert.IsFalse(k.next || k.stop || k.pod || k.bring || k.status);
            Assert.IsTrue(K(true, true, PlacementMode.Desk, previewing: true, canCycle: true).next);
        }

        [Test]
        public void MovingShowsOnlyStopAndDisablesEveryPlace()
        {
            var k = K(true, true, PlacementMode.Floor, transitioning: true);
            Assert.IsTrue(k.stop);
            Assert.IsFalse(k.next || k.confirm || k.cancel || k.pod || k.bring || k.status);
            Assert.IsTrue(k.deskDisabled && k.floorDisabled && k.ceilingDisabled);
            Assert.AreEqual(1, k.active);
        }

        [Test]
        public void PreviewWhileSomethingMovesKeepsThePreviewKeysNotStop()
        {
            var k = K(true, true, PlacementMode.Floor, previewing: true, transitioning: true);
            Assert.IsTrue(k.confirm && k.cancel);
            Assert.IsFalse(k.stop);
        }

        [Test]
        public void FloorAtRestOffersBringAndStatus()
        {
            var k = K(true, true, PlacementMode.Floor);
            Assert.AreEqual(1, k.active);
            Assert.IsTrue(k.bring && k.status);
            Assert.IsFalse(k.pod || k.stop || k.confirm);
        }

        [Test]
        public void CeilingAtRestOffersLowerThenRaiseThenWaiting()
        {
            var up = K(true, true, PlacementMode.Ceiling, pod: CeilingPodState.Docked);
            Assert.AreEqual(2, up.active);
            Assert.IsTrue(up.pod && up.status);
            Assert.IsFalse(up.bring);
            Assert.AreEqual(ModeConsoleLogic.Lower, up.podWords);

            var down = K(true, true, PlacementMode.Ceiling, pod: CeilingPodState.Engaged);
            Assert.IsTrue(down.pod);
            Assert.AreEqual(ModeConsoleLogic.Raise, down.podWords);

            var waiting = K(true, true, PlacementMode.Ceiling, pod: CeilingPodState.Engaged, waiting: true);
            Assert.AreEqual(ModeConsoleLogic.Waiting, waiting.podWords);
        }

        [Test]
        public void CeilingPodBetweenStatesHasNoPodKey()
        {
            var k = K(true, true, PlacementMode.Ceiling, pod: CeilingPodState.PreparingToDescend);
            Assert.IsFalse(k.pod);
            Assert.IsTrue(k.status);
        }
    }
}
#endif
