namespace MuseDesk.Spatial
{
    /// <summary>How a grab ended: a genuine release (a tracked hand let go) or a cancel (tracking was lost).</summary>
    public enum GrabEnd { Release = 0, Cancel = 1 }

    /// <summary>
    /// Pure helper for the decision "did this grab END because the user let go, or because hand tracking
    /// dropped?" (Polish S2). The Interaction SDK deselects a grab when its hand is lost, so a consequential
    /// handler that polls "is it still held" (the task card drag) cannot tell the two apart by itself.
    /// A lost hand is never a release: the caller feeds this one sample per held frame and asks once, on
    /// the first frame the grab is gone.
    ///
    /// Which hand grabbed is not known to the polling handler, so the rule is deliberately conservative
    /// (a wrong answer only returns the card home, it never sends anything): every hand that was tracked
    /// when the hold began is "watched"; if any watched hand is untracked now, or was untracked at any held
    /// sample within <c>lookbackSeconds</c> before the grab ended, the end is a Cancel.
    /// A hand that was already untracked when the hold began is ignored (it is not the one holding).
    /// No allocation; no engine references.
    /// </summary>
    public struct GrabTrackingWatch
    {
        bool _w0, _w1;                 // watched: tracked when the hold began
        float _lost0, _lost1;          // last time a held sample saw the hand untracked (-1 = never)

        /// <summary>Call on the frame the hold begins.</summary>
        public void Begin(bool tracked0, bool tracked1)
        {
            _w0 = tracked0; _w1 = tracked1;
            _lost0 = -1f; _lost1 = -1f;
        }

        /// <summary>Call on every frame the grab is still held.</summary>
        public void Sample(bool tracked0, bool tracked1, float now)
        {
            if (!tracked0) _lost0 = now;
            if (!tracked1) _lost1 = now;
        }

        /// <summary>Call on the first frame the grab is no longer held.</summary>
        public GrabEnd Decide(bool tracked0, bool tracked1, float now, float lookbackSeconds)
        {
            if (Lost(_w0, tracked0, _lost0, now, lookbackSeconds)) return GrabEnd.Cancel;
            if (Lost(_w1, tracked1, _lost1, now, lookbackSeconds)) return GrabEnd.Cancel;
            return GrabEnd.Release;
        }

        static bool Lost(bool watched, bool trackedNow, float lastLost, float now, float lookback)
        {
            if (!watched) return false;
            if (!trackedNow) return true;
            return lastLost >= 0f && now - lastLost <= lookback;
        }
    }
}
