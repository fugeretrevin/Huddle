// =============================================================================
// MuseDesk · EditMode tests · rc22 Select Agents (owner, worn rc21): "the grabbing should keep hold of them and they should
// drop where you place them", and "if we interact with something else it should get rid of that mode". Pure logic only:
// DeskDropRule (where a released agent lands, rig space), CarryGrip on a real HandOwnershipArbiter (release grace and lost-
// hand grace while carrying), SelectModeExit (which presses switch armed Select off). Not proven here: ISDK hands, visuals,
// the runtime hook points (WallUi / ModeConsole / the per-frame edge), which Rc22SelectProbe covers in Play Mode.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    [Category("MuseDesk")]
    public class Rc22SelectGrabTests
    {
        const float Dt = 1f / 72f;

        // Rig space as the scene has it: +Z toward the user, user's right is -X, desk top y = 0.
        static readonly Vector3 Pad = new Vector3(0.22f, 0.004f, 0.40f);
        static readonly Vector3 Eye = new Vector3(0f, 0.30f, 0.62f);
        static readonly Vector3 Home = new Vector3(0.1f, 0.9f, -0.9f);         // the habitat on the wall in front
        static readonly Vector3 Dock = new Vector3(-0.19f, 0.045f, 0.50f);
        static readonly Vector3 CardAtPad = new Vector3(0.26f, 0.13f, 0.40f);

        static DeskDropResult Drop(Vector3 release, VisitDrop legacy = VisitDrop.Home, DeskDropSettings s = null, Vector3? eye = null, Vector3? home = null)
        {
            return DeskDropRule.Decide(release, Pad, eye ?? Eye, home ?? Home, true, Dock, legacy, s ?? new DeskDropSettings());
        }

        // ── drop where you place them ──

        [Test]
        public void Drop_15cmLeftAndRightOfThePad_LandsThere_OnTheDeskTop()
        {
            DeskDropResult left = Drop(new Vector3(Pad.x + 0.15f, 0.10f, Pad.z));
            Assert.AreEqual(DeskDropKind.Spot, left.kind, left.why);
            Assert.AreEqual(Pad.x + 0.15f, left.spot.x, 1e-4f, "user's left is +X");
            Assert.AreEqual(Pad.y, left.spot.y, 1e-5f, "dropped onto the desk top (the pad's height)");
            Assert.AreEqual(Pad.z, left.spot.z, 1e-4f);
            Assert.IsFalse(left.adjusted);

            DeskDropResult right = Drop(new Vector3(Pad.x - 0.15f, 0.12f, Pad.z - 0.05f));
            Assert.AreEqual(DeskDropKind.Spot, right.kind, right.why);
            Assert.AreEqual(Pad.x - 0.15f, right.spot.x, 1e-4f);
            Assert.AreEqual(Pad.z - 0.05f, right.spot.z, 1e-4f);
        }

        [Test]
        public void Drop_OnOrRightNextToThePad_LandsExactlyOnThePad()
        {
            DeskDropResult r = Drop(Pad + new Vector3(0.02f, 0.02f, -0.01f));
            Assert.AreEqual(DeskDropKind.Pad, r.kind);
            Assert.AreEqual(Pad, r.spot);
        }

        [Test]
        public void Drop_InFrontOfTheChest_IsPulledOutOfTheBodyKeepOut()
        {
            var s = new DeskDropSettings();
            DeskDropResult r = Drop(new Vector3(0f, 0.12f, 0.55f), VisitDrop.Perch, s);
            Assert.AreEqual(DeskDropKind.Spot, r.kind, r.why);
            Assert.IsTrue(r.adjusted, "clamped");
            Assert.LessOrEqual(r.spot.z, Eye.z - s.bodyClearance + 1e-4f, "never nearer the user than the keep-out line");
            Assert.LessOrEqual(r.spot.z, s.nearZ + 1e-4f);

            // Leaning forward moves the keep-out line toward the wall; the pad itself always stays a valid spot.
            Vector3 lean = new Vector3(0f, 0.30f, 0.50f);
            DeskDropResult l = Drop(new Vector3(-0.05f, 0.10f, 0.45f), VisitDrop.Perch, s, lean);
            Assert.AreEqual(DeskDropKind.Spot, l.kind, l.why);
            Assert.LessOrEqual(l.spot.z, Mathf.Max(Pad.z, lean.z - s.bodyClearance) + 1e-4f);
        }

        [Test]
        public void Drop_JustOffTheDeskEdge_IsPulledOntoIt_FurtherIsOutOfRange()
        {
            var s = new DeskDropSettings();
            DeskDropResult edge = Drop(new Vector3(s.halfWidth + 0.06f, 0.08f, 0.35f), VisitDrop.Home, s);
            Assert.AreEqual(DeskDropKind.Spot, edge.kind, edge.why);
            Assert.AreEqual(s.halfWidth, edge.spot.x, 1e-4f);
            Assert.IsTrue(edge.adjusted);

            DeskDropResult far = Drop(new Vector3(s.halfWidth + 0.20f, 0.05f, 0.45f), VisitDrop.Home, s);
            Assert.AreEqual(DeskDropKind.Home, far.kind, "out of range falls back to the previous rule (home)");
            Assert.IsNotNull(far.why);
        }

        [Test]
        public void Drop_OutOfReach_TooHigh_TooLow_OrNearerTheWall_FallsBackToThePreviousRule()
        {
            // Arm stretched toward the wall at about desk height: ~0.8 m from the eye. Previous rule said Home -> Home (a put-back
            // stays a put-back), even though it is over the far part of the desk.
            Vector3 stretched = Eye + (new Vector3(0.1f, 0.25f, -0.9f) - Eye).normalized * 0.8f;
            Assert.AreEqual(DeskDropKind.Home, Drop(stretched, VisitDrop.Home).kind);
            Assert.AreEqual("out of reach", Drop(stretched, VisitDrop.Home).why);
            // Raised toward the habitat (above the desk window): also home.
            Vector3 raised = Eye + (Home - Eye).normalized * 0.8f;
            Assert.AreEqual(DeskDropKind.Home, Drop(raised, VisitDrop.Home).kind);

            // Too high above the desk: the previous rule decides (Perch -> the pad, Home -> home).
            Vector3 high = new Vector3(0.1f, 0.55f, 0.40f);
            Assert.AreEqual(DeskDropKind.Pad, Drop(high, VisitDrop.Perch).kind);
            Assert.AreEqual(Pad, Drop(high, VisitDrop.Perch).spot);
            Assert.AreEqual(DeskDropKind.Home, Drop(high, VisitDrop.Home).kind);

            // Under the desk.
            Assert.AreEqual(DeskDropKind.Home, Drop(new Vector3(0.1f, -0.2f, 0.4f), VisitDrop.Home).kind);

            // A habitat right above the desk: a release nearer the agent's wall home than the user is never a desk spot.
            Vector3 nearHome = new Vector3(0.15f, 0.30f, 0.15f);
            DeskDropResult r = Drop(new Vector3(0.15f, 0.20f, 0.12f), VisitDrop.Home, null, null, nearHome);
            Assert.AreEqual(DeskDropKind.Home, r.kind);
            Assert.AreEqual("nearer the wall than you", r.why);
        }

        [Test]
        public void Drop_OnTheDockKeys_IsMovedOutOfTheDockRing()
        {
            var s = new DeskDropSettings();
            DeskDropResult r = Drop(new Vector3(Dock.x + 0.02f, 0.10f, Dock.z - 0.03f), VisitDrop.Home, s);
            Assert.AreNotEqual(DeskDropKind.Home, r.kind, r.why);
            float d = new Vector2(r.spot.x - Dock.x, r.spot.z - Dock.z).magnitude;
            Assert.GreaterOrEqual(d, s.dockClearRadius - 1e-3f, "clear of the controls");
            Assert.IsTrue(r.adjusted);
            float minX, maxX, minZ, maxZ;
            DeskDropRule.Area(Pad, Eye, s, out minX, out maxX, out minZ, out maxZ);
            Assert.That(r.spot.x, Is.InRange(minX - 1e-4f, maxX + 1e-4f));
            Assert.That(r.spot.z, Is.InRange(minZ - 1e-4f, maxZ + 1e-4f));
        }

        [Test]
        public void Drop_Disabled_IsThePreviousRuleOnly_AndTheAreaAlwaysHoldsThePad()
        {
            var off = new DeskDropSettings { enabled = false };
            Assert.AreEqual(DeskDropKind.Pad, Drop(new Vector3(0.37f, 0.1f, 0.4f), VisitDrop.Perch, off).kind);
            Assert.AreEqual(DeskDropKind.Home, Drop(new Vector3(0.37f, 0.1f, 0.4f), VisitDrop.Home, off).kind);

            // Nonsense settings still keep the designed pad a valid landing.
            var odd = new DeskDropSettings { halfWidth = 0.05f, farZ = 0.45f, nearZ = 0.30f };
            float minX, maxX, minZ, maxZ;
            DeskDropRule.Area(Pad, Eye, odd, out minX, out maxX, out minZ, out maxZ);
            Assert.That(Pad.x, Is.InRange(minX, maxX));
            Assert.That(Pad.z, Is.InRange(minZ, maxZ));
            Assert.AreEqual(DeskDropKind.Pad, Drop(Pad + Vector3.up * 0.05f, VisitDrop.Home, odd).kind);
        }

        [Test]
        public void CardFor_FollowsTheAgent_AndStaysOutboard()
        {
            Assert.Less((DeskDropRule.CardFor(Pad, Pad, CardAtPad) - CardAtPad).magnitude, 1e-5f, "on the pad: the usual card spot");
            Vector3 left = DeskDropRule.CardFor(Pad + new Vector3(0.1f, 0f, -0.05f), Pad, CardAtPad);
            Assert.Less((left - (CardAtPad + new Vector3(0.1f, 0f, -0.05f))).magnitude, 1e-5f, "moves with the agent");
            Vector3 spot = new Vector3(-0.20f, Pad.y, 0.30f);
            Vector3 right = DeskDropRule.CardFor(spot, Pad, CardAtPad);
            Assert.AreEqual(spot.x - (CardAtPad.x - Pad.x), right.x, 1e-5f, "mirrored to the user's right side");
            Assert.AreEqual(spot.y + (CardAtPad.y - Pad.y), right.y, 1e-5f);
        }

        // ── keep hold: CarryGrip on the real arbiter ──

        static AimHandSample Open() { return AimHandSample.Ray(Vector3.zero, Vector3.forward, false); }
        static AimHandSample Pinch() { return AimHandSample.Ray(Vector3.zero, Vector3.forward, true); }

        static HandOwnershipArbiter Claimed()
        {
            var a = new HandOwnershipArbiter();
            for (int i = 0; i < 36; i++) a.Tick(Dt, AimHandSample.None, Open());
            bool ready = false;
            for (int i = 0; i < 30 && !ready; i++) { a.Tick(Dt, AimHandSample.None, Pinch()); ready = a.Signal(1) == HandGateSignal.Ready; }
            Assert.IsTrue(ready, "a clean pinch becomes Ready");
            Assert.IsTrue(a.TryClaim(1, HandClaim.AgentSelect));
            return a;
        }

        /// <summary>Tick n frames of one sample; returns the first non-Holding/Frozen signal seen (or Holding) and when.</summary>
        static HandGateSignal Run(HandOwnershipArbiter a, AimHandSample right, int frames, out float at)
        {
            at = -1f;
            for (int i = 0; i < frames; i++)
            {
                a.Tick(Dt, AimHandSample.None, right);
                HandGateSignal s = a.Signal(1);
                if (s == HandGateSignal.Released || s == HandGateSignal.Cancelled) { at = (i + 1) * Dt; return s; }
            }
            return a.Signal(1);
        }

        [Test]
        public void WithoutTheGrip_AShortPinchDropout_DropsTheAgent()
        {
            var a = Claimed();
            float at;
            Assert.AreEqual(HandGateSignal.Released, Run(a, Open(), 11, out at), "0.15 s open is a release at the arbiter's 0.12 s");
        }

        [Test]
        public void WithTheGrip_A100msPinchDropout_KeepsHold_AndA200msOpenReleases()
        {
            var a = Claimed();
            var grip = new CarryGrip();
            var g = new AgentSelectGrabSettings();
            grip.Apply(a.settings, true, g);
            Assert.IsTrue(grip.Applied);
            Assert.AreEqual(g.carryReleaseSeconds, a.settings.releaseDebounce, 1e-6f);
            Assert.AreEqual(g.carryLostSeconds, a.settings.lostGraceSeconds, 1e-6f);

            float at;
            Assert.AreEqual(HandGateSignal.Holding, Run(a, Open(), 7, out at), "a 0.1 s dropout (7 frames) is not a release");
            Assert.AreEqual(HandGateSignal.Holding, Run(a, Pinch(), 5, out at), "the pinch is back: still carrying");
            Assert.IsTrue(a.IsClaimed(1));

            Assert.AreEqual(HandGateSignal.Released, Run(a, Open(), 30, out at), "opened for real: released");
            Assert.GreaterOrEqual(at, g.carryReleaseSeconds - 1e-4f, "only after the pinch read open for the whole grace");
            Assert.Less(at, g.carryReleaseSeconds + 2f * Dt);
        }

        [Test]
        public void WithTheGrip_ALostHand_IsHeldForASecond_BackPinching_CarriesOn()
        {
            var a = Claimed();
            new CarryGrip().Apply(a.settings, true, new AgentSelectGrabSettings());
            for (int i = 0; i < 36; i++)                                     // 0.5 s untracked
            {
                a.Tick(Dt, AimHandSample.None, AimHandSample.None);
                Assert.AreEqual(HandGateSignal.Frozen, a.Signal(1), "frozen, neither move nor release, at frame " + i);
            }
            a.Tick(Dt, AimHandSample.None, Pinch());
            Assert.AreEqual(HandGateSignal.Holding, a.Signal(1), "back, still pinched: the carry goes on");

            float at;
            Assert.AreEqual(HandGateSignal.Cancelled, Run(a, AimHandSample.None, 90, out at), "lost longer than the grace: cancelled (home)");
            Assert.Greater(at, 1.0f - 1e-4f, "not before the 1 s grace");
            Assert.AreEqual(HandBlock.TrackingLost, a.LastBlock(1));
        }

        [Test]
        public void WithTheGrip_ALostHandBackOpen_IsCancelled_NeverAReleaseOrAPlacement()
        {
            var a = Claimed();
            new CarryGrip().Apply(a.settings, true, new AgentSelectGrabSettings());
            for (int i = 0; i < 20; i++) a.Tick(Dt, AimHandSample.None, AimHandSample.None);
            a.Tick(Dt, AimHandSample.None, Open());
            Assert.AreEqual(HandGateSignal.Cancelled, a.Signal(1));
        }

        [Test]
        public void Grip_GivesTheArbiterItsOwnValuesBack_AndNeverShortensThem()
        {
            var s = new HandOwnershipSettings();
            float r0 = s.releaseDebounce, l0 = s.lostGraceSeconds;
            var grip = new CarryGrip();
            var g = new AgentSelectGrabSettings();
            grip.Apply(s, true, g);
            grip.Apply(s, true, g);                                          // every frame while carrying: idempotent
            grip.Apply(s, false, g);
            Assert.IsFalse(grip.Applied);
            Assert.AreEqual(r0, s.releaseDebounce, 1e-6f);
            Assert.AreEqual(l0, s.lostGraceSeconds, 1e-6f);

            var slow = new HandOwnershipSettings { releaseDebounce = 0.5f, lostGraceSeconds = 2f };
            grip.Apply(slow, true, g);
            Assert.AreEqual(0.5f, slow.releaseDebounce, 1e-6f, "a longer value stays");
            Assert.AreEqual(2f, slow.lostGraceSeconds, 1e-6f);
            grip.Apply(slow, false, g);
            Assert.AreEqual(0.5f, slow.releaseDebounce, 1e-6f);
        }

        // ── Select off when the user does something else ──

        [Test]
        public void SelectExit_AnotherKeyDisarms_TheSelectKeyDoesNot_NorAnythingWhenNotArmed()
        {
            Assert.IsTrue(SelectModeExit.ShouldDisarm(true, "Manager"));
            Assert.IsTrue(SelectModeExit.ShouldDisarm(true, "Key_Move"));
            Assert.IsTrue(SelectModeExit.ShouldDisarm(true, "Key0"), "a card key");
            Assert.IsFalse(SelectModeExit.ShouldDisarm(true, "Select"));
            Assert.IsFalse(SelectModeExit.ShouldDisarm(true, "Key_Select"));
            Assert.IsFalse(SelectModeExit.ShouldDisarm(false, "Manager"));
        }

        [Test]
        public void SelectExit_ThePickItselfAndACarry_AreNeverDisarmed()
        {
            var flow = new AgentVisitFlow(new AgentPresentationRegistry());
            Assert.IsTrue(flow.Arm());
            Assert.IsTrue(flow.Grab("developer"));
            Assert.AreEqual(VisitState.Carrying, flow.State);
            Assert.IsFalse(SelectModeExit.ShouldDisarm(flow.IsArmed, "Manager"), "a key while carrying does not end the carry");
            Assert.IsFalse(SelectModeExit.EdgeTrips(flow.IsArmed, false, true), "nor does a grab starting");
        }

        [Test]
        public void SelectExit_OnlyARisingEdgeWhileArmed_Trips()
        {
            Assert.IsTrue(SelectModeExit.EdgeTrips(true, false, true), "something else began while armed");
            Assert.IsFalse(SelectModeExit.EdgeTrips(true, true, true), "already going on when Select was armed");
            Assert.IsFalse(SelectModeExit.EdgeTrips(true, true, false));
            Assert.IsFalse(SelectModeExit.EdgeTrips(false, false, true), "not armed");
        }

        [Test]
        public void SelectExit_Raise_ReachesListeners_BeforeTheKeyActs()
        {
            int before = SelectModeExit.Raised;
            string seen = null;
            System.Action<string> h = w => seen = w;
            SelectModeExit.OtherAction += h;
            try
            {
                SelectModeExit.Raise("Manager");
                Assert.AreEqual("Manager", seen);
                Assert.AreEqual(before + 1, SelectModeExit.Raised);
                Assert.AreEqual("Manager", SelectModeExit.Last);
            }
            finally { SelectModeExit.OtherAction -= h; }
            seen = null;
            SelectModeExit.Raise("Move");
            Assert.IsNull(seen, "unsubscribed");
        }
    }
}
#endif
