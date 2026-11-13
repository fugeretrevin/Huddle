// =============================================================================
// MuseDesk · EditMode tests · the stamp leaves after a decision and comes back
// =============================================================================
//
// Exercises the REAL runtime type MuseDesk.Documents.StampPoofMachine by reflection
// (the test assembly cannot reference Assembly-CSharp; see SpatialReachTests.cs).
// The machine is a plain C# class with primitive-typed members. No scene involved.
// =============================================================================

using System;
using System.Reflection;
using NUnit.Framework;

namespace MuseDesk.Tests.EditMode
{
    public class StampPoofTests
    {
        const string TypeName = "MuseDesk.Documents.StampPoofMachine";
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public;
        const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public;

        // Mirrors of the machine's public constants (checked against the type in Constants_Match).
        const int Shown = 0, Waiting = 1, Leaving = 2, Hidden = 3, Arriving = 4;
        const int EvNone = 0, EvLeaveStarted = 1, EvPop = 2, EvHidden = 3, EvShown = 4;

        static Type T()
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(TypeName, false);
                if (t != null) return t;
            }
            Assert.Fail("Runtime type not found: " + TypeName);
            return null;
        }

        static object New(float linger = 2f, float leave = 0.25f, float arrive = 0.3f, float grace = 0.15f)
        {
            return Activator.CreateInstance(T(), linger, leave, arrive, grace);
        }

        static object Call(object m, string method, params object[] args)
        {
            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++) types[i] = args[i].GetType();
            MethodInfo mi = T().GetMethod(method, Inst, null, types, null);
            Assert.IsNotNull(mi, "method not found: " + method);
            return mi.Invoke(m, args);
        }

        static object Get(object m, string prop)
        {
            PropertyInfo pi = T().GetProperty(prop, Inst);
            Assert.IsNotNull(pi, "property not found: " + prop);
            return pi.GetValue(m);
        }

        static float Curve(string name, float t)
        {
            MethodInfo mi = T().GetMethod(name, Stat, null, new[] { typeof(float) }, null);
            Assert.IsNotNull(mi, "static method not found: " + name);
            return (float)mi.Invoke(null, new object[] { t });
        }

        static int Phase(object m) { return (int)Get(m, "Phase"); }
        static int Tick(object m, float dt, bool held = false) { return (int)Call(m, "Tick", dt, held); }

        /// <summary>Tick in 1/72 s steps until an event comes or the time runs out.</summary>
        static int RunUntilEvent(object m, float maxSeconds, bool held = false)
        {
            const float dt = 1f / 72f;
            for (float t = 0f; t < maxSeconds; t += dt)
            {
                int ev = Tick(m, dt, held);
                if (ev != EvNone) return ev;
            }
            return EvNone;
        }

        [Test]
        public void Constants_Match()
        {
            Type t = T();
            Assert.AreEqual(Shown, (int)t.GetField("Shown", Stat).GetValue(null));
            Assert.AreEqual(Waiting, (int)t.GetField("Waiting", Stat).GetValue(null));
            Assert.AreEqual(Leaving, (int)t.GetField("Leaving", Stat).GetValue(null));
            Assert.AreEqual(Hidden, (int)t.GetField("Hidden", Stat).GetValue(null));
            Assert.AreEqual(Arriving, (int)t.GetField("Arriving", Stat).GetValue(null));
            Assert.AreEqual(EvLeaveStarted, (int)t.GetField("EventLeaveStarted", Stat).GetValue(null));
            Assert.AreEqual(EvPop, (int)t.GetField("EventPop", Stat).GetValue(null));
            Assert.AreEqual(EvHidden, (int)t.GetField("EventHidden", Stat).GetValue(null));
            Assert.AreEqual(EvShown, (int)t.GetField("EventShown", Stat).GetValue(null));
        }

        [Test]
        public void Starts_Shown_And_Grabbable_And_Stays_So()
        {
            object m = New();
            Assert.AreEqual(Shown, Phase(m));
            Assert.IsTrue((bool)Get(m, "Grabbable"));
            Assert.AreEqual(EvNone, RunUntilEvent(m, 5f));
            Assert.AreEqual(Shown, Phase(m));
            Assert.AreEqual(1f, (float)Get(m, "ScaleY"));
        }

        [Test]
        public void Dismiss_Lingers_Then_Leaves_Pops_And_Hides()
        {
            object m = New();
            Assert.IsTrue((bool)Call(m, "Dismiss"));
            Assert.AreEqual(Waiting, Phase(m));
            Assert.IsTrue((bool)Get(m, "Grabbable"), "still grabbable while it lingers");

            Assert.AreEqual(EvNone, RunUntilEvent(m, 1.9f), "nothing happens before the linger is over");
            Assert.AreEqual(EvLeaveStarted, RunUntilEvent(m, 0.2f));
            Assert.AreEqual(Leaving, Phase(m));
            Assert.IsFalse((bool)Get(m, "Grabbable"), "not grabbable from the moment it starts to leave");

            Assert.AreEqual(EvPop, RunUntilEvent(m, 0.25f));
            Assert.AreEqual(EvHidden, RunUntilEvent(m, 0.25f));
            Assert.AreEqual(Hidden, Phase(m));
            Assert.Less((float)Get(m, "ScaleY"), 0.01f);
            Assert.Less((float)Get(m, "ScaleXZ"), 0.01f);
            Assert.Greater((float)Get(m, "ScaleY"), 0f, "hidden by a tiny scale, never zero");
        }

        [Test]
        public void Held_Stamp_Waits_For_Release_Then_Grace()
        {
            object m = New();
            Call(m, "Dismiss");
            Assert.AreEqual(EvNone, RunUntilEvent(m, 6f, held: true), "never leaves the hand");
            Assert.AreEqual(Waiting, Phase(m));

            Assert.AreEqual(EvNone, Tick(m, 0.05f, false), "a short beat after the release");
            Assert.AreEqual(EvNone, Tick(m, 0.05f, false));
            Assert.AreEqual(EvLeaveStarted, RunUntilEvent(m, 0.2f));
        }

        [Test]
        public void Regrab_During_Grace_Keeps_It()
        {
            object m = New();
            Call(m, "Dismiss");
            RunUntilEvent(m, 3f, held: true);
            Tick(m, 0.05f, false);
            Assert.AreEqual(EvNone, RunUntilEvent(m, 2f, held: true));
            Assert.AreEqual(Waiting, Phase(m));
        }

        [Test]
        public void Summon_While_Lingering_Cancels_The_Leave()
        {
            object m = New();
            Call(m, "Dismiss");
            RunUntilEvent(m, 1f);
            Assert.IsFalse((bool)Call(m, "Summon"), "no arrival needed: it never left");
            Assert.AreEqual(Shown, Phase(m));
            Assert.AreEqual(EvNone, RunUntilEvent(m, 5f));
        }

        [Test]
        public void Summon_From_Hidden_Arrives_And_Is_Grabbable_Only_When_Shown()
        {
            object m = New();
            Call(m, "ForceHidden");
            Assert.IsFalse((bool)Get(m, "Grabbable"));
            Assert.IsTrue((bool)Call(m, "Summon"));
            Assert.AreEqual(Arriving, Phase(m));
            Assert.IsFalse((bool)Get(m, "Grabbable"));
            Assert.AreEqual(EvShown, RunUntilEvent(m, 0.4f));
            Assert.AreEqual(Shown, Phase(m));
            Assert.IsTrue((bool)Get(m, "Grabbable"));
            Assert.AreEqual(1f, (float)Get(m, "ScaleXZ"));
        }

        [Test]
        public void Summon_Mid_Leave_Turns_Round()
        {
            object m = New();
            Call(m, "Dismiss");
            RunUntilEvent(m, 2.2f);                 // leave started
            Tick(m, 0.05f);
            Assert.AreEqual(Leaving, Phase(m));
            Assert.IsTrue((bool)Call(m, "Summon"));
            Assert.AreEqual(Arriving, Phase(m));
            Assert.Greater((float)Get(m, "Progress"), 0.5f, "resumes from about where the leave had got to");
            Assert.AreEqual(EvShown, RunUntilEvent(m, 0.4f));
        }

        [Test]
        public void Dismiss_While_Arriving_Leaves_After_Arrival()
        {
            object m = New();
            Call(m, "ForceHidden");
            Call(m, "Summon");
            Assert.IsTrue((bool)Call(m, "Dismiss"));
            Assert.AreEqual(EvShown, RunUntilEvent(m, 0.4f));
            Assert.AreEqual(Waiting, Phase(m));
            Assert.AreEqual(EvLeaveStarted, RunUntilEvent(m, 2.2f));
        }

        [Test]
        public void Dismiss_When_Already_Gone_Does_Nothing()
        {
            object m = New();
            Call(m, "ForceHidden");
            Assert.IsFalse((bool)Call(m, "Dismiss"));
            Assert.AreEqual(Hidden, Phase(m));
            Assert.AreEqual(EvNone, RunUntilEvent(m, 3f));
        }

        [Test]
        public void Leave_Curve_Squashes_Pops_And_Ends_Gone()
        {
            Assert.AreEqual(1f, Curve("LeaveY", 0f), 1e-4f);
            Assert.AreEqual(1f, Curve("LeaveXZ", 0f), 1e-4f);
            Assert.Less(Curve("LeaveY", 0.38f), 0.8f, "squashed: shorter");
            Assert.Greater(Curve("LeaveXZ", 0.38f), 1.1f, "squashed: wider");
            Assert.Greater(Curve("LeaveY", 0.62f), 1.1f, "popped: taller");
            Assert.Less(Curve("LeaveY", 1f), 0.01f);
            Assert.Less(Curve("LeaveXZ", 1f), 0.01f);
            for (float t = 0f; t <= 1f; t += 0.02f)
            {
                Assert.Greater(Curve("LeaveY", t), 0f);
                Assert.Less(Curve("LeaveY", t), 1.3f);
                Assert.Less(Curve("LeaveXZ", t), 1.3f);
            }
        }

        [Test]
        public void Arrive_Curve_Overshoots_A_Little_And_Ends_Full()
        {
            Assert.Less(Curve("ArriveY", 0f), 0.01f);
            Assert.AreEqual(1f, Curve("ArriveY", 1f), 1e-4f);
            Assert.AreEqual(1f, Curve("ArriveXZ", 1f), 1e-4f);
            float peak = 0f;
            for (float t = 0f; t <= 1f; t += 0.02f)
            {
                Assert.Greater(Curve("ArriveXZ", t), 0f);
                peak = Math.Max(peak, Curve("ArriveXZ", t));
            }
            Assert.Greater(peak, 1.02f);
            Assert.Less(peak, 1.2f);
        }
    }
}
