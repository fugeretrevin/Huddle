// =============================================================================
// MuseDesk · EditMode tests · hand ownership and gesture conflicts (rc6)
// =============================================================================
//
// Pure logic only: HandOwnershipArbiter + HandClaimRegistry, the ceiling pull through
// CeilingPullSession, the floor drag through FloorAimSession, the console's Place here rule
// (ModeConsoleLogic) and the guide adopting a preview (PlacementGuideLogic). Hand samples carry what
// the Interaction SDK interactors would report (holding / nearHover / uiHover); the runtime reads
// them with HandInteractionProbe, which these tests do NOT exercise (no rig, no real hands).
// Every scenario that could move the office records the poses written to the root (SpatialRig).
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;

namespace MuseDesk.Tests.EditMode
{
    static class Hands
    {
        public const float Dt = 1f / 72f;

        public static AimHandSample Open() { return AimHandSample.Ray(Vector3.zero, Vector3.forward, false); }
        public static AimHandSample Pinch() { return AimHandSample.Ray(Vector3.zero, Vector3.forward, true); }

        /// <summary>Tick the arbiter for <paramref name="seconds"/> with the right hand as given (left untracked).
        /// Returns true if the right hand's signal was Ready on any tick (and claims it when asked).</summary>
        public static bool Run(HandOwnershipArbiter a, float seconds, AimHandSample right, HandClaim claim = HandClaim.None)
        {
            bool ready = false;
            int n = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < n; i++)
            {
                a.Tick(Dt, AimHandSample.None, right);
                if (a.Signal(1) == HandGateSignal.Ready)
                {
                    ready = true;
                    if (claim != HandClaim.None) a.TryClaim(1, claim);
                }
            }
            return ready;
        }

        /// <summary>The signals the right hand gave over <paramref name="seconds"/>.</summary>
        public static List<HandGateSignal> Signals(HandOwnershipArbiter a, float seconds, AimHandSample right)
        {
            var list = new List<HandGateSignal>();
            int n = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < n; i++) { a.Tick(Dt, AimHandSample.None, right); list.Add(a.Signal(1)); }
            return list;
        }
    }

    [Category("MuseDesk")]
    public class HandOwnershipTests
    {
        [Test]
        public void AFreshPinch_HeldTheMinimumTime_OnAClearHand_IsReady()
        {
            var a = new HandOwnershipArbiter();
            Assert.IsFalse(Hands.Run(a, 0.5f, Hands.Open()));
            Assert.AreEqual(HandOwner.Ambient, a.Owner(1));
            Assert.IsFalse(Hands.Run(a, 0.12f, Hands.Pinch()), "0.12 s is under the 0.15 s debounce");
            Assert.IsTrue(Hands.Run(a, 0.1f, Hands.Pinch(), HandClaim.Placement));
            Assert.AreEqual(HandOwner.Placement, a.Owner(1));
            Assert.AreEqual(HandGateSignal.Holding, Hands.Signals(a, 0.2f, Hands.Pinch())[0]);
        }

        [Test]
        public void ThePinchIsHeldThroughADropout_AndReleasedOnlyWhenSeenOpen()
        {
            var a = new HandOwnershipArbiter();
            Hands.Run(a, 0.5f, Hands.Open());
            Assert.IsTrue(Hands.Run(a, 0.3f, Hands.Pinch(), HandClaim.Placement));
            // Two frames of an open reading (a pinch dropout): still holding.
            var s = Hands.Signals(a, 2 * Hands.Dt, Hands.Open());
            CollectionAssert.DoesNotContain(s, HandGateSignal.Released);
            Assert.AreEqual(HandGateSignal.Holding, Hands.Signals(a, Hands.Dt, Hands.Pinch())[0]);
            // Seen open for the release debounce: released, once.
            s = Hands.Signals(a, 0.3f, Hands.Open());
            Assert.AreEqual(1, s.FindAll(x => x == HandGateSignal.Released).Count);
            Assert.AreEqual(HandClaim.None, a.Registry.Owner(1), "the claim is given back");
        }

        [Test]
        public void PinchWhileHoldingAPaperStampOrCard_NeverArms()
        {
            // This hand holds something.
            var a = new HandOwnershipArbiter();
            Hands.Run(a, 0.5f, Hands.Open().With(true, false, false));
            Assert.AreEqual(HandOwner.Holding, a.Owner(1));
            Assert.IsFalse(Hands.Run(a, 1f, Hands.Pinch().With(true, false, false)));
            Assert.AreEqual(HandBlock.Holding, a.LastBlock(1));

            // The OTHER hand holds something: the right hand's pinch still never arms.
            var b = new HandOwnershipArbiter();
            for (int i = 0; i < 40; i++) b.Tick(Hands.Dt, Hands.Open().With(true, false, false), Hands.Open());
            bool ready = false;
            for (int i = 0; i < 72; i++) { b.Tick(Hands.Dt, Hands.Open().With(true, false, false), Hands.Pinch()); ready |= b.Signal(1) == HandGateSignal.Ready; }
            Assert.IsFalse(ready);
            Assert.AreEqual(HandBlock.Holding, b.LastBlock(1));

            // A paper's transition hold (no grab interactor involved).
            var c = new HandOwnershipArbiter { externallyHeld = true };
            Hands.Run(c, 0.5f, Hands.Open());
            Assert.IsFalse(Hands.Run(c, 1f, Hands.Pinch()));
            Assert.AreEqual(HandBlock.Holding, c.LastBlock(1));
        }

        [Test]
        public void PinchWhileHoveringACloseObject_NeverArms_EvenWhenTheHoverIsLate()
        {
            // Hovering a paper when the pinch begins.
            var a = new HandOwnershipArbiter();
            Hands.Run(a, 0.5f, Hands.Open().With(false, true, false));
            Assert.AreEqual(HandOwner.CloseInteraction, a.Owner(1));
            Assert.IsFalse(Hands.Run(a, 1f, Hands.Pinch().With(false, true, false)));
            Assert.AreEqual(HandBlock.CloseInteraction, a.LastBlock(1));

            // The SDK reports the hover only 3 frames after the pinch began (the race the arbiter exists for).
            var b = new HandOwnershipArbiter();
            Hands.Run(b, 0.5f, Hands.Open());
            Assert.IsFalse(Hands.Run(b, 3 * Hands.Dt, Hands.Pinch()));
            Assert.IsFalse(Hands.Run(b, 1f, Hands.Pinch().With(false, true, false)));
            Assert.AreEqual(HandBlock.CloseInteraction, b.LastBlock(1));
            // ... and the hover going away again does not revive that pinch.
            Assert.IsFalse(Hands.Run(b, 1f, Hands.Pinch()));

            // The hand left a paper 0.1 s before pinching: inside the clear window, spent.
            var c = new HandOwnershipArbiter();
            Hands.Run(c, 0.5f, Hands.Open().With(false, true, false));
            Hands.Run(c, 0.1f, Hands.Open());
            Assert.IsFalse(Hands.Run(c, 1f, Hands.Pinch()));
            Assert.AreEqual(HandBlock.CloseInteraction, c.LastBlock(1));
            // Open, wait past the window, pinch again: now it is the user's.
            Hands.Run(c, 0.4f, Hands.Open());
            Assert.IsTrue(Hands.Run(c, 0.3f, Hands.Pinch()));
        }

        [Test]
        public void APokeOnAUiKey_ThenAPinch_DoesNotArm()
        {
            var a = new HandOwnershipArbiter();
            Hands.Run(a, 0.5f, Hands.Open());
            Hands.Run(a, 0.2f, Hands.Open().With(false, false, true));          // the finger pokes a key
            Hands.Run(a, 0.05f, Hands.Open());                                  // and leaves it
            Assert.IsFalse(Hands.Run(a, 1f, Hands.Pinch()), "the pinch began inside the clear window");
            Assert.AreEqual(HandBlock.Ui, a.LastBlock(1));

            // A ray over a UI control while pinching: spent as well.
            var b = new HandOwnershipArbiter();
            Hands.Run(b, 0.5f, Hands.Open());
            Assert.IsFalse(Hands.Run(b, 1f, Hands.Pinch().With(false, false, true)));
            Assert.AreEqual(HandBlock.Ui, b.LastBlock(1));
        }

        [Test]
        public void TrackingLost_FreezesThenCancels_AndIsNeverARelease()
        {
            var a = new HandOwnershipArbiter();
            Hands.Run(a, 0.5f, Hands.Open());
            Assert.IsTrue(Hands.Run(a, 0.3f, Hands.Pinch(), HandClaim.Summon));
            var s = Hands.Signals(a, 0.15f, AimHandSample.None);
            Assert.IsTrue(s.TrueForAll(x => x == HandGateSignal.Frozen), "frozen inside the grace time");
            s = Hands.Signals(a, 0.2f, AimHandSample.None);
            Assert.AreEqual(1, s.FindAll(x => x == HandGateSignal.Cancelled).Count);
            CollectionAssert.DoesNotContain(s, HandGateSignal.Released);
            Assert.AreEqual(HandBlock.TrackingLost, a.LastBlock(1));
            Assert.AreEqual(HandClaim.None, a.Registry.Owner(1));

            // Lost briefly and back OPEN: cancelled, not released.
            var b = new HandOwnershipArbiter();
            Hands.Run(b, 0.5f, Hands.Open());
            Assert.IsTrue(Hands.Run(b, 0.3f, Hands.Pinch(), HandClaim.Placement));
            Hands.Signals(b, 0.1f, AimHandSample.None);
            s = Hands.Signals(b, 0.3f, Hands.Open());
            Assert.AreEqual(HandGateSignal.Cancelled, s[0]);
            CollectionAssert.DoesNotContain(s, HandGateSignal.Released);

            // Lost briefly and back PINCHED: carries on.
            var c = new HandOwnershipArbiter();
            Hands.Run(c, 0.5f, Hands.Open());
            Assert.IsTrue(Hands.Run(c, 0.3f, Hands.Pinch(), HandClaim.Placement));
            Hands.Signals(c, 0.1f, AimHandSample.None);
            Assert.AreEqual(HandGateSignal.Holding, Hands.Signals(c, Hands.Dt, Hands.Pinch())[0]);
        }

        [Test]
        public void AfterAnyCancel_AFreshPinchIsRequired()
        {
            var a = new HandOwnershipArbiter();
            Hands.Run(a, 0.5f, Hands.Open());
            Assert.IsTrue(Hands.Run(a, 0.3f, Hands.Pinch(), HandClaim.Placement));
            // Something is picked up mid-gesture (the other hand grabs a paper): cancelled.
            a.Tick(Hands.Dt, Hands.Open().With(true, false, false), Hands.Pinch());
            Assert.AreEqual(HandGateSignal.Cancelled, a.Signal(1));
            Assert.AreEqual(HandBlock.Holding, a.LastBlock(1));
            // Nothing held any more, the pinch still closed: not ready, however long.
            Assert.IsFalse(Hands.Run(a, 2f, Hands.Pinch()));
            // A flicker of open (one frame) is not "open": still nothing.
            Hands.Run(a, Hands.Dt, Hands.Open());
            Assert.IsFalse(Hands.Run(a, 1f, Hands.Pinch()));
            // Open properly, then pinch: ready again.
            Hands.Run(a, 0.4f, Hands.Open());
            Assert.IsTrue(Hands.Run(a, 0.3f, Hands.Pinch()));

            // A pinch already closed when the arbiter starts (the hand that pressed a key) is not fresh.
            var b = new HandOwnershipArbiter();
            Assert.IsFalse(Hands.Run(b, 2f, Hands.Pinch()));
            Assert.AreEqual(HandBlock.NotFresh, b.LastBlock(1));
        }

        [Test]
        public void OneOwnerPerHand_PlacementOutranksSummon()
        {
            var registry = new HandClaimRegistry();
            var floor = new HandOwnershipArbiter(registry);
            var pull = new HandOwnershipArbiter(registry);
            for (int i = 0; i < 36; i++) { floor.Tick(Hands.Dt, AimHandSample.None, Hands.Open()); pull.Tick(Hands.Dt, AimHandSample.None, Hands.Open()); }

            // Both see the same pinch ready; the summon takes it first, the placement then preempts it.
            bool pulled = false, placed = false;
            for (int i = 0; i < 25 && !placed; i++)
            {
                pull.Tick(Hands.Dt, AimHandSample.None, Hands.Pinch());
                floor.Tick(Hands.Dt, AimHandSample.None, Hands.Pinch());
                if (!pulled && pull.Signal(1) == HandGateSignal.Ready) pulled = pull.TryClaim(1, HandClaim.Summon);
                if (floor.Signal(1) == HandGateSignal.Ready) placed = floor.TryClaim(1, HandClaim.Placement);
            }
            Assert.IsTrue(pulled);
            Assert.IsTrue(placed);
            Assert.AreEqual(HandClaim.Placement, registry.Owner(1));
            pull.Tick(Hands.Dt, AimHandSample.None, Hands.Pinch());
            Assert.AreEqual(HandGateSignal.Cancelled, pull.Signal(1));
            Assert.AreEqual(HandBlock.Preempted, pull.LastBlock(1));

            // The other way round: a summon cannot take a hand the placement holds.
            var r2 = new HandClaimRegistry();
            Assert.IsTrue(r2.TryClaim(0, HandClaim.Placement));
            Assert.IsFalse(r2.TryClaim(0, HandClaim.Summon));
            Assert.IsTrue(r2.TryClaim(1, HandClaim.Summon), "the other hand is free");
        }

        [Test]
        public void FarClaimPrecedence_AgentSelect_Then_Placement_Then_Summon()
        {
            var r = new HandClaimRegistry();
            Assert.IsTrue(r.TryClaim(0, HandClaim.Summon));
            Assert.IsTrue(r.TryClaim(0, HandClaim.Placement), "placement outranks summon");
            Assert.IsFalse(r.TryClaim(0, HandClaim.Summon));
            Assert.IsTrue(r.TryClaim(0, HandClaim.AgentSelect), "agent selection outranks placement");
            Assert.IsFalse(r.TryClaim(0, HandClaim.Placement));
            Assert.IsFalse(r.TryClaim(0, HandClaim.Summon));
            Assert.AreEqual(HandClaim.AgentSelect, r.Owner(0));
        }

        [Test]
        public void Registry_IsOwnerAware_TwoArbitersSameKindCannotBothHoldAHand()
        {
            var registry = new HandClaimRegistry();
            var a = new HandOwnershipArbiter(registry);
            var b = new HandOwnershipArbiter(registry);
            for (int i = 0; i < 36; i++) { a.Tick(Hands.Dt, AimHandSample.None, Hands.Open()); b.Tick(Hands.Dt, AimHandSample.None, Hands.Open()); }
            bool aTook = false, bTook = false;
            for (int i = 0; i < 25 && !(aTook && i > 12); i++)
            {
                a.Tick(Hands.Dt, AimHandSample.None, Hands.Pinch());
                b.Tick(Hands.Dt, AimHandSample.None, Hands.Pinch());
                if (a.Signal(1) == HandGateSignal.Ready) aTook = a.TryClaim(1, HandClaim.Placement);
                if (b.Signal(1) == HandGateSignal.Ready) bTook = b.TryClaim(1, HandClaim.Placement);
            }
            Assert.IsTrue(aTook);
            Assert.IsFalse(bTook, "a second arbiter with the same claim kind is refused");
            Assert.AreEqual(HandBlock.Preempted, b.LastBlock(1));

            // Direct registry use: same kind, different owner refused; same owner is idempotent.
            var r = new HandClaimRegistry();
            var o1 = new object(); var o2 = new object();
            Assert.IsTrue(r.TryClaim(0, HandClaim.Placement, o1));
            Assert.IsTrue(r.TryClaim(0, HandClaim.Placement, o1));
            Assert.IsFalse(r.TryClaim(0, HandClaim.Placement, o2));
            r.Release(0, HandClaim.Placement, o2);
            Assert.AreEqual(HandClaim.Placement, r.Owner(0), "a non-owner cannot release it");
            r.Release(0, HandClaim.Placement, o1);
            Assert.AreEqual(HandClaim.None, r.Owner(0));
        }
    }

    // ───────────────────────── the ceiling pull with ownership ─────────────────────────

    [Category("MuseDesk")]
    public class CeilingPullOwnershipTests
    {
        const float Dt = 1f / 72f;
        static readonly Vector3 Hand0 = new Vector3(0.1f, 1.4f, 0.3f);

        static AimHandSample At(Vector3 p, bool pinch) { return AimHandSample.Ray(p, Vector3.up, pinch); }

        /// <summary>Run with the right hand at <paramref name="p"/>; targeted / keep apply to the right hand.</summary>
        static void Run(CeilingPullSession s, float seconds, AimHandSample right, bool targeted, bool keep, bool allowed = true,
                        CeilingPullDirection dir = CeilingPullDirection.Down)
        {
            int n = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < n; i++) s.Tick(Dt, AimHandSample.None, right, false, targeted, false, keep, allowed, dir);
        }

        /// <summary>Pull down by <paramref name="dy"/> over <paramref name="seconds"/>, keeping the target as given.</summary>
        static void Pull(CeilingPullSession s, float dy, float seconds, bool keep)
        {
            int n = Mathf.CeilToInt(seconds / Dt);
            for (int i = 1; i <= n; i++) s.Tick(Dt, AimHandSample.None, At(Hand0 + Vector3.down * dy * i / n, true), false, false, false, keep, true, CeilingPullDirection.Down);
        }

        static CeilingPullSession Armed()
        {
            var s = new CeilingPullSession();
            Run(s, 0.5f, At(Hand0, false), true, true);
            Run(s, 0.25f, At(Hand0, true), true, true);
            Assert.AreEqual(CeilingPullState.Armed, s.Machine(1).State);
            return s;
        }

        [Test]
        public void AFullPull_OnTheTarget_Fires()
        {
            var s = Armed();
            Pull(s, 0.14f, 0.5f, true);
            Assert.AreEqual(CeilingPullState.Pulling, s.Machine(1).State);
            Run(s, 0.3f, At(Hand0 + Vector3.down * 0.14f, false), false, true);
            Assert.AreEqual(1, s.Machine(1).Fires);
        }

        [Test]
        public void APullWithNoTarget_NeverArms()
        {
            var s = new CeilingPullSession();
            Run(s, 0.5f, At(Hand0, false), false, false);
            Run(s, 0.4f, At(Hand0, true), false, false);
            Assert.AreEqual(CeilingPullState.Idle, s.Machine(1).State);
            Pull(s, 0.14f, 0.5f, false);
            Run(s, 0.3f, At(Hand0, false), false, false);
            Assert.AreEqual(0, s.Machine(1).Fires);

            // A pinch that began off the target and is swept onto it does not arm either.
            Run(s, 1f, At(Hand0, false), false, false);
            Run(s, 0.05f, At(Hand0, true), false, false);
            Run(s, 0.4f, At(Hand0, true), true, true);
            Assert.AreEqual(CeilingPullState.Idle, s.Machine(1).State);
        }

        [Test]
        public void TheTargetLostMidPull_Cancels_AndNothingFires()
        {
            var s = Armed();
            Pull(s, 0.06f, 0.2f, true);
            Assert.IsTrue(s.Machine(1).Active);
            Pull(s, 0.14f, 0.4f, false);                         // the pod leaves the ray and the gaze for 0.4 s
            Assert.AreEqual(CeilingPullCancel.TargetLost, s.Machine(1).LastCancel);
            Run(s, 0.3f, At(Hand0 + Vector3.down * 0.14f, false), false, false);
            Assert.AreEqual(0, s.Machine(1).Fires);

            // A short gap (under 0.25 s) is tolerated.
            var t = Armed();
            Pull(t, 0.05f, 0.15f, true);
            Pull(t, 0.08f, 0.1f, false);
            Pull(t, 0.14f, 0.2f, true);
            Assert.IsTrue(t.Machine(1).Active);
        }

        [Test]
        public void TrackingLostMidPull_Cancels_AndIsNeverARelease()
        {
            var s = Armed();
            Pull(s, 0.13f, 0.4f, true);
            Assert.IsTrue(s.Machine(1).Active, "the full distance was reached");
            Run(s, 0.1f, AimHandSample.None, false, false);
            Assert.IsTrue(s.Machine(1).Active, "frozen inside the grace time");
            Run(s, 0.3f, AimHandSample.None, false, false);
            Assert.AreEqual(CeilingPullCancel.Lost, s.Machine(1).LastCancel);
            Assert.AreEqual(0, s.Machine(1).Fires, "a lost hand never fires, even past the distance");

            // Back open after a short gap: a cancel too.
            var t = Armed();
            Pull(t, 0.13f, 0.4f, true);
            Run(t, 0.1f, AimHandSample.None, false, false);
            Run(t, 0.3f, At(Hand0 + Vector3.down * 0.13f, false), false, true);
            Assert.AreEqual(0, t.Machine(1).Fires);
            Assert.AreEqual(CeilingPullCancel.Lost, t.Machine(1).LastCancel);
        }

        [Test]
        public void APinchWhileHoldingAPaper_DoesNotArm_AndSaysSo()
        {
            var s = new CeilingPullSession();
            s.Arbiter.externallyHeld = true;
            Run(s, 0.5f, At(Hand0, false), true, true);
            bool said = false;
            for (int i = 0; i < 36; i++)
            {
                s.Tick(Dt, AimHandSample.None, At(Hand0, true), false, true, false, true, true, CeilingPullDirection.Down);
                said |= s.PinchedAtTargetWhileHeld(1);
            }
            Assert.AreEqual(CeilingPullState.Idle, s.Machine(1).State);
            Assert.IsTrue(said);

            // Picked up mid-pull (a grab interactor of the other hand): cancelled, nothing fires.
            var t = Armed();
            Pull(t, 0.06f, 0.2f, true);
            t.Tick(Dt, AimHandSample.Ray(Vector3.zero, Vector3.forward, false).With(true, false, false), At(Hand0 + Vector3.down * 0.07f, true),
                   false, true, false, true, true, CeilingPullDirection.Down);
            Assert.AreEqual(CeilingPullCancel.Held, t.Machine(1).LastCancel);
            Assert.AreEqual(0, t.Machine(1).Fires);
        }

        [Test]
        public void AfterACancel_TheNextPullNeedsAFreshPinch()
        {
            var s = Armed();
            Pull(s, 0.06f, 0.2f, true);
            Pull(s, 0.06f, 0.5f, false);                         // target lost: cancelled, pinch still closed
            Assert.IsFalse(s.Machine(1).Active);
            Run(s, 1.5f, At(Hand0, true), true, true);           // still pinched, back on the pod, cooldown over
            Assert.AreEqual(CeilingPullState.Idle, s.Machine(1).State);
            Run(s, 0.5f, At(Hand0, false), true, true);
            Run(s, 0.25f, At(Hand0, true), true, true);
            Assert.AreEqual(CeilingPullState.Armed, s.Machine(1).State);
        }
    }

    // ───────────────────────── the floor drag with ownership ─────────────────────────

    [Category("MuseDesk")]
    public class FloorAimOwnershipTests
    {
        static void Run(FloorAimSession s, float seconds, AimHandSample right) { AimRig.Run(s, seconds, right); }

        [Test]
        public void APinchWhileHoldingAPaperStampOrCard_NeverGrabsTheFootprint()
        {
            var rig = AimRig.OpenFloor();
            var session = new FloorAimSession(rig.core);
            Vector3 centre = AimRig.FootprintCentre(rig.core);
            RootPose start = rig.core.PreviewPose;
            Run(session, 0.5f, AimRig.At(centre, false).With(true, false, false));
            Run(session, 1f, AimRig.At(centre, true).With(true, false, false));
            Run(session, 1f, AimRig.At(centre + Vector3.right * 0.5f, true).With(true, false, false));
            Assert.AreEqual(FloorAimState.Aiming, session.State);
            Assert.AreEqual(0, session.Grabs);
            Assert.AreEqual(HandBlock.Holding, session.Arbiter.LastBlock(1));
            SpatialRooms.AssertPose(start, rig.core.PreviewPose);

            // A paper's transition hold, no grab interactor: the same.
            var other = new FloorAimSession(rig.core);
            other.Arbiter.externallyHeld = true;
            Run(other, 0.5f, AimRig.At(centre, false));
            Run(other, 1f, AimRig.At(centre, true));
            Assert.AreEqual(0, other.Grabs);
            rig.Run(2f);
            Assert.AreEqual(0, rig.writes);
        }

        [Test]
        public void APinchWhileHoveringAClose_Object_OrOverAUiKey_NeverGrabsTheFootprint()
        {
            var rig = AimRig.OpenFloor();
            Vector3 centre = AimRig.FootprintCentre(rig.core);
            var near = new FloorAimSession(rig.core);
            Run(near, 0.5f, AimRig.At(centre, false).With(false, true, false));
            Run(near, 1f, AimRig.At(centre, true).With(false, true, false));
            Assert.AreEqual(0, near.Grabs);

            var ui = new FloorAimSession(rig.core);
            Run(ui, 0.5f, AimRig.At(centre, false));
            Run(ui, 0.2f, AimRig.At(centre, false).With(false, false, true));   // a poke on a key
            Run(ui, 1f, AimRig.At(centre, true));                             // then a pinch
            Assert.AreEqual(0, ui.Grabs);
            Assert.AreEqual(HandBlock.Ui, ui.Arbiter.LastBlock(1));
            rig.Run(2f);
            Assert.AreEqual(0, rig.writes);
        }

        [Test]
        public void PickingSomethingUpMidDrag_CallsTheDragOff()
        {
            var rig = AimRig.OpenFloor();
            var session = new FloorAimSession(rig.core);
            RootPose start = rig.core.PreviewPose;
            Vector3 centre = AimRig.FootprintCentre(rig.core);
            Vector3 to = rig.core.GetCandidate(1).position;
            Run(session, 0.5f, AimRig.At(centre, false));
            Run(session, AimRig.Arm, AimRig.At(centre, true));
            Run(session, 1.2f, AimRig.At(centre + (to - start.position), true));
            Assert.AreEqual(FloorAimState.Dragging, session.State);

            session.Tick(AimRig.Dt, AimHandSample.Ray(Vector3.zero, Vector3.forward, false).With(true, false, false),
                         AimRig.At(centre + (to - start.position), true));
            Assert.AreEqual(FloorAimState.Aiming, session.State);
            Assert.AreEqual(1, session.Cancels);
            Assert.IsFalse(rig.core.AimDragging);
            SpatialRooms.AssertPose(start, rig.core.PreviewPose);
            rig.Run(2f);
            Assert.AreEqual(0, rig.writes);
        }

        [Test]
        public void AnAccidentalGesture_NeverRelocatesTheWorkspace()
        {
            // Every pinch shape we know of, on and off the footprint, with and without things in hand:
            // no pose is ever written. Only Place here moves the office.
            var rig = AimRig.OpenFloor();
            var session = new FloorAimSession(rig.core);
            Vector3 centre = AimRig.FootprintCentre(rig.core);
            Vector3 to = rig.core.GetCandidate(1).position;
            Vector3 from = rig.core.PreviewPose.position;
            var shapes = new List<System.Action>
            {
                () => { Run(session, 0.5f, AimRig.At(centre, false)); Run(session, 3f, AimRig.At(centre, true)); Run(session, 0.5f, AimRig.At(centre, false)); },
                () => { Run(session, 0.5f, AimRig.At(centre, false)); Run(session, AimRig.Arm, AimRig.At(centre, true)); Run(session, 1f, AimRig.At(centre + (to - from), true)); Run(session, 0.5f, AimRig.At(centre + (to - from), false)); },
                () => { Run(session, 0.5f, AimRig.At(centre, false)); Run(session, AimRig.Arm, AimRig.At(centre, true)); Run(session, 1f, AimRig.At(centre + (to - from), true)); Run(session, 1f, AimHandSample.None); },
                () => { Run(session, 0.5f, AimRig.At(centre, false).With(true, false, false)); Run(session, 1f, AimRig.At(centre, true).With(true, false, false)); },
                () => { for (int i = 0; i < 20; i++) { Run(session, 0.05f, AimRig.At(centre, i % 2 == 0)); } },
            };
            foreach (var shape in shapes)
            {
                shape();
                rig.Run(1f);
                Assert.AreEqual(0, rig.writes);
                Assert.AreEqual(PlacementMode.Desk, rig.core.Mode);
                Assert.AreEqual(DirectorPhase.Previewing, rig.core.Phase);
            }
            Assert.IsTrue(rig.core.PlaceHere(), rig.core.StatusText);
            rig.Run(6f);
            Assert.AreEqual(PlacementMode.Floor, rig.core.Mode);
        }
    }

    // ───────────────────────── keys and the guide card ─────────────────────────

    [Category("MuseDesk")]
    public class PlaceHereKeyTests
    {
        [Test]
        public void TheConsoleConfirmKey_InAFloorPreview_ShowsOnlyWhenPlaceHereApplies()
        {
            var ready = ModeConsoleLogic.Compute(true, true, PlacementMode.Desk, true, true, false, CeilingPodState.Docked, false, true, true);
            Assert.IsTrue(ready.confirm);
            var notReady = ModeConsoleLogic.Compute(true, true, PlacementMode.Desk, true, true, false, CeilingPodState.Docked, false, true, false);
            Assert.IsFalse(notReady.confirm);
            Assert.IsTrue(notReady.cancel, "Cancel stays");
            Assert.IsTrue(notReady.next, "Other spot stays");
            // A ceiling preview is unchanged.
            var ceiling = ModeConsoleLogic.Compute(true, true, PlacementMode.Desk, true, true, false, CeilingPodState.Docked, false, false, false);
            Assert.IsTrue(ceiling.confirm);
            // The old call (no floor arguments) is unchanged.
            Assert.IsTrue(ModeConsoleLogic.Compute(true, true, PlacementMode.Desk, true, false, false, CeilingPodState.Docked, false).confirm);
        }

        [Test]
        public void TheGuideAdoptsAFloorPreviewItDidNotOpen_OnlyFromRest()
        {
            var g = new PlacementGuideLogic();
            Assert.IsTrue(g.Adopt(GuideTarget.Floor));
            Assert.AreEqual(GuideState.Previewing, g.State);
            Assert.AreEqual(GuideTarget.Floor, g.Target);
            Assert.IsFalse(g.Adopt(GuideTarget.Floor), "already active");
            g.ReportPreviewing(false);
            Assert.AreEqual(GuideState.Done, g.State);
            Assert.IsTrue(g.Adopt(GuideTarget.Floor), "from Done");

            var guiding = new PlacementGuideLogic();
            guiding.Begin(GuideTarget.Ceiling);
            Assert.IsFalse(guiding.Adopt(GuideTarget.Floor), "never takes over a guide in progress");
            Assert.IsFalse(new PlacementGuideLogic().Adopt(GuideTarget.None));
        }
    }

    // ───────────────────────── one command in flight, stable idempotency keys (S2) ─────────────────────────

    [Category("MuseDesk")]
    public class CommandLedgerTests
    {
        [Test]
        public void SecondCommandOnTheSameTarget_IsRefusedWhileOneIsInFlight()
        {
            var l = new CommandLedger();
            Assert.IsTrue(l.TryLock("task-1"));
            Assert.IsFalse(l.TryLock("task-1"));
            Assert.IsTrue(l.TryLock("task-2"), "another card is independent");
            l.Unlock("task-1", "k", true);
            Assert.IsTrue(l.TryLock("task-1"));
        }

        [Test]
        public void KeyIsStableForARetry_AndNewOnlyAfterADefinitiveAnswer()
        {
            var l = new CommandLedger();
            int n = 0;
            string a1 = l.KeyFor("t|pause", () => "k" + (++n));
            Assert.IsTrue(l.TryLock("t"));
            l.Unlock("t", a1, definitive: false);                     // transport failure
            string a2 = l.KeyFor("t|pause", () => "k" + (++n));
            Assert.AreEqual(a1, a2, "the retry reuses the key");
            Assert.IsTrue(l.TryLock("t"));
            l.Unlock("t", a2, definitive: true);                      // the server answered
            string a3 = l.KeyFor("t|pause", () => "k" + (++n));
            Assert.AreNotEqual(a1, a3);
            Assert.AreNotEqual(l.KeyFor("t|resume", () => "k" + (++n)), a3, "a different action has its own key");
        }

        [Test]
        public void DecisionApproveAndReject_ShareOneLock_ButKeepSeparateKeys()
        {
            var l = new CommandLedger();
            int n = 0;
            string ka = l.KeyFor("d1|approve", () => "a" + (++n));
            string kr = l.KeyFor("d1|reject", () => "r" + (++n));
            Assert.AreNotEqual(ka, kr);
            Assert.IsTrue(l.TryLock("d1"));
            Assert.IsFalse(l.TryLock("d1"), "reject cannot be sent while approve is in flight");
        }
    }

    // ───────────────────────── a grab that ends by tracking loss is a cancel (S2) ─────────────────────────

    [Category("MuseDesk")]
    public class GrabTrackingWatchTests
    {
        [Test]
        public void TrackedHandLettingGo_IsARelease()
        {
            var w = new GrabTrackingWatch();
            w.Begin(false, true);
            for (int i = 0; i < 30; i++) w.Sample(false, true, i * 0.014f);
            Assert.AreEqual(GrabEnd.Release, w.Decide(false, true, 0.5f, 0.25f), "left hand was never in play");
        }

        [Test]
        public void HandUntrackedAtTheEnd_IsACancel()
        {
            var w = new GrabTrackingWatch();
            w.Begin(true, true);
            w.Sample(true, true, 0.1f);
            Assert.AreEqual(GrabEnd.Cancel, w.Decide(true, false, 0.2f, 0.25f));
        }

        [Test]
        public void HandLostJustBeforeTheDeselect_IsACancel_EvenIfBackNow()
        {
            var w = new GrabTrackingWatch();
            w.Begin(true, false);
            w.Sample(true, false, 0.10f);
            w.Sample(false, false, 0.20f);                 // lost while held
            Assert.AreEqual(GrabEnd.Cancel, w.Decide(true, false, 0.30f, 0.25f));
            Assert.AreEqual(GrabEnd.Release, w.Decide(true, false, 0.60f, 0.25f), "lost long ago, tracked since: a real release");
        }

        [Test]
        public void HandNotTrackedWhenTheHoldBegan_IsIgnored()
        {
            var w = new GrabTrackingWatch();
            w.Begin(true, false);                          // right hand was never tracked
            w.Sample(true, false, 0.1f);
            Assert.AreEqual(GrabEnd.Release, w.Decide(true, false, 0.2f, 0.25f));
        }
    }
}
#endif
