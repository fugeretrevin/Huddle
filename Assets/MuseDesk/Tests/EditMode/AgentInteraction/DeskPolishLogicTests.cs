using NUnit.Framework;
using MuseDesk.Agents;

namespace MuseDesk.Tests.AgentInteraction
{
    public class DeskPolishLogicTests
    {
        // ── turn clips ──
        [TestCase(-1, TurnClip.Left)]
        [TestCase(1, TurnClip.Right)]
        [TestCase(0, TurnClip.None)]
        [TestCase(-7, TurnClip.Left)]
        [TestCase(3, TurnClip.Right)]
        public void TurnClip_FollowsTurnInPlaceDirection(int dir, TurnClip expected)
        {
            Assert.AreEqual(expected, DeskTurnRule.Select(dir));
        }

        // ── nod gate ──
        [Test]
        public void Nod_FirstNodIsAllowed()
        {
            Assert.IsTrue(NodGate.Allow(10f, -100f, NodGate.DefaultGapSeconds, false, false));
        }

        [Test]
        public void Nod_RateLimitedPerAgent()
        {
            Assert.IsFalse(NodGate.Allow(12f, 10f, 6f, false, false), "2 s after the last nod");
            Assert.IsTrue(NodGate.Allow(16.01f, 10f, 6f, false, false), "past the gap");
        }

        [Test]
        public void Nod_NeverUnderFocus()
        {
            Assert.IsFalse(NodGate.Allow(100f, -100f, 6f, true, false));
        }

        [Test]
        public void Nod_NeverWhileTheBodyIsBusy()
        {
            Assert.IsFalse(NodGate.Allow(100f, -100f, 6f, false, true), "walking, offering or in another reaction: no stacking");
        }

        // ── idle gestures under Focus ──
        [Test]
        public void IdleGestures_AmbientOffUnderFocusOnOtherwise()
        {
            Assert.IsFalse(IdleGesturePolicy.AmbientAllowed(true));
            Assert.IsTrue(IdleGesturePolicy.AmbientAllowed(false));
        }

        [Test]
        public void IdleGestures_StatusBackedRepeatsSlowerUnderFocusButStillShows()
        {
            Assert.AreEqual(1f, IdleGesturePolicy.StatusRepeatMultiplier(false, 2.5f));
            Assert.AreEqual(2.5f, IdleGesturePolicy.StatusRepeatMultiplier(true, 2.5f));
            Assert.AreEqual(1f, IdleGesturePolicy.StatusRepeatMultiplier(true, 0.2f), "never faster than normal");
        }

        // ── poke / wave while the hands are busy ──
        [TestCase(false, false, false, false)]
        [TestCase(true, false, false, true)]
        [TestCase(false, true, false, true)]
        [TestCase(false, false, true, true)]
        [TestCase(true, true, true, true)]
        public void HandReactions_QuietWhenHandsAreBusy(bool held, bool l, bool r, bool busy)
        {
            Assert.AreEqual(busy, HandReactionGate.Busy(held, l, r));
        }

        // ── held card: the hide waits for the release ──
        [Test]
        public void DeferredHide_WaitsWhileHeldThenFiresOnce()
        {
            var d = new DeferredHide();
            Assert.IsFalse(d.Pending);
            d.Request();
            Assert.IsTrue(d.Pending);
            Assert.IsFalse(d.Tick(true), "still held");
            Assert.IsFalse(d.Tick(true));
            Assert.IsTrue(d.Pending);
            Assert.IsTrue(d.Tick(false), "released");
            Assert.IsFalse(d.Pending);
            Assert.IsFalse(d.Tick(false), "only once");
        }

        [Test]
        public void DeferredHide_CancelledWhenTheTaskComesBack()
        {
            var d = new DeferredHide();
            d.Request();
            d.Cancel();
            Assert.IsFalse(d.Tick(false));
        }

        [Test]
        public void DeferredHide_NotPendingMeansNothingToDo()
        {
            var d = new DeferredHide();
            Assert.IsFalse(d.Tick(false));
            Assert.IsFalse(d.Tick(true));
        }

        // ── sound gating ──
        [Test]
        public void Cues_AmbientMutedUnderFocus()
        {
            Assert.IsFalse(DeskCuePolicy.Allow(DeskCueKind.CardLanded, 50f, -10f, true));
            Assert.IsFalse(DeskCuePolicy.Allow(DeskCueKind.PaperArrived, 50f, -10f, true));
            Assert.IsFalse(DeskCuePolicy.Allow(DeskCueKind.Delivered, 50f, -10f, true));
            Assert.IsTrue(DeskCuePolicy.Allow(DeskCueKind.CardLanded, 50f, -10f, false));
        }

        [Test]
        public void Cues_OutcomeCuesAreNotMutedByFocus()
        {
            Assert.IsTrue(DeskCuePolicy.Allow(DeskCueKind.Other, 50f, -10f, true));
        }

        [Test]
        public void Cues_RateLimitedPerKind()
        {
            Assert.IsFalse(DeskCuePolicy.Allow(DeskCueKind.CardLanded, 10.2f, 10f, false), "0.2 s after a landing");
            Assert.IsTrue(DeskCuePolicy.Allow(DeskCueKind.CardLanded, 10.4f, 10f, false));
            Assert.IsFalse(DeskCuePolicy.Allow(DeskCueKind.PaperArrived, 11f, 10f, false));
            Assert.IsTrue(DeskCuePolicy.Allow(DeskCueKind.PaperArrived, 11.6f, 10f, false));
        }
    }
}
