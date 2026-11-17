#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using MuseDesk.Motion;

namespace MuseDesk.Tests.EditMode.Motion
{
    /// <summary>Polish S9a: the pure parts of the pop-in fixes (hide / reveal state and the resync diff).</summary>
    public class PresenceTests
    {
        static readonly MotionToken Tok = new MotionToken(0.2f, EaseKind.InOutCubic);

        [TearDown] public void Reset() { MotionSettings.SetReducedMotion(false); }

        [Test]
        public void Hide_RunsToHiddenFactor_NeverZero_AndReportsCompletionOnce()
        {
            var f = new PresenceFader(true);
            f.Hide(Tok);
            Assert.AreEqual(PresenceFader.Phase.Hiding, f.State);
            Assert.IsTrue(f.IsLeaving);
            int completions = 0;
            for (int i = 0; i < 40; i++)
            {
                if (f.Step(0.016f)) completions++;
                Assert.GreaterOrEqual(f.Factor, PresenceFader.HiddenFactor);
            }
            Assert.AreEqual(1, completions);
            Assert.AreEqual(PresenceFader.Phase.Hidden, f.State);
            Assert.AreEqual(PresenceFader.HiddenFactor, f.Factor, 0f);
            Assert.IsFalse(f.Step(0.016f), "no second completion");
        }

        [Test]
        public void ShowDuringHide_GrowsBackFromCurrentFactor_AndNeverReportsHideCompletion()
        {
            var f = new PresenceFader(true);
            f.Hide(Tok);
            for (int i = 0; i < 5; i++) f.Step(0.016f);
            float mid = f.Factor;
            Assert.Less(mid, 1f); Assert.Greater(mid, PresenceFader.HiddenFactor);

            f.Show(Tok);                                    // the task came back mid-hide
            Assert.AreEqual(PresenceFader.Phase.Showing, f.State);
            Assert.IsFalse(f.IsLeaving);
            Assert.AreEqual(mid, f.Factor, 1e-6f, "no jump at the reversal");
            bool hideDone = false;
            for (int i = 0; i < 40; i++) hideDone |= f.Step(0.016f);
            Assert.IsFalse(hideDone, "a cancelled hide must never fire its completion");
            Assert.AreEqual(PresenceFader.Phase.Shown, f.State);
            Assert.AreEqual(1f, f.Factor, 0f, "final state exact, never half-scaled");
            Assert.IsTrue(f.Interactable);
        }

        [Test]
        public void Interactable_OnlyWhenFullyShown()
        {
            var f = new PresenceFader(false);
            Assert.IsFalse(f.Interactable);
            f.Show(Tok);
            Assert.IsFalse(f.Interactable, "not while growing");
            while (f.Moving) f.Step(0.016f);
            Assert.IsTrue(f.Interactable);
            f.Hide(Tok);
            Assert.IsFalse(f.Interactable, "off the moment it starts to hide");
        }

        [Test]
        public void ReducedMotion_ShortensButStillEndsExact()
        {
            var normal = new PresenceFader(true); normal.Hide(Tok);
            int n = 0; while (normal.Moving) { normal.Step(0.01f); n++; }
            MotionSettings.SetReducedMotion(true);
            var reduced = new PresenceFader(true); reduced.Hide(Tok);
            int r = 0; while (reduced.Moving) { reduced.Step(0.01f); r++; }
            Assert.Less(r, n);
            Assert.AreEqual(PresenceFader.HiddenFactor, reduced.Factor, 0f);
        }

        [Test]
        public void NoAnimate_LandsAtOnce_AndHugeStepIsBounded()
        {
            var f = new PresenceFader(true);
            f.Hide(Tok, animate: false);
            Assert.AreEqual(PresenceFader.Phase.Hidden, f.State);
            f.Show(Tok);
            f.Step(10f);                                   // a hitch: SafeStep keeps it from skipping the whole show
            Assert.AreEqual(PresenceFader.Phase.Showing, f.State);
            for (int i = 0; i < 20; i++) f.Step(0.05f);
            Assert.AreEqual(PresenceFader.Phase.Shown, f.State);
        }

        [Test]
        public void Diff_OnlyNewIdsAreAdded_OnlyMissingAreRemoved_KeptAreUntouched()
        {
            var added = new List<string>(); var removed = new List<string>(); var kept = new List<string>();
            PresenceDiff.Compute(new[] { "a", "b", "c" }, new[] { "b", "c", "d", "e" }, added, removed, kept);
            CollectionAssert.AreEqual(new[] { "d", "e" }, added);
            CollectionAssert.AreEqual(new[] { "a" }, removed);
            CollectionAssert.AreEqual(new[] { "b", "c" }, kept);
        }

        [Test]
        public void Diff_IdenticalSnapshot_ChangesNothing_AndIgnoresEmptyAndDuplicateIds()
        {
            var added = new List<string> { "x" }; var removed = new List<string> { "y" }; var kept = new List<string>();
            PresenceDiff.Compute(new[] { "a", "b", "b", "", null }, new[] { "a", "", "b", "b" }, added, removed, kept);
            Assert.AreEqual(0, added.Count);
            Assert.AreEqual(0, removed.Count);
            CollectionAssert.AreEqual(new[] { "a", "b" }, kept);
        }

        [Test]
        public void Diff_FromEmptyAndToEmpty()
        {
            var added = new List<string>(); var removed = new List<string>(); var kept = new List<string>();
            PresenceDiff.Compute(null, new[] { "a", "a", "b" }, added, removed, kept);
            CollectionAssert.AreEqual(new[] { "a", "b" }, added);
            PresenceDiff.Compute(new[] { "a", "b" }, null, added, removed, kept);
            CollectionAssert.AreEqual(new[] { "a", "b" }, removed);
            Assert.AreEqual(0, added.Count);
        }
    }
}
#endif
