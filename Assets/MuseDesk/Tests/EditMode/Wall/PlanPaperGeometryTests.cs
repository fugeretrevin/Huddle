// =============================================================================
// MuseDesk · EditMode tests · Wall feedback v1: the plan paper's stamp zone and key geometry, key-label fitting, and the rule
// that recognises the character prefabs baked into the habitat art. Not proven here: how it looks on the Quest, the real TMP glyph widths.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using NUnit.Framework;
using MuseDesk.Spatial.Ui;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class PlanPaperGeometryTests
    {
        const float W = 0.22f, H = 0.30f, KeyScale = 0.023f;

        [Test]
        public void StampZone_IsInsideThePaper_AndCentred()
        {
            PaperRect z = PlanPaperGeometry.StampZone(W, H);
            Assert.That(z.Left, Is.GreaterThanOrEqualTo(-W * 0.5f + PlanPaperGeometry.SideMargin - 1e-5f));
            Assert.That(z.Right, Is.LessThanOrEqualTo(W * 0.5f - PlanPaperGeometry.SideMargin + 1e-5f));
            Assert.That(z.CenterX, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(z.Top, Is.LessThan(H * 0.5f));
            Assert.That(z.Bottom, Is.GreaterThan(-H * 0.5f));
        }

        [Test]
        public void StampZone_SitsBetweenTheTextAndTheKeys()
        {
            PaperRect z = PlanPaperGeometry.StampZone(W, H);
            float textBottom = H * 0.5f - PlanPaperGeometry.TopMargin - PlanPaperGeometry.PageHeightMm(H, true) / 1000f;
            Assert.That(textBottom, Is.GreaterThanOrEqualTo(z.Top - 1e-5f), "the text rows stop above the framed zone");
            float keysTop = PlanPaperGeometry.KeyRowCenterY(H, 0) + PlanPaperGeometry.KeyHalfHeight;
            Assert.That(z.Bottom, Is.GreaterThan(keysTop), "the zone does not run into the upper key row");
        }

        [Test]
        public void PageMark_SitsInTheGap_NotOnTheZoneOrKeys()
        {
            float y = PlanPaperGeometry.PageMarkY(H);
            PaperRect z = PlanPaperGeometry.StampZone(W, H);
            float keysTop = PlanPaperGeometry.KeyRowCenterY(H, 0) + PlanPaperGeometry.KeyHalfHeight;
            Assert.That(y, Is.LessThan(z.Bottom));
            Assert.That(y, Is.GreaterThan(keysTop));
        }

        [Test]
        public void ReservingTheZone_ShrinksThePage_ByZoneAndGap()
        {
            float free = PlanPaperGeometry.PageHeightMm(H, false), withZone = PlanPaperGeometry.PageHeightMm(H, true);
            Assert.That(free - withZone, Is.EqualTo((PlanPaperGeometry.StampZoneHeight + PlanPaperGeometry.StampZoneGap) * 1000f).Within(1e-3f));
            Assert.That(withZone, Is.GreaterThan(100f), "a plan page still holds a useful amount of text");
        }

        [Test]
        public void Keys_AreWiderThanTheTemplate_ButNeverOverlapTheirNeighbours()
        {
            float s = PlanPaperGeometry.KeyWidthScale(W, KeyScale);
            Assert.That(s, Is.GreaterThan(1.5f), "wide keys for the paper");
            Assert.That(s, Is.LessThanOrEqualTo(PlanPaperGeometry.MaxKeyWidthScale));
            float pitch = PlanPaperGeometry.KeyColumnX(W, 1) - PlanPaperGeometry.KeyColumnX(W, 0);
            Assert.That(PlanPaperGeometry.KeyWidth(KeyScale, s), Is.LessThan(pitch), "a gap remains between neighbouring keys");
            Assert.That(PlanPaperGeometry.KeyColumnX(W, 3) + PlanPaperGeometry.KeyWidth(KeyScale, s) * 0.5f, Is.LessThan(W * 0.5f), "the last key stays on the plate");
        }

        [Test]
        public void KeyWidthScale_IsNeverBelowOne_AndHandlesZero()
        {
            Assert.AreEqual(1f, PlanPaperGeometry.KeyWidthScale(0.05f, 0.023f), 1e-6f);
            Assert.AreEqual(1f, PlanPaperGeometry.KeyWidthScale(0.22f, 0f), 1e-6f);
            Assert.AreEqual(PlanPaperGeometry.MaxKeyWidthScale, PlanPaperGeometry.KeyWidthScale(1f, 0.023f), 1e-6f);
        }

        [Test]
        public void KeyRows_MatchTheOriginalCardLayout()
        {
            Assert.AreEqual(-H * 0.5f + 0.022f + 0.03f, PlanPaperGeometry.KeyRowCenterY(H, 0), 1e-6f);
            Assert.AreEqual(-H * 0.5f + 0.022f, PlanPaperGeometry.KeyRowCenterY(H, 1), 1e-6f);
        }

        // ── key labels ──

        static readonly string[] ManagerLabels =
        {
            "Keep 1", "Skip 3", "More", "Revise", "Confirm risk", "Close", "Status", "New plan", "Plan", "Receipt", "Retry 3", "Retrying 3",
            "Login flow", "Fix tests", "Write docs",
        };

        [Test]
        public void ManagerKeyLabels_FitTheirWideKey_WithoutAnEllipsis()
        {
            float widthScale = PlanPaperGeometry.KeyWidthScale(W, KeyScale);
            float keyMm = PlanPaperGeometry.KeyWidth(KeyScale, widthScale) * 1000f;
            const float fontMm = 5.5f;
            foreach (string l in ManagerLabels)
            {
                float tw = KeyLabelFit.EstimateWidthMm(l, fontMm);
                float k = KeyLabelFit.FontScale(tw, keyMm);
                Assert.That(KeyLabelFit.Fits(tw, keyMm, k), Is.True, l + " must fit a " + keyMm.ToString("F0") + " mm key");
                Assert.That(k, Is.GreaterThanOrEqualTo(KeyLabelFit.DefaultMinScale));
            }
        }

        [Test]
        public void ApproveAndReject_AreNotKeyLabels() // the stamps do that; the paper keeps More and Revise
        {
            foreach (string l in ManagerLabels) { Assert.AreNotEqual("Approve", l); Assert.AreNotEqual("Reject", l); }
        }

        [Test]
        public void FontScale_ShrinksOnlyWhenNeeded_AndNeverBelowTheMinimum()
        {
            Assert.AreEqual(1f, KeyLabelFit.FontScale(20f, 40f), 1e-6f);
            Assert.AreEqual(1f, KeyLabelFit.FontScale(0f, 40f), 1e-6f);
            float k = KeyLabelFit.FontScale(50f, 40f);
            Assert.That(k, Is.LessThan(1f).And.GreaterThanOrEqualTo(KeyLabelFit.DefaultMinScale));
            Assert.AreEqual(KeyLabelFit.DefaultMinScale, KeyLabelFit.FontScale(500f, 40f), 1e-6f);
        }

        // ── baked characters in the habitat art ──

        [TestCase("Agent_Manager", true)]
        [TestCase("Agent_Researcher", true)]
        [TestCase("Bay_Developer_Agent", true)]
        [TestCase("Bay_Tester_Agent", true)]
        [TestCase("Bay_Developer", false)]
        [TestCase("Bay_Developer_Workstation", false)]
        [TestCase("Manager_TerminalProp", false)]
        [TestCase("AgentHomePose", false)]
        [TestCase("Manager.AgentHomePose", false)]
        [TestCase("WallHabitat_ManagerLookout", false)]
        [TestCase("", false)]
        public void BakedCharacterNames_AreRecognised(string name, bool expected)
        {
            Assert.AreEqual(expected, HabitatArtRules.IsBakedCharacterName(name));
        }

        [Test]
        public void RoleWord_IsFoundInCharacterNames()
        {
            Assert.AreEqual("Manager", HabitatArtRules.RoleWordOf("Agent_Manager"));
            Assert.AreEqual("Developer", HabitatArtRules.RoleWordOf("Bay_Developer_Agent"));
            Assert.IsNull(HabitatArtRules.RoleWordOf("ElevatorCar"));
        }
    }
}
#endif
