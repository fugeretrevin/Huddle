// =============================================================================
// MuseDesk · EditMode tests · Wall Habitat backend glue, pure parts: change-request detection, honest
// offline /btw reply, template plan, commit validation and ordering, backend reply and error parsing.
// Not proven here: the HTTP round trip, the board gateway, the reducers' event bridge (Play Mode).
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using MuseDesk.Spatial.Wall;

namespace MuseDesk.Tests.EditMode.Wall
{
    public class WallOfflineLogicTests
    {
        // ── change requests ──

        [TestCase("Please cancel this task", true)]
        [TestCase("use Redis instead", true)]
        [TestCase("Can you switch to the other design?", true)]
        [TestCase("deploy it now", true)]
        [TestCase("Why is this blocked?", false)]
        [TestCase("What are you working on?", false)]
        [TestCase("", false)]
        public void ChangeRequestDetection(string q, bool expected)
        {
            Assert.AreEqual(expected, WallOfflineLogic.LooksLikeChangeRequest(q));
        }

        // ── offline side reply ──

        [Test]
        public void OfflineSide_UsesOnlyRealTaskState_AndIsLabelled()
        {
            var ex = WallOfflineLogic.BuildOfflineSide("r1", "How is it going?", "developer", true, "Build login", "running", 4, 2, -1, false);
            Assert.AreEqual("OFFLINE_SNAPSHOT", ex.provenance);
            Assert.AreEqual("answered", ex.status);
            Assert.AreEqual(4, ex.contextRevision);
            Assert.IsTrue(ex.staleContext, "asked at revision 2, task is at 4");
            StringAssert.Contains("Build login", ex.reply);
            StringAssert.Contains("running", ex.reply);
            StringAssert.Contains("OFFLINE SNAPSHOT", ex.reply);
            StringAssert.Contains("not an AI answer", ex.reply);
        }

        [Test]
        public void OfflineSide_ScriptedDemo_IsLabelledScripted()
        {
            var ex = WallOfflineLogic.BuildOfflineSide("r1", "status?", "researcher", true, "Task", "queued", 0, 0, -1, true);
            Assert.AreEqual("SCRIPTED_DEMO", ex.provenance);
            StringAssert.StartsWith("[SCRIPTED DEMO]", ex.reply);
            Assert.IsFalse(ex.staleContext);
        }

        [Test]
        public void OfflineSide_NoTask_SaysSo_NeverInventsProgress()
        {
            var ex = WallOfflineLogic.BuildOfflineSide("r1", "status?", "tester", false, null, null, 0, -1, -1, false);
            StringAssert.Contains("No task right now", ex.reply);
            StringAssert.DoesNotContain("%", ex.reply);
            StringAssert.DoesNotContain("revision", ex.reply);
        }

        [Test]
        public void OfflineSide_ChangeRequest_IsAmendmentAndNotApplied()
        {
            var ex = WallOfflineLogic.BuildOfflineSide("r1", "cancel the build", "developer", true, "Build", "running", 1, 1, -1, false);
            Assert.AreEqual("amendment_suggested", ex.status);
            Assert.AreEqual("cancel the build", ex.amendment);
            StringAssert.Contains("NOT applied", ex.reply);
        }

        [Test]
        public void OfflineSide_Manager_HasNoTaskOfItsOwn()
        {
            var ex = WallOfflineLogic.BuildOfflineSide("r1", "what is on the board?", "manager", false, null, null, 0, -1, 3, false);
            StringAssert.Contains("no task of its own", ex.reply);
            StringAssert.Contains("3 cards", ex.reply);
        }

        // ── template plan ──

        [Test]
        public void TemplatePlan_IsChain_WithOwnersAndLabel()
        {
            var p = WallOfflineLogic.BuildTemplatePlan("Add a login page", 7);
            Assert.AreEqual("demo-plan-7", p.planId);
            Assert.AreEqual(1, p.revision);
            Assert.AreEqual("SCRIPTED_DEMO", p.provenance);
            Assert.AreEqual(3, p.items.Count);
            Assert.AreEqual("a", p.items[0].clientItemId); Assert.AreEqual("researcher", p.items[0].proposedAgent);
            Assert.AreEqual("b", p.items[1].clientItemId); Assert.AreEqual("developer", p.items[1].proposedAgent);
            Assert.AreEqual("c", p.items[2].clientItemId); Assert.AreEqual("tester", p.items[2].proposedAgent);
            CollectionAssert.IsEmpty(p.items[0].dependsOn);
            CollectionAssert.AreEqual(new[] { "a" }, p.items[1].dependsOn);
            CollectionAssert.AreEqual(new[] { "b" }, p.items[2].dependsOn);
            foreach (var it in p.items) { Assert.AreEqual(1, it.acceptanceCriteria.Length); Assert.IsFalse(it.dangerous); }
        }

        [TestCase("Deploy to production", "deploy")]
        [TestCase("delete old users", "delete")]
        [TestCase("Rotate the password store", "password")]
        [TestCase("store the api token", "token")]
        public void TemplatePlan_FlagsDangerousObjectives(string objective, string word)
        {
            var p = WallOfflineLogic.BuildTemplatePlan(objective, 1);
            Assert.IsTrue(p.items[1].dangerous);
            StringAssert.Contains(word, string.Join(",", p.items[1].dangerReasons));
        }

        // ── commit validation + ordering ──

        static List<ManagerPlanItemView> Chain() { return WallOfflineLogic.BuildTemplatePlan("Add a login page", 1).items; }

        [Test]
        public void Commit_RefusesDependencyNotApproved()
        {
            string code, msg;
            Assert.IsFalse(WallOfflineLogic.ValidateCommit(Chain(), new List<string> { "b" }, new List<string>(), out code, out msg));
            Assert.AreEqual("DEPENDENCY_NOT_APPROVED", code);
        }

        [Test]
        public void Commit_RefusesUnauthorizedDangerous_AndAcceptsAuthorized()
        {
            var items = WallOfflineLogic.BuildTemplatePlan("deploy the site", 1).items;
            var all = new List<string> { "a", "b", "c" };
            string code, msg;
            Assert.IsFalse(WallOfflineLogic.ValidateCommit(items, all, new List<string>(), out code, out msg));
            Assert.AreEqual("AUTHORIZATION_REQUIRED", code);
            Assert.IsTrue(WallOfflineLogic.ValidateCommit(items, all, new List<string> { "b" }, out code, out msg));
        }

        [Test]
        public void Commit_RefusesNothingApproved_AndUnknownItem()
        {
            string code, msg;
            Assert.IsFalse(WallOfflineLogic.ValidateCommit(Chain(), new List<string>(), null, out code, out msg));
            Assert.AreEqual("NO_ITEMS_APPROVED", code);
            Assert.IsFalse(WallOfflineLogic.ValidateCommit(Chain(), new List<string> { "zzz" }, null, out code, out msg));
            Assert.AreEqual("UNKNOWN_ITEM", code);
        }

        [Test]
        public void OrderForCreate_IsDependencyOrder_EvenIfPlanOrderIsNot()
        {
            var items = Chain();
            items.Reverse();       // c, b, a
            var ordered = WallOfflineLogic.OrderForCreate(items, new List<string> { "a", "b", "c" });
            Assert.AreEqual(3, ordered.Count);
            Assert.AreEqual("a", ordered[0].clientItemId);
            Assert.AreEqual("b", ordered[1].clientItemId);
            Assert.AreEqual("c", ordered[2].clientItemId);
        }

        [Test]
        public void OrderForCreate_CycleGivesEmpty()
        {
            var items = Chain();
            items[0].dependsOn = new[] { "c" };
            Assert.AreEqual(0, WallOfflineLogic.OrderForCreate(items, new List<string> { "a", "b", "c" }).Count);
        }

        // ── backend reply parsing ──

        const string PlanJson = "{\"plan_id\":\"mp-1\",\"title\":\"T\",\"provenance\":\"TEMPLATE_PLAN\",\"provenance_note\":\"n\",\"status\":\"proposed\",\"revision\":1,"
            + "\"items\":[{\"client_item_id\":\"a\",\"title\":\"Research X\",\"kind\":\"research\",\"proposed_agent\":\"researcher\",\"depends_on\":[],"
            + "\"acceptance_criteria\":[\"c1\"],\"required_permission\":\"read_repo\",\"dangerous\":false,\"danger_reasons\":[]},"
            + "{\"client_item_id\":\"b\",\"title\":\"Do X\",\"kind\":\"code\",\"proposed_agent\":\"developer\",\"depends_on\":[\"a\"],"
            + "\"acceptance_criteria\":[],\"required_permission\":\"workspace_write\",\"dangerous\":true,\"danger_reasons\":[\"deployment\"]}]}";

        [Test]
        public void ParsePlan_MapsItems()
        {
            WallParsedPlan p;
            Assert.IsTrue(WallOfflineLogic.TryParsePlan(PlanJson, out p));
            Assert.AreEqual("mp-1", p.planId);
            Assert.AreEqual("TEMPLATE_PLAN", p.provenance);
            Assert.AreEqual(2, p.items.Count);
            Assert.AreEqual("developer", p.items[1].proposedAgent);
            CollectionAssert.AreEqual(new[] { "a" }, p.items[1].dependsOn);
            Assert.IsTrue(p.items[1].dangerous);
            CollectionAssert.AreEqual(new[] { "deployment" }, p.items[1].dangerReasons);
            Assert.AreEqual("workspace_write", p.items[1].requiredPermission);
        }

        [Test]
        public void ParsePlan_RejectsGarbageAndEmpty()
        {
            WallParsedPlan p;
            Assert.IsFalse(WallOfflineLogic.TryParsePlan("not json", out p));
            Assert.IsFalse(WallOfflineLogic.TryParsePlan("{\"plan_id\":\"x\",\"items\":[]}", out p));
            Assert.IsFalse(WallOfflineLogic.TryParsePlan(null, out p));
        }

        [Test]
        public void ParseCommit_MapsCanonicalIds_AndFillsTitles_NeverInvents()
        {
            string json = "{\"plan_id\":\"mp-1\",\"status\":\"committed\",\"revision\":2,\"tasks\":["
                + "{\"client_item_id\":\"a\",\"task_id\":\"t2-aaa\",\"agent_id\":\"researcher\",\"state\":\"queued\",\"revision\":0},"
                + "{\"client_item_id\":\"b\",\"task_id\":\"\",\"agent_id\":\"developer\"}]}";
            var titles = new Dictionary<string, string> { { "a", "Research X" } };
            WallParsedCommit c;
            Assert.IsTrue(WallOfflineLogic.TryParseCommit(json, titles, out c));
            Assert.AreEqual(2, c.revision);
            Assert.AreEqual(1, c.tasks.Count, "an item without a task id is dropped, not invented");
            Assert.AreEqual("t2-aaa", c.tasks[0].taskId);
            Assert.AreEqual("Research X", c.tasks[0].title);
            Assert.AreEqual("researcher", c.tasks[0].agentId);
        }

        [Test]
        public void ParseSide_MapsFields()
        {
            string json = "{\"request_id\":\"q1\",\"reply\":\"hello\",\"provenance\":\"TASK_SNAPSHOT\",\"status\":\"amendment_suggested\","
                + "\"context_revision\":4,\"stale_context\":true,\"proposed_amendment\":{\"summary\":\"use Redis\",\"requires_approval\":true}}";
            SideExchange ex;
            Assert.IsTrue(WallOfflineLogic.TryParseSide(json, "fallback", out ex));
            Assert.AreEqual("q1", ex.requestId);
            Assert.AreEqual("TASK_SNAPSHOT", ex.provenance);
            Assert.AreEqual("amendment_suggested", ex.status);
            Assert.AreEqual(4, ex.contextRevision);
            Assert.IsTrue(ex.staleContext);
            Assert.AreEqual("use Redis", ex.amendment);
        }

        [Test]
        public void ParseSide_NullAmendmentStaysNull()
        {
            SideExchange ex;
            Assert.IsTrue(WallOfflineLogic.TryParseSide("{\"reply\":\"r\",\"provenance\":\"NONE\",\"status\":\"busy\",\"proposed_amendment\":null}", "rid", out ex));
            Assert.AreEqual("rid", ex.requestId);
            Assert.IsNull(ex.amendment);
        }

        // ── errors ──

        [Test]
        public void Error_FeatureDisabled_HasFixedWords()
        {
            var e = WallOfflineLogic.ParseError(404, "{\"code\":\"FEATURE_DISABLED\",\"message\":\"x\"}");
            Assert.AreEqual("FEATURE_DISABLED", e.code);
            Assert.AreEqual("Wall features are switched off on the backend", WallOfflineLogic.FriendlyMessage(e));
        }

        [Test]
        public void Error_RevisionConflict_CarriesCanonicalRevision()
        {
            var e = WallOfflineLogic.ParseError(409, "{\"code\":\"REVISION_CONFLICT\",\"message\":\"stale\",\"canonical_plan\":{\"plan_id\":\"mp-1\",\"revision\":3}}");
            Assert.AreEqual("REVISION_CONFLICT", e.code);
            Assert.AreEqual(3, e.canonicalRevision);
            StringAssert.Contains("revision 3", WallOfflineLogic.FriendlyMessage(e));
        }

        [Test]
        public void Error_UnparseableBody_FallsBackToHttpCode()
        {
            var e = WallOfflineLogic.ParseError(502, "<html>bad gateway</html>");
            Assert.AreEqual("HTTP_502", e.code);
            StringAssert.Contains("502", WallOfflineLogic.FriendlyMessage(e));
            Assert.AreEqual("UNAUTHORIZED", WallOfflineLogic.ParseError(401, null).code);
        }

        [Test]
        public void ScriptedDeliverable_IsLabelledAndNeverClaimsExecution()
        {
            string kind, summary, title, body;
            WallOfflineLogic.ScriptedDeliverable("researcher", "Research: login", "Build the login flow", out kind, out summary, out title, out body);
            Assert.AreEqual("research_report", kind);
            StringAssert.StartsWith("[SCRIPTED DEMO]", body);

            WallOfflineLogic.ScriptedDeliverable("tester", "Test: login", "Build the login flow", out kind, out summary, out title, out body);
            Assert.AreEqual("implementation_plan", kind, "a test plan is never a test_report: no tests ran");
            StringAssert.Contains("No tests were run", body);

            WallOfflineLogic.ScriptedDeliverable("developer", "Implement: login", "Build the login flow", out kind, out summary, out title, out body);
            Assert.AreEqual("implementation_plan", kind);
            StringAssert.Contains("Nothing was applied", body);
            StringAssert.StartsWith("Proposed change", summary);
        }
    }
}
#endif
