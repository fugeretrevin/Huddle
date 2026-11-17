using NUnit.Framework;
using MuseDesk.Spatial.Onboarding;

namespace MuseDesk.Tests.EditMode.Onboarding
{
    public class OnboardingCoachTests
    {
        sealed class ManualClock : IOnboardingClock { public double T; public double Now { get { return T; } } }

        ManualClock _clock;
        MemoryOnboardingStore _store;
        OnboardingCoach _coach;
        OnboardingContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _store = new MemoryOnboardingStore();
            _coach = new OnboardingCoach(_clock, _store);
            _ctx = new OnboardingContext();
            _coach.SetContext(_ctx);
        }

        void Ctx(bool focus = false, bool busy = false, OnboardingFlags flags = OnboardingFlags.None, bool reduced = false)
        {
            _ctx = new OnboardingContext { FocusActive = focus, HandsBusy = busy, Flags = flags, ReducedMotion = reduced };
            _coach.SetContext(_ctx);
        }

        // Advances time in quarter-second ticks.
        void Run(double seconds)
        {
            double end = _clock.T + seconds;
            while (_clock.T < end - 1e-9) { _clock.T += 0.25; _coach.Tick(); }
        }

        void Skip(params HintId[] ids) { foreach (var id in ids) _store.MarkDone(id); }

        HintId Cur() { return _coach.Current == null ? HintId.None : _coach.Current.Id; }

        // ---- catalog ----

        [Test]
        public void Catalog_AllCopyIsAtMostNineWords()
        {
            for (int i = 0; i < HintCatalog.Count; i++)
            {
                var d = HintCatalog.At(i);
                Assert.LessOrEqual(d.WordCount(), HintCatalog.MaxWords, d.Id + ": " + d.Text);
                Assert.Greater(d.WordCount(), 0, d.Id.ToString());
            }
        }

        [Test]
        public void Catalog_IdsAreUniqueAndLookupWorks()
        {
            for (int i = 0; i < HintCatalog.Count; i++)
            {
                var d = HintCatalog.At(i);
                Assert.AreEqual(i, HintCatalog.IndexOf(d.Id));
                Assert.AreSame(d, HintCatalog.Get(d.Id));
            }
            Assert.IsNull(HintCatalog.Get(HintId.None));
        }

        [Test]
        public void Catalog_EveryHintHasAnActionToComplete()
        {
            for (int i = 0; i < HintCatalog.Count; i++)
                Assert.Greater(HintCatalog.At(i).Completes.Length, 0, HintCatalog.At(i).Id.ToString());
        }

        [Test]
        public void Catalog_HasRecoveryHintsForCommonMistakes()
        {
            Assert.IsTrue(HintCatalog.Get(HintId.RecoverDroppedPaper).Recovery);
            Assert.IsTrue(HintCatalog.Get(HintId.RecoverWorkspaceMoved).Recovery);
            Assert.IsTrue(HintCatalog.Get(HintId.RecoverTrackingLost).Recovery);
            Assert.IsTrue(HintCatalog.Get(HintId.RecoverNoUndo).Recovery);
            StringAssert.Contains("Undo", HintCatalog.Get(HintId.RecoverNoUndo).Text);
        }

        [Test]
        public void Catalog_UsesAllFiveAnchorKinds()
        {
            var seen = new System.Collections.Generic.HashSet<HintAnchorKind>();
            for (int i = 0; i < HintCatalog.Count; i++) seen.Add(HintCatalog.At(i).Anchor);
            Assert.AreEqual(5, seen.Count);
        }

        // ---- triggers ----

        [Test]
        public void NothingShowsWithoutAnObservedEvent()
        {
            Run(60);
            Assert.IsNull(_coach.Current);
        }

        [Test]
        public void HintShowsOnlyAfterItsTriggerAndDelay()
        {
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(1);
            Assert.IsNull(_coach.Current);
            Run(1.5);
            Assert.AreEqual(HintId.MeetAgents, Cur());
        }

        [Test]
        public void CompletingTheActionEndsHintAndPersists()
        {
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(2.5);
            _coach.Observe(OnboardingEvent.AgentTouched);
            Assert.IsNull(_coach.Current);
            Assert.IsTrue(_store.IsDone(HintId.MeetAgents));
        }

        [Test]
        public void DoneHintNeverRepeatsEvenForANewCoachOnTheSameStore()
        {
            Skip(HintId.MeetAgents, HintId.StartWork);
            var again = new OnboardingCoach(_clock, _store);
            again.Observe(OnboardingEvent.SessionReady);
            for (int i = 0; i < 80; i++) { _clock.T += 0.25; again.Tick(); }
            Assert.IsNull(again.Current);
        }

        [Test]
        public void OnlyOneHintIsVisibleAtATime()
        {
            _coach.Observe(OnboardingEvent.SessionReady);
            for (int i = 0; i < 40; i++)
            {
                Run(0.25);
                int visible = _coach.IsShowing ? 1 : 0;
                Assert.LessOrEqual(visible, 1);
            }
            Assert.AreEqual(HintId.MeetAgents, Cur());
            Assert.IsTrue(_coach.IsPending(HintId.StartWork));
        }

        [Test]
        public void CooldownSeparatesTwoHints()
        {
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(2.5);
            Assert.AreEqual(HintId.MeetAgents, Cur());
            // The user ignores the tutorial hint and dismisses it; StartWork must wait out the cooldown.
            _coach.Dismiss();
            Run(1);
            Assert.IsNull(_coach.Current);
            Run(3);
            Assert.AreEqual(HintId.StartWork, Cur());
        }

        [Test]
        public void StartWorkWaitsForMeetAgentsToBeDone()
        {
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(11.5);
            Assert.AreEqual(HintId.MeetAgents, Cur()); // StartWork's delay passed long ago but it is gated
            Run(1);                                    // meet-agents times out (1 show max) and is done
            Assert.IsTrue(_store.IsDone(HintId.MeetAgents));
            Run(3.5);
            Assert.AreEqual(HintId.StartWork, Cur());
        }

        // ---- suppression ----

        [Test]
        public void FocusSuppressesNewHints()
        {
            Ctx(focus: true);
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(30);
            Assert.IsNull(_coach.Current);
            Assert.IsTrue(_coach.IsPending(HintId.MeetAgents));
        }

        [Test]
        public void FocusHidesVisibleHintAndItComesBackWhenFocusEnds()
        {
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(2.5);
            Assert.AreEqual(HintId.MeetAgents, Cur());
            Ctx(focus: true);
            Run(0.25);
            Assert.IsNull(_coach.Current);
            Assert.IsFalse(_store.IsDone(HintId.MeetAgents));
            Assert.AreEqual(0, _store.GetShownCount(HintId.MeetAgents)); // suppression does not use up a show
            Ctx();
            Run(4);
            Assert.AreEqual(HintId.MeetAgents, Cur());
        }

        [Test]
        public void BusyHandsBlockANewHint()
        {
            Ctx(busy: true);
            _coach.Observe(OnboardingEvent.DecisionRequested);
            Run(10);
            Assert.IsNull(_coach.Current);
            Ctx(busy: false);
            Run(1);
            Assert.AreEqual(HintId.PaperArrives, Cur());
        }

        [Test]
        public void BusyHandsStillAllowHintsAboutTheHeldPaper()
        {
            Skip(HintId.PaperArrives);
            Ctx(busy: true);
            _coach.Observe(OnboardingEvent.PaperGrabbed);
            Run(1.5);
            Assert.AreEqual(HintId.InspectPaper, Cur());
        }

        [Test]
        public void BusyHandsHideAHintThatIsNotAboutTheHeldObject()
        {
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(2.5);
            Ctx(busy: true);
            Run(0.25);
            Assert.IsNull(_coach.Current);
        }

        // ---- flags ----

        [Test]
        public void WallHintNeedsTheWallFlag()
        {
            Skip(HintId.MeetAgents, HintId.StartWork, HintId.ReceiptDelivered, HintId.MoveWorkspace, HintId.FocusMode, HintId.CalmMotion);
            _coach.Observe(OnboardingEvent.TaskCompleted);
            Run(14);
            Assert.IsNull(_coach.Current);
            Ctx(flags: OnboardingFlags.Wall);
            Run(1);
            Assert.AreEqual(HintId.WallPlace, Cur());
        }

        [Test]
        public void WallHintNeverShowsBeforeTheCoreLoopIsDone()
        {
            Skip(HintId.MeetAgents, HintId.StartWork);
            Ctx(flags: OnboardingFlags.Wall);
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(30);
            Assert.AreNotEqual(HintId.WallPlace, Cur());
            Assert.IsFalse(_coach.IsPending(HintId.WallPlace));
        }

        [Test]
        public void ManagerHintNeedsBothWallAndManagerFlags()
        {
            Ctx(flags: OnboardingFlags.Manager);
            _coach.Observe(OnboardingEvent.ManagerPlanShown);
            Run(5);
            Assert.IsNull(_coach.Current);
            Ctx(flags: OnboardingFlags.Manager | OnboardingFlags.Wall);
            Run(1);
            Assert.AreEqual(HintId.ManagerPlan, Cur());
        }

        [Test]
        public void PortalHintNeedsPortalFlag()
        {
            Ctx(flags: OnboardingFlags.Wall);
            _coach.Observe(OnboardingEvent.WallPlaced);      // MeetKeyV1: the Meet hint follows the habitat being placed
            Run(5);
            Assert.IsNull(_coach.Current);
            Ctx(flags: OnboardingFlags.Wall | OnboardingFlags.Portal);
            Run(1);
            Assert.AreEqual(HintId.PortalMeet, Cur());
        }

        [Test]
        public void MoveWorkspaceHintNeedsSpatialFlag()
        {
            Skip(HintId.ReceiptDelivered, HintId.FocusMode, HintId.CalmMotion);
            _coach.Observe(OnboardingEvent.TaskCompleted);
            Run(12);
            Assert.IsNull(_coach.Current);
            Ctx(flags: OnboardingFlags.Spatial);
            Run(1);
            Assert.AreEqual(HintId.MoveWorkspace, Cur());
        }

        [Test]
        public void ResetLayoutHintFollowsTheFirstPanelMoveAndEndsOnReset()
        {
            Run(10);
            Assert.IsNull(_coach.Current, "nothing before a panel is moved");
            _coach.Observe(OnboardingEvent.PanelMoved);
            Run(1);
            Assert.IsNull(_coach.Current, "waits a moment after the release");
            Run(1.5);
            Assert.AreEqual(HintId.ResetLayout, Cur());
            StringAssert.Contains("reset", HintCatalog.Get(HintId.ResetLayout).Text);
            _coach.Observe(OnboardingEvent.LayoutReset);
            Run(0.5);
            Assert.AreNotEqual(HintId.ResetLayout, Cur());
            Assert.IsTrue(_store.IsDone(HintId.ResetLayout));
            _coach.Observe(OnboardingEvent.PanelMoved);
            Run(5);
            Assert.AreNotEqual(HintId.ResetLayout, Cur(), "shown once");
        }

        [Test]
        public void TrackingLostHintShowsWhileHoldingAndEndsWhenHandsReturn()
        {
            Ctx(busy: true);
            _coach.Observe(OnboardingEvent.TrackingLostWhileHolding);
            Run(1);
            Assert.AreEqual(HintId.RecoverTrackingLost, Cur(), "shown even with the hands busy");
            _coach.Observe(OnboardingEvent.TrackingRegained);
            Run(0.5);
            Assert.AreNotEqual(HintId.RecoverTrackingLost, Cur());
        }

        [Test]
        public void NewHintIdsAreAppendedSoSavedProgressKeepsItsMeaning()
        {
            // PlayerPrefsOnboardingStore keys progress by the id's number: existing ids must never shift.
            Assert.AreEqual(16, (int)HintId.RecoverNoUndo);
            Assert.AreEqual(17, (int)HintId.ResetLayout);
            Assert.AreEqual(18, (int)HintId.VisitorHello);
        }

        [Test]
        public void VisitorHelloHint_FollowsThePortalOpening_AndEndsOnAPokeOrWave()
        {
            Ctx(flags: OnboardingFlags.Wall | OnboardingFlags.Portal);
            Skip(HintId.PortalMeet);
            _coach.Observe(OnboardingEvent.PortalOpened);
            Run(3);
            Assert.AreEqual(HintId.VisitorHello, Cur());
            _coach.Observe(OnboardingEvent.VisitorGreeted);
            Run(0.5);
            Assert.AreNotEqual(HintId.VisitorHello, Cur());
            Assert.IsTrue(_store.IsDone(HintId.VisitorHello));
        }

        [Test]
        public void VisitorHelloHint_NeedsThePortalFlag()
        {
            Ctx(flags: OnboardingFlags.Wall);
            _coach.Observe(OnboardingEvent.PortalOpened);
            Run(5);
            Assert.AreNotEqual(HintId.VisitorHello, Cur());
        }

        // ---- dismiss, skip, timeout, reset ----

        [Test]
        public void DismissMarksDoneAndItNeverReturns()
        {
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(2.5);
            _coach.Dismiss();
            Assert.IsTrue(_store.IsDone(HintId.MeetAgents));
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(2.5);
            Assert.AreNotEqual(HintId.MeetAgents, Cur());
        }

        [Test]
        public void SkipAllStopsEverythingIncludingRecoveryHints()
        {
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(2.5);
            _coach.SkipAll();
            Assert.IsNull(_coach.Current);
            _coach.Observe(OnboardingEvent.PaperDropped);
            Run(10);
            Assert.IsNull(_coach.Current);
        }

        [Test]
        public void IgnoredHintRetriesOnceThenGivesUp()
        {
            Skip(HintId.MeetAgents);
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(3.25);
            Assert.AreEqual(HintId.StartWork, Cur());
            Run(12);                       // times out
            Assert.IsNull(_coach.Current);
            Assert.IsFalse(_store.IsDone(HintId.StartWork));
            Run(4);                        // second show (cap is 2)
            Assert.AreEqual(HintId.StartWork, Cur());
            Run(12.25);
            Assert.IsNull(_coach.Current);
            Assert.IsTrue(_store.IsDone(HintId.StartWork));
            Run(30);
            Assert.IsNull(_coach.Current);
        }

        [Test]
        public void ActionDoneBeforeTheHintShowsMarksItDoneSilently()
        {
            _coach.Observe(OnboardingEvent.SessionReady);
            _coach.Observe(OnboardingEvent.TaskStarted);   // user poked Start straight away
            Run(20);
            Assert.IsNull(_coach.Current);
            Assert.IsTrue(_store.IsDone(HintId.MeetAgents));
            Assert.IsTrue(_store.IsDone(HintId.StartWork));
        }

        [Test]
        public void ActionDoneBeforeTheTriggerNeverShowsTheHintLater()
        {
            _coach.Observe(OnboardingEvent.FocusEntered);  // user found Focus on their own
            _coach.Observe(OnboardingEvent.TaskCompleted);
            Assert.IsFalse(_coach.IsPending(HintId.FocusMode));
            Assert.IsTrue(_store.IsDone(HintId.FocusMode));
        }

        [Test]
        public void ResetProgressReplaysTheTour()
        {
            Skip(HintId.MeetAgents);
            _coach.ResetProgress();
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(2.5);
            Assert.AreEqual(HintId.MeetAgents, Cur());
        }

        // ---- the full first-use flow ----

        [Test]
        public void FullFlow_ParticipantWalksTheWholeStepGraph()
        {
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(2.5);
            Assert.AreEqual(HintId.MeetAgents, Cur());
            _coach.Observe(OnboardingEvent.AgentTouched);
            Run(4);
            Assert.AreEqual(HintId.StartWork, Cur());
            _coach.Observe(OnboardingEvent.TaskStarted);
            Assert.IsNull(_coach.Current);

            _coach.Observe(OnboardingEvent.DecisionRequested);
            Run(4);
            Assert.AreEqual(HintId.PaperArrives, Cur());
            Ctx(busy: true);
            _coach.Observe(OnboardingEvent.PaperGrabbed);
            Assert.IsNull(_coach.Current);
            Run(4);
            Assert.AreEqual(HintId.InspectPaper, Cur());
            _coach.Observe(OnboardingEvent.PaperDetailsOpened);
            Run(4);
            Assert.AreEqual(HintId.StampDecision, Cur());
            _coach.Observe(OnboardingEvent.DecisionSent);
            Ctx();

            _coach.Observe(OnboardingEvent.DecisionResolvedApproved);
            Run(4);
            Assert.AreEqual(HintId.WatchHandoff, Cur());
            _coach.Observe(OnboardingEvent.HandoffObserved);

            _coach.Observe(OnboardingEvent.TaskCompleted);
            Run(4);
            Assert.AreEqual(HintId.ReceiptDelivered, Cur());
            Ctx(flags: OnboardingFlags.Spatial);
            _coach.Observe(OnboardingEvent.ReceiptViewed);
            Run(12);
            Assert.AreEqual(HintId.MoveWorkspace, Cur());
            _coach.Observe(OnboardingEvent.WorkspaceMoved);
            Run(4);
            Assert.AreEqual(HintId.FocusMode, Cur());
        }

        // ---- recovery ----

        [Test]
        public void RecoveryHintPreemptsAnOnboardingHint()
        {
            Skip(HintId.MeetAgents, HintId.StartWork);
            _coach.Observe(OnboardingEvent.DecisionRequested);
            Run(1.25);
            Assert.AreEqual(HintId.PaperArrives, Cur());
            _coach.Observe(OnboardingEvent.PaperDropped);
            Run(1.25);
            Assert.AreEqual(HintId.RecoverDroppedPaper, Cur());
            Assert.IsTrue(_coach.IsPending(HintId.PaperArrives)); // given back, shown later
        }

        [Test]
        public void RecoveryHintEndsWhenThePaperIsGrabbedAgain()
        {
            Skip(HintId.MeetAgents, HintId.StartWork, HintId.PaperArrives);
            _coach.Observe(OnboardingEvent.PaperDropped);
            Run(1);
            Assert.AreEqual(HintId.RecoverDroppedPaper, Cur());
            _coach.Observe(OnboardingEvent.PaperGrabbed);
            Assert.IsNull(_coach.Current);
            Assert.IsFalse(_store.IsDone(HintId.RecoverDroppedPaper)); // recovery is never "done"
        }

        [Test]
        public void RecoveryHintIsCappedPerLifetime()
        {
            for (int n = 0; n < 5; n++)
            {
                _coach.Observe(OnboardingEvent.PaperDropped);
                Run(1);
                Assert.AreEqual(HintId.RecoverDroppedPaper, Cur(), "show " + n);
                _coach.Observe(OnboardingEvent.PaperReturned);
                Run(1);
            }
            _coach.Observe(OnboardingEvent.PaperDropped);
            Run(2);
            Assert.IsNull(_coach.Current);
        }

        [Test]
        public void TrackingLostWhileHoldingShowsRecoveryEvenWithBusyHands()
        {
            Skip(HintId.MeetAgents, HintId.StartWork);
            Ctx(busy: true);
            _coach.Observe(OnboardingEvent.TrackingLostWhileHolding);
            Run(1);
            Assert.AreEqual(HintId.RecoverTrackingLost, Cur());
            _coach.Observe(OnboardingEvent.TrackingRegained);
            Assert.IsNull(_coach.Current);
        }

        [Test]
        public void RecoveryHintsRespectFocus()
        {
            Ctx(focus: true);
            _coach.Observe(OnboardingEvent.WorkspaceMovedByAccident);
            Run(5);
            Assert.IsNull(_coach.Current);
        }

        [Test]
        public void UndoAttemptShowsHonestNoUndoCopy()
        {
            Skip(HintId.MeetAgents, HintId.StartWork);
            _coach.Observe(OnboardingEvent.UndoAttempted);
            Run(1);
            Assert.AreEqual(HintId.RecoverNoUndo, Cur());
            StringAssert.Contains("isn't available", _coach.Current.Text);
        }

        // ---- reduced motion and events ----

        [Test]
        public void ReducedMotionTurnsAnimationOff()
        {
            Assert.IsTrue(_coach.Animate);
            Ctx(reduced: true);
            Assert.IsFalse(_coach.Animate);
        }

        [Test]
        public void ShownAndEndedEventsFireWithReasons()
        {
            int shown = 0; HintEndReason last = HintEndReason.TimedOut;
            _coach.HintShown += d => shown++;
            _coach.HintEnded += (d, r) => last = r;
            _coach.Observe(OnboardingEvent.SessionReady);
            Run(2.5);
            Assert.AreEqual(1, shown);
            _coach.Observe(OnboardingEvent.AgentTouched);
            Assert.AreEqual(HintEndReason.Completed, last);
        }
    }
}
