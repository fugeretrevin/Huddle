// MuseDesk · EditMode tests · Sprint A6 presentation system (TypeScale, TextFit, PlanReviewLayout)
#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using MuseDesk.Spatial.Ui;

namespace MuseDesk.Tests.EditMode.Ui
{
    [Category("MuseDesk")]
    public class TypeScaleTests
    {
        [Test] public void LadderIsDescending()
        {
            for (int i = 1; i < TypeScale.Ladder.Length; i++) Assert.Less(TypeScale.Ladder[i], TypeScale.Ladder[i - 1]);
        }
        [Test] public void ProvenanceFloorIsAtLeast4Point2() { Assert.GreaterOrEqual(TypeScale.ProvenanceFloorMm, 4.2f); }
        [Test] public void LadderEndsAtProvenanceFloor() { Assert.AreEqual(TypeScale.ProvenanceFloorMm, TypeScale.Ladder[TypeScale.Ladder.Length - 1], 1e-5f); }
        [Test] public void MinAtCloseRangeIsTheProvenanceFloor() { Assert.AreEqual(4.2f, TypeScale.MinMmAt(0.3f), 1e-4f); }
        [Test] public void MinAtSixtyCentimetresMatchesFormula() { Assert.AreEqual(5.754f, TypeScale.MinMmAt(0.6f), 0.01f); }
        [Test] public void MinGrowsWithDistance() { Assert.Greater(TypeScale.MinMmAt(1.2f), TypeScale.MinMmAt(0.8f)); }
        [Test] public void MinIsLinearInDistanceAboveFloor() { Assert.AreEqual(2f * TypeScale.MinMmAt(0.8f), TypeScale.MinMmAt(1.6f), 0.02f); }
        [Test] public void ComfortIsAtLeastMin() { Assert.GreaterOrEqual(TypeScale.ComfortMmAt(0.7f), TypeScale.MinMmAt(0.7f)); }
        [Test] public void BadDistanceFallsBackToFloor() { Assert.AreEqual(4.2f, TypeScale.MinMmAt(0f), 1e-4f); Assert.AreEqual(4.2f, TypeScale.MinMmAt(-1f), 1e-4f); }
        [Test] public void MaxDistanceForProvenanceIsAboutFortyFourCentimetres() { Assert.AreEqual(0.438f, TypeScale.MaxDistanceFor(4.2f), 0.005f); }
        [Test] public void MaxDistanceRoundTripsWithMmForAngle()
        {
            float d = TypeScale.MaxDistanceFor(6f);
            Assert.AreEqual(6f, TypeScale.MmForAngle(d, TypeScale.MinXHeightDegrees), 0.01f);
        }
        [Test] public void IsLegibleAgreesWithMin()
        {
            Assert.IsTrue(TypeScale.IsLegible(4.2f, 0.4f));
            Assert.IsFalse(TypeScale.IsLegible(4.2f, 0.9f));
        }
    }

    [Category("MuseDesk")]
    public class TextFitTests
    {
        [Test] public void ShortTextKeepsPreferredSize()
        {
            var r = TextFit.Fit(100f, 30f, 12, FontWeightKind.Medium, TypeScale.TitleMm, 0.5f);
            Assert.AreEqual(TypeScale.TitleMm, r.SizeMm, 1e-4f);
            Assert.IsTrue(r.FitsFully); Assert.IsFalse(r.ShowMore);
        }
        [Test] public void LongTextStepsDownTheLadder()
        {
            var r = TextFit.Fit(100f, 20f, 90, FontWeightKind.Medium, TypeScale.TitleMm, 0.5f);
            Assert.Less(r.SizeMm, TypeScale.TitleMm);
            Assert.IsTrue(r.FitsFully);
        }
        [Test] public void SizeIsAlwaysOnTheLadderWhenWithinRange()
        {
            var r = TextFit.Fit(100f, 20f, 90, FontWeightKind.Medium, TypeScale.TitleMm, 0.5f);
            Assert.Contains(r.SizeMm, TypeScale.Ladder);
        }
        [Test] public void NeverShrinksBelowSurfaceMinimum()
        {
            float floor = TypeScale.MinMmAt(0.8f);
            var r = TextFit.Fit(60f, 12f, 400, FontWeightKind.Medium, TypeScale.BodyMm, 0.8f);
            Assert.GreaterOrEqual(r.SizeMm + 1e-4f, floor);
        }
        [Test] public void OverflowSetsMoreAndEllipsisInsteadOfShrinking()
        {
            var r = TextFit.Fit(60f, 12f, 600, FontWeightKind.Medium, TypeScale.BodyMm, 0.5f);
            Assert.IsTrue(r.Truncated); Assert.IsTrue(r.ShowMore); Assert.IsTrue(r.UseEllipsis);
            Assert.GreaterOrEqual(r.SizeMm, TypeScale.ProvenanceFloorMm);
            Assert.Less(r.VisibleChars, 600);
        }
        [Test] public void ApplyAddsEllipsisAndShortens()
        {
            string t = new string('a', 600);
            var r = TextFit.Fit(60f, 12f, t.Length, FontWeightKind.Medium, TypeScale.BodyMm, 0.5f);
            string s = TextFit.Apply(t, r);
            Assert.IsTrue(s.EndsWith("…")); Assert.Less(s.Length, t.Length);
        }
        [Test] public void ApplyLeavesFittingTextAlone()
        {
            var r = TextFit.Fit(100f, 30f, 5, FontWeightKind.Medium, TypeScale.BodyMm, 0.5f);
            Assert.AreEqual("hello", TextFit.Apply("hello", r));
        }
        [Test] public void ApplyHandlesNull() { Assert.AreEqual(string.Empty, TextFit.Apply(null, new TextFitResult())); }
        [Test] public void BoldIsWiderThanRegular() { Assert.Greater(TextFit.AvgCharWidthEm(FontWeightKind.Bold), TextFit.AvgCharWidthEm(FontWeightKind.Regular)); }
        [Test] public void BoldHoldsFewerCharsPerLine()
        {
            Assert.Less(TextFit.CharsPerLine(100f, 5f, FontWeightKind.Bold), TextFit.CharsPerLine(100f, 5f, FontWeightKind.Regular));
        }
        [Test] public void MaxLinesCapIsHonoured()
        {
            var r = TextFit.Fit(100f, 60f, 400, FontWeightKind.Medium, TypeScale.BodyMm, 0.5f, 2);
            Assert.LessOrEqual(r.MaxLines, 2);
            Assert.IsTrue(r.ShowMore);
        }
        [Test] public void EmptyTextFitsTrivially()
        {
            var r = TextFit.Fit(50f, 10f, 0, FontWeightKind.Medium, TypeScale.BodyMm, 0.5f);
            Assert.IsTrue(r.FitsFully); Assert.IsFalse(r.ShowMore);
        }
        [Test] public void ZeroHeightBoxShowsMoreWithNoChars()
        {
            var r = TextFit.Fit(50f, 0f, 20, FontWeightKind.Medium, TypeScale.BodyMm, 0.5f);
            Assert.IsTrue(r.ShowMore); Assert.AreEqual(0, r.VisibleChars);
        }
        [Test] public void FarSurfaceClampsToMinimumAndStillFitsWhenRoomy()
        {
            float floor = TypeScale.MinMmAt(1.5f);
            var r = TextFit.Fit(300f, 60f, 20, FontWeightKind.Medium, TypeScale.BodyMm, 1.5f);
            Assert.IsTrue(r.ClampedToMinimum); Assert.IsTrue(r.FitsFully);
            Assert.AreEqual(floor, r.SizeMm, 1e-3f);
        }
        [Test] public void LinesNeededRoundsUp() { Assert.AreEqual(3, TextFit.LinesNeeded(21, 10)); Assert.AreEqual(0, TextFit.LinesNeeded(0, 10)); }
    }

    [Category("MuseDesk")]
    public class PlanReviewLayoutTests
    {
        static PlanModel Sample(PlanState state = PlanState.Proposed, string source = "LIVE_MODEL")
        {
            return new PlanModel
            {
                Title = "Add dark mode",
                UserRequest = "Please add dark mode to settings",
                Objective = "Ship a dark theme toggle",
                State = state, Source = source,
                Risks = new List<string> { "Contrast regressions" },
                Items = new List<PlanItemModel>
                {
                    new PlanItemModel { Id = "a", Title = "Research theming", ProposedAgent = "researcher" },
                    new PlanItemModel { Id = "b", Title = "Implement toggle", ProposedAgent = "developer", DependsOn = new List<string> { "a" },
                        ExpectedOutputs = new List<string> { "Toggle persists across restarts" } },
                    new PlanItemModel { Id = "c", Title = "Test", ProposedAgent = "tester", DependsOn = new List<string> { "b" } },
                }
            };
        }

        static PlanRow First(PlanLayoutResult r, PlanRowKind k)
        {
            foreach (var row in r.Rows) if (row.Kind == k) return row;
            Assert.Fail("no row of kind " + k); return default;
        }
        static int Count(PlanLayoutResult r, PlanRowKind k) { int n = 0; foreach (var row in r.Rows) if (row.Kind == k) n++; return n; }

        [Test] public void ProposedStateIsNeverTeal()
        {
            var r = PlanReviewLayout.Layout(Sample(PlanState.Proposed), 140f, 0f, 0.5f);
            foreach (var row in r.Rows) Assert.AreNotEqual(PlanTone.Teal, row.Tone);
            Assert.AreEqual(PlanTone.Amber, First(r, PlanRowKind.State).Tone);
        }
        [Test] public void CommittedIsNotTealEither()
        {
            Assert.AreNotEqual(PlanTone.Teal, PlanReviewLayout.StateTone(PlanState.Committed));
        }
        [Test] public void StateLabelsDiffer()
        {
            Assert.AreNotEqual(PlanReviewLayout.StateLabel(PlanState.Proposed), PlanReviewLayout.StateLabel(PlanState.Committed));
            StringAssert.Contains("PROPOSED", PlanReviewLayout.StateLabel(PlanState.Proposed));
        }
        [Test] public void ScriptedDemoSourceShowsDemoTag()
        {
            Assert.AreEqual("[SCRIPTED DEMO]", PlanReviewLayout.ProvenanceLabel("SCRIPTED_DEMO"));
        }
        [Test] public void LiveModelSourceShowsModelWritten()
        {
            Assert.AreEqual("Model-written", PlanReviewLayout.ProvenanceLabel("LIVE_MODEL"));
        }
        [Test] public void MissingSourceNeverClaimsLive()
        {
            string s = PlanReviewLayout.ProvenanceLabel(null);
            StringAssert.DoesNotContain("Model", s); StringAssert.DoesNotContain("live", s.ToLowerInvariant());
            Assert.AreEqual(s, PlanReviewLayout.ProvenanceLabel("  "));
        }
        [Test] public void TemplateSourceIsNotCalledModelWritten()
        {
            StringAssert.DoesNotContain("Model-written", PlanReviewLayout.ProvenanceLabel("TEMPLATE_PLAN"));
        }
        [Test] public void UnknownSourceIsEchoedNotUpgraded()
        {
            Assert.AreEqual("Source: mystery", PlanReviewLayout.ProvenanceLabel("mystery"));
        }
        [Test] public void ProvenanceIsLastRowAndAtLeastFloor()
        {
            var r = PlanReviewLayout.Layout(Sample(source: "SCRIPTED_DEMO"), 140f, 0f, 0.5f);
            var last = r.Rows[r.Rows.Count - 1];
            Assert.AreEqual(PlanRowKind.Provenance, last.Kind);
            Assert.AreEqual("[SCRIPTED DEMO]", last.Text);
            Assert.GreaterOrEqual(last.SizeMm, 4.2f);
        }
        [Test] public void ProvenanceSurvivesTightHeight()
        {
            var r = PlanReviewLayout.Layout(Sample(), 140f, 40f, 0.5f);
            Assert.AreEqual(PlanRowKind.Provenance, r.Rows[r.Rows.Count - 1].Kind);
        }
        [Test] public void EveryItemGetsATitleRowWhenUnlimited()
        {
            var r = PlanReviewLayout.Layout(Sample(), 140f, 0f, 0.5f);
            Assert.AreEqual(3, Count(r, PlanRowKind.ItemTitle));
            Assert.IsFalse(r.Overflowed);
        }
        [Test] public void ItemsKeepBackendOrder()
        {
            var r = PlanReviewLayout.Layout(Sample(), 140f, 0f, 0.5f);
            var titles = new List<string>();
            foreach (var row in r.Rows) if (row.Kind == PlanRowKind.ItemTitle) titles.Add(row.Text);
            StringAssert.StartsWith("1.", titles[0]); StringAssert.Contains("Research", titles[0]);
            StringAssert.StartsWith("3.", titles[2]);
        }
        [Test] public void DependencyIsShownAsOrdinal()
        {
            var plan = Sample();
            Assert.AreEqual("Developer - after 1", PlanReviewLayout.ItemDetail(plan, plan.Items[1]));
        }
        [Test] public void UnresolvedDependencyIsShownRaw()
        {
            var plan = Sample();
            plan.Items[2].DependsOn = new List<string> { "zz" };
            Assert.AreEqual("Tester - after zz", PlanReviewLayout.ItemDetail(plan, plan.Items[2]));
        }
        [Test] public void ItemWithNoAgentOrDepsHasNoDetailRow()
        {
            var plan = new PlanModel { Items = new List<PlanItemModel> { new PlanItemModel { Id = "a", Title = "Only" } } };
            Assert.IsNull(PlanReviewLayout.ItemDetail(plan, plan.Items[0]));
            var r = PlanReviewLayout.Layout(plan, 140f, 0f, 0.5f);
            Assert.AreEqual(0, Count(r, PlanRowKind.ItemDetail));
        }
        [Test] public void ExpectedOutputsAppearOnlyWhenPresent()
        {
            var r = PlanReviewLayout.Layout(Sample(), 140f, 0f, 0.5f);
            Assert.AreEqual(1, Count(r, PlanRowKind.ItemOutput));
            StringAssert.Contains("persists", First(r, PlanRowKind.ItemOutput).Text);
        }
        [Test] public void MissingFieldsAreOmittedNotInvented()
        {
            var plan = new PlanModel { State = PlanState.Proposed, Source = "LIVE_MODEL" };
            var r = PlanReviewLayout.Layout(plan, 140f, 0f, 0.5f);
            Assert.AreEqual(0, Count(r, PlanRowKind.Title));
            Assert.AreEqual(0, Count(r, PlanRowKind.Text));
            Assert.AreEqual(0, Count(r, PlanRowKind.ItemTitle));
            Assert.AreEqual(0, Count(r, PlanRowKind.Risk));
            Assert.AreEqual(0, Count(r, PlanRowKind.Section));
        }
        [Test] public void NullPlanDoesNotThrowAndStaysHonest()
        {
            var r = PlanReviewLayout.Layout(null, 140f, 0f, 0.5f);
            Assert.AreEqual(PlanRowKind.Provenance, r.Rows[r.Rows.Count - 1].Kind);
            StringAssert.DoesNotContain("Model-written", r.ProvenanceText);
        }
        [Test] public void ObjectiveSameAsRequestIsNotRepeated()
        {
            var plan = Sample(); plan.Objective = plan.UserRequest.ToUpperInvariant();
            var r = PlanReviewLayout.Layout(plan, 140f, 0f, 0.5f);
            Assert.AreEqual(1, Count(r, PlanRowKind.Text));
        }
        [Test] public void RisksUseCoralSectionHeader()
        {
            var r = PlanReviewLayout.Layout(Sample(), 140f, 0f, 0.5f);
            bool found = false;
            foreach (var row in r.Rows) if (row.Kind == PlanRowKind.Section && row.Text == "Risks") { found = true; Assert.AreEqual(PlanTone.Coral, row.Tone); }
            Assert.IsTrue(found);
        }
        [Test] public void TightHeightDropsItemsAndAddsMoreRow()
        {
            var plan = Sample();
            for (int i = 0; i < 10; i++) plan.Items.Add(new PlanItemModel { Id = "x" + i, Title = "Extra step " + i });
            var r = PlanReviewLayout.Layout(plan, 140f, 90f, 0.5f);
            Assert.IsTrue(r.Overflowed);
            Assert.Greater(r.DroppedItems, 0);
            Assert.AreEqual(1, Count(r, PlanRowKind.More));
            Assert.IsTrue(First(r, PlanRowKind.More).ShowMore);
        }
        [Test] public void HeightBudgetIsRespectedWhenOverflowing()
        {
            var plan = Sample();
            for (int i = 0; i < 10; i++) plan.Items.Add(new PlanItemModel { Id = "x" + i, Title = "Extra step " + i });
            var r = PlanReviewLayout.Layout(plan, 140f, 90f, 0.5f);
            Assert.LessOrEqual(r.TotalHeightMm, 90f + PlanReviewLayout.BlockGapMm * 2f);
        }
        [Test] public void AllRowSizesAreLegibleAtTheSurfaceDistance()
        {
            float dist = 0.7f;
            var r = PlanReviewLayout.Layout(Sample(), 160f, 0f, dist);
            foreach (var row in r.Rows) Assert.IsTrue(TypeScale.IsLegible(row.SizeMm, dist), row.Kind + " " + row.SizeMm);
        }
        [Test] public void LongItemTitleIsTruncatedWithMoreFlag()
        {
            var plan = new PlanModel { Items = new List<PlanItemModel> { new PlanItemModel { Id = "a", Title = new string('w', 500) } } };
            var r = PlanReviewLayout.Layout(plan, 80f, 0f, 0.5f);
            var row = First(r, PlanRowKind.ItemTitle);
            Assert.IsTrue(row.ShowMore); Assert.Less(row.Text.Length, row.FullText.Length);
        }
        [Test] public void PaletteTealMatchesDesignToken()
        {
            var c = PlanPalette.Of(PlanTone.Teal);
            Assert.AreEqual(0x2F, c.r); Assert.AreEqual(0xA3, c.g); Assert.AreEqual(0x9A, c.b);
        }
        [Test] public void AgentLabelCapitalises()
        {
            Assert.AreEqual("Researcher", PlanReviewLayout.AgentLabel("researcher")); Assert.IsNull(PlanReviewLayout.AgentLabel(" "));
        }
    }
}
#endif
