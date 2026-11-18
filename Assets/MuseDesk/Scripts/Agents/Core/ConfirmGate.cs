namespace MuseDesk.Agents
{
    /// <summary>
    /// Two-press confirm for a destructive key (rc22: Remove, Clear). The first press arms the key for a few
    /// seconds ("Sure?"); a second press inside the window confirms; after the window the key is idle again, so
    /// a stray poke can never delete. Time is passed in, so the rule is testable without a clock.
    /// </summary>
    public sealed class ConfirmGate
    {
        public readonly float Window;
        float _armedUntil = -1f;
        bool _armed;

        public ConfirmGate(float windowSeconds = 3f) { Window = windowSeconds; }

        /// <summary>True while armed and the window has not run out.</summary>
        public bool IsArmed(float now) => _armed && now < _armedUntil;

        /// <summary>A press. Returns true when it CONFIRMS (the key was armed); false when it only armed the key.</summary>
        public bool Press(float now)
        {
            if (IsArmed(now)) { _armed = false; return true; }
            _armed = true;
            _armedUntil = now + Window;
            return false;
        }

        public void Reset() { _armed = false; _armedUntil = -1f; }

        /// <summary>The label for a key: busy dots while a request is in flight, the confirm words while armed, else idle.</summary>
        public string Label(float now, bool busy, string idle, string armed)
            => busy ? "..." : IsArmed(now) ? armed : idle;
    }
}
