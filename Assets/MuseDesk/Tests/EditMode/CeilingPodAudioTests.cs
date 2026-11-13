// =============================================================================
// MuseDesk · EditMode tests · ceiling pod audio (pure logic and synthesised clips)
// =============================================================================
// Covers CeilingPodAudioCues (state change -> cue, site, travel pitch) and CeilingPodAudioSynth
// (length, no NaN, peak, loop ends). NOT covered: the AudioSource component, spatialisation, mute.
// =============================================================================

#if MUSEDESK_SPATIAL_TESTS
using NUnit.Framework;
using UnityEngine;
using MuseDesk.Spatial;

namespace MuseDesk.Tests.EditMode
{
    [Category("MuseDesk")]
    public class CeilingPodAudioCueTests
    {
        const CeilingPodState D = CeilingPodState.Docked;

        [Test]
        public void UnlockOnBothPreparingStates()
        {
            Assert.AreEqual(CeilingAudioCue.Unlock, CeilingPodAudioCues.CueFor(D, CeilingPodState.PreparingToDescend));
            Assert.AreEqual(CeilingAudioCue.Unlock, CeilingPodAudioCues.CueFor(CeilingPodState.Engaged, CeilingPodState.PreparingToRetract));
            Assert.AreEqual(CeilingAudioSite.Mount, CeilingPodAudioCues.SiteFor(CeilingPodState.PreparingToDescend));
            Assert.AreEqual(CeilingAudioSite.Pod, CeilingPodAudioCues.SiteFor(CeilingPodState.PreparingToRetract));
        }

        [Test]
        public void LockOnArrivalBelowAndOnDockAfterReturning()
        {
            Assert.AreEqual(CeilingAudioCue.Lock, CeilingPodAudioCues.CueFor(CeilingPodState.Descending, CeilingPodState.Stabilizing));
            Assert.AreEqual(CeilingAudioSite.Pod, CeilingPodAudioCues.SiteFor(CeilingPodState.Stabilizing));
            Assert.AreEqual(CeilingAudioCue.Lock, CeilingPodAudioCues.CueFor(CeilingPodState.ReturningToDock, D));
            Assert.AreEqual(CeilingAudioSite.Mount, CeilingPodAudioCues.SiteFor(D));
        }

        [Test]
        public void NoCueForOtherChanges()
        {
            Assert.AreEqual(CeilingAudioCue.None, CeilingPodAudioCues.CueFor(CeilingPodState.ErrorRecovery, D));
            Assert.AreEqual(CeilingAudioCue.None, CeilingPodAudioCues.CueFor(CeilingPodState.PreparingToDescend, D));   // stop before moving
            Assert.AreEqual(CeilingAudioCue.None, CeilingPodAudioCues.CueFor(CeilingPodState.Stabilizing, CeilingPodState.Engaged));
            Assert.AreEqual(CeilingAudioCue.None, CeilingPodAudioCues.CueFor(CeilingPodState.Descending, CeilingPodState.Retracting));   // Stop: no extra cue
            Assert.AreEqual(CeilingAudioCue.None, CeilingPodAudioCues.CueFor(CeilingPodState.Retracting, CeilingPodState.ReturningToDock));
            Assert.AreEqual(CeilingAudioCue.None, CeilingPodAudioCues.CueFor(D, D));
        }

        [Test]
        public void TravelStatesAndDirection()
        {
            foreach (var s in new[] { CeilingPodState.Descending, CeilingPodState.Retracting, CeilingPodState.ReturningToDock, CeilingPodState.ErrorRecovery })
                Assert.IsTrue(CeilingPodAudioCues.IsTravel(s), s.ToString());
            foreach (var s in new[] { D, CeilingPodState.PreparingToDescend, CeilingPodState.Stabilizing, CeilingPodState.Engaged, CeilingPodState.PreparingToRetract })
                Assert.IsFalse(CeilingPodAudioCues.IsTravel(s), s.ToString());
            Assert.AreEqual(-1, CeilingPodAudioCues.Direction(CeilingPodState.Descending));
            Assert.AreEqual(1, CeilingPodAudioCues.Direction(CeilingPodState.Retracting));
            Assert.AreEqual(1, CeilingPodAudioCues.Direction(CeilingPodState.ErrorRecovery));
            Assert.AreEqual(0, CeilingPodAudioCues.Direction(CeilingPodState.Engaged));
        }

        [Test]
        public void PitchDownLowerThanUp()
        {
            float down = CeilingPodAudioCues.TravelPitch(-1), up = CeilingPodAudioCues.TravelPitch(1);
            Assert.Less(down, 1f);
            Assert.Greater(up, 1f);
            Assert.Less(up - down, 0.4f, "only slightly different");
        }
    }

    [Category("MuseDesk")]
    public class CeilingPodAudioSynthTests
    {
        static void CheckClean(float[] d, float peakLimit)
        {
            float peak = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(d[i]) || float.IsInfinity(d[i]), "bad sample at " + i);
                peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            }
            Assert.LessOrEqual(peak, peakLimit);
            Assert.Greater(peak, 0.1f, "not silent");
        }

        [Test]
        public void UnlockLengthAndPeak()
        {
            float[] d = CeilingPodAudioSynth.UnlockSamples();
            Assert.AreEqual((int)(CeilingPodAudioSynth.Rate * 0.4f), d.Length);
            CheckClean(d, 0.9f);
            Assert.AreEqual(0f, d[d.Length - 1], 1e-4f);
        }

        [Test]
        public void LockLengthAndPeak()
        {
            float[] d = CeilingPodAudioSynth.LockSamples();
            Assert.AreEqual((int)(CeilingPodAudioSynth.Rate * 0.5f), d.Length);
            CheckClean(d, 0.9f);
            Assert.AreEqual(0f, d[d.Length - 1], 1e-4f);
        }

        [Test]
        public void LoopStartsAndEndsNearZero()
        {
            float[] d = CeilingPodAudioSynth.LoopSamples();
            Assert.AreEqual(CeilingPodAudioSynth.Rate * 2, d.Length);
            CheckClean(d, 0.9f);
            Assert.AreEqual(0f, d[0], 0.02f);
            Assert.AreEqual(0f, d[d.Length - 1], 0.03f);
            // The join: the step across the loop boundary is no bigger than the largest step inside.
            float maxStep = 0f;
            for (int i = 1; i < d.Length; i++) maxStep = Mathf.Max(maxStep, Mathf.Abs(d[i] - d[i - 1]));
            Assert.LessOrEqual(Mathf.Abs(d[0] - d[d.Length - 1]), maxStep);
        }

        [Test]
        public void ClipsCanBeCreated()
        {
            AudioClip a = CeilingPodAudioSynth.UnlockClip(), b = CeilingPodAudioSynth.LockClip(), c = CeilingPodAudioSynth.LoopClip();
            Assert.AreEqual(CeilingPodAudioSynth.Rate * 2, c.samples);
            Assert.AreEqual(0.4f, a.length, 0.01f);
            Assert.AreEqual(0.5f, b.length, 0.01f);
            Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(c);
        }
    }
}
#endif
