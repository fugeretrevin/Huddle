using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>The one-shot sounds of the ceiling pod. Travel is a loop and has no cue.</summary>
    public enum CeilingAudioCue
    {
        None = 0,
        /// <summary>Latch release: two soft clicks and a small air hiss.</summary>
        Unlock = 1,
        /// <summary>A gentle thunk and a bright confirming chime.</summary>
        Lock = 2,
    }

    /// <summary>Where a one-shot cue is heard from.</summary>
    public enum CeilingAudioSite
    {
        /// <summary>The ceiling mount (docking / undocking above).</summary>
        Mount = 0,
        /// <summary>The pod with the office (arriving or leaving below).</summary>
        Pod = 1,
    }

    /// <summary>
    /// Pure rules of the ceiling pod's three-stage sound: which state change makes which cue, where
    /// the cue is heard, and what the travel loop's pitch is. No scene, no AudioSource.
    ///
    ///   Unlock : PreparingToDescend (at the mount), PreparingToRetract (at the pod)
    ///   Travel : Descending (low), Retracting / ReturningToDock / ErrorRecovery (high); a loop
    ///   Lock   : Stabilizing (at the pod, arrival below), Docked after ReturningToDock (at the mount)
    ///
    /// A Stop that reverses a descent is just Descending -> Retracting: the loop stays on and only
    /// its pitch goes up; there is no cue for it.
    /// </summary>
    public static class CeilingPodAudioCues
    {
        public const float PitchDown = 0.90f;
        public const float PitchUp = 1.10f;
        /// <summary>Seconds the loop takes to fade in or out.</summary>
        public const float FadeSeconds = 0.15f;
        /// <summary>Pitch units per second when the direction changes (a Stop glides, it does not jump).</summary>
        public const float PitchGlidePerSecond = 0.5f;

        /// <summary>The cue for a real state change <paramref name="prev"/> to <paramref name="next"/>.</summary>
        public static CeilingAudioCue CueFor(CeilingPodState prev, CeilingPodState next)
        {
            if (prev == next) return CeilingAudioCue.None;
            switch (next)
            {
                case CeilingPodState.PreparingToDescend:
                case CeilingPodState.PreparingToRetract:
                    return CeilingAudioCue.Unlock;
                case CeilingPodState.Stabilizing:
                    return CeilingAudioCue.Lock;
                case CeilingPodState.Docked:
                    return prev == CeilingPodState.ReturningToDock ? CeilingAudioCue.Lock : CeilingAudioCue.None;
                default:
                    return CeilingAudioCue.None;
            }
        }

        /// <summary>Where the cue for this change is heard.</summary>
        public static CeilingAudioSite SiteFor(CeilingPodState next)
        {
            return next == CeilingPodState.PreparingToDescend || next == CeilingPodState.Docked
                ? CeilingAudioSite.Mount : CeilingAudioSite.Pod;
        }

        /// <summary>True in the states where the office is travelling and the loop should sound.</summary>
        public static bool IsTravel(CeilingPodState s)
        {
            return s == CeilingPodState.Descending || s == CeilingPodState.Retracting
                || s == CeilingPodState.ReturningToDock || s == CeilingPodState.ErrorRecovery;
        }

        /// <summary>-1 going down, +1 going up, 0 when not travelling.</summary>
        public static int Direction(CeilingPodState s)
        {
            if (s == CeilingPodState.Descending) return -1;
            return IsTravel(s) ? 1 : 0;
        }

        /// <summary>Loop pitch for a travel direction (down lower, up higher; 1 for none).</summary>
        public static float TravelPitch(int direction)
        {
            return direction < 0 ? PitchDown : direction > 0 ? PitchUp : 1f;
        }
    }

    /// <summary>
    /// Procedural clips for the ceiling pod. Deterministic (its own noise generator, no Random), so
    /// the same clip comes out every time. Every clip is normalised to a peak of 0.8 or below.
    /// </summary>
    public static class CeilingPodAudioSynth
    {
        public const int Rate = 44100;
        public const float UnlockSeconds = 0.4f;
        public const float LockSeconds = 0.5f;
        public const float LoopSeconds = 2f;

        public static float[] UnlockSamples()
        {
            int n = (int)(Rate * UnlockSeconds);
            var d = new float[n];
            uint seed = 12345u;
            float lpFast = 0f, lpSlow = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float v = Click(t, 0f, 1f) + Click(t, 0.11f, 0.8f);
                // Air: noise with the lows and the very highs taken off, swelling in and dying away.
                float w = Noise(ref seed);
                lpFast += 0.35f * (w - lpFast);
                lpSlow += 0.03f * (w - lpSlow);
                float air = lpFast - lpSlow;
                float tk = t - 0.09f;
                if (tk > 0f) v += 0.55f * air * (1f - Mathf.Exp(-tk / 0.02f)) * Mathf.Exp(-tk / 0.09f);
                d[i] = v;
            }
            return Finish(d, 0.8f, 0.012f);
        }

        public static float[] LockSamples()
        {
            int n = (int)(Rate * LockSeconds);
            var d = new float[n];
            uint seed = 9876u;
            float lp = 0f;
            const float tau = 0.05f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                // Thunk: a body that falls from about 110 Hz to 55 Hz.
                float phase = 2f * Mathf.PI * (55f * t + 55f * tau * (1f - Mathf.Exp(-t / tau)));
                float v = Mathf.Sin(phase) * (1f - Mathf.Exp(-t / 0.002f)) * Mathf.Exp(-t / 0.07f);
                // A soft knock of dull noise on top.
                lp += 0.12f * (Noise(ref seed) - lp);
                v += 0.9f * lp * Mathf.Exp(-t / 0.015f);
                // Chime: two bright notes a moment later.
                float tc = t - 0.09f;
                if (tc > 0f)
                {
                    float env = (1f - Mathf.Exp(-tc / 0.003f)) * Mathf.Exp(-tc / 0.14f);
                    v += 0.55f * env * (Mathf.Sin(2f * Mathf.PI * 1568f * tc) + 0.6f * Mathf.Sin(2f * Mathf.PI * 2349f * tc));
                    float tc2 = tc - 0.06f;
                    if (tc2 > 0f) v += 0.25f * (1f - Mathf.Exp(-tc2 / 0.003f)) * Mathf.Exp(-tc2 / 0.12f) * Mathf.Sin(2f * Mathf.PI * 3136f * tc2);
                }
                d[i] = v;
            }
            return Finish(d, 0.8f, 0.012f);
        }

        /// <summary>
        /// A soft spool hum. Every partial is a whole number of Hz and the clip is a whole number of
        /// seconds long, so the waveform is periodic over the clip: it starts and ends at zero and
        /// loops without a click.
        /// </summary>
        public static float[] LoopSamples()
        {
            int n = (int)(Rate * LoopSeconds);
            var d = new float[n];
            const float tau = 2f * Mathf.PI;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float swell = 1f + 0.2f * Mathf.Sin(tau * 4f * t);
                float v = Mathf.Sin(tau * 110f * t)
                        + 0.5f * Mathf.Sin(tau * 220f * t)
                        + 0.25f * Mathf.Sin(tau * 165f * t);
                v *= swell;
                v += 0.12f * Mathf.Sin(tau * 660f * t) * (0.5f + 0.5f * Mathf.Sin(tau * 3f * t));
                d[i] = v;
            }
            // Level only: no end fade, which would spoil the seamless join.
            return Finish(d, 0.5f, 0f);
        }

        public static AudioClip UnlockClip() { return ToClip("ceiling_unlock", UnlockSamples(), false); }
        public static AudioClip LockClip() { return ToClip("ceiling_lock", LockSamples(), false); }
        public static AudioClip LoopClip() { return ToClip("ceiling_travel", LoopSamples(), true); }

        static AudioClip ToClip(string name, float[] data, bool loop)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Click(float t, float t0, float amp)
        {
            float tk = t - t0;
            if (tk < 0f) return 0f;
            float env = (1f - Mathf.Exp(-tk / 0.0015f)) * Mathf.Exp(-tk / 0.010f);
            return amp * env * (0.6f * Mathf.Sin(2f * Mathf.PI * 1500f * tk) + 0.4f * Mathf.Sin(2f * Mathf.PI * 620f * tk));
        }

        // Small linear congruential generator, white noise in -1..1.
        static float Noise(ref uint s)
        {
            s = s * 1664525u + 1013904223u;
            return ((s >> 8) & 0xFFFF) / 32767.5f - 1f;
        }

        /// <summary>Scale to the given peak, then fade the last <paramref name="fadeSeconds"/> to zero.</summary>
        static float[] Finish(float[] d, float peak, float fadeSeconds)
        {
            float max = 1e-6f;
            for (int i = 0; i < d.Length; i++) { float a = Mathf.Abs(d[i]); if (a > max) max = a; }
            float g = peak / max;
            for (int i = 0; i < d.Length; i++) d[i] *= g;
            int fade = Mathf.Min(d.Length, (int)(Rate * fadeSeconds));
            for (int i = 0; i < fade; i++) d[d.Length - 1 - i] *= (float)i / fade;
            return d;
        }
    }
}
