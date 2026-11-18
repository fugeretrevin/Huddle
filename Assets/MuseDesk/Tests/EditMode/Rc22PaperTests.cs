using System;
using System.Reflection;
using NUnit.Framework;

namespace MuseDesk.Tests.EditMode
{
    /// <summary>rc22: which why-answers an agent says aloud, and that a gone stamp is never grabbable.
    /// Runtime types are reached by reflection (the test assembly cannot reference Assembly-CSharp).</summary>
    public class Rc22PaperTests
    {
        static Type Find(string name)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(name, false);
                if (t != null) return t;
            }
            Assert.Fail("Runtime type not found: " + name);
            return null;
        }

        static bool Speakable(string s)
        {
            MethodInfo mi = Find("MuseDesk.Documents.DecisionPanel").GetMethod("IsSpeakableAnswer", BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(mi);
            return (bool)mi.Invoke(null, new object[] { s });
        }

        [Test]
        public void RealAnswer_IsSpoken()
        {
            Assert.IsTrue(Speakable("The tests cover the login path, so this is low risk."));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("Asking...")]
        [TestCase("(no answer)")]
        [TestCase("(couldn't reach the backend for an answer)")]
        [TestCase("(no decision to ask about yet)")]
        public void Placeholders_AreNotSpoken(string s)
        {
            Assert.IsFalse(Speakable(s));
        }

        [Test]
        public void Stamp_NotGrabbable_WhileLeavingHiddenOrArriving()
        {
            Type t = Find("MuseDesk.Documents.StampPoofMachine");
            object m = Activator.CreateInstance(t, 0f, 0.25f, 0.3f, 0.15f);
            MethodInfo tick = t.GetMethod("Tick");
            PropertyInfo grab = t.GetProperty("Grabbable");
            Assert.IsTrue((bool)grab.GetValue(m));
            t.GetMethod("Dismiss").Invoke(m, null);
            Assert.IsTrue((bool)grab.GetValue(m), "waiting still grabbable");
            tick.Invoke(m, new object[] { 0.01f, false });          // leave starts
            Assert.IsFalse((bool)grab.GetValue(m), "leaving");
            tick.Invoke(m, new object[] { 1f, false });             // hidden
            Assert.IsFalse((bool)grab.GetValue(m), "hidden");
            t.GetMethod("Summon").Invoke(m, null);
            Assert.IsFalse((bool)grab.GetValue(m), "arriving");
            tick.Invoke(m, new object[] { 1f, false });
            Assert.IsTrue((bool)grab.GetValue(m), "shown again");
        }
    }
}
