#if MUSEDESK_SPATIAL_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using MuseDesk.Motion;

namespace MuseDesk.Tests.EditMode.Motion
{
    public class TransformOwnershipTests
    {
        const int T = 100;
        const int A = 1, B = 2, C = 3;
        TransformOwnership reg;

        [SetUp] public void Make() { reg = new TransformOwnership(); }

        OwnershipLease Take(int target, int owner, MovePriority p, OwnershipLostHandler h = null)
        {
            OwnershipLease l;
            Assert.IsTrue(reg.TryAcquire(target, owner, p, out l, h));
            return l;
        }

        // ---- acquire / release ----
        [Test] public void Acquire_Free_Grants()
        {
            OwnershipLease l;
            Assert.IsTrue(reg.TryAcquire(T, A, MovePriority.Navigation, out l));
            Assert.IsTrue(l.IsGranted); Assert.IsTrue(reg.IsOwner(l));
            Assert.AreEqual(A, reg.CurrentOwner(T));
            Assert.AreEqual(MovePriority.Navigation, reg.CurrentPriority(T));
        }

        [Test] public void NoOwner_ReportsSentinel()
        {
            Assert.AreEqual(TransformOwnership.NoOwner, reg.CurrentOwner(T));
            Assert.AreEqual(MovePriority.None, reg.CurrentPriority(T));
        }

        [Test] public void Acquire_PriorityNone_Refused()
        {
            OwnershipLease l;
            Assert.IsFalse(reg.TryAcquire(T, A, MovePriority.None, out l));
            Assert.IsFalse(l.IsGranted); Assert.AreEqual(0, reg.Count);
        }

        [Test] public void Release_FreesTarget_NoCallback()
        {
            int calls = 0;
            OwnershipLease l = Take(T, A, MovePriority.Navigation, (x, r) => calls++);
            Assert.IsTrue(reg.Release(l));
            Assert.AreEqual(TransformOwnership.NoOwner, reg.CurrentOwner(T));
            Assert.IsFalse(reg.IsOwner(l));
            Assert.AreEqual(0, calls);
        }

        [Test] public void Release_Twice_SecondIsFalse()
        {
            OwnershipLease l = Take(T, A, MovePriority.Navigation);
            Assert.IsTrue(reg.Release(l)); Assert.IsFalse(reg.Release(l));
        }

        [Test] public void Release_DefaultLease_IsFalse()
        {
            Assert.IsFalse(reg.Release(default(OwnershipLease)));
            Assert.IsFalse(reg.IsOwner(default(OwnershipLease)));
        }

        [Test] public void SameOwnerSamePriority_IsIdempotent()
        {
            OwnershipLease a = Take(T, A, MovePriority.Navigation);
            OwnershipLease b = Take(T, A, MovePriority.Navigation);
            Assert.AreEqual(a.Version, b.Version);
            Assert.IsTrue(reg.IsOwner(a));
            Assert.AreEqual(1, reg.Count);
        }

        [Test] public void SameOwnerChangesPriority_NewVersion_OldStale_NoCallback()
        {
            int calls = 0;
            OwnershipLease a = Take(T, A, MovePriority.Ambient, (x, r) => calls++);
            OwnershipLease b = Take(T, A, MovePriority.Navigation);
            Assert.AreNotEqual(a.Version, b.Version);
            Assert.IsFalse(reg.IsOwner(a)); Assert.IsTrue(reg.IsOwner(b));
            Assert.AreEqual(0, calls);
        }

        // ---- priority / preemption ----
        [Test] public void EqualPriority_OtherOwner_Refused()
        {
            OwnershipLease a = Take(T, A, MovePriority.Navigation);
            OwnershipLease b;
            Assert.IsFalse(reg.TryAcquire(T, B, MovePriority.Navigation, out b));
            Assert.IsFalse(b.IsGranted); Assert.IsTrue(reg.IsOwner(a));
        }

        [Test] public void LowerPriority_Refused()
        {
            Take(T, A, MovePriority.Carrier);
            OwnershipLease b;
            Assert.IsFalse(reg.TryAcquire(T, B, MovePriority.Navigation, out b));
            Assert.AreEqual(A, reg.CurrentOwner(T));
        }

        [Test] public void FullPreemptionLadder()
        {
            OwnershipLease amb = Take(T, A, MovePriority.Ambient);
            OwnershipLease nav = Take(T, B, MovePriority.Navigation);
            OwnershipLease car = Take(T, C, MovePriority.Carrier);
            OwnershipLease held = Take(T, 4, MovePriority.UserHeld);
            Assert.IsFalse(reg.IsOwner(amb)); Assert.IsFalse(reg.IsOwner(nav)); Assert.IsFalse(reg.IsOwner(car));
            Assert.IsTrue(reg.IsOwner(held));
            Assert.AreEqual(4, reg.CurrentOwner(T));
        }

        [Test] public void UserHeld_CannotBePreemptedByAnyone()
        {
            Take(T, A, MovePriority.UserHeld);
            OwnershipLease l;
            Assert.IsFalse(reg.TryAcquire(T, B, MovePriority.Carrier, out l));
            Assert.IsFalse(reg.TryAcquire(T, B, MovePriority.UserHeld, out l));
        }

        [Test] public void Preempt_CallsOldCallbackOnce_WithReason()
        {
            int calls = 0; OwnershipLossReason got = 0; OwnershipLease lost = default(OwnershipLease);
            OwnershipLease a = Take(T, A, MovePriority.Ambient, (x, r) => { calls++; got = r; lost = x; });
            Take(T, B, MovePriority.Navigation);
            Assert.AreEqual(1, calls); Assert.AreEqual(OwnershipLossReason.Preempted, got);
            Assert.AreEqual(a.Version, lost.Version);
        }

        [Test] public void Preempt_DoesNotCallNewOwnersCallback()
        {
            int newCalls = 0;
            Take(T, A, MovePriority.Ambient);
            Take(T, B, MovePriority.Navigation, (x, r) => newCalls++);
            Assert.AreEqual(0, newCalls);
        }

        // ---- cancel ----
        [Test] public void Cancel_CallsCallbackExactlyOnce()
        {
            int calls = 0; OwnershipLossReason got = 0;
            Take(T, A, MovePriority.Navigation, (x, r) => { calls++; got = r; });
            Assert.IsTrue(reg.Cancel(T, OwnershipLossReason.Cancelled));
            Assert.IsFalse(reg.Cancel(T));
            Assert.AreEqual(1, calls); Assert.AreEqual(OwnershipLossReason.Cancelled, got);
            Assert.AreEqual(TransformOwnership.NoOwner, reg.CurrentOwner(T));
        }

        [Test] public void Cancel_Unowned_IsFalse() { Assert.IsFalse(reg.Cancel(T)); }

        [Test] public void Cancel_ThenPreemptAttempt_NoSecondCallback()
        {
            int calls = 0;
            Take(T, A, MovePriority.Ambient, (x, r) => calls++);
            reg.Cancel(T);
            Take(T, B, MovePriority.Navigation);
            Assert.AreEqual(1, calls);
        }

        [Test] public void Cancel_NullCallback_Safe()
        {
            Take(T, A, MovePriority.Navigation);
            Assert.DoesNotThrow(() => reg.Cancel(T));
        }

        [Test] public void Callback_Throwing_IsSwallowed_StateConsistent()
        {
            Take(T, A, MovePriority.Ambient, (x, r) => { throw new InvalidOperationException("boom"); });
            OwnershipLease b = Take(T, B, MovePriority.Navigation);
            Assert.AreEqual(1, reg.CallbackErrors);
            Assert.IsTrue(reg.IsOwner(b));
        }

        // ---- stale lease ----
        [Test] public void StaleLease_AfterPreempt_Rejected()
        {
            OwnershipLease a = Take(T, A, MovePriority.Ambient);
            Take(T, B, MovePriority.Navigation);
            Assert.IsFalse(reg.IsOwner(a));
            Assert.IsFalse(reg.Release(a));
            Assert.AreEqual(B, reg.CurrentOwner(T));
        }

        [Test] public void StaleLease_AfterReacquireBySameOwner_Rejected()
        {
            OwnershipLease first = Take(T, A, MovePriority.Navigation);
            reg.Release(first);
            OwnershipLease second = Take(T, A, MovePriority.Navigation);
            Assert.IsFalse(reg.IsOwner(first)); Assert.IsTrue(reg.IsOwner(second));
            Assert.AreNotEqual(first.Version, second.Version);
        }

        [Test] public void StaleLease_AfterCancel_Rejected()
        {
            OwnershipLease a = Take(T, A, MovePriority.Navigation);
            reg.Cancel(T);
            Assert.IsFalse(reg.IsOwner(a));
            Take(T, B, MovePriority.Ambient);
            Assert.IsFalse(reg.IsOwner(a));
        }

        // ---- recover ----
        [Test] public void Recover_DeadOwner_ClearsWithoutCallback()
        {
            int calls = 0;
            OwnershipLease a = Take(T, A, MovePriority.Navigation, (x, r) => calls++);
            reg.IsOwnerAlive = id => false;
            Assert.IsTrue(reg.Recover(T));
            Assert.IsFalse(reg.IsOwner(a)); Assert.AreEqual(0, calls);
            Take(T, B, MovePriority.Ambient); // free again
        }

        [Test] public void Recover_LiveOwner_Untouched()
        {
            OwnershipLease a = Take(T, A, MovePriority.Navigation);
            reg.IsOwnerAlive = id => true;
            Assert.IsFalse(reg.Recover(T)); Assert.IsTrue(reg.IsOwner(a));
        }

        [Test] public void Recover_NoPredicate_IsNoOp()
        {
            Take(T, A, MovePriority.Navigation);
            Assert.IsFalse(reg.Recover(T)); Assert.AreEqual(1, reg.Count);
        }

        [Test] public void RecoverAll_ClearsOnlyDead()
        {
            HashSet<int> dead = new HashSet<int> { A, C };
            OwnershipLease a = Take(1, A, MovePriority.Navigation);
            OwnershipLease b = Take(2, B, MovePriority.Navigation);
            Take(3, C, MovePriority.Ambient);
            reg.IsOwnerAlive = id => !dead.Contains(id);
            Assert.AreEqual(2, reg.RecoverAll());
            Assert.IsFalse(reg.IsOwner(a)); Assert.IsTrue(reg.IsOwner(b)); Assert.AreEqual(1, reg.Count);
        }

        [Test] public void Recover_Unowned_IsFalse()
        {
            reg.IsOwnerAlive = id => false;
            Assert.IsFalse(reg.Recover(T));
        }

        // ---- re-entrancy ----
        [Test] public void Reentrant_AcquireInsideCancelCallback_SameTarget_Succeeds()
        {
            OwnershipLease again = default(OwnershipLease); bool ok = false;
            Take(T, A, MovePriority.Navigation, (x, r) => { ok = reg.TryAcquire(T, A, MovePriority.Ambient, out again); });
            reg.Cancel(T);
            Assert.IsTrue(ok); Assert.IsTrue(reg.IsOwner(again));
            Assert.AreEqual(A, reg.CurrentOwner(T));
        }

        [Test] public void Reentrant_AcquireInsidePreemptCallback_LowerRankRefused()
        {
            bool ok = true; OwnershipLease again;
            Take(T, A, MovePriority.Ambient, (x, r) => { ok = reg.TryAcquire(T, A, MovePriority.Ambient, out again); });
            OwnershipLease b = Take(T, B, MovePriority.Navigation);
            Assert.IsFalse(ok); Assert.IsTrue(reg.IsOwner(b)); Assert.AreEqual(B, reg.CurrentOwner(T));
        }

        [Test] public void Reentrant_PreemptorCallbackAcquiresOtherTarget()
        {
            Take(T, A, MovePriority.Ambient, (x, r) => { OwnershipLease l; reg.TryAcquire(200, A, MovePriority.Ambient, out l); });
            Take(T, B, MovePriority.Navigation);
            Assert.AreEqual(A, reg.CurrentOwner(200)); Assert.AreEqual(B, reg.CurrentOwner(T));
        }

        [Test] public void Reentrant_CancelOtherTargetInsideCallback()
        {
            int calls2 = 0;
            Take(200, C, MovePriority.Navigation, (x, r) => calls2++);
            Take(T, A, MovePriority.Navigation, (x, r) => reg.Cancel(200));
            reg.Cancel(T);
            Assert.AreEqual(1, calls2); Assert.AreEqual(0, reg.Count);
        }

        [Test] public void Reentrant_CancelSameTargetInsideItsOwnCallback_NoDoubleCall()
        {
            int calls = 0;
            Take(T, A, MovePriority.Navigation, (x, r) => { calls++; reg.Cancel(T); });
            reg.Cancel(T);
            Assert.AreEqual(1, calls);
        }

        // ---- many targets ----
        [Test] public void ManyTargets_Independent()
        {
            const int n = 500;
            OwnershipLease[] ls = new OwnershipLease[n];
            for (int i = 0; i < n; i++) ls[i] = Take(i, i + 1000, MovePriority.Navigation);
            Assert.AreEqual(n, reg.Count);
            for (int i = 0; i < n; i += 2) reg.Cancel(i);
            for (int i = 0; i < n; i++) Assert.AreEqual(i % 2 != 0, reg.IsOwner(ls[i]), "target " + i);
            Assert.AreEqual(n / 2, reg.Count);
        }

        [Test] public void SameOwnerManyTargets_ReleaseOneKeepsOthers()
        {
            OwnershipLease a = Take(1, A, MovePriority.Navigation);
            OwnershipLease b = Take(2, A, MovePriority.Navigation);
            reg.Release(a);
            Assert.IsTrue(reg.IsOwner(b)); Assert.IsFalse(reg.IsOwner(a));
        }

        [Test] public void Clear_DropsAllWithoutCallbacks()
        {
            int calls = 0;
            Take(1, A, MovePriority.Navigation, (x, r) => calls++);
            reg.Clear();
            Assert.AreEqual(0, reg.Count); Assert.AreEqual(0, calls);
        }

        [Test] public void Shared_IsOneInstance() { Assert.AreSame(TransformOwnership.Shared, TransformOwnership.Shared); }

        // ---- OwnedMover ----
        [Test] public void OwnedMover_AcquiresThenWrites()
        {
            OwnershipLease l = default(OwnershipLease);
            Assert.AreEqual(WriteDecision.Write, OwnedMover.Decide(reg, ref l, T, A, MovePriority.Navigation));
            Assert.AreEqual(WriteDecision.Write, OwnedMover.Decide(reg, ref l, T, A, MovePriority.Navigation));
        }

        [Test] public void OwnedMover_BlockedWhileBetterOwnerHolds()
        {
            Take(T, B, MovePriority.Carrier);
            OwnershipLease l = default(OwnershipLease);
            Assert.AreEqual(WriteDecision.Blocked, OwnedMover.Decide(reg, ref l, T, A, MovePriority.Navigation));
            Assert.IsFalse(l.IsGranted);
        }

        [Test] public void OwnedMover_LostAfterPreempt_ThenBlocked_ThenReacquiresAfterRelease()
        {
            OwnershipLease l = default(OwnershipLease);
            OwnedMover.Decide(reg, ref l, T, A, MovePriority.Ambient);
            OwnershipLease carrier = Take(T, B, MovePriority.Carrier);
            Assert.AreEqual(WriteDecision.Lost, OwnedMover.Decide(reg, ref l, T, A, MovePriority.Ambient));
            Assert.AreEqual(WriteDecision.Blocked, OwnedMover.Decide(reg, ref l, T, A, MovePriority.Ambient));
            reg.Release(carrier);
            Assert.AreEqual(WriteDecision.Write, OwnedMover.Decide(reg, ref l, T, A, MovePriority.Ambient));
        }

        [Test] public void OwnedMover_Finish_ReleasesAndIsSafeWhenStale()
        {
            OwnershipLease l = default(OwnershipLease);
            OwnedMover.Decide(reg, ref l, T, A, MovePriority.Navigation);
            OwnedMover.Finish(reg, ref l);
            Assert.AreEqual(TransformOwnership.NoOwner, reg.CurrentOwner(T));
            Assert.DoesNotThrow(() => OwnedMover.Finish(reg, ref l));
        }

        [Test] public void OwnedMover_Finish_StaleLease_DoesNotReleaseNewOwner()
        {
            OwnershipLease l = default(OwnershipLease);
            OwnedMover.Decide(reg, ref l, T, A, MovePriority.Ambient);
            OwnershipLease b = Take(T, B, MovePriority.Navigation);
            OwnedMover.Finish(reg, ref l);
            Assert.IsTrue(reg.IsOwner(b));
        }
    }
}
#endif
