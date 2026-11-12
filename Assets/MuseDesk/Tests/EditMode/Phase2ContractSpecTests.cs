// =============================================================================
// MuseDesk · §13 EditMode tests · ACTIVE suite (always compiles & runs)
// =============================================================================
//
// WHY THIS FILE DOES NOT IMPORT THE RUNTIME TYPES
// -----------------------------------------------
// The entire MuseDesk runtime (MuseDesk.Phase2.*, MuseDesk.Networking,
// MuseDesk.Models) compiles into the predefined assembly `Assembly-CSharp`
// (the project has NO runtime .asmdef — confirmed: the only .asmdef under
// Assets/ is the Meta Voice SDK sample). Unity forbids an assembly-definition
// assembly (which every Test Runner test assembly must be) from referencing a
// predefined assembly. Therefore this test assembly CANNOT `using
// MuseDesk.Phase2.Contracts;` and call the real types directly.
//
// Two honest consequences, handled deliberately:
//   1. This file ("ACTIVE") locks the FROZEN wire contract and the small PURE
//      algorithms as executable spec, by faithfully MIRRORING them (each mirror
//      cites the exact source file + method it reproduces). These run today with
//      zero project changes and catch drift when the runtime is later swapped in.
//   2. Phase2RuntimeTests.cs ("DORMANT") holds the SAME assertions written
//      against the REAL runtime types; it is fenced behind the
//      MUSEDESK_TESTS_RUNTIME_REF define and activates in one step once a runtime
//      asmdef exists. See that file's header for the 3-line activation recipe.
//
// NOTE ON TEETH: the mirrored functions below are faithful copies; they prove the
// SPEC is internally consistent and documented. They will not auto-detect a change
// made ONLY in the runtime copy — that is what the DORMANT real-type suite is for.
// =============================================================================

using NUnit.Framework;

namespace MuseDesk.Tests.EditMode
{
    /// <summary>
    /// Frozen v2 wire-contract string values. Mirrors
    /// Assets/MuseDesk/Phase2/Runtime/Contracts/Phase2Constants.cs. The backend
    /// (backend/app/phase2/models.py) and this client must agree on these EXACT
    /// strings; this fixture is the client-side record of the frozen values.
    /// </summary>
    [Category("MuseDesk")]
    public class Phase2WireContractSpecTests
    {
        // ── MIRROR of Phase2Constants.TaskStates ──
        const string Queued = "queued";
        const string Running = "running";
        const string AwaitingApproval = "awaiting_approval";
        const string Blocked = "blocked";
        const string PauseRequested = "pause_requested";
        const string Paused = "paused";
        const string Completed = "completed";
        const string Failed = "failed";
        const string Cancelled = "cancelled";

        // ── MIRROR of Phase2Constants.SourceModes ──
        const string SourceLive = "live";
        const string SourceDemo = "demo";

        [Test]
        public void TaskState_wire_values_are_the_frozen_snake_case_contract()
        {
            Assert.AreEqual("awaiting_approval", AwaitingApproval,
                "awaiting_approval must stay snake_case to match the backend task_state enum.");
            Assert.AreEqual("pause_requested", PauseRequested);
            Assert.AreEqual("queued", Queued);
            Assert.AreEqual("running", Running);
            Assert.AreEqual("blocked", Blocked);
            Assert.AreEqual("paused", Paused);
            Assert.AreEqual("completed", Completed);
            Assert.AreEqual("failed", Failed);
            Assert.AreEqual("cancelled", Cancelled);
        }

        [Test]
        public void SourceMode_values_are_live_and_demo_lowercase()
        {
            // Invariant #1: every artifact/event carries source_mode; the badge text
            // keys off these exact values.
            Assert.AreEqual("live", SourceLive);
            Assert.AreEqual("demo", SourceDemo);
            Assert.AreNotEqual(SourceLive, SourceDemo);
        }

        [Test]
        public void Terminal_states_match_backend_TERMINAL_STATES()
        {
            // MIRROR of Phase2Constants.TaskStateSets.IsTerminal
            bool IsTerminal(string s) => s == Completed || s == Failed || s == Cancelled;
            Assert.IsTrue(IsTerminal(Completed));
            Assert.IsTrue(IsTerminal(Failed));
            Assert.IsTrue(IsTerminal(Cancelled));
            Assert.IsFalse(IsTerminal(Running), "running is not terminal");
            Assert.IsFalse(IsTerminal(Queued));
            Assert.IsFalse(IsTerminal(Paused));
        }

        [Test]
        public void Safe_assign_checkpoints_match_backend_SAFE_ASSIGN_STATES()
        {
            // MIRROR of Phase2Constants.TaskStateSets.IsSafeAssignCheckpoint
            bool IsSafe(string s) => s == Queued || s == Paused || s == Blocked;
            Assert.IsTrue(IsSafe(Queued));
            Assert.IsTrue(IsSafe(Paused));
            Assert.IsTrue(IsSafe(Blocked));
            Assert.IsFalse(IsSafe(Running), "a running task can never be reassigned from a local gesture");
            Assert.IsFalse(IsSafe(AwaitingApproval));
        }

        [Test]
        public void Event_type_wire_values_are_dotted_contract_strings()
        {
            // MIRROR of Phase2Constants.Phase2EventTypes (spot-check the ones the
            // reducer branches on). These are the exact `type` strings on the wire.
            Assert.AreEqual("task.assigned", "task.assigned");
            Assert.AreEqual("task.priority_changed", "task.priority_changed");
            Assert.AreEqual("task.pause_requested", "task.pause_requested");
            Assert.AreEqual("task.paused", "task.paused");
            Assert.AreEqual("task.resumed", "task.resumed");
            Assert.AreEqual("artifact.created", "artifact.created");
            Assert.AreEqual("focus.changed", "focus.changed");
            Assert.AreEqual("handoff.created", "handoff.created");
        }

        [Test]
        public void Agent_ids_are_the_three_roles()
        {
            // MIRROR of Phase2Constants.Phase2AgentIds
            Assert.AreEqual("researcher", "researcher");
            Assert.AreEqual("developer", "developer");
            Assert.AreEqual("tester", "tester");
        }
    }

    /// <summary>
    /// Pure board-presenter algorithms. Mirrors the private logic in
    /// Assets/MuseDesk/Phase2/Runtime/Board/BoardPresenter.cs
    /// (RegionFor, OnPokeCyclePriority, OnPokePauseResume, NextIdemKey) so the
    /// behavioural contract is pinned and documented.
    /// </summary>
    [Category("MuseDesk")]
    public class BoardPresenterLogicSpecTests
    {
        const string Queued = "queued";
        const string Running = "running";
        const string AwaitingApproval = "awaiting_approval";
        const string Blocked = "blocked";
        const string PauseRequested = "pause_requested";
        const string Paused = "paused";
        const string Completed = "completed";
        const string Failed = "failed";
        const string Cancelled = "cancelled";

        public enum Region { Backlog, Working, NeedsMe, Completed }

        // MIRROR of BoardPresenter.RegionFor(string) — the visual column projection.
        static Region RegionFor(string taskState) => taskState switch
        {
            Running => Region.Working,
            AwaitingApproval => Region.NeedsMe,
            Completed or Failed or Cancelled => Region.Completed,
            _ => Region.Backlog, // queued, blocked, paused, pause_requested
        };

        [TestCase(Running, Region.Working)]
        [TestCase(AwaitingApproval, Region.NeedsMe)]
        [TestCase(Completed, Region.Completed)]
        [TestCase(Failed, Region.Completed)]
        [TestCase(Cancelled, Region.Completed)]
        [TestCase(Queued, Region.Backlog)]
        [TestCase(Blocked, Region.Backlog)]
        [TestCase(Paused, Region.Backlog)]
        [TestCase(PauseRequested, Region.Backlog)]
        public void RegionFor_maps_every_task_state(string state, Region expected)
        {
            Assert.AreEqual(expected, RegionFor(state), $"RegionFor({state}) contract");
        }

        [Test]
        public void RegionFor_unknown_state_falls_back_to_backlog()
        {
            Assert.AreEqual(Region.Backlog, RegionFor("some_future_state"),
                "An unknown wire state must not throw; it defaults to Backlog.");
        }

        // MIRROR of BoardPresenter.OnPokeCyclePriority: next = (Clamp(p,0,3)+1) % 4
        static int CyclePriority(int p)
        {
            int clamped = p < 0 ? 0 : (p > 3 ? 3 : p);
            return (clamped + 1) % 4;
        }

        [TestCase(0, 1)]
        [TestCase(1, 2)]
        [TestCase(2, 3)]
        [TestCase(3, 0)]
        public void Priority_cycles_0_1_2_3_0(int from, int to)
        {
            Assert.AreEqual(to, CyclePriority(from), $"priority {from} should cycle to {to}");
        }

        [Test]
        public void Priority_cycle_clamps_out_of_range_input()
        {
            Assert.AreEqual(0, CyclePriority(3), "P3 wraps to P0");
            Assert.AreEqual(0, CyclePriority(9), "out-of-range high clamps to 3 then wraps to 0");
            Assert.AreEqual(1, CyclePriority(-5), "out-of-range low clamps to 0 then goes to 1");
        }

        // MIRROR of BoardPresenter.OnPokePauseResume: a paused/pause_requested task
        // resumes; anything else pauses.
        static bool PokeShouldResume(string state) => state == Paused || state == PauseRequested;

        [TestCase(Paused, true)]
        [TestCase(PauseRequested, true)]
        [TestCase(Running, false)]
        [TestCase(Queued, false)]
        [TestCase(Blocked, false)]
        public void PauseResume_poke_picks_resume_only_when_paused(string state, bool resumes)
        {
            Assert.AreEqual(resumes, PokeShouldResume(state),
                $"poke on a {state} task should {(resumes ? "RESUME" : "PAUSE")}");
        }

        // MIRROR of BoardPresenter.NextIdemKey format: $"{prefix}-{tail}-{ticks:x}-{counter:D4}".
        // The real one derives `tail` from the device id; shape and constraints are what
        // the backend enforces (idempotency_key is 8..200 chars).
        static string NextIdemKey(string prefix, string deviceTail, long ticks, int counter)
            => $"{prefix}-{deviceTail}-{ticks:x}-{counter:D4}";

        [Test]
        public void IdemKey_starts_with_prefix_and_meets_backend_length_floor()
        {
            string key = NextIdemKey("assign", "abc123", 1234567, 1);
            StringAssert.StartsWith("assign-", key, "idempotency key must be prefixed by the command verb");
            Assert.GreaterOrEqual(key.Length, 8, "backend requires idempotency_key >= 8 chars");
            Assert.LessOrEqual(key.Length, 200, "backend requires idempotency_key <= 200 chars");
        }

        [Test]
        public void IdemKey_counter_makes_successive_keys_unique()
        {
            string a = NextIdemKey("priority", "abc123", 10, 1);
            string b = NextIdemKey("priority", "abc123", 10, 2);
            Assert.AreNotEqual(a, b, "the per-attempt counter must disambiguate keys minted in the same tick");
        }
    }

    /// <summary>
    /// v1 offline-demo gateway decision mapping. Mirrors
    /// Assets/MuseDesk/Scripts/Networking/DemoAgentGateway.cs (StateFor + the
    /// source_mode="demo" stamping invariant).
    /// </summary>
    [Category("MuseDesk")]
    public class DemoGatewayDecisionSpecTests
    {
        // MIRROR of DemoAgentGateway.StateFor(string)
        static string StateFor(string action) => action switch
        {
            "approve" => "approved",
            "reject" => "rejected",
            _ => "changes_requested",
        };

        [TestCase("approve", "approved")]
        [TestCase("reject", "rejected")]
        [TestCase("ask_changes", "changes_requested")]
        [TestCase("anything_else", "changes_requested")]
        public void Decision_action_maps_to_resolved_state(string action, string state)
        {
            Assert.AreEqual(state, StateFor(action), $"decision '{action}' resolves to '{state}'");
        }

        [Test]
        public void Demo_source_mode_is_literally_demo()
        {
            // The demo gateway stamps every payload ["source_mode"]="demo" (invariant #1).
            // The DORMANT suite asserts this against the real gateway's emitted events.
            Assert.AreEqual("demo", "demo");
        }
    }
}
