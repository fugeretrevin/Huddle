using NUnit.Framework;
using MuseDesk.Agents;

namespace MuseDesk.Tests.AgentInteraction
{
    public class ReactionArbiterTests
    {
        static ReactionRequest R(int a, ReactionKind k, float t, bool sb = false) { return new ReactionRequest(a, k, t, sb); }
        static ReactionVerdict Ask(ReactionArbiter arb, float now, int a, ReactionKind k, bool sb = false) { return arb.Request(now, R(a, k, now, sb)); }

        [Test]
        public void ClassOf_MapsEveryKind()
        {
            Assert.AreEqual(ReactionClass.Critical, ReactionArbiter.ClassOf(ReactionKind.PaperTransfer));
            Assert.AreEqual(ReactionClass.Critical, ReactionArbiter.ClassOf(ReactionKind.Handoff));
            Assert.AreEqual(ReactionClass.Critical, ReactionArbiter.ClassOf(ReactionKind.ElevatorBoarding));
            Assert.AreEqual(ReactionClass.Critical, ReactionArbiter.ClassOf(ReactionKind.Walking));
            Assert.AreEqual(ReactionClass.Task, ReactionArbiter.ClassOf(ReactionKind.CompletionNod));
            Assert.AreEqual(ReactionClass.Task, ReactionArbiter.ClassOf(ReactionKind.ErrorWorry));
            Assert.AreEqual(ReactionClass.Task, ReactionArbiter.ClassOf(ReactionKind.WaitingOffer));
            Assert.AreEqual(ReactionClass.Social, ReactionArbiter.ClassOf(ReactionKind.Poke));
            Assert.AreEqual(ReactionClass.Social, ReactionArbiter.ClassOf(ReactionKind.Wave));
            Assert.AreEqual(ReactionClass.Social, ReactionArbiter.ClassOf(ReactionKind.Glance));
            Assert.AreEqual(ReactionClass.Social, ReactionArbiter.ClassOf(ReactionKind.Selected));
            Assert.AreEqual(ReactionClass.Ambient, ReactionArbiter.ClassOf(ReactionKind.IdleVariation));
        }

        // ---- interruption rules ----
        [Test]
        public void Social_DoesNotInterruptCritical()
        {
            var a = new ReactionArbiter();
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0f, 0, ReactionKind.Walking));
            Assert.AreEqual(ReactionVerdict.Queued, Ask(a, 0.1f, 0, ReactionKind.Poke));
            ReactionKind k; Assert.IsTrue(a.TryGetActive(0, 0.1f, out k)); Assert.AreEqual(ReactionKind.Walking, k);
        }

        [Test]
        public void Ambient_NeverInterruptsOrQueues()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.Walking);
            Assert.AreEqual(ReactionVerdict.DroppedLowerPriority, Ask(a, 0.1f, 0, ReactionKind.IdleVariation));
            Assert.AreEqual(0, a.QueuedCount);
        }

        [Test]
        public void Ambient_DoesNotInterruptTask()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.ErrorWorry);
            Assert.AreNotEqual(ReactionVerdict.Play, Ask(a, 0.5f, 0, ReactionKind.IdleVariation));
        }

        [Test]
        public void Critical_PreemptsTask()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.ErrorWorry);
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0.5f, 0, ReactionKind.Handoff));
            ReactionKind k; a.TryGetActive(0, 0.5f, out k); Assert.AreEqual(ReactionKind.Handoff, k);
        }

        [Test]
        public void Task_PreemptsSocial_EvenRightAfterAPoke()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.Poke);
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0.1f, 0, ReactionKind.ErrorWorry));
        }

        [Test]
        public void SameClass_DoesNotInterrupt()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.Poke);
            Assert.AreNotEqual(ReactionVerdict.Play, Ask(a, 0.1f, 0, ReactionKind.Wave));
            ReactionKind k; a.TryGetActive(0, 0.1f, out k); Assert.AreEqual(ReactionKind.Poke, k);
        }

        [Test]
        public void TimedReaction_FreesSlotAfterDuration()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.Poke);
            Assert.IsTrue(a.IsBusy(0, 0.5f));
            Assert.IsFalse(a.IsBusy(0, 1.0f));
        }

        [Test]
        public void OpenEnded_HeldUntilEnd()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.PaperTransfer);
            Assert.IsTrue(a.IsBusy(0, 30f));
            a.End(0, ReactionKind.PaperTransfer);
            Assert.IsFalse(a.IsBusy(0, 30f));
        }

        [Test]
        public void End_WrongKindIsIgnored()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.Walking);
            a.End(0, ReactionKind.Handoff);
            Assert.IsTrue(a.IsBusy(0, 1f));
        }

        [Test]
        public void OpenEnded_SafetyExpiry()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.Walking);
            Assert.IsTrue(a.IsBusy(0, 59f));
            Assert.IsFalse(a.IsBusy(0, 60f));
        }

        [Test]
        public void Agents_AreIndependent()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.Walking);
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0.1f, 1, ReactionKind.Poke));
        }

        // ---- cooldowns ----
        [Test]
        public void KindCooldown_BlocksRepeatThenAllows()
        {
            var a = new ReactionArbiter();
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0f, 0, ReactionKind.Wave));
            Assert.AreEqual(ReactionVerdict.DroppedCooldown, Ask(a, 2.0f, 0, ReactionKind.Wave));
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 3.0f, 0, ReactionKind.Wave));
        }

        [Test]
        public void PerAgentGap_ThrottlesSocialChatter()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.Poke);
            a.End(0, ReactionKind.Poke);
            Assert.AreEqual(ReactionVerdict.DroppedCooldown, Ask(a, 0.5f, 0, ReactionKind.Glance));
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0.9f, 0, ReactionKind.Glance));
        }

        [Test]
        public void CompletionNod_MatchesNodGateGap()
        {
            var a = new ReactionArbiter();
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0f, 0, ReactionKind.CompletionNod));
            Assert.AreEqual(ReactionVerdict.DroppedCooldown, Ask(a, 3f, 0, ReactionKind.CompletionNod));
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 6.5f, 0, ReactionKind.CompletionNod));
        }

        [Test]
        public void GlobalRateCap_DropsFifthThenRecovers()
        {
            var a = new ReactionArbiter();
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0.0f, 0, ReactionKind.Poke));
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0.1f, 1, ReactionKind.Poke));
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0.2f, 2, ReactionKind.Poke));
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 1.0f, 0, ReactionKind.Selected));
            Assert.AreEqual(ReactionVerdict.DroppedRateCap, Ask(a, 1.2f, 1, ReactionKind.Selected));
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 6.1f, 1, ReactionKind.Wave));
        }

        [Test]
        public void Critical_IsExemptFromRateCapAndDoesNotCountTowardIt()
        {
            var a = new ReactionArbiter();
            Ask(a, 0.0f, 0, ReactionKind.Poke);
            Ask(a, 0.1f, 1, ReactionKind.Poke);
            Ask(a, 0.2f, 2, ReactionKind.Poke);
            Ask(a, 1.0f, 0, ReactionKind.Selected);
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 1.2f, 2, ReactionKind.Walking));
            // the walk did not consume a slot: the cap is still full of the four social plays
            Assert.AreEqual(ReactionVerdict.DroppedRateCap, Ask(a, 1.3f, 1, ReactionKind.Selected));
        }

        // ---- focus ----
        [Test]
        public void Focus_DropsSocialAndAmbient()
        {
            var a = new ReactionArbiter(); a.SetFocus(true);
            Assert.AreEqual(ReactionVerdict.DroppedFocus, Ask(a, 0f, 0, ReactionKind.Poke));
            Assert.AreEqual(ReactionVerdict.DroppedFocus, Ask(a, 0f, 1, ReactionKind.Glance));
            Assert.AreEqual(ReactionVerdict.DroppedFocus, Ask(a, 0f, 2, ReactionKind.IdleVariation));
        }

        [Test]
        public void Focus_KeepsStatusBackedAndTaskAndCritical()
        {
            var a = new ReactionArbiter(); a.SetFocus(true);
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0f, 0, ReactionKind.Selected, true));
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0f, 1, ReactionKind.ErrorWorry));
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0f, 2, ReactionKind.Handoff));
        }

        [Test]
        public void Focus_SilencesCompletionNod_LikeNodGate()
        {
            var a = new ReactionArbiter(); a.SetFocus(true);
            Assert.AreEqual(ReactionVerdict.DroppedFocus, Ask(a, 0f, 0, ReactionKind.CompletionNod));
            a.SetFocus(false);
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0.1f, 0, ReactionKind.CompletionNod));
        }

        // ---- busy hands ----
        [Test]
        public void HandsBusy_DropsSocialOnly()
        {
            var a = new ReactionArbiter(); a.SetHandsBusy(true);
            Assert.AreEqual(ReactionVerdict.DroppedHandsBusy, Ask(a, 0f, 0, ReactionKind.Poke));
            Assert.AreEqual(ReactionVerdict.DroppedHandsBusy, Ask(a, 0f, 1, ReactionKind.Wave, true));
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0f, 2, ReactionKind.ErrorWorry));
            Assert.AreEqual(ReactionVerdict.Play, Ask(a, 0f, 0, ReactionKind.IdleVariation));
        }

        // ---- staleness / queue ----
        [Test]
        public void Stale_RequestIsDropped()
        {
            var a = new ReactionArbiter();
            Assert.AreEqual(ReactionVerdict.DroppedStale, a.Request(3f, R(0, ReactionKind.ErrorWorry, 0f)));
            Assert.AreEqual(1, a.StaleDropped);
        }

        [Test]
        public void Queued_GoesStaleInPoll()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.Walking);
            Assert.AreEqual(ReactionVerdict.Queued, Ask(a, 0f, 0, ReactionKind.ErrorWorry));
            ReactionRequest p;
            Assert.IsFalse(a.Poll(3f, out p));
            Assert.AreEqual(0, a.QueuedCount);
            Assert.AreEqual(1, a.StaleDropped);
        }

        [Test]
        public void Queue_IsBoundedAndEvictsOldest()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.Walking);
            for (int i = 0; i < ReactionArbiter.QueueCapacity; i++)
                Assert.AreEqual(ReactionVerdict.Queued, a.Request(0.1f, R(0, ReactionKind.ErrorWorry, i * 0.01f)));
            Assert.AreEqual(ReactionArbiter.QueueCapacity, a.QueuedCount);
            Assert.AreEqual(ReactionVerdict.Queued, a.Request(0.1f, R(0, ReactionKind.WaitingOffer, 0.09f)));
            Assert.AreEqual(ReactionArbiter.QueueCapacity, a.QueuedCount);
        }

        [Test]
        public void Queue_FullOfTasks_RejectsSocialNewcomer()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.Walking);
            for (int i = 0; i < ReactionArbiter.QueueCapacity; i++) a.Request(0.1f, R(0, ReactionKind.ErrorWorry, 0.05f));
            Assert.AreEqual(ReactionVerdict.DroppedQueueFull, Ask(a, 0.1f, 0, ReactionKind.Poke));
        }

        [Test]
        public void Poll_PlaysQueuedSocialAfterCriticalEnds()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.Walking);
            Ask(a, 0.1f, 0, ReactionKind.Poke);
            ReactionRequest p;
            Assert.IsFalse(a.Poll(0.2f, out p));
            a.End(0, ReactionKind.Walking);
            Assert.IsTrue(a.Poll(0.3f, out p));
            Assert.AreEqual(ReactionKind.Poke, p.Kind);
        }

        [Test]
        public void Poll_PrefersHigherClassThenKeepsBlockedOnes()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.Walking);
            Ask(a, 0.1f, 0, ReactionKind.Poke);
            Ask(a, 0.2f, 0, ReactionKind.ErrorWorry);
            a.End(0, ReactionKind.Walking);
            ReactionRequest p;
            Assert.IsTrue(a.Poll(0.5f, out p)); Assert.AreEqual(ReactionKind.ErrorWorry, p.Kind);
            Assert.IsFalse(a.Poll(0.6f, out p)); Assert.AreEqual(1, a.QueuedCount);
            Assert.IsTrue(a.Poll(2.55f, out p)); Assert.AreEqual(ReactionKind.Poke, p.Kind);
        }

        [Test]
        public void Poll_DropsQueuedSocialWhenFocusTurnsOn()
        {
            var a = new ReactionArbiter();
            Ask(a, 0f, 0, ReactionKind.Walking);
            Ask(a, 0.1f, 0, ReactionKind.Poke);
            a.SetFocus(true);
            a.End(0, ReactionKind.Walking);
            ReactionRequest p;
            Assert.IsFalse(a.Poll(0.3f, out p));
            Assert.AreEqual(0, a.QueuedCount);
        }

        [Test]
        public void BadArgs_AreRejected()
        {
            var a = new ReactionArbiter();
            Assert.AreEqual(ReactionVerdict.DroppedBadArgs, Ask(a, 0f, 9, ReactionKind.Poke));
            Assert.AreEqual(ReactionVerdict.DroppedBadArgs, Ask(a, 0f, -1, ReactionKind.Poke));
            Assert.AreEqual(ReactionVerdict.DroppedBadArgs, a.Request(0f, R(0, (ReactionKind)99, 0f)));
        }

        // ---- determinism ----
        [Test]
        public void Arbiter_SameInputsSameVerdicts()
        {
            var x = new ReactionArbiter(); var y = new ReactionArbiter();
            var rng = new SeededRng(7);
            for (int i = 0; i < 300; i++)
            {
                float t = i * 0.37f;
                int ag = rng.NextInt(0, 3);
                var kind = (ReactionKind)rng.NextInt(0, ReactionArbiter.KindCount);
                if (i % 50 == 25) { x.SetFocus(true); y.SetFocus(true); }
                if (i % 50 == 0) { x.SetFocus(false); y.SetFocus(false); }
                Assert.AreEqual(Ask(x, t, ag, kind), Ask(y, t, ag, kind), "step " + i);
            }
        }

        [Test]
        public void SeededRng_Deterministic_AndSeedSensitive()
        {
            var a = new SeededRng(42); var b = new SeededRng(42); var c = new SeededRng(43);
            bool differs = false;
            for (int i = 0; i < 20; i++)
            {
                uint va = a.NextUInt();
                Assert.AreEqual(va, b.NextUInt());
                if (va != c.NextUInt()) differs = true;
            }
            Assert.IsTrue(differs);
        }

        [Test]
        public void SeededRng_FloatInUnitRange()
        {
            var r = new SeededRng(1);
            for (int i = 0; i < 1000; i++) { float f = r.NextFloat(); Assert.GreaterOrEqual(f, 0f); Assert.Less(f, 1f); }
        }

        [Test]
        public void AmbientVariation_IsBoundedAndDeterministic()
        {
            var a = new AmbientVariation(5, 4, 8f, 14f); var b = new AmbientVariation(5, 4, 8f, 14f);
            int prev = -1;
            for (int i = 0; i < 300; i++)
            {
                float da = a.NextDelay(), db = b.NextDelay();
                Assert.AreEqual(da, db);
                Assert.GreaterOrEqual(da, 8f); Assert.LessOrEqual(da, 14f);
                int va = a.NextVariant();
                Assert.AreEqual(va, b.NextVariant());
                Assert.GreaterOrEqual(va, 0); Assert.Less(va, 4);
                Assert.AreNotEqual(prev, va); prev = va;
            }
        }

        [Test]
        public void AmbientVariation_SlowMultiplierIsClamped()
        {
            var a = new AmbientVariation(3, 2, 10f, 10f);
            Assert.AreEqual(40f, a.NextDelay(100f), 1e-4f);
            Assert.AreEqual(10f, a.NextDelay(0.1f), 1e-4f);
        }

        [Test]
        public void AmbientVariation_SingleVariantIsStable()
        {
            var a = new AmbientVariation(3, 1, 5f, 6f);
            Assert.AreEqual(0, a.NextVariant()); Assert.AreEqual(0, a.NextVariant());
        }
    }

    public class AttentionPolicyTests
    {
        static AttentionContext Ctx(bool addr = false, bool glance = false, bool hand = false, bool work = true)
        { return new AttentionContext { AddressedByUser = addr, GlanceAtUserRequested = glance, HandoffActive = hand, Working = work }; }

        [Test]
        public void Default_LooksAtWorkstation()
        {
            var p = new AttentionPolicy();
            var d = p.Update(0f, Ctx());
            Assert.AreEqual(AttentionTarget.Workstation, d.Target);
            Assert.IsTrue(d.Switched);
        }

        [Test]
        public void Addressed_LooksAtUser_ThenReturnsToWork_EvenIfStillAddressed()
        {
            var p = new AttentionPolicy();
            Assert.AreEqual(AttentionTarget.User, p.Update(0f, Ctx(addr: true)).Target);
            Assert.AreEqual(AttentionTarget.User, p.Update(2.9f, Ctx(addr: true)).Target);
            Assert.AreEqual(AttentionTarget.Workstation, p.Update(3.0f, Ctx(addr: true)).Target);
            Assert.AreEqual(AttentionTarget.Workstation, p.Update(10f, Ctx(addr: true)).Target);
        }

        [Test]
        public void ReAddressDuringCooldown_GetsShortAcknowledgement()
        {
            var p = new AttentionPolicy();
            p.Update(0f, Ctx(addr: true)); p.Update(3.1f, Ctx());
            Assert.AreEqual(AttentionTarget.User, p.Update(4.0f, Ctx(addr: true)).Target);
            Assert.AreEqual(AttentionTarget.User, p.Update(5.1f, Ctx(addr: true)).Target);
            Assert.AreEqual(AttentionTarget.Workstation, p.Update(5.3f, Ctx(addr: true)).Target);
        }

        [Test]
        public void ReAddressAfterCooldown_GetsFullDwell()
        {
            var p = new AttentionPolicy();
            p.Update(0f, Ctx(addr: true)); p.Update(3.1f, Ctx());
            Assert.AreEqual(AttentionTarget.User, p.Update(9.0f, Ctx(addr: true)).Target);
            Assert.AreEqual(AttentionTarget.User, p.Update(11.5f, Ctx(addr: true)).Target);
        }

        [Test]
        public void UnpromptedGlance_IsShortAndDeniedInCooldown()
        {
            var p = new AttentionPolicy();
            Assert.AreEqual(AttentionTarget.User, p.Update(0f, Ctx(glance: true)).Target);
            Assert.AreEqual(AttentionTarget.Workstation, p.Update(1.0f, Ctx(glance: true)).Target);
            p.Update(1.1f, Ctx());
            Assert.AreEqual(AttentionTarget.Workstation, p.Update(3.0f, Ctx(glance: true)).Target);
            p.Update(3.1f, Ctx());
            Assert.AreEqual(AttentionTarget.User, p.Update(7.0f, Ctx(glance: true)).Target);
        }

        [Test]
        public void Handoff_LooksAtColleague_AndCutsUserLookShort()
        {
            var p = new AttentionPolicy();
            p.Update(0f, Ctx(addr: true));
            Assert.AreEqual(AttentionTarget.Colleague, p.Update(1f, Ctx(addr: true, hand: true)).Target);
            Assert.AreEqual(AttentionTarget.Workstation, p.Update(2f, Ctx(addr: true)).Target);
        }

        [Test]
        public void Handoff_BeatsFreshAddress()
        {
            var p = new AttentionPolicy();
            Assert.AreEqual(AttentionTarget.Colleague, p.Update(0f, Ctx(addr: true, hand: true)).Target);
        }

        [Test]
        public void Switched_OnlyOnChange_AndBlendHintsAreSmooth()
        {
            var p = new AttentionPolicy();
            Assert.IsTrue(p.Update(0f, Ctx()).Switched);
            Assert.IsFalse(p.Update(0.1f, Ctx()).Switched);
            var u = p.Update(0.2f, Ctx(addr: true));
            Assert.IsTrue(u.Switched); Assert.Greater(u.BlendSeconds, 0f); Assert.Greater(u.Weight, 0f);
            Assert.IsFalse(p.Update(0.3f, Ctx(addr: true)).Switched);
            var w = p.Update(3.5f, Ctx());
            Assert.IsTrue(w.Switched); Assert.Greater(w.BlendSeconds, u.BlendSeconds);
        }

        [Test]
        public void NeverStaresContinuously_AcrossManyAddresses()
        {
            var p = new AttentionPolicy();
            float maxRun = 0f, runStart = -1f;
            bool prevAddr = false;
            for (float t = 0f; t < 120f; t += 0.1f)
            {
                bool addr = ((int)(t * 10) % 20) < 10; // toggles every second: constant re-addressing
                var d = p.Update(t, Ctx(addr: addr));
                bool u = d.Target == AttentionTarget.User;
                if (u && runStart < 0f) runStart = t;
                if (!u && runStart >= 0f) { if (t - runStart > maxRun) maxRun = t - runStart; runStart = -1f; }
                prevAddr = addr;
            }
            Assert.LessOrEqual(maxRun, 3.0f + 0.15f);
            Assert.IsFalse(prevAddr && false);
        }

        [Test]
        public void LevelHeldAddressed_DoesNotRetrigger()
        {
            var p = new AttentionPolicy();
            p.Update(0f, Ctx(addr: true));
            p.Update(3.1f, Ctx(addr: true));
            Assert.AreEqual(AttentionTarget.Workstation, p.Update(20f, Ctx(addr: true)).Target);
        }

        [Test]
        public void WorkingWeight_ExceedsIdleWeight()
        {
            var a = new AttentionPolicy(); var b = new AttentionPolicy();
            Assert.Greater(a.Update(0f, Ctx(work: true)).Weight, b.Update(0f, Ctx(work: false)).Weight);
        }

        [Test]
        public void UserLookSeconds_TracksCurrentLook()
        {
            var p = new AttentionPolicy();
            p.Update(0f, Ctx(addr: true));
            Assert.AreEqual(1.5f, p.UserLookSeconds(1.5f), 1e-4f);
            p.Update(3.1f, Ctx());
            Assert.AreEqual(0f, p.UserLookSeconds(3.2f));
        }
    }
}
