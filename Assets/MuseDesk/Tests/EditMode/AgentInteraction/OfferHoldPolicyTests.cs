using NUnit.Framework;
using MuseDesk.Agents;

namespace MuseDesk.Tests.AgentInteraction
{
    /// <summary>The offered paper is raised once and held until it is taken: no give / take-back loop.</summary>
    public class OfferHoldPolicyTests
    {
        const float Present = OfferHoldPolicy.PresentClipHoldAt;

        [Test]
        public void FreezesOnlyOnceTheRaiseIsReached()
        {
            Assert.IsFalse(OfferHoldPolicy.ShouldFreeze(true, false, false, false, true, 0.30f, Present));
            Assert.IsTrue(OfferHoldPolicy.ShouldFreeze(true, false, false, false, true, 0.86f, Present));
            // A looped clip that is already past the hold time (e.g. first observed late) holds as well.
            Assert.IsTrue(OfferHoldPolicy.ShouldFreeze(true, false, false, false, true, 1.4f, Present));
        }

        [Test]
        public void NeverFreezesDuringTheCrossFadeOrInTheWrongStateOrWhenNotOffering()
        {
            Assert.IsFalse(OfferHoldPolicy.ShouldFreeze(true, false, false, true, true, 0.9f, Present), "cross-fade");
            Assert.IsFalse(OfferHoldPolicy.ShouldFreeze(true, false, false, false, false, 0.9f, Present), "other state");
            Assert.IsFalse(OfferHoldPolicy.ShouldFreeze(false, false, false, false, true, 0.9f, Present), "not offering");
            Assert.IsFalse(OfferHoldPolicy.ShouldFreeze(true, true, false, false, true, 0.9f, Present), "walking");
            Assert.IsFalse(OfferHoldPolicy.ShouldFreeze(true, false, true, false, true, 0.9f, Present), "one-shot over it");
        }

        [Test]
        public void ReleasesAsSoonAsTheOfferIsNoLongerWhatTheAgentDoes()
        {
            Assert.IsFalse(OfferHoldPolicy.ShouldRelease(true, true, false, false), "still offering: stays held");
            Assert.IsTrue(OfferHoldPolicy.ShouldRelease(true, false, false, false), "paper taken");
            Assert.IsTrue(OfferHoldPolicy.ShouldRelease(true, true, true, false), "walks");
            Assert.IsTrue(OfferHoldPolicy.ShouldRelease(true, true, false, true), "one-shot reaction");
            Assert.IsFalse(OfferHoldPolicy.ShouldRelease(false, false, true, true), "nothing frozen, nothing to release");
        }

        [Test]
        public void HoldTimeDependsOnWhetherTheControllerHasTheRaiseClip()
        {
            Assert.AreEqual(OfferHoldPolicy.PresentClipHoldAt, OfferHoldPolicy.HoldAt(true));
            Assert.AreEqual(OfferHoldPolicy.LoopClipHoldAt, OfferHoldPolicy.HoldAt(false));
            Assert.Less(OfferHoldPolicy.PresentClipHoldAt, 0.95f, "must stop before the clip's exit transition (0.95)");
        }

        [Test]
        public void ThePresentGestureIsNotRepeatedWhileThePaperIsHeldOut()
        {
            Assert.IsFalse(OfferHoldPolicy.AllowPresentGesture(true));
            Assert.IsTrue(OfferHoldPolicy.AllowPresentGesture(false));
        }

        [Test]
        public void ReassertsOnlyAKnockedOffOffer()
        {
            // expected, pending, not taken, standing at the spot, not offering, no one-shot: raise again.
            Assert.IsTrue(OfferHoldPolicy.NeedsReassert(true, true, false, false, false, false, false));
            Assert.IsFalse(OfferHoldPolicy.NeedsReassert(true, true, false, false, false, true, false), "already offering");
            Assert.IsFalse(OfferHoldPolicy.NeedsReassert(false, true, false, false, false, false, false), "no offer expected");
            Assert.IsFalse(OfferHoldPolicy.NeedsReassert(true, false, false, false, false, false, false), "decision gone");
            Assert.IsFalse(OfferHoldPolicy.NeedsReassert(true, true, true, false, false, false, false), "paper taken");
            Assert.IsFalse(OfferHoldPolicy.NeedsReassert(true, true, false, true, false, false, false), "walking");
            Assert.IsFalse(OfferHoldPolicy.NeedsReassert(true, true, false, false, true, false, false), "at home desk");
            Assert.IsFalse(OfferHoldPolicy.NeedsReassert(true, true, false, false, false, false, true), "one-shot playing");
        }
    }
}
