namespace MuseDesk.Agents
{
    /// <summary>
    /// Desk S-followup: when an agent may show its Listening clip. The only INPUT that starts it is
    /// "the user addressed this agent with the mic open" (VoiceAddressing events); this gate only
    /// decides whether the body is free to show it. Never while walking, holding the paper out, in a
    /// one-shot reaction, or in a Critical (Error) state. Pure, so EditMode tests reach it.
    /// </summary>
    public static class ListeningGate
    {
        public static bool Show(bool addressedAndMicOpen, bool walking, bool offerHold, bool oneShotPlaying, bool criticalState)
        {
            if (!addressedAndMicOpen) return false;
            return !(walking || offerHold || oneShotPlaying || criticalState);
        }
    }
}
