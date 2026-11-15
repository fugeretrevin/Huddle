using NUnit.Framework;
using MuseDesk.Spatial.Onboarding;

namespace MuseDesk.Tests.EditMode.Onboarding
{
    public class OnboardingAdapterTests
    {
        [Test]
        public void RiseEdge_FiresOnlyOnFalseToTrue()
        {
            var e = new RiseEdge();
            Assert.IsFalse(e.Update(false));
            Assert.IsTrue(e.Update(true));
            Assert.IsFalse(e.Update(true));
            Assert.IsFalse(e.Update(false));
            Assert.IsTrue(e.Update(true));
        }

        [Test]
        public void RiseEdge_TrueAtStartCountsAsARise()
        {
            Assert.IsTrue(new RiseEdge().Update(true));
        }

        [Test]
        public void ChangeEdge_FirstValueIsBaselineNotAChange()
        {
            var e = new ChangeEdge();
            Assert.IsFalse(e.Update(2));
            Assert.IsFalse(e.Update(2));
            Assert.IsTrue(e.Update(1));
            Assert.IsFalse(e.Update(1));
            Assert.AreEqual(1, e.Last);
        }

        [Test]
        public void ChangeEdge_ResetForgetsTheBaseline()
        {
            var e = new ChangeEdge();
            e.Update(0); e.Reset();
            Assert.IsFalse(e.Update(3));
        }

        [Test]
        public void Handoff_NeedsAnApprovalFirst()
        {
            var h = new HandoffTracker();
            h.DecisionRequested("researcher");
            Assert.IsFalse(h.AgentChanged("developer"));
            h.DecisionResolved("rejected");
            Assert.IsFalse(h.AgentChanged("developer"));
        }

        [Test]
        public void Handoff_FiresOnceForAnotherAgentAfterApproval()
        {
            var h = new HandoffTracker();
            h.DecisionRequested("researcher");
            h.DecisionResolved("approved");
            Assert.IsFalse(h.AgentChanged("researcher"), "the asking agent is not a handoff");
            Assert.IsTrue(h.AgentChanged("developer"));
            Assert.IsFalse(h.AgentChanged("tester"), "once");
        }

        [Test]
        public void Handoff_IgnoresEmptyIdAndNewRequestDisarms()
        {
            var h = new HandoffTracker();
            h.DecisionRequested("a");
            h.DecisionResolved("APPROVED");
            Assert.IsFalse(h.AgentChanged(null));
            Assert.IsFalse(h.AgentChanged(""));
            h.DecisionRequested("b");
            Assert.IsFalse(h.Armed);
            Assert.IsFalse(h.AgentChanged("c"));
        }

        [Test]
        public void FlagMap_SpatialNeedsFloorOrCeiling()
        {
            Assert.AreEqual(OnboardingFlags.None, OnboardingFlagMap.Map(false, false, false, false, false, false, false));
            Assert.AreEqual(OnboardingFlags.Spatial, OnboardingFlagMap.Map(true, false, false, false, false, false, false));
            Assert.AreEqual(OnboardingFlags.Spatial, OnboardingFlagMap.Map(false, true, false, false, false, false, false));
        }

        [Test]
        public void FlagMap_DependentWallFlagsNeedWall()
        {
            Assert.AreEqual(OnboardingFlags.None, OnboardingFlagMap.Map(false, false, false, false, true, true, true));
            var on = OnboardingFlagMap.Map(false, false, false, true, true, true, true);
            Assert.AreEqual(OnboardingFlags.Wall | OnboardingFlags.Manager | OnboardingFlags.Portal, on);
            Assert.AreEqual(OnboardingFlags.Wall, OnboardingFlagMap.Map(false, false, false, true, false, false, true), "portal needs agentSelect");
        }

        [Test]
        public void FlagMap_CalmToggle()
        {
            Assert.AreEqual(OnboardingFlags.CalmToggle, OnboardingFlagMap.Map(false, false, true, false, false, false, false));
        }

        [Test]
        public void ManualClock_FeedsTheCoach()
        {
            var clock = new ManualOnboardingClock();
            var coach = new OnboardingCoach(clock, new MemoryOnboardingStore());
            coach.Observe(OnboardingEvent.SessionReady);
            clock.Time = 5; coach.Tick();
            Assert.IsTrue(coach.IsShowing);
        }
    }
}
