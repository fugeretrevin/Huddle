using NUnit.Framework;
using MuseDesk.Agents;

namespace MuseDesk.Tests.AgentInteraction
{
    public class ReactionAdapterTests
    {
        // ---- state -> Critical kind ----
        [Test]
        public void CriticalKindFor_NoneWhenNothingIsHappening()
        {
            ReactionKind k;
            Assert.IsFalse(ReactionAdapter.CriticalKindFor(false, false, false, false, out k));
        }

        [Test]
        public void CriticalKindFor_PriorityOrder()
        {
            ReactionKind k;
            Assert.IsTrue(ReactionAdapter.CriticalKindFor(true, true, true, true, out k));
            Assert.AreEqual(ReactionKind.PaperTransfer, k);
            Assert.IsTrue(ReactionAdapter.CriticalKindFor(false, true, true, true, out k));
            Assert.AreEqual(ReactionKind.ElevatorBoarding, k);
            Assert.IsTrue(ReactionAdapter.CriticalKindFor(false, false, true, true, out k));
            Assert.AreEqual(ReactionKind.Handoff, k);
            Assert.IsTrue(ReactionAdapter.CriticalKindFor(false, false, false, true, out k));
            Assert.AreEqual(ReactionKind.Walking, k);
        }

        // ---- CriticalSync ----
        [Test]
        public void CriticalSync_WalkingBlocksSocialUntilItEnds()
        {
            var arb = new ReactionArbiter(new ReactionArbiterConfig { AgentCount = 2 });
            var sync = new CriticalSync(2);
            Assert.IsTrue(sync.Sync(arb, 0, 1f, false, false, false, true));
            Assert.IsTrue(arb.IsBusy(0, 1.5f));
            Assert.IsFalse(ReactionAdapter.Allow(arb, 1.5f, 0, ReactionKind.Poke, true));
            // the other agent is free
            Assert.IsTrue(ReactionAdapter.Allow(arb, 1.5f, 1, ReactionKind.Poke, true));
            // walking ends: the slot is released
            Assert.IsFalse(sync.Sync(arb, 0, 3f, false, false, false, false));
            Assert.IsFalse(sync.Holds(0));
            Assert.IsFalse(arb.IsBusy(0, 3f));
            Assert.IsTrue(ReactionAdapter.Allow(arb, 3f, 0, ReactionKind.Glance, false));
        }

        [Test]
        public void CriticalSync_SwitchesKindWithoutBeingBlockedByItself()
        {
            var arb = new ReactionArbiter(new ReactionArbiterConfig { AgentCount = 1 });
            var sync = new CriticalSync(1);
            Assert.IsTrue(sync.Sync(arb, 0, 1f, false, false, false, true));
            ReactionKind k;
            Assert.IsTrue(arb.TryGetActive(0, 1.1f, out k));
            Assert.AreEqual(ReactionKind.Walking, k);
            Assert.IsTrue(sync.Sync(arb, 0, 2f, false, false, true, true));
            Assert.IsTrue(arb.TryGetActive(0, 2.1f, out k));
            Assert.AreEqual(ReactionKind.Handoff, k);
        }

        [Test]
        public void CriticalSync_ReassertsAfterSafetyExpiry()
        {
            var arb = new ReactionArbiter(new ReactionArbiterConfig { AgentCount = 1, MaxOpenEndedSeconds = 5f });
            var sync = new CriticalSync(1);
            sync.Sync(arb, 0, 0f, false, false, false, true);
            Assert.IsFalse(arb.IsBusy(0, 6f));                     // safety expiry
            Assert.IsTrue(sync.Sync(arb, 0, 6f, false, false, false, true));
            Assert.IsTrue(arb.IsBusy(0, 6.1f));                    // still walking: held again
        }

        [Test]
        public void CriticalSync_BadArgsAreSafe()
        {
            var arb = new ReactionArbiter();
            var sync = new CriticalSync(1);
            Assert.IsFalse(sync.Sync(null, 0, 0f, false, false, false, true));
            Assert.IsFalse(sync.Sync(arb, 5, 0f, false, false, false, true));
            Assert.IsFalse(sync.Holds(-1));
        }

        // ---- Allow / Release ----
        [Test]
        public void Allow_NullArbiterAlwaysAllows()
        {
            Assert.IsTrue(ReactionAdapter.Allow(null, 0f, 0, ReactionKind.Poke, false));
            ReactionAdapter.Release(null, 0, ReactionKind.Poke);
        }

        [Test]
        public void Release_FreesSlotSoSecondReactionCanPlay()
        {
            var arb = new ReactionArbiter(new ReactionArbiterConfig { AgentCount = 1 });
            Assert.IsTrue(ReactionAdapter.Allow(arb, 0f, 0, ReactionKind.Wave, true));
            ReactionAdapter.Release(arb, 0, ReactionKind.Wave);
            Assert.IsFalse(arb.IsBusy(0, 0.1f));
        }

        [Test]
        public void Focus_DropsGlanceButNotUserTouch()
        {
            var arb = new ReactionArbiter(new ReactionArbiterConfig { AgentCount = 1 });
            arb.SetFocus(true);
            Assert.IsFalse(ReactionAdapter.Allow(arb, 0f, 0, ReactionKind.Glance, false));
            Assert.IsTrue(ReactionAdapter.Allow(arb, 0f, 0, ReactionKind.Poke, true));
        }

        [Test]
        public void HandsBusy_DropsSocialEvenWhenUserInitiated()
        {
            var arb = new ReactionArbiter(new ReactionArbiterConfig { AgentCount = 1 });
            arb.SetHandsBusy(true);
            Assert.IsFalse(ReactionAdapter.Allow(arb, 0f, 0, ReactionKind.Poke, true));
            Assert.IsFalse(ReactionAdapter.Allow(arb, 0f, 0, ReactionKind.Selected, true));
        }

        [Test]
        public void WaitingOffer_IsStatusBackedAndSurvivesFocus()
        {
            var arb = new ReactionArbiter(new ReactionArbiterConfig { AgentCount = 1 });
            arb.SetFocus(true);
            Assert.IsTrue(ReactionAdapter.Allow(arb, 0f, 0, ReactionKind.WaitingOffer, true));
        }

        [Test]
        public void WaitingOffer_NotRepeatedInsideItsCooldownButAfterTheAdapterDelay()
        {
            var arb = new ReactionArbiter(new ReactionArbiterConfig { AgentCount = 1 });
            Assert.IsTrue(ReactionAdapter.Allow(arb, 0f, 0, ReactionKind.WaitingOffer, true));
            // AgentAttention schedules the next offer 8.1 .. 12 s later (AmbientVariation(min 8.1)).
            var v = new AmbientVariation(ReactionAdapter.SeedFor(0, 2), 1, 8.1f, 12f);
            for (int i = 0; i < 20; i++)
            {
                float d = v.NextDelay(1f);
                Assert.GreaterOrEqual(d, 8.1f);
                Assert.IsTrue(ReactionAdapter.Allow(arb, d, 0, ReactionKind.WaitingOffer, true));
                arb = new ReactionArbiter(new ReactionArbiterConfig { AgentCount = 1 });
                ReactionAdapter.Allow(arb, 0f, 0, ReactionKind.WaitingOffer, true);
            }
        }

        // ---- idle choice ----
        [Test]
        public void ChooseIdleAction_AlwaysLegalForTheSituation()
        {
            for (int variant = 0; variant < 3; variant++)
                for (int mask = 0; mask < 8; mask++)
                {
                    bool worker = (mask & 1) != 0, any = (mask & 2) != 0, focus = (mask & 4) != 0;
                    if (worker && !any) continue; // impossible: a working colleague is a colleague
                    IdleAction a = ReactionAdapter.ChooseIdleAction(variant, worker, any, focus);
                    if (a == IdleAction.LookAtWorker) Assert.IsTrue(worker);
                    if (a == IdleAction.LookAtColleague) Assert.IsTrue(any);
                    if (a == IdleAction.Stretch) Assert.IsFalse(focus);
                }
        }

        [Test]
        public void ChooseIdleAction_VariantsSpreadOverActions()
        {
            Assert.AreEqual(IdleAction.LookAtWorker, ReactionAdapter.ChooseIdleAction(0, true, true, false));
            Assert.AreEqual(IdleAction.Stretch, ReactionAdapter.ChooseIdleAction(1, true, true, false));
            Assert.AreEqual(IdleAction.LookAtColleague, ReactionAdapter.ChooseIdleAction(2, true, true, false));
            Assert.AreEqual(IdleAction.None, ReactionAdapter.ChooseIdleAction(0, false, false, false));
            Assert.AreEqual(IdleAction.Stretch, ReactionAdapter.ChooseIdleAction(2, false, false, false));
        }

        // ---- ambient determinism ----
        [Test]
        public void IdleVariation_SeededPerAgentIsReproducibleAndBounded()
        {
            var a1 = new AmbientVariation(ReactionAdapter.SeedFor(0, 1), 3, 7f, 16f);
            var a2 = new AmbientVariation(ReactionAdapter.SeedFor(0, 1), 3, 7f, 16f);
            var b = new AmbientVariation(ReactionAdapter.SeedFor(1, 1), 3, 7f, 16f);
            bool differs = false;
            for (int i = 0; i < 30; i++)
            {
                float d1 = a1.NextDelay(2.5f), d2 = a2.NextDelay(2.5f), d3 = b.NextDelay(2.5f);
                Assert.AreEqual(d1, d2);
                Assert.GreaterOrEqual(d1, 7f);
                Assert.LessOrEqual(d1, 16f * 2.5f);
                if (d1 != d3) differs = true;
                int v1 = a1.NextVariant(), v2 = a2.NextVariant();
                Assert.AreEqual(v1, v2);
                Assert.IsTrue(v1 >= 0 && v1 < 3);
            }
            Assert.IsTrue(differs, "agents must not idle in lockstep");
        }

        // ---- queued social plays after a body-blocking Critical ends ----
        [Test]
        public void SocialQueuedBehindWalking_PlaysAfterItEnds()
        {
            var arb = new ReactionArbiter(new ReactionArbiterConfig { AgentCount = 1 });
            var sync = new CriticalSync(1);
            sync.Sync(arb, 0, 0f, false, false, false, true);
            Assert.AreEqual(ReactionVerdict.Queued, arb.Request(0.5f, new ReactionRequest(0, ReactionKind.Wave, 0.5f, true)));
            sync.Sync(arb, 0, 1f, false, false, false, false);
            ReactionRequest played;
            Assert.IsTrue(arb.Poll(1.2f, out played));
            Assert.AreEqual(ReactionKind.Wave, played.Kind);
        }
    }
}
