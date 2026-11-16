// =============================================================================
// MuseDesk · EditMode tests · the ONE Spaces dock and the start-up reach (pure logic)
// =============================================================================
//
// Exercises the REAL rules in MuseDesk.Spatial.Core: SpacesDockLogic (which keys of the unified Desk | Floor |
// Ceiling | Wall dock apply in each mode and phase, and that every word fits its key) and StartupReachRule (when the
// near-user panels are re-seated in front of the head at start-up, and where). No scene, no MonoBehaviour. Compiled
// under the same define as the other spatial tests (see SpatialPlacementTests.cs).
//
// NOT covered: the scene keys themselves (poke, scale ease), the wall host, NearUserKit's transforms (PLAY:
// AgentScripts/UnifiedDockDrive.cs), and anything on a headset.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;

namespace MuseDesk.Tests.EditMode
{
    [Category("MuseDesk")]
    public class SpacesDockLogicTests
    {
        static SpacesDockInput In(PlacementMode mode = PlacementMode.Desk, bool floor = true, bool ceiling = true, bool wall = true)
        {
            return new SpacesDockInput { floorEnabled = floor, ceilingEnabled = ceiling, wallEnabled = wall, mode = mode, podState = CeilingPodState.Docked, placeReady = true };
        }

        static bool AnyContext(SpacesDockKeys k)
        {
            return k.next || k.confirm || k.cancel || k.stop || k.pod || k.bring || k.status || k.layout || k.move || k.select || k.manager;
        }

        [Test]
        public void DeskAtRestOffersAllFourPlacesAndNoContextKeys()
        {
            var k = SpacesDockLogic.Compute(In());
            Assert.AreEqual(0, k.active);
            Assert.IsFalse(k.deskDisabled || k.floorDisabled || k.ceilingDisabled || k.wallDisabled);
            Assert.IsFalse(AnyContext(k));
        }

        [Test]
        public void EachFlagDisablesOnlyItsPlace()
        {
            var k = SpacesDockLogic.Compute(In(wall: false));
            Assert.IsTrue(k.wallDisabled);
            Assert.IsFalse(k.floorDisabled || k.ceilingDisabled);
            k = SpacesDockLogic.Compute(In(floor: false));
            Assert.IsTrue(k.floorDisabled);
            Assert.IsFalse(k.wallDisabled || k.ceilingDisabled);
        }

        [Test]
        public void WallIsOfferedOnlyFromTheDeskAtRest()
        {
            var i = In(PlacementMode.Floor);
            Assert.IsTrue(SpacesDockLogic.Compute(i).wallDisabled, "Floor -> Wall must go through Desk (a swept move)");
            i = In(PlacementMode.Ceiling);
            Assert.IsTrue(SpacesDockLogic.Compute(i).wallDisabled);
            i = In(); i.previewing = true; i.floorPreview = true;
            Assert.IsTrue(SpacesDockLogic.Compute(i).wallDisabled, "not while a floor preview is open");
            i = In(); i.transitioning = true;
            var k = SpacesDockLogic.Compute(i);
            Assert.IsTrue(k.wallDisabled && k.deskDisabled && k.floorDisabled && k.ceilingDisabled, "every place is disabled while the office moves");
            Assert.IsTrue(k.stop);
        }

        [Test]
        public void WallPreviewUsesTheSharedPreviewKeysPlusLayout()
        {
            var i = In(); i.wallPreviewing = true; i.wallCanPlace = true;
            var k = SpacesDockLogic.Compute(i);
            Assert.IsTrue(k.wallContext);
            Assert.IsTrue(k.next && k.confirm && k.cancel && k.layout);
            Assert.IsFalse(k.confirmDisabled);
            Assert.IsFalse(k.stop || k.pod || k.bring || k.status || k.move || k.select || k.manager);
            Assert.AreEqual(SpacesDockLogic.Place, k.confirmWords);
            Assert.AreEqual(SpacesDockLogic.NextSpot, k.nextWords);
            Assert.AreEqual(0, k.active, "entering: the office is still at the desk");
        }

        [Test]
        public void WallPlaceIsArmedOnlyWhileTheSpotFits()
        {
            var i = In(); i.wallPreviewing = true; i.wallCanPlace = false;
            var k = SpacesDockLogic.Compute(i);
            Assert.IsTrue(k.confirm, "shown, so the user sees where it is");
            Assert.IsTrue(k.confirmDisabled, "but not armed");
        }

        [Test]
        public void WallAtRestOffersMoveSelectManagerAndLeavesTheOtherPlacesOpen()
        {
            var i = In(); i.wallActive = true; i.wallSelectEnabled = true; i.wallManagerEnabled = true;
            var k = SpacesDockLogic.Compute(i);
            Assert.AreEqual(3, k.active);
            Assert.IsTrue(k.move && k.select && k.manager);
            Assert.IsFalse(k.next || k.confirm || k.cancel || k.layout || k.stop || k.pod || k.bring || k.status);
            Assert.IsFalse(k.deskDisabled || k.floorDisabled || k.ceilingDisabled, "Floor and Ceiling stay reachable from Wall (it is left safely first)");
            i.wallSelectEnabled = false; i.wallManagerEnabled = false;
            k = SpacesDockLogic.Compute(i);
            Assert.IsTrue(k.move);
            Assert.IsFalse(k.select || k.manager);
        }

        [Test]
        public void MovingTheHabitatIsAPreviewWithTheWallLit()
        {
            var i = In(); i.wallActive = true; i.wallPreviewing = true; i.wallCanPlace = true;
            var k = SpacesDockLogic.Compute(i);
            Assert.AreEqual(3, k.active);
            Assert.IsTrue(k.wallContext && k.confirm && k.cancel && k.next && k.layout);
            Assert.IsFalse(k.move || k.select || k.manager);
        }

        [Test]
        public void NoPlaceKeyExistsOutsideAPreview()
        {
            PlacementMode[] modes = { PlacementMode.Desk, PlacementMode.Floor, PlacementMode.Ceiling };
            CeilingPodState[] pods = { CeilingPodState.Docked, CeilingPodState.Engaged, CeilingPodState.Descending };
            foreach (var m in modes)
                foreach (var p in pods)
                    for (int bits = 0; bits < 8; bits++)
                    {
                        var i = In(m);
                        i.podState = p;
                        i.transitioning = (bits & 1) != 0;
                        i.wallActive = (bits & 2) != 0;
                        i.wallCanPlace = (bits & 4) != 0;
                        var k = SpacesDockLogic.Compute(i);
                        Assert.IsFalse(k.confirm, $"Place shown outside a preview: mode={m} pod={p} bits={bits}");
                        Assert.IsFalse(k.wallContext);
                    }
        }

        [Test]
        public void WordsFollowTheWallState()
        {
            var i = In(); i.wallPreviewing = true; i.wallLayoutFull = true;
            Assert.AreEqual(SpacesDockLogic.Compact, SpacesDockLogic.Compute(i).layoutWords);
            i.wallLayoutFull = false;
            Assert.AreEqual(SpacesDockLogic.Full, SpacesDockLogic.Compute(i).layoutWords);
            i = In(); i.wallActive = true; i.wallSelectEnabled = true; i.wallSelecting = true;
            Assert.AreEqual(SpacesDockLogic.Done, SpacesDockLogic.Compute(i).selectWords);
        }

        [Test]
        public void WithoutTheWallTheTableIsTheModeConsoleTable()
        {
            PlacementMode[] modes = { PlacementMode.Desk, PlacementMode.Floor, PlacementMode.Ceiling };
            CeilingPodState[] pods = { CeilingPodState.Docked, CeilingPodState.Engaged, CeilingPodState.Retracting };
            foreach (var m in modes)
                foreach (var p in pods)
                    for (int bits = 0; bits < 32; bits++)
                    {
                        bool prev = (bits & 1) != 0, cyc = (bits & 2) != 0, tr = (bits & 4) != 0, fp = (bits & 8) != 0, ready = (bits & 16) != 0;
                        var i = In(m, wall: false);
                        i.previewing = prev; i.canCycle = cyc; i.transitioning = tr; i.podState = p; i.floorPreview = fp && prev; i.placeReady = ready;
                        var a = SpacesDockLogic.Compute(i);
                        var b = ModeConsoleLogic.Compute(true, true, m, prev, cyc, tr, p, false, fp && prev, ready);
                        string at = $"mode={m} pod={p} bits={bits}";
                        Assert.AreEqual(b.active, a.active, at);
                        Assert.AreEqual(b.deskDisabled, a.deskDisabled, at);
                        Assert.AreEqual(b.floorDisabled, a.floorDisabled, at);
                        Assert.AreEqual(b.ceilingDisabled, a.ceilingDisabled, at);
                        Assert.AreEqual(b.next, a.next, at); Assert.AreEqual(b.confirm, a.confirm, at); Assert.AreEqual(b.cancel, a.cancel, at);
                        Assert.AreEqual(b.stop, a.stop, at); Assert.AreEqual(b.pod, a.pod, at); Assert.AreEqual(b.bring, a.bring, at);
                        Assert.AreEqual(b.status, a.status, at); Assert.AreEqual(b.podWords, a.podWords, at);
                        Assert.IsFalse(a.layout || a.move || a.select || a.manager || a.wallContext || a.confirmDisabled, at);
                    }
        }

        [Test]
        public void EveryDockWordFitsItsKeyWithoutAnEllipsis()
        {
            foreach (string w in SpacesDockLogic.AllWords)
                Assert.IsTrue(SpacesDockLogic.FitsOnKey(w), "'" + w + "' does not fit a " + SpacesDockLogic.KeyCapWidthMm + " mm key at " + SpacesDockLogic.KeyLabelMinMm + " mm");
        }

        [Test]
        public void TheOldLongLabelsWereTooLongForTheKey()
        {
            // rc8: these were shown on 36 mm keys and ended in an ellipsis.
            foreach (string w in new[] { "Select agents", "Stop selecting", "Place here", "Other spot" })
                Assert.IsFalse(SpacesDockLogic.FitsOnKey(w), "'" + w + "' unexpectedly fits");
        }
    }

    [Category("MuseDesk")]
    public class StartupReachRuleTests
    {
        static StartupReachInput Ok(float worst = 0.5f)
        {
            return new StartupReachInput
            {
                secondsSinceDeskPose = 2f, headUsable = true, headSteadySeconds = 1f, placeable = true,
                worstDistance = worst, nearestDistance = 0.4f, secondsSinceLastReseat = 100f,
            };
        }

        [Test]
        public void WaitsUntilThereIsADeskPose()
        {
            var i = Ok(2.5f); i.secondsSinceDeskPose = -1f;
            Assert.AreEqual(StartupReachAction.Wait, StartupReachRule.Decide(i));
        }

        [Test]
        public void KeepsPanelsThatAreInReach()
        {
            Assert.AreEqual(StartupReachAction.Keep, StartupReachRule.Decide(Ok(0.52f)));
        }

        [Test]
        public void ReseatsWhenAPanelIsBeyondArmsReachOrInsideTheHead()
        {
            Assert.AreEqual(StartupReachAction.Reseat, StartupReachRule.Decide(Ok(StartupReachRule.MaxReach + 0.01f)));
            var i = Ok(0.5f); i.nearestDistance = 0.1f;
            Assert.AreEqual(StartupReachAction.Reseat, StartupReachRule.Decide(i));
        }

        [Test]
        public void ReseatsOnceWhenTheFirstPoseWasTakenWithoutAHead()
        {
            var i = Ok(0.5f); i.firstPoseWithoutHead = true;
            Assert.AreEqual(StartupReachAction.Reseat, StartupReachRule.Decide(i));
            i.reseats = 1;
            Assert.AreEqual(StartupReachAction.Keep, StartupReachRule.Decide(i), "after the re-seat, distance decides");
        }

        [Test]
        public void NeverActsOnTrackingLossAMovingHeadOrDuringAPreview()
        {
            var i = Ok(3f); i.headUsable = false;
            Assert.AreEqual(StartupReachAction.Wait, StartupReachRule.Decide(i), "tracking loss");
            i = Ok(3f); i.headSteadySeconds = 0.1f;
            Assert.AreEqual(StartupReachAction.Wait, StartupReachRule.Decide(i), "head still turning");
            i = Ok(3f); i.placeable = false;
            Assert.AreEqual(StartupReachAction.Wait, StartupReachRule.Decide(i), "preview, move or held object");
            i = Ok(3f); i.firstPoseWithoutHead = true; i.headUsable = false;
            Assert.AreEqual(StartupReachAction.Wait, StartupReachRule.Decide(i));
        }

        [Test]
        public void TheUsersOwnLayoutWinsAndTheWindowEnds()
        {
            var i = Ok(3f); i.userMovedPanel = true;
            Assert.AreEqual(StartupReachAction.Done, StartupReachRule.Decide(i));
            i = Ok(3f); i.secondsSinceDeskPose = StartupReachRule.WindowSeconds + 1f;
            Assert.AreEqual(StartupReachAction.Done, StartupReachRule.Decide(i));
            i = Ok(3f); i.reseats = StartupReachRule.MaxReseats;
            Assert.AreEqual(StartupReachAction.Done, StartupReachRule.Decide(i));
        }

        [Test]
        public void ReseatsAreSpacedOut()
        {
            var i = Ok(3f); i.reseats = 1; i.secondsSinceLastReseat = 0.2f;
            Assert.AreEqual(StartupReachAction.Wait, StartupReachRule.Decide(i));
            i.secondsSinceLastReseat = StartupReachRule.MinGapSeconds + 0.1f;
            Assert.AreEqual(StartupReachAction.Reseat, StartupReachRule.Decide(i));
        }

        [Test]
        public void HeadUsableRefusesNoPoseNaNAndLookingAtTheFloor()
        {
            Assert.IsFalse(StartupReachRule.HeadUsable(Vector3.zero, Vector3.forward), "at the tracking origin: no pose yet");
            Assert.IsFalse(StartupReachRule.HeadUsable(new Vector3(float.NaN, 1f, 0f), Vector3.forward));
            Vector3 down = Quaternion.Euler(60f, 0f, 0f) * Vector3.forward;
            Assert.IsFalse(StartupReachRule.HeadUsable(new Vector3(0f, 1.2f, 0f), down));
            Assert.IsTrue(StartupReachRule.HeadUsable(new Vector3(0f, 1.2f, 0f), Vector3.forward));
            Vector3 slightlyDown = Quaternion.Euler(20f, 0f, 0f) * Vector3.forward;
            Assert.IsTrue(StartupReachRule.HeadUsable(new Vector3(0.3f, 1.1f, -2f), slightlyDown));
        }

        [Test]
        public void ReseatPoseIsUprightInFrontAndFacesTheUser()
        {
            Vector3 head = new Vector3(1.5f, 1.2f, -0.7f);
            Vector3 look = Quaternion.Euler(25f, 40f, 0f) * Vector3.forward;   // looking down a little, turned
            StartupReachRule.ReseatPose(head, look, 0f, 0.62f, 0.39f, out Vector3 p, out Quaternion q);
            Vector3 flat = look; flat.y = 0f; flat.Normalize();
            Assert.AreEqual(head.y - 0.39f, p.y, 1e-4f);
            Assert.AreEqual(0.62f, Vector3.Dot(p - head, flat), 1e-4f, "ahead along the level forward");
            Assert.AreEqual(1f, Vector3.Dot(q * Vector3.up, Vector3.up), 1e-4f, "upright (yaw only)");
            Vector3 toUser = head - p; toUser.y = 0f;
            Assert.Greater(Vector3.Dot(q * Vector3.forward, toUser.normalized), 0.999f, "+Z points at the user");
        }

        [Test]
        public void ReseatPoseSideIsTheUsersRight()
        {
            StartupReachRule.ReseatPose(new Vector3(0f, 1.2f, 0f), Vector3.forward, 0.75f, 0.1f, 0.55f, out Vector3 p, out Quaternion q);
            Assert.AreEqual(0.75f, p.x, 1e-4f, "facing +Z the user's right is +X");
        }

        [Test]
        public void TheDefaultPanelsLandWithinArmsReachBelowTheEyesInFront()
        {
            // The scene's panel poses in the near-user rig (MainMRScene.unity, Panel_Dock/TaskDock and Panel_Mode/ModeConsole)
            // and the scene's off-desk numbers (side 0, depth 0.62, drop 0.39).
            Vector3 head = new Vector3(0.2f, 1.15f, 0.4f);
            Vector3 look = Quaternion.Euler(10f, -30f, 0f) * Vector3.forward;
            StartupReachRule.ReseatPose(head, look, 0f, 0.62f, 0.39f, out Vector3 rigPos, out Quaternion rigRot);
            Vector3 flat = look; flat.y = 0f; flat.Normalize();
            foreach (Vector3 local in new[] { new Vector3(0.24f, 0.04f, 0.44f), new Vector3(-0.3545f, 0.0434f, 0.465f) })
            {
                Vector3 w = rigPos + rigRot * local;
                float d = Vector3.Distance(w, head);
                Assert.That(d, Is.InRange(0.35f, 0.55f), "distance from the head " + d);
                Assert.Less(w.y, head.y, "below the eyes");
                Assert.Greater(Vector3.Dot(w - head, flat), 0f, "in front");
            }
        }

        [Test]
        public void RootLocalRoundTripIsExact()
        {
            Vector3 rp = new Vector3(0.4f, 0.8f, 1.1f); Quaternion rr = Quaternion.Euler(0f, 137f, 0f);
            Vector3 p = new Vector3(-0.2f, 0.75f, 0.3f); Quaternion q = Quaternion.Euler(0f, -20f, 0f);
            StartupReachRule.ToRootLocal(rp, rr, p, q, out Vector3 lp, out Quaternion lq);
            StartupReachRule.FromRootLocal(rp, rr, lp, lq, out Vector3 p2, out Quaternion q2);
            Assert.Less(Vector3.Distance(p, p2), 1e-5f);
            Assert.Less(Quaternion.Angle(q, q2), 0.01f);
        }
    }
}
#endif
