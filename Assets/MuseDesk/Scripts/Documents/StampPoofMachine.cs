using UnityEngine;

namespace MuseDesk.Documents
{
    /// <summary>
    /// The timing of a stamp that leaves after a decision and comes back for the next one. Plain
    /// logic with primitive-typed members (no scene, no Unity object), so the EditMode tests can
    /// drive it by reflection. <see cref="StampPoof"/> owns one per stamp and turns its phase and
    /// scale factors into what the user sees.
    ///
    ///   Shown    on the pad, grabbable.
    ///   Waiting  a decision was confirmed; the stamp stays a moment (and for as long as it is held).
    ///   Leaving  the squash-and-pop out.
    ///   Hidden   gone; not grabbable.
    ///   Arriving the pop back in on the pad.
    /// </summary>
    public sealed class StampPoofMachine
    {
        public const int Shown = 0, Waiting = 1, Leaving = 2, Hidden = 3, Arriving = 4;

        /// <summary>Returned by <see cref="Tick"/>: nothing happened this step.</summary>
        public const int EventNone = 0;
        /// <summary>The stamp began to leave (make it ungrabbable now).</summary>
        public const int EventLeaveStarted = 1;
        /// <summary>The pop of the leave: the moment for the puff.</summary>
        public const int EventPop = 2;
        /// <summary>The stamp is gone.</summary>
        public const int EventHidden = 3;
        /// <summary>The stamp is fully back (make it grabbable again).</summary>
        public const int EventShown = 4;

        /// <summary>Fraction of the leave at which the pop happens and the stamp starts to vanish.</summary>
        public const float PopAt = 0.62f;
        /// <summary>Scale factor of a hidden stamp (never exactly zero).</summary>
        public const float HiddenScale = 0.001f;

        public float LingerSeconds;
        public float LeaveSeconds;
        public float ArriveSeconds;
        /// <summary>After a held stamp is let go, it stays this long before it leaves.</summary>
        public float ReleaseGraceSeconds;

        public int Phase { get; private set; }
        /// <summary>0..1 through the leave or the arrival; 0 otherwise.</summary>
        public float Progress { get; private set; }

        float _wait;
        float _grace;
        bool _wasHeldWhileDue;
        bool _popped;
        bool _dismissAfterArrive;

        public StampPoofMachine(float lingerSeconds, float leaveSeconds, float arriveSeconds, float releaseGraceSeconds)
        {
            LingerSeconds = lingerSeconds;
            LeaveSeconds = leaveSeconds;
            ArriveSeconds = arriveSeconds;
            ReleaseGraceSeconds = releaseGraceSeconds;
            Phase = Shown;
        }

        /// <summary>True while a hand may take the stamp.</summary>
        public bool Grabbable => Phase == Shown || Phase == Waiting;

        /// <summary>Its job is done: leave after <see cref="LingerSeconds"/>. Returns true if that changed anything.</summary>
        public bool Dismiss() { return DismissAfter(LingerSeconds); }

        /// <summary>As <see cref="Dismiss"/> with its own wait.</summary>
        public bool DismissAfter(float seconds)
        {
            if (Phase == Arriving) { _dismissAfterArrive = true; _wait = seconds; return true; }
            if (Phase != Shown) return false;
            Phase = Waiting;
            _wait = seconds;
            _grace = 0f;
            _wasHeldWhileDue = false;
            return true;
        }

        /// <summary>The stamp is needed. Returns true if an arrival started (it was leaving or gone).</summary>
        public bool Summon()
        {
            _dismissAfterArrive = false;
            if (Phase == Waiting) { Phase = Shown; return false; }
            if (Phase == Hidden) { Phase = Arriving; Progress = 0f; return true; }
            if (Phase == Leaving)
            {
                // Turn round from roughly where the leave had got to.
                Phase = Arriving;
                Progress = Mathf.Clamp01(1f - Progress);
                return true;
            }
            return false;
        }

        public void ForceHidden() { Phase = Hidden; Progress = 0f; _dismissAfterArrive = false; }
        public void ForceShown() { Phase = Shown; Progress = 0f; _dismissAfterArrive = false; }

        /// <summary>Advance by <paramref name="dt"/> seconds. Returns one of the Event constants.</summary>
        public int Tick(float dt, bool held)
        {
            if (dt < 0f) dt = 0f;
            switch (Phase)
            {
                case Waiting:
                    _wait -= dt;
                    if (_wait > 0f) return EventNone;
                    // Never out of the user's hand: wait for the release, then a short beat.
                    if (held) { _wasHeldWhileDue = true; _grace = ReleaseGraceSeconds; return EventNone; }
                    if (_wasHeldWhileDue && _grace > 0f) { _grace -= dt; if (_grace > 0f) return EventNone; }
                    Phase = Leaving;
                    Progress = 0f;
                    _popped = false;
                    return EventLeaveStarted;

                case Leaving:
                    Progress += dt / Mathf.Max(0.01f, LeaveSeconds);
                    if (!_popped && Progress >= PopAt) { _popped = true; return EventPop; }
                    if (Progress >= 1f)
                    {
                        Phase = Hidden;
                        Progress = 0f;
                        return EventHidden;
                    }
                    return EventNone;

                case Arriving:
                    Progress += dt / Mathf.Max(0.01f, ArriveSeconds);
                    if (Progress >= 1f)
                    {
                        Progress = 0f;
                        if (_dismissAfterArrive)
                        {
                            _dismissAfterArrive = false;
                            Phase = Waiting;
                            _grace = 0f;
                            _wasHeldWhileDue = false;
                        }
                        else Phase = Shown;
                        return EventShown;
                    }
                    return EventNone;
            }
            return EventNone;
        }

        /// <summary>Width and depth factor of the stamp right now (1 = full size).</summary>
        public float ScaleXZ
        {
            get
            {
                if (Phase == Hidden) return HiddenScale;
                if (Phase == Leaving) return LeaveXZ(Progress);
                if (Phase == Arriving) return ArriveXZ(Progress);
                return 1f;
            }
        }

        /// <summary>Height factor of the stamp right now (1 = full size).</summary>
        public float ScaleY
        {
            get
            {
                if (Phase == Hidden) return HiddenScale;
                if (Phase == Leaving) return LeaveY(Progress);
                if (Phase == Arriving) return ArriveY(Progress);
                return 1f;
            }
        }

        // ── curves ──
        // Leave: squash (shorter, wider), spring up (taller, thinner), then gone.
        const float SquashEnd = 0.38f;

        public static float LeaveY(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < SquashEnd) return Mathf.Lerp(1f, 0.70f, Smooth(t / SquashEnd));
            if (t < PopAt) return Mathf.Lerp(0.70f, 1.22f, Smooth((t - SquashEnd) / (PopAt - SquashEnd)));
            float u = (t - PopAt) / (1f - PopAt);
            return Mathf.Max(HiddenScale, Mathf.Lerp(1.22f, 0f, u * u));
        }

        public static float LeaveXZ(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < SquashEnd) return Mathf.Lerp(1f, 1.18f, Smooth(t / SquashEnd));
            if (t < PopAt) return Mathf.Lerp(1.18f, 0.88f, Smooth((t - SquashEnd) / (PopAt - SquashEnd)));
            float u = (t - PopAt) / (1f - PopAt);
            return Mathf.Max(HiddenScale, Mathf.Lerp(0.88f, 0f, u * u));
        }

        // Arrive: grows past full size and settles; the height leads the width a little.
        public static float ArriveY(float t) { return Mathf.Max(HiddenScale, Back(Mathf.Clamp01(t * 1.25f))); }
        public static float ArriveXZ(float t) { return Mathf.Max(HiddenScale, Back(Mathf.Clamp01(t))); }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        // Ease out with a small overshoot (0 at 0, 1 at 1, about 1.10 on the way).
        static float Back(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float y = x - 1f;
            return 1f + c3 * y * y * y + c1 * y * y;
        }
    }
}
