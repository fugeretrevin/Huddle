// =============================================================================
// MuseDesk · EditMode tests · Living Office follow-ups: the plan-paper stamp gate, deferred completion nods, the plan picture
// rebuilt from manager/status. Not proven here: the held stamp in a hand, the scene, the backend, the Quest.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class LivingOfficeFollowupTests
    {
        // ── physical stamp on the plan paper ──

        [Test]
        public void StampGate_FiresOnceAfterDwell_WhenHeldArmedAndInContact()
        {
            var g = new PlanStampGate();
            Assert.IsFalse(g.Tick(0.05f, true, true, 0.02f));
            Assert.IsFalse(g.Tick(0.05f, true, true, 0.02f));
            Assert.IsTrue(g.Tick(0.05f, true, true, 0.02f), "0.15 s of contact is past the 0.12 s dwell");
            Assert.IsFalse(g.Tick(0.5f, true, true, 0.02f), "the cooldown stops a second press");
        }

        [Test]
        public void StampGate_NeverFires_WhenNotHeld_NotArmed_OrOutOfRange()
        {
            var g = new PlanStampGate();
            for (int i = 0; i < 20; i++)
            {
                Assert.IsFalse(g.Tick(0.1f, false, true, 0.01f), "not held (a stamp lying on the pad)");
                Assert.IsFalse(g.Tick(0.1f, true, false, 0.01f), "not armed (nothing awaits the stamp, or a commit is in flight)");
                Assert.IsFalse(g.Tick(0.1f, true, true, 0.20f), "ink head away from the paper");
            }
        }

        [Test]
        public void StampGate_BrushPast_DoesNotFire_AndLeavingRestartsTheDwell()
        {
            var g = new PlanStampGate();
            Assert.IsFalse(g.Tick(0.08f, true, true, 0.02f));
            Assert.IsFalse(g.Tick(0.05f, true, true, 0.30f), "left the spot");
            Assert.IsFalse(g.Tick(0.08f, true, true, 0.02f), "the dwell starts again from zero");
            Assert.IsTrue(g.Tick(0.08f, true, true, 0.02f));
        }

        [Test]
        public void StampGate_FiresAgainAfterCooldown()
        {
            var g = new PlanStampGate();
            Assert.IsTrue(g.Tick(0.2f, true, true, 0f));
            Assert.IsFalse(g.Tick(0.5f, true, true, 0f));
            Assert.IsFalse(g.Tick(0.4f, true, true, 0f), "still cooling down (0.1 s left)");
            Assert.IsTrue(g.Tick(0.2f, true, true, 0f), "cooldown over, contact held past the dwell");
        }

        // ── completion nods that wait for the agent to be home ──

        [Test]
        public void Nod_WaitsWhileAway_ThenPlaysOnceWhenHome()
        {
            var n = new DeferredNods(4);
            Assert.IsTrue(n.Enqueue(1, 10f));
            Assert.IsFalse(n.TryRelease(1, 12f, false), "still on the pick-up trip");
            Assert.IsFalse(n.TryRelease(1, 25f, false), "15 s, still bounded");
            Assert.IsTrue(n.TryRelease(1, 29f, true), "home and free at 19 s");
            Assert.IsFalse(n.TryRelease(1, 30f, true), "played once");
            Assert.AreEqual(1, n.Released);
            Assert.AreEqual(0, n.Expired);
        }

        [Test]
        public void Nod_IsDroppedAfterTheBound_NotPlayedLate()
        {
            var n = new DeferredNods(4);
            n.Enqueue(0, 0f);
            Assert.IsFalse(n.TryRelease(0, DeferredNods.MaxWaitSeconds + 0.5f, true), "past 20 s a nod would be stale");
            Assert.IsFalse(n.IsPending(0));
            Assert.AreEqual(1, n.Expired);
            Assert.AreEqual(0, n.Released);
        }

        [Test]
        public void Nod_OnePerAgent_AndAgentsAreIndependent()
        {
            var n = new DeferredNods(4);
            Assert.IsTrue(n.Enqueue(0, 1f));
            Assert.IsFalse(n.Enqueue(0, 2f), "a second completion while one waits adds nothing");
            Assert.IsTrue(n.Enqueue(2, 2f));
            Assert.IsTrue(n.TryRelease(2, 3f, true));
            Assert.IsTrue(n.IsPending(0));
            Assert.IsFalse(n.Enqueue(9, 1f), "bad agent index");
        }

        [Test]
        public void Nod_DropAndClear_PlayNothing()
        {
            var n = new DeferredNods(4);
            n.Enqueue(0, 0f); n.Enqueue(1, 0f);
            n.Drop(0);
            n.Clear();
            Assert.IsFalse(n.TryRelease(0, 1f, true));
            Assert.IsFalse(n.TryRelease(1, 1f, true));
            Assert.AreEqual(0, n.Released);
        }

        // ── plan picture rebuilt after a resync ──

        const string TwoPlans =
            "{\"session_id\":\"s1\",\"plans\":[" +
            "{\"plan_id\":\"mp-1\",\"title\":\"Login\",\"plan_status\":\"committed\",\"provenance\":\"SCRIPTED_DEMO\",\"total\":2,\"settled\":false," +
            "\"cards\":[{\"task_id\":\"t-a\",\"client_item_id\":\"a\",\"title\":\"Research\",\"agent_id\":\"researcher\",\"state\":\"completed\",\"revision\":3,\"result\":\"Notes done\"}," +
            "{\"task_id\":\"t-b\",\"client_item_id\":\"b\",\"title\":\"Implement\",\"agent_id\":\"developer\",\"state\":\"failed\",\"revision\":2,\"failure\":\"Compile error\"}]}," +
            "{\"plan_id\":\"mp-2\",\"title\":\"Open plan\",\"plan_status\":\"proposed\",\"total\":0,\"cards\":[]}" +
            "],\"counts\":{}}";

        [Test]
        public void ManagerStatus_ParsesCommittedPlansOnly_WithTheBackendsReasons()
        {
            List<WallParsedPlanStatus> plans;
            Assert.IsTrue(WallOfflineLogic.TryParseManagerStatus(TwoPlans, out plans));
            Assert.AreEqual(1, plans.Count, "a proposed plan has no cards and is not restored");
            Assert.AreEqual("mp-1", plans[0].planId);
            Assert.AreEqual(2, plans[0].cards.Count);
            Assert.AreEqual("Compile error", plans[0].cards[1].failureReason);
            Assert.AreEqual("Notes done", plans[0].cards[0].resultSummary);
        }

        [Test]
        public void ManagerStatus_RejectsNonStatusReplies_AndAcceptsAnEmptyOne()
        {
            List<WallParsedPlanStatus> plans;
            Assert.IsFalse(WallOfflineLogic.TryParseManagerStatus("", out plans));
            Assert.IsFalse(WallOfflineLogic.TryParseManagerStatus("{\"detail\":\"unknown session\"}", out plans));
            Assert.IsTrue(WallOfflineLogic.TryParseManagerStatus("{\"session_id\":\"s\",\"plans\":[]}", out plans));
            Assert.AreEqual(0, plans.Count);
        }

        static List<ManagerPrintJob> Jobs()
        {
            return new List<ManagerPrintJob>
            {
                new ManagerPrintJob { taskId = "t-a", agentId = "researcher", title = "Research" },
                new ManagerPrintJob { taskId = "t-b", agentId = "developer", title = "Implement" },
            };
        }

        [Test]
        public void Restore_RebuildsTheBoardAndTheManager_WithoutPrintingAnything()
        {
            List<WallParsedPlanStatus> plans;
            WallOfflineLogic.TryParseManagerStatus(TwoPlans, out plans);
            var flow = new ManagerPlanFlow();
            var board = new PlanBoard();
            WallParsedPlanStatus s = plans[0];
            board.AddCommitted(s.planId, Jobs());
            for (int i = 0; i < s.cards.Count; i++) board.Apply(s.cards[i]);
            Assert.IsTrue(flow.RestoreCommitted(s.planId, s.title, s.provenance, Jobs()));

            Assert.AreEqual(ManagerFlowState.Monitoring, flow.State);
            Assert.AreEqual("mp-1", flow.CommittedPlanId);
            ManagerPrintJob job;
            Assert.IsFalse(flow.TryDequeuePrint(out job), "the cards exist already: nothing prints again");
            Assert.AreEqual("completed", board.Find("t-a").state);
            Assert.IsTrue(board.Find("t-b").Failed);
            Assert.AreEqual("Compile error", board.Find("t-b").failureReason, "the failure reason is the backend's");

            string planId, key, problem;
            Assert.IsTrue(board.TryBeginRetry("t-b", out planId, out key, out problem), "Retry works again on the restored card");
            Assert.AreEqual("mp-1", planId);
            Assert.IsFalse(board.TryBeginRetry("t-a", out planId, out key, out problem), "a completed card cannot be retried");
        }

        [Test]
        public void Restore_NeverReplacesAPlanThatIsBeingReviewed()
        {
            var flow = new ManagerPlanFlow();
            string rid, key;
            Assert.IsTrue(flow.Request("Build the login flow", out rid, out key));
            Assert.IsFalse(flow.RestoreCommitted("mp-1", "Login", "SCRIPTED_DEMO", Jobs()));
            Assert.AreEqual(ManagerFlowState.Proposing, flow.State);
            Assert.IsNull(flow.CommittedPlanId);
        }

        [Test]
        public void Restore_NeedsACommittedPlanWithCards()
        {
            var flow = new ManagerPlanFlow();
            Assert.IsFalse(flow.RestoreCommitted("", "t", "p", Jobs()));
            Assert.IsFalse(flow.RestoreCommitted("mp-1", "t", "p", new List<ManagerPrintJob>()));
            Assert.AreEqual(ManagerFlowState.Idle, flow.State);
        }
    }
}
#endif
