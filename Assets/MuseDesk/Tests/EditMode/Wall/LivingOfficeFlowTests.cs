// =============================================================================
// MuseDesk · EditMode tests · Living Office pure logic: PlanBoard (real card states, blocked/failed reasons, Retry lock
// and idempotency key, receipt), the plan-paper mapper and pager, the Manager flow's revise path and commit event,
// and the offline roll-up / reason words. Not proven here: the drawn paper, the backend, the Quest.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using MuseDesk.Spatial.Ui;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class LivingOfficeFlowTests
    {
        static List<ManagerPrintJob> Jobs()
        {
            return new List<ManagerPrintJob>
            {
                new ManagerPrintJob { taskId = "t-a", clientItemId = "a", agentId = "researcher", title = "Research: login" },
                new ManagerPrintJob { taskId = "t-b", clientItemId = "b", agentId = "developer", title = "Implement: login" },
                new ManagerPrintJob { taskId = "t-c", clientItemId = "c", agentId = "tester", title = "Test: login" },
            };
        }

        static PlanBoard Board()
        {
            var b = new PlanBoard();
            Assert.AreEqual(3, b.AddCommitted("mp-1", Jobs()));
            return b;
        }

        // ── cards come only from a commit; updates only from real reports ──

        [Test]
        public void Cards_ExistOnlyForCommittedIds_AndAreNotDuplicatedByTheStreamEvent()
        {
            var b = Board();
            Assert.AreEqual(0, b.AddCommitted("mp-1", Jobs()), "the commit reply and manager.plan_committed carry the same ids");
            Assert.AreEqual(3, b.Count);
            Assert.AreEqual(0, b.AddCommitted("mp-1", new List<ManagerPrintJob> { new ManagerPrintJob { taskId = null, agentId = "tester" } }), "no id, no card");
            Assert.IsFalse(b.Apply(PlanCardUpdate.Of("t-other", "running", 1)), "a task that is not a plan card is ignored");
            Assert.IsNull(b.Find("t-other"));
        }

        [Test]
        public void StaleRevision_IsIgnored_NewerOneWins()
        {
            var b = Board();
            Assert.IsTrue(b.Apply(PlanCardUpdate.Of("t-a", "running", 2)));
            Assert.IsFalse(b.Apply(PlanCardUpdate.Of("t-a", "queued", 1)));
            Assert.AreEqual("running", b.Find("t-a").state);
            Assert.IsTrue(b.Apply(PlanCardUpdate.Of("t-a", "completed", 3)));
            Assert.AreEqual("completed", b.Find("t-a").state);
        }

        [Test]
        public void BlockedAndFailedReasons_AreTheBackendsWords_AndClearWhenTheStateMovesOn()
        {
            var b = Board();
            var fail = PlanCardUpdate.Of("t-a", "failed", 2);
            fail.failureReason = "The model could not work this step.";
            b.Apply(fail);
            var blocked = PlanCardUpdate.Of("t-b", "blocked", 0);
            blocked.blockedReason = "Blocked: it depends on 'Research: login' (failed). Retry the failed step to continue.";
            blocked.blockedBy = new[] { "t-a" };
            b.Apply(blocked);

            Assert.AreEqual("FAILED: The model could not work this step.", PlanBoard.StatusWords(b.Find("t-a")));
            Assert.AreEqual(blocked.blockedReason, PlanBoard.StatusWords(b.Find("t-b")));
            Assert.IsTrue(b.Find("t-a").CanRetry);
            Assert.IsFalse(b.Find("t-b").CanRetry, "only a failed card offers Retry");

            // No reason reported: say so, never make one up.
            b.Apply(PlanCardUpdate.Of("t-c", "blocked", 0));
            Assert.AreEqual("Blocked (no reason was reported)", PlanBoard.StatusWords(b.Find("t-c")));

            b.Apply(PlanCardUpdate.Of("t-a", "queued", 3));
            Assert.AreEqual("", b.Find("t-a").failureReason, "the failure text does not outlive the failed state");
            b.Apply(PlanCardUpdate.Of("t-b", "queued", 1));
            Assert.AreEqual("", b.Find("t-b").blockedReason);
            Assert.AreEqual(0, b.Find("t-b").blockedBy.Length);
        }

        // ── retry ──

        [Test]
        public void Retry_OnlyForAFailedCard_OneInFlight_KeyStableUntilADefinitiveAnswer()
        {
            var b = Board();
            string plan, key, problem;
            Assert.IsFalse(b.TryBeginRetry("t-a", out plan, out key, out problem), "queued cards cannot be retried");
            Assert.IsFalse(b.TryBeginRetry("nope", out plan, out key, out problem));
            b.Apply(PlanCardUpdate.Of("t-a", "failed", 2));

            Assert.IsTrue(b.TryBeginRetry("t-a", out plan, out key, out problem));
            Assert.AreEqual("mp-1", plan);
            Assert.GreaterOrEqual(key.Length, 8, "the backend requires a key of at least 8 characters");
            Assert.IsTrue(b.Find("t-a").retryInFlight);
            Assert.IsFalse(b.Find("t-a").CanRetry);
            string plan2, key2;
            Assert.IsFalse(b.TryBeginRetry("t-a", out plan2, out key2, out problem), "a second poke while one is in flight is ignored");
            Assert.AreEqual("Retry already sent", problem);

            // No answer (transport): the lock is released, the SAME key is reused so the backend can deduplicate.
            b.EndRetry("t-a", false, false, "Backend unreachable");
            Assert.AreEqual("Backend unreachable", b.Find("t-a").retryMessage);
            Assert.IsTrue(b.TryBeginRetry("t-a", out plan2, out key2, out problem));
            Assert.AreEqual(key, key2);

            // Accepted: the card is still shown failed until the stream says otherwise.
            b.EndRetry("t-a", true, true, null);
            Assert.AreEqual("failed", b.Find("t-a").state, "the wall waits for the backend's state, it does not predict it");
            b.Apply(PlanCardUpdate.Of("t-a", "queued", 3));
            Assert.IsFalse(b.Find("t-a").CanRetry);

            // It fails again at a new revision: a new command, a new key.
            b.Apply(PlanCardUpdate.Of("t-a", "failed", 5));
            string key3;
            Assert.IsTrue(b.TryBeginRetry("t-a", out plan2, out key3, out problem));
            Assert.AreNotEqual(key, key3);
        }

        [Test]
        public void Retry_Refused_IsDefinitive_AndShowsTheBackendsMessage()
        {
            var b = Board();
            b.Apply(PlanCardUpdate.Of("t-b", "failed", 4));
            string plan, key, key2, problem;
            Assert.IsTrue(b.TryBeginRetry("t-b", out plan, out key, out problem));
            b.EndRetry("t-b", true, false, "only a failed card can be retried (it is queued)");
            Assert.AreEqual("only a failed card can be retried (it is queued)", b.Find("t-b").retryMessage);
            Assert.IsTrue(b.TryBeginRetry("t-b", out plan, out key2, out problem));
            Assert.AreNotEqual(key, key2, "a refusal is an answer: the next press is a new command");
        }

        // ── delivery, settle, receipt ──

        [Test]
        public void Delivery_MovesOnlyThatAgentsCards()
        {
            var b = Board();
            Assert.AreEqual(1, b.TrayCount("developer"));
            Assert.AreEqual(1, b.MoveCards("developer", CardDelivery.InTray, CardDelivery.Carried));
            Assert.AreEqual(0, b.TrayCount("developer"));
            Assert.AreEqual(1, b.TrayCount("tester"));
            Assert.AreEqual(0, b.MoveCards("developer", CardDelivery.InTray, CardDelivery.Carried), "nothing left to pick up");
            Assert.AreEqual(1, b.MoveCards("developer", CardDelivery.Carried, CardDelivery.OnDesk));
            Assert.AreEqual(CardDelivery.OnDesk, b.Find("t-b").delivery);
            Assert.IsFalse(b.Find("t-b").deliveredByChute);
            b.MoveCards("tester", CardDelivery.InTray, CardDelivery.OnDesk, true);
            Assert.IsTrue(b.Find("t-c").deliveredByChute);
        }

        [Test]
        public void Settled_MatchesTheBackendRule_AndTheReceiptAppearsOncePerArtifact_OnlyForOurCards()
        {
            var b = Board();
            Assert.IsFalse(b.Settled("mp-1"));
            b.Apply(PlanCardUpdate.Of("t-a", "completed", 2));
            b.Apply(PlanCardUpdate.Of("t-b", "failed", 2));
            var blocked = PlanCardUpdate.Of("t-c", "blocked", 0);
            blocked.blockedReason = "waiting"; blocked.blockedBy = new string[0];
            b.Apply(blocked);
            Assert.IsFalse(b.Settled("mp-1"), "blocked while merely waiting is not settled");
            blocked.blockedBy = new[] { "t-b" };
            b.Apply(blocked);
            Assert.IsTrue(b.Settled("mp-1"), "finished, or blocked behind a failure");
            Assert.IsFalse(b.Settled("mp-unknown"));

            Assert.IsFalse(b.OnSynthesis("art-x", "t-not-ours", "Plan summary"), "a completion artifact of another task is not this plan's receipt");
            Assert.AreEqual(ReceiptStage.None, b.Receipt.stage);
            Assert.IsTrue(b.OnSynthesis("art-1", "t-a", "Plan summary: 1 of 3 steps completed"));
            Assert.AreEqual(ReceiptStage.Ready, b.Receipt.stage);
            Assert.AreEqual("mp-1", b.Receipt.planId);
            Assert.IsFalse(b.OnSynthesis("art-1", "t-a", "again"), "once per artifact");
            b.SetReceiptStage(ReceiptStage.ComingDown);
            b.SetReceiptStage(ReceiptStage.OnDesk);
            Assert.AreEqual(ReceiptStage.OnDesk, b.Receipt.stage);
            b.DismissReceipt();
            Assert.AreEqual(ReceiptStage.None, b.Receipt.stage);
            Assert.IsFalse(b.OnSynthesis("art-1", "t-a", "again"), "dismissed receipts do not come back");
            b.Reset();
            Assert.AreEqual(0, b.Count);
        }

        // ── Manager flow: revise + commit event ──

        static ManagerPlanFlow Proposed(out List<ManagerPlanItemView> items)
        {
            var f = new ManagerPlanFlow();
            string rid, key;
            Assert.IsTrue(f.Request("Build the login flow", out rid, out key));
            var plan = WallOfflineLogic.BuildTemplatePlan("Build the login flow", 1);
            items = plan.items;
            f.OnProposed(plan.planId, plan.revision, plan.title, plan.provenance, plan.items);
            f.SetPlanNotes(plan.objective, plan.assumptions, plan.risks);
            return f;
        }

        [Test]
        public void Revise_KeepsTickedSteps_RefusesBrokenDependencies_AndNeverTouchesTheBoard()
        {
            List<ManagerPlanItemView> items;
            var f = Proposed(out items);
            List<ManagerPlanItemView> kept; int rev; string key, problem;
            Assert.IsFalse(f.TryBuildRevision(out kept, out rev, out key, out problem), "nothing unticked: nothing to revise");
            f.SetApproved("b", false);
            Assert.IsFalse(f.TryBuildRevision(out kept, out rev, out key, out problem), "c depends on b");
            StringAssert.Contains("keep both or drop both", problem);
            f.SetApproved("c", false);
            Assert.IsTrue(f.TryBuildRevision(out kept, out rev, out key, out problem));
            Assert.AreEqual(1, kept.Count);
            Assert.AreEqual("a", kept[0].clientItemId);
            Assert.AreEqual(1, rev);
            string keyAgain;
            Assert.IsTrue(f.TryBuildRevision(out kept, out rev, out keyAgain, out problem));
            Assert.AreEqual(key, keyAgain, "same selection, same key");

            f.BeginRevise();
            Assert.AreEqual(ManagerFlowState.Revising, f.State);
            string r2, k2;
            Assert.IsFalse(f.Request("something else", out r2, out k2), "busy while revising");
            f.OnReviseFailed("UNREACHABLE", "Backend unreachable");
            Assert.AreEqual(ManagerFlowState.AwaitingApproval, f.State);
            Assert.AreEqual(3, f.Items.Count, "a failed revision changes nothing");
            Assert.IsTrue(f.TryBuildRevision(out kept, out rev, out keyAgain, out problem));
            Assert.AreEqual(key, keyAgain, "no answer: the retry reuses the key");

            f.BeginRevise();
            f.OnProposed(f.PlanId, 2, f.Title, f.Provenance, kept);
            Assert.AreEqual(ManagerFlowState.AwaitingApproval, f.State);
            Assert.AreEqual(2, f.Revision);
            Assert.AreEqual(1, f.Items.Count);
            Assert.AreEqual(0, f.CommittedCount, "revising creates no cards");
        }

        [Test]
        public void CommitEvent_AndCommitReply_ProduceOneSetOfCards_WhicheverComesFirst()
        {
            List<ManagerPlanItemView> items;
            var f = Proposed(out items);
            int raised = 0;
            f.Committed += (planId, tasks) => { raised++; Assert.AreEqual("demo-plan-1", planId); };
            List<string> approved, authorized; int rev; string key, problem;
            Assert.IsTrue(f.TryBuildCommit(out approved, out authorized, out rev, out key, out problem));
            f.BeginCommit();
            Assert.IsFalse(f.OnCommittedEvent("some-other-plan", 2, Jobs()), "another plan's commit is not ours");
            Assert.IsTrue(f.OnCommittedEvent("demo-plan-1", 2, Jobs()));
            Assert.AreEqual(ManagerFlowState.Delegating, f.State);
            f.OnCommitted(2, Jobs());                       // the HTTP reply arrives second: ignored
            Assert.AreEqual(1, raised);
            Assert.AreEqual(3, f.CommittedCount);
            Assert.AreEqual("demo-plan-1", f.CommittedPlanId);
            ManagerPrintJob job;
            Assert.IsFalse(f.TryDequeuePrint(out job), "nothing prints until the board has the card");
            f.OnBoardTask("t-b");
            Assert.IsTrue(f.TryDequeuePrint(out job));
            Assert.AreEqual("t-b", job.taskId);
        }

        // ── plan paper ──

        [Test]
        public void PaperModel_UsesOnlyRealFields_AndTheProvenanceIsNeverDefaultedToLive()
        {
            List<ManagerPlanItemView> items;
            var f = Proposed(out items);
            PlanModel m = PlanPaperMapper.ToModel(f);
            Assert.AreEqual(PlanState.Proposed, m.State);
            Assert.AreEqual("SCRIPTED_DEMO", m.Source);
            Assert.AreEqual(3, m.Items.Count);
            Assert.AreEqual("researcher", m.Items[0].ProposedAgent);
            CollectionAssert.AreEqual(new[] { "a" }, m.Items[1].DependsOn);
            Assert.AreEqual(2, m.Assumptions.Count);
            Assert.AreEqual(1, m.Risks.Count);
            PlanLayoutResult all = PlanReviewLayout.Layout(m, 180f, 0f, 0.5f);
            Assert.AreEqual("[SCRIPTED DEMO]", all.ProvenanceText);
            Assert.IsFalse(all.Overflowed);

            // A dangerous step shows up as a risk with the backend's reasons.
            var danger = new ManagerPlanFlow();
            string rid, key;
            danger.Request("Deploy the app to production", out rid, out key);
            var plan = WallOfflineLogic.BuildTemplatePlan("Deploy the app to production", 2);
            danger.OnProposed(plan.planId, plan.revision, plan.title, null, plan.items);
            PlanModel dm = PlanPaperMapper.ToModel(danger);
            Assert.IsTrue(dm.Risks.Exists(r => r.Contains("Step 2 needs your explicit OK") && r.Contains("deploy")));
            Assert.AreEqual("Source not stated", PlanReviewLayout.Layout(dm, 180f, 0f, 0.5f).ProvenanceText);
        }

        [Test]
        public void Pager_ShowsEveryRowExactlyOnce_InOrder_WithinThePageHeight_KeepingItemsTogether()
        {
            List<ManagerPlanItemView> items;
            var f = Proposed(out items);
            PlanLayoutResult all = PlanReviewLayout.Layout(PlanPaperMapper.ToModel(f), 180f, 0f, 0.5f);
            int rows = 0;
            foreach (PlanRow r in all.Rows) if (r.Kind != PlanRowKind.Provenance) rows++;

            List<PlanPage> one = PlanPaperPager.Paginate(all, 0f);
            Assert.AreEqual(1, one.Count, "no height limit: one page");
            Assert.AreEqual(rows, one[0].Rows.Count);

            const float pageH = 90f;
            List<PlanPage> pages = PlanPaperPager.Paginate(all, pageH);
            Assert.Greater(pages.Count, 1);
            var seen = new List<string>();
            foreach (PlanPage p in pages)
            {
                Assert.Greater(p.Rows.Count, 0);
                Assert.LessOrEqual(p.UsedMm, pageH + 1e-3f);
                Assert.AreEqual(0f, p.TopMm[0], 1e-4f, "a page starts at the top");
                for (int i = 1; i < p.Rows.Count; i++) Assert.Greater(p.TopMm[i], p.TopMm[i - 1]);
                Assert.AreNotEqual(PlanRowKind.ItemDetail, p.Rows[0].Kind, "an item's detail is not orphaned at the top of a page");
                Assert.AreNotEqual(PlanRowKind.ItemOutput, p.Rows[0].Kind);
                foreach (PlanRow r in p.Rows) { Assert.AreNotEqual(PlanRowKind.Provenance, r.Kind); seen.Add(r.Kind + "|" + r.FullText); }
            }
            var want = new List<string>();
            foreach (PlanRow r in all.Rows) if (r.Kind != PlanRowKind.Provenance) want.Add(r.Kind + "|" + r.FullText);
            CollectionAssert.AreEqual(want, seen);
            // The section head stays with the first item it introduces.
            foreach (PlanPage p in pages)
                Assert.IsFalse(p.Rows[p.Rows.Count - 1].Kind == PlanRowKind.Section && p.Rows.Count > 1 && p != pages[pages.Count - 1], "a section head is not left alone at the bottom");
        }

        [Test]
        public void Pager_ABlockTallerThanThePage_IsSplitBetweenRows_NothingLost()
        {
            var m = new PlanModel { Title = "T", Source = "TEMPLATE_PLAN", Items = new List<PlanItemModel>() };
            var it = new PlanItemModel { Id = "a", Title = "One step", ExpectedOutputs = new List<string>() };
            for (int i = 0; i < 12; i++) it.ExpectedOutputs.Add("Output number " + i);
            m.Items.Add(it);
            PlanLayoutResult all = PlanReviewLayout.Layout(m, 180f, 0f, 0.5f);
            List<PlanPage> pages = PlanPaperPager.Paginate(all, 40f);
            int n = 0;
            foreach (PlanPage p in pages) { n += p.Rows.Count; Assert.LessOrEqual(p.UsedMm, 40f + 1e-3f); }
            Assert.AreEqual(all.Rows.Count - 1, n);
            Assert.AreEqual(0, PlanPaperPager.Paginate(null, 40f).Count);
        }

        // ── offline words mirror the backend ──

        [Test]
        public void OfflineReasonsAndRollup_UseTheBackendsWording_AndOnlyRecordedFacts()
        {
            Assert.AreEqual("Blocked: it depends on 'Research: login' (failed). Retry the failed step to continue.",
                            WallOfflineLogic.BlockedByFailureReason(new List<string> { "Research: login" }));
            Assert.AreEqual("Waiting for 'Research: login' to finish.", WallOfflineLogic.BlockedWaitingReason(new List<string> { "Research: login" }));
            Assert.AreEqual("Waiting to be released.", WallOfflineLogic.BlockedWaitingReason(new List<string>()));
            StringAssert.StartsWith("[SCRIPTED DEMO]", WallOfflineLogic.ScriptedFailureReason);

            var steps = new List<WallOfflineLogic.RollupStep>
            {
                new WallOfflineLogic.RollupStep { title = "Research: login", agentId = "researcher", state = "completed", result = "Research write-up: scripted" },
                new WallOfflineLogic.RollupStep { title = "Implement: login", agentId = "developer", state = "failed", result = WallOfflineLogic.ScriptedFailureReason },
                new WallOfflineLogic.RollupStep { title = "Test: login", agentId = "tester", state = "blocked", result = null },
            };
            string summary, body;
            WallOfflineLogic.OfflineRollup("Plan: login", "Build the login flow", steps, out summary, out body);
            Assert.AreEqual("[SCRIPTED DEMO] Plan summary: 1 of 3 steps completed", summary);
            StringAssert.Contains("Result: 1 of 3 steps completed, 1 failed, 1 blocked, 0 cancelled.", body);
            StringAssert.Contains("3. [blocked] Test: login (tester): No result recorded.", body);
            StringAssert.Contains("Open issues", body);
            steps[1] = new WallOfflineLogic.RollupStep { title = "Implement: login", agentId = "developer", state = "completed", result = "ok" };
            steps[2] = new WallOfflineLogic.RollupStep { title = "Test: login", agentId = "tester", state = "completed", result = "ok" };
            WallOfflineLogic.OfflineRollup("Plan: login", "Build the login flow", steps, out summary, out body);
            Assert.IsFalse(body.Contains("Open issues"));
        }

        // rc14 (E2E gap G-1): a text-only result never reads as executed work.
        [Test]
        public void TextOnlyResults_SayWrittenNotRun()
        {
            Assert.IsTrue(PlanBoard.IsTextOnlyResult("Proposed change: a 12-character password validator"));
            Assert.IsTrue(PlanBoard.IsTextOnlyResult("Test plan: boundary cases"));
            Assert.IsTrue(PlanBoard.IsTextOnlyResult("Research write-up: NIST rules"));
            Assert.IsFalse(PlanBoard.IsTextOnlyResult("OAuth login scaffold complete; 6 passed / 0 failed"));
            Assert.IsFalse(PlanBoard.IsTextOnlyResult(""));
            var b = new PlanBoard();
            b.AddCommitted("p1", new List<ManagerPrintJob> { new ManagerPrintJob { taskId = "t-a", clientItemId = "a", agentId = "developer", title = "Implement" } });
            var u = PlanCardUpdate.Of("t-a", "completed", 2);
            u.resultSummary = "Proposed change: a 12-character password validator";
            b.Apply(u);
            Assert.AreEqual("Written, not run · Proposed change: a 12-character password validator", PlanBoard.StatusWords(b.Find("t-a")));
            Assert.AreEqual("8 of 8 written up (text only, nothing was run)", PlanBoard.ProgressWords(8, 8, 8, 0, 0));
            Assert.AreEqual("3 of 4 completed (2 as text only) · 1 running", PlanBoard.ProgressWords(3, 4, 2, 1, 0));
            Assert.AreEqual("2 of 3 completed · 1 FAILED", PlanBoard.ProgressWords(2, 3, 0, 0, 1));
        }

        [Test]
        public void ParsePlanStatus_ReadsReasonsAndSynthesis_AndRetryReply()
        {
            const string json = "{\"plan_id\":\"mp-1\",\"title\":\"Login\",\"plan_status\":\"committed\",\"provenance\":\"TEMPLATE_PLAN\",\"total\":2,"
                + "\"cards\":[{\"task_id\":\"t-a\",\"client_item_id\":\"a\",\"title\":\"Research\",\"agent_id\":\"researcher\",\"state\":\"failed\",\"revision\":3,\"depends_on\":[],\"failure\":\"The model could not work this step.\"},"
                + "{\"task_id\":\"t-b\",\"client_item_id\":\"b\",\"title\":\"Implement\",\"agent_id\":\"developer\",\"state\":\"blocked\",\"revision\":0,\"depends_on\":[\"t-a\"],\"blocked_reason\":\"Blocked: it depends on 'Research' (failed). Retry the failed step to continue.\",\"blocked_by\":[\"t-a\"]}],"
                + "\"settled\":true,\"synthesis\":{\"artifact_id\":\"art-9\",\"task_id\":\"t-a\",\"mode\":\"rollup\",\"completed\":0,\"total\":2},\"source_mode\":\"live\"}";
            WallParsedPlanStatus s;
            Assert.IsTrue(WallOfflineLogic.TryParsePlanStatus(json, out s));
            Assert.AreEqual(2, s.cards.Count);
            Assert.AreEqual("The model could not work this step.", s.cards[0].failureReason);
            CollectionAssert.AreEqual(new[] { "t-a" }, s.cards[1].blockedBy);
            Assert.IsTrue(s.settled);
            Assert.AreEqual("art-9", s.synthesisArtifactId);
            Assert.IsFalse(WallOfflineLogic.TryParsePlanStatus("{\"detail\":\"unknown session\"}", out s));

            var b = Board();
            WallOfflineLogic.TryParsePlanStatus(json, out s);
            foreach (PlanCardUpdate u in s.cards) b.Apply(u);
            Assert.IsTrue(b.Find("t-a").CanRetry);
            Assert.IsTrue(b.Settled("mp-1") == false, "t-c is still queued on this board");

            bool retried; string state; int rev;
            Assert.IsTrue(WallOfflineLogic.TryParseRetry("{\"task_id\":\"t-a\",\"retried\":true,\"state\":\"queued\",\"revision\":4}", out retried, out state, out rev));
            Assert.IsTrue(retried);
            Assert.AreEqual("queued", state);
            Assert.AreEqual(4, rev);
            Assert.IsFalse(WallOfflineLogic.TryParseRetry("{}", out retried, out state, out rev));
        }
    }
}
#endif
