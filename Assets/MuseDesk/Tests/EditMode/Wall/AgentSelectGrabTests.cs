// =============================================================================
// MuseDesk · EditMode tests · Select Agents force grab (AgentSelectGrabV1). Pure logic only:
// AgentRayTargeting, AgentTargetLock, VisitDropRule, SelectBlockWords, EnableLedger, the new
// AgentVisitFlow regrab / put-back states, the arbiter's read-only IsPending/IsSpent, and the
// hand-claim precedence the grab relies on. Not proven here: ISDK hands, the hidden-office mute on a
// real rig, visuals. No avatar movement in this feature changes backend task state (nothing here can).
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    [Category("MuseDesk")]
    public class AgentSelectGrabTests
    {
        const float Dt = 1f / 72f;

        // ── ray targeting ──

        static Vector3 Off(Vector3 dir, float degrees) { return Quaternion.AngleAxis(degrees, Vector3.up) * dir; }

        [Test]
        public void Pick_SmallAgentAtTwoMetres_AcceptsTheAngularCone_RejectsBeyond()
        {
            var centers = new List<Vector3> { new Vector3(0, 0, 2f) };
            var ok = new List<bool> { true };
            // 0.03 m physical radius alone is under 1 deg at 2 m: a real hand ray cannot hold that.
            Assert.AreEqual(-1, AgentRayTargeting.Pick(Vector3.zero, Off(Vector3.forward, 3f), centers, ok, 0.03f, 0f, 0.2f), "physical radius only");
            Assert.AreEqual(0, AgentRayTargeting.Pick(Vector3.zero, Off(Vector3.forward, 3f), centers, ok, 0.03f, 4.5f, 0.2f), "3 deg off is inside the 4.5 deg cone");
            Assert.AreEqual(-1, AgentRayTargeting.Pick(Vector3.zero, Off(Vector3.forward, 7f), centers, ok, 0.03f, 4.5f, 0.2f), "7 deg off is not");
        }

        [Test]
        public void Pick_SkipsIneligible_Behind_AndPrefersTheClosestMiss()
        {
            var centers = new List<Vector3> { new Vector3(0.10f, 0, 2f), new Vector3(0.02f, 0, 2f), new Vector3(0, 0, -2f), new Vector3(0, 0, 0.1f) };
            var ok = new List<bool> { true, true, true, true };
            Assert.AreEqual(1, AgentRayTargeting.Pick(Vector3.zero, Vector3.forward, centers, ok, 0.03f, 4.5f, 0.2f), "smallest normalised miss wins; behind and too close are ignored");
            ok[1] = false;
            Assert.AreEqual(0, AgentRayTargeting.Pick(Vector3.zero, Vector3.forward, centers, ok, 0.03f, 4.5f, 0.2f), "an agent away from home is never a target");
            Assert.AreEqual(-1, AgentRayTargeting.Pick(Vector3.zero, Vector3.zero, centers, ok, 0.03f, 4.5f, 0.2f), "no direction, no pick");
        }

        // ── hover lock ──

        [Test]
        public void Lock_HoldsThroughAShortGap_DropsAfterTheGrace()
        {
            var l = new AgentTargetLock { graceSeconds = 0.35f };
            l.Update(1, 2, 0f);
            l.Update(1, -1, 0.2f);
            Assert.AreEqual(2, l.Locked(1), "a 0.2 s gap keeps the lock");
            l.Update(1, -1, 0.4f);
            Assert.AreEqual(-1, l.Locked(1), "past the grace it is dropped");
            Assert.AreEqual(-1, l.Locked(0), "per hand");
        }

        [Test]
        public void Lock_ThePinchTakesWhatItBeganOn_EvenWhenTheRayDipsOntoAnother()
        {
            var l = new AgentTargetLock { graceSeconds = 0.35f };
            l.Update(1, 0, 0f);
            l.NoteBegan(1);                        // the fingers close on agent 0
            l.Update(1, 3, 0.10f);                 // the ray dips onto agent 3 while the pinch debounces
            Assert.AreEqual(3, l.Locked(1), "the highlight follows the ray");
            Assert.AreEqual(0, l.PinchTarget(1), "the grab takes the pinch-start target");
            l.ClearPinch(1);
            Assert.AreEqual(3, l.PinchTarget(1));
            l.NoteBegan(1); l.Forget(3);
            Assert.AreEqual(-1, l.PinchTarget(1), "a target that left home is forgotten");
        }

        // ── drop rule ──

        [Test]
        public void Drop_NearPerch_OrPulledTowardTheUser_Perches_TowardTheWall_GoesHome()
        {
            var s = new VisitDropSettings { perchSnapRadius = 0.30f, nearUserRadius = 0.55f };
            Vector3 eye = new Vector3(0, 1.2f, 0), perch = new Vector3(0.2f, 0.75f, 0.25f), home = new Vector3(0, 1.4f, 2.0f);
            Assert.AreEqual(VisitDrop.Perch, VisitDropRule.Decide(perch + new Vector3(0.2f, 0.1f, 0f), perch, eye, home, s), "0.22 m from the perch");
            Assert.AreEqual(VisitDrop.Perch, VisitDropRule.Decide(new Vector3(-0.3f, 1.1f, 0.4f), perch, eye, home, s), "pulled in front of the user");
            Assert.AreEqual(VisitDrop.Home, VisitDropRule.Decide(new Vector3(0, 1.3f, 1.4f), perch, eye, home, s), "let go toward the wall");
            Vector3 stretched = eye + (home - eye).normalized * 0.72f;                // arm out (~0.58 m) + the 0.14 m carry offset
            Assert.AreEqual(VisitDrop.Home, VisitDropRule.Decide(stretched, perch, eye, home, s), "arm stretched toward the wall is a put-back");
            Vector3 chest = eye + (home - eye).normalized * 0.35f + Vector3.down * 0.2f;
            Assert.AreEqual(VisitDrop.Perch, VisitDropRule.Decide(chest, perch, eye, home, s), "hand pulled in to the chest is a landing");
            Assert.AreEqual(VisitDrop.Home, VisitDropRule.Decide(perch + new Vector3(0.2f, 0.1f, 0f), perch, eye, home, null), "no settings: the old 0.13 m pad rule");
        }

        [Test]
        public void BlockWords_EveryBusyReason_IsSaid()
        {
            foreach (HandBlock b in new[] { HandBlock.Holding, HandBlock.CloseInteraction, HandBlock.Ui, HandBlock.NotFresh, HandBlock.TrackingLost, HandBlock.Preempted })
                Assert.IsFalse(string.IsNullOrEmpty(SelectBlockWords.For(b)), b.ToString());
            Assert.IsNull(SelectBlockWords.For(HandBlock.None));
            Assert.IsNull(SelectBlockWords.For(HandBlock.NotTaken), "an untaken pinch off any target says nothing");
        }

        // ── mute ledger ──

        sealed class Thing { public bool on = true; public bool busy; }

        [Test]
        public void Ledger_GivesBackExactlyWhatItMuted()
        {
            var a = new Thing(); var b = new Thing { on = false }; var c = new Thing { busy = true };
            var l = new EnableLedger<Thing>();
            System.Func<Thing, bool> isOn = t => t.on, isBusy = t => t.busy;
            System.Action<Thing, bool> set = (t, v) => t.on = v;
            Assert.IsTrue(l.Mute(a, isOn, isBusy, set));
            Assert.IsFalse(l.Mute(a, isOn, isBusy, set), "twice is once");
            Assert.IsFalse(l.Mute(b, isOn, isBusy, set), "already off: not ours");
            Assert.IsFalse(l.Mute(c, isOn, isBusy, set), "held: never pulled from under a hand");
            Assert.IsFalse(a.on); Assert.IsTrue(c.on);
            Assert.AreEqual(1, l.RestoreAll(set));
            Assert.IsTrue(a.on); Assert.IsFalse(b.on, "what was off before stays off");
            Assert.AreEqual(0, l.Count);
        }

        [Test]
        public void Ledger_SomethingSwitchedBackOnByAnotherScript_IsMutedAgain_AndRestoredOnce()
        {
            var a = new Thing();
            var l = new EnableLedger<Thing>();
            System.Action<Thing, bool> set = (t, v) => t.on = v;
            l.Mute(a, t => t.on, null, set);
            a.on = true;                                             // e.g. ReRegisterInteractable's enable cycle
            Assert.IsFalse(l.Mute(a, t => t.on, null, set));
            Assert.IsFalse(a.on, "off again");
            Assert.AreEqual(1, l.Count);
            Assert.AreEqual(1, l.RestoreAll(set));
            Assert.IsTrue(a.on);
        }

        [Test]
        public void Ledger_PartialRestore()
        {
            var a = new Thing(); var b = new Thing();
            var l = new EnableLedger<Thing>();
            l.Mute(a, t => t.on, null, (t, v) => t.on = v);
            l.Mute(b, t => t.on, null, (t, v) => t.on = v);
            Assert.AreEqual(1, l.Restore(t => t == a, (t, v) => t.on = v));
            Assert.IsTrue(a.on); Assert.IsFalse(b.on); Assert.AreEqual(1, l.Count);
        }

        // ── visit: pick up off the perch, put back ──

        static AgentVisitFlow Perched(out AgentPresentationRegistry reg)
        {
            reg = new AgentPresentationRegistry();
            var f = new AgentVisitFlow(reg);
            Assert.IsTrue(f.Arm());
            Assert.IsTrue(f.Grab("developer"));
            f.Release(VisitDrop.Perch);
            Assert.AreEqual(VisitState.Perched, f.State);
            return f;
        }

        static void Tick(AgentVisitFlow f, float s) { for (float t = 0; t < s; t += 0.02f) f.Tick(0.02f); }

        [Test]
        public void Regrab_ThenLetGoTowardTheWall_PutsItBack_ExactlyOnePresentationThroughout()
        {
            AgentPresentationRegistry reg;
            var f = Perched(out reg);
            Assert.IsTrue(f.Regrab());
            Assert.AreEqual(VisitState.Carrying, f.State);
            Assert.IsTrue(f.IsRegrab);
            Assert.AreEqual(AgentPresentation.CarriedProxy, reg.Of("developer"));
            Assert.AreEqual(1, reg.AwayCount);
            f.Release(VisitDrop.Home);
            Assert.AreEqual(VisitState.Returning, f.State);
            Assert.AreEqual(VisitEndReason.UserReturn, f.LastEnd, "a put-back is the user's return, not an invalid drop");
            Assert.IsFalse(f.IsRegrab);
            Tick(f, 1.0f);
            Assert.AreEqual(VisitState.Off, f.State);
            Assert.AreEqual(AgentPresentation.Home, reg.Of("developer"));
            Assert.AreEqual(0, reg.AwayCount);
            Assert.IsTrue(f.Arm(), "Select Agents works again after the put-back");
        }

        [Test]
        public void Regrab_ThenLetGoOverTheDesk_StaysVisiting()
        {
            AgentPresentationRegistry reg;
            var f = Perched(out reg);
            f.Regrab();
            f.Release(VisitDrop.Perch);
            Assert.AreEqual(VisitState.Perched, f.State);
            Assert.AreEqual(AgentPresentation.DeskVisit, reg.Of("developer"));
            Assert.IsFalse(f.IsRegrab);
        }

        [Test]
        public void Regrab_HandLost_GoesBackToThePerch_NeverAPlacementElsewhere()
        {
            AgentPresentationRegistry reg;
            var f = Perched(out reg);
            f.Regrab();
            f.CancelCarry(VisitEndReason.TrackingLost);
            Assert.AreEqual(VisitState.Perched, f.State, "its previous state, not wherever the hand was");
            Assert.AreEqual(AgentPresentation.DeskVisit, reg.Of("developer"));
            StringAssert.Contains("stays on your desk", f.Message);
        }

        [Test]
        public void FreshCarry_LetGoTowardTheWall_GoesHome_AsBefore()
        {
            var reg = new AgentPresentationRegistry();
            var f = new AgentVisitFlow(reg);
            f.Arm(); f.Grab("tester");
            f.Release(VisitDrop.Home);
            Assert.AreEqual(VisitEndReason.InvalidDrop, f.LastEnd);
            Tick(f, 1.0f);
            Assert.AreEqual(0, reg.AwayCount);
        }

        [Test]
        public void Regrab_OnlyFromThePerch()
        {
            var reg = new AgentPresentationRegistry();
            var f = new AgentVisitFlow(reg);
            Assert.IsFalse(f.Regrab(), "nothing perched");
            f.Arm();
            Assert.IsFalse(f.Regrab(), "armed is not perched");
            f.Grab("developer");
            Assert.IsFalse(f.Regrab(), "already carried");
        }

        [Test]
        public void ReturnKey_DuringARegrab_SendsItHome()
        {
            AgentPresentationRegistry reg;
            var f = Perched(out reg);
            f.Regrab();
            f.Return(VisitEndReason.UserReturn);
            Assert.AreEqual(VisitState.Returning, f.State);
            Tick(f, 1.0f);
            Assert.AreEqual(AgentPresentation.Home, reg.Of("developer"));
        }

        // ── arbiter read-outs and precedence ──

        static AimHandSample Open() { return AimHandSample.Ray(Vector3.zero, Vector3.forward, false); }
        static AimHandSample Pinch() { return AimHandSample.Ray(Vector3.zero, Vector3.forward, true); }

        static void Run(HandOwnershipArbiter a, float s, AimHandSample right)
        {
            int n = Mathf.CeilToInt(s / Dt);
            for (int i = 0; i < n; i++) a.Tick(Dt, AimHandSample.None, right);
        }

        [Test]
        public void Arbiter_IsPending_ThenReady_OnAClearHand()
        {
            var a = new HandOwnershipArbiter();
            Run(a, 0.5f, Open());
            a.Tick(Dt, AimHandSample.None, Pinch());
            Assert.IsTrue(a.Began(1));
            Assert.IsTrue(a.IsPending(1));
            bool ready = false;
            for (int i = 0; i < 20 && !ready; i++) { a.Tick(Dt, AimHandSample.None, Pinch()); ready = a.Signal(1) == HandGateSignal.Ready; }
            Assert.IsTrue(ready);
            Assert.IsTrue(a.TryClaim(1, HandClaim.AgentSelect));
            Assert.IsFalse(a.IsPending(1)); Assert.IsFalse(a.IsSpent(1)); Assert.IsTrue(a.IsClaimed(1));
        }

        [Test]
        public void Arbiter_APinchStartedWhileOverAGrabbable_IsSpent_WithAReasonToSay()
        {
            var a = new HandOwnershipArbiter();
            Run(a, 0.5f, Open().With(false, true, false));          // a distance grab hovers something
            a.Tick(Dt, AimHandSample.None, Pinch());                 // the hover just ended, the pinch closes
            Assert.IsTrue(a.Began(1));
            Assert.IsTrue(a.IsSpent(1), "the pinch belongs to the grabbable, not to Select Agents");
            Assert.AreEqual(HandBlock.CloseInteraction, a.LastBlock(1));
            Assert.IsNotNull(SelectBlockWords.For(a.LastBlock(1)));
        }

        [Test]
        public void Arbiter_HoldingSomething_RefusesTheAgentPinch_Visibly()
        {
            var a = new HandOwnershipArbiter();
            Run(a, 0.5f, Open());
            a.externallyHeld = true;                                  // a paper's transition hold
            Run(a, 0.3f, Pinch());
            Assert.IsTrue(a.IsSpent(1));
            Assert.AreEqual(HandBlock.Holding, a.LastBlock(1));
            StringAssert.Contains("holding", SelectBlockWords.For(a.LastBlock(1)));
        }

        [Test]
        public void ClaimRegistry_AgentSelectOutranksPlacementAndSummon_NoCrossOwnerSteal()
        {
            var reg = new HandClaimRegistry();
            object wall = new object(), floor = new object(), other = new object();
            Assert.IsTrue(reg.TryClaim(1, HandClaim.Placement, floor));
            Assert.IsTrue(reg.TryClaim(1, HandClaim.AgentSelect, wall), "AgentSelect > Placement");
            Assert.IsFalse(reg.TryClaim(1, HandClaim.Summon, floor), "Summon cannot take it back");
            Assert.IsFalse(reg.TryClaim(1, HandClaim.AgentSelect, other), "same kind, different owner: refused");
            Assert.AreEqual(HandClaim.None, reg.Owner(0), "the other hand is untouched");
        }
    }
}
#endif
