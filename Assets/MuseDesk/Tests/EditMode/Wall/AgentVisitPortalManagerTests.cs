// =============================================================================
// MuseDesk · EditMode tests · Select Agents / desk visit, /btw client session, large-agent portal,
// Manager plan flow, and the AgentSelect hand claim. Pure logic. Not proven here: ISDK hands, the
// backend round trip (backend/tests/phase3 covers the server side), visuals.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class AgentVisitPortalManagerTests
    {
        // ── presentation registry ──

        [Test]
        public void Registry_OneAwayAtATime_OnePresentationPerAgent()
        {
            var reg = new AgentPresentationRegistry();
            string why;
            Assert.IsTrue(reg.TryBegin("developer", AgentPresentation.CarriedProxy, out why));
            Assert.IsFalse(reg.TryBegin("developer", AgentPresentation.LargePortal, out why), "visit and portal are exclusive per agent");
            Assert.IsFalse(reg.TryBegin("tester", AgentPresentation.CarriedProxy, out why), "global cap 1");
            StringAssert.Contains("One visit", why);
            reg.End("developer");
            Assert.IsTrue(reg.TryBegin("tester", AgentPresentation.LargePortal, out why));
        }

        // ── select + desk visit ──

        static AgentVisitFlow Visit(out AgentPresentationRegistry reg) { reg = new AgentPresentationRegistry(); return new AgentVisitFlow(reg); }

        static void Tick(AgentVisitFlow f, float seconds) { for (float t = 0; t < seconds; t += 0.02f) f.Tick(0.02f); }

        [Test]
        public void Grab_OnlyWhenArmed()
        {
            AgentPresentationRegistry reg;
            var f = Visit(out reg);
            Assert.IsFalse(f.Grab("developer"), "outside Select Agents an agent can never be yanked");
            Assert.AreEqual(AgentPresentation.Home, reg.Of("developer"));
            Assert.IsTrue(f.Arm());
            Assert.IsTrue(f.Grab("developer"));
            Assert.AreEqual(AgentPresentation.CarriedProxy, reg.Of("developer"));
        }

        [Test]
        public void ValidDrop_Perches_ThenReturn_GoesHome()
        {
            AgentPresentationRegistry reg;
            var f = Visit(out reg);
            f.Arm(); f.Grab("developer");
            f.Release(true);
            Assert.AreEqual(VisitState.Perched, f.State);
            Assert.AreEqual(AgentPresentation.DeskVisit, reg.Of("developer"));
            f.Return();
            Assert.AreEqual(VisitState.Returning, f.State);
            Tick(f, 1f);
            Assert.AreEqual(VisitState.Off, f.State);
            Assert.AreEqual(AgentPresentation.Home, reg.Of("developer"));
        }

        [Test]
        public void InvalidDrop_And_TrackingLoss_NeverPerch()
        {
            AgentPresentationRegistry reg;
            var f = Visit(out reg);
            f.Arm(); f.Grab("tester");
            f.Release(false);
            Assert.AreEqual(VisitState.Returning, f.State);
            Assert.AreEqual(VisitEndReason.InvalidDrop, f.LastEnd);
            Tick(f, 1f);
            f.Arm(); f.Grab("tester");
            f.CancelCarry(VisitEndReason.TrackingLost);
            Assert.AreEqual(VisitState.Returning, f.State);
            Assert.AreEqual(VisitEndReason.TrackingLost, f.LastEnd);
            Tick(f, 1f);
            Assert.AreEqual(AgentPresentation.Home, reg.Of("tester"));
        }

        [Test]
        public void Armed_TimesOut_AndInterruptionsReturnFromEveryState()
        {
            AgentPresentationRegistry reg;
            var f = Visit(out reg);
            f.Arm();
            Tick(f, f.settings.armedTimeout + 0.5f);
            Assert.AreEqual(VisitState.Off, f.State);
            f.Arm(); f.Grab("researcher"); f.Release(true);
            f.Return(VisitEndReason.Paused);
            Tick(f, 1f);
            Assert.AreEqual(VisitState.Off, f.State);
            Assert.AreEqual(0, reg.AwayCount);
        }

        [Test]
        public void SecondVisit_RefusedWhilePerched()
        {
            AgentPresentationRegistry reg;
            var f = Visit(out reg);
            f.Arm(); f.Grab("researcher"); f.Release(true);
            Assert.IsFalse(f.Arm());
            Assert.AreEqual(VisitState.Perched, f.State);
        }

        [Test]
        public void HandOverToPortal_EndsVisit_PortalTakesTheAgent()
        {
            var reg = new AgentPresentationRegistry();
            var f = new AgentVisitFlow(reg);
            var p = new PortalFlow(reg);
            f.Arm(); f.Grab("developer"); f.Release(true);
            Assert.IsFalse(p.BeginPreview("developer", new PortalCandidate { valid = true, roomChecked = true }), "still on the desk");
            string agent;
            Assert.IsTrue(f.HandOverToPortal(out agent));
            Assert.AreEqual("developer", agent);
            Assert.IsTrue(p.BeginPreview(agent, new PortalCandidate { valid = true, roomChecked = true }));
            Assert.AreEqual(AgentPresentation.LargePortal, reg.Of("developer"));
        }

        // ── /btw session ──

        [Test]
        public void SideSession_OneInFlight_LateRepliesIgnored_HistoryCapped()
        {
            var s = new SideConversationSession("developer", "visit-1") { maxHistory = 3 };
            string id, key;
            Assert.IsFalse(s.TryBegin("  ", 0f, out id, out key));
            Assert.IsTrue(s.TryBegin("Why OAuth?", 0f, out id, out key));
            StringAssert.StartsWith("btw-", key);
            string id2, key2;
            Assert.IsFalse(s.TryBegin("And?", 0f, out id2, out key2), "one question at a time");
            Assert.IsFalse(s.Complete(new SideExchange { requestId = "someone-else", status = "answered" }));
            Assert.IsTrue(s.Complete(new SideExchange { requestId = id, status = "answered", reply = "Because.", provenance = "TASK_SNAPSHOT", contextRevision = 4 }));
            Assert.AreEqual(4, s.contextRevision);
            Assert.AreEqual("Why OAuth?", s.History[0].question);
            for (int i = 0; i < 5; i++) { s.TryBegin("q" + i, 0f, out id, out key); s.Complete(new SideExchange { requestId = id, status = "answered" }); }
            Assert.AreEqual(3, s.History.Count);
        }

        [Test]
        public void SideSession_Timeout_SaysWorkIsUnaffected()
        {
            var s = new SideConversationSession("tester", "v");
            string id, key;
            s.TryBegin("status?", 0f, out id, out key);
            Assert.IsFalse(s.CheckTimeout(1f));
            Assert.IsTrue(s.CheckTimeout(s.clientTimeout + 1f));
            Assert.AreEqual(SideAskState.Failed, s.State);
            StringAssert.Contains("not affected", s.History[0].reply);
        }

        // ── portal placement ──

        static SyntheticRoomGeometry EmptyRoom(float ceiling = 2.5f) { return SyntheticRoomGeometry.Rectangle(0, 0, 5, 5, 0, ceiling); }
        static SeatedUser User() { return new SeatedUser(new Vector3(0, 1.2f, 0), Vector3.forward); }

        [Test]
        public void Portal_FindsClearSpotsBesideTheUser_NotInFrontNotBehind()
        {
            var s = new PortalSettings();
            var list = PortalPlacementEvaluator.Find(EmptyRoom(), User(), s);
            Assert.Greater(list.Count, 0);
            foreach (var c in list)
            {
                Vector3 d = c.position - new Vector3(0, c.position.y, 0);
                float ang = Vector3.Angle(Vector3.forward, d.normalized);
                Assert.GreaterOrEqual(ang, s.frontConeDeg - 1e-3f);
                Assert.LessOrEqual(ang, s.maxSideDeg + 1e-3f);
                Assert.AreEqual(0f, c.position.y, 1e-4f, "stands on the floor");
                Assert.That(c.avatarHeight >= s.minAvatarHeight && c.avatarHeight <= s.maxAvatarHeight);
            }
        }

        [Test]
        public void Portal_Furniture_LowCeiling_InFront_AreRefused()
        {
            var s = new PortalSettings();
            var room = EmptyRoom();
            room.AddFurniture("CHAIR", new Vector3(1.15f, 0.45f, 0f), new Vector3(0.5f, 0.9f, 0.5f));
            Assert.AreEqual(PortalProblem.HitsFurniture, PortalPlacementEvaluator.Evaluate(room, new Vector3(1.15f, 0, 0), User(), s).problem);
            Assert.AreEqual(PortalProblem.LowCeiling, PortalPlacementEvaluator.Evaluate(EmptyRoom(0.95f), new Vector3(1.15f, 0, 0), User(), s).problem);
            Assert.AreEqual(PortalProblem.InFrontOfUser, PortalPlacementEvaluator.Evaluate(EmptyRoom(), new Vector3(0, 0, 1.2f), User(), s).problem);
            Assert.AreEqual(PortalProblem.TooCloseToUser, PortalPlacementEvaluator.Evaluate(EmptyRoom(), new Vector3(0.6f, 0, 0), User(), s).problem);
            Assert.AreEqual(PortalProblem.NearWall, PortalPlacementEvaluator.Evaluate(EmptyRoom(), new Vector3(2.4f, 0, 0), User(), s).problem);
            PortalCandidate mid = PortalPlacementEvaluator.Evaluate(EmptyRoom(1.3f), new Vector3(1.15f, 0, 0), User(), s);
            Assert.IsTrue(mid.valid, mid.reason);
            Assert.AreEqual(1.3f - s.headRoom, mid.avatarHeight, 1e-4f, "scaled down to fit under a low ceiling");
        }

        [Test]
        public void Portal_NoRoom_OnlyTheSmallConfirmedFallback()
        {
            var room = EmptyRoom();
            room.forcedAvailability = RoomAvailability.NoRoomSetup;
            Assert.AreEqual(0, PortalPlacementEvaluator.Find(room, User(), new PortalSettings()).Count);
            PortalCandidate m = PortalPlacementEvaluator.ManualSmall(User(), 70f, new PortalSettings());
            Assert.IsFalse(m.roomChecked);
            Assert.Less(m.avatarHeight, 0.5f);
        }

        [Test]
        public void PortalFlow_ConfirmOnlyOnValid_InterruptFreesEverything()
        {
            var reg = new AgentPresentationRegistry();
            var p = new PortalFlow(reg);
            Assert.IsTrue(p.BeginPreview("tester", new PortalCandidate { valid = false, reason = "Furniture is in the way" }));
            Assert.AreEqual(PortalState.Preview, p.State);
            Assert.IsFalse(p.Confirm());
            p.UpdateSpot(new PortalCandidate { valid = true, roomChecked = true });
            Assert.IsTrue(p.Confirm());
            for (float t = 0; t < 3f; t += 0.02f) p.Tick(0.02f);
            Assert.AreEqual(PortalState.Conversing, p.State);
            p.Interrupt("recentered");
            for (float t = 0; t < 1f; t += 0.02f) p.Tick(0.02f);
            Assert.AreEqual(PortalState.Hidden, p.State);
            Assert.AreEqual(AgentPresentation.Home, reg.Of("tester"));
        }

        [Test]
        public void PortalFlow_ComfortMode_IsShorter()
        {
            var p = new PortalFlow(new AgentPresentationRegistry()) { comfortMode = true };
            p.BeginPreview("developer", new PortalCandidate { valid = true, roomChecked = true });
            p.Confirm();
            float t = 0f;
            while (p.State != PortalState.Conversing && t < 5f) { p.Tick(0.02f); t += 0.02f; }
            Assert.Less(t, (p.timings.opening + p.timings.emerging) * 0.6f);
        }

        // ── manager plan flow ──

        static List<ManagerPlanItemView> Plan(bool dangerousTest = false)
        {
            return new List<ManagerPlanItemView>
            {
                new ManagerPlanItemView { clientItemId = "a", title = "Investigate auth", kind = "research", proposedAgent = "researcher" },
                new ManagerPlanItemView { clientItemId = "b", title = "Implement auth", kind = "code", proposedAgent = "developer", dependsOn = new[] { "a" } },
                new ManagerPlanItemView { clientItemId = "c", title = dangerousTest ? "Deploy to production" : "Test auth", kind = "test", proposedAgent = "tester", dependsOn = new[] { "b" }, dangerous = dangerousTest, dangerReasons = dangerousTest ? new[] { "deploy" } : new string[0] },
            };
        }

        static ManagerPlanFlow Proposed(bool dangerous = false)
        {
            var m = new ManagerPlanFlow();
            string rid, key;
            Assert.IsTrue(m.Request("Build the login flow", out rid, out key));
            m.OnProposed("mp-1", 1, "Login", "TEMPLATE_PLAN", Plan(dangerous));
            return m;
        }

        [Test]
        public void Manager_ProposedPlan_NeverPrints()
        {
            var m = Proposed();
            m.OnBoardTask("task-1");
            ManagerPrintJob j;
            Assert.IsFalse(m.TryDequeuePrint(out j));
            StringAssert.Contains("Template plan", m.Message);
        }

        [Test]
        public void Manager_Commit_DependencyAndDangerRules()
        {
            var m = Proposed(true);
            Assert.IsFalse(m.Items[2].approved, "dangerous items start unticked");
            List<string> ap, au; int rev; string key, problem;
            Assert.IsTrue(m.TryBuildCommit(out ap, out au, out rev, out key, out problem), problem);
            CollectionAssert.AreEqual(new[] { "a", "b" }, ap);
            m.SetApproved("a", false);
            Assert.IsFalse(m.TryBuildCommit(out ap, out au, out rev, out key, out problem));
            StringAssert.Contains("needs", problem);
            m.SetApproved("a", true);
            m.SetApproved("c", true);
            Assert.IsFalse(m.TryBuildCommit(out ap, out au, out rev, out key, out problem));
            StringAssert.Contains("explicit OK", problem);
            m.Authorize("c");
            Assert.IsTrue(m.TryBuildCommit(out ap, out au, out rev, out key, out problem), problem);
            CollectionAssert.AreEqual(new[] { "c" }, au);
            string key2;
            m.TryBuildCommit(out ap, out au, out rev, out key2, out problem);
            Assert.AreEqual(key, key2, "a retry of the same selection reuses the idempotency key");
            Assert.AreEqual(1, rev);
        }

        [Test]
        public void Manager_PrintsOnlyCommittedIds_SeenOnTheBoard_Once()
        {
            var m = Proposed();
            m.BeginCommit();
            m.OnCommitted(2, new List<ManagerPrintJob>
            {
                new ManagerPrintJob { taskId = "t-a", clientItemId = "a", agentId = "researcher" },
                new ManagerPrintJob { taskId = "t-b", clientItemId = "b", agentId = "developer" },
            });
            ManagerPrintJob j;
            Assert.IsFalse(m.TryDequeuePrint(out j), "not on the board yet");
            m.OnBoardTask("t-b");
            m.OnBoardTask("t-b");
            m.OnBoardTask("someone-elses-task");
            Assert.IsTrue(m.TryDequeuePrint(out j));
            Assert.AreEqual("t-b", j.taskId);
            Assert.AreEqual("Implement auth", j.title);
            Assert.IsFalse(m.TryDequeuePrint(out j), "printed once");
            m.OnBoardTask("t-a");
            Assert.IsTrue(m.TryDequeuePrint(out j));
            Assert.IsFalse(m.TryDequeuePrint(out j));
            Assert.AreEqual(ManagerFlowState.Monitoring, m.State);
        }

        [Test]
        public void Manager_CommitFailures_CreateNothing()
        {
            var m = Proposed();
            m.BeginCommit();
            m.OnCommitFailed("REVISION_CONFLICT", "stale");
            Assert.AreEqual(ManagerFlowState.Failed, m.State);
            Assert.AreEqual(0, m.CommittedCount);
            var m2 = Proposed();
            m2.BeginCommit();
            m2.OnCommitFailed("AUTHORIZATION_REQUIRED", "needs ok");
            Assert.AreEqual(ManagerFlowState.AwaitingApproval, m2.State);
            m2.Reject();
            Assert.AreEqual(ManagerFlowState.Rejected, m2.State);
        }

        // ── AgentSelect claim respects the existing priorities ──

        static AimHandSample Hand(bool pinch, bool holding = false, bool near = false)
        {
            return new AimHandSample { tracked = true, pinching = pinch, holding = holding, nearHover = near };
        }

        [Test]
        public void AgentSelectClaim_NeverTakesAPinchThatBelongsToAPaper()
        {
            var arb = new HandOwnershipArbiter(new HandClaimRegistry());
            for (int i = 0; i < 40; i++) arb.Tick(1f / 72f, Hand(false), Hand(false, false, true));      // right hand hovers a paper
            bool ready = false;
            for (int i = 0; i < 40; i++) { arb.Tick(1f / 72f, Hand(false), Hand(true, false, true)); ready |= arb.Signal(1) == HandGateSignal.Ready; }
            Assert.IsFalse(ready);
            Assert.IsFalse(arb.TryClaim(1, HandClaim.AgentSelect));

            var arb2 = new HandOwnershipArbiter(new HandClaimRegistry());
            for (int i = 0; i < 40; i++) arb2.Tick(1f / 72f, Hand(false), Hand(false));
            bool claimed = false;
            for (int i = 0; i < 40 && !claimed; i++) { arb2.Tick(1f / 72f, Hand(false), Hand(true)); if (arb2.Signal(1) == HandGateSignal.Ready) claimed = arb2.TryClaim(1, HandClaim.AgentSelect); }
            Assert.IsTrue(claimed);
            Assert.AreEqual(HandOwner.AgentSelect, arb2.Owner(1));
            arb2.Tick(1f / 72f, Hand(false), Hand(true, true));       // picks something up
            Assert.AreEqual(HandGateSignal.Cancelled, arb2.Signal(1), "holding something cancels the select carry");
        }
    }
}
#endif
