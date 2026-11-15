using System.Collections.Generic;
using UnityEngine;

namespace MuseDesk.Motion
{
    /// <summary>
    /// Show / hide state for a thing that must never pop: a scale factor that moves between
    /// <see cref="HiddenFactor"/> (never zero: a zero scale breaks poke-surface maths) and 1 with a motion token.
    /// Pure logic, no allocation per step, no callbacks: completion is reported by the step that finishes the hide, and
    /// only while the phase is still Hiding, so a hide that was cancelled can never fire a stale "now deactivate it".
    ///
    /// Reversal is continuous: Show during a hide (or Hide during a show) starts from the CURRENT factor over a time
    /// proportional to the remaining distance, so nothing jumps and a half-scaled card is never left behind.
    /// </summary>
    public sealed class PresenceFader
    {
        /// <summary>Same value as ScaleEase.HiddenFactor.</summary>
        public const float HiddenFactor = 0.001f;

        public enum Phase { Hidden = 0, Showing = 1, Shown = 2, Hiding = 3 }

        public Phase State { get; private set; }
        /// <summary>Scale multiplier to apply, HiddenFactor..1.</summary>
        public float Factor { get; private set; }

        MotionToken _token;
        float _from, _to, _t, _dur;

        public PresenceFader(bool shown)
        {
            State = shown ? Phase.Shown : Phase.Hidden;
            Factor = shown ? 1f : HiddenFactor;
        }

        /// <summary>True only when fully shown: the moment pokes and colliders may be enabled.</summary>
        public bool Interactable { get { return State == Phase.Shown; } }
        /// <summary>True while hiding or hidden: colliders must be off and layout must not give it a slot.</summary>
        public bool IsLeaving { get { return State == Phase.Hiding || State == Phase.Hidden; } }
        public bool Moving { get { return State == Phase.Showing || State == Phase.Hiding; } }

        /// <summary>Grow to full size. animate=false (or a zero-length token) lands at once.</summary>
        public void Show(MotionToken token, bool animate = true) { Begin(1f, token, animate, Phase.Showing, Phase.Shown); }

        /// <summary>Shrink to the hidden factor. animate=false lands at once (Hidden).</summary>
        public void Hide(MotionToken token, bool animate = true) { Begin(HiddenFactor, token, animate, Phase.Hiding, Phase.Hidden); }

        void Begin(float to, MotionToken token, bool animate, Phase moving, Phase rest)
        {
            if (State == rest && Mathf.Approximately(Factor, to)) return;
            if (State == moving && Mathf.Approximately(_to, to)) return;   // already heading there
            _token = token;
            _from = Factor; _to = to; _t = 0f;
            float remaining = Mathf.Abs(to - _from) / (1f - HiddenFactor);
            _dur = token.Duration * Mathf.Clamp01(remaining);
            if (!animate || _dur <= 1e-4f) { Factor = to; State = rest; return; }
            State = moving;
        }

        /// <summary>Advance by dt (clamped by SafeStep). Returns true on the step that finishes a HIDE.</summary>
        public bool Step(float dt)
        {
            if (!Moving) return false;
            _t += MotionSettings.SafeStep(dt);
            if (_t < _dur)
            {
                float e = _token.Evaluate(_t / _dur);
                Factor = Mathf.Max(HiddenFactor, Mathf.LerpUnclamped(_from, _to, e));
                return false;
            }
            Factor = _to;
            bool wasHiding = State == Phase.Hiding;
            State = wasHiding ? Phase.Hidden : Phase.Shown;
            return wasHiding;
        }

        /// <summary>Snap to shown / hidden with no motion (pool reset).</summary>
        public void Snap(bool shown)
        {
            State = shown ? Phase.Shown : Phase.Hidden;
            Factor = shown ? 1f : HiddenFactor;
        }
    }

    /// <summary>
    /// Which ids are new, gone or kept between two snapshots of a set. Used by the board so a resync only reveals the
    /// cards that were not already on it and only hides the ones that really left.
    /// </summary>
    public static class PresenceDiff
    {
        /// <summary>Fills added (in next, not in current), removed (in current, not in next), kept (in both). The
        /// output lists are cleared first. Null or empty ids are ignored. Order follows the input order.</summary>
        public static void Compute(IEnumerable<string> current, IEnumerable<string> next,
                                   List<string> added, List<string> removed, List<string> kept)
        {
            added.Clear(); removed.Clear(); kept.Clear();
            var nextSet = new HashSet<string>();
            if (next != null) foreach (var id in next) if (!string.IsNullOrEmpty(id)) nextSet.Add(id);
            var curSet = new HashSet<string>();
            if (current != null)
                foreach (var id in current)
                {
                    if (string.IsNullOrEmpty(id) || !curSet.Add(id)) continue;
                    if (nextSet.Contains(id)) kept.Add(id); else removed.Add(id);
                }
            if (next == null) return;
            var seen = new HashSet<string>();
            foreach (var id in next)
                if (!string.IsNullOrEmpty(id) && !curSet.Contains(id) && seen.Add(id)) added.Add(id);
        }
    }
}
