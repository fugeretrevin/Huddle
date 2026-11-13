using System;
using System.Reflection;
using NUnit.Framework;

namespace MuseDesk.Tests.EditMode
{
    /// <summary>Tests the pure timing logic of the ceiling scan grid by reflection (the test assembly cannot reference Assembly-CSharp).</summary>
    public class CeilingScanRevealTests
    {
        static Type T()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType("MuseDesk.Spatial.CeilingScanRevealLogic");
                if (t != null) return t;
            }
            Assert.Fail("CeilingScanRevealLogic not found");
            return null;
        }
        static object Call(string name, params object[] args) => T().GetMethod(name, BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
        static float F(string name, params object[] args) => (float)Call(name, args);
        static bool B(string name, params object[] args) => (bool)Call(name, args);
        static float Const(string name) => (float)T().GetField(name, BindingFlags.Public | BindingFlags.Static).GetRawConstantValue();

        [Test] public void Constants() { Assert.AreEqual(0.6f, Const("WipeSeconds")); Assert.AreEqual(0.3f, Const("FadeOutSeconds")); }

        [Test]
        public void Radius_StartsAtZero_EndsAtMax_AndIsMonotonic()
        {
            Assert.AreEqual(0f, F("RevealRadius", 0f, 0.6f, 0.9f), 1e-6f);
            Assert.AreEqual(0.9f, F("RevealRadius", 0.6f, 0.6f, 0.9f), 1e-6f);
            Assert.AreEqual(0.9f, F("RevealRadius", 5f, 0.6f, 0.9f), 1e-6f);
            float prev = -1f;
            for (int i = 0; i <= 12; i++)
            {
                float r = F("RevealRadius", i * 0.05f, 0.6f, 0.9f);
                Assert.GreaterOrEqual(r, prev);
                prev = r;
            }
            Assert.Greater(F("RevealRadius", 0.3f, 0.6f, 0.9f), 0.45f, "ease-out: past half way at half time");
        }

        [Test] public void Radius_ZeroDuration_IsInstant() => Assert.AreEqual(0.9f, F("RevealRadius", 0f, 0f, 0.9f));

        [Test]
        public void Wipe_StopsAtDuration()
        {
            Assert.AreEqual(0.6f, F("StepWipe", 0.55f, 1f, 0.6f));
            Assert.AreEqual(0.1f, F("StepWipe", 0f, 0.1f, 0.6f), 1e-6f);
            Assert.AreEqual(0.2f, F("StepWipe", 0.2f, -1f, 0.6f), 1e-6f);
        }

        [Test]
        public void Fade_OutTakesAboutThreeTenthsOfASecond()
        {
            float f = 1f;
            for (int i = 0; i < 29; i++) f = F("StepFade", f, false, 0.01f, 0.1f, 0.3f);
            Assert.Greater(f, 0f);
            f = F("StepFade", f, false, 0.02f, 0.1f, 0.3f);
            Assert.AreEqual(0f, f, 1e-5f);
            Assert.IsFalse(B("Visible", f));
        }

        [Test]
        public void Fade_InIsQuick_AndClamped()
        {
            Assert.AreEqual(1f, F("StepFade", 0f, true, 0.1f, 0.1f, 0.3f), 1e-6f);
            Assert.AreEqual(1f, F("StepFade", 0.95f, true, 1f, 0.1f, 0.3f));
            Assert.AreEqual(0f, F("StepFade", 0.1f, false, 5f, 0.1f, 0.3f));
        }

        [Test]
        public void WantShown_NeedsCeilingPreviewAndARealHeight()
        {
            Assert.IsTrue(B("WantShown", true, true, 2.5f));
            Assert.IsFalse(B("WantShown", false, true, 2.5f), "not previewing (confirm, cancel)");
            Assert.IsFalse(B("WantShown", true, false, 2.5f), "floor preview");
            Assert.IsFalse(B("WantShown", true, true, 0f), "no ceiling known");
        }

        [Test]
        public void Restart_OnStart_OnCandidateChange_OnBigMove_NotOnNudge()
        {
            Assert.IsTrue(B("ShouldRestart", false, true, false, 0f));
            Assert.IsTrue(B("ShouldRestart", true, true, true, 0f));
            Assert.IsTrue(B("ShouldRestart", true, true, false, 0.4f));
            Assert.IsFalse(B("ShouldRestart", true, true, false, 0.05f));
            Assert.IsFalse(B("ShouldRestart", true, false, true, 1f), "never restarts while not wanted");
        }
    }
}
