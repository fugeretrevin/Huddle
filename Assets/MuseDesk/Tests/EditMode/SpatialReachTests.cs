// =============================================================================
// MuseDesk · EditMode tests · spatial workspace, reach side
// =============================================================================
//
// These exercise the REAL runtime types (MuseDesk.Spatial.AttentionModel,
// RemoteHoldLedger, RemoteDeliveryMachine, SummonVoicePhrases, SpatialWorkspaceState),
// not mirrors. The test assembly cannot reference the predefined Assembly-CSharp
// where the runtime lives (see Phase2ContractSpecTests.cs), so the types are reached
// by reflection: they are plain C# classes with primitive-typed members precisely so
// that this is practical. If a type or member is renamed, the test fails with the
// name it could not find. No scene, no MonoBehaviour, no Unity object is involved.
// =============================================================================

using System;
using System.Reflection;
using NUnit.Framework;

namespace MuseDesk.Tests.EditMode
{
    static class SpatialReachReflect
    {
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public;
        const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public;

        public static Type T(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            Assert.Fail("Runtime type not found: " + fullName);
            return null;
        }

        public static object New(string fullName, params object[] args)
        {
            return Activator.CreateInstance(T(fullName), args);
        }

        public static object Call(object target, string method, params object[] args)
        {
            MethodInfo m = target.GetType().GetMethod(method, Inst);
            Assert.IsNotNull(m, "Method not found: " + target.GetType().Name + "." + method);
            return m.Invoke(target, args);
        }

        public static object CallStatic(string fullName, string method, params object[] args)
        {
            MethodInfo m = T(fullName).GetMethod(method, Stat);
            Assert.IsNotNull(m, "Static method not found: " + fullName + "." + method);
            return m.Invoke(null, args);
        }

        public static object Get(object target, string property)
        {
            PropertyInfo p = target.GetType().GetProperty(property, Inst);
            Assert.IsNotNull(p, "Property not found: " + target.GetType().Name + "." + property);
            return p.GetValue(target, null);
        }

        public static object GetStatic(string fullName, string property)
        {
            PropertyInfo p = T(fullName).GetProperty(property, Stat);
            Assert.IsNotNull(p, "Static property not found: " + fullName + "." + property);
            return p.GetValue(null, null);
        }

        public static void SetField(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, Inst);
            Assert.IsNotNull(f, "Field not found: " + target.GetType().Name + "." + field);
            f.SetValue(target, value);
        }

        public static string Name(object target, string property) { return Convert.ToString(Get(target, property)); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Attention classification
    // ─────────────────────────────────────────────────────────────────────────

    [Category("MuseDesk")]
    public class SpatialReachAttentionTests
    {
        const string Model = "MuseDesk.Spatial.AttentionModel";

        static object NewModel() { return SpatialReachReflect.New(Model); }
        static string Level(object m)
        {
            string level = SpatialReachReflect.Name(m, "Level");
            // The Suggestion level has no backend event behind it and must never be produced.
            Assert.AreNotEqual("Suggestion", level, "Suggestion must be unreachable");
            return level;
        }
        static string Agent(object m) { return (string)SpatialReachReflect.Get(m, "AgentId"); }
        static int Quiet(object m) { return (int)SpatialReachReflect.Get(m, "QuietCount"); }
        static int Pending(object m) { return (int)SpatialReachReflect.Get(m, "PendingDecisions"); }
        static int Version(object m) { return (int)SpatialReachReflect.Get(m, "Version"); }
        static void Do(object m, string method, params object[] args) { SpatialReachReflect.Call(m, method, args); }

        [Test]
        public void Starts_at_none()
        {
            object m = NewModel();
            Assert.AreEqual("None", Level(m));
            Assert.IsNull(Agent(m));
            Assert.AreEqual(0, Quiet(m));
        }

        [Test]
        public void Suggestion_is_defined_but_no_input_produces_it()
        {
            Type levels = SpatialReachReflect.T("MuseDesk.Spatial.AttentionLevel");
            Assert.IsTrue(Enum.IsDefined(levels, "Suggestion"));
            object m = NewModel();
            // Every input the model has, in an arbitrary order; Level() asserts after each step.
            Do(m, "AgentProgress", "developer"); Level(m);
            Do(m, "WorkDelivered", "tester"); Level(m);
            Do(m, "TaskFailed", "developer"); Level(m);
            Do(m, "DecisionRequested", "d1", "researcher"); Level(m);
            Do(m, "SetFocus", true); Level(m);
            Do(m, "AgentProgress", "tester"); Level(m);
            Do(m, "WorkDelivered", "tester"); Level(m);
            Do(m, "DecisionResolved", "d1"); Level(m);
            Do(m, "SetFocus", false); Level(m);
            Do(m, "RunStarted"); Level(m);
            Do(m, "Acknowledge"); Level(m);
            Do(m, "Reset"); Level(m);
        }

        [Test]
        public void Progress_is_passive_and_names_the_agent()
        {
            object m = NewModel();
            Do(m, "AgentProgress", "developer");
            Assert.AreEqual("Passive", Level(m));
            Assert.AreEqual("developer", Agent(m));
        }

        [Test]
        public void Pending_decision_is_blocking_and_outranks_everything()
        {
            object m = NewModel();
            Do(m, "AgentProgress", "developer");
            Do(m, "DecisionRequested", "d1", "researcher");
            Assert.AreEqual("Blocking", Level(m));
            Assert.AreEqual("researcher", Agent(m));

            Do(m, "WorkDelivered", "tester");
            Assert.AreEqual("Blocking", Level(m));
            Do(m, "TaskFailed", "developer");
            Assert.AreEqual("Blocking", Level(m));
            Assert.AreEqual("researcher", Agent(m));

            Do(m, "DecisionResolved", "d1");
            Assert.AreEqual("Error", Level(m), "the failure that arrived meanwhile is what remains");
            Assert.AreEqual("developer", Agent(m));
        }

        [Test]
        public void Blocking_is_cleared_only_by_resolution_or_reset()
        {
            object m = NewModel();
            Do(m, "DecisionRequested", "d1", "researcher");
            Do(m, "Acknowledge");
            Assert.AreEqual("Blocking", Level(m), "acknowledging never answers a decision");
            Do(m, "RunStarted");
            Assert.AreEqual("Blocking", Level(m));
            Do(m, "SetFocus", true);
            Assert.AreEqual("Blocking", Level(m), "focus mode never hides a decision");
            Do(m, "DecisionResolved", "some-other-id");
            Assert.AreEqual("Blocking", Level(m), "an unknown id resolves nothing");
            Assert.AreEqual(1, Pending(m));
            Do(m, "DecisionResolved", "d1");
            Assert.AreEqual("None", Level(m));
            Assert.AreEqual(0, Pending(m));
        }

        [Test]
        public void Duplicate_decision_request_is_counted_once()
        {
            object m = NewModel();
            Do(m, "DecisionRequested", "d1", "researcher");
            Do(m, "DecisionRequested", "d1", "researcher");
            Assert.AreEqual(1, Pending(m));
            Do(m, "DecisionResolved", "d1");
            Assert.AreEqual("None", Level(m));
        }

        [Test]
        public void Two_decisions_stay_blocking_until_both_resolve()
        {
            object m = NewModel();
            Do(m, "DecisionRequested", "d1", "researcher");
            Do(m, "DecisionRequested", "d2", "tester");
            Do(m, "DecisionResolved", "d1");
            Assert.AreEqual("Blocking", Level(m));
            Assert.AreEqual("tester", Agent(m));
            Do(m, "DecisionResolved", "d2");
            Assert.AreEqual("None", Level(m));
        }

        [Test]
        public void Delivery_is_completed_then_a_failure_replaces_it_then_a_new_run_clears_both()
        {
            object m = NewModel();
            Do(m, "WorkDelivered", "tester");
            Assert.AreEqual("Completed", Level(m));
            Assert.AreEqual("tester", Agent(m));
            Do(m, "TaskFailed", "developer");
            Assert.AreEqual("Error", Level(m));
            Assert.AreEqual("developer", Agent(m));
            Do(m, "RunStarted");
            Assert.AreEqual("None", Level(m));
        }

        [Test]
        public void Delivery_after_a_failure_stands_as_completed()
        {
            object m = NewModel();
            Do(m, "TaskFailed", "developer");
            Do(m, "WorkDelivered", "tester");
            Assert.AreEqual("Completed", Level(m));
        }

        [Test]
        public void Focus_mode_accumulates_passive_and_completed_quietly()
        {
            object m = NewModel();
            Do(m, "SetFocus", true);
            Do(m, "AgentProgress", "developer");
            Do(m, "AgentProgress", "tester");
            Do(m, "WorkDelivered", "tester");
            Assert.AreEqual("None", Level(m), "nothing is raised while focused");
            Assert.AreEqual(3, Quiet(m));
        }

        [Test]
        public void Focus_mode_keeps_blocking_and_error_visible()
        {
            object m = NewModel();
            Do(m, "SetFocus", true);
            Do(m, "TaskFailed", "developer");
            Assert.AreEqual("Error", Level(m));
            Do(m, "DecisionRequested", "d1", "researcher");
            Assert.AreEqual("Blocking", Level(m));
            Assert.AreEqual(0, Quiet(m), "neither is counted as a quiet update");
        }

        [Test]
        public void Leaving_focus_surfaces_the_held_completion_and_keeps_the_count()
        {
            object m = NewModel();
            Do(m, "SetFocus", true);
            Do(m, "WorkDelivered", "tester");
            Assert.AreEqual("None", Level(m));
            Do(m, "SetFocus", false);
            Assert.AreEqual("Completed", Level(m));
            Assert.AreEqual("tester", Agent(m));
            Assert.AreEqual(1, Quiet(m));
            Do(m, "Acknowledge");
            Assert.AreEqual("None", Level(m));
            Assert.AreEqual(0, Quiet(m));
        }

        [Test]
        public void Entering_focus_again_starts_a_fresh_count()
        {
            object m = NewModel();
            Do(m, "SetFocus", true);
            Do(m, "AgentProgress", "developer");
            Do(m, "SetFocus", false);
            Assert.AreEqual(1, Quiet(m));
            Do(m, "SetFocus", true);
            Assert.AreEqual(0, Quiet(m));
        }

        [Test]
        public void Reset_clears_everything_but_the_focus_setting()
        {
            object m = NewModel();
            Do(m, "SetFocus", true);
            Do(m, "AgentProgress", "developer");
            Do(m, "TaskFailed", "developer");
            Do(m, "DecisionRequested", "d1", "researcher");
            Do(m, "Reset");
            Assert.AreEqual("None", Level(m));
            Assert.IsNull(Agent(m));
            Assert.AreEqual(0, Quiet(m));
            Assert.AreEqual(0, Pending(m));
            Assert.IsTrue((bool)SpatialReachReflect.Get(m, "Focus"));
            // Still focused: the next progress is quiet again.
            Do(m, "AgentProgress", "tester");
            Assert.AreEqual("None", Level(m));
            Assert.AreEqual(1, Quiet(m));
        }

        [Test]
        public void Changed_is_raised_only_when_something_changed()
        {
            object m = NewModel();
            int v0 = Version(m);
            Do(m, "AgentProgress", "developer");
            int v1 = Version(m);
            Assert.AreEqual(v0 + 1, v1);
            Do(m, "AgentProgress", "developer");
            Assert.AreEqual(v1, Version(m), "same level, same agent: no change");
            Do(m, "DecisionResolved", "nothing");
            Assert.AreEqual(v1, Version(m));
            Do(m, "SetFocus", false);
            Assert.AreEqual(v1, Version(m));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Transition hold balance
    // ─────────────────────────────────────────────────────────────────────────

    [Category("MuseDesk")]
    public class SpatialReachHoldTests
    {
        const string Ledger = "MuseDesk.Spatial.RemoteHoldLedger";
        const string State = "MuseDesk.Spatial.SpatialWorkspaceState";

        int _acquired, _released;

        object NewCountingLedger()
        {
            _acquired = 0; _released = 0;
            Action acquire = () => _acquired++;
            Action release = () => _released++;
            return SpatialReachReflect.New(Ledger, acquire, release);
        }

        static bool Holding(object l) { return (bool)SpatialReachReflect.Get(l, "Holding"); }

        [Test]
        public void Grab_then_release_is_one_acquire_and_one_release()
        {
            object l = NewCountingLedger();
            SpatialReachReflect.Call(l, "Set", true);      // grabbed
            SpatialReachReflect.Call(l, "Set", true);      // still held next frame
            SpatialReachReflect.Call(l, "Set", true);
            Assert.AreEqual(1, _acquired);
            Assert.IsTrue(Holding(l));
            SpatialReachReflect.Call(l, "Set", false);     // released
            SpatialReachReflect.Call(l, "Set", false);
            Assert.AreEqual(1, _released);
            Assert.IsFalse(Holding(l));
        }

        [Test]
        public void Resolve_or_reset_while_held_releases_exactly_once()
        {
            object l = NewCountingLedger();
            SpatialReachReflect.Call(l, "Acquire");        // grabbed
            SpatialReachReflect.Call(l, "Release");        // decision resolved, paper filed
            SpatialReachReflect.Call(l, "Release");        // session reset right after
            SpatialReachReflect.Call(l, "Release");        // OnDisable safety release
            Assert.AreEqual(1, _acquired);
            Assert.AreEqual(1, _released);
        }

        [Test]
        public void Release_without_a_hold_does_nothing()
        {
            object l = NewCountingLedger();
            SpatialReachReflect.Call(l, "Release");
            SpatialReachReflect.Call(l, "Set", false);
            Assert.AreEqual(0, _acquired);
            Assert.AreEqual(0, _released);
        }

        [Test]
        public void Any_interleaving_ends_balanced()
        {
            object l = NewCountingLedger();
            bool[] held = { true, true, false, true, false, false, true, true, true, false, true };
            for (int i = 0; i < held.Length; i++) SpatialReachReflect.Call(l, "Set", held[i]);
            SpatialReachReflect.Call(l, "Release");        // reset / disable at the end
            Assert.AreEqual(_acquired, _released);
            Assert.AreEqual((int)SpatialReachReflect.Get(l, "AcquireCount"), (int)SpatialReachReflect.Get(l, "ReleaseCount"));
            Assert.IsFalse(Holding(l));
        }

        [Test]
        public void Ledger_drives_the_shared_transition_hold_and_leaves_it_as_it_found_it()
        {
            Type state = SpatialReachReflect.T(State);
            var acquire = (Action)Delegate.CreateDelegate(typeof(Action),
                state.GetMethod("AcquireTransitionHold", BindingFlags.Static | BindingFlags.Public));
            var release = (Action)Delegate.CreateDelegate(typeof(Action),
                state.GetMethod("ReleaseTransitionHold", BindingFlags.Static | BindingFlags.Public));
            bool before = (bool)SpatialReachReflect.GetStatic(State, "TransitionHeld");

            object l = SpatialReachReflect.New(Ledger, acquire, release);
            SpatialReachReflect.Call(l, "Set", true);
            SpatialReachReflect.Call(l, "Set", true);
            Assert.IsTrue((bool)SpatialReachReflect.GetStatic(State, "TransitionHeld"));
            SpatialReachReflect.Call(l, "Set", false);
            SpatialReachReflect.Call(l, "Release");
            Assert.AreEqual(before, (bool)SpatialReachReflect.GetStatic(State, "TransitionHeld"));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Remote delivery state machine (injected clock)
    // ─────────────────────────────────────────────────────────────────────────

    [Category("MuseDesk")]
    public class SpatialReachDeliveryTests
    {
        const string Machine = "MuseDesk.Spatial.RemoteDeliveryMachine";

        static object NewMachine()
        {
            object m = SpatialReachReflect.New(Machine);
            SpatialReachReflect.SetField(m, "offerDwellSeconds", 0.7f);
            SpatialReachReflect.SetField(m, "agentWaitTimeoutSeconds", 3f);
            SpatialReachReflect.SetField(m, "platformSeconds", 0.5f);
            SpatialReachReflect.SetField(m, "flightSeconds", 1.0f);
            SpatialReachReflect.SetField(m, "courierTimeoutSeconds", 3f);
            return m;
        }

        // Tick(now, canDeliver, paperOffered, paperTaken, agentSettled)
        static void Tick(object m, float now, bool canDeliver, bool offered, bool taken, bool settled)
        {
            SpatialReachReflect.Call(m, "Tick", now, canDeliver, offered, taken, settled);
        }
        static string State(object m) { return SpatialReachReflect.Name(m, "State"); }
        static float Progress(object m) { return (float)SpatialReachReflect.Get(m, "Progress"); }
        static bool Snapped(object m) { return (bool)SpatialReachReflect.Get(m, "SnappedByTimeout"); }
        static int Arrivals(object m) { return (int)SpatialReachReflect.Get(m, "Arrivals"); }

        /// <summary>Run a delivery up to the start of the courier path; returns the time it started.</summary>
        static float ToCourierStart(object m)
        {
            Tick(m, 0f, true, true, false, false);          // paper appears, agent walking
            Tick(m, 1.0f, true, true, false, true);         // agent arrived
            Tick(m, 1.8f, true, true, false, true);         // offer dwell (0.7 s) is over
            Assert.AreEqual("ToPlatform", State(m));
            return 1.8f;
        }

        [Test]
        public void Desk_mode_never_leaves_idle()
        {
            object m = NewMachine();
            for (int i = 0; i < 50; i++) Tick(m, i * 0.1f, false, true, false, true);
            Assert.AreEqual("Idle", State(m));
            Tick(m, 6f, false, false, true, true);          // taken at the desk: tracked, nothing moved by us
            Assert.AreEqual("Taken", State(m));
            Tick(m, 7f, false, false, false, true);
            Assert.AreEqual("Idle", State(m));
            Assert.AreEqual(0, Arrivals(m));
        }

        [Test]
        public void Full_delivery_runs_office_platform_flight_hover_taken_idle()
        {
            object m = NewMachine();
            Tick(m, 0f, true, true, false, false);
            Assert.AreEqual("AtOffice", State(m));
            Tick(m, 0.5f, true, true, false, false);
            Assert.AreEqual("AtOffice", State(m), "waits while the agent walks");
            Tick(m, 1.0f, true, true, false, true);
            Assert.AreEqual("AtOffice", State(m), "the offer is left to read");
            Tick(m, 1.5f, true, true, false, true);
            Assert.AreEqual("AtOffice", State(m));
            Tick(m, 1.8f, true, true, false, true);
            Assert.AreEqual("ToPlatform", State(m));

            Tick(m, 2.05f, true, true, false, true);
            Assert.AreEqual("ToPlatform", State(m));
            Assert.AreEqual(0.5f, Progress(m), 0.02f);
            Tick(m, 2.35f, true, true, false, true);
            Assert.AreEqual("Flying", State(m));
            Assert.AreEqual(0f, Progress(m), 1e-4f);

            Tick(m, 2.85f, true, true, false, true);
            Assert.AreEqual("Flying", State(m));
            Assert.AreEqual(0.5f, Progress(m), 0.02f);
            Tick(m, 3.40f, true, true, false, true);
            Assert.AreEqual("Hovering", State(m));
            Assert.IsFalse(Snapped(m));
            Assert.AreEqual(1, Arrivals(m));

            Tick(m, 30f, true, true, false, true);
            Assert.AreEqual("Hovering", State(m), "it waits for the user as long as it takes");

            Tick(m, 31f, true, false, true, true);
            Assert.AreEqual("Taken", State(m));
            Tick(m, 40f, true, false, false, true);          // decided and filed: the paper is gone
            Assert.AreEqual("Idle", State(m));
        }

        [Test]
        public void Agent_that_never_settles_does_not_block_the_courier()
        {
            object m = NewMachine();
            Tick(m, 0f, true, true, false, false);
            Tick(m, 2.9f, true, true, false, false);
            Assert.AreEqual("AtOffice", State(m));
            Tick(m, 3.05f, true, true, false, false);
            Assert.AreEqual("ToPlatform", State(m));
        }

        [Test]
        public void Stalled_courier_path_snaps_to_the_user_after_the_timeout()
        {
            object m = NewMachine();
            SpatialReachReflect.SetField(m, "flightSeconds", 60f);     // a flight that would never finish
            float start = ToCourierStart(m);
            Tick(m, start + 0.6f, true, true, false, true);
            Assert.AreEqual("Flying", State(m));
            Tick(m, start + 2.9f, true, true, false, true);
            Assert.AreEqual("Flying", State(m));
            Assert.IsFalse(Snapped(m));
            Tick(m, start + 3.01f, true, true, false, true);
            Assert.AreEqual("Hovering", State(m));
            Assert.IsTrue(Snapped(m));
            Assert.AreEqual(1, Arrivals(m));
        }

        [Test]
        public void One_long_stall_on_the_platform_leg_also_snaps()
        {
            object m = NewMachine();
            float start = ToCourierStart(m);
            Tick(m, start + 10f, true, true, false, true);              // the frame loop stalled
            Assert.AreEqual("Hovering", State(m));
            Assert.IsTrue(Snapped(m));
        }

        [Test]
        public void Snap_now_puts_a_delivery_at_the_user_and_ignores_idle()
        {
            object m = NewMachine();
            SpatialReachReflect.Call(m, "SnapNow", 0f);
            Assert.AreEqual("Idle", State(m));
            Tick(m, 0f, true, true, false, false);
            SpatialReachReflect.Call(m, "SnapNow", 0.1f);
            Assert.AreEqual("Hovering", State(m));
            Assert.IsTrue(Snapped(m));
        }

        [Test]
        public void Snapped_flag_is_cleared_for_the_next_delivery()
        {
            object m = NewMachine();
            Tick(m, 0f, true, true, false, false);
            SpatialReachReflect.Call(m, "SnapNow", 0.1f);
            Tick(m, 1f, true, false, false, true);                      // resolved without being taken
            Assert.AreEqual("Idle", State(m));
            Tick(m, 2f, true, true, false, false);                      // next decision
            Assert.AreEqual("AtOffice", State(m));
            Assert.IsFalse(Snapped(m));
        }

        [Test]
        public void Grab_in_mid_flight_goes_straight_to_taken()
        {
            object m = NewMachine();
            float start = ToCourierStart(m);
            Tick(m, start + 0.6f, true, true, false, true);
            Assert.AreEqual("Flying", State(m));
            Tick(m, start + 0.8f, true, false, true, true);
            Assert.AreEqual("Taken", State(m));
        }

        [Test]
        public void Office_coming_within_reach_abandons_the_delivery()
        {
            object m = NewMachine();
            float start = ToCourierStart(m);
            Tick(m, start + 0.6f, true, true, false, true);
            Tick(m, start + 0.7f, false, true, false, true);            // no longer remote
            Assert.AreEqual("Idle", State(m));
            Tick(m, start + 0.8f, true, true, false, true);             // remote again: a fresh delivery
            Assert.AreEqual("AtOffice", State(m));
        }

        [Test]
        public void Paper_withdrawn_before_it_is_taken_returns_to_idle()
        {
            object m = NewMachine();
            float start = ToCourierStart(m);
            Tick(m, start + 0.2f, true, false, false, true);            // session reset hid the paper
            Assert.AreEqual("Idle", State(m));
        }

        [Test]
        public void A_new_decision_on_the_taken_sheet_starts_a_new_delivery()
        {
            object m = NewMachine();
            Tick(m, 0f, true, false, true, true);
            Assert.AreEqual("Taken", State(m));
            Tick(m, 1f, true, true, false, false);                      // revised proposal offered again
            Assert.AreEqual("AtOffice", State(m));
        }

        [Test]
        public void Reset_returns_to_idle_from_any_phase()
        {
            object m = NewMachine();
            float start = ToCourierStart(m);
            Tick(m, start + 0.6f, true, true, false, true);
            SpatialReachReflect.Call(m, "Reset");
            Assert.AreEqual("Idle", State(m));
            Assert.IsFalse(Snapped(m));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Voice phrases for summoning
    // ─────────────────────────────────────────────────────────────────────────

    [Category("MuseDesk")]
    public class SpatialReachVoicePhraseTests
    {
        static string Match(string transcript)
        {
            return Convert.ToString(SpatialReachReflect.CallStatic("MuseDesk.Spatial.SummonVoicePhrases", "Match", transcript));
        }

        [Test]
        public void Bring_phrases_ask_for_the_office()
        {
            Assert.AreEqual("BringOfficeHere", Match("bring down the office"));
            Assert.AreEqual("BringOfficeHere", Match("Heard: Bring the office here"));
        }

        [Test]
        public void Send_phrases_send_it_back_and_win_over_bring()
        {
            Assert.AreEqual("SendOfficeBack", Match("send the office back"));
            Assert.AreEqual("SendOfficeBack", Match("bring the office here then send it back"));
        }

        [Test]
        public void Decision_words_and_noise_are_not_summons()
        {
            Assert.AreEqual("None", Match("approve"));
            Assert.AreEqual("None", Match("reject"));
            Assert.AreEqual("None", Match("the office is nice"));
            Assert.AreEqual("None", Match(""));
            Assert.AreEqual("None", Match(null));
        }
    }
}
