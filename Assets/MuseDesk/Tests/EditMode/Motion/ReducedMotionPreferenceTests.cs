using System.Collections.Generic;
using NUnit.Framework;
using MuseDesk.Motion;

namespace MuseDesk.Tests.EditMode
{
    public class ReducedMotionPreferenceTests
    {
        sealed class FakeStore : IPrefStore
        {
            public readonly Dictionary<string, int> Data = new Dictionary<string, int>();
            public int GetInt(string key, int fallback) { int v; return Data.TryGetValue(key, out v) ? v : fallback; }
            public void SetInt(string key, int value) { Data[key] = value; }
        }

        [TearDown] public void Reset() { MotionSettings.SetReducedMotion(false); }

        [Test]
        public void Resolve_NothingSaved_FollowsTheBuildDefault()
        {
            var s = new FakeStore();
            Assert.IsFalse(ReducedMotionPreference.Resolve(s, false));
            Assert.IsTrue(ReducedMotionPreference.Resolve(s, true));
        }

        [Test]
        public void Resolve_ASavedChoice_BeatsTheBuildDefault_BothWays()
        {
            var s = new FakeStore();
            ReducedMotionPreference.Save(s, false);
            Assert.IsFalse(ReducedMotionPreference.Resolve(s, true));     // saved OFF wins over a build that defaults ON
            ReducedMotionPreference.Save(s, true);
            Assert.IsTrue(ReducedMotionPreference.Resolve(s, false));
        }

        [Test]
        public void Flip_SavesTheOpposite_AndPersistsAcrossAReload()
        {
            var s = new FakeStore();
            bool now = ReducedMotionPreference.Flip(s, false);
            Assert.IsTrue(now);
            Assert.AreEqual(1, s.Data[ReducedMotionPreference.Key]);
            Assert.IsTrue(ReducedMotionPreference.Resolve(s, false));     // the next start reads it back
            now = ReducedMotionPreference.Flip(s, now);
            Assert.IsFalse(now);
            Assert.AreEqual(0, s.Data[ReducedMotionPreference.Key]);
            Assert.IsFalse(ReducedMotionPreference.Resolve(s, true));
        }

        [Test]
        public void Key_IsTheOneTheApplierAlwaysUsed()
        {
            Assert.AreEqual("MuseDesk.ReducedMotion", ReducedMotionPreference.Key);
        }

        [Test]
        public void KeyText_SaysTheStateInWords()
        {
            Assert.AreEqual("Calm\nmotion\nON", ReducedMotionPreference.KeyText(true));
            Assert.AreEqual("Calm\nmotion\nOFF", ReducedMotionPreference.KeyText(false));
            Assert.AreEqual("Calm motion", ReducedMotionPreference.Label);
        }

        [Test]
        public void KeyVisible_OnlyWhenTheFlagIsOn()
        {
            Assert.IsFalse(ReducedMotionPreference.KeyVisible(false));
            Assert.IsTrue(ReducedMotionPreference.KeyVisible(true));
        }

        [Test]
        public void ApplyingTheFlippedChoice_ChangesMotionSettings_AndRaisesChangedOnce()
        {
            var s = new FakeStore();
            int raised = 0;
            System.Action h = () => raised++;
            MotionSettings.Changed += h;
            try
            {
                bool now = ReducedMotionPreference.Flip(s, MotionSettings.ReducedMotion);
                MotionSettings.SetReducedMotion(now);
                MotionSettings.SetReducedMotion(now);          // setting the same value again is silent
                Assert.IsTrue(MotionSettings.ReducedMotion);
                Assert.AreEqual(1, raised);
            }
            finally { MotionSettings.Changed -= h; }
        }
    }
}
