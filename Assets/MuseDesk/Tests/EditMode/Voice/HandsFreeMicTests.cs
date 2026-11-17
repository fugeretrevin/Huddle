using System.Collections.Generic;
using NUnit.Framework;
using MuseDesk.Voice;

namespace MuseDesk.Tests.Voice
{
    public class HandsFreeMicTests
    {
        sealed class Store : IMicPrefStore
        {
            public readonly Dictionary<string, int> D = new Dictionary<string, int>();
            public int GetInt(string k, int f) { return D.TryGetValue(k, out var v) ? v : f; }
            public void SetInt(string k, int v) { D[k] = v; }
        }

        static MicAction Idle(HandsFreeMic m, float t, MicProblem a = MicProblem.None) { return m.Tick(t, a, false, false, true); }

        [Test]
        public void MutedByDefault_NeverBegins()
        {
            var m = new HandsFreeMic();
            for (float t = 0; t < 100; t += 1f) Assert.AreEqual(MicAction.None, Idle(m, t));
            Assert.AreEqual(MicPhase.Muted, m.Phase);
            Assert.AreEqual(0, m.BeginCount);
        }

        [Test]
        public void Preference_DefaultsMuted_AndPersists()
        {
            var s = new Store();
            Assert.IsFalse(MicPreference.Load(s));
            MicPreference.Save(s, true);
            Assert.IsTrue(MicPreference.Load(s));
            MicPreference.Save(s, false);
            Assert.IsFalse(MicPreference.Load(s));
            Assert.IsFalse(MicPreference.Load(null));
        }

        [Test]
        public void Unmute_BeginsAtOnce_ThenWaitsForTheGrace()
        {
            var m = new HandsFreeMic();
            m.SetUnmuted(true, 10f);
            Assert.AreEqual(MicAction.Begin, Idle(m, 10f));
            Assert.AreEqual(MicAction.None, Idle(m, 11f), "no second request while the microphone is still opening");
            Assert.AreEqual(MicAction.None, m.Tick(11f, MicProblem.None, true, false, true));
            Assert.AreEqual(MicPhase.Listening, m.Phase);
        }

        [Test]
        public void Hearing_ThenProcessing_ThenRearmsAfterDelay()
        {
            var m = new HandsFreeMic();
            m.SetUnmuted(true, 0f);
            Idle(m, 0f);
            m.NotifyListeningChanged(true, 0.5f);
            m.NotifyPartial();
            m.Tick(1f, MicProblem.None, true, false, true);
            Assert.AreEqual(MicPhase.Hearing, m.Phase);
            m.NotifyListeningChanged(false, 2f);
            m.Tick(2f, MicProblem.None, false, true, true);
            Assert.AreEqual(MicPhase.Processing, m.Phase);
            m.NotifyFinal(2.2f);
            Assert.AreEqual(MicAction.None, Idle(m, 2.4f));
            Assert.AreEqual(MicAction.Begin, Idle(m, 2.8f));
        }

        [Test]
        public void NoSpeech_RearmsQuickly_AndIsNotAnError()
        {
            var m = new HandsFreeMic();
            m.SetUnmuted(true, 0f);
            Idle(m, 0f);
            m.NotifyProblem(MicProblem.NoSpeech, 5f);
            Assert.AreEqual(0, m.ConsecutiveErrors);
            Assert.AreEqual(MicAction.None, Idle(m, 5.3f));
            Assert.AreEqual(MicAction.Begin, Idle(m, 5.7f));
        }

        [Test]
        public void Errors_BackOffExponentially_AndCap()
        {
            var m = new HandsFreeMic();
            Assert.AreEqual(1f, m.BackoffFor(1)); Assert.AreEqual(2f, m.BackoffFor(2));
            Assert.AreEqual(4f, m.BackoffFor(3)); Assert.AreEqual(30f, m.BackoffFor(20));
            m.SetUnmuted(true, 0f);
            Idle(m, 0f);
            m.NotifyProblem(MicProblem.ServiceError, 10f);
            m.NotifyProblem(MicProblem.Timeout, 10f);
            Assert.AreEqual(2, m.ConsecutiveErrors);
            Assert.AreEqual(MicAction.None, Idle(m, 11.9f));
            Assert.AreEqual(MicAction.Begin, Idle(m, 12.1f));
        }

        [Test]
        public void ErrorLoop_NeverBeginsFasterThanBackoff()
        {
            var m = new HandsFreeMic();
            m.SetUnmuted(true, 0f);
            int begins = 0; float t = 0f;
            while (t < 300f)
            {
                if (Idle(m, t) == MicAction.Begin) { begins++; m.NotifyProblem(MicProblem.ServiceError, t); }
                t += 0.1f;
            }
            Assert.Less(begins, 16, "1+2+4+8+16 then 30 s steps over 300 s");
        }

        [Test]
        public void Final_ResetsBackoff()
        {
            var m = new HandsFreeMic();
            m.SetUnmuted(true, 0f);
            m.NotifyProblem(MicProblem.ServiceError, 1f);
            m.NotifyProblem(MicProblem.ServiceError, 1f);
            m.NotifyFinal(5f);
            Assert.AreEqual(0, m.ConsecutiveErrors);
        }

        [Test]
        public void PermanentProblems_LatchUnavailable_UntilToggled()
        {
            var m = new HandsFreeMic();
            m.SetUnmuted(true, 0f);
            Idle(m, 0f);
            m.NotifyProblem(MicProblem.PermissionDenied, 1f);
            Assert.AreEqual(MicAction.None, Idle(m, 100f));
            Assert.AreEqual(MicPhase.Unavailable, m.Phase);
            m.SetUnmuted(false, 101f);
            m.SetUnmuted(true, 102f);
            Assert.AreEqual(MicAction.Begin, Idle(m, 102f));
        }

        [Test]
        public void NoRecognizer_AndNoToken_AreUnavailable_NotPretending()
        {
            var m = new HandsFreeMic();
            m.SetUnmuted(true, 0f);
            Assert.AreEqual(MicAction.None, Idle(m, 1f, MicProblem.NoRecognizer));
            Assert.AreEqual(MicPhase.Unavailable, m.Phase);
            Assert.AreEqual(MicAction.None, Idle(m, 2f, MicProblem.NoToken));
            Assert.AreEqual("Voice\noffline", MicPreference.KeyText(m.Phase));
        }

        [Test]
        public void NoInternet_ShowsOffline_AndRecoversByItself()
        {
            var m = new HandsFreeMic();
            m.SetUnmuted(true, 0f);
            Assert.AreEqual(MicAction.None, Idle(m, 1f, MicProblem.NoInternet));
            Assert.AreEqual(MicPhase.Unavailable, m.Phase);
            Assert.AreEqual(MicAction.Begin, Idle(m, 2f, MicProblem.None));
        }

        [Test]
        public void Mute_StopsEverything()
        {
            var m = new HandsFreeMic();
            m.SetUnmuted(true, 0f);
            m.SetUnmuted(false, 1f);
            Assert.AreEqual(MicAction.None, Idle(m, 50f));
            Assert.AreEqual(MicPhase.Muted, m.Phase);
        }

        [Test]
        public void MayBeginFalse_HoldsTheLoop()
        {
            var m = new HandsFreeMic();
            m.SetUnmuted(true, 0f);
            Assert.AreEqual(MicAction.None, m.Tick(5f, MicProblem.None, false, false, false));
            Assert.AreEqual(MicAction.Begin, m.Tick(5.1f, MicProblem.None, false, false, true));
        }

        [Test]
        public void KeyText_FitsTwoShortLines()
        {
            foreach (MicPhase p in System.Enum.GetValues(typeof(MicPhase)))
            {
                string[] lines = MicPreference.KeyText(p).Split('\n');
                Assert.AreEqual(2, lines.Length);
                foreach (var l in lines) Assert.LessOrEqual(l.Length, 7, l);
            }
        }
    }
}
