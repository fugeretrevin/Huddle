// =============================================================================
// MuseDesk · EditMode tests · Meet (MeetKeyV1): the one-poke way into the large-agent portal.
// Pure logic: AgentVisitFlow.PrepareMeet, the Meet key's table (MeetEntry), the dock row (SpacesDockLogic), the copy,
// and the lifecycle guarantees a Meet relies on (registry Away/Home exactly once, PresentationMirror, Cancel any time,
// the small-visitor fallback). Not proven here: the scene keys and pokes, the chooser card, the keep-out guard in a room
// (PortalKeepOutTests covers the guard; AgentScripts/MeetDrive.cs the whole path in Play Mode), anything on a headset.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Onboarding;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    [Category("MuseDesk")]
    public class MeetPortalEntryTests
    {
        /// <summary>Every presentation change the registry announces, per agent: leaves (Home -> away) and returns (-> Home).</summary>
        sealed class Recorder
        {
            public readonly Dictionary<string, int> leaves = new Dictionary<string, int>(), homes = new Dictionary<string, int>();
            readonly Dictionary<string, AgentPresentation> _last = new Dictionary<string, AgentPresentation>();
            public readonly List<string> trace = new List<string>();

            public Recorder(AgentPresentationRegistry r) { r.Changed += On; }

            void On(string agent, AgentPresentation p)
            {
                AgentPresentation was;
                if (!_last.TryGetValue(agent, out was)) was = AgentPresentation.Home;
                if (p == AgentPresentation.Home) Bump(homes, agent);
                else if (was == AgentPresentation.Home) Bump(leaves, agent);
                _last[agent] = p;
                trace.Add(agent + ":" + p);
            }

            static void Bump(Dictionary<string, int> d, string k) { int n; d.TryGetValue(k, out n); d[k] = n + 1; }
            public int Left(string a) { int n; return leaves.TryGetValue(a, out n) ? n : 0; }
            public int Home(string a) { int n; return homes.TryGetValue(a, out n) ? n : 0; }
        }

        static AgentPresentationRegistry _reg;
        static AgentVisitFlow _visit;
        static PortalFlow _portal;
        static Recorder _rec;

        [SetUp]
        public void SetUp()
        {
            _reg = new AgentPresentationRegistry();
            _visit = new AgentVisitFlow(_reg);
            _portal = new PortalFlow(_reg);
            _rec = new Recorder(_reg);
        }

        static PortalCandidate Large()
        {
            return new PortalCandidate { position = new Vector3(1f, 0f, 0.2f), rotation = Quaternion.identity, avatarHeight = 1.1f, valid = true, roomChecked = true, reason = "Clear space beside you" };
        }

        static void TickPortal(float seconds) { for (float t = 0; t < seconds; t += 0.02f) _portal.Tick(0.02f); }
        static void TickVisit(float seconds) { for (float t = 0; t < seconds; t += 0.02f) _visit.Tick(0.02f); }

        /// <summary>What the controller's MeetAgent does, without the scene: PrepareMeet, then the portal's own preview.</summary>
        static bool Meet(string agent)
        {
            bool handedOver;
            if (!_visit.PrepareMeet(agent, out handedOver)) return false;
            return _portal.BeginPreview(agent, Large());
        }

        // ── AgentVisitFlow.PrepareMeet ──

        [Test]
        public void FromHome_PreviewReservesTheAgent_NoDeskVisitOnTheWay()
        {
            bool handedOver;
            Assert.IsTrue(_visit.PrepareMeet("researcher", out handedOver));
            Assert.IsFalse(handedOver);
            Assert.AreEqual(VisitState.Off, _visit.State, "no desk visit is created");
            Assert.AreEqual(0, _reg.AwayCount, "PrepareMeet itself reserves nothing");
            Assert.IsTrue(_portal.BeginPreview("researcher", Large()));
            Assert.AreEqual(PortalState.AwaitConfirm, _portal.State);
            Assert.AreEqual(AgentPresentation.LargePortal, _reg.Of("researcher"));
            Assert.AreEqual(1, _reg.AwayCount);
            CollectionAssert.AreEqual(new[] { "researcher:LargePortal" }, _rec.trace, "one change only: straight to the portal");
        }

        [Test]
        public void FullRoundTrip_LeavesOnceAndComesHomeOnce()
        {
            Assert.IsTrue(Meet("researcher"));
            Assert.IsTrue(_portal.Confirm());
            TickPortal(2.5f);
            Assert.AreEqual(PortalState.Conversing, _portal.State);
            _portal.SendBack();
            TickPortal(2f);
            Assert.AreEqual(PortalState.Hidden, _portal.State);
            Assert.AreEqual(AgentPresentation.Home, _reg.Of("researcher"));
            Assert.AreEqual(1, _rec.Left("researcher"));
            Assert.AreEqual(1, _rec.Home("researcher"));
            Assert.AreEqual(0, _reg.AwayCount);
        }

        [Test]
        public void CancelAtEveryStage_AlwaysHomeExactlyOnce()
        {
            // Preview, opening, emerging, conversing: an interruption from each frees the agent once.
            float[] after = { 0f, 0.3f, 1.2f, 2.5f };
            foreach (float t in after)
            {
                SetUp();
                Assert.IsTrue(Meet("tester"));
                if (t > 0f) { Assert.IsTrue(_portal.Confirm()); TickPortal(t); }
                _portal.Interrupt("Cancelled");
                TickPortal(2f);
                Assert.AreEqual(PortalState.Hidden, _portal.State, "after " + t);
                Assert.AreEqual(1, _rec.Left("tester"), "after " + t);
                Assert.AreEqual(1, _rec.Home("tester"), "after " + t);
            }
        }

        [Test]
        public void WhileSelectAgentsIsArmed_MeetSwitchesItOff()
        {
            Assert.IsTrue(_visit.Arm());
            bool handedOver;
            Assert.IsTrue(_visit.PrepareMeet("developer", out handedOver));
            Assert.AreEqual(VisitState.Off, _visit.State);
            Assert.IsFalse(_visit.IsArmed);
            Assert.IsFalse(_visit.Grab("developer"), "an armed pinch can no longer start a carry");
        }

        [Test]
        public void PerchedSameAgent_HandsOverWithoutAFlyHome()
        {
            _visit.Arm(); _visit.Grab("developer"); _visit.Release(true);
            Assert.AreEqual(VisitState.Perched, _visit.State);
            bool handedOver;
            Assert.IsTrue(_visit.PrepareMeet("developer", out handedOver));
            Assert.IsTrue(handedOver);
            Assert.AreEqual(VisitState.Off, _visit.State);
            Assert.AreEqual(VisitEndReason.PortalTakesOver, _visit.LastEnd);
            Assert.IsTrue(_portal.BeginPreview("developer", Large()));
            Assert.AreEqual(AgentPresentation.LargePortal, _reg.Of("developer"));
            Assert.AreEqual(1, _reg.AwayCount);
        }

        [Test]
        public void PerchedOtherAgent_RefusedAndNothingChanges()
        {
            _visit.Arm(); _visit.Grab("developer"); _visit.Release(true);
            bool handedOver;
            Assert.IsFalse(_visit.PrepareMeet("tester", out handedOver));
            Assert.IsFalse(handedOver);
            StringAssert.Contains("Developer back first", _visit.Message);
            Assert.AreEqual(VisitState.Perched, _visit.State);
            Assert.AreEqual(AgentPresentation.DeskVisit, _reg.Of("developer"));
            Assert.AreEqual(AgentPresentation.Home, _reg.Of("tester"));
        }

        [Test]
        public void CarryingOrReturning_Refused()
        {
            bool handedOver;
            _visit.Arm(); _visit.Grab("tester");
            Assert.IsFalse(_visit.PrepareMeet("tester", out handedOver));
            StringAssert.Contains("Finish", _visit.Message);
            Assert.AreEqual(VisitState.Carrying, _visit.State);
            _visit.Release(false);
            Assert.AreEqual(VisitState.Returning, _visit.State);
            Assert.IsFalse(_visit.PrepareMeet("tester", out handedOver));
            StringAssert.Contains("is home", _visit.Message);
            TickVisit(1f);
            Assert.IsTrue(_visit.PrepareMeet("tester", out handedOver), "once home, Meet works again");
        }

        [Test]
        public void NoAgent_Refused()
        {
            bool handedOver;
            Assert.IsFalse(_visit.PrepareMeet(null, out handedOver));
            Assert.IsFalse(_visit.PrepareMeet("", out handedOver));
        }

        [Test]
        public void OnePortalAtATime_OnePresentationPerAgent()
        {
            Assert.IsTrue(Meet("researcher"));
            Assert.IsFalse(Meet("tester"), "a second portal never opens");
            Assert.AreEqual(AgentPresentation.Home, _reg.Of("tester"));
            Assert.AreEqual(1, _reg.AwayCount);
            // and no desk visit of the same agent while it is in the portal
            Assert.IsTrue(_visit.Arm());
            Assert.IsFalse(_visit.Grab("researcher"));
        }

        [Test]
        public void SmallVisitorFallback_IsConfirmable()
        {
            // No clear large spot ("Not enough space here"): the small visitor beside the user is offered and can be opened.
            var user = new SeatedUser(new Vector3(0f, 1.2f, 0f), Vector3.forward);
            PortalCandidate small = PortalPlacementEvaluator.ManualSmall(user, 70f, new PortalSettings());
            bool handedOver;
            Assert.IsTrue(_visit.PrepareMeet("manager", out handedOver));
            Assert.IsTrue(_portal.BeginPreview("manager", small));
            Assert.AreEqual(PortalState.AwaitConfirm, _portal.State);
            Assert.IsTrue(_portal.Confirm());
            Assert.Less(_portal.Spot.avatarHeight, 0.5f, "small visitor");
        }

        [Test]
        public void InvalidSpot_CannotBeOpened()
        {
            PortalCandidate bad = Large(); bad.valid = false; bad.reason = "Furniture is in the way";
            bool handedOver;
            Assert.IsTrue(_visit.PrepareMeet("researcher", out handedOver));
            Assert.IsTrue(_portal.BeginPreview("researcher", bad));
            Assert.AreEqual(PortalState.Preview, _portal.State);
            Assert.IsFalse(_portal.Confirm(), "nothing opens on a spot that is not clear");
            _portal.Interrupt("Cancelled");
            Assert.AreEqual(1, _rec.Home("researcher"));
        }

        // ── the mirror sees ONE portal presentation and no phantom desk visit ──

        [Test]
        public void Mirror_PortalOnly_OpenedAndClosedOnce()
        {
            var m = new PresentationMirror();
            int opened = 0, closed = 0, returned = 0, visitOpened = 0;
            m.portal.Opened += (p, a) => opened++;
            m.portal.Closed += c => closed++;
            m.portal.ReturnHome += p => returned++;
            m.visit.Opened += (p, a) => visitOpened++;
            Assert.IsTrue(Meet("tester"));
            for (int f = 0; f < 5; f++) m.Sync(false, null, Vector3.zero, _portal.State != PortalState.Hidden, _portal.Agent, Vector3.zero);
            _portal.Interrupt("Cancelled");
            for (int f = 0; f < 5; f++) m.Sync(false, null, Vector3.zero, _portal.State != PortalState.Hidden, _portal.Agent, Vector3.zero);
            Assert.AreEqual(1, opened);
            Assert.AreEqual(1, closed);
            Assert.AreEqual(1, returned);
            Assert.AreEqual(0, visitOpened, "Meet never creates a desk-visit presentation");
            Assert.IsFalse(m.AnyOpen);
        }

        // ── the Meet key's table ──

        static MeetKeyAction Decide(bool flag = true, bool active = true, bool previewing = false, bool choosing = false,
                                    PortalState portal = PortalState.Hidden, VisitState visit = VisitState.Off)
        {
            string why;
            return MeetEntry.Decide(flag, active, previewing, choosing, portal, visit, out why);
        }

        [Test]
        public void MeetKey_Table()
        {
            Assert.AreEqual(MeetKeyAction.OpenChooser, Decide());
            Assert.AreEqual(MeetKeyAction.OpenChooser, Decide(visit: VisitState.Armed), "Select Agents armed: Meet is the other way in");
            Assert.AreEqual(MeetKeyAction.CloseChooser, Decide(choosing: true), "the key is a toggle");
            Assert.AreEqual(MeetKeyAction.MeetPerched, Decide(visit: VisitState.Perched));
            Assert.AreEqual(MeetKeyAction.Refuse, Decide(flag: false));
            Assert.AreEqual(MeetKeyAction.Refuse, Decide(active: false));
            Assert.AreEqual(MeetKeyAction.Refuse, Decide(previewing: true), "choosing a wall spot");
            Assert.AreEqual(MeetKeyAction.Refuse, Decide(visit: VisitState.Carrying));
            Assert.AreEqual(MeetKeyAction.Refuse, Decide(visit: VisitState.Returning));
            foreach (PortalState p in new[] { PortalState.Preview, PortalState.AwaitConfirm, PortalState.Opening, PortalState.Emerging, PortalState.Conversing, PortalState.Returning, PortalState.Closing })
                Assert.AreEqual(MeetKeyAction.Refuse, Decide(portal: p), p.ToString());
        }

        [Test]
        public void MeetKey_EveryRefusalSaysWhy()
        {
            string why;
            MeetEntry.Decide(false, true, false, false, PortalState.Hidden, VisitState.Off, out why);
            Assert.AreEqual(MeetCopy.PortalOff, why);
            MeetEntry.Decide(true, true, true, false, PortalState.Hidden, VisitState.Off, out why);
            Assert.AreEqual(MeetCopy.PlaceFirst, why);
            MeetEntry.Decide(true, true, false, false, PortalState.AwaitConfirm, VisitState.Off, out why);
            Assert.AreEqual(MeetCopy.AnswerCardFirst, why);
            MeetEntry.Decide(true, true, false, false, PortalState.Conversing, VisitState.Off, out why);
            Assert.AreEqual(MeetCopy.SendBackFirst, why);
            MeetEntry.Decide(true, true, false, false, PortalState.Hidden, VisitState.Carrying, out why);
            Assert.AreEqual(MeetCopy.FinishVisitFirst, why);
        }

        // ── the dock row ──

        static SpacesDockInput WallAtRest(bool meet)
        {
            return new SpacesDockInput { floorEnabled = true, ceilingEnabled = true, wallEnabled = true, mode = PlacementMode.Desk, podState = CeilingPodState.Docked,
                                         placeReady = true, wallActive = true, wallSelectEnabled = true, wallManagerEnabled = true, wallMeetEnabled = meet };
        }

        [Test]
        public void Dock_MeetOnlyAtRestOnTheWall_WithTheFlag()
        {
            var k = SpacesDockLogic.Compute(WallAtRest(true));
            Assert.IsTrue(k.meet && k.move && k.select && k.manager);
            Assert.IsFalse(k.layout, "Meet takes the Layout key's slot: the two never show together");
            Assert.IsFalse(SpacesDockLogic.Compute(WallAtRest(false)).meet, "flag off: no Meet key");

            var i = WallAtRest(true); i.wallPreviewing = true; i.wallCanPlace = true;
            k = SpacesDockLogic.Compute(i);
            Assert.IsTrue(k.layout);
            Assert.IsFalse(k.meet, "not while a wall spot is previewed");

            i = WallAtRest(true); i.wallActive = false;
            Assert.IsFalse(SpacesDockLogic.Compute(i).meet, "not at the desk");
            i.mode = PlacementMode.Floor;
            Assert.IsFalse(SpacesDockLogic.Compute(i).meet, "not in Floor");
        }

        [Test]
        public void Dock_MeetWordFitsTheKey()
        {
            CollectionAssert.Contains(SpacesDockLogic.AllWords, SpacesDockLogic.Meet);
            Assert.IsTrue(SpacesDockLogic.FitsOnKey(SpacesDockLogic.Meet));
        }

        // ── copy ──

        [Test]
        public void Copy_PlacedHintIsOneShortSentenceNamingMeet()
        {
            string s = MeetCopy.PlacedHint;
            StringAssert.Contains("Meet", s);
            Assert.LessOrEqual(s.Length, 48, "fits the dock's one-line status");
            Assert.IsTrue(s.EndsWith("."));
            Assert.AreEqual(1, s.Split('.').Length - 1, "one sentence");
            Assert.LessOrEqual(s.Split(' ').Length, HintCatalog.MaxWords);
        }

        [Test]
        public void Copy_ChooserListsTheFourAgentsWithShortLabels()
        {
            CollectionAssert.AreEqual(new[] { "researcher", "developer", "tester", "manager" }, MeetCopy.Agents);
            foreach (string a in MeetCopy.Agents)
            {
                HabitatRole r;
                Assert.IsTrue(HabitatStage.TryRole(a, out r), a);
                Assert.LessOrEqual(AgentPresentationRegistry.Pretty(a).Length, "Meet larger".Length, "no longer than the visit card's longest key");
            }
            StringAssert.Contains("Open here", MeetCopy.ChooserFoot);
            StringAssert.DoesNotContain("AI", MeetCopy.ChooserBody, "no claim of live AI");
        }

        [Test]
        public void Onboarding_TheMeetHintFollowsTheHabitatBeingPlaced()
        {
            HintDef d = HintCatalog.Get(HintId.PortalMeet);
            Assert.AreEqual(OnboardingEvent.WallPlaced, d.Trigger);
            StringAssert.Contains("Meet", d.Text);
            Assert.LessOrEqual(d.WordCount(), HintCatalog.MaxWords);
            Assert.IsTrue(d.CompletesOn(OnboardingEvent.PortalOpened));
        }
    }
}
#endif
