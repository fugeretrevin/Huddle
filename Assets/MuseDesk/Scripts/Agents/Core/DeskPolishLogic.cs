namespace MuseDesk.Agents
{
    // Polish S9b: the pure decisions behind the desk character/audio pass. No engine types, so EditMode tests reach them.

    /// <summary>Which in-place turn clip (if any) the desk agent should play.</summary>
    public enum TurnClip { None = 0, Left = 1, Right = 2 }

    public static class DeskTurnRule
    {
        /// <summary>Turn_Left / Turn_Right only while the agent is standing and really turning on the spot
        /// (AgentMovement.TurnInPlaceDirection: -1 left, +1 right). Otherwise nothing: walking, idle and the
        /// state clips stay as they are. Same rule as the Wall (a turn on the spot instead of walking in place).</summary>
        public static TurnClip Select(int turnInPlaceDirection)
        {
            return turnInPlaceDirection < 0 ? TurnClip.Left : turnInPlaceDirection > 0 ? TurnClip.Right : TurnClip.None;
        }
    }

    /// <summary>
    /// Per-agent gate for the one-shot nod on a confirmed task.completed: never under Focus, never while the
    /// agent is busy with a body (walking, holding the paper out, another reaction), and at most one per
    /// <c>minGapSeconds</c> per agent. State lives in the caller's float (the time of the last nod).
    /// </summary>
    public static class NodGate
    {
        public const float DefaultGapSeconds = 6f;

        public static bool Allow(float now, float lastNodAt, float minGapSeconds, bool focus, bool bodyBusy)
        {
            if (focus || bodyBusy) return false;
            return now - lastNodAt >= minGapSeconds;
        }
    }

    /// <summary>How the agents' random ambient gestures behave under Focus and for status-backed ones.</summary>
    public static class IdleGesturePolicy
    {
        /// <summary>Random ambient gestures (glance at a colleague, stretch, look over at the worker) are off under Focus.</summary>
        public static bool AmbientAllowed(bool focus) { return !focus; }

        /// <summary>Status-backed gestures (presenting the paper while waiting for the user) always show; under Focus
        /// they repeat less often (multiplier on the interval, never below 1).</summary>
        public static float StatusRepeatMultiplier(bool focus, float focusSlowdown)
        {
            return focus ? (focusSlowdown < 1f ? 1f : focusSlowdown) : 1f;
        }
    }

    /// <summary>When the user's hands are busy, the agents' hand reactions (poke, wave, glance, high five, flinch) stay quiet.</summary>
    public static class HandReactionGate
    {
        /// <param name="anyHeld">HandInteractionProbe.AnyHeld (a paper, stamp, card or panel is held, or a paper transition hold).</param>
        /// <param name="leftAgentSelect">The Select Agents carry claims the left hand.</param>
        /// <param name="rightAgentSelect">The Select Agents carry claims the right hand.</param>
        public static bool Busy(bool anyHeld, bool leftAgentSelect, bool rightAgentSelect)
        {
            return anyHeld || leftAgentSelect || rightAgentSelect;
        }
    }

    /// <summary>
    /// Defers a card's hide while a hand holds it: BeginHide used to switch the colliders off at once, which drops
    /// a card the user is holding. <see cref="Request"/> arms it; <see cref="Tick"/> returns true on the first
    /// frame the card is NOT held, which is when the hide really starts; <see cref="Cancel"/> disarms it (the task came back).
    /// </summary>
    public struct DeferredHide
    {
        bool _pending;

        public bool Pending { get { return _pending; } }

        public void Request() { _pending = true; }

        public void Cancel() { _pending = false; }

        /// <summary>True exactly once: pending and not held. Clears the pending flag.</summary>
        public bool Tick(bool held)
        {
            if (!_pending || held) return false;
            _pending = false;
            return true;
        }
    }

    /// <summary>
    /// Rate limit and Focus gate for the desk sound cues. Ambient cues (card landed, paper arrived, delivery chime) are
    /// silent under Focus; outcome cues the user caused (approve, key click, error) are not touched by Focus.
    /// </summary>
    public static class DeskCuePolicy
    {
        /// <summary>Minimum seconds between two plays of the same cue, by cue name.</summary>
        public static float MinGap(DeskCueKind kind)
        {
            switch (kind)
            {
                case DeskCueKind.CardLanded: return 0.35f;      // several cards reflowing into lanes: one tick
                case DeskCueKind.PaperArrived: return 1.5f;
                default: return 0.12f;
            }
        }

        public static bool Allow(DeskCueKind kind, float now, float lastPlayedAt, bool focus)
        {
            if (focus && IsAmbient(kind)) return false;
            return now - lastPlayedAt >= MinGap(kind);
        }

        public static bool IsAmbient(DeskCueKind kind)
        {
            return kind == DeskCueKind.CardLanded || kind == DeskCueKind.PaperArrived || kind == DeskCueKind.Delivered;
        }
    }

    public enum DeskCueKind { Other = 0, CardLanded = 1, PaperArrived = 2, Delivered = 3 }
}
