// =============================================================================
// MuseDesk - EditMode tests against the REAL runtime types (drift guards)
// =============================================================================
// The Phase 2 runtime (Phase2Constants, BoardPresenter) and the networking layer
// (DemoAgentGateway) live in the predefined Assembly-CSharp, which an asmdef test
// assembly cannot reference at compile time. Phase2ContractSpecTests therefore
// tests MIRRORED copies and cannot see drift in the real classes.
//
// These tests close that gap WITHOUT any asmdef change: they resolve the real
// types from the loaded Assembly-CSharp by full name via reflection and assert the
// same contract against the real values. If a runtime string/mapping changes (or a
// type is renamed/removed), these fail loudly, which the mirrored fixtures cannot.
//
// The permanent fix is a MuseDesk.Runtime asmdef plus a reference from
// MuseDesk.Tests.EditMode (see Phase2RuntimeTests.cs header and docs/Engineering/TESTING.md).
// =============================================================================

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MuseDesk.Tests.EditMode.Real
{
    static class RealType
    {
        public static Type Find(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "Assembly-CSharp") continue;
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            Assert.Fail("Real runtime type not found in Assembly-CSharp: " + fullName +
                        " (renamed, moved, or moved into an asmdef? update this test.)");
            return null;
        }

        public static string Const(string typeFullName, string field)
        {
            var f = Find(typeFullName).GetField(field, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(f, typeFullName + "." + field + " is missing from the real type");
            return (string)f.GetRawConstantValue();
        }

        public static bool Bool(string typeFullName, string method, string arg)
        {
            var m = Find(typeFullName).GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(m, typeFullName + "." + method + " is missing from the real type");
            return (bool)m.Invoke(null, new object[] { arg });
        }
    }

    [Category("MuseDesk")]
    public class Phase2ConstantsRealTests
    {
        const string NS = "MuseDesk.Phase2.Contracts.";

        [TestCase("TaskStates", "Queued", "queued")]
        [TestCase("TaskStates", "Running", "running")]
        [TestCase("TaskStates", "AwaitingApproval", "awaiting_approval")]
        [TestCase("TaskStates", "Blocked", "blocked")]
        [TestCase("TaskStates", "PauseRequested", "pause_requested")]
        [TestCase("TaskStates", "Paused", "paused")]
        [TestCase("TaskStates", "Completed", "completed")]
        [TestCase("TaskStates", "Failed", "failed")]
        [TestCase("TaskStates", "Cancelled", "cancelled")]
        [TestCase("SourceModes", "Live", "live")]
        [TestCase("SourceModes", "Demo", "demo")]
        [TestCase("Phase2AgentIds", "Researcher", "researcher")]
        [TestCase("Phase2AgentIds", "Developer", "developer")]
        [TestCase("Phase2AgentIds", "Tester", "tester")]
        [TestCase("Phase2EventTypes", "TaskAssigned", "task.assigned")]
        [TestCase("Phase2EventTypes", "TaskPriorityChanged", "task.priority_changed")]
        [TestCase("Phase2EventTypes", "TaskPauseRequested", "task.pause_requested")]
        [TestCase("Phase2EventTypes", "TaskPaused", "task.paused")]
        [TestCase("Phase2EventTypes", "TaskResumed", "task.resumed")]
        [TestCase("Phase2EventTypes", "ArtifactCreated", "artifact.created")]
        [TestCase("Phase2EventTypes", "FocusChanged", "focus.changed")]
        [TestCase("Phase2EventTypes", "HandoffCreated", "handoff.created")]
        [TestCase("TaskTemplates", "AuthDemo", "auth_demo")]
        public void Wire_value_matches_frozen_contract(string cls, string field, string expected)
        {
            Assert.AreEqual(expected, RealType.Const(NS + cls, field), cls + "." + field);
        }

        [TestCase("completed", true)]
        [TestCase("failed", true)]
        [TestCase("cancelled", true)]
        [TestCase("running", false)]
        [TestCase("queued", false)]
        [TestCase("paused", false)]
        public void IsTerminal_matches_backend_TERMINAL_STATES(string state, bool expected)
        {
            Assert.AreEqual(expected, RealType.Bool(NS + "TaskStateSets", "IsTerminal", state));
        }

        [TestCase("queued", true)]
        [TestCase("paused", true)]
        [TestCase("blocked", true)]
        [TestCase("running", false)]
        [TestCase("awaiting_approval", false)]
        public void IsSafeAssignCheckpoint_matches_backend_SAFE_ASSIGN_STATES(string state, bool expected)
        {
            Assert.AreEqual(expected, RealType.Bool(NS + "TaskStateSets", "IsSafeAssignCheckpoint", state));
        }

        [Test]
        public void Only_auth_demo_template_is_executable()
        {
            Assert.IsTrue(RealType.Bool(NS + "TaskTemplates", "IsExecutable", "auth_demo"));
            Assert.IsFalse(RealType.Bool(NS + "TaskTemplates", "IsExecutable", "anything_else"));
        }
    }

    [Category("MuseDesk")]
    public class BoardPresenterRealTests
    {
        const string Presenter = "MuseDesk.Phase2.Board.BoardPresenter";

        static string Region(string state)
        {
            var m = RealType.Find(Presenter).GetMethod("RegionFor", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(m, "BoardPresenter.RegionFor must stay public static");
            return m.Invoke(null, new object[] { state }).ToString();
        }

        [TestCase("running", "Working")]
        [TestCase("awaiting_approval", "NeedsMe")]
        [TestCase("completed", "Completed")]
        [TestCase("failed", "Completed")]
        [TestCase("cancelled", "Completed")]
        [TestCase("queued", "Backlog")]
        [TestCase("blocked", "Backlog")]
        [TestCase("paused", "Backlog")]
        [TestCase("pause_requested", "Backlog")]
        [TestCase("some_future_state", "Backlog")]
        public void RegionFor_maps_state_to_column(string state, string expectedRegion)
        {
            Assert.AreEqual(expectedRegion, Region(state), "BoardPresenter.RegionFor(" + state + ")");
        }

        [Test]
        public void NextIdemKey_has_prefix_backend_length_bounds_and_is_unique()
        {
            var t = RealType.Find(Presenter);
            var go = new GameObject("RealTest_BoardPresenter") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var comp = go.AddComponent(t);
                var m = t.GetMethod("NextIdemKey", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(m, "BoardPresenter.NextIdemKey must stay public");
                string a = (string)m.Invoke(comp, new object[] { "assign" });
                string b = (string)m.Invoke(comp, new object[] { "assign" });
                StringAssert.StartsWith("assign-", a);
                Assert.GreaterOrEqual(a.Length, 8, "backend idempotency_key >= 8 chars");
                Assert.LessOrEqual(a.Length, 200, "backend idempotency_key <= 200 chars");
                Assert.AreNotEqual(a, b, "successive keys must differ (counter)");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }

    [Category("MuseDesk")]
    public class DemoGatewayRealTests
    {
        [TestCase("approve", "approved")]
        [TestCase("reject", "rejected")]
        [TestCase("ask_changes", "changes_requested")]
        [TestCase("anything_else", "changes_requested")]
        public void StateFor_maps_decision_action(string action, string expected)
        {
            var t = RealType.Find("MuseDesk.Networking.DemoAgentGateway");
            var m = t.GetMethod("StateFor", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(m, "DemoAgentGateway.StateFor missing (renamed? update this test)");
            Assert.AreEqual(expected, (string)m.Invoke(null, new object[] { action }));
        }
    }
}
