using UnityEngine;

namespace MuseDesk.Spatial
{
    /// <summary>
    /// Pure timing logic for the ceiling scan grid (no scene, primitives only so EditMode tests can
    /// reach it by reflection): how far the radial wipe has got, how the fade moves, when the wipe
    /// restarts and whether the grid should be on show at all.
    /// </summary>
    public static class CeilingScanRevealLogic
    {
        public const float WipeSeconds = 0.6f;
        public const float FadeOutSeconds = 0.3f;
        public const float FadeInSeconds = 0.1f;
        public const float RestartDistance = 0.25f;

        /// <summary>Wipe radius after t seconds: ease-out cubic from 0 to maxRadius over duration.</summary>
        public static float RevealRadius(float t, float duration, float maxRadius)
        {
            if (duration <= 0f) return maxRadius;
            float u = Mathf.Clamp01(t / duration);
            float inv = 1f - u;
            return maxRadius * (1f - inv * inv * inv);
        }

        /// <summary>Advance the wipe clock, stopping at the end of the wipe.</summary>
        public static float StepWipe(float t, float dt, float duration)
        {
            return Mathf.Min(duration, t + Mathf.Max(0f, dt));
        }

        /// <summary>Fade towards 1 while wanted (quickly), towards 0 otherwise (over fadeOutSeconds).</summary>
        public static float StepFade(float fade, bool wanted, float dt, float fadeInSeconds, float fadeOutSeconds)
        {
            dt = Mathf.Max(0f, dt);
            if (wanted)
                return fadeInSeconds <= 0f ? 1f : Mathf.Min(1f, fade + dt / fadeInSeconds);
            return fadeOutSeconds <= 0f ? 0f : Mathf.Max(0f, fade - dt / fadeOutSeconds);
        }

        /// <summary>
        /// The grid is wanted only while a Ceiling placement is being previewed and a real ceiling
        /// height is known (the evaluator never assumes one).
        /// </summary>
        public static bool WantShown(bool previewing, bool pendingIsCeiling, float ceilingHeight)
        {
            return previewing && pendingIsCeiling && ceilingHeight > 0.5f;
        }

        /// <summary>
        /// Restart the wipe when the grid just became wanted, when the candidate index changed, or
        /// when the dock moved more than <see cref="RestartDistance"/> (a nudge slides the grid
        /// without restarting it).
        /// </summary>
        public static bool ShouldRestart(bool wasWanted, bool wanted, bool indexChanged, float movedMetres)
        {
            if (!wanted) return false;
            return !wasWanted || indexChanged || movedMetres > RestartDistance;
        }

        /// <summary>True while any part of the grid is still visible (so the renderer can be switched off at 0).</summary>
        public static bool Visible(float fade)
        {
            return fade > 0.001f;
        }
    }
}
